# 0015 — Plan

## Approved: 2026-07-27

Per ADR-0020 (input layer), ADR-0022 (upgrade gate), ADR-0019 amendment (revalidation).

1. **Kernel forgiving date** — `src/Hms.Kernel/Text/FlexibleDate.cs`: `TryParse` accepting
   `d/M/yyyy`, `dd/MM/yyyy`, `d-M-yyyy`, `dd-MM-yyyy`, `yyyy-MM-dd`, `d/M/yy`, `dd/MM/yy`,
   `d MMM yyyy`, `d MMMM yyyy`. `Registration/New.ParseAge` delegates its date branch to it.
2. **`hms-date`** — `TagHelpers/HmsDateTagHelper.cs` (`<input hms-date>` → text input, hint
   placeholder, `data-hms-date`) + `wwwroot/js/hms-date.js` (echo `dd MMM yyyy` on blur for
   recognised formats; `data-implies` sets a period select to `custom` on edit; period
   select change clears paired dates). Server side parses via `FlexibleDate`.
   Replace native dates: `/admin/masters` `EffectiveFrom` (bind string, parse), `/billing/reports`.
3. **Search contract** — Dues/Refund: resolve matching patient ids in `reg` (ILIKE, capped),
   then filter in `bill` by `InvoiceNo ILIKE @q OR PatientId IN (…)` — predicate in SQL per
   context, join in memory (ADR-0003 rule).
4. **Type-ahead** — `GET /api/typeahead/patients?q=` (min 2 chars, ILIKE over name/UHID/
   phone, active + unmerged only, ranked exact-prefix-first, take 10, `{value,label}`),
   `RequireAuthorization(Perm.RegistrationRead)`, via `HmsTx`. Extend `typeahead.js` with
   `data-submit` (submit form on selection). Replace the three patient `<select>`s with
   typeahead input + hidden `PatientId` (same field name; GET binding untouched); show the
   selected patient, offer "change patient".
5. **Reports range** — `From`/`To` become strings parsed by `FlexibleDate`; if either parses
   and `Range != "custom"`, dates win (treated as custom). Headings echo the range.
6. **Security stamp** — `SecurityStampValidatorOptions.ValidationInterval = 5 min`
   (config-able); role/permission edits in Admin call `UpdateSecurityStampAsync`.
7. **Cross-context guard** — add method-chain detection to `CrossContextQueryTests`:
   `.Join(`/`.GroupJoin(` whose receiver and argument name different `s.X` scopes.
8. **Upgrade gate** — `eng/verify/upgrade/`: `prev-release.sql` (demo-data dump of the
   deployed release), `run.sh` (fresh DB ← dump, boot app, run golden thread), CI job with
   a Postgres service container.
9. **CI grep** — `eng/check-no-native-date.sh` (no `type="date"` in `src/`), wired into
   `ci.yml` guard step.
10. **RUNBOOK** — go-live switch section: seed off, rotate demo credentials, verify no demo
    login works, provisional-price check (P8).
11. **Verify** — build + 81 tests + updated Playwright + golden-thread + discount-and-dues
    on fresh DB; upgrade gate run; close spec.
