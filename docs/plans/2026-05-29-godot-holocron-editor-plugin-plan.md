# Plan: Godot Holocron Editor Plugin (KOTOR File Editing)

**Date:** 2026-05-29  
**Status:** Phase 0 in progress  
**Ideation:** `docs/ideation/2026-05-29-godot-holocron-editor-plugin.md`  
**Reference:** [OpenKotOR/HolocronToolset](https://github.com/OpenKotOR/HolocronToolset)

## Problem

Modders need a Godot 4 editor plugin that can open, edit, and save KOTOR resources with the breadth of HolocronToolset (~30 file-type editors, installation browsing, container formats). ModSync has C# format code in `src/HoloPatcher/` but no Godot integration and HoloPatcher does not build standalone in this repo today. HolocronToolset is the functional reference implementation (Python/PyKotor/PyQt).

## Scope boundary

### In scope (this program)

- Godot 4 `EditorPlugin` under `tools/godot-holocron/`
- Python format bridge CLI using **PyKotor** (same stack as Holocron)
- Editor registry mirroring Holocron extension → editor mapping
- Phase 0 editors: **TwoDA grid**, **generic GFF tree**, **plain text** (NSS/TXT/TX)
- `probe`, `read`, `write`, `list-installations` bridge commands
- Documentation and automated bridge CLI tests in `KOTORModSync.Tests`

### Out of scope (later phases)

- Full DLG node editor, MDL/BWM 3D viewport, Blender integration
- TSLPatchData generator UI, reference search, diff tools
- C# GDExtension / in-process HoloPatcher (blocked on library extraction)
- Shipping Godot plugin as part of ModSync GUI release

### Honesty on "100% complete"

HolocronToolset represents years of PyQt UI across 30+ editors. This plan delivers **Phase 0 foundation + roadmap** for full parity; complete Holocron equivalence is a **multi-phase program** (see §Phases).

## Architecture decision

| Layer | Choice | Rationale |
|-------|--------|-----------|
| Format I/O | Python CLI → PyKotor | Matches Holocron; proven JSON serialization (KotorMCP); HoloPatcher C# not buildable in-repo |
| Godot UI | GDScript `EditorPlugin` + dock editors | Native Godot editor UX; no Qt embedding |
| Editor pattern | Holocron-style registry + `BaseKotorEditor` | Proven in `PLUGIN.md` / `editor_wiki_mapping.py` |
| Game data | Bridge calls `Installation` API | Same as Holocron resource browser |

```
Godot EditorPlugin (GDScript)
    → FormatBridge.gd (OS.execute python bridge)
        → kotor_format_bridge.py (PyKotor read/write)
            → binary .2da / .gff / .tlk / ...
```

## Requirements traceability

| ID | Requirement | Phase | Verification |
|----|-------------|-------|--------------|
| R1 | Open `.2da` in Godot editor dock, edit cells, save round-trip | 0 | Bridge test + manual Godot |
| R2 | Open GFF-based files in generic tree editor, save | 0 | Bridge test + manual Godot |
| R3 | Open plain text resources (`.nss`, `.txt`) | 0 | Manual Godot |
| R4 | `probe` detects resource type for Holocron editor map | 0 | CLI test |
| R5 | Editor registry lists all Holocron editor targets | 0 | `resource_types.gd` parity table |
| R6 | TLK/SSF/ERF read support | 1 | Bridge + UI |
| R7 | Specialized UTC–UTW GFF templates | 1 | Dedicated editors |
| R8 | DLG node editor | 2 | Holocron parity checklist |
| R9 | MDL/BWM/ARE/GIT level tools | 3 | Holocron parity checklist |

## File layout

```
tools/godot-holocron/
  project.godot
  README.md
  bridge/
    kotor_format_bridge.py      # CLI: probe, read, write, installations
  addons/kotor_holocron/
    plugin.cfg
    kotor_holocron_plugin.gd
    core/
      resource_types.gd         # Extension → editor + ResourceType
      format_bridge.gd          # Subprocess wrapper
      editor_registry.gd
    editors/
      base_kotor_editor.gd
      twoda_editor.gd
      gff_editor.gd
      text_editor.gd
    scenes/
      twoda_editor.tscn
      gff_editor.tscn
      text_editor.tscn

src/KOTORModSync.Tests/
  KotorFormatBridgeCliTests.cs  # subprocess bridge tests (skip if no PyKotor)
```

## Phase 0 implementation units

### Unit 0.1 — Python format bridge

**Files:** `tools/godot-holocron/bridge/kotor_format_bridge.py`

**Commands:**

- `probe <path>` — extension, `resource_type`, suggested `editor_id`, `capabilities`
- `read <path> [--max-depth N] [--max-fields N]` — JSON document to stdout
- `write <path> --input <json_path>` — deserialize JSON, write binary
- `installations` — list detected K1/K2 paths (optional `--scan`)

**Formats (Phase 0):** `twoda`, `gff` (+ GFF subclasses by extension), `text` (nss, txt, ncs disasm deferred)

**Test scenarios (`KotorFormatBridgeCliTests`):**

1. `probe` on sample `.2da` returns `editor_id=twoda`
2. `read` + `write` round-trip on temp 2DA preserves headers/row count
3. `read` on sample GFF returns `root` object
4. Missing file returns non-zero exit + JSON error

### Unit 0.2 — Godot plugin shell

**Files:** `plugin.cfg`, `kotor_holocron_plugin.gd`, `resource_types.gd`, `editor_registry.gd`, `format_bridge.gd`

**Behavior:**

- Register plugin in Project → Plugins
- Menu: **KOTOR → Open Resource…** (file dialog)
- Route extension through registry to editor scene
- Settings: path to Python, path to `kotor_format_bridge.py`, optional `PYTHONPATH` for PyKotor

**Test scenarios:** Manual — open plugin in Godot 4.6+, load sample 2DA/GFF from `mod-builds` or test fixtures.

### Unit 0.3 — TwoDA editor

**Files:** `editors/twoda_editor.gd`, `scenes/twoda_editor.tscn`

- `ItemList`/`Tree` grid: row labels × column headers
- Load via bridge `read`; save via `write`
- Dirty flag + save confirmation

### Unit 0.4 — GFF generic editor

**Files:** `editors/gff_editor.gd`, `scenes/gff_editor.tscn`

- `Tree` with struct/list nesting from JSON `root`
- Leaf value editing (LineEdit) for scalars
- Save reconstructs JSON and calls bridge `write`

### Unit 0.5 — Text editor

**Files:** `editors/text_editor.gd`, `scenes/text_editor.tscn`

- `CodeEdit` for `.nss`, `.txt`, `.tx`, `.ncs` (hex fallback later)

## Holocron editor parity matrix (target state)

| Holocron editor | Extension(s) | Phase | Godot editor |
|-----------------|-------------|-------|--------------|
| TwoDAEditor | 2da | 0 | `twoda_editor` |
| GFFEditor + UTC–UTW | gff, utc, utd, … | 0/1 | `gff_editor` (+ templates) |
| TLKEditor | tlk | 1 | `tlk_editor` |
| ERFEeditor | erf, rim, mod, sav | 1 | `container_browser` |
| NSSEditor | nss | 0 | `text_editor` |
| DLGEditor | dlg | 2 | `dlg_editor` |
| MDLEditor | mdl | 3 | `mdl_editor` / Holocron subprocess |
| … | … | 2–3 | per `editor_wiki_mapping.py` |

## Phases (full program)

1. **Phase 0 (this PR):** Bridge + plugin shell + 2DA + GFF + text + registry + tests
2. **Phase 1:** TLK, SSF, ERF/RIM extract/inject, installation browser, specialized GFF templates
3. **Phase 2:** DLG graph, NSS compile, NCS decompile view, LIP/WAV
4. **Phase 3:** MDL/BWM, ARE/GIT/IFO module tools, diff, TSLPatchData, Holocron subprocess fallback

## Risks

| Risk | Mitigation |
|------|------------|
| PyKotor not on PATH | Document `PYTHONPATH`; plugin settings |
| Godot can't run in CI | Bridge CLI tests in `KOTORModSync.Tests`; skip Godot headless initially |
| GFF JSON round-trip data loss | Use PyKotor `GFF.from_json` / `write_gff`; add round-trip tests |
| Scope creep | Strict phase gates; parity matrix tracks remaining editors |

## Dependencies

- Godot 4.2+ (developed against 4.6)
- Python 3.10+ with PyKotor installed (`pip install -e` from PyKotor repo or PyPI when available)
- Optional: `mod-builds` sample files for manual validation

## Success criteria (Phase 0)

- [ ] `python tools/godot-holocron/bridge/kotor_format_bridge.py probe <file>` works for 2DA/GFF
- [ ] Round-trip 2DA via bridge preserves data (automated test)
- [ ] Godot plugin loads; opens and saves `.2da` and `.utc` (GFF) from file dialog
- [ ] `resource_types.gd` documents all Holocron editor targets with phase tags
- [ ] README in `tools/godot-holocron/` with setup steps
