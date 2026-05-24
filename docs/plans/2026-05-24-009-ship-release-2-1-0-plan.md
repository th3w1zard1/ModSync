---
title: Ship Release Please 2.1.0 release PR
type: chore
status: shipped
date: 2026-05-24
---

# Ship Release Please 2.1.0 release PR

## Summary

Merge bot-generated release PR #81 to complete the Release Please pipeline, bump versions to 2.1.0, and close stale issue #78 (plist xml xpath — fixed in #79).

---

## Problem Frame

Release Please automation is green (plans 006–008). PR #81 (`chore(master): release KOTORModSync 2.1.0`) is open with CHANGELOG and version file updates. Issue #78 remains open despite fix in #79.

---

## Requirements

- R1. Merge PR #81 into `master`.
- R2. Manifest and version files reflect `2.1.0` on `master` after merge.
- R3. Close issue #78 with reference to #79.
- R4. Release Please workflow remains green after merge.

---

## Implementation Units

### U1. Merge release PR #81

Use `gh pr merge 81 --merge` (preserve Release Please commit history) or squash per repo convention.

### U2. Close issue #78

Comment that xml xpath extra-files landed in #79; close issue.

### U3. Record plan shipped

Commit plan status update on `master`.

---

## Verification

- `gh pr view 81` state: MERGED
- `.release-please-manifest.json` contains `"2.1.0"`
- Issue #78 state: CLOSED
