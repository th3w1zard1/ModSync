---
title: Fix install coordinator cleanup on Windows CI
type: fix
status: completed
date: 2026-05-23
---

# Fix install coordinator cleanup on Windows CI

## Summary

Stabilize the Windows `InstallCoordinatorTests` teardown path so the shared pipeline tests stop failing with `UnauthorizedAccessException` while deleting `.modsync`. The plan fixes the real resource-lifetime bug instead of weakening teardown behavior: release the checkpoint repository deterministically, align cleanup helpers with the actual checkpoint path layout, and add regression coverage around the production path that CI already exercises.

---

## Problem Frame

`Build and Test KOTORModSync` is red on `master` because the `InstallCoordinatorTests` filter fails in teardown on `windows-latest` while deleting temporary `.modsync` folders. The current helper already retries deletion and attempts Git-specific cleanup, which means the remaining failures point to a real ownership/path mismatch rather than a simple timing hiccup.

---

## Assumptions

*This plan was authored without synchronous user confirmation. The items below are agent inferences that fill gaps in the input — un-validated bets that should be reviewed before implementation proceeds.*

- The next highest-value LFG target is the red default-branch CI rather than a new feature request.
- The failing Windows teardown is caused primarily by undisposed `LibGit2Sharp.Repository` handles in the real `InstallCoordinator` path, not by a workflow-only environment quirk.
- The right fix should preserve the current `InstallCoordinatorTests` CI filter instead of changing the workflow to avoid the failing coverage.

---

## Requirements

- R1. `InstallCoordinatorTests` running on Windows CI should stop failing teardown with `UnauthorizedAccessException` while deleting temporary `.modsync` state.
- R2. Cleanup should target the real checkpoint repository layout under `.modsync/checkpoints` and release live Git handles deterministically instead of relying only on retries and garbage collection.
- R3. The production/test ownership path for checkpoint resources should be explicit enough that `InstallCoordinator`-driven flows can dispose or clear them without leaking handles across teardown.
- R4. Regression coverage should exercise the failing production path or the exact teardown helper path used by the shared pipeline tests, not only adjacent cleanup-only tests.
- R5. The fix must repair the actual cleanup bug without weakening assertions, reducing workflow coverage, or masking failures behind broad exception swallowing.

---

## Scope Boundaries

- No GUI, wizard, or `telemetry-auth/` changes.
- No checkpoint-system redesign beyond the cleanup/disposal behavior needed to stop the Windows teardown failure.
- No workflow-filter changes unless implementation proves a targeted regression test must be added to the existing `InstallCoordinatorTests` path.

### Deferred to Follow-Up Work

- Broader audit of other test suites that create temp checkpoint repositories but are not part of the shared pipeline filter.
- README/build-doc cleanup for the repo's lingering .NET 8 vs .NET 9 wording mismatch.

---

## Context & Research

### Relevant Code and Patterns

- `src/KOTORModSync.Core/Installation/InstallCoordinator.cs` creates `CheckpointManager` and `GitCheckpointService`, but `InstallCoordinator` itself is not disposable and `ClearSessionForTests` only attempts a raw recursive delete of `.modsync`.
- `src/KOTORModSync.Core/Services/GitCheckpointService.cs` initializes the repository under `CheckpointPaths.GetCheckpointsRoot(gameDirectory)`, which resolves to `.modsync/checkpoints`, and exposes `Dispose()` plus `ClearAllCheckpointsAsync()`.
- `src/KOTORModSync.Core/Services/Checkpoints/CheckpointPaths.cs` is the authoritative layout for `.modsync`, `checkpoints`, session state, and backup artifacts.
- `src/KOTORModSync.Core/Services/InstallationService.cs` creates an `InstallCoordinator` and uses `coordinator.CheckpointService.CreateCheckpointAsync(...)`, but does not currently dispose the coordinator/checkpoint service afterward.
- `src/KOTORModSync.Tests/InstallCoordinatorTestsHelper.cs` centralizes teardown cleanup, but its Git-specific cleanup logic currently assumes `.modsync/.git` instead of the real `.modsync/checkpoints/.git` location.
- `src/KOTORModSync.Tests/InstallCoordinatorTests.cs` is the exact suite exercised by `.github/workflows/build-and-test.yml` in the failing `Run shared pipeline tests` step.
- `src/KOTORModSync.Tests/GitCheckpointCleanupTests.cs` already covers adjacent cleanup scenarios and is the closest existing pattern for checkpoint-disposal regression coverage.

### Institutional Learnings

- There is no `docs/solutions/` history for this area; the useful institutional knowledge currently lives in the test suite and the failing CI logs.
- The repo already recognized Git-lock cleanup as a test concern and added dedicated cleanup tests, so the plan should extend those ideas into the real `InstallCoordinator` path instead of replacing them with looser teardown semantics.
- The highest-confidence repo-local clue is a path mismatch: helper cleanup looks for `.modsync/.git`, while the actual repository lives in `.modsync/checkpoints/.git`.

### External References

- None. The codebase and CI logs already expose the failure shape and the intended checkpoint layout.

---

## Key Technical Decisions

- Make resource cleanup deterministic in the real install/coordinator path rather than adding more retry-only behavior. The Windows failure is a handle-lifetime problem first and a delete-retry problem second.
- Use `CheckpointPaths` as the single source of truth for cleanup paths so the helper and core cleanup code cannot drift from the actual `.modsync/checkpoints` layout again.
- Keep teardown failures meaningful. The fix should release the real repository handles; it should not "solve" the issue by suppressing the final teardown exception when cleanup is still genuinely broken.
- Preserve the existing workflow scope. The shared pipeline should keep running `InstallCoordinatorTests`; the code should become reliable under that filter.

---

## Open Questions

### Resolved During Planning

- Should this be treated as a workflow-only flake? No — repeated `master` failures and the repo-path mismatch point to a real cleanup bug in code.
- Is the current helper path authoritative for `.git` cleanup? No — `CheckpointPaths` and `GitCheckpointService` show the authoritative repository path is `.modsync/checkpoints/.git`.

### Deferred to Implementation

- Whether `InstallCoordinator` should become `IDisposable`, expose an explicit cleanup method, or both to make checkpoint-service lifetime obvious in tests and in `InstallationService`.
- Whether the best regression belongs directly in `InstallCoordinatorTests`, in `GitCheckpointCleanupTests`, or in both once the implementation shape is clear.

---

## Implementation Units

### U1. Make checkpoint-service lifetime explicit in the install path

**Goal:** Ensure the real `InstallCoordinator` / `InstallationService` path can deterministically release checkpoint repository handles before test teardown or post-install cleanup runs.

**Requirements:** R1, R2, R3, R5

**Dependencies:** None

**Files:**
- Modify: `src/KOTORModSync.Core/Installation/InstallCoordinator.cs`
- Modify: `src/KOTORModSync.Core/Services/InstallationService.cs`
- Modify: `src/KOTORModSync.Core/Services/GitCheckpointService.cs`
- Test: `src/KOTORModSync.Tests/InstallCoordinatorTests.cs`

**Approach:**
- Trace the ownership boundary for `CheckpointService` and make disposal explicit instead of relying on garbage collection.
- Update the coordinator/install flow so the `LibGit2Sharp.Repository` held by `GitCheckpointService` is disposed after the install/checkpoint lifecycle that created it is complete.
- Keep the public behavior of installation/checkpoint creation intact while tightening cleanup semantics.

**Execution note:** Add characterization coverage around the current teardown-sensitive path before changing disposal behavior.

**Patterns to follow:**
- `src/KOTORModSync.Core/Services/GitCheckpointService.cs` disposal boundary
- `src/KOTORModSync.Tests/GitCheckpointCleanupTests.cs` for checkpoint-handle cleanup expectations

**Test scenarios:**
- Happy path: an `InstallCoordinator`-driven initialization/checkpoint flow can complete and then release checkpoint resources without leaving `.modsync/checkpoints/.git` locked.
- Error path: cleanup/disposal after a partial install or checkpoint-save path still releases repository handles before teardown.
- Integration: the real `InstallationService` path that creates and uses `InstallCoordinator.CheckpointService` can finish and leave the temp working directory deletable.

**Verification:**
- The production install/checkpoint path has an explicit cleanup boundary, and the failing Windows teardown no longer depends on GC timing to delete `.modsync`.

### U2. Align test cleanup helpers with the real checkpoint layout

**Goal:** Make shared teardown logic clean up the actual checkpoint repository path and related files created by the core checkpoint system.

**Requirements:** R1, R2, R5

**Dependencies:** U1

**Files:**
- Modify: `src/KOTORModSync.Tests/InstallCoordinatorTestsHelper.cs`
- Modify: `src/KOTORModSync.Core/Services/Checkpoints/CheckpointPaths.cs`
- Test: `src/KOTORModSync.Tests/GitCheckpointCleanupTests.cs`

**Approach:**
- Replace helper assumptions about `.modsync/.git` with the authoritative path helpers from `CheckpointPaths`.
- Keep targeted retry/attribute-reset behavior where it is still useful, but make it operate on the real `.modsync/checkpoints/.git` object store.
- Reconcile the helper's intent/comments with its actual final-failure behavior so teardown semantics stay deliberate.

**Patterns to follow:**
- `src/KOTORModSync.Core/Services/Checkpoints/CheckpointPaths.cs` for root/checkpoint/session path derivation
- Existing retry/backoff structure in `src/KOTORModSync.Tests/InstallCoordinatorTestsHelper.cs`

**Test scenarios:**
- Happy path: helper cleanup deletes a temp directory containing `.modsync/checkpoints/.git` after a repository-backed checkpoint run.
- Edge case: cleanup still handles read-only or nested Git object files under `.modsync/checkpoints/.git/objects`.
- Error path: if cleanup cannot remove a locked file after all retries, the failure remains explicit instead of being silently swallowed.
- Integration: `GitCheckpointCleanupTests` use the same helper path as `InstallCoordinatorTests` and pass against the actual checkpoint directory structure.

**Verification:**
- Helper cleanup targets the same path layout the core checkpoint system creates, and the regression coverage proves that alignment.

### U3. Add regression coverage on the CI-filtered install-coordinator path

**Goal:** Protect the exact failure shape that breaks the shared pipeline tests so future cleanup regressions surface before they hit `master`.

**Requirements:** R1, R4, R5

**Dependencies:** U1, U2

**Files:**
- Modify: `src/KOTORModSync.Tests/InstallCoordinatorTests.cs`
- Modify: `src/KOTORModSync.Tests/GitCheckpointCleanupTests.cs`
- Modify: `.github/workflows/build-and-test.yml`

**Approach:**
- Prefer assertions inside `InstallCoordinatorTests` that exercise the same init/install + teardown path already selected by the CI filter.
- Only touch the workflow if implementation shows the current filter misses the new regression test name; keep the workflow scope otherwise unchanged.
- Verify that the new regression would have failed under the old path/disposal behavior before accepting the fix.

**Patterns to follow:**
- Existing `InstallCoordinatorTests` temp-directory setup/teardown shape
- `GitCheckpointCleanupTests` assertions around deletable checkpoint directories after disposal

**Test scenarios:**
- Happy path: the `InstallCoordinatorTests` subset executed by CI passes after creating checkpoints and tearing down temp directories on Windows-style paths.
- Edge case: repeated coordinator initialization in the same temp directory lifecycle does not reintroduce a locked checkpoint repo between runs.
- Integration: the regression test covers the end-to-end sequence that previously failed in CI—checkpoint creation, session persistence, teardown helper cleanup, and temp-directory deletion.

**Verification:**
- The shared pipeline `InstallCoordinatorTests` filter passes on the fixed branch, and the added regression would fail if the disposal/path fix were removed.

---

## System-Wide Impact

- **Interaction graph:** `InstallCoordinator`, `InstallationService`, `GitCheckpointService`, `CheckpointManager`, and the shared test cleanup helper all participate in the same temp-directory lifecycle.
- **Error propagation:** Cleanup should still surface real disposal failures, but the normal success path should no longer leak repository handles into teardown.
- **State lifecycle risks:** Checkpoint commits, backup zips, and session JSON must remain valid until cleanup runs; disposal changes must not break checkpoint persistence or resume behavior.
- **API surface parity:** Any disposal/cleanup contract added to `InstallCoordinator` should be used consistently by both tests and the production install path.
- **Integration coverage:** The critical proof is the real `InstallCoordinatorTests` CI filter on Windows, not only isolated helper tests.
- **Unchanged invariants:** This fix must not change checkpoint semantics, install ordering, or backup/session persistence behavior outside of cleanup reliability.

---

## Risks & Dependencies

| Risk | Mitigation |
|------|------------|
| Disposal changes accidentally break checkpoint persistence or resume state | Keep regression coverage around checkpoint creation/state persistence while introducing cleanup ownership |
| Helper cleanup is "fixed" by swallowing the final teardown failure instead of releasing handles | Preserve explicit failure behavior and require regression tests to prove the directory becomes deletable |
| Workflow tweaks drift away from the real failing path | Keep `.github/workflows/build-and-test.yml` changes minimal and only adjust filters if the new regression name truly requires it |

---

## Documentation / Operational Notes

- No user-facing docs are required for the bug fix itself.
- If the final implementation exposes a stable cleanup/disposal contract on `InstallCoordinator`, add a short code comment or test-level note where future maintainers will encounter it first.

---

## Sources & References

- Related code: `src/KOTORModSync.Core/Installation/InstallCoordinator.cs`
- Related code: `src/KOTORModSync.Core/Services/GitCheckpointService.cs`
- Related code: `src/KOTORModSync.Core/Services/Checkpoints/CheckpointPaths.cs`
- Related code: `src/KOTORModSync.Tests/InstallCoordinatorTestsHelper.cs`
- Related code: `src/KOTORModSync.Tests/InstallCoordinatorTests.cs`
- Related code: `src/KOTORModSync.Tests/GitCheckpointCleanupTests.cs`
- Related workflow: `.github/workflows/build-and-test.yml`
- Related PR: `th3w1zard1/ModSync#71`
- Related CI run: `https://github.com/th3w1zard1/ModSync/actions/runs/25337662870`
