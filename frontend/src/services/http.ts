import { env } from '@/config/env';
import type { Session } from '@/types/api';

/** Error carrying the backend's ProblemDetails (stable code + Vietnamese message). */
export class ApiError extends Error {
  constructor(
    public readonly status: number,
    public readonly code: string,
    message: string,
    public readonly errors: Record<string, string[]> | null = null,
    public readonly traceId: string | null = null,
  ) {
    super(message);
    this.name = 'ApiError';
  }
}

// ---------------- Access token (memory only — never localStorage) ----------------
let accessToken: string | null = null;
let sessionListener: ((session: Session | null) => void) | null = null;

export function setAccessToken(token: string | null) {
  accessToken = token;
}

export function onSessionChange(listener: (session: Session | null) => void) {
  sessionListener = listener;
}

/** House selected in the workspace; sent as X-House-Id (a selector the server re-validates). */
let currentHouseId: string | null = null;
export function setCurrentHouse(houseId: string | null) {
  currentHouseId = houseId;
}
export function getCurrentHouse() {
  return currentHouseId;
}

export function apiUrl(path: string) {
  return `${env.apiUrl}${path}`;
}

async function parseError(response: Response): Promise<ApiError> {
  let body: { code?: string; detail?: string; errors?: Record<string, string[]>; traceId?: string } = {};
  try {
    body = await response.json();
  } catch {
    // non-JSON error (proxy / network layer)
  }

  const fallback =
    response.status === 429
      ? 'Bạn thao tác quá nhanh, vui lòng thử lại sau.'
      : response.status >= 500
        ? 'Máy chủ đang gặp sự cố, vui lòng thử lại sau.'
        : 'Yêu cầu không thành công.';
  return new ApiError(response.status, body.code ?? `http_${response.status}`, body.detail ?? fallback, body.errors ?? null, body.traceId ?? null);
}

// ---------------- Refresh (single flight, serialized across tabs) ----------------
let refreshInFlight: Promise<Session | null> | null = null;

async function doRefresh(): Promise<Session | null> {
  const response = await fetch(apiUrl('/api/auth/refresh'), {
    method: 'POST',
    credentials: 'include',
    headers: { 'X-Requested-With': 'fetch' },
  });
  // 204 = no refresh cookie (anonymous visitor); any error = expired/revoked session.
  if (!response.ok || response.status === 204) {
    setAccessToken(null);
    sessionListener?.(null);
    return null;
  }

  const session = (await response.json()) as Session;
  setAccessToken(session.accessToken);
  sessionListener?.(session);
  return session;
}

/** Rotating refresh tokens must not be used by two tabs at once — Web Locks serialize them. */
export function refreshSession(): Promise<Session | null> {
  if (!refreshInFlight) {
    const run = () => doRefresh();
    const locked = typeof navigator !== 'undefined' && navigator.locks
      ? navigator.locks.request('cp-refresh', run)
      : run();
    refreshInFlight = Promise.resolve(locked).finally(() => {
      refreshInFlight = null;
    });
  }

  return refreshInFlight;
}

/** Auth headers for raw fetches that cannot go through request() (e.g. streamed private package downloads). */
export function authHeaders(house = false): Record<string, string> {
  const headers: Record<string, string> = {};
  if (accessToken) headers.Authorization = `Bearer ${accessToken}`;
  if (house && currentHouseId) headers['X-House-Id'] = currentHouseId;
  return headers;
}

// ---------------- Requests ----------------
export interface RequestOptions {
  method?: string;
  body?: unknown;
  /** Send X-House-Id (house workspace endpoints). */
  house?: boolean;
  signal?: AbortSignal;
}

export async function request<T>(path: string, options: RequestOptions = {}, retried = false): Promise<T> {
  const headers: Record<string, string> = { Accept: 'application/json' };
  if (options.body !== undefined) headers['Content-Type'] = 'application/json';
  if (accessToken) headers.Authorization = `Bearer ${accessToken}`;
  if (options.house && currentHouseId) headers['X-House-Id'] = currentHouseId;

  let response: Response;
  try {
    response = await fetch(apiUrl(path), {
      method: options.method ?? 'GET',
      headers,
      body: options.body === undefined ? undefined : JSON.stringify(options.body),
      credentials: 'include',
      signal: options.signal,
    });
  } catch (e) {
    if ((e as Error).name === 'AbortError') throw e;
    throw new ApiError(0, 'network_error', 'Không kết nối được tới máy chủ. Vui lòng kiểm tra mạng.');
  }

  if (response.status === 401 && accessToken && !retried) {
    const session = await refreshSession();
    if (session) return request<T>(path, options, true);
  }

  if (!response.ok) throw await parseError(response);
  if (response.status === 204) return undefined as T;
  return (await response.json()) as T;
}

/** Multipart upload with byte progress (fetch has no upload progress events). */
export function uploadFile<T>(path: string, file: File, onProgress: (fraction: number) => void, signal?: AbortSignal): Promise<T> {
  return new Promise<T>((resolve, reject) => {
    const xhr = new XMLHttpRequest();
    xhr.open('POST', apiUrl(path));
    xhr.withCredentials = true;
    if (accessToken) xhr.setRequestHeader('Authorization', `Bearer ${accessToken}`);
    if (currentHouseId) xhr.setRequestHeader('X-House-Id', currentHouseId);
    xhr.upload.onprogress = (e) => e.lengthComputable && onProgress(e.loaded / e.total);
    xhr.onerror = () => reject(new ApiError(0, 'network_error', 'Mất kết nối khi tải lên. Vui lòng thử lại.'));
    xhr.onabort = () => reject(new DOMException('Aborted', 'AbortError'));
    xhr.onload = async () => {
      const response = new Response(xhr.responseText, { status: xhr.status, headers: { 'Content-Type': xhr.getResponseHeader('Content-Type') ?? '' } });
      if (xhr.status >= 200 && xhr.status < 300) resolve(JSON.parse(xhr.responseText) as T);
      else reject(await parseError(response));
    };
    signal?.addEventListener('abort', () => xhr.abort());
    const form = new FormData();
    form.append('file', file, file.name);
    xhr.send(form);
  });
}

export function putFile<T>(path: string, file: File): Promise<T> {
  return new Promise<T>((resolve, reject) => {
    const xhr = new XMLHttpRequest();
    xhr.open('PUT', apiUrl(path));
    xhr.withCredentials = true;
    if (accessToken) xhr.setRequestHeader('Authorization', `Bearer ${accessToken}`);
    if (currentHouseId) xhr.setRequestHeader('X-House-Id', currentHouseId);
    xhr.onerror = () => reject(new ApiError(0, 'network_error', 'Mất kết nối khi tải ảnh lên.'));
    xhr.onload = async () => {
      if (xhr.status >= 200 && xhr.status < 300) resolve(JSON.parse(xhr.responseText) as T);
      else reject(await parseError(new Response(xhr.responseText, { status: xhr.status })));
    };
    const form = new FormData();
    form.append('file', file, file.name);
    xhr.send(form);
  });
}
