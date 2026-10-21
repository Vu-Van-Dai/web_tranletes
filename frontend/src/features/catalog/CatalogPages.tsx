import { useState } from 'react';
import { Link, useParams, useSearchParams } from 'react-router';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { ChevronRight, Flame, Sparkles, CheckCircle2, Users } from 'lucide-react';
import { paths } from '@/app/paths';
import { EmptyState, ErrorState, Pagination, Select, Skeleton, Tabs, ButtonLink } from '@/components/ui';
import { publicApi } from '@/services/api';
import { useDocumentTitle } from '@/hooks/useTheme';
import { comicStatusLabel, sortOptions } from '@/text/vi';
import { ComicGrid, Cover } from './ComicCard';

function Section({ title, icon, link, children }: { title: string; icon: React.ReactNode; link?: string; children: React.ReactNode }) {
  return (
    <section className="mt-10 first:mt-6">
      <div className="mb-4 flex items-center justify-between">
        <h2 className="flex items-center gap-2 text-lg font-semibold">
          <span className="text-accent">{icon}</span>
          {title}
        </h2>
        {link && (
          <Link to={link} className="flex items-center gap-0.5 text-sm text-muted hover:text-accent">
            Xem tất cả <ChevronRight className="h-4 w-4" />
          </Link>
        )}
      </div>
      {children}
    </section>
  );
}

export function HomePage() {
  useDocumentTitle(undefined);
  const updated = useQuery({ queryKey: ['comics', { sort: 'updated', pageSize: 18 }], queryFn: () => publicApi.comics({ sort: 'updated', pageSize: 18 }) });
  const newest = useQuery({ queryKey: ['comics', { sort: 'new', pageSize: 6 }], queryFn: () => publicApi.comics({ sort: 'new', pageSize: 6 }) });
  const completed = useQuery({ queryKey: ['comics', { status: 'completed', pageSize: 6 }], queryFn: () => publicApi.comics({ status: 'completed', pageSize: 6 }) });
  const [period, setPeriod] = useState<'day' | 'week' | 'month'>('week');
  const ranking = useQuery({ queryKey: ['rankings', period], queryFn: () => publicApi.rankings(period), staleTime: 60_000 });
  const houses = useQuery({ queryKey: ['houses'], queryFn: publicApi.houses, staleTime: 60_000 });

  return (
    <div className="container-page">
      <section className="mt-6 rounded-2xl border border-line bg-surface p-5 sm:p-6" aria-labelledby="home-intro-title">
        <h1 id="home-intro-title" className="text-2xl font-bold leading-tight sm:text-3xl">
          Rồng Nhỏ – Đọc truyện tranh online
        </h1>
        <p className="mt-3 max-w-4xl text-sm leading-7 text-muted sm:text-base">
          Chào mừng bạn đến với Rồng Nhỏ, nơi khám phá và đọc truyện tranh được dịch sang tiếng Việt bởi các nhóm dịch.
          Bạn có thể tìm truyện mới, xem danh sách chương, theo dõi những bộ truyện yêu thích và quay lại đọc khi có cập nhật.
        </p>
        <p className="mt-2 max-w-4xl text-sm leading-7 text-muted sm:text-base">
          Truyện được sắp xếp theo nội dung, thể loại và tình trạng cập nhật để bạn dễ tìm kiếm.
          Hãy bắt đầu từ danh sách truyện hoặc khám phá các nhóm dịch đang đăng tải tác phẩm trên Rồng Nhỏ.
        </p>
        <div className="mt-3 flex flex-wrap gap-x-4 gap-y-2 text-sm">
          <Link className="font-medium text-accent hover:underline" to={paths.browse}>Khám phá danh sách truyện</Link>
          <Link className="font-medium text-accent hover:underline" to={paths.houses}>Tìm hiểu các nhóm dịch</Link>
        </div>
      </section>
      <Section title="Mới cập nhật" icon={<Sparkles className="h-5 w-5" />} link={paths.browse}>
        {updated.isError ? (
          <ErrorState error={updated.error} onRetry={() => updated.refetch()} />
        ) : updated.data?.items.length === 0 ? (
          <EmptyState title="Chưa có truyện nào được đăng" description="Khi các nhóm dịch đăng chương mới, truyện sẽ xuất hiện tại đây." />
        ) : (
          <ComicGrid comics={updated.data?.items} loading={updated.isLoading} count={12} />
        )}
      </Section>

      <div className="grid gap-x-8 lg:grid-cols-[1fr_20rem]">
        <div>
          <Section title="Truyện mới" icon={<Flame className="h-5 w-5" />} link={`${paths.browse}?sort=new`}>
            {newest.data?.items.length === 0 ? <p className="text-sm text-muted">Chưa có truyện.</p> : <ComicGrid comics={newest.data?.items} loading={newest.isLoading} count={6} />}
          </Section>
          <Section title="Đã hoàn thành" icon={<CheckCircle2 className="h-5 w-5" />} link={`${paths.browse}?status=completed`}>
            {completed.data?.items.length === 0 ? <p className="text-sm text-muted">Chưa có truyện hoàn thành.</p> : <ComicGrid comics={completed.data?.items} loading={completed.isLoading} count={6} />}
          </Section>
        </div>
        <aside>
          <section className="mt-10">
            <div className="mb-3 flex items-center justify-between">
              <h2 className="text-lg font-semibold">Xem nhiều</h2>
              <Tabs value={period} onChange={setPeriod} items={[{ value: 'day', label: 'Ngày' }, { value: 'week', label: 'Tuần' }, { value: 'month', label: 'Tháng' }]} />
            </div>
            <ol className="card divide-y divide-line">
              {ranking.isLoading && Array.from({ length: 5 }, (_, i) => <li key={i} className="p-3"><Skeleton className="h-10 w-full" /></li>)}
              {ranking.data?.length === 0 && <li className="p-4 text-sm text-muted">Chưa có dữ liệu lượt xem.</li>}
              {ranking.data?.map((c, i) => (
                <li key={c.id}>
                  <Link to={paths.comic(c.id, c.slug)} className="flex items-center gap-3 p-3 hover:bg-surface-2">
                    <span className={`w-6 text-center text-lg font-bold ${i < 3 ? 'text-accent' : 'text-muted'}`}>{i + 1}</span>
                    <Cover url={c.coverUrl} title={c.translatedTitle} className="w-10 shrink-0 rounded-lg" />
                    <span className="line-clamp-2 text-sm font-medium">{c.translatedTitle}</span>
                  </Link>
                </li>
              ))}
            </ol>
          </section>
          <section className="mt-8">
            <h2 className="mb-3 flex items-center gap-2 text-lg font-semibold"><Users className="h-5 w-5 text-accent" /> Nhóm dịch</h2>
            <div className="flex flex-wrap gap-2">
              {houses.data?.slice(0, 12).map((h) => (
                <Link key={h.id} to={paths.house(h.slug)} className="rounded-full border border-line bg-surface px-3 py-1 text-sm hover:border-accent hover:text-accent">
                  {h.name} <span className="text-muted">· {h.comicCount}</span>
                </Link>
              ))}
            </div>
          </section>
        </aside>
      </div>
    </div>
  );
}

/** Browse, search (/tim-kiem?q=) and genre (/the-loai/:slug) share one page. */
export function BrowsePage({ mode }: { mode: 'browse' | 'search' | 'genre' }) {
  const { slug } = useParams();
  const [params, setParams] = useSearchParams();
  const q = params.get('q') ?? '';
  const status = params.get('status') ?? '';
  const sort = params.get('sort') ?? 'updated';
  const page = Number(params.get('page') ?? '1') || 1;
  const tags = useQuery({ queryKey: ['tags'], queryFn: publicApi.tags, staleTime: 5 * 60_000 });
  const tagName = tags.data?.find((t) => t.slug === slug)?.name;
  const title = mode === 'search' ? (q ? `Kết quả cho "${q}"` : 'Tìm kiếm') : mode === 'genre' ? `Thể loại: ${tagName ?? slug}` : 'Danh sách truyện';
  useDocumentTitle(title);

  const query = { q: q || undefined, tag: mode === 'genre' ? slug : undefined, status: status || undefined, sort, page, pageSize: 24 };
  const result = useQuery({ queryKey: ['comics', query], queryFn: ({ signal }) => publicApi.comics(query, signal), placeholderData: keepPreviousData });

  const update = (patch: Record<string, string>) => {
    const next = new URLSearchParams(params);
    for (const [k, v] of Object.entries(patch)) {
      if (v) next.set(k, v);
      else next.delete(k);
    }
    if (!('page' in patch)) next.delete('page');
    setParams(next);
  };

  return (
    <div className="container-page py-6">
      <div className="mb-6 flex flex-col gap-4 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <h1 className="text-2xl font-semibold">{title}</h1>
          {result.data && <p className="mt-1 text-sm text-muted">{result.data.total} truyện</p>}
        </div>
        <div className="flex gap-2">
          <Select aria-label="Trạng thái" value={status} onChange={(e) => update({ status: e.target.value })} className="w-40">
            <option value="">Mọi trạng thái</option>
            {Object.entries(comicStatusLabel).map(([v, l]) => <option key={v} value={v}>{l}</option>)}
          </Select>
          <Select aria-label="Sắp xếp" value={sort} onChange={(e) => update({ sort: e.target.value })} className="w-40">
            {sortOptions.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
          </Select>
        </div>
      </div>

      {mode !== 'genre' && tags.data && tags.data.length > 0 && (
        <div className="-mx-4 mb-6 flex gap-2 overflow-x-auto px-4 pb-1">
          {tags.data.map((t) => (
            <Link key={t.id} to={paths.genre(t.slug)} className="shrink-0 rounded-full border border-line bg-surface px-3 py-1 text-sm hover:border-accent hover:text-accent">
              {t.name}
            </Link>
          ))}
        </div>
      )}

      {result.isError ? (
        <ErrorState error={result.error} onRetry={() => result.refetch()} />
      ) : result.data?.items.length === 0 ? (
        <EmptyState title="Không tìm thấy truyện phù hợp" description={q ? 'Thử từ khoá khác — có thể gõ không dấu, ví dụ "tham tu".' : 'Hãy thử bỏ bớt bộ lọc.'} />
      ) : (
        <ComicGrid comics={result.data?.items} loading={result.isLoading} count={12} />
      )}
      {result.data && <Pagination page={result.data.page} totalPages={result.data.totalPages} onChange={(p) => update({ page: String(p) })} />}
    </div>
  );
}

export function HousesPage() {
  useDocumentTitle('Nhóm dịch');
  const houses = useQuery({ queryKey: ['houses'], queryFn: publicApi.houses, staleTime: 60_000 });
  return (
    <div className="container-page py-6">
      <h1 className="mb-6 text-2xl font-semibold">Nhóm dịch</h1>
      {houses.isError && <ErrorState error={houses.error} onRetry={() => houses.refetch()} />}
      {houses.data?.length === 0 && <EmptyState title="Chưa có nhóm dịch nào" />}
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        {houses.isLoading && Array.from({ length: 6 }, (_, i) => <Skeleton key={i} className="h-28 rounded-2xl" />)}
        {houses.data?.map((h) => (
          <Link key={h.id} to={paths.house(h.slug)} className="card flex items-center gap-4 p-4 transition-colors hover:border-accent">
            <HouseLogo url={h.logoUrl} name={h.name} />
            <div className="min-w-0">
              <p className="truncate font-semibold">{h.name}</p>
              <p className="text-sm text-muted">{h.comicCount} truyện</p>
              {h.description && <p className="mt-1 line-clamp-1 text-xs text-muted">{h.description}</p>}
            </div>
          </Link>
        ))}
      </div>
    </div>
  );
}

export function HouseLogo({ url, name, size = 'md' }: { url: string | null; name: string; size?: 'md' | 'lg' }) {
  const cls = size === 'lg' ? 'h-20 w-20 text-2xl' : 'h-14 w-14 text-lg';
  return url ? (
    <img src={url} alt={`Logo ${name}`} className={`${cls} shrink-0 rounded-2xl object-cover`} loading="lazy" />
  ) : (
    <span className={`${cls} grid shrink-0 place-items-center rounded-2xl bg-accent-soft font-bold text-accent`}>{name.slice(0, 1).toUpperCase()}</span>
  );
}

export function HousePage() {
  const { slug = '' } = useParams();
  const [params, setParams] = useSearchParams();
  const page = Number(params.get('page') ?? '1') || 1;
  const house = useQuery({ queryKey: ['house-public', slug], queryFn: () => publicApi.house(slug) });
  const comics = useQuery({ queryKey: ['comics', { house: slug, page }], queryFn: () => publicApi.comics({ house: slug, page, pageSize: 24 }), placeholderData: keepPreviousData });
  useDocumentTitle(house.data?.name);

  if (house.isError) return <div className="container-page py-10"><ErrorState error={house.error} onRetry={() => house.refetch()} /></div>;
  const h = house.data;
  return (
    <div className="container-page py-6">
      <div className="card mb-8 flex flex-col gap-4 p-5 sm:flex-row sm:items-center">
        {h ? <HouseLogo url={h.logoUrl} name={h.name} size="lg" /> : <Skeleton className="h-20 w-20 rounded-2xl" />}
        <div className="min-w-0 flex-1">
          <h1 className="text-2xl font-semibold">{h?.name ?? '…'}</h1>
          {h?.description && <p className="mt-1 text-sm text-muted">{h.description}</p>}
          <div className="mt-2 flex flex-wrap gap-3 text-sm">
            {h?.links.facebook && <a className="link" href={h.links.facebook} target="_blank" rel="noopener noreferrer nofollow">Facebook</a>}
            {h?.links.discord && <a className="link" href={h.links.discord} target="_blank" rel="noopener noreferrer nofollow">Discord</a>}
            {h?.links.website && <a className="link" href={h.links.website} target="_blank" rel="noopener noreferrer nofollow">Trang web</a>}
          </div>
        </div>
      </div>
      <h2 className="mb-4 text-lg font-semibold">Truyện của nhóm</h2>
      {comics.data?.items.length === 0 ? <EmptyState title="Nhóm chưa đăng truyện nào" /> : <ComicGrid comics={comics.data?.items} loading={comics.isLoading} />}
      {comics.data && <Pagination page={comics.data.page} totalPages={comics.data.totalPages} onChange={(p) => setParams({ page: String(p) })} />}
    </div>
  );
}

export function NotFoundPage() {
  useDocumentTitle('Không tìm thấy');
  return (
    <div className="container-page flex min-h-[60dvh] flex-col items-center justify-center gap-4 text-center">
      <p className="text-6xl font-bold text-accent">404</p>
      <h1 className="text-xl font-semibold">Không tìm thấy trang</h1>
      <p className="text-sm text-muted">Đường dẫn có thể đã thay đổi hoặc nội dung đã bị gỡ.</p>
      <ButtonLink to={paths.home} variant="outline">Về trang chủ</ButtonLink>
    </div>
  );
}
