---
title: Fix Release Please CI failure (unsupported plist extraFile)
type: fix
status: shipped
date: 2026-05-24
---

# Fix Release Please CI failure (unsupported plist extraFile)

## Summary

Restore green **Release Please** workflow on `master` by fixing `release-please-config.json`: remove the unsupported `plist` extra-file type (not valid for `release-type: simple`), correct the `MainConfig.cs` path, and keep macOS version bumps via `generic` extra-files plus existing `build-and-release.yml` plist step.

---

## Problem Frame

`Release Please` fails on every `master` push with `release-please failed: unsupported extraFile type: plist`. D-Bus remediation and verification docs are shipped; the next high-value bare `/lfg` slice is unblocking release automation.

---

## Requirements

- R1. `release-please-config.json` must not use unsupported `plist` extra-file type.
- R2. `MainConfig.cs` extra-file path must resolve to `src/KOTORModSync.Core/MainConfig.cs`.
- R3. macOS bundle metadata paths (`Info.plist`, `src/KOTORModSync.GUI/Info.plist`) remain versioned via supported mechanisms (`generic` extra-files and/or release workflow).
- R4. Local or CI validation confirms Release Please no longer errors on config parse.

---

## Implementation Units

### U1. Fix release-please-config.json

**Files:** `release-please-config.json`

Remove `plist` entry; fix `MainConfig.cs` path; use `xml` + xpath for both Info.plist version keys.

### U3. Replace plist generic extra-files with xml xpath updaters

**Files:** `release-please-config.json`

Use `type: xml` with xpath for `CFBundleShortVersionString` and `CFBundleVersion` in both plist paths.

### U2. Validate Release Please config

**Commands:** `npx release-please manifest-pr --dry-run` or inspect CI run on push.

---

## Verification

- Release Please workflow completes without `unsupported extraFile type: plist`
- `src/KOTORModSync.Core/MainConfig.cs` path present in extra-files
