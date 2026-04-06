# KOTORModSync agent guide

This file is the short entry point for agents working on the Avalonia desktop GUI, the install wizard, or full-build flows against `mod-builds`. For step-by-step commands and tooling, read `docs/local_desktop_agent_runbook.md` next.

## Purpose

Use this repository's local-agent assets whenever a task touches:

- the Avalonia desktop GUI
- the install wizard
- full-build validation against `mod-builds`
- any manual test that requires a real desktop session instead of pure headless tests

Start with:

- `docs/local_desktop_agent_runbook.md`
- `.cursor/skills/local_desktop_gui_testing/SKILL.md`
- `.cursor/skills/full_build_install_validation/SKILL.md`

## Verified local desktop baseline

These steps were verified in a Linux desktop VM similar to the one used during development:

- OS: Ubuntu 24.04 x64
- .NET SDK: 9.0.x
- GUI display: X11 desktop session with `DISPLAY=:1`
- App launch style: prebuilt DLL with CLI preload args, not file pickers

Verified preload flags:

- `--instructionFile=<path>`
- `--kotorPath=<path>`
- `--modDirectory=<path>`

The GUI auto-loads the instruction file when those arguments are present.

## Use the repo scripts

Prefer these scripts over ad hoc shell commands:

- `scripts/agents/create_template_kotor_install.sh`
- `scripts/agents/ensure_linux_holopatcher.sh`
- `scripts/agents/launch_gui_desktop.sh`

Example launch (after `mod-builds` exists at repo root and template dirs are created or will be auto-created):

`./scripts/agents/launch_gui_desktop.sh --instruction-file ./mod-builds/TOMLs/KOTOR1_Full.toml --kotor-dir ./tmp/kotor_template --mod-dir ./tmp/mod_downloads`

Clone `mod-builds` at the repo root if missing:

`git clone https://github.com/th3w1zard1/mod-builds ./mod-builds`

Typical local desktop flow:

1. Clone `mod-builds` into the repo root if missing.
2. Create a fake/template KOTOR install and empty mod workspace.
3. Build the GUI project.
4. Ensure Linux `Resources/holopatcher` exists.
5. Launch the GUI with CLI preload args.
6. Drive the wizard manually in the desktop session.

## GUI testing rules

- Anything GUI-facing must be exercised in a real desktop session, not only headless tests.
- Prefer CLI preload args over native file-picker interaction.
- For wizard tests, use the install wizard pages instead of the legacy top-menu flow unless the task explicitly targets the legacy flow.
- Expand validation logs and capture the exact failure text before changing code.
- For full-build tests, clone `mod-builds` to the repo root. The repo expects that location.

## Project-specific wizard control map

### Directory + onboarding flow

- `GettingStartedTab`
  - `Step1ModDirectoryPicker`
  - `Step1KotorDirectoryPicker`
  - `Step2Button` (`📄 Load Instruction File`)
  - `ScrapeDownloadsButton` (`Fetch Downloads`)
  - `OpenModDirectoryButton`
  - `DownloadStatusButton`
  - `StopDownloadsButton`
  - `ValidateButton` (`🔍 Validate`)

### Install wizard flow

Wizard pages are created in this order:

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

Widescreen-only pages are added dynamically after the base install when needed.

### Key wizard controls

- `ModSelectionPage`
  - `SelectAllButton`
  - `DeselectAllButton`
  - `SelectByTierButton`
  - `SelectByCategoryButton`
  - `SearchTextBox`
  - `CategoryFilterComboBox`
  - `TierFilterComboBox`
  - `SpoilerFreeToggle`
  - `ExpandCollapseAllButton`
- `ValidatePage`
  - `ValidateButton` (`🔍 Run Validation`)
  - `ValidationProgress`
  - `StatusText`
  - `LogExpander`
  - `LogText`
  - `SummaryText`
  - `SummaryDetails`
  - `ErrorCountBadge`
  - `WarningCountBadge`
  - `PassedCountBadge`
- `DownloadsExplainPage`
  - background downloads continue while the wizard advances
- `InstallStartPage`
  - review page before the real install begins

## Full-build workflow expectation

For `KOTOR1_Full.toml` / `KOTOR2_Full.toml` tests:

1. Launch the GUI with CLI preload args.
2. Go to `ModSelectionPage`.
3. Click `SelectAllButton`.
4. Go to the downloads step and click `Fetch Downloads`.
5. Open download status if needed.
6. Run validation from `ValidatePage`.
7. Only proceed to install after validation is acceptable for the task at hand.

## Linux-specific note

The plain Debug output can run the GUI, but local Linux validation/install checks may still require:

- `scripts/agents/ensure_linux_holopatcher.sh`

That script links the bundled Linux HoloPatcher binary into the `Resources` folder expected by the app.

## When new local runbook knowledge is discovered

Update all of the following together:

- `docs/local_desktop_agent_runbook.md`
- the relevant `.cursor/skills/*/SKILL.md`
- `.cursorrules` if the rule should always apply
- `.cursor/mcp.json` or the wrapper scripts if agent tooling changed
- `.vscode/tasks.json` / `.vscode/launch.json` if the launch flow changed

## Cursor Cloud specific instructions

### Overview

KOTORModSync is a cross-platform multi-mod installer for Star Wars: KOTOR, built with C#/.NET 9.0 and AvaloniaUI. The solution (`KOTORModSync.sln`) contains three projects: `KOTORModSync.Core` (library), `KOTORModSync.GUI` (desktop app), and `KOTORModSync.Tests` (NUnit + xUnit tests).

### Prerequisites (installed via VM snapshot)

- .NET 9.0 SDK at `$HOME/.dotnet` (ensure `DOTNET_ROOT` and `PATH` include it)
- PowerShell (`pwsh`) for running tests per `.cursorrules` conventions
- X11 libraries for AvaloniaUI rendering (see README for list)
- Git submodules: `src/AvRichTextBox` and `src/RtfDomParserAvalonia` are initialized; `vendor/HoloPatcher.NET` is unavailable (private/not-found repo) but not required for building or testing the main solution

### Build, Test, Lint, Run

- **Build**: `dotnet build KOTORModSync.sln --configuration Debug` from repo root
- **Run GUI**: `dotnet run --project src/KOTORModSync.GUI/KOTORModSync.csproj --configuration Debug --framework net9.0` (must specify `--framework net9.0` since Debug can multi-target)
- **Lint**: `dotnet format KOTORModSync.sln --verify-no-changes` (pre-existing formatting diffs exist)
- **Tests**: See `.cursorrules` for the required PowerShell-based test runner pattern. Quick non-long-running test run: `dotnet test src/KOTORModSync.Tests/KOTORModSync.Tests.csproj --filter "FullyQualifiedName!~LongRunning&FullyQualifiedName!~GitHubRunnerSeeding&FullyQualifiedName!~DistributedCache" --configuration Debug`
- **Distributed cache tests**: `dotnet test KOTORModSync.Tests/KOTORModSync.Tests.csproj --filter "FullyQualifiedName~DistributedCache&FullyQualifiedName!~LongRunning&FullyQualifiedName!~GitHubRunnerSeeding"` (run from `src/` dir or adjust path)

### Non-obvious gotchas

- The `DISPLAY=:1` environment variable must be set for the AvaloniaUI GUI to render on the VM's virtual display.
- `CrossPlatformFileWatcherTests` fail in the cloud VM due to container filesystem inotify limitations; this is expected.
- Some xUnit-based UI tests may fail headlessly depending on Avalonia headless support; these are pre-existing.
- The NuGet config (`NuGet.config`) includes a GitHub Packages feed (`github-th3w1zard1`). Public packages restore without auth; if private packages are added, a GitHub PAT may be needed.
- `vendor/HoloPatcher.NET` submodule references a repo that currently returns 404. The build succeeds without it (only used in optional PostBuild copy targets).
