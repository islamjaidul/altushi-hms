# 0060 — Every HRM deploy was a no-op that certified itself

- **Status:** Done
- **Date:** 2026-08-06
- **PRD ref:** §8 N6 (a maintenance action must not leave the counter down), §16
- **Parent:** `docs/specs/0053-cicd-pipeline/` — this is a defect in what 0053 built.

## Problem

`deploy-hrm` has been reporting success and changing nothing. `hrm-app-1` on the production box has
run `hrm-app:dev` — a locally-built image of unknown provenance — since long before this was found,
through every green deploy of the HRM SKU.

Two defects compound, and neither is visible from a green pipeline:

**1. `compose.hrm.yml` hardcoded `image: hrm-app:dev`.** `deploy-remote.sh` publishes the registry
ref as `HMS_APP_IMAGE` and re-runs `up -d --no-deps app`. The ERP's `compose.yml` reads
`"${HMS_APP_IMAGE:-hms-app:dev}"` and swaps correctly. The HRM file never interpolated it, so
compose compared the running container against the same `hrm-app:dev` it was already running,
found nothing to do, and printed `Container hrm-app-1 Running`.

**2. `deploy-remote.sh` printed the running digest without ever asserting it.** The rollback path
has always compared `{{.Image}}` against the recorded digest and failed loudly on a mismatch. The
success path only echoed it. So after the no-op, `wait_healthy` got its 200 **from the container
the deploy was trying to replace**, and the script logged
`Healthy on ghcr.io/islamjaidul/altushi-hms/hrm:<sha>` — naming an image that was not running.

Defect 2 is the serious one. Defect 1 is a typo in a YAML file; defect 2 is why nobody found it for
weeks, and it would hide the same failure on the ERP the day `compose.yml` is edited the same way.

## Requirements

- [M] `compose.hrm.yml` must take the deployed image from `HMS_APP_IMAGE`, on the same contract as
  the ERP's `compose.yml`.
- [M] A deploy that leaves the previous container running must **fail**, not pass. Health is not
  proof of deployment.
- [M] The assertion must name the likely cause, because the failure mode is silent and the next
  person to hit it will be reading this message and nothing else.

## Acceptance criteria

1. `deploy-remote.sh` against the HRM SKU replaces the container and the running image ID equals the
   deployed ref's ID.
2. A deploy whose swap silently no-ops exits non-zero with the digests printed.
3. `hrm.specshipper.com` serves an image built by CI from a known commit, not `hrm-app:dev`.
4. The ERP path is unchanged — its compose already interpolated correctly.

## What landed

| Area | Delivered |
|---|---|
| `deploy/compose.hrm.yml` | `image: "${HMS_APP_IMAGE:-hrm-app:dev}"`, with the history in a comment so the bare tag is not restored by someone tidying up. |
| `deploy/deploy-remote.sh` | The success path now inspects `{{.Id}}` of the deployed ref and `{{.Image}}` of the running container and exits 1 when they differ, with a message pointing at the `image:` interpolation. Mirrors the check the rollback path already had. |

## Notes

**Health checks answer the wrong question.** `wait_healthy` asks "is something serving on :8091?"
The deploy needs "is *this image* serving on :8091?" Every layer of 0053's design — the pre-deploy
dump, the rollback point, the forced command on the CI key — assumed the swap had happened. Nothing
checked.

**Found by hand, not by the drill.** `eng/verify/deploy-rollback-drill.sh` exercises the ERP compose
path, where the interpolation is correct, so it was green throughout. A drill that ran both SKUs
would have caught this on the day the HRM compose file was written. Recorded here rather than fixed
now: the drill's scope is a change of its own.

**The `hrm-app:dev` image on the box is still unidentified.** It was built locally at some point and
no commit is recorded against it. Nothing that ran under it can be attributed to a known revision.
