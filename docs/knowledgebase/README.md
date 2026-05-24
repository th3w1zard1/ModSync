# KOTORModSync knowledgebase

Canonical index for humans and coding agents working on this repository. Start here when routing a task or verifying how agent workflows map to product behavior.

## Evidence labels

Use these when citing findings in plans, PRs, or audits:

| Label | Meaning |
|-------|---------|
| `[REPO]` | Observed from source, tests, or scripts in this repository |
| `[UI]` | Requires a real desktop Avalonia session |
| `[OFFICIAL]` | External vendor or upstream documentation |
| `[SYNTH]` | Synthesized conclusion from multiple sources |
| `[OPEN]` | Unverified or environment-dependent |

## Read order by task shape

| If you are… | Read first | Then |
|-------------|------------|------|
| New to the repo | `AGENTS.md` | This file → `doc-hierarchy.md` |
| Headless build/test/core | `.github/copilot-instructions.md` | `core-cli-reference.md` |
| GUI / install wizard / full-build | `docs/local_desktop_agent_runbook.md` | `agent-action-parity.md` |
| Release or versioning | `docs/manual-release.md` | `docs/solutions/manual-release-workflow.md` |
| Agent capability gaps | `agent-native-audit.md` | `agent-action-parity.md` |
| telemetry-auth sidecar | `telemetry-auth/README.md` | Do not use Avalonia runbooks |

## Topic index

### Architecture and agent parity

- [Documentation hierarchy](doc-hierarchy.md) — which doc is authoritative for what
- [Agent-native architecture audit](agent-native-audit.md) — scored review of eight agent-native principles
- [Agent action parity](agent-action-parity.md) — user/GUI flows vs CLI, scripts, and tests
- [Core CLI reference](core-cli-reference.md) — `ModBuildConverter` verbs (`validate`, `install`, `convert`, …)

### Runbooks (procedural)

- [Local desktop agent runbook](../local_desktop_agent_runbook.md) — GUI launch, wizard order, full-build flow
- [Manual release](../manual-release.md) — when and how to publish GitHub releases

### Agent scripts

- [scripts/agents/README.md](../../scripts/agents/README.md) — helper script catalog and when to use each

### Institutional learnings

- [docs/solutions/](../solutions/) — durable postmortems and patterns (prefer over chat history)
- [Manual release workflow](../solutions/manual-release-workflow.md)
- [Agent guidance layering](../solutions/agent-guidance-layering.md)

### Plans (implementation history)

- [docs/plans/](../plans/) — dated plans with requirements and status frontmatter

### Always-on rules (do not duplicate here)

- `.cursorrules` — path sandboxing, VFS, test naming, Avalonia gotchas
- `AGENTS.md` — routing layer and wizard control map
- `.github/copilot-instructions.md` — Copilot default inference and build commands

## Quick commands

```bash
# Standard non-long-running tests
./scripts/agents/run_headless_tests.sh

# Validate an instruction file (full validation needs game + mod dirs)
./scripts/agents/cli_validate.sh --input ./mod-builds/TOMLs/KOTOR1_Full.toml \
  --game-dir ./tmp/kotor_template --source-dir ./tmp/mod_downloads --full

# Best-effort headless full-list install (long-running; needs Nexus key for many mods)
./scripts/agents/install_best_effort.sh \
  ./mod-builds/TOMLs/KOTOR1_Full.toml ./tmp/kotor_template ./tmp/mod_downloads

# Launch GUI with preload args (desktop session required)
./scripts/agents/launch_gui_desktop.sh \
  --instruction-file ./mod-builds/TOMLs/KOTOR1_Full.toml \
  --kotor-dir ./tmp/kotor_template \
  --mod-dir ./tmp/mod_downloads
```

## What this knowledgebase is not

- Not a substitute for generated maps (`docs/KOTORModSync_Master.md`, `docs/KOTORModSync_Codebase_Map.json`) — use those for exhaustive symbol lookup, not routing.
- Not a promise that every GUI button has a headless equivalent — see the audit and parity docs for explicit gaps.
