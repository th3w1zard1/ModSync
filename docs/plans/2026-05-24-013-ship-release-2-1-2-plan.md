---
title: Ship Release Please 2.1.2 release PR
type: chore
status: shipped
date: 2026-05-24
---

# Ship Release Please 2.1.2 release PR

## Summary

Merge bot-generated release PR #85 to publish patch release 2.1.2 (documents plan 012) and keep manifest, plists, and `MainConfig.CurrentVersion` aligned.

---

## Problem Frame

After shipping 2.1.1 and committing plan 012, Release Please opened PR #85 with CHANGELOG and version bumps for 2.1.2. The release branch must stay mergeable with `master` and pass parity checks.

---

## Requirements

- R1. Merge PR #85 into `master` (update branch if stale).
- R2. `.release-please-manifest.json` and version files reflect `2.1.2` after merge.
- R3. `MainConfig.CurrentVersion` matches manifest (no stale alpha suffix).
- R4. `ReleaseVersionAlignmentTests` passes on `master`.
- R5. Release Please workflow green after merge.
- R6. Plan marked shipped in `docs/plans/`.

---

## Implementation Units

### U1. Refresh and merge PR #85

Use `gh pr update-branch 85` if needed, then `gh pr merge 85 --merge`.

### U2. Verify version parity on master

Run alignment test; confirm manifest and MainConfig.

### U3. Record plan shipped

Commit plan status on `master`.

---

## Verification

- `gh pr view 85` state: MERGED
- Manifest `"."` is `2.1.2`
- `dotnet test --filter FullyQualifiedName~ReleaseVersionAlignment`

---

## Scope Boundaries

- Does not tackle open user issues (#32, #50, #52, #53).
- Does not merge draft PRs #69/#70.
