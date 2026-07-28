#!/usr/bin/env python3
"""LC-XCUT-11, first cut — what happens when N operators use the app at once.

`docs/architecture/06-deployment.md` §2a says it plainly: the verification suite "says nothing
about 40 operators at once", and the QA pass confirmed no load or concurrency test exists
anywhere in the repo. This is the smallest honest step towards one, and it is deliberately
**not wired into any tier**.

It is not the answer to LC-XCUT-11. What N should be, what mix of reads and writes a real
Bangladeshi counter morning is, and what happens to the 3 GB budget under it are architecture
questions, raised in `ADR-0024` (Proposed). This measures one
thing — concurrent read latency against §8 N1 — so that conversation starts from a number.

Read-only: every request is a GET of a screen the signed-in role already holds. Safe to point
at any environment, including production, though the load itself is a courtesy question.

    python3 load-probe.py                       # 8 operators, 10 rounds
    python3 load-probe.py --operators 40 --rounds 5
    BASE_URL=https://hms.example.com python3 load-probe.py --operators 12

Stdlib threading only. A load tool that needs a dependency installed on a 2 vCPU / 3 GB box to
tell you the box is too small has answered its own question (G15).
"""
import argparse
import pathlib
import statistics
import sys
import threading
import time

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from _harness import BASE, Session, guard   # noqa: E402

# One representative screen per operator role — the pages a shift actually sits on.
JOURNEYS = {
    "jashim": ["/", "/registration", "/frontdesk", "/ipd/board", "/appointments"],
    "rasel": ["/", "/billing/opd", "/billing/dues", "/billing/session", "/diagnostics/order"],
    "ripon": ["/", "/lis/board", "/lis/results"],
    "farhana": ["/", "/lis/verify", "/radiology/worklist"],
    "parvin": ["/", "/pharmacy/pos", "/pharmacy/stock", "/pharmacy/dashboard"],
    "nasrin": ["/", "/ipd/indents", "/emr/vitals", "/emr/charts"],
    "chowdhury": ["/", "/emr/queue", "/emr/history"],
    "shaheen": ["/", "/ot/board", "/ot/register"],
    "moinul": ["/", "/radiology/worklist"],
    "shahid": ["/", "/admin/approvals"],
    "md": ["/", "/dashboard"],
    "admin": ["/", "/admin/users", "/admin/audit"],
}

# §8 N1: the response budget an operator screen is held to.
BUDGET_MS = 2000

samples: dict[str, list[float]] = {}
errors: list[str] = []
lock = threading.Lock()


def operator(user: str, rounds: int) -> None:
    try:
        sess = Session(user)
    except Exception as e:                                  # noqa: BLE001
        with lock:
            errors.append(f"{user} could not sign in: {e}")
        return
    for _ in range(rounds):
        for path in JOURNEYS[user]:
            started = time.perf_counter()
            try:
                sess.get(path)
                elapsed = (time.perf_counter() - started) * 1000
                with lock:
                    samples.setdefault(path, []).append(elapsed)
            except Exception as e:                          # noqa: BLE001
                with lock:
                    errors.append(f"{user} {path}: {type(e).__name__}: {e}")


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--operators", type=int, default=8)
    ap.add_argument("--rounds", type=int, default=10)
    args = ap.parse_args()

    guard("t0")

    # Operators are drawn round-robin from the cast, so 40 means 40 concurrent sessions across
    # the twelve roles rather than 40 copies of one privileged one.
    users = list(JOURNEYS)
    assigned = [users[i % len(users)] for i in range(args.operators)]

    print("=" * 78)
    print(f"LOAD PROBE  target={BASE}  operators={args.operators}  rounds={args.rounds}")
    print(f"budget: §8 N1 = {BUDGET_MS} ms per screen")
    print("=" * 78)

    started = time.perf_counter()
    threads = [threading.Thread(target=operator, args=(u, args.rounds)) for u in assigned]
    for t in threads:
        t.start()
    for t in threads:
        t.join()
    wall = time.perf_counter() - started

    total = sum(len(v) for v in samples.values())
    print(f"\n{total} request(s) in {wall:.1f}s — {total / wall:.1f} req/s\n")
    print(f"{'route':32} {'n':>5} {'p50':>8} {'p95':>8} {'max':>8}   over budget")
    breaches = 0
    for path in sorted(samples, key=lambda p: -statistics.median(samples[p])):
        v = sorted(samples[path])
        p50 = statistics.median(v)
        p95 = v[min(len(v) - 1, int(len(v) * 0.95))]
        over = sum(1 for x in v if x > BUDGET_MS)
        breaches += over
        print(f"{path:32} {len(v):5} {p50:7.0f}ms {p95:7.0f}ms {v[-1]:7.0f}ms   "
              f"{over or '-'}")

    if errors:
        print(f"\n{len(errors)} error(s):")
        for e in errors[:10]:
            print(f"  {e}")

    print("\n" + "=" * 78)
    print(f"{breaches} response(s) over the {BUDGET_MS} ms budget, {len(errors)} error(s)")
    print("This is a probe, not a verdict — LC-XCUT-11's pass criteria are an open "
          "question for the architect (ADR-0024, Proposed).")
    print("=" * 78)
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
