---
title: Remediate HoloPatcher sibling Avalonia D-Bus path
type: fix
status: shipped
date: 2026-05-24
---

# Remediate HoloPatcher sibling Avalonia D-Bus path

## Summary

Clear the deferred `Tmds.DBus.Protocol 0.21.2` finding on sibling HoloPatcher projects by uplifting their Avalonia package set to **11.3.16** (matching the shipped `KOTORModSync.GUI` remediation) and tightening CI so sibling graphs must stay patched.

---

## Problem Frame

`KOTORModSync.sln` is clean after PR #75, but CI's informational inventory still reports `Tmds.DBus.Protocol 0.21.2` on `src/HoloPatcher/HoloPatcher.csproj` and `src/HoloPatcher.UI/HoloPatcher.UI/HoloPatcher.UI.csproj` via Avalonia 11.3.9 → FreeDesktop.

---

## Requirements

- R1. Both HoloPatcher sibling projects must stop resolving `Tmds.DBus.Protocol 0.21.2` on `net9.0` restore graphs.
- R2. Avalonia package versions must align with the in-repo `KOTORModSync.GUI` pattern (11.3.16 core, ItemsRepeater 11.1.5, ReactiveUI 11.3.9 where no 11.3.16 package exists).
- R3. CI must fail if sibling HoloPatcher graphs reintroduce vulnerable `Tmds.DBus.Protocol` (promote inventory from informational to gated).
- R4. Scope stays limited to sibling HoloPatcher csproj package/CI changes — no HoloPatcher source refactors or broken legacy project-reference repairs unless required for restore audit.

---

## Implementation Units

### U1. Uplift HoloPatcher Avalonia packages

**Files:** `src/HoloPatcher/HoloPatcher.csproj`, `src/HoloPatcher.UI/HoloPatcher.UI/HoloPatcher.UI.csproj`

### U2. Gate sibling graphs in CI

**Files:** `.github/workflows/build-and-test.yml`

---

## Verification

- `dotnet list src/HoloPatcher/HoloPatcher.csproj package --vulnerable --include-transitive` — no findings on net9
- `dotnet list src/HoloPatcher.UI/HoloPatcher.UI/HoloPatcher.UI.csproj package --vulnerable --include-transitive` — no findings on net9

---

## Verification Log (2026-05-24)

| Check | Result |
|-------|--------|
| `dotnet list KOTORModSync.sln package --vulnerable --include-transitive` | No vulnerable packages on Core, GUI, or Tests |
| HoloPatcher sibling restore + `dotnet list --vulnerable --include-transitive` | Clean on `HoloPatcher` and `HoloPatcher.UI` |
| `dotnet nuget why` | `Tmds.DBus.Protocol 0.21.3` via Avalonia.FreeDesktop (patched line) |
| PR #76 CI | All checks green before merge |
| Merge | Squash merged to `master` as `7b81c80` via PR #76 |
| Master CI post-merge | Verified green on merge push |
