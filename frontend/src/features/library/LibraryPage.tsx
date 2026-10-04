import { useState } from 'react';
import { Link } from 'react-router';
import { useQuery } from '@tanstack/react-query';
import { paths } from '@/app/paths';
import { EmptyState, ErrorState, Tabs } from '@/components/ui';
import { ComicGrid, Cover } from '@/features/catalog/ComicCard';
import { libraryApi } from '@/services/api';
import { useDocumentTitle } from '@/hooks/useTheme';
import { chapterLabel, timeAgo } from '@/utils/format';

export function LibraryPage() {
  useDocumentTitle('Tủ truyện');
  const [tab, setTab] = useState<'follows' | 'history'>('follows');
  const follows = useQuery({ queryKey: ['follows'], queryFn: libraryApi.follows, enabled: tab === 'follows' });
  const history = useQuery({ queryKey: ['history'], queryFn: libraryApi.history, enabled: tab === 'history' });

  return (
    <div className="container-page py-6">
      <div className="mb-6 flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-semibold">Tủ truyện</h1>
        <Tabs value={tab} onChange={setTab} items={[{ value: 'follows', label: 'Đang theo dõi' }, { value: 'history', label: 'Lịch sử đọc' }]} />
      </div>
      {tab === 'follows' ? (
        follows.isError ? <ErrorState error={follows.error} onRetry={() => follows.refetch()} />
          : follows.data?.length === 0 ? <EmptyState title="Bạn chưa theo dõi truyện nào" description="Bấm “Theo dõi” ở trang truyện để nhận chương mới tại đây." />
            : <ComicGrid comics={follows.data} loading={follows.isLoading} />
      ) : history.isError ? (
        <ErrorState error={history.error} onRetry={() => history.refetch()} />
      ) : history.data?.length === 0 ? (
        <EmptyState title="Chưa có lịch sử đọc" />
      ) : (
        <ul className="card divide-y divide-line">
          {history.data?.map((h) => (
            <li key={h.comic.id} className="flex items-center gap-4 p-3">
              <Link to={paths.comic(h.comic.id, h.comic.slug)} className="w-14 shrink-0"><Cover url={h.comic.coverUrl} title={h.comic.translatedTitle} className="rounded-lg" /></Link>
              <div className="min-w-0 flex-1">
                <Link to={paths.comic(h.comic.id, h.comic.slug)} className="line-clamp-1 font-medium hover:text-accent">{h.comic.translatedTitle}</Link>
                <p className="text-sm text-muted">Đã đọc {chapterLabel(h.chapter.number)} · trang {h.page} · {timeAgo(h.updatedAt)}</p>
              </div>
              <Link to={paths.read(h.chapter.id)} className="shrink-0 rounded-xl bg-accent px-3 py-1.5 text-sm font-medium text-accent-ink">Đọc tiếp</Link>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
