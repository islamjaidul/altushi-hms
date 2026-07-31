#!/bin/bash
# Joins docs/qa/patient-lifecycle.md against the code it claims to describe, so the document
# cannot quietly become fiction. Three checks:
#
#   1. Every case marked covered names a script that exists.
#   2. Every case names a performing user that exists in DevSeed.cs's cast.
#   3. Every permission in Perm.cs appears in the role-journey route table, and that table's
#      route/permission pairs still match the [Authorize] attributes on the page models.
#
# Check 3 is the one that matters: it is the drift that already affects
# eng/verify/ui/helpers/users.ts, whose tables are hand-synced with three C# files.
#
# Usage: check-lifecycle-traceability.sh [--stats]
set -uo pipefail
cd "$(dirname "$0")/.."

DOC=docs/qa/patient-lifecycle.md
JOURNEYS=eng/verify/role-journeys.py
SEED=src/Hms.Web/DevSeed.cs
PERMS=src/Hms.Web/Perm.cs
# Every permission-constant file, not just the ERP host's: HR ships its own alongside the razor
# class library that holds its screens (ADR-0025).
PERMS_FILES=$(find src -name 'Perm.cs' -o -name 'HrPerm.cs' | sort | tr '\n' ' ')
fail=0

note() { printf '  %-6s %s\n' "$1" "$2"; }

[ -f "$DOC" ] || { echo "FAIL   missing $DOC"; exit 1; }

# --- 1. cases ---------------------------------------------------------------
cases=$(grep -E '^\|\s*LC-[A-Z]+-[0-9]+\s*\|' "$DOC" | grep -cvE '\*\*(High|Medium|Low)\*\*')
covered=$(grep -E '^\|\s*LC-[A-Z]+-[0-9]+\s*\|' "$DOC" | grep -cE '\|[^|]*(auto|ui|xunit|manual)[^|]*\|[^|]*$')
gaps=$(grep -E '^\|\s*LC-[A-Z]+-[0-9]+\s*\|' "$DOC" | grep -cE '\|[^|]*gap[^|]*\|[^|]*$')

# Every script named in a coverage cell must exist.
missing_scripts=""
for s in $(grep -oE '(auto) [a-z0-9-]+' "$DOC" | awk '{print $2}' | sort -u); do
  [ -f "eng/verify/$s.py" ] || missing_scripts="$missing_scripts $s"
done
if [ -n "$missing_scripts" ]; then
  note FAIL "coverage cites scripts that do not exist:$missing_scripts"; fail=1
else
  note ok "every cited verification script exists"
fi

# Every xUnit CLASS named in a coverage cell must exist too. Until spec 0032 this check did not
# exist, and three rows cited classes that were never there — LC-BIL-12 `RateTests` (it is
# `ApprovalAndRateTests`), LC-PHA-18 `PharmacyTests` (it is `PharmacyStockTests`), and LC-XCUT-05
# "architecture tests", which is a category and not a class at all. A `xunit` citation was the
# one kind of coverage claim nothing verified, which is exactly where the register rotted:
# LC-BIL-09 claimed `MoneySpineTests` proved the refund path while that file had no refund test.
#
# This catches a name that does not resolve. It cannot catch a class that exists but does not
# assert what the row claims — that needs a human reading the tests, which is what a periodic
# module sweep is for (docs/qa/module-coverage.md).
missing_classes=""
for c in $(grep -oE '`xunit` [A-Za-z]+Tests' "$DOC" | awk '{print $2}' | sort -u); do
  grep -rqs --include='*.cs' "class $c" tests/ || missing_classes="$missing_classes $c"
done
if [ -n "$missing_classes" ]; then
  note FAIL "coverage cites xUnit classes that do not exist:$missing_classes"; fail=1
else
  note ok "every cited xUnit test class exists"
fi

# --- 2. performing users ----------------------------------------------------
cast=$(grep -oE '\("[a-z]+", "[^"]+", "[^"]+"\)' "$SEED" | cut -d'"' -f2 | sort -u)
badusers=""
for u in $(grep -oE '`[a-z]+`' "$DOC" | tr -d '`' | sort -u); do
  case "$u" in
    jashim|rasel|ripon|farhana|shahid|admin|md|parvin|nasrin|chowdhury|shaheen|moinul)
      echo "$cast" | grep -qx "$u" || badusers="$badusers $u" ;;
  esac
done
if [ -n "$badusers" ]; then
  note FAIL "document names users absent from DevSeed.cs cast:$badusers"; fail=1
else
  note ok "every performing user exists in the seeded cast"
fi

# --- 3. permissions and routes ---------------------------------------------
# Every permission declared in Perm.cs must actually be enforced somewhere: either as a page
# policy (and therefore present in the role-journey route table) or as a finer-grained
# `Can("...")` check inside a handler, which is how the pages split a shared screen between
# two roles (Lis/Board.cshtml.cs:43 is the canonical example).
#
# KNOWN EXCEPTION — appointments.create. Declared in Perm.cs, granted to the Receptionist in
# DevSeed.cs, and enforced nowhere: /appointments carries only AppointmentsRead, and neither
# OnPostIssueAsync nor OnPostAdvanceAsync adds a check. Tracked in
# docs/qa/patient-lifecycle.md as LC-QUE-08. Remove from this list when it is enforced.
# Permissions that are deliberately declared without an enforcement point. Empty since spec
# 0030: `appointments.create` was the last entry, and it was not deliberate — it was granted
# to the Receptionist and enforced nowhere, so `appointments.read` alone could issue serials.
# Adding to this list should be a considered act, not a way to make the guard quiet.
KNOWN_UNENFORCED=""

declared=$(grep -rhoE '= P \+ "[a-z.]+"' $PERMS_FILES | cut -d'"' -f2 | sort -u)
in_routes=$(grep -oE '": "[a-z.]+"' "$JOURNEYS" | cut -d'"' -f3)
in_handlers=$(grep -rhoE 'Can\("[a-z.]+"\)' src/ 2>/dev/null | cut -d'"' -f2)
used=$(printf '%s\n%s\n%s\n' "$in_routes" "$in_handlers" "$KNOWN_UNENFORCED" | sort -u)
unused=$(comm -23 <(echo "$declared") <(echo "$used"))
if [ -n "$unused" ]; then
  note FAIL "permissions declared in Perm.cs but enforced nowhere: $(echo $unused | tr '\n' ' ')"
  fail=1
else
  note ok "all $(echo "$declared" | wc -l | tr -d ' ') permissions are enforced or listed as known"
fi

# Route/permission pairs must match the [Authorize] attributes actually on the page models.
drift=$(python3 - "$JOURNEYS" <<'PY'
import pathlib, re, sys
routes = {}
# Every UI root, not just the host's. HR's screens live in a razor class library so the same
# build serves the standalone HRM SKU (ADR-0025); a guard that only knew src/Hms.Web/Pages
# would pass while covering none of them.
PAGE_ROOTS = [p for p in pathlib.Path("src").rglob("Pages") if p.is_dir()]
for f in [f for root in PAGE_ROOTS for f in root.rglob("*.cshtml")]:
    head = f.read_text(errors="ignore").splitlines()[0] if f.stat().st_size else ""
    m = re.match(r'@page\s+"([^"]+)"', head.strip())
    if not m:
        continue
    raw = m.group(1)
    # an optional parameter ({x:long?}) also matches with the segment absent
    route = re.sub(r"/?\{[^}]*\?\}", "", raw)
    route = re.sub(r"\{[^}]*\}", "1", route).rstrip("/") or "/"
    code = f.with_suffix(".cshtml.cs")
    if not code.exists():
        continue
    a = re.search(r"\[Authorize\(Policy = (?:Hr)?Perm\.(\w+)\)\]", code.read_text(errors="ignore"))
    if a:
        routes[route] = a.group(1)
consts = {}
for perms in list(pathlib.Path("src").rglob("Perm.cs")) + list(pathlib.Path("src").rglob("HrPerm.cs")):
    consts.update(re.findall(r'(\w+) = P \+ "([a-z.]+)"', perms.read_text()))
real = {r: consts[c] for r, c in routes.items() if c in consts}
src = pathlib.Path(sys.argv[1]).read_text()
block = re.search(r"ROUTES = \{(.*?)\n\}", src, re.S).group(1)
table = dict(re.findall(r'"([^"]+)": "([a-z.]+)"', block))
out = []
for route, perm in sorted(real.items()):
    if route not in table:
        out.append(f"{route} protected by {perm} is absent from the table")
    elif table[route] != perm:
        out.append(f"{route}: table says {table[route]}, code says {perm}")
for route in sorted(set(table) - set(real)):
    out.append(f"{route} is in the table but no page declares it")
print("\n".join(out))
PY
)
if [ -n "$drift" ]; then
  note FAIL "route table has drifted from the [Authorize] attributes:"
  echo "$drift" | sed 's/^/         /'
  fail=1
else
  note ok "route table matches every [Authorize] attribute on disk"
fi

# --- stats ------------------------------------------------------------------
if [ "${1:-}" = "--stats" ]; then
  echo
  echo "  cases: $cases   covered: $covered   gap: $gaps"
fi

echo
if [ "$fail" -eq 0 ]; then
  echo "lifecycle traceability OK — $cases cases, $covered covered, $gaps gaps"
else
  echo "lifecycle traceability FAILED"
fi
exit "$fail"
