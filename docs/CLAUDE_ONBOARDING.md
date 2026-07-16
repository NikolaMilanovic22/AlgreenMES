# CLAUDE_ONBOARDING.md — Cold-start context for a fresh Claude session

If you are a new Claude session opened on any of these repos: **read this first.** It is the canonical handoff doc covering everything across the repos that share this codebase tree. Existing memory files in `~/.claude/projects/.../memory/` may also be relevant but they assume context you don't have yet — start here.

> **Note (2026-07-16):** this doc is Sprint-3-era and stale in places (e.g. it still calls the algreen pilot "FROZEN" — it went live ~24.06.2026, and now takes mirror waves from alblue after QA) and is due a fuller refresh.

---

## 1. The product map (who uses what)

Two environments, one backend, one shared developer (Milos) + one BE collaborator (Nikola):

| Environment | Audience | Repo (BE) | Repo (FE) | Status |
|---|---|---|---|---|
| **algreen** pilot | Mile's company (real production, paying user) | `algreen-tracker-be` (master branch) | `algreen-tracker-fe` (main) | Live (thawed ~24.06.2026). Deploy only after Sale/Bojan QA + after factory hours. |
| **alblue** staging | Bojan + Sale test before Mile gets it | `algreen-tracker-be` (staging branch) | `alblue-tracker-fe` (main) | Active source for new work |

Key rule: **algreen pilot is Mile's production.** Never deploy to it casually. Bojan/Sale test alblue first; when satisfied, the same code goes to algreen pilot — but only when Milos explicitly thaws.

---

## 2. Repo layout (absolute paths on Milos's Mac)

```
/Users/milosmitrovic/Projects/skysoft/algreen/algreen-mes/algreen-tracker-be   ← BE source (alblue + algreen)
/Users/milosmitrovic/Projects/skysoft/algreen/algreen-mes/algreen-tracker-fe   ← FE pilot (algreen)
/Users/milosmitrovic/Projects/skysoft/algreen/alblue-mes/alblue-tracker-fe     ← FE staging (alblue)
```

**BE single source of truth:** `algreen-tracker-be` (one repo, `staging`→alblue / `master`→algreen).

**FE two separate repos** with diverged branding (logos, theme colors, titles, localStorage keys, workspace import names: `@algreen/*`, `@alblue/*`). **Never `cp` between FE repos.** Use line-by-line `Edit` (or rsync for full mirror waves). The mirroring rule has burned past Claude sessions twice already (see `gotchas.md`).

---

## 3. Infrastructure

### Droplets

| Droplet IP | Role | Hosts |
|---|---|---|
| `46.101.166.137` (Frankfurt, Ubuntu 24.04, 2GB) | skysoft | Both alblue + algreen pilot (separate `/opt/*` paths, separate systemd services, separate Postgres DBs in same container) |

### Postgres containers

- skysoft droplet: `algreen-postgres-1` (one container, two DBs: `algreen_tracker`, `alblue_tracker`)

### Domain map

| Service | URL |
|---|---|
| algreen dashboard | `https://tracker-app.algreen.rs` |
| algreen tablet | `https://tracker-tablet.algreen.rs` |
| algreen API | `https://tracker-api.algreen.rs` |
| alblue dashboard | `https://alblue.duckdns.org` |
| alblue tablet | `https://alblue-tablet.duckdns.org` |
| alblue API | `https://alblue.duckdns.org/api/*` (same domain, nginx routes `/api/*`) |

### SSH

`ssh root@46.101.166.137` — skysoft droplet. Key already in Milos's `~/.ssh/`.

### Deploy commands

**BE (`algreen-tracker-be`):**
```bash
./deploy.sh staging        # → alblue (branch=staging, /opt/alblue/api/, alblue-api service)
./deploy.sh pilot          # → algreen (branch=master,  /opt/algreen/api/, algreen-api service)
```

Each `deploy.sh staging/pilot`:
1. Refuses to run on a dirty working tree
2. Checks out the target branch, pulls
3. `dotnet publish`
4. `rsync` (excludes `appsettings.Production.json` and `uploads/`)
5. Runs `dotnet AlgreenMES.API.dll --migrate` against the deployed binary (migrations as an explicit step, not on startup — Sprint 3.4)
6. Restarts the systemd service

**FE deploys** are simpler: `./deploy.sh dashboard|tablet|all` in each FE repo. `algreen-tracker-fe` is pilot-only (no flag — only deploys to `/opt/algreen/`). `alblue-tracker-fe` deploys to `/opt/alblue/`.

---

## 4. Architecture quick reference

**.NET 9 modular monolith** with 4 modules in BE:
- `Tenancy` — tenants table, multi-tenant glue
- `Identity` — users, roles, JWT, refresh tokens
- `Production` — processes, sub-processes, product categories, special request types
- `Orders` — orders, order items, work sessions, notifications, push, change/block requests, dashboard queries

**Multi-tenancy model:**
- Single Postgres per environment, multiple tenants by `tenant_id` column
- `HasQueryFilter` on every tenant-scoped entity, scoped by `ITenantService.GetCurrentTenantId()`
- `ITenantService` reads `tenant_id` claim from JWT
- `ICurrentUserService` reads `sub` claim from JWT for user ID
- Bypass paths (login, refresh, seeder) use `.IgnoreQueryFilters()` and pass `tenantId` explicitly

**Roles:** `SuperAdmin` (platform), `Admin` (tenant), `Manager`, `Coordinator`, `SalesManager`, `Department`. Sprint 3.0 added: only SuperAdmin can change roles via `UpdateUserCommandHandler` (throws `ForbiddenException("FORBIDDEN_ROLE_CHANGE", ...)`).

**Audit columns:** `AuditableEntityInterceptor` (Sprint 3.5) auto-stamps `created_at`/`created_by_user_id`/`updated_at`/`updated_by_user_id` on every save for entities implementing `IAuditableEntity`. Older rows updated pre-3.5 have `NULL` audit columns — historical, not a bug.

**Exception → HTTP code mapping** (`GlobalExceptionHandlerMiddleware`):
- `NotFoundException` → 404
- `ValidationException` → 422
- `ForbiddenException` → 403
- `DomainException` (parent class) → 400
- Anything else → 500

**Observability stack** (Sprint 3.1-3.6):
- Sentry SDK on BE + FE. Single project `mes-api` (team `#sky-hard`). Distinguished by `environment` tag: `alblue-staging`, `algreen-pilot`. DSN: `https://315954545e637502fd5497b3090b5c9c@o4511398917177344.ingest.de.sentry.io/4511398994313296`. Lives in `appsettings.Production.json` on each droplet (gitignored) and in each FE's `deploy.sh` (public-by-design — embedded in JS bundle anyway).
- Sentry filter drops `DomainException`/`ForbiddenException` (business rules, not bugs).
- Serilog structured JSON to `/var/log/<target>/api-YYYYMMDD.log` (30-day rolling). Enriched with `TenantId`/`UserId`/`CorrelationId`/`RequestId`.
- Health endpoints: `GET /api/health/live` (self), `GET /api/health/ready` (self + Npgsql ping). Anonymous.

**FE stack:**
- Dashboard apps: React + TypeScript + Vite + **antd** (Ant Design) + TanStack Query + Zustand
- Tablet apps: React + TypeScript + Vite + **Tailwind** (no antd) + vite-plugin-pwa
- Shared packages per repo: `@<brand>/shared-types`, `@<brand>/api-client`, `@<brand>/auth`, `@<brand>/signalr-client`, `@<brand>/i18n` (`<brand>` = algreen, alblue)

**FE 403 fallback:** `setOnForbidden(code => message.error(...))` wired in `dashboard/src/main.tsx`. Specific error codes get specific Serbian messages, generic fallback for unknown codes. Tablet has no toaster — Department workers rarely hit 403.

---

## 5. Workflow rules (the "if you forget these you will break something" list)

1. **NO `cp` between FE repos.** Use `Edit` line-by-line (or `rsync` for full mirror waves). Branding (logos, theme tokens, titles, `@<brand>/*` imports, localStorage keys) diverges per FE repo. Whole-file copies have broken alblue branding twice. (`gotchas.md` line 51)
2. **Algreen pilot is Mile's production.** Never `./deploy.sh pilot` (BE) or deploy the algreen FE without explicit Milos go-ahead, and only after factory hours. Read-only DB queries are fine for forensics.
3. **Mirror FE fixes alblue → algreen only when told** ("deploy to pilot"), as a batch, after Sale/Bojan QA — not incrementally during active dev.
4. **Build before commit.** `dotnet build` BE, `pnpm build` FE. Catches the compile-time half of mistakes.
5. **Don't modify production data unprompted.** If the DB state is unexpected (a user has weird role, a config looks wrong), report it — don't fix it. The client may have intentionally set it that way. algreen pilot is Mile's data and is strictly off-limits.
6. **Commit messages.** Conventional commits style: `feat(scope): ...`, `fix(scope): ...`, `chore(scope): ...`. Multi-paragraph for non-trivial changes. The repo uses `Co-Authored-By: Claude` co-author footer.

---

## 6. What landed in Sprint 3 (2026-05-16 to 2026-05-17)

Read `audit/01_forensics.md` through `audit/06_pilot_unfreeze_runbook.md` for the full story. Quick map:

- **3.0 Multi-tenant audit + role guards.** 13 findings, 5 fixes shipped (F-1 last-Admin block, F-2 DeleteUser guards, F-3 refresh-token revocation on role change, F-7 only-SuperAdmin-changes-roles, F-11 ChangePassword self-only). 5 more deferred (F-8 to F-13) in `03_backlog.md`.
- **3.1 Sentry BE** — see "Observability" above.
- **3.2 Serilog** — structured JSON logs with enrichment.
- **3.3 Health endpoints** — `/api/health/live`, `/api/health/ready`.
- **3.4 Migrations** — extracted from startup to `--migrate` CLI flag.
- **3.5 Audit interceptor** — `AuditableEntityInterceptor` auto-populates audit columns.
- **3.6 Sentry FE** — `@sentry/react` in dashboard + tablet across the FE repos.

Plus performance fixes during this work: N+1 query fixes on `/api/tablet/active`, `/api/tablet/queue`, `/api/orders/master-view` + `.AsSplitQuery()` on the order paged + active-orders queries. Tablet login/logout sub-process auto-resume bug fixed via new `paused_by_station_at` column.

12 integration tests added in `tests/AlGreenMES.Tests.Integration/IdentityAuthzTests.cs` covering all Sprint 3.0 guards. They compile clean but don't run on Apple Silicon Mac due to a pre-existing Testcontainers + xUnit lifecycle issue affecting ALL existing tests (see "Gotchas" below).

---

## 7. Backlog and pending decisions

- **algreen pilot unfreeze.** Has zero SuperAdmin users — verified 2026-05-17. Must add one before Sprint 3.0 deploys to pilot (F-7 would otherwise lock the tenant out of role management). Suggested: a dedicated `skyhard@algreen.rs` account, NOT Mile's. Runbook at `audit/06_pilot_unfreeze_runbook.md`.
- **Sprint 4.** Not yet defined. Waiting on Nikola.
- **Integration test infrastructure fix.** macOS + arm64 + Docker Desktop + Testcontainers lifecycle bug — `_postgres.GetConnectionString()` returns default `localhost:5432` instead of the dynamic port. Affects ALL existing tests, not just new ones. Likely fix: untangle `IClassFixture` + `ICollectionFixture` double-registration in `AlgreenWebApplicationFactory`. Sprint 4 housekeeping candidate.
- **UptimeRobot monitors** against `/api/health/ready` for both droplets — not yet added.

---

## 8. Milos's working style preferences (saved in feedback memory)

- **Brief responses.** No essays, no padding. State the thing, move on.
- **No multiple-choice questions.** Decide the best-practice path yourself. Ask only when truly stuck.
- **Don't underestimate Claude's speed.** If you can do something in 5 minutes, don't say "30 minutes". Don't say "let me finish for now and continue tomorrow." Just do it.
- **Confirm before destructive ops.** Reset/force-push/drop/delete — ask first. Reversible local edits — just do.
- **Sentry filtering is correct, not broken.** `DomainException` / `ForbiddenException` don't email — they're 4xx business rules, not 500-class bugs. Don't be surprised by silence.

---

## 9. Gotchas (the "real bug taught us this" list)

Read `~/.claude/projects/-Users-milosmitrovic-Projects-skysoft-algreen-tracker-algreen-tracker-fe/memory/gotchas.md` for the full list. The high-value ones for cold-start:

- **NO `cp` between FE repos.** Branding diverges. Use `Edit`. (See rule 1 above.)
- **`User.Update()` does not call `SetUpdated()`.** Pre-Sprint-3.5 this meant role changes left no audit trail. Post-3.5 the interceptor compensates. Don't try to forensic an older audit gap via `updated_by_user_id` — for pre-3.5 events it's `NULL` regardless of who did the change.
- **Integration tests don't run locally on Apple Silicon Mac.** Testcontainers Postgres setup fails to start; `GetConnectionString()` returns default `localhost:5432`. All existing tests fail the same way. CI on Linux works. Do NOT add `dotnet test` as a gate in deploy.sh until fixed.
- **Sprint 2.4a `HasQueryFilter` was applied to `TenancyDbContext`** and silently filtered TenantSettings by SuperAdmin's home tenant. Fixed by dropping the filter from `TenancyDbContext` only (Tenancy is SuperAdmin-cross-tenant by design). Don't reintroduce.
- **Empty product name is valid.** `OrderItem.ProductName` is nullable. FormData with `undefined` will stringify to literal `"undefined"` — send empty string instead. (Bit us once.)
- **`MarkCompleted()` + `UndoComplete()` are a pair.** Don't break it.
- **CORS in Production:** `Cors.AllowedOrigins` MUST be in `/opt/{target}/api/appsettings.Production.json` after Sprint 1 task 6, or BE crashes on startup.
- **Tablet PWA service worker registration must be `.catch()`-wrapped** — failure on Safari private mode etc. is non-critical and shouldn't ping Sentry.

---

## 10. How to use auto-memory while running

Memory location for sessions launched from `algreen-tracker-fe`:
`~/.claude/projects/-Users-milosmitrovic-Projects-skysoft-algreen-tracker-algreen-tracker-fe/memory/`

Files there are scoped to that working directory's sessions. Use this `CLAUDE_ONBOARDING.md` as the canonical bootstrap and have each session read it explicitly at start.

Auto-memory pattern (this codebase): use it heavily, but never write code patterns / file paths / git history into memory (re-derivable). Use it for: who is the user, what feedback they've given, project state that isn't in code, references to external systems.

---

## 11. Active threads (as of 2026-05-18 — Sprint-3-era, likely stale)

- Nikola back from break, Sprint 4 not yet defined. Pending his return.
- Bojan signed off on alblue Sprint 3 (2026-05-16).
- algreen pilot was frozen at the time; it has since gone live (~24.06.2026).

---

If a new session reads only one file, this one. If it needs more depth, point it at:
- `audit/01_forensics.md` through `audit/06_pilot_unfreeze_runbook.md` (Sprint 3.0 detail)
- `~/.claude/projects/.../memory/MEMORY.md` and the linked files (Milos's preferences, feedback history)
- `gotchas.md` in the memory folder (the longer landmines list)
- `CLAUDE.md` in each repo root (per-repo coding conventions)
