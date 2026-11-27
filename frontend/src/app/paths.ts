/** All client routes (Vietnamese, ASCII). New links are advertised by the backend
 * only after the additive slug API is deployed. Old API responses remain usable. */
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
export function comicRoute(id: string, slug?: string) {
  return slug && !uuid.test(id) ? { house: id, slug } : null;
}
export function chapterRoute(pathname: string) {
  const m = /^\/doc\/([^/]+)\/([^/]+)\/chuong-(\d+(?:\.\d{1,2})?)\/?$/.exec(pathname);
  if (!m) return null;
  try { return { house: decodeURIComponent(m[1]!), slug: decodeURIComponent(m[2]!), number: m[3]! }; } catch { return null; }
}
export function comicLink(c: { id: string; slug: string; publicPath?: string }) {
  return c.publicPath || paths.comic(c.id, c.slug);
}
export function chapterLink(c: { id: string; number: number }, comic?: { publicPath?: string }) {
  return comic?.publicPath ? '/doc/' + comic.publicPath.slice('/truyen/'.length) + '/chuong-' + c.number : paths.read(c.id);
}
export function chapterSummaryLink(c: { id: string; publicPath?: string }) {
  return c.publicPath || paths.read(c.id);
}
export const paths = {
  home: '/',
  browse: '/truyen',
  comic: (id: string, slug?: string) => `/truyen/${id}${slug ? `/${slug}` : ''}`,
  read: (chapterId: string) => `/doc/${chapterId}`,
  search: '/tim-kiem',
  genre: (slug: string) => `/the-loai/${slug}`,
  about: '/gioi-thieu',
  privacy: '/chinh-sach-bao-mat',
  contact: '/lien-he',
  terms: '/dieu-khoan-su-dung',
  faq: '/faq',
  houses: '/nhom-dich',
  house: (slug: string) => `/nhom-dich/${slug}`,
  library: '/tu-truyen',
  login: '/dang-nhap',
  register: '/dang-ky',
  changePassword: '/doi-mat-khau',
  admin: {
    root: '/quan-tri',
    houses: '/quan-tri/nhom-dich',
    house: (id: string) => `/quan-tri/nhom-dich/${id}`,
    users: '/quan-tri/nguoi-dung',
    stats: '/quan-tri/thong-ke',
    audit: '/quan-tri/nhat-ky',
    settings: '/quan-tri/cai-dat',
    revenue: '/quan-tri/doanh-thu',
    ads: '/quan-tri/quang-cao',
  },
  studio: {
    root: '/quan-ly-nhom',
    profile: '/quan-ly-nhom/ho-so',
    members: '/quan-ly-nhom/thanh-vien',
    comics: '/quan-ly-nhom/truyen',
    comic: (id: string) => `/quan-ly-nhom/truyen/${id}`,
    chapter: (id: string) => `/quan-ly-nhom/chuong/${id}`,
    preview: (id: string, version: number) => `/quan-ly-nhom/chuong/${id}/xem-truoc/${version}`,
    processing: '/quan-ly-nhom/xu-ly',
    stats: '/quan-ly-nhom/thong-ke',
    settings: '/quan-ly-nhom/cai-dat',
  },
} as const;
