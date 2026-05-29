---
title: Full mod-builds pipeline through install
type: feat
status: completed
date: 2026-05-29
origin: docs/plans/2026-05-29-019-auto-instruction-dryrun-install-plan.md
---

# Full mod-builds pipeline through install

## Summary

Slice 4 closes the agent loop from `mod-builds` sources through merge, optional local auto-instruction generation, four-format export, VFS dry-run, and best-effort headless install. Slices 1–3 already prove deserialize/round-trip/dry-run-only; this slice wires install and documents the one-command path.

## Problem Frame

Agents can merge and dry-run without archives, but cannot run a single script through install. `--auto-generate-local` lacks an integration test. Users refer to `KOTOR1_FULL.md` but canonical paths are `mod-builds/content/k*/full.md` plus TOML merge.

## Requirements

- R1. `cli_full_build_pipeline.sh --install` runs best-effort install after merge (requires game + source dirs).
- R2. `cli_full_build_pipeline.sh --export-all-formats` writes TOML/JSON/YAML/XML from merged output.
- R3. Add `AutoGenerateLocalCliIntegrationTests` for merge + `--auto-generate-local` with synthetic archive.
- R4. Document end-to-end agent command sequence in KB and brainstorm.
- R5. (Deferred) `FullBuildInstallLongRunning` with all archives present.

## Scope Boundaries

- **In scope:** Agent scripts, integration test, KB.
- **Out of scope:** CI download of all mod archives; GUI wizard changes.
- **Deferred:** LongRunning install test with full archive set.

## Implementation Units

### U1. Pipeline install + export-all-formats

**Files:** `scripts/agents/cli_full_build_pipeline.sh`, `scripts/agents/README.md`

### U2. Auto-generate-local integration test

**Files:** `src/KOTORModSync.Tests/AutoGenerateLocalCliIntegrationTests.cs`

### U3. KB and plan status

**Files:** `docs/knowledgebase/core-cli-reference.md`, `docs/brainstorms/2026-05-29-mod-builds-pipeline-requirements.md`, mark plan 019 completed

## Verification

```bash
dotnet test src/KOTORModSync.Tests/KOTORModSync.Tests.csproj \
  --filter "FullyQualifiedName~AutoGenerateLocalCliIntegrationTests|FullyQualifiedName~FullBuildMergedDryRunTests"

./scripts/agents/cli_full_build_pipeline.sh --game k1 \
  --game-dir ./tmp/kotor_template --source-dir ./tmp/mod_downloads \
  --export-all-formats --dry-run-only
```
