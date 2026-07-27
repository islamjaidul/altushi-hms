# Upgrade-path gate (ADR-0022)

`prev-release.sql` is a `pg_dump` of the **previous release's** demo database — schema plus the
golden-thread/verification data, never production data. `run.sh` restores it, boots the current
build over it (running migrations and seed-upgraders), smokes every route over the old records,
and runs the dirty-database-tolerant money workflow (`discount-and-dues.py`) end to end.

This is the gate that would have caught the reference-band defect (spec 0013, commit
`095f552`): result templates written by an older release deserialised with a null band list and
result entry 500'd — but only on the deployed instance, because every local run started from a
fresh database.

## Refreshing the fixture (at each release cut)

```sh
# from a database freshly built + verified by the release being cut:
docker exec hms-dev-db pg_dump -U postgres --no-owner --no-privileges hms > eng/verify/upgrade/prev-release.sql
```

Commit the new dump together with the release tag. Keep exactly one previous release —
customers update release-to-release (single-VM deployments, `deploy/RUNBOOK.md` §4); deeper
chains guard nothing real.

## Notes

- `golden-thread.py` asserts absolute figures (e.g. today's income is exactly ৳550), so it is
  fresh-database-only; `discount-and-dues.py` asserts relative outcomes and is the workflow
  probe used here. If golden-thread's assertions are ever made relative, add it to `run.sh`.
- Locally the gate needs port 5199 free and the `hms-dev-db` container running.
