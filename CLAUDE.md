# OpenInventorExport — Project Guide

> Source of truth for Claude (and any human contributor). Read fully before suggesting code or architecture.
> Update whenever a decision changes. **Last updated: 2026-11-01. Tagged through v0.4.2. Config v1 steps 1-4 are done; step 5 (wire the CLI to the config) remains.**

---

## 0. Where we are right now (read this first)

**Status:** Milestones 0–2.5, the file-export task runner (#7), read-only property schema validation (#4), config file v1 steps 1-4 (Core only), the Content Center flag and unresolved-component handling are done and validated against real Inventor 2027 data.

- Repo: `OpenInventorExport` (GitHub), local path `C:\dev\OpenInventorExport`. Solution file is in the repo root.
- `main` is buildable; work happens on short-lived feature branches. Tags so far, in order: `v0.2-csv`, `v0.3-task-runner` (as `v0.3.5`), `v0.3.6` (DWG/DXF sheet fix), `v0.3.7` (property schema validation), `v0.3.8` (snapshot property sets, schema v2), `v0.3.9` (fixtures + totals test), `v0.3.10` (config steps 1-3), `v0.4` (rollup key + BOM columns in config), `v0.4.1` (Content Center flag), `v0.4.2` (unresolved components + export gate). Keep tags in order. 
- **Hard rule (decided): this tool is READ-ONLY toward Inventor.** It never creates, edits or saves anything in Inventor files. Exports use `SaveCopyAs` and `Close(true)` only. Properties are gathered as they are; any adjusting happens later in Core, on snapshot data, via the rules engine. Do not propose write-back features.
- **Current BOM validation against Inventor 2027** (expected values are Inventor's own, never this tool's output):

  | Assembly | Parts Only qty | Parts Only rows | Structured qty | Structured rows |
  |---|---|---|---|---|
  | PJ MK 2 (Robot) | 443 | 124 | 419 | 174 |
  | Engine | 1009 | 184 | 820 | 239 |

  Totals are asserted by `Totals_MatchInventorBom`, row counts by `RowCounts_MatchInventorBom` (both in `FixtureTotalsTests`). The row counts confirm that merging by Part Number matches Inventor on these assemblies.
  A third assembly (rotary, `MOTORGRUPPE.iam`) has unresolved components, so Inventor cannot save a BOM for it. It is used only as a "loads and rolls up" fixture.
- **Fixtures:** three `*snapshot.json` files in `samples/snapshots/` (schema version 2): robot, engine, rotary. They were collected before `IsContentCenter` and `IsUnresolved` existed, so those fields read as null/false in them.
- **File export verified on Inventor 2027:** STEP (model), PDF (drawing; all sheets and first sheet only), DWG, DXF, DWF (drawing) and DWF of the top-level model. The runner (`JobRunner`) executes planned jobs, writes one result per job, and detects extra files written by the translator.
- **CLI is interactive:** run with no arguments for a menu; one-shot commands still work (section 5). Exports are **blocked** while any component could not be read (4.9).
- **Config v1 (steps 1-4 done, Core only, works from snapshots):** `ExportTaskJson`, `ExportConfig`/`ExportConfigJson`, `ExportConfigFile`, `DocumentTypes`. Loads tasks, property schema, rollup options and BOM columns from one strict JSON file. Details in 4.5. **Not wired into the CLI yet** (step 5).
- **Rollup key (decided):** default is **Part Number** (matches Inventor), configurable to `FilePath`. Default mode stays `Flat`. Details in 4.4.
- Fixtures `engine-snapshot.json`, `robot-snapshot.json`, `rotary-snapshot.json` have been updated with content center and unresolved tags. `rotary-snapshot.json` should be used for any unresolved testing.
- The guard test `EveryFixture_NonSuppressedComponentsBelowTheRootHaveABomStructure` (in `FixtureTotalsTests`) was added.

**Deadline pressure:** the owner is on a **30-day Inventor 2027 trial that began about 2026-10-28** (expires roughly 2026-11-27; owner should confirm). After it expires nothing needing Inventor can be run or tested. **Prioritize Inventor-dependent work first.** Pure-Core work (config wiring, rules engine, XLSX/SQLite, config editor) can be built after the trial from saved snapshots.

**Next up (suggested order):**
1. Inventor-dependent verification (owner will do while the trial lasts): iPart / iAssembly members, virtual component properties, model states / LOD, timing on the largest assembly, whether `Part Icon` (a picture) appears in a fixture. Save more sanitized snapshots as fixtures (one per assembly type, including a small unresolved one).
2. Config v1 step 5: replace the hard-coded `SampleTask()`, test schema, BOM columns and rollup options in `Program.cs` with the config (`--config` option and a menu item). `BomTotals` keeps using default rollup rules on purpose (it is the comparison against Inventor).
3. Rules engine (include/exclude/route), including Content Center filtering and a policy for unresolved components (open questions 10 and 13). Consider reporting excluded phantom/reference items in `Skipped` (open question 2).
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
  - Record only verified behavior in this file; mark anything unverified as unverified.
- **Only use code the owner wrote or has the rights to.** Do not copy code belonging to a previous employer or any third party into this repo.
- The owner works in **Visual Studio 2022 (Windows)** and pastes exact error text/screenshots; CS error codes are the fastest way to diagnose.
- Never commit Inventor sample/third-party model files. Test assemblies stay outside the repo. Commit only sanitized snapshot fixtures: check for usernames, real names (Designer, Engineer, Author, Company properties) and company names before committing.
- When experimenting with unresolved files, work on a **copy** of a small assembly outside the repo, rename (do not delete) part files, and do not save the assemblies.

---

## 3. Scope

### In scope
1. **Collect:** traverse an Inventor assembly and gather every occurrence with its properties (read-only).
2. **Export tables:** CSV (done), XLSX, SQLite database, JSON; extensible.
3. **Export files:** per component DWG, DXF, STEP, PDF, DWF via a task system (done in Core and Inventor; config file loading done in Core, CLI wiring pending).
4. **Property schema validation:** define expected properties and report missing/empty/wrong-type values (done, read-only).
5. **Export configuration:** how many exports, what each contains, rules, file naming, destinations.
6. **Rules engine:** include/exclude/route components by model type, file name, properties, Content Center membership.

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
│  Model, SnapshotJson, SnapshotChecks, Rollup, BomTotals,     │
│  CsvTableWriter, ExportTask/Validator/Planner,               │
│  ExportResult(+Log), PropertySchema, ExportConfig(+Json/File),│
│  DocumentTypes                                               │
└──────────────────────────────────────────────────────────────┘
```

**Key rules:**
- `Core` never references `Autodesk.Inventor.Interop`.
- **No Inventor types may appear in public signatures outside the Inventor project** (CS0012 otherwise). Use facade methods (e.g. `SnapshotCollector.CollectFromRunningInventor()`) that return Core types only.

### 4.3 Two-phase pipeline (non-negotiable)

1. **Collect:** traverse the assembly once, produce a `Snapshot` (components + skipped list with reasons), saved as `snapshot.json`.
2. **Export:** from the Snapshot only: apply rollup/rules, write tables, plan and run file-export tasks.

The snapshot is the test fixture mechanism: Core can be developed and tested without Inventor.

**Snapshot schema version is 2** (`Snapshot.CurrentSchemaVersion`). `SnapshotJson.FromJson` throws on any other version instead of silently loading empty properties. Bump the version for any breaking format change and re-collect fixtures. **Additive fields do not bump the version** (`IsContentCenter` and `IsUnresolved` were added this way): old fixtures read them as null/false, so a new field must have a default that is honest for old data (`bool?` null = "not recorded").

### 4.4 Quantity rollup (implemented, validated)

Rollup is a **Core transformation on the snapshot**, never done during traversal. Modes (`RollupMode`):
- `Occurrence`: one row per occurrence, qty 1.
- `Structured`: roll up within each parent. A parent is identified by its **full ancestor chain**, so the same sub-assembly under two different parents lists its children under each, and the same part in several assemblies gets a row under each.
- `Flat`: roll up across the whole tree; counting occurrences multiplies quantities through levels.

`RollupOptions` (also the `Rollup` section of the config file): `Mode` (default `Flat`), `IncludeAssemblies`, `IncludeRoot` (default false), `ExcludePhantom` (true), `ExcludeReference` (true), `ExcludeSuppressed` (true), `Inseparable` (`AsSingleItem` | `IncludeChildren`; **null = Inventor's behavior for the mode**), `Key` (`RollupKey.PartNumber` default | `FilePath`).

**Rollup key (decided, open question 6 resolved):**
- `PartNumber` (default) groups by the Design Tracking `Part Number`, like Inventor. A component with a missing or empty Part Number **falls back to its file path**, so it never merges with unrelated parts. `FilePath` lists each file separately.
- Implemented in `RollupOptions.KeyOf`; keys are prefixed `PN:` / `File:` so a Part Number can never collide with a path. Matching is case-sensitive; the Robot and Engine row counts match Inventor with this.
- For one line per component use `Mode: Occurrence`; for one line per file use `Key: FilePath`.

`BomTotals` (`PartsOnlyRows`, `StructuredRows`, `PartsOnly`, `Structured`) are the shared definitions of the views compared against Inventor's BOM. They always use default rollup rules, not the config. Used by the CLI `totals` command and the fixture tests.

**Validated BOM rules (against Inventor 2027):**
- **Inseparable assemblies depend on the BOM view.** Parts Only hides their children; Structured lists them. **Implemented:** `Inseparable == null` resolves to `AsSingleItem` for Flat and `IncludeChildren` for Structured; covered by tests. The snapshot keeps all children and each assembly's `BomStructure`, so other behaviors can be configured later. Do not hide inseparable children in every mode: that breaks the Structured match.
- **Phantom** is excluded in both views. **Reference** excluded. **Suppressed** excluded. **Purchased** included. Virtual components counted once.
- **Inventor merges BOM rows that share a Part Number**, even when the files differ. We now do the same by default.
- Compare **total quantity and row counts** against Inventor's own numbers.
- If a number differs from Inventor, it is a finding (stale snapshot or an unseen BOM rule). Never paste the tool's number into a test.

### 4.5 Configuration (v1: steps 1-4 done)

The config file is the existing Core classes written as JSON, not a second model. v1 is a single file (splitting into several files is possible later):

```
{ "SchemaVersion": 1,
  "Tasks": [ ExportTask... ],
  "Properties": [ SchemaProperty... ],
  "Rollup": { RollupOptions },
  "BomColumns": [ { "Name": "...", "Set": "..." }... ] }
```

Decided rules:
- **Strict loading.** Unknown/misspelled names and unknown enum values are rejected (`UnmappedMemberHandling.Disallow`), never ignored. Enums are strings, names are case-insensitive, `//` comments and trailing commas are allowed. A missing or wrong `SchemaVersion` is rejected. Errors list every problem found.
- **Validated on load:** each task through `ExportTaskValidator`; task names unique (ignoring case, because they become folder names); at least one task; `DocumentTypes` and `AppliesTo` against the fixed list in `DocumentTypes`; property and BOM column `Set` against the known sets; no blank or duplicate property or column (same Set + Name).
- **Paths:** relative paths (e.g. `IniFile`) are relative to the **folder containing the config file**. **Absolute paths are allowed** (local, vault, shared directory, UNC). A missing ini file is a load error, so a bad path can never fall back to the translator's last-used sheet setting.
- **The loaded config holds resolved absolute paths. Never write it back to disk** (the config editor must keep its own unresolved copy, or relative paths would become absolute).
- `Properties`, `Rollup` and `BomColumns` are optional. No `Properties` = no property check; no `Rollup` = `RollupOptions` defaults (Flat, PartNumber); no `BomColumns` = only File Name, Qty and Status columns. `ExportConfig.ToPropertySchema()` feeds `PropertySchemaValidator`; `ExportConfig.ToCsvColumns()` feeds `CsvTableWriter`. `PropertySchema.SchemaVersion` is unused (the file's version covers it).
- Still hard-coded in `Program.cs`: the sample task, the test schema, BOM columns and rollup options. **Step 5 replaces these with the config** (`--config` option and a menu item).

### 4.6 Export tasks (implemented in Core, tested)

A **task** is one named deliverable. It has:
- **Selection:** `Scope` (`TopLevelOnly` | `AllUniqueFiles`), `DocumentTypes` (empty = any), and `Filters` (`PropertyFilter`: `Set`, `Property`, `Operator`, `Value`; AND-combined; Equals, NotEquals, Contains, IsEmpty, IsNotEmpty; case-insensitive; missing counts as empty; `Set` defaults to Design Tracking).
- **Outputs:** list of `OutputSpec` (Format Step/Pdf/Dwg/Dxf/Dwf, Source Model/Drawing, `PdfSheets`, optional `IniFile` for DWG/DXF).
- **FolderTemplate:** relative path under the output root with tokens `{task}`, `{format}`, `{type}`. Default `{task}`; `{task}/{format}` gives one folder per format.

Rules:
- Valid combinations: STEP from Model only; PDF/DWG/DXF from Drawing only; DWF from either. Invalid combinations, unknown tokens, unknown document types, rooted paths and `..` are rejected up front by `ExportTaskValidator`.
- Selection works on **unique model files** (member suffix stripped), not occurrences. Suppressed, virtual and unresolved components are never selected.
- `ExportPlanner.Plan` returns `ExportJob`s (`Planned` or `Conflict`). Same output path twice → second is `Conflict`; nothing is overwritten within a run. (Files left over from earlier runs are overwritten by `SaveCopyAs`.)
- Sheet metal is identified by `DocumentType == "SheetMetal"` (`SubType` is not set by the traverser).
- Filtering by `IsContentCenter` is not available yet (rules engine).

### 4.7 Job runner (implemented, verified)

`JobRunner.Run(jobs, onResult)` returns **exactly one `ExportResult` per job**.
- `ExportResult.ForJob`: Conflict jobs stay `Conflict`; every other job starts as `Failed` ("Not run") so an unexecuted job can never look like a success (`Exported` is enum value 0).
- Drawing jobs: find drawing via `DrawingLocator`, verify it references the model (`DrawingMismatch` otherwise), `NoDrawing` if none found. Model jobs: file must exist.
- Uses the document if the user already has it open; otherwise opens invisibly and closes with `Close(true)`.
- **Written-file check:** after saving, looks for files in the output folder written since the job started (name = planned name or `<base>_Sheet*`). None → `Failed`; extra or renamed files → `Exported` with `Detail = "Files written: ..."`. This catches translators that ignore the planned file name.
- DWG/DXF: sheet 1 is activated before saving (translator exports the active sheet when `ALL SHEETS=No`) and the previous sheet is restored afterwards.
- `ExportResultLog`: `ToCsv` (people), `ToJson` (tools), `Summary` (counts per status).

### 4.8 Property schema validation (implemented, read-only)

`SchemaProperty` (`Set`, `Name`, `DataType`, `Required`, `AppliesTo`) in a `PropertySchema`. `PropertySchemaValidator.Validate(snapshot, schema)` checks each unique model file once and returns `PropertyIssue`s: `Missing`, `Empty` (only when `Required`), `WrongType`. Suppressed, virtual and unresolved components are skipped (an unresolved file has no properties, so checking it would report false `Missing` findings). It reads the snapshot only, so it works after the trial.

**Property storage in the snapshot (schema v2):** `ComponentRecord.PropertySets` is `set name → property name → PropertyValue`, mirroring Inventor. Names only have to be unique within a set. Set names are constants in `PropertySetNames` (`Design Tracking Properties`, `Inventor User Defined Properties`). Use `FindProperty(set, name)`. `SetProperty` only changes the in-memory record. An existing but empty set is stored as empty; an unreadable/missing set is absent.

**Decided (2026-10-31): only the Design Tracking and Inventor User Defined property sets are read.** Summary Information and Document Summary Information (Title, Author, Last Saved By, Company, Manager, Thumbnail...) are out of scope: they are not needed for manufacturing exports, and they carry personal and company names. Reopen only with a written decision. `KnownSets` in `ExportConfigJson` and `setNames` in the traverser stay at two entries. Unverified: Design Tracking includes `Part Icon`, a picture; `ToPropertyValue` would store it as a useless string. Check a fixture for it and decide whether to skip picture-valued properties.

### 4.9 Unresolved components and the export gate (implemented, verified on Inventor 2027)

**What an unresolved file looks like to the API:** `occ.ReferencedDocumentDescriptor.FullDocumentName` still reads (the path is known), but `occ.Definition.Document` throws `E_FAIL`. The traverser's error text names the step (`While reading properties: ...`).

**How the traverser records it:** the occurrence is added to `Components` with `IsUnresolved = true`, `DocumentType = "Unknown"`, `FullPath` from the descriptor and no properties, **and** to `Skipped` as `FileMissing` with the file path in `Detail`. Unresolved assemblies are recorded the same way; **their children cannot be seen and are not recorded** (confirmed). `FileMissing` is also used if the file name itself cannot be read.

**In lists:** rollups include unresolved components. The BOM CSV has a `Status` column that says `Unresolved` for them. Because their type is `Unknown`, an unresolved `.iam` also appears in Parts Only (`IncludeAssemblies = false` does not hide it); it is visible and flagged, which is accepted. Planner and property check skip them (4.6, 4.8).

**Export gate (decided):** an unresolved component means the list is incomplete, so a manufacturing export must not run. `SnapshotChecks.BlockingProblems(snapshot)` returns `FileMissing` entries plus `Error` entries whose occurrence is **not** in `Components` (so it is absent from the list). Suppressed, virtual, reference and rule exclusions are deliberate and never block; an `Error` on a component that is still listed (e.g. unreadable virtual properties) does not block. `Actions.Export` in `Program.cs` throws before planning anything, with the first 10 problems listed; `Actions.Plan` still works and prints a WARNING. Both menu and one-shot `export` stop with the same message (one-shot returns exit code 1). **There is no override yet.** A configurable policy (block / warn / skip those components) belongs to the rules engine (open question 13).

**Unverified:** whether our totals match Inventor's when unresolved parts are present. Inventor cannot save a BOM for such an assembly, so there is nothing to compare.

### 4.10 Content Center flag (implemented, verified)

`ComponentRecord.IsContentCenter` is `bool?`: `true`/`false` from `PartComponentDefinition.IsContentMember` for parts and sheet metal parts; `false` for virtual components; **null = not recorded** (assemblies, suppressed or unresolved occurrences, snapshots collected before v0.4.1, or Inventor would not say). Verified on an assembly with two Content Center parts added by the owner: the summary reported 2. The summary line `Content Center parts: N (flag not recorded on M parts)` counts only parts and sheet metal in M, so a freshly collected snapshot should show 0 there. The flag exists so the rules engine can include/exclude Content Center items per export (open question 10). The folder path is not used to identify them (it is set per project, so fragile).

---

## 5. Current code map

```
OpenInventorExport/               (repo root; .slnx here)
├─ CLAUDE.md, README.md, LICENSE, .gitignore, .gitattributes
├─ Directory.Build.props          # InventorPublicAssemblies path (default: C:\Program Files\Autodesk\Inventor 2027\Bin\Public Assemblies)
├─ config/                        # one-sheet DWG/DXF ini files (dwg-one-sheet.ini, dxf-one-sheet.ini); CLI looks here
├─ samples/snapshots/             # sanitized snapshot fixtures (*snapshot.json, schema v2): robot, engine, rotary; .gitignore has !samples/snapshots/*.json
├─ src/
│  ├─ OpenInventorExport.Core/        netstandard2.0, Nullable on. NO implicit usings (add explicit usings)
│  │   Model.cs                   # PropertyDataType, SkipReason, PropertySetNames, PropertyValue, ComponentRecord (incl. IsContentCenter, IsUnresolved), SkippedRecord, Snapshot
│  │   SnapshotJson.cs            # indented JSON, enums as strings, schema-version check
│  │   SnapshotSummary.cs         # counts by DocumentType/BomStructure/Virtual, unique files, Content Center count, warns on unrecognized document types
│  │   SnapshotChecks.cs          # BlockingProblems: what makes the component list incomplete (export gate)
│  │   Rollup.cs                  # RollupMode, RollupKey, RollupOptions (+KeyOf), ExportRow, Rollup.Apply
│  │   BomTotals.cs               # PartsOnlyRows/StructuredRows and totals (match Inventor's views)
│  │   CsvTableWriter.cs          # CSV with escaping; columns are (Set, Name) pairs; last column Status
│  │   ExportResult.cs            # PdfSheetMode, ExportStatus, ExportResult.ForJob
│  │   ExportResultLog.cs         # results -> CSV / JSON / summary
│  │   PathUtil.cs                # StripMemberSuffix, DrawingBaseName
│  │   ExportTask.cs              # formats, scope, filters, selection, OutputSpec, ExportTask
│  │   ExportTaskValidator.cs
│  │   ExportPlanner.cs           # JobState, ExportJob, Plan()
│  │   PropertySchema.cs          # SchemaProperty, PropertySchema, PropertyIssue, PropertySchemaValidator
│  │   DocumentTypes.cs           # fixed list: Part, SheetMetal, Assembly, Virtual, Unknown; IsKnown()
│  │   ExportTaskJson.cs          # strict JSON options (shared), ExportTask <-> JSON, validates
│  │   ExportConfig.cs            # ExportConfig (SchemaVersion, Tasks, Properties, Rollup, BomColumns), BomColumn, ExportConfigJson
│  │   ExportConfigFile.cs        # Load(path): parse, validate, resolve ini paths against the config folder
│  ├─ OpenInventorExport.Inventor/    net8.0-windows (net48 pending), ImplicitUsings on
│  │   InventorConnection.cs      # P/Invoke GetActiveObject
│  │   AssemblyTraverser.cs       # recursive walk -> Snapshot (names the failing step in errors; flags unresolved, Content Center)
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
│     ExportTaskJsonTests, ExportConfigTests, ExportConfigFileTests, ConfigPropertiesTests, DocumentTypesTests,
│     ConfigRollupTests, ContentCenterFlagTests, UnresolvedTests
└─ docs/, schemas/                # planned
```

### CLI usage

**Run with no arguments: interactive menu** (the normal way; nothing to configure in launch settings). The menu shows the current output folder and loaded snapshot, and offers: collect from Inventor, load a saved snapshot, summary, plan (dry run, warns if components could not be read), run exports (asks for confirmation first; refuses while components are unresolved), check properties, write BOM CSV, totals for saved snapshots (pick from `samples/snapshots` or paste a path), change output folder. The snapshot stays loaded between actions; collect again after changing the assembly.

**One-shot commands (for scripts)**: the first argument is the command.

```
totals <snapshot.json>...   Rollup totals from saved snapshots. No Inventor needed.
snapshot [outDir]           Collect from running Inventor, save snapshot.json, print summary.
plan     [outDir]           Collect, save snapshot, print the export plan. Exports nothing.
export   [outDir]           Collect, save snapshot, run the plan, write export-log.csv/.json. Refuses if components are unresolved.
props    [outDir]           Check properties against the hard-coded test schema. Report only.
bom      [outDir]           Write bom-flat.csv (parts only, merged by Part Number; Part Number, Description, Material, Status).
--from <snapshot.json>      (plan/export/props/bom) use a saved snapshot instead of collecting.
```

`outDir` defaults to `./output` (git-ignored). Commands that collect need Inventor running with the assembly as the **active document**. `Program.cs` has three parts: `Actions` (the work), `Interactive` (menu), `Cli` (one-shot arguments). The CLI project still needs the Inventor interop DLL to build, even for `totals`. None of this reads the config file yet (step 5).

---

## 6. Domain model (as implemented)

```
Snapshot: SchemaVersion (2), CreatedUtc, InventorVersion, SourceFile, ModelState, Components[], Skipped[]

ComponentRecord: OccurrencePath, ParentPath, FileName, FullPath, DocumentType (Part/SheetMetal/Assembly/Virtual/Unknown),
  SubType (unused), Quantity, Level, IsSuppressed, IsVirtual, IsContentCenter (bool?), IsUnresolved, BomStructure,
  PropertySets: { setName: { propertyName: PropertyValue } }

PropertyValue: DataType (Text/Number/Date/Boolean), Value (string?, invariant culture; dates ISO "yyyy-MM-ddTHH:mm:ss"; Inventor's 1601 "empty date" -> null)

SkippedRecord: OccurrencePath, Reason (Suppressed, Virtual, ReferenceOnly, FileMissing, Error, ExcludedByRule), Detail
```

`OccurrencePath` is built from occurrence names (e.g. `Top.iam/Sub:1/Bolt:3`); an **occurrence is not a file**. `FullPath` comes from `occ.ReferencedDocumentDescriptor.FullDocumentName`.

Traverser: records the top-level assembly as Level 0 (its `BomStructure` is **null by design**: the root is not an occurrence, nothing reads it, and a made-up value would be worse; `SnapshotSummary` labels it `(root)`); records suppressed occurrences (reason Suppressed) and does not recurse into them (their `DocumentType` shows "Unknown" because `occ.Definition` throws; cosmetic); `SheetMetal` set via `DocumentType`; flags virtual components and Content Center parts; records `BomStructure`; reads Design Tracking + User Defined sets (all properties, ~60 per component; configurable column selection is later); records unresolved files as in 4.9; wraps each occurrence in try/catch (reason Error) and names the step that failed. A component that fails outright is only in `Skipped` as `Error` and **not** in `Components`; the export gate treats that as blocking. Virtual component names are trimmed (leading space).

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
- **Unresolved files:** the descriptor name reads, `occ.Definition.Document` throws `E_FAIL` (4.9). The bolted connections (Design Accelerator assemblies) in the rotary assembly are examples. An unresolved assembly hides its children. Inventor cannot save a BOM for an assembly with unresolved files.
- **Content Center:** `PartComponentDefinition.IsContentMember` works (verified).
- **Not yet verified:** iPart / iAssembly members; Reference BOM handling beyond totals; quantity overrides in Inventor's BOM; multiple model states/LOD; patterns; performance on large assemblies; COM object release; virtual component iProperties; mass (mostly `0`, treat as unreliable); picture-valued properties (`Part Icon`).
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
| 2 | Rollup engine + basic CSV | Done, validated vs Inventor (totals and row counts) |
| 2.5 | Edge-case assembly review | Done |
| 7 | File export: translators, planner, runner, result log | **Done** (STEP, PDF, DWG, DXF, DWF verified) |
| 4 | Property schema validation (read-only) | **Done** (Core). Snapshot property sets restructured (schema v2) |
| 2.9 | Fixtures + regression totals test | **Done** (v0.3.9) |
| 2.95 | Content Center flag; unresolved components + export gate | **Done** (v0.4.1, v0.4.2) |
| 3 | Config file v1 (tasks, schema, rollup, BOM columns from JSON) | **In progress**: steps 1-4 done (v0.3.10, v0.4). Step 5 CLI wiring remains |
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
- Errors on a single component are caught, logged, and recorded in `Skipped`; they never abort the collection. A component that cannot be read blocks the *export* instead (4.9).
- Core: explicit `using` statements (netstandard2.0, no implicit usings). Inventor/Cli: `ImplicitUsings` enabled.
- Every Core feature gets a unit test (Arrange/Act/Assert). Inventor-dependent code gets a manual checklist in `docs/`.
- Config schema changes require `schemaVersion` bump and migration note. Additive snapshot fields get an honest default for old data instead of a version bump (4.3).
- Config and other hand-edited JSON are loaded strictly (unknown names rejected), with every problem reported at once.
- Logging: planned `Microsoft.Extensions.Logging` to a file next to outputs.
- `Program.cs` stays thin: parse arguments, call Core/Inventor, print.

---

## 10. Git workflow

- `main` is always buildable. One short-lived branch per task (`feature/...`, `fix/...`).
- Commit often, imperative messages. **Push after committing**, and check the GitHub page.
- Merge via Pull Request even when solo. Tag milestones **in order** (`v0.4.2` done; the owner picks the next number).
- `.gitignore` includes `bin/`, `obj/`, `output/`, `snapshot*.json`, with `!samples/snapshots/*.json` for intentional fixtures.
- Never commit: Inventor DLLs, third-party or employer models, files with usernames or real names.

---

## 11. Open questions

1. Drawing association strategy for file exports (same folder/same name? naming rule?). Owner to describe typical workflow.
2. Should inseparable/phantom/reference handling be user-configurable per export, and should excluded phantom/reference items also appear in `Skipped` with a reason? (Currently silently excluded by rollup, which conflicts with "nothing silently missing".)
3. ~~Write-back~~ **Resolved: read-only, never write to Inventor.**
4. File-export destination types: local folder, network share, zip?
5. Single user or shared team config location? Partly answered: absolute paths in a config are allowed (so a shared team config can point at a network share); the config location itself is still open.
6. ~~Rollup key default~~ **Resolved: Part Number default (empty falls back to file path), `FilePath` configurable** (see 4.4).
7. Add-in launcher timing, and who will test `net48` on 2023/2024.
8. ~~DWG/DXF multi-sheet~~ **Resolved:** `ALL SHEETS=Yes|No` in the ini, per format, only via `Export_Acad_IniFile`; runner activates sheet 1. Still open: per-export ini setting in config.
9. Output collisions: reported as `Conflict`. Still open: a rule to disambiguate on request (suffix).
10. Should task selection also include/exclude by BOM structure, Content Center membership (`IsContentCenter` now exists) or occurrence-level rules (e.g. only parts under a given sub-assembly)? The owner wants Content Center items configurable per export.
11. ~~How should unresolved components be reported?~~ **Mostly resolved** (4.9): listed in the BOM flagged, recorded as `FileMissing`, exports blocked. Still unverified: totals against Inventor with unresolved items (no Inventor BOM exists to compare).
12. ~~Summary Information / Document Summary Information property sets~~ **Resolved: not read** (see 4.8).
13. Unresolved-component policy: today a hard block with no override. Should it become configurable per run (block / warn / skip those components) as part of the rules engine? The owner's stated default is block.

---

## 12. Definition of done (any task)

- Builds without new warnings; `net48` target must build too once re-enabled
- Core logic unit-tested
- Tested manually against at least one real assembly (note which)
- Skipped/failed components reported, never hidden
- README/docs updated if behavior or config changed
- Committed **and pushed** with a clear message
