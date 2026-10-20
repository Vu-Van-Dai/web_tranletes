export async function onRequest(context: {
  request: Request;
  env: { PUBLIC_API_URL?: string };
}): Promise<Response> {
  // Cloudflare Pages serves the SPA on the main domain. Proxy this reserved path
  // to the API so the dynamic database-backed sitemap is available at /sitemap.xml.
  const apiBase = (context.env.PUBLIC_API_URL || "https://api.rongnho.shop").replace(/\/+$/, "");

  try {
    const upstream = await fetch(`${apiBase}/sitemap.xml`, {
      headers: { Accept: "application/xml" },
    });

    if (!upstream.ok) {
      return new Response(`Sitemap upstream returned HTTP ${upstream.status}`, {
        status: 502,
        headers: { "Content-Type": "text/plain; charset=utf-8", "Cache-Control": "no-store" },
      });
    }

    return new Response(upstream.body, {
      status: 200,
      headers: {
        "Content-Type": upstream.headers.get("Content-Type") || "application/xml; charset=utf-8",
        "Cache-Control": "public, max-age=300",
        "X-Robots-Tag": "all",
      },
    });
  } catch {
    return new Response("Sitemap is temporarily unavailable", {
      status: 502,
      headers: { "Content-Type": "text/plain; charset=utf-8", "Cache-Control": "no-store" },
    });
  }
}
