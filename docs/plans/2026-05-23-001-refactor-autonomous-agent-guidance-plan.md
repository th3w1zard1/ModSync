---
title: Refactor autonomous agent guidance
type: refactor
status: shipped
date: 2026-05-23
---

# Refactor autonomous agent guidance

## Summary

Tighten the repository's always-on guidance so future Copilot/autopilot sessions infer the default path from repo context instead of stopping for avoidable clarification. Keep the current layered model intact: short repo-entry guidance sets defaults and routing, while deeper runbooks remain the source of procedural detail.

---

## Problem Frame

The repo already documents the right workflows for headless work, GUI/manual validation, MCP wrappers, and path/validation constraints, but that knowledge is spread across several surfaces and not framed as "what to assume before asking." That makes agents rediscover obvious defaults mid-task, especially when switching between normal .NET work, Avalonia GUI work, and local desktop/full-build flows.

---

## Assumptions

*This plan was authored without synchronous user confirmation. The items below are agent inferences that fill gaps in the input — un-validated bets that should be reviewed before implementation proceeds.*

- The requested improvement is guidance-first: update maintained instruction surfaces rather than product runtime code.
- The highest-value outcome is better default inference for future Copilot sessions, with the same intent mirrored in the repo-root agent guidance that other agent surfaces already consume.
- Existing runbooks and helper scripts are mostly correct; the main issue is discoverability and decision routing, not missing procedural detail.

---

## Requirements

- R1. Future Copilot/autopilot sessions should get an explicit default-inference path for common task shapes before asking for clarification.
- R2. Guidance should distinguish headless .NET work, GUI/manual-validation work, and repo-adjacent sidecar work so agents route themselves correctly.
- R3. Guidance should explicitly state the small set of conditions that really do justify asking or stopping.
- R4. The repo should keep its layered guidance model: short always-on entrypoints point to deeper runbooks instead of duplicating them.
- R5. Updated guidance should preserve the current repo constraints around path sandboxing, VFS-based validation, test placement, GUI validation expectations, and MCP wrapper usage.

---

## Scope Boundaries

- No product runtime or UI behavior changes.
- No changes to generated documentation such as `docs/KOTORModSync_Master.md`.
- No new MCP servers, helper scripts, or IDE tasks unless implementation uncovers a documentation mismatch that must be corrected.

### Deferred to Follow-Up Work

- Audit Cursor skill files under `.cursor/skills/` only if the root-surface wording reveals contradictory task-routing behavior that cannot stay isolated to `.github/copilot-instructions.md`, `AGENTS.md`, and `.cursorrules`.

---

## Context & Research

### Relevant Code and Patterns

- `.github/copilot-instructions.md` is already the best Copilot-specific surface, but it currently emphasizes commands/architecture/conventions more than "infer this first."
- `AGENTS.md` already acts as the repo-root routing layer for GUI/install-wizard/full-build work and explicitly points agents to `docs/local_desktop_agent_runbook.md` and `.cursor/skills/*`.
- `.cursorrules` already captures the strongest repo-wide "always/never" constraints: path sandboxing, VFS-only validation, single test project, `LongRunning` naming, and local desktop GUI expectations.
- `docs/local_desktop_agent_runbook.md` is the deep procedural reference for GUI/full-build work; it should stay detailed rather than becoming the always-on entrypoint.
- `mcp.json` and `.cursor/mcp.json` already define the preferred MCP wrappers (`repo-filesystem`, `desktop-commander`, `playwright`) and should be referenced, not reinvented.

### Institutional Learnings

- Preserve the existing layered model: short entrypoint guidance should route to deeper runbooks and skills instead of absorbing all detail.
- The repo already has enough context for agents to act autonomously in many cases; the missing piece is clearer default assumptions and clearer stop conditions.
- Guidance changes should keep maintained docs authoritative and avoid elevating generated files into sources of truth.

### External References

- None. Local repo patterns are already strong enough for this guidance-only task.

---

## Key Technical Decisions

- Update the source-of-truth trio first: `.github/copilot-instructions.md`, `AGENTS.md`, and `.cursorrules`. These are the highest-leverage surfaces future sessions are most likely to load early.
- Add a compact decision matrix rather than prose-heavy reminders. The change should make autonomous routing obvious at a glance.
- Frame questions as exception handling, not the default workflow. Guidance should tell agents what to assume first, then enumerate the small set of blockers that justify asking.
- Keep deeper runbooks referenced by path rather than duplicating procedures. This preserves the current layered documentation model and reduces future drift.

---

## Open Questions

### Resolved During Planning

- Should this work modify product code? No — the request is best served by tightening repo guidance surfaces.
- Should generated docs be updated? No — generated artifacts should remain downstream context, not primary edit targets.
- Should this plan include MCP configuration changes? No — current MCP wrappers are sufficient; guidance should point agents to them rather than adding new servers.

### Deferred to Implementation

- Whether the final wording belongs in a new section or a revision of existing sections within `AGENTS.md` and `.cursorrules`; that can be decided while editing for the cleanest fit.
- Whether any Cursor skill file needs wording sync after the top-level guidance is updated; only implementation-time comparison can confirm actual drift.

---

## Implementation Units

### U1. Strengthen Copilot-first autonomous defaults

**Goal:** Make `.github/copilot-instructions.md` tell future Copilot sessions what to infer by default before asking for clarification.

**Requirements:** R1, R2, R3, R5

**Dependencies:** None

**Files:**
- Modify: `.github/copilot-instructions.md`

**Approach:**
- Add a compact default-inference section that covers the main repo task shapes: standard headless .NET work, GUI/manual-validation work, and repo-adjacent sidecar work.
- Add an explicit "ask/stop only when X is missing" list grounded in current repo constraints such as missing `mod-builds`, lack of desktop/X11 for GUI validation, or tasks that genuinely require user policy decisions.
- Keep existing command and architecture content, but reframe it so future sessions can choose a default path quickly instead of reading the whole file as passive reference material.

**Patterns to follow:**
- Existing concise summary style in `.github/copilot-instructions.md`
- Routing style already used by `AGENTS.md`

**Test scenarios:**
- Test expectation: none -- documentation-only unit. Review should confirm the new section gives one obvious default path for headless work, one for GUI/manual work, and explicit stop conditions.

**Verification:**
- A future Copilot session can read `.github/copilot-instructions.md` and identify a likely next action without consulting another file for basic routing.
- The file still reads as a short always-on brief, not a duplicated runbook.

---

### U2. Align the repo-root agent entrypoint with the new defaults

**Goal:** Update `AGENTS.md` so the repo-root guidance matches the same autonomous assumptions and escalation rules as the Copilot-specific brief.

**Requirements:** R1, R2, R3, R4, R5

**Dependencies:** U1

**Files:**
- Modify: `AGENTS.md`

**Approach:**
- Add or revise a short section near the repo-root workflow guidance that tells agents what to assume first for common task types and what conditions actually justify asking or stopping.
- Preserve the current handoff model to `docs/local_desktop_agent_runbook.md` and `.cursor/skills/*` instead of copying their detail.
- Keep the emphasis on helper scripts, preload args, and real desktop validation for GUI work.

**Patterns to follow:**
- Existing layered guidance in `AGENTS.md`
- Current task routing to local runbook and Cursor skills

**Test scenarios:**
- Test expectation: none -- documentation-only unit. Review should confirm `AGENTS.md` still points to the deeper runbook/skills while making the default routing decision clearer.

**Verification:**
- `AGENTS.md` and `.github/copilot-instructions.md` no longer send mixed signals about when to infer, when to ask, and when to escalate to deeper docs.

---

### U3. Encode the hard "don't ask unless missing X" rules at the repo-wide constraint layer

**Goal:** Use `.cursorrules` to turn the most important autonomous defaults and exceptions into hard repo-wide rules.

**Requirements:** R1, R3, R4, R5

**Dependencies:** U1

**Files:**
- Modify: `.cursorrules`

**Approach:**
- Add a focused rule block that encodes the repo's strongest autonomous assumptions: headless-first unless the task is GUI-facing, prefer helper scripts and preload args, treat missing prerequisites as the main reason to stop, and preserve the existing VFS/path/test constraints.
- Keep this file in "hard rule" territory; do not duplicate the full command matrix or runbook detail.

**Patterns to follow:**
- Existing imperative rule style in `.cursorrules`
- Existing high-signal constraint sections for path sandboxing, VFS, testing, and GUI work

**Test scenarios:**
- Test expectation: none -- documentation-only unit. Review should confirm the new wording behaves like a constraint layer, not a second runbook.

**Verification:**
- `.cursorrules` captures the key autonomous guardrails in a way that complements, rather than duplicates, the richer guidance files.

---

## System-Wide Impact

- **Interaction graph:** Future Copilot sessions should read `.github/copilot-instructions.md`; broader agent workflows should align through `AGENTS.md`; repo-wide hard constraints should remain enforced through `.cursorrules`.
- **Error propagation:** Poorly scoped guidance causes wasted clarification loops and incorrect workflow selection; clearer defaults should reduce both.
- **API surface parity:** GUI/manual-validation routing and MCP wrapper usage must remain consistent across the top-level guidance surfaces.
- **Unchanged invariants:** Path sandboxing, VFS-first validation, single test project, `LongRunning` naming, helper-script preference, and real desktop validation for GUI work remain unchanged.

---

## Risks & Dependencies

| Risk | Mitigation |
|------|------------|
| Top-level docs drift apart and create new contradictions | Update the three highest-leverage surfaces together and verify they still point to the same deeper references |
| Guidance becomes too long and recreates the runbook at the top level | Keep new content in matrix/checklist form and defer procedural depth to `docs/local_desktop_agent_runbook.md` |
| New guidance overreaches into product or workflow policy decisions | Limit changes to defaults, routing, and stop conditions already evidenced by repo docs |

---

## Documentation / Operational Notes

- If implementation reveals a real mismatch between root guidance and deeper runbooks/skills, update the affected deeper doc in the same pass.
- Do not edit generated docs as part of this work.

---

## Sources & References

- `.github/copilot-instructions.md`
- `AGENTS.md`
- `.cursorrules`
- `docs/local_desktop_agent_runbook.md`
- `.cursor/mcp.json`
- `mcp.json`

---

## Verification log

| Check | Result |
|-------|--------|
| U1 Copilot defaults | `Default inference path` section in `.github/copilot-instructions.md` (PR #71, `25d72a9`) |
| U2 AGENTS.md alignment | `Autonomous defaults` section mirrors same routing (PR #71) |
| U3 .cursorrules constraints | `AUTONOMOUS EXECUTION DEFAULTS` block encodes hard rules (PR #71) |
| R1–R5 | Satisfied — layered model preserved; stop conditions aligned across trio |
| Merge | Shipped on `master` via PR #71; plan closed on LFG pipeline pass |
