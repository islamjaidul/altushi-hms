# 0050 — Plan

## Approved: 2026-08-04

(Section of the approved demo-day plan; shared checkpoint/verify/deploy steps apply.)

**Name**: Program.cs:182 default → "Sylhet Evergreen Hospital"; `Org__Name`/`Tagline`/`Address`/`Phone` in deploy compose; OrgIdentity.cs:42-43 default address refresh; DevSeed.cs:117 + additive fixup renaming branch MAIN "Altushi General Hospital" → "Sylhet Evergreen Hospital" (covers the VM row). Login/_Layout/_Letterhead/_SheetFooter/registration-card banner/SMS `{hospital}` all read OrgIdentity → free. Leave signed entitlement customer string, BanglaSpikeTests, Barcode128Tests, prev-release.sql untouched.

**UHID SEH-**: RegistrationService.cs:51 → `"SEH-{n:D6}"`; PharmacySale.cs:23 → `"SEH-WALKIN"`; idempotent boot fixup: patient row `ALT-WALKIN` → `SEH-WALKIN` (+ cosmetic `number_series.display_format`). The series formatter uses the passed const, so existing DBs flip immediately. Update assertions: RegistrationTests.cs:76, emr-thread.py:89, golden-thread.py:58, ui/helpers/fixtures.ts:28, ux-principles.spec.ts:107/118.

**Logo**: process the supplied PNG → `src/Hms.Shell/wwwroot/img/{logo-lockup.png, logo-mark.png, favicon.png}` (white→transparent if ImageMagick/PIL available; fallback: white rounded chip behind the mark). `<img>` replaces the text monogram in _Layout sidebar + Login aside + `_Letterhead`; `<link rel="icon">` in _Layout + Login. Brand greens as new tokens in tokens.css. Guards: no external hosts, no literal hex outside tokens.css, `asp-append-version` on refs.

**Login blur**: layered radial-gradient mesh in brand greens + blurred `::before` layer + frosted card (`backdrop-filter`). DOM contract untouched (spec-0040-login.spec.ts asserts `input[name=Username/Password]`, `[data-error-for]` exact texts, single submit button, `.input-invalid`).

**Sidebar**: _Layout.cshtml:61-75 — group titles become `<button data-nav-toggle aria-expanded>` with the already-subset `chevron_right` glyph rotating via CSS (do NOT add glyphs — subset rebuild avoided); items wrapped in `.nav-group-items`; app.js toggle + localStorage per group; active group forced open; JS-off = expanded. New classes defined in app.css (check-css-classes). Whole-rail icon collapse = stretch only.
