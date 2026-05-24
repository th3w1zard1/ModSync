# Cloud agent starter

## When to use

Use this skill first when you are new to this repo and need the fastest path to:

- verify environment + auth state
- launch and test the app
- run high-signal tests by codebase area
- apply common environment toggles ("feature flags" / debug switches)

## First 5 minutes (cloud setup)

From repo root:

1. Verify branch + repo state:
   - `git status -sb`
2. Verify GitHub CLI auth (read-only usage in this repo):
   - `gh auth status`
3. Ensure vendor dependencies:
   - `git submodule update --init --recursive`
4. Confirm .NET SDK:
   - `dotnet --info`

Notes:

- There is no in-app username/password login flow for the GUI installer itself.
- "Login" for cloud agents usually means GitHub CLI auth state and access to remotes/packages.

## Common environment toggles (feature-flag-like behavior)

Use these when a task needs behavior changes without code edits:

- `DISPLAY=:1` for Linux desktop GUI runs.
- `REQUIRE_AUTH=false` to disable telemetry-auth request validation for local auth-service testing.
- `KOTORMODSYNC_SIGNING_SECRET=<secret>` to enable signed telemetry auth checks.
- `NCS_INTERPRETER_DEBUG=true` for extra NCS interpreter debug behavior in HoloPatcher code paths.

## Codebase areas and concrete test workflows

## 1) GUI + install wizard (`src/KOTORModSync.GUI`)

Use for Avalonia UI, wizard pages, selection/validation/install flow.

Setup + launch:

1. Clone mod metadata repo at root if missing:
   - `git clone https://github.com/th3w1zard1/mod-builds ./mod-builds`
2. Prepare disposable dirs:
   - `./scripts/agents/create_template_kotor_install.sh ./tmp/kotor_template ./tmp/mod_downloads`
3. Ensure Linux HoloPatcher resource path:
   - `./scripts/agents/ensure_linux_holopatcher.sh`
4. Launch with preload args (avoid file pickers):
   - `./scripts/agents/launch_gui_desktop.sh --instruction-file ./mod-builds/TOMLs/KOTOR1_Full.toml --kotor-dir ./tmp/kotor_template --mod-dir ./tmp/mod_downloads`

Manual validation workflow:

1. Advance to `ModSelectionPage`.
2. Click `SelectAllButton` (for full-build validation scenarios).
3. Continue to downloads page and click `Fetch Downloads`.
4. Continue to `ValidatePage` and click `Run Validation`.
5. Expand validation logs and capture exact failure text before any code changes.

## 2) Core parsing/install logic (`src/KOTORModSync.Core`)

Use for path sandboxing, instruction execution, VFS/dry-run, parser/serialization work.

Targeted tests (examples):

- `dotnet test KOTORModSync.Tests/KOTORModSync.Tests.csproj --filter "FullyQualifiedName~PathSandboxingSecurityTests"`
- `dotnet test KOTORModSync.Tests/KOTORModSync.Tests.csproj --filter "FullyQualifiedName~PathResolutionAndSandboxingTests"`
- `dotnet test KOTORModSync.Tests/KOTORModSync.Tests.csproj --filter "FullyQualifiedName~VirtualFileSystemDryRunValidationTests"`
- `dotnet test KOTORModSync.Tests/KOTORModSync.Tests.csproj --filter "FullyQualifiedName~MarkdownImportTests"`

Rules to preserve:

- Instruction paths must start with `<<modDirectory>>` or `<<kotorDirectory>>`.
- Dry-run/validation logic should use virtual file system flows, not direct real FS validation shortcuts.

## 3) Telemetry auth service (`telemetry-auth/`)

Use when touching request signing, auth middleware behavior, or telemetry auth deployment scripts.

Quick local run:

1. `cd telemetry-auth`
2. `openssl rand -hex 32 > signing_secret.txt`
3. `docker compose up -d`
4. `./scripts/test-auth.sh all`

Common mock/real switch:

- Mock auth off: run service with `REQUIRE_AUTH=false`.
- Real auth on: provide `KOTORMODSYNC_SIGNING_SECRET` (or mounted secret file) and use signed test requests.

## 4) Vendor KPatcher integration (`vendor/KPatcher`)

Use when patcher engine behavior changes or vendor sync introduces regressions.

High-signal check:

- `dotnet test vendor/KPatcher/tests/KPatcher.Tests/KPatcher.Tests.csproj`

If GUI validation reports missing HoloPatcher on Linux, run:

- `./scripts/agents/ensure_linux_holopatcher.sh`

## How to update this skill when new runbook knowledge is found

When you discover a better launch/test/debug workflow:

1. Update this file with the exact new command sequence.
2. Update `docs/local_desktop_agent_runbook.md` with matching detail.
3. Update related skills:
   - `.cursor/skills/local_desktop_gui_testing/SKILL.md`
   - `.cursor/skills/full_build_install_validation/SKILL.md`
4. If it is a hard rule for all tasks, also update `.cursorrules`.

Keep updates minimal, concrete, and copy-pasteable.
