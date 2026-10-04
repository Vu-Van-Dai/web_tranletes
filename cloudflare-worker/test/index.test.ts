import { afterEach, describe, expect, it, vi } from 'vitest';
import { IMMUTABLE, handleRequest, parseKey, type CacheLike, type Env, type R2ObjectLike } from '../src/index';

const COMIC = '0193f0a1-1111-7aaa-8bbb-000000000001';
const CHAPTER = '0193f0a1-2222-7aaa-8bbb-000000000002';
const PKG = `comics/${COMIC}/chapters/${CHAPTER}/v3.pkg`;
const COVER = `comics/${COMIC}/cover/0123456789abcdef.webp`;
const BASE = 'https://img.example.com/';

class FakeCache implements CacheLike {
  store = new Map<string, Response>();
  puts = 0;
  async match(request: Request) {
    const hit = this.store.get(request.url);
    return hit ? hit.clone() : undefined;
  }
  async put(request: Request, response: Response) {
    this.puts++;
    // Consume the clone like the real Cache API does.
    const body = await response.arrayBuffer();
    this.store.set(request.url, new Response(body, { status: response.status, headers: response.headers }));
  }
}

function bucket(objects: Record<string, Uint8Array<ArrayBuffer>>): Env['BUCKET'] & { gets: string[] } {
  const gets: string[] = [];
  return {
    gets,
    async get(key: string): Promise<R2ObjectLike | null> {
      gets.push(key);
      const data = objects[key];
      if (!data) return null;
      return { body: new Response(data).body, size: data.byteLength, httpEtag: '"etag-1"' };
    },
  };
}

function ctx() {
  const pending: Promise<unknown>[] = [];
  return { waitUntil: (p: Promise<unknown>) => void pending.push(p), flush: () => Promise.all(pending) };
}

afterEach(() => vi.unstubAllGlobals());

describe('parseKey', () => {
  it('accepts only generated key shapes', () => {
    expect(parseKey('/' + PKG)).toBe(PKG);
    expect(parseKey('/' + COVER)).toBe(COVER);
    expect(parseKey(`/houses/${COMIC}/logo/0123456789abcdef.webp`)).not.toBeNull();
    for (const bad of [
      '/comics/../../etc/passwd',
      `/comics/${COMIC}/chapters/${CHAPTER}/v0.pkg`,
      `/comics/${COMIC}/chapters/${CHAPTER}/v1.pkg/extra`,
      `/comics/${COMIC.toUpperCase()}/cover/0123456789abcdef.webp`,
      `/comics/${COMIC}/cover/0123456789ABCDEF.webp`,
      `/comics/${COMIC}/chapters/${CHAPTER}/v1.zip`,
      '/',
    ]) {
      expect(parseKey(bad), bad).toBeNull();
    }
  });
});

describe('handleRequest', () => {
  it('serves from R2 on miss, then from the edge cache', async () => {
    const data = new Uint8Array([1, 2, 3, 4, 5]);
    const cache = new FakeCache();
    const env = { BUCKET: bucket({ [PKG]: data }) };

    const c1 = ctx();
    const first = await handleRequest(new Request(BASE + PKG), env, c1, cache);
    await c1.flush();
    expect(first.status).toBe(200);
    expect(first.headers.get('X-Cache')).toBe('MISS');
    expect(first.headers.get('Cache-Control')).toBe(IMMUTABLE);
    expect(first.headers.get('Access-Control-Allow-Origin')).toBe('*');
    expect(first.headers.get('Content-Type')).toBe('application/octet-stream');
    expect(new Uint8Array(await first.arrayBuffer())).toEqual(data);
    expect(cache.puts).toBe(1);

    const second = await handleRequest(new Request(BASE + PKG + '?bust=1'), env, ctx(), cache);
    expect(second.headers.get('X-Cache')).toBe('HIT');
    expect(new Uint8Array(await second.arrayBuffer())).toEqual(data);
    expect(env.BUCKET.gets).toHaveLength(1); // R2 hit only once
  });

  it('falls back to the origin with the shared secret when R2 misses', async () => {
    const fetchMock = vi.fn(async (_url: string, init?: RequestInit) => {
      expect((init?.headers as Record<string, string>)['X-Origin-Secret']).toBe('s3cret');
      return new Response(new Uint8Array([9, 9]), { status: 200, headers: { 'Content-Length': '2' } });
    });
    vi.stubGlobal('fetch', fetchMock);
    const cache = new FakeCache();
    const env: Env = { BUCKET: bucket({}), ORIGIN_URL: 'https://api.example.com/', ORIGIN_SHARED_SECRET: 's3cret' };

    const c = ctx();
    const response = await handleRequest(new Request(BASE + COVER), env, c, cache);
    await c.flush();
    expect(response.status).toBe(200);
    expect(response.headers.get('X-Cache')).toBe('ORIGIN');
    expect(response.headers.get('Content-Type')).toBe('image/webp');
    expect(fetchMock).toHaveBeenCalledWith(`https://api.example.com/internal/objects/${COVER}`, expect.anything());
    expect(cache.puts).toBe(1);
  });

  it('returns 404 when neither R2 nor the origin has the object, 502 on origin failure', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => new Response('nope', { status: 404 })));
    const env: Env = { BUCKET: bucket({}), ORIGIN_URL: 'https://api.example.com', ORIGIN_SHARED_SECRET: 'x' };
    const missing = await handleRequest(new Request(BASE + PKG), env, ctx(), new FakeCache());
    expect(missing.status).toBe(404);
    expect(missing.headers.get('Cache-Control')).toBe('public, max-age=60');

    vi.stubGlobal('fetch', vi.fn(async () => new Response('boom', { status: 500 })));
    expect((await handleRequest(new Request(BASE + PKG), env, ctx(), new FakeCache())).status).toBe(502);

    vi.stubGlobal('fetch', vi.fn(async () => { throw new Error('network'); }));
    expect((await handleRequest(new Request(BASE + PKG), env, ctx(), new FakeCache())).status).toBe(502);
  });

  it('rejects unknown paths and methods without touching R2', async () => {
    const env = { BUCKET: bucket({}) };
    expect((await handleRequest(new Request(BASE + 'comics/../../secret'), env, ctx(), new FakeCache())).status).toBe(404);
    const post = await handleRequest(new Request(BASE + PKG, { method: 'POST', body: 'x' }), env, ctx(), new FakeCache());
    expect(post.status).toBe(405);
    expect(post.headers.get('Allow')).toContain('GET');
    expect(env.BUCKET.gets).toHaveLength(0);
  });

  it('answers HEAD without a body and preflight with CORS', async () => {
    const env = { BUCKET: bucket({ [PKG]: new Uint8Array([1, 2, 3]) }) };
    const head = await handleRequest(new Request(BASE + PKG, { method: 'HEAD' }), env, ctx(), new FakeCache());
    expect(head.status).toBe(200);
    expect(head.headers.get('Content-Length')).toBe('3');
    expect(head.body).toBeNull();

    const options = await handleRequest(new Request(BASE + PKG, { method: 'OPTIONS' }), env, ctx(), new FakeCache());
    expect(options.status).toBe(204);
    expect(options.headers.get('Access-Control-Allow-Methods')).toContain('GET');
  });
});
