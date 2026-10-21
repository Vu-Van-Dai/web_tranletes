import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowLeft, ChevronLeft, ChevronRight, Columns2, Rows3, Sun, Moon, Keyboard } from 'lucide-react';
import { paths } from '@/app/paths';
import { Button, ButtonLink, ErrorState, Modal, ProgressBar, Spinner } from '@/components/ui';
import { libraryApi, publicApi } from '@/services/api';
import { useAuth } from '@/features/auth/AuthProvider';
import { useDocumentTitle } from '@/hooks/useTheme';
import { chapterLabel, cn, formatBytes } from '@/utils/format';
import type { ReaderData } from '@/types/api';
import { loadPackage, PackageError, type LoadedPackage } from './package';
import { PageWindow } from './pageWindow';
import { useVerifiedView } from './viewTracking';
import { AdSlot, AdsRuntime } from '@/features/ads/Ads';

type Mode = 'vertical' | 'paged';

function readPref<T extends string>(key: string, fallback: T, allowed: readonly T[]): T {
  try {
    const v = localStorage.getItem(key) as T | null;
    return v && allowed.includes(v) ? v : fallback;
  } catch {
    return fallback;
  }
}

function writePref(key: string, value: string) {
  try {
    localStorage.setItem(key, value);
  } catch {
    // ignore (private mode)
  }
}

export function ReaderPage() {
  const { chapterId = '' } = useParams();
  const reader = useQuery({ queryKey: ['reader', chapterId], queryFn: ({ signal }) => publicApi.reader(chapterId, signal), staleTime: 30_000, retry: 1 });
  const data = reader.data;
  useDocumentTitle(data ? `${data.chapter.comicTitle} – ${chapterLabel(data.chapter.number)}` : 'Đọc truyện');

  if (reader.isError) {
    return (
      <div data-theme="dark" className="min-h-dvh bg-bg p-6 text-ink">
        <ErrorState error={reader.error} onRetry={() => reader.refetch()} title="Không mở được chương" />
      </div>
    );
  }

  if (!data) {
    return (
      <div data-theme="dark" className="flex min-h-dvh items-center justify-center bg-bg text-ink">
        <Spinner label="Đang tải thông tin chương…" />
      </div>
    );
  }

  return <MatureGate data={data}>{() => <Reader key={`${chapterId}-${data.package.version}`} data={data} onStale={() => reader.refetch()} />}</MatureGate>;
}

function MatureGate({ data, children }: { data: ReaderData; children: () => React.ReactNode }) {
  const navigate = useNavigate();
  const [ok, setOk] = useState(() => !data.chapter.isMature || readPref('mature-ok', 'no', ['yes', 'no']) === 'yes');
  if (ok) return <>{children()}</>;
  return (
    <div data-theme="dark" className="min-h-dvh bg-bg text-ink">
      <Modal open title="Nội dung dành cho người trên 18 tuổi" onClose={() => navigate(paths.comic(data.chapter.comicId, data.chapter.comicSlug))}
        footer={
          <>
            <Button variant="outline" onClick={() => navigate(paths.home)}>Quay lại</Button>
            <Button onClick={() => { writePref('mature-ok', 'yes'); setOk(true); }}>Tôi đủ 18 tuổi, tiếp tục</Button>
          </>
        }>
        <p className="text-sm text-muted">Truyện này được nhóm dịch đánh dấu có nội dung nhạy cảm. Bạn xác nhận mình đã đủ 18 tuổi để tiếp tục đọc?</p>
      </Modal>
    </div>
  );
}

function Reader({ data, onStale }: { data: ReaderData; onStale: () => void }) {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { status } = useAuth();
  const { chapter, pages } = data;

  const [mode, setMode] = useState<Mode>(() => readPref<Mode>('reader-mode', 'vertical', ['vertical', 'paged']));
  const [readerTheme, setReaderTheme] = useState(() => readPref('reader-theme', 'dark', ['dark', 'light']));
  const [pkg, setPkg] = useState<LoadedPackage | null>(null);
  const [progress, setProgress] = useState(0);
  const [error, setError] = useState<unknown>(null);
  const [current, setCurrent] = useState(0);
  const [urls, setUrls] = useState<Map<number, string>>(new Map());
  const [barsVisible, setBarsVisible] = useState(true);
  const [showKeys, setShowKeys] = useState(false);
  const windowRef = useRef<PageWindow | null>(null);
  const retriedStale = useRef(false);
  const pageRefs = useRef<(HTMLDivElement | null)[]>([]);

  // ---- Download + verify the package (one request for the whole chapter) ----
  useEffect(() => {
    const controller = new AbortController();
    setPkg(null);
    setError(null);
    setProgress(0);
    loadPackage(data.package.url, { size: data.package.size, sha256: data.package.sha256 }, (loaded, total) => setProgress(loaded / total), controller.signal)
      .then((loaded) => {
        windowRef.current?.dispose();
        windowRef.current = new PageWindow(loaded.parsed.entries.length, loaded.pageBlob);
        setPkg(loaded);
      })
      .catch((e: unknown) => {
        if ((e as Error).name === 'AbortError') return;
        // A newer version replaced this package: refresh the metadata once.
        if (e instanceof PackageError && e.message === 'not_found' && !retriedStale.current) {
          retriedStale.current = true;
          onStale();
          return;
        }
        setError(e);
      });
    return () => controller.abort();
  }, [data.package.url, data.package.size, data.package.sha256, onStale]);

  useEffect(() => () => windowRef.current?.dispose(), []);

  // ---- Keep object URLs only around the reading position ----
  useEffect(() => {
    if (pkg && windowRef.current) setUrls(windowRef.current.update(current));
  }, [pkg, current]);

  // ---- Vertical mode: track the most visible page ----
  useEffect(() => {
    if (mode !== 'vertical' || !pkg) return;
    const ratios = new Map<number, number>();
    const observer = new IntersectionObserver(
      (entries) => {
        for (const entry of entries) ratios.set(Number((entry.target as HTMLElement).dataset.index), entry.intersectionRatio);
        let best = -1;
        let bestRatio = 0;
        for (const [index, ratio] of ratios) {
          if (ratio > bestRatio) {
            best = index;
            bestRatio = ratio;
          }
        }
        if (best >= 0) setCurrent(best);
      },
      { threshold: [0, 0.25, 0.5, 0.75, 1] },
    );
    pageRefs.current.forEach((el) => el && observer.observe(el));
    return () => observer.disconnect();
  }, [mode, pkg]);

  // ---- Auto-hide bars while scrolling down ----
  useEffect(() => {
    if (mode !== 'vertical') return;
    let last = window.scrollY;
    const onScroll = () => {
      const y = window.scrollY;
      if (Math.abs(y - last) > 40) {
        setBarsVisible(y < last || y < 80);
        last = y;
      }
    };
    window.addEventListener('scroll', onScroll, { passive: true });
    return () => window.removeEventListener('scroll', onScroll);
  }, [mode]);

  // ---- Reading statistics, progress and next-chapter prefetch ----
  useVerifiedView(chapter.id, pkg !== null, current);

  useEffect(() => {
    if (status !== 'authenticated') return;
    const id = setTimeout(() => libraryApi.saveProgress(chapter.id, current + 1).catch(() => undefined), 1500);
    return () => clearTimeout(id);
  }, [status, chapter.id, current]);

  const prefetched = useRef(false);
  useEffect(() => {
    const next = chapter.next;
    const saveData = (navigator as Navigator & { connection?: { saveData?: boolean } }).connection?.saveData;
    if (!next || prefetched.current || saveData || current < Math.floor(pages.length * 0.7)) return;
    prefetched.current = true;
    queryClient
      .fetchQuery({ queryKey: ['reader', next.id], queryFn: () => publicApi.reader(next.id), staleTime: 30_000 })
      .then((nextData) => fetch(nextData.package.url, { credentials: 'omit', priority: 'low' } as RequestInit).then((r) => r.arrayBuffer()))
      .catch(() => undefined);
  }, [current, chapter.next, pages.length, queryClient]);

  // ---- Navigation ----
  const goChapter = useCallback((id: string | undefined) => id && navigate(paths.read(id)), [navigate]);
  const goPage = useCallback(
    (index: number) => {
      const clamped = Math.max(0, Math.min(pages.length - 1, index));
      setCurrent(clamped);
      if (mode === 'vertical') pageRefs.current[clamped]?.scrollIntoView({ block: 'start' });
      else window.scrollTo({ top: 0 });
    },
    [mode, pages.length],
  );

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if ((e.target as HTMLElement).closest('input, select, textarea')) return;
      if (e.key === ']') goChapter(chapter.next?.id);
      else if (e.key === '[') goChapter(chapter.prev?.id);
      else if (e.key === 'Escape') navigate(paths.comic(chapter.comicId, chapter.comicSlug));
      else if (mode === 'paged' && (e.key === 'ArrowRight' || e.key === 'd')) {
        if (current >= pages.length - 1) goChapter(chapter.next?.id);
        else goPage(current + 1);
      } else if (mode === 'paged' && (e.key === 'ArrowLeft' || e.key === 'a')) goPage(current - 1);
      else if (e.key === '?') setShowKeys(true);
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [mode, current, pages.length, chapter, goChapter, goPage, navigate]);

  const toggleMode = () => {
    const next: Mode = mode === 'vertical' ? 'paged' : 'vertical';
    writePref('reader-mode', next);
    setMode(next);
  };

  const toggleTheme = () => {
    const next = readerTheme === 'dark' ? 'light' : 'dark';
    writePref('reader-theme', next);
    setReaderTheme(next);
  };

  const chapterList = useQuery({ queryKey: ['chapters', chapter.comicId], queryFn: () => publicApi.chapters(chapter.comicId), staleTime: 60_000 });
  const pageNumbers = useMemo(() => pages.map((p) => p.n), [pages]);

  return (
    <div data-theme={readerTheme} className="min-h-dvh bg-bg text-ink" data-testid="reader-root" data-mode={mode}>
      {/* Top bar */}
      <header className={cn('fixed inset-x-0 top-0 z-30 border-b border-line bg-surface/95 backdrop-blur transition-transform', !barsVisible && '-translate-y-full')}>
        <div className="mx-auto flex h-14 max-w-5xl items-center gap-2 px-3">
          <ButtonLink to={paths.comic(chapter.comicId, chapter.comicSlug)} variant="ghost" size="icon" aria-label="Về trang truyện">
            <ArrowLeft className="h-5 w-5" />
          </ButtonLink>
          <div className="min-w-0 flex-1">
            <Link to={paths.comic(chapter.comicId, chapter.comicSlug)} className="block truncate text-sm font-semibold hover:underline">
              {chapter.comicTitle}
            </Link>
            <p className="truncate text-xs text-muted">{chapterLabel(chapter.number, chapter.title)} · {chapter.house.name}</p>
          </div>
          <select
            aria-label="Chọn chương"
            data-testid="chapter-select"
            className="hidden h-9 max-w-44 rounded-lg border border-line bg-surface px-2 text-sm sm:block"
            value={chapter.id}
            onChange={(e) => goChapter(e.target.value)}
          >
            {(chapterList.data ?? [{ id: chapter.id, number: chapter.number, title: chapter.title, publishedAt: null, pageCount: 0 }]).map((c) => (
              <option key={c.id} value={c.id}>
                {chapterLabel(c.number)}
              </option>
            ))}
          </select>
          <Button variant="ghost" size="icon" onClick={toggleMode} aria-label={mode === 'vertical' ? 'Chuyển sang đọc từng trang' : 'Chuyển sang cuộn dọc'} data-testid="mode-toggle">
            {mode === 'vertical' ? <Columns2 className="h-5 w-5" /> : <Rows3 className="h-5 w-5" />}
          </Button>
          <Button variant="ghost" size="icon" onClick={toggleTheme} aria-label="Đổi nền đọc">
            {readerTheme === 'dark' ? <Sun className="h-5 w-5" /> : <Moon className="h-5 w-5" />}
          </Button>
          <Button variant="ghost" size="icon" className="hidden sm:inline-flex" onClick={() => setShowKeys(true)} aria-label="Phím tắt">
            <Keyboard className="h-5 w-5" />
          </Button>
        </div>
      </header>

      {/* Content */}
      <main className="pb-24 pt-14">
        <AdSlot slot="reader_top" className="mx-auto my-3 max-w-[900px]" />
        {error ? (
          <div className="mx-auto max-w-xl p-6">
            <ErrorState error={error instanceof PackageError ? new Error(error.message) : error} title="Không tải được chương" onRetry={() => onStale()} />
            {error instanceof PackageError && <p className="mt-2 text-center text-sm text-muted">{error.message}</p>}
          </div>
        ) : !pkg ? (
          <div className="mx-auto flex max-w-md flex-col items-center gap-3 px-6 py-24" data-testid="package-progress">
            <Spinner label="Đang tải chương…" />
            <ProgressBar value={progress} label={`${formatBytes(Math.round(progress * data.package.size))} / ${formatBytes(data.package.size)}`} />
          </div>
        ) : mode === 'vertical' ? (
          <div className="mx-auto max-w-[900px]" onClick={() => setBarsVisible((v) => !v)}>
            {pages.map((page, index) => {
              const url = urls.get(index);
              return (
                <div
                  key={page.n}
                  ref={(el) => {
                    pageRefs.current[index] = el;
                  }}
                  data-index={index}
                  className="relative w-full bg-surface-2"
                  style={{ aspectRatio: `${page.w} / ${page.h}` }}
                >
                  {url && (
                    <img src={url} alt={`Trang ${page.n}`} width={page.w} height={page.h} decoding="async" draggable={false}
                      data-testid="reader-page" data-page={page.n} className="absolute inset-0 block h-full w-full select-none" />
                  )}
                </div>
              );
            })}
          </div>
        ) : (
          <PagedView pages={pages} current={current} url={urls.get(current)} onPrev={() => goPage(current - 1)}
            onNext={() => (current >= pages.length - 1 ? goChapter(chapter.next?.id) : goPage(current + 1))}
            onToggleBars={() => setBarsVisible((v) => !v)} />
        )}

        {pkg && <AdSlot slot="reader_bottom" className="mx-auto my-4 max-w-[900px]" />}
        {pkg && (
          <EndOfChapter data={data} onPrev={() => goChapter(chapter.prev?.id)} onNext={() => goChapter(chapter.next?.id)} />
        )}
      </main>
      <AdsRuntime />

      {/* Bottom bar */}
      <footer className={cn('fixed inset-x-0 bottom-0 z-30 border-t border-line bg-surface/95 backdrop-blur transition-transform', !barsVisible && 'translate-y-full')}>
        <div className="mx-auto flex h-14 max-w-5xl items-center gap-2 px-3">
          <Button variant="outline" size="sm" disabled={!chapter.prev} onClick={() => goChapter(chapter.prev?.id)} data-testid="prev-chapter" icon={<ChevronLeft className="h-4 w-4" />}>
            <span className="hidden sm:inline">Chương trước</span>
          </Button>
          <div className="flex-1 text-center text-xs text-muted" aria-live="polite" data-testid="page-indicator">
            Trang {pageNumbers[current] ?? 1} / {pages.length}
          </div>
          <Button variant="outline" size="sm" disabled={!chapter.next} onClick={() => goChapter(chapter.next?.id)} data-testid="next-chapter">
            <span className="hidden sm:inline">Chương sau</span>
            <ChevronRight className="h-4 w-4" />
          </Button>
        </div>
      </footer>

      <Modal open={showKeys} onClose={() => setShowKeys(false)} title="Phím tắt" size="sm">
        <ul className="space-y-2 text-sm">
          <li><kbd className="rounded bg-surface-2 px-1.5">←</kbd> / <kbd className="rounded bg-surface-2 px-1.5">→</kbd> Trang trước / sau (chế độ từng trang)</li>
          <li><kbd className="rounded bg-surface-2 px-1.5">[</kbd> / <kbd className="rounded bg-surface-2 px-1.5">]</kbd> Chương trước / sau</li>
          <li><kbd className="rounded bg-surface-2 px-1.5">Esc</kbd> Về trang truyện</li>
        </ul>
      </Modal>
    </div>
  );
}

function PagedView({ pages, current, url, onPrev, onNext, onToggleBars }: {
  pages: ReaderData['pages']; current: number; url: string | undefined; onPrev: () => void; onNext: () => void; onToggleBars: () => void;
}) {
  const page = pages[current];
  if (!page) return null;
  return (
    <div className="relative mx-auto flex min-h-[calc(100dvh-7rem)] max-w-5xl items-center justify-center px-2">
      {url ? (
        <img src={url} alt={`Trang ${page.n}`} width={page.w} height={page.h} decoding="async" draggable={false}
          data-testid="reader-page" data-page={page.n} className="max-h-[calc(100dvh-7rem)] w-auto max-w-full select-none object-contain" />
      ) : (
        <div className="w-full max-w-[700px] bg-surface-2" style={{ aspectRatio: `${page.w} / ${page.h}` }} />
      )}
      {/* Tap zones: left 30% back, right 30% forward, middle toggles the bars */}
      <button className="absolute inset-y-0 left-0 w-[30%]" aria-label="Trang trước" onClick={onPrev} />
      <button className="absolute inset-y-0 left-[30%] w-[40%]" aria-label="Hiện/ẩn thanh công cụ" onClick={onToggleBars} />
      <button className="absolute inset-y-0 right-0 w-[30%]" aria-label="Trang sau" onClick={onNext} />
    </div>
  );
}

function EndOfChapter({ data, onPrev, onNext }: { data: ReaderData; onPrev: () => void; onNext: () => void }) {
  const { chapter } = data;
  return (
    <div className="mx-auto mt-8 max-w-xl px-4 text-center">
      <p className="text-sm text-muted">Hết {chapterLabel(chapter.number)}</p>
      <div className="mt-4 flex justify-center gap-3">
        <Button variant="outline" disabled={!chapter.prev} onClick={onPrev} icon={<ChevronLeft className="h-4 w-4" />}>Chương trước</Button>
        <Button disabled={!chapter.next} onClick={onNext}>
          Chương sau <ChevronRight className="h-4 w-4" />
        </Button>
      </div>
      <p className="mt-4 text-xs text-muted">Bản dịch của nhóm <Link className="link" to={paths.house(chapter.house.slug)}>{chapter.house.name}</Link></p>
    </div>
  );
}
