# API list

Base path `/api`. JSON is camelCase. Errors are RFC 7807 ProblemDetails:
`{ status, title, detail (Vietnamese), code (stable), traceId, errors? }`.
House endpoints accept an optional `X-House-Id` header that **selects among the caller's own memberships**
(validated server-side; never trusted as authority). Public GETs send `Cache-Control: public, max-age=30–300`.

## Public (anonymous)
| Method | Path | Notes |
|---|---|---|
| GET | `/site` | registration flag, announcement |
| GET | `/comics?q&tag&house&status&sort=updated\|new\|az\|chapters&page&pageSize≤48` | visible comics; diacritic-insensitive search |
| GET | `/comics/{id}` | detail, tags, first/latest chapter |
| GET | `/comics/{id}/chapters` | published chapters |
| GET | `/chapters/{id}` | chapter summary + prev/next |
| GET | `/chapters/{id}/reader` | package `{url, version, size, sha256, pageCount, format}`, pages `[{n,w,h}]`, prev/next |
| GET | `/tags`, `/houses`, `/houses/{slug}` | genres with counts, public house pages |
| GET | `/rankings?period=day\|week\|month` | most viewed |
| POST | `/chapters/{id}/view` | view beacon (deduplicated, batched) |

## Auth
| Method | Path | Notes |
|---|---|---|
| POST | `/auth/register` | creates a Reader; sets refresh cookie |
| POST | `/auth/login` | email or username; lockout after 5 failures / 15 min |
| POST | `/auth/refresh` | rotates the HttpOnly refresh cookie; reuse revokes the family |
| POST | `/auth/logout` | revokes the token family |
| POST | `/auth/change-password` | clears `mustChangePassword`, signs out other sessions |
| GET | `/auth/me` | profile, system role, memberships + permissions |

## Reader library (signed in)
`GET /me/follows` · `PUT/DELETE /me/follows/{comicId}` · `GET /me/history` · `PUT /me/progress` · `GET /me/comics/{comicId}/state`

## System admin (`SystemAdmin`, re-verified from the DB on every request)
| Method | Path |
|---|---|
| GET, POST | `/admin/translation-houses` (POST may create the owner account with a generated temporary password) |
| GET, PUT | `/admin/translation-houses/{id}` |
| POST | `/admin/translation-houses/{id}/activate`, `/admin/translation-houses/{id}/suspend` |
| GET, POST | `/admin/translation-houses/{id}/members` |
| PUT, DELETE | `/admin/translation-houses/{id}/members/{memberId}` |
| GET | `/admin/translation-houses/{id}/comics` (metadata only, no pages) |
| GET | `/admin/users` · POST `/admin/users/{id}/disable` · `/enable` · `/reset-password` |
| GET | `/admin/stats` · `/admin/audit-logs?action&houseId` · `/admin/system` |
| GET, PUT | `/admin/settings` |

## Translation house (house context required; permission per endpoint)
| Method | Path | Permission |
|---|---|---|
| GET / PUT | `/translation-house/profile` | any / `manageProfile` |
| PUT | `/translation-house/profile/logo` (multipart image) | `manageProfile` |
| GET | `/translation-house/dashboard`, `/translation-house/processing` | any member |
| GET | `/translation-house/members` | `viewMembers` |
| POST, PUT, DELETE | `/translation-house/members[/{id}]` | `manageMembers` |
| GET | `/translation-house/comics`, `/translation-house/comics/{id}` | any member |
| POST, PUT, DELETE | `/translation-house/comics[/{id}]`, PUT `/comics/{id}/cover` | `manageComics` |
| GET | `/translation-house/comics/{id}/chapters`, `/chapters/{id}`, `/chapters/{id}/processing` | any member |
| POST, PUT, DELETE | `/translation-house/comics/{id}/chapters`, `/chapters/{id}` | `manageChapters` |
| POST | `/translation-house/chapters/{id}/upload` (multipart .zip/.cbz) → 202 | `uploadChapters` |
| POST | `/translation-house/chapters/{id}/retry` | `uploadChapters` |
| GET | `/translation-house/upload-limits` (limits the browser uses for the pre-upload check) | any member |
| GET | `/translation-house/chapters/{id}/versions/{v}/preview` → pages `[{n,w,h,size,sourceName}]`, sha256, `canPublish` (ready/published versions only) | any member |
| GET | `/translation-house/chapters/{id}/versions/{v}/package` → the private CPK1 package (`Cache-Control: private, no-store`, never via CDN) | any member |
| POST | `/translation-house/chapters/{id}/publish`, `/unpublish` | `publishChapters` |

## Infrastructure
| Method | Path | Notes |
|---|---|---|
| GET | `/api/health` | liveness |
| GET | `/api/health/ready` | DB, writable storage + free space, queue |
| GET/HEAD | `/internal/objects/{key}` | Worker fallback; requires `X-Origin-Secret`; only public objects |
| GET/HEAD | `/cdn/{key}` | **dev/E2E only** (`PACKAGE_STORE=local`), same headers as the Worker |
