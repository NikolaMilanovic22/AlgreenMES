# 06 — Algreen Pilot Unfreeze Runbook

This is a checklist for the moment you and Nikola decide to bring algreen pilot up to date with the alblue/easy-mes Sprint 3 work. It is **not** an instruction to do it now. The pilot is frozen until you explicitly choose to thaw it.

## Why this exists

After 2026-05-16/17 the alblue staging and easy-mes side-business droplets carry the full Sprint 3 line (Sentry, Serilog, health endpoints, --migrate CLI, audit interceptor, FE Sentry, and Sprint 3.0 multi-tenant authz fixes). Algreen pilot is on the old code — it has none of these. At some point Mile should get them too. This document lists exactly what has to be true before that deploy can happen safely, and the order of operations once it does.

---

## Hard preconditions (must all be true before the first deploy)

### P-1. Algreen pilot has at least one SuperAdmin user

**Current state (read on 2026-05-17):** ZERO SuperAdmins on algreen pilot. 21 users: 2 Admins (`admin@algreen.rs`, `admin@demo.com`), 1 Manager, 17 Department, 1 SalesManager.

**Why it matters:** Sprint 3.0 F-7 enforces "only SuperAdmin can change a user's role". Without a SuperAdmin on the tenant, no role changes are possible — Mile cannot promote, demote, or recover from any role mistake. The escape hatch must exist before the door closes.

**What to do:** Pick one of these options and execute BEFORE running step S-1 below.

- **Option A (recommended) — Dedicated ops SuperAdmin.** Mirrors the easy-mes pattern (`skyhard@easymes.app`). Create a new user `skyhard@algreen.rs` (or whatever you prefer) with role `SuperAdmin`. Mile doesn't get the credentials — you and Nikola hold them. The account is only used for tenant administration and emergencies.

- **Option B — Promote `admin@algreen.rs` to SuperAdmin.** Faster (no new account), but gives Mile platform-level powers including the ability to manage other tenants if any get created later. Probably overkill for his actual day-to-day role.

- **Option C — Do nothing, don't deploy Sprint 3.0.** Deploy only 3.1–3.6 (Sentry/Serilog/health/migrations/audit interceptor/Sentry FE). The pilot keeps the partial role-management protection (only the existing SuperAdmin-grant guard) and continues without Sprint 3.0's tightening. Lower security, zero migration risk on roles.

**SQL template for Option A** (do not run; the password hash + email must be filled in first):

```sql
-- Algreen pilot SuperAdmin onboarding — DO NOT RUN until you have:
--   1. Final email (e.g. skyhard@algreen.rs)
--   2. Password hashed with PasswordHasher (BCrypt). Generate by running
--      the API locally with PasswordHasher.HashPassword('<your-password>')
--      and pasting the resulting hash here. Never store plain passwords.
BEGIN;
DO $$
DECLARE
    v_tenant_id UUID;
BEGIN
    SELECT id INTO v_tenant_id FROM tenancy.tenants WHERE code = 'ALGREEN'; -- verify actual code
    IF v_tenant_id IS NULL THEN
        RAISE EXCEPTION 'Algreen tenant not found.';
    END IF;
    INSERT INTO identity.users (
        id, tenant_id, email, password_hash, first_name, last_name,
        role, is_active, created_at, can_include_withdrawn_in_analysis
    ) VALUES (
        gen_random_uuid(), v_tenant_id, 'skyhard@algreen.rs',
        '<BCRYPT_HASH_HERE>', 'Sky', 'Hard',
        'SuperAdmin', true, NOW(), false
    );
END $$;
COMMIT;
```

Verify the actual tenant code with `SELECT id, code FROM tenancy.tenants;` first — could be `ALGREEN`, `ALG`, or something else.

### P-2. Bojan/Sale have signed off on alblue for at least 48h with no new defects

Sprint 3.0 only landed on 2026-05-17. Give it real exposure before letting it touch Mile's production. The first deploy to pilot should be the **fourth** environment to see this code, after dev/local, alblue, and easy-mes.

### P-3. Algreen pilot DB has a fresh backup

`/opt/algreen/scripts/backup-db.sh` runs daily at 02:00 UTC per `infrastructure.md`. Either let it run naturally and deploy after, or trigger it manually before deploy:

```bash
ssh root@46.101.166.137 "/opt/algreen/scripts/backup-db.sh"
ls -la /var/backups/algreen/ | tail -3   # confirm a fresh dump landed
```

### P-4. Algreen-tracker-fe is in lockstep with alblue-tracker-fe

The FE code is committed but not deployed (commits `7757dfc`, `049bb1f`, `c850b96` are the Sprint 3 trail). Verify branding files (algreen-logo, theme.ts green color, `Algreen MES` title, `@algreen/*` workspace imports, `algreen-auth` localStorage key) are intact in `algreen-tracker-fe` — should be untouched since Sprint 3.6's branding incident.

---

## Deploy order (after all preconditions met)

The BE deploy.sh on this repo handles `staging|pilot`. Pilot uses branch `master`. The FE has its own pilot-only `deploy.sh`. Each step has a smoke check; if any check fails, stop and roll back (see "Rollback" below).

### S-1. Merge `staging` → `master` on `NikolaMilanovic22/AlgreenMES`

```bash
git checkout master && git pull
git merge --no-ff staging   # carries all Sprint 3 commits forward
git push origin master
```

### S-2. Deploy BE to pilot

```bash
cd algreen-tracker-be
./deploy.sh pilot
```

deploy.sh will run `--migrate` automatically before the systemctl restart, so the AuditableEntityInterceptor migration + any other pending migrations land in one shot.

**Smoke checks (BE):**

```bash
curl -sS https://tracker-api.algreen.rs/api/health/live    # expect {"status":"Healthy",...}
curl -sS https://tracker-api.algreen.rs/api/health/ready   # expect {"status":"Healthy",...} (incl. postgres)
ssh root@46.101.166.137 "ls -la /var/log/algreen/api-*.log | tail -1"   # expect today's log file
```

Login as `admin@algreen.rs` via the API (or via the dashboard once S-3 lands):

```bash
curl -sS -X POST https://tracker-api.algreen.rs/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"admin@algreen.rs","password":"<Mile-password>","tenantCode":"<TENANT_CODE>"}'
# Expect 200 with token + refreshToken
```

If 5xx or refused: STOP. Roll back via the previous git tag/commit + `./deploy.sh pilot` again from the prior commit, then investigate.

### S-3. Deploy FE to pilot

```bash
cd algreen-tracker-fe
./deploy.sh   # pilot-only, no flag (the script enforces /opt/algreen/{dashboard,tablet}/)
```

**Smoke checks (FE):**

- Open `https://tracker-app.algreen.rs/` in browser → expect Algreen-branded dashboard load, no console errors.
- Open `https://tracker-tablet.algreen.rs/` → tablet load, no SW registration error (caught silently per the Sprint 3.6 fix).
- Log in as Mile → main coordinator dashboard loads, master-view shows orders.

### S-4. Behavioral spot-checks (Sprint 3.0 specific)

The five guards must be exercised once each on the pilot to confirm no env-specific drift:

1. Log in as Mile (`admin@algreen.rs`, role Admin). Open `/admin/users` and look at the role dropdown — it should be **disabled** (per the FE change). Verifies F-7 FE side.
2. Use Sentry → confirm one `mes-api` event has arrived from `environment=algreen-pilot` (you may have to trigger an action that errors, or just check that the deploy itself emitted a Serilog `Starting AlGreenMES.API` line).
3. From the SuperAdmin account created in P-1, open `/admin/users` — role dropdown **enabled**. Try demoting Mile to Manager → should succeed (or fail with `LAST_ADMIN_REMOVAL` if he's the only Admin — which he won't be once P-1 added the SuperAdmin). Then re-promote him back.
4. From Mile's session (Admin), try `DELETE /api/users/{any-other-admin-id}` — should fail with `LAST_ADMIN_REMOVAL` or `FORBIDDEN_ROLE_CHANGE` depending on the scenario.

If any of these don't match expectations, that's a P0 — stop and call Nikola.

### S-5. Update memory

Once you're satisfied:

- Edit `pilot-deploy-gate.md` to remove the freeze marker (or replace with "freeze lifted YYYY-MM-DD").
- Edit `sprint-3-ops.md` to note pilot deploy completed.
- Commit memory changes if your memory backend is git-tracked.

---

## Rollback

deploy.sh for BE supports rollback by checking out a previous commit and re-deploying. Concretely:

```bash
cd algreen-tracker-be
git checkout master
git log --oneline -5     # find the commit immediately BEFORE Sprint 3 changes
git checkout <prev-sha>
./deploy.sh pilot        # redeploys old binary; --migrate will see "no pending migrations"
                         # (migrations are forward-only; downgrade not attempted)
```

**Caveat — migrations are not auto-reversed.** If you need to revert the schema (e.g. drop the new `paused_by_station_at` columns from Sprint 3.0's earlier tablet fix), you'll have to write a manual DOWN SQL. In practice rolling back the binary is enough for most issues because the new columns are nullable additions — old code that doesn't read them is fine.

**FE rollback** is just a fresh `./deploy.sh` from a previous git commit. No DB involvement.

If rollback is needed and Mile is mid-shift, prioritize getting him back to a working state first; investigate the failure afterward.

---

## What NOT to deploy with this

- **Branding migration** — if Bojan eventually decides to rebrand algreen to a neutral "MES", that's a separate deploy. Don't try to combine.
- **Easy-mes-be / easy-mes-fe** — different droplet, different repo, already on Sprint 3. Nothing to do here.
- **Sprint 4 work** — assumes Nikola hasn't sent it yet; if he has, do Sprint 3 deploy + smoke first, then Sprint 4 as a separate cycle.

---

## Open questions to settle with Nikola before flipping the switch

1. **Which option from P-1 (A / B / C)?** A is recommended but it's a judgment call.
2. **What is `admin@demo.com` doing on algreen pilot?** It's a test/demo account on a production tenant — is it intentional or leftover? If leftover, deactivate before deploy (one less moving piece).
3. **Does Mile need any communication before the deploy window?** Sprint 3 is transparent to him (no UX changes for an Admin user except the disabled role dropdown in `/admin/users` — which he'd only notice if he was already using it).
4. **Is there a scheduled maintenance window on algreen?** Pilot deploys aren't free even when changes are safe — a 5-minute service restart matters if Mile is mid-shift.

When you and Nikola align on the above, the runbook is ready to execute.
