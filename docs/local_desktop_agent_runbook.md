# Local desktop agent runbook

This runbook is for local LLM agents running outside Cursor Cloud in a real desktop session or VM.

It captures the workflow that was actually exercised against this repo's Avalonia GUI on Linux.

## Goal

Make it easy for a local desktop-capable agent to:

- start the GUI reliably
- avoid file pickers by using CLI preload args
- create a disposable KOTOR install target
- drive the install wizard
- run `mod-builds` import, download, validation, and installation flows

## Verified local baseline

- Linux desktop VM / X11 session
- `DISPLAY=:1`
- .NET SDK 9.0.x
- repo checked out at a writable local path
- `mod-builds` cloned at repo root as `./mod-builds`

## Recommended local tools

### Required

- `dotnet` 9.x
- `bash`
- `git`

### Strongly recommended for GUI automation

- `xdotool`
- `wmctrl`
- `xwininfo`
- `ffmpeg`
- `imagemagick`

### Recommended for local MCP clients

- `node` / `npm`

The repo ships wrapper scripts for common MCP servers under `scripts/agents/`.

## Quick start

### 1. Clone `mod-builds`

The repo's real-world tests and GUI full-build flow expect `mod-builds` at the repo root:

`git clone https://github.com/th3w1zard1/mod-builds ./mod-builds`

### 2. Create disposable test directories

Use the helper script:

`./scripts/agents/create_template_kotor_install.sh ./tmp/kotor_template ./tmp/mod_downloads`

This creates:

- a fake KOTOR install rooted at `./tmp/kotor_template`
- an empty mod workspace at `./tmp/mod_downloads`

The template install includes the minimum expected structure:

- `swkotor.exe`
- `dialog.tlk`
- `Override/`
- `modules/extras/`
- `TexturePacks/`
- other common game folders

### 3. Build and launch the GUI

Use the launch helper:

`./scripts/agents/launch_gui_desktop.sh --instruction-file ./mod-builds/TOMLs/KOTOR1_Full.toml --kotor-dir ./tmp/kotor_template --mod-dir ./tmp/mod_downloads`

What the script does:

- builds `src/KOTORModSync.GUI`
- ensures Linux `Resources/holopatcher` exists
- launches the app with:
  - `--instructionFile=...`
  - `--kotorPath=...`
  - `--modDirectory=...`

This is the preferred local-agent launch path.

## Why CLI preload args matter

The GUI supports:

- `--instructionFile=<path>`
- `--kotorPath=<path>`
- `--modDirectory=<path>`

Using them is much more reliable than making an agent navigate native file dialogs.

The app auto-loads the instruction file during startup when `--instructionFile` is present.

## Wizard control map

## Install wizard page order

The wizard is built in this order:

1. `LoadInstructionPage`
2. `WelcomePage`
3. optional `PreamblePage`
4. `ModDirectoryPage`
5. `GameDirectoryPage`
6. optional `AspyrNoticePage`
7. `ModSelectionPage`
8. `DownloadsExplainPage`
9. `ValidatePage`
10. `InstallStartPage`
11. `InstallingPage`
12. `BaseInstallCompletePage`
13. `FinishedPage`

Widescreen pages are inserted dynamically after the base install if widescreen mods are selected.

### Important controls by page

#### `ModSelectionPage`

- `SelectAllButton` — `✓ Select All`
- `DeselectAllButton` — `✗ Deselect All`
- `SelectByTierButton`
- `SelectByCategoryButton`
- `SearchTextBox`
- `CategoryFilterComboBox`
- `TierFilterComboBox`
- `SpoilerFreeToggle`
- `ExpandCollapseAllButton`

This is the page used to select all mods in a full-build test.

#### `DownloadsExplainPage`

The text explains that downloads continue in the background while the wizard advances.

#### `ValidatePage`

- `ValidateButton` — `🔍 Run Validation`
- `ValidationProgress`
- `StatusText`
- `LogExpander`
- `LogText`
- `SummaryText`
- `SummaryDetails`
- `ErrorCountBadge`
- `WarningCountBadge`
- `PassedCountBadge`

Always expand the validation log before concluding that a run passed or failed.

#### `InstallStartPage`

This is the last review page before installation begins.

## Getting Started tab control map

The non-wizard onboarding tab uses these controls:

- `Step1ModDirectoryPicker`
- `Step1KotorDirectoryPicker`
- `Step2Button` — `📄 Load Instruction File`
- `ScrapeDownloadsButton` — `Fetch Downloads`
- `OpenModDirectoryButton`
- `DownloadStatusButton`
- `StopDownloadsButton`
- `ValidateButton` — `🔍 Validate`

## Full-build test recipe

### KOTOR 1 full build

1. Ensure `./mod-builds/TOMLs/KOTOR1_Full.toml` exists.
2. Launch the GUI with preload args.
3. Advance to `ModSelectionPage`.
4. Click `SelectAllButton`.
5. Advance to the downloads step.
6. Click `Fetch Downloads`.
7. Wait until progress stabilizes or required downloads finish.
8. Advance to `ValidatePage`.
9. Click `Run Validation`.
10. Expand the validation log.
11. Only continue if the validation result is acceptable for the task.

### KOTOR 2 full build

Use the same flow with:

`./mod-builds/TOMLs/KOTOR2_Full.toml`

## Real-world behavior observed while validating this runbook

- The preload args work.
- The GUI wizard can be driven end to end in a Linux desktop VM.
- `Fetch Downloads` really downloads missing archives.
- `mod-builds` itself is instruction metadata only; the actual archives are fetched from third-party sources.
- The validation log is the main place to inspect VFS / dry-run issues.

## Linux HoloPatcher workaround

For a plain Debug build on Linux, the GUI can start even when validation later reports HoloPatcher missing.

Use:

`./scripts/agents/ensure_linux_holopatcher.sh`

This links:

- `vendor/bin/HoloPatcher_linux`

to:

- `src/KOTORModSync.GUI/bin/Debug/net9.0/Resources/holopatcher`

That is the path `InstallationService.FindHolopatcherAsync()` checks on non-Windows platforms.

## Local MCP setup

The repo includes:

- `.cursor/mcp.json`
- `scripts/agents/mcp_filesystem.sh`
- `scripts/agents/mcp_desktop_commander.sh`
- `scripts/agents/mcp_playwright.sh`

These wrappers let local agents start standard MCP servers without hardcoding the repo path into the config.

Recommended server roles:

- filesystem access scoped to the repo
- terminal / desktop shell control
- browser automation when browser-side testing is needed

Native desktop clicking still requires a desktop-capable agent or local automation tools such as `xdotool`.

## VS Code / IDE helpers

The repo also includes:

- `.vscode/tasks.json`
- `.vscode/launch.json`
- `.vscode/extensions.json`

Use them as the default local-agent entrypoints before inventing new ad hoc commands.

## High-signal automated checks

Prefer targeted tests:

- `MainWindowHeadlessTests`
- `ValidatePageHeadlessTests`
- markdown import tests that read from `mod-builds`
- real TOML loading tests in `RealModIntegrationTests`

## When this runbook must be updated

Update this file any time one of these changes:

- launch command
- preload flags
- wizard page order
- control names / labels
- Linux HoloPatcher workaround
- `mod-builds` path expectations
- recommended MCP servers
- local VM package requirements
