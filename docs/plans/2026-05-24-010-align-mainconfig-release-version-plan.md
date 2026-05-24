---
title: Align MainConfig.CurrentVersion with Release Please manifest
type: fix
status: shipped
date: 2026-05-24
---

# Align MainConfig.CurrentVersion with Release Please manifest

## Summary

After shipping 2.1.0 (plan 009), `MainConfig.CurrentVersion` still reads `2.1.0a1` while `.release-please-manifest.json` and both `Info.plist` files are `2.1.0`. Align the in-app version string and add a guard test so future releases stay consistent.

---

## Problem Frame

Release Please generic extra-file updater preserved the legacy `a1` pre-release suffix when bumping from `2.0.0a1`. Users see `KOTORModSync v2.1.0a1` in the window title while the shipped release is 2.1.0.

---

## Requirements

- R1. `MainConfig.CurrentVersion` equals `2.1.0` (matches manifest).
- R2. Version string uses semver without stale alpha suffix unless manifest explicitly includes pre-release.
- R3. Automated test reads `.release-please-manifest.json` and asserts parity with `MainConfig.CurrentVersion`.
- R4. Existing tests pass (`FullyQualifiedName!~LongRunning`).

---

## Implementation Units

### U1. Fix MainConfig version literal

**Files:** `src/KOTORModSync.Core/MainConfig.cs`

Change `"2.1.0a1"` → `"2.1.0"`; keep `// x-release-please-version` marker.

### U2. Add manifest parity test

**Files:** `src/KOTORModSync.Tests/ReleaseVersionAlignmentTests.cs`

Parse manifest JSON at repo root; assert `MainConfig.CurrentVersion` matches `"."` package version.

---

## Verification

- `MainConfig.CurrentVersion` is `2.1.0`
- `dotnet test src/KOTORModSync.Tests/KOTORModSync.Tests.csproj --filter "FullyQualifiedName~ReleaseVersionAlignment"`
- Release Please workflow remains green on push

---

## Scope Boundaries

- Does not change HoloPatcher vendored version strings.
- Does not alter release-please-config generic updater behavior beyond clean semver in MainConfig.
