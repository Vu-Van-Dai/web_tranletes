import { useEffect, useLayoutEffect, useId, useMemo, useRef, useState } from 'react';
import { Outlet, useLocation } from 'react-router';
import { useQuery } from '@tanstack/react-query';
import { X } from 'lucide-react';
import { publicApi } from '@/services/api';
import type { AdPage, AdSlot as AdSlotName, AdUnit } from '@/types/api';
import { isAllowedAdUnit, isDirectBanner, requiresDirectScript } from './providers';
import { hasVisibleCreative, loadedDirectBanners, mountDirectBanner } from './directCreative';
import { useAuth } from '@/features/auth/AuthProvider';
import { dimensions, fitsSlot, standaloneLink, noteAd, adKind, singleNetworkScript, adsterraScriptPosition, planBanners, chooseNetworkUnit, eligibleOnClickLink, isPublicContentClick, ONCLICK_MINUTES } from './placement';
import { comicRoute, chapterRoute } from '@/app/paths';
import { cn } from '@/utils/format';

/*
 * Advertising runtime. Ads are configured by the SystemAdmin (/quan-tri/quang-cao) and delivered with /api/site.
 * Eligible public catalog and non-mature reader pages may render ads for visitors, including signed-in users.
 * The house workspace (/quan-ly-nhom) and the admin console (/quan-tri) are ad-free: nothing here is mounted
 * there, and a full reload is forced when entering them after network scripts were injected (see useAdFreeZone).
 */

// '/workspace' is where an earlier deployment's redirect sent visitors (still cached by some browsers).
const AD_FREE_PREFIXES = ['/quan-ly-nhom', '/quan-tri', '/workspace', '/dang-nhap', '/dang-ky', '/doi-mat-khau', '/tu-truyen', '/gioi-thieu', '/lien-he', '/chinh-sach-bao-mat', '/dieu-khoan-su-dung', '/faq'];

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

function useAdEligibility() {
  const { pathname } = useLocation();
  const group = pageGroupOf(pathname);
  const [, , id = '', slug] = pathname.split('/');
  const friendlyComic = group === 'comic' ? comicRoute(id, slug) : null;
  const friendlyChapter = group === 'reader' ? chapterRoute(pathname) : null;
  const comic = useQuery({ queryKey: friendlyComic ? ['comic-slug', friendlyComic.house, friendlyComic.slug] : ['comic', id], queryFn: () => friendlyComic ? publicApi.comicBySlug(friendlyComic.house, friendlyComic.slug) : publicApi.comic(id), enabled: group === 'comic' && !!id });
  const chapter = useQuery({ queryKey: friendlyChapter ? ['chapter-slug', friendlyChapter.house, friendlyChapter.slug, friendlyChapter.number] : ['ad-chapter', id], queryFn: () => friendlyChapter ? publicApi.chapterBySlug(friendlyChapter.house, friendlyChapter.slug, friendlyChapter.number) : publicApi.chapter(id), enabled: group === 'reader' && !!id, staleTime: 30_000 });
  // Fail closed while age classification is loading or cannot be retrieved.
  const allowed = !isAdFreePath(pathname) && group !== 'other' &&
    (group !== 'comic' || (comic.isSuccess && comic.data.isMature === false)) &&
    (group !== 'reader' || (chapter.isSuccess && chapter.data.isMature === false));
  const site = useQuery({ queryKey: ['site'], queryFn: publicApi.site, staleTime: 60_000, refetchInterval: 60_000 });
  return { enabled: allowed && site.isSuccess && site.data?.ads?.enabled === true, group, site };
}
function useAdUnits(): AdUnit[] {
  const { enabled, group, site } = useAdEligibility();
  const { status } = useAuth();
  // Do not mix third-party placements with AdSense auto ads in the same deployment.
  return useMemo(() => (enabled && !adsenseClient ? (site.data?.ads?.units ?? []).filter(u => site.data && isAllowedAdUnit(site.data.ads, u) && targets(u, group) && (!requiresDirectScript(u) || status === 'anonymous')) : []), [enabled, group, site.data, status]);
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

/** A real navigated document has its own CSP, unlike srcDoc which inherits the parent CSP. */
function HtmlCreative({ html, title, minHeight = 90, onState }: { html: string; title: string; minHeight?: number; onState?: (visible:boolean)=>void }) {
  const frame = useRef<HTMLIFrameElement>(null);
  const sent = useRef(false);
  const [height, setHeight] = useState(minHeight);
  const id = useId().replace(/[^a-zA-Z0-9_-]/g, '_');
  const visible = useRef(false);
  const timer = useRef<number | undefined>(undefined);
  const stateCallback = useRef(onState);
  stateCallback.current = onState;
  useEffect(()=>()=>clearTimeout(timer.current),[]);
  useLayoutEffect(() => {
    const onMessage = (e: MessageEvent) => {
      if (e.origin !== 'null' || e.source !== frame.current?.contentWindow) return;
      const data = e.data as { type?: string; id?: string; h?: number; visible?: boolean } | null;
      if (data?.type === 'rn-ad-size' && data.id === id && typeof data.h === 'number' && Number.isFinite(data.h)) {
        setHeight(Math.min(Math.max(Math.ceil(data.h), 30), 1200));
        if(data.visible===true&&!visible.current){visible.current=true;stateCallback.current?.(true);}
      }
    };
    window.addEventListener('message', onMessage);
    return () => window.removeEventListener('message', onMessage);
  }, [id]);
  return (
    <iframe
      ref={frame}
      title={title}
      src="/ad-frame"
      sandbox="allow-scripts allow-popups allow-popups-to-escape-sandbox allow-forms"
      referrerPolicy="strict-origin-when-cross-origin"
      loading="lazy"
      onLoad={() => {
        if (sent.current || !frame.current?.contentWindow) return;
        sent.current = true;
        timer.current=window.setTimeout(()=>stateCallback.current?.(visible.current),12000);
        // The sandbox's origin is opaque, so a literal target origin cannot be used.
        frame.current.contentWindow.postMessage({ type: 'rn-ad-init', id, html }, '*');
      }}
      className="block w-full border-0"
      style={{ height }}
    />
  );
}

/** Rotate only on an explicitly labelled sponsored link. A navigation click
 * can still use the separately disclosed HilltopAds flow. */
function SponsoredLink({unit, href, label}: {unit: AdUnit; href: string; label: string}) {
  const units = useAdUnits();
  const { pathname } = useLocation();
  const { status } = useAuth();
  const [availableAt, setAvailableAt] = useState(() => sponsoredDeadline());
  useEffect(() => {
    if (!availableAt) return;
    const timer = window.setTimeout(() => setAvailableAt(0), Math.max(0, availableAt - Date.now()));
    return () => clearTimeout(timer);
  }, [availableAt]);
  const open = () => {
    if (!networkDue() || !onClickDue()) { setAvailableAt(sponsoredDeadline()); return; }
    const alternatives = status === 'anonymous' ? units.filter(eligibleOnClickLink) : [];
    const smartlink = {...unit, frequencyMinutes: ONCLICK_MINUTES};
    const due = [smartlink, ...alternatives].filter(u => isDue(u, pathname));
    const candidate = due.sort((a,b) => lastSeen(a.id) - lastSeen(b.id))[0];
    if (!candidate) return;
    recordShown(candidate, pathname);
    markOnClick();
    setAvailableAt(sponsoredDeadline());
    window.open(candidate.id === unit.id ? href : candidate.targetUrl!, '_blank', 'noopener,noreferrer');
  };
  return <button type="button" disabled={availableAt > Date.now()} onClick={open}
    className="mx-auto my-2 inline-flex rounded-lg border border-line px-4 py-2 text-sm font-medium text-accent disabled:opacity-50">
    {availableAt > Date.now() ? 'Vui lòng chờ trước khi mở quảng cáo tiếp theo' : label}
  </button>;
}

function Creative({ unit, onClick, onState }: { unit: AdUnit; onClick?: () => void; onState?: (visible:boolean)=>void }) {
  if (unit.imageUrl && unit.targetUrl) {
    return (
      <a href={unit.targetUrl} target="_blank" rel="sponsored noopener noreferrer" onClick={onClick} className="block">
        <img src={unit.imageUrl} alt={unit.name} className="mx-auto block h-auto max-h-[70dvh] w-auto max-w-full" loading="lazy" decoding="async" onLoad={()=>onState?.(true)} onError={()=>onState?.(false)} />
      </a>
    );
  }
  const link=standaloneLink(unit.html);
  if(link && adKind(unit)==='smartlink')return <SponsoredLink unit={unit} href={link.href} label="Mở quảng cáo trong tab mới" />;
  if(link)return <a href={link.href} target="_blank" rel="sponsored noopener noreferrer" onClick={onClick} className="mx-auto my-2 inline-flex rounded-lg border border-line px-4 py-2 text-sm font-medium text-accent hover:underline">{link.label}</a>;
  return unit.html ? <HtmlCreative key={unit.id+':'+unit.html} html={unit.html} title={unit.name} minHeight={dimensions(unit)?.height??90} onState={onState}/> : null;
}

const AdLabel = () => <span className="text-[10px] font-medium uppercase tracking-wider text-muted">Quảng cáo</span>;

// ---------------- Inline banners ----------------

/** Approved direct banner: the provider runs in the actual public document.
 * It may create its own iframe. Never use this renderer in a signed-in session.
 */
function DirectBanner({ unit, slot, className, onFailure }: {unit: AdUnit; slot: AdSlotName; className?: string; onFailure?:()=>void}) {
  const host = useRef<HTMLDivElement>(null);
  const [empty, setEmpty] = useState(false);
  const { pathname } = useLocation();
  const { status } = useAuth();
  useEffect(() => {
    const el = host.current;
    if (!el || !unit.html || status !== 'anonymous') return;
    const controller = new AbortController();
    let noFill: number | undefined;
    // Defer execution past React StrictMode's trial mount.
    const start = window.setTimeout(async () => {
      if (controller.signal.aborted || (window.__rnAdPath && window.__rnAdPath !== pathname)) return;
      noteAd(unit.id,slot,'loading');
      const failure: {reason:import('./directCreative').DirectFailure} = {reason:'script-error'};
      const ok = await mountDirectBanner(el, unit.id, unit.html!, controller.signal, () => {
        window.__rnAdScripts = true;
        window.__rnAdPath = pathname;
      }, reason => {failure.reason=reason;}, unit.provider);
      if (controller.signal.aborted) return;
      if (!ok) { noteAd(unit.id,slot,failure.reason==='cancelled'?'script-error':failure.reason); setEmpty(true);onFailure?.();return; }
      // Cannot inspect cross-origin iframe contents or assert a counted impression.
      noFill = window.setTimeout(() => {
        if(controller.signal.aborted)return;
        const visible=hasVisibleCreative(el);noteAd(unit.id,slot,visible?'element':'no-element');if(!visible){setEmpty(true);onFailure?.();}
      }, 10000);
    }, 0);
    return () => {
      controller.abort();
      clearTimeout(start);
      if (noFill !== undefined) clearTimeout(noFill);
    };
  }, [unit.id, unit.html, pathname, status, slot]);
  if (empty || status !== 'anonymous') return null;
  return (
    <aside className={cn('rounded-2xl border border-line bg-surface p-2 text-center', className)} aria-label="Quảng cáo" data-ad="" data-testid={'ad-slot-' + slot}>
      <AdLabel />
      <div ref={host} className="mx-auto w-full overflow-hidden" style={{maxWidth:dimensions(unit)?.width}} />
    </aside>
  );
}

function IsolatedBanner({unit,slot,onFailure}:{unit:AdUnit;slot:AdSlotName;onFailure?:()=>void}){
  const [empty,setEmpty]=useState(false);
  if(empty)return null;
  return <aside className="rounded-2xl border border-line bg-surface p-2 text-center" aria-label="Quảng cáo" data-ad="" data-testid={'ad-slot-'+slot}>
    <AdLabel/><div className="mx-auto w-full overflow-hidden" style={{maxWidth:dimensions(unit)?.width}}><Creative unit={unit} onState={visible=>{noteAd(unit.id,slot,visible?'element':'no-element');if(!visible){setEmpty(true);onFailure?.();}}}/></div>
  </aside>;
}
// All slot components share the same seed and deterministic allocation per document.
const bannerPlanSeed=Math.random();
/** A distinct creative per slot, with a reserved independent fallback. */
export function AdSlot({slot,className}:{slot:AdSlotName;className?:string}){
  const units=useAdUnits();
  const {pathname}=useLocation();
  const group=pageGroupOf(pathname);
  const host=useRef<HTMLDivElement>(null);
  const [size,setSize]=useState({width:0,viewport:0});
  const [failed,setFailed]=useState<string[]>([]);
  useEffect(()=>setFailed([]),[pathname]);
  useLayoutEffect(()=>{
    const el=host.current;if(!el)return;
    const measure=()=>{const style=getComputedStyle(el);const next={width:Math.max(0,el.clientWidth-parseFloat(style.paddingLeft||'0')-parseFloat(style.paddingRight||'0')-18),viewport:window.innerWidth};setSize(old=>old.width===next.width&&old.viewport===next.viewport?old:next);};
    measure();const observer=new ResizeObserver(measure);observer.observe(el);window.addEventListener('resize',measure);
    return ()=>{observer.disconnect();window.removeEventListener('resize',measure);};
  },[]);
  const plan=useMemo(()=>planBanners(units,group,size.viewport,bannerPlanSeed),[units,group,size.viewport]);
  const candidates=(plan[slot]??[]).filter(u=>fitsSlot(u,slot,size.width,size.viewport));
  const identity=(u:AdUnit)=>u.id+':'+u.html;
  const unit=candidates.find(u=>!failed.includes(identity(u)));
  const fail=()=>{if(unit)setFailed(old=>old.includes(identity(unit))?old:[...old,identity(unit)]);};
  const primary=plan[slot]?.[0];
  useEffect(()=>{if(unit&&primary&&unit.id!==primary.id)noteAd(unit.id,slot,'fallback');},[primary,unit,slot]);
  return <div ref={host} data-ad-slot-wrapper="" className={cn('w-full min-w-0',className)}>
    {unit&&(isDirectBanner(unit)?<DirectBanner key={unit.id+':'+unit.html} unit={unit} slot={slot} onFailure={fail}/>: (
      <IsolatedBanner key={unit.id+':'+unit.html} unit={unit} slot={slot} onFailure={fail}/>))}
  </div>;
}

// ---------------- Pop-ups, new-tab ads, network scripts ----------------

declare global {
  interface Window {
    __rnAdScripts?: boolean;
    __rnAdPath?: string;
  }
}

/** Ad-network code is injected into the page itself (popunder, social bar, auto ads need the real document). */
function injectScriptUnit(unit: AdUnit):boolean {
  if (!unit.scriptApproved || (window.__rnAdPath && window.__rnAdPath !== window.location.pathname)) return false;
  if (!unit.html || document.querySelector(`[data-rn-ad="${CSS.escape(unit.id)}"]`)) return false;
  // Adsterra supplies Popunder for the end of HEAD, Social Bar for the end of BODY.
  // React loads these dynamically after eligibility/approval, not during HTML parsing.
  const kind=adKind(unit);
  if(unit.provider==='adsterra'&&(kind==='popunder'||kind==='socialbar')){
    const original=singleNetworkScript(unit.html);
    const position=adsterraScriptPosition(unit);
    if(!original||!position){noteAd(unit.id,'network','invalid-tag');return false;}
    const script=document.createElement('script');
    for(const attr of original.attributes)script.setAttribute(attr.name,attr.value);
    script.async=original.hasAttribute('async');
    script.dataset.rnAd=unit.id;
    script.dataset.rnAdContent=unit.html;
    script.addEventListener('load',()=>noteAd(unit.id,'network','injected'),{once:true});
    script.addEventListener('error',()=>noteAd(unit.id,'network','script-error'),{once:true});
    window.__rnAdScripts=true;
    window.__rnAdPath=window.location.pathname;
    noteAd(unit.id,'network','loading');
    (position==='head'?document.head:document.body).appendChild(script);
    return true;
  }
  // HilltopAds and Clickadu tags remain unmodified. Single external scripts
  // are placed directly at the end of body; compound tags use the existing host.
  const original=singleNetworkScript(unit.html);
  if(original&&(unit.provider==='hilltopads'||unit.provider==='clickadu')){
    const script=document.createElement('script');
    for(const attr of original.attributes)script.setAttribute(attr.name,attr.value);
    script.async=original.hasAttribute('async');
    script.dataset.rnAd=unit.id;script.dataset.rnAdContent=unit.html;
    script.addEventListener('load',()=>noteAd(unit.id,'network','injected'),{once:true});
    script.addEventListener('error',()=>noteAd(unit.id,'network','script-error'),{once:true});
    window.__rnAdScripts=true;window.__rnAdPath=window.location.pathname;
    noteAd(unit.id,'network','loading');document.body.appendChild(script);return true;
  }
  const host = document.createElement('div');
  host.dataset.rnAd = unit.id;
  host.dataset.rnAdContent = unit.html;
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
  window.__rnAdPath = window.location.pathname;
  noteAd(unit.id,'network','injected');
  return true;
}

const NETWORK_UNTIL='rn_ad_network_until';
const ONCLICK_LAST='rn_ad_onclick_last_v2';
let lastOnClick=0;
function onClickDeadline(){try{return Math.max(lastOnClick,Number(localStorage.getItem(ONCLICK_LAST))||0)+ONCLICK_MINUTES*60_000;}catch{return lastOnClick+ONCLICK_MINUTES*60_000;}}
function onClickDue(){return Date.now()>=onClickDeadline();}
function markOnClick(){lastOnClick=Date.now();try{localStorage.setItem(ONCLICK_LAST,String(lastOnClick));}catch{/* optional */}}
let networkUntil=0;
function networkDeadline(){try{return Math.max(networkUntil,Number(localStorage.getItem(NETWORK_UNTIL))||0);}catch{return networkUntil;}}
function networkDue(){return Date.now()>=networkDeadline();}
function sponsoredDeadline(){return Math.max(networkDeadline(),onClickDeadline());}
function recordNetwork(unit:AdUnit){networkUntil=Date.now()+Math.max(1,unit.frequencyMinutes??30)*60_000;try{localStorage.setItem(NETWORK_UNTIL,String(networkUntil));}catch{/* optional */}}

/**
 * Mounted once per public page shell (public layout and reader). Shows at most one pop-up at a time,
 * opens new-tab ads on the visitor's first click (browsers block it otherwise) and injects network scripts.
 */
export function AdNavigationBoundary() {
  const { pathname } = useLocation();
  const { status } = useAuth();
  useLayoutEffect(() => {
    if ((window.__rnAdPath && window.__rnAdPath !== pathname) || (window.__rnAdScripts && status !== 'anonymous')) window.location.reload();
  }, [pathname, status]);
  return <Outlet />;
}

export function AdsRuntime() {
  const units = useAdUnits();
  const { pathname } = useLocation();
  const group = pageGroupOf(pathname);
  const { enabled, site } = useAdEligibility();
  const [networkPick]=useState(()=>Math.random());
  const {status}=useAuth();
  const [onClickOff,setOnClickOff]=useState(()=>{try{return sessionStorage.getItem('rn_onclick_consent')==='no';}catch{return false;}});
  const turnOffOnClick=()=>{setOnClickOff(true);try{sessionStorage.setItem('rn_onclick_consent','no');}catch{/* optional */}};
  const onClickAvailable=status==='anonymous'&&units.some(u=>eligibleOnClickLink(u)||(u.format==='script'&&adKind(u)==='popunder'));
  // Choose one delivery mode for a document. Never stack popup networks.
  const selected=useRef<{path:string;id:string}|null>(null);
  const choices=units.filter(u=>u.format==='script'||eligibleOnClickLink(u));
  // Opting out excludes all network scripts (MultiTag can also open popups).
  const allowedChoices=onClickOff?[]:choices;
  const previous=selected.current?.path===pathname?allowedChoices.find(u=>u.id===selected.current?.id):undefined;
  const delivery=previous??chooseNetworkUnit(allowedChoices.filter(u=>eligibleOnClickLink(u)||
    (networkDue()&&isDue(u,pathname)&&(adKind(u)!=='popunder'||onClickDue()))),lastSeen,networkPick);
  if(delivery)selected.current={path:pathname,id:delivery.id};
  const directConfigured=!!delivery&&eligibleOnClickLink(delivery);
  const [popup, setPopup] = useState<{ unit: AdUnit; path: string } | null>(null);
  const adsenseAllowed = enabled && group === 'comic' && !!adsenseClient && site.data?.ads.providerEnabled?.adsense !== false;

  // Network scripts: once per page load.
  useEffect(() => {
    // Scripts can register listeners that removing their tag cannot undo. Reset the document when
    // leaving the route where they ran, or when the provider/global switch is turned off.
    const injected = Array.from(document.querySelectorAll<HTMLElement>('[data-rn-ad]'));
    const hasAdsense = !!document.querySelector(`script[src^="${ADSENSE_SRC}"]`);
    if ((window.__rnAdPath && window.__rnAdPath !== pathname) ||
        (injected.length>0 && (onClickOff || !delivery || injected.some(el=>el.dataset.rnAd!==delivery.id))) ||
        (window.__rnAdScripts && (!enabled || injected.some(el => !units.some(u => u.id === el.dataset.rnAd && u.format === 'script' && u.html === el.dataset.rnAdContent)) || loadedDirectBanners().some(([id, html]) => !units.some(u => u.id === id && isDirectBanner(u) && u.html === html)))) ||
        (hasAdsense && !adsenseAllowed)) {
      window.location.reload();
      return;
    }
    if (!enabled || onClickOff || !delivery || delivery.format!=='script') return;
    // Cap injection, not the vendor's actual popup count or paid impressions.
    if(injected.length||!networkDue())return;
    const candidate=delivery;
    if(!isDue(candidate,pathname)||(adKind(candidate)==='popunder'&&!onClickDue()))return;
    const timer=window.setTimeout(()=>{
      if(document.querySelector('[data-rn-ad]')||!isDue(candidate,pathname)||!networkDue()||(adKind(candidate)==='popunder'&&!onClickDue()))return;
      if(injectScriptUnit(candidate)){recordShown(candidate,pathname);recordNetwork(candidate);if(adKind(candidate)==='popunder')markOnClick();}
    },Math.max(0,candidate.delaySeconds??0)*1000);
    return ()=>clearTimeout(timer);
  }, [units, pathname, enabled, adsenseAllowed, delivery,onClickOff]);

  // Pop-up: first eligible unit for this page, after its delay.
  useEffect(() => {
    if (isAdFreePath(pathname) || onClickOff || delivery) return;
    const unit = units.find((u) => u.format === 'popup' && targets(u, group) && isDue(u, pathname));
    if (!unit) return;
    const id = setTimeout(() => {
      recordShown(unit, pathname);
      setPopup({ unit, path: pathname });
    }, Math.max(0, unit.delaySeconds ?? 0) * 1000);
    return () => clearTimeout(id);
  }, [units, group, pathname,delivery,onClickOff]);

  // Only explicitly enabled Auto ads on classified, non-mature comic pages.
  useEffect(() => {
    if (adsenseAllowed && (!window.__rnAdPath || window.__rnAdPath === pathname)) ensureAdsense();
  }, [adsenseAllowed, pathname]);

  // Default-on with visible notice and opt-out; provider-supported URL only.
  useEffect(()=>{
    if(!onClickAvailable||onClickOff||!directConfigured||!delivery||isAdFreePath(pathname))return;
    const candidate=delivery;
    const shownAt=Date.now();
    const onClick=(e:MouseEvent)=>{
      if(!e.isTrusted||e.defaultPrevented||e.button!==0||e.ctrlKey||e.metaKey||e.shiftKey||e.altKey||!onClickDue())return;
      if(!(e.target instanceof Element)||!isPublicContentClick(e.target))return;
      if(document.querySelector('[data-rn-ad]')||Date.now()-shownAt<Math.max(0,candidate.delaySeconds??0)*1000)return;
      // No preventDefault, simulated clicks or retries. Browser decides tab focus.
      markOnClick();
      recordShown(candidate,pathname);
      window.open(candidate.targetUrl!,'_blank','noopener,noreferrer');
    };
    document.addEventListener('click',onClick,true);
    return ()=>document.removeEventListener('click',onClick,true);
  },[pathname,onClickAvailable,onClickOff,directConfigured,delivery]);

  return <>
    {onClickAvailable&&!onClickOff&&<p data-ad="" className="text-xs leading-relaxed text-muted">Khi điều hướng, có thể mở tab quảng cáo (tối đa 1 lần / 2 phút).{' '}<button type="button" onClick={turnOffOnClick} className="underline underline-offset-2">Tắt mở tab</button></p>}
    {popup && popup.path === pathname && units.some(u => u.id === popup.unit.id) && <AdPopup unit={popup.unit} onClose={() => setPopup(null)} />}
  </>;
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
  window.__rnAdPath = window.location.pathname;
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
