# Changelog

All notable changes to the Algreen MES backend. Format: roughly
[keep-a-changelog](https://keepachangelog.com/) — grouped by date, with
short user-facing summaries and links to the deepest relevant doc/code.

---

## 2026-07-10 — Test-gap hardening sweep (3 real bug fixes + ~50 tests)

A codebase-wide test-coverage audit (6 module areas) turned up several
unpinned behaviors + real defects. Fixes and regression tests below; full
suite now 319 integration + 15 unit, 0 failing.

### Fixed
- **Efficiency % still showed >100% in the per-DAY rows.** The 08.07 cap fix
  covered the per-worker summary + the work-efficiency report but MISSED the
  daily breakdown row (`ReportingQueryService` ~1339). Now `Math.Min(…,100)`
  at all **three** sites. Pinned by tests on all three.
- **Security — push subscription user-id hijack.** `PushController.Subscribe`
  trusted a client-supplied `UserId`, so any authenticated user could register
  their browser endpoint under another user's id and receive that user's push
  payloads (order/block content). Now derived from the JWT
  (`ICurrentUserService`); the DTO field is ignored.
- **Security — push unsubscribe had no ownership check.** `WebPush.Unsubscribe`
  deactivated any subscription matching an endpoint regardless of owner. Now
  scoped to the authenticated caller.
- **Hardening — `StartProcessWork` dependency guard was dead code.** It reads
  `orderItem.Processes` to enforce category dependencies, but the repo query
  never loaded that collection (lazy loading is off), so `DEPENDENCY_NOT_MET`
  could never fire. Restored via `.Include(oi => oi.Processes)`. **Low impact**:
  the tablet queue already enforces dependency ordering by visibility
  (`GetTabletQueueQueryHandler` `allDepsCompleted`), so normal factory flow is
  unaffected — this only restores the API-level backstop.

### Tests (regression guards, ~50 new methods)
- auth/security: deactivated login/refresh, refresh rotation + expiry +
  cross-tenant, SuperAdmin self-only password, user-management role-gate matrix,
  cross-tenant user-create, password strength.
- workflow: dependency gate, subprocess-not-complete guard, block-request
  sibling auto-approve, restart-preserve-duration.
- reporting: efficiency cap ×3 sites, auto-logout cap timezone (4th UTC→local
  site), sub-process duration override, complexity tie-breaks, active-log union
  overlap, no-shift fallback, cross-midnight working-minutes, delivery bucketing.
- CRUD/warehouse: Serbian-filename attachment download (MES-API-F), cross-tenant
  delete, OrderType + ProductCategory CRUD, multi-line Izlaz atomicity, low-stock
  boundary, material threshold validation.
- dashboard/tenancy: `DashboardController` (previously **zero** tests), bulk +
  single notification user-scope isolation, billing 14-day boundary + multi-admin,
  tenant logo size limit, push subscribe/unsubscribe security.

### Related FE change (alblue-tracker-fe)
- Auto-mark logic extracted into `useAutoMarkAllReadOnOpen` / `useAutoMarkAllReadOnce`
  hooks; **vitest infra added to `packages/api-client` + `packages/auth`** (had
  none); ~63 new FE tests incl. the 401 refresh-retry interceptor, auth store,
  JWT utils, route guards.

---

## 2026-07-09 — Report tweaks (Saša) + notifications auto-mark

Shipped to alblue staging.

### Fixed
- **Operator efficiency capped at 100%.** A short session with no break could yield
  active > effective → efficiency >100%, which Saša flagged as illogical (08.07).
  `Math.Min(…, 100.0)` at both calc sites (daily detail + per-worker aggregate) in
  `ReportingQueryService`. FE just renders the BE value.

### Changed
- **"Trajanje izrade proizvoda po narudžbini" table moved to the bottom** of the
  Vremena procesa → Trajanje izrade proizvoda tab (below the aggregate + chart),
  per Saša. Its export button + a bottom margin on the chart card moved with it.
- **Tablet notifications page auto-marks-all-read** ~800ms after a worker opens it
  (fires ONCE per visit, ref-guarded; nothing is deleted). Same rationale as the
  dashboard bell — workers never tap "mark all", so the unread badge grew
  meaningless.
- **Dashboard bell auto-mark no longer restarts its timer** when a notification
  arrives while the popover is open (audit finding — a trickle could defer the
  mark indefinitely).

### Config (pilot, by Bojan/Saša)
- Early-arrival gap (workers clocking in before 07:00 → no shift → no auto-logout,
  ~37% of morning check-ins) resolved by **adding III smena 23:00–07:00** on the
  Algreen tenant — its cross-midnight window catches pre-07:00 check-ins, and its
  break/cap match I smena so hours compute the same. **Caveat:** III crosses UTC
  midnight, so the deferred night-shift date-bucketing audit findings become
  relevant *if* genuine night-shift work happens (they don't affect early-morning
  arrivals). Confirm with the client whether a real night shift will run.

## 2026-07-03 — Code audit fixes

Multi-agent audit of BE + FE; findings verified against code before acting.
All shipped to **alblue staging**; the timezone fix below was also cherry-picked
to **master + deployed to the algreen pilot** (03.07, after hours; integration
suite green 267/0/3 first). The perf/FE items stay on the mirror wave.

### Fixed
- **4th shift-match site converted to tenant-local.** `ComputeEffectiveSessionEnd`
  (the auto-logout-cap / effective-end calc, live for every "Sati radnika" row)
  still took the time-of-day of a **UTC** check-in vs local shift windows — the
  same class fixed at 3 sites on 29.06 but missed here. Morning check-ins failed
  to match their shift → session not capped → inflated hours. One-line
  `LocalTimeOfDay(checkIn)` swap.
- **"Handled At" column sorts/displays `handledAt`, not `updatedAt`** (Change &
  Block Requests). A merely-touched request bumped `updatedAt`, so the grid
  showed a handled date for never-handled requests and disagreed with the export.
  Added a `handledat` sort case to `ChangeRequestRepository` + `BlockRequestRepository`.

### Performance
- **`AsNoTracking`** on `GetActiveOrdersWithProcessesAsync` +
  `GetPagedWithProcessesAsync` — pure-read graph loads on the tablet-poll +
  master-view hot paths (verified no mutation callers).
- **N+1 removed** in `GetBlockRequests` (batch process names via `GetByIdsAsync`)
  and `ImportMaterials` (preload existing codes into a HashSet instead of one
  `EXISTS` per imported row).

### Frontend (no dedicated FE changelog — recorded here)
- **Blob-URL leak** in pending-attachment thumbnails: 4 inline
  `URL.createObjectURL(file)` calls in render leaked a blob every re-render
  (drawers re-render on a 10s tick). New `PendingFileThumbnail` mints once +
  revokes on unmount.
- **Tablet stale process names**: `ProcessDefinitionUpdated` invalidated
  `['processes-batch']` (matched nothing); now invalidates the real keys so an
  admin's process rename/reorder reaches workers.
- **Admin process lookups** fetched only `pageSize: 100` (Users /
  SpecialRequestTypes / ProductCategories) → processes past 100 rendered as
  `? — ?`; bumped to the repo's fetch-all convention.

### Verified NOT bugs (audit)
- Timestamps: columns are `timestamptz` → Npgsql `Kind=Utc` → `Z`-suffixed JSON
  → FE parses UTC correctly.
- `TotalDurationMinutes` / `DurationMinutes` actually hold **seconds** (systemic
  misnomer); FE treats them as seconds, so no 60× bug. `AddDuration(int minutes)`
  is a landmine (param named minutes, receives seconds) — do not "fix" by ×60.

## 2026-07-02 — Attachment download: non-ASCII filename fix (pilot)

### Fixed
- **`GET /api/Orders/{id}/attachments/{id}/download` returned 500 for filenames
  with Serbian letters** (`0x0161` = š, also č/ć/ž/đ). The inline-PDF branch
  hand-built `Content-Disposition` with the raw filename, which ASP.NET rejects.
  Now built via `ContentDispositionHeaderValue.SetHttpFileName` (RFC 5987),
  matching the non-PDF `File(...)` path. Sentry **MES-API-F**, `algreen-pilot`.
  Shipped **staging + pilot**.

## 2026-07-01 — Tablet N+1 elimination + FE observability + order-type data

### Performance
- **Tablet queue/incoming/active N+1 removed.** Each order card fetched its own
  `/attachments` just for a count badge; the count now rides in the queue/
  incoming/active response (batched `GetRefsByOrderIdsAsync` +
  `AttachmentCountLookup`, mirroring the FE indicator's filter). Also removes the
  per-card fetch when offline. (staging)

### Observability
- **FE source maps uploaded to Sentry on deploy** (`@sentry/vite-plugin`, gated
  on `SENTRY_AUTH_TOKEN` in `~/.zshrc` / passed by `deploy.sh`) → FE error stack
  traces are readable instead of minified.
- **Client-noise filter on the Serilog Sentry sink** (`Program.IsClientNoise`):
  drops request cancellations (incl. wrapped in an EF connection error) +
  client `BadHttpRequestException`, which were paging as errors. Genuine failures
  still reach Sentry. (staging)
- Sentry MCP connected for triage (org `sky-hard`, project `mes-api`, `de` region);
  4 stale benign issues resolved.

### Data
- **Order-type code casing normalized** on both tenant DBs (legacy PascalCase
  `Standard`/`Complaint`/`Repair`/`Rework` → uppercase config `Code`), so the
  "Vremena po procesu" order-type filter matches. No new drift — the create-flow
  has validated against the config + stored the uppercase code since 20.06.

## 2026-06-29 — Shift-matching timezone fix + pilot cleanup

### Fixed
- **Shift matching now converts the UTC check-in → tenant-local (Europe/Belgrade)
  before matching against local shift `StartTime`/`EndTime`.** `CheckInTime` is
  stored UTC but shift times are the local clock values the admin typed; matching
  by time-of-day without conversion mis-assigned shifts (a 06:54-local / 04:54-UTC
  check-in matched the 22:00–06:00 night shift), which broke auto-logout, break
  subtraction, effective time, overtime split and efficiency — reported by Bojan
  on the pilot. `ReportingQueryService.LocalTimeOfDay()` applied at 3 match sites
  (active-session/auto-logout, overtime quota, worker-hours). Regression test
  `WorkerHours_matches_shift_by_local_time_not_utc`. **Belgrade is hardcoded —
  must become per-tenant for the white-label case.** Shipped staging + pilot.

### Data / config (pilot)
- Capped stale never-closed `work_sessions` (worker forgot to log out → 45h
  "overtime") to a sane bound; backup taken first.
- Removed phantom seed shifts (Jutarnja/Popodnevna/Noćna, created 18.03) that were
  active in the DB but hidden from the UI and interfering with matching — leaving
  only "I smena" + "II smena" per Bojan.

### Frontend (staging-only)
- **Self-heal work session** (`useEnsureWorkSession`, tablet): creates a session
  for an authenticated worker who has none (the "logged-in but no prijava" limbo),
  strictly complementary to `AutoLogoutBanner`.

## ~late 2026-06 — Tablet offline writes (staging-only, flag-gated)

### Added
- **Offline-first tablet writes** behind `VITE_OFFLINE_WRITES` (default **off**):
  workflow actions queue locally and replay on reconnect with optimistic UI, so
  workers keep going when factory wifi drops. BE dedupes replays via a
  `ProcessedAction` entity (unique `action_id`) + `occurredAt` on 5 workflow
  commands (migration `AddProcessedActions`). On **alblue staging only** —
  `master`/pilot has neither the flag nor the migration. Removable via the flag.

## 2026-06-28 — Notification unread-count cap + Sentry pilot activation

### Performance
- **Unread-count endpoint capped at 100.** Sentry weekly report
  27.06.2026 flagged `GET /api/Notifications/unread-count` p95 latency
  climbing 82→259ms. Root cause: alblue staging accumulated 300+ unread
  notifications per manager/coord because virtually nobody clicks the
  bell to mark them read; the `COUNT(*)` query cost grows linearly
  with the unread table. The FE `<Badge>` truncates display to "99+"
  for any count > 99 anyway, so returning the exact 326 was wasted
  work that just gets visually truncated. `NotificationRepository.
  GetUnreadCountAsync` now adds `LIMIT 100` → constant-time response
  regardless of how many unread sit in the table.

### Observability
- **Sentry activated on algreen pilot (`environment:algreen-pilot`).**
  Original deploy-script comment said "dormant until Sprint 3 ships";
  that gate is now met. Both environments now use the same DSN (sky-hard
  org, mes-api project) — events distinguished by environment tag.
  Pre-staged config in `/opt/algreen/api/appsettings.Production.json`
  (no in-repo change — production-only); BE picked up after `systemctl
  restart algreen-api`. Pipeline verified end-to-end (transactions
  flowing, synthetic test event indexed, alert rule fired). Mile's
  production environment now has the same error visibility as
  Bojan/Sale's staging.

### Security hygiene
- **nginx scan-probe blocking** on both droplets. `/etc/nginx/snippets/
  scan-block.conf` returns 444 for `.env`, `.git`, `.aws`, `.ssh`,
  `wp-admin`, `wp-login.php`, `xmlrpc.php`, `phpmyadmin`,
  `administrator/index.php`. Bots scanning for misconfig files
  (`GET /api/.env` showed up 7×/week in Sentry transactions) no
  longer reach the .NET app, don't pollute Sentry telemetry, and
  don't fill access logs. Real `/api/orders` and other valid paths
  unaffected.

### Tests
- `UnreadCount_CapsAt100_WhenUserHasMoreUnread` in
  `NotificationSideEffectTests.cs` — seeds 150 unread, asserts
  response is exactly 100. Locks the cap so a future refactor can't
  silently drop it.

### Related FE change (alblue-tracker-fe + algreen-tracker-fe)
- Bell popover now auto-mark-all-read on open (after 800ms delay).
  Treats the underlying cause of the cap fix above: nobody clicks
  "Mark all read" manually, so unread accumulates indefinitely. The
  delay lets the user briefly see the unread state before it clears;
  a quick open/close cancels the timer.

---

## 2026-06-23 — OrderType custom-code activate bug (follow-up to 20.06.2026 refactor)

### Fixed
- **Bug from Saša (23.06.2026)**: orders with admin-created OrderType
  codes (e.g. "Novi") couldn't be activated — POST `/orders/{id}/activate`
  returned 200 but the FE refetch surfaced as "narudžbina nije nađena."
  Root cause: `OrderDto.OrderType` and `OrderDetailDto.OrderType` were
  still typed as the C# `OrderType` enum after the 20.06.2026 refactor
  changed `Order.OrderType` to `string`. Mapster silently coerced
  unknown string codes to `Standard` (enum value 0) on the GET-back,
  corrupting the response payload and breaking the FE order detail
  query.

### Changed
- `OrderDto.OrderType` and `OrderDetailDto.OrderType` are now `string`,
  matching the domain entity. The "free-form OrderType" refactor is now
  consistent across all 4 DTOs (`OrderMasterViewDto` and the two
  Reports DTOs were already `string` from the original change).

### Removed
- Deleted `Domain.Enums.OrderType` (the dead 4-value enum left behind
  by the 20.06.2026 refactor). Verified zero references across `src/`
  and `tests/` first — removing it means nobody can accidentally
  reimport it and reintroduce the same coercion bug class.
- Codebase-wide audit confirmed CLEAN: no other DTOs declare a
  property as an enum where the matching entity field is `string`.

### Tests
- 3 new integration tests in `OrdersTests.cs` lock the regression:
  - `CreateOrder_WithCustomOrderTypeCode_RoundtripsCodeUnchanged` —
    POST `/orders` with custom code → GET → both responses preserve it.
  - `ActivateOrder_WithCustomOrderTypeCode_Succeeds` — the exact Saša
    flow: seed an order + item + process with a custom code, POST
    `/activate`, GET back, assert 204 + Status=Active + code preserved.
  - `GetOrderById_WithCustomOrderTypeCode_DoesNotCoerceToStandard` —
    surgical Mapster regression guard.
- `TestDataSeeder.SeedOrderTypeAsync` helper added for seeding a custom
  OrderType beyond the 4 defaults; `SeedOrderAsync` /
  `SeedOrderWithProcessesAsync` now take an optional `orderType` param.

### Process lesson (durable rule, saved as memory)
- The 20.06.2026 refactor was marked "done" on a build-clean +
  tests-green check, but missed two DTOs in the same folder I was
  editing. Build-clean ≠ done for refactors where Mapster /
  serializers sit in the path. Saved
  `feedback-self-audit-before-claiming-done.md` to grep all callers /
  sibling DTOs / shared-types and trace one full runtime request
  before claiming completion next time. The dropped "Testcontainers
  quirk" test from yesterday was also a red flag I dismissed instead
  of investigating — the 500 it threw was probably this bug
  manifesting.

---

## 2026-06-20 — OrderType: admins can create custom types beyond the original 4 (Saša)

### Fixed
- **Bug from Saša (20.06.2026)**: newly-created order types (e.g. "Novi")
  didn't show up in the create-order dropdown. Root cause: `Order.OrderType`
  was a C# enum with 4 fixed values (Standard/Repair/Complaint/Rework);
  the OrderTypes admin table only RENAMED those 4 slots. Creating a 5th
  row had no enum value to map to, so orders couldn't reference it and
  the dropdown (which iterated the enum) silently skipped it.

### Changed
- `Order.OrderType` is now a free-form `string` referencing
  `OrderType.Code` in the per-tenant `OrderTypes` table. DB schema
  unchanged — the column was always `varchar(20)` via
  `HasConversion<string>()`; only the C# domain constraint blocked
  custom values. No migration needed.
- `CreateOrderCommandHandler` validates the order type code exists for
  the tenant + is active (`INVALID_ORDER_TYPE` on miss). This replaces
  the enum-shape validation FluentValidation used to do via `IsInEnum`.
- All call sites that filtered/sorted/grouped by `OrderType` enum
  (OrderRepository, ReportingQueryService, Reports + Orders query
  handlers + DTOs + API requests) now use `string` / `List<string>`.
- `OrderTypeRepository.IsInUseAsync` does direct string comparison
  instead of round-tripping through the enum.
- `TestDataSeeder.SeedTenantWithUserAsync` now also seeds the 4
  default `OrderType` rows per tenant — the new handler-level
  validation requires at least one matching row to exist.

### Migration notes
- Existing tenants on staging/pilot already have `OrderType` rows
  (Saša's screenshot showed the DEMO tenant with 5 rows: Vrata,
  Prozor, Reklamacija, Dorada, Novi). No data migration required.
- New tenants created via the SA admin flow will need the 4 default
  `OrderType` rows seeded manually — added to onboarding checklist.
- The `Domain.Enums.OrderType` C# enum is left in place for now (dead
  code, removable in a follow-up sweep).

---

## 2026-06-19 — Audit follow-up: role constants, guard helper, tenant-isolation regression tests

### Added
- **`RoleNames` constants** (`BuildingBlocks.Common.Authorization`). Replaces
  every inline `IsInRole("SuperAdmin")` / `[Authorize(Roles = "...")]` magic
  string. A typo in the string form silently flipped authorization to false
  (the SA case never fired and the handler treated them as a regular user) —
  the constants make that impossible.
- **`ICurrentUserService.IsSuperAdmin`** property — typed shortcut for the
  most common role check, removes the need to even import `RoleNames` in
  handlers.
- **`UserAuthorizationGuards.RequireSameTenantOrSuperAdminTarget`**
  (Identity.Application/Services). Single source of truth for the
  cross-tenant boundary check on user-management mutations — extracted from
  three handlers (Update / Delete / ResetPassword) that all duplicated the
  same SA-exempt three-clause check.
- **`TenantIsolationRegressionTests`** integration suite. One golden-path
  test per high-risk listing endpoint (orders, processes, shifts, users):
  seed two tenants, write in A, log in as B, assert A's IDs aren't in the
  response. If the `HasQueryFilter` regresses again (e.g., someone reverts
  `EF.Property<Guid?>` back to `EF.Property<Guid>`), one of these turns red
  immediately instead of leaking for days.

### Changed
- `UpdateUserCommandHandler`, `DeleteUserCommandHandler`,
  `ResetPasswordCommandHandler`, `CreateUserCommandHandler` now use
  `_currentUser.IsSuperAdmin` / `UserAuthorizationGuards` instead of inline
  string checks + duplicated guard logic.
- `SuperAdminReadOnlyMiddleware` and `TenantBlockedMiddleware` use
  `RoleNames.SuperAdmin`.

---

## 2026-06-19 — Tenant filter regression fix + tenantless-SA handler hardening

### Fixed
- **Cross-tenant data leak in EF query filters.** The 16.06.2026 refactor
  that made `TenantEntity.TenantId` nullable (for tenantless SAs)
  silently turned off `HasQueryFilter` on every `TenantEntity` child —
  the strongly-typed `EF.Property<Guid>` lambda no longer matched the
  now-nullable column, so the filter became a no-op for Shifts, Orders,
  Production rows, etc. Caught by integration tests after Saša's
  19.06.2026 prod audit. Fix: switch filter to `EF.Property<Guid?>` in
  `IdentityDbContext` / `OrdersDbContext` / `ProductionDbContext`. SQL
  `NULL = X` is still false so tenantless SA rows are still correctly
  excluded from tenant-scoped queries.
- `LoginAttempt` is explicitly skipped from the Identity tenant filter
  (it's intentionally not a `TenantEntity` — pre-auth failures with an
  unknown tenant code must still be logged).

### Changed
- **User-management handlers locate tenantless SAs explicitly.**
  `UpdateUserCommandHandler`, `DeleteUserCommandHandler`, and
  `ResetPasswordCommandHandler` now load the target via the new
  `IUserRepository.GetByIdWithProcessesIgnoreFiltersAsync` and enforce
  the cross-tenant boundary inline (non-SA caller hitting a different
  tenant → 404). SA targets are exempted from the cross-tenant check so
  the role-based peer-SA guard (`FORBIDDEN_PEER_SUPERADMIN`) fires
  instead of returning a misleading 404. Without this an Admin attempting
  to delete an SA got 404 instead of 403, and SA self-update silently
  failed because the filter hid the SA from their own session.
- `UsersController`: `[AllowSuperAdminWrite]` added to `UpdateUser` /
  `DeleteUser` / `ResetPassword`. SAs can call these endpoints; the
  handler-level peer-SA guard does the real protection. Aligns the
  controller with the class-doc design ("Self-modification IS allowed;
  only peer-targeting operations are blocked").
- `NotificationCreator.NotifyManagementAsync` includes tenantless
  SuperAdmins in the recipient set (their `tenant_id` is NULL so
  `GetByTenantIdAsync` misses them, but they should still see bell
  notifications for tenants they're viewing).

### Tests
- `IdentityAuthzTests.DeleteUser_AdminDeletesSuperAdmin_Returns403_F2b`
  now asserts `FORBIDDEN_PEER_SUPERADMIN` (the older
  `FORBIDDEN_SUPERADMIN_DELETE` was subsumed on 15.06.2026).
- `SuperAdminPeerProtectionTests` peer-target assertions switched from
  middleware-level `SUPERADMIN_READ_ONLY` to handler-level
  `FORBIDDEN_PEER_SUPERADMIN`. `UpdateUser_Self_AsSuperAdmin_IsAlsoBlocked`
  renamed to `UpdateUser_Self_AsSuperAdmin_Succeeds` to match the class
  doc — self-modification is allowed.

---

## 2026-06-18 — Saša feedback round + Admin Naplata view + daily subscription reminders

### Added
- **Tenant feature flags** (Saša #7). New `tenants.disabled_features` JSON
  column + `Tenant.DisabledFeatures` / `SetDisabledFeatures()` /
  `KnownFeatures` / `BasicPlanDisabledFeatures`. Endpoint
  `PUT /api/tenants/{id}/features` (`[AllowSuperAdminWrite]`); unknown
  feature keys rejected with `UNKNOWN_FEATURE`. New tenants default to
  Basic (`["process-times", "magacin"]` disabled); existing tenants
  grandfathered to everything enabled by the migration.
- **Cross-tenant payments endpoint** (Saša #4).
  `GET /api/tenants/payments` (SA-only) returns the paged aggregated
  view across all tenants with tenant name + code denormalised. Filters:
  `tenantId`, `paidFrom`, `paidTo`, `currency`. Sort: `paidAt` (default
  desc) / `tenantName` / `amount` / `periodStart`.
- **Admin read-only payment ledger.** `GET /api/tenants/me/payments`
  resolves tenant from JWT — Admin role only; no mutation endpoints
  exposed on `/me`. Powers the new Profil firme → Naplata tab on the FE.
- **Daily subscription-expiry nudge.** `BillingReminderService`
  (HostedService) scans hourly, fires the actual work at 06:00 UTC. For
  each active tenant whose `paidThrough` is ≤ today+14 days OR already
  past, creates one `SubscriptionExpiring` (warning) or
  `SubscriptionExpired` (error) notification per Admin user.
  Idempotent per `(user, day)`. SA manual trigger at
  `POST /api/tenants/billing-reminders/run` so testing doesn't have to
  wait for the next morning.
- **`NotificationType.SubscriptionExpiring`** and
  **`SubscriptionExpired`** enum values, stored as strings so no
  migration needed.
- **`TenantBlockedMiddleware`** (Saša 17.06.2026 follow-up). Rejects
  authenticated requests from a blocked tenant's user with `401
  TENANT_BLOCKED` so the FE's axios interceptor force-logs-out instead
  of letting the JWT linger until expiry. SuperAdmins bypass.

### Changed
- **`paidThrough` semantics** (Saša #2 follow-up).
  `TenantPaymentRepository.GetPaidThroughAsync` /
  `GetPaidThroughByTenantAsync` now filter `periodStart <= today` so a
  pre-paid future period doesn't promote the tenant to "Plaćeno" until
  its start date arrives. Same filter governs the daily-reminder
  threshold.
- **Login flow** defers `tenant.IsActive` check until after the user is
  resolved, and skips it entirely for SuperAdmins. Blocking the MPMS /
  platform tenant no longer locks SAs out.
- **`Tenant.Update()`** no longer takes `isActive`. `Block` / `Unblock`
  are the only off-switch for a tenant — legacy "Deactivate" command
  parameter removed end-to-end.

### Fixed (during BillingReminderService test backfill)
- Admin-user lookup in the daily-reminder loop now uses
  `IgnoreQueryFilters()`. Without it, the tenant-scoped Users query
  returned zero rows in the absence of a JWT and the service silently
  notified nobody on prod.
- Same fix applied to the idempotency check (`alreadyNotified` query).
  Without it the check returned zero, so the service would have
  created a fresh batch of duplicate notifications on every run.

### Migrations
- `20260618072741_AddTenantDisabledFeatures` (TenancyDbContext) — adds
  `tenants.disabled_features text NOT NULL DEFAULT '[]'`. Existing rows
  inherit the empty list (grandfathered); new tenants go through
  `Tenant.Create` which seeds the Basic plan.

### Tests
- `TenantBillingTests` grows from 8 to 17: SA bypass of blocked-tenant
  login, paidThrough excludes future / counts started periods, feature
  flag toggle round-trip + unknown-key rejection + 403 for regular
  Admin, cross-tenant payments listing + tenantId filter + 403.
- New `BillingReminderServiceTests` (6 cases): expiring window creates
  warning for Admin only, expired uses the Expired type, beyond
  threshold + no payments + blocked all skip, double-run is idempotent.
  Suite total: 23/23.

---

## 2026-06-16 — Naplata (SA-only billing) + seed CLI flag + tenantless SA refactor

### Added
- **SuperAdmin "Naplata" (billing) feature**. New `tenant_payments` table
  (id, tenant_id, period_start, period_end, amount, currency, paid_at,
  invoice_number?, notes?) tracked via the `TenantPayment` aggregate
  with a date-range period so monthly / quarterly / annual subscriptions
  all fit. `Tenant` gained `BlockedAt` + `BlockedReason`; `Block(reason)`
  / `Unblock()` flip `IsActive`. New SA-only endpoints:
  - `GET    /api/tenants/{id}/payments`
  - `POST   /api/tenants/{id}/payments`
  - `PUT    /api/tenants/{id}/payments/{paymentId}`
  - `DELETE /api/tenants/{id}/payments/{paymentId}`
  - `POST   /api/tenants/{id}/block` (body `{ reason }`)
  - `POST   /api/tenants/{id}/unblock`
  All four write endpoints carry `[AllowSuperAdminWrite]` so the
  `SuperAdminReadOnlyMiddleware` lets them through. No auto-block on
  unpaid invoices — manual SA action only (Milos 16.06.2026).
- **`TenantDto.LastPaidAt`** injected into `GetTenants` (one batched
  query via `ITenantPaymentRepository.GetLastPaidAtByTenantAsync`) and
  `GetTenantById`, so the SA TenantsPage renders the "Poslednja uplata"
  column without N+1 round-trips.
- **`--seed` CLI flag**. The DataSeeder no longer runs on startup
  (overwrote locally-changed passwords on every BE restart). Use
  `dotnet run --project AlgreenMES.API -- --seed` (or
  `dotnet AlgreenMES.API.dll --seed`). Idempotent: re-running against
  an already-seeded DB is safe and doesn't reset existing passwords.

### Changed
- **Login distinguishes `TENANT_BLOCKED` from `TENANT_INACTIVE`**.
  `ITenantLookupService.TenantLookupResult` now carries `IsBlocked`;
  `LoginCommandHandler` picks the error code based on whether
  `Tenant.BlockedAt` is set. FE i18n has the matching pair
  (`Pretplata je na čekanju...` vs `Firma nije aktivna...`).
- **Tenantless SuperAdmin model** (carried over from the 06-16 morning
  refactor). SAs have `user.tenant_id = NULL`; the cross-tenant banner /
  claim machinery is gone. `SuperAdminReadOnlyMiddleware` blocks all
  non-GET writes by SA callers unless the action is opted in via
  `[AllowSuperAdminWrite]`. Allow-list today: tenant CRUD +
  `UpdateTenantSettings({id})` + `CreateUser` + `ChangePassword` + the
  five Naplata endpoints above.

### Tests
- New `TenantBillingTests` (6 cases): block → `TENANT_BLOCKED` on
  login, unblock restores login, payment add → list round-trips,
  regular Admin hits 403 on every billing route, payment update
  overwrites fields and persists across list, and a path-tampered
  cross-tenant update returns 404 (handler verifies
  `payment.TenantId == request.TenantId` before applying).
  All pass against the testcontainers Postgres fixture.

### Migrations
- `20260616145051_AddTenantBillingAndBlock` (TenancyDbContext) — adds
  `tenants.blocked_at`, `tenants.blocked_reason`, and the
  `tenant_payments` table with a FK cascade on `tenant_id` plus an
  index on `(tenant_id, paid_at)` for "most recent first" queries.

---

## 2026-06-09 — Magacin module polish

### Fixed
- **Izlaz refuses to take stock below zero**. `CreateStockEntryCommandHandler`
  now aggregates requested qty per material on Outflow and looks up
  current on-hand once via `IStockMovementRepository.GetQuantitiesAsync`
  (new). Throws `DomainException("STOCK_INSUFFICIENT", "Nedovoljno na
  stanju za 'KOD — NAZIV': trenutno X JM, traženo Y JM.")` on first
  shortage. No FIFO / no LOTs yet (Saša 08.06.2026), but the basic
  invariant stays.
- **`StockMovementDto.MaterialKod` / `MaterialNaziv` → `MaterialCode` /
  `MaterialName`**. Serbian property names serialized to `materialKod` /
  `materialNaziv`, which mismatched the FE's `materialCode` /
  `materialName`. Istorija columns rendered blank until the rename.

### Added
- **Server-side sort on `GET /warehouse/history`**. `GetStockHistoryQuery`
  + `IStockMovementRepository.GetPagedAsync` now accept `sortBy` +
  `sortDirection`. Repository switches on lower-cased field name →
  movementDate / type / materialCode / materialName / quantity /
  unitPrice / totalPrice / documentReference / category. Always ties on
  `CreatedAt desc` for stable ordering. Falls back to
  `MovementDate desc` for unknown fields.
- **Category filter on `GET /warehouse/history`**. `GetStockHistoryQuery`
  + repo + controller take `category` (free-text string match on
  `Material.Category`). Excel spec explicitly listed "prema kategoriji"
  under Filteri.

### Refactored
- **Serbian identifiers → English** in the Production module surface:
  `MagacinController` → `WarehouseController`; controller actions
  `GetStanje` → `GetStockBalances`, `GetIstorija` → `GetStockHistory`;
  query types `GetStanjeQuery` / `GetIstorijaQuery` + handlers +
  namespaces → `GetStockBalances*` / `GetStockHistory*`. Route URLs
  (`/warehouse/stock`, `/warehouse/history`) unchanged. Comments still
  mention Stanje / Ulaz / Izlaz / Istorija / Magacin / prijemnica —
  those map UI Serbian → English code and stay.

---

## 2026-06-07 — "Station" rename + quality-of-life batch

### Refactored
- **Pause/Resume rename**: `PauseStation*`/`ResumeStation*` → `PauseOnLogout*`/
  `ResumeOnLogin*` across entity props, methods, commands, routes (`POST
  /pause-on-logout`, `/resume-on-login`), and DB column
  (`paused_by_station_at` → `paused_on_logout_at`). Pure rename — no
  behaviour change. `StationRequest` → `WorkerProcessRequest`. The old
  naming was a leftover from an early prototype with physical stations.
- **`ResumeOnLoginCommand` per-worker scoping** (was bleeding into other
  workers' paused OIPs). Now only auto-resumes when the most-recent
  `OrderItemProcessLog` / `OrderItemSubProcessLog` `UserId` matches the
  logging-in user; falls back to `StartedByUserId` for pre-06.06 rows.
  Two new `ResumeStationTests` (→ now `ResumeOnLoginTests`) cover skip +
  resume.
- **Orders Migrations folder consolidated**. All migrations + ModelSnapshot
  in `Persistence/Migrations/`. EF tooling no longer needs `--output-dir`.

### Added
- **`StartProcessWorkTests`** — covers the `ValueGeneratedNever` +
  `Include(ProcessLogs)` fixes from 06.06 + already-started guard.

### Migrations
- `20260607103142_RenamePausedByStationAtToPausedOnLogoutAt` — uses
  `RenameColumn` (data preserved) on `order_item_processes` and
  `order_item_sub_processes`.

---

## 2026-06-06 — OrderItemProcessLog entity + Bojan/Sale Bug D-H batch

### Added
- **`OrderItemProcessLog` entity + `order_item_process_logs` table** —
  per-work-period log for process-level work (mirrors
  `OrderItemSubProcessLog` for processes WITHOUT sub-processes, e.g.
  Krojenje). Tracks each Start→Pause/Resume cycle with the working user.
  Reporting code now queries logs directly (was using the
  `StartedAt → PausedAt ?? CompletedAt` shortcut, which overcounted
  offline gaps / auto-logout windows as active).
- **Tablet auto-logout beep** (Web Audio oscillator) — 880Hz/300ms on
  warning window enter, 440Hz/1200ms on modal show.

### Fixed
- **`ResumeOnLogin` cumulative OT cap (Bug E)** — when relogin happens
  during overtime quota, per-session cap shrinks to remaining quota
  (`min(quotaLeftMinutes, perSessionCap)`), so the worker can't
  silently overrun MaxOvertimeHours by reopening sessions.
- **Trend chart Min line + range Area band** (Bug D). Tooltip + clip-label
  fix (`margin.right: 40`, XAxis padding 16/16). Efikasnost charts
  switched horizontal → vertical bars.
- **Dangling open process logs** (Bug F-style) — repo methods
  (`GetByIdWithOrderDetailsAsync`, `GetByIdWithFullDetailsAsync`,
  `GetInProgressByProcessIdAsync`) now `.Include(p => p.ProcessLogs)`
  so `EndOpenProcessLog()` actually closes them. Local cleanup SQL ran
  for 16 dangling rows; staging already clean.
- **`OrderItemProcessLog.Id` `ValueGeneratedNever()`** — without it EF
  treated Id as DB-generated and threw `DbUpdateConcurrencyException`
  at `StartProcessWork`.
- **Client-disconnect noise** — `GlobalExceptionHandlerMiddleware` now
  catches `BadHttpRequestException` → 400 + Warning (no Sentry page) and
  `OperationCanceledException` when `RequestAborted` → silent. Stops
  weekly Sentry digest from filling with torn-connection 500s.
- **Tablet `/queue` N+1** — `tablet-process-definitions` batched into a
  single `processesApi.getAll({ pageSize: 200 })` call (was firing one
  request per process row).

### Tests
- 4 new `WorkerHoursReportTests` + 2 `ActiveWorkSessionTests` Bug E
  scenarios + fixed 2 pre-existing flaky overtime tests
  (time-of-day-dependent shift-start anchoring).

### Migrations
- `20260606081854_AddOrderItemProcessLogs` — table + backfill SQL
  (`gen_random_uuid()`).

---

## 2026-06-04 — Bug B + Bug C (Bojan testing 04.06)

### Fixed
- **Bug B — process-level Aktivno attributed per user**. Previously a
  process WITHOUT sub-processes summed all worker time into a single
  bucket; now per-user via `OrderItemProcess.StartedByUserId`.
- **Bug C — cap + clip on trend chart**. Robust stats already applied
  in 05-27; clip-label fix here so labels don't overflow the SVG.
- **Auto-logout pauses processes WITHOUT sub-processes** — `PauseWork`
  walks sub-process logs only, so Krojenje-style processes kept ticking
  after auto-logout. AutoCheckOut now mirrors FE manual-logout by
  firing `PauseOnLogoutCommand` per user-process (in addition to
  `PauseWork`).
- **Auto-logout pauses sub-processes** (mirrors manual logout). Logout
  business logic must be identical, only diff allowed = backdated time
  + `WasAutoClosed` flag + `AutoLoggedOut` event.

### Tests
- Integration tests for Bug B (process-level Aktivno) + Bug C (cap +
  clip math).

---

## 2026-06-03 — AutoLogout BG service hardening + seed cleanup

### Fixed
- **`AutoLogoutBackgroundService` `InvalidCastException`** — projecting
  to `ValueTuple` via `Select(ValueTuple.Create(...))` made Npgsql try
  to read a Postgres `record` type. Materialize to anonymous type first,
  then tuple-ify in memory. Heartbeat log added (one Information line
  per scan) so silent crashes show up as "no heartbeat" in Sentry.
- **`AutoLogoutBackgroundService` missing tenant context** — synthetic
  `HttpContext` with `tenant_id`, `sub`, and `SuperAdmin` role claims so
  `OrdersDbContext`'s tenant filter resolves correctly (was returning
  zero open sessions every scan).
- Three Bojan-reported bugs from 03.06.2026 testing (auto-logout flow
  edge cases — see commit `c203e0d` for specifics).
- `--migrate` flag works in Development; safer shift cleanup
  (deletes the 3 duplicate "smjena" rows in the cleanup migration).

---

## 2026-06-02 — AutoLogout enforcement + WorkSession events

### Added
- **`AutoLogoutBackgroundService`** — proactive safety net every 2 min,
  scans all open `WorkSessions` across tenants and fires
  `AutoCheckOutCommand` for any past its cap. Routes through mediator
  so cap math + notifications + `WasAutoClosed` flag stay in one place.
- **`WorkSession.WasAutoClosed`** flag + `AutoLoggedOut` event.
- Cap calculation: `CheckIn + ShiftDuration + MaxOvertimeHours`. Switch
  shows spinner during BE save + error toast (Uključi/Isključi from
  Reports row).

### Migrations
- `20260601205742_AddWasAutoClosedToWorkSession`

---

## 2026-05-29 — Apply Sale/Bojan answers + role restrictions

### Fixed
- **Worker reports restricted to Department role** (Sati radnika +
  Efikasnost). Coordinators/managers/sales no longer show up as zero-
  hour rows.
- **Trajanje izrade proizvoda**: one row per order item, complexityShare,
  process duration = operator active time (sub-process sum / OIP total)
  rather than Stop−Start wall-clock.
- **Blokade po procesu**: average duration excludes 0-working-hour
  blocks.
- **Sati radnika / Efikasnost**: per-worker-per-day, regular/overtime
  split at shift duration, effective = worked − break, active =
  subprocess log union.

### Tests
- Integration tests for all of the above; `REPORTS.md` updated.

---

## 2026-05-27 — Trend MIN/MAX → two-pass robust stats (Bojan round 3)

### Fixed
- **Trend MIN/MAX**. Both prior interpretations (MINIFS/MAXIFS;
  literal μ±σ) were wrong. New: two-pass robust stats — Pass 1 μ₀/σ₀
  on raw, Pass 2 μ′/σ′ on samples in `[μ₀±σ₀]`. `MIN = max(0, μ′−σ′)`,
  `MAX = μ′+σ′`, `Realni prosek = μ′`. Same robust stats for the
  Normativ baseline. Tighter, visually meaningful band centered on
  the cleaned mean. PREDKROJENJE/S example: MIN ≈ 0, MAX ≈ 28.43
  (was 0.67 / 46 before).

### Tests
- `Trend_robust_min_max_uses_cleaned_mean_plus_minus_sigma`.

---

## 2026-05-26 — Three new /reports analyses + lazy auto-logout

### Added
- **`GET /api/reports/blocks-per-process`** — per-process roll-up of block
  requests with **working-hours** average duration (intersection of
  `CreatedAt → HandledAt` with the union of active shift windows, incl.
  cross-midnight). Approved = Approved + Resolved per Bojan; rejected
  contributes 0 duration. FE: new tab on `/reports` (table + 2 charts:
  avg duration + submitted-vs-approved). Spec: Bojan 25.05.2026.
- **`GET /api/reports/product-manufacturing-time`** — per-completed-order
  breakdown of process timings + inter-process gaps. **Najzastupljenija
  težina** tie-break (T/S→S, S/L→L, T/L→L, all-tied→L). Overlapping
  processes clipped (no negative gaps). FE: wide horizontally-scrollable
  all-processes table. Spec: Bojan 25.05.2026.
- **`GET /api/reports/work-efficiency`** — per-worker per-day breakdown
  of Pravo vreme rada / Aktivno na procesima (wall-clock union of
  subprocess log ranges) / Pauze / Efikasnost %. FE: new tab with
  color-coded efficiency column (≥80 green / 50–80 yellow / <50 red).
  Spec: Bojan 25.05.2026.
- **`GET /api/work-sessions/current`** — calling worker's open session +
  pre-computed `alarmAtUtc` / `logoutAtUtc` for the tablet auto-logout
  countdown banner. Cap = CheckIn + shift duration + MaxOvertimeHours.
- **`Shift` entity gained 4 per-shift config fields**: `BreakMinutes`,
  `MaxOvertimeHours`, `AutoLogoutAfterHours`, `AlarmBeforeLogoutMinutes`.
  Defaults (0/6/2/5) match Bojan's stated values. Admin → Smene form
  exposes them as direct fields (Bojan UI choice).
- **Lazy auto-logout** applied to `/reports/work-efficiency` and
  `/reports/worker-hours`: any session (open OR closed) whose end
  exceeds `CheckIn + ShiftDuration + MaxOvertimeHours` is treated as
  capped for reporting. No background service; pure read-side math.
- **Tablet auto-logout banner** — client-side countdown using shift
  config from `/work-sessions/current`. Banner appears at `alarmAtUtc`,
  turns red at `logoutAtUtc`. No SignalR; pure FE.

### Fixed
- **`/reports/process-time-trend` MIN/MAX math** — reverted a brief
  detour into literal μ±σ. Trend chart now consistently uses Excel's
  `MINIFS`/`MAXIFS` semantics (window-clamped smallest/largest sample
  inside the band) — same as the table. Avoids the 1-bucket / huge
  outlier explosion seen during Bojan review 26.05.2026.
- **FE trend chart UX** — auto-defaults to first process + complexity S
  on mount + adds a period selector (Mesec / 3 meseca / 6 meseci /
  Godina dana). No more "Izaberite proces i kompleksnost" wall.
- **Closed sessions with bogus durations** were silently bypassing the
  lazy auto-logout cap (the report used the stored `DurationMinutes`
  instead of recomputing from the effective end). Found via integration
  tests, fixed in both reports.

### Tests
- **36 new integration tests** across 6 files:
  - `BlocksPerProcessReportTests` (3) — roll-up math, cross-tenant, auth
  - `ProductManufacturingTimeReportTests` (5) — row count, last-gap=0,
    T/S→S tie-break, cross-tenant, auth
  - `WorkEfficiencyReportTests` (5) — closed-cap, open-past-cap,
    open-within-cap excluded, cross-tenant, auth
  - `ActiveWorkSessionTests` (4) — 204 when no session, alarm/logout
    math, null when no shift match, auth
  - `ShiftConfigTests` (8) — CRUD with new fields, Department user
    blocked on create/update (403), cross-tenant write rejected,
    GET isolation, negative-value validation
  - `ProcessTimeTrendTests` (5) — window-clamped math, outlier
    excluded, single sample, Normativ = 85% of trimmed mean, empty period
  - `WorkerHoursReportTests` (2) — closed-session cap, legit session
- Suite total: 79 (was 50), 76 passing + 3 pre-existing skips.

### Migrations
- `20260526171601_AddShiftTimeTrackingConfig` (Identity) — adds 4 int
  columns with defaults (0/6/2/5) to `identity.shifts`.

---

## 2026-05-24 — Test coverage + UX polish for /reports

### Added
- **Unit tests (11)** for `ReportingStats.ComputeStats` covering the
  window-clamped MIN/MAX + trimmed-mean math. See
  `tests/AlGreenMES.Tests.Unit/ReportingStatsTests.cs`.
- **Integration tests (13)** for the new `/reports` endpoints:
  `PATCH /excluded-from-reports` (persistence + 404 + cross-tenant),
  `GetProcessTimes` filtering by `IsExcludedFromReports`, funnel
  ready-logic (no-deps / unmet-dep / met-dep / InProgress + Blocked),
  delivery compliance on-time boundary, cross-tenant isolation for all
  three chart endpoints. See `tests/AlGreenMES.Tests.Integration/ReportsTests.cs`.
- **`docs/REPORTS.md`** — formulas, design decisions, dependency map.
  Captures the Sale/Bojan Excel spec inline so future sessions don't
  have to reverse-engineer it.
- FE: `Uključi` per-row switch shows antd's built-in spinner during the
  BE save + error toast if the PATCH fails (with auto-rollback).

### Refactored
- `ComputeStats` extracted from `ReportingQueryService` into a new public
  `ReportingStats` static class. Behavior identical; the service still
  calls it. Makes the math unit-testable without spinning up an HTTP host.

### Suite status
- 44 integration tests passing (was 31 before reports work). 0 failures.

---

## 2026-05-23 — Reports wave 2: Trend + Funnel charts

### Added
- `GET /api/reports/process-time-trend` — per-period (week/month) stats
  for a single (process × complexity). Returns buckets with window-
  clamped MIN/MAX + trimmed mean per bucket, plus a single Normativ =
  85% of trimmed mean across the whole filtered period (constant
  target line).
- `GET /api/reports/active-process-funnel` — per-process counts of
  active OrderItemProcesses split into:
  - InProgress → "U toku" (blue)
  - Ready → "Proces spreman za izvršavanje" (gray; Pending with every
    dependency complete-or-withdrawn)
  - Blocked → "Blokirano" (red)
  Dependency resolution mirrors `GetOrdersMasterView` (manual deps when
  order has them, category-level deps as fallback).

### Fixed
- Funnel endpoint was returning HTTP 500 due to an Include cycle in the
  no-tracking query (`OrderItem → Processes → OrderItem`). Switched to
  `AsNoTrackingWithIdentityResolution`. Sentry 9cfbe33.

---

## 2026-05-22 — Reports wave 1: Sale/Bojan feedback fixes

### Fixed
- **MIN/MAX formula**: switched from population min/max to window-
  clamped per the Excel StDev sheet (smallest sample ≥ μ−σ, largest
  ≤ μ+σ). Real-world impact: B PREDKROJENJE Srednje was showing max
  `48:38:28` (abandoned-process outlier); now shows `0:46:00`.
- **Parent process duration = sum of sub-process durations** when subs
  exist. Was wall-clock between Start/Complete (counted idle gaps);
  now sums only the active sub-process work. Fixes ORD-2026-025
  E-STAKLO showing `0:06:56` when subs summed to `0:03:21`.

### Added
- New BE column `IsExcludedFromReports` on `order_item_processes` (EF
  migration `AddIsExcludedFromReports`). `PATCH /api/order-item-processes/
  {id}/excluded-from-reports` toggles it. Excluded rows are filtered
  from `GetProcessTimes` aggregation at source.
- `GET /api/reports/delivery-compliance` — per-period (week/month)
  on-time vs late breakdown of completed orders.

---

## 2026-05-20 — Reports rework (Nikola, then refinement)

### Changed
- Renamed endpoint `/api/reports/process-averages` → `/api/reports/
  process-times`. New DTOs (`ProcessTimesDto`, `ProcessTimeItemDto`,
  `ComplexityStatsDto`) — decimal-minutes unit (BE divides the legacy
  seconds-storing-as-minutes column by 60).
- `ReportsController` derives tenantId from JWT via `ITenantService`
  instead of `[FromQuery]`. Matches every other controller's pattern.
- Time-tracking DTO: replaced `productName` with `productCategoryName`,
  added `orderType` + `subProcesses[]` drill-down per row, renamed
  `totalDurationMinutes` (which stored seconds) → `durationSeconds`,
  dropped the response summary block.
- Added filters: `productCategoryIds` + `orderTypes` on both report
  queries; `orderNumber` substring (ILIKE) on time-tracking.

### Added
- EF migration `AddPausedByStationAt` for tablet station-pause
  auto-resume (Sprint 2.4b prep).

---

## 2026-05-18 — Sprint 3.0 security hardening

### Security
- **F-1**: last-Admin removal blocked via `LAST_ADMIN_REMOVAL`
  DomainException → 403. Prevents tenant from locking itself out.
- **F-2**: `DeleteUser` blocks deletion of the last active Admin too.
- **F-3**: refresh tokens revoked on role change. JWT TTL is 60 min so
  freshly-issued tokens still work until expiry, but the user can't
  refresh into a new token with old privileges.
- **F-7**: only SuperAdmin can change ANY user's role (incl. their
  own). `UpdateUserCommandHandler` throws `FORBIDDEN_ROLE_CHANGE`.
- **F-11**: `ChangePassword` only allowed for self (non-SuperAdmin
  can't change other users' passwords).
- Integration tests for all five guards in `IdentityAuthzTests.cs`.

### Fixed
- Authz exceptions now use `ForbiddenException` so they map to HTTP
  403 (was 500). FE 403 toaster works correctly.

---

## 2026-05-16 — Sprint 3.6 ops + performance

### Performance
- Master-view N+1 fixed: batch order-item lookup + `AsSplitQuery`.
- Tablet active/queue N+1 fixed.

### Fixed
- Tablet station-pause: stamp `PausedByStationAt` when ending
  sub-process logs at worker logout. Without this, station-pause
  resume wouldn't auto-restart sub-process timers.
- Sentry no longer captures expected business-rule exceptions
  (`DomainException`, `NotFoundException`, `ForbiddenException`) — was
  drowning the dashboard.

---

## 2026-05-15 — Sprint 3.3–3.5 infrastructure

### Added
- `/api/health/live` + `/api/health/ready` endpoints (Sprint 3.3).
- `AuditableEntityInterceptor` — auto-stamps `CreatedAt` / `UpdatedAt`
  / `CreatedByUserId` / `UpdatedByUserId` on every Modified entry
  (Sprint 3.5). Replaces manual `SetUpdated()` calls that were silently
  missed on some entities.
- `--migrate` CLI flag — extracted DB migrations from startup. Deploys
  can now run migrations as a discrete step before the service boots
  (Sprint 3.4).

### Fixed
- Serilog request summary now captures 401/403 short-circuits (was
  blank for unauthorized requests).
- Health endpoints mounted under `/api/health/*` so nginx routing
  works without separate location blocks.
