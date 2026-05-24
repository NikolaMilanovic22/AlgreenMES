# /reports — formulas, design, and dependency map

Source of truth for everything the `/api/reports/*` endpoints compute. Sale/Bojan
sent the spec as an Excel file (`sastanak 20.05.2026^.xlsx`) plus follow-up text
feedback (22.05.2026 + 23.05.2026); this doc captures it in the repo so future
sessions don't have to reverse-engineer formulas from screenshots.

## Endpoints

| Endpoint | Returns | Used by FE chart/tab |
|---|---|---|
| `GET /api/reports/process-times` | Per (process × complexity) aggregate stats | "Vremena po procesu" table + "Prosečno vreme po procesu" bar chart |
| `GET /api/reports/time-tracking` | Per-row completed process detail incl. sub-processes | "Praćenje vremena" table |
| `GET /api/reports/process-time-trend` | Per-period (week/month) stats for ONE (process × complexity) | "Trend prosečnog vremena po nedelji" chart |
| `GET /api/reports/delivery-compliance` | Per-period on-time vs late order count | "Analiza kašnjenja i poštovanja rokova" chart |
| `GET /api/reports/active-process-funnel` | Per-process active OIP counts split by status | "Napredak aktivnih narudžbina" chart |
| `GET /api/reports/worker-hours` | Per-worker time totals + daily breakdown | "Sati radnika" table |
| `PATCH /api/order-item-processes/{id}/excluded-from-reports` | (204) Toggle the IsExcludedFromReports flag | "Uključi" switch in Praćenje vremena |

## Core math: window-clamped MIN/MAX + trimmed mean

The Excel spec's StDev sheet defines a 1-sigma window for filtering outliers.
All `/reports` aggregations use this formula consistently:

```
μ           = AVERAGE(samples)
σ           = sqrt(AVERAGE((xi − μ)²))        // population stdev
window      = [μ − σ, μ + σ]

count       = samples.length                  // raw count, NOT window-restricted
avgMinutes  = μ
minMinutes  = MIN of samples inside the window     (i.e. smallest sample ≥ μ − σ)
maxMinutes  = MAX of samples inside the window     (i.e. largest sample ≤ μ + σ)
stdevMinutes        = σ
trimmedMeanMinutes  = AVERAGE of samples inside the window  // "Realni prosek"
```

**Why window-clamped, not population min/max?** Sale/Bojan's spec
(22.05.2026 feedback) explicitly: *"MIN treba da bude prvi veći od μ − σ, dok
MAX treba da bude prvi manji od μ + σ"*. Real-world example: B PREDKROJENJE
Srednje had a 48-hour abandoned process. Window-clamped MAX excluded it
(0:46:00 instead of 48:38:28), so the displayed range reflects normal-flow
variation, not forgotten-timer outliers.

**Why count is NOT window-restricted.** Sale/Bojan need to see how many real
process completions the bucket aggregates. Trimmed-window count hides
sample-size pressure on the stat reliability.

**Edge cases** (see `ReportingStats.ComputeStats` + unit tests):
- Empty input → throws `ArgumentException`
- Single sample → all five window stats return the sample value
- All-identical samples (σ = 0) → window degenerates to that value
- Bimodal pathology (no sample inside [μ−σ, μ+σ]) → fall back to population
  min/max + plain mean (rare; defensive)

Implementation: `src/Modules/Orders/AlGreenMES.Modules.Orders.Infrastructure/Services/ReportingStats.cs`
Unit tests: `tests/AlGreenMES.Tests.Unit/ReportingStatsTests.cs`

## Parent process duration = sum of sub-process durations

**Bug Sale/Bojan reported (22.05.2026):** ORD-2026-025 process E-STAKLO showed
0:06:56 but its sub-processes (ZALIVANJE TERMO STAKLA 0:00:11 + REZANJE STAKLA
0:03:10) summed to 0:03:21. The parent column was wrong by 3:35.

**Cause:** `OrderItemProcess.TotalDurationMinutes` counts wall-clock time
between `Start()` and `Complete()`, including idle gaps between sub-process
activations (worker steps away, sub-process A ends, worker comes back 5 min
later and starts sub-process B). For reports we want only the active
sub-process work time.

**Fix:** `EffectiveDurationSeconds(p)` in `ReportingQueryService` returns
`sum(p.SubProcesses.Where(!IsWithdrawn).Select(sp => sp.TotalDurationMinutes))`
when sub-processes exist, else the parent column. Applied in both
`GetProcessTimes` (aggregation) and `GetTimeTrackingReport` (display).

DB column is unchanged — wall-clock duration still recorded for the
underlying timer logic. Only the report layer projects to "effective".

## The legacy `TotalDurationMinutes` column actually stores SECONDS

Pre-existing bug documented in `memory/gotchas.md`. The column was named
"minutes" but the timer code writes elapsed seconds into it. Don't rename
without sweeping all callers. Reports convert by dividing by 60 to get the
decimal minutes used in the DTOs (`avgMinutes`, etc.).

## IsExcludedFromReports — server-side manual exclusion

**Field:** `OrderItemProcess.IsExcludedFromReports` (boolean, default false).
Migration `20260523105623_AddIsExcludedFromReports`.

**Toggle endpoint:** `PATCH /api/order-item-processes/{id}/excluded-from-reports`
with `{ excluded: bool }`. Returns 204. 404 if id unknown. Tenant query
filter rejects cross-tenant writes (NotFoundException at the repo layer).

**Where the flag is honored:**
| Endpoint | Behavior |
|---|---|
| `GetProcessTimes` | Excluded rows filtered out at source (`WHERE NOT IsExcludedFromReports`). Excluded samples do not contribute to averages. |
| `GetProcessTimeTrend` | Same — excluded rows are filtered from per-period stats. |
| `GetTimeTrackingReport` | Excluded rows DO appear (FE renders them faded with the toggle off, so user can re-include them). The flag is exposed per row as `isExcludedFromReports`. |
| XLSX/CSV export from Praćenje vremena | FE filters `includedItems = items.filter(i => !i.isExcludedFromReports)` before export. |
| `GetActiveProcessFunnel` / `GetDeliveryCompliance` | Not honored. The funnel measures live in-flight state (the exclusion is a stats-bias guard, not a "hide this order" toggle). Delivery compliance counts every completed order's on-time status. |

## "Ready to execute" — the most complex derived status

The funnel's `Spreman za izvršavanje` (gray) bucket: a Pending OIP counts as
ready ONLY when every dependency of that process is `Completed` or `Withdrawn`
on the same OrderItem. If any dep is still `Pending` / `InProgress` / `Blocked`,
the OIP is "waiting on dep" and is NOT counted in any of the three funnel
buckets (Sale/Bojan's spec only tracks three active statuses).

**Dependency resolution priority** (matches `GetOrdersMasterView` to keep the
funnel and the live drawer consistent):
1. If the order has manual processes (`Order.HasManualProcesses == true`),
   use `Order.ManualProcessDependencies` for that order.
2. Otherwise, use `ProductCategoryDependency` rows for the item's product
   category.

**Edge cases** (covered by integration tests):
- Dep references a process not present on this item → treat as "effectively
  withdrawn" → ready.
- Dep is `Withdrawn` → counted as satisfied → ready.

Implementation: `ReportingQueryService.GetActiveProcessFunnelAsync` →
`IsReady(p)` local function. Note: uses `AsNoTrackingWithIdentityResolution`
because the `Include(OrderItem.Processes)` chain forms a cycle (sibling
processes ⊃ this process ⊃ this OrderItem); plain `AsNoTracking` refuses
cycles and throws.

## Period bucketing (Trend + Delivery compliance)

`GetDeliveryComplianceAsync` and `GetProcessTimeTrendAsync` both bucket
samples by ISO week (Monday-start) or calendar month. Helper:
`BucketStart(d, granularity)`. Both return `DateTime.Kind = Utc` start
date so the FE can format consistently.

**On-time vs late** (Delivery compliance only): day-precision comparison.
`CompletedAt.Date <= DeliveryDate.Date` → on time, else late. Delivery
dates have wall-clock-of-day semantics; we don't compare timestamps.

## Normativ (cilj) — the red dashed line on the Trend chart

`Normativ = 85% × trimmedMean(all samples in the filtered period)`.

A constant horizontal target line — not per-bucket. Computed BE-side so all
clients see the same number (otherwise per-client floating-point drift would
cause display variance).

When the filter window has zero samples, `normativMinutes` is `null`. FE skips
rendering the line.

## Tip narudžbine names come from `/api/order-types`

`Order.OrderType` is the immutable code (`Standard`, `Repair`, `Complaint`,
`Rework`, etc.). The configurable display name lives on the per-tenant
`OrderType` entity. Reports return the code; FE resolves to name via the
`orderTypesApi.getAll()` query (cached by react-query, shared across the
two report tabs).

If admin renames a type, the new name appears on the next refresh — no FE
state update needed because the name resolution happens per render via the
fresh react-query cache.

## Test coverage

| Test type | Where | What |
|---|---|---|
| Unit (11 tests) | `tests/AlGreenMES.Tests.Unit/ReportingStatsTests.cs` | Window-clamped MIN/MAX, trimmed mean, edge cases, rounding |
| Integration (13 tests) | `tests/AlGreenMES.Tests.Integration/ReportsTests.cs` | PATCH exclusion (persistence, 404, cross-tenant), `process-times` filtering, `time-tracking` flag exposure, funnel ready logic (no deps / unmet dep / met dep / InProgress + Blocked), delivery compliance on-time boundary, cross-tenant isolation for all 3 chart endpoints |

Run locally with the existing macOS Testcontainers setup:
```
DOCKER_HOST=unix:///var/run/docker.sock dotnet test
```

## Mirrored to easy-mes-be

The full reports stack (BE code + tests + this doc) lives byte-identical in
`easy-mes-be` at `/Users/milosmitrovic/Projects/skyhard/easy-mes-be`. Any
change here must be mirrored. Use `diff -rq src/ tests/` to verify drift.
