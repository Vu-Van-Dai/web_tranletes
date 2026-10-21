import { describe, expect, it } from 'vitest';
import { isAdFreePath, pageGroupOf } from '@/features/ads/Ads';
import { chapterNumberFromName } from '@/features/house/BulkUploadDialog';

describe('ad placement', () => {
  it('keeps the house workspace and the admin console ad-free', () => {
    expect(isAdFreePath('/quan-ly-nhom')).toBe(true);
    expect(isAdFreePath('/quan-ly-nhom/truyen/abc')).toBe(true);
    expect(isAdFreePath('/quan-tri/cai-dat')).toBe(true);
    expect(isAdFreePath('/')).toBe(false);
    expect(isAdFreePath('/doc/123')).toBe(false);
    expect(isAdFreePath('/quan-tri-vien')).toBe(false);
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
