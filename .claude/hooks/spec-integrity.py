#!/usr/bin/env python3
"""Spec archive integrity check (Stop hook).

Deterministic verification of the rules in CLAUDE.md Rule 0 and
docs/specs/README.md. Advisory by design: prints findings and always
exits 0, so it reports drift without blocking work.

Checks:
  1. every spec dir has a spec.md with a recognised Status
  2. every spec at Approved/In Progress/Done has an archived plan.md
  3. every spec dir appears in the docs/specs/README.md index
  4. every index row corresponds to a real spec dir

Run directly to audit on demand:  python3 .claude/hooks/spec-integrity.py
"""
import json
import re
import sys
from pathlib import Path

VALID = ("Draft", "Approved", "In Progress", "Done", "Superseded", "Abandoned")
NEEDS_PLAN = ("Approved", "In Progress", "Done")

root = Path(__file__).resolve().parents[2]
specs_dir = root / "docs" / "specs"
readme = specs_dir / "README.md"

if not specs_dir.is_dir():
    sys.exit(0)  # archive not set up yet; nothing to police

index_text = readme.read_text(encoding="utf-8") if readme.exists() else ""
findings = []
seen_ids = {}

spec_dirs = sorted(
    d for d in specs_dir.iterdir()
    if d.is_dir() and not d.name.startswith(("_", "."))
)

for d in spec_dirs:
    spec_file = d / "spec.md"
    if not spec_file.exists():
        findings.append(f"HIGH  {d.name}/ has no spec.md")
        continue

    text = spec_file.read_text(encoding="utf-8")
    m = re.search(r"^-\s*\*\*Status:\*\*\s*(.+)$", text, re.M)
    status = m.group(1).strip() if m else ""
    # strip markdown emphasis and trailing option lists from the value
    status_word = next((v for v in VALID if status.startswith(v)), None)

    if not status_word:
        findings.append(f"HIGH  {d.name}/spec.md has missing/unrecognised Status ({status!r})")
    elif status_word in NEEDS_PLAN and not (d / "plan.md").exists():
        findings.append(
            f"HIGH  {d.name}/ is '{status_word}' but has no archived plan.md "
            f"(CLAUDE.md Rule 0: plans must be archived in docs/)"
        )

    if d.name not in index_text:
        findings.append(f"MED   {d.name}/ is missing from the docs/specs/README.md index")

    sid = d.name.split("-", 1)[0]
    if sid in seen_ids:
        findings.append(f"HIGH  duplicate spec ID {sid}: {seen_ids[sid]} and {d.name}")
    seen_ids[sid] = d.name

# index rows referencing directories that don't exist
on_disk = {d.name for d in spec_dirs}
# slug must start with a letter, so dates (2026-07-26) are not mistaken for spec IDs
for ref in set(re.findall(r"\b(\d{4}-[a-z][a-z0-9-]*)\b", index_text)):
    if ref not in on_disk:
        findings.append(f"MED   index references '{ref}' but no such spec dir exists")

if findings:
    lines = ["Spec integrity — {} finding(s):".format(len(findings))]
    lines += ["  " + f for f in sorted(findings)]
    lines.append("  (advisory; see CLAUDE.md Rule 0 and the spec-flow skill)")
    body = "\n".join(lines)
    if sys.stdout.isatty():
        # run by hand from a terminal: plain text is friendlier
        print(body)
    else:
        # run as a Stop hook: systemMessage is what surfaces to the user
        print(json.dumps({"systemMessage": body, "suppressOutput": True}))

# advisory by design: never block the turn
sys.exit(0)
