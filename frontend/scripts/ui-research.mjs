// UX research on the reference sites (structure only).
// - Read-only, one visit per page, polite delays.
// - Images / media / fonts are BLOCKED: no artwork is downloaded or captured.
// - Records layout & navigation patterns + console errors / failed requests (Chrome DevTools data).
// Output (git-ignored): ../research/ui-research.json and ../research/screenshots/*.png
import { chromium, devices } from '@playwright/test';
import { mkdir, writeFile } from 'node:fs/promises';
import path from 'node:path';

const SITES = [
  'https://meothamthuy123.com/',
  'https://truyentranhdammyy.site/',
  'https://dualeotruyenpet.com/',
  'https://teamsany.com/',
];
const OUT = path.resolve(import.meta.dirname, '..', '..', 'research');
const VIEWPORTS = {
  desktop: { viewport: { width: 1366, height: 900 } },
  mobile: { ...devices['Pixel 7'] },
};

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function analyse(page) {
  return page.evaluate(() => {
    const text = (el) => (el?.textContent ?? '').replace(/\s+/g, ' ').trim();
    const visible = (el) => {
      const r = el.getBoundingClientRect();
      return r.width > 0 && r.height > 0;
    };
    const header = document.querySelector('header, .header, #header, nav');
    const headerStyle = header ? getComputedStyle(header) : null;
    const navLinks = [...document.querySelectorAll('header a, nav a, .menu a, .navbar a')]
      .filter(visible).map(text).filter((t) => t && t.length < 40);
    const headings = [...document.querySelectorAll('h1, h2, h3')].filter(visible).map((h) => ({ tag: h.tagName, text: text(h).slice(0, 60) })).slice(0, 25);
    const search = document.querySelector('input[type=search], input[name*=search i], input[placeholder*="tìm" i], input[placeholder*="search" i]');
    // Repeated card-like blocks: elements with an <a> and an <img> placeholder and short text.
    const cards = [...document.querySelectorAll('article, .item, .card, .comic-item, .page-item-detail, li')]
      .filter((el) => el.querySelector('a') && el.querySelector('img') && visible(el));
    let columns = null;
    if (cards.length > 3) {
      const tops = new Map();
      for (const c of cards.slice(0, 40)) {
        const top = Math.round(c.getBoundingClientRect().top / 10);
        tops.set(top, (tops.get(top) ?? 0) + 1);
      }
      columns = Math.max(...tops.values());
    }
    const buttons = [...document.querySelectorAll('button, a.btn, .btn, select')].filter(visible).map(text).filter(Boolean).slice(0, 40);
    const body = getComputedStyle(document.body);
    return {
      title: document.title.slice(0, 80),
      lang: document.documentElement.lang,
      header: headerStyle ? { position: headerStyle.position, background: headerStyle.backgroundColor, height: Math.round(header.getBoundingClientRect().height) } : null,
      navLinks: [...new Set(navLinks)].slice(0, 40),
      headings,
      search: search ? { placeholder: search.getAttribute('placeholder') } : null,
      cardCount: cards.length,
      columns,
      buttons: [...new Set(buttons)],
      fontFamily: body.fontFamily,
      background: body.backgroundColor,
      color: body.color,
      footerLinks: document.querySelectorAll('footer a').length,
      pageHeight: document.documentElement.scrollHeight,
      hasPagination: !!document.querySelector('.pagination, .page-numbers, nav[aria-label*=page i]'),
    };
  });
}

async function findLink(page, patterns) {
  const hrefs = await page.$$eval('a[href]', (as) => as.map((a) => a.href));
  const origin = new URL(page.url()).origin;
  for (const re of patterns) {
    const hit = hrefs.find((h) => re.test(h) && h.startsWith(origin));
    if (hit) return hit;
  }
  return null;
}

async function main() {
  await mkdir(path.join(OUT, 'screenshots'), { recursive: true });
  const browser = await chromium.launch();
  const report = [];
  for (const site of SITES) {
    const host = new URL(site).hostname;
    for (const [vpName, vp] of Object.entries(VIEWPORTS)) {
      const context = await browser.newContext({ ...vp, locale: 'vi-VN' });
      await context.route('**/*', (route) => {
        const type = route.request().resourceType();
        return ['image', 'media', 'font'].includes(type) ? route.abort() : route.continue();
      });
      const page = await context.newPage();
      const consoleErrors = [];
      const failed = [];
      page.on('console', (m) => m.type() === 'error' && consoleErrors.push(m.text().slice(0, 160)));
      page.on('requestfailed', (r) => {
        const type = r.request?.resourceType?.() ?? r.resourceType();
        if (!['image', 'media', 'font'].includes(type)) failed.push(`${type} ${r.url().slice(0, 100)}`);
      });

      const entry = { site: host, viewport: vpName, pages: {} };
      const visit = async (name, url) => {
        try {
          const response = await page.goto(url, { waitUntil: 'domcontentloaded', timeout: 45_000 });
          await sleep(2500);
          entry.pages[name] = { url, status: response?.status(), ...(await analyse(page)) };
          await page.screenshot({ path: path.join(OUT, 'screenshots', `${host}-${name}-${vpName}.png`), fullPage: false });
        } catch (e) {
          entry.pages[name] = { url, error: String(e).slice(0, 200) };
        }
      };

      await visit('home', site);
      if (vpName === 'desktop') {
        const detail = await findLink(page, [/\/truyen[^/]*\/[^/]+\/?$/i, /\/manga\/[^/]+\/?$/i, /\/(comic|series)\/[^/]+\/?$/i]);
        if (detail) {
          await visit('detail', detail);
          const chapter = await findLink(page, [/chap(ter)?[-_]?\d+/i, /chuong[-_]?\d+/i]);
          if (chapter) await visit('reader', chapter);
        }
      }

      entry.consoleErrors = consoleErrors.slice(0, 15);
      entry.failedRequests = failed.slice(0, 15);
      report.push(entry);
      await context.close();
      await sleep(1500);
    }
  }

  await browser.close();
  await writeFile(path.join(OUT, 'ui-research.json'), JSON.stringify(report, null, 2), 'utf8');
  console.log(`Wrote ${report.length} entries to ${path.join(OUT, 'ui-research.json')}`);
}

main().catch((e) => {
  console.error(e);
  process.exit(1);
});
