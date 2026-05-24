---
title: Merge PR #84 MainConfig version alignment
type: chore
status: shipped
date: 2026-05-24
---

# Merge PR #84 MainConfig version alignment

## Summary

Land open PR #84 (`fix/align-mainconfig-release-version`) so plan 010 ships to `master`: correct in-app version display and manifest parity test.

---

## Problem Frame

Plan 010 implementation is on branch `fix/align-mainconfig-release-version` as PR #84. CI must pass before merge. Release Please may open follow-up PR #83 (2.1.1) after merge — handle separately if needed.

---

## Requirements

- R1. PR #84 CI checks succeed (or failures fixed on branch).
- R2. Merge PR #84 into `master`.
- R3. `MainConfig.CurrentVersion` is `2.1.0` on `master` after merge.
- R4. `ReleaseVersionAlignmentTests` present on `master`.
- R5. Plan 010 and 011 marked shipped in `docs/plans/`.

---

## Implementation Units

### U1. Verify CI and fix if needed

Poll `gh pr checks 84`. If failures, fix on branch and push.

### U2. Merge PR #84

`gh pr merge 84 --squash` or `--merge` per repo convention (squash typical for fix PRs).

### U3. Record plans shipped

Commit plan status updates on `master` after pull.

---

## Verification

- `gh pr view 84` state: MERGED
- `git show master:src/KOTORModSync.Core/MainConfig.cs` contains `2.1.0`
- `dotnet test --filter FullyQualifiedName~ReleaseVersionAlignment` on master

---

## Scope Boundaries

- Does not merge Release Please PR #83 in this plan (optional follow-up).
- Does not change release-please-config beyond what PR #84 already contains.
