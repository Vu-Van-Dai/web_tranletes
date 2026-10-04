// @vitest-environment node
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { webcrypto } from 'node:crypto';
import { beforeAll, describe, expect, it } from 'vitest';
import { PackageError, parsePackage, verifyPackage } from '@/features/reader/package';
import { PageWindow, type UrlFactory } from '@/features/reader/pageWindow';

// Golden fixture written by the C# PackageWriter (backend unit test) → cross-language parity.
const fixtureDir = path.resolve(process.cwd(), 'tests', 'fixtures') + path.sep;
const golden = new Uint8Array(readFileSync(fixtureDir + 'golden-3pages.pkg'));
const meta = JSON.parse(readFileSync(fixtureDir + 'golden-3pages.json', 'utf8')) as {
  sha256: string;
  size: number;
  pages: { n: number; w: number; h: number; size: number }[];
};

beforeAll(() => {
  if (!globalThis.crypto?.subtle) Object.defineProperty(globalThis, 'crypto', { value: webcrypto });
});

describe('parsePackage (golden fixture from C#)', () => {
  it('reads the index exactly as the backend wrote it', () => {
    expect(golden.byteLength).toBe(meta.size);
    const parsed = parsePackage(golden);
    expect(parsed.entries.map((e) => ({ n: e.pageNumber, w: e.width, h: e.height, size: e.size }))).toEqual(meta.pages);
    expect(parsed.trailerHex).toBe(meta.sha256);
    // Page bytes are the raw source bytes (b + i for page i).
    const first = parsed.entries[0]!;
    const bytes = golden.subarray(parsed.dataStart + first.offset, parsed.dataStart + first.offset + first.size);
    expect(Array.from(bytes)).toEqual(Array.from({ length: 10 }, (_, b) => b + 1));
  });

  it('verifies the SHA-256 against the trailer and the API checksum', async () => {
    const parsed = parsePackage(golden);
    await expect(verifyPackage(golden, parsed, meta.sha256)).resolves.toBeUndefined();
    await expect(verifyPackage(golden, parsed, '0'.repeat(64))).rejects.toBeInstanceOf(PackageError);
  });

  it('detects tampering, truncation and malformed headers', async () => {
    const tampered = golden.slice();
    tampered[tampered.byteLength - 40]! ^= 0xff;
    await expect(verifyPackage(tampered, parsePackage(tampered), meta.sha256)).rejects.toThrow();

    expect(() => parsePackage(golden.slice(0, golden.byteLength - 5))).toThrow(PackageError);

    const badMagic = golden.slice();
    badMagic[0] = 0x58;
    expect(() => parsePackage(badMagic)).toThrow(PackageError);

    const overlap = golden.slice();
    new DataView(overlap.buffer).setBigUint64(32 + 24 + 4, 0n, true); // page 2 offset → 0
    expect(() => parsePackage(overlap)).toThrow(PackageError);

    expect(() => parsePackage(golden, 2)).toThrow(PackageError); // page limit
  });
});

describe('PageWindow', () => {
  it('keeps object URLs bounded and revokes everything it created', () => {
    const live = new Set<string>();
    let created = 0;
    const factory: UrlFactory = {
      create: () => {
        const url = `blob:${++created}`;
        live.add(url);
        return url;
      },
      revoke: (url) => {
        expect(live.delete(url)).toBe(true); // never double-revoked
      },
    };
    const window = new PageWindow(200, () => new Blob(), factory, 3, 6);

    for (let page = 0; page < 200; page++) {
      const urls = window.update(page);
      expect(urls.has(page)).toBe(true);
      expect(live.size).toBeLessThanOrEqual(13);
    }

    // Jumping back re-creates only what is needed.
    window.update(0);
    expect(live.size).toBeLessThanOrEqual(13);
    window.dispose();
    expect(live.size).toBe(0);
  });
});
