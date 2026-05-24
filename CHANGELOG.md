# Changelog

All notable changes to the Algreen MES backend. Format: roughly
[keep-a-changelog](https://keepachangelog.com/) — grouped by date, with
short user-facing summaries and links to the deepest relevant doc/code.

Mirrored to `easy-mes-be` (skyhard) — keep both in sync when editing.

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
