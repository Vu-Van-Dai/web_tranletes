import { adFrameResponse } from '../edge/adFrame';

// Do not copy the application's CSP or X-Frame-Options: DENY here.
// Pages Functions generate their own response headers.
export function onRequest(context: { request: Request }): Response {
  return adFrameResponse(context.request.method);
}
