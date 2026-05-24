# Agent-native architecture audit

`[SYNTH]` Scored review of KOTORModSync against the eight core agent-native principles from Compound Engineering's agent-native architecture framework. `[REPO]` Evidence is from `ModBuildConverter`, `scripts/agents/`, `AGENTS.md`, and headless tests as of 2026-05-24.

This product is a **desktop mod installer**, not a web agent host. Scores reflect how well **coding agents and headless automation** can operate the repo—not whether end users chat with the app.

## Overall score

| Metric | Value |
|--------|-------|
| Principles scored | 8 / 8 |
| Weighted average | **62%** |
| Headless agent readiness | Strong for Core CLI + tests |
| Desktop-only gap | GUI wizard, downloads UX, widescreen flow |

## Principle scores

| # | Principle | Score | Summary |
|---|-----------|-------|---------|
| 1 | **Parity** | 14/25 (56%) | Core paths (`validate`, `install`, convert) are CLI-accessible; many wizard-only flows lack headless equivalents. |
| 2 | **Granularity** | 16/20 (80%) | CLI verbs are composable; scripts wrap common combos without hiding primitives. |
| 3 | **Composability** | 12/15 (80%) | New agent workflows combine `dotnet run` + scripts + tests without code changes. |
| 4 | **Emergent capability** | 10/15 (67%) | Agents can fix TOMLs and run installs; limited without Nexus keys, real game dirs, or desktop. |
| 5 | **Improvement over time** | 6/10 (60%) | `docs/solutions/` and knowledgebase improve routing; no in-app agent learning loop. |
| 6 | **Context injection** | 11/15 (73%) | `AGENTS.md`, copilot instructions, and KB index inject routing; runtime app state is not exposed to agents. |
| 7 | **Shared workspace** | 14/15 (93%) | Repo files, TOMLs, `tmp/` template dirs, and CLI paths are the shared workspace. |
| 8 | **Agent-native testing** | 14/20 (70%) | Rich headless tests; GUI/full-build still need desktop; `LongRunning` tests excluded from CI defaults. |

**Total: 97 / 155 ≈ 62%**

---

### 1. Parity

| User / GUI capability | Agent path | Parity |
|----------------------|------------|--------|
| Load instruction file | GUI `--instructionFile=` or CLI `-i` | Yes |
| Set mod / game directories | GUI preload or CLI `-g` / `-s` | Yes |
| Run validation | `ValidatePage` or `validate --full` | Yes (full needs dirs) |
| Fetch downloads | Wizard / `ScrapeDownloadsButton` | Partial — CLI `install -d` / `convert -d` |
| Install mods | Wizard or `install` | Yes |
| Mod selection / filters | `ModSelectionPage` UI | Partial — CLI `--select` |
| Widescreen-only install block | Dynamic wizard pages | No — desktop only |
| Rich-text / spoiler UI | GUI controls | No |
| Telemetry-auth sidecar | Separate Python stack | Routed via `telemetry-auth/README.md` |

**Strengths:** `[REPO]` `ModBuildConverter` covers validate/install/convert/merge; `install_best_effort.sh` documents a full-build-style headless path.

**Gaps:** `[OPEN]` No headless API for every wizard button; widescreen and Aspyr notice flows are `[UI]` only.

**Recommendations (Tier 1):** Keep `agent-action-parity.md` current when wizard pages change. Document `--select` examples for tier/category installs.

---

### 2. Granularity

| Tool layer | Examples |
|------------|----------|
| Atomic CLI verbs | `validate`, `install`, `convert`, `merge`, `holopatcher` |
| Composition | `install -d --concurrent --best-effort -y` |
| Scripts | Thin wrappers (`cli_validate.sh`, `run_headless_tests.sh`) |

**Strengths:** Scripts delegate to `dotnet run` on Core; they do not reimplement install logic.

**Gaps:** `install_best_effort.sh` bundles many flags—acceptable as a documented recipe, not a single opaque tool.

---

### 3. Composability

Agents can assemble workflows from:

- Core CLI flags and `--select`
- `scripts/agents/*` helpers
- `dotnet test` filters
- `mod-builds` TOMLs at repo root

**Gaps:** No MCP tools inside the app; MCP wrappers in `scripts/agents/mcp_*.sh` are optional IDE tooling, not product features.

---

### 4. Emergent capability

Agents handle open-ended repo tasks (fix tests, edit TOMLs, run validation) when prerequisites exist. Full mod-list installs depend on network, disk, Nexus credentials, and hours-long downloads—environment-dependent `[OPEN]`.

---

### 5. Improvement over time

| Mechanism | Present |
|-----------|---------|
| `docs/solutions/` learnings | Yes (manual release, guidance layering) |
| Knowledgebase index | Yes |
| In-app agent feedback loop | No |

---

### 6. Context injection

| Source | What agents get |
|--------|-----------------|
| `.cursorrules` | VFS, path sandbox, test naming |
| `AGENTS.md` | Wizard map, preload args |
| `docs/knowledgebase/` | Audits, parity, CLI reference |
| Live GUI state | Not exported |

---

### 7. Shared workspace

Files are the interface: instruction TOMLs, `tmp/kotor_template`, `tmp/mod_downloads`, test fixtures, and Core `settings.json`. Agents and humans read/write the same paths. `[REPO]` Path placeholders `<<modDirectory>>` / `<<kotorDirectory>>` enforce safe instruction definitions.

---

### 8. Agent-native testing

| Layer | Coverage |
|-------|----------|
| Core / VFS / CLI | `VirtualFileSystem*Tests`, `CliInstallIntegrationTests` |
| Headless Avalonia | `HeadlessUITests`, wizard flow tests |
| Full-build / LongRunning | Local / manual; excluded from default filter |
| Desktop-only validation | `[UI]` runbook + `launch_gui_desktop.sh` |

**Recommendations (Tier 2):** Add parity test rows when new wizard pages ship. Prefer `ReleaseVersionAlignmentTests` for quick script smoke checks.

---

## Top recommendations

1. **Tier 1:** Start every agent task at `docs/knowledgebase/README.md`; use `cli_validate.sh` and `run_headless_tests.sh` instead of ad hoc commands.
2. **Tier 1:** Treat GUI-only flows as `[UI]` in plans; do not assume headless parity without checking `agent-action-parity.md`.
3. **Tier 2:** When adding wizard steps, add a CLI or script path—or document the gap in the parity matrix.
4. **Tier 3:** Optional future work: structured JSON validation output for agents (not in current scope).

## Strengths summary

- Mature **Core CLI** for validate/install/convert
- **Documented agent scripts** and preload GUI args
- **VirtualFileSystem**-aligned validation model
- **Single test project** with headless Avalonia coverage
