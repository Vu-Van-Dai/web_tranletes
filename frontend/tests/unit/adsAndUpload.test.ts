import { isAllowedAdUnit, isDirectBanner, requiresDirectScript } from '@/features/ads/providers';
import { parseDirectTag } from '@/features/ads/directCreative';
import type { AdUnit } from '@/types/api';
import { describe, expect, it } from 'vitest';
import { AD_TRIGGER_SELECTOR, isAdFreePath, pageGroupOf } from '@/features/ads/Ads';
import { comicLink, chapterLink, chapterRoute } from '@/app/paths';
import { chapterNumberFromName } from '@/features/house/BulkUploadDialog';

describe('public URL compatibility', () => {
  it('keeps old API responses usable and preserves decimal chapters', () => {
    expect(comicLink({ id: 'abc', slug: 'ten' })).toBe('/truyen/abc/ten');
    const comic = {id:'abc',slug:'ten',publicPath:'/truyen/nhom/ten'};
    expect(comicLink(comic)).toBe(comic.publicPath);
    expect(chapterLink({id:'def',number:1.5},comic)).toBe('/doc/nhom/ten/chuong-1.5');
    expect(chapterRoute('/doc/nhom/ten/chuong-1.5')).toEqual({house:'nhom',slug:'ten',number:'1.5'});
  });
});
describe('ad placement', () => {
  it('keeps the house workspace and the admin console ad-free', () => {
    expect(isAdFreePath('/quan-ly-nhom')).toBe(true);
    expect(isAdFreePath('/quan-ly-nhom/truyen/abc')).toBe(true);
    expect(isAdFreePath('/quan-tri/cai-dat')).toBe(true);
    expect(isAdFreePath('/')).toBe(false);
    expect(isAdFreePath('/doc/123')).toBe(false);
    expect(isAdFreePath('/quan-tri-vien')).toBe(false);
    for (const path of ['/dang-nhap', '/dang-ky', '/tu-truyen', '/lien-he']) expect(isAdFreePath(path)).toBe(true);
  });

  it('maps public URLs to page groups', () => {
    expect(pageGroupOf('/')).toBe('home');
    expect(pageGroupOf('/doc/0193')).toBe('reader');
    expect(pageGroupOf('/truyen/0193/ten-truyen')).toBe('comic');
    expect(pageGroupOf('/truyen')).toBe('browse');
    expect(isAdFreePath('/tim-kiem')).toBe(false);
    expect(pageGroupOf('/tim-kiem')).toBe('browse');
    expect(pageGroupOf('/the-loai/hai-huoc')).toBe('browse');
    expect(pageGroupOf('/nhom-dich/rong-nho')).toBe('house');
    expect(pageGroupOf('/tu-truyen')).toBe('library');
    expect(pageGroupOf('/dang-nhap')).toBe('other');
  });
});

describe('bulk upload chapter numbers', () => {
  it.each([
    ['Chapter 12.zip', 12],
    ['ch_012.5.cbz', 12.5],
    ['Chương 7 - Gặp lại.zip', 7],
    ['Tu ke bat chuoc - chap 3.zip', 3],
    ['15.zip', 15],
    ['Truyen 2 phan 4.zip', 4],
    ['10,5.zip', 10.5],
    ['khong-co-so.zip', null],
  ])('%s → %s', (name, expected) => {
    expect(chapterNumberFromName(name)).toBe(expected);
  });
});

describe('new-tab ad triggers', () => {
  it('treats story links, chapter links and marked buttons as content clicks', () => {
    document.body.innerHTML = `
      <a id="story" href="/truyen/0193/ten"><span id="inner">Truyện</span></a>
      <a id="chapter" href="/doc/0193">Chương 1</a>
      <a id="browse" href="/truyen">Danh sách</a>
      <button id="follow" data-ad-trigger>Theo dõi</button>
      <button id="theme">Đổi nền</button>`;
    const hit = (id: string) => document.getElementById(id)!.closest(AD_TRIGGER_SELECTOR) !== null;
    expect(hit('story')).toBe(true);
    expect(hit('inner')).toBe(true);
    expect(hit('chapter')).toBe(true);
    expect(hit('follow')).toBe(true);
    expect(hit('browse')).toBe(false);
    expect(hit('theme')).toBe(false);
  });
});

describe('ad provider controls', () => {
  const script = { id:'script',enabled:true,format:'script',provider:'custom',scriptApproved:true } as AdUnit;
  const config = {enabled:true,units:[script]};
  it('requires global and per-script permission and respects the provider kill switch', () => {
    expect(isAllowedAdUnit(config,script)).toBe(false);
    expect(isAllowedAdUnit({...config,allowDirectScripts:true},script)).toBe(true);
    expect(isAllowedAdUnit({...config,allowDirectScripts:true,providerEnabled:{custom:false}},script)).toBe(false);
    expect(isAllowedAdUnit({...config,allowDirectScripts:true},{...script,scriptApproved:false})).toBe(false);
  });
});

describe('approved direct banners', () => {
  const banner = {id:'bn',enabled:true,format:'banner',provider:'adsterra',html:'<script async src="https://example.com/ad.js"></script><div id="container"></div>',scriptApproved:true} as AdUnit;
  it('defaults to isolation, requires global permission for direct execution', () => {
    expect(isDirectBanner(banner)).toBe(true);
    expect(requiresDirectScript(banner)).toBe(true);
    expect(isAllowedAdUnit({enabled:true,units:[banner]},banner)).toBe(false);
    expect(isAllowedAdUnit({enabled:true,units:[banner],allowDirectScripts:true},banner)).toBe(true);
    expect(isAllowedAdUnit({enabled:true,units:[banner],allowDirectScripts:true,providerEnabled:{adsterra:false}},banner)).toBe(false);
    expect(isAllowedAdUnit({enabled:true,units:[banner]},{...banner,scriptApproved:false})).toBe(true);
  });
  it('accepts official async and sync tag shapes, rejects duplicate and non-HTTPS scripts', () => {
    expect(parseDirectTag(banner.html!).sync).toBe(false);
    expect(parseDirectTag('<script>atOptions={}</script><script src="https://example.com/ad.js"></script>').sync).toBe(true);
    expect(() => parseDirectTag('<script src="http://example.com/ad.js"></script>')).toThrow();
    expect(() => parseDirectTag('<script src="https://example.com/ad.js"></script><script src="https://example.com/ad.js"></script>')).toThrow();
    expect(() => parseDirectTag('<div><script src="https://example.com/ad.js"></script></div>')).toThrow();
  });
});

import { dimensions, fitsSlot, weightedPick, standaloneLink, suggestedAdsterraLayout } from '@/features/ads/placement';
describe('responsive ad slots',()=>{
  const wide={id:'wide',name:'Banner 728x90',format:'banner',provider:'adsterra',enabled:true,scriptApproved:false,html:"<script>atOptions={'width':728,'height':90}</script>",slots:['home_top']} as AdUnit;
  it('reads fixed dimensions and preserves approval while assigning a desktop slot',()=>{
    expect(dimensions(wide)).toEqual({width:728,height:90});
    const result=suggestedAdsterraLayout(wide);
    expect(result.slots).toEqual(['site_top']);
    expect(result.scriptApproved).toBe(false);
    expect(fitsSlot(result,'site_top',340,390)).toBe(false);
    expect(fitsSlot(result,'site_top',1000,1280)).toBe(true);
  });
  it('requires enough space for side rails',()=>{
    const rail={...wide,width:160,height:600,slots:['side_left']} as AdUnit;
    expect(fitsSlot(rail,'side_left',162,1600)).toBe(false);
    expect(fitsSlot(rail,'side_left',162,1700)).toBe(true);
  });
  it('rotates by relative weight and recognizes only an explicit safe link',()=>{
    expect(weightedPick([{id:'a',weight:1},{id:'b',weight:3}],0.5)?.id).toBe('b');
    expect(standaloneLink('<a href="https://example.com">Ad</a>')?.label).toBe('Ad');
    expect(standaloneLink('<a href="https://example.com" onclick="alert(1)">Ad</a>')).toBeNull();
  });
});

import { directTagKind } from '@/features/ads/directCreative';
import { adKind, suggestedPopunderTest, imageFallback } from '@/features/ads/placement';
describe('network ad compatibility',()=>{
  it('allows independent async option queues while keeping sync/global configs conservative',()=>{
    expect(directTagKind('<script>atAsyncOptions.push({key:"a"})</script><script async src="//example.com/a.js"></script><div id="a"></div>')).toBe('async');
    expect(directTagKind('<script>atOptions={}</script><script async src="https://example.com/a.js"></script>')).toBe('sync');
    expect(directTagKind('<script src="http://example.com/a.js"></script>')).toBe('invalid');
  });
  it('prepares a popunder test without approving or enabling code',()=>{
    const pop={id:'pop',name:'Adsterra - Popunder',provider:'adsterra',format:'script',enabled:false,scriptApproved:false} as AdUnit;
    expect(adKind(pop)).toBe('popunder');
    expect(suggestedPopunderTest(pop)).toMatchObject({enabled:false,scriptApproved:false,pages:['home','browse','comic','reader','house'],frequencyMinutes:2});
    expect(suggestedPopunderTest({...pop,name:'Adsterra - Smartlink',format:'banner',enabled:true}).enabled).toBe(false);
  });
  it('does not request another paid network as a fallback',()=>{
    const ad={id:'first',provider:'adsterra',html:'<script></script>'} as AdUnit;
    const own={id:'own',provider:'direct',imageUrl:'https://example.com/a.webp',targetUrl:'https://example.com',html:null} as AdUnit;
    expect(imageFallback([ad],ad.id,0)).toBeUndefined();
    expect(imageFallback([ad,own],ad.id,0)?.id).toBe('own');
  });
});

import { singleNetworkScript, adsterraScriptPosition } from '@/features/ads/placement';
describe('Adsterra publisher tag placement',()=>{
  const tag=(src:string)=>'<script data-cfasync="false" src="'+src+'"></script>';
  const script={id:'example',name:'Network',provider:'adsterra',format:'script',enabled:true} as AdUnit;
  it('routes the supplied tag families to their documented positions without executing them',()=>{
    const pop={...script,html:tag('https://abscloud.org/1/'+'a'.repeat(32))};
    const social={...script,html:tag('https://bauval.org/14/'+'b'.repeat(32))};
    expect(adsterraScriptPosition(pop)).toBe('head');
    expect(adsterraScriptPosition(social)).toBe('body');
    expect(adKind(pop)).toBe('popunder');
    expect(adKind(social)).toBe('socialbar');
    expect(singleNetworkScript(pop.html)?.getAttribute('data-cfasync')).toBe('false');
    expect(adsterraScriptPosition({...pop,provider:'custom'})).toBeNull();
    expect(adsterraScriptPosition({...pop,html:pop.html+'<div></div>'})).toBeNull();
    expect(singleNetworkScript('<script>alert(1)</script>')).toBeNull();
    expect(singleNetworkScript(tag('http://example.com/ad.js'))).toBeNull();
  });
  it('classifies all six fixed banner shapes as sync, regardless of key or size',()=>{
    for(const [width,height] of [[468,60],[300,250],[160,600],[160,300],[320,50],[728,90]]){
      const html='<script>atOptions={key:"example",format:"iframe",height:'+height+',width:'+width+',params:{}}</script><script data-cfasync="false" src="https://bauval.org/22/example"></script>';
      expect(directTagKind(html)).toBe('sync');
      expect(dimensions({...script,format:'banner',html})).toEqual({width,height});
    }
  });
});

import { assignedBannerSlot, isKnownAdsterraTag, eligibleOnClickLink, isPublicContentClick } from '@/features/ads/placement';
describe('public ad placements and opt-in OnClick',()=>{
  it('assigns a reused native tag to one preferred slot on each page',()=>{
    const n={provider:'adsterra',format:'banner',html:'<script async src="https://bauval.org/21/'+ 'a'.repeat(32)+'"></script>',slots:['site_top','home_top','home_middle','comic_chapters','reader_bottom']} as AdUnit;
    expect(assignedBannerSlot(n,'home')).toBe('home_middle');
    expect(assignedBannerSlot(n,'comic')).toBe('comic_chapters');
    expect(assignedBannerSlot(n,'reader')).toBe('reader_bottom');
    expect(isKnownAdsterraTag(n)).toBe(true);
  });
  it('only accepts supported HTTPS new-tab URLs',()=>{
    const ad={provider:'hilltopads',format:'newtab',targetUrl:'https://example.com/pop'} as AdUnit;
    expect(eligibleOnClickLink(ad)).toBe(true);
    expect(eligibleOnClickLink({...ad,provider:'adsterra'})).toBe(false);
    expect(eligibleOnClickLink({...ad,targetUrl:'javascript:alert(1)'})).toBe(false);
  });
  it('limits clicks to public content and excludes management and forms',()=>{
    document.body.innerHTML='<a id="logo" href="/">Logo</a><a id="list" href="/truyen">All</a><a id="house" href="/nhom-dich/rong-nho">House</a><a id="admin" href="/quan-tri">Admin</a><a id="studio" href="/quan-ly-nhom">Studio</a><button id="genre" data-ad-trigger>Genre</button><form><button id="form" data-ad-trigger>Form</button></form><div data-ad><a id="ad" href="/truyen">Ad</a></div>';
    for(const id of ['logo','list','house','genre'])expect(isPublicContentClick(document.getElementById(id)!)).toBe(true);
    for(const id of ['admin','studio','form','ad'])expect(isPublicContentClick(document.getElementById(id)!)).toBe(false);
  });
});


describe('HilltopAds MultiTag bootstrap compatibility',()=>{
  const bootstrap=(url:string)=>'<script>(function(ctpa){var d = document, s = d.createElement("script"), l = d.currentScript || d.scripts[d.scripts.length - 1]; s.settings = ctpa || {}; s.src = '+JSON.stringify(url).replaceAll('/','\\/')+'; s.async = true; s.referrerPolicy = "no-referrer-when-downgrade"; l.parentNode.insertBefore(s, l);})({});</script>';
  it('recognizes the supplied inline bootstrap without executing it',()=>{
    const tag=bootstrap('//same-upstairs.com/example/zone-a');
    expect(directTagKind(tag,'hilltopads')).toBe('async');
    expect(parseDirectTag(tag,'hilltopads').urls).toEqual(['https://same-upstairs.com/example/zone-a']);
    expect(directTagKind(tag,'adsterra')).toBe('invalid');
  });
  it('rejects unrecognized or insecure inline-only scripts',()=>{
    expect(directTagKind(bootstrap('http://same-upstairs.com/a'),'hilltopads')).toBe('invalid');
    expect(directTagKind(bootstrap('//example.com/a'),'hilltopads')).toBe('invalid');
    expect(directTagKind('<script>alert(1)</script>','hilltopads')).toBe('invalid');
    expect(directTagKind(bootstrap('//same-upstairs.com/a')+'<script>alert(1)</script>','hilltopads')).toBe('invalid');
  });
});

import { suggestedHilltopLayout, ONCLICK_MINUTES } from '@/features/ads/placement';
describe('HilltopAds home layout preset',()=>{
  it('uses independent top banners and the middle native banner, without approving anything',()=>{
    const h={provider:'hilltopads',format:'banner',name:'RongNho - Banner Desktop',placementId:'7669125',enabled:false,scriptApproved:false} as AdUnit;
    expect(suggestedHilltopLayout(h)).toMatchObject({slots:['site_top'],device:'desktop',width:300,height:250,enabled:false,scriptApproved:false});
    expect(suggestedHilltopLayout({...h,name:'RongNho - Banner Mobile',placementId:'7669141'})).toMatchObject({slots:['site_top'],device:'mobile',width:300,height:100});
    expect(suggestedHilltopLayout({...h,provider:'adsterra',name:'Native Banner'}).slots).toContain('home_middle');
    expect(suggestedHilltopLayout({...h,provider:'adsterra',name:'Smartlink',enabled:true}).enabled).toBe(false);
    expect(suggestedHilltopLayout({...h,provider:'adsterra',format:'script',name:'Popunder',enabled:true}).enabled).toBe(false);
    expect(suggestedHilltopLayout({...h,format:'newtab',targetUrl:'https://example.com/official',name:'OnClick'}).frequencyMinutes).toBe(ONCLICK_MINUTES);
    expect(suggestedHilltopLayout({...h,provider:'adsterra',name:'Banner 728x90',slots:['site_top'],enabled:true}).enabled).toBe(false);
    expect(ONCLICK_MINUTES).toBe(2);
  });
});

import { bannerFallback } from '@/features/ads/placement';
describe('cross-provider banner fallback',()=>{
  it('prefers a distinct provider and does not retry the failed creative',()=>{
    const ad={id:'a',provider:'adsterra',html:'<script src="https://example.com/a"></script>',format:'banner',enabled:true} as AdUnit;
    const other={...ad,id:'b',provider:'hilltopads',html:'<script src="https://example.com/b"></script>'} as AdUnit;
    expect(bannerFallback([ad,{...ad,id:'same'},other],ad.id,0)?.id).toBe('b');
    expect(bannerFallback([ad,{...other,html:ad.html}],ad.id,0)).toBeUndefined();
    expect(bannerFallback([ad],ad.id,0)).toBeUndefined();
  });
});

import { isVisualBanner, footerBannerReservation } from '@/features/ads/placement';
describe('visual footer banner inventory',()=>{
  const banner={id:'a',name:'Native Banner',provider:'adsterra',format:'banner',enabled:true,html:'<script src="https://example.com/a.js"></script>',slots:['home_middle','site_bottom']} as AdUnit;
  const link={...banner,id:'link',name:'Adsterra Smartlink',html:'<a href="https://example.com">Ad</a>',slots:['site_bottom']} as AdUnit;
  it('does not allocate Smartlink or plain hyperlinks to banner slots',()=>{
    expect(isVisualBanner(link)).toBe(false);
    expect(fitsSlot(link,'site_bottom',1000,1280)).toBe(false);
    expect(isVisualBanner({...link,provider:'custom',name:'Text link'})).toBe(false);
    expect(fitsSlot(banner,'site_bottom',1000,1280)).toBe(true);
    expect(suggestedAdsterraLayout(link).slots).toEqual([]);
  });
  it('reserves a separate footer tag only when an earlier slot has spare inventory',()=>{
    const other={...banner,id:'b',provider:'hilltopads',html:'<script src="https://example.com/b.js"></script>'} as AdUnit;
    expect(footerBannerReservation([banner,link],'home',1280)).toBeUndefined();
    expect(footerBannerReservation([banner,{...other,html:banner.html}],'home',1280)).toBeUndefined();
    expect(footerBannerReservation([banner,other],'home',1280)).toBe('a');
    expect(footerBannerReservation([banner,{...other,slots:['site_bottom']}],'home',1280)).toBeUndefined();
    expect(footerBannerReservation([banner,other],'reader',1280)).toBeUndefined();
  });
});
