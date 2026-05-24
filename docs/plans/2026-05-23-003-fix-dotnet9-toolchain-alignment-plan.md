---
title: Align repo toolchain on .NET 9
type: fix
status: completed
date: 2026-05-23
---

# Align repo toolchain on .NET 9

## Summary

Pin the repository's SDK selection and align the remaining build/release/documentation surfaces with the .NET 9 baseline already used by the main build/test workflows and local GUI guidance. The plan keeps the current `net48` legacy release target intact while removing repo-wide ambiguity about which SDK and target family contributors and automation should use.

---

## Problem Frame

The repo currently sends mixed toolchain signals. Core contributor guidance, the main build/test workflows, and the local desktop runbook all say ".NET 9", but there is no `global.json`, `build-and-release.yml` still advertises `8.0.x` / `net8.0`, `mod-build-validation.yml` sets up 8.0 while restoring `net9.0`, `dotnet-desktop.yml` is still on 8.0, and `README.md` still describes NET8-supported builds. That drift makes local setup fragile and makes workflow behavior harder to reason about than it should be.

---

## Assumptions

*This plan was authored without synchronous user confirmation. The items below are agent inferences that fill gaps in the input — un-validated bets that should be reviewed before implementation proceeds.*

- The next highest-value LFG slice is repo-wide .NET toolchain alignment rather than the one-off `AttachDevTools` symptom, because current research points to broader 8.0 vs 9.0 drift across active workflows and docs.
- The active product/runtime baseline should remain `.NET 9` for `net9.0` builds while preserving existing `net48` publishing support for legacy Windows release outputs.
- The right fix is narrow: pin the SDK and align current workflow/doc surfaces, not a broader Avalonia/package-version migration.

---

## Requirements

- R1. Repository-local CLI builds should resolve to a pinned .NET 9 SDK instead of whichever system SDK happens to be first on `PATH`.
- R2. Active GitHub workflows that build, restore, test, or publish `net9.0` targets should use matching .NET 9 setup values and labels.
- R3. Developer-facing docs should stop instructing contributors to build/run NET8 artifacts when the repo's maintained baseline is .NET 9.
- R4. The fix must preserve current `net48` release/publishing support and avoid unrelated package or target-framework migrations.
- R5. The fix should stay focused on toolchain/runtime-selection drift; it should not turn into a repo-wide GUI or Avalonia refactor.

---

## Scope Boundaries

- No Avalonia package upgrades or diagnostics/devtools refactors.
- No `csproj` target-framework migration beyond pinning and aligning the already-declared `.NET 9` baseline.
- No changes to unrelated CI logic, secrets, signing, or release packaging behavior beyond version-selection consistency.

### Deferred to Follow-Up Work

- Audit whether `dotnet-desktop.yml` should be retired entirely instead of kept aligned, if the repo decides it is legacy and no longer needed.
- Broader documentation cleanup outside the build/runtime guidance areas touched by this plan.

---

## Context & Research

### Relevant Code and Patterns

- `.github/workflows/build-and-test.yml`, `.github/workflows/lint.yml`, `.github/workflows/code-cleanup.yml`, and `.github/workflows/push-integration.yml` already use `9.0.x`, which is the strongest current workflow baseline.
- `.github/workflows/build-and-release.yml` still sets `DOTNET_VERSION: "8.0.x"` and `DOTNET_VERSION_SHORT: "net8.0"` while publishing modern non-`net48` release targets.
- `.github/workflows/mod-build-validation.yml` installs `8.0.x` but restores `KOTORModSync.sln /p:TargetFramework=net9.0`, which is internally inconsistent.
- `.github/workflows/dotnet-desktop.yml` still installs `8.0.x` even though current repo guidance says `.NET 9`.
- `AGENTS.md` and `docs/local_desktop_agent_runbook.md` both describe the active developer baseline as `.NET SDK 9.0.x`.
- `README.md` still says supported modern builds are "NET8" and lists NET8 as a build prerequisite.
- The repo currently has no `global.json`, so local SDK selection is unpinned.

### Institutional Learnings

- `docs/plans/2026-05-23-002-fix-install-coordinator-cleanup-plan.md` already captured a nearby repo truth: repeated build/test drift should be treated as real code/config mismatch, not as a flaky environment issue to paper over.
- `AGENTS.md` and `docs/local_desktop_agent_runbook.md` are the maintained sources of truth for local GUI/build guidance; generated or stale docs should not override them.
- No `docs/solutions/` history exists for this issue class, so the strongest current evidence is in the live workflows, README, and agent/runbook guidance.

### External References

- None. The active repo workflows and guidance already expose the drift clearly enough to plan the fix without external docs.

---

## Key Technical Decisions

- Add a repo-root `global.json` to make local SDK selection explicit instead of relying on ambient machine state.
- Treat the main `.NET 9` workflow set as the baseline to align toward, not the older NET8 wording in `README.md` and release workflows.
- Preserve `net48` publishing as an explicit exception rather than collapsing all outputs to a single runtime family.
- Keep the fix surface small and declarative: version pins, workflow metadata/restore targets, and build guidance text only.

---

## Open Questions

### Resolved During Planning

- Should this plan target a dialog-specific Avalonia fix? No — current repo evidence shows broader .NET 8 vs 9 drift, and the dialog symptom is not a strong enough standalone planning target.
- Should this plan change project target frameworks? No — it should align selection and workflow config around the current declared targets, not migrate them.

### Deferred to Implementation

- Whether `dotnet-desktop.yml` should be modernized in place or left untouched if implementation confirms it is truly obsolete and not used in practice.
- Which .NET 9 SDK patch version to pin in `global.json`, based on the repo's currently validated workflow/tooling baseline.

---

## Implementation Units

### U1. Pin repo-local SDK selection to the .NET 9 baseline

**Goal:** Make local CLI/build behavior deterministic by committing the SDK family the repo already expects contributors and agents to use.

**Requirements:** R1, R4

**Dependencies:** None

**Files:**
- Create: `global.json`
- Modify: `.github/copilot-instructions.md`
- Modify: `AGENTS.md`

**Approach:**
- Add a repo-root SDK pin that selects the intended .NET 9 toolchain without affecting the existing `net48` publish target.
- Align any repo guidance that still implies "ambient SDK selection" so the pinned SDK becomes the unambiguous local build path.
- Keep the scope to SDK selection only; do not change project target frameworks in this unit.

**Execution note:** Start with characterization of the current version surfaces (`9.0.x` guidance vs no `global.json`) before choosing the pinned SDK patch.

**Patterns to follow:**
- Existing repo-root guidance in `.github/copilot-instructions.md` and `AGENTS.md`
- Current `.NET 9` usage in `.github/workflows/build-and-test.yml`

**Test scenarios:**
- Test expectation: none -- config/guidance-only unit. Validate by confirming the repo declares a single local SDK family and that guidance references the pinned baseline consistently.

**Verification:**
- The repo contains an explicit SDK pin, and contributor/agent guidance no longer depends on whichever system SDK appears first on `PATH`.

### U2. Align active workflow setup with modern `net9.0` builds

**Goal:** Remove the remaining 8.0-vs-9.0 mismatches from active workflows that restore, build, test, or publish `net9.0` targets.

**Requirements:** R2, R4, R5

**Dependencies:** U1

**Files:**
- Modify: `.github/workflows/build-and-release.yml`
- Modify: `.github/workflows/mod-build-validation.yml`
- Modify: `.github/workflows/dotnet-desktop.yml`

**Approach:**
- Update workflow setup-dotnet versions, target-framework labels, and restore/publish wording so they match the repo's active `.NET 9` baseline where those workflows build `net9.0`.
- Preserve the explicit `net48` release branch inside `build-and-release.yml`; only the modern SDK/runtime path should move.
- If a workflow turns out to be intentionally legacy, document that explicitly or defer its retirement rather than silently keeping contradictory values.

**Patterns to follow:**
- `.github/workflows/build-and-test.yml`
- `.github/workflows/lint.yml`
- `.github/workflows/code-cleanup.yml`

**Test scenarios:**
- Test expectation: none -- workflow/config-only unit. Validate by checking that each touched workflow now installs the same SDK family it restores/builds for and that `net48` handling remains explicit where required.

**Verification:**
- The touched workflows no longer mix `8.0.x` setup with `net9.0` restore/build paths, and the release workflow still clearly preserves the `net48` exception.

### U3. Update README build/runtime guidance to match the repo baseline

**Goal:** Bring the public contributor/runtime guidance back in line with the repo's current .NET 9 reality.

**Requirements:** R3, R4, R5

**Dependencies:** U1, U2

**Files:**
- Modify: `README.md`

**Approach:**
- Replace outdated NET8 language in supported-platform and build-instruction sections with the modern `.NET 9` baseline.
- Clarify the distinction between the legacy Windows `.NET Framework 4.8` release path and the modern self-contained `.NET 9` builds for current platforms.
- Keep this unit focused on build/runtime truth, not broader README reorganization.

**Patterns to follow:**
- Current repo baseline in `AGENTS.md`
- Current local desktop workflow in `docs/local_desktop_agent_runbook.md`

**Test scenarios:**
- Test expectation: none -- documentation-only unit. Validate by checking that README build/runtime statements no longer contradict the repo's maintained `.NET 9` guidance.

**Verification:**
- README no longer advertises NET8 as the active modern build/runtime baseline, and the documented platform/build story matches the workflows and repo guidance.

---

## System-Wide Impact

- **Interaction graph:** Affects local contributors, coding agents, and GitHub Actions workflow runners rather than end-user runtime behavior.
- **Error propagation:** The main failure mode is build/config confusion; aligning the toolchain should reduce spurious local build failures and mismatched workflow setup.
- **State lifecycle risks:** Minimal runtime risk; the main safety requirement is preserving the `net48` release branch and existing publish packaging behavior.
- **API surface parity:** No public product/API behavior changes; parity concern is between local CLI guidance, CI workflows, and release automation.
- **Integration coverage:** Validation should confirm the touched workflow surfaces and local build guidance point to the same SDK family.
- **Unchanged invariants:** Existing project target frameworks, release packaging layout, and legacy Windows support remain unchanged.

---

## Risks & Dependencies

| Risk | Mitigation |
|------|------------|
| A workflow still intentionally depends on 8.0 behavior | Characterize each touched workflow before changing it; preserve or defer truly legacy paths rather than flattening them blindly |
| SDK pin selection is too specific or too stale | Choose the pin from the repo's currently validated 9.x baseline and keep the pin scoped to the modern SDK family |
| The fix accidentally removes or obscures `net48` publishing support | Keep `net48` as an explicit exception in the release workflow and call it out in README wording |

---

## Documentation / Operational Notes

- No runtime monitoring changes are expected.
- If implementation confirms one of the legacy workflows should be retired instead of aligned, record that decision explicitly in the PR description so the remaining workflow surface stays understandable.

---

## Sources & References

- Related code: `.github/workflows/build-and-release.yml`
- Related code: `.github/workflows/mod-build-validation.yml`
- Related code: `.github/workflows/dotnet-desktop.yml`
- Related code: `.github/workflows/build-and-test.yml`
- Related guidance: `AGENTS.md`
- Related guidance: `docs/local_desktop_agent_runbook.md`
- Related docs: `README.md`
- Related plan: `docs/plans/2026-05-23-002-fix-install-coordinator-cleanup-plan.md`
