# 0049 — Role permissions as a form, not five hundred buttons

- **Status:** Approved
- **Date:** 2026-08-04
- **PRD ref:** §5 M21 (admin/user management), §12 (authorization), §7 (operator UX)
- **MVP:** in scope — usability + trust defect in a shipped admin screen
- **Requested by:** the owner ("checkbox and a submit button; if I submit, data should persist and come up on reload")

## Problem

The role-permission matrix is ~520 independent one-click micro-forms — every click is an
immediate committed write and a full page reload. Worse, two side-effects make honest saves
*look* lost: permission claims live in the auth cookie and refresh only on a timer (up to five
minutes), and saving a role you yourself hold invalidates your own session — the admin edits a
grant, gets bounced to the login page, and reasonably concludes nothing was saved. The
operator's mental model (tick boxes, press Save, see it stick) is the correct one; the screen
should match it.

## Requirements

- [M] The matrix is **checkboxes with a single Save button**; one submit persists all changes
  and the reloaded page shows exactly what was saved.
- [M] Saving never signs out the acting administrator, and their own menu reflects the change
  immediately.
- [M] Every grant/revoke stays individually audited, as today.
- [M] A save cannot remove the user-management permission from its last holding role
  (no self-lockout).
- [S] Other signed-in users pick up changes within about a minute (was: up to five).

## Acceptance criteria

1. Tick/untick several cells across roles, press Save → success toast; hard-reload shows the
   saved state; the `adm.permission` rows match (asserted by row, not markup).
2. Editing the Admin role while signed in as admin does not end the session, and the admin's
   sidebar reflects a self-affecting change without re-login.
3. Attempting to clear `admin.users.manage` from every role that holds it is refused with a
   clear message and nothing is written.
4. Audit shows one `role.grant`/`role.revoke` event per changed cell.
5. LC-ROLE-14 (money-and-controls) and grant-drift pass unchanged in behaviour.

## Out of scope

- Per-user (non-role) grants, permission scopes, or role hierarchy.
- Any change to how policies are evaluated at runtime.

## Risks / open questions

- Unchecked-checkbox-means-revoke semantics: bounded because the form always renders the full
  catalog for every role, plus the lockout guard; the legacy single-cell handler is kept
  verbatim for the verify-script API contract.
