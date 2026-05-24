---
title: Fix Release Please PR creation permissions
type: fix
status: shipped
date: 2026-05-24
---

# Fix Release Please PR creation permissions

## Summary

Release Please config parse is fixed (PR #79), but the workflow still fails at PR creation with `GitHub Actions is not permitted to create or approve pull requests`. Enable repository workflow permission `can_approve_pull_request_reviews` (and document the setting).

---

## Problem Frame

After merging #79, Release Please progresses through commit tree creation but fails when opening the release PR. Repo API shows `can_approve_pull_request_reviews: false`.

---

## Requirements

- R1. Repository Actions workflow permissions allow GITHUB_TOKEN to create release PRs.
- R2. `.github/workflows/release-please.yml` retains `pull-requests: write` and `contents: write`.
- R3. Release Please workflow completes successfully on next `master` push (creates or updates release PR).

---

## Implementation Units

### U1. Enable repo workflow PR permission via GitHub API

Set `can_approve_pull_request_reviews: true` on `repos/th3w1zard1/ModSync/actions/permissions/workflow`.

### U2. Document setting in workflow comment (optional)

Add plate comment in `release-please.yml` pointing to required repo setting if disabled.

### U3. Verify Release Please run green

Re-run workflow or push empty commit to trigger.

---

## Verification

- `gh api repos/th3w1zard1/ModSync/actions/permissions/workflow` shows `can_approve_pull_request_reviews: true`
- Latest Release Please run succeeds (no PR permission error)
