import { useRef, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowLeft, Eye, RotateCcw, ScanSearch, Send, Trash2, Undo2 } from 'lucide-react';
import { paths } from '@/app/paths';
import { Badge, Button, ConfirmDialog, DataTable, Dropzone, ErrorState, Field, Input, Modal, PageHeader, ProgressBar, Skeleton, useToast } from '@/components/ui';
import { useAuth } from '@/features/auth/AuthProvider';
import { houseApi } from '@/services/api';
import { useDocumentTitle } from '@/hooks/useTheme';
import { chapterLabel, formatBytes, formatDateTime, timeAgo } from '@/utils/format';
import { ChapterStatusBadge, isActiveJob, JobProgress, JobStatusBadge } from './shared';
import { PromoImagesPicker, promoParts, usePromos } from './PromoImages';
import { UploadCheckDialog } from './UploadCheckDialog';

export function HouseChapterPage() {
  const { id = '' } = useParams();
  const { currentHouse, can } = useAuth();
  const navigate = useNavigate();
  const toast = useToast();
  const queryClient = useQueryClient();
  const key = ['house', currentHouse?.houseId, 'chapter', id];
  const chapter = useQuery({
    queryKey: key,
    queryFn: () => houseApi.chapter(id),
    refetchInterval: (q) => (isActiveJob(q.state.data?.latestJob) ? 1500 : false),
  });
  const c = chapter.data;
  useDocumentTitle(c ? `${c.comicTitle} – ${chapterLabel(c.number)}` : 'Chương');

  const limits = useQuery({ queryKey: ['house', 'upload-limits'], queryFn: houseApi.uploadLimits, staleTime: Infinity });
  const [checking, setChecking] = useState<File | null>(null);
  const [uploadProgress, setUploadProgress] = useState<number | null>(null);
  const [confirm, setConfirm] = useState<'publish' | 'unpublish' | 'delete' | null>(null);
  const [editing, setEditing] = useState(false);
  const abort = useRef<AbortController | null>(null);
  const promos = usePromos();
  const refresh = () => {
    queryClient.invalidateQueries({ queryKey: key });
    queryClient.invalidateQueries({ queryKey: ['house', currentHouse?.houseId, 'comic', c?.comicId] });
  };

  const upload = async (file: File) => {
    abort.current = new AbortController();
    setUploadProgress(0);
    try {
      await houseApi.upload(id, file, setUploadProgress, abort.current.signal, promoParts(promos));
      toast.success('Đã tải lên. Hệ thống đang xử lý chương…');
      refresh();
    } catch (e) {
      if ((e as Error).name !== 'AbortError') toast.error(e);
    } finally {
      setUploadProgress(null);
    }
  };

  const action = useMutation({
    mutationFn: async (kind: 'publish' | 'unpublish' | 'retry' | 'delete') => {
      if (kind === 'publish') return houseApi.publish(id);
      if (kind === 'unpublish') return houseApi.unpublish(id);
      if (kind === 'retry') return houseApi.retry(id);
      return houseApi.deleteChapter(id);
    },
    onSuccess: (_, kind) => {
      setConfirm(null);
      if (kind === 'delete') {
        toast.success('Đã xoá chương.');
        navigate(c ? paths.studio.comic(c.comicId) : paths.studio.comics);
        return;
      }
      toast.success({ publish: 'Đã đăng chương!', unpublish: 'Đã gỡ chương khỏi trang đọc.', retry: 'Đã đưa vào hàng đợi xử lý lại.' }[kind]);
      refresh();
    },
    onError: (e) => {
      setConfirm(null);
      toast.error(e);
    },
  });

  if (chapter.isError) return <ErrorState error={chapter.error} onRetry={() => chapter.refetch()} />;
  if (!c) return <Skeleton className="h-96 rounded-2xl" />;

  const job = c.latestJob;
  const active = isActiveJob(job);

  return (
    <>
      <Link to={paths.studio.comic(c.comicId)} className="mb-3 inline-flex items-center gap-1 text-sm text-muted hover:text-ink">
        <ArrowLeft className="h-4 w-4" /> {c.comicTitle}
      </Link>
      <PageHeader
        title={chapterLabel(c.number, c.title)}
        description={<span className="flex flex-wrap items-center gap-2"><ChapterStatusBadge status={c.status} />{c.publishedVersion && <span>Đang đăng phiên bản v{c.publishedVersion}</span>}{c.pageCount > 0 && <span>· {c.pageCount} trang</span>}</span>}
        actions={
          <>
            {c.status === 'published' && <Button variant="outline" icon={<Eye className="h-4 w-4" />} onClick={() => window.open(paths.read(c.id), '_blank')}>Xem</Button>}
            {c.readyVersion && (
              <Button variant="outline" icon={<ScanSearch className="h-4 w-4" />} onClick={() => navigate(paths.studio.preview(c.id, c.readyVersion!))} data-testid="preview">
                Xem trước v{c.readyVersion}
              </Button>
            )}
            {can('publishChapters') && c.actions.canPublish && (
              <Button icon={<Send className="h-4 w-4" />} onClick={() => setConfirm('publish')} data-testid="publish">
                {c.status === 'published' ? `Đăng bản v${c.readyVersion}` : 'Đăng chương'}
              </Button>
            )}
            {can('publishChapters') && c.actions.canUnpublish && <Button variant="outline" icon={<Undo2 className="h-4 w-4" />} onClick={() => setConfirm('unpublish')} data-testid="unpublish">Gỡ chương</Button>}
            {can('manageChapters') && <Button variant="ghost" onClick={() => setEditing(true)}>Sửa</Button>}
            {can('manageChapters') && c.actions.canDelete && <Button variant="ghost" className="text-danger" icon={<Trash2 className="h-4 w-4" />} onClick={() => setConfirm('delete')}>Xoá</Button>}
          </>
        }
      />

      <div className="grid gap-6 lg:grid-cols-2">
        <section className="card space-y-4 p-5">
          <h2 className="font-semibold">Tải chương lên</h2>
          {can('uploadChapters') ? (
            uploadProgress !== null ? (
              <div className="space-y-3" data-testid="upload-progress">
                <ProgressBar value={uploadProgress} label="Đang tải tệp lên máy chủ" />
                <Button variant="outline" size="sm" onClick={() => abort.current?.abort()}>Huỷ tải lên</Button>
              </div>
            ) : (
              <>
              <PromoImagesPicker disabled={!c.actions.canUpload} />
              <Dropzone accept=".zip,.cbz,application/zip" maxBytes={limits.data?.maxUploadSize ?? 200 * 1024 * 1024} disabled={!c.actions.canUpload || !limits.data} onFile={setChecking}
                title={c.actions.canUpload ? 'Kéo thả hoặc bấm để chọn tệp ZIP/CBZ' : 'Đang xử lý bản tải lên trước…'}
                hint={c.status === 'published' ? 'Tải lại sẽ tạo phiên bản mới; bản đang đăng vẫn hiển thị đến khi bạn đăng bản mới.' : 'Ảnh JPEG/PNG/WebP, tối đa 200 MB.'} />
              </>
            )
          ) : (
            <p className="text-sm text-muted">Bạn chưa được giao quyền tải chương lên.</p>
          )}
        </section>

        <section className="card space-y-4 p-5" data-testid="processing-status">
          <div className="flex items-center justify-between">
            <h2 className="font-semibold">Trạng thái xử lý</h2>
            {job && <JobStatusBadge status={job.status} />}
          </div>
          {!job ? (
            <p className="text-sm text-muted">Chưa có tệp nào được tải lên.</p>
          ) : (
            <>
              <JobProgress job={job} />
              <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1 text-sm">
                <dt className="text-muted">Tệp</dt><dd className="truncate">{job.originalFileName ?? '—'} ({formatBytes(job.uploadSize)})</dd>
                <dt className="text-muted">Bắt đầu</dt><dd>{formatDateTime(job.startedAt ?? job.createdAt)}</dd>
                {job.finishedAt && (<><dt className="text-muted">Kết thúc</dt><dd>{timeAgo(job.finishedAt)}</dd></>)}
                <dt className="text-muted">Lần thử</dt><dd>{job.attempts}</dd>
              </dl>
              {job.status === 'failed' && (
                <div className="rounded-xl bg-danger-soft p-3 text-sm text-danger" role="alert" data-testid="job-error">
                  {job.errorMessage}
                  {can('uploadChapters') && c.actions.canRetry && (
                    <div className="mt-2">
                      <Button size="sm" variant="outline" loading={action.isPending} icon={<RotateCcw className="h-4 w-4" />} onClick={() => action.mutate('retry')}>Thử lại</Button>
                    </div>
                  )}
                </div>
              )}
              {job.status === 'succeeded' && c.actions.canPublish && c.readyVersion && (
                <div className="flex flex-wrap items-center gap-3 rounded-xl bg-success-soft p-3 text-sm text-success">
                  <span>Đã xử lý xong — hãy xem trước để kiểm tra thứ tự và nội dung trang trước khi đăng.</span>
                  <Button size="sm" variant="outline" icon={<ScanSearch className="h-4 w-4" />} onClick={() => navigate(paths.studio.preview(c.id, c.readyVersion!))}>Xem trước</Button>
                </div>
              )}
              {active && <p className="text-xs text-muted">Trang tự cập nhật trong lúc xử lý.</p>}
            </>
          )}
        </section>
      </div>

      <h2 className="mb-3 mt-8 font-semibold">Phiên bản</h2>
      {c.versions.length === 0 ? (
        <p className="text-sm text-muted">Chưa có phiên bản nào.</p>
      ) : (
        <DataTable
          rows={c.versions}
          rowKey={(v) => v.id}
          columns={[
            { key: 'v', header: 'Phiên bản', cell: (v) => <span className="font-medium">v{v.version}</span> },
            { key: 's', header: 'Trạng thái', cell: (v) => <Badge tone={v.status === 'published' ? 'success' : v.status === 'ready' ? 'accent' : v.status === 'failed' ? 'danger' : 'neutral'}>{versionLabel[v.status] ?? v.status}</Badge> },
            { key: 'p', header: 'Trang', cell: (v) => v.pageCount || '—' },
            { key: 'size', header: 'Dung lượng', cell: (v) => formatBytes(v.fileSize), hideOnMobile: true },
            { key: 'sha', header: 'SHA-256', cell: (v) => <code className="text-xs text-muted">{v.checksum?.slice(0, 12) ?? '—'}</code>, hideOnMobile: true },
            { key: 'at', header: 'Tạo lúc', cell: (v) => <span className="text-xs text-muted">{formatDateTime(v.createdAt)}</span>, hideOnMobile: true },
            {
              key: 'preview',
              header: '',
              cell: (v) => (v.status === 'ready' || v.status === 'published') && (
                <Link to={paths.studio.preview(c.id, v.version)} className="inline-flex items-center gap-1 text-sm text-accent hover:underline">
                  <ScanSearch className="h-4 w-4" /> Xem trước
                </Link>
              ),
            },
          ]}
        />
      )}

      {checking && limits.data && (
        <UploadCheckDialog
          file={checking}
          limits={limits.data}
          promos={promos}
          onCancel={() => setChecking(null)}
          onConfirm={() => {
            const file = checking;
            setChecking(null);
            void upload(file);
          }}
        />
      )}
      {editing && <EditChapterDialog id={id} number={c.number} title={c.title} onClose={() => { setEditing(false); refresh(); }} />}
      <ConfirmDialog
        open={confirm !== null}
        danger={confirm === 'delete' || confirm === 'unpublish'}
        loading={action.isPending}
        title={{ publish: 'Đăng chương', unpublish: 'Gỡ chương', delete: 'Xoá chương' }[confirm ?? 'publish']}
        confirmLabel={{ publish: 'Đăng ngay', unpublish: 'Gỡ chương', delete: 'Xoá vĩnh viễn' }[confirm ?? 'publish']}
        message={{
          publish: `Bản v${c.readyVersion} sẽ được đưa lên kho lưu trữ và hiển thị cho mọi người đọc. Nếu chưa kiểm tra, hãy bấm “Xem trước” trước khi đăng.`,
          unpublish: 'Người đọc sẽ không xem được chương này nữa. Bạn có thể đăng lại bất cứ lúc nào.',
          delete: 'Chương và các phiên bản sẽ bị xoá vĩnh viễn.',
        }[confirm ?? 'publish']}
        onClose={() => setConfirm(null)}
        onConfirm={() => confirm && action.mutate(confirm)}
      />
    </>
  );
}

const versionLabel: Record<string, string> = { building: 'Đang dựng', ready: 'Sẵn sàng', published: 'Đang đăng', failed: 'Lỗi', superseded: 'Đã thay thế' };

function EditChapterDialog({ id, number, title, onClose }: { id: string; number: number; title: string | null; onClose: () => void }) {
  const toast = useToast();
  const [n, setN] = useState(String(number));
  const [t, setT] = useState(title ?? '');
  const save = useMutation({
    mutationFn: () => houseApi.updateChapter(id, { number: Number(n), title: t || undefined }),
    onSuccess: () => { toast.success('Đã lưu chương.'); onClose(); },
    onError: (e) => toast.error(e),
  });
  return (
    <Modal open onClose={onClose} title="Sửa chương" size="sm"
      footer={<><Button variant="outline" onClick={onClose}>Huỷ</Button><Button loading={save.isPending} onClick={() => save.mutate()}>Lưu</Button></>}>
      <div className="space-y-4">
        <Field label="Số chương">{(fid) => <Input id={fid} type="number" step="0.01" min={0} value={n} onChange={(e) => setN(e.target.value)} />}</Field>
        <Field label="Tên chương">{(fid) => <Input id={fid} value={t} onChange={(e) => setT(e.target.value)} />}</Field>
      </div>
    </Modal>
  );
}
