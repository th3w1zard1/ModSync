# Agent guidance layering

`[SYNTH]` How to keep agent instructions consistent across `.cursorrules`, `AGENTS.md`, Copilot instructions, knowledgebase, runbooks, skills, and plans—without duplicating procedural walls of text.

## Problem

KOTORModSync agent knowledge was spread across:

- Always-on rules (path sandbox, VFS, tests)
- Routing entry points (`AGENTS.md`, copilot instructions)
- Long GUI runbooks
- Ad hoc discovery of `ModBuildConverter` in source

Agents re-discovered the Core CLI and wizard order on every task.

## Layering model

| Layer | Location | Holds |
|-------|----------|--------|
| Hard constraints | `.cursorrules` | Path placeholders, VFS-only validation, test naming, Avalonia traps |
| Routing | `AGENTS.md`, `.github/copilot-instructions.md` | Headless vs GUI vs telemetry-auth; wizard control names |
| Index + audits | `docs/knowledgebase/` | Parity matrix, CLI reference, scored audit |
| Procedures | `docs/local_desktop_agent_runbook.md` | Step-by-step desktop/full-build |
| Learnings | `docs/solutions/` | Why releases are manual; how to edit guidance |
| Plans | `docs/plans/` | Dated implementation intent |
| Shortcuts | `.cursor/skills/*` | Cursor-specific quick paths |

Authority order when docs conflict: see [doc-hierarchy.md](../knowledgebase/doc-hierarchy.md).

## Rules for contributors

1. **One home per fact** — Put procedural steps in runbooks; put “where to start” in knowledgebase README.
2. **Link, don't copy** — `AGENTS.md` should link to `docs/knowledgebase/README.md`, not duplicate CLI flag lists.
3. **Update together** — When wizard order or preload args change, update `AGENTS.md`, runbook, and `agent-action-parity.md`.
4. **Evidence labels** — Use `[REPO]` / `[UI]` / `[OPEN]` in audits and research (see knowledgebase README).
5. **Do not edit generated maps** — `docs/KOTORModSync_Master.md` is tooling output.

## Drift signals

- README in knowledgebase links to a missing file
- Copilot instructions contradict `.cursorrules` on VFS
- Skills reference commands that moved under `scripts/agents/`

Fix by updating the highest-priority layer first, then the index links.

## Related

- [Documentation hierarchy](../knowledgebase/doc-hierarchy.md)
- [Agent-native audit](../knowledgebase/agent-native-audit.md)
