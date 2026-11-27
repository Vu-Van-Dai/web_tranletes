import type { IncomingMessage, ServerResponse } from 'node:http';
import type { Plugin } from 'vite';
import { AD_FRAME_PATH, AD_FRAME_HEADERS, AD_FRAME_HTML } from './adFrame';

// Match production headers in local development and Vite preview.
export function adFrameDev(): Plugin {
  function serve(req: IncomingMessage, res: ServerResponse, next: () => void) {
    if (req.url?.split('?')[0] !== AD_FRAME_PATH) return next();
    const method = req.method ?? 'GET';
    const allowed = method === 'GET' || method === 'HEAD';
    res.writeHead(allowed ? 200 : 405, allowed ? AD_FRAME_HEADERS : { ...AD_FRAME_HEADERS, Allow: 'GET, HEAD' });
    res.end(method === 'GET' ? AD_FRAME_HTML : undefined);
  }
  return {
    name: 'rongnho-ad-frame',
    configureServer(server) { server.middlewares.use(serve); },
    configurePreviewServer(server) { server.middlewares.use(serve); },
  };
}
