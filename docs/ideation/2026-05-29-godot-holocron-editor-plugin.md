# Ideation: Godot Editor Plugin for KOTOR File Editing (Holocron Parity)

**Date:** 2026-05-29  
**Subject:** Godot 4 `EditorPlugin` that can open, edit, and save KOTOR resource files at parity with [OpenKotOR/HolocronToolset](https://github.com/OpenKotOR/HolocronToolset).  
**Mode:** Repo-grounded (ModSync + Holocron reference + PyKotor ecosystem).

## Grounding Context

### Codebase context

- ModSync is a C#/.NET 9 Avalonia mod installer; it embeds `src/HoloPatcher/` format libraries (GFF, 2DA, TLK, NCS, RIM, SSF, etc.) ported from PyKotor.
- HoloPatcher does **not** currently build standalone in this repo (broken project references to `HoloPatcher.UI` paths).
- **No Godot project exists** in ModSync today.
- System PyKotor is available at `../PyKotor` and is the same library HolocronToolset uses.

### External context (HolocronToolset)

- **Stack:** Python 3.8+, PyQt/PySide via `qtpy`, built on **PyKotor**.
- **~30 specialized editors** (`ARE`, `BWM`, `DLG`, `ERF`, `FAC`, generic `GFF`, `GIT`, `IFO`, `JRL`, `LTR`, `LIP`, `MDL`, `NSS`, `NCS`, `PTH`, `SAV`, `SSF`, `TLK`, `TPC`, `TwoDA`, `TXT`, `UTC`–`UTW`, `WAV`, etc.).
- **Plugin pattern:** Dual-mode standalone + Spyder IDE plugin (`src/plugin/PLUGIN.md`) with base `Editor` class, `ResourceType` enum, editor registry, game installation management.
- **Not Godot** — closest prior art is Spyder plugin architecture, not Godot `EditorPlugin`.

### Topic axes

1. **Format I/O layer** — read/write all KOTOR binary formats reliably (PyKotor vs C# HoloPatcher vs hybrid).
2. **Editor UI surface** — Godot dock tabs, inspectors, node graphs (DLG), 3D views (MDL/BWM), grids (2DA).
3. **Editor integration** — file association, installation browser, module/ERF/RIM container navigation.
4. **Architecture & reuse** — subprocess bridge vs GDExtension vs porting Holocron Qt UI.
5. **Delivery & parity roadmap** — phased path to Holocron feature completeness.

## Survivors (ranked)

### 1. PyKotor subprocess bridge + Godot EditorPlugin UI (recommended)

- **Summary:** Godot 4 `EditorPlugin` in `tools/godot-holocron/` calls a Python CLI (`bridge/kotor_format_bridge.py`) that wraps PyKotor `read_*` / `write_*` APIs. Godot editors consume JSON DTOs; specialized GDScript editors mirror Holocron's per-type subclasses. Game installation and resource browsing reuse PyKotor `Installation` APIs via the same bridge.
- **Axis:** Format I/O layer + Architecture & reuse
- **Basis:** `direct:` HolocronToolset uses PyKotor exclusively; KotorMCP `conversion.py` already serializes GFF/2DA/TLK to JSON; HoloPatcher C# does not build in-repo.
- **Why it matters:** Fastest path to correct binary I/O without rewriting 30 editors' worth of format logic; stays aligned with Holocron bugfixes upstream.

### 2. Holocron-style editor registry in GDScript

- **Summary:** Port Holocron's `Editor` base class + `ResourceType` → editor class map into GDScript (`base_kotor_editor.gd`, `editor_registry.gd`). Each extension opens the correct dock; unsupported types fall back to generic GFF or hex/text view.
- **Axis:** Editor UI surface
- **Basis:** `direct:` `editor_wiki_mapping.py` lists 30+ editor types; Holocron `PLUGIN.md` describes dual-mode editor architecture.
- **Why it matters:** Scales to full parity incrementally; one registration point matches Holocron mental model.

### 3. Phased parity program (not single PR)

- **Summary:** Phase 0: bridge + 2DA + generic GFF + text. Phase 1: TLK, SSF, ERF/RIM browser, UTC–UTW templates. Phase 2: DLG node editor, NSS/NCS, LIP/WAV. Phase 3: MDL/BWM/ARE/GIT level tools, Blender hooks, TSLPatchData editor.
- **Axis:** Delivery & parity roadmap
- **Basis:** `reasoned:` Holocron has ~30 editors and years of UI work; attempting 100% in one session is infeasible; phased delivery matches Spyder plugin migration strategy in Holocron docs.
- **Why it matters:** Sets honest expectations while preserving "eventual full parity" goal.

### 4. Optional C# GDExtension later (when HoloPatcher is library-ready)

- **Summary:** Extract `src/HoloPatcher/Formats/` into `KOTORModSync.Formats` NuGet; bind via Godot .NET or GDExtension for in-process I/O.
- **Axis:** Architecture & reuse
- **Basis:** `direct:` ModSync already ports PyKotor formats to C# in HoloPatcher; `reasoned:` in-process avoids subprocess latency for large modules.
- **Why it matters:** Long-term performance and single-language debugging; blocked until HoloPatcher builds as library.

### 5. Embed Holocron headless for complex editors (DLG, MDL)

- **Summary:** For editors that take months to rebuild in Godot (DLG graph, MDL viewport), spawn HolocronToolset subprocess with `--editor <path>` until native Godot UI exists.
- **Axis:** Editor UI surface
- **Basis:** `external:` Holocron already ships complete DLG/MDL editors; `reasoned:` hybrid shell gives "can edit any file" early.
- **Why it matters:** De-risks parity timeline for hardest 20% of UI.

## Rejected (sample)

| Idea | Reason |
|------|--------|
| Rewrite all format parsers in GDScript | Duplicates PyKotor/HoloPatcher; unmaintainable vs upstream |
| Port Holocron Qt UI into Godot via Qt bindings | Fragile, fights Godot editor UX |
| Single generic hex editor only | Fails user request for Holocron richness |
| Block on HoloPatcher C# build fix first | Delays Godot work; PyKotor bridge unblocks immediately |

## Recommended next step

Run `ce-brainstorm` on survivor **#1 + #2**, then `ce-plan` for Phase 0 vertical slice (bridge + 2DA + GFF + plugin shell).
