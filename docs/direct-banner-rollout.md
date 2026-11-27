# Direct banner integration

- Keep app CSP unchanged. Inline and popup creatives are sandboxed by default.
- Deploy the backend first or together with the frontend. No database schema migration: the existing scriptApproved JSON field is also accepted for HTML banners. Older backend versions drop that flag.
- In Admin > Quảng cáo, choose Adsterra, Banner trong trang, Mã HTML, paste the official Native/JSAsync tag, select a single slot, check direct banner approval, enable the provider and the global script switch, save.
- Test as a logged-out visitor. Direct banners and existing global network scripts are not mounted for authenticated/loading sessions. Route and session transitions trigger a document reload after direct scripts have run. This reduces exposure but is not an isolation or security guarantee: third-party JavaScript has the page's privileges. Only approved lawful/general-audience creatives, no secret tokens or payment data. Use the sandbox for untrusted HTML.
- Prefer the official JSAsync tag. IFRAME SYNC is best effort (one synchronous banner per document, because of global atOptions). Do not override document.write, eval, cookie APIs or vendor tracking. If the official sync tag fails, ask the provider for JSAsync.
- Duplicate tag URLs are skipped per document. The loader collapses on a script error or if no plausible visible creative exists after the wait; it cannot inspect cross-origin frames or assert a counted impression.
- The global script switch and provider switches stop direct units. Uncheck banner approval and save to use the existing isolated iframe instead.
- Test real ad delivery without clicking your own ads. CI only uses a synthetic provider script. No changes to R2, image Worker, SEO, sitemap, package URL, or ad revenue reports.
