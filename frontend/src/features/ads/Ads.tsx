import { useEffect, useId, useMemo, useRef, useState } from 'react';
import { useLocation } from 'react-router';
import { useQuery } from '@tanstack/react-query';
import { X } from 'lucide-react';
import { publicApi } from '@/services/api';
import type { AdPage, AdSlot as AdSlotName, AdUnit } from '@/types/api';
import { cn } from '@/utils/format';

/*
 * Advertising runtime. Ads are configured by the SystemAdmin (/quan-tri/quang-cao) and delivered with /api/site.
 * They render on every public page and in the reader — for every visitor, signed in or not, house members included.
 * The house workspace (/quan-ly-nhom) and the admin console (/quan-tri) are ad-free: nothing here is mounted
 * there, and a full reload is forced when entering them after network scripts were injected (see useAdFreeZone).
 */

const AD_FREE_PREFIXES = ['/quan-ly-nhom', '/quan-tri'];

export const isAdFreePath = (pathname: string) => AD_FREE_PREFIXES.some((p) => pathname === p || pathname.startsWith(`${p}/`));

/** Which page group a public URL belongs to (targets popup / new-tab / script ads). */
export function pageGroupOf(pathname: string): AdPage {
  if (pathname === '/') return 'home';
  if (pathname.startsWith('/doc/')) return 'reader';
  if (/^\/truyen\/[^/]+/.test(pathname)) return 'comic';
  if (pathname === '/truyen' || pathname.startsWith('/tim-kiem') || pathname.startsWith('/the-loai/')) return 'browse';
  if (pathname.startsWith('/nhom-dich')) return 'house';
  if (pathname.startsWith('/tu-truyen')) return 'library';
  return 'other';
}

/** Clicks that count as "content" for new-tab ads: story links, chapter links and marked action buttons. */
export const AD_TRIGGER_SELECTOR = 'a[href^="/truyen/"], a[href^="/doc/"], [data-ad-trigger]';

const targets = (unit: AdUnit, group: AdPage) => !unit.pages || unit.pages.length === 0 || unit.pages.includes(group);

function useAdUnits(): AdUnit[] {
  const site = useQuery({ queryKey: ['site'], queryFn: publicApi.site, staleTime: 60_000 });
  return useMemo(() => (site.data?.ads?.enabled ? site.data.ads.units.filter((u) => u.enabled) : []), [site.data]);
}

// ---------------- Frequency capping (per browser; storage may be unavailable) ----------------
const seenKey = (id: string) => `rn_ad_seen_${id}`;

function lastSeen(id: string): number {
  try {
    return Number(localStorage.getItem(seenKey(id)) ?? 0);
  } catch {
    return 0;
  }
}

function markSeen(id: string) {
  try {
    localStorage.setItem(seenKey(id), String(Date.now()));
  } catch {
    // private mode: the cap then only holds for this page load
  }
}

const sessionShown = new Set<string>();

/** frequencyMinutes > 0: at most once per that many minutes; 0/empty: once per page view. */
function isDue(unit: AdUnit, viewKey: string): boolean {
  const minutes = unit.frequencyMinutes ?? 0;
  if (minutes > 0) return Date.now() - lastSeen(unit.id) >= minutes * 60_000;
  return !sessionShown.has(`${unit.id}|${viewKey}`);
}

function recordShown(unit: AdUnit, viewKey: string) {
  sessionShown.add(`${unit.id}|${viewKey}`);
  markSeen(unit.id);
}

// ---------------- Creatives ----------------

/** Network HTML runs in a sandboxed iframe (opaque origin: it cannot read the page or the session). */
function HtmlCreative({ html, title, minHeight = 90 }: { html: string; title: string; minHeight?: number }) {
  const frame = useRef<HTMLIFrameElement>(null);
  const [height, setHeight] = useState(minHeight);
  const id = useId();
  useEffect(() => {
    const onMessage = (e: MessageEvent) => {
      if (e.source !== frame.current?.contentWindow) return;
      const data = e.data as { rnAd?: string; h?: number };
      if (data?.rnAd === id && typeof data.h === 'number') setHeight(Math.min(Math.max(data.h, 30), 1200));
    };
    window.addEventListener('message', onMessage);
    return () => window.removeEventListener('message', onMessage);
  }, [id]);
  const doc = `<!doctype html><html><head><meta charset="utf-8"><base target="_blank"><style>html,body{margin:0;padding:0;display:flex;justify-content:center;background:transparent}</style></head><body>${html}<script>(function(){var s=function(){parent.postMessage({rnAd:${JSON.stringify(id)},h:document.documentElement.scrollHeight},'*')};new ResizeObserver(s).observe(document.documentElement);addEventListener('load',s);s();})();</script></body></html>`;
  return (
    <iframe
      ref={frame}
      title={title}
      srcDoc={doc}
      sandbox="allow-scripts allow-popups allow-popups-to-escape-sandbox allow-forms"
      referrerPolicy="no-referrer-when-downgrade"
      loading="lazy"
      className="block w-full border-0"
      style={{ height }}
    />
  );
}

function Creative({ unit, onClick }: { unit: AdUnit; onClick?: () => void }) {
  if (unit.imageUrl && unit.targetUrl) {
    return (
      <a href={unit.targetUrl} target="_blank" rel="sponsored noopener noreferrer" onClick={onClick} className="block">
        <img src={unit.imageUrl} alt={unit.name} className="mx-auto block h-auto max-h-[70dvh] w-auto max-w-full" loading="lazy" decoding="async" />
      </a>
    );
  }
  return unit.html ? <HtmlCreative html={unit.html} title={unit.name} /> : null;
}

const AdLabel = () => <span className="text-[10px] font-medium uppercase tracking-wider text-muted">Quảng cáo</span>;

// ---------------- Inline banners ----------------

/** Banner position. Several banners in one slot rotate (one random pick per mount). */
export function AdSlot({ slot, className }: { slot: AdSlotName; className?: string }) {
  const units = useAdUnits().filter((u) => u.format === 'banner' && u.slots?.includes(slot));
  const [pick] = useState(() => Math.random());
  if (units.length === 0) return null;
  const unit = units[Math.floor(pick * units.length) % units.length]!;
  return (
    <aside className={cn('rounded-2xl border border-line bg-surface p-2 text-center', className)} aria-label="Quảng cáo" data-ad="" data-testid={`ad-slot-${slot}`}>
      <AdLabel />
      <Creative unit={unit} />
    </aside>
  );
}

// ---------------- Pop-ups, new-tab ads, network scripts ----------------

declare global {
  interface Window {
    __rnAdScripts?: boolean;
  }
}

/** Ad-network code is injected into the page itself (popunder, social bar, auto ads need the real document). */
function injectScriptUnit(unit: AdUnit) {
  if (!unit.html || document.querySelector(`[data-rn-ad="${CSS.escape(unit.id)}"]`)) return;
  const host = document.createElement('div');
  host.dataset.rnAd = unit.id;
  const template = document.createElement('template');
  template.innerHTML = unit.html;
  for (const node of Array.from(template.content.childNodes)) {
    if (node instanceof HTMLScriptElement) {
      // Scripts parsed via innerHTML never run; re-create them so the browser executes them.
      const script = document.createElement('script');
      for (const attr of Array.from(node.attributes)) script.setAttribute(attr.name, attr.value);
      script.text = node.text;
      host.appendChild(script);
    } else {
      host.appendChild(node.cloneNode(true));
    }
  }
  document.body.appendChild(host);
  window.__rnAdScripts = true;
}

/**
 * Mounted once per public page shell (public layout and reader). Shows at most one pop-up at a time,
 * opens new-tab ads on the visitor's first click (browsers block it otherwise) and injects network scripts.
 */
export function AdsRuntime() {
  const units = useAdUnits();
  const { pathname } = useLocation();
  const group = pageGroupOf(pathname);
  const [popup, setPopup] = useState<AdUnit | null>(null);

  // Network scripts: once per page load.
  useEffect(() => {
    if (isAdFreePath(pathname)) return;
    units.filter((u) => u.format === 'script' && targets(u, group)).forEach(injectScriptUnit);
  }, [units, group, pathname]);

  // Pop-up: first eligible unit for this page, after its delay.
  useEffect(() => {
    if (isAdFreePath(pathname)) return;
    const unit = units.find((u) => u.format === 'popup' && targets(u, group) && isDue(u, pathname));
    if (!unit) return;
    const id = setTimeout(() => {
      recordShown(unit, pathname);
      setPopup(unit);
    }, Math.max(0, unit.delaySeconds ?? 0) * 1000);
    return () => clearTimeout(id);
  }, [units, group, pathname]);

  // Google AdSense on public pages: present in index.html; loaded here after arriving from an ad-free shell.
  useEffect(() => {
    if (!isAdFreePath(pathname)) ensureAdsense();
  }, [pathname]);

  // New-tab ads. The clicked link or button keeps working normally in this tab; the ad opens in a separate tab
  // the reader can close. Trigger "any": the next click anywhere. Trigger "content": clicks on a story, a chapter
  // or a marked action button. Each unit's frequency cap applies (0 = once per page view).
  useEffect(() => {
    if (isAdFreePath(pathname)) return;
    const candidates = units.filter((u) => u.format === 'newtab' && u.targetUrl && targets(u, group));
    if (candidates.length === 0) return;
    const shownAt = Date.now();
    const onClick = (e: MouseEvent) => {
      const target = e.target instanceof Element ? e.target : null;
      if (!target || target.closest('[data-ad]')) return;
      const onContent = target.closest(AD_TRIGGER_SELECTOR) !== null;
      const unit = candidates.find((u) =>
        (u.trigger !== 'content' || onContent)
        && Date.now() - shownAt >= Math.max(0, u.delaySeconds ?? 0) * 1000
        && isDue(u, pathname));
      if (!unit) return;
      recordShown(unit, pathname);
      window.open(unit.targetUrl!, '_blank', 'noopener');
    };
    document.addEventListener('click', onClick, true);
    return () => document.removeEventListener('click', onClick, true);
  }, [units, group, pathname]);

  return popup ? <AdPopup unit={popup} onClose={() => setPopup(null)} /> : null;
}

/** Overlay panel; with closeMode "countdown" the close button only appears after the ad was visible long enough. */
function AdPopup({ unit, onClose }: { unit: AdUnit; onClose: () => void }) {
  const forced = unit.closeMode === 'countdown' ? Math.max(1, unit.countdownSeconds ?? 5) : 0;
  const [left, setLeft] = useState(forced);
  const canClose = left <= 0;

  useEffect(() => {
    if (left <= 0) return;
    // The countdown only runs while the tab is visible ("must watch").
    const id = setInterval(() => document.visibilityState === 'visible' && setLeft((s) => s - 1), 1000);
    return () => clearInterval(id);
  }, [left]);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && canClose && onClose();
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [canClose, onClose]);

  return (
    <div className="fixed inset-0 z-[60] flex items-center justify-center bg-black/60 p-4 backdrop-blur-[2px]" role="dialog" aria-modal="true" aria-label="Quảng cáo" data-ad="" data-testid="ad-popup">
      <div className="relative w-full max-w-lg rounded-2xl bg-surface p-3 shadow-xl">
        <div className="mb-2 flex items-center justify-between gap-2 px-1">
          <AdLabel />
          {canClose ? (
            <button type="button" onClick={onClose} className="grid h-8 w-8 place-items-center rounded-full bg-surface-2 text-ink hover:bg-surface-3" aria-label="Đóng quảng cáo" data-testid="ad-popup-close">
              <X className="h-4 w-4" />
            </button>
          ) : (
            <span className="rounded-full bg-surface-2 px-3 py-1 text-xs font-medium text-muted" aria-live="polite">Đóng sau {left}s</span>
          )}
        </div>
        <div className="max-h-[75dvh] overflow-auto rounded-xl">
          <Creative unit={unit} />
        </div>
      </div>
    </div>
  );
}

declare const __ADSENSE_CLIENT__: string | undefined;

/** Google AdSense publisher id baked in at build time (empty = disabled). */
const adsenseClient = typeof __ADSENSE_CLIENT__ === 'string' ? __ADSENSE_CLIENT__ : '';
const ADSENSE_SRC = 'https://pagead2.googlesyndication.com/pagead/js/adsbygoogle.js';

function ensureAdsense() {
  if (!adsenseClient || document.querySelector(`script[src^="${ADSENSE_SRC}"]`)) return;
  const script = document.createElement('script');
  script.async = true;
  script.crossOrigin = 'anonymous';
  script.src = `${ADSENSE_SRC}?client=${adsenseClient}`;
  document.head.appendChild(script);
}

/** Whether this page load started in an ad-free area (then it was served the ad-free shell; never reload). */
const loadedOnAdFreePath = typeof window !== 'undefined' && isAdFreePath(window.location.pathname);

/**
 * Called by the workspace/admin shells: if ad-network scripts or AdSense were loaded earlier in this page load
 * (SPA navigation from a public page), reload so the ad-free shell (workspace.html) is served instead. A load that
 * started on an ad-free path never reloads, so hosts that ignore _redirects cannot loop.
 */
export function useAdFreeZone() {
  useEffect(() => {
    if (loadedOnAdFreePath) return;
    if (window.__rnAdScripts || document.querySelector(`script[src^="${ADSENSE_SRC}"]`)) window.location.reload();
  }, []);
}
