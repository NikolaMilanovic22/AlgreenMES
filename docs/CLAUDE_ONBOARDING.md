# CLAUDE_ONBOARDING.md — Cold-start context for a fresh Claude session

If you are a new Claude session opened on any of these repos: **read this first.** It is the canonical handoff doc covering everything across the 5 repos that share this codebase tree. Existing memory files in `~/.claude/projects/.../memory/` may also be relevant but they assume context you don't have yet — start here.

---

## 1. The product map (who uses what)

Three products, two backends, one shared developer (Milos) + one BE collaborator (Nikola):

| Product | Audience | Repo (BE) | Repo (FE) | Status |
|---|---|---|---|---|
| **algreen** pilot | Mile's company (real production, paying user) | `algreen-tracker-be` (master branch) | `algreen-tracker-fe` (main) | **FROZEN.** Do not deploy without explicit thaw. See `audit/06_pilot_unfreeze_runbook.md`. |
| **alblue** staging | Bojan + Sale test before Mile gets it | `algreen-tracker-be` (staging branch) | `alblue-tracker-fe` (main) | Active, all Sprint 3 work landed |
| **easy-mes** side business | Milos's red-black client, isolated from skysoft (Mile/Nikola/Sale/Bojan workstream) | `easy-mes-be` (main) | `easy-mes-fe` (main) | Active, mirror of alblue + custom branding |

Key rule: **algreen pilot is Mile's production.** Never deploy to it casually. Bojan/Sale test alblue first; when satisfied, the same code goes to algreen pilot — but only when Milos explicitly thaws.

---

## 2. Repo layout (absolute paths on Milos's Mac)

```
/Users/milosmitrovic/Projects/skysoft/algreen-tracker/algreen-tracker-be    ← BE source (alblue + algreen)
/Users/milosmitrovic/Projects/skysoft/algreen-tracker/algreen-tracker-fe    ← FE pilot (algreen)
/Users/milosmitrovic/Projects/skysoft/alblue-tracker/alblue-tracker-fe      ← FE staging (alblue)
/Users/milosmitrovic/Projects/skyhard/easy-mes-be                            ← BE side-business mirror
/Users/milosmitrovic/Projects/skyhard/easy-mes-fe                            ← FE side-business
```

**BE single source of truth:** `algreen-tracker-be`. `easy-mes-be` is a mirror — every BE change lands in both. Files there are safe to `cp` between (BE has no per-product branding divergence).

**FE three separate repos** with diverged branding (logos, theme colors, titles, localStorage keys, workspace import names: `@algreen/*`, `@alblue/*`, `@easymes/*`). **Never `cp` between FE repos.** Use line-by-line `Edit` only. The mirroring rule has burned past Claude sessions twice already (see `gotchas.md`).

---

## 3. Infrastructure

### Droplets

| Droplet IP | Role | Hosts |
|---|---|---|
| `46.101.166.137` (Frankfurt, Ubuntu 24.04, 2GB) | skysoft | Both alblue + algreen pilot (separate `/opt/*` paths, separate systemd services, separate Postgres DBs in same container) |
| `46.101.125.31` (Frankfurt) | skyhard / easy-mes | Just easy-mes |

### Postgres containers

- skysoft droplet: `algreen-postgres-1` (one container, two DBs: `algreen_tracker`, `alblue_tracker`)
- easy-mes droplet: `easy-mes-postgres` (DB: `easy_mes`)

### Domain map

| Service | URL |
|---|---|
| algreen dashboard | `https://tracker-app.algreen.rs` |
| algreen tablet | `https://tracker-tablet.algreen.rs` |
| algreen API | `https://tracker-api.algreen.rs` |
| alblue dashboard | `https://alblue.duckdns.org` |
| alblue tablet | `https://alblue-tablet.duckdns.org` |
| alblue API | `https://alblue.duckdns.org/api/*` (same domain, nginx routes `/api/*`) |
| easy-mes dashboard | `https://easy-mes.duckdns.org` |
| easy-mes tablet | `https://easy-mes-tablet.duckdns.org` |
| easy-mes API | `https://easy-mes.duckdns.org/api/*` |

### SSH

`ssh root@46.101.166.137` — skysoft droplet. Key already in Milos's `~/.ssh/`.
`ssh root@46.101.125.31` — easy-mes droplet. Same.

### Deploy commands

**BE (`algreen-tracker-be`):**
```bash
./deploy.sh staging        # → alblue (branch=staging, /opt/alblue/api/, alblue-api service)
./deploy.sh pilot          # → algreen (branch=master,  /opt/algreen/api/, algreen-api service)
```

**BE (`easy-mes-be`):**
```bash
./deploy.sh easymes        # → easy-mes (branch=main, /opt/easy-mes/api/, easy-mes-api service)
```

Each `deploy.sh staging/pilot/easymes`:
1. Refuses to run on a dirty working tree
2. Checks out the target branch, pulls
3. `dotnet publish`
4. `rsync` (excludes `appsettings.Production.json` and `uploads/`)
5. Runs `dotnet AlgreenMES.API.dll --migrate` against the deployed binary (migrations as an explicit step, not on startup — Sprint 3.4)
6. Restarts the systemd service

**FE deploys** are simpler: `./deploy.sh dashboard|tablet|all` in each FE repo. `algreen-tracker-fe` is pilot-only (no flag — only deploys to `/opt/algreen/`). `alblue-tracker-fe` deploys to `/opt/alblue/`. `easy-mes-fe` deploys to its own droplet.

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
- Sentry SDK on BE + FE. Single project `mes-api` (team `#sky-hard`). Distinguished by `environment` tag: `alblue-staging`, `easy-mes-prod`, `algreen-pilot`. DSN: `https://315954545e637502fd5497b3090b5c9c@o4511398917177344.ingest.de.sentry.io/4511398994313296`. Lives in `appsettings.Production.json` on each droplet (gitignored) and in each FE's `deploy.sh` (public-by-design — embedded in JS bundle anyway).
- Sentry filter drops `DomainException`/`ForbiddenException` (business rules, not bugs).
- Serilog structured JSON to `/var/log/<target>/api-YYYYMMDD.log` (30-day rolling). Enriched with `TenantId`/`UserId`/`CorrelationId`/`RequestId`.
- Health endpoints: `GET /api/health/live` (self), `GET /api/health/ready` (self + Npgsql ping). Anonymous.

**FE stack:**
- Dashboard apps: React + TypeScript + Vite + **antd** (Ant Design) + TanStack Query + Zustand
- Tablet apps: React + TypeScript + Vite + **Tailwind** (no antd) + vite-plugin-pwa
- Shared packages per repo: `@<brand>/shared-types`, `@<brand>/api-client`, `@<brand>/auth`, `@<brand>/signalr-client`, `@<brand>/i18n` (`<brand>` = algreen, alblue, easymes)

**FE 403 fallback:** `setOnForbidden(code => message.error(...))` wired in `dashboard/src/main.tsx`. Specific error codes get specific Serbian messages, generic fallback for unknown codes. Tablet has no toaster — Department workers rarely hit 403.

---

## 5. Workflow rules (the "if you forget these you will break something" list)

1. **NO `cp` between FE repos.** Use `Edit` line-by-line. Branding (logos, theme tokens, titles, `@<brand>/*` imports, localStorage keys) diverges per FE repo. Whole-file copies have broken alblue branding twice. (`gotchas.md` line 51)
2. **`cp` between BE repos is OK.** The BE code is identical between `algreen-tracker-be` and `easy-mes-be`. Mirror via `cp`.
3. **Algreen pilot is FROZEN.** Never `./deploy.sh pilot` without explicit Milos go-ahead. Read-only DB queries are fine for forensics.
4. **Mirror every BE security/code fix to BOTH BE repos.** Skipping one creates security drift.
5. **Mirror dashboard FE fixes to all 3 FE repos** when they apply (most do). Commit to algreen-tracker-fe but don't deploy (pilot freeze) — code stays in lockstep, deploy waits.
6. **Build before commit.** `dotnet build` BE, `pnpm build` FE. Catches the compile-time half of mistakes.
7. **Don't modify production data unprompted.** If the DB state is unexpected (a user has weird role, a config looks wrong), report it — don't fix it. The client may have intentionally set it that way. easy-mes is more lax (Milos's own infra) but algreen pilot is Mile's data and is strictly off-limits.
8. **Commit messages.** Conventional commits style: `feat(scope): ...`, `fix(scope): ...`, `chore(scope): ...`. Multi-paragraph for non-trivial changes. The repo uses `Co-Authored-By: Claude` co-author footer.
9. **Mirroring across BE repos is one commit per repo, not one mega-commit.** Each repo gets its own commit message referencing the other.

---

## 6. What landed in Sprint 3 (2026-05-16 to 2026-05-17)

Read `audit/01_forensics.md` through `audit/06_pilot_unfreeze_runbook.md` for the full story. Quick map:

- **3.0 Multi-tenant audit + role guards.** 13 findings, 5 fixes shipped (F-1 last-Admin block, F-2 DeleteUser guards, F-3 refresh-token revocation on role change, F-7 only-SuperAdmin-changes-roles, F-11 ChangePassword self-only). 5 more deferred (F-8 to F-13) in `03_backlog.md`. Restore SQL for easy-mes (`tenant code='DEMO'`) in `scripts/2026-05-restore-easy-mes-roles.sql`.
- **3.1 Sentry BE** — see "Observability" above.
- **3.2 Serilog** — structured JSON logs with enrichment.
- **3.3 Health endpoints** — `/api/health/live`, `/api/health/ready`.
- **3.4 Migrations** — extracted from startup to `--migrate` CLI flag.
- **3.5 Audit interceptor** — `AuditableEntityInterceptor` auto-populates audit columns.
- **3.6 Sentry FE** — `@sentry/react` in dashboard + tablet across 3 FE repos.

Plus performance fixes during this work: N+1 query fixes on `/api/tablet/active`, `/api/tablet/queue`, `/api/orders/master-view` + `.AsSplitQuery()` on the order paged + active-orders queries. Tablet login/logout sub-process auto-resume bug fixed via new `paused_by_station_at` column.

12 integration tests added in `tests/AlGreenMES.Tests.Integration/IdentityAuthzTests.cs` covering all Sprint 3.0 guards. They compile clean but don't run on Apple Silicon Mac due to a pre-existing Testcontainers + xUnit lifecycle issue affecting ALL existing tests (see "Gotchas" below).

---

## 7. Backlog and pending decisions

- **algreen pilot unfreeze.** Has zero SuperAdmin users — verified 2026-05-17. Must add one before Sprint 3.0 deploys to pilot (F-7 would otherwise lock the tenant out of role management). Suggested: `skyhard@algreen.rs` mirroring easy-mes's `skyhard@easymes.app` pattern, NOT Mile's account. Runbook at `audit/06_pilot_unfreeze_runbook.md`.
- **Sprint 4.** Not yet defined. Waiting on Nikola.
- **easy-mes rebranding** (red-black client). Discussion ongoing as of 2026-05-18. Likely path: heavy antd theming + custom CSS layer, NOT a UI library swap. Caveat: brand red ≠ status red (status red is reserved for critical processes; pick a darker burgundy/wine for brand to avoid alarm-signal confusion).
- **Integration test infrastructure fix.** macOS + arm64 + Docker Desktop + Testcontainers lifecycle bug — `_postgres.GetConnectionString()` returns default `localhost:5432` instead of the dynamic port. Affects ALL existing tests, not just new ones. Likely fix: untangle `IClassFixture` + `ICollectionFixture` double-registration in `AlgreenWebApplicationFactory`. Sprint 4 housekeeping candidate.
- **UptimeRobot monitors** against `/api/health/ready` for both droplets — not yet added.

---

## 8. Milos's working style preferences (saved in feedback memory)

- **Brief responses.** No essays, no padding. State the thing, move on.
- **No multiple-choice questions.** Decide the best-practice path yourself. Ask only when truly stuck.
- **Don't underestimate Claude's speed.** If you can do something in 5 minutes, don't say "30 minutes". Don't say "let me finish for now and continue tomorrow." Just do it.
- **Mirror everything.** Anything that lands on alblue should also land on easy-mes (with caveats for FE divergence). Same for BE security fixes.
- **Confirm before destructive ops.** Reset/force-push/drop/delete — ask first. Reversible local edits — just do.
- **Sentry filtering is correct, not broken.** `DomainException` / `ForbiddenException` don't email — they're 4xx business rules, not 500-class bugs. Don't be surprised by silence.

---

## 9. Gotchas (the "real bug taught us this" list)

Read `~/.claude/projects/-Users-milosmitrovic-Projects-skysoft-algreen-tracker-algreen-tracker-fe/memory/gotchas.md` for the full list. The high-value ones for cold-start:

- **NO `cp` between FE repos.** Branding diverges. Use `Edit`. (See rule 1 above.)
- **`User.Update()` does not call `SetUpdated()`.** Pre-Sprint-3.5 this meant role changes left no audit trail. Post-3.5 the interceptor compensates. Don't try to forensic an older audit gap via `updated_by_user_id` — for pre-3.5 events it's `NULL` regardless of who did the change.
- **Integration tests don't run locally on Apple Silicon Mac.** Testcontainers Postgres setup fails to start; `GetConnectionString()` returns default `localhost:5432`. All existing tests fail the same way. CI on Linux works. Do NOT add `dotnet test` as a gate in deploy.sh until fixed.
- **easy-mes tenant code is `'DEMO'`, NOT `'easy-mes'` or any other slug.** The droplet/environment name and the in-DB tenant identifier are different. Restore scripts must look up by `code = 'DEMO'`.
- **Sprint 2.4a `HasQueryFilter` was applied to `TenancyDbContext`** and silently filtered TenantSettings by SuperAdmin's home tenant. Fixed by dropping the filter from `TenancyDbContext` only (Tenancy is SuperAdmin-cross-tenant by design). Don't reintroduce.
- **Empty product name is valid.** `OrderItem.ProductName` is nullable. FormData with `undefined` will stringify to literal `"undefined"` — send empty string instead. (Bit us once.)
- **`MarkCompleted()` + `UndoComplete()` are a pair.** Don't break it.
- **CORS in Production:** `Cors.AllowedOrigins` MUST be in `/opt/{target}/api/appsettings.Production.json` after Sprint 1 task 6, or BE crashes on startup.
- **Tablet PWA service worker registration must be `.catch()`-wrapped** — failure on Safari private mode etc. is non-critical and shouldn't ping Sentry.

---

## 10. How to use auto-memory while running

Memory location for sessions launched from `algreen-tracker-fe`:
`~/.claude/projects/-Users-milosmitrovic-Projects-skysoft-algreen-tracker-algreen-tracker-fe/memory/`

Files there are scoped to that working directory's sessions. Sessions launched from `easy-mes-fe` or `easy-mes-be` get their OWN memory folder, separate from this one. **A new Claude session in easy-mes-fe will not see this memory by default.**

If you want to share memory across project boundaries (e.g. easy-mes Claude should know skysoft context), either:
- Launch Claude from a parent directory that covers both, OR
- Use this `CLAUDE_ONBOARDING.md` as the canonical bootstrap and have each session read it explicitly at start

Auto-memory pattern (this codebase): use it heavily, but never write code patterns / file paths / git history into memory (re-derivable). Use it for: who is the user, what feedback they've given, project state that isn't in code, references to external systems.

---

## 11. If you are an easy-mes-specific Claude session

You probably don't need:
- Sections about algreen pilot (frozen, not your concern unless mirroring code FROM alblue)
- skysoft-side deployment commands

You DO need to understand:
- That `alblue-tracker-fe` is upstream — code mirrors come FROM alblue TO easy-mes, never the other way
- That BE work is mirrored across both BE repos (easy-mes-be IS in scope for you on BE changes that originate from main session)
- The mirroring tax: easy-mes is meant to diverge visually but stay structurally similar to alblue so mirroring stays cheap. If you change easy-mes structure (component tree, navigation, routes), mirror cost from alblue jumps significantly.
- Red-black brand has a status-color conflict — use wine/burgundy for brand red, keep the bright status red separate.

---

## 12. If you are a main-skysoft Claude session

Your scope is alblue + algreen. You touch easy-mes-be only when BE security/code fixes need mirroring to it. You touch easy-mes-fe rarely or never — that's the dedicated easy-mes Claude's job.

When you need to mirror a security fix to easy-mes-be:
1. `cp` the relevant files from `algreen-tracker-be/src/Modules/...` to `easy-mes-be/src/Modules/...`
2. `cd /Users/milosmitrovic/Projects/skyhard/easy-mes-be && dotnet build`
3. Commit with a message referencing the alblue commit hash
4. `./deploy.sh easymes`

---

## 13. Active threads (as of 2026-05-18)

- Nikola back from break, Sprint 4 not yet defined. Pending his return.
- Bojan signed off on alblue Sprint 3 (2026-05-16).
- easy-mes red-black rebrand pending logo from client.
- Decision pending tonight (2026-05-18) on rebranding strategy: heavy theming vs structural divergence vs UI library swap.
- Possible split into a dedicated easy-mes Claude session after rebrand begins.
- algreen pilot remains frozen until SuperAdmin is provisioned and Bojan/Sale sign-off on alblue is solid.

---

If a new session reads only one file, this one. If it needs more depth, point it at:
- `audit/01_forensics.md` through `audit/06_pilot_unfreeze_runbook.md` (Sprint 3.0 detail)
- `~/.claude/projects/.../memory/MEMORY.md` and the linked files (Milos's preferences, feedback history)
- `gotchas.md` in the memory folder (the longer landmines list)
- `CLAUDE.md` in each repo root (per-repo coding conventions)
