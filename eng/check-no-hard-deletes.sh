#!/usr/bin/env bash
# Hard rule 4 (spec 0039 WP2.6): financial and clinical rows are never hard-deleted — a
# correction is a reversal. The database revokes DELETE from hms_app, but dev and migrations
# run with owner rights, so the code convention is the second lock. This fails the build when
# an EF Remove/RemoveRange appears in the modules that hold money or the audit trail.
#
# If a delete is ever genuinely legitimate (a draft's scratch rows, a pure mapping table),
# it does not belong in these modules — or it needs this guard changed in the same review.
set -euo pipefail
cd "$(dirname "$0")/.."

hits=$(grep -rn '\.Remove(\|\.RemoveRange(' \
    src/Modules/Billing src/Modules/Ipd src/Modules/Pharmacy src/Hms.Kernel \
    --include='*.cs' 2>/dev/null | grep -v '/Migrations/' || true)

if [ -n "$hits" ]; then
    echo "FAIL: hard delete in a financial/clinical module (hard rule 4 — reversals, not deletes):"
    echo "$hits"
    exit 1
fi
echo "OK: no hard deletes in billing, IPD, pharmacy or kernel."
