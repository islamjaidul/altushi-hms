# 0000 — Plan (archived, retroactive)

## Approved: 2026-07-26 (retroactively archived under spec 0001)

> **Provenance note.** This is the plan-mode file for the *live competitor-system analysis*
> that produced PRD v1.1. It was recovered from `~/.claude/plans/` after the spec archive
> was introduced, so it is archived retroactively rather than at approval time.
>
> The earlier plan that produced **PRD v1.0** (the proposal-based PRD) was overwritten in
> place by this one and **is not recoverable**. Stating that plainly rather than
> reconstructing it — a fabricated record is worse than a missing one.
>
> Demo credentials present in the original have been **redacted**; secrets do not belong
> in git history.

---

# Plan: Live-System Analysis of PrimeMIS & MEDISpa → Merge Findings into PRD

## Context

A comprehensive PRD already exists at `docs/project_manager.md` (22 modules, personas, data flows, data dictionary, state machines, permission matrix), built from the two **written proposals**. The user has now supplied **live demo credentials** for both competitor products and wants me to log in, deep-analyze the *actual running software*, find gaps in the PRD, and merge real observed detail into it so the Software Architect gets richer, evidence-based input.

**Credentials provided (user-authorized, demo accounts):**
- PrimeMIS — `https://primemis.farazymaxit.com/auth/login` — `<credentials redacted — see PM>`
- MEDISpa — `https://medispa.imposetechltd.com` (→ `/login`) — `<credentials redacted — see PM>`

**Confirmed from HTTP headers (read-only probe already done):**
- PrimeMIS = ASP.NET / IIS, server-rendered, anti-forgery token on login form.
- MEDISpa = Laravel + Inertia.js, `XSRF-TOKEN` + `laravel_session` cookies, `X-Inertia` responses (JSON page props — structurally rich and parseable).

## Capability constraint (must be transparent to user & in deliverable)

- **No browser automation / screenshot tool is available.** Only scripted HTTP via `curl`/Bash (cookie jar + CSRF handling).
- I can harvest **structural** evidence: navigation/module trees, screen routes, form field labels, table columns, report & dashboard names, workflow states, dropdown options.
- I **cannot** observe rendered visual UI/UX (spacing, colors, real screenshots).
- **No fabrication:** every finding folded into the PRD must trace to an actual retrieved response. Anything unreachable (JS-only nav, captcha, login failure) is explicitly marked "not accessible" — the proposal-based analysis already in the PRD stands as the fallback.

## Steps (executed after approval)

### 1. Authenticate to both apps (Bash + curl, cookie jars)
- **MEDISpa (Inertia/Laravel):** GET `/login` → capture `XSRF-TOKEN` + session cookie → POST `/login` with credentials + `X-XSRF-TOKEN` header (URL-decoded) + `X-Requested-With` + `X-Inertia`. Try username variants (`demo`, and email-style if the field demands it). On success, request pages with `X-Inertia` header to receive JSON page props.
- **PrimeMIS (ASP.NET):** GET `/auth/login` → extract `__RequestVerificationToken` + antiforgery cookie → POST credentials → follow to dashboard.
- If either login fails after reasonable attempts, record it honestly and proceed with whatever succeeded.

### 2. Crawl the authenticated surface (read-only GETs)
- Fetch the post-login dashboard/home of each; parse the **sidebar/nav menu** to enumerate module → sub-menu → screen routes.
- Walk representative screens per module (registration, OPD/diagnostic billing, IPD/folio, lab/LIS, pharmacy, accounts, HR, dashboards, OT, reports).
- For each screen, extract: route/URL, page title, form field labels, required-field markers, table column headers, action buttons, filter options, dropdown/enum values, report names. For MEDISpa, mine Inertia JSON `component` names + prop keys (reveals data model hints).
- Keep raw captures in the scratchpad dir for traceability; never dump them into the PRD verbatim.

### 3. Gap analysis vs current PRD
Build a comparison table: **Observed in live software → Present / Missing / Under-specified in PRD**. Look specifically for:
- Modules/sub-screens the running product has that the proposal PDFs (and thus the PRD) omitted.
- Field-level and workflow detail richer than the proposals (e.g., exact discharge-bill line types, LIS status stages, HR payroll components, accounts voucher types, dashboard KPI tiles).
- Enum/status values that sharpen the §11 state machines.
- Report catalogs (exact report names) that sharpen §22 MIS.
- Navigation/IA patterns that inform §7 UX.

### 4. Merge into `docs/project_manager.md`
- Add **§2.4 "Live System Walkthrough — Observed Findings"**: what was accessed, per-product module/menu maps as actually seen, and an evidence table. Clearly label reachability.
- Fold concrete, verified detail into the affected existing sections: module sub-features (§5), state machines (§11), data dictionary (§10), reports/dashboards (§22), integration/hardware (§13), and UX/IA (§7) — each new item tagged with a source marker (e.g., `[obs: PrimeMIS]` / `[obs: MEDISpa]`) so the architect can distinguish proposal-derived vs live-observed requirements.
- Update the "gap" columns in §2.2/§2.3 with anything newly discovered.
- Bump doc version to v1.1 with a short changelog line.

### 5. Verify
- Confirm every `[obs:...]` item has a corresponding raw capture in scratchpad (no fabrication).
- Re-check internal consistency (new modules/screens appear in module list, data flow, and permission matrix as needed).
- Confirm no technology decisions leaked in (PM discipline); observed *stack* facts live only as architect-relevant notes, not as mandates.
- Report to the user: which screens were reachable, what genuine gaps were found, and what could not be accessed.

## Files
- **Edit**: `/Users/jidulislam/Projects/hms-erp/docs/project_manager.md` (merge findings; the single deliverable)
- **Scratchpad** (traceability only): raw login/crawl captures under the session scratchpad dir.

## Risk / honesty note
Scripted login against unknown CSRF/anti-bot flows may partially or fully fail; SPA menus rendered only in client JS may hide routes from curl. Realistic outcome is *partial* structural coverage. The plan degrades gracefully: verified findings get merged and marked; gaps are stated plainly; the existing proposal-based PRD remains valid on its own.
