# 0040 — Plan

Approved shape: fix the two defects at their causes, keep the server tier exactly as it is
(it is correct and tested), and add the client tier that was missing.

## WP1 — Make the app's validation the one that speaks

**Cause:** `required` makes the browser refuse the submit, so neither tier of the product's
own validation is ever reached.

1. `novalidate` on the login form. The browser's bubble stops pre-empting us; `required`
   **stays on the inputs** for the accessibility semantics (screen readers announce the field
   as required) — it is the form-level UI that is suppressed, not the meaning.
2. Message constants on `LoginModel` (`UsernameRequired`, `PasswordRequired`); `Validate`
   returns them. The view renders them into `data-msg-*` on the form, so the script cannot
   drift from the server (spec rule 2).
3. Always render the error element for each field — empty and `hidden` when there is nothing
   to say. One element, filled by whichever tier speaks first.
4. `src/Hms.Shell/wwwroot/js/login.js`, referenced with `asp-append-version`: on submit,
   validate the same two conditions, fill the same elements, mark `aria-invalid`, focus the
   first bad field, and `preventDefault`. Anything else submits normally.
5. Accessibility: `aria-describedby` ties each input to its error; the error region is
   `aria-live="polite"` so the message is announced, not just drawn.

**Degradation:** with scripting off nothing above runs, the form posts, and the server
renders the same strings into the same elements. That is the property that keeps this honest.

## WP2 — Carry the operator's page through sign-out

1. The layout's sign-out form posts `returnUrl` = `Request.Path + Request.QueryString`.
2. `LogoutModel.OnPostAsync(string? returnUrl)` signs out and redirects to
   `/login?returnUrl=…` when the URL survives the safety check, plain `/login` otherwise.
3. `LoginModel.SafeReturnUrl(returnUrl, isLocal)` — pure, so it is testable without HTTP:
   returns `/` unless the URL is local **and** its path is not `/login`, `/denied` or
   `/logout`. Path compared before `?`/`#`, exact segment match (so `/loginhelp` is not
   caught by a prefix test).
   The framework's `Url.IsLocalUrl` supplies `isLocal` — the security-critical half stays
   with the framework rather than being re-implemented here.
4. `LoginModel` uses it in place of the `LocalRedirect` + ad-hoc prefix check, so a hostile
   URL lands on `/` instead of throwing into the fault boundary.

## Verification — each watched red first

| Proof | Layer | Goes red without |
|---|---|---|
| `LoginReturnUrlTests` | `Hms.Web.Tests` (pure) | WP2.3 — foreign host, `/denied`, `/login`, null, query preserved |
| `LoginValidationTests` (extended) | `Hms.Web.Tests` (pure) | WP1.2 — `Validate` returning anything but the constants |
| `login.spec.ts` | Playwright, real browser | WP1.1/1.4 — the exact reproduction in the spec |
| existing HTTP path | `role-journeys.py` etc. | the server tier, unchanged |

Then: `dotnet test hms-erp.slnx`, `eng/check-*` guards, and both SKUs driven by hand in a
browser (the ERP and the HRM share this page, so both must be looked at).
