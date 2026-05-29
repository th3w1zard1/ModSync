# KOTOR Holocron — Godot Editor Plugin

Godot 4 **editor plugin** that edits KOTOR 1/2 resources through a **PyKotor** Python bridge—the same format stack used by [HolocronToolset](https://github.com/OpenKotOR/HolocronToolset).

## Prerequisites

- Godot 4.3+ (tested with 4.6)
- Python 3.10+ with **PyKotor** installed (`pip install pykotor` or a local checkout on `PYTHONPATH`)
- Optional: `KOTOR_PYTHON` and `KOTOR_FORMAT_BRIDGE` environment variables

## Layout

```
tools/godot-holocron/
  project.godot              # Open this in Godot (editor project)
  bridge/kotor_format_bridge.py
  addons/kotor_holocron/     # EditorPlugin + editors
```

## Quick start

1. Install PyKotor (or point `PYTHONPATH` at a PyKotor checkout).
2. Open `tools/godot-holocron/project.godot` in Godot.
3. Enable **Project → Project Settings → Plugins → KOTOR Holocron** if not already on.
4. Use the **KOTOR Holocron** dock (left panel): browse or paste a path, click **Open**.

## Bridge CLI

```bash
python3 tools/godot-holocron/bridge/kotor_format_bridge.py probe /path/to/file.2da
python3 tools/godot-holocron/bridge/kotor_format_bridge.py read /path/to/file.utc
python3 tools/godot-holocron/bridge/kotor_format_bridge.py installations
python3 tools/godot-holocron/bridge/kotor_format_bridge.py supported-types
```

## Parity roadmap

HolocronToolset ships ~30 specialized PyQt editors. This plugin implements **Phase 0**:

| Area | Status |
|------|--------|
| PyKotor JSON bridge (probe/read/write/installations) | Done |
| TwoDA table editor | Done |
| GFF / JSON tree editor (all GFF-family extensions) | Done |
| Text editor (NSS, LYT, VIS, …) | Done |
| TLK, SSF, ERF viewers | Read via JSON; dedicated UI in Phase 1 |
| MDL, WAV, TPC, DLG graph | Phase 2–3 |

Full Holocron parity is a **multi-phase program** tracked in `docs/plans/2026-05-29-godot-holocron-editor-plugin-plan.md`.

## Tests

From repo root:

```bash
dotnet test src/KOTORModSync.Tests/KOTORModSync.Tests.csproj --filter "FullyQualifiedName~KotorFormatBridge"
```

Tests skip automatically when PyKotor is not importable.
