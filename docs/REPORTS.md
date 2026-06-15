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
| `GET /api/reports/blocks-per-process` | Per-process block-request roll-up + **working-hours** avg duration | "Blokade po procesu" tab (table + 2 charts) |
| `GET /api/reports/product-manufacturing-time` | Per-completed-order process timings + inter-process gaps | "Trajanje izrade proizvoda" tab (wide table) |
| `GET /api/reports/work-efficiency` | Per-worker per-day Pravo vreme rada / Aktivno / Pauze / Efikasnost % | "Efikasnost radnog vremena" tab |
| `GET /api/work-sessions/current` | Calling worker's open session + alarm/logout timestamps | Tablet auto-logout banner |
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

## Blokade po procesu — working-hours duration math

Per Bojan 25.05.2026: average block duration counts only **active shift
hours**, not wall-clock. A block opened Friday 13:00 and resolved Monday
07:00 should not claim 66 hours — most of that span is night/weekend.

Implementation: `WorkingMinutesBetween(from, to, shifts)` walks the date
range day-by-day and sums each day's intersection of `[from, to]` with
every active shift's `[StartTime, EndTime]` window. Cross-midnight
shifts (`End ≤ Start`, e.g. 22:00–06:00) are split into two windows
(`[Start, 24:00)` on day N + `[00:00, End)` on day N+1).

Approved = `Approved + Resolved` (per Bojan); rejected blocks count
toward `totalSubmitted` but contribute 0 duration to the average.

**0-working-hour blocks are excluded from the average** (Bojan 29.05.2026,
"izbaciti 0"): an approved/resolved block whose `[CreatedAt, HandledAt]`
span lands entirely outside every shift window has 0 working hours. It
still counts toward `totalSubmitted`/`approved`, but is dropped from the
average denominator so it no longer drags the average toward zero
(e.g. CNC stops showing 28h = (56+0)/2 and shows 56h).

## Trajanje izrade proizvoda — najzastupljenija težina + overlap clipping

Per Bojan 25.05.2026:

**Najzastupljenija težina** — mode of `Complexity` across all OIPs in the
order, with **low-bias tie-break**: T/S equal → S, S/L equal → L,
T/L equal → L, all three tied → L. Null when no OIP has complexity set.

**Overlap clipping** — when process N+1's `StartedAt` precedes process
N's `CompletedAt`, the inter-process gap is `max(0, raw_gap)` (zero, not
negative). Rows are per ORDER ITEM (29.05.2026 reshape); within an item,
OIPs sharing a ProcessId collapse to one slot via `Min(StartedAt)` +
`Max(CompletedAt)` — used only to order columns and compute the gap.

**Trajanje procesa = active time, not Stop−Start** (Bojan 29.05.2026,
List 2 Q2): each process's `durationSeconds` is the operator's active
work time — sub-process `TotalDurationMinutes` sum when present, else the
OIP's own active-timer total — the same basis as the "Vremena po procesu"
tab, NOT the wall-clock `Completed − Started` span. The slot's
`Min`/`Max` timestamps drive only the inter-process gap, not the duration.

Two totals: `totalWithoutGapsSeconds` (active durations only) and
`totalWithGapsSeconds` (active durations + positive gaps).

## Efikasnost radnog vremena — wall-clock union + lazy auto-logout

Per Bojan 25.05.2026:

```
Pravo vreme rada      = sum(session.CheckOut − session.CheckIn) per worker per day
Aktivno na procesima  = wall-clock UNION of all OrderItemSubProcessLog
                         [StartTime, EndTime] for that worker on that day
                         (parallel sub-processes counted ONCE, not summed)
Pauze                 = max(0, Pravo vreme rada − Aktivno na procesima)
Efikasnost %          = Aktivno / Pravo vreme rada × 100
```

**Worker scope** (Milos 29.05.2026): both "Sati radnika" and "Efikasnost
radnog vremena" include ONLY `Department`-role users (factory-floor workers).
Admin / Manager / Coordinator / SalesManager / SuperAdmin are excluded even if
they have a check-in session — filtered in `ComputeWorkerDayStatsAsync`. The FE
worker-filter dropdowns already query `role=Department`.

FE thresholds (Bojan programmer-notes, sheet 3): efficiency color ≥80 green /
60–79 yellow / <60 red. Status (Efikasnost tab only): ≥80 Odlično / 60–79
Prihvatljivo / 40–59 Ispod norme / <40 Neprihvatljivo. Two charts on the
Efikasnost tab: "Raspodela radnog vremena" (Aktivno + Nepokriveno stacked,
horizontal per worker) and "Efikasnost po radniku %".

### Lazy auto-logout — applied to both Efikasnost and Sati radnika

Per Bojan 25.05.2026 + design discussion 26.05.2026 (lazy approach,
no background service):

```
shiftDuration = Shift.EndTime − Shift.StartTime    (handles cross-midnight)
cap           = CheckIn + shiftDuration + Shift.MaxOvertimeHours hours

effectiveEnd  =
  null                         if no shift matches CheckIn time-of-day
  min(CheckOut, cap)           if session is closed
  cap                          if session is open AND now ≥ cap
  null (excluded from report)  if session is open AND now < cap
```

The shift is resolved by matching `TimeOnly.FromDateTime(CheckIn)` against
each active shift's `[StartTime, EndTime]` window. Both reports always
recompute `duration` from `effectiveEnd − CheckIn` — never the stored
`DurationMinutes` — so capped sessions don't silently use uncapped
DB values (regression caught by integration test 26.05.2026).

Implementation: `ComputeEffectiveSessionEnd` in
`ReportingQueryService.cs`. Used by `GetWorkEfficiencyAsync`,
`GetWorkerHoursReportAsync`, and `GetActiveWorkSessionAsync`.

## Tablet auto-logout banner (no SignalR)

`GET /api/work-sessions/current` returns the worker's open session plus
pre-computed `alarmAtUtc` / `logoutAtUtc` (the same shift-matching math
as the lazy cap). The tablet polls every 5 min + on focus, drives a
local `setInterval` ticking every 60s. Banner shows orange at
`alarmAtUtc`, red at `logoutAtUtc`. There's no server-pushed enforcement
— the report-side cap is what limits "claimed" working time.

## Per-shift time-tracking config

`Shift` entity gained 4 fields (Bojan spec 25.05.2026):

| Field | Default | Drives |
|---|---|---|
| `BreakMinutes` | 0 | (FE display only for now) |
| `MaxOvertimeHours` | 6 | Lazy auto-logout cap |
| `AutoLogoutAfterHours` | 2 | (Reserved for future background-job mode) |
| `AlarmBeforeLogoutMinutes` | 5 | Tablet banner trigger time |

Admin → Smene form exposes all four as direct InputNumber fields (Bojan
preferred option (a) over a nested subsection). Validation rejects
negative values via DomainException.

## Test coverage

| Test type | Where | What |
|---|---|---|
| Unit (11 tests) | `tests/AlGreenMES.Tests.Unit/ReportingStatsTests.cs` | Window-clamped MIN/MAX, trimmed mean, edge cases, rounding |
| Integration (legacy 13) | `tests/AlGreenMES.Tests.Integration/ReportsTests.cs` | PATCH exclusion (persistence, 404, cross-tenant), `process-times` filtering, `time-tracking` flag exposure, funnel ready logic, delivery compliance on-time boundary, cross-tenant isolation |
| Integration (5) | `BlocksPerProcessReportTests.cs` + `ProductManufacturingTimeReportTests.cs` | Blokade roll-up + cross-tenant; Trajanje row count + last-gap=0 + T/S→S tie-break + cross-tenant |
| Integration (5) | `WorkEfficiencyReportTests.cs` | Closed-session cap, open-past-cap, open-within-cap excluded, cross-tenant, auth |
| Integration (4) | `ActiveWorkSessionTests.cs` | 204 when no session, alarm/logout math, null when no shift match, auth |
| Integration (8) | `ShiftConfigTests.cs` | CRUD with new fields, Department user blocked (403), cross-tenant write rejected, GET isolation, negative-value validation |
| Integration (5) | `ProcessTimeTrendTests.cs` | Window-clamped math, outlier excluded, single sample, Normativ = 85% of trimmed mean, empty period |
| Integration (2) | `WorkerHoursReportTests.cs` | Closed-session cap, legit session passes through |

Total: 79 integration tests, 76 passing + 3 pre-existing skips.

Run locally with the existing macOS Testcontainers setup:
```
DOCKER_HOST=unix:///var/run/docker.sock dotnet test
```

## Mirrored to easy-mes-be

The full reports stack (BE code + tests + this doc) lives byte-identical in
`easy-mes-be` at `/Users/milosmitrovic/Projects/skyhard/easy-mes-be`. Any
change here must be mirrored. Use `diff -rq src/ tests/` to verify drift.
