# 0040 — Tasks

Status legend: [x] done · [ ] open. Evidence cites the proof that goes red without the change.

## WP1 — the app's validation is the one that speaks
- [x] `novalidate` on the login form; `required` kept on the inputs for a11y semantics
      — `spec-0040-login.spec.ts` "an empty submit shows OUR message under each box"
- [x] `LoginModel.UsernameRequired` / `PasswordRequired` constants; `Validate` returns them
      — `LoginValidationTests.The_messages_come_from_the_shared_constants`
- [x] Error element always in the DOM, `hidden` when empty, filled by whichever tier speaks
- [x] `login.js` — same two conditions, same strings (read from `data-msg-*`, never retyped),
      `aria-invalid`, focus the first bad field, withdraw the message as it stops being true
- [x] `aria-describedby` + `role="alert"` + `aria-live="polite"` on both error regions

## WP2 — sign-out carries the operator's page
- [x] Layout's sign-out form posts `returnUrl` = path + query
- [x] `LogoutModel.OnPostAsync(string? returnUrl)` → `/login?returnUrl=…` when it survives
- [x] `LoginModel.SafeReturnUrl(returnUrl, isLocal)` — pure; local only; never `/login`,
      `/denied`, `/logout`; path matched whole, before `?`/`#`
- [x] `LoginModel` uses it instead of `LocalRedirect`, so a hostile URL degrades to `/`
      instead of throwing into the fault boundary as a 500

## Verification
- [x] `LoginReturnUrlTests` — 19 cases (foreign host, protocol-relative, auth pages, casing,
      trailing slash, null/blank, query preserved, `/loginhelp` not eaten by a prefix test)
- [x] `LoginValidationTests` extended — 9 cases, constants asserted
- [x] `spec-0040-login.spec.ts` — 6 browser cases including the no-JS path and the
      ten-blank-submits lockout guard
- [x] **Watched red**: with `novalidate` removed and the logout redirect reverted, **5 of the 6
      browser cases fail**. The sixth (hostile `returnUrl`) covers the login-side hardening,
      which that revert did not touch; `LoginReturnUrlTests` is its red-able proof.
      Notable: the no-JS case failed too — `required` alone blocked the post, so the pre-fix
      page was mute even with scripting off. That is the root cause, stated as a test.
- [x] `dotnet test hms-erp.slnx` — 427 passed, 0 failed (was 407)
- [x] `lifecycle-suite --tier t0` GREEN (all twelve role logins) and `--tier t1` GREEN
      (10 scripts, 12/12 roles) — the shared layout carries the new field on every page
- [x] `hrm-thread.py` 38/0 against :5299 — both SKUs share this page, so both were driven
- [x] Guards: ui-tokens, no-external-hosts (the script is local), no-native-date, fkeys,
      no-hard-deletes, traceability — all OK
