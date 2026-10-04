/**
 * Package / image delivery Worker.
 *
 *   GET img.<domain>/comics/{comicId}/chapters/{chapterId}/v{n}.pkg
 *   GET img.<domain>/comics/{comicId}/cover/{hash16}.webp
 *   GET img.<domain>/houses/{houseId}/logo/{hash16}.webp
 *
 * Flow: edge cache (caches.default) → R2 → Oracle origin fallback.
 * Every URL is versioned or content-hashed, so responses are immutable and cached for a year.
 * The Worker never decodes, resizes or assembles images — packages are built before publishing.
 */

export interface Env {
  BUCKET: R2BucketLike;
  /** e.g. https://api.example.com — used only when R2 misses. */
  ORIGIN_URL?: string;
  /** Shared secret sent to the origin fallback endpoint (wrangler secret). */
  ORIGIN_SHARED_SECRET?: string;
}

/** Minimal R2 surface we rely on (keeps tests independent of the runtime). */
export interface R2BucketLike {
  get(key: string): Promise<R2ObjectLike | null>;
}

export interface R2ObjectLike {
  body: ReadableStream | null;
  size: number;
  httpEtag: string;
}

export interface CacheLike {
  match(request: Request): Promise<Response | undefined>;
  put(request: Request, response: Response): Promise<void>;
}

export interface ContextLike {
  waitUntil(promise: Promise<unknown>): void;
}

const GUID = '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}';
const KEY_PATTERN = new RegExp(
  `^/((?:comics/${GUID}/chapters/${GUID}/v[1-9][0-9]{0,5}\\.pkg)|(?:comics/${GUID}/cover/[0-9a-f]{16}\\.webp)|(?:houses/${GUID}/logo/[0-9a-f]{16}\\.webp))$`,
);

export const IMMUTABLE = 'public, max-age=31536000, immutable';
const ORIGIN_TIMEOUT_MS = 15_000;

export function parseKey(pathname: string): string | null {
  return KEY_PATTERN.exec(pathname)?.[1] ?? null;
}

function contentTypeFor(key: string): string {
  return key.endsWith('.pkg') ? 'application/octet-stream' : 'image/webp';
}

function baseHeaders(): Headers {
  const headers = new Headers();
  headers.set('Access-Control-Allow-Origin', '*');
  headers.set('Access-Control-Expose-Headers', 'Content-Length, ETag, X-Cache');
  headers.set('Cross-Origin-Resource-Policy', 'cross-origin');
  headers.set('X-Content-Type-Options', 'nosniff');
  return headers;
}

function objectHeaders(key: string, size: number | null, etag: string | null): Headers {
  const headers = baseHeaders();
  headers.set('Content-Type', contentTypeFor(key));
  headers.set('Cache-Control', IMMUTABLE);
  if (size !== null) headers.set('Content-Length', String(size));
  if (etag) headers.set('ETag', etag);
  return headers;
}

function plain(status: number, message: string, cacheSeconds = 0): Response {
  const headers = baseHeaders();
  headers.set('Content-Type', 'text/plain; charset=utf-8');
  headers.set('Cache-Control', cacheSeconds > 0 ? `public, max-age=${cacheSeconds}` : 'no-store');
  return new Response(message, { status, headers });
}

/** Re-labels a (possibly cached) response and strips the body for HEAD. */
function finalize(response: Response, source: string, method: string): Response {
  const headers = new Headers(response.headers);
  headers.set('X-Cache', source);
  return new Response(method === 'HEAD' ? null : response.body, { status: response.status, headers });
}

export async function handleRequest(request: Request, env: Env, ctx: ContextLike, cache: CacheLike): Promise<Response> {
  if (request.method === 'OPTIONS') {
    const headers = baseHeaders();
    headers.set('Access-Control-Allow-Methods', 'GET, HEAD, OPTIONS');
    headers.set('Access-Control-Allow-Headers', '*');
    headers.set('Access-Control-Max-Age', '86400');
    return new Response(null, { status: 204, headers });
  }

  if (request.method !== 'GET' && request.method !== 'HEAD') {
    const response = plain(405, 'Method Not Allowed');
    response.headers.set('Allow', 'GET, HEAD, OPTIONS');
    return response;
  }

  const url = new URL(request.url);
  const key = parseKey(url.pathname);
  if (!key) {
    return plain(404, 'Not Found', 60);
  }

  // Query strings never change the content: normalise the cache key so they cannot bust the cache.
  const cacheKey = new Request(`${url.origin}${url.pathname}`, { method: 'GET' });
  const cached = await cache.match(cacheKey);
  if (cached) {
    return finalize(cached, 'HIT', request.method);
  }

  const object = await env.BUCKET.get(key);
  if (object) {
    const response = new Response(object.body, { status: 200, headers: objectHeaders(key, object.size, object.httpEtag) });
    ctx.waitUntil(cache.put(cacheKey, response.clone()));
    return finalize(response, 'MISS', request.method);
  }

  // R2 miss (e.g. upload still propagating): fall back to the Oracle origin, which serves only public objects.
  if (env.ORIGIN_URL && env.ORIGIN_SHARED_SECRET) {
    let origin: Response;
    try {
      origin = await fetch(`${env.ORIGIN_URL.replace(/\/+$/, '')}/internal/objects/${key}`, {
        headers: { 'X-Origin-Secret': env.ORIGIN_SHARED_SECRET },
        signal: AbortSignal.timeout(ORIGIN_TIMEOUT_MS),
      });
    } catch {
      return plain(502, 'Origin unavailable');
    }

    if (origin.status === 200) {
      const length = origin.headers.get('Content-Length');
      const response = new Response(origin.body, {
        status: 200,
        headers: objectHeaders(key, length ? Number(length) : null, origin.headers.get('ETag')),
      });
      ctx.waitUntil(cache.put(cacheKey, response.clone()));
      return finalize(response, 'ORIGIN', request.method);
    }

    if (origin.status !== 404) {
      return plain(502, 'Origin error');
    }
  }

  return plain(404, 'Not Found', 60);
}

export default {
  async fetch(request: Request, env: Env, ctx: ContextLike): Promise<Response> {
    // caches.default only works on a custom domain route (it is inert on *.workers.dev).
    const cache = (globalThis as unknown as { caches: { default: CacheLike } }).caches.default;
    return handleRequest(request, env, ctx, cache);
  },
};
