// Public HTML generated at Cloudflare Pages; the API and database remain unchanged.
import type { ComicCard, ComicDetail, PublicChapter, ChapterSummary, Paged, PublicHouse, TagWithCount } from '../src/types/api';

export const ORIGIN = 'https://rongnho.shop';
const DEFAULT_IMAGE = ORIGIN + '/brand/og-cover.webp';
const HOME_DESCRIPTION = 'Rồng Nhỏ là website đọc truyện tranh online với các bộ truyện được dịch sang tiếng Việt bởi các nhóm dịch.';
const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const SLUG = /^[a-z0-9][a-z0-9-]{0,99}$/;
const INFO: Record<string, [string, string]> = {
  '/gioi-thieu': ['Giới thiệu Rồng Nhỏ', 'Rồng Nhỏ là nền tảng giúp độc giả khám phá truyện tranh dịch tiếng Việt và theo dõi những bộ truyện mình yêu thích trên điện thoại hoặc máy tính.'],
  '/chinh-sach-bao-mat': ['Chính sách bảo mật', 'Chính sách này giải thích những thông tin Rồng Nhỏ có thể xử lý khi bạn truy cập, đăng ký hoặc sử dụng các tính năng của rongnho.shop.'],
  '/lien-he': ['Liên hệ', 'Rồng Nhỏ tiếp nhận phản hồi về lỗi đọc truyện, tài khoản, hợp tác nhóm dịch, quyền tác giả và những vấn đề khác liên quan đến website.'],
  '/dieu-khoan-su-dung': ['Điều khoản sử dụng', 'Khi truy cập và sử dụng Rồng Nhỏ, bạn đồng ý tuân thủ các điều khoản dưới đây và các quy định pháp luật có liên quan.'],
  '/faq': ['Câu hỏi thường gặp (FAQ)', 'Tổng hợp những câu hỏi phổ biến khi sử dụng Rồng Nhỏ.'],
};
const PRIVATE = new Set(['/dang-nhap', '/dang-ky', '/doi-mat-khau', '/tu-truyen', '/tim-kiem']);
export const escapeHtml = (value: unknown) => String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]!));
const comicPath = (c: {id: string; slug: string}) => '/truyen/' + encodeURIComponent(c.id) + '/' + encodeURIComponent(c.slug);
const chapterPath = (id: string) => '/doc/' + encodeURIComponent(id);
const housePath = (slug: string) => '/nhom-dich/' + encodeURIComponent(slug);
const link = (path: string, name: string) => '<a href="' + escapeHtml(path) + '">' + escapeHtml(name) + '</a>';
const list = (links: string[]) => links.length ? '<ul>' + links.map(a => '<li>' + a + '</li>').join('') + '</ul>' : '';
const para = (value: unknown) => value ? '<p>' + escapeHtml(value) + '</p>' : '';
const chapterLabel = (c: {number: number; title: string | null}) => 'Chương ' + c.number + (c.title ? ' – ' + c.title : '');
const clean = (s: string) => s.replace(/\s+/g, ' ').trim().slice(0, 300);
const imageUrl = (raw: string | null | undefined) => {
  if (!raw) return DEFAULT_IMAGE;
  try { const u = new URL(raw, ORIGIN); return u.protocol === 'https:' ? u.href : DEFAULT_IMAGE; } catch { return DEFAULT_IMAGE; }
};
type Meta = { title: string; description: string; path?: string; body: string; noindex?: boolean; mature?: boolean; image?: string; status?: number };
type Result = Meta | { redirect: string };
type Api = <T>(path: string) => Promise<T>;
export class ApiFailure extends Error {
  constructor(readonly status: number) { super('Public API HTTP ' + status); }
}
const missing = (): Meta => ({title:'Không tìm thấy', description:'Trang không tồn tại.', body:para('Trang không tồn tại.') + link('/', 'Về trang chủ'), noindex:true, status:404});
const wrap = (title: string, body: string) => '<main style="max-width:72rem;margin:2rem auto;padding:1rem"><nav>' + link('/', 'Rồng Nhỏ') + ' · ' + link('/truyen','Danh sách truyện') + '</nav><h1>' + escapeHtml(title) + '</h1>' + body + '</main>';
export function isSeoPath(path: string) {
  return path === '/' || path === '/truyen' || path === '/nhom-dich' || Object.hasOwn(INFO,path) || PRIVATE.has(path) ||
    /^\/(?:truyen|doc|the-loai|nhom-dich)(?:\/|$)/.test(path);
}
function listMeta(title: string, description: string, path: string, data: Paged<ComicCard>, query: URLSearchParams): Meta {
  const page = data.page > 1 ? '?page=' + data.page : '';
  const filtered = ['q','status','sort'].some(k => query.has(k) && !!query.get(k) && !(k === 'sort' && query.get(k) === 'updated'));
  const pages = [
    ...(data.page > 1 ? [link(path + (data.page > 2 ? '?page=' + (data.page - 1) : ''), 'Trang trước')] : []),
    ...(data.page < data.totalPages ? [link(path + '?page=' + (data.page + 1), 'Trang tiếp')] : []),
  ];
  return {title, description, path:path+page, noindex:filtered || data.items.length===0, body:para(description)+list(data.items.map(c=>link(comicPath(c),c.translatedTitle)))+pages.join(' · ')};
}
export async function resolvePage(url: URL, api: Api): Promise<Result> {
  const path = url.pathname.replace(/\/+$/, '') || '/';
  const parts = path.split('/').slice(1);
  if (PRIVATE.has(path)) return {title: path === '/tim-kiem' ? 'Tìm kiếm' : 'Tài khoản', description:HOME_DESCRIPTION, body:'', noindex:true};
  if (Object.hasOwn(INFO,path)) {
    const [title,description] = INFO[path]!;
    return {title,description,path,body:para(description) + (path === '/lien-he' ? '<p><a href="mailto:rongnhotranlation@gmail.com">rongnhotranlation@gmail.com</a></p>' : '')};
  }
  if (path === '/') {
    const data = await api<Paged<ComicCard>>('/api/comics?sort=updated&pageSize=18');
    return {title:'Đọc truyện tranh online, truyện dịch tiếng Việt',description:HOME_DESCRIPTION,path,body:para(HOME_DESCRIPTION)+list(data.items.map(c=>link(comicPath(c),c.translatedTitle)))};
  }
  if (parts[0] === 'truyen' && parts.length >= 2) {
    if (parts.length > 3 || !UUID.test(parts[1] ?? '')) return missing();
    const id=parts[1]!;
    const c=await api<ComicDetail>('/api/comics/'+id);
    const canonical=comicPath(c);
    if (url.pathname!==canonical) return {redirect:canonical+url.search};
    const chapters=await api<PublicChapter[]>('/api/comics/'+id+'/chapters');
    const description=clean(c.description || ('Thông tin và danh sách chương truyện '+c.translatedTitle+' tại Rồng Nhỏ.'));
    const details=[c.originalTitle,c.author && 'Tác giả: '+c.author,c.artist && 'Họa sĩ: '+c.artist,c.isMature && 'Nội dung dành cho người trưởng thành'].filter(Boolean).map(para).join('');
    return {title:c.translatedTitle, description,path:canonical, mature:c.isMature,image:c.isMature?DEFAULT_IMAGE:imageUrl(c.coverUrl),body:details+para(c.description)+para('Nhóm dịch: '+c.house.name)+link(housePath(c.house.slug),c.house.name)+list(c.tags.map(t=>link('/the-loai/'+encodeURIComponent(t.slug),t.name)))+'<h2>Danh sách chương</h2>'+list(chapters.map(ch=>link(chapterPath(ch.id),chapterLabel(ch))))};
  }
  if (parts[0] === 'doc') {
    if (parts.length!==2 || !UUID.test(parts[1]??'')) return missing();
    const ch=await api<ChapterSummary>('/api/chapters/'+parts[1]);
    const title=ch.comicTitle+' – '+chapterLabel(ch);
    return {title,description:clean('Thông tin '+title+' tại Rồng Nhỏ.'),path:chapterPath(ch.id),mature:ch.isMature,noindex:ch.isMature,body:para('Nhóm dịch: '+ch.house.name)+link(comicPath({id:ch.comicId,slug:ch.comicSlug}),ch.comicTitle)+list([...(ch.prev?[link(chapterPath(ch.prev.id),chapterLabel(ch.prev))]:[]),...(ch.next?[link(chapterPath(ch.next.id),chapterLabel(ch.next))]:[])])};
  }
  if (path==='/nhom-dich') {
    const houses=await api<PublicHouse[]>('/api/houses');
    return {title:'Nhóm dịch',description:'Khám phá các nhóm dịch truyện tranh tại Rồng Nhỏ.',path,body:list(houses.map(h=>link(housePath(h.slug),h.name)))};
  }
  if (path==='/truyen' || parts[0]==='the-loai' || parts[0]==='nhom-dich') {
    const query=new URLSearchParams();
    const requested=Number(url.searchParams.get('page')??1);
    const page=Number.isSafeInteger(requested)&&requested>0?requested:1;
    query.set('page',String(page));query.set('pageSize','24');
    let title='Danh sách truyện',description='Khám phá truyện tranh dịch tiếng Việt tại Rồng Nhỏ.';
    if (parts[0]==='the-loai' || parts[0]==='nhom-dich') {
      if (parts.length!==2 || !SLUG.test(parts[1]??'')) return missing();
      const slug=parts[1]!;
      if (parts[0]==='the-loai') {
        const tags=await api<TagWithCount[]>('/api/tags');
        const tag=tags.find(t=>t.slug===slug);
        if (!tag) return missing();
        title='Thể loại: '+tag.name;description='Danh sách truyện thuộc thể loại '+tag.name+' tại Rồng Nhỏ.';
        query.set('tag',slug);
      } else {
        const house=await api<PublicHouse>('/api/houses/'+encodeURIComponent(slug));
        title=house.name;description=house.description||('Truyện của nhóm dịch '+house.name+' tại Rồng Nhỏ.');
        query.set('house',slug);
      }
    }
    if (parts[0]!=='nhom-dich') for (const key of ['q','status','sort']) {
      const value=url.searchParams.get(key);if(value)query.set(key,value);
    }
    const data=await api<Paged<ComicCard>>('/api/comics?'+query);
    if(page>1 && page>data.totalPages)return missing();
    return listMeta(title,description,path,data,url.searchParams);
  }
  return missing();
}
export function renderHtml(shell: string, meta: Meta): string {
  const title=meta.title+' · Rồng Nhỏ';
  const description=clean(meta.description);
  const tags = [
    '<title>'+escapeHtml(title)+'</title>',
    '<meta name="description" content="'+escapeHtml(description)+'">',
    '<meta name="robots" content="'+(meta.noindex?'noindex, follow':'index, follow, max-image-preview:large')+'">',
    ...(meta.path?['<link rel="canonical" href="'+escapeHtml(ORIGIN+meta.path)+'">','<meta property="og:url" content="'+escapeHtml(ORIGIN+meta.path)+'">']:[]),
    ...(meta.mature?['<meta name="rating" content="adult">']:[]),
    '<meta property="og:title" content="'+escapeHtml(title)+'">',
    '<meta property="og:description" content="'+escapeHtml(description)+'">',
    '<meta property="og:image" content="'+escapeHtml(meta.image||DEFAULT_IMAGE)+'">',
    '<meta name="twitter:title" content="'+escapeHtml(title)+'">',
    '<meta name="twitter:description" content="'+escapeHtml(description)+'">',
    '<meta name="twitter:image" content="'+escapeHtml(meta.image||DEFAULT_IMAGE)+'">',
  ];
  return shell.replace(/<title>[\s\S]*?<\/title>/i,'')
    .replace(/<meta\s+(?:name|property)="(?:description|robots|rating|og:(?:title|description|url|image(?::(?:type|width|height))?)|twitter:(?:title|description|image))"[^>]*>/gi,'')
    .replace(/<link\s+rel="canonical"[^>]*>/gi,'')
    .replace('</head>',()=>tags.join('\n')+'\n</head>')
    .replace('<div id="root"></div>',()=>'<div id="root">'+(meta.body?wrap(meta.title,meta.body):'')+'</div>');
}
export async function serveSeo(request: Request, env: { ASSETS: {fetch(input: string):Promise<Response>}; PUBLIC_API_URL?: string }, fetcher: typeof fetch = fetch): Promise<Response> {
  const url=new URL(request.url);
  const shell=await env.ASSETS.fetch(new URL('/',url).toString());
  if(!shell.ok)return shell;
  const apiBase=(env.PUBLIC_API_URL||'https://api.rongnho.shop').replace(/\/+$/,'');
  const api: Api=async <T>(path:string):Promise<T>=>{
    const response=await fetcher(apiBase+path,{headers:{Accept:'application/json'},signal:AbortSignal.timeout(3500)});
    if(!response.ok)throw new ApiFailure(response.status);
    return await response.json() as T;
  };
  let meta:Meta;
  try {
    const result=await resolvePage(url,api);
    if('redirect' in result)return new Response(null,{status:301,headers:{Location:ORIGIN+result.redirect,'Cache-Control':'public, max-age=60'}});
    meta=result;
  } catch (error) {
    meta=error instanceof ApiFailure && error.status===404?missing():{title:'Rồng Nhỏ',description:HOME_DESCRIPTION,body:para('Đang kết nối dữ liệu truyện. Vui lòng thử lại.'),status:503};
  }
  const status=meta.status??200;
  const headers=new Headers(shell.headers);
  for(const key of ['Content-Length','Content-Encoding','ETag','Last-Modified','Location','X-Robots-Tag'])headers.delete(key);
  headers.set('Content-Type','text/html; charset=utf-8');
  headers.set('Cache-Control',status===200&&!meta.noindex?'public, max-age=30':'no-store');
  if(meta.noindex)headers.set('X-Robots-Tag','noindex, follow');
  if(status===503)headers.set('Retry-After','60');
  const html=renderHtml(await shell.text(),meta);
  return new Response(request.method==='HEAD'?null:html,{status,headers});
}
