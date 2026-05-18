# AlGreen MES Backend

> **New Claude session: read `docs/CLAUDE_ONBOARDING.md` first.** Covers the full picture across all 5 repos (algreen pilot + alblue staging + easy-mes side-business; 2 BE + 3 FE), infrastructure, deploy commands, Sprint 3 outcomes, workflow rules, gotchas, and Milos's working preferences. This file is per-repo coding conventions only.

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
dotnet build                                    # Build
dotnet run --project AlgreenMES.API             # Run on :5030
dotnet ef migrations add {Name} -p src/Modules/Orders/...Infrastructure -s AlgreenMES.API
```

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
