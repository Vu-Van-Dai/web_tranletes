const dateFormatter = new Intl.DateTimeFormat('vi-VN', { day: '2-digit', month: '2-digit', year: 'numeric' });
const dateTimeFormatter = new Intl.DateTimeFormat('vi-VN', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' });
const relative = new Intl.RelativeTimeFormat('vi', { numeric: 'auto' });
const numberFormatter = new Intl.NumberFormat('vi-VN');

export const formatDate = (iso: string | null | undefined) => (iso ? dateFormatter.format(new Date(iso)) : '—');

export const formatDateTime = (iso: string | null | undefined) => (iso ? dateTimeFormatter.format(new Date(iso)) : '—');

export const formatNumber = (n: number) => numberFormatter.format(n);

/** "3 giờ trước", "hôm qua", … */
export function timeAgo(iso: string | null | undefined, now = Date.now()): string {
  if (!iso) return '—';
  const seconds = Math.round((new Date(iso).getTime() - now) / 1000);
  const abs = Math.abs(seconds);
  if (abs < 60) return 'vừa xong';
  if (abs < 3600) return relative.format(Math.round(seconds / 60), 'minute');
  if (abs < 86_400) return relative.format(Math.round(seconds / 3600), 'hour');
  if (abs < 30 * 86_400) return relative.format(Math.round(seconds / 86_400), 'day');
  if (abs < 365 * 86_400) return relative.format(Math.round(seconds / (30 * 86_400)), 'month');
  return relative.format(Math.round(seconds / (365 * 86_400)), 'year');
}

export const isRecent = (iso: string | null | undefined, hours = 48) =>
  !!iso && Date.now() - new Date(iso).getTime() < hours * 3_600_000;

export function formatBytes(bytes: number | null | undefined): string {
  if (bytes == null) return '—';
  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  let value = bytes;
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit++;
  }
  return `${value.toLocaleString('vi-VN', { maximumFractionDigits: unit === 0 ? 0 : 1 })} ${units[unit]}`;
}

/** "Chương 12" / "Chương 10.5 – Tựa đề". */
export function chapterLabel(number: number, title?: string | null): string {
  const n = Number(number).toLocaleString('vi-VN', { maximumFractionDigits: 2 });
  return title ? `Chương ${n} – ${title}` : `Chương ${n}`;
}

export function slugify(value: string): string {
  return value
    .replace(/đ/g, 'd')
    .replace(/Đ/g, 'D')
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, 100);
}

export const cn = (...classes: (string | false | null | undefined)[]) => classes.filter(Boolean).join(' ');
