// Fills a LOCAL development API with Vietnamese demo content so the UI can be reviewed.
// Everything goes through the public REST API (same path as a real translation house), so running
// it also exercises upload → processing → package → publish end to end.
//   node scripts/seed-demo.mjs [http://localhost:5080]
// Admin credentials are read from the root .env (SEED_ADMIN_USERNAME / SEED_ADMIN_PASSWORD).
// Never run this against production: it creates accounts with well-known demo passwords.
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import sharp from 'sharp';
import { zipSync } from 'fflate';

const base = process.argv[2] ?? 'http://localhost:5080';
if (!/^http:\/\/(localhost|127\.0\.0\.1)(:\d+)?$/.test(base)) {
  console.error('seed-demo only targets a local API (http://localhost:…).');
  process.exit(1);
}

const env = Object.fromEntries(
  readFileSync(resolve(import.meta.dirname, '../../.env'), 'utf8')
    .split(/\r?\n/)
    .filter((l) => /^[A-Z_]+=/.test(l))
    .map((l) => [l.slice(0, l.indexOf('=')), l.slice(l.indexOf('=') + 1)]),
);

const step = (msg) => console.log(`✔ ${msg}`);

async function call(method, path, { token, houseId, body, form, expect = [200, 201, 202, 204] } = {}) {
  const res = await fetch(base + path, {
    method,
    headers: {
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...(houseId ? { 'X-House-Id': houseId } : {}),
      ...(body ? { 'Content-Type': 'application/json' } : {}),
      Origin: 'http://localhost:5173',
    },
    body: form ?? (body ? JSON.stringify(body) : undefined),
  });
  const text = await res.text();
  if (!expect.includes(res.status)) throw new Error(`${method} ${path} → ${res.status}: ${text}`);
  return text ? JSON.parse(text) : null;
}

const login = async (name, password) => (await call('POST', '/api/auth/login', { body: { login: name, password } })).accessToken;

// ---------------------------------------------------------------- demo data (original, invented)
const DEMO_PASSWORD = 'TramTruyen-Demo-2026';

const houses = [
  {
    name: 'Nhóm Dịch Mây Chiều',
    description: 'Nhóm dịch nhỏ chuyên truyện đời thường, học đường và lãng mạn nhẹ nhàng. Lịch ra chương: tối thứ Tư và Chủ nhật.',
    links: { facebook: 'https://facebook.com/maychieu.demo', discord: null, website: null },
    owner: { username: 'may_chieu', email: 'maychieu@demo.local', displayName: 'Mây Chiều' },
    staff: { username: 'tho_may', email: 'thomay@demo.local', displayName: 'Thợ Mây', permissions: ['viewMembers', 'manageChapters', 'uploadChapters'] },
    palette: ['#f6d8b8', '#e98a5f', '#7a3e2b'],
    comics: [
      {
        translatedTitle: 'Tiệm Trà Cuối Phố', originalTitle: 'The Last Teahouse on the Street', author: 'Hạ Vy', artist: 'Hạ Vy',
        status: 'ongoing', tags: ['Đời thường', 'Lãng mạn'],
        description: 'Sau khi rời thành phố, Lam nhận lại tiệm trà cũ của bà ngoại ở một con phố nhỏ. Mỗi vị khách ghé qua đều mang theo một câu chuyện chưa kể.',
        chapters: [[1, 'Ấm trà đầu tiên'], [2, 'Vị khách mưa'], [3, 'Hương nhài'], [4, 'Bản nháp chưa đăng', 'draft']],
      },
      {
        translatedTitle: 'Lớp Học Lúc Nửa Đêm', originalTitle: 'Midnight Classroom', author: 'Trần Khải', artist: 'Mộc',
        status: 'ongoing', tags: ['Học đường', 'Hài hước', 'Kỳ ảo'],
        description: 'Ngôi trường cấp ba bình thường vào ban ngày, nhưng đúng mười hai giờ đêm, phòng 3B lại sáng đèn và có một giáo viên không ai từng gặp.',
        chapters: [[1, 'Phòng 3B'], [2, 'Bài kiểm tra kỳ lạ'], [2.5, 'Ngoại truyện: Bác bảo vệ'], [3, 'Chờ đăng', 'ready']],
      },
      {
        translatedTitle: 'Mùa Hạ Không Tên', originalTitle: 'Nameless Summer', author: 'Lục Nhiên', artist: 'Lục Nhiên',
        status: 'completed', tags: ['Lãng mạn', 'Học đường'],
        description: 'Mùa hè cuối cùng trước kỳ thi đại học, hai người bạn thân hứa sẽ cùng nhau đi hết danh sách những điều chưa từng làm.',
        chapters: [[1, 'Danh sách'], [2, 'Biển đêm']],
      },
    ],
  },
  {
    name: 'Hiên Trăng Team',
    description: 'Team dịch truyện hành động, phiêu lưu và trinh thám. Nhận góp ý bản dịch qua Discord.',
    links: { facebook: null, discord: 'https://discord.gg/hientrangdemo', website: 'https://hientrang.demo.local' },
    owner: { username: 'hien_trang', email: 'hientrang@demo.local', displayName: 'Hiên Trăng' },
    palette: ['#c9d6f2', '#4f6db8', '#1d2a4d'],
    comics: [
      {
        translatedTitle: 'Kiếm Khách Phương Nam', originalTitle: 'Swordsman of the South', author: 'Đoàn Phong', artist: 'Studio Gió',
        status: 'ongoing', tags: ['Hành động', 'Phiêu lưu', 'Kỳ ảo'],
        description: 'Một kiếm khách mất trí nhớ tỉnh dậy bên bờ sông Hậu, trên tay chỉ còn nửa thanh kiếm và một tấm bản đồ bị xé đôi.',
        chapters: [[1, 'Nửa thanh kiếm'], [2, 'Bến đò'], [3, 'Đêm trăng máu'], [4, 'Thủy trại']],
      },
      {
        translatedTitle: 'Thám Tử Hẻm 42', originalTitle: 'Alley 42 Detective', author: 'Nguyễn Minh Triết', artist: 'Kha',
        status: 'hiatus', tags: ['Trinh thám', 'Hài hước'],
        description: 'Văn phòng thám tử nhỏ nhất Sài Gòn nằm sâu trong hẻm 42. Khách hàng đầu tiên trong tháng: một con mèo mất tích và một chiếc két sắt rỗng.',
        chapters: [[1, 'Con mèo mất tích'], [2, 'Chiếc két sắt']],
      },
      {
        translatedTitle: 'Vực Thẳm Không Đáy', originalTitle: 'Bottomless Abyss', author: 'Hắc Vũ', artist: 'Hắc Vũ',
        status: 'ongoing', tags: ['Kinh dị', 'Hành động'], isMature: true,
        description: 'Truyện có yếu tố bạo lực và kinh dị, chỉ phù hợp với độc giả từ 18 tuổi. Đội thám hiểm thứ bảy đi xuống vực thẳm, và chỉ có một tín hiệu quay trở lại.',
        chapters: [[1, 'Tín hiệu cuối']],
      },
    ],
  },
];

// ---------------------------------------------------------------- generated artwork (original, abstract)
const esc = (s) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;');

function coverSvg(title, [light, mid, dark], seed) {
  const circles = Array.from({ length: 6 }, (_, i) => {
    const x = (seed * 97 + i * 131) % 600;
    const y = (seed * 53 + i * 211) % 900;
    const r = 60 + ((seed + i * 37) % 140);
    return `<circle cx="${x}" cy="${y}" r="${r}" fill="${i % 2 ? mid : light}" opacity="0.55"/>`;
  }).join('');
  const words = title.split(' ');
  const lines = [];
  for (const w of words) {
    if (lines.length && (lines.at(-1) + ' ' + w).length <= 12) lines[lines.length - 1] += ' ' + w;
    else lines.push(w);
  }
  const text = lines
    .map((l, i) => `<text x="40" y="${640 + i * 72}" font-size="64" font-weight="700" font-family="Segoe UI, Arial, sans-serif" fill="#fffaf2">${esc(l)}</text>`)
    .join('');
  return `<svg xmlns="http://www.w3.org/2000/svg" width="600" height="900">
    <defs><linearGradient id="g" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="${light}"/><stop offset="1" stop-color="${dark}"/></linearGradient></defs>
    <rect width="600" height="900" fill="url(#g)"/>${circles}
    <rect x="0" y="560" width="600" height="340" fill="${dark}" opacity="0.55"/>${text}</svg>`;
}

function pageSvg(comic, chapterLabel, page, total, [light, mid, dark]) {
  const panels =
    page % 3 === 0
      ? `<rect x="40" y="40" width="720" height="1120" rx="6" fill="${light}" stroke="${dark}" stroke-width="6"/>`
      : `<rect x="40" y="40" width="720" height="540" rx="6" fill="${light}" stroke="${dark}" stroke-width="6"/>
         <rect x="40" y="620" width="340" height="540" rx="6" fill="#fffaf2" stroke="${dark}" stroke-width="6"/>
         <rect x="420" y="620" width="340" height="540" rx="6" fill="${mid}" opacity="0.6" stroke="${dark}" stroke-width="6"/>`;
  return `<svg xmlns="http://www.w3.org/2000/svg" width="800" height="1200">
    <rect width="800" height="1200" fill="#fffaf2"/>${panels}
    <circle cx="${200 + (page * 70) % 400}" cy="300" r="90" fill="${mid}" opacity="0.7"/>
    <text x="400" y="230" text-anchor="middle" font-size="44" font-weight="700" font-family="Segoe UI, Arial, sans-serif" fill="${dark}">${esc(comic)}</text>
    <text x="400" y="460" text-anchor="middle" font-size="36" font-family="Segoe UI, Arial, sans-serif" fill="${dark}">${esc(chapterLabel)}</text>
    <text x="400" y="1120" text-anchor="middle" font-size="30" font-family="Segoe UI, Arial, sans-serif" fill="${dark}">Trang ${page} / ${total}</text></svg>`;
}

function logoSvg(name, [light, mid, dark]) {
  const initials = name.split(' ').filter((w) => w.length > 1 && w !== 'Team').slice(-2).map((w) => w[0]).join('');
  return `<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256">
    <rect width="256" height="256" rx="48" fill="${dark}"/><circle cx="190" cy="70" r="44" fill="${light}" opacity="0.8"/>
    <text x="128" y="160" text-anchor="middle" font-size="96" font-weight="700" font-family="Segoe UI, Arial, sans-serif" fill="${mid === dark ? light : '#fffaf2'}">${esc(initials)}</text></svg>`;
}

const png = async (svg) => new Uint8Array(await sharp(Buffer.from(svg)).png().toBuffer());
const imageForm = (bytes, name, type) => {
  const form = new FormData();
  form.append('file', new Blob([bytes], { type }), name);
  return form;
};

async function waitForJob(token, houseId, chapterId) {
  for (let i = 0; i < 240; i++) {
    const job = await call('GET', `/api/translation-house/chapters/${chapterId}/processing`, { token, houseId });
    if (job.status === 'succeeded') return job;
    if (job.status === 'failed') throw new Error(`processing failed: ${job.errorMessage}`);
    await new Promise((r) => setTimeout(r, 400));
  }
  throw new Error('processing timed out');
}

// ---------------------------------------------------------------- run
await call('GET', '/api/health/ready');
const admin = await login(env.SEED_ADMIN_USERNAME, env.SEED_ADMIN_PASSWORD);
step('admin login');

const existing = await call('GET', '/api/admin/translation-houses?pageSize=100', { token: admin });
if ((existing.items ?? []).some((h) => h.name === houses[0].name)) {
  console.log('Demo data already present — nothing to do.');
  process.exit(0);
}

let seed = 1;
const allComics = [];
for (const house of houses) {
  const created = await call('POST', '/api/admin/translation-houses', {
    token: admin,
    body: { name: house.name, description: house.description, links: house.links, owner: { newAccount: { ...house.owner, temporaryPassword: null } } },
  });
  const temp = await login(house.owner.username, created.generatedPassword);
  const owner = (await call('POST', '/api/auth/change-password', { token: temp, body: { currentPassword: created.generatedPassword, newPassword: DEMO_PASSWORD } })).accessToken;
  const houseId = created.house.id;
  step(`house "${house.name}" + owner ${house.owner.username}`);

  await call('PUT', '/api/translation-house/profile/logo', {
    token: owner, houseId,
    form: imageForm(await png(logoSvg(house.name, house.palette)), 'logo.png', 'image/png'),
  });

  if (house.staff) {
    await call('POST', '/api/auth/register', { body: { username: house.staff.username, email: house.staff.email, displayName: house.staff.displayName, password: DEMO_PASSWORD } });
    await call('POST', '/api/translation-house/members', { token: owner, houseId, body: { login: house.staff.username, role: 'staff', permissions: house.staff.permissions } });
    step(`staff ${house.staff.username} (${house.staff.permissions.join(', ')})`);
  }

  for (const c of house.comics) {
    const comic = await call('POST', '/api/translation-house/comics', {
      token: owner, houseId,
      body: { translatedTitle: c.translatedTitle, originalTitle: c.originalTitle, description: c.description, author: c.author, artist: c.artist, status: c.status, isMature: c.isMature ?? false, tags: c.tags },
    });
    await call('PUT', `/api/translation-house/comics/${comic.id}/cover`, { token: owner, houseId, form: imageForm(await png(coverSvg(c.translatedTitle, house.palette, seed++)), 'bia.png', 'image/png') });
    allComics.push(comic);

    for (const [number, title, state = 'published'] of c.chapters) {
      const chapter = await call('POST', `/api/translation-house/comics/${comic.id}/chapters`, { token: owner, houseId, body: { number, title } });
      if (state === 'draft') continue;
      const total = 6 + (seed % 5);
      const files = {};
      for (let p = 1; p <= total; p++) {
        files[`trang-${p}.png`] = await png(pageSvg(c.translatedTitle, `Chương ${number}: ${title}`, p, total, house.palette));
      }
      await call('POST', `/api/translation-house/chapters/${chapter.id}/upload`, { token: owner, houseId, form: imageForm(zipSync(files), `chuong-${number}.zip`, 'application/zip') });
      await waitForJob(owner, houseId, chapter.id);
      if (state === 'published') await call('POST', `/api/translation-house/chapters/${chapter.id}/publish`, { token: owner, houseId });
    }
    step(`  ${c.translatedTitle}: ${c.chapters.length} chương`);
  }
}

// A reader with a followed comic and reading progress.
await call('POST', '/api/auth/register', { body: { username: 'doc_gia', email: 'docgia@demo.local', displayName: 'Độc Giả Demo', password: DEMO_PASSWORD } });
const reader = await login('doc_gia', DEMO_PASSWORD);
await call('PUT', `/api/me/follows/${allComics[0].id}`, { token: reader });
await call('PUT', `/api/me/follows/${allComics[3].id}`, { token: reader });
const firstChapters = await call('GET', `/api/comics/${allComics[3].id}/chapters`);
const chapterList = firstChapters.items ?? firstChapters;
await call('PUT', '/api/me/progress', { token: reader, body: { chapterId: chapterList[0].id, page: 3 } });
step('reader doc_gia (2 follows, reading progress)');

await call('PUT', '/api/admin/settings', { token: admin, body: { registrationEnabled: true, announcement: 'Đây là bản chạy thử nội bộ — dữ liệu chỉ dùng để xem giao diện.' } });
step('site announcement');

console.log(`\nDEMO OK — demo password for all demo accounts: ${DEMO_PASSWORD}`);
