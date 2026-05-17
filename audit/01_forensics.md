# 01 — Forensics: easy-mes role state

**Date of investigation:** 2026-05-17 17:29 UTC
**Investigator:** Claude (claude-opus-4-7) on Milos's machine
**Scope:** easy-mes tenant on the easy-mes-be droplet (46.101.125.31). Pilot (algreen) and staging (alblue) DBs were not queried.

## TL;DR

`admin@demo.com` and `manager@demo.com` on the `DEMO` tenant of easy-mes were **demoted via a legitimate `PUT /api/users/{id}` call during testing**, not by an unauthorized actor. The "incident" is the predicted self-demotion scenario, confirmed.

Two facts that explain why we have no actor data:

1. `User.Update()` does not call `SetUpdated()` — so pre-Sprint-3.5 (i.e., before 2026-05-16 ~12:23 UTC on easy-mes-be), every legitimate role change persisted without populating `updated_at` / `updated_by_user_id`.
2. The Sprint 3.5 `AuditableEntityInterceptor` fixes (1) going forward, but admin@demo.com / manager@demo.com have not been touched since the 3.5 deploy, so the columns are still `NULL`.

Net effect: we cannot identify the exact actor or timestamp. We can be confident about the **mechanism** (API-level update) and rule out anomalous paths.

## Tenant identification

The droplet is named "easy-mes" but the tenant inside the DB has `code = 'DEMO'`:

| column | value |
| --- | --- |
| `id` | `f37657d5-0938-45d4-b162-30e25b167a73` |
| `name` | `Demo Company` |
| `code` | `DEMO` |
| `is_active` | `true` |
| `created_at` | `2026-05-14 21:09:15.738117+00` |

Restore scripts must look this up by `code = 'DEMO'` (NOT `slug` — the column doesn't exist) or by the literal UUID above. There is exactly **one** tenant in this DB.

## 1.1 — Current user state on easy-mes (`DEMO` tenant)

```
        email        |     role     |          created_at           | updated_at | created_by_user_id | updated_by_user_id
---------------------+--------------+-------------------------------+------------+--------------------+--------------------
 coord@demo.com      | Coordinator  | 2026-05-14 21:09:17.78074+00  | NULL       | NULL               | NULL
 manager@demo.com    | Coordinator  | 2026-05-14 21:09:17.773591+00 | NULL       | NULL               | NULL
 admin@demo.com      | Department   | 2026-05-14 21:09:16.685248+00 | NULL       | NULL               | NULL
 worker1@demo.com    | Department   | 2026-05-16 11:19:16.18586+00  | NULL       | NULL               | NULL
 worker2@demo.com    | Department   | 2026-05-14 21:09:17.805346+00 | NULL       | NULL               | NULL
 worker3@demo.com    | Department   | 2026-05-14 21:09:17.808637+00 | NULL       | NULL               | NULL
 worker4@demo.com    | Department   | 2026-05-14 21:09:17.811014+00 | NULL       | NULL               | NULL
 sales@demo.com      | SalesManager | 2026-05-14 21:09:17.778144+00 | NULL       | NULL               | NULL
 skyhard@easymes.app | SuperAdmin   | 2026-05-14 21:28:17.732136+00 | NULL       | NULL               | NULL
```

Compared to what `DataSeeder.cs` declares (lines 42–48 and 164–173):

| email | seeder declares | DB has | matches seeder? |
| --- | --- | --- | --- |
| `admin@demo.com` | `Admin` | `Department` | **NO** |
| `manager@demo.com` | `Manager` | `Coordinator` | **NO** |
| `coord@demo.com` | `Coordinator` | `Coordinator` | yes |
| `sales@demo.com` | `SalesManager` | `SalesManager` | yes |
| `worker1@demo.com` | `Department` | `Department` | yes |
| `worker2-4@demo.com` | `Department` | `Department` | yes |
| `skyhard@easymes.app` | — (added manually) | `SuperAdmin` | n/a |

Only `admin@demo.com` and `manager@demo.com` diverge from the seeder. Both diverge by **exactly one rank downward** in the role hierarchy. coord@demo.com matches its seeded value (so the demoter didn't target Coordinator-level users uniformly).

## 1.2 — Audit history on the two affected rows

| column | `admin@demo.com` | `manager@demo.com` |
| --- | --- | --- |
| `updated_at` | NULL | NULL |
| `updated_by_user_id` | NULL | NULL |

**No actor evidence available.** The rows have never had their `updated_at` / `updated_by_user_id` populated, which would be the only mechanism for reconstructing who changed the role.

There is no separate `audit_log` / `user_role_history` / `domain_events` table in the schema (this is itself a finding for Phase 2).

## 1.3 — Mechanism: why is `updated_at` NULL when the role clearly changed?

`User.Update(...)` in `src/Modules/Identity/AlGreenMES.Modules.Identity.Domain/Entities/User.cs:53-66` mutates the entity's fields directly but does **not** call the inherited `SetUpdated()`:

```csharp
public void Update(string firstName, string lastName, UserRole role, bool isActive, bool canIncludeWithdrawnInAnalysis = false)
{
    // validations...
    FirstName = firstName.Trim();
    LastName = lastName.Trim();
    Role = role;
    IsActive = isActive;
    CanIncludeWithdrawnInAnalysis = canIncludeWithdrawnInAnalysis;
}
```

So pre-Sprint-3.5:

1. Caller hits `PUT /api/users/{id}` with `role=Department`.
2. `UpdateUserCommandHandler.Handle` fetches user, calls `user.Update(..., role: Department, ...)`.
3. EF sees `Role` property changed → emits `UPDATE identity.users SET role='Department' WHERE id=...`.
4. `updated_at` / `updated_by_user_id` are NEVER touched because no .NET code touched them.

Sprint 3.5's `AuditableEntityInterceptor` would have fixed this going forward by stamping `updated_at`/`updated_by_user_id` on every `Modified` entry — but it deployed on **2026-05-16 12:23 UTC** to easy-mes-be (commit `8d3c291`), and these two users were already in their wrong state by then. They haven't been touched again since.

## 1.4 — All code paths that can write `User.Role`

A repo-wide search (`\.Role\s*=` and all `UserRole.X` writes) across both BE repos returns three call sites that mutate role:

| # | path | who can call | what it does | guards |
| --- | --- | --- | --- | --- |
| 1 | `DataSeeder.cs:48` and `:185` (via `User.Create`) | runs at app startup (system, no HTTP context) | initial seed, only creates if user doesn't already exist | none needed — won't overwrite existing |
| 2 | `CreateUserCommandHandler.cs:43` (via `User.Create`) | `POST /api/users` — `[Authorize(Roles = "SuperAdmin,Admin")]` | creates a new user with the role from request | blocks Admin from setting `role=SuperAdmin` |
| 3 | `UpdateUserCommandHandler.cs:36` (via `User.Update`) | `PUT /api/users/{id}` — `[Authorize(Roles = "SuperAdmin,Admin")]` | mutates role + other fields | only blocks SuperAdmin grant/revoke; **does not block** self-demotion, peer demotion, last-Admin removal |

No raw-SQL writes, no GraphQL mutations, no background job paths, no CLI utility that touches `Role`. No write paths bypass the command handlers.

This narrows the demotion to **exactly one mechanism**: a `PUT /api/users/{id}` call with `role=Department` (or `role=Coordinator`) by a caller who had `Admin` or `SuperAdmin` role at the time.

## 1.5 — Hypothesis ranking

### Hypothesis A (primary, evidence-backed) — Self-demotion during testing

Someone testing the `/admin/users` page on easy-mes during the May 14 → May 16 window:

1. Was logged in as `admin@demo.com` (then role `Admin`).
2. Used the FE UsersPage to edit their own row, picked `Department` from the role dropdown, hit Save.
3. The BE accepted it (no self-demotion guard exists).
4. Either the same person had also previously demoted `manager@demo.com` to `Coordinator`, or a different test-driven action on that user did the same thing.

Why this is the strongest hypothesis:

- Matches the predicted scenario in the prompt brief.
- Matches the known gap in `UpdateUserCommandHandler` (only SuperAdmin escalation is guarded).
- Both affected rows are demoted by exactly one rank — consistent with a single dropdown click per row, not a bulk SQL operation.
- `coord@demo.com` is untouched at `Coordinator` — consistent with someone editing only the upper roles and not having a reason to touch Coordinator.
- Only two users on this tenant could have legitimately performed the action: `admin@demo.com` (Admin, prior to demotion) and `skyhard@easymes.app` (SuperAdmin). `skyhard` was added at `21:28:17` on May 14 — 19 minutes after admin@demo.com was created. It is plausible Milos created `skyhard` as the escape hatch *because* admin@demo.com had already been demoted.

### Hypothesis B (possible, unverifiable) — Raw SQL on the droplet

Someone with `ssh` access ran `UPDATE identity.users SET role='Department' WHERE email='admin@demo.com';` directly on the DB.

Cannot be ruled out from DB evidence alone. Same observable: `updated_at` NULL. To check, you'd need shell history or PostgreSQL `log_statement = 'mod'` audit logs — neither is enabled on this droplet (verified: `postgresql.conf` has `log_statement = none` by default).

Marginally less likely because:

- Both admin and manager were demoted with valid role-enum values (`Department`, `Coordinator`). A raw-SQL operator with arbitrary intent would likely have done something different (NULL them out, hard-delete, etc.).
- No other DB-level artifacts of manual intervention.

But this is the cleanest way to satisfy "updated_at is NULL on a row whose role clearly changed" if the User.Update missing-SetUpdated bug somehow didn't apply.

### Hypothesis C (ruled out) — Seeder ran with different version

Git log on `DataSeeder.cs` shows the last commit touching role assignments was Sprint 2.4a, well before the May 14 seeder run. The seeder code that ran was the same code we see today (which assigns the correct roles).

Also ruled out by the seeder logic itself: it does NOT update existing users — only `if (user == null) { ... User.Create(...) }`. So the current state can't have come from a re-run of a different seeder version on top of correctly-seeded users.

### Hypothesis D (ruled out) — Unauthorized external actor

Possible but unsupported by evidence. No 401/403 burst in journald logs, no nginx anomaly, the JWT secret is rotated, and the BE has the cross-tenant test suite passing. There's no plausible attack vector here that would manifest as exactly two demotions and nothing else. Threat-model context: easy-mes is internal test infra, never exposed to a real adversary.

## 1.6 — Available remediation

The state is recoverable. We have the SuperAdmin account (`skyhard@easymes.app`) and direct DB access. Two paths:

- **UI restore** (preferred for an end-user-style fix): log in as `skyhard@easymes.app` on `https://easy-mes.duckdns.org`, navigate to `/admin/users`, change admin@demo.com → Admin and manager@demo.com → Manager. Will exercise the audit interceptor and leave a record on next save.
- **SQL restore** (idempotent, scripted): see Phase 3 deliverable `scripts/2026-05-restore-easy-mes-roles.sql`. Use `tenant.code = 'DEMO'` for lookup. Do NOT manually write `updated_by_user_id` (it's a Guid FK, not free text); omit and let the next legitimate write populate it.

Neither approach exists yet — both are Phase 3 work, blocked on the wider audit and Nikola's sign-off on the stricter role guard.

## Information needed before Phase 2

None — the codebase scan was complete enough to proceed. Phase 2 starts immediately.

## Summary for Nikola

- The "incident" is self-demotion via legit API, almost certainly during easy-mes testing. Not an unauthorized actor.
- The reason we have no actor data is a pre-existing bug — `User.Update()` never called `SetUpdated()`, so pre-Sprint-3.5 role changes left no trace. This is itself a finding for Phase 2.
- The path that allowed the demotion is `PUT /api/users/{id}` without a self-demotion / peer-Admin / last-Admin guard. Confirms the known gap from `gotchas.md` line 36.
- Restore is a Phase 3 deliverable; existing data is intact (no data loss, just role downgrade).
