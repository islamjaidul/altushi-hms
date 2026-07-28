# UI smoke suite (spec 0012-ui-pass)

Playwright coverage of every routed page against PRD §7's binding UX principles, §12/G10
authorisation, and the icon-font/print-CSS risks called out in the spec. Chromium only, real
HTTP login (no mocked auth) — this drives the same app a browser does.

## Prerequisites

1. **App running** at `http://localhost:5199`:
   ```sh
   export PATH="$HOME/.dotnet:$PATH"
   cd src/Hms.Web
   ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5199 dotnet run --no-launch-profile
   ```
2. **Fresh, seeded DB.** The suite asserts against specific ids (patient 1, invoice 1, order 1,
   statement 1) and exact content (e.g. "Rahim Uddin", "ALT-000001"), so it needs the same
   golden-thread data the app's own verification scripts produce — on a **freshly reset** DB:
   ```sh
   docker exec hms-dev-db psql -U postgres -d postgres \
     -c "DROP DATABASE IF EXISTS hms WITH (FORCE);" -c "CREATE DATABASE hms;"
   # restart the app so it migrates + re-seeds, then:
   python3 eng/verify/golden-thread.py
   python3 eng/verify/discount-and-dues.py
   # spec 0031 added the eleven detail screens to ROUTES; /ot/case/1 needs an operation to
   # exist, and only the OT thread creates one on a fresh database.
   python3 eng/verify/ot-thread.py
   ```
   Run all three, in that order, **before** the Playwright run.

## Install

```sh
cd eng/verify/ui
npm install
```

Chromium is expected to already be cached under `~/Library/Caches/ms-playwright`
(`chromium-1223`, matching the pinned `@playwright/test@1.60.0`). If it isn't, `npx playwright
install chromium` will fetch it.

## Run

```sh
npx playwright test                 # headless, all specs
npx playwright test smoke           # one file
npx playwright test --headed        # watch it click
npm run report                      # open the last HTML report
```

`globalSetup` (`global-setup.ts`) logs every demo-cast user in once via a real `POST /login`
(antiforgery token and all) and saves a `storageState` per user under `.auth/` — individual
tests then open contexts from those files instead of re-logging-in. It also calls
`helpers/seed-extra.ts`, which creates a few extra diagnostics orders over HTTP so that
`/lis/results` and `/lis/verify` have a patient selected by default (the golden-thread scripts
fully process order #1 through delivery, so without this, both worklists would be empty when the
suite runs) — see the comments in that file for exactly what it creates.

`tests/ux-principles.spec.ts`'s U7(b) test drives its own fixture live (create a part-paid
diagnostics order → confirm it's absent from `/lis/board` → collect the balance at
`/billing/dues` → confirm it now appears with a collect control) rather than seeding ahead of
time — that scenario is the point of the test, not incidental setup for it.

## Layout

| File | Covers |
|---|---|
| `tests/smoke.spec.ts` | Every route × a permitted user: 200, no dev-exception page, no console/network errors, shell renders, icon font loads, heading matches the nav item. |
| `tests/authz.spec.ts` | §12/G10 — 8 route/user pairs lacking the permission land on `/denied`, never a 404 or the real page. |
| `tests/ux-principles.spec.ts` | PRD §7 U1, U3, U4, U7, U9, U12. |
| `tests/documents.spec.ts` | §7 U10 — `.sheet` + letterhead + Print control, and print-media CSS hiding the shell. |
| `helpers/users.ts` | The demo cast, permission table, and the route list — kept in sync with `src/Hms.Web/DevSeed.cs`, `Perm.cs`, `ModuleNav.cs` by hand; if those drift, update here too. |
| `helpers/assertions.ts` | Shared shell/console/icon-font assertions. |
| `helpers/seed-extra.ts` | Extra HTTP-driven fixtures beyond what the two Python scripts leave behind. |

This suite only writes test files — it never touches `src/`. A failing assertion here is either a
bad selector (fix the test) or a real app defect (report it); see the top-level report for which
is which on the last run.
