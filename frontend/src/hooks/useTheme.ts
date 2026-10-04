import { useCallback, useEffect, useState } from 'react';

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

export function useDocumentTitle(title: string | undefined) {
  useEffect(() => {
    const app = (import.meta.env.PUBLIC_APP_NAME as string | undefined) || 'Trạm Truyện';
    document.title = title ? `${title} · ${app}` : app;
  }, [title]);
}

export function useDebounced<T>(value: T, ms = 300): T {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const id = setTimeout(() => setDebounced(value), ms);
    return () => clearTimeout(id);
  }, [value, ms]);
  return debounced;
}
