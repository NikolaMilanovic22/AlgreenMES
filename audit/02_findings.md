# 02 — Audit Findings: Multi-tenant + Authorization

**Date:** 2026-05-17 17:35 UTC
**Scope:** Identity module (both BE repos), DbContext layer, API contract, FE guards (3 repos), DB constraints, audit trail, cross-tenant test suite.
**Auditor:** Claude (claude-opus-4-7), single pass.

## Summary

| Severity | Count |
| --- | --- |
| CRITICAL | 0 |
| HIGH | 3 |
| MEDIUM | 4 |
| LOW | 4 |
| INFO | 2 |

**No CRITICAL findings.** Cross-tenant isolation via `HasQueryFilter` is intact; tenant-code validation on login correctly scopes by `(email, tenantId)`. The gaps are all **within-tenant** privilege management.

3 HIGH findings cluster around the same root cause: `UpdateUserCommandHandler` and `DeleteUserCommandHandler` have insufficient guards, and refresh tokens are not revoked when a user's role changes.

---

## HIGH

### F-1: Admin can demote the last Admin (tenant lockout)

**Severity:** HIGH
**Layer:** BE handler
**Repos affected:** both BE (`AlgreenMES`, `easy-mes-be`)
**File(s):** `src/Modules/Identity/AlGreenMES.Modules.Identity.Application/Commands/UpdateUser/UpdateUserCommandHandler.cs:30-36`

**Description:** `UpdateUserCommandHandler` only blocks SuperAdmin grant/revoke (line 33). It does not check whether the target user is the last Admin in the tenant. If the sole Admin demotes themselves (or another Admin demotes them), the tenant is left with no Admin and cannot reach `/admin/users` to recover. Only a SuperAdmin can rescue.

**Reproduction:** On a tenant with exactly one Admin user, log in as that Admin. PUT `/api/users/{ownId}` with `role=Department`. Server returns 200 OK. Subsequent logins as that user have no admin privileges and the tenant has no Admin.

**Impact:** Tenant administrative lockout. Requires platform-level intervention (SuperAdmin or DB write) to recover. This is exactly what happened on easy-mes (forensics doc).

**Suggested fix:** In `UpdateUserCommandHandler`, when a role change demotes the target from Admin to anything else, count remaining active Admins in the tenant. If `count == 1 && target.Role == Admin && newRole != Admin`, throw `DomainException("LAST_ADMIN_REMOVAL", ...)`. Same logic in `DeleteUserCommandHandler` (see F-2).

---

### F-2: DeleteUser has zero guards

**Severity:** HIGH
**Layer:** BE handler
**Repos affected:** both BE
**File(s):** `src/Modules/Identity/AlGreenMES.Modules.Identity.Application/Commands/DeleteUser/DeleteUserCommandHandler.cs:20-29`

**Description:** Handler is 9 lines: fetch user, delete, save. No checks for self-delete, last-Admin delete, SuperAdmin protection, or active work sessions.

**Reproduction:**
- Self-delete: log in as Admin, `DELETE /api/users/{ownId}` → 204 No Content. Next request 401 (user gone), then login fails (`INVALID_CREDENTIALS`).
- SuperAdmin delete: log in as Admin, `DELETE /api/users/{superAdminId}` → 204 No Content. Platform SuperAdmin gone if there's only one.
- Last-Admin delete: same path, no check.

**Impact:** Same family as F-1 plus self-locking. The forensics gap on easy-mes could have been worse — DeleteUser would have left no row at all.

**Suggested fix:** Mirror F-1 guards. Also block `targetUserId == currentUserId` for delete operations. SuperAdmin deletion only allowed by another SuperAdmin.

---

### F-3: Refresh tokens not revoked on role change

**Severity:** HIGH (LOW if JWT TTL is enforced strictly)
**Layer:** Auth (session lifecycle)
**Repos affected:** both BE
**File(s):** `src/Modules/Identity/AlGreenMES.Modules.Identity.Application/Commands/UpdateUser/UpdateUserCommandHandler.cs` (no revocation), `RefreshTokenRepository.cs` (no role-change hook), `JwtTokenService.cs` (60-minute TTL)

**Description:** When a user's role is changed via `PUT /api/users/{id}`, the existing access JWT and any outstanding refresh tokens remain valid. The access JWT carries the OLD role claim until expiry. The refresh token can be exchanged for a NEW access JWT — but the refresh flow re-reads the user's current role from DB (`UserRepository.GetByEmailAsync`) so subsequent refreshes pick up the new role.

JWT TTL is **60 minutes** (`appsettings.json -> JwtSettings.ExpirationMinutes`). Worst case: a user demoted from Admin → Department keeps Admin privileges for up to 60 minutes via their current access token.

**Reproduction:** Login as Admin (get JWT). On another session, demote that Admin → Department. With the original JWT (still in 60-min window), call any `[Authorize(Roles = "Admin")]` endpoint → still 200 OK.

**Impact:** Stale-role privilege window. Mitigated by the 60-min TTL but still a real exposure for privilege removal. Hits a HIGH because the typical pattern for revoking access (firing/demoting a user) should be immediate, not "wait 60 minutes."

**Suggested fix:** On role change in `UpdateUserCommandHandler`, revoke all refresh tokens for that user (`RefreshTokenRepository.RevokeAllForUserAsync(userId)`). Maintain a per-user `security_stamp` UUID that gets rotated on role change and is encoded in the JWT; reject incoming JWTs whose stamp ≠ current. The stamp approach is stronger but more invasive — refresh revocation is the minimum acceptable fix.

---

## MEDIUM

### F-4: UpdateUser allows self-demotion

**Severity:** MEDIUM
**Layer:** BE handler
**Repos affected:** both BE
**File(s):** `src/Modules/Identity/AlGreenMES.Modules.Identity.Application/Commands/UpdateUser/UpdateUserCommandHandler.cs`

**Description:** No comparison between `command.Id` and `currentUserService.GetCurrentUserId()`. An Admin can PUT their own row with a lower role and lock themselves out. This is the proximate cause of the easy-mes incident.

**Reproduction:** Login as Admin, PUT `/api/users/{ownId}` with `role=Department`. Server returns 200.

**Impact:** UX foot-gun. Subsumed by Nikola's pre-approved stricter fix (3.3: only SuperAdmin changes roles).

**Suggested fix:** Covered by F-7 (stricter role-management fix below). If F-7 is rejected, add explicit `if (command.Id == _currentUser.GetCurrentUserId() && command.Role != user.Role) throw ForbiddenException("SELF_ROLE_CHANGE_FORBIDDEN")`.

---

### F-5: UpdateUser allows peer-Admin demotion

**Severity:** MEDIUM
**Layer:** BE handler
**Repos affected:** both BE
**File(s):** `src/Modules/Identity/AlGreenMES.Modules.Identity.Application/Commands/UpdateUser/UpdateUserCommandHandler.cs`

**Description:** Admin-A can demote Admin-B within the same tenant. This is the "Admin-vs-Admin" power vacuum scenario.

**Reproduction:** Tenant with two Admins. Admin-A PUTs `/api/users/{adminB-id}` with `role=Department` → 200 OK.

**Impact:** Lower-stakes than F-1 because at least one Admin remains, but it's still a privilege-management anomaly.

**Suggested fix:** Covered by F-7. Without F-7, require SuperAdmin to demote any Admin.

---

### F-6: Cross-tenant test suite has zero coverage of role management

**Severity:** MEDIUM
**Layer:** Tests
**Repos affected:** both BE
**File(s):** `tests/AlGreenMES.Tests.Integration/CrossTenant/*`

**Description:** The 7 existing cross-tenant tests cover read/write isolation on Orders, Processes, ProductCategories, and SpecialRequestTypes. None test the User domain: Admin-A in tenant-A cannot affect users in tenant-B; non-Admin cannot call UpdateUser; SuperAdmin can. Phase 3 will add these.

**Suggested fix:** Add tests in Phase 3 covering: (a) `UpdateUser` cross-tenant rejection, (b) self-demotion rejection (when guard exists), (c) last-Admin removal rejection, (d) SuperAdmin can change role anywhere, (e) Department cannot call UpdateUser at all.

---

### F-7: Tenant Admin can change roles (Nikola's pre-approved tightening)

**Severity:** MEDIUM (design decision, not a current bug)
**Layer:** BE handler + FE
**Repos affected:** both BE + all 3 FE
**File(s):** `UpdateUserCommandHandler.cs`, all 3 FE `apps/dashboard/src/pages/admin/UsersPage.tsx` (or equivalent)

**Description:** Today, `[Authorize(Roles = "SuperAdmin,Admin")]` on `PUT /api/users/{id}` lets any Admin in a tenant change roles for users in that tenant (subject to the SuperAdmin grant guard). Per Nikola's pre-approval, the stricter rule is "only SuperAdmin can change ANY user's role; Admins can edit name/email/active but not role".

**Reproduction:** Not a bug today, but the demotion pathway. Once F-7 is implemented, this becomes a guarded path.

**Suggested fix:** In `UpdateUserCommandHandler`, if `command.Role != user.Role` and caller is not SuperAdmin → `throw DomainException("FORBIDDEN_ROLE_CHANGE", ...)`. In FE `UsersPage`, hide the role dropdown for non-SuperAdmin viewers; show a static read-only label. Search for any onboarding/auto-promote flow that assumes Admin can change roles (none found in current scan but Phase 3 must verify) and document any breakage in `audit/04_breaking_changes.md`.

---

## LOW

### F-8: `User.Update()` does not call `SetUpdated()` (pre-Sprint-3.5 audit gap)

**Severity:** LOW (mitigated post-Sprint-3.5 by `AuditableEntityInterceptor`)
**Layer:** Audit
**Repos affected:** both BE
**File(s):** `src/Modules/Identity/AlGreenMES.Modules.Identity.Domain/Entities/User.cs:53-66`

**Description:** The `User.Update()` domain method mutates properties but does not call the inherited `AuditableEntity.SetUpdated(userId)`. Pre-Sprint-3.5 this meant every user mutation persisted without `updated_at` / `updated_by_user_id`. Sprint 3.5 added the `AuditableEntityInterceptor` which stamps these columns on every `Modified` entry automatically — so post-3.5 the column population is correct regardless of whether `SetUpdated` is called. The legacy code is now dead defensive infrastructure but harmless.

**Suggested fix:** Either remove `SetUpdated()` from `AuditableEntity` entirely (no longer needed post-3.5), or leave it as belt-and-suspenders. Either way, no Phase 3 action required. Add to backlog.

**Note for forensics:** This bug is the reason `updated_at` is NULL on demoted easy-mes users despite the role change. See `audit/01_forensics.md`.

---

### F-9: No `user_role_change_log` history table

**Severity:** LOW
**Layer:** Audit
**Repos affected:** both BE
**File(s):** N/A (table doesn't exist)

**Description:** The `AuditableEntityInterceptor` records *who last modified* a row, not *what the old value was*. For role changes specifically, a history table (`user_role_change_log(tenant_id, user_id, old_role, new_role, changed_by, changed_at, reason)`) would allow reconstructing the full history. Adding it requires a migration + wiring into `UpdateUserCommandHandler`.

**Suggested fix:** Backlog for Sprint 4. Useful but not blocking. Out of scope for Sprint 3.0 per prompt 2.7.

---

### F-10: PostgreSQL RLS not enabled

**Severity:** LOW
**Layer:** DB
**Repos affected:** both BE
**File(s):** N/A

**Description:** Multi-tenant isolation relies entirely on EF Core's `HasQueryFilter`. PostgreSQL row-level security (RLS) policies would add a database-layer safety net catching raw-SQL bypasses and direct connections. Big lift, low marginal value at current threat model.

**Suggested fix:** Backlog. Reconsider when/if onboarding multiple production customers on a shared DB.

---

### F-11: `ChangePassword` endpoint missing explicit role authz

**Severity:** LOW
**Layer:** BE handler
**Repos affected:** both BE
**File(s):** `src/Modules/Identity/AlGreenMES.Modules.Identity.Api/Controllers/UsersController.cs:99-107`

**Description:** `POST /api/users/{id}/change-password` has no `[Authorize(Roles = ...)]`, only the class-level `[Authorize]`. Any authenticated user can call it for any `{id}`. The handler does require the correct `CurrentPassword`, so the attacker would need to know the target's existing password — which downgrades the impact to "indirect compromise after credential leak." But there's also no check that `request.UserId == currentUserService.GetCurrentUserId()`.

**Suggested fix:** Add an explicit handler check: if `request.UserId != currentUser.GetCurrentUserId() && !currentUser.IsInRole("SuperAdmin")` → throw forbidden. Self-service password change is fine; using this endpoint to change someone else's is the admin-flavored `reset-password` endpoint's job (which IS role-gated). Backlog or Phase 3 — small fix.

---

## INFO

### F-12: ChangePassword handler does not invalidate refresh tokens

**Severity:** INFO
**Layer:** Auth
**Repos affected:** both BE
**Description:** Similar to F-3 but for password change. When a user changes their password (legitimate or attacker-forced), existing refresh tokens remain valid. Best practice is to revoke. Documenting; treat with F-3 fix.

---

### F-13: Cross-tenant test infrastructure must whitelist SuperAdmin

**Severity:** INFO
**Layer:** Tests
**Repos affected:** both BE
**Description:** Existing cross-tenant tests assert "user from tenant A cannot affect tenant B". SuperAdmin is the explicit exception (platform role with cross-tenant powers). Phase 3 must extend the test framework so SuperAdmin-flagged callers are NOT considered violations in the cross-tenant suite. Documenting now so it isn't a surprise.

---

## CRITICAL findings — NONE

Verified clean:

- **2.1.A Login tenant-code verification:** `UserRepository.GetByEmailAsync` filters `WHERE Email = @e AND TenantId = @t`. A user from tenant A cannot log in with tenant B's code. ✅
- **2.3 HasQueryFilter coverage:** `IdentityDbContext` applies the filter to every tenant-scoped entity. Bypasses (`IgnoreQueryFilters`) exist only in login, refresh, and seeder — each documented and uses explicit tenantId. ✅
- **2.4 API contract:** No endpoint accepts `tenantId` from the request body. The `UpdateUserCommand.TenantId` field is populated by the controller from `_tenantService.GetCurrentTenantId()` (JWT-derived). ✅
- **2.6 DB constraints:** `users.tenant_id` is NOT NULL with FK to `tenancy.tenants(id)`. `users` unique index is `(tenant_id, email)` — per-tenant email is correctly enforced. ✅

## Phase 3 plan (high-level, executed without further pauses)

Order, one finding per commit, both BE repos in lockstep, with TDD where applicable:

1. **F-7** (role-management tightening) — biggest behavioral change. Test first (`Admin cannot change any user's role` + `SuperAdmin can` + cross-tenant whitelist). Implement BE guard + FE dropdown hide in all 3 FE repos.
2. **F-1** (last-Admin removal). Test + handler guard.
3. **F-2** (DeleteUser guards). Test + handler guards (self-delete, last-Admin, SuperAdmin protection).
4. **F-3** (refresh token revocation on role change). Test + repo method + handler call.
5. **F-11** (ChangePassword self-target). Test + handler guard.
6. **F-6** (cross-tenant test additions) — implicit in 1-5; covered by their tests.
7. **Restore script** for easy-mes (`scripts/2026-05-restore-easy-mes-roles.sql`) using `code='DEMO'` lookup.
8. **Update `gotchas.md`** — remove obsolete line 36, document the new rule.

F-4 (self-demotion) and F-5 (peer-Admin demotion) are subsumed by F-7 — no separate fix needed once the stricter "only SuperAdmin changes roles" rule is in place.

F-8 (User.Update missing SetUpdated), F-9 (role change log table), F-10 (RLS), F-12 (password change refresh revocation), F-13 (test framework whitelist) → `audit/03_backlog.md`.
