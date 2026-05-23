# KOTORModSync Copilot Instructions

## Build, test, and lint

```bash
dotnet build KOTORModSync.sln --configuration Debug

dotnet run --project src/KOTORModSync.GUI/KOTORModSync.csproj --configuration Debug --framework net9.0

dotnet test src/KOTORModSync.Tests/KOTORModSync.Tests.csproj --filter "FullyQualifiedName!~LongRunning" --configuration Debug

dotnet test src/KOTORModSync.Tests/KOTORModSync.Tests.csproj --filter "FullyQualifiedName=KOTORModSync.Tests.MainWindowHeadlessTests.LandingPage_Shows_When_NoInstructionsLoaded" --configuration Debug

dotnet format KOTORModSync.sln --verify-no-changes
```

For normal test execution, use the plain `dotnet test` commands above. `.cursorrules` and `AGENTS.md` also include a PowerShell timeout wrapper for classifying a single named test's duration so agents can tell the difference between a normal test and a `LongRunning` test.

Debug builds and test runs assume the .NET 9 toolchain described in `AGENTS.md`. If the environment also has a system `dotnet`, make sure commands are using the repo's intended .NET 9 SDK/runtime before trusting test results. `dotnet format` is a real repo command, but the repo currently has pre-existing analyzer/format warnings.

## Default inference path

- Treat `.github/copilot-instructions.md`, `AGENTS.md`, and `.cursorrules` as the source-of-truth trio for initial routing. Read those before asking broad "where should I start?" questions.
- If the task touches `src/KOTORModSync.Core`, `src/KOTORModSync.Tests`, repo-root config/docs, or build/test/lint behavior, default to the headless .NET workflow and start from the repo-root commands above.
- If the task touches `src/KOTORModSync.GUI`, wizard pages, `scripts/agents/`, or full-build/manual validation, default to the GUI workflow in `AGENTS.md` and `docs/local_desktop_agent_runbook.md`. Prefer helper scripts and CLI preload args before trying file-picker automation.
- If the task touches `telemetry-auth/`, treat it as the Python/Docker sidecar with its own README, CONTRIBUTING, deployment docs, and GitHub workflows instead of routing through the Avalonia app workflow.
- Ask or stop only when a real prerequisite is missing: no `./mod-builds` for full-build work, no desktop/X11 for required GUI validation, missing credentials/secrets/manual external approval, or conflicting local changes that block a safe edit.
- Prefer the repo's existing MCP wrappers in `mcp.json` and `.cursor/mcp.json` over inventing repo-specific server commands.

## High-level architecture

- `src/KOTORModSync.Core` is the main runtime. It owns the instruction model (`Instruction`, `ModComponent`), dependency resolution, path handling, validation, install orchestration, checkpointing, download services, and HoloPatcher/Python integration.
- Instruction files are loaded through `src/KOTORModSync.Core/Services/FileLoadingService.cs` and `src/KOTORModSync.Core/Services/ModComponentSerializationService.cs`. The loader auto-detects TOML, Markdown, YAML, and JSON, then resolves component dependency order before the GUI works with the result.
- Dry-run validation and pre-install analysis are built around `src/KOTORModSync.Core/Services/FileSystem/VirtualFileSystemProvider.cs`. It simulates file mutations against current disk state, and that virtual path is the expected flow for validation-style work.
- Real installs route through `src/KOTORModSync.Core/Services/InstallationService.cs`, which also bootstraps the embedded Python environment used by the patching stack.
- `src/KOTORModSync.GUI` is the Avalonia desktop shell. `MainWindow` composes many focused GUI services under `src/KOTORModSync.GUI/Services`, while the install wizard lives under `src/KOTORModSync.GUI/Dialogs/WizardPages`.
- CLI preload args (`--instructionFile`, `--kotorPath`, `--modDirectory`) are the supported way to launch the GUI into a known state for local validation and agent-driven runs.
- `src/KOTORModSync.Tests` is the only automated test project. It covers both core logic and Avalonia headless UI flows.
- `vendor/KPatcher` is the canonical upstream patcher source, but it is a git submodule and may need `git submodule update --init --recursive` before inspection. The `src/HoloPatcher*` trees still exist in the solution, but the repo docs say not to treat them as the primary upstream for new patcher work.

## Key conventions

- Instruction definitions are path-sandboxed. In instruction content, Source/Destination paths must start with `<<modDirectory>>` or `<<kotorDirectory>>`; do not introduce absolute paths into TOML/YAML/Markdown instruction files.
- If you touch validation, dry-run, or download-analysis logic, keep it on the virtual file system path. Repo guidance explicitly says not to use the real file system provider for validation/analysis flows.
- All automated tests belong in `src/KOTORModSync.Tests`; do not add more test projects. Tests that take more than 2 minutes should use the `LongRunning` suffix and are excluded from standard runs.
- GUI-facing changes are not considered fully validated by headless tests alone. For local/manual flows, prefer the helper scripts in `scripts/agents/` and the wizard flow documented in `AGENTS.md` and `docs/local_desktop_agent_runbook.md`.
- Full-build/manual GUI work expects `mod-builds` cloned at the repo root as `./mod-builds`, and preload args are preferred over native file-pickers.
- The `telemetry-auth/` directory is a separate Python/Docker sidecar. Do not assume the repo-root .NET commands or Avalonia runbook apply there.
- In Avalonia XAML, do not hardcode font/style/color properties on controls unless absolutely necessary; rely on the implicit theme defaults.
- The repo already ships MCP wrapper configs in `mcp.json` and `.cursor/mcp.json` for `repo-filesystem`, `desktop-commander`, and `playwright`; prefer those wrappers over inventing repo-specific server commands.
- Repo docs call out a few environment-specific gotchas that are worth preserving in agent work: `CrossPlatformFileWatcherTests` are expected to fail in the cloud VM, some UI tests only make sense in a real desktop session, and Linux GUI validation may require `scripts/agents/ensure_linux_holopatcher.sh`.
