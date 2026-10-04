// @vitest-environment node
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { describe, expect, it } from 'vitest';
import { strToU8, zipSync } from 'fflate';
import { naturalCompare, planArchive, readEntry, readZipEntries, type UploadLimits } from '@/features/house/zipInspect';

const limits: UploadLimits = {
  maxUploadSize: 200 * 1024 * 1024,
  maxExtractedSize: 600 * 1024 * 1024,
  maxPages: 200,
  maxEntrySize: 50 * 1024 * 1024,
  maxCompressionRatio: 100,
  maxImageDimension: 16383,
  maxImagePixels: 40_000_000,
  pageMaxWidth: 2000,
};

const blob = (files: Record<string, Uint8Array>) => new Blob([zipSync(files) as Uint8Array<ArrayBuffer>]);
const img = (seed: number) => new Uint8Array(64).map((_, i) => (i * seed) & 0xff);

describe('naturalCompare', () => {
  it('matches the server order exactly (fixture written by the C# NaturalStringComparer test)', () => {
    const fixture = JSON.parse(readFileSync(path.resolve(process.cwd(), 'tests', 'fixtures', 'natural-order.json'), 'utf8')) as {
      input: string[];
      expected: string[];
    };
    expect([...fixture.input].sort(naturalCompare)).toEqual(fixture.expected);
  });
});

describe('planArchive', () => {
  it('orders pages like the server, renumbers from 1 and explains a numbering that starts at 002', async () => {
    const file = blob({
      'chuong/004.png': img(3),
      'chuong/002.png': img(1),
      'chuong/3.png': img(2),
      'ghi-chu.txt': strToU8('Nhóm dịch'),
      '__MACOSX/chuong/._002.png': img(9),
      'chuong/.hidden.png': img(9),
      'Thumbs.db': img(9),
    });
    const plan = planArchive(await readZipEntries(file), limits);
    expect(plan.pages.map((p) => [p.page, p.entry.name])).toEqual([
      [1, 'chuong/002.png'],
      [2, 'chuong/3.png'],
      [3, 'chuong/004.png'],
    ]);
    expect(plan.ok).toBe(true);
    expect(plan.ignoredFiles).toEqual(['ghi-chu.txt']);
    const messages = plan.issues.map((i) => `${i.level}: ${i.message}`);
    expect(messages.some((m) => m.startsWith('info: Số thứ tự bắt đầu từ 2'))).toBe(true);
    expect(messages.some((m) => m.includes('thiếu trang'))).toBe(false);

    // Thumbnails decompress the exact bytes that were zipped.
    expect(await readEntry(file, plan.pages[0]!.entry)).toEqual(img(1));
  });

  it('warns about missing numbers, duplicates and image formats the server would silently skip', async () => {
    const plan = planArchive(
      await readZipEntries(blob({ '001.jpg': img(1), '1.jpg': img(2), '002.jpg': img(3), '004.jpg': img(4), '003.gif': img(5), 'bia.psd': img(6) })),
      limits,
    );
    const warnings = plan.issues.filter((i) => i.level === 'warning').map((i) => i.message);
    expect(plan.pages).toHaveLength(4);
    expect(warnings.some((m) => m.includes('BỊ BỎ QUA') && m.includes('003.gif') && m.includes('bia.psd'))).toBe(true);
    expect(warnings.some((m) => m.includes('trùng số thứ tự') && m.includes('1.jpg / 001.jpg'))).toBe(true);
    expect(warnings.some((m) => m.includes('thiếu trang') && m.includes('3'))).toBe(true);
    expect(plan.ok).toBe(true); // warnings never block — the uploader decides
  });

  it('blocks archives the server would reject', async () => {
    const tooMany: Record<string, Uint8Array> = {};
    for (let i = 1; i <= 3; i++) tooMany[`${i}.png`] = img(i);
    expect(planArchive(await readZipEntries(blob(tooMany)), { ...limits, maxPages: 2 }).ok).toBe(false);
    expect(planArchive(await readZipEntries(blob({ 'ghi-chu.txt': strToU8('x') })), limits).ok).toBe(false);
    expect(planArchive(await readZipEntries(blob({ 'rong.png': new Uint8Array(0) })), limits).ok).toBe(false);
  });

  it('rejects files that are not ZIP archives', async () => {
    await expect(readZipEntries(new Blob([strToU8('không phải zip')]))).rejects.toThrow('ZIP');
  });
});
