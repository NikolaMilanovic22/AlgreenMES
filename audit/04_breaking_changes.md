# 04 — Breaking Changes from Sprint 3.0 fixes

## F-7 — "Only SuperAdmin can change a user's role"

### Affected behavior

Before Sprint 3.0, a tenant Admin could change roles of users in their tenant (subject to the partial SuperAdmin grant/revoke guard). After Sprint 3.0, only a SuperAdmin can change roles. Tenant Admins can still edit name, email, active flag, process assignments — but the **role field becomes effectively read-only**.

### What this breaks

Scan of both BE repos + all 3 FE repos for flows that assume "Admin can change role":

- **`UpdateUserCommandHandler`** — direct caller; will now throw `FORBIDDEN_ROLE_CHANGE` (403) for any role change attempt by a non-SuperAdmin. **Intended.**
- **No tenant-onboarding auto-promotion flow** exists. `CreateTenantCommandHandler` does not create users; users are created separately via `POST /api/users`. So tenant-creation flows are NOT broken.
- **`CreateUserCommandHandler`** still allows tenant Admins to create new users with any non-SuperAdmin role. **Unchanged** — F-7 targets *changing* existing roles, not *creating* users. An Admin onboarding a new Manager creates them as Manager at creation time; no subsequent change needed.
- **FE UsersPage** — role `<Select>` in the edit form is now `disabled` for non-SuperAdmin viewers. They can still open the edit dialog and update name/email/active. **Intended.**

### Migration path

Existing tenants on alblue and easy-mes have at least one SuperAdmin available (`admin@demo.com` on alblue is SuperAdmin; `skyhard@easymes.app` on easy-mes is SuperAdmin). No tenant is locked out of role management.

For algreen pilot — when the freeze ends and Sprint 3.0 deploys there, Mile's tenant needs at least one SuperAdmin account *before* the deploy lands. **Verify before unfreezing the pilot.** Otherwise the only path to assign roles will require platform-level DB intervention.

### Operator-visible

- An Admin who previously edited a user's role will now see the role dropdown disabled (FE) and, if they bypass FE somehow, get a 403 toast: _"Samo SuperAdmin može da menja ulogu korisnika."_
- Sentry will NOT capture these 403 events — they're business-rule rejections that Sprint 3.1's `SetBeforeSend` filter drops. This is correct (not a bug). Mentioned in `05_summary.md` so this isn't mistaken for an outage.

## F-2c — Cannot delete the last active Admin

If a tenant has exactly one active Admin, attempting to delete that Admin returns 403 with `LAST_ADMIN_REMOVAL`. This is a new restriction; previously the delete would have succeeded. Standard tenant-lockout protection.

## F-2a — Cannot delete yourself

Even a SuperAdmin can no longer single-click delete their own account. The recovery is to have another SuperAdmin do it, or demote first (which the SuperAdmin cannot do to themselves either if they're the only SuperAdmin). The friction is intentional — accidental self-delete is the failure mode F-2 prevents.

## F-3 — Role change revokes refresh tokens

When a user's role changes, all their refresh tokens are revoked. The user's current access JWT remains valid until expiry (60 minutes). On their next browser refresh after the JWT expires, they'll be forced to re-login.

This is a UX speed bump for ops who routinely demote-then-promote during testing, but is the correct security behavior. Not flagged as broken — flagged as new.

## F-11 — Change-password is now strictly self-service

`POST /api/users/{id}/change-password` previously accepted any `{id}` as long as the caller knew the current password. Now the caller must either be the target user or a SuperAdmin. The admin-flavored equivalent for resetting another user's password (without knowing it) is still `/reset-password`, which is role-gated and unchanged.

Anyone using the `change-password` endpoint to act on someone else's account (we found no such caller in code, but FE could in theory) would now get 403 `CHANGE_PASSWORD_NOT_SELF`. Expected breakage: none in current FE.
