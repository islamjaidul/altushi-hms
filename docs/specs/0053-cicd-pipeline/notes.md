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
5. **Rollback on an unhealthy deploy** — **verified, after the drill proved it was broken.**
   `eng/verify/deploy-rollback-drill.sh`: 11 assertions, now green, and in CI as `deploy-drill`.
   See "What the drill found" below.
6. **No key material in the workflow** — secrets only; the registry token goes to the VM over
   stdin, never as an argv element that `ps` would expose on a box with four tenants.
7. **SDK pinned** — **done.** `global.json` names 10.0.302 with `rollForward: latestPatch`;
   `setup-dotnet` installs from it rather than resolving "latest 10.0"; both Dockerfiles build on
   the same version. Before this, CI proved one binary and shipped another compiled by a floating
   `sdk:10.0` tag.

## First production deploy

2026-08-06, approved by the owner, commit `e72dee8`. Backup → pull → swap → healthy in 21 s; the
job took 40 s. `/health` 200 locally and `https://hms.specshipper.com` 200. The other eleven
containers on the box (HRM, four DMS, POS, MySQL, pharmacy, and two strays) were confirmed
untouched by uptime. Payroll migrations from 0052 applied to the live database as part of it.

## What the drill found

Writing `eng/verify/deploy-rollback-drill.sh` was supposed to confirm the rollback. It failed on
the first honest run, with exit 2 — *rollback also failed*.

`deploy-remote.sh` recorded the rollback point with `docker inspect --format '{{.Image}}'`, which
returns the image **ID**, then handed that `sha256:…` back to compose as `image:`. Compose reads
that as an image *name*, cannot resolve it, and leaves the broken container in place. **The
recovery step that exists to keep the counter up would itself have failed, in exactly the
situation it was written for** — and it had already been through review and one production deploy.

The fix keeps the two apart: `.Config.Image` is the reference to redeploy; `.Image` is the ID to
verify against afterwards, because a tag can be moved and a rollback that silently lands somewhere
else is worse than one that fails loudly.

A second lesson, cheaper but the same shape: the drill's first version used local image tags, so
`docker pull` failed and all four rollback assertions passed **for the wrong reason**. Testing a
recovery path against a weakened version of the production contract proves nothing. The drill now
runs a throwaway registry and pulls, exactly as the real deploy does.

## The concurrency deadlock

One concurrency group per ref with `cancel-in-progress: false` on main meant a run parked in
`waiting` for its deploy approval **held the group**. Every later push queued behind a human
decision that might never come: two runs cancelled, a third pending ten minutes with no jobs and
nothing in the UI explaining why. Cancelling the parked run released the queue instantly.

Serialising deploys is correct — two deploys must not race onto one box — but that belongs on the
deploy jobs, which carry their own `concurrency: deploy-production`. Builds are independent of
each other; deploys are not. One group over both turned the safe rule for deploys into an outage
for builds.

## Follow-ups
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
- ~~**Pin the SDK**~~ **Done** — see AC 7.
- ~~**The HRM SKU has no CD.**~~ **Done** — `image-hrm` + `deploy-hrm`, its own `production-hrm`
  environment and reviewer, gated by `DEPLOY_HRM_ENABLED`. `deploy-remote.sh` is parameterised
  rather than forked; the gate reads the SKU off the image repository name.
- ~~**`main` has no branch protection.**~~ **Done** — `gate` is the single required check, with
  force-push, deletion and non-linear history refused. Admin enforcement is deliberately **off**
  so the owner can still push directly; turn it on when the team is bigger than one.
- **The `hrm` database still has no nightly backup.** Every HRM deploy now takes a pre-deploy dump
  (which is new — it previously had none at all), but `hms-backup-1` still dumps only `hms`. A
  restore point that only exists when someone deploys is not a backup schedule. Teach the backup
  loop a second `PGDATABASE`.
- **Nothing scans the built image.** The package scan covers NuGet; the base image's OS packages
  are unexamined. A Trivy/Grype step on the pushed image is the standard next gate.
- **No supply-chain attestation.** Consider build provenance and image signing so the VM can
  verify the image came from this pipeline, not merely from a registry it can reach.
- **The runtime base image floats** on `aspnet:10.0` while the SDK is pinned. That is deliberate —
  runtime patches are security fixes — but it means the image is not bit-reproducible.
- **`.env` is readable by the `deploy` group.** Required for the stack to start, and narrower than
  world-readable, but it is still the database passwords on a box with four tenants.
