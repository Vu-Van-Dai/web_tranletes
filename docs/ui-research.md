# UI research — reference sites (UX conventions only)

**Method.** `frontend/scripts/ui-research.mjs` (Playwright, Chromium 1.63) visited the home, comic-detail and
reader pages of the four reference sites on desktop (1366×900) and mobile (Pixel 7). Images, media and fonts were
**blocked**, so no artwork was downloaded or captured; only DOM structure, computed styles, navigation labels and
DevTools signals (console errors, failed requests) were recorded. Raw output stays in the git-ignored `research/`.

References: meothamthuy123.com · truyentranhdammyy.site · dualeotruyenpet.com · teamsany.com.
**Nothing (branding, logos, colours, artwork, code) is copied** — the findings below only inform an original design.

## What readers on Vietnamese comic sites are used to (keep the convention)

| Area | Observed convention | Our decision |
|---|---|---|
| Home | A "Mới cập nhật / Chap mới cập nhật" feed is the primary block; secondary blocks "Truyện mới", "Truyện hoàn" | Home = "Mới cập nhật" grid first, then "Truyện mới", "Đã hoàn thành", genres, translation groups |
| Cards | Cover + title + **2 latest chapters with relative time** ("9 giờ trước"); "NEW" badge | Same information density; relative time via `Intl.RelativeTimeFormat('vi')`; "Mới" badge < 48 h |
| Grid | 4 columns desktop, 2 columns mobile | 2 → 3 → 4 → 6 columns (container widths), stable 3:4 cover ratio |
| Genres | Genres are first-class navigation (top menu) | "Thể loại" menu + chips + `/the-loai/:slug` |
| Reader | "Chap trước / Chap tiếp" + full chapter dropdown, top **and** bottom; one site offers "List / Paged" style | Same controls, plus keyboard (←/→, [ ]) and tap zones; vertical **and** page-by-page modes |
| Reader extras | "Báo lỗi chap", comments | Out of scope for v1 (listed as next steps) |
| Groups | Translation teams are visible brands | Public "Nhóm dịch" pages with their comics |

## What we deliberately do differently (problems observed)

| Observation | Impact | Our design |
|---|---|---|
| Low-contrast body text (`#888` on white / pastel) on 3/4 sites | WCAG failure, hard to read | Tokens checked for ≥ 4.5:1 body contrast in both themes |
| Search placeholder "Search..." in English on 3/4 sites | Inconsistent localisation | 100 % Vietnamese copy, diacritic-insensitive search |
| Non-sticky headers; reader controls scroll away | Navigation lost on long chapters (pages 17 000 px tall) | Auto-hiding sticky reader bars, progress indicator |
| Chapter dropdown lists hundreds of mixed, unsorted labels | Hard to find a chapter | Numeric chapter model (`10.5`), sorted, searchable chapter list |
| 15+ console errors and failed requests on every site (ads/trackers) | Slow, fragile pages | Zero third-party scripts; E2E tests fail on any console error |
| Every page image requested separately | Many requests, no integrity check | One immutable CPK1 package per chapter, SHA-256 verified, windowed rendering |
| Decorative/cursive fonts (Pangolin, Quicksand) | Poor legibility of Vietnamese diacritics at small sizes | **Be Vietnam Pro** (designed for Vietnamese), self-hosted subsets |

## Not observable (designed from first principles)

Admin dashboard, translation-house workspace, upload UI and processing status are private on the reference sites.
They follow common dashboard patterns: sidebar + top bar, KPI tiles, data tables that collapse to cards on mobile,
drag-and-drop upload with byte progress, then live processing progress (polling) with Vietnamese error messages.

## Original visual direction — "mực & giấy" (ink & paper)

- Light theme: warm paper surfaces, near-black ink text. Dark theme: deep ink-navy; the reader defaults to dark.
- One accent: vermilion (seal-stamp red) for primary actions and "Mới" badges; teal only for success states.
- Rounded cards (12–16 px), generous spacing, no gradients or decorative banners.
- States for every list: skeletons while loading, illustrated empty states, error states with "Thử lại" + request id.
