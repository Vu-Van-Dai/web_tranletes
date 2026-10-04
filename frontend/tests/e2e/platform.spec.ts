import { devices, expect, test, type Browser, type Page } from '@playwright/test';
import { ADMIN, apiLogin, makeChapterZip, uiLogin, waitForJob, watchConsole } from './helpers';
import { unzipSync, zipSync } from 'fflate';

/**
 * End-to-end flow, in order (spec §22):
 *  1 register/login · 2 admin login · 3 admin creates house · 4 owner login (forced password change)
 *  5 comic · 6 chapter · 7 upload · 8 processing status · 9 publish · 10 reader opens comic
 *  11 reader opens chapter · 12 package downloaded · 13 pages rendered · 14 next/prev navigation
 *  15 mobile · 16 unauthorized access · 17 cross-tenant rejection · 18 logout
 */
test.describe.configure({ mode: 'serial' });

const run = Date.now().toString(36);
const house = { name: `Nhóm Dịch Mèo Mun ${run}`, owner: `chu_meo_${run}` };
const ownerPassword = 'Mat-khau-chu-nhom-2026';
const comicTitle = `Thám Tử Lừng Danh ${run}`;
const state: { tempPassword?: string; comicId?: string; chapter1?: string; chapter2?: string; ownerToken?: string } = {};

async function freshPage(browser: Browser, options = {}) {
  const context = await browser.newContext({ locale: 'vi-VN', ...options });
  const page = await context.newPage();
  return { context, page };
}

test('1. Người đọc đăng ký, đăng xuất và đăng nhập lại', async ({ page }) => {
  const watch = watchConsole(page, [/401/]);
  const username = `docgia_${run}`;
  await page.goto('/dang-ky');
  await page.getByLabel('Tên đăng nhập').fill(username);
  await page.getByLabel('Tên hiển thị').fill('Độc Giả Thử Nghiệm');
  await page.getByLabel('Email').fill(`${username}@example.com`);
  await page.getByLabel('Mật khẩu', { exact: true }).fill('Mat-khau-doc-gia-123');
  await page.getByLabel('Nhập lại mật khẩu').fill('Mat-khau-doc-gia-123');
  await page.getByRole('button', { name: 'Đăng ký' }).click();
  await expect(page.getByTestId('account-menu')).toContainText('Độc Giả Thử Nghiệm');

  await page.getByTestId('account-menu').click();
  await page.getByTestId('logout').click();
  await expect(page.getByTestId('login-link')).toBeVisible();

  await uiLogin(page, username, 'Mat-khau-doc-gia-123');
  await expect(page.getByTestId('account-menu')).toBeVisible();

  // Wrong password shows the Vietnamese server message.
  await page.getByTestId('account-menu').click();
  await page.getByTestId('logout').click();
  await uiLogin(page, username, 'sai-mat-khau-roi');
  await expect(page.getByTestId('form-error')).toContainText('không đúng');
  watch.assertClean();
});

test('2–3. SystemAdmin đăng nhập và tạo nhóm dịch kèm tài khoản chủ nhóm', async ({ page }) => {
  const watch = watchConsole(page, [/401/]);
  await uiLogin(page, ADMIN.login, ADMIN.password);
  await expect(page.getByTestId('account-menu')).toBeVisible();
  await page.goto('/quan-tri');
  await expect(page.getByRole('heading', { name: 'Tổng quan hệ thống' })).toBeVisible();
  await expect(page.getByTestId('system-status')).toContainText('Hàng đợi xử lý');

  await page.goto('/quan-tri/nhom-dich');
  await page.getByTestId('create-house').click();
  await page.getByTestId('house-name').fill(house.name);
  await page.getByTestId('owner-username').fill(house.owner);
  await page.getByTestId('owner-email').fill(`${house.owner}@example.com`);
  await page.getByTestId('owner-display').fill('Chủ Nhóm Mèo');
  await page.getByTestId('create-house-submit').click();
  state.tempPassword = (await page.getByTestId('temp-password').textContent())?.trim();
  expect(state.tempPassword).toMatch(/^Tmp-/);

  await page.getByTestId('go-to-house').click();
  await expect(page.getByRole('heading', { name: house.name })).toBeVisible();
  await expect(page.getByTestId('house-status')).toHaveText('Đang hoạt động');
  await expect(page.getByTestId('members-table')).toContainText(house.owner);
  watch.assertClean();
});

test('4–6. Chủ nhóm đổi mật khẩu tạm, tạo truyện và chương', async ({ page }) => {
  const watch = watchConsole(page, [/401/]);
  await uiLogin(page, house.owner, state.tempPassword!);
  await expect(page).toHaveURL(/doi-mat-khau/);
  await page.getByLabel('Mật khẩu hiện tại').fill(state.tempPassword!);
  await page.getByLabel('Mật khẩu mới', { exact: true }).fill(ownerPassword);
  await page.getByLabel('Nhập lại mật khẩu mới').fill(ownerPassword);
  await page.getByRole('button', { name: 'Lưu mật khẩu mới' }).click();
  await expect(page).toHaveURL(/quan-ly-nhom/);
  await expect(page.getByText(house.name).first()).toBeVisible();

  // 5. Comic
  await page.goto('/quan-ly-nhom/truyen');
  await page.getByTestId('new-comic').click();
  await page.getByTestId('comic-title-input').fill(comicTitle);
  await page.getByLabel('Tên gốc').fill('Meitantei Conan');
  await page.getByLabel('Tác giả').fill('Aoyama Gōshō');
  await page.getByLabel('Thể loại').fill('Trinh thám, Học đường');
  await page.getByTestId('save-comic').click();
  await expect(page).toHaveURL(/quan-ly-nhom\/truyen\/[0-9a-f-]{36}$/);
  state.comicId = page.url().split('/').pop();
  await expect(page.getByRole('heading', { name: comicTitle })).toBeVisible();

  // 6. Chapter
  await page.getByTestId('new-chapter').click();
  await page.getByTestId('chapter-number').fill('1');
  await page.getByLabel('Tên chương').fill('Thám tử học sinh');
  await page.getByTestId('save-chapter').click();
  await expect(page).toHaveURL(/quan-ly-nhom\/chuong\/[0-9a-f-]{36}$/);
  state.chapter1 = page.url().split('/').pop();
  await expect(page.getByTestId('chapter-status')).toHaveText('Bản nháp');
  watch.assertClean();
});

test('7–9. Tải chương lên, theo dõi xử lý và đăng chương', async ({ page }) => {
  const watch = watchConsole(page, [/401/]);
  await uiLogin(page, house.owner, ownerPassword);
  await expect(page.getByTestId('account-menu')).toBeVisible();
  await page.goto(`/quan-ly-nhom/chuong/${state.chapter1}`);

  // 7a. Pre-upload check: a missing page and an unsupported image are flagged before anything is sent.
  const bad = await makeChapterZip(3);
  const badFiles = unzipSync(new Uint8Array(bad));
  delete badFiles['trang-2.jpg'];
  badFiles['trang-2.gif'] = new Uint8Array([0x47, 0x49, 0x46, 0x38, 0x39, 0x61]);
  await page.getByTestId('file-input').setInputFiles({ name: 'loi.zip', mimeType: 'application/zip', buffer: Buffer.from(zipSync(badFiles)) });
  const issues = page.getByTestId('upload-issues');
  await expect(issues).toContainText('BỊ BỎ QUA');
  await expect(issues).toContainText('thiếu trang');
  await page.getByRole('button', { name: 'Chọn tệp khác' }).click();
  await expect(page.getByTestId('upload-check')).toHaveCount(0);

  // 7b. Good archive: the browser shows the server's page order (natural sort), then the user confirms.
  await page.getByTestId('file-input').setInputFiles({ name: 'chuong-1.zip', mimeType: 'application/zip', buffer: await makeChapterZip(6) });
  const planned = page.getByTestId('upload-page');
  await expect(planned).toHaveCount(6);
  await expect(planned.nth(0)).toContainText('Trang 1');
  await expect(planned.nth(0)).toContainText('trang-1.png');
  await expect(planned.nth(5)).toContainText('trang-6.jpg');
  await expect(planned.nth(0).locator('img')).toHaveJSProperty('complete', true);
  await page.getByTestId('confirm-upload').click();

  // 8. Processing status is polled until the job succeeds.
  const status = page.getByTestId('processing-status');
  await expect(status).toContainText('Hoàn tất', { timeout: 60_000 });
  await expect(page.getByTestId('chapter-status')).toHaveText('Sẵn sàng đăng');

  // 8b. Pre-publish preview of the real (private) package: every page decoded, with its source file.
  await page.getByTestId('preview').click();
  const grid = page.getByTestId('preview-grid');
  await expect(grid.locator('img')).toHaveCount(6);
  await expect(grid).toContainText('từ trang-1.png');
  await expect(grid.locator('li').nth(5)).toContainText('từ trang-6.jpg');
  expect(await grid.locator('img').evaluateAll((imgs) => imgs.every((i) => (i as HTMLImageElement).src.startsWith('blob:')))).toBe(true);

  // 9. Publish from the preview
  await page.getByTestId('preview-publish').click();
  await page.getByRole('button', { name: 'Đăng ngay' }).click();
  await expect(page.getByTestId('chapter-status')).toHaveText('Đã đăng');
  await expect(page.getByTestId('toast-success').filter({ hasText: 'Đã đăng chương' })).toBeVisible();

  // Second chapter through the API (used for next/previous navigation).
  const request = page.request;
  state.ownerToken = await apiLogin(request, house.owner, ownerPassword);
  const auth = { Authorization: `Bearer ${state.ownerToken}` };
  const ch2 = await (await request.post(`/api/translation-house/comics/${state.comicId}/chapters`, { headers: auth, data: { number: 2, title: 'Chuyến tàu' } })).json();
  state.chapter2 = ch2.id;
  const up = await request.post(`/api/translation-house/chapters/${ch2.id}/upload`, {
    headers: auth,
    multipart: { file: { name: 'chuong-2.zip', mimeType: 'application/zip', buffer: await makeChapterZip(3, 'Chương 2 – trang') } },
  });
  expect(up.status()).toBe(202);
  expect((await waitForJob(request, state.ownerToken, ch2.id)).status).toBe('succeeded');
  expect((await request.post(`/api/translation-house/chapters/${ch2.id}/publish`, { headers: auth })).status()).toBe(200);
  watch.assertClean();
});

test('10–14. Người đọc mở truyện, tải gói chương, xem trang và chuyển chương', async ({ browser }) => {
  const { context, page } = await freshPage(browser);
  const watch = watchConsole(page, [/401/]);

  // 10. Open the comic from the home page.
  await page.goto('/');
  await page.getByTestId('comic-card').filter({ hasText: comicTitle }).first().getByRole('link').first().click();
  await expect(page.getByTestId('comic-title')).toHaveText(comicTitle);
  await expect(page.getByTestId('chapter-list')).toContainText('Chương 2');

  // Diacritic-insensitive search finds it too.
  await page.goto(`/tim-kiem?q=${encodeURIComponent('tham tu lung danh ' + run)}`);
  await expect(page.getByTestId('comic-card').filter({ hasText: comicTitle })).toHaveCount(1);
  await page.goBack();

  // 11–12. Open chapter 1: exactly one package request, immutable caching headers.
  const cdp = await context.newCDPSession(page);
  await cdp.send('Network.enable');
  const packageResponse = page.waitForResponse((r) => r.url().endsWith('/v1.pkg'));
  await page.getByTestId('read-first').click();
  const pkg = await packageResponse;
  expect(pkg.status()).toBe(200);
  expect(pkg.headers()['cache-control']).toContain('immutable');
  expect(pkg.headers()['content-type']).toBe('application/octet-stream');
  const reader = page.getByTestId('reader-root');
  await expect(reader).toBeVisible();

  // 13. Pages are rendered from object URLs (decoded, non-zero size), in natural order.
  const first = page.getByTestId('reader-page').first();
  await expect(first).toBeVisible();
  await expect.poll(() => first.evaluate((img: HTMLImageElement) => img.complete && img.naturalWidth)).toBeGreaterThan(0);
  expect(await first.getAttribute('src')).toMatch(/^blob:/);
  expect(await first.getAttribute('data-page')).toBe('1');
  await expect(page.getByTestId('page-indicator')).toContainText('/ 6');
  // Individual page images are never requested over the network (only the single package).
  const webpRequests: string[] = [];
  page.on('request', (r) => r.url().endsWith('.webp') && webpRequests.push(r.url()));

  // Paged mode + keyboard navigation.
  await page.getByTestId('mode-toggle').click();
  await expect(reader).toHaveAttribute('data-mode', 'paged');
  await page.keyboard.press('ArrowRight');
  await expect(page.getByTestId('page-indicator')).toContainText('Trang 2 / 6');
  await page.keyboard.press('ArrowLeft');
  await expect(page.getByTestId('page-indicator')).toContainText('Trang 1 / 6');
  await page.getByTestId('mode-toggle').click();

  // 14. Next / previous chapter.
  await page.getByTestId('next-chapter').click();
  await expect(page).toHaveURL(new RegExp(`/doc/${state.chapter2}$`));
  await expect(page.getByTestId('page-indicator')).toContainText('/ 3');
  await expect(page.getByTestId('next-chapter')).toBeDisabled();
  await page.keyboard.press('[');
  await expect(page).toHaveURL(new RegExp(`/doc/${state.chapter1}$`));

  // Revisit: the immutable package is served from the browser cache (Chrome DevTools Protocol).
  const fromCache = new Promise<boolean>((resolve) => {
    cdp.on('Network.responseReceived', (e) => {
      if (e.response.url.endsWith('/v1.pkg')) resolve(e.response.fromDiskCache || e.response.fromPrefetchCache || e.response.fromServiceWorker || false);
    });
    cdp.on('Network.requestServedFromCache', () => resolve(true));
  });
  await page.reload();
  await expect(page.getByTestId('reader-page').first()).toBeVisible();
  expect(await Promise.race([fromCache, new Promise<boolean>((r) => setTimeout(() => r(false), 10_000))])).toBe(true);

  expect(webpRequests).toEqual([]);
  watch.assertClean();
  await context.close();
});

test('15. Giao diện di động (Pixel 7)', async ({ browser }) => {
  const { context, page } = await freshPage(browser, { ...devices['Pixel 7'] });
  const watch = watchConsole(page, [/401/]);
  await page.goto('/');
  await expect(page.getByTestId('mobile-nav')).toBeVisible();

  // No horizontal overflow and a two-column comic grid.
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(overflow).toBeLessThanOrEqual(1);
  const columns = await page.getByTestId('comic-grid').first().evaluate((el) => getComputedStyle(el).gridTemplateColumns.split(' ').length);
  expect(columns).toBe(2);

  await page.goto(`/doc/${state.chapter1}`);
  await expect(page.getByTestId('reader-page').first()).toBeVisible();
  const width = await page.getByTestId('reader-page').first().evaluate((img) => img.getBoundingClientRect().width);
  expect(width).toBeGreaterThan(300);

  // Workspace collapses its sidebar into a drawer.
  await uiLogin(page, house.owner, ownerPassword);
  await expect(page.getByTestId('account-menu')).toBeVisible();
  await page.goto('/quan-ly-nhom');
  await page.getByTestId('workspace-menu').click();
  await expect(page.getByRole('dialog', { name: 'Quản lý nhóm dịch' })).toBeVisible();
  watch.assertClean();
  await context.close();
});

test('16. Truy cập trái phép bị chặn', async ({ browser }) => {
  const { context, page } = await freshPage(browser);
  // Anonymous → redirected to login.
  await page.goto('/quan-tri');
  await expect(page).toHaveURL(/dang-nhap\?next=%2Fquan-tri/);
  await page.goto('/quan-ly-nhom');
  await expect(page).toHaveURL(/dang-nhap/);

  // A plain reader sees "forbidden" in both workspaces.
  await uiLogin(page, `docgia_${run}`, 'Mat-khau-doc-gia-123');
  await expect(page.getByTestId('account-menu')).toBeVisible();
  await page.goto('/quan-tri');
  await expect(page.getByTestId('forbidden')).toBeVisible();
  await page.goto('/quan-ly-nhom');
  await expect(page.getByTestId('forbidden')).toContainText('chưa thuộc nhóm dịch');

  // And the API refuses regardless of the UI.
  const anon = await page.request.get('/api/admin/stats');
  expect(anon.status()).toBe(401);
  const readerToken = await apiLogin(page.request, `docgia_${run}`, 'Mat-khau-doc-gia-123');
  const asReader = await page.request.get('/api/admin/stats', { headers: { Authorization: `Bearer ${readerToken}` } });
  expect(asReader.status()).toBe(403);
  await context.close();
});

test('17. Nhóm dịch khác không truy cập được dữ liệu của nhóm này', async ({ browser, request }) => {
  // Admin creates a second house (XYZ) through the API.
  const adminToken = await apiLogin(request, ADMIN.login, ADMIN.password);
  const created = await request.post('/api/admin/translation-houses', {
    headers: { Authorization: `Bearer ${adminToken}` },
    data: { name: `Nhóm XYZ ${run}`, owner: { newAccount: { username: `chu_xyz_${run}`, email: `xyz_${run}@example.com`, displayName: 'Chủ XYZ' } } },
  });
  expect(created.status()).toBe(201);
  const temp = (await created.json()).generatedPassword as string;
  const tempToken = await apiLogin(request, `chu_xyz_${run}`, temp);
  const changed = await request.post('/api/auth/change-password', {
    headers: { Authorization: `Bearer ${tempToken}` },
    data: { currentPassword: temp, newPassword: 'Mat-khau-xyz-12345' },
  });
  const xyzToken = (await changed.json()).accessToken as string;
  const xyz = { Authorization: `Bearer ${xyzToken}` };

  // API: every attempt on ABC's resources is 403.
  for (const [method, url] of [
    ['get', `/api/translation-house/comics/${state.comicId}`],
    ['get', `/api/translation-house/chapters/${state.chapter1}`],
    ['post', `/api/translation-house/chapters/${state.chapter1}/unpublish`],
    ['delete', `/api/translation-house/comics/${state.comicId}`],
  ] as const) {
    const res = await request[method](url, { headers: xyz });
    expect(res.status(), `${method} ${url}`).toBe(403);
    expect((await res.json()).code).toBe('cross_tenant');
  }

  // UI: XYZ's owner opening ABC's chapter page gets an error, not data.
  const { context, page } = await freshPage(browser);
  await uiLogin(page, `chu_xyz_${run}`, 'Mat-khau-xyz-12345');
  await expect(page.getByTestId('account-menu')).toBeVisible();
  await page.goto(`/quan-ly-nhom/chuong/${state.chapter1}`);
  await expect(page.getByRole('alert')).toContainText('không có quyền truy cập dữ liệu của nhóm dịch khác');
  await page.goto('/quan-ly-nhom/truyen');
  await expect(page.getByText(comicTitle)).toHaveCount(0);

  // ABC's chapter is still published and readable.
  const stillPublic = await request.get(`/api/chapters/${state.chapter1}/reader`);
  expect(stillPublic.status()).toBe(200);
  await context.close();
});

test('18. Đăng xuất', async ({ page }) => {
  await uiLogin(page, house.owner, ownerPassword);
  await expect(page.getByTestId('account-menu')).toBeVisible();
  await page.getByTestId('account-menu').click();
  await page.getByTestId('logout').click();
  await expect(page.getByTestId('login-link')).toBeVisible();

  // The refresh cookie is gone: reloading does not sign back in, protected pages redirect.
  await page.reload();
  await expect(page.getByTestId('login-link')).toBeVisible();
  await page.goto('/tu-truyen');
  await expect(page).toHaveURL(/dang-nhap/);
});

test('19. Ảnh chụp giao diện cho tài liệu (desktop + di động)', async ({ browser }) => {
  const out = '../docs/screenshots';
  for (const [name, options] of [['desktop', { viewport: { width: 1366, height: 900 } }], ['mobile', devices['Pixel 7']]] as const) {
    const { context, page } = await freshPage(browser, options);
    await page.goto('/');
    await expect(page.getByTestId('comic-card').first()).toBeVisible();
    await page.screenshot({ path: `${out}/${name}-home.png` });
    await page.goto(`/truyen/${state.comicId}`);
    await expect(page.getByTestId('comic-title')).toBeVisible();
    await page.screenshot({ path: `${out}/${name}-comic.png` });
    await page.goto(`/doc/${state.chapter1}`);
    await expect(page.getByTestId('reader-page').first()).toBeVisible();
    await page.screenshot({ path: `${out}/${name}-reader.png` });
    await uiLogin(page, house.owner, ownerPassword);
    await expect(page.getByTestId('account-menu')).toBeVisible();
    await page.goto(`/quan-ly-nhom/chuong/${state.chapter1}`);
    await expect(page.getByTestId('processing-status')).toBeVisible();
    await page.screenshot({ path: `${out}/${name}-studio-chapter.png`, fullPage: true });
    await context.close();
  }
  const { context, page } = await freshPage(browser, { viewport: { width: 1366, height: 900 } });
  await uiLogin(page, ADMIN.login, ADMIN.password);
  await expect(page.getByTestId('account-menu')).toBeVisible();
  await page.goto('/quan-tri');
  await expect(page.getByTestId('system-status')).toContainText('Hàng đợi');
  await page.screenshot({ path: `${out}/desktop-admin.png`, fullPage: true });
  await context.close();
});

test.afterEach(async ({ page }, testInfo) => {
  if (testInfo.status !== testInfo.expectedStatus) {
    await page.screenshot({ path: testInfo.outputPath('failure.png'), fullPage: true }).catch(() => undefined);
  }
});

export type { Page };
