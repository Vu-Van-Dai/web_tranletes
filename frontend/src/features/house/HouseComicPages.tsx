import { useState } from 'react';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { ExternalLink, Plus, Search, Trash2 } from 'lucide-react';
import { paths } from '@/app/paths';
import {
  Badge, Button, Checkbox, ConfirmDialog, DataTable, EmptyState, ErrorState, Field, Input, Modal, PageHeader, Pagination, Select, Skeleton, Textarea, useToast,
} from '@/components/ui';
import { useAuth } from '@/features/auth/AuthProvider';
import { Cover } from '@/features/catalog/ComicCard';
import { houseApi, type SaveComicBody } from '@/services/api';
import { useDebounced, useDocumentTitle } from '@/hooks/useTheme';
import { comicStatusLabel } from '@/text/vi';
import type { HouseComicDetail } from '@/types/api';
import { applyServerErrors } from '@/utils/forms';
import { chapterLabel, formatDate, timeAgo } from '@/utils/format';
import { ChapterStatusBadge, isActiveJob, JobProgress } from './shared';

const comicSchema = z.object({
  translatedTitle: z.string().trim().min(1, 'Vui lòng nhập tên truyện.').max(300),
  originalTitle: z.string().trim().min(1, 'Vui lòng nhập tên gốc.').max(300),
  slug: z.string().trim().regex(/^([a-z0-9]+(?:-[a-z0-9]+)*)?$/, 'Chỉ gồm chữ thường không dấu, số và dấu gạch ngang.'),
  author: z.string().max(200),
  artist: z.string().max(200),
  description: z.string().max(5000, 'Tối đa 5000 ký tự.'),
  status: z.enum(['ongoing', 'completed', 'hiatus', 'dropped']),
  tags: z.string().max(600),
  isMature: z.boolean(),
});
type ComicForm = z.infer<typeof comicSchema>;

const toBody = (v: ComicForm): SaveComicBody => ({
  ...v,
  slug: v.slug || undefined,
  tags: v.tags.split(',').map((t) => t.trim()).filter(Boolean),
});

function ComicFormFields({ form }: { form: ReturnType<typeof useForm<ComicForm>> }) {
  const e = form.formState.errors;
  return (
    <div className="space-y-4">
      <div className="grid gap-4 sm:grid-cols-2">
        <Field label="Tên truyện (tiếng Việt)" error={e.translatedTitle?.message} required>{(id) => <Input id={id} {...form.register('translatedTitle')} data-testid="comic-title-input" />}</Field>
        <Field label="Tên gốc" error={e.originalTitle?.message} required>{(id) => <Input id={id} {...form.register('originalTitle')} />}</Field>
        <Field label="Tác giả" error={e.author?.message}>{(id) => <Input id={id} {...form.register('author')} />}</Field>
        <Field label="Hoạ sĩ" error={e.artist?.message}>{(id) => <Input id={id} {...form.register('artist')} />}</Field>
        <Field label="Trạng thái">
          {(id) => (
            <Select id={id} {...form.register('status')}>
              {Object.entries(comicStatusLabel).map(([v, l]) => <option key={v} value={v}>{l}</option>)}
            </Select>
          )}
        </Field>
        <Field label="Đường dẫn" error={e.slug?.message} hint="Để trống để tạo tự động từ tên truyện.">{(id) => <Input id={id} {...form.register('slug')} />}</Field>
      </div>
      <Field label="Thể loại" error={e.tags?.message} hint="Phân tách bằng dấu phẩy, ví dụ: Hành động, Hài hước">{(id) => <Input id={id} {...form.register('tags')} />}</Field>
      <Field label="Giới thiệu" error={e.description?.message}>{(id) => <Textarea id={id} rows={5} {...form.register('description')} />}</Field>
      <Checkbox label="Nội dung 18+" description="Người đọc phải xác nhận đủ 18 tuổi trước khi đọc." {...form.register('isMature')} />
    </div>
  );
}

const emptyComic: ComicForm = { translatedTitle: '', originalTitle: '', slug: '', author: '', artist: '', description: '', status: 'ongoing', tags: '', isMature: false };

export function HouseComicsPage() {
  useDocumentTitle('Truyện của nhóm');
  const { currentHouse, can } = useAuth();
  const navigate = useNavigate();
  const toast = useToast();
  const [params, setParams] = useSearchParams();
  const [q, setQ] = useState(params.get('q') ?? '');
  const debounced = useDebounced(q);
  const page = Number(params.get('page') ?? '1') || 1;
  const [creating, setCreating] = useState(false);
  const comics = useQuery({
    queryKey: ['house', currentHouse?.houseId, 'comics', debounced, page],
    queryFn: () => houseApi.comics({ q: debounced || undefined, page }),
    placeholderData: keepPreviousData,
  });

  const form = useForm<ComicForm>({ resolver: zodResolver(comicSchema), defaultValues: emptyComic });
  const create = form.handleSubmit(async (v) => {
    try {
      const comic = await houseApi.createComic(toBody(v));
      toast.success('Đã tạo truyện.');
      setCreating(false);
      form.reset(emptyComic);
      navigate(paths.studio.comic(comic.id));
    } catch (e) {
      if (!applyServerErrors(e, form.setError, ['translatedTitle', 'originalTitle', 'slug', 'author', 'artist', 'description', 'tags'])) toast.error(e);
    }
  });

  return (
    <>
      <PageHeader title="Truyện của nhóm" description={comics.data ? `${comics.data.total} truyện` : undefined}
        actions={can('manageComics') && <Button icon={<Plus className="h-4 w-4" />} onClick={() => setCreating(true)} data-testid="new-comic">Thêm truyện</Button>} />
      <div className="relative mb-4 max-w-sm">
        <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted" />
        <Input value={q} onChange={(e) => { setQ(e.target.value); setParams({}); }} placeholder="Tìm theo tên truyện…" aria-label="Tìm truyện" className="pl-9" />
      </div>
      {comics.isError ? <ErrorState error={comics.error} onRetry={() => comics.refetch()} />
        : comics.isLoading ? <Skeleton className="h-64 rounded-2xl" />
          : comics.data?.items.length === 0 ? (
            <EmptyState title={debounced ? 'Không tìm thấy truyện' : 'Nhóm chưa có truyện nào'} description="Tạo truyện, thêm chương rồi tải tệp ZIP/CBZ lên để bắt đầu."
              action={can('manageComics') && !debounced && <Button onClick={() => setCreating(true)}>Thêm truyện đầu tiên</Button>} />
          ) : (
            <DataTable
              rows={comics.data?.items ?? []}
              rowKey={(c) => c.id}
              onRowClick={(c) => navigate(paths.studio.comic(c.id))}
              testId="house-comics"
              columns={[
                {
                  key: 'title', header: 'Truyện',
                  cell: (c) => (
                    <div className="flex items-center gap-3">
                      <Cover url={c.coverUrl} title={c.translatedTitle} className="w-10 shrink-0 rounded-lg" />
                      <div className="min-w-0">
                        <p className="truncate font-medium">{c.translatedTitle}</p>
                        <p className="truncate text-xs text-muted">{c.originalTitle}</p>
                      </div>
                    </div>
                  ),
                },
                { key: 'status', header: 'Trạng thái', cell: (c) => <Badge>{comicStatusLabel[c.status]}</Badge> },
                { key: 'chapters', header: 'Chương', cell: (c) => <span>{c.publishedChapterCount}/{c.chapterCount} đã đăng</span> },
                { key: 'updated', header: 'Cập nhật', cell: (c) => <span className="text-xs text-muted">{timeAgo(c.updatedAt)}</span>, hideOnMobile: true },
              ]}
            />
          )}
      {comics.data && <Pagination page={comics.data.page} totalPages={comics.data.totalPages} onChange={(p) => setParams({ page: String(p) })} />}
      <Modal open={creating} onClose={() => setCreating(false)} title="Thêm truyện" size="lg"
        footer={<><Button variant="outline" onClick={() => setCreating(false)}>Huỷ</Button><Button loading={form.formState.isSubmitting} onClick={create} data-testid="save-comic">Tạo truyện</Button></>}>
        <form onSubmit={create} noValidate><ComicFormFields form={form} /></form>
      </Modal>
    </>
  );
}

export function HouseComicPage() {
  const { id = '' } = useParams();
  const { currentHouse, can } = useAuth();
  const navigate = useNavigate();
  const toast = useToast();
  const queryClient = useQueryClient();
  const key = ['house', currentHouse?.houseId, 'comic', id];
  const comic = useQuery({ queryKey: key, queryFn: () => houseApi.comic(id) });
  const chapters = useQuery({
    queryKey: [...key, 'chapters'],
    queryFn: () => houseApi.chapters(id),
    refetchInterval: (q) => (q.state.data?.some((c) => isActiveJob(c.latestJob)) ? 2500 : false),
  });
  useDocumentTitle(comic.data?.translatedTitle);
  const [editing, setEditing] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const [newChapter, setNewChapter] = useState(false);

  const form = useForm<ComicForm>({ resolver: zodResolver(comicSchema), defaultValues: emptyComic });
  const startEdit = (c: HouseComicDetail) => {
    form.reset({ translatedTitle: c.translatedTitle, originalTitle: c.originalTitle, slug: c.slug, author: c.author ?? '', artist: c.artist ?? '', description: c.description ?? '', status: c.status, tags: c.tags.map((t) => t.name).join(', '), isMature: c.isMature });
    setEditing(true);
  };
  const save = form.handleSubmit(async (v) => {
    try {
      queryClient.setQueryData(key, await houseApi.updateComic(id, toBody(v)));
      toast.success('Đã lưu thông tin truyện.');
      setEditing(false);
    } catch (e) {
      if (!applyServerErrors(e, form.setError, ['translatedTitle', 'originalTitle', 'slug', 'author', 'artist', 'description', 'tags'])) toast.error(e);
    }
  });

  const cover = useMutation({
    mutationFn: (file: File) => houseApi.uploadCover(id, file),
    onSuccess: (data) => { queryClient.setQueryData(key, data); toast.success('Đã cập nhật ảnh bìa.'); },
    onError: (e) => toast.error(e),
  });
  const remove = useMutation({
    mutationFn: () => houseApi.deleteComic(id),
    onSuccess: () => { toast.success('Đã xoá truyện.'); navigate(paths.studio.comics); },
    onError: (e) => toast.error(e),
  });

  if (comic.isError) return <ErrorState error={comic.error} onRetry={() => comic.refetch()} />;
  const c = comic.data;
  const manage = can('manageComics');

  return (
    <>
      <PageHeader
        title={c?.translatedTitle ?? '…'}
        description={c && <>{c.originalTitle} · <Link className="link" to={paths.comic(c.id, c.slug)} target="_blank">Xem trang công khai <ExternalLink className="inline h-3 w-3" /></Link></>}
        actions={c && manage && (
          <>
            <Button variant="outline" onClick={() => startEdit(c)}>Sửa thông tin</Button>
            <Button variant="ghost" className="text-danger" icon={<Trash2 className="h-4 w-4" />} onClick={() => setDeleting(true)}>Xoá truyện</Button>
          </>
        )}
      />
      <div className="grid gap-6 lg:grid-cols-[12rem_1fr]">
        {/* Phones: small cover beside the details so the chapter list stays near the top. */}
        <div className="flex items-start gap-4 lg:block lg:space-y-2">
          <div className="w-28 shrink-0 space-y-2 sm:w-36 lg:w-auto">
            {c ? <Cover url={c.coverUrl} title={c.translatedTitle} /> : <Skeleton className="aspect-[3/4] w-full rounded-xl" />}
            {manage && (
              <label className="block cursor-pointer text-center text-sm text-accent hover:underline">
                {cover.isPending ? 'Đang xử lý ảnh…' : 'Đổi ảnh bìa'}
                <input type="file" accept="image/jpeg,image/png,image/webp" className="sr-only" data-testid="cover-input" onChange={(e) => e.target.files?.[0] && cover.mutate(e.target.files[0])} />
              </label>
            )}
          </div>
          {c && (
            <div className="space-y-1 text-xs text-muted">
              <p>{comicStatusLabel[c.status]}{c.isMature ? ' · 18+' : ''}</p>
              <p>{c.tags.map((t) => t.name).join(', ') || 'Chưa có thể loại'}</p>
              <p>Tạo ngày {formatDate(c.createdAt)}</p>
            </div>
          )}
        </div>
        <section>
          <div className="mb-3 flex items-center justify-between">
            <h2 className="font-semibold">Chương ({chapters.data?.length ?? 0})</h2>
            {can('manageChapters') && <Button size="sm" icon={<Plus className="h-4 w-4" />} onClick={() => setNewChapter(true)} data-testid="new-chapter">Thêm chương</Button>}
          </div>
          {chapters.isError ? <ErrorState error={chapters.error} onRetry={() => chapters.refetch()} />
            : chapters.data?.length === 0 ? <EmptyState title="Chưa có chương nào" description="Thêm chương rồi tải tệp ZIP/CBZ chứa các trang." />
              : (
                <DataTable
                  rows={chapters.data ?? []}
                  rowKey={(ch) => ch.id}
                  onRowClick={(ch) => navigate(paths.studio.chapter(ch.id))}
                  testId="house-chapters"
                  columns={[
                    { key: 'n', header: 'Chương', cell: (ch) => <span className="font-medium">{chapterLabel(ch.number, ch.title)}</span> },
                    {
                      key: 'status', header: 'Trạng thái',
                      cell: (ch) => (
                        <div className="flex flex-wrap items-center gap-1">
                          <ChapterStatusBadge status={ch.status} />
                          {ch.readyVersion && ch.status === 'published' && <Badge tone="accent">Có bản mới v{ch.readyVersion}</Badge>}
                        </div>
                      ),
                    },
                    {
                      key: 'job', header: 'Xử lý', className: 'w-56', hideOnMobile: true,
                      cell: (ch) => (isActiveJob(ch.latestJob) && ch.latestJob ? <JobProgress job={ch.latestJob} /> : ch.latestJob?.status === 'failed' ? <span className="text-xs text-danger">Lỗi xử lý</span> : <span className="text-xs text-muted">{ch.pageCount ? `${ch.pageCount} trang` : '—'}</span>),
                    },
                    { key: 'pub', header: 'Đăng lúc', cell: (ch) => <span className="text-xs text-muted">{formatDate(ch.publishedAt)}</span>, hideOnMobile: true },
                  ]}
                />
              )}
        </section>
      </div>

      <Modal open={editing} onClose={() => setEditing(false)} title="Sửa thông tin truyện" size="lg"
        footer={<><Button variant="outline" onClick={() => setEditing(false)}>Huỷ</Button><Button loading={form.formState.isSubmitting} onClick={save}>Lưu</Button></>}>
        <form onSubmit={save} noValidate><ComicFormFields form={form} /></form>
      </Modal>
      {newChapter && <NewChapterDialog comicId={id} nextNumber={(chapters.data?.[0]?.number ?? 0) + 1} onClose={() => setNewChapter(false)} />}
      <ConfirmDialog open={deleting} danger title="Xoá truyện" confirmLabel="Xoá truyện" loading={remove.isPending}
        message={<>Truyện <strong>{c?.translatedTitle}</strong> và mọi chương sẽ bị gỡ khỏi trang đọc ngay lập tức. Dữ liệu được giữ 30 ngày trước khi xoá vĩnh viễn.</>}
        onClose={() => setDeleting(false)} onConfirm={() => remove.mutate()} />
    </>
  );
}

const chapterSchema = z.object({
  number: z.coerce.number({ message: 'Số chương không hợp lệ.' }).min(0, 'Số chương không hợp lệ.').max(99999.99),
  title: z.string().max(200, 'Tối đa 200 ký tự.'),
});

function NewChapterDialog({ comicId, nextNumber, onClose }: { comicId: string; nextNumber: number; onClose: () => void }) {
  const navigate = useNavigate();
  const toast = useToast();
  const form = useForm<z.input<typeof chapterSchema>, unknown, z.output<typeof chapterSchema>>({ resolver: zodResolver(chapterSchema), defaultValues: { number: nextNumber, title: '' } });
  const submit = form.handleSubmit(async (v) => {
    try {
      const chapter = await houseApi.createChapter(comicId, { number: v.number, title: v.title || undefined });
      toast.success('Đã tạo chương. Hãy tải tệp lên.');
      navigate(paths.studio.chapter(chapter.id));
    } catch (e) {
      if (!applyServerErrors(e, form.setError, ['number', 'title'])) toast.error(e);
    }
  });
  return (
    <Modal open onClose={onClose} title="Thêm chương" size="sm"
      footer={<><Button variant="outline" onClick={onClose}>Huỷ</Button><Button loading={form.formState.isSubmitting} onClick={submit} data-testid="save-chapter">Tạo chương</Button></>}>
      <form onSubmit={submit} className="space-y-4" noValidate>
        <Field label="Số chương" error={form.formState.errors.number?.message} hint="Có thể dùng số lẻ, ví dụ 10.5" required>
          {(id) => <Input id={id} type="number" step="0.01" min={0} {...form.register('number')} data-testid="chapter-number" />}
        </Field>
        <Field label="Tên chương" error={form.formState.errors.title?.message}>{(id) => <Input id={id} {...form.register('title')} />}</Field>
      </form>
    </Modal>
  );
}
