# Core CLI reference (ModBuildConverter)

`[REPO]` The headless entry point is `src/KOTORModSync.Core/Program.cs`, which delegates to `ModBuildConverter.Run`. Invoke from the repo root:

```bash
dotnet run --project src/KOTORModSync.Core/KOTORModSync.Core.csproj -f net9.0 -- <verb> [options]
```

Wrapper: `./scripts/agents/cli_validate.sh` for validation.

## Global options

All verbs inherit from `BaseOptions`:

| Flag | Description |
|------|-------------|
| `-v` / `--verbose` | Verbose logging |
| `--plaintext` | Plain-text log output (no ANSI) |

## Verbs

### `validate`

Validate an instruction file. Structural checks work with `-i` alone; environment checks need directories.

| Flag | Required | Description |
|------|----------|-------------|
| `-i` / `--input` | Yes | Instruction file path |
| `-g` / `--game-dir` | For `--full` | KOTOR install directory |
| `-s` / `--source-dir` | For `--full` | Mod download workspace |
| `--select` | No | Filter components (`category:Name` or `tier:Name`; repeatable) |
| `--full` | No | Full validation (requires game + source dirs) |
| `--errors-only` | No | Suppress warnings/info |
| `--ignore-errors` | No | Best-effort dependency order |

**Example:**

```bash
dotnet run --project src/KOTORModSync.Core/KOTORModSync.Core.csproj -f net9.0 -- \
  validate -i ./mod-builds/TOMLs/KOTOR1_Full.toml \
  -g ./tmp/kotor_template -s ./tmp/mod_downloads --full
```

---

### `install`

Install selected mods from an instruction file.

| Flag | Required | Description |
|------|----------|-------------|
| `-i` / `--input` | Yes | Instruction file |
| `-g` / `--game-dir` | Yes | Game install directory |
| `-s` / `--source-dir` | No | Mod workspace (defaults near input file) |
| `--select` | No | Subset by category/tier |
| `-d` / `--download` | No | Download archives to source dir first |
| `--concurrent` | No | Parallel downloads |
| `-y` / `--yes` | No | Auto-confirm prompts |
| `--skip-validation` | No | Skip pre-install checks (not recommended) |
| `--no-checkpoint` | No | Disable checkpointing |
| `--best-effort` | No | Continue on missing sources and mod failures; implies `-y` |
| `--continue-on-missing-sources` | No | Partial install when archives missing |
| `--continue-on-mod-failure` | No | Continue after per-mod failure |
| `--nexus-api-key` | No | Nexus API key (or env `KOTOR_MODSYNC_NEXUS_API_KEY` / `NEXUS_MODS_API_KEY`) |
| `--download-timeout-hours` | No | Max hours for download phase (default 48) |
| `--patcher-engine` | No | `Holopatcher` or `KPatcher` |
| `--kpatcher-path` | No | KPatcher executable when using KPatcher |
| `--ignore-errors` | No | Best-effort dependency resolution |

**Example (best-effort full list):** see `scripts/agents/install_best_effort.sh`.

---

### `convert`

Convert format, autogenerate links, download, or merge (with `-m`).

| Flag | Description |
|------|-------------|
| `-i` / `--input` | Single-file input |
| `-o` / `--output` | Output path (stdout if omitted) |
| `-f` / `--format` | `toml`, `yaml`, `json`, `xml`, `ini`, `markdown` |
| `-a` / `--auto` | Autogenerate from URLs (no download) |
| `-d` / `--download` | Download mods to `--source-path` |
| `--source-path` | Mod workspace for downloads |
| `-s` / `--select` | Component filter |
| `-m` / `--merge` | Merge mode (use with `-e` / `-n`) |
| `-e` / `--existing`, `-n` / `--incoming` | Merge inputs |
| Merge preference flags | `--prefer-existing-*`, `--prefer-incoming-*`, `--exclude-*-only`, `--use-existing-order` |
| `--concurrent`, `--ignore-errors`, `--spoiler-free` | As labeled in `--help` |

---

### `merge`

Dedicated merge of two instruction sets (`-e` and `-n` required). Supports the same merge/download/select flags as `convert --merge`.

---

### `set-nexus-api-key`

Store and optionally validate a Nexus Mods API key.

```bash
dotnet run --project src/KOTORModSync.Core/KOTORModSync.Core.csproj -f net9.0 -- \
  set-nexus-api-key YOUR_KEY
```

| Flag | Description |
|------|-------------|
| `--skip-validation` | Save without remote validation |

---

### `install-python-deps`

Install HoloPatcher Python dependencies (build-time / local setup).

| Flag | Description |
|------|-------------|
| `--force` | Reinstall even if present |

---

### `holopatcher`

Run bundled HoloPatcher with optional arguments.

| Flag | Description |
|------|-------------|
| `-a` / `--args` | Arguments passed to HoloPatcher |

---

## GUI preload args (separate from Core CLI)

`[REPO]` `src/KOTORModSync.GUI/CLIArguments.cs` — Avalonia app only, `--key=value` form:

| Arg | Purpose |
|-----|---------|
| `--instructionFile=` | Auto-load instruction file |
| `--kotorPath=` | Game directory |
| `--modDirectory=` | Mod workspace |

Used by `scripts/agents/launch_gui_desktop.sh`. See `agent-action-parity.md`.

## Related docs

- [Agent action parity](agent-action-parity.md)
- [scripts/agents/README.md](../../scripts/agents/README.md)
- `.cursor/skills/cloud-agents-starter/SKILL.md` — quick headless examples
