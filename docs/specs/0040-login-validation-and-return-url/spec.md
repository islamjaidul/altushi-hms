# 0040 — The login screen: validation the operator can see, and a sign-in that returns them where they were

- **Status:** Done
- **Date:** 2026-08-03
- **PRD ref:** §7 (U1, U8 — operator UX), §12 (auth), §8 N2
- **MVP:** in scope — defect fix on a shipped screen, no new product scope
- **Follows:** [0039-lifecycle-hardening](../0039-lifecycle-hardening/spec.md) (the validation tier this screen was left out of)

## Problem

Two defects on the one screen every operator meets first, reported from use.

### 1. An empty sign-in says nothing

`LoginModel.Validate` is correct, tested (`LoginValidationTests`, 8 cases) and renders
correctly — a POST with empty fields returns the page carrying *"Enter your username."*,
*"Enter your password."* and `input-invalid` on both boxes. Verified over HTTP against both
hosts.

**In a browser none of that ever runs.** Both inputs carry `required`, so the browser refuses
to submit the form at all. Reproduced in Chromium against :5199:

```
URL after submit:            http://localhost:5199/login      (never posted)
username validationMessage:  "Please fill out this field."    (the browser's, not ours)
server-rendered errors?:     false
visible .help-text.bad:      0
```

So the operator's only feedback is the browser's native bubble, which:

- is **the browser's text in the browser's locale** — a Chrome set to Bengali says
  *"এই ফিল্ডটি পূরণ করুন"*, on a product whose operator UI is English-only by constant;
- **vanishes** on the next keystroke or click, and names only the first bad field;
- is styled by the browser, so it is not the product's error language (`.help-text.bad`)
  that operators are trained on everywhere else;
- and, on a slow shared counter PC, is easy to miss entirely — which is exactly what was
  reported: *"it does not give me any error message"*.

The validation tier spec 0039 built for every other screen stops at the login page: the
server tier is right and unreachable, and there is no client tier at all.

### 2. Signing out forgets where you were

`LogoutModel.OnPostAsync` signs out and returns `Redirect("/login")` — no `returnUrl`. The
layout's sign-out button posts nothing but the antiforgery token. So an operator interrupted
on `/billing/opd?PatientId=118` signs out, signs back in and lands on the dashboard, then
navigates back by hand. The framework's own challenge path (hit a protected page while signed
out) *does* carry `ReturnUrl` and works; only the deliberate sign-out drops it.

Related, found while reading it: `LoginModel` calls `LocalRedirect(target)` on a `returnUrl`
straight from the query string. That is not an open redirect — `LocalRedirect` refuses a
foreign host — but it refuses it by **throwing**, which the 0039 fault boundary then renders
as a recoverable 500. A crafted link produces an error page where it should produce the
dashboard.

## Users and value

- **As an operator**, I want to be told which box I left empty, in the product's own words,
  so that I can fix it without guessing what the browser is complaining about.
  **AC:** submitting an empty form shows *"Enter your username."* under the username box
  and *"Enter your password."* under the password box, both visible without a round trip,
  and neither box is marked valid.
- **As an operator**, I want signing back in to return me to the screen I was on, so that an
  interruption does not cost me the navigation back.
  **AC:** signing out from `/billing/opd?PatientId=118` and back in lands on
  `/billing/opd?PatientId=118`, query string intact.
- **As the hospital**, I want a hostile `returnUrl` to be ignored rather than to error,
  so that a pasted link cannot bounce an operator anywhere but this product.
  **AC:** `?returnUrl=https://evil.example/` signs in and lands on `/` — no redirect, no 500.

## Scope

**In:** the login and logout screens in `Hms.Shell` (shared by both SKUs — ERP and HRM),
their tests, and one browser-level regression test.

**Out:** the password policy, 2FA, lockout thresholds (ADR-0019 owns those), and the
`HmsPageModel` input gate — the login page is `[AllowAnonymous]` and deliberately outside it.

## Rules

1. **The server stays authoritative.** The client tier is an accelerator, never the gate.
   With scripting off the form must still submit and the server must still refuse it — the
   behaviour proven over HTTP today does not regress.
2. **One source of truth for the words.** The two tiers must not drift, so both read the same
   message constants. A test asserts the rendered page carries what `Validate` returns.
3. **No new dependency, no external host.** Vendored, local, stdlib-equivalent script only
   (§8 N2 — the product must render with the network unplugged).
4. **A blank box must never reach `PasswordSignInAsync`** — the existing reason stands and is
   load-bearing: Identity charges a wrong password against the *account's* lockout counter, so
   stray Enter presses on a shared PC lock out a colleague. This spec must not weaken it.
5. **`returnUrl` is untrusted input.** Local URLs only, never back to `/login`, `/denied` or
   `/logout`, and a bad one degrades to `/` silently rather than throwing.
