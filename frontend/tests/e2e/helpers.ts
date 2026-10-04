import { expect, type APIRequestContext, type Page } from '@playwright/test';
import sharp from 'sharp';
import { zipSync } from 'fflate';

export const ADMIN = { login: 'e2e_admin', password: 'E2E-Admin-Password-123' };

/** Numbered test pages rendered with sharp (SVG text → PNG/JPEG) and zipped with fflate. */
export async function makeChapterZip(pages: number, label = 'Trang'): Promise<Buffer> {
  const files: Record<string, Uint8Array> = {};
  for (let i = 1; i <= pages; i++) {
    const svg = Buffer.from(
      `<svg xmlns="http://www.w3.org/2000/svg" width="800" height="1200">
         <rect width="100%" height="100%" fill="hsl(${(i * 47) % 360} 55% 82%)"/>
         <text x="50%" y="50%" font-size="120" font-family="sans-serif" text-anchor="middle" fill="#222">${label} ${i}</text>
       </svg>`,
    );
    const img = sharp(svg);
    // Deliberately unsorted names + mixed formats: natural ordering is tested end-to-end.
    files[i % 2 === 0 ? `trang-${i}.jpg` : `trang-${i}.png`] = new Uint8Array(i % 2 === 0 ? await img.jpeg({ quality: 80 }).toBuffer() : await img.png().toBuffer());
  }
  return Buffer.from(zipSync(files));
}

/**
 * "Google Debug": every page is watched for console errors, uncaught exceptions and failed API calls.
 * Expected failures (e.g. intentional 401/403 checks) are whitelisted per test.
 */
export function watchConsole(page: Page, allow: RegExp[] = []) {
  const problems: string[] = [];
  page.on('console', (msg) => {
    if (msg.type() === 'error' && !allow.some((r) => r.test(msg.text()))) problems.push(`console: ${msg.text()}`);
  });
  page.on('pageerror', (err) => problems.push(`pageerror: ${err.message}`));
  page.on('response', (res) => {
    const url = res.url();
    if (url.includes('/api/') && res.status() >= 500) problems.push(`api ${res.status()}: ${url}`);
  });
  return {
    problems,
    assertClean: () => expect(problems, problems.join('\n')).toEqual([]),
  };
}

export async function apiLogin(request: APIRequestContext, login: string, password: string): Promise<string> {
  const res = await request.post('/api/auth/login', { data: { login, password } });
  expect(res.status(), await res.text()).toBe(200);
  return (await res.json()).accessToken as string;
}

export async function waitForJob(request: APIRequestContext, token: string, chapterId: string) {
  for (let i = 0; i < 120; i++) {
    const res = await request.get(`/api/translation-house/chapters/${chapterId}/processing`, { headers: { Authorization: `Bearer ${token}` } });
    const job = await res.json();
    if (job.status === 'succeeded' || job.status === 'failed') return job;
    await new Promise((r) => setTimeout(r, 500));
  }
  throw new Error('processing did not finish');
}

export async function uiLogin(page: Page, login: string, password: string) {
  await page.goto('/dang-nhap');
  await page.getByLabel('Email hoặc tên đăng nhập').fill(login);
  await page.getByLabel('Mật khẩu').fill(password);
  await page.getByRole('button', { name: 'Đăng nhập' }).click();
}
