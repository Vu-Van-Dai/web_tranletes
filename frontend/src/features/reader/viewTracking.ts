import { useEffect, useRef } from 'react';
import { env } from '@/config/env';
import { publicApi } from '@/services/api';

/**
 * Verified views (paid to translation houses): once the chapter is loaded the reader asks the server for a
 * signed ticket, then reports the view only after the visitor has actually read — enough visible time and
 * enough distinct pages displayed. The server re-checks the timing from the ticket, so this cannot be faked
 * by editing the page; the client-side checks just avoid sending reports that would be rejected anyway.
 */
export function useVerifiedView(chapterId: string, ready: boolean, current: number) {
  const seen = useRef(new Set<number>());
  const state = useRef<{ ticket: string; minSeconds: number; minPages: number; active: number; sent: boolean } | null>(null);

  useEffect(() => {
    seen.current.add(current);
  }, [current]);

  useEffect(() => {
    if (!ready) return;
    let cancelled = false;
    seen.current = new Set([0]);
    state.current = null;
    humanToken()
      .then((token) => publicApi.viewTicket(chapterId, token))
      .then((t) => {
        if (!cancelled) state.current = { ...t, active: 0, sent: false };
      })
      .catch(() => undefined);

    const tick = setInterval(() => {
      const s = state.current;
      if (!s || s.sent || document.visibilityState !== 'visible') return;
      s.active += 1;
      if (s.active >= s.minSeconds + 1 && seen.current.size >= s.minPages) {
        s.sent = true;
        publicApi.view(chapterId, { ticket: s.ticket, pagesSeen: seen.current.size, activeSeconds: s.active }).catch(() => undefined);
      }
    }, 1000);
    return () => {
      cancelled = true;
      clearInterval(tick);
    };
  }, [chapterId, ready]);
}

// ---------------- Optional Cloudflare Turnstile (PUBLIC_TURNSTILE_SITE_KEY) ----------------
interface Turnstile {
  render: (el: HTMLElement, options: Record<string, unknown>) => string;
  remove: (id: string) => void;
}

let turnstileScript: Promise<Turnstile> | null = null;

function loadTurnstile(): Promise<Turnstile> {
  turnstileScript ??= new Promise((resolve, reject) => {
    const script = document.createElement('script');
    script.src = 'https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit';
    script.async = true;
    script.onload = () => {
      const t = (window as unknown as { turnstile?: Turnstile }).turnstile;
      if (t) resolve(t);
      else reject(new Error('turnstile'));
    };
    script.onerror = () => reject(new Error('turnstile'));
    document.head.appendChild(script);
  });
  return turnstileScript;
}

/** Invisible human check; undefined when Turnstile is not configured. */
async function humanToken(): Promise<string | undefined> {
  if (!env.turnstileSiteKey) return undefined;
  const turnstile = await loadTurnstile();
  return new Promise((resolve, reject) => {
    const host = document.createElement('div');
    host.style.display = 'none';
    document.body.appendChild(host);
    const done = (fn: () => void) => {
      fn();
      setTimeout(() => {
        turnstile.remove(id);
        host.remove();
      }, 0);
    };
    const id = turnstile.render(host, {
      sitekey: env.turnstileSiteKey,
      appearance: 'interaction-only',
      callback: (token: string) => done(() => resolve(token)),
      'error-callback': () => done(() => reject(new Error('turnstile'))),
    });
  });
}
