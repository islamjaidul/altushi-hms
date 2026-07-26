---
name: prd-lookup
description: Find and cite requirements in docs/project_manager.md (the HMS ERP PRD) without reading the 123 KB file. Use whenever a question concerns modules, user stories, personas, data flows, entities, workflow states, permissions, integrations, volumetrics, MVP scope, or competitor findings for this project.
---

# PRD lookup

The PRD is ~1,350 lines / 123 KB. Reading it whole wastes most of a context window. Grep to a section, then read that range only.

## Method

1. Locate: `grep -n '^## ' docs/project_manager.md` (top-level) or `grep -n '^### Module 9' docs/project_manager.md`.
2. Read with `offset` + `limit` covering just that section.
3. Cite as `§9A.2` or `§5 Module 9`. **Never cite line numbers** — they drift as the doc changes.

## Section map

| § | Contents |
|---|---|
| 1 | Vision, differentiators (D1–D5), target customer, out-of-scope |
| 2 | Competitor analysis (2.1 proposals, 2.2 coverage table, 2.3 adopt/improve/reject) |
| **2.4** | **Live-system walkthrough** — how MEDISpa/PrimeMIS were accessed + observed module maps |
| 3 | Bangladesh context: 3.1 regulatory (DGHS, blood-bank law), 3.2 payment/billing culture, 3.3 operations, **3.4 financial rails (BEFTN/TDS/VAT)** |
| 4 | 15 personas (P1–P15) |
| 5 | **22 modules** with sub-features + user stories (`### Module 1..22`) |
| **5A** | Live-observed enrichments (5A-1..21) + 4 new sub-modules (R1 reporting consultant, R2 health/discount cards, R3 queue display, R4 bill-block) |
| 6 | Data flows: 6.1 system context, 6.2 OPD, 6.3 IPD, 6.4 lab, 6.5 pharmacy, 6.6 revenue→accounts |
| 7 | UX principles U1–U15 for 30+ operators, training bar, screen inventory |
| 8 | Non-functional needs N1–N10 |
| 9 | 3-phase release plan · **9A = frozen MVP scope** |
| 10 | Business data dictionary (~30 entities, owner/reader/lifecycle) |
| 11 | Workflow state machines (⚿ = needs approval) |
| 12 | Roles × permissions matrix + cross-role approval workflows |
| 13 | Integration & hardware inventory (I1–I15) |
| 14 | Volumetrics & design ceilings |
| 15 | Success metrics |
| 16 | Assumptions, **open questions Q1–Q15 for the architect**, handoff checklist |
| 17 | References |

## Quick greps

```bash
grep -n '^### Module ' docs/project_manager.md          # all 22 module headings
grep -n 'US[0-9]*\.[0-9]' docs/project_manager.md       # user stories
grep -n '\[obs: ' docs/project_manager.md               # live-observed items
grep -n '^| Q[0-9]' docs/project_manager.md             # architect open questions
```

## Rules

- Answer from the PRD, not memory. If it isn't in the PRD, say so — don't invent a requirement.
- `[obs: …]` tags mean live-verified from a competitor system; untagged items come from the written proposals or industry research. Preserve that distinction when quoting.
