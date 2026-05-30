---
title: Full pipeline E2E accuracy closure
type: feat
status: completed
date: 2026-05-29
origin: docs/brainstorms/2026-05-29-mod-builds-pipeline-requirements.md
---

# Full pipeline E2E accuracy closure

## Summary

Close remaining gaps between unit/integration tests and the agent CLI pipeline: merged KOTOR1/KOTOR2 full builds must round-trip all formats, reload with instruction parity, validate via `ModBuildConverter validate --dry-run-only` (InstallationValidationPipeline), export JSON/YAML/XML via CLI, and smoke the `cli_full_build_pipeline.sh` path. Full install with all archives remains a local LongRunning test.

## Problem Frame

Serialization, markdown merge, and DryRunValidator tests pass locally, but the CLI validate path and multi-format export after merge were not covered on real mod-builds full builds. Agent script markdown aliases (`KOTOR*_FULL.md`) are shell-only with no automated test.

## Requirements

- R1. K1/K2 merged full builds: `validate --dry-run-only --game-dir --source-dir` exits 0 via `ModBuildConverter.Run`.
- R2. K1/K2 merged full builds: CLI `convert` to JSON, YAML, XML produces reloadable files with instruction parity vs canonical TOML.
- R3. Markdown alias filenames (`KOTOR1_FULL.md`, `KOTOR2_FULL.md`) deserialize to the same component counts as canonical `content/k*/full.md`.
- R4. Harden `ValidationPipelineParityTests` for missing-archive validate (exit 1) and install restriction auto-deselect.
- R5. Smoke `scripts/agents/cli_full_build_pipeline.sh --export-all-formats --dry-run-only` for k1 and k2.
- R6. Document deferred: `FullBuildInstallLongRunning` (requires all mod archives).

## Scope Boundaries

- **In scope:** Tests, minor test fixes, plan/docs updates.
- **Out of scope:** CI wiring for mod-builds clone, real full install in default CI, GUI changes.
- **Deferred:** LongRunning full install test on CI.

## Implementation Units

### U1. FullBuildCliPipelineTests

**Goal:** CLI E2E on merged mod-builds full sets.  
**Files:** `src/KOTORModSync.Tests/FullBuildCliPipelineTests.cs`  
**Approach:** Merge TOML+markdown; write temp merged TOML; run validate dry-run-only and convert all formats; assert exit codes and parity.  
**Test expectation:** New tests pass when `./mod-builds` is present; ignore otherwise.

### U2. ValidationPipelineParityTests hardening

**Goal:** Stronger assertions on validate/install parity paths.  
**Files:** `src/KOTORModSync.Tests/ValidationPipelineParityTests.cs`  
**Test expectation:** Existing + updated tests pass.

### U3. Agent pipeline smoke

**Goal:** Script path matches test expectations.  
**Files:** `scripts/agents/cli_full_build_pipeline.sh`  
**Approach:** Run k1/k2 with template dirs, export-all-formats, dry-run-only.  
**Verification:** Exit 0.

## Verification

```bash
dotnet test src/KOTORModSync.Tests/KOTORModSync.Tests.csproj -c Debug \
  --filter "FullyQualifiedName~FullBuildCliPipelineTests"

dotnet test src/KOTORModSync.Tests/KOTORModSync.Tests.csproj -c Debug \
  --filter "FullyQualifiedName~ValidationPipelineParityTests"

./scripts/agents/create_template_kotor_install.sh ./tmp/kotor_template ./tmp/mod_downloads
./scripts/agents/cli_full_build_pipeline.sh --game k1 \
  --game-dir ./tmp/kotor_template --source-dir ./tmp/mod_downloads \
  --export-all-formats --dry-run-only
./scripts/agents/cli_full_build_pipeline.sh --game k2 \
  --game-dir ./tmp/kotor_template --source-dir ./tmp/mod_downloads \
  --export-all-formats --dry-run-only
```
