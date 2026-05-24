---
name: cloud-agents-starter
description: Minimal runbook for Cloud agents - how to run, test, and configure this codebase. Use when setting up environments, executing tests, or resolving common workflow issues.
---

# KOTORModSync Cloud Agents Starter Skill

A minimal runbook for Cloud agents covering practical setup, execution, and testing. Organized by codebase area.

**Knowledgebase:** [docs/knowledgebase/README.md](../../../docs/knowledgebase/README.md) — index, [core-cli-reference.md](../../../docs/knowledgebase/core-cli-reference.md), [agent-action-parity.md](../../../docs/knowledgebase/agent-action-parity.md).

**Quick scripts:** `./scripts/agents/run_headless_tests.sh`, `./scripts/agents/cli_validate.sh` (see [scripts/agents/README.md](../../../scripts/agents/README.md)).

---

## 1. Initial Setup

**Verify environment:**
```bash
git status -sb
dotnet --info          # must be 9.0.x
gh auth status         # read-only GitHub CLI usage
```

**Vendor dependencies (required before first build):**
```bash
git submodule update --init --recursive
```

**NuGet sources:** `NuGet.config` at repo root. Only nuget.org is configured (the GitHub Packages feed `github-th3w1zard1` was removed in PR #65). No auth needed for public package restore.

---

## 2. Build

```bash
# Full solution
dotnet build KOTORModSync.sln --configuration Debug

# GUI project only
dotnet build src/KOTORModSync.GUI/KOTORModSync.csproj

# Core CLI project only (also usable standalone via dotnet run)
dotnet build src/KOTORModSync.Core/KOTORModSync.Core.csproj -f net9.0
```

---

## 3. GUI Application (`src/KOTORModSync.GUI`)

**Entry point:** `src/KOTORModSync.GUI/Program.cs` (Avalonia UI)

**Cloud agents are headless — do NOT run the GUI.** Use automated tests instead.

For local desktop runs (requires `DISPLAY=:1`):
```bash
dotnet run --project src/KOTORModSync.GUI/KOTORModSync.csproj --configuration Debug --framework net9.0
```

**Preload CLI args** (avoid file-picker interaction):
```bash
dotnet run ... -- \
  --instructionFile=./mod-builds/TOMLs/KOTOR1_Full.toml \
  --kotorPath=./tmp/kotor_template \
  --modDirectory=./tmp/mod_downloads
```

**Helper scripts (prefer these over ad hoc commands):**
```bash
./scripts/agents/create_template_kotor_install.sh ./tmp/kotor_template ./tmp/mod_downloads
./scripts/agents/ensure_linux_holopatcher.sh
./scripts/agents/launch_gui_desktop.sh \
  --instruction-file ./mod-builds/TOMLs/KOTOR1_Full.toml \
  --kotor-dir ./tmp/kotor_template \
  --mod-dir ./tmp/mod_downloads
```

---

## 4. Core CLI (`src/KOTORModSync.Core`)

**Common verbs:** `convert`, `merge`, `validate`, `install`, `set-nexus-api-key`, `install-python-deps`, `holopatcher`.

```bash
dotnet run --project src/KOTORModSync.Core/KOTORModSync.Core.csproj -f net9.0 -- \
  validate -i path/to/instructions.toml
```

**Best-effort install (CI automation):**
```bash
./scripts/agents/install_best_effort.sh ./mod-builds/TOMLs/KOTOR1_Full.toml ./tmp/game ./tmp/mods
```

See [core-cli-reference.md](../../../docs/knowledgebase/core-cli-reference.md) for flags.

---

## 5. Testing

**All tests live in one project:** `src/KOTORModSync.Tests/KOTORModSync.Tests.csproj`  
**Do not create additional test projects.**

### Standard Cloud / headless test run

```bash
./scripts/agents/run_headless_tests.sh
```

Or explicitly:
```bash
dotnet test src/KOTORModSync.Tests/KOTORModSync.Tests.csproj \
  --filter "FullyQualifiedName!~LongRunning&FullyQualifiedName!~GitHubRunnerSeeding&FullyQualifiedName!~DistributedCache" \
  --configuration Debug
```

### Distributed cache tests (separate filter required)

```bash
dotnet test src/KOTORModSync.Tests/KOTORModSync.Tests.csproj \
  --filter "FullyQualifiedName~DistributedCache&FullyQualifiedName!~LongRunning&FullyQualifiedName!~GitHubRunnerSeeding"
```

### Single test with 120-second timeout (PowerShell — required by .cursorrules)

```pwsh
pwsh -Command '& {
  $proj = "src/KOTORModSync.Tests/KOTORModSync.Tests.csproj"
  $args = "test {0} --filter ""FullyQualifiedName~<TestName>"" --list-tests" -f $proj
  $psi = New-Object System.Diagnostics.ProcessStartInfo
  $psi.FileName = "dotnet"
  $psi.Arguments = $args
  $psi.RedirectStandardOutput = $true
  $psi.RedirectStandardError = $true
  $psi.UseShellExecute = $false
  $process = New-Object System.Diagnostics.Process
  $process.StartInfo = $psi
  $null = $process.Start()
  if (-not $process.WaitForExit(120000)) {
    $stdout = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    $process.Kill()
    $process.WaitForExit()
    Write-Output $stdout
    Write-Output $stderr
    Write-Output "--- COMMAND TIMED OUT AFTER 120s ---"
  } else {
    $stdout = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    Write-Output $stdout
    Write-Output $stderr
  }
}'
```

### Test naming conventions

| Suffix | Purpose | Max duration |
|--------|---------|--------------|
| `GitHubRunnerSeeding` | GitHub Actions seeding only | 5–6 hours |
| `LongRunning` | Local long tests, not for GitHub runners | >2 minutes |
| (none) | Regular tests | <2 minutes |

### Known expected failures on Cloud VMs

- `CrossPlatformFileWatcherTests` — inotify not available in container FS; pre-existing, ignore.
- Some Avalonia headless xUnit tests — headless display support limitations; pre-existing, ignore.

### Tests by codebase area

| Area | Filter |
|------|--------|
| Path sandboxing / zip-slip | `FullyQualifiedName~PathHelperArchiveExtractionTests` |
| URL scheme security | `FullyQualifiedName~UrlUtilitiesSecurityTests` |
| Settings file permissions | `FullyQualifiedName~SettingsFilePermissionsTests` |
| Markdown renderer | `FullyQualifiedName~MarkdownRendererPlainTextTests` |
| Install coordinator | `FullyQualifiedName~InstallCoordinatorTests` |
| Download queue | `FullyQualifiedName~DownloadQueueHeadlessTests` |
| Auto-update service | `FullyQualifiedName~AutoUpdateServiceTests` |
| Telemetry | `Name~ComputeSignature_SameInputs_ReturnsDeterministicLowercaseHex` |

---

## 6. Feature Flags & Build Constants

No runtime feature-flag system. Behavior is controlled by build-time preprocessor defines.

| Define | Effect |
|--------|--------|
| `OFFICIAL_BUILD` | Enables embedded telemetry signing key in release builds |

```bash
# Enable OFFICIAL_BUILD (release pipeline only — never for dev/PR builds)
dotnet build -c Release /p:DefineConstants="OFFICIAL_BUILD"
```

---

## 7. Auth / Telemetry

**No user login.** Telemetry uses HMAC auth. Defaults to **disabled** until the user explicitly consents on first run.

**Client signing secret (order of precedence):**
1. `KOTORMODSYNC_SIGNING_SECRET` env var
2. `{ApplicationData}/KOTORModSync/telemetry.key`
3. `EmbeddedSecrets.TELEMETRY_SIGNING_KEY` (only when `OFFICIAL_BUILD`; file is gitignored)

**Telemetry consent:** Must be `IsEnabled=true` AND `UserConsented=true` in `telemetry_config.json`. Default is both `false`.

---

## 8. Environment Variables

| Variable | Purpose |
|----------|---------|
| `KOTORMODSYNC_SIGNING_SECRET` | Telemetry HMAC signing secret |
| `KOTORMODSYNC_RELAY_CREDENTIAL` | Relay auth credential (format: `user:pass`); default falls back to `admin:adminadmin` for local test Docker |
| `KOTOR_MODSYNC_NEXUS_API_KEY` | Nexus Mods API key (also: `NEXUS_MODS_API_KEY`) |
| `REQUIRE_AUTH=false` | Disable auth validation in telemetry-auth service (local testing) |
| `NCS_INTERPRETER_DEBUG` | HoloPatcher NCS interpreter debug (`true` to enable) |
| `PYTHON_KEYRING_BACKEND` | Set to `keyring.backends.null.Keyring` to avoid pip hangs |
| `DISPLAY` | Set to `:1` for Linux desktop GUI; do NOT clear/empty it (needed by child tools) |

---

## 9. Configuration Files

| Path | Purpose |
|------|---------|
| `{ApplicationData}/KOTORModSync/settings.json` | GUI preferences, paths, Nexus API key |
| `{ApplicationData}/KOTORModSync/telemetry_config.json` | Telemetry options |
| `{ApplicationData}/KOTORModSync/telemetry.key` | Signing secret fallback |
| `NuGet.config` | Package sources (nuget.org only) |

---

## 10. Security Notes for Code Changes

When modifying auth, settings, or telemetry code, keep these invariants:

- **Never log the full settings JSON**: `LoadSettings` logs the raw JSON at verbose level which includes `nexusModsApiKey`. When adding new secret-bearing fields to `AppSettings`, strip them from the verbose log.
- **Never embed secrets in log messages**: Only log that a key was loaded, never its value.
- **URL scheme allow-list**: `UrlUtilities.OpenUrl` and `OpenLinkConverter` only allow `https://`, `http://`, and `mailto:`. Do not add new schemes without security review.
- **Archive entry paths**: All archive extraction must go through `PathHelper.TryGetZipSafeArchiveEntryExtractPath` to prevent zip-slip.
- **SFX execution guard**: Before executing an `.exe` as a 7z SFX, verify it has the MZ PE header via `ArchiveHelper.IsPotentialSevenZipSFX`.
- **Telemetry consent**: `TelemetryService.Initialize()` requires both `IsEnabled` and `UserConsented` to be true.
- **Relay credential**: Production relay deployments must set `KOTORMODSYNC_RELAY_CREDENTIAL`. The fallback `admin:adminadmin` is for local Docker containers only.
- **Settings file permissions**: On Unix, `settings.json` must be `chmod 600` (owner-read/write only) to protect the Nexus API key.

---

## 11. Quick Reference

| Task | Command |
|------|---------|
| Init submodules | `git submodule update --init --recursive` |
| Build solution | `dotnet build KOTORModSync.sln --configuration Debug` |
| Run Core CLI | `dotnet run --project src/KOTORModSync.Core/KOTORModSync.Core.csproj -f net9.0 -- <verb>` |
| Headless tests (wrapper) | `./scripts/agents/run_headless_tests.sh` |
| Run standard tests | `dotnet test src/KOTORModSync.Tests/KOTORModSync.Tests.csproj --filter "FullyQualifiedName!~LongRunning&FullyQualifiedName!~GitHubRunnerSeeding&FullyQualifiedName!~DistributedCache"` |
| Run DistCache tests | `dotnet test src/KOTORModSync.Tests/KOTORModSync.Tests.csproj --filter "FullyQualifiedName~DistributedCache&FullyQualifiedName!~LongRunning&FullyQualifiedName!~GitHubRunnerSeeding"` |
| Release build (with telemetry) | `dotnet build -c Release /p:DefineConstants="OFFICIAL_BUILD"` |

---

## 12. Updating This Skill

When you discover new runbook steps or testing tricks:

1. **Edit this file:** `.cursor/skills/cloud-agents-starter/SKILL.md`
2. **Update the right section** with concrete, copy-pasteable commands.
3. **Mirror changes** to `docs/knowledgebase/README.md`, `docs/local_desktop_agent_runbook.md`, and related skills.
4. **If it's a hard rule for all tasks**, also add it to `.cursorrules`.
5. **Commit** so future Cloud agent runs pick it up automatically.

Keep it minimal — only practical, runnable instructions. No theory.
