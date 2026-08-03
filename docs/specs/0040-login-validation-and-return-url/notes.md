# 0040 — Notes

## How the defect hid from every layer we had

`LoginModel.Validate` was correct and covered by eight unit cases; an HTTP POST with empty
fields returned the page carrying both messages, so the verify scripts saw nothing wrong
either. The screen was still mute, because **the form never reached the server**: `required`
made the browser refuse the submit, and its native bubble — its wording, its locale, gone by
the next keystroke — was the operator's only feedback.

The lesson is the mirror of the one 0037 wrote down. There the rule was *assert the row, not
the status code*, because a handler returned 302 and wrote nothing. Here the server was right
and the browser never asked it. **A validation tier that only exists server-side is untested
until something drives it the way a person does.** This is what `eng/verify/ui` is for, and
this screen had no case there.

Reproduced before fixing, in Chromium:

```
URL after submit:            http://localhost:5199/login      (never posted)
username validationMessage:  "Please fill out this field."    (the browser's, not ours)
server-rendered errors?:     false
visible .help-text.bad:      0
```

## Decisions

- **`novalidate` on the form, `required` left on the inputs.** The attribute carries meaning
  for assistive technology ("required field"); it is the browser's *UI* that had to stop
  pre-empting ours, not the semantics. Removing `required` would have cost the a11y signal to
  fix a presentation problem.
- **The message strings live in C# and are rendered into `data-msg-*`.** The client tier reads
  them from the DOM and never retypes them, so the instant message and the one that survives a
  round trip are the same string by construction. A test asserts `Validate` returns the
  constants, so inlining a literal breaks the build's proof rather than drifting silently.
- **One error element per field, always present.** Both tiers fill the same node. The
  alternative — the script creating its own — gives two elements that can disagree.
- **`SafeReturnUrl` takes `isLocal` as a parameter** rather than calling `Url.IsLocalUrl`
  itself. The security-critical half stays with the framework; the policy half (which pages
  are never a destination) stays pure and testable with no HTTP.
- **A hostile `returnUrl` degrades, it does not throw.** `LocalRedirect` refuses a foreign
  host by raising, which the 0039 fault boundary renders as a recoverable 500 — an error page
  where the dashboard was the right answer. It was never an open redirect; it was a bad
  outcome for a good refusal.

## Deliberately not done

- **Remembering the page across the session-expiry path.** When a cookie expires the framework
  challenges with its own `ReturnUrl` and that already works; this spec only closed the
  deliberate sign-out, which was the reported gap.
- **Client-side checking of anything but emptiness.** Username shape and password rules belong
  to the server and to ADR-0019; guessing at them in the browser would teach operators a rule
  the product does not actually enforce.

## Evidence at close

`dotnet test hms-erp.slnx` 427/0 (was 407) · `spec-0040-login.spec.ts` 6/6, and 5 of those 6
watched fail against the reverted fix · `lifecycle-suite --tier t0` and `--tier t1` GREEN,
12/12 roles · `hrm-thread.py` 38/0 · all six `eng/check-*` guards OK. Both SKUs were driven,
since `Hms.Shell` ships this page to each.
