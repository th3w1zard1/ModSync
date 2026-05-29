---
title: Mod-builds markdown merge and full pipeline
type: docs
status: active
date: 2026-05-29
---

# Mod-builds markdown merge and full pipeline

## Summary

Extend the slice-1 serialization/dry-run work (plan 017) so agents can load **both** mod-builds sources (`content/k*/full.md` + `TOMLs/KOTOR*_Full.toml`), merge them with the documented two-source semantics, round-trip through TOML/JSON/YAML/XML, and run headless validate/dry-run via a single agent script.

---

## Problem Frame

Slice 1 proved canonical **TOML-only** round-trips and CLI `--dry-run`. The user goal also requires:

- Deserializing `mod-builds/content/k1/full.md` and `k2/full.md`
- Merging markdown metadata with TOML machine instructions (189 K1 / 145 K2 components)
- Saving/reloading all four serialization formats without instruction loss
- Agent-visible pipeline for validate/dry-run (install with real archives remains environment-dependent)

Markdown alone does not carry install instructions; merge must prefer **existing TOML** for `Instructions`, `Options`, and `ResourceRegistry`.

---

## Requirements

- R1. Tests load and deserialize K1/K2 `full.md` from `./mod-builds` (skip if missing).
- R2. Tests merge `full.md` (incoming) with `KOTOR*_Full.toml` (existing) using prefer-existing for instructions/options/registry; merged component count matches TOML canonical counts (189 / 145).
- R3. Merged components round-trip TOML/JSON/YAML/XML with instruction/option parity vs canonical TOML.
- R4. Add `scripts/agents/cli_full_build_pipeline.sh` — merge + optional format export + validate `--dry-run`.
- R5. Document mod-builds merge flags and pipeline script in `docs/knowledgebase/core-cli-reference.md`.
- R6. Update `docs/brainstorms/2026-05-29-mod-builds-pipeline-requirements.md` to reflect slice-2 scope; defer LongRunning full install with all archives.

---

## Scope Boundaries

- **In scope:** Merge tests, agent pipeline script, KB updates.
- **Out of scope:** Unrelated GUI wizard changes; auto-instruction generation over the network in CI; LongRunning real install gate.
- **Deferred:** Re-enabling full `DocumentationRoundTripTests` markdown doc parity; Nexus download automation in CI.

---

## Implementation Units

### U1. Merge round-trip tests

**File:** `src/KOTORModSync.Tests/FullBuildMarkdownMergeRoundTripTests.cs`

### U2. Agent pipeline script

**File:** `scripts/agents/cli_full_build_pipeline.sh`

### U3. Documentation

**Files:** `docs/knowledgebase/core-cli-reference.md`, `scripts/agents/README.md`, brainstorm requirements doc

---

## Verification

```bash
dotnet test src/KOTORModSync.Tests/KOTORModSync.Tests.csproj \
  --filter "FullyQualifiedName~FullBuildMarkdownMergeRoundTripTests|FullyQualifiedName~FullBuildSerializationRoundTripTests"

./scripts/agents/cli_full_build_pipeline.sh --game k1 \
  --game-dir ./tmp/kotor_template --source-dir ./tmp/mod_downloads --dry-run
```

---

## Success Criteria

- K1/K2 merge tests pass when `./mod-builds` is present.
- Pipeline script produces merged TOML and invokes validate `--dry-run`.
- KB documents the two-source merge command line.
