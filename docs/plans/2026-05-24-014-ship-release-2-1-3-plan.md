---
title: Ship Release Please 2.1.3 release PR
type: chore
status: shipped
date: 2026-05-24
---

# Ship Release Please 2.1.3 release PR

## Summary

Merge bot-generated release PR #86 to publish patch release 2.1.3 (documents plan 013) and keep manifest, plists, and `MainConfig.CurrentVersion` aligned.

---

## Problem Frame

After shipping 2.1.2 and committing plan 013, Release Please opened PR #86 with CHANGELOG and version bumps for 2.1.3. The release branch must stay mergeable with `master` and pass parity checks.

---

## Requirements

- R1. Merge PR #86 into `master` (update branch if stale).
- R2. `.release-please-manifest.json` and version files reflect `2.1.3` after merge.
- R3. `MainConfig.CurrentVersion` matches manifest (no stale alpha suffix).
- R4. `ReleaseVersionAlignmentTests` passes on `master`.
- R5. Release Please workflow green after merge.
- R6. Plan marked shipped in `docs/plans/`.

---

## Implementation Units

### U1. Refresh and merge PR #86

Use `gh pr update-branch 86` if needed, then `gh pr merge 86 --merge`.

### U2. Verify version parity on master

Run alignment test; confirm manifest and MainConfig.

### U3. Record plan shipped

Commit plan status on `master`.

---

## Verification

- `gh pr view 86` state: MERGED
- Manifest `"."` is `2.1.3`
- `dotnet test --filter FullyQualifiedName~ReleaseVersionAlignment`

---

## Scope Boundaries

- Does not tackle open user issues (#32, #50, #52, #53).
- Does not merge draft PRs #69/#70.
