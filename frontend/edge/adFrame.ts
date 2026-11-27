// A separate, opaque-origin document for inline ad network tags.
// Never apply this policy to the application or use allow-same-origin on the iframe.
export const AD_FRAME_PATH = '/ad-frame';
export const AD_FRAME_HEADERS: Record<string, string> = {
  'Content-Type': 'text/html; charset=utf-8',
  'Cache-Control': 'no-store',
  'X-Robots-Tag': 'noindex, nofollow',
  'X-Content-Type-Options': 'nosniff',
  'X-Frame-Options': 'SAMEORIGIN',
  'Referrer-Policy': 'strict-origin-when-cross-origin',
  'Permissions-Policy': 'camera=(), microphone=(), geolocation=()',
  'Content-Security-Policy': [
    "default-src 'none'",
    "script-src 'unsafe-inline' 'unsafe-eval' https:",
    "style-src 'unsafe-inline' https:",
    'img-src https: data: blob:',
    'frame-src https: blob: data:',
    'connect-src https:',
    'media-src https: blob:',
    'font-src https: data:',
    'form-action https:',
    "object-src 'none'",
    "base-uri 'none'",
    "frame-ancestors 'self'",
    'sandbox allow-scripts allow-popups allow-popups-to-escape-sandbox allow-forms',
  ].join('; '),
};

// The parent sends only the already-public creative, never credentials or user data.
// document.write in this dedicated document preserves parser order for synchronous
// banner tags; document.open does not remove the iframe sandbox.
export const AD_FRAME_HTML = `<!doctype html>
<html lang="vi"><head><meta charset="utf-8"><meta name="robots" content="noindex, nofollow"><title>Quảng cáo</title></head>
<body><script>
(function () {
  if (window.parent === window) return;
  const origin = location.origin;
  function init(event) {
    if (event.source !== parent || event.origin !== origin) return;
    const data = event.data;
    if (!data || data.type !== 'rn-ad-init' || typeof data.id !== 'string' ||
        !/^[a-zA-Z0-9_-]{1,128}$/.test(data.id) || typeof data.html !== 'string' || data.html.length > 100000) return;
    removeEventListener('message', init);
    const resize = '(function(){var id=' + JSON.stringify(data.id) +
      ';var send=function(){var body=document.body;if(!body)return;var visible=Array.from(body.querySelectorAll("iframe,img,video,a,object,embed,div,span,p")).some(function(el){var r=el.getBoundingClientRect(),s=getComputedStyle(el);return r.width>1&&r.height>1&&s.display!=="none"&&s.visibility!=="hidden"&&(el.tagName==="IMG"?el.complete&&el.naturalWidth>1:/^(IFRAME|VIDEO|OBJECT|EMBED)$/.test(el.tagName)||Array.from(el.childNodes).some(function(n){return n.nodeType===3&&n.textContent.trim().length>0}))});parent.postMessage({type:"rn-ad-size",id:id,h:Math.ceil(Math.max(body.scrollHeight,body.getBoundingClientRect().height)),visible:visible},' +
      JSON.stringify(origin) + ')};new ResizeObserver(send).observe(document.body);addEventListener("load",send,true);new MutationObserver(send).observe(document.body,{childList:true,subtree:true,characterData:true});send()})();';
    document.open();
    document.write('<!doctype html><html lang="vi"><head><meta charset="utf-8"><base target="_blank">' +
      '<style>html,body{margin:0;padding:0;background:transparent}body{display:flex;justify-content:center;align-items:flex-start}</style>' +
      '</head><body>' + data.html + '<scr' + 'ipt>' + resize + '</scr' + 'ipt></body></html>');
    document.close();
  }
  addEventListener('message', init);
})();
</script></body></html>`;

export function adFrameResponse(method: string): Response {
  if (method !== 'GET' && method !== 'HEAD') {
    return new Response(null, { status: 405, headers: { ...AD_FRAME_HEADERS, Allow: 'GET, HEAD' } });
  }
  return new Response(method === 'HEAD' ? null : AD_FRAME_HTML, { headers: AD_FRAME_HEADERS });
}
