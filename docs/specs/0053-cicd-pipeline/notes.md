# 0053 — Notes

## What the red pipeline was actually hiding

The reported symptom was "CI fails on every push and I get an email each time". The cause was one
line, and the guard that found it was right: `Ipd/Certificates.cshtml:85` used a native
`<input type="date">`, which ADR-0020 forbids because it renders the browser's locale rather than
the dd/mm an operator reads. Not a CI defect at all — a product defect, correctly reported, for
days, to someone who had stopped reading.

Fixing the markup forced the binding. `FollowUpOn` was a `DateOnly?` that only bound because the
native picker posts ISO; it is now free text through `FlexibleDate`, matching `Consult.cshtml.cs`.
Unparseable input is **refused with a message** rather than dropped as Consult does — the
certificate body freezes at issue and reprints never recompute, so a date the operator typed and
never saw again would be a permanent hole in a document that cannot be reissued.

**The expensive part was the masking.** `Guard scripts` ran before everything, so for as long as it
was red, all five test projects, the vulnerable-package scan, the additive-migration gate and the
image build were **skipped, not passed**. The moment the guard went green, the next gate failed —
and it had never once run:

`dotnet ef` spells `--configuration` in full; `-c` is the short form of `--context`. The workflow
said `-c Release ... --context KernelDbContext`, which named a DbContext called "Release", then
overrode it, and left the configuration at its Debug default — so the gate looked in `bin/Debug`
after a Release build and died on a missing `deps.json`. The check that proves no migration drops
a column had been dead on arrival, and nothing reported it because an earlier step always failed
first. This is the argument for the per-guard reporting that replaced the single `-e` block: a gate
that stops at the first failure cannot tell you it is the only one running.

## Acceptance criteria — how each was verified

1. **Full gate on a code push** — run 31083069044, all steps ✓ including the migration gate.
2. **Docs-only commit skips the build** — the `changes` job gates on it and reports which files
   decided. *Not yet exercised by an actual docs-only push; the closing commit will be the first.*
3. **GHCR image, same digest on the VM** — `ghcr.io/islamjaidul/altushi-hms/app:e72dee8`, and
   `docker inspect hms-app-1` on the VM reports `sha256:eea88ec1…` for that ref.
4. **Deploy waits, VM unchanged while waiting** — job status `waiting`, and the VM was confirmed
   still on `sha256:3e4dd6d0…`/`hms-app:dev` during the wait.
5. **Rollback on an unhealthy deploy** — **NOT verified.** The path is written and reviewed, and
   the happy path proved the health probe works, but no deploy has been made to fail deliberately.
   See follow-ups.
6. **No key material in the workflow** — secrets only; the registry token goes to the VM over
   stdin, never as an argv element that `ps` would expose on a box with four tenants.
7. **SDK pinned** — **NOT done.** `10.0.x` still floats. The run summary now records the resolved
   SDK so drift is visible, which was the cheaper half. See follow-ups.

## First production deploy

2026-08-06, approved by the owner, commit `e72dee8`. Backup → pull → swap → healthy in 21 s; the
job took 40 s. `/health` 200 locally and `https://hms.specshipper.com` 200. The other eleven
containers on the box (HRM, four DMS, POS, MySQL, pharmacy, and two strays) were confirmed
untouched by uptime. Payroll migrations from 0052 applied to the live database as part of it.

## Follow-ups

- **The rollback path has never fired.** Rehearse it: deploy an image whose `/health` fails, on the
  demo/HRM stack rather than the ERP, and confirm the previous digest comes back. Until that is
  done, §8 N6's guarantee is a claim about code that has not been run.
- ~~**The CI key is root-equivalent.**~~ **Done** — `hms-deploy-gate.sh` is a forced command
  (`no-pty`, no forwarding) that accepts one thing: `<registry-user> <image>`, where the image must
  start with our own GHCR prefix and carry a 40-character sha tag. `:latest` is refused because a
  moving tag is not a reviewed commit.

  The load-bearing half is not the forced command but the ownership change beside it. A forced
  command is worthless if the account can rewrite the script the command runs — or `compose.yml`,
  which can mount `/` into a container just as well as `docker run` can. `deploy/` is therefore
  now `root:deploy` with group write removed, and CI no longer scps into it. **Consequence:**
  changing `deploy-remote.sh` or the compose files requires a root `git pull` plus a re-run of
  `vm-harden-deploy-key.sh` on the VM. That is the cost of the key not being root, and it is worth
  it. Requires one root command to install (`deploy/vm-harden-deploy-key.sh`); until that is run,
  the new CI protocol will fail at the deploy step — safely, at the gate, not in production.
- **`VM_USER` is the secret `deploy`**, so GitHub masks the word "deploy" everywhere in the run
  log — "Pre-*** backup taken". Harmless but it degrades the log. Make it a variable, not a secret;
  a username on a host that already exposes SSH is not the sensitive part.
- **Pin the SDK** once a known-good version has held for a few runs (AC 7).
- **The HRM SKU has no CD.** `compose.hrm.vm.yml` is still a manual `git pull && build` on the VM —
  the exact thing this spec removed for the ERP, and on the same 3 GB box.
- **`main` has no branch protection**, so "merged or pushed" is always a direct push and the gate
  reports rather than blocks. Adding a protection rule would make it a gate in fact.
