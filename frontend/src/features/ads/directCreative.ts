// Direct banner code is a privileged, explicitly approved integration, not an XSS sandbox.
// Keep the provider tag intact; do not emulate cookies, eval, document.write or impressions.
const loaded = new Map<string, string>();
const sources = new Set<string>();
let queue: Promise<unknown> = Promise.resolve();
let syncBannerStarted = false;
export const loadedDirectBanners = () => [...loaded.entries()];

export function parseDirectTag(html: string) {
  const template = document.createElement('template');
  template.innerHTML = html;
  const scripts = [...template.content.children].filter((el): el is HTMLScriptElement => el instanceof HTMLScriptElement);
  if (!scripts.length || template.content.querySelectorAll('script').length !== scripts.length) throw Error('Expected top-level provider scripts');
  const external = scripts.filter(s => s.hasAttribute('src'));
  if (!external.length || external.some(s => !/^https:\/\//i.test(s.getAttribute('src') ?? ''))) throw Error('Expected HTTPS provider script');
  const urls = external.map(s => new URL(s.getAttribute('src')!).href);
  if (new Set(urls).size !== urls.length) throw Error('Duplicate provider script');
  const sync = scripts.some(s => !s.hasAttribute('src')) || external.some(s => !s.hasAttribute('async'));
  return { template, scripts, urls, sync };
}

/** Mount at most once per unit and script URL in a document. No automatic retries. */
export function mountDirectBanner(host: HTMLElement, id: string, html: string, signal: AbortSignal, beforeRun: () => void): Promise<boolean> {
  const run = async () => {
    if (signal.aborted || !host.isConnected || loaded.has(id)) return false;
    let tag: ReturnType<typeof parseDirectTag>;
    try { tag = parseDirectTag(html); } catch { return false; }
    if (tag.urls.some(url => sources.has(url)) || (tag.sync && syncBannerStarted)) return false;
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
      if (signal.aborted || !host.isConnected) return false;
      const original = tag.scripts[i]!;
      const marker = positions[i]!;
      const script = document.createElement('script');
      for (const attr of original.attributes) script.setAttribute(attr.name, attr.value);
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
      if (!ok) return false;
    }
    return !signal.aborted;
  };
  const result = queue.then(run, run);
  queue = result.catch(() => false);
  return result.catch(() => false);
}

export function hasVisibleCreative(host: HTMLElement): boolean {
  return [...host.querySelectorAll('iframe, img, video, a, object, embed')].some(el => {
    const r = el.getBoundingClientRect();
    const style = getComputedStyle(el);
    return r.width > 1 && r.height > 1 && style.display !== 'none' && style.visibility !== 'hidden' &&
      (!(el instanceof HTMLImageElement) || (el.complete && el.naturalWidth > 1));
  });
}
