# OpenInventorExport — Project Guide

> Source of truth for Claude (and any human contributor). Read fully before suggesting code or architecture.
> Update whenever a decision changes. **Last updated: file-export probes done; export planning (Core) done and tested; Inventor-side task runner next.**

---

## 0. Where we are right now (read this first)

**Status:** Milestones 0–2 are done and validated against real Inventor 2027 data.

- Repo: `OpenInventorExport` (GitHub), local path `C:\dev\OpenInventorExport`. Solution file is in the repo root.
- Latest tag: `v0.2-csv`. `main` is buildable; work happens on short-lived feature branches.
- **Validation result (the key proof), assembly 1** (552 occurrences, 68 sub-assembly occurrences, 18 unique assemblies, 159 unique files): flat / parts-only qty **482 = Inventor "Parts Only" BOM**; structured qty **424 = Inventor "Structured" BOM**.
- **Validation result, edge-case assembly (`PJ MK 2.iam`)**: 541 components, 6 suppressed, 1 virtual, 2 phantom, 2 reference, 1 purchased, 20 inseparable assemblies. Flat parts-only qty **444 = Inventor Parts Only**. Structured qty **420 = Inventor Structured**, but only with `Inseparable = IncludeChildren` and `ExcludePhantom = true` (see 4.4).
- **File export verified on Inventor 2027:** STEP (model), PDF (drawing, all sheets and first sheet only), DWG, DXF, DWF (drawing) all export correctly. DWF of the top-level assembly model also verified. Drawing lookup via `ResolveFile` (same name, any project folder) found the top-level drawing and others; no `DrawingMismatch` results on the edge-case assembly.
- **Export planning (Core, tested):** `ExportTask` (Selection + Outputs + FolderTemplate), `ExportTaskValidator`, `ExportPlanner`. Planning decides every output path before Inventor is touched; same-path collisions are reported as `Conflict`, never overwritten. All unit tests pass.
- Edge cases confirmed matching Inventor: suppressed (excluded), virtual (counted once), phantom (excluded), reference (excluded), purchased (included), inseparable (mode-dependent).
- **Corrected from this review:** the CSV now shows the virtual row's Part Number; Structured mode does not yet default to `IncludeChildren` automatically (**pending**, see 4.4).

**Deadline pressure:** the owner is on a **30-day Inventor 2027 trial that began about 2026-10-28** (expires roughly 2026-11-27; owner should confirm the exact date). After it expires nothing that needs Inventor can be run or tested. **Prioritize Inventor-dependent work first** (file-export translators, property read/write, edge cases). Pure-Core work (config format, rules engine, XLSX/SQLite writers, config editor UI) can be built after the trial from saved snapshots.

**Next up (suggested order):**
1. **Inventor-side task runner (roadmap #7):** execute the `ExportJob`s produced by `ExportPlanner`. Needs a model-based exporter for STEP/DWF (open the model invisibly, strip the `<member>` suffix, `SaveCopyAs`, close unless already open) and a runner that dispatches each job to the right exporter, honors `JobState.Conflict` (report, never export), and writes one results log (CSV/JSON) with every job's status.
2. Reorganize the existing drawing exporters so they work per job (output path comes from the plan, not from `DrawingBatch`'s own naming).
3. Custom property read/write and schema validation (roadmap #4).
4. Save sanitized snapshots from every test assembly as fixtures for post-trial development.
5. Later (no Inventor needed): config file format (maps directly onto `ExportTask`), rules engine, XLSX/SQLite, config editor.

---

## 1. Purpose

An open-source tool that exports Autodesk Inventor assemblies into **reliable, table-based component lists** plus the **associated files** (drawings, models, neutral formats).

**Why it exists:** Inventor's built-in export misses components depending on component state (suppressed, virtual, etc.), level of detail / model state, configuration, and other factors. This tool traverses the assembly itself and is explicit about what it includes and why.

**Core promise:** Every occurrence is either exported or explicitly reported as skipped with a reason. Nothing is silently missing.

---

## 2. About the project owner (how Claude should behave)

- Experienced Inventor user, **not a software engineer**. Learning git, C#, and the Inventor API as we go.
- Claude acts as **architect and mentor**, not just a code generator:
  - Explain *why* before *how*; short, plain language; define jargon on first use.
  - Prefer the simplest approach that works; push back on over-engineering.
  - Work in **small, testable steps**; say exactly which file to create/edit and where it goes.
  - Flag risks early (licensing, Inventor API quirks, scope creep, the trial deadline).
  - **Be honest when writing Inventor API calls from memory** (member names may not compile first try); ask for the exact error code and line when something fails.
  - Remind the owner to commit/push at sensible checkpoints. (Commit ≠ push: push uploads to GitHub.)
- **Only use code the owner wrote or has the rights to.** Do not copy code belonging to a previous employer or any third party into this repo.
- The owner works in **Visual Studio 2022 (Windows)** and pastes exact error text/screenshots; CS error codes are the fastest way to diagnose.
- Never commit Inventor sample/third-party model files. Test assemblies stay outside the repo; commit only sanitized `snapshot.json` fixtures (no usernames in paths) or the owner's own tiny hand-built models.

---

## 3. Scope

### In scope
1. **Collect:** traverse an Inventor assembly and gather every occurrence with configured properties.
2. **Export tables:** CSV (done), XLSX, SQLite database, JSON; extensible.
3. **Export files:** per component DWG, DXF, STEP, PDF, others via a task system.
4. **Property schema configuration:** define custom iProperties (name, type, default, where they live) and apply/validate them in Inventor.
5. **Export configuration:** how many exports, what each contains, rules, file naming, destinations.
6. **Rules engine:** include/exclude/route components by model type, file name, properties.

### Out of scope (for now)
Vault/PDM integration (design so it can be added), cloud/web hosting, non-Windows platforms, editing geometry.

---

## 4. Architecture decisions

### 4.1 Technology stack

| Concern | Decision | Reason |
|---|---|---|
| Language | **C#** | Best-supported Inventor API language |
| Supported Inventor versions | **2023 and newer** (decided). 2023–2024 = `net48` build; 2025+ = `net8.0-windows` build. Do not add older versions without a written decision in `docs/decisions/` | Each version multiplies testing |
| Runtime strategy | `OpenInventorExport.Core` targets `netstandard2.0`. `OpenInventorExport.Inventor` is meant to multi-target `net48;net8.0-windows`. **CURRENT STATE: Inventor project is `net8.0-windows` only**, because the owner only has 2027 (trial). **`net48` has NOT been built or tested** and must be re-added and tested when 2023/2024 is available. The Interop reference for older versions may differ | A single net48 add-in is unreliable on 2025+; one build can't cover all |
| Process model | **CLI (out-of-process) is the primary engine**; the add-in is a thin launcher later. CLI currently targets `net8.0-windows` | External exe isn't tied to Inventor's .NET version |
| IDE | Visual Studio 2022 Community | |
| Config format | JSON files + JSON Schema | Diffable, human-readable |
| JSON | `System.Text.Json` (NuGet in Core), enums as strings | |
| XLSX writer | **ClosedXML** (MIT). Do not use EPPlus | Licensing |
| Database | SQLite first (`Microsoft.Data.Sqlite`) | Zero setup |
| Config UI | WPF desktop app (separate from Inventor) | |
| Tests | xUnit (net8.0) | |
| License | MIT or Apache-2.0 (confirm what is in the repo) | |

### 4.2 Shape of the system

```
┌──────────────────────────────────────────────────────────────┐
│  FRONT ENDS (thin, no business logic)                        │
│  • CLI runner (exists)  • Inventor Add-in (later)            │
│  • Config Editor WPF (later)                                 │
└───────────────┬──────────────────────────────────────────────┘
┌───────────────▼──────────────────────────────────────────────┐
│  OpenInventorExport.Inventor (ONLY layer touching Inventor)  │
│  InventorConnection, AssemblyTraverser, SnapshotCollector    │
│  (facade), InventorProbe; later: property read/write,        │
│  translators                                                 │
└───────────────┬──────────────────────────────────────────────┘
┌───────────────▼──────────────────────────────────────────────┐
│  OpenInventorExport.Core (pure C#, NO Inventor references)   │
│  Model, SnapshotJson, SnapshotSummary, Rollup, CsvTableWriter│
│  later: config models, rules engine, XLSX/SQLite, task plan  │
└──────────────────────────────────────────────────────────────┘
```

**Key rules:**
- `Core` never references `Autodesk.Inventor.Interop`.
- **No Inventor types may appear in public signatures outside the Inventor project.** Exposing them to the CLI caused compiler error CS0012 (missing assembly reference). Use facade methods (e.g. `SnapshotCollector.CollectFromRunningInventor()`) that return Core types only.

### 4.3 Two-phase pipeline (non-negotiable)

1. **Collect:** traverse the assembly once, produce a `Snapshot` (components + skipped list with reasons), saved as `snapshot.json`.
2. **Export:** from the Snapshot only: apply rollup/rules, write tables, plan and run file-export tasks.

The snapshot is the test fixture mechanism: Core can be developed and tested without Inventor.

### 4.4 Quantity rollup (implemented, validated)

Rollup is a **Core transformation on the snapshot**, never done during traversal. Modes (`RollupMode`):
- `Occurrence`: one row per occurrence, qty 1.
- `Structured`: roll up within each parent. A parent is identified by its **full ancestor chain** (not just its file), so the same sub-assembly under two different parents lists its children under each. (An earlier version keyed parents by file only and came out 4 short vs Inventor.)
- `Flat`: roll up across the whole tree; counting occurrences multiplies quantities through levels.
`RollupOptions`: `Mode`, `IncludeAssemblies`, `IncludeRoot` (default false), `ExcludePhantom` (default true), `ExcludeReference` (default true), `ExcludeSuppressed` (default true), `Inseparable` (`AsSingleItem` | `IncludeChildren`), `Key` (default: full path/file name; later configurable, e.g. Part Number).
**Validated BOM rules (against Inventor 2027):**
- **Inseparable assemblies depend on the BOM view.** Inventor's *Parts Only* BOM hides their children (use `AsSingleItem`). Inventor's *Structured* BOM lists their children (use `IncludeChildren`). Structured mode must therefore default to `IncludeChildren`. **(pending implementation in `Rollup.cs`; until then, set it explicitly in the caller.)**
- **Phantom** is excluded (`ExcludePhantom = true`) in both views. Including it gave 416/422 instead of 414/420.
- **Reference** is excluded. **Suppressed** is excluded. **Purchased** is included.
- **Inventor merges BOM rows that share a Part Number**, even when the files differ (e.g. four `Alu C 1x2x1x*.ipt` files with Part Number `Alu C 1x2x1x35` become one row of 9). Our default key is the file path, so we list them separately. Totals still match; row counts and per-row quantities do not. Choosing the rollup key (file vs Part Number) is an open decision (Section 11).
- Do not compare per-file quantities to Inventor's rows. Compare **total quantity**, or group both sides by Part Number first.

### 4.5 Configuration (not built yet)

- `properties.schema.json`: custom iProperty definitions.
- `exports/*.export.json`: one per export definition (traversal options, columns, rules, rollup, outputs, destinations).
- Config Editor edits these; CLI/add-in read them.
- Currently CSV columns and rollup options are **hard-coded in the CLI's `Program.cs`** (`Part Number, Description, Material`). Replacing that with config is milestone 3.

### 4.6 Export tasks (implemented in Core, tested)

A **task** is one named deliverable (e.g. "SheetMetalPackage"). It has:
- **Selection:** `Scope` (`TopLevelOnly` | `AllUniqueFiles`), `DocumentTypes` (empty = any; e.g. `SheetMetal`), and `Filters` (property tests, AND-combined; operators Equals, NotEquals, Contains, IsEmpty, IsNotEmpty; case-insensitive; a missing property counts as empty).
- **Outputs:** a list of `OutputSpec` (Format Step/Pdf/Dwg/Dxf/Dwf, Source Model/Drawing, `PdfSheets`, optional `IniFile` for DWG/DXF).
- **FolderTemplate:** relative path under the output root with tokens `{task}`, `{format}`, `{type}`. Default `{task}` (all formats of a task in one folder); `{task}/{format}` gives one folder per format.

Rules:
- Valid combinations: STEP from Model only; PDF/DWG/DXF from Drawing only; DWF from either. Invalid combinations, unknown tokens, rooted paths and `..` are rejected up front by `ExportTaskValidator`.
- Selection works on **unique model files** (member suffix stripped), not occurrences. Suppressed and virtual components are never selected (no file). Quantities belong to the BOM, not to export.
- `ExportPlanner.Plan(snapshot, task, outputRoot)` returns `ExportJob`s (`Planned` or `Conflict`). Two jobs resolving to the same output path (e.g. same base name from different folders) make the second a `Conflict`; the first wins; nothing is overwritten.
- Sheet metal is identified by `DocumentType == "SheetMetal"` (the traverser does not set `SubType`).
- `ExportStatus` now includes `Conflict`.

---

## 5. Current code map

```
OpenInventorExport/               (repo root; .sln here)
├─ CLAUDE.md, README.md, LICENSE, .gitignore
├─ Directory.Build.props          # InventorPublicAssemblies path (default: C:\Program Files\Autodesk\Inventor 2027\Bin\Public Assemblies)
├─ src/
│  ├─ OpenInventorExport.Core/        netstandard2.0, Nullable on. NO implicit usings (add explicit usings)
│  │   Model.cs                   # PropertyDataType, SkipReason, PropertyValue, ComponentRecord, SkippedRecord, Snapshot
│  │   SnapshotJson.cs            # indented JSON, enums as strings
│  │   SnapshotSummary.cs         # counts by DocumentType/BomStructure/Virtual (+ unique files, if added)
│  │   Rollup.cs                  # RollupMode, RollupOptions, ExportRow, Rollup.Apply
│  │   CsvTableWriter.cs          # CSV with proper escaping
│  │   ExportResult.cs            # PdfSheetMode, ExportStatus (incl. Conflict), ExportResult (one per export task)
│  │   PathUtil.cs                # StripMemberSuffix, DrawingBaseName
│  │   ExportTask.cs              # ExportFormat/Source/Scope, FilterOperator, PropertyFilter, ExportSelection, OutputSpec, ExportTask
│  │   ExportTaskValidator.cs     # rejects invalid format/source combos, bad folder templates
│  │   ExportPlanner.cs           # JobState, ExportJob, Plan(): selection, folder template, conflict detection
│  ├─ OpenInventorExport.Inventor/    net8.0-windows (net48 pending), ImplicitUsings on
│  │   InventorConnection.cs      # P/Invoke GetActiveObject (Marshal.GetActiveObject doesn't exist in modern .NET)
│  │   AssemblyTraverser.cs       # recursive walk -> Snapshot
│  │   SnapshotCollector.cs       # facade used by the CLI
│  │   InventorProbe.cs           # connection test
│  │   DrawingLocator.cs          # finds drawing by model name via DesignProjectManager.ResolveFile (.idw, .dwg)
│  │   DrawingBatch.cs            # shared loop: find drawing, open invisibly, verify it references the model, save, close; wrapped in SilentOperation
│  │   StepExporter.cs, PdfExporter.cs, CadExporter.cs   # translator-based exporters (CadExporter = DWG/DXF/DWF)
│  └─ OpenInventorExport.Cli/         net8.0-windows exe
│      Program.cs                 # collect -> snapshot.json -> rollup -> CSV; prints totals vs Inventor's Parts Only BOM and a Structured option matrix
├─ tests/
│  └─ OpenInventorExport.Core.Tests/  xUnit: snapshot round-trip, rollup
|  |   ExportPlannerTests.cs
|  |   PathUtilTests.cs
|  |   RollupTests.cs
|  |   SnapshotSerializationTests.cs
(occurrence/flat/structured/ancestor-chain), CSV escaping
├─ output/                        # git-ignored: snapshots and CSVs
└─ docs/, samples/, schemas/      # planned
```

CLI usage: first argument = output path for `snapshot.json` (CSV is written next to it with `.csv`). In Visual Studio set it under project Properties > Debug > launch profiles > command line arguments; or `dotnet run --project src\OpenInventorExport.Cli -- <path>`. Inventor must be running with the assembly as the **active document**.

---

## 6. Domain model (as implemented)

```
Snapshot: SchemaVersion, CreatedUtc, InventorVersion, SourceFile, ModelState, Components[], Skipped[]

ComponentRecord: OccurrencePath, ParentPath, FileName, FullPath, DocumentType (Part/Assembly/...),
  SubType (SheetMetal/Virtual/...), Quantity, Level, IsSuppressed, IsVirtual, BomStructure,
  Properties: Dictionary<string, PropertyValue>

PropertyValue: DataType (Text/Number/Date/Boolean), Value (string?, invariant culture; dates ISO "yyyy-MM-ddTHH:mm:ss"; Inventor's 1601 "empty date" -> null)

SkippedRecord: OccurrencePath, Reason (Suppressed, Virtual, ReferenceOnly, FileMissing, Error, ExcludedByRule), Detail
```

`OccurrencePath` is a string built from occurrence names (e.g. `Top.iam/Sub:1/Bolt:3`); an **occurrence is not a file**: one `.ipt` can be many occurrences. `FullPath` comes from `occ.ReferencedDocumentDescriptor.FullDocumentName`.

Traverser currently: records suppressed occurrences in `Skipped` (reason Suppressed) and does not recurse into them; their `DocumentType` shows as "Unknown" because `occ.Definition` throws on suppressed items (cosmetic, to be fixed); sets `DocumentType = "SheetMetal"` for sheet metal (`SubType` is not used yet); flags virtual components via `Definition.Type == kVirtualComponentDefinitionObject`; records `BomStructure`; reads Design Tracking + User Defined property sets (all properties, ~60 per component, noisy; configurable column selection is the next fix); wraps each occurrence in try/catch so one bad component never aborts the run (reason Error). Virtual components: properties are read from the definition's `PropertySets` and the name is trimmed (it has a leading space). **(pending: verify against real data.)**

---

## 7. Inventor API knowledge and gotchas (learned so far)

- Inventor collections are **1-indexed**.
- Interop interface types need **explicit casts**: `AssemblyDocument` → `Document` (error CS1503 otherwise).
- Our namespace `OpenInventorExport.Inventor` collides with Autodesk's `Inventor` namespace: always alias `using Inv = global::Inventor;`.
- The interop DLL (`Autodesk.Inventor.Interop.dll`) is referenced by `HintPath` from the install folder via `Directory.Build.props`; **never commit it**. A plain `<Reference>` does **not** flow to dependent projects (hence the facade rule).
- Model state lookup works: `asm.ComponentDefinition.ModelStates.ActiveModelState.Name` returned `[Primary]` on 2027.
- `BOMStructure` values matter: **Phantom** parts are excluded from Inventor's Parts Only BOM (the tool matched Inventor by excluding them); Reference and Inseparable still need handling (Inseparable assemblies: Inventor's BOM hides children; our traversal currently walks into them; make this a traversal option, and record what was done).  Inseparable assemblies: Parts Only hides their children, Structured shows them (see 4.4).
- Inventor's **Structured BOM lists each unique sub-assembly's contents once per distinct parent chain**, with qty = count within that parent. **Parts Only** multiplies through levels. Don't compare raw occurrence counts to either.
- Property gotchas: Windows "empty date" is 1601-01-01; values are `object`; read per property set (`Design Tracking Properties`, `Inventor User Defined Properties`).
- **Verified on the edge-case assembly:** suppressed, virtual (count), phantom, reference, purchased and inseparable handling match Inventor's BOM totals. `FullDocumentName` **does** carry a `<member>` or model-state suffix (e.g. `V5 Motor (No Cover).ipt<No Cover>`) that is not part of the real file path: strip it before comparing against `Document.FullFileName` or passing the path to translators. Inventor's BOM compare must key virtual components separately, since they have no `Document`.
- **Not yet verified:** Content Center / iPart / iAssembly members in detail; Reference BOM handling beyond the totals; quantity overrides in Inventor's BOM; multiple model states/LOD; files that fail to resolve; patterns (not explicitly checked); performance on large assemblies; COM object release; virtual component iProperties (fix pending); mass (most parts read `0`, probably never computed in the file, so treat Mass as unreliable).
- **Drawings are not discoverable from the model** (a drawing references a model, not vice versa). Needs a configurable association strategy (same-name-in-same-folder, search paths, naming rules; Vault later).
- Translators (DWG/DXF/STEP/PDF) use Inventor translator add-ins with `NameValueMap` option maps; each format needs its own options in config.
- **Translators:** IDs confirmed for PDF `{0AC6FD96-2F4D-42CE-8BE0-8AEA580399E4}`, DWG `{C24E3AC2-122E-11D5-8E91-0010B541CD80}`, DXF `{C24E3AC4-122E-11D5-8E91-0010B541CD80}`, DWF `{0AC6FD95-2F4D-42CE-8BE0-8AEA580399E4}`, STEP `{90AF7F40-0C01-11D5-8E83-0010B541CD80}`. Use a fresh `NameValueMap` and set only needed options. DWG/DXF: `USE_TRANSMITTAL="No"`; `Export_Acad_IniFile` is optional (layer/version mapping) and will become an export-config setting. `Export All Sheets` option must be set in the .ini config file, otherwise will export with last option. DWF: `Launch_Viewer=0`, `Publish_All_Component_Props=1`, `Publish_All_Physical_Props=1`; `Publish_3D_Models=1` for models. PDF: `All_Color_AS_Black`, `Remove_Line_Weights`, `Vector_Resolution`, `Sheet_Range` (`AllInOne` and `FirstOnly` verified). STEP: `ApplicationProtocolType=3` (AP214) after `HasSaveCopyAsOptions`. **Do not call `HasSaveCopyAsOptions` for DWG/DXF** (it caused E_INVALIDARG in our early attempt, though the exact cause was not isolated). DWG/DXF export one file per sheet, named <name>_Sheet_N, ignoring the planned filename, ALL SHEETS=Yes|No in the ini, that it’s saved per format, and that it’s controlled only through the ini passed as `Export_Acad_IniFile`.
- **`Application.SilentOperation = true`** during batch exports suppresses option dialogs and prompts; always restore the previous value in `finally`.
- Drawings are opened invisibly (`Documents.Open(path, false)`), verified via `ReferencedDocuments`, exported, then closed unless the user already had them open. Verified: no drawings left open or modified.
- Interop types are interfaces: `DrawingDocument` to `Document` needs an explicit cast when passed to a `Document` parameter.

---

## 8. Roadmap

| # | Milestone | Status |
|---|---|---|
| 0 | Repo + solution skeleton | Done |
| 1 | Traverse & dump (`snapshot.json`) | Done |
| 2 | Rollup engine + basic CSV | **Done, validated vs Inventor (482 / 424)** |
| 2.5 | Edge-case assembly review (suppressed, virtual, phantom, reference, inseparable) | **Done: flat 444 = 444, structured 420 = 420.** |
| 7 | File export: probes done (STEP, PDF, DWG, DXF, DWF); export planning (Core) done; Inventor task runner next | In progress |
| 4 | Property schema (read custom iProperties) | Needs Inventor; do during trial |
| 3 | Config file v1 (columns, traversal, rollup from JSON) | After trial OK |
| 5 | Rules engine | After trial OK |
| 6 | XLSX, SQLite, multiple exports per run | After trial OK |
| 8 | Config Editor UI (WPF) | After trial OK |
| 9 | Polish, add-in launcher, `net48` build & testing on 2023/2024, installer, release | |

Resist building UI early; a good config *format* first.

---

## 9. Coding conventions

- Small classes, clear names, XML doc comments on public members.
- No Inventor types outside `OpenInventorExport.Inventor`.
- Errors on a single component are caught, logged, and recorded in `Skipped`; they never abort the export.
- Core: explicit `using` statements (netstandard2.0, no implicit usings). Inventor/Cli: `ImplicitUsings` enabled (needed for `Path`, `List<>`, `Exception`, etc.).
- Every Core feature gets a unit test (Arrange/Act/Assert). Inventor-dependent code gets a manual checklist in `docs/`.
- Config schema changes require `schemaVersion` bump and migration note.
- Logging: planned `Microsoft.Extensions.Logging` to a file next to outputs.

---

## 10. Git workflow

- `main` is always buildable. One short-lived branch per task (`feature/file-export`, `fix/inseparable-handling`).
- Commit often, imperative messages (`Add rollup engine and CSV writer`). **Push after committing**, and check the GitHub page.
- Merge via Pull Request even when solo. Tag milestones (`v0.2-csv`, next `v0.3-...`).
- `.gitignore` must include `bin/`, `obj/`, `output/`, `snapshot*.json` (until sanitized fixtures are intentionally added to `samples/snapshots/`).
- Never commit: Inventor DLLs, third-party or employer models, files with usernames in paths.

---

## 11. Open questions

1. Drawing association strategy for file exports (same folder/same name? naming rule?). Owner to describe typical workflow.
2. ~~Inseparable / Reference / Phantom handling~~ Resolved for BOM matching (see 4.4). Still open: should these be user-configurable per export, and should excluded phantom/reference items also appear in `Skipped` with a reason? (Currently they are silently excluded by rollup, which conflicts with the "nothing silently missing" promise.)
3. Write-back: create/repair custom properties in v1, or read-only first? **Never write inventor properties, only ever read-only**
4. File-export destination types: local folder, network share, zip?
5. Single user or shared team config location?
6. Rollup key default (file path vs Part Number) and conflict rule. Inventor merges rows by Part Number, so this now affects row-for-row comparison with Inventor's BOM.
7. Add-in launcher timing, and who will test `net48` on 2023/2024.
8. DWG/DXF layer-mapping ini: per-export setting in config (optional). Multi-sheet behavior of DWG/DXF must be set in .ini file, otherwise uses last used setting. Resolved: `ALL SHEETS=Yes|No` in the ini, that it’s saved per format, and that it’s controlled only through the ini passed as `Export_Acad_IniFile`
9. ~~Output file naming and collisions~~ Resolved: collisions are reported as `Conflict`. Still open: a rule to disambiguate on request (suffix) instead of just reporting.
10. Should task selection also be able to include/exclude by BOM structure or by occurrence-level rules (e.g. only parts used under a particular sub-assembly)? Currently selection is by unique file, document type and properties only.

---

## 12. Definition of done (any task)

- Builds without new warnings; `net48` target must build too once re-enabled
- Core logic unit-tested
- Tested manually against at least one real assembly (note which)
- Skipped/failed components reported, never hidden
- README/docs updated if behavior or config changed
- Committed **and pushed** with a clear message
