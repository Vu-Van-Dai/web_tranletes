import { describe, expect, it } from 'vitest';
import { AD_FRAME_HTML, adFrameResponse } from '../../edge/adFrame';
import { serveSeo } from '../../edge/seo';

describe('isolated ad document', () => {
  it('allows eval only inside the opaque sandbox and cannot be indexed', async () => {
    const response = adFrameResponse('GET');
    const csp = response.headers.get('Content-Security-Policy')!;
    expect(response.status).toBe(200);
    expect(csp).toContain("'unsafe-eval'");
    expect(csp).toContain("frame-ancestors 'self'");
    expect(csp).toContain('sandbox allow-scripts');
    expect(csp).not.toContain('allow-same-origin');
    expect(csp).not.toContain('allow-top-navigation');
    expect(response.headers.get('X-Frame-Options')).toBe('SAMEORIGIN');
    expect(response.headers.get('Cache-Control')).toBe('no-store, no-transform');
    expect(response.headers.get('X-Robots-Tag')).toContain('noindex');
    expect(await response.text()).toBe(AD_FRAME_HTML);
    expect(AD_FRAME_HTML).toContain('event.source !== parent || event.origin !== origin');
    expect(AD_FRAME_HTML).toContain('if (window.parent === window) return');
  });
  it('handles HEAD and rejects other methods', async () => {
    expect(await adFrameResponse('HEAD').text()).toBe('');
    expect(adFrameResponse('POST').status).toBe(405);
  });
  it('does not relax the public application CSP', async () => {
    const result = await serveSeo(new Request('https://rongnho.shop/gioi-thieu'), {
      ASSETS: {fetch: async () => new Response('<html><head></head><body><div id="root"></div></body></html>')},
    });
    expect(result.headers.get('Content-Security-Policy')).not.toContain("'unsafe-eval'");
    expect(result.headers.get('X-Frame-Options')).toBe('DENY');
  });
});
