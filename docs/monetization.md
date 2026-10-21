# Ads, verified views and revenue

## 1. Adding ads to the site (admin → **Quản trị hệ thống → Quảng cáo**, `/quan-tri/quang-cao`)

1. Sign in as the SystemAdmin and open **Quảng cáo**.
2. Click **Thêm quảng cáo**, pick a format, fill it in, click **Xong**, then **Lưu thay đổi**.
   Readers see the change within about a minute (`/api/site` is cached for 60 s).
3. Tick **Bật quảng cáo trên toàn trang** to switch all ads on/off without deleting them.

| Format | What it does | Typical source |
|---|---|---|
| **Banner trong trang** (`banner`) | Inline block at the chosen positions: under the header on every page, home top/middle, browse, comic page, chapter list, house page, reader top / end of chapter. Several banners in one position rotate. | Direct deals (upload an image → stored as WebP, add the target link) or a network's banner HTML |
| **Pop-up** (`popup`) | Overlay panel on the chosen pages. *Có nút đóng ngay*: closable at once. *Bắt buộc xem rồi mới đóng*: the close button appears only after 3–60 s of the tab being visible. Optional delay and frequency cap (per browser). | Image + link or network HTML |
| **Mở tab mới khi bấm** (`newtab`) | The visitor's next click opens the link in a new tab (browsers block pop-ups that are not triggered by a click). Frequency cap per browser. | Your own link / a network's "direct link" |
| **Mã của mạng quảng cáo** (`script`) | Pastes the network's code into the public pages (popunder, social bar, in-page push, auto ads). | Adsterra, Monetag, PropellerAds, … |

Where ads appear: home, browse/search/genre, comic pages, the reader, house pages, library and other public pages —
for **every** visitor, including members of translation houses reading with their own accounts. The **house workspace
(`/quan-ly-nhom`) and the admin console (`/quan-tri`) never show ads**: no ad component is mounted there, and if a
network script was injected earlier in the same tab, entering those areas forces a reload so it stops running.

Notes:

- Banner/pop-up HTML runs in a sandboxed iframe (no access to the page or the reader's session). The "script" format runs
  in the page itself — paste only code from networks you trust.
- Google AdSense does not allow pop-ups/popunders and does not run inside sandboxed iframes; use the "script" format for
  its Auto ads snippet and follow its content policies.
- The Content-Security-Policy in `frontend/public/_headers` allows `https:` scripts and frames for this reason.
- Uploaded ad images are converted to WebP (max 1200×1200) and served from `img.<domain>/ads/{hash}.webp`.

### Google AdSense

- The AdSense tag (`ca-pub-9474823258875185`) is injected **literally into `<head>` of `dist/index.html` at build time**
  (`frontend/vite.config.ts`), which is what AdSense's ownership check and every public page receive.
  Override with `PUBLIC_ADSENSE_CLIENT=ca-pub-…`, or set it to an empty value to build without AdSense.
- `public/ads.txt` declares the publisher (`google.com, pub-9474823258875185, DIRECT, f08c47fec0942fa0`).
- The build also writes `dist/workspace.html` — the same page **without** the tag — and `public/_redirects` serves it for
  `/quan-ly-nhom/*` and `/quan-tri/*`, so the workspace and admin console stay ad-free. Moving from a public page to
  those areas inside the app forces one reload so the ad-free page is served.

## 2. Verified views (anti-inflation)

A view is counted — and paid — only when **all** checks pass:

1. When a chapter has loaded, the reader requests a **signed, single-use ticket** (`POST /api/chapters/{id}/view-ticket`).
   It binds the chapter, the issue time, the required reading time and the visitor (account, or network /24–/48 + browser).
   The signing key changes on every API restart.
2. The reader reports the view after the visitor has read: tab visible for the required time (pages × 1.5 s, 10–60 s)
   and at least half of the pages displayed. **The server re-checks the time from the ticket**, so editing the page or
   calling the API directly cannot shorten it.
3. Rejected: missing/forged/expired tickets, tickets used twice, tickets used from another machine/network, bot
   user agents (curl, python, headless browsers…), more than 60 counted views per visitor or 300 per IP per hour,
   and reads by **members of the house that owns the chapter**.
4. One counted view per visitor, chapter and day (Vietnam time), enforced in PostgreSQL (`view_receipts`), so restarts
   cannot double count.
5. Optional: set `TURNSTILE_SECRET_KEY` + `PUBLIC_TURNSTILE_SITE_KEY` (Cloudflare Turnstile, free) to require an
   invisible human check before a ticket is issued — the strongest protection against bot farms.

Rejected attempts are recorded per chapter (**Bị loại**) and per reason (admin report), so unusual patterns — e.g. a
house whose rejected share suddenly jumps — are visible before paying.

Thresholds are environment variables (`VIEW_*`, see `.env.example`).

## 3. Revenue

- Set **Đơn giá trả nhóm dịch (VNĐ / 1.000 lượt xem hợp lệ)** in **Cài đặt hệ thống**. Each verified view is credited at
  the rate in force when it is counted; changing the rate never rewrites past revenue.
- The ledger (`chapter_view_daily`) is per chapter and day; **series totals are the sum of their chapters** and house
  totals the sum of their series. It has no foreign keys, so deleting a chapter or comic keeps its payout history.
- **House workspace → Lượt xem & doanh thu** (`/quan-ly-nhom/thong-ke`): every member sees views; only owners see revenue.
- **Admin → Doanh thu** (`/quan-tri/doanh-thu`): one row per house (views, rejected views and share, revenue), the
  rejection reasons, and a drill-down per series/chapter. Both pages export CSV for payouts.
