import { describe, expect, it, vi } from 'vitest';
import { renderHtml, resolvePage, serveSeo } from '../../edge/seo';

const id='01a162fc-d82d-78f2-a84d-a424749eb8f4';
const chapter='01a16d4c-2f13-7ae2-85d7-ad569ecc25d3';
const canonical='/truyen/'+id+'/anh-nhin';
const shell='<html><head><title>Old</title><link rel="canonical" href="https://rongnho.shop/"><meta name="description" content="old"></head><body><div id="root"></div></body></html>';
const comic={id,slug:'anh-nhin',translatedTitle:'A & B <script>',description:'Mô tả "$&"',originalTitle:'Original',author:null,artist:null,coverUrl:null,isMature:false,house:{name:'Nhóm',slug:'nhom'},tags:[]};
const mockApi=async <T>(path:string):Promise<T> => (path.endsWith('/chapters')?[{id:chapter,number:1,title:null}]:comic) as T;
describe('edge public SEO',()=>{
  it('normalizes an outdated or omitted slug without a backend change',async()=>{
    expect(await resolvePage(new URL('https://rongnho.shop/truyen/'+id+'/old'),mockApi)).toEqual({redirect:canonical});
    expect(await resolvePage(new URL('https://rongnho.shop/truyen/'+id),mockApi)).toEqual({redirect:canonical});
  });
  it('renders unique canonical, escaped metadata and chapter links in the initial HTML',async()=>{
    const meta=await resolvePage(new URL('https://rongnho.shop'+canonical),mockApi);
    if('redirect' in meta)throw Error('unexpected redirect');
    const html=renderHtml(shell,meta);
    expect(html).toContain('href="https://rongnho.shop'+canonical+'"');
    expect(html).toContain('A &amp; B &lt;script&gt;');
    expect(html).toContain('href="/doc/'+chapter+'"');
    expect(html).not.toContain('href="https://rongnho.shop/"');
    expect(html).toContain('$&amp;');
  });
  it('switches to friendly paths only when the backend advertises them',async()=>{
    const friendly='/truyen/nhom/anh-nhin';
    const api=async <T>(path:string):Promise<T> => (path.endsWith('/chapters')?[{id:chapter,number:1,title:null}]:{...comic,publicPath:friendly}) as T;
    expect(await resolvePage(new URL('https://rongnho.shop'+canonical),api)).toEqual({redirect:friendly});
    const meta=await resolvePage(new URL('https://rongnho.shop'+friendly),api);
    if('redirect' in meta)throw Error('unexpected redirect');
    const html=renderHtml(shell,meta);
    expect(html).toContain('/doc/nhom/anh-nhin/chuong-1');
    expect(html).toContain('BreadcrumbList');
    expect(html).not.toContain('<script> ·');
  });
  it('returns real 404 for missing API resources and 503 for transient failures',async()=>{
    const env={ASSETS:{fetch:async()=>new Response(shell,{headers:{'Content-Type':'text/html'}})}};
    const request=new Request('https://rongnho.shop'+canonical);
    const notFound=await serveSeo(request,env,vi.fn(async()=>new Response(null,{status:404})));
    expect(notFound.status).toBe(404);
    expect(notFound.headers.get('X-Robots-Tag')).toContain('noindex');
    const failed=await serveSeo(request,env,vi.fn(async()=>new Response(null,{status:429})));
    expect(failed.status).toBe(503);
    expect(failed.headers.has('X-Robots-Tag')).toBe(false);
  });
  it('does not index private search pages or expose adult chapter content',async()=>{
    const search=await resolvePage(new URL('https://rongnho.shop/tim-kiem?q=abc'),mockApi);
    expect(search).toMatchObject({noindex:true});
    const adult=await resolvePage(new URL('https://rongnho.shop/doc/'+chapter),async <T>()=>({id:chapter,comicId:id,comicSlug:'anh-nhin',comicTitle:'Tên truyện',number:1,title:null,isMature:true,house:{name:'Nhóm'},prev:null,next:null}) as T);
    expect(adult).toMatchObject({noindex:true,mature:true});
  });
});
