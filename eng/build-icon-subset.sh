#!/bin/sh
# Regenerates src/Hms.Shell/wwwroot/fonts/MaterialSymbolsOutlined-subset.woff2 down to exactly the
# ligatures eng/icon-ligatures.sh finds in the source.
#
# The full variable font is ~3.9 MB and would be absurd to ship to a 2 vCPU box's browsers over a
# Bangladeshi ADSL line; the subset is under 15 KB. It is a *build* input, not a runtime one — the
# app references only the vendored subset, so `check-no-external-hosts.sh` is unaffected.
#
# Run this after adding an icon to a page, then commit the font. `check-icon-glyphs.sh` fails CI if
# you forget.
#
#   eng/build-icon-subset.sh [path-to-full-font]
#
# Without an argument it downloads the upstream variable font to a temp dir. That is the only step
# needing network, and it happens on a developer's machine, never on the deployment.
set -eu
here=$(dirname "$0")
root="$here/../src"
out="$root/Hms.Shell/wwwroot/fonts/MaterialSymbolsOutlined-subset.woff2"
upstream="https://github.com/google/material-design-icons/raw/master/variablefont/MaterialSymbolsOutlined%5BFILL%2CGRAD%2Copsz%2Cwght%5D.woff2"

python="${PYTHON:-python3}"
"$python" -c 'import fontTools, brotli' 2>/dev/null || {
  echo "build-icon-subset: needs fontTools and brotli." >&2
  echo "  uv venv .venv && uv pip install --python .venv/bin/python fonttools brotli" >&2
  echo "  PYTHON=.venv/bin/python eng/build-icon-subset.sh" >&2
  exit 2
}

tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT

full="${1:-}"
if [ -z "$full" ]; then
  full="$tmp/full.woff2"
  echo "Fetching the upstream variable font…"
  curl -sSL --fail --max-time 120 -o "$full" "$upstream"
fi

"$here/icon-ligatures.sh" "$root" > "$tmp/ligatures"
echo "$(wc -l < "$tmp/ligatures" | tr -d ' ') ligatures wanted"

"$python" - "$full" "$tmp/ligatures" "$out" <<'PY'
import sys
from fontTools import subset
from fontTools.ttLib import TTFont
from fontTools.varLib import instancer

src, ligature_file, dest = sys.argv[1:4]
ligatures = [l.strip() for l in open(ligature_file) if l.strip()]

# Every character that appears in any ligature name, plus the private-use codepoints the ligatures
# resolve to. Keeping only the letters would drop the glyphs; keeping only the glyphs would drop the
# substitution's inputs and nothing would ever trigger.
text = "".join(sorted(set("".join(ligatures))))

font = TTFont(src, fontNumber=0, lazy=True)
cmap = font.getBestCmap()
reverse = {g: cp for cp, g in cmap.items()}

# Walk GSUB for the ligatures we want, so their target glyphs survive the subset.
def component_text(glyph):
    cp = reverse.get(glyph)
    return chr(cp) if cp else None

keep = set()
for lookup in font["GSUB"].table.LookupList.Lookup:
    for sub in lookup.SubTable:
        sub = getattr(sub, "ExtSubTable", sub)
        for first, ligs in getattr(sub, "ligatures", {}).items():
            for lig in ligs:
                parts = [first] + [component_text(c) for c in lig.Component]
                if all(parts) and "".join(parts) in ligatures:
                    keep.add(lig.LigGlyph)

missing = set(ligatures) - {
    "".join([first] + [component_text(c) for c in lig.Component])
    for lookup in font["GSUB"].table.LookupList.Lookup
    for st in lookup.SubTable
    for first, ligs in getattr(getattr(st, "ExtSubTable", st), "ligatures", {}).items()
    for lig in ligs
    if all([first] + [component_text(c) for c in lig.Component])
}
if missing:
    print(f"  not in the upstream font at all: {', '.join(sorted(missing))}", file=sys.stderr)
    raise SystemExit(1)

options = subset.Options()
options.layout_features = ["liga", "dlig", "rlig", "calt", "ccmp"]
options.flavor = "woff2"
options.desubroutinize = False
options.notdef_outline = True
options.recalc_bounds = True
options.drop_tables += ["STAT", "MVAR"]

font = TTFont(src, fontNumber=0)

# Prune GSUB to the ligatures we asked for, before the subsetter runs.
#
# The subsetter closes over layout: given the letters a–z it retains every ligature those letters can
# form, and then every glyph those ligatures target — all 3,805 of them, at 252 KB. Deleting the
# rules first means the closure has nothing to pull in, and the file lands at the size of 60 icons.
wanted = set(ligatures)
for lookup in font["GSUB"].table.LookupList.Lookup:
    for st in lookup.SubTable:
        ext = getattr(st, "ExtSubTable", st)
        table = getattr(ext, "ligatures", None)
        if table is None:
            continue
        for first in list(table):
            kept = [
                lig for lig in table[first]
                if all(parts := [first] + [component_text(c) for c in lig.Component])
                and "".join(parts) in wanted
            ]
            if kept:
                table[first] = kept
            else:
                del table[first]

# Pin the axes before subsetting. Upstream ships a variable font whose deltas dwarf the outlines:
# subsetting it while still variable keeps every master for the 60 glyphs and lands at 2.8 MB, which
# is 300x the file it replaces. We render icons at one weight and one optical size, so instancing
# costs nothing and is what makes the subset small.
font = instancer.instantiateVariableFont(
    font, {"FILL": 0, "GRAD": 0, "opsz": 24, "wght": 400}, inplace=True, updateFontNames=False)

subsetter = subset.Subsetter(options=options)
subsetter.populate(text=text, glyphs=sorted(keep))
subsetter.subset(font)
font.flavor = "woff2"
font.save(dest)
print(f"  wrote {dest} — {len(keep)} icon glyphs")
PY

ls -l "$out"
