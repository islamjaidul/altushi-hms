# 0053 — A CI/CD pipeline that is green when the product is good, and deploys when it is

- **Status:** Draft
- **Date:** 2026-08-06
- **PRD ref:** §8 N6 (availability — maintenance must not block the counter), §8 N3 (data safety), §16 (single-VM constraint)
- **Scope:** defect repair + engineering infrastructure. No §5 module requirement changes; this is the delivery pipeline that ships them.

## Problem

Three things are wrong with delivery today, and the first has made the other two invisible.

**The pipeline is red and nobody reads it any more.** `ci` has failed on every push to `main`
for at least the last three commits (`d85cbba`, `a3a46ea`, `ccb9d4b`). The owner receives a
failure email per push and has stopped acting on them. A gate that is always red is not a gate:
the `build-test` job carries the money tests, the guard scripts and the additive-migration check
that hard rule 4 depends on, and right now none of them are believed. This is the defect.

**The pipeline is red for reasons unrelated to the product.** `a3a46ea` is titled "Graphify cache
updated" — a commit touching only `graphify-out/`. It ran the whole .NET build, all five test
projects, Testcontainers, and a Docker image build. Documentation and spec commits do the same.
Most of the noise in the owner's inbox is the pipeline reporting on commits it has no opinion about.

**There is no CD at all.** `main` going green does nothing. Deployment is a human at a terminal
running `git pull && docker compose build && up -d` (RUNBOOK §10), which compiles a 34-project
solution **on the hospital VM** — 2 vCPU / 3 GB, already into swap with four products on it (§16).
That build competes with the running app for the RAM the counter needs, takes minutes rather than
the sub-minute swap §8 N6 requires, and can leave the box wedged with no automatic way back. The
deploy that touches real patient and financial records is the least rehearsed step we have.

## Requirements

- [M] `ci` is green on `main` — the existing gates pass on their own merits, not by being removed
      or marked continue-on-error. A gate that cannot be made to pass is reported, not deleted.
- [M] A commit that changes only `docs/`, `graphify-out/`, or top-level `*.md` does not run the
      .NET build, the test projects, or the image build, and does not produce a failure email.
- [M] The app image is built **once**, on CI hardware, and the VM never compiles anything. The
      hospital VM's RAM belongs to the running product (§16).
- [M] Deployment to the VM happens only from a commit whose full `ci` gate passed, and only after
      an explicit human approval. A green build does not by itself reach the hospital.
- [M] A deploy that leaves the app unhealthy is detected by the pipeline and rolled back to the
      previously running image without a human at a terminal (§8 N6).
- [M] No credential — SSH key, registry token, database password — is written into the repository
      or into a workflow file. (§8 N5, and `security-guardrails`.)
- [S] Deploy takes the database backup described in RUNBOOK §5 before it swaps the image, so the
      rollback point is a fact and not an assumption (§8 N3).
- [S] CI reports which gate failed without the owner opening a browser — the failing step is named
      in the run summary.
- [C] Restore lock-file and NuGet caching so the feedback loop is short enough to be used.

## Acceptance criteria

1. A push to `main` touching `src/` runs the full gate and reports green on a commit known good;
   the run summary names each gate that ran.
2. A push touching only `graphify-out/` completes without running `dotnet build` and reports
   success — verified by pushing exactly such a commit and reading the run.
3. `ghcr.io/islamjaidul/altushi-hms/app:<sha>` exists after a green `main` run, and
   `docker image inspect` on the VM after a deploy reports that same digest.
4. The deploy job sits in a `waiting` state until approved, and the VM is unchanged while it waits
   — verified by checking the running container's image digest during the wait.
5. Stopping the app's health endpoint from returning 200 during a deploy causes the pipeline to
   restore the prior image tag, and `/health` returns 200 afterwards, with the run marked failed.
6. `git grep` over the workflow files finds no key material, and the deploy authenticates using
   repository secrets only.
7. The SDK version CI restores with is pinned, so `--locked-mode` cannot fail from a runner-image
   change rather than a dependency change.

## Out of scope

- Changing what the existing gates *assert*. The guard scripts, money tests, additive-migration
  check and upgrade-path job keep their current meaning; this spec makes them run and be believed.
- The HRM SKU's deploy path (`compose.hrm.vm.yml`). ERP first; HRM follows the same shape once
  this is proven, recorded as a follow-up.
- Blue/green or zero-downtime deployment. §8 N6's sub-minute swap is met by a fast image pull and
  container replace, per RUNBOOK §4 — a second live instance does not fit in 3 GB.
- Any change to the production database, its data, or the go-live switch (RUNBOOK §9).

## Risks / open questions

- **The pipeline cannot be validated locally.** The .NET SDK is not installed on the maintainer's
  machine, so every fix to the build gate is proven only by pushing. Recommended default: fix
  forward on `main` in small commits, since `main` is already red and cannot be made worse — and
  the CI is the only environment that can render a verdict.
- **The VM is shared.** 103.132.96.250 runs four products. A deploy that restarts the wrong
  compose project takes down someone else's. Recommended default: the deploy job addresses the
  ERP project by explicit `-p`/`-f` file set and never runs a bare `docker compose down`.
- **Registry visibility.** A private GHCR package needs the VM to hold a read token. Recommended
  default: private package + a read-only token in the VM's docker config, since the image contains
  the hospital's entitlement files.
- **First deploy is the risky one.** Recommended default: rehearse against the demo/HRM stack or a
  scratch compose project on the same VM before pointing it at the ERP.
