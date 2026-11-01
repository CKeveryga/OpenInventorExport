# OpenInventorExport — Project Guide

> Source of truth for Claude (and any human contributor). Read fully before suggesting code or architecture.
> Update whenever a decision changes. **Last updated: 2026-10-31. Tagged through v0.3.10. Config v1 steps 1-3 are done on `feature/config-task-json` (tag after merging).**

---

## 0. Where we are right now (read this first)

**Status:** Milestones 0–2.5, the file-export task runner (#7) and read-only property schema validation (#4) are done and validated against real Inventor 2027 data.

- Repo: `OpenInventorExport` (GitHub), local path `C:\dev\OpenInventorExport`. Solution file is in the repo root.
- `main` is buildable; work happens on short-lived feature branches. Tags so far: `v0.2-csv`, `v0.3-task-runner` (as `v0.3.5`), `v0.3.6` (DWG/DXF sheet fix), `v0.3.7` (property schema validation), `v0.3.8` (snapshot property sets, schema v2), `v0.3.9` (fixtures + totals test). Keep tags in order.
- **Hard rule (decided): this tool is READ-ONLY toward Inventor.** It never creates, edits or saves anything in Inventor files. Exports use `SaveCopyAs` and `Close(true)` only. Properties are gathered as they are; any adjusting happens later in Core, on snapshot data, via the rules engine. Do not propose write-back features.
- **Current BOM validation (assemblies changed since the earlier 482/424 and 444/420 runs):**

  | Assembly | Inventor Parts Only | Inventor Structured |
  |---|---|---|
  | PJ MK 2 (Robot) | 443 | 419 |
  | Engine | 1009 | 820 |

  A third assembly has unresolved components, so no Inventor BOM could be saved. Its snapshot still collected. It is used only as a "loads and rolls up" fixture.
- **Fixtures:** three `*snapshot.json` files in `samples/snapshots/` (schema version 2). `FixtureTotalsTests` asserts the totals above against Inventor's own numbers. These tests keep working after the trial ends.
- **File export verified on Inventor 2027:** STEP (model), PDF (drawing; all sheets and first sheet only), DWG, DXF, DWF (drawing) and DWF of the top-level model. The runner (`JobRunner`) executes planned jobs, writes one result per job, and detects extra files written by the translator.
- **CLI is interactive:** run with no arguments for a menu; one-shot commands still work (section 5).
- **Config v1 (steps 1-3 done, Core only, works from snapshots):** `ExportTaskJson`, `ExportConfig`/`ExportConfigJson`, `ExportConfigFile`, `DocumentTypes`. Loads tasks and the property schema from one strict JSON file. Details in 4.5. Not wired into the CLI yet.

**Deadline pressure:** the owner is on a **30-day Inventor 2027 trial that began about 2026-10-28** (expires roughly 2026-11-27; owner should confirm). After it expires nothing needing Inventor can be run or tested. **Prioritize Inventor-dependent work first.** Pure-Core work (config format, rules engine, XLSX/SQLite, config editor) can be built after the trial from saved snapshots.

**Next up (suggested order):**
1. Inventor-dependent verification (owner will do later): files that fail to resolve (check how the traverser records them; they must appear in `Skipped` as `FileMissing`, never vanish), Content Center / iPart / iAssembly members, virtual component properties, timing on the largest assembly. Save more sanitized snapshots as fixtures (one per assembly type).
2. Config v1 remaining: step 4 rollup options and BOM columns (needs the decision on open question 6, rollup key); step 5 replace the hard-coded `SampleTask()`, test schema and BOM columns in `Program.cs` with the config (`--config` option and a menu item).
3. Rules engine (include/exclude/route). Consider reporting excluded phantom/reference items in `Skipped` (see open question 2).
4. XLSX (ClosedXML), SQLite, multiple exports per run.
5. Config editor (WPF), add-in launcher, `net48` build and testing.

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
  - **Be honest when writing Inventor API calls from memory** (member names may not compile first try or option keys may be wrong); ask for the exact error code and line when something fails. Do not guess translator option names.
  - **Do not decide open questions silently.** If CLAUDE.md lists something as open, ask the owner first.
  - Remind the owner to commit/push at sensible checkpoints and to tag in order. (Commit ≠ push.)
- **Only use code the owner wrote or has the rights to.** Do not copy code belonging to a previous employer or any third party into this repo.
- The owner works in **Visual Studio 2022 (Windows)** and pastes exact error text/screenshots; CS error codes are the fastest way to diagnose.
- Never commit Inventor sample/third-party model files. Test assemblies stay outside the repo. Commit only sanitized snapshot fixtures: check for usernames, real names (Designer, Engineer, Author, Company properties) and company names before committing.

---

## 3. Scope

### In scope
1. **Collect:** traverse an Inventor assembly and gather every occurrence with its properties (read-only).
2. **Export tables:** CSV (done), XLSX, SQLite database, JSON; extensible.
3. **Export files:** per component DWG, DXF, STEP, PDF, DWF via a task system (done in Core and Inventor; config file loading done in Core, CLI wiring pending).
4. **Property schema validation:** define expected properties and report missing/empty/wrong-type values (done, read-only).
5. **Export configuration:** how many exports, what each contains, rules, file naming, destinations.
6. **Rules engine:** include/exclude/route components by model type, file name, properties.

### Out of scope
Writing to Inventor files or properties (decided: never). Vault/PDM integration (design so it can be added), cloud/web hosting, non-Windows platforms, editing geometry.

---

## 4. Architecture decisions

### 4.1 Technology stack

| Concern | Decision | Reason |
|---|---|---|
| Language | **C#** | Best-supported Inventor API language |
| Supported Inventor versions | **2023 and newer** (decided). 2023–2024 = `net48` build; 2025+ = `net8.0-windows` build. Do not add older versions without a written decision in `docs/decisions/` | Each version multiplies testing |
| Runtime strategy | `OpenInventorExport.Core` targets `netstandard2.0`. `OpenInventorExport.Inventor` is meant to multi-target `net48;net8.0-windows`. **CURRENT STATE: Inventor project is `net8.0-windows` only** (owner has 2027 trial only). **`net48` has NOT been built or tested**; re-add and test when 2023/2024 is available. The Interop reference for older versions may differ | A single net48 add-in is unreliable on 2025+ |
| Process model | **CLI (out-of-process) is the primary engine**; the add-in is a thin launcher later. CLI targets `net8.0-windows` | External exe isn't tied to Inventor's .NET version |
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
│  (facade), DrawingLocator, exporters, JobRunner              │
└───────────────┬──────────────────────────────────────────────┘
┌───────────────▼──────────────────────────────────────────────┐
│  OpenInventorExport.Core (pure C#, NO Inventor references)   │
│  Model, SnapshotJson, Rollup, BomTotals, CsvTableWriter,     │
│  ExportTask/Validator/Planner, ExportResult(+Log),           │
│  PropertySchema, ExportConfig(+Json/File), DocumentTypes     │
└──────────────────────────────────────────────────────────────┘
```

**Key rules:**
- `Core` never references `Autodesk.Inventor.Interop`.
- **No Inventor types may appear in public signatures outside the Inventor project** (CS0012 otherwise). Use facade methods (e.g. `SnapshotCollector.CollectFromRunningInventor()`) that return Core types only.

### 4.3 Two-phase pipeline (non-negotiable)

1. **Collect:** traverse the assembly once, produce a `Snapshot` (components + skipped list with reasons), saved as `snapshot.json`.
2. **Export:** from the Snapshot only: apply rollup/rules, write tables, plan and run file-export tasks.

The snapshot is the test fixture mechanism: Core can be developed and tested without Inventor.

**Snapshot schema version is 2** (`Snapshot.CurrentSchemaVersion`). `SnapshotJson.FromJson` throws on any other version instead of silently loading empty properties. Bump the version for any breaking format change and re-collect fixtures.

### 4.4 Quantity rollup (implemented, validated)

Rollup is a **Core transformation on the snapshot**, never done during traversal. Modes (`RollupMode`):
- `Occurrence`: one row per occurrence, qty 1.
- `Structured`: roll up within each parent. A parent is identified by its **full ancestor chain**, so the same sub-assembly under two different parents lists its children under each.
- `Flat`: roll up across the whole tree; counting occurrences multiplies quantities through levels.

`RollupOptions`: `Mode`, `IncludeAssemblies`, `IncludeRoot` (default false), `ExcludePhantom` (true), `ExcludeReference` (true), `ExcludeSuppressed` (true), `Inseparable` (`AsSingleItem` | `IncludeChildren`; **null = Inventor's behavior for the mode**), `Key` (default: full path/file name; later configurable, e.g. Part Number).

`BomTotals.PartsOnly` (Flat, no assemblies) and `BomTotals.Structured` (Structured) are the shared definitions of the totals compared against Inventor's BOM views. Used by the CLI `totals` command and the fixture tests.

**Validated BOM rules (against Inventor 2027):**
- **Inseparable assemblies depend on the BOM view.** Parts Only hides their children; Structured lists them. **Implemented:** `Inseparable == null` resolves to `AsSingleItem` for Flat and `IncludeChildren` for Structured; covered by tests. The snapshot keeps all children and each assembly's `BomStructure`, so other behaviors can be configured later. Do not hide inseparable children in every mode: that breaks the Structured match.
- **Phantom** is excluded in both views. **Reference** excluded. **Suppressed** excluded. **Purchased** included. Virtual components counted once.
- **Inventor merges BOM rows that share a Part Number**, even when the files differ. Our default key is the file path, so we list them separately. Totals match; row counts and per-row quantities may not. Choosing the rollup key (file vs Part Number) is an open decision (section 11).
- Compare **total quantity** against Inventor, never per-file rows (or group both sides by Part Number first).
- If a total differs from Inventor, it is a finding (stale snapshot or an unseen BOM rule). Never paste the tool's number into a test.

### 4.5 Configuration (v1 in progress)

The config file is the existing Core classes written as JSON, not a second model. v1 is a single file (splitting into several files is possible later):

```
{ "SchemaVersion": 1, "Tasks": [ ExportTask... ], "Properties": [ SchemaProperty... ] }
```

Decided rules:
- **Strict loading.** Unknown/misspelled names are rejected (`UnmappedMemberHandling.Disallow`), never ignored. Enums are strings, names are case-insensitive, `//` comments and trailing commas are allowed. A missing or wrong `SchemaVersion` is rejected. Errors list every problem found.
- **Validated on load:** each task through `ExportTaskValidator`; task names unique (ignoring case, because they become folder names); at least one task; `DocumentTypes` and `AppliesTo` against the fixed list in `DocumentTypes`; property `Set` against the known sets; no blank or duplicate property (same Set + Name).
- **Paths:** relative paths (e.g. `IniFile`) are relative to the **folder containing the config file**. **Absolute paths are allowed** (local, vault, shared directory, UNC). A missing ini file is a load error, so a bad path can never fall back to the translator's last-used sheet setting.
- **The loaded config holds resolved absolute paths. Never write it back to disk** (the config editor must keep its own unresolved copy, or relative paths would become absolute).
- `Properties` is optional; empty = no property check. `ExportConfig.ToPropertySchema()` feeds `PropertySchemaValidator`. `PropertySchema.SchemaVersion` is unused (the file's version covers it).
- Still hard-coded in `Program.cs`: the sample task, the test schema, BOM columns and rollup options (steps 4-5).

### 4.6 Export tasks (implemented in Core, tested)

A **task** is one named deliverable. It has:
- **Selection:** `Scope` (`TopLevelOnly` | `AllUniqueFiles`), `DocumentTypes` (empty = any), and `Filters` (`PropertyFilter`: `Set`, `Property`, `Operator`, `Value`; AND-combined; Equals, NotEquals, Contains, IsEmpty, IsNotEmpty; case-insensitive; missing counts as empty; `Set` defaults to Design Tracking).
- **Outputs:** list of `OutputSpec` (Format Step/Pdf/Dwg/Dxf/Dwf, Source Model/Drawing, `PdfSheets`, optional `IniFile` for DWG/DXF).
- **FolderTemplate:** relative path under the output root with tokens `{task}`, `{format}`, `{type}`. Default `{task}`; `{task}/{format}` gives one folder per format.

Rules:
- Valid combinations: STEP from Model only; PDF/DWG/DXF from Drawing only; DWF from either. Invalid combinations, unknown tokens, unknown document types, rooted paths and `..` are rejected up front by `ExportTaskValidator`.
- Selection works on **unique model files** (member suffix stripped), not occurrences. Suppressed and virtual components are never selected.
- `ExportPlanner.Plan` returns `ExportJob`s (`Planned` or `Conflict`). Same output path twice → second is `Conflict`; nothing is overwritten within a run. (Files left over from earlier runs are overwritten by `SaveCopyAs`.)
- Sheet metal is identified by `DocumentType == "SheetMetal"` (`SubType` is not set by the traverser).

### 4.7 Job runner (implemented, verified)

`JobRunner.Run(jobs, onResult)` returns **exactly one `ExportResult` per job**.
- `ExportResult.ForJob`: Conflict jobs stay `Conflict`; every other job starts as `Failed` ("Not run") so an unexecuted job can never look like a success (`Exported` is enum value 0).
- Drawing jobs: find drawing via `DrawingLocator`, verify it references the model (`DrawingMismatch` otherwise), `NoDrawing` if none found. Model jobs: file must exist.
- Uses the document if the user already has it open; otherwise opens invisibly and closes with `Close(true)`.
- **Written-file check:** after saving, looks for files in the output folder written since the job started (name = planned name or `<base>_Sheet*`). None → `Failed`; extra or renamed files → `Exported` with `Detail = "Files written: ..."`. This catches translators that ignore the planned file name.
- DWG/DXF: sheet 1 is activated before saving (translator exports the active sheet when `ALL SHEETS=No`) and the previous sheet is restored afterwards.
- `ExportResultLog`: `ToCsv` (people), `ToJson` (tools), `Summary` (counts per status).

### 4.8 Property schema validation (implemented, read-only)

`SchemaProperty` (`Set`, `Name`, `DataType`, `Required`, `AppliesTo`) in a `PropertySchema`. `PropertySchemaValidator.Validate(snapshot, schema)` checks each unique model file once and returns `PropertyIssue`s: `Missing`, `Empty` (only when `Required`), `WrongType`. It reads the snapshot only, so it works after the trial.

**Property storage in the snapshot (schema v2):** `ComponentRecord.PropertySets` is `set name → property name → PropertyValue`, mirroring Inventor. Names only have to be unique within a set. Set names are constants in `PropertySetNames` (`Design Tracking Properties`, `Inventor User Defined Properties`). Use `FindProperty(set, name)`. `SetProperty` only changes the in-memory record. An existing but empty set is stored as empty; an unreadable/missing set is absent.

**Decided (2026-10-31): only the Design Tracking and Inventor User Defined property sets are read.** Summary Information and Document Summary Information (Title, Author, Last Saved By, Company, Manager, Thumbnail...) are out of scope: they are not needed for manufacturing exports, and they carry personal and company names. Reopen only with a written decision. `KnownSets` in `ExportConfigJson` and `setNames` in the traverser stay at two entries. Unverified: Design Tracking includes `Part Icon`, a picture; `ToPropertyValue` would store it as a useless string. Check a fixture for it and decide whether to skip picture-valued properties.

---

## 5. Current code map

```
OpenInventorExport/               (repo root; .slnx here)
├─ CLAUDE.md, README.md, LICENSE, .gitignore, .gitattributes
├─ Directory.Build.props          # InventorPublicAssemblies path (default: C:\Program Files\Autodesk\Inventor 2027\Bin\Public Assemblies)
├─ config/                        # one-sheet DWG/DXF ini files (dwg-one-sheet.ini, dxf-one-sheet.ini); CLI looks here
├─ samples/snapshots/             # sanitized snapshot fixtures (*snapshot.json, schema v2); .gitignore has !samples/snapshots/*.json
├─ src/
│  ├─ OpenInventorExport.Core/        netstandard2.0, Nullable on. NO implicit usings (add explicit usings)
│  │   Model.cs                   # PropertyDataType, SkipReason, PropertySetNames, PropertyValue, ComponentRecord, SkippedRecord, Snapshot
│  │   SnapshotJson.cs            # indented JSON, enums as strings, schema-version check
│  │   SnapshotSummary.cs         # counts by DocumentType/BomStructure/Virtual, unique files, warns on unrecognized document types
│  │   Rollup.cs                  # RollupMode, RollupOptions, ExportRow, Rollup.Apply
│  │   BomTotals.cs               # PartsOnly / Structured totals (match Inventor's views)
│  │   CsvTableWriter.cs          # CSV with escaping; columns are (Set, Name) pairs
│  │   ExportResult.cs            # PdfSheetMode, ExportStatus, ExportResult.ForJob
│  │   ExportResultLog.cs         # results -> CSV / JSON / summary
│  │   PathUtil.cs                # StripMemberSuffix, DrawingBaseName
│  │   ExportTask.cs              # formats, scope, filters, selection, OutputSpec, ExportTask
│  │   ExportTaskValidator.cs
│  │   ExportPlanner.cs           # JobState, ExportJob, Plan()
│  │   PropertySchema.cs          # SchemaProperty, PropertySchema, PropertyIssue, PropertySchemaValidator
│  │   DocumentTypes.cs           # fixed list: Part, SheetMetal, Assembly, Virtual, Unknown; IsKnown()
│  │   ExportTaskJson.cs          # strict JSON options (shared), ExportTask <-> JSON, validates
│  │   ExportConfig.cs            # ExportConfig (SchemaVersion, Tasks, Properties) + ExportConfigJson
│  │   ExportConfigFile.cs        # Load(path): parse, validate, resolve ini paths against the config folder
│  ├─ OpenInventorExport.Inventor/    net8.0-windows (net48 pending), ImplicitUsings on
│  │   InventorConnection.cs      # P/Invoke GetActiveObject
│  │   AssemblyTraverser.cs       # recursive walk -> Snapshot
│  │   SnapshotCollector.cs       # facade for the CLI (+ ReadPartsOnlyBom dev helper; sets PartsOnlyViewEnabled, do not save the assembly)
│  │   InventorProbe.cs           # connection test
│  │   DrawingLocator.cs          # drawing by model name via ResolveFile (.idw, .dwg)
│  │   DrawingBatch.cs            # legacy per-model drawing loop; now only helpers (References, FindOpenDocument) are used
│  │   Translators.cs             # lookup + activate translator add-in by ID
│  │   StepExporter.cs, PdfExporter.cs, CadExporter.cs   # translator-based savers
│  │   JobRunner.cs               # executes ExportJobs, one ExportResult each
│  └─ OpenInventorExport.Cli/         net8.0-windows exe
│      Program.cs                 # command-based CLI (below)
├─ tests/OpenInventorExport.Core.Tests/   xUnit
│     SnapshotSerializationTests, RollupTests, ExportPlannerTests, ExportResultTests,
│     PathUtilTests, PropertySchemaTests, FixtureTotalsTests,
│     ExportTaskJsonTests, ExportConfigTests, ExportConfigFileTests, ConfigPropertiesTests, DocumentTypesTests
└─ docs/, schemas/                # planned
```

### CLI usage

**Run with no arguments: interactive menu** (the normal way; nothing to configure in launch settings). The menu shows the current output folder and loaded snapshot, and offers: collect from Inventor, load a saved snapshot, summary, plan (dry run), run exports (asks for confirmation first), check properties, write BOM CSV, totals for saved snapshots (pick from `samples/snapshots` or paste a path), change output folder. The snapshot stays loaded between actions; collect again after changing the assembly.

**One-shot commands (for scripts)**: the first argument is the command.

```
totals <snapshot.json>...   Rollup totals from saved snapshots. No Inventor needed.
snapshot [outDir]           Collect from running Inventor, save snapshot.json, print summary.
plan     [outDir]           Collect, save snapshot, print the export plan. Exports nothing.
export   [outDir]           Collect, save snapshot, run the plan, write export-log.csv/.json.
props    [outDir]           Check properties against the hard-coded test schema. Report only.
bom      [outDir]           Write bom-flat.csv (parts only; Part Number, Description, Material).
--from <snapshot.json>      (plan/export/props/bom) use a saved snapshot instead of collecting.
```

`outDir` defaults to `./output` (git-ignored). Commands that collect need Inventor running with the assembly as the **active document**. `Program.cs` has three parts: `Actions` (the work), `Interactive` (menu), `Cli` (one-shot arguments). The CLI project still needs the Inventor interop DLL to build, even for `totals`.

---

## 6. Domain model (as implemented)

```
Snapshot: SchemaVersion (2), CreatedUtc, InventorVersion, SourceFile, ModelState, Components[], Skipped[]

ComponentRecord: OccurrencePath, ParentPath, FileName, FullPath, DocumentType (Part/SheetMetal/Assembly/Virtual/...),
  SubType (unused), Quantity, Level, IsSuppressed, IsVirtual, BomStructure,
  PropertySets: { setName: { propertyName: PropertyValue } }

PropertyValue: DataType (Text/Number/Date/Boolean), Value (string?, invariant culture; dates ISO "yyyy-MM-ddTHH:mm:ss"; Inventor's 1601 "empty date" -> null)

SkippedRecord: OccurrencePath, Reason (Suppressed, Virtual, ReferenceOnly, FileMissing, Error, ExcludedByRule), Detail
```

`OccurrencePath` is built from occurrence names (e.g. `Top.iam/Sub:1/Bolt:3`); an **occurrence is not a file**. `FullPath` comes from `occ.ReferencedDocumentDescriptor.FullDocumentName`.

Traverser: records the top-level assembly as Level 0; records suppressed occurrences (reason Suppressed) and does not recurse into them (their `DocumentType` shows "Unknown" because `occ.Definition` throws; cosmetic); `SheetMetal` set via `DocumentType`; flags virtual components; records `BomStructure`; reads Design Tracking + User Defined sets (all properties, ~60 per component; configurable column selection is later); wraps each occurrence in try/catch (reason Error). Virtual component names are trimmed (leading space). A file that cannot be resolved should be recorded as `FileMissing`; **verify this against the unresolved-components assembly**.

Document types the traverser emits are the list in `DocumentTypes` (Weldment is reported as Assembly). `SnapshotSummary` warns about any other value, and `FixtureTotalsTests` fails if a fixture contains one. Core cannot use Inventor's `ObjectTypeEnum` (no interop in Core), and that enum is far larger than the four document kinds we need.

---

## 7. Inventor API knowledge and gotchas (learned so far)

- Inventor collections are **1-indexed**.
- Interop interface types need **explicit casts** (`AssemblyDocument`/`DrawingDocument` → `Document`, CS1503).
- Our namespace `OpenInventorExport.Inventor` collides with Autodesk's `Inventor`: always alias `using Inv = global::Inventor;`.
- `PropertyDefinition` clashes with `System.Reflection.Metadata` under implicit usings; ours is `SchemaProperty`.
- The interop DLL is referenced by `HintPath` via `Directory.Build.props`; **never commit it**. A plain `<Reference>` does not flow to dependent projects (hence the facade rule).
- Model state lookup: `asm.ComponentDefinition.ModelStates.ActiveModelState.Name` (`[Primary]` on 2027).
- `BOMStructure` values: Phantom, Reference, Inseparable, Purchased, Normal: see 4.4.
- Inventor's **Structured BOM lists each unique sub-assembly's contents once per distinct parent chain**; **Parts Only** multiplies through levels. Don't compare raw occurrence counts to either.
- Property gotchas: Windows "empty date" is 1601-01-01; values are `object`; read per property set.
- `FullDocumentName` **can carry a `<member>` or model-state suffix** (e.g. `Motor.ipt<No Cover>`). Strip it (`PathUtil.StripMemberSuffix`) before comparing with `Document.FullFileName` or passing to translators. STEP therefore exports the file's primary state, not that variant. Inventor's BOM compare must key virtual components separately (no `Document`).
- **Not yet verified:** Content Center / iPart / iAssembly members; Reference BOM handling beyond totals; quantity overrides in Inventor's BOM; multiple model states/LOD; files that fail to resolve; patterns; performance on large assemblies; COM object release; virtual component iProperties; mass (mostly `0`, treat as unreliable).
- **Drawings are not discoverable from the model.** Current strategy: same base name, any project path (`ResolveFile`). A configurable association strategy is still open.
- **Translators:** IDs confirmed for PDF `{0AC6FD96-2F4D-42CE-8BE0-8AEA580399E4}`, DWG `{C24E3AC2-122E-11D5-8E91-0010B541CD80}`, DXF `{C24E3AC4-122E-11D5-8E91-0010B541CD80}`, DWF `{0AC6FD95-2F4D-42CE-8BE0-8AEA580399E4}`, STEP `{90AF7F40-0C01-11D5-8E83-0010B541CD80}`. Use a fresh `NameValueMap` and set only needed options.
  - STEP: `ApplicationProtocolType=3` (AP214) after `HasSaveCopyAsOptions`.
  - PDF: `All_Color_AS_Black`, `Remove_Line_Weights`, `Vector_Resolution`, `Sheet_Range` (`AllInOne` and `FirstOnly` verified). `FirstOnly` activates sheet 1 and does not restore the previously active sheet (acceptable; owner decided restoring is not needed).
  - DWF: `Launch_Viewer=0`, `Publish_All_Component_Props=1`, `Publish_All_Physical_Props=1`; `Publish_3D_Models=1` for models.
  - DWG/DXF: `USE_TRANSMITTAL="No"`; `Export_Acad_IniFile` optional. **Do not call `HasSaveCopyAsOptions` for DWG/DXF** (E_INVALIDARG earlier).
  - **DWG/DXF sheets:** with `ALL SHEETS=Yes` (in the ini, `[EXPORT DESTINATION]`) the translator writes one file per sheet named `<name>_Sheet_N` and ignores the planned filename. With `ALL SHEETS=No` it exports the **active sheet** (verified), under the planned name. The setting is stored **per format** and is controlled only through the ini passed as `Export_Acad_IniFile` (not through `NameValueMap`). With no ini, the translator uses whatever was last saved from the dialog. Fix: pass one-sheet ini files (saved from each dialog via "Save Configuration...", `config/`), and the runner activates sheet 1 first. Check other ini settings when saving (e.g. `USE CUSTOMIZE`, `DESTINATION DXF` differs per format, `SHOW DESTINATION PAGE`).
- **`Application.SilentOperation = true`** during batch exports suppresses dialogs; always restore the previous value in `finally`.
- Drawings and models are opened invisibly (`Documents.Open(path, false)`) and closed with `Close(true)` unless the user already had them open.

---

## 8. Roadmap

| # | Milestone | Status |
|---|---|---|
| 0 | Repo + solution skeleton | Done |
| 1 | Traverse & dump (`snapshot.json`) | Done |
| 2 | Rollup engine + basic CSV | Done, validated vs Inventor |
| 2.5 | Edge-case assembly review | Done |
| 7 | File export: translators, planner, runner, result log | **Done** (STEP, PDF, DWG, DXF, DWF verified) |
| 4 | Property schema validation (read-only) | **Done** (Core). Snapshot property sets restructured (schema v2) |
| 2.9 | Fixtures + regression totals test | **Done** (v0.3.9) |
| 3 | Config file v1 (columns, traversal, rollup, tasks, schema from JSON) | **In progress**: steps 1-3 done (tasks, envelope, paths, property schema, document types). Step 4 rollup/columns, step 5 CLI wiring remain |
| 5 | Rules engine | After config |
| 6 | XLSX, SQLite, multiple exports per run | After config |
| 8 | Config Editor UI (WPF) | Later |
| 9 | Polish, add-in launcher, `net48` build/testing on 2023/2024, installer, release | Later |

Resist building UI early; a good config *format* first.

---

## 9. Coding conventions

- Small classes, clear names, XML doc comments on public members.
- No Inventor types outside `OpenInventorExport.Inventor`.
- **Never write to Inventor documents.** Exports use `SaveCopyAs` and `Close(true)` only. Any temporary UI state change (e.g. active sheet) is restored where it matters.
- Errors on a single component are caught, logged, and recorded in `Skipped`; they never abort the export.
- Core: explicit `using` statements (netstandard2.0, no implicit usings). Inventor/Cli: `ImplicitUsings` enabled.
- Every Core feature gets a unit test (Arrange/Act/Assert). Inventor-dependent code gets a manual checklist in `docs/`.
- Config schema changes require `schemaVersion` bump and migration note.
- Config and other hand-edited JSON are loaded strictly (unknown names rejected), with every problem reported at once.
- Logging: planned `Microsoft.Extensions.Logging` to a file next to outputs.
- `Program.cs` stays thin: parse arguments, call Core/Inventor, print.

---

## 10. Git workflow

- `main` is always buildable. One short-lived branch per task (`feature/...`, `fix/...`).
- Commit often, imperative messages. **Push after committing**, and check the GitHub page.
- Merge via Pull Request even when solo. Tag milestones **in order** (`v0.3.9` done; next `v0.3.10`/`v0.4` as the owner prefers).
- `.gitignore` includes `bin/`, `obj/`, `output/`, `snapshot*.json`, with `!samples/snapshots/*.json` for intentional fixtures.
- Never commit: Inventor DLLs, third-party or employer models, files with usernames or real names.

---

## 11. Open questions

1. Drawing association strategy for file exports (same folder/same name? naming rule?). Owner to describe typical workflow.
2. Should inseparable/phantom/reference handling be user-configurable per export, and should excluded phantom/reference items also appear in `Skipped` with a reason? (Currently silently excluded by rollup, which conflicts with "nothing silently missing".)
3. ~~Write-back~~ **Resolved: read-only, never write to Inventor.**
4. File-export destination types: local folder, network share, zip?
5. Single user or shared team config location? Partly answered: absolute paths in a config are allowed (so a shared team config can point at a network share); the config location itself is still open.
6. Rollup key default (file path vs Part Number) and conflict rule. Inventor merges rows by Part Number.
7. Add-in launcher timing, and who will test `net48` on 2023/2024.
8. ~~DWG/DXF multi-sheet~~ **Resolved:** `ALL SHEETS=Yes|No` in the ini, per format, only via `Export_Acad_IniFile`; runner activates sheet 1. Still open: per-export ini setting in config.
9. Output collisions: reported as `Conflict`. Still open: a rule to disambiguate on request (suffix).
10. Should task selection also include/exclude by BOM structure or occurrence-level rules (e.g. only parts under a given sub-assembly)?
11. How should unresolved components be reported and what should the Parts Only/Structured totals do with them? (Third fixture has unresolved components; no Inventor BOM to compare.)
12. ~~Summary Information / Document Summary Information property sets~~ **Resolved: not read** (see 4.8).

---

## 12. Definition of done (any task)

- Builds without new warnings; `net48` target must build too once re-enabled
- Core logic unit-tested
- Tested manually against at least one real assembly (note which)
- Skipped/failed components reported, never hidden
- README/docs updated if behavior or config changed
- Committed **and pushed** with a clear message
