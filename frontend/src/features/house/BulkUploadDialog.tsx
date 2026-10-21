import { useEffect, useRef, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { FilePlus2, Upload } from 'lucide-react';
import { Badge, Button, Checkbox, Input, Modal, ProgressBar, useToast } from '@/components/ui';
import { useAuth } from '@/features/auth/AuthProvider';
import { ApiError } from '@/services/http';
import { houseApi } from '@/services/api';
import type { HouseChapterItem } from '@/types/api';
import { cn, formatBytes } from '@/utils/format';
import { PromoImagesPicker, promoParts, usePromos } from './PromoImages';
import { planArchive, readZipEntries, type UploadLimits } from './zipInspect';

type RowStatus = 'checking' | 'ready' | 'invalid' | 'creating' | 'uploading' | 'processing' | 'done' | 'published' | 'error' | 'skipped';

interface Row {
  key: string;
  file: File;
  number: string;
  pages: number | null;
  status: RowStatus;
  message: string | null;
  progress: number;
  chapterId: string | null;
}

const statusLabel: Record<RowStatus, string> = {
  checking: 'Đang kiểm tra…',
  ready: 'Sẵn sàng',
  invalid: 'Tệp lỗi',
  creating: 'Đang tạo chương…',
  uploading: 'Đang tải lên',
  processing: 'Đang xử lý',
  done: 'Đã tải lên',
  published: 'Đã đăng',
  error: 'Lỗi',
  skipped: 'Bỏ qua',
};

const statusTone: Partial<Record<RowStatus, 'neutral' | 'accent' | 'success' | 'warn' | 'danger'>> = {
  ready: 'accent',
  invalid: 'danger',
  error: 'danger',
  done: 'success',
  published: 'success',
  processing: 'warn',
  uploading: 'warn',
};

/** "Chapter 12.zip", "ch_012.5.cbz", "Chương 7 - Tên.zip", "12.zip" → 12 / 12.5 / 7 / 12. */
export function chapterNumberFromName(name: string): number | null {
  const base = name.replace(/\.(zip|cbz)$/i, '');
  const labelled = /(?:chapter|chap|chương|chuong|ch|ep|c)[\s._#-]*(\d+(?:[.,]\d+)?)/i.exec(base);
  const match = labelled ?? /(\d+(?:[.,]\d+)?)(?!.*\d)/.exec(base);
  if (!match) return null;
  const n = Number(match[1]!.replace(',', '.'));
  return Number.isFinite(n) && n >= 0 && n <= 99999.99 ? n : null;
}

/**
 * Uploads many chapters of one comic in one go: chapter numbers are read from the file names (editable),
 * missing chapters are created, existing ones get a new version, and the same promotional images are added
 * to every chapter. Optionally publishes each chapter as soon as it is processed.
 */
export function BulkUploadDialog({ comicId, chapters, limits, onClose }: { comicId: string; chapters: HouseChapterItem[]; limits: UploadLimits; onClose: () => void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const { currentHouse, can } = useAuth();
  const promos = usePromos();
  const [rows, setRows] = useState<Row[]>([]);
  const [running, setRunning] = useState(false);
  const [autoPublish, setAutoPublish] = useState(false);
  const input = useRef<HTMLInputElement>(null);
  const abort = useRef<AbortController | null>(null);
  const update = (key: string, patch: Partial<Row>) => setRows((rs) => rs.map((r) => (r.key === key ? { ...r, ...patch } : r)));

  const existingByNumber = new Map(chapters.map((c) => [c.number, c]));

  const addFiles = (files: FileList | null) => {
    if (!files) return;
    const added: Row[] = Array.from(files)
      .filter((f) => /\.(zip|cbz)$/i.test(f.name))
      .map((file) => ({
        key: crypto.randomUUID(),
        file,
        number: String(chapterNumberFromName(file.name) ?? ''),
        pages: null,
        status: file.size > limits.maxUploadSize ? 'invalid' : 'checking',
        message: file.size > limits.maxUploadSize ? `Lớn hơn ${formatBytes(limits.maxUploadSize)}` : null,
        progress: 0,
        chapterId: null,
      }));
    setRows((rs) => [...rs, ...added].sort((a, b) => Number(a.number || Infinity) - Number(b.number || Infinity)));
    // Same pre-upload check as single uploads (central directory only, nothing is sent).
    for (const row of added.filter((r) => r.status === 'checking')) {
      readZipEntries(row.file)
        .then((entries) => {
          const plan = planArchive(entries, { ...limits, maxPages: Math.max(1, limits.maxPages - promos.length) });
          const firstError = plan.issues.find((i) => i.level === 'error');
          update(row.key, { pages: plan.pages.length, status: plan.ok ? 'ready' : 'invalid', message: firstError?.message ?? null });
        })
        .catch((e: Error) => update(row.key, { status: 'invalid', message: e.message || 'Không đọc được tệp nén.' }));
    }
  };

  const numberProblem = (row: Row, all: Row[]): string | null => {
    const n = Number(row.number);
    if (row.number.trim() === '' || !Number.isFinite(n) || n < 0) return 'Nhập số chương';
    if (all.some((r) => r !== row && r.status !== 'skipped' && Number(r.number) === n)) return 'Trùng số chương';
    return null;
  };

  const queue = rows.filter((r) => r.status === 'ready' && !numberProblem(r, rows));

  const start = async () => {
    setRunning(true);
    abort.current = new AbortController();
    const uploaded: { key: string; chapterId: string }[] = [];
    try {
      for (const row of queue) {
        if (abort.current.signal.aborted) break;
        const number = Number(row.number);
        try {
          let chapterId = existingByNumber.get(number)?.id ?? null;
          if (!chapterId) {
            update(row.key, { status: 'creating' });
            chapterId = (await houseApi.createChapter(comicId, { number })).id;
          }
          update(row.key, { status: 'uploading', chapterId, progress: 0 });
          await houseApi.upload(chapterId, row.file, (f) => update(row.key, { progress: f }), abort.current.signal, promoParts(promos));
          update(row.key, { status: autoPublish ? 'processing' : 'done', progress: 1 });
          uploaded.push({ key: row.key, chapterId });
        } catch (e) {
          if ((e as Error).name === 'AbortError') {
            update(row.key, { status: 'ready', progress: 0 });
            break;
          }
          update(row.key, { status: 'error', message: e instanceof ApiError ? e.message : 'Tải lên thất bại.' });
        }
      }
      queryClient.invalidateQueries({ queryKey: ['house', currentHouse?.houseId, 'comic', comicId] });
      if (autoPublish && uploaded.length > 0) await publishWhenProcessed(uploaded);
      toast.success(`Đã tải lên ${uploaded.length} chương.`);
    } finally {
      setRunning(false);
      queryClient.invalidateQueries({ queryKey: ['house', currentHouse?.houseId, 'comic', comicId] });
    }
  };

  /** Polls the chapter list and publishes each chapter once its new version is ready (window must stay open). */
  const publishWhenProcessed = async (pending: { key: string; chapterId: string }[]) => {
    let waiting = [...pending];
    while (waiting.length > 0 && !abort.current?.signal.aborted) {
      await new Promise((r) => setTimeout(r, 3000));
      const list = await houseApi.chapters(comicId).catch(() => null);
      if (!list) continue;
      for (const item of waiting) {
        const ch = list.find((c) => c.id === item.chapterId);
        const job = ch?.latestJob;
        if (!ch || !job || job.status === 'queued' || job.status === 'running') continue;
        waiting = waiting.filter((w) => w !== item);
        if (job.status !== 'succeeded') {
          update(item.key, { status: 'error', message: job.errorMessage ?? 'Xử lý thất bại.' });
          continue;
        }
        try {
          await houseApi.publish(item.chapterId);
          update(item.key, { status: 'published' });
        } catch (e) {
          update(item.key, { status: 'done', message: e instanceof ApiError ? `Chưa đăng: ${e.message}` : 'Chưa đăng được.' });
        }
      }
    }
  };

  useEffect(() => () => abort.current?.abort(), []);

  return (
    <Modal
      open
      size="lg"
      onClose={() => !running && onClose()}
      title="Tải nhiều chương"
      footer={
        <>
          {running ? (
            <Button variant="outline" onClick={() => abort.current?.abort()}>Dừng</Button>
          ) : (
            <Button variant="outline" onClick={onClose}>Đóng</Button>
          )}
          <Button icon={<Upload className="h-4 w-4" />} loading={running} disabled={running || queue.length === 0} onClick={start} data-testid="bulk-start">
            Tải lên {queue.length} chương
          </Button>
        </>
      }
    >
      <div className="space-y-4" data-testid="bulk-upload">
        <PromoImagesPicker disabled={running} />
        <div className="flex flex-wrap items-center gap-3">
          <Button variant="outline" icon={<FilePlus2 className="h-4 w-4" />} disabled={running} onClick={() => input.current?.click()}>Chọn tệp ZIP/CBZ</Button>
          <span className="text-xs text-muted">Mỗi tệp là một chương. Số chương được lấy từ tên tệp (ví dụ “Chapter 12.zip”), bạn có thể sửa lại.</span>
          <input ref={input} type="file" multiple accept=".zip,.cbz,application/zip" className="sr-only" data-testid="bulk-input"
            onChange={(e) => { addFiles(e.target.files); e.target.value = ''; }} />
        </div>
        {can('publishChapters') && (
          <Checkbox label="Tự động đăng khi xử lý xong" description="Giữ cửa sổ này mở cho đến khi tất cả chương được đăng." checked={autoPublish} disabled={running}
            onChange={(e) => setAutoPublish(e.target.checked)} />
        )}

        {rows.length > 0 && (
          <ul className="divide-y divide-line rounded-2xl border border-line">
            {rows.map((row) => {
              const problem = row.status === 'ready' ? numberProblem(row, rows) : null;
              const existing = existingByNumber.get(Number(row.number));
              return (
                <li key={row.key} className="grid gap-2 p-3 sm:grid-cols-[1fr_7rem_9rem] sm:items-center" data-testid="bulk-row">
                  <div className="min-w-0">
                    <p className="truncate text-sm font-medium" title={row.file.name}>{row.file.name}</p>
                    <p className="text-xs text-muted">
                      {formatBytes(row.file.size)}
                      {row.pages !== null && ` · ${row.pages} trang${promos.length ? ` + ${promos.length} ảnh quảng bá` : ''}`}
                      {existing && row.status === 'ready' && ' · chương đã có → tạo phiên bản mới'}
                    </p>
                    {(row.message || problem) && <p className={cn('text-xs', row.status === 'done' ? 'text-warn' : 'text-danger')}>{problem ?? row.message}</p>}
                    {row.status === 'uploading' && <ProgressBar value={row.progress} label="Đang tải lên" />}
                  </div>
                  <Input aria-label={`Số chương của ${row.file.name}`} type="number" step="0.01" min={0} value={row.number} disabled={running || !['ready', 'invalid', 'checking'].includes(row.status)}
                    onChange={(e) => update(row.key, { number: e.target.value })} />
                  <div className="flex items-center justify-between gap-2 sm:justify-end">
                    <Badge tone={problem ? 'danger' : statusTone[row.status] ?? 'neutral'}>{problem ? 'Cần sửa' : statusLabel[row.status]}</Badge>
                    {!running && ['ready', 'invalid', 'error'].includes(row.status) && (
                      <button type="button" className="text-xs text-muted hover:text-danger" onClick={() => setRows((rs) => rs.filter((r) => r.key !== row.key))}>Bỏ</button>
                    )}
                  </div>
                </li>
              );
            })}
          </ul>
        )}
      </div>
    </Modal>
  );
}
