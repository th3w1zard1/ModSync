# Local desktop GUI testing

## When to use

Use this skill when a task needs a real desktop session or VM to:

- launch the Avalonia app
- click through the install wizard
- verify button/label behavior
- test downloads, validation, or install flow manually

Do not use this skill for pure parser / serialization / core-only work.

## Read first

- `AGENTS.md`
- `docs/local_desktop_agent_runbook.md`

## Verified local launch pattern

Prefer the repo helper script:

`./scripts/agents/launch_gui_desktop.sh --instruction-file ./mod-builds/TOMLs/KOTOR1_Full.toml --kotor-dir ./tmp/kotor_template --mod-dir ./tmp/mod_downloads`

Why:

- avoids native file pickers
- preloads the instruction file
- preloads both directories
- is close to the VM workflow already exercised against this repo

## Required local setup

1. Clone `mod-builds` to repo root if missing.
2. Run:
   - `./scripts/agents/create_template_kotor_install.sh ./tmp/kotor_template ./tmp/mod_downloads`
3. On Linux, run:
   - `./scripts/agents/ensure_linux_holopatcher.sh`
4. Launch the GUI with `scripts/agents/launch_gui_desktop.sh`.

## Preload CLI args

The app accepts:

- `--instructionFile=<path>`
- `--kotorPath=<path>`
- `--modDirectory=<path>`

Local agents should use these flags instead of interacting with file dialogs.

## Wizard page order

The install wizard is built in this order:

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

Widescreen pages are injected later if needed.

## Control map for manual agents

### `ModSelectionPage`

- `SelectAllButton`
- `DeselectAllButton`
- `SelectByTierButton`
- `SelectByCategoryButton`
- `SearchTextBox`
- `CategoryFilterComboBox`
- `TierFilterComboBox`
- `SpoilerFreeToggle`
- `ExpandCollapseAllButton`

### `ValidatePage`

- `ValidateButton`
- `ValidationProgress`
- `LogExpander`
- `LogText`
- `SummaryText`
- `SummaryDetails`
- `ErrorCountBadge`
- `WarningCountBadge`
- `PassedCountBadge`

### `GettingStartedTab`

- `Step1ModDirectoryPicker`
- `Step1KotorDirectoryPicker`
- `Step2Button`
- `ScrapeDownloadsButton`
- `DownloadStatusButton`
- `StopDownloadsButton`
- `ValidateButton`

## High-signal local manual test flow

1. Launch the GUI with preload args.
2. Advance to `ModSelectionPage`.
3. Use `SelectAllButton` if the task is a full-build or stress flow.
4. Advance to downloads and click `Fetch Downloads`.
5. Advance to `ValidatePage`.
6. Click `Run Validation`.
7. Expand the validation log and capture the exact summary.
8. Only continue to install once the validation state is acceptable for the task.

## Good evidence to collect

- screenshot of the loaded wizard page
- screenshot showing selected mod count
- screenshot showing validation results
- screenshot of any blocking dialog
- terminal log from the launch command

## Known local Linux caveat

If validation says HoloPatcher is missing, use:

`./scripts/agents/ensure_linux_holopatcher.sh`

Do not spend time on file-picker automation first. Fix the resource path and relaunch with preload args.

## After discovering new GUI behavior

Update:

- `docs/local_desktop_agent_runbook.md`
- this skill
- `.cursorrules` if the rule should always apply
- `.vscode/tasks.json` / `.vscode/launch.json` if launch flow changed
