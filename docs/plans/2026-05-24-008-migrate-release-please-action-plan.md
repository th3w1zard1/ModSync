---
title: Migrate Release Please GitHub Action to googleapis org
type: fix
status: shipped
date: 2026-05-24
---

# Migrate Release Please GitHub Action to googleapis org

## Summary

Replace deprecated `google-github-actions/release-please-action@v4` with maintained `googleapis/release-please-action@v4` to remove CI deprecation warnings and stay on the supported action path.

---

## Problem Frame

Release Please runs successfully after plans 006–007, but workflow annotations warn that `google-github-actions/release-please-action` is deprecated and runs on Node.js 20. Development moved to `googleapis/release-please-action`.

---

## Requirements

- R1. `.github/workflows/release-please.yml` uses `googleapis/release-please-action@v4`.
- R2. Existing inputs (`token: GITHUB_TOKEN`) and manifest config unchanged.
- R3. Release Please workflow succeeds on next run (no deprecation annotation for old action).

---

## Implementation Units

### U1. Swap action reference

**Files:** `.github/workflows/release-please.yml`

Change `uses:` from `google-github-actions/release-please-action@v4` to `googleapis/release-please-action@v4`.

### U2. Verify CI

Push branch, confirm Release Please job passes (or re-run workflow).

---

## Verification

- Grep shows no remaining `google-github-actions/release-please-action` references
- Release Please workflow run conclusion: success
