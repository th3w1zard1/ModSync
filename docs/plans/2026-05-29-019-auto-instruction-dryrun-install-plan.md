---
title: Auto-instruction generation and accurate dry-run pipeline
type: docs
status: completed
date: 2026-05-29
---

# Auto-instruction generation and accurate dry-run pipeline

## Summary

Slice 3 closes the mod-builds pipeline loop: local archive auto-instruction generation in CLI, VFS-only dry-run mode for structural accuracy without archive presence, merged full-build dry-run smoke tests, and agent script/docs updates. Real full install with all archives remains environment-gated (LongRunning).

## Problem Frame

Slices 1–2 proved four-format round-trip and two-source merge. Remaining gaps:

- `validate --dry-run` always runs `ComponentValidation` first; empty mod workspaces fail on missing archives even when VFS simulation would be the desired signal.
- `ComponentProcessingService.TryGenerateFromLocalArchivesAsync` exists but is not exposed on CLI merge/convert.
- No automated test exercises `DryRunValidator` against merged full builds.

## Requirements

- R1. Add `validate --dry-run-only` — skip per-component file checks; exit code reflects VFS dry-run only.
- R2. Add `--auto-generate-local` on `convert` and `merge` — fill instructions from local archives when `--source-path` is set (skips components that already have instructions).
- R3. Add `FullBuildMergedDryRunTests` — merged K1/K2 structural checks + `DryRunValidator` smoke (skip if `./mod-builds` missing).
- R4. Extend `cli_validate.sh` and `cli_full_build_pipeline.sh` with new flags.
- R5. Update `docs/knowledgebase/core-cli-reference.md`, `scripts/agents/README.md`, brainstorm requirements.
- R6. Document full install as manual/LongRunning when archives present (`install_best_effort.sh`).

## Scope Boundaries

- **In scope:** CLI flags, tests, agent scripts, KB.
- **Out of scope:** GUI wizard changes; CI download of all mod archives; guaranteeing dry-run pass without archives.
- **Deferred:** LongRunning full install test; markdown-only full install without TOML merge.

## Implementation Units

### U1. CLI validate `--dry-run-only`

**File:** `src/KOTORModSync.Core/CLI/ModBuildConverter.cs`

### U2. CLI `--auto-generate-local`

**Files:** `ModBuildConverter.cs` (ConvertOptions, MergeOptions, RunConvertAsync, RunMergeAsync)

### U3. Tests

**File:** `src/KOTORModSync.Tests/FullBuildMergedDryRunTests.cs`

### U4. Agent scripts + docs

**Files:** `scripts/agents/cli_validate.sh`, `scripts/agents/cli_full_build_pipeline.sh`, KB docs

## Verification

```bash
dotnet test src/KOTORModSync.Tests/KOTORModSync.Tests.csproj \
  --filter "FullyQualifiedName~FullBuildMergedDryRunTests|FullyQualifiedName~FullBuildMarkdownMergeRoundTripTests|FullyQualifiedName~FullBuildSerializationRoundTripTests"

./scripts/agents/cli_full_build_pipeline.sh --game k1 \
  --game-dir ./tmp/kotor_template --source-dir ./tmp/mod_downloads --dry-run-only
```

## Success Criteria

- `--dry-run-only` exits on VFS result when component file checks would fail on empty workspace.
- `--auto-generate-local` generates instructions for components with archives and empty instruction lists.
- Merged full-build tests pass when `./mod-builds` is present.
- KB documents new flags and install deferral.
