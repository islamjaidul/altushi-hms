#!/usr/bin/env python3
"""The lifecycle runner — executes the suite by tier and reports per-case.

Tiers exist because the scripts differ in what they assume, not in what they cover:

  t0  read-only. Logins, route probes, public surfaces. Safe against any environment.
  t1  mutating, but each script provisions its own patient and asserts only about records it
      created, so a dirty database is fine.
  t2  mutating AND assuming a fresh database — `golden-thread.py` asserts absolute money
      totals ("today's income is exactly 550"), which is true only on an empty ledger.

Mixing t2 into a normal regression run is the classic false red: the assertion is about the
database, not the build. Keeping it a separate tier is the whole point.

  python3 lifecycle-suite.py --tier t0
  python3 lifecycle-suite.py --tier t1
  BASE_URL=https://hms.example.com HMS_QA_ENV=prod HMS_QA_CONFIRM=hms.example.com \
      python3 lifecycle-suite.py --tier t1
"""
import argparse
import json
import os
import pathlib
import re
import subprocess
import sys
import urllib.parse

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from _harness import BASE, CAST, is_local  # noqa: E402

TIERS = {
    "t0": ["role-journeys.py", "grant-drift.py"],
    "t1": ["lifecycle-thread.py", "edge-cases.py", "ipd-thread.py", "emr-thread.py",
           "ot-thread.py", "radiology-thread.py", "pharmacy-thread.py", "pharmacy-full.py",
           "frontdesk-check.py", "money-and-controls.py"],
    "t2": ["golden-thread.py", "discount-and-dues.py"],
}
# golden-thread registers the patient discount-and-dues then bills — order is load-bearing.

DOC = HERE.parent.parent / "docs" / "qa" / "patient-lifecycle.md"


def doc_cases() -> dict[str, str]:
    """Every LC id in the lifecycle document, mapped to its coverage cell."""
    out = {}
    if not DOC.exists():
        return out
    for line in DOC.read_text().splitlines():
        m = re.match(r"\|\s*(LC-[A-Z]+-\d+)\s*\|", line)
        if m:
            cells = [c.strip() for c in line.split("|")]
            out.setdefault(m.group(1), cells[-2] if len(cells) > 2 else "")
    return out


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--tier", default="t0", choices=["t0", "t1", "t2", "all"])
    ap.add_argument("--env", default=os.environ.get("HMS_QA_ENV", ""),
                    choices=["", "local", "vm", "prod"])
    ap.add_argument("--only", default="", help="comma-separated script names")
    args = ap.parse_args()

    if args.env:
        os.environ["HMS_QA_ENV"] = args.env
    tiers = ["t0", "t1", "t2"] if args.tier == "all" else [args.tier]

    host = urllib.parse.urlparse(BASE).hostname or "?"
    print("=" * 78)
    print(f"LIFECYCLE SUITE   target={BASE}   tiers={','.join(tiers)}")
    print(f"local={is_local()}   env={args.env or '(unset)'}")
    print("=" * 78)

    if not is_local() and any(t in ("t1", "t2") for t in tiers):
        if args.env not in ("vm", "prod"):
            sys.exit(f"REFUSED: {host} is not localhost. A mutating tier needs --env vm|prod.")
        if "t2" in tiers and args.env == "prod":
            sys.exit("REFUSED: tier t2 is never permitted against production.")
        if os.environ.get("HMS_QA_CONFIRM") != host:
            sys.exit(f"REFUSED: set HMS_QA_CONFIRM={host} to write to {args.env}. "
                     "Read docs/qa/README.md first — a mutating run leaves audit and ledger "
                     "rows that Rule 4 forbids deleting.")

    only = {s.strip() for s in args.only.split(",") if s.strip()}
    results, users_seen = [], set()

    for tier in tiers:
        for script in TIERS[tier]:
            if only and script not in only:
                continue
            print(f"\n>>> [{tier}] {script}")
            env = dict(os.environ)
            proc = subprocess.run([sys.executable, script], cwd=HERE, env=env,
                                  capture_output=True, text=True)
            out = proc.stdout + proc.stderr
            ok = proc.returncode == 0
            fails = [l.strip() for l in out.splitlines()
                     if l.strip().startswith(("✗", "FAIL")) or " FAIL " in l]
            # A script that dies on a traceback prints neither ✗ nor FAIL, so the summary used
            # to read "FAIL exit=1 (0 failed check(s))" with an empty list — the failure was
            # real and invisible (spec 0029). Attach the tail so the cause is on screen.
            if not ok and not fails:
                tail = [l.rstrip() for l in out.splitlines() if l.strip()][-6:]
                fails = [f"crashed with no failed check — last output:"] + tail
            # Which roles this script drives. The legacy threads do not announce their logins,
            # so read it from the source — and only credit a script that actually passed, or a
            # crashed run would claim coverage it never reached.
            if ok:
                src = (HERE / script).read_text(errors="ignore")
                users_seen |= {u for u in CAST if f'Session("{u}"' in src}
            results.append({"tier": tier, "script": script, "ok": ok,
                            "failures": fails[:20], "returncode": proc.returncode})
            print(f"    {'PASS' if ok else 'FAIL'}  exit={proc.returncode}"
                  f"{'' if ok else f'  ({len(fails)} failed check(s))'}")
            for f in fails[:8]:
                print(f"      {f}")

    # ---- report -----------------------------------------------------------
    cases = doc_cases()
    gaps = [c for c, cov in cases.items() if "gap" in cov]
    print("\n" + "=" * 78)
    bad = [r for r in results if not r["ok"]]
    print(f"SUITE {'GREEN' if not bad else 'RED'} — {len(results)} scripts, {len(bad)} failed")
    print(f"lifecycle document: {len(cases)} cases, {len(gaps)} with no coverage")

    missing = sorted(set(CAST) - users_seen)
    if "t1" in tiers or args.tier == "all":
        if missing:
            print(f"INCOMPLETE: these roles never drove the run — {', '.join(missing)}")
        else:
            print(f"roles exercised: {len(users_seen)}/{len(CAST)}")

    for r in bad:
        print(f"\n  {r['script']} (tier {r['tier']}):")
        for f in r["failures"][:10]:
            print(f"    {f}")

    if os.environ.get("HMS_QA_JSON"):
        pathlib.Path(os.environ["HMS_QA_JSON"]).write_text(json.dumps(
            {"base": BASE, "tiers": tiers, "results": results,
             "users": sorted(users_seen), "gaps": gaps}, indent=2))
    print("=" * 78)
    return 1 if bad or (("t1" in tiers or args.tier == "all") and missing) else 0


if __name__ == "__main__":
    sys.exit(main())
