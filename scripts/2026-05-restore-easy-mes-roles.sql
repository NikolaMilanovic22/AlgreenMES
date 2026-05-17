-- Sprint 3.0 — restore admin@demo.com (Admin) and manager@demo.com (Manager)
-- on the easy-mes tenant after discovery of UpdateUserCommandHandler authz
-- gaps (see audit/01_forensics.md + audit/02_findings.md).
--
-- This script is for the easy-mes-be droplet only (46.101.125.31). DO NOT run
-- against algreen pilot or alblue staging.
--
-- Schema details discovered during forensics:
--   - Tenant identifier column is `tenancy.tenants.code` (NOT slug).
--   - easy-mes tenant has code = 'DEMO' (id = f37657d5-0938-45d4-b162-30e25b167a73).
--   - User role is a text enum column `identity.users.role` (NOT a FK to a
--     roles table). Valid values: SuperAdmin, Admin, Manager, Coordinator,
--     SalesManager, Department.
--   - DO NOT write `updated_at` or `updated_by_user_id` manually. The
--     AuditableEntityInterceptor (Sprint 3.5) will populate them on the
--     next normal save through EF. Manually setting `updated_by_user_id`
--     is impossible anyway — it's a Guid FK, not free text.
--
-- Idempotent: the WHERE clause includes `role <> 'Admin'` so re-running this
-- after the fix is applied is a no-op.

BEGIN;

DO $$
DECLARE
    v_tenant_id UUID;
    v_admin_rows INT;
    v_manager_rows INT;
BEGIN
    SELECT id INTO v_tenant_id
    FROM tenancy.tenants
    WHERE code = 'DEMO'
    LIMIT 1;

    IF v_tenant_id IS NULL THEN
        RAISE EXCEPTION 'Tenant with code DEMO not found — aborting restore.';
    END IF;

    UPDATE identity.users
    SET role = 'Admin'
    WHERE tenant_id = v_tenant_id
      AND email = 'admin@demo.com'
      AND role <> 'Admin';
    GET DIAGNOSTICS v_admin_rows = ROW_COUNT;

    UPDATE identity.users
    SET role = 'Manager'
    WHERE tenant_id = v_tenant_id
      AND email = 'manager@demo.com'
      AND role <> 'Manager';
    GET DIAGNOSTICS v_manager_rows = ROW_COUNT;

    RAISE NOTICE 'Restore complete for tenant DEMO (%): admin updated rows=%, manager updated rows=%.',
        v_tenant_id, v_admin_rows, v_manager_rows;
END $$;

COMMIT;

-- Verification (read-only, expect Admin / Manager):
-- SELECT email, role FROM identity.users
-- WHERE tenant_id = (SELECT id FROM tenancy.tenants WHERE code='DEMO')
--   AND email IN ('admin@demo.com', 'manager@demo.com');
