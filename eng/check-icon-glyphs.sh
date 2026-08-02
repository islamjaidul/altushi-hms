#!/bin/sh
# CI gate: every icon ligature the product renders must exist in the vendored subset font.
#
# Material Symbols renders an icon by turning the literal text "person_off" into a glyph, through a
# GSUB ligature. If the subset lacks that ligature the browser falls back to the body font and draws
# the word — PERSON_OFF, in capitals, across a dashboard tile. Nothing errors: the CSS is valid, the
# font loads, the markup is right, and the response is 200.
#
# The subset was cut in spec 0012 for the ERP's screens. HR added six icons it never contained, and
# `local_shipping` shows this is not only HR's problem — the ERP has shipped one broken glyph since
# that subset was made.
set -eu
root="${1:-src}"
font="$root/Hms.Shell/wwwroot/fonts/MaterialSymbolsOutlined-subset.woff2"
here=$(dirname "$0")

[ -f "$font" ] || { echo "check-icon-glyphs: $font not found" >&2; exit 1; }

python="${PYTHON:-python3}"
if ! "$python" -c 'import fontTools' 2>/dev/null; then
  # Loud, and non-zero. A guard that quietly passes when its dependency is missing is worse than
  # no guard — it reports green over an unchecked font.
  echo "check-icon-glyphs: fontTools is not importable by '$python'." >&2
  echo "  Install it (uv pip install fonttools brotli), or point PYTHON= at an interpreter that has it." >&2
  exit 2
fi

"$here/icon-ligatures.sh" "$root" > /tmp/hms-icon-used.$$
trap 'rm -f /tmp/hms-icon-used.$$' EXIT

"$python" - "$font" /tmp/hms-icon-used.$$ <<'PY'
import sys
from fontTools.ttLib import TTFont

font, wanted_file = sys.argv[1], sys.argv[2]
f = TTFont(font)
codepoint_of = {glyph: cp for cp, glyph in f.getBestCmap().items()}

def text(glyph):
    cp = codepoint_of.get(glyph)
    return chr(cp) if cp else None

have = set()
for lookup in f["GSUB"].table.LookupList.Lookup:
    for sub in lookup.SubTable:
        sub = getattr(sub, "ExtSubTable", sub)      # ligatures sit behind extension subtables
        for first, ligs in getattr(sub, "ligatures", {}).items():
            for lig in ligs:
                parts = [first] + [text(c) for c in lig.Component]
                if all(parts):
                    have.add("".join(parts))

wanted = [l.strip() for l in open(wanted_file) if l.strip()]
missing = sorted(set(wanted) - have)

if missing:
    print(f"{len(missing)} icon ligature(s) used by a page but absent from the subset font.", file=sys.stderr)
    print("They will render as their own names, in the body font:", file=sys.stderr)
    for m in missing:
        print(f"  {m}", file=sys.stderr)
    print("\nRegenerate the subset: eng/build-icon-subset.sh", file=sys.stderr)
    raise SystemExit(1)

print(f"icon-glyphs: OK ({len(wanted)} ligatures, {len(have)} in font)")
PY
