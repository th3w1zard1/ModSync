# Mod-builds full pipeline — requirements

**Date:** 2026-05-29  
**Status:** active  
**Related plans:** `docs/plans/2026-05-29-017-full-build-roundtrip-dryrun-plan.md`, `docs/plans/2026-05-29-018-mod-builds-markdown-merge-pipeline-plan.md`

## Outcome

Agents and CI can load canonical KOTOR full builds from `mod-builds`, round-trip them through TOML/JSON/YAML/XML without losing component fidelity, and run headless **dry-run** validation that matches GUI VFS behavior.

## Success criteria

1. `mod-builds/TOMLs/KOTOR1_Full.toml` and `KOTOR2_Full.toml` deserialize and re-serialize to TOML, JSON, YAML, and XML with stable component counts and names.
2. Core `validate --dry-run` runs `DryRunValidator` when `--game-dir` and `--source-dir` are provided.
3. `scripts/agents/cli_validate.sh` forwards `--dry-run`.
4. Automated tests cover synthetic and full-build round-trips including XML.
5. Markdown `full.md` deserializes; merged with TOML via `--use-existing-order` + `--prefer-existing-instructions` preserves instruction parity (189 K1 / 145 K2).
6. `scripts/agents/cli_full_build_pipeline.sh` merges sources and can invoke validate `--dry-run`.

## Scope boundaries

**In scope:** Core serialization, CLI dry-run wiring, tests, agent script, KB docs.

**Out of scope:** Unrelated GUI wizard work; full LongRunning install with all mod archives on CI.

**Deferred:** Full LongRunning install with all mod archives on CI; markdown-only install without TOML merge.

**Slice 3 (2026-05-29):** CLI `--auto-generate-local` on convert/merge; `validate --dry-run-only`; `FullBuildMergedDryRunTests`; agent script updates. See `docs/plans/2026-05-29-019-auto-instruction-dryrun-install-plan.md`.

**Slice 4 (2026-05-29):** `cli_full_build_pipeline.sh --export-all-formats` and `--install`; `AutoGenerateLocalCliIntegrationTests`; KB agent path. Full install with all archives remains manual/LongRunning via `install_best_effort.sh` or pipeline `--install` when downloads exist. See `docs/plans/2026-05-29-020-full-pipeline-install-plan.md`.

## Canonical sources

| User label | Path |
|------------|------|
| KOTOR1 full markdown | `mod-builds/content/k1/full.md` |
| KOTOR2 full markdown | `mod-builds/content/k2/full.md` |
| KOTOR1 full TOML | `mod-builds/TOMLs/KOTOR1_Full.toml` |
| KOTOR2 full TOML | `mod-builds/TOMLs/KOTOR2_Full.toml` |

Machine instructions and GUIDs live in TOML; markdown carries human metadata. **Two-source merge** (TOML existing + markdown incoming, `--use-existing-order`) is required for lossless instruction fidelity; markdown-only import is metadata-only.

## Assumptions

- `mod-builds` submodule or clone is present at repo root for local/CI tests; tests skip gracefully when files are missing.
- Dry-run with empty template directories validates structure and VFS paths; archive presence remains environment-dependent.
- XML round-trip uses JSON intermediate (same as existing Core design).

## Non-goals

- Re-enabling excluded documentation round-trip tests in one pass.
- Resolving all Nexus/download dependencies in CI.
