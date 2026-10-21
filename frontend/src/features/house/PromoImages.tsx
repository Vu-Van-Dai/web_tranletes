import { useRef, useSyncExternalStore } from 'react';
import { ImagePlus, Megaphone, X } from 'lucide-react';
import { Button, Select } from '@/components/ui';
import type { ExtraPart } from '@/services/http';
import { formatBytes } from '@/utils/format';

/** At most this many promotional images per chapter (enforced by the server too). */
export const MAX_PROMOS = 2;
const MAX_PROMO_BYTES = 8 * 1024 * 1024;

export type PromoPosition = 'start' | 'end';

export interface PromoImage {
  id: string;
  file: File;
  position: PromoPosition;
  url: string;
}

/**
 * Promotional images (house poster, credits page…) chosen once and reused for every upload in this browser
 * session — single chapters and bulk uploads alike. The server converts them to WebP and inserts them as
 * pages at the start or the end of the chapter.
 */
// Object URLs live as long as the selection (it outlives the page), and are revoked when an image is removed.
let promos: PromoImage[] = [];
const listeners = new Set<() => void>();
const emit = () => listeners.forEach((l) => l());

export function usePromos(): PromoImage[] {
  return useSyncExternalStore(
    (l) => {
      listeners.add(l);
      return () => listeners.delete(l);
    },
    () => promos,
  );
}

function setPromos(next: PromoImage[]) {
  promos = next;
  emit();
}

/** Multipart parts for the upload request: promoStart / promoEnd, sent before the archive. */
export function promoParts(list: PromoImage[]): ExtraPart[] {
  return list.map((p) => ({ field: p.position === 'start' ? 'promoStart' : 'promoEnd', file: p.file }));
}

export function PromoImagesPicker({ disabled }: { disabled?: boolean }) {
  const list = usePromos();
  const input = useRef<HTMLInputElement>(null);
  const error = useRef<HTMLParagraphElement>(null);

  const add = (files: FileList | null) => {
    if (!files) return;
    const next = [...list];
    for (const file of Array.from(files)) {
      if (next.length >= MAX_PROMOS) break;
      if (!/^image\/(jpeg|png|webp)$/.test(file.type)) {
        if (error.current) error.current.textContent = `"${file.name}" không phải ảnh JPEG/PNG/WebP.`;
        continue;
      }
      if (file.size > MAX_PROMO_BYTES) {
        if (error.current) error.current.textContent = `"${file.name}" lớn hơn ${formatBytes(MAX_PROMO_BYTES)}.`;
        continue;
      }
      next.push({ id: crypto.randomUUID(), file, position: next.some((p) => p.position === 'end') ? 'start' : 'end', url: URL.createObjectURL(file) });
    }
    setPromos(next);
  };

  const remove = (id: string) => {
    const item = list.find((p) => p.id === id);
    if (item) URL.revokeObjectURL(item.url);
    setPromos(list.filter((p) => p.id !== id));
  };

  return (
    <div className="space-y-3 rounded-2xl border border-line bg-surface-2/60 p-4" data-testid="promo-picker">
      <div className="flex items-start gap-2">
        <Megaphone className="mt-0.5 h-4 w-4 shrink-0 text-accent" aria-hidden />
        <div className="min-w-0 text-sm">
          <p className="font-medium">Ảnh quảng bá (không bắt buộc, tối đa {MAX_PROMOS} ảnh)</p>
          <p className="text-muted">Ví dụ poster của nhóm. Chọn trước khi tải tệp ZIP — ảnh được chuyển sang WebP và chèn vào đầu hoặc cuối chương. Lựa chọn được giữ cho các lần tải tiếp theo.</p>
        </div>
      </div>
      {list.length > 0 && (
        <ul className="grid gap-3 sm:grid-cols-2">
          {list.map((p) => (
            <li key={p.id} className="flex items-center gap-3 rounded-xl border border-line bg-surface p-2">
              <img src={p.url} alt="" className="h-16 w-12 shrink-0 rounded-md object-cover" />
              <div className="min-w-0 flex-1 space-y-1">
                <p className="truncate text-xs font-medium" title={p.file.name}>{p.file.name}</p>
                <Select
                  aria-label={`Vị trí của ${p.file.name}`}
                  value={p.position}
                  disabled={disabled}
                  className="h-8 text-xs"
                  onChange={(e) => setPromos(list.map((x) => (x.id === p.id ? { ...x, position: e.target.value as PromoPosition } : x)))}
                >
                  <option value="start">Đầu chương</option>
                  <option value="end">Cuối chương</option>
                </Select>
              </div>
              <Button variant="ghost" size="icon" disabled={disabled} aria-label={`Bỏ ${p.file.name}`} onClick={() => remove(p.id)}>
                <X className="h-4 w-4" />
              </Button>
            </li>
          ))}
        </ul>
      )}
      {list.length < MAX_PROMOS && (
        <Button variant="outline" size="sm" disabled={disabled} icon={<ImagePlus className="h-4 w-4" />} onClick={() => input.current?.click()}>
          Thêm ảnh quảng bá
        </Button>
      )}
      <input ref={input} type="file" multiple accept="image/jpeg,image/png,image/webp" className="sr-only" data-testid="promo-input"
        onChange={(e) => {
          if (error.current) error.current.textContent = '';
          add(e.target.files);
          e.target.value = '';
        }} />
      <p ref={error} className="text-sm text-danger empty:hidden" role="alert" />
    </div>
  );
}
