# 09 — Questions for the PM

- **Status:** Open · **Date:** 2026-07-26 · **Spec:** `docs/specs/0003-mvp-architecture/`
- Genuine business decisions surfaced by the architecture work. Each has a **recommended default** so nothing blocks — silence = default applies at build time, revisitable until the sprint that consumes it.

| # | Question | Why it surfaced | Recommended default |
|---|---|---|---|
| P1 | **Fiscal-year convention** for number resets (ADR-0004): Bangladesh FY (July–June), calendar year, or per-hospital choice? | Invoice numbering & day-close reporting | Per-hospital config, **default July–June**; demo uses July–June |
| P2 | **Business-day boundary** for 24/7 day-close and "today" (ADR-0004, edge 16) | Night-shift attribution | Configurable; default **00:00 Asia/Dhaka**, sessions attributed to opening day |
| P3 | **UHID scope** when branches arrive (ADR-0007): hospital-wide or per-branch? | Patient identity across branches | **Hospital-wide UHID**; branch appears in visit numbers, not patient identity |
| P4 | **Approval delegation policy** (edge 19): who may delegate, max window, night-shift escalation chain and timeout? | Engine is built; policy is data | Supervisors delegate ≤ 14 days to same-or-higher role; escalation after **10 min** to next tier; MD is terminal tier |
| P5 | **Partial-refund rule for cancelled-after-collection tests** (edge 21): full refund, minus collection fee, or per-test policy? | Cancellation flow | Per-test-department policy table, default **full refund before processing starts, none after result entry**, always approval-gated |
| P6 | **Entitlement expiry behaviour** (ADR-0016): grace period then read-only, or hard stop? | Licensing enforcement | **30-day grace with banner → read-only** (never lock clinical data); commercial wording is sales' |
| P7 | **Demo dataset naming**: fictional "Altushi General Hospital" acceptable, or brand it to the prospect per meeting? | Seed kit build | Fictional default + settings screen re-brand live in the demo (it's a selling moment — identity applies everywhere instantly) |
| P8 | **Provisional-record billing**: during construction-config, may a `provisional` price be used on a (test) invoice, or is billing blocked until confirmed? | Edge 11 tighten-before-go-live | Allowed **pre-go-live only**; the go-live switch requires zero provisional prices |
| P9 | **SMS sender & consent posture**: masked sender ID string, and do report-ready SMS go to every patient with a phone by default? | I7 integration + §8 N5 privacy | Opt-out model (SMS on unless declined at registration), sender ID per hospital config |
| P10 | **Whole-taka display convention**: show `৳ 1,500` or `1,500/-` on prints? Bangla numerals anywhere? | Print layouts (§7 U15 vocabulary) | `৳ 1,500` on screen, `Tk 1,500/-` + words-in-English on money documents (matches market samples); Western numerals everywhere |
| P11 | **Demo date + team size**: S1–S7 assumes ~14 weeks with 2–3 engineers (08 §0). Confirm or give the real constraints so the cut list activates now, not in panic later | Build plan | — (needs an answer; plan holds as drafted) |
| P12 | **Counter hardware baseline** to certify: cheapest acceptable thermal printer models + scanner models we test against (§13 I2–I6) | Print/scan spikes (S1) | Vendor proposes a 2-printer + 1-scanner reference set after S1 spike; PM approves the "supported hardware" sales line |

**Explicit disagreements/flags (ground rule 6):** none yet with PRD substance. One caution: §14's 150-operator design ceiling coexists with the 3 GB mandate only via the scale-up path in `06-deployment.md` §5 — sales material must not promise design-ceiling load on MVP hardware.
