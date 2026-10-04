/**
 * Pre-upload check of a chapter archive, entirely in the browser.
 *
 * Reads only the ZIP central directory (a few KB at the end of the file, so even a 200 MB archive opens
 * instantly) and applies the SAME rules as the server's SafeZipReader: which entries become pages, which are
 * ignored, and the natural page order. Page images are decompressed one at a time, only for thumbnails.
 * Keep `naturalCompare`, `IGNORED_NAMES` and `PAGE_EXTENSIONS` in sync with
 * backend/src/Domain/Common/NaturalStringComparer.cs and Infrastructure/Archives/SafeZipReader.cs.
 */
import { inflateSync } from 'fflate';

export const PAGE_EXTENSIONS = ['.jpg', '.jpeg', '.png', '.webp'];
const IGNORED_NAMES = ['thumbs.db', 'desktop.ini', '.ds_store'];
/** Image types people commonly put in chapters that the server will NOT turn into pages. */
const OTHER_IMAGE_EXTENSIONS = ['.gif', '.bmp', '.tif', '.tiff', '.psd', '.heic', '.heif', '.avif', '.jfif', '.svg', '.jxl'];

export interface UploadLimits {
  maxUploadSize: number;
  maxExtractedSize: number;
  maxPages: number;
  maxEntrySize: number;
  maxCompressionRatio: number;
  maxImageDimension: number;
  maxImagePixels: number;
  pageMaxWidth: number;
}

export interface ZipEntry {
  name: string;
  method: number;
  compressedSize: number;
  size: number;
  localHeaderOffset: number;
  encrypted: boolean;
  symlink: boolean;
}

export interface PlannedPage {
  page: number;
  entry: ZipEntry;
}

export type IssueLevel = 'error' | 'warning' | 'info';
export interface Issue {
  level: IssueLevel;
  message: string;
}

export interface ArchivePlan {
  pages: PlannedPage[];
  skippedImages: string[];
  ignoredFiles: string[];
  totalSize: number;
  issues: Issue[];
  /** No error: the server is expected to accept the archive (decoding problems can still surface there). */
  ok: boolean;
}

export class ZipReadError extends Error {}

// ---------------------------------------------------------------- ordering / filtering (mirrors the server)

const isDigit = (c: string) => c >= '0' && c <= '9';
const upper = (c: string) => {
  const u = c.toUpperCase();
  return u.length === 1 ? u : c; // .NET's ToUpperInvariant maps per UTF-16 unit; never expand ("ß" → "SS")
};

/** Same order as the server's NaturalStringComparer: "2.jpg" < "10.jpg", case-insensitive otherwise. */
export function naturalCompare(x: string, y: string): number {
  if (x === y) return 0;
  let i = 0;
  let j = 0;
  while (i < x.length && j < y.length) {
    if (isDigit(x.charAt(i)) && isDigit(y.charAt(j))) {
      const si = i;
      const sj = j;
      while (i < x.length && isDigit(x.charAt(i))) i++;
      while (j < y.length && isDigit(y.charAt(j))) j++;
      const a = x.slice(si, i).replace(/^0+/, '');
      const b = y.slice(sj, j).replace(/^0+/, '');
      if (a.length !== b.length) return a.length < b.length ? -1 : 1;
      if (a !== b) return a < b ? -1 : 1;
      const raw = i - si - (j - sj);
      if (raw !== 0) return raw < 0 ? -1 : 1;
    } else {
      const a = upper(x.charAt(i)).charCodeAt(0);
      const b = upper(y.charAt(j)).charCodeAt(0);
      if (a !== b) return a < b ? -1 : 1;
      i++;
      j++;
    }
  }
  const rest = x.length - i - (y.length - j);
  if (rest !== 0) return rest < 0 ? -1 : 1;
  return x < y ? -1 : x > y ? 1 : 0;
}

export function isIgnored(name: string): boolean {
  const normalized = name.replace(/\\/g, '/');
  if (normalized.toLowerCase().startsWith('__macosx/')) return true;
  const segments = normalized.split('/');
  if (segments.some((s) => s.startsWith('.'))) return true;
  return IGNORED_NAMES.includes((segments.at(-1) ?? '').toLowerCase());
}

export function extensionOf(name: string): string {
  const base = name.replace(/\\/g, '/').split('/').pop() ?? '';
  const dot = base.lastIndexOf('.');
  return dot <= 0 ? '' : base.slice(dot).toLowerCase();
}

// ---------------------------------------------------------------- central directory

const u16 = (v: DataView, o: number) => v.getUint16(o, true);
const u32 = (v: DataView, o: number) => v.getUint32(o, true);
const u64 = (v: DataView, o: number) => Number(v.getBigUint64(o, true));

async function readBytes(file: Blob, start: number, end: number): Promise<DataView> {
  return new DataView(await file.slice(start, end).arrayBuffer());
}

/** Lists the entries of a ZIP file (ZIP64 aware) without reading the compressed data. */
export async function readZipEntries(file: Blob): Promise<ZipEntry[]> {
  const tailSize = Math.min(file.size, 22 + 65_535);
  const tail = await readBytes(file, file.size - tailSize, file.size);
  let eocd = -1;
  for (let i = tail.byteLength - 22; i >= 0; i--) {
    if (u32(tail, i) === 0x06054b50) {
      eocd = i;
      break;
    }
  }
  if (eocd < 0) throw new ZipReadError('Tệp không phải ZIP/CBZ hợp lệ (hoặc bị hỏng).');

  let count = u16(tail, eocd + 10);
  let cdSize = u32(tail, eocd + 12);
  let cdOffset = u32(tail, eocd + 16);
  if (count === 0xffff || cdSize === 0xffffffff || cdOffset === 0xffffffff) {
    const locator = eocd - 20;
    if (locator < 0 || u32(tail, locator) !== 0x07064b50) throw new ZipReadError('Không đọc được danh mục ZIP64.');
    const recordOffset = u64(tail, locator + 8);
    const record = await readBytes(file, recordOffset, recordOffset + 56);
    if (u32(record, 0) !== 0x06064b50) throw new ZipReadError('Không đọc được danh mục ZIP64.');
    count = u64(record, 32);
    cdSize = u64(record, 40);
    cdOffset = u64(record, 48);
  }
  if (cdOffset + cdSize > file.size) throw new ZipReadError('Tệp nén bị cắt cụt hoặc hỏng.');

  const cd = await readBytes(file, cdOffset, cdOffset + cdSize);
  const utf8 = new TextDecoder('utf-8');
  const entries: ZipEntry[] = [];
  let p = 0;
  for (let n = 0; n < count; n++) {
    if (p + 46 > cd.byteLength || u32(cd, p) !== 0x02014b50) throw new ZipReadError('Danh mục tệp nén bị hỏng.');
    const flags = u16(cd, p + 8);
    const method = u16(cd, p + 10);
    let compressedSize = u32(cd, p + 20);
    let size = u32(cd, p + 24);
    const nameLength = u16(cd, p + 28);
    const extraLength = u16(cd, p + 30);
    const commentLength = u16(cd, p + 32);
    const external = u32(cd, p + 38);
    let localHeaderOffset = u32(cd, p + 42);
    const name = utf8.decode(new Uint8Array(cd.buffer, cd.byteOffset + p + 46, nameLength));

    // ZIP64 extended information extra field.
    let e = p + 46 + nameLength;
    const extraEnd = e + extraLength;
    while (e + 4 <= extraEnd) {
      const id = u16(cd, e);
      const len = u16(cd, e + 2);
      if (id === 0x0001) {
        let q = e + 4;
        if (size === 0xffffffff) { size = u64(cd, q); q += 8; }
        if (compressedSize === 0xffffffff) { compressedSize = u64(cd, q); q += 8; }
        if (localHeaderOffset === 0xffffffff) { localHeaderOffset = u64(cd, q); }
      }
      e += 4 + len;
    }

    entries.push({
      name,
      method,
      compressedSize,
      size,
      localHeaderOffset,
      encrypted: (flags & 1) === 1,
      symlink: ((external >>> 16) & 0xf000) === 0xa000,
    });
    p += 46 + nameLength + extraLength + commentLength;
  }
  return entries;
}

/** Decompresses one entry (stored or deflate) — used for thumbnails only. */
export async function readEntry(file: Blob, entry: ZipEntry): Promise<Uint8Array> {
  const header = await readBytes(file, entry.localHeaderOffset, entry.localHeaderOffset + 30);
  if (u32(header, 0) !== 0x04034b50) throw new ZipReadError('Không đọc được ảnh trong tệp nén.');
  const start = entry.localHeaderOffset + 30 + u16(header, 26) + u16(header, 28);
  const data = new Uint8Array(await file.slice(start, start + entry.compressedSize).arrayBuffer());
  if (entry.method === 0) return data;
  if (entry.method === 8) return inflateSync(data, { out: new Uint8Array(entry.size) });
  throw new ZipReadError('Kiểu nén không được hỗ trợ.');
}

// ---------------------------------------------------------------- plan

const fmtMB = (bytes: number) => `${Math.round(bytes / (1024 * 1024))} MB`;
const baseName = (name: string) => name.replace(/\\/g, '/').split('/').pop() ?? name;

/** Last run of digits in the file name (without extension): "chap-02_trang-007.jpg" → 7. */
function pageNumberOf(name: string): number | null {
  const base = baseName(name);
  const stem = base.slice(0, base.length - extensionOf(base).length);
  const runs = stem.match(/\d+/g);
  return runs ? Number(runs[runs.length - 1]) : null;
}

function listSample(items: (string | number)[], max = 8): string {
  return items.length <= max ? items.join(', ') : `${items.slice(0, max).join(', ')} … (+${items.length - max})`;
}

/** Decides which entries become pages (and in which order) and explains anything surprising. */
export function planArchive(entries: ZipEntry[], limits: UploadLimits): ArchivePlan {
  const issues: Issue[] = [];
  const pageEntries: ZipEntry[] = [];
  const skippedImages: string[] = [];
  const ignoredFiles: string[] = [];
  let totalSize = 0;

  for (const entry of entries) {
    const name = entry.name;
    if (name.endsWith('/') || name.endsWith('\\')) continue;
    if (entry.symlink) issues.push({ level: 'error', message: `Tệp nén chứa liên kết tượng trưng: ${name}` });
    if (entry.encrypted) {
      issues.push({ level: 'error', message: 'Tệp nén có đặt mật khẩu — hãy nén lại không đặt mật khẩu.' });
      break;
    }
    if (isIgnored(name)) continue;
    const ext = extensionOf(name);
    if (!PAGE_EXTENSIONS.includes(ext)) {
      (OTHER_IMAGE_EXTENSIONS.includes(ext) ? skippedImages : ignoredFiles).push(name);
      continue;
    }
    if (entry.size <= 0) issues.push({ level: 'error', message: `Ảnh "${name}" rỗng (0 byte).` });
    else if (entry.size > limits.maxEntrySize) issues.push({ level: 'error', message: `Ảnh "${name}" lớn hơn ${fmtMB(limits.maxEntrySize)}.` });
    totalSize += entry.size;
    pageEntries.push(entry);
  }

  pageEntries.sort((a, b) => naturalCompare(a.name.replace(/\\/g, '/'), b.name.replace(/\\/g, '/')));
  const pages = pageEntries.map((entry, i) => ({ page: i + 1, entry }));

  if (pages.length === 0) issues.push({ level: 'error', message: 'Không có ảnh JPEG/PNG/WebP nào trong tệp nén.' });
  if (pages.length > limits.maxPages) issues.push({ level: 'error', message: `Có ${pages.length} ảnh, vượt giới hạn ${limits.maxPages} trang mỗi chương.` });
  if (totalSize > limits.maxExtractedSize) issues.push({ level: 'error', message: `Tổng dung lượng ảnh (${fmtMB(totalSize)}) vượt giới hạn ${fmtMB(limits.maxExtractedSize)}.` });

  if (skippedImages.length > 0) {
    issues.push({
      level: 'warning',
      message: `${skippedImages.length} ảnh có định dạng không hỗ trợ sẽ BỊ BỎ QUA (chỉ nhận JPEG/PNG/WebP): ${listSample(skippedImages.map(baseName))}. Hãy chuyển đổi nếu đó là trang truyện.`,
    });
  }

  // Numbering sanity: catches missing or duplicated pages before anything is uploaded.
  const numbers = pages.map((p) => pageNumberOf(p.entry.name));
  if (pages.length > 1 && numbers.every((n) => n !== null)) {
    const nums = numbers as number[];
    const seen = new Map<number, string[]>();
    pages.forEach((p, i) => {
      const n = nums[i] as number;
      seen.set(n, [...(seen.get(n) ?? []), baseName(p.entry.name)]);
    });
    const duplicates = [...seen.values()].filter((names) => names.length > 1);
    if (duplicates.length > 0) {
      issues.push({ level: 'warning', message: `Có tệp trùng số thứ tự: ${listSample(duplicates.map((d) => d.join(' / ')), 4)}. Kiểm tra lại thứ tự trang bên dưới.` });
    }
    const sorted = [...seen.keys()].sort((a, b) => a - b);
    const min = sorted[0] ?? 0;
    const max = sorted.at(-1) ?? min;
    if (max - min < 5000) {
      const missing: number[] = [];
      for (let n = min; n <= max; n++) if (!seen.has(n)) missing.push(n);
      if (missing.length > 0) issues.push({ level: 'warning', message: `Có thể thiếu trang — không thấy số: ${listSample(missing)}.` });
    }
    if (min > 1) issues.push({ level: 'info', message: `Số thứ tự bắt đầu từ ${min}: ảnh đó sẽ là trang 1, các trang được đánh số lại liên tục.` });
  } else if (pages.length > 1 && numbers.some((n) => n === null)) {
    issues.push({ level: 'info', message: 'Một số tên tệp không có số thứ tự — trang được sắp theo tên tệp (A→Z). Hãy kiểm tra thứ tự bên dưới.' });
  }

  const folders = new Set(pages.map((p) => p.entry.name.replace(/\\/g, '/').split('/').slice(0, -1).join('/')));
  if (folders.size > 1) issues.push({ level: 'info', message: `Ảnh nằm trong ${folders.size} thư mục khác nhau — thứ tự xếp theo đường dẫn đầy đủ.` });
  if (ignoredFiles.length > 0) issues.push({ level: 'info', message: `Bỏ qua ${ignoredFiles.length} tệp không phải ảnh: ${listSample(ignoredFiles.map(baseName), 5)}.` });

  return { pages, skippedImages, ignoredFiles, totalSize, issues, ok: !issues.some((i) => i.level === 'error') };
}

export function mimeFor(name: string): string {
  const ext = extensionOf(name);
  return ext === '.png' ? 'image/png' : ext === '.webp' ? 'image/webp' : 'image/jpeg';
}
