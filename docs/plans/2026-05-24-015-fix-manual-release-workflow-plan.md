---
title: "fix: Manual-only GitHub releases; reset to v2.0.0a1"
type: fix
status: completed
date: 2026-05-24
---

# fix: Manual-only GitHub releases; reset to v2.0.0a1

## Summary

Stop automatic GitHub Releases triggered by Release Please and build-and-release on every `master` push. Retract 2.1.x releases, realign version files to the last intentional publish (`v2.0.0a1`), and document an explicit manual publish flow.

---

## Problem Frame

Release Please ran on every push to `master`, opened version-bump PRs for low-signal commits, and published GitHub Releases on merge. Build-and-release also reacted to `release` and `workflow_run` events, amplifying noise while core product functionality is not release-ready.

---

## Requirements

- R1. No GitHub Release or tag is created automatically from merges to `master`.
- R2. Latest published GitHub release remains `v2.0.0a1`; retracted 2.1.x tags/releases stay deleted.
- R3. Version files (manifest, `MainConfig.cs`, plists) match `2.0.0a1`.
- R4. Release Please may still open version-bump PRs, but only when manually dispatched.
- R5. Build-and-release runs only via `workflow_dispatch`; GitHub Release creation requires explicit input.
- R6. Document the manual release process for maintainers.

---

## Scope Boundaries

- Merging PR #88 into `master` (this plan's delivery vehicle).
- Re-enabling automated releases later (future decision).
- Fixing unrelated open issues or plan-doc churn.

---

## Key Technical Decisions

- **workflow_dispatch only** for both release workflows — standard explicit gate for publishing.
- **`skip-github-release: true`** in Release Please (workflow + config) — version PRs without auto-publish.
- **`create_github_release` input default false** — opt-in publish on build workflow.
- **Hide `docs` commits** in release-please-config — reduces spurious version bumps when Release Please is run manually.

---

## Implementation Units

- U1. **Retract GitHub releases after v2.0.0a1**

**Goal:** Remove erroneous 2.1.x releases and tags via `gh`.

**Requirements:** R2

**Dependencies:** None

**Files:** (GitHub only — no repo files)

**Verification:**
- `gh release list` shows `v2.0.0a1` as newest 2.x pre-release; no v2.1.x entries.

- U2. **Manual-only workflow triggers**

**Goal:** Remove push/release/workflow_run auto-triggers; add dispatch inputs.

**Requirements:** R1, R4, R5

**Dependencies:** None

**Files:**
- Modify: `.github/workflows/release-please.yml`
- Modify: `.github/workflows/build-and-release.yml`

**Test scenarios:**
- Test expectation: none — CI workflow YAML; verified by inspection and post-merge behavior.

**Verification:**
- Neither workflow lists `push`, `release`, or `workflow_run` under `on:`.

- U3. **Version realignment and changelog cleanup**

**Goal:** Reset tracked version to `2.0.0a1`; fold 2.1.x changelog into `[Unreleased]`.

**Requirements:** R3

**Dependencies:** U2

**Files:**
- Modify: `.release-please-manifest.json`
- Modify: `src/KOTORModSync.Core/MainConfig.cs`
- Modify: `Info.plist`, `src/KOTORModSync.GUI/Info.plist`
- Modify: `CHANGELOG.md`
- Modify: `release-please-config.json`

**Test scenarios:**
- Happy path: `ReleaseVersionAlignmentTests.CurrentVersion_MatchesReleasePleaseManifest` passes.

**Verification:**
- Manifest and `MainConfig.CurrentVersion` both read `2.0.0a1`.

- U4. **Release documentation and discoverability**

**Goal:** Maintainer runbook for intentional publishes.

**Requirements:** R6

**Dependencies:** U2

**Files:**
- Create: `docs/manual-release.md`
- Modify: `AGENTS.md` (link to runbook)

**Verification:**
- Runbook describes Release Please dispatch and Build-and-Release with `create_github_release`.

---

## Risks & Dependencies

| Risk | Mitigation |
|------|------------|
| Maintainers forget manual dispatch | `docs/manual-release.md` + AGENTS.md link |
| Old Release Please PRs reopen | Close stale release PRs (#87) |

---

## Sources & References

- PR: #88
- Prior work: retracted releases via `gh release delete --cleanup-tag`
