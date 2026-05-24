---
title: Agent-native knowledgebase and agent parity
type: docs
status: completed
date: 2026-05-24
---

# Agent-native knowledgebase and agent parity

## Summary

Organize agent-facing documentation into a canonical knowledgebase, run a scored agent-native architecture audit, document user-action → agent-capability parity (including the existing Core CLI), and add thin agent helper scripts so headless workflows are discoverable without reading source.

---

## Problem Frame

KOTORModSync already supports substantial agent workflows (headless tests, Core CLI `validate`/`install`, GUI preload args, `scripts/agents/*`), but that capability is scattered across `AGENTS.md`, runbooks, and source. There is no `docs/knowledgebase/` index, no institutional learnings under `docs/solutions/`, and no scored audit of agent-native principles. Agents rediscover the Core CLI and best-effort install path repeatedly.

This is a **documentation and agent-tooling** slice, not a product feature to make the Avalonia app agent-native at runtime.

---

## Requirements

- R1. Add `docs/knowledgebase/README.md` as the canonical entry point with evidence-labeled links.
- R2. Produce `docs/knowledgebase/agent-native-audit.md` with numeric scores for all eight agent-native principles.
- R3. Produce `docs/knowledgebase/agent-action-parity.md` mapping GUI/user flows to CLI args, Core CLI verbs, scripts, and tests.
- R4. Document Core CLI verbs in `docs/knowledgebase/core-cli-reference.md`.
- R5. Add `docs/solutions/` learnings for manual release and agent guidance layering.
- R6. Add `scripts/agents/README.md` plus `cli_validate.sh` and `run_headless_tests.sh` wrappers.
- R7. Link knowledgebase from `AGENTS.md` and `.github/copilot-instructions.md`.
- R8. Sync `.cursor/skills/cloud-agents-starter` and `local_desktop_gui_testing` to reference knowledgebase and Core CLI (deferred item from plan 2026-05-23-001).

---

## Scope Boundaries

- No Avalonia UI changes, no new MCP servers, no generated-doc edits (`docs/KOTORModSync_Master.md`).
- Do not commit untracked junk (`.compound-engineering/`, `agentdecompile_projects/`, `audit/`, `package.json`, `tmp/`).
- Agent-native scores will reflect a **desktop installer**, not a web agent product; gaps are documented honestly.

---

## Implementation Units

### U1. Plan and knowledgebase scaffold

**Files:**
- Create: `docs/plans/2026-05-24-016-agent-native-knowledgebase-plan.md`
- Create: `docs/knowledgebase/README.md`
- Create: `docs/knowledgebase/doc-hierarchy.md`

### U2. Agent-native audit report

**Files:**
- Create: `docs/knowledgebase/agent-native-audit.md`

**Approach:** Score all eight principles with X/Y tables, top recommendations, and strengths. Distinguish headless agent paths from desktop-only GUI flows.

### U3. Action parity and Core CLI reference

**Files:**
- Create: `docs/knowledgebase/agent-action-parity.md`
- Create: `docs/knowledgebase/core-cli-reference.md`

**Sources:** `ModBuildConverter.cs` verbs, `CLIArguments.cs`, wizard map in `AGENTS.md`, `scripts/agents/*`, headless tests.

### U4. Institutional learnings

**Files:**
- Create: `docs/solutions/manual-release-workflow.md`
- Create: `docs/solutions/agent-guidance-layering.md`

### U5. Agent scripts and routing updates

**Files:**
- Create: `scripts/agents/README.md`
- Create: `scripts/agents/cli_validate.sh`
- Create: `scripts/agents/run_headless_tests.sh`
- Modify: `AGENTS.md`, `.github/copilot-instructions.md`
- Modify: `.cursor/skills/cloud-agents-starter/SKILL.md`, `.cursor/skills/local_desktop_gui_testing/SKILL.md`

---

## Verification

- Read new docs for internal link integrity.
- Run `scripts/agents/run_headless_tests.sh` with a narrow filter (ReleaseVersionAlignmentTests or similar) to confirm wrapper works.
- Run `bash -n` on new shell scripts.

---

## Success Criteria

- Agents can start at `docs/knowledgebase/README.md` and reach every runbook, script, and CLI verb without searching source.
- Audit report includes all eight scored principles and overall percentage.
- Entry surfaces (`AGENTS.md`, copilot instructions) link to the knowledgebase.
