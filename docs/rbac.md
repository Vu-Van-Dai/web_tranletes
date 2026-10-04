# RBAC matrix and tenant isolation

## Roles

- **SystemAdmin** — `users.system_role = SystemAdmin`; manages houses, house accounts/members, users, settings;
  sees content *metadata* only. It is **not** a house member, so every house-workspace endpoint returns 403.
- **TranslationHouseOwner** — membership role `Owner`: all house permissions.
- **TranslationHouseStaff** — membership role `Staff`: only the delegable permissions the owner assigned.
- **Reader** — any signed-in user (anonymous visitors can also read published content).

A user may belong to several houses. Permissions are flags on the membership.

| Capability | Anon | Reader | Staff | Owner | SysAdmin |
|---|:-:|:-:|:-:|:-:|:-:|
| Browse, search, read published content | ✓ | ✓ | ✓ | ✓ | ✓ |
| Follow, reading history | – | ✓ | ✓ | ✓ | ✓ |
| House dashboard, comic/chapter lists, processing status | – | – | ✓ | ✓ | – |
| View members | – | – | `viewMembers` | ✓ | – |
| Add/edit/remove members | – | – | – | ✓ | – |
| Edit house profile / logo | – | – | – | ✓ | – |
| Create/edit/delete comics, covers, tags | – | – | `manageComics` | ✓ | – |
| Create/edit/delete chapters | – | – | `manageChapters` | ✓ | – |
| Upload chapters, retry processing | – | – | `uploadChapters` | ✓ | – |
| Publish / unpublish | – | – | `publishChapters` | ✓ | – |
| Create/suspend/activate houses; manage house accounts & members; users; stats; audit; settings | – | – | – | – | ✓ |
| View a house's comic metadata (no pages) | – | – | – | – | ✓ |

Extra rules: at least one active owner per house; a temporary password (`must_change_password`) blocks house and
admin APIs until changed; disabled users and suspended houses lose access on the next request.

## Tenant isolation strategy (fail closed, five layers)

1. **Authentication** — the JWT carries only the subject (plus a UI hint); roles, status and memberships are
   re-read from PostgreSQL on every house/admin request, so removals and suspensions apply immediately.
2. **House context** — the `/api/translation-house` group resolves the tenant from the caller's active memberships in
   active houses. `X-House-Id` may only select one of those memberships; anything else → 403 `not_house_member`
   (audited). No membership → 403; several and no selector → 409.
3. **Permission filter** — each endpoint declares the permission it needs (`RequirePermission`).
4. **Fail-closed data filter** — EF Core 10 named query filter `Tenant` on comics, chapters, packages, pages and jobs
   compares with the resolved house id, or `Guid.Empty` when no tenant is resolved (→ zero rows). Cross-house readers
   (public catalog, admin, background jobs) opt out explicitly with `IgnoreQueryFilters(["Tenant"])`; the separate
   `SoftDelete` filter stays active.
5. **Resource guard** — ids from the URL are probed without the tenant filter: missing → 404, another house → **403
   `cross_tenant`** (logged + audited as `security.cross_tenant_denied`), then loaded again through the filtered set.

House ids are never read from request bodies (a `translationHouseId` field in JSON is ignored); new comics take the
resolved tenant, chapters inherit through their guarded comic.

**Verified by:** `TenantIsolationTests` (every house endpoint with XYZ ids as the ABC owner → 403; lists never leak;
forged selector; body id ignored; staff permissions; removed members; last owner), Playwright scenario 17 (UI + API),
and the audit log check.
