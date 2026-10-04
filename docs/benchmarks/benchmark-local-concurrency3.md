# Benchmark — 2026-10-03 15:33 UTC

- Target `http://localhost:5182`, server processors visible to .NET: **2**, image concurrency **3**, store `local`.
- Sample chapter: 20 PNG pages 1000×1500 (96.2 MB zip) → package **8.95 MB**, processed in **3.2 s** (status `succeeded`).

| Scenario | Concurrency | Requests | Errors | Req/s | p50 ms | p95 ms | p99 ms | Server CPU | Server RAM | Max DB conns |
|---|---|---|---|---|---|---|---|---|---|---|
| 10 users: login + browse | 10 | 50 | 0 | 121.1 | 43.4 | 230.8 | 257.8 | 95% of 2 | 192 MB | 11 |
| 50 users: mixed browse/search/me | 50 | 500 | 0 | 255.8 | 190.1 | 276.3 | 393.2 | 128% of 2 | 217 MB | 24 |
| 100 readers: metadata | 100 | 500 | 0 | 596.3 | 166.3 | 214.4 | 322.0 | 91% of 2 | 224 MB | 31 |
| 100 readers: package download (origin, no CDN) | 100 | 100 | 0 | 80.4 | 1231.9 | 1238.0 | 1238.8 | 166% of 2 | 231 MB | 31 |

## 10 concurrent uploads (20 pages each)

- All jobs: 10 succeeded; wall time **31.4 s**; per-chapter latency (queue + processing) min **4.8 s**, max **31.3 s**.
- Max concurrent image conversions observed: **3** (limit 3) — bounded as designed.
- Server CPU during processing: **156%** of available cores; RAM after: **270 MB**.

## Read latency while 4 chapters are processing

| Scenario | Concurrency | Requests | Errors | Req/s | p50 ms | p95 ms | p99 ms | Server CPU | Server RAM | Max DB conns |
|---|---|---|---|---|---|---|---|---|---|---|
| 50 readers during processing | 50 | 500 | 0 | 490.7 | 97.4 | 170.2 | 194.4 | 182% of 2 | 293 MB | 31 |

- 2 MB JSON body to `/api/auth/login` → **413** (expected 413, Kestrel MaxRequestBodySize = 1 MB).
