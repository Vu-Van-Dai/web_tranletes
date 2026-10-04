import { Link } from 'react-router';
import { ImageOff } from 'lucide-react';
import { paths } from '@/app/paths';
import { Badge, Skeleton } from '@/components/ui';
import { comicStatusLabel } from '@/text/vi';
import type { ComicCard as Card } from '@/types/api';
import { chapterLabel, cn, isRecent, timeAgo } from '@/utils/format';

export function Cover({ url, title, className }: { url: string | null; title: string; className?: string }) {
  return (
    <div className={cn('relative aspect-[3/4] overflow-hidden rounded-xl bg-surface-3', className)}>
      {url ? (
        <img src={url} alt={`Bìa truyện ${title}`} loading="lazy" decoding="async" className="h-full w-full object-cover transition-transform duration-300 group-hover:scale-[1.03]" />
      ) : (
        <div className="flex h-full w-full flex-col items-center justify-center gap-2 p-3 text-center text-muted">
          <ImageOff className="h-6 w-6" aria-hidden />
          <span className="line-clamp-3 text-xs">{title}</span>
        </div>
      )}
    </div>
  );
}

export function ComicCard({ comic }: { comic: Card }) {
  return (
    <article className="group @container flex flex-col gap-2" data-testid="comic-card">
      <Link to={paths.comic(comic.id, comic.slug)} className="relative block" aria-label={comic.translatedTitle}>
        <Cover url={comic.coverUrl} title={comic.translatedTitle} />
        <div className="absolute left-2 top-2 flex gap-1">
          {isRecent(comic.lastChapterPublishedAt) && (
            <span className="rounded-full bg-accent px-2.5 py-0.5 text-xs font-semibold text-accent-ink shadow-sm">Mới</span>
          )}
          {comic.isMature && <Badge tone="danger">18+</Badge>}
        </div>
      </Link>
      <div className="min-w-0">
        <Link to={paths.comic(comic.id, comic.slug)} className="line-clamp-2 text-sm font-semibold leading-snug hover:text-accent">
          {comic.translatedTitle}
        </Link>
        <ul className="mt-1.5 space-y-1">
          {comic.latestChapters.map((ch) => (
            <li key={ch.id} className="flex items-center justify-between gap-2 text-xs">
              {/* The chapter number never truncates; on narrow cards the timestamp gives way instead. */}
              <Link to={paths.read(ch.id)} className="shrink-0 whitespace-nowrap rounded-md bg-surface-2 px-2 py-0.5 hover:bg-accent-soft hover:text-accent">
                {chapterLabel(ch.number)}
              </Link>
              <span className="hidden min-w-0 truncate text-muted @[10.5rem]:inline">{timeAgo(ch.publishedAt)}</span>
            </li>
          ))}
        </ul>
        <p className="mt-1 truncate text-xs text-muted">
          {comic.house.name} · {comicStatusLabel[comic.status]}
        </p>
      </div>
    </article>
  );
}

export function ComicGrid({ comics, loading, count = 12 }: { comics?: Card[]; loading?: boolean; count?: number }) {
  return (
    <div className="grid grid-cols-2 gap-x-4 gap-y-6 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-6" data-testid="comic-grid">
      {loading
        ? Array.from({ length: count }, (_, i) => (
            <div key={i} className="space-y-2">
              <Skeleton className="aspect-[3/4] w-full rounded-xl" />
              <Skeleton className="h-4 w-4/5" />
              <Skeleton className="h-3 w-3/5" />
            </div>
          ))
        : comics?.map((c) => <ComicCard key={c.id} comic={c} />)}
    </div>
  );
}
