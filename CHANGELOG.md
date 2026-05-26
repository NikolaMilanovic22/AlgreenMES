# Changelog

All notable changes to the Algreen MES backend. Format: roughly
[keep-a-changelog](https://keepachangelog.com/) — grouped by date, with
short user-facing summaries and links to the deepest relevant doc/code.

Mirrored to `easy-mes-be` (skyhard) — keep both in sync when editing.

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
