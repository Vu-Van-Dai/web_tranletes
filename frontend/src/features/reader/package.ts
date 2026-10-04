/**
 * CPK1 chapter package parser (mirrors backend ComicPlatform.Infrastructure.Packaging).
 *
 *   Header 32 B : "CPK1" | u16 formatVersion | u16 headerSize | u32 pageCount | u32 indexSize
 *                 | u64 dataSize | u32 flags | u32 reserved
 *   Index 24 B/page : u32 pageNumber | u64 offset | u32 size | u16 contentType | u16 reserved | u16 width | u16 height
 *   Data    : raw WebP bytes (contiguous, page order)
 *   Trailer : 32 B SHA-256 over header + index + data
 */

export const HEADER_SIZE = 32;
export const INDEX_ENTRY_SIZE = 24;
export const TRAILER_SIZE = 32;
export const CONTENT_TYPE_WEBP = 1;

export interface PackageEntry {
  pageNumber: number;
  offset: number;
  size: number;
  width: number;
  height: number;
}

export interface ParsedPackage {
  formatVersion: number;
  dataStart: number;
  dataSize: number;
  entries: PackageEntry[];
  trailerHex: string;
}

export class PackageError extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'PackageError';
  }
}

const toHex = (bytes: Uint8Array) => Array.from(bytes, (b) => b.toString(16).padStart(2, '0')).join('');

/** Reads a little-endian u64 that must fit in a JS safe integer. */
function readU64(view: DataView, offset: number): number {
  const value = view.getBigUint64(offset, true);
  if (value > BigInt(Number.MAX_SAFE_INTEGER)) throw new PackageError('Giá trị 64-bit vượt giới hạn.');
  return Number(value);
}

export function parsePackage(bytes: Uint8Array, maxPages = 2000): ParsedPackage {
  if (bytes.byteLength < HEADER_SIZE + TRAILER_SIZE) throw new PackageError('Gói chương quá nhỏ.');
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);

  if (bytes[0] !== 0x43 || bytes[1] !== 0x50 || bytes[2] !== 0x4b || bytes[3] !== 0x31) {
    throw new PackageError('Không phải gói chương CPK1.');
  }

  const formatVersion = view.getUint16(4, true);
  if (formatVersion !== 1) throw new PackageError(`Phiên bản gói ${formatVersion} không được hỗ trợ.`);

  const headerSize = view.getUint16(6, true);
  if (headerSize < HEADER_SIZE) throw new PackageError('Kích thước header không hợp lệ.');

  const pageCount = view.getUint32(8, true);
  if (pageCount === 0 || pageCount > maxPages) throw new PackageError('Số trang không hợp lệ.');

  const indexSize = view.getUint32(12, true);
  if (indexSize !== pageCount * INDEX_ENTRY_SIZE) throw new PackageError('Kích thước chỉ mục không khớp.');

  const dataSize = readU64(view, 16);
  const dataStart = headerSize + indexSize;
  if (dataSize === 0 || dataStart + dataSize + TRAILER_SIZE !== bytes.byteLength) {
    throw new PackageError('Kích thước gói không khớp với khai báo.');
  }

  const entries: PackageEntry[] = [];
  let expected = 0;
  for (let i = 0; i < pageCount; i++) {
    const at = headerSize + i * INDEX_ENTRY_SIZE;
    const pageNumber = view.getUint32(at, true);
    const offset = readU64(view, at + 4);
    const size = view.getUint32(at + 12, true);
    const contentType = view.getUint16(at + 16, true);
    const width = view.getUint16(at + 20, true);
    const height = view.getUint16(at + 22, true);

    if (pageNumber !== i + 1) throw new PackageError('Số trang không liên tục.');
    if (offset !== expected || size === 0 || offset + size > dataSize) throw new PackageError(`Trang ${pageNumber} nằm ngoài vùng dữ liệu.`);
    if (contentType !== CONTENT_TYPE_WEBP) throw new PackageError(`Trang ${pageNumber} có định dạng không hỗ trợ.`);
    if (width === 0 || height === 0) throw new PackageError(`Trang ${pageNumber} có kích thước không hợp lệ.`);

    entries.push({ pageNumber, offset, size, width, height });
    expected += size;
  }

  if (expected !== dataSize) throw new PackageError('Vùng dữ liệu thừa hoặc thiếu byte.');

  return { formatVersion, dataStart, dataSize, entries, trailerHex: toHex(bytes.subarray(bytes.byteLength - TRAILER_SIZE)) };
}

/** SHA-256 of header+index+data must equal the trailer and the checksum announced by the API. */
export async function verifyPackage(bytes: Uint8Array, parsed: ParsedPackage, expectedSha256: string): Promise<void> {
  const body = bytes.subarray(0, bytes.byteLength - TRAILER_SIZE);
  const digest = toHex(new Uint8Array(await crypto.subtle.digest('SHA-256', body as Uint8Array<ArrayBuffer>)));
  if (digest !== parsed.trailerHex || digest !== expectedSha256.toLowerCase()) {
    throw new PackageError('Gói chương bị hỏng (sai mã kiểm tra).');
  }
}

export interface LoadedPackage {
  /** Single backing Blob; pages are zero-copy slices of it. */
  blob: Blob;
  parsed: ParsedPackage;
  pageBlob: (index: number) => Blob;
}

/**
 * Downloads a package with progress into one pre-sized buffer, validates it, verifies its SHA-256 and
 * returns a Blob-backed view. The ArrayBuffer is released afterwards: only the Blob stays in memory.
 */
export async function loadPackage(
  url: string,
  expected: { size: number; sha256: string },
  onProgress?: (loaded: number, total: number) => void,
  signal?: AbortSignal,
  headers?: Record<string, string>,
): Promise<LoadedPackage> {
  const response = await fetch(url, { signal, credentials: 'omit', headers });
  if (!response.ok) {
    throw new PackageError(response.status === 404 ? 'not_found' : `Tải gói chương thất bại (${response.status}).`);
  }

  const total = expected.size;
  let bytes: Uint8Array;
  if (response.body && total > 0) {
    bytes = new Uint8Array(total);
    const reader = response.body.getReader();
    let loaded = 0;
    for (;;) {
      const { done, value } = await reader.read();
      if (done) break;
      if (loaded + value.byteLength > total) throw new PackageError('Gói chương lớn hơn kích thước khai báo.');
      bytes.set(value, loaded);
      loaded += value.byteLength;
      onProgress?.(loaded, total);
    }
    if (loaded !== total) throw new PackageError('Gói chương tải về không đầy đủ.');
  } else {
    bytes = new Uint8Array(await response.arrayBuffer());
    onProgress?.(bytes.byteLength, bytes.byteLength);
  }

  const parsed = parsePackage(bytes);
  await verifyPackage(bytes, parsed, expected.sha256);
  const blob = new Blob([bytes as Uint8Array<ArrayBuffer>], { type: 'application/octet-stream' });
  return {
    blob,
    parsed,
    pageBlob: (index: number) => {
      const entry = parsed.entries[index];
      if (!entry) throw new PackageError('Trang không tồn tại.');
      const start = parsed.dataStart + entry.offset;
      return blob.slice(start, start + entry.size, 'image/webp');
    },
  };
}
