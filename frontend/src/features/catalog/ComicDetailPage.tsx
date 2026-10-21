import { useMemo, useState } from 'react';
import { Link, useParams } from 'react-router';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { BookOpen, Bookmark, BookmarkCheck, PlayCircle, Search } from 'lucide-react';
import { paths } from '@/app/paths';
import { Badge, Button, ButtonLink, EmptyState, ErrorState, Input, Skeleton, useToast } from '@/components/ui';
import { AdSlot } from '@/features/ads/Ads';
import { useAuth } from '@/features/auth/AuthProvider';
import { libraryApi, publicApi } from '@/services/api';
import { useDocumentTitle } from '@/hooks/useTheme';
import { comicStatusLabel } from '@/text/vi';
import { chapterLabel, cn, formatDate, isRecent, timeAgo } from '@/utils/format';
import { Cover } from './ComicCard';

export function ComicDetailPage() {
  const { id = '' } = useParams();
  const { status } = useAuth();
  const toast = useToast();
  const queryClient = useQueryClient();
  const comic = useQuery({ queryKey: ['comic', id], queryFn: () => publicApi.comic(id) });
  const chapters = useQuery({ queryKey: ['chapters', id], queryFn: () => publicApi.chapters(id) });
  const state = useQuery({ queryKey: ['library-state', id], queryFn: () => libraryApi.state(id), enabled: status === 'authenticated' });
  const [filter, setFilter] = useState('');
  const [expanded, setExpanded] = useState(false);
  useDocumentTitle(comic.data?.translatedTitle);

  const follow = useMutation({
    mutationFn: (following: boolean) => (following ? libraryApi.unfollow(id) : libraryApi.follow(id)),
    onSuccess: (_, wasFollowing) => {
      toast.success(wasFollowing ? 'Đã bỏ theo dõi truyện.' : 'Đã thêm vào tủ truyện.');
      queryClient.invalidateQueries({ queryKey: ['library-state', id] });
      queryClient.invalidateQueries({ queryKey: ['follows'] });
    },
    onError: (e) => toast.error(e),
  });

  const visibleChapters = useMemo(() => {
    const term = filter.trim();
    return (chapters.data ?? []).filter((c) => !term || String(c.number).includes(term) || c.title?.toLowerCase().includes(term.toLowerCase()));
  }, [chapters.data, filter]);

  if (comic.isError) return <div className="container-page py-10"><ErrorState error={comic.error} onRetry={() => comic.refetch()} title="Không tìm thấy truyện" /></div>;
  const c = comic.data;

  return (
    <div className="container-page py-6">
      <AdSlot slot="comic_top" className="mb-6" />
      <div className="grid gap-6 md:grid-cols-[14rem_1fr] lg:grid-cols-[16rem_1fr]">
        <div className="mx-auto w-44 md:mx-0 md:w-full">
          {c ? <Cover url={c.coverUrl} title={c.translatedTitle} className="shadow-card" /> : <Skeleton className="aspect-[3/4] w-full rounded-xl" />}
        </div>
        <div className="min-w-0">
          {c ? (
            <>
              <h1 className="text-2xl font-semibold leading-tight sm:text-3xl" data-testid="comic-title">{c.translatedTitle}</h1>
              <p className="mt-1 text-sm text-muted">{c.originalTitle}</p>
              <div className="mt-3 flex flex-wrap gap-2">
                <Badge tone={c.status === 'completed' ? 'success' : 'neutral'}>{comicStatusLabel[c.status]}</Badge>
                {c.isMature && <Badge tone="danger">18+</Badge>}
                {c.tags.map((t) => (
                  <Link key={t.id} to={paths.genre(t.slug)}><Badge tone="accent">{t.name}</Badge></Link>
                ))}
              </div>
              <dl className="mt-4 grid grid-cols-[auto_1fr] gap-x-4 gap-y-1.5 text-sm">
                {c.author && (<><dt className="text-muted">Tác giả</dt><dd>{c.author}</dd></>)}
                {c.artist && (<><dt className="text-muted">Hoạ sĩ</dt><dd>{c.artist}</dd></>)}
                <dt className="text-muted">Nhóm dịch</dt>
                <dd><Link className="link" to={paths.house(c.house.slug)}>{c.house.name}</Link></dd>
                <dt className="text-muted">Số chương</dt><dd>{c.chapterCount}</dd>
                <dt className="text-muted">Cập nhật</dt><dd>{timeAgo(c.lastChapterPublishedAt)}</dd>
              </dl>
              <div className="mt-5 flex flex-wrap gap-2">
                {state.data?.lastRead ? (
                  <ButtonLink to={paths.read(state.data.lastRead.id)} icon={<PlayCircle className="h-4 w-4" />}>
                    Đọc tiếp {chapterLabel(state.data.lastRead.number)}
                  </ButtonLink>
                ) : (
                  c.firstChapter && <ButtonLink to={paths.read(c.firstChapter.id)} icon={<BookOpen className="h-4 w-4" />} data-testid="read-first">Đọc từ đầu</ButtonLink>
                )}
                {c.latestChapter && c.latestChapter.id !== c.firstChapter?.id && (
                  <ButtonLink to={paths.read(c.latestChapter.id)} variant="outline">Chương mới nhất</ButtonLink>
                )}
                {status === 'authenticated' ? (
                  <Button variant="outline" loading={follow.isPending} onClick={() => follow.mutate(!!state.data?.following)}
                    icon={state.data?.following ? <BookmarkCheck className="h-4 w-4 text-accent" /> : <Bookmark className="h-4 w-4" />}>
                    {state.data?.following ? 'Đang theo dõi' : 'Theo dõi'}
                  </Button>
                ) : (
                  <ButtonLink to={`${paths.login}?next=${encodeURIComponent(paths.comic(id))}`} variant="ghost" icon={<Bookmark className="h-4 w-4" />}>Đăng nhập để theo dõi</ButtonLink>
                )}
              </div>
              {c.description && (
                <div className="mt-6">
                  <h2 className="mb-2 font-semibold">Giới thiệu</h2>
                  <p className={`whitespace-pre-line text-sm leading-relaxed text-muted ${expanded ? '' : 'line-clamp-4'}`}>{c.description}</p>
                  {c.description.length > 280 && (
                    <button className="mt-1 text-sm text-accent" onClick={() => setExpanded((v) => !v)}>{expanded ? 'Thu gọn' : 'Xem thêm'}</button>
                  )}
                </div>
              )}
            </>
          ) : (
            <div className="space-y-3"><Skeleton className="h-8 w-2/3" /><Skeleton className="h-4 w-1/3" /><Skeleton className="h-24 w-full" /></div>
          )}
        </div>
      </div>

      <AdSlot slot="comic_chapters" className="mt-10" />
      <section className="mt-10">
        <div className="mb-4 flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
          <h2 className="text-lg font-semibold">Danh sách chương</h2>
          <div className="relative w-full sm:w-64">
            <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted" />
            <Input value={filter} onChange={(e) => setFilter(e.target.value)} placeholder="Tìm chương…" aria-label="Tìm chương" className="pl-9" />
          </div>
        </div>
        {chapters.isError ? (
          <ErrorState error={chapters.error} onRetry={() => chapters.refetch()} />
        ) : chapters.data?.length === 0 ? (
          <EmptyState title="Chưa có chương nào" />
        ) : (
          <ul className="card grid divide-y divide-line sm:grid-cols-2 sm:divide-y-0" data-testid="chapter-list">
            {chapters.isLoading && Array.from({ length: 6 }, (_, i) => <li key={i} className="p-3"><Skeleton className="h-6 w-full" /></li>)}
            {visibleChapters.map((ch, i, all) => (
              // Two columns from sm up: the last row gets no bottom rule (one or two items depending on parity).
              <li key={ch.id} className={cn('border-line sm:border-b', i >= all.length - (all.length % 2 === 0 ? 2 : 1) && 'sm:border-b-0')}>
                <Link to={paths.read(ch.id)} className="flex items-center justify-between gap-3 px-4 py-3 hover:bg-surface-2">
                  <span className="min-w-0 truncate text-sm font-medium">
                    {chapterLabel(ch.number, ch.title)}
                    {state.data?.lastRead?.id === ch.id && <Badge tone="accent" className="ml-2">Đang đọc</Badge>}
                  </span>
                  <span className="flex shrink-0 items-center gap-2 text-xs text-muted">
                    {isRecent(ch.publishedAt) && <Badge tone="accent">Mới</Badge>}
                    {formatDate(ch.publishedAt)}
                  </span>
                </Link>
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  );
}
