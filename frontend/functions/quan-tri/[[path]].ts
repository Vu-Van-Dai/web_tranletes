interface AssetFetcher {
  fetch(input: string): Promise<Response>;
}

/**
 * Ad-free shell for the admin console (/quan-tri/*) and the house workspace (/quan-ly-nhom/*): serves
 * dist/workspace.html — the SPA without the AdSense tag — at the requested URL with status 200.
 * (A _redirects rewrite to /workspace.html does not work: Pages' pretty URLs answer it with a redirect to /workspace.)
 */
export async function onRequest(context: { request: Request; env: { ASSETS: AssetFetcher } }): Promise<Response> {
  const { request, env } = context;
  if (request.method !== 'GET' && request.method !== 'HEAD') {
    return new Response(null, { status: 405, headers: { Allow: 'GET, HEAD' } });
  }

  let shell = await env.ASSETS.fetch(new URL('/workspace', request.url).toString());
  // Pretty URLs may bounce between /workspace and /workspace.html: follow that inside the asset store.
  for (let hop = 0; hop < 2 && shell.status >= 300 && shell.status < 400; hop++) {
    const location = shell.headers.get('Location');
    if (!location) break;
    shell = await env.ASSETS.fetch(new URL(location, request.url).toString());
  }

  if (!shell.ok) {
    // Never break the app: fall back to the regular SPA shell.
    return env.ASSETS.fetch(new URL('/', request.url).toString());
  }

  const headers = new Headers(shell.headers);
  headers.set('Cache-Control', 'no-cache');
  headers.set('X-Robots-Tag', 'noindex');
  headers.delete('Location');
  return new Response(request.method === 'HEAD' ? null : shell.body, { status: 200, headers });
}
