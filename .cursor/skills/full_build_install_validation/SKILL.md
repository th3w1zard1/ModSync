# Full build install validation

## When to use

Use this skill when the task explicitly involves:

- `mod-builds`
- `KOTOR1_Full.toml`
- `KOTOR2_Full.toml`
- full import / full-build validation
- select-all mod install testing
- proving that the dry-run / VFS step makes sense before a real install

## Read first

- `docs/local_desktop_agent_runbook.md`
- `.cursor/skills/local_desktop_gui_testing/SKILL.md`

## Required repo state

- repo root contains `./mod-builds`
- local test directories exist or can be created
- the GUI is launched with preload args

## Preparation

1. Clone `mod-builds` to repo root if needed:
   - `git clone https://github.com/th3w1zard1/mod-builds ./mod-builds`
2. Create disposable directories:
   - `./scripts/agents/create_template_kotor_install.sh ./tmp/kotor_template ./tmp/mod_downloads`
3. On Linux, ensure the HoloPatcher resource path exists:
   - `./scripts/agents/ensure_linux_holopatcher.sh`
4. Launch the GUI:
   - `./scripts/agents/launch_gui_desktop.sh --instruction-file ./mod-builds/TOMLs/KOTOR1_Full.toml --kotor-dir ./tmp/kotor_template --mod-dir ./tmp/mod_downloads`

## Full-build GUI workflow

1. Let the wizard auto-load the TOML.
2. Go to `ModSelectionPage`.
3. Click `SelectAllButton`.
4. Confirm the selected count is the expected full-build count.
5. Advance to the downloads step.
6. Click `Fetch Downloads`.
7. Wait until download progress stabilizes.
8. Advance to `ValidatePage`.
9. Click `Run Validation`.
10. Expand `LogExpander`.
11. Review:
   - environment result
   - install-order result
   - dry-run / VFS issues
12. Only continue to install if the current task expects a real install and validation is acceptable.

## What to record

- selected mod count
- whether downloads were already present or newly fetched
- validation pass / fail summary
- whether `Next` is blocked by critical errors
- install completion or the exact blocking dialog

## Project-specific behavior that matters

- `mod-builds` provides metadata and instructions; archives are fetched from third-party sources.
- The wizard download step is not just documentation; it really kicks off background downloads.
- The validation page is the main user-facing entrypoint for the dry-run / VFS check.
- `MainConfig` path placeholders still matter even in GUI-driven full-build flows:
  - `<<modDirectory>>`
  - `<<kotorDirectory>>`

## Interpreting validation results

### Good sign

- environment passes
- install order passes
- dry-run issues are empty or limited to acceptable warnings for the task

### Bad sign

- HoloPatcher missing from `Resources`
- blocking extraction pattern failures for selected mods
- critical errors that stop the wizard from advancing

## Preferred local baseline

Use a Linux desktop VM or workstation with:

- .NET 9.x
- X11 / desktop display
- repo-local `mod-builds`
- helper scripts from `scripts/agents/`

This is the closest local reproduction of the validated VM-style workflow already exercised for this repo.

## Update rule

Whenever a better full-build workflow is discovered, update:

- this skill
- `docs/local_desktop_agent_runbook.md`
- `AGENTS.md`
- `.cursorrules`
