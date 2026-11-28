import type { AdProvider, AdUnit, AdsConfig } from '@/types/api';

export const providerLabels: Record<AdProvider, string> = {
  direct: 'Quảng cáo trực tiếp', custom: 'Mạng tùy chỉnh', adsense: 'Google AdSense',
  adsterra: 'Adsterra', hilltopads: 'HilltopAds', clickadu: 'Clickadu', monetag: 'Monetag',
  exoclick: 'ExoClick', adcash: 'Adcash', other: 'Khác',
};
export function providerOf(unit: AdUnit): AdProvider {
  return unit.provider ?? (unit.format === 'script' || unit.html ? 'custom' : 'direct');
}
export function isAdcashBanner(unit: AdUnit): boolean {
  return unit.provider === 'adcash' && unit.format === 'banner' && /^\d{1,20}$/.test(unit.placementId ?? '');
}
export function isDirectBanner(unit: AdUnit): boolean {
  return unit.format === 'banner' && unit.scriptApproved === true &&
    (isAdcashBanner(unit) || (!!unit.html && !unit.imageUrl));
}
export function requiresDirectScript(unit: AdUnit): boolean {
  return unit.format === 'script' || isAdcashBanner(unit) ||
    (unit.format === 'banner' && !!unit.html && !unit.imageUrl && unit.scriptApproved === true);
}
export function isAllowedAdUnit(config: AdsConfig, unit: AdUnit): boolean {
  const provider = providerOf(unit);
  return config.enabled && unit.enabled && provider !== 'adsense' &&
    !(unit.provider === 'adcash' && !isAdcashBanner(unit)) &&
    config.providerEnabled?.[provider] !== false &&
    (!requiresDirectScript(unit) || (config.allowDirectScripts === true && unit.scriptApproved === true));
}
