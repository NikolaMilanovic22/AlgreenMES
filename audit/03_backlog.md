# 03 — Backlog (LOW / INFO findings deferred from Sprint 3.0)

Each item below is from `audit/02_findings.md`. None block Phase 3; they are queued for Nikola to triage in a future sprint.

## F-8 — `User.Update()` does not call `SetUpdated()` (LOW)

The `AuditableEntityInterceptor` from Sprint 3.5 already auto-stamps `updated_at` / `updated_by_user_id` on every `Modified` entry, making the manual `SetUpdated()` calls redundant. Two options:

- **Remove `SetUpdated()` from `AuditableEntity` entirely.** Cleaner, but a sweep across all entities that call it.
- **Leave it as defense-in-depth.** Harmless overhead, no behavior change. Recommended.

Either way no immediate action.

## F-9 — `user_role_change_log` history table (LOW)

The current setup tells you *who last changed* a row, not *what changed*. A dedicated history table:

```sql
CREATE TABLE identity.user_role_change_log (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id UUID NOT NULL REFERENCES tenancy.tenants(id),
    user_id UUID NOT NULL REFERENCES identity.users(id),
    old_role VARCHAR(50),
    new_role VARCHAR(50) NOT NULL,
    changed_by_user_id UUID NOT NULL REFERENCES identity.users(id),
    changed_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    reason TEXT
);
CREATE INDEX idx_role_change_log_tenant_user
    ON identity.user_role_change_log (tenant_id, user_id, changed_at DESC);
```

`UpdateUserCommandHandler` would insert one row per role change. Useful for SOC2-style audit trails or "who downgraded my account on April 3rd?" investigations. Sprint 4 candidate.

## F-10 — PostgreSQL Row-Level Security (LOW)

`HasQueryFilter` enforces tenant isolation at the EF Core layer. Adding PostgreSQL RLS policies would add a DB-layer safety net catching raw-SQL bypass paths and direct DB connections. Big lift (new policies per table, role-based connection users, careful testing of bypass paths in seeders and migration runner), low marginal value at current threat model where the only DB access path is through the .NET app. Backlog indefinitely; reconsider only if we host multiple customers in the same DB or grant direct DB access to anyone.

## F-12 — ChangePassword does not revoke refresh tokens (INFO)

Similar to F-3 (which IS fixed in Phase 3 for role change). When a user changes their password, refresh tokens stay valid. Best practice would revoke. Small follow-up — extend the F-3 refresh-revocation pattern to ChangePasswordCommandHandler and ResetPasswordCommandHandler.

## F-13 — Cross-tenant test framework whitelist for SuperAdmin (INFO)

Existing cross-tenant tests assert "user from tenant A cannot affect tenant B". SuperAdmin is the platform exception. Phase 3 added one explicit test for the cross-tenant SuperAdmin pattern; the wider test infrastructure should formalize this by tagging SuperAdmin in test fixtures as exempt from the cross-tenant assertions. Cleanup task, not a behavioral fix.
