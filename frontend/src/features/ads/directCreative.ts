// Direct banner code is a privileged, explicitly approved integration, not an XSS sandbox.
// Keep the provider tag intact; do not emulate cookies, eval, document.write or impressions.
const loaded = new Map<string, string>();
const sources = new Set<string>();
let queue: Promise<unknown> = Promise.resolve();
let syncBannerStarted = false;
export const loadedDirectBanners = () => [...loaded.entries()];
export type DirectFailure = 'invalid-tag' | 'duplicate' | 'sync-limit' | 'script-error' | 'cancelled' | 'library-error' | 'zone-error';

// HilltopAds MultiTag supplies an inline bootstrap which creates its own async
// script. Read the literal URL for duplicate detection, never evaluate the code
// during parsing. This is compatibility detection, NOT a trust/safety validator.
function hilltopBootstrapUrl(script: HTMLScriptElement): string | null {
  const code=script.text;
  if(script.hasAttribute('src')||!/\bd\.createElement\(\s*['"]script['"]\s*\)/.test(code)||
      !/\bs\.async\s*=\s*true\s*;/.test(code)||
      !/\bl\.parentNode\.insertBefore\(\s*s\s*,\s*l\s*\)/.test(code)||
      !/\bd\.currentScript\b/.test(code))return null;
  const literals=[...code.matchAll(/\bs\.src\s*=\s*("(?:\\.|[^"\\])*")\s*;/g)];
  if(literals.length!==1)return null;
  try{
    const raw=JSON.parse(literals[0]![1]!) as string;
    if(!/^(https:)?\/\//i.test(raw))return null;
    const url=new URL(raw,'https://example.invalid');
    // Exact observed HilltopAds CDN; other domains need separate review.
    if(url.protocol!=='https:'||url.hostname!=='same-upstairs.com'||url.username||url.password||url.port||url.pathname==='/')return null;
    return url.href;
  }catch{return null;}
}
export function parseDirectTag(html: string, provider?: string | null) {
  const template = document.createElement('template');
  template.innerHTML = html;
  const scripts = [...template.content.children].filter((el): el is HTMLScriptElement => el instanceof HTMLScriptElement);
  if (!scripts.length || template.content.querySelectorAll('script').length !== scripts.length) throw Error('Expected top-level provider scripts');
  const external = scripts.filter(s => s.hasAttribute('src'));
  if(!external.length&&provider==='hilltopads'&&scripts.length===1&&template.content.children.length===1){
    const url=hilltopBootstrapUrl(scripts[0]!);
    if(url)return {template,scripts,urls:[url],sync:false};
  }
  if (!external.length || external.some(s => !/^(https:)?\/\//i.test(s.getAttribute('src') ?? ''))) throw Error('Expected HTTPS provider script');
  const urls = external.map(s => {
    const url = new URL(s.getAttribute('src')!, 'https://example.invalid');
    if(url.protocol!=='https:' || url.username || url.password) throw Error('Expected HTTPS provider script');
    return url.href;
  });
  if (new Set(urls).size !== urls.length) throw Error('Duplicate provider script');
  const inline = scripts.filter(s => !s.hasAttribute('src'));
  // Recognize independent official-style async option queues. Do not convert IFRAME SYNC
  // into JSAsync or rewrite provider code. Unknown inline configurations stay conservative.
  const queueOptions = inline.length>0 && inline.every(s => /\batAsyncOptions\s*\.push\s*\(/.test(s.text) && !/\batOptions\b/.test(s.text));
  const sync = external.some(s => !s.hasAttribute('async')) || (inline.length>0 && !queueOptions);
  return { template, scripts, urls, sync };
}

export function directTagKind(html: string, provider?: string | null): 'async' | 'sync' | 'invalid' {
  try { return parseDirectTag(html,provider).sync ? 'sync' : 'async'; } catch { return 'invalid'; }
}

/** Mount at most once per unit and script URL in a document. No automatic retries. */
export function mountDirectBanner(host: HTMLElement, id: string, html: string, signal: AbortSignal, beforeRun: () => void, onFailure?: (reason:DirectFailure)=>void, provider?: string | null): Promise<boolean> {
  const run = async () => {
    const fail = (reason:DirectFailure) => { onFailure?.(reason); return false; };
    if (signal.aborted || !host.isConnected) return fail('cancelled');
    if (loaded.has(id)) return fail('duplicate');
    let tag: ReturnType<typeof parseDirectTag>;
    try { tag = parseDirectTag(html,provider); } catch { return fail('invalid-tag'); }
    const ids = [...tag.template.content.querySelectorAll('[id]')].map(el=>el.id);
    if (tag.urls.some(url => sources.has(url)) || new Set(ids).size!==ids.length || ids.some(id=>document.getElementById(id))) return fail('duplicate');
    if (tag.sync && syncBannerStarted) return fail('sync-limit');
    loaded.set(id, html);
    tag.urls.forEach(url => sources.add(url));
    if (tag.sync) syncBannerStarted = true;
    beforeRun();
    // Install containers first; script placeholders preserve original insertion positions.
    const positions = tag.scripts.map(s => {
      const marker = document.createComment('provider-script');
      s.replaceWith(marker);
      return marker;
    });
    host.replaceChildren(tag.template.content);
    for (let i = 0; i < tag.scripts.length; i++) {
      if (signal.aborted || !host.isConnected) return fail('cancelled');
      const original = tag.scripts[i]!;
      const marker = positions[i]!;
      const script = document.createElement('script');
      for (const attr of original.attributes) script.setAttribute(attr.name, attr.value);
      if (original.getAttribute('src')?.startsWith('//')) script.src='https:'+original.getAttribute('src');
      script.text = original.text;
      if (!script.src) {
        marker.replaceWith(script);
        continue;
      }
      if (!original.hasAttribute('async')) script.async = false;
      const ok = await new Promise<boolean>(resolve => {
        let settled = false;
        const done = (value: boolean) => {
          if (settled) return;
          settled = true;
          clearTimeout(timer);
          signal.removeEventListener('abort', abort);
          resolve(value);
        };
        const abort = () => { script.remove(); done(false); };
        const timer = window.setTimeout(abort, 8000);
        script.addEventListener('load', () => done(true), {once:true});
        script.addEventListener('error', () => done(false), {once:true});
        signal.addEventListener('abort', abort, {once:true});
        marker.replaceWith(script);
      });
      if (!ok) return fail(signal.aborted?'cancelled':'script-error');
    }
    return !signal.aborted;
  };
  const result = queue.then(run, run);
  queue = result.catch(() => false);
  return result.catch(() => {onFailure?.('script-error');return false;});
}

declare global {
  interface Window {
    aclib?: {
      runBanner: (options: { zoneId: string; renderIn: string }) => void;
    };
  }
}

const ADCASH_LIBRARY_SRC = 'https://acscdn.com/script/aclib.js';
let adcashLibraryPromise: Promise<void> | null = null;

/** Load Adcash's official shared library once. Zone IDs are data, never executable HTML. */
function ensureAdcashLibrary(): Promise<void> {
  if (typeof window.aclib?.runBanner === 'function') return Promise.resolve();
  if (adcashLibraryPromise) return adcashLibraryPromise;

  const promise = new Promise<void>((resolve, reject) => {
    const existing = document.querySelector<HTMLScriptElement>(`script[src="${ADCASH_LIBRARY_SRC}"]`);
    const script = existing ?? document.createElement('script');
    let settled = false;
    const finish = (ok: boolean) => {
      if (settled) return;
      settled = true;
      window.clearTimeout(timer);
      if (ok) resolve();
      else reject(new Error('Adcash library failed to load or did not expose aclib.runBanner'));
    };
    const timer = window.setTimeout(() => finish(false), 10000);
    script.addEventListener('load', () => {
      script.dataset.rnAdcashLoaded = 'true';
      finish(typeof window.aclib?.runBanner === 'function');
    }, { once: true });
    script.addEventListener('error', () => finish(false), { once: true });

    if (!existing) {
      script.async = true;
      script.src = ADCASH_LIBRARY_SRC;
      script.dataset.rnAdcashLibrary = 'true';
      document.head.appendChild(script);
    } else if (script.dataset.rnAdcashLoaded === 'true') {
      finish(typeof window.aclib?.runBanner === 'function');
    }
  });

  adcashLibraryPromise = promise.catch(error => {
    adcashLibraryPromise = null;
    throw error;
  });
  return adcashLibraryPromise;
}

/** Dedicated Adcash Display adapter: one shared library, one validated zone per container. */
export function mountAdcashBanner(
  host: HTMLElement,
  id: string,
  zoneId: string,
  renderIn: string,
  signal: AbortSignal,
  beforeRun: () => void,
  onFailure?: (reason: DirectFailure) => void,
): Promise<boolean> {
  const run = async () => {
    const fail = (reason: DirectFailure) => { onFailure?.(reason); return false; };
    if (signal.aborted || !host.isConnected) return fail('cancelled');
    if (!/^\d{1,20}$/.test(zoneId) || !renderIn.startsWith('#')) return fail('invalid-tag');
    if (loaded.has(id) || [...document.querySelectorAll<HTMLElement>('[data-rn-adcash-zone]')].some(el => el.dataset.rnAdcashZone === zoneId)) {
      return fail('duplicate');
    }

    try {
      await ensureAdcashLibrary();
    } catch {
      return fail('library-error');
    }
    if (signal.aborted || !host.isConnected) return fail('cancelled');
    if (typeof window.aclib?.runBanner !== 'function') return fail('library-error');

    host.dataset.rnAdcashZone = zoneId;
    loaded.set(id, `adcash:${zoneId}`);
    beforeRun();
    try {
      window.aclib.runBanner({ zoneId, renderIn });
    } catch {
      loaded.delete(id);
      delete host.dataset.rnAdcashZone;
      return fail('zone-error');
    }
    return true;
  };

  const result = queue.then(run, run);
  queue = result.catch(() => false);
  return result.catch(() => { onFailure?.('zone-error'); return false; });
}

export function hasVisibleCreative(host: HTMLElement): boolean {
  return [...host.querySelectorAll('iframe, img, video, a, object, embed')].some(el => {
    const r = el.getBoundingClientRect();
    const style = getComputedStyle(el);
    return r.width > 1 && r.height > 1 && style.display !== 'none' && style.visibility !== 'hidden' &&
      (!(el instanceof HTMLImageElement) || (el.complete && el.naturalWidth > 1));
  });
}
