# AlGreen MES Backend

> **New Claude session: read `docs/CLAUDE_ONBOARDING.md` first.** Covers the full picture across the repos (algreen pilot + alblue staging; 1 BE + 2 FE), infrastructure, deploy commands, Sprint 3 outcomes, workflow rules, gotchas, and Milos's working preferences. This file is per-repo coding conventions only.

## Overview
.NET 9 Web API, modular monolith, PostgreSQL, JWT auth, SignalR real-time.

## Project Structure
```
AlgreenMES.API/              → Host (Program.cs, middleware, DI)
src/
  BuildingBlocks/Common/     → Shared exceptions, base classes
  Modules/
    Identity/                → Users, auth, shifts, JWT/refresh tokens
    Orders/                  → Orders, processes, work sessions, block/change requests, notifications
    Production/              → Processes, product categories, special request types, sub-processes
    Tenancy/                 → Multi-tenant support
```

## Module Structure (each module)
```
{Module}.Domain/             → Entities, enums, repository interfaces
{Module}.Application/        → Commands/, Queries/, DTOs/, Interfaces/
{Module}.Infrastructure/     → Repositories, persistence, EF configs, services
{Module}.Api/                → Controllers, Requests (API DTOs)
```

## Key Commands
```bash
./setup.sh                                      # One-time: activate git pre-commit hooks
dotnet build                                    # Build
dotnet run --project AlgreenMES.API             # Run on :5030
dotnet ef migrations add {Name} -p src/Modules/Orders/...Infrastructure -s AlgreenMES.API
```

## Pre-commit hooks
`./setup.sh` (one-time after clone) points git at `.githooks/` so the
pre-commit script fires on every commit. Catches regressions we've
actually shipped or could plausibly ship (~0.5s total):
1. **`EF.Property<Guid>(... "TenantId" ...)`** — must be `Guid?`
   (19.06.2026 cross-tenant filter no-op; CHANGELOG 2026-06-19).
2. **Magic `IsInRole("…")`** — must use `RoleNames.*` / `IsSuperAdmin`.
3. **Magic `[Authorize(Roles = "…")]`** — must use `RoleGroups.*`.
4. **Merge conflict markers** — fails on staged `<<<<<<<` / `=======` /
   `>>>>>>>`.
5. **`DateTime.Now`** — must be `DateTime.UtcNow` (Postgres columns are
   `timestamptz`; mixing local time corrupts stored times).
6. **`.Result` / `.GetAwaiter().GetResult()`** on Tasks — classic async
   deadlock pattern in ASP.NET request contexts.
7. **Controller without `[Authorize]` or `[AllowAnonymous]`** —
   accidentally-public endpoint. Same blast radius as the tenant-filter
   regression.
8. **`Console.WriteLine`** — must be `ILogger` (Serilog enrichment is
   how we get correlation id / tenant id into log queries).
9. **`Thread.Sleep`** — must be `await Task.Delay(...)` (sync sleep in
   async pipelines starves ThreadPool).

Bypass with `git commit --no-verify` only when reverting.

## Tooling
- **CI**: `.github/workflows/ci.yml` runs `dotnet build` + the full
  integration test suite (Testcontainers spins up Postgres on the GHA
  Ubuntu runner) on every push to `staging`/`master` + PR. Catches
  anything bypassed via `--no-verify`.
- **Dependabot**: `.github/dependabot.yml` — weekly NuGet patch/minor
  PRs grouped, immediate security advisories, monthly GHA updates.

## Patterns
- **CQRS + MediatR**: Commands return DTOs, Queries return DTOs/lists
- **Command structure**: `{Name}Command.cs` (record : IRequest<T>) + `{Name}CommandHandler.cs`
- **API Requests**: `Api/Requests/{Name}Request.cs` (record, mapped to command in controller)
- **Repos**: Interface in Domain, implementation in Infrastructure
- **Unit of Work**: `IOrdersUnitOfWork.SaveChangesAsync()` for transactional saves
- **Mapping**: Mapster (`entity.Adapt<Dto>()`)
- **Error handling**: DomainException → 400, NotFoundException → 404, ValidationException → 422, ForbiddenException → 403

## Key Entities (Orders module)
- `Order` → `OrderItem` → `OrderItemProcess` → `OrderItemSubProcess` → `OrderItemSubProcessLog`
- `WorkSession` (check-in/check-out — currently unused by tablet)
- `BlockRequest`, `ChangeRequest`, `Notification`, `OrderAttachment`

## Timer Mechanism
Two paths depending on whether process has sub-processes:
1. **No sub-processes**: `OrderItemProcess.Pause()` / `ResumeTimer()` — uses `PausedAt` + `AccumulatedDurationSeconds`
2. **With sub-processes**: `OrderItemSubProcessLog.StartLog()` / `End()` — duration stored in seconds via `TotalSeconds`

Station-level pause/resume: `PauseStation` / `ResumeStation` commands (called by tablet on logout/login)

## API Base URL
`http://localhost:5030/api`

## Controllers → Routes
- `/api/auth` — login, refresh, change-password
- `/api/users` — CRUD
- `/api/orders` — CRUD, activate, cancel, reopen, attachments
- `/api/order-item-processes` — start, stop, resume, complete, block, unblock, withdraw, pause-station, resume-station
- `/api/sub-processes` — start, complete
- `/api/block-requests` — create, approve, reject
- `/api/change-requests` — create, approve, reject
- `/api/processes` — CRUD, reorder
- `/api/product-categories` — CRUD, reorder processes
- `/api/special-request-types` — CRUD
- `/api/work-sessions` — check-in, check-out (unused by tablet)
- `/api/tablet` — getQueue, getActive, getIncoming
- `/api/dashboard` — stats endpoints
- `/api/notifications` — list, mark read/unread, delete
- `/api/tenants` — CRUD
- `/api/shifts` — CRUD

## SignalR
Hub: `/hubs/production` (JWT via query string)
Groups: `tenant-{id}`, `process-{id}`
