// Deployment smoke test: full flow against a running API (e.g. the docker-compose stack).
//   node scripts/smoke.mjs https://localhost:18443 smoke_admin Smoke-Admin-Password-123
// Set NODE_TLS_REJECT_UNAUTHORIZED=0 only for a self-signed local certificate.
import sharp from 'sharp';
import { zipSync } from 'fflate';

const [base = 'http://localhost:5080', adminLogin, adminPassword] = process.argv.slice(2);
const run = Date.now().toString(36);
const step = (msg) => console.log(`✔ ${msg}`);

async function call(method, path, { token, body, form, expect = 200 } = {}) {
  const res = await fetch(base + path, {
    method,
    headers: { ...(token ? { Authorization: `Bearer ${token}` } : {}), ...(body ? { 'Content-Type': 'application/json' } : {}) },
    body: form ?? (body ? JSON.stringify(body) : undefined),
  });
  const text = await res.text();
  if (res.status !== expect) throw new Error(`${method} ${path} → ${res.status}: ${text}`);
  return text ? JSON.parse(text) : null;
}

const health = await call('GET', '/api/health/ready');
step(`ready: ${JSON.stringify(health.checks)}`);

const admin = (await call('POST', '/api/auth/login', { body: { login: adminLogin, password: adminPassword } })).accessToken;
step('admin login');

const created = await call('POST', '/api/admin/translation-houses', {
  token: admin, expect: 201,
  body: { name: `Nhóm Kiểm Thử ${run}`, owner: { newAccount: { username: `smoke_${run}`, email: `smoke_${run}@example.com`, displayName: 'Chủ nhóm smoke' } } },
});
step(`house created (${created.house.slug})`);

const temp = created.generatedPassword;
const tempToken = (await call('POST', '/api/auth/login', { body: { login: `smoke_${run}`, password: temp } })).accessToken;
await call('GET', '/api/translation-house/comics', { token: tempToken, expect: 403 });
step('temporary password blocks the workspace (403)');
const owner = (await call('POST', '/api/auth/change-password', { token: tempToken, body: { currentPassword: temp, newPassword: 'Smoke-Owner-Password-123' } })).accessToken;

const comic = await call('POST', '/api/translation-house/comics', { token: owner, expect: 201, body: { translatedTitle: `Truyện Kiểm Thử ${run}`, originalTitle: 'Smoke', status: 'ongoing', tags: ['Kiểm thử'] } });
const chapter = await call('POST', `/api/translation-house/comics/${comic.id}/chapters`, { token: owner, expect: 201, body: { number: 1, title: 'Mở đầu' } });
step('comic + chapter created');

const files = {};
for (let i = 1; i <= 5; i++) {
  const svg = Buffer.from(`<svg xmlns="http://www.w3.org/2000/svg" width="800" height="1200"><rect width="100%" height="100%" fill="#f3e2c7"/><text x="50%" y="50%" font-size="110" text-anchor="middle" font-family="sans-serif">Trang ${i}</text></svg>`);
  files[`trang-${i}.png`] = new Uint8Array(await sharp(svg).png().toBuffer());
}
const form = new FormData();
form.append('file', new Blob([zipSync(files)], { type: 'application/zip' }), 'chuong-1.zip');
await call('POST', `/api/translation-house/chapters/${chapter.id}/upload`, { token: owner, form, expect: 202 });
let job;
for (let i = 0; i < 120; i++) {
  job = await call('GET', `/api/translation-house/chapters/${chapter.id}/processing`, { token: owner });
  if (job.status === 'succeeded' || job.status === 'failed') break;
  await new Promise((r) => setTimeout(r, 500));
}
if (job.status !== 'succeeded') throw new Error(`processing ${job.status}: ${job.errorMessage}`);
step(`processed ${job.progressTotal} pages`);

await call('POST', `/api/translation-house/chapters/${chapter.id}/publish`, { token: owner });
const reader = await call('GET', `/api/chapters/${chapter.id}/reader`);
step(`published → ${reader.package.url} (${reader.package.size} bytes, sha256 ${reader.package.sha256.slice(0, 12)}…)`);

const page = await call('GET', `/api/comics?q=${encodeURIComponent('truyen kiem thu ' + run)}`);
if (page.items.length !== 1) throw new Error('search did not find the comic');
step('public catalog + diacritic-insensitive search');

const fallback = await fetch(`${base}/internal/objects/${reader.package.url.replace(/^https?:\/\/[^/]+\//, '')}`);
if (fallback.status !== 404) throw new Error('origin fallback must require the shared secret');
step('origin fallback refuses requests without the shared secret');

console.log(`\nSMOKE OK — package key: ${reader.package.url.replace(/^https?:\/\/[^/]+\//, '')}`);
