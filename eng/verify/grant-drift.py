#!/usr/bin/env python3
"""LC-XCUT-14 — does this deployment's §12 grant matrix still match the code's?

Spec 0030 F1. `DevSeed.cs` seeds permissions **additively**: it inserts a grant when absent and
never removes one. A grant from an older build, or one an administrator added by hand at
`/admin/users`, therefore survives every subsequent deploy, and nothing anywhere compares the
two. On `hms.specshipper.com` that had gone far enough that the Billing Operator held
`admin.approvals.decide` — the operator who requests a discount could approve it — and the
Admin role reached thirty routes beyond its granted set.

Detection is possible without a new endpoint because `/admin/users` already renders the whole
matrix, one button per (role, permission) pair, labelled `Revoke <perm> for <role>` when held
and `Grant <perm> for <role>` when not. So one authenticated GET recovers a deployment's entire
grant matrix.

    python3 grant-drift.py                              # read-only, exits 1 on any difference
    BASE_URL=https://hms.example.com python3 grant-drift.py
    BASE_URL=https://hms.example.com HMS_QA_ENV=prod HMS_QA_CONFIRM=hms.example.com \
        python3 grant-drift.py --fix                    # revoke what the code does not grant

`--fix` only ever REVOKES, and only what the code does not grant. It never grants: inventing a
permission on a customer database is the hazard ADR-0023 exists to avoid. Each revocation goes
through the same `/admin/users?handler=Permission` POST an administrator would use, so it
writes a tier-2 `role.revoke` audit event and bumps every holder's security stamp.
"""
import argparse
import pathlib
import re
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from _harness import BASE, Session, case, check, grant_cells, guard, report   # noqa: E402

DEV_SEED = pathlib.Path(__file__).resolve().parents[2] / "src" / "Hms.Web" / "DevSeed.cs"


def code_matrix() -> dict[str, set[str]]:
    """The §12 matrix as the code states it — `DevSeed.cs`'s `Roles` dictionary."""
    src = DEV_SEED.read_text()
    block = re.search(r"Roles = new\(\)\s*\{(.*?)\n    \};", src, re.S)
    if not block:
        sys.exit(f"REFUSED: could not find the Roles dictionary in {DEV_SEED} — "
                 "the parser is out of date and a silent empty matrix would report "
                 "every grant on the deployment as drift.")
    out: dict[str, set[str]] = {}
    for role, perms in re.findall(r'\["([^"]+)"\]\s*=\s*\[(.*?)\]', block.group(1), re.S):
        out[role] = set(re.findall(r'"([a-z][a-z.]+)"', perms))
    if not out:
        sys.exit("REFUSED: parsed an empty grant matrix out of DevSeed.cs.")
    return out


def deployment_matrix(sess: Session) -> dict[str, set[str]]:
    """The matrix this deployment is actually running, read off `/admin/users`."""
    html = sess.get("/admin/users")
    if "Role permissions" not in html:
        sys.exit("REFUSED: /admin/users did not render the permission matrix — the session "
                 "may lack admin.users.manage, or the sign-in failed.")
    out: dict[str, set[str]] = {}
    for role, perm, _role_id, held in grant_cells(html):
        out.setdefault(role, set())
        if held:                        # the button offers to revoke, so the role holds it
            out[role].add(perm)
    return out


def role_ids(sess: Session) -> dict[str, str]:
    """Role name → id, needed to POST a revocation."""
    return {role: rid for role, _perm, rid, _held in grant_cells(sess.get("/admin/users"))}


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--fix", action="store_true",
                    help="revoke grants the code does not carry (audited; never grants)")
    ap.add_argument("--user", default="admin", help="a demo user holding admin.users.manage")
    args = ap.parse_args()

    # Reading is safe anywhere; revoking is a write to a real deployment and needs consent.
    guard("t0" if not args.fix else "t1")

    code = code_matrix()
    sess = Session(args.user)
    live = deployment_matrix(sess)

    case("LC-XCUT-14", "A deployment's role grants match the code's grant matrix", args.user)
    print(f"      code: {len(code)} roles · deployment {BASE}: {len(live)} roles")

    extra_total, missing_total = {}, {}
    for role in sorted(set(code) | set(live)):
        want, have = code.get(role, set()), live.get(role, set())
        if role not in code:
            # A role somebody created at /admin/users. Not drift against the code — the code
            # has nothing to say about it — but worth naming rather than passing over.
            print(f"      note  role '{role}' exists on the deployment and not in the code "
                  f"({len(have)} grant(s))")
            continue
        extra, missing = sorted(have - want), sorted(want - have)
        if extra:
            extra_total[role] = extra
        if missing:
            missing_total[role] = missing

    check(not extra_total,
          "no role holds a permission the code does not grant" +
          ("" if not extra_total else " — DRIFT: " + "; ".join(
              f"{r} has {', '.join(p)}" for r, p in extra_total.items())))
    check(not missing_total,
          "every permission the code grants is present on the deployment" +
          ("" if not missing_total else " — MISSING: " + "; ".join(
              f"{r} lacks {', '.join(p)}" for r, p in missing_total.items())))

    if extra_total and args.fix:
        print("\n  revoking the grants the code does not carry")
        ids = role_ids(sess)
        for role, perms in extra_total.items():
            rid = ids.get(role)
            if not rid:
                print(f"      !!  could not resolve a role id for {role}")
                continue
            for perm in perms:
                page = sess.get("/admin/users")
                sess.post("/admin/users?handler=Permission",
                          {"roleId": rid, "permission": perm, "grant": "false"}, page)
                print(f"      revoked {perm} from {role}")
        after = deployment_matrix(sess)
        still = {r: sorted(after.get(r, set()) - code.get(r, set()))
                 for r in code if after.get(r, set()) - code.get(r, set())}
        check(not still, "re-read after the fix: no extra grant remains" +
              ("" if not still else f" — {still}"))

    return report("GRANT DRIFT")


if __name__ == "__main__":
    sys.exit(main())
