import { isSeoPath, serveSeo } from '../edge/seo';

// Existing sitemap and workspace functions continue to handle their own paths.
export async function onRequest(context: {
  request: Request;
  env: { ASSETS: { fetch(input: string): Promise<Response> }; PUBLIC_API_URL?: string };
  next(): Promise<Response>;
}): Promise<Response> {
  const { request, env } = context;
  const path = new URL(request.url).pathname.replace(/\/+$/, '') || '/';
  if (!['GET', 'HEAD'].includes(request.method) || !isSeoPath(path)) return context.next();
  return serveSeo(request, env);
}
