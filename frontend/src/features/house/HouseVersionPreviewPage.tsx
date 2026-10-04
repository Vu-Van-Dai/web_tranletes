import { useEffect, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowLeft, Columns3, Rows3, Send, ShieldCheck } from 'lucide-react';
import { paths } from '@/app/paths';
import { Badge, Button, ConfirmDialog, ErrorState, PageHeader, ProgressBar, Skeleton, useToast } from '@/components/ui';
import { useAuth } from '@/features/auth/AuthProvider';
import { loadPackage } from '@/features/reader/package';
import { houseApi } from '@/services/api';
import { authHeaders } from '@/services/http';
import { useDocumentTitle } from '@/hooks/useTheme';
import { chapterLabel, cn, formatBytes } from '@/utils/format';

/**
 * Pre-publish preview: downloads the private package of a processed version (exactly what readers will get),
 * verifies its SHA-256 and shows every page with the archive file it came from.
 */
export function HouseVersionPreviewPage() {
  const { id = '', version: versionParam = '' } = useParams();
  const version = Number(versionParam);
  const { currentHouse, can } = useAuth();
  const toast = useToast();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const preview = useQuery({ queryKey: ['house', currentHouse?.houseId, 'preview', id, version], queryFn: () => houseApi.preview(id, version), staleTime: 0 });
  const p = preview.data;
  useDocumentTitle(p ? `Xem trước ${chapterLabel(p.number)} v${p.version}` : 'Xem trước');

  const [urls, setUrls] = useState<string[] | null>(null);
  const [progress, setProgress] = useState(0);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [mode, setMode] = useState<'grid' | 'strip'>('grid');
  const [confirm, setConfirm] = useState(false);

  useEffect(() => {
    if (!p) return;
    const controller = new AbortController();
    let created: string[] = [];
    setUrls(null);
    setLoadError(null);
    loadPackage(houseApi.previewPackageUrl(id, p.version), { size: p.fileSize, sha256: p.sha256 }, (done, total) => setProgress(done / total), controller.signal, authHeaders(true))
      .then((pkg) => {
        // Zero-copy slices of one Blob; a preview shows every page at once (no reader windowing needed).
        created = pkg.parsed.entries.map((_, i) => URL.createObjectURL(pkg.pageBlob(i)));
        setUrls(created);
      })
      .catch((e: Error) => {
        if (e.name !== 'AbortError') setLoadError(e.message === 'not_found' ? 'Không tìm thấy gói chương của phiên bản này.' : e.message);
      });
    return () => {
      controller.abort();
      created.forEach((u) => URL.revokeObjectURL(u));
    };
  }, [id, p]);

  const publish = useMutation({
    mutationFn: () => houseApi.publish(id),
    onSuccess: () => {
      toast.success('Đã đăng chương!');
      queryClient.invalidateQueries({ queryKey: ['house', currentHouse?.houseId] });
      navigate(paths.studio.chapter(id));
    },
    onError: (e) => {
      setConfirm(false);
      toast.error(e);
    },
  });

  if (preview.isError) return <ErrorState error={preview.error} onRetry={() => preview.refetch()} />;
  if (!p) return <Skeleton className="h-96 rounded-2xl" />;

  const openPage = (n: number) => {
    setMode('strip');
    requestAnimationFrame(() => document.getElementById(`trang-${n}`)?.scrollIntoView({ block: 'start' }));
  };

  return (
    <>
      <Link to={paths.studio.chapter(id)} className="mb-3 inline-flex items-center gap-1 text-sm text-muted hover:text-ink">
        <ArrowLeft className="h-4 w-4" /> {p.comicTitle} · {chapterLabel(p.number, p.title)}
      </Link>
      <PageHeader
        title={`Xem trước bản v${p.version}`}
        description={
          <span className="flex flex-wrap items-center gap-2">
            <Badge tone={p.isPublished ? 'success' : 'accent'}>{p.isPublished ? 'Đang đăng' : 'Chưa đăng'}</Badge>
            <span>{p.pageCount} trang · {formatBytes(p.fileSize)}</span>
          </span>
        }
        actions={
          <>
            <div className="flex rounded-xl bg-surface-2 p-1" role="group" aria-label="Kiểu xem">
              <ModeButton active={mode === 'grid'} onClick={() => setMode('grid')} icon={<Columns3 className="h-4 w-4" />}>Lưới</ModeButton>
              <ModeButton active={mode === 'strip'} onClick={() => setMode('strip')} icon={<Rows3 className="h-4 w-4" />}>Đọc thử</ModeButton>
            </div>
            {can('publishChapters') && p.canPublish && (
              <Button icon={<Send className="h-4 w-4" />} onClick={() => setConfirm(true)} disabled={!urls} data-testid="preview-publish">Đăng bản này</Button>
            )}
          </>
        }
      />

      <p className="mb-5 flex items-start gap-2 rounded-xl bg-surface-2 px-3 py-2 text-sm text-muted">
        <ShieldCheck className="mt-0.5 h-4 w-4 shrink-0 text-success" aria-hidden />
        Đây chính là gói chương người đọc sẽ nhận (đã chuyển sang WebP, đã kiểm tra mã SHA-256). Hãy kiểm tra thứ tự và nội dung từng trang trước khi đăng.
      </p>

      {loadError ? (
        <ErrorState error={new Error(loadError)} onRetry={() => preview.refetch()} />
      ) : !urls ? (
        <div className="card p-5"><ProgressBar value={progress} label="Đang tải gói chương để xem trước" /></div>
      ) : mode === 'grid' ? (
        <ol className="grid grid-cols-2 gap-4 sm:grid-cols-4 lg:grid-cols-6" data-testid="preview-grid">
          {p.pages.map((page, i) => (
            <li key={page.n} className="min-w-0">
              <button type="button" onClick={() => openPage(page.n)} className="block w-full overflow-hidden rounded-lg border border-line bg-surface-2 hover:border-accent" aria-label={`Xem trang ${page.n}`}>
                <img src={urls[i]} alt={`Trang ${page.n}`} width={page.w} height={page.h} loading="lazy" decoding="async" className="aspect-[3/4] h-auto w-full object-contain" />
              </button>
              <p className="mt-1 text-xs font-semibold">Trang {page.n}</p>
              <p className="truncate text-xs text-muted" title={page.sourceName ?? undefined}>{page.sourceName ? `từ ${page.sourceName}` : '—'}</p>
              <p className="text-[11px] text-muted">{page.w}×{page.h} · {formatBytes(page.size)}</p>
            </li>
          ))}
        </ol>
      ) : (
        <div className="mx-auto max-w-3xl space-y-4" data-testid="preview-strip">
          {p.pages.map((page, i) => (
            <figure key={page.n} id={`trang-${page.n}`} className="scroll-mt-20">
              <img src={urls[i]} alt={`Trang ${page.n}`} width={page.w} height={page.h} loading="lazy" decoding="async" className="h-auto w-full rounded-lg bg-surface-2" />
              <figcaption className="mt-1 text-center text-xs text-muted">Trang {page.n}{page.sourceName ? ` · từ ${page.sourceName}` : ''}</figcaption>
            </figure>
          ))}
        </div>
      )}

      <ConfirmDialog
        open={confirm}
        title="Đăng chương"
        confirmLabel="Đăng ngay"
        loading={publish.isPending}
        message={`Bản v${p.version} (${p.pageCount} trang) sẽ hiển thị cho mọi người đọc.`}
        onClose={() => setConfirm(false)}
        onConfirm={() => publish.mutate()}
      />
    </>
  );
}

function ModeButton({ active, onClick, icon, children }: { active: boolean; onClick: () => void; icon: React.ReactNode; children: React.ReactNode }) {
  return (
    <button type="button" onClick={onClick} aria-pressed={active}
      className={cn('inline-flex items-center gap-1.5 rounded-lg px-3 py-1.5 text-sm', active ? 'bg-surface font-medium shadow-sm' : 'text-muted hover:text-ink')}>
      {icon}
      {children}
    </button>
  );
}
