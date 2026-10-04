# Benchmark — 2026-10-03 15:31 UTC

- Target `http://localhost:5182`, server processors visible to .NET: **2**, image concurrency **2**, store `local`.
- Sample chapter: 20 PNG pages 1000×1500 (96.2 MB zip) → package **8.95 MB**, processed in **4.1 s** (status `succeeded`).

| Scenario | Concurrency | Requests | Errors | Req/s | p50 ms | p95 ms | p99 ms | Server CPU | Server RAM | Max DB conns |
|---|---|---|---|---|---|---|---|---|---|---|
| 10 users: login + browse | 10 | 50 | 0 | 123.0 | 40.6 | 195.3 | 341.1 | 102% of 2 | 195 MB | 10 |
| 50 users: mixed browse/search/me | 50 | 500 | 0 | 228.1 | 200.3 | 346.9 | 556.4 | 112% of 2 | 200 MB | 27 |
| 100 readers: metadata | 100 | 500 | 0 | 607.0 | 168.1 | 204.8 | 236.7 | 99% of 2 | 210 MB | 31 |
| 100 readers: package download (origin, no CDN) | 100 | 100 | 0 | 78.3 | 1235.9 | 1268.8 | 1270.4 | 171% of 2 | 218 MB | 31 |

## 10 concurrent uploads (20 pages each)

- All jobs: 10 succeeded; wall time **36.9 s**; per-chapter latency (queue + processing) min **5.9 s**, max **36.8 s**.
- Max concurrent image conversions observed: **2** (limit 2) — bounded as designed.
- Server CPU during processing: **114%** of available cores; RAM after: **241 MB**.

## Read latency while 4 chapters are processing

| Scenario | Concurrency | Requests | Errors | Req/s | p50 ms | p95 ms | p99 ms | Server CPU | Server RAM | Max DB conns |
|---|---|---|---|---|---|---|---|---|---|---|
| 50 readers during processing | 50 | 500 | 0 | 503.2 | 104.0 | 122.3 | 175.9 | 181% of 2 | 256 MB | 31 |

- 2 MB JSON body to `/api/auth/login` → **413** (expected 413, Kestrel MaxRequestBodySize = 1 MB).
