# Changelog

All notable changes to the Algreen MES backend. Format: roughly
[keep-a-changelog](https://keepachangelog.com/) — grouped by date, with
short user-facing summaries and links to the deepest relevant doc/code.

Mirrored to `easy-mes-be` (skyhard) — keep both in sync when editing.

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
  for 16 dangling rows; staging + easy-mes already clean.
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
