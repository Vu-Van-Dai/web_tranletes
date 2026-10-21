import { useEffect, useRef, useState } from 'react';
import { AlertTriangle, CircleX, Info, Upload } from 'lucide-react';
import { Button, Modal, Skeleton } from '@/components/ui';
import { cn, formatBytes } from '@/utils/format';
import type { PromoImage } from './PromoImages';
import { mimeFor, planArchive, readEntry, readZipEntries, type ArchivePlan, type Issue, type PlannedPage, type UploadLimits } from './zipInspect';

/**
 * Shown after a chapter archive is chosen and BEFORE anything is uploaded: the exact page order the server
 * will produce (page n ← original file), images that would be skipped, missing/duplicate numbers, limits.
 */
export function UploadCheckDialog({ file, limits, promos = [], onCancel, onConfirm }: { file: File; limits: UploadLimits; promos?: PromoImage[]; onCancel: () => void; onConfirm: () => void }) {
  const [plan, setPlan] = useState<ArchivePlan | null>(null);
  const [readError, setReadError] = useState<string | null>(null);
  const [broken, setBroken] = useState<Record<number, string>>({});

  useEffect(() => {
    let alive = true;
    readZipEntries(file)
      // Promotional pages count toward the page limit.
      .then((entries) => alive && setPlan(planArchive(entries, { ...limits, maxPages: Math.max(1, limits.maxPages - promos.length) })))
      .catch((e: Error) => alive && setReadError(e.message || 'Không đọc được tệp nén.'));
    return () => {
      alive = false;
    };
  }, [file, limits, promos.length]);

  const startPromos = promos.filter((p) => p.position === 'start');
  const endPromos = promos.filter((p) => p.position === 'end');
  const totalPages = (plan?.pages.length ?? 0) + promos.length;

  const brokenIssues: Issue[] = Object.entries(broken).map(([page, reason]) => ({ level: 'warning', message: `Trang ${page}: ${reason}` }));
  const issues = [...(plan?.issues ?? []), ...brokenIssues];
  const order: Record<Issue['level'], number> = { error: 0, warning: 1, info: 2 };
  issues.sort((a, b) => order[a.level] - order[b.level]);
  const canUpload = plan !== null && plan.ok && file.size <= limits.maxUploadSize;

  return (
    <Modal
      open
      size="lg"
      onClose={onCancel}
      title="Kiểm tra trước khi tải lên"
      footer={
        <>
          <Button variant="outline" onClick={onCancel}>Chọn tệp khác</Button>
          <Button icon={<Upload className="h-4 w-4" />} disabled={!canUpload} onClick={onConfirm} data-testid="confirm-upload">
            {plan ? `Tải lên ${totalPages} trang` : 'Tải lên'}
          </Button>
        </>
      }
    >
      <div className="space-y-4" data-testid="upload-check">
        <p className="text-sm text-muted">
          <span className="font-medium text-ink">{file.name}</span> · {formatBytes(file.size)}
          {plan && <> · <span className="font-medium text-ink">{totalPages} trang</span> theo đúng thứ tự dưới đây</>}
          {promos.length > 0 && <> (gồm {promos.length} ảnh quảng bá)</>}
        </p>

        {readError && <IssueRow issue={{ level: 'error', message: readError }} />}
        {file.size > limits.maxUploadSize && <IssueRow issue={{ level: 'error', message: `Tệp lớn hơn giới hạn ${formatBytes(limits.maxUploadSize)}.` }} />}
        {issues.length > 0 && (
          <ul className="space-y-2" data-testid="upload-issues">
            {issues.map((issue, i) => <IssueRow key={i} issue={issue} />)}
          </ul>
        )}
        {plan?.ok && issues.every((i) => i.level === 'info') && (
          <p className="rounded-xl bg-success-soft px-3 py-2 text-sm text-success">Không phát hiện vấn đề. Hãy lướt nhanh thứ tự trang trước khi tải lên.</p>
        )}

        {!plan && !readError && <Skeleton className="h-48 w-full rounded-xl" />}
        {plan && plan.pages.length > 0 && (
          <ol className="grid grid-cols-3 gap-3 sm:grid-cols-4 md:grid-cols-5" data-testid="upload-pages">
            {startPromos.map((p, i) => <PromoThumb key={p.id} promo={p} page={i + 1} />)}
            {plan.pages.map((p) => (
              <Thumb key={p.page} file={file} planned={p} offset={startPromos.length} limits={limits} broken={broken[p.page]}
                onBroken={(reason) => setBroken((b) => (b[p.page] ? b : { ...b, [p.page]: reason }))} />
            ))}
            {endPromos.map((p, i) => <PromoThumb key={p.id} promo={p} page={startPromos.length + plan.pages.length + i + 1} />)}
          </ol>
        )}
      </div>
    </Modal>
  );
}

function PromoThumb({ promo, page }: { promo: PromoImage; page: number }) {
  return (
    <li className="min-w-0 list-none" data-testid="upload-promo">
      <div className="flex aspect-[3/4] items-center justify-center overflow-hidden rounded-lg border-2 border-gold bg-gold-soft">
        <img src={promo.url} alt={`Trang ${page} (ảnh quảng bá)`} className="h-full w-full object-contain" />
      </div>
      <p className="mt-1 text-xs font-semibold">Trang {page}</p>
      <p className="truncate text-xs text-muted" title={promo.file.name}>Ảnh quảng bá · {promo.file.name}</p>
    </li>
  );
}

function IssueRow({ issue }: { issue: Issue }) {
  const Icon = issue.level === 'error' ? CircleX : issue.level === 'warning' ? AlertTriangle : Info;
  return (
    <li
      className={cn(
        'flex list-none items-start gap-2 rounded-xl px-3 py-2 text-sm',
        issue.level === 'error' && 'bg-danger-soft text-danger',
        issue.level === 'warning' && 'bg-warn-soft text-warn',
        issue.level === 'info' && 'bg-surface-2 text-muted',
      )}
      role={issue.level === 'info' ? undefined : 'alert'}
    >
      <Icon className="mt-0.5 h-4 w-4 shrink-0" aria-hidden />
      <span>{issue.message}</span>
    </li>
  );
}

/** Decompresses one page only when it scrolls into view; object URL revoked on unmount. */
function Thumb({ file, planned, offset, limits, broken, onBroken }: { file: File; planned: PlannedPage; offset: number; limits: UploadLimits; broken?: string; onBroken: (reason: string) => void }) {
  const ref = useRef<HTMLLIElement>(null);
  const [url, setUrl] = useState<string | null>(null);
  const [size, setSize] = useState<string | null>(null);
  const name = planned.entry.name;

  useEffect(() => {
    const el = ref.current;
    if (!el) return;
    let objectUrl: string | null = null;
    let cancelled = false;
    const observer = new IntersectionObserver(
      (entries) => {
        if (!entries.some((e) => e.isIntersecting)) return;
        observer.disconnect();
        readEntry(file, planned.entry)
          .then((bytes) => {
            if (cancelled) return;
            objectUrl = URL.createObjectURL(new Blob([bytes as Uint8Array<ArrayBuffer>], { type: mimeFor(name) }));
            setUrl(objectUrl);
          })
          .catch(() => !cancelled && onBroken(`không đọc được "${name}" trong tệp nén.`));
      },
      { rootMargin: '200px' },
    );
    observer.observe(el);
    return () => {
      cancelled = true;
      observer.disconnect();
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps -- one extraction per page
  }, [file, planned.entry]);

  return (
    <li ref={ref} className="min-w-0 list-none" data-testid="upload-page">
      <div className={cn('flex aspect-[3/4] items-center justify-center overflow-hidden rounded-lg border bg-surface-2', broken ? 'border-warn' : 'border-line')}>
        {url ? (
          <img
            src={url}
            alt={`Trang ${planned.page + offset}`}
            className="h-full w-full object-contain"
            decoding="async"
            onLoad={(e) => {
              const { naturalWidth: w, naturalHeight: h } = e.currentTarget;
              setSize(`${w}×${h}`);
              if (Math.max(w, h) > limits.maxImageDimension || w * h > limits.maxImagePixels) onBroken(`"${name}" quá lớn (${w}×${h}px) — máy chủ sẽ từ chối.`);
            }}
            onError={() => onBroken(`"${name}" bị hỏng hoặc không đúng định dạng ảnh.`)}
          />
        ) : (
          <span className="text-xs text-muted">{broken ? 'Lỗi' : '…'}</span>
        )}
      </div>
      <p className="mt-1 text-xs font-semibold">Trang {planned.page + offset}</p>
      <p className="truncate text-xs text-muted" title={name}>{name.replace(/\\/g, '/').split('/').pop()}</p>
      {size && <p className="text-[11px] text-muted">{size}</p>}
    </li>
  );
}
