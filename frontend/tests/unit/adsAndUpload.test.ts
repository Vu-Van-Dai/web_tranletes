import { isAllowedAdUnit } from '@/features/ads/providers';
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
    for (const path of ['/dang-nhap', '/dang-ky', '/tu-truyen', '/lien-he', '/tim-kiem']) expect(isAdFreePath(path)).toBe(true);
  });

  it('maps public URLs to page groups', () => {
    expect(pageGroupOf('/')).toBe('home');
    expect(pageGroupOf('/doc/0193')).toBe('reader');
    expect(pageGroupOf('/truyen/0193/ten-truyen')).toBe('comic');
    expect(pageGroupOf('/truyen')).toBe('browse');
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
