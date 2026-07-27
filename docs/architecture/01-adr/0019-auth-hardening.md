# 0019 — Auth hardening: sessions, 2FA, idle lock, per-role menus, shared logins (Q15)

- **Status:** Accepted
- **Date:** 2026-07-26
- **Answers:** PRD §16 Q15 (§12; §5A-21; edge case 29; C5)

## Context

Market expectation (both live competitors): two-step auth, lock screens, dynamic per-role menu trees; PrimeMIS adds reCAPTCHA/Firebase — which we **cannot** adopt as-is (cloud dependency violates the offline demo, edge 1). Endemic reality: staff share one login (edge 29), which destroys C5 attribution unless the design pushes back. Operators are non-technical (§7).

## Options considered

| Option | Pros | Cons |
|---|---|---|
| Passwords only | Frictionless | Fails market expectation on finance screens; weak against C5 |
| Mandatory 2FA for everyone | Strong | Counter staff without smartphones/SMS during outages → lockouts at the front desk |
| **Tiered hardening (chosen)** | Strength where money/permissions live, speed where §7 lives | Policy matrix to maintain |

## Decision

- **AuthN:** ASP.NET Core Identity, salted modern hashing, server-side cookie sessions (revocable), login throttling + per-account lockout with audit (no CAPTCHA dependency; LAN threat model). **TOTP 2FA required** for finance-approver, admin and MD roles (offline-friendly authenticator apps; recovery codes printed to the admin pack); optional per §12 role elsewhere. SMS-based 2FA rejected (outage-dependent).
- **Sessions:** short idle timeout with a **lock screen** (re-enter password/PIN to resume, same operator only) on counter roles — market-expected behaviour, and the C5 mitigation for walk-away terminals. Configurable timeout per role class.
- **Fast user switching against shared logins (edge 29):** the counter lock screen offers "switch operator" in ≤ 2 keystrokes and ~2 s, so per-person accountability is *faster than sharing a login* — design pressure, not policing. Every money receipt prints the operator's name (visible attribution norm); concurrent-session anomalies (same account, two active counters) alert supervisors. Residual risk accepted and stated: no technical control fully prevents credential sharing; policy + payslip-level accountability complete it (per-user accounts are seat-unlimited so licensing never incentivises sharing — commercial note for PM).
- **AuthZ:** §12 matrix enforced **server-side** as permission policies (module.action granularity); the sidebar/menu tree is *composed from* the same permission set (dynamic per-role menus, §5A-21 parity, §7 U1) so UI and enforcement can't diverge. Approval thresholds (discount limits etc.) are data (C7 engine), not role code.
- **Secrets/transport:** LAN TLS via Caddy's internal CA (trust distributed by setup script to counter PCs); passwords never logged; audit on all permission changes (ADR-0011 Tier 1).

## Consequences

- Finance screens meet market expectation (2FA + lock + dynamic menus) with zero internet dependence.
- Cost accepted: TOTP enrolment friction for approver roles (one-time, done at implementation); lock-screen UX must be genuinely ≤ 2 s or operators will share logins anyway — this is a §7-grade UX requirement, tested as such.

## Reversal trigger

Pilot evidence that fast-switch still loses to shared logins at real counters → escalate to hardware second factors on approver actions (cheap USB/NFC tokens or supervisor barcode-badge co-sign), costed then.

## Amendment — 2026-07-27: security-stamp revalidation (Phase-2 review, `10-mvp-review.md` §4.3)

As shipped, permissions are stamped into the auth cookie at sign-in (`PermissionClaimsFactory`, `Program.cs:49`) and never revalidated, so a revoked grant survives until the next voluntary sign-in. For a hospital with shift handover that is not acceptable: a supervisor grant pulled mid-shift must die mid-shift. **Decision:** enable ASP.NET Core Identity security-stamp validation with a ≤ 5-minute interval; permission/role changes update the user's security stamp, forcing principal refresh (re-stamping current permissions) or sign-out on the next request after the interval. The permission model, dynamic menus and server-side policies are unchanged — only staleness is bounded. Cost: one DB read per user per interval, negligible at §14 volumes. Implemented in the Wave-0 spec.
