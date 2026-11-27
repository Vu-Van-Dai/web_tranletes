import type { AdProvider, AdUnit, AdsConfig } from '@/types/api';

export const providerLabels: Record<AdProvider, string> = {
  direct: 'Quảng cáo trực tiếp', custom: 'Mạng tùy chỉnh', adsense: 'Google AdSense',
  adsterra: 'Adsterra', hilltopads: 'HilltopAds', clickadu: 'Clickadu', monetag: 'Monetag',
  exoclick: 'ExoClick', other: 'Khác',
};
export function providerOf(unit: AdUnit): AdProvider {
  return unit.provider ?? (unit.format === 'script' || unit.html ? 'custom' : 'direct');
}
export function isAllowedAdUnit(config: AdsConfig, unit: AdUnit): boolean {
  const provider = providerOf(unit);
  return config.enabled && unit.enabled && provider !== 'adsense' &&
    config.providerEnabled?.[provider] !== false &&
    (unit.format !== 'script' || (config.allowDirectScripts === true && unit.scriptApproved === true));
}
