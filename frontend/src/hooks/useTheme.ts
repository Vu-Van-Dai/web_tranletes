import { useCallback, useEffect, useState } from 'react';
import { useLocation } from 'react-router';

export type Theme = 'light' | 'dark';

function current(): Theme {
  return document.documentElement.dataset.theme === 'dark' ? 'dark' : 'light';
}

/** Light/dark theme persisted per browser (initial value applied in index.html before paint). */
export function useTheme() {
  const [theme, setTheme] = useState<Theme>(current);

  useEffect(() => {
    document.documentElement.dataset.theme = theme;
  }, [theme]);

  const toggle = useCallback(() => {
    setTheme((t) => {
      const next = t === 'dark' ? 'light' : 'dark';
      try {
        localStorage.setItem('theme', next);
      } catch {
        // ignore
      }
      return next;
    });
  }, []);

  return { theme, toggle };
}

export function useDocumentTitle(title: string | undefined, seo?: { description?: string | null; canonical?: string; image?: string | null; mature?: boolean; noindex?: boolean; pending?: boolean }) {
  const { pathname, search } = useLocation();
  const description = seo?.description;
  const canonical = seo?.canonical;
  const image = seo?.image;
  const mature = seo?.mature;
  const noindex = seo?.noindex;
  const pending = seo?.pending;
  useEffect(() => {
    // Keep the initial edge metadata intact while the API data is loading.
    if (pending) return;
    const app = (import.meta.env.PUBLIC_APP_NAME as string | undefined) || 'Rồng Nhỏ';
    document.title = title ? `${title} · ${app}` : pathname === '/' ? 'Đọc truyện tranh online, truyện dịch tiếng Việt · Rồng Nhỏ' : app;
    const origin = 'https://rongnho.shop';
    const params = new URLSearchParams(search);
    const page = Number(params.get('page') ?? 1);
    const pageSuffix = /^(\/truyen|\/the-loai\/[^/]+|\/nhom-dich\/[^/]+)$/.test(pathname) && Number.isSafeInteger(page) && page > 1 ? '?page=' + page : '';
    const blocked = /^(\/quan-tri|\/quan-ly-nhom|\/workspace|\/dang-nhap|\/dang-ky|\/doi-mat-khau|\/tu-truyen|\/tim-kiem)(\/|$)/.test(pathname);
    const filtered = /^(\/truyen|\/the-loai\/[^/]+|\/nhom-dich\/[^/]+)$/.test(pathname) && ['q', 'status', 'sort'].some(k => !!params.get(k) && !(k === 'sort' && params.get(k) === 'updated'));
    const href = origin + (canonical ?? pathname + pageSuffix);
    let tag = document.querySelector<HTMLLinkElement>('link[rel="canonical"]');
    if (blocked) tag?.remove();
    else {
      if (!tag) { tag = document.createElement('link'); tag.rel = 'canonical'; document.head.appendChild(tag); }
      tag.href = href;
    }
    const setMeta = (attribute: 'name' | 'property', key: string, value: string | null) => {
      let element = document.querySelector<HTMLMetaElement>(`meta[${attribute}="${key}"]`);
      if (value === null) { element?.remove(); return; }
      if (!element) { element = document.createElement('meta'); element.setAttribute(attribute, key); document.head.appendChild(element); }
      element.content = value;
    };
    const summary = (description || (title ? title + ' tại Rồng Nhỏ.' : 'Rồng Nhỏ – website đọc truyện tranh dịch tiếng Việt.')).replace(/\s+/g, ' ').trim().slice(0, 300);
    const cover = !mature && image ? image : origin + '/brand/og-cover.webp';
    setMeta('name', 'description', summary);
    setMeta('name', 'robots', blocked || noindex || filtered ? 'noindex, follow' : 'index, follow, max-image-preview:large');
    setMeta('name', 'rating', mature ? 'adult' : null);
    setMeta('property', 'og:url', blocked ? null : href);
    setMeta('property', 'og:title', document.title);
    setMeta('property', 'og:description', summary);
    setMeta('property', 'og:image', cover);
    setMeta('name', 'twitter:title', document.title);
    setMeta('name', 'twitter:description', summary);
    setMeta('name', 'twitter:image', cover);
  }, [title, pathname, search, description, canonical, image, mature, noindex, pending]);
}

export function useDebounced<T>(value: T, ms = 300): T {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const id = setTimeout(() => setDebounced(value), ms);
    return () => clearTimeout(id);
  }, [value, ms]);
  return debounced;
}
