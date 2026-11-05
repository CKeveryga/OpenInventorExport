# OpenInventorExport — Project Guide

> Source of truth for Claude (and any human contributor). Read fully before suggesting code or architecture.
> Update whenever a decision changes. **Last updated: 2026-11-04. Tagged through v0.6.1 (see section 0 for what the owner says it contains). Config v1 is complete. The rules engine is built in Core, and its BOM structure defaults are now verified against Inventor 2027. XLSX and table outputs are built. Numeric filters understand units. The `Unresolved: Warn` path and virtual component properties are verified in Inventor. The Inventor-dependent checklist is almost finished (see section 0).**

---

## 0. Where we are right now (read this first)

**Status:** Milestones 0–2.5, the file-export task runner (#7), read-only property schema validation (#4), config file v1 (all 5 steps, CLI wired), the Content Center flag, unresolved-component handling and policy, active-project recording and check, BOM structure rules and the exclusion report, XLSX output, table outputs with per-output folders, names, columns, rollup mode and key, `--only`, numeric property filters **with units**, **verified BOM structure children defaults**, **a verified `Unresolved: Warn` run**, and **collect and export timers** are done and validated (except where marked unverified).

- Repo: `OpenInventorExport` (GitHub), local path `C:\dev\OpenInventorExport`. Solution file is in the repo root.
- `main` is buildable; work happens on short-lived feature branches. Tags so far, in order: `v0.2-csv`, `v0.3-task-runner` (as `v0.3.5`), `v0.3.6` (DWG/DXF sheet fix), `v0.3.7` (property schema validation), `v0.3.8` (snapshot property sets, schema v2), `v0.3.9` (fixtures + totals test), `v0.3.10` (config steps 1-3), `v0.4` (rollup key + BOM columns in config), `v0.4.1` (Content Center flag), `v0.4.2` (unresolved components + export gate), `v0.5` (config wired into the CLI, `PlanAll`, active project recorded and checked), `v0.5.1` (BOM structure rules, exclusion report, Content Center filter, `Unresolved` policy, exit code 2), `v0.6` (XLSX writer, table outputs, `Folder`, per-output `Mode`/`Key`, `--only`, numeric filters), **`v0.6.1`** (unit-aware numeric filters and the export timer are merged under it; the BOM-structure children defaults change, the two new fixtures (`testing-snapshot.json`, `unresolved-small-snapshot.json`) and their tests were committed). Keep tags in order.
- **Hard rule (decided): this tool is READ-ONLY toward Inventor.** It never creates, edits or saves anything in Inventor files. Exports use `SaveCopyAs` and `Close(true)` only. Properties are gathered as they are; any adjusting happens later in Core, on snapshot data, via the rules engine. Do not propose write-back features.
- **Current BOM validation against Inventor 2027** (expected values are Inventor's own, never this tool's output):

  | Assembly | Parts Only qty | Parts Only rows | Structured qty | Structured rows |
  |---|---|---|---|---|
  | PJ MK 2 (Robot) | 445 | 125 | 421 | 175 |
  | Engine | 1009 | 184 | 820 | 239 |
  | Testing (small, one Phantom, one Reference, one Purchased sub-assembly, 2 parts each) | 3 | 3 | 5 | 5 |

  (Robot numbers rose from 443/124/419/174 when the owner added two Content Center parts; the fixture tests failed until the expected values were updated, which is the intended behavior.) Totals are asserted by `Totals_MatchInventorBom`, row counts by `RowCounts_MatchInventorBom` (both in `FixtureTotalsTests`). The row counts confirm that merging by Part Number matches Inventor on these assemblies.
  A third assembly (rotary, `MOTORGRUPPE.iam`) has unresolved components, so Inventor cannot save a BOM for it. It is used only as a "loads and rolls up" fixture. The small unresolved fixture (below) is used for precise assertions.
- **The robot assembly in Inventor has changed since `robot-snapshot.json` was collected** (the owner added iProperties to a virtual component and made two levels of detail, one with suppressed parts). **Never overwrite `robot-snapshot.json` with a new collection**: its expected totals (445/125/421/175) belong to the old state. LOD experiments were saved under other names and are not committed.
- **Fixtures:** `*snapshot.json` files in `samples/snapshots/` (schema version 2): robot, engine, rotary, **testing** (`testing-snapshot.json`: Phantom/Reference/Purchased sub-assemblies, totals above) and **unresolved-small** (`unresolved-small-snapshot.json`: the same layout with `Part5.ipt` renamed on disk, so exactly one unresolved part inside the Purchased assembly; has `ProjectFile`). The owner re-collected the robot, engine and rotary fixtures containing `ProjectFile`. 
- **File export verified on Inventor 2027:** STEP (model), PDF (drawing; all sheets and first sheet only), DWG, DXF, DWF (drawing) and DWF of the top-level model. The runner (`JobRunner`) executes planned jobs, writes one result per job, and detects extra files written by the translator.
- **Tables (v0.6):** `Csv` and `Xlsx` are **output formats** like `Pdf`. A task's folder tree is configured with the task's `FolderTemplate` (root) plus each output's `Folder` (below the root). A table lists **every file its task selects**; the export log says what was actually delivered. See 4.13. There is **no `Package` concept any more** (a config containing `Package` is rejected).
- **CLI is interactive:** run with no arguments for a menu; one-shot commands still work (section 5). `plan`, `export`, `props` and `bom` **need a config file** (default `config/export.config.json`, or `--config`). Exports are **blocked** while components could not be read, unless the config says `"Unresolved": "Warn"` (4.9). `plan` and `export` take `--only <task>` (the menu asks for a task name when the config has two or more tasks).
- **Lesson (2026-11-02):** when a config fails to load, the CLI/menu keeps the previously loaded config and only prints `Config not loaded: ...`. The owner once tested a whole session against an old config without noticing. Always check for that line after loading, and a wrong-looking output folder name is a sign the old config is active.
- **Lesson (2026-11-04):** a test config must name document types that match the test files. The first `Warn` run used `"DocumentTypes": [ "Part" ]` and planned nothing, because the test parts are sheet metal (`SheetMetal`); the exclusion report (`Left out by rules: ... DocumentType`) showed it immediately. Read the "Left out by rules" lines before assuming the tool is wrong.
- **Verified (2026-11-02):** the active Inventor project matters for drawing exports. With the wrong project active, drawing jobs report `NoDrawing` (drawings are found through the active project's paths). STEP model exports are not affected. The CLI warns about a project mismatch (4.12).
- **Rollup key (decided):** default is **Part Number** (matches Inventor), configurable to `FilePath`. Default mode stays `Flat`. Details in 4.4.
- **Verified in Inventor 2027 on 2026-11-04** (details in the sections named): BOM structure children for Phantom, Reference and Purchased (4.11); units of numeric properties (4.14); a real `Unresolved: Warn` and `Block` run (4.9); virtual component properties come through the virtual definition's property sets (7); model state suffix `<Model State1>` appears in `FileName` and `FullPath` of occurrences (7); timing (below).
- **Timing (measured 2026-11-04, Inventor 2027):** collection runs at about **26–28 components per second**, linear (Engine 1067 components in 40.1 s; Robot 542 in 20.6 s and 19.3 s on a repeat, so there is no first-run caching effect). The collect time is printed after every Collect. A full export run on the robot (154 STEP, 167 DWF, 13 each of PDF, DWG, DXF) took **5 min 51 s**. Per format: Dwf 0.5 s average, Step 0.8 s, Pdf 1.3 s, Dxf 4.1 s, Dwg 4.8 s; the slowest job of each format was 10–34 s (probably the big assemblies) and about a third of the total sits in five jobs. **Opening a drawing costs only 0.1–0.2 s per job**, so "open each drawing once" is not worth building, and copying a result to a second folder only helps when the same file and format go to several folders (not measured as common). Neither optimization is built; the log columns `Seconds` and `OpenSeconds` and the `Timing:` lines after `export` make it easy to re-measure.

**Deadline pressure:** the owner is on a **30-day Inventor 2027 trial that began about 2026-10-28** (expires roughly 2026-11-27; about 23 days left as of 2026-11-04; owner to confirm). After it expires nothing needing Inventor can be run or tested. **Prioritize Inventor-dependent work first.** Pure-Core work (config editor, further rules, `Remainder`) can be built after the trial from saved snapshots.

**Done in the 2026-11-04 session (all passed):** BOM structure children test and the resulting defaults; unit-aware numeric filters; `Unresolved: Warn` and `Block` runs; unresolved-fixture tests; collect timer and per-job export timer; units check on millimetre and inch documents; virtual component properties; LOD experiment.

**Next up (suggested order):**
1. **Inventor-dependent, while the trial lasts (owner does these):**
   - **iPart / iAssembly members: parked by owner decision** until the rest of the tool is done. If the trial is nearly over and time is left, do it then (the `<member>` handling in `PathUtil` is only tested with made-up names).
   - Model states beyond the primary state: not tested; the traverser records only the active model state's name.
2. **`Remainder` selection** ("every file no other task selects"), design in open question 17. Pure Core.
3. Zip (one task-level option that zips the task's root folder; deliberately not built, see open question 19).
4. Config editor (WPF), add-in launcher, `net48` build and testing.
5. Small cleanups: see open question 26 (stale "package" wording).

---

## 1. Purpose

An open-source tool that exports Autodesk Inventor assemblies into **reliable, table-based component lists** plus the **associated files** (drawings, models, neutral formats).

**Why it exists:** Inventor's built-in export misses components depending on component state (suppressed, virtual, etc.), level of detail / model state, configuration, and other factors. This tool traverses the assembly itself and is explicit about what it includes and why.

**Core promise:** Every occurrence is either exported or explicitly reported as skipped with a reason. Nothing is silently missing.

**Typical use (owner's workflow):** a manufacturer needs several deliverables from one assembly, for example one folder for vendor A (sheet metal with `Vendor = "ABC"`, DXF and PDF plus a cut list), one for vendor B, one for everything else, plus an ERP import table with one line per placed item. Each deliverable is one **task** in the config.

---

## 2. About the project owner (how Claude should behave)

- Experienced Inventor user, **not a software engineer**. Learning git, C#, and the Inventor API as we go.
- Claude acts as **architect and mentor**, not just a code generator:
  - Explain *why* before *how*; short, plain language; define jargon on first use.
  - Prefer the simplest approach that works; push back on over-engineering.
  - Work in **small, testable steps**; say exactly which file to create/edit and where it goes.
  - Flag risks early (licensing, Inventor API quirks, scope creep, the trial deadline).
  - **Be honest when writing Inventor API calls from memory** (member names may not compile first try or option keys may be wrong); ask for the exact error code and line when something fails. Do not guess translator option names.
  - **Do not decide open questions silently.** If CLAUDE.md lists something as open, ask the owner first. Lead with a recommendation on each so the owner can answer "agreed".
  - **Check the shape of a design against the owner's real workflow before building** (2026-11-02: the first "package" design, with hardcoded `packages/` folders and a separate section, did not match what the owner wanted and was replaced after the first run). Describe how the config section will look in an example the owner can recognize, and ask about it, before writing code. When the owner says "it all feels wrong", first explain what each config section does today, with a table, before proposing changes.
  - Remind the owner to commit/push at sensible checkpoints and to tag in order. (Commit ≠ push.)
  - Record only verified behavior in this file; mark anything unverified as unverified. Claude only edits CLAUDE.md at the end of a chat, when the owner asks, and produces the whole file.
  - Claude's memory of Inventor's BOM rules is unreliable: say so, and ask for an Inventor test instead of deciding from memory. (2026-11-04: the memory was wrong twice: Purchased assemblies behave like Inseparable ones, and mass is stored in grams, not kilograms.)
  - **When Claude's explanation of a result is a guess, say so, and ask for the data that would settle it** (2026-11-04: the Structured-count explanation for the LOD test was first a guess and was corrected after the owner pasted the two suppressed paths).
- **Only use code the owner wrote or has the rights to.** Do not copy code belonging to a previous employer or any third party into this repo.
- The owner works in **Visual Studio 2022 (Windows)** and pastes exact error text/screenshots; CS error codes are the fastest way to diagnose. Test failures are reported by pasting the xUnit message. Claude cannot compile: say so and ask for errors.
- **The owner said (2026-11-04) they do not need to match Inventor's behavior where Inventor's behavior is unclear or unimportant to them.** Defaults verified against Inventor stay as they are unless the owner decides otherwise.
- Never commit Inventor sample/third-party model files. Test assemblies stay outside the repo. Commit only sanitized snapshot fixtures: check for usernames (including in `SourceFile` and `ProjectFile` paths), real names (Designer, Engineer, Author, Company properties) and company names before committing.
- When experimenting with unresolved files, work on a **copy** of a small assembly outside the repo, rename (do not delete) part files, and do not save the assemblies.
- Experiments with config go in their own file (e.g. `config/test-export.config.json`, `config/warn-test.config.json`), never in `config/export.config.json`: `ExampleConfigTests` loads that one and a typo there fails the test suite (this happened on 2026-11-02).

---

## 3. Scope

### In scope
1. **Collect:** traverse an Inventor assembly and gather every occurrence with its properties (read-only).
2. **Export tables:** CSV (done), XLSX (done), both as table outputs inside a task's folder tree; JSON later; extensible.
3. **Export files:** per component DWG, DXF, STEP, PDF, DWF via a task system (done; config-driven from the CLI), with a configurable folder layout per task.
4. **Property schema validation:** define expected properties and report missing/empty/wrong-type values (done, read-only).
5. **Export configuration:** how many exports, what each contains, rules, file naming, destinations (config v1 done).
6. **Rules engine:** include/exclude components by BOM structure (with child handling), Content Center membership, document type, properties (text and numeric tests, numeric tests with units); exclusion report; unresolved policy (done in Core). Occurrence-level rules (e.g. only parts under a given sub-assembly) and `Remainder` are not built.

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
| XLSX writer | **ClosedXML** (MIT), referenced in Core (`PackageReference` ClosedXML 0.104.2; the version was written from memory and built, but confirm in the csproj). **Do not pin below 0.102** (older versions turned `"001"` into a number). Do not use EPPlus | Licensing |
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
│  BomStructurePolicy, Exclusions, ContentCenterFilter, Units, │
│  CsvTableWriter, XlsxTableWriter, TaskTable,                 │
│  ExportTask/Validator/Planner, ExportResult(+Log),           │
│  PropertySchema, ExportConfig(+Json/File), DocumentTypes     │
└──────────────────────────────────────────────────────────────┘
```

**Key rules:**
- `Core` never references `Autodesk.Inventor.Interop`.
- **No Inventor types may appear in public signatures outside the Inventor project** (CS0012 otherwise). Use facade methods (e.g. `SnapshotCollector.CollectFromRunningInventor()`, `SnapshotCollector.GetActiveProjectFile()`) that return Core types or plain strings only.

### 4.3 Two-phase pipeline (non-negotiable)

1. **Collect:** traverse the assembly once, produce a `Snapshot` (components + skipped list with reasons), saved as `snapshot.json`.
2. **Export:** from the Snapshot only: apply rollup/rules, write tables, plan and run file-export tasks.

The snapshot is the test fixture mechanism: Core can be developed and tested without Inventor.

**Snapshot schema version is 2** (`Snapshot.CurrentSchemaVersion`). `SnapshotJson.FromJson` throws on any other version instead of silently loading empty properties. Bump the version for any breaking format change and re-collect fixtures. **Additive fields do not bump the version** (`IsContentCenter`, `IsUnresolved` and `Snapshot.ProjectFile` were added this way): old fixtures read them as null/false, so a new field must have a default that is honest for old data (`bool?`/`string?` null = "not recorded").

**Known limit (decided 2026-11-04, no field added):** a snapshot does not record which level of detail was active when it was collected; only `ModelState` (the name) is recorded. Two collections of the same assembly in different LODs differ only in their suppression flags (verified: same component count, same model state name, 6 vs 8 suppressed occurrences). When working with saved snapshots, name the file after the LOD by hand. A fresh collection from Inventor always reflects what is active.

### 4.4 Quantity rollup (implemented, validated)

Rollup is a **Core transformation on the snapshot**, never done during traversal. Modes (`RollupMode`):
- `Occurrence`: one row per occurrence, qty 1 (fits an ERP import that wants one line per placed item).
- `Structured`: roll up within each parent. A parent is identified by its **full ancestor chain**, so the same sub-assembly under two different parents lists its children under each, and the same part in several assemblies gets a row under each (an indented BOM; fits a weldment BOM).
- `Flat`: roll up across the whole tree; counting occurrences multiplies quantities through levels (the total to make or buy; fits cut lists and vendor lists).

**Rollup mode only changes how table rows are counted** (the `bom` command and `Csv`/`Xlsx` outputs). It has **no effect on file exports**: a PDF or DXF export is always one file per unique model file.

`RollupOptions` (also the `Rollup` section of the config file): `Mode` (default `Flat`), `IncludeAssemblies`, `IncludeRoot` (default false), `ExcludeSuppressed` (true), `BomStructures` (per-structure rules, 4.11), `ContentCenter` (`Include` default | `Exclude` | `Only`, 4.10), `Inseparable` (`AsSingleItem` | `IncludeChildren`; **null = Inventor's behavior for the mode**; a `BomStructures` rule for Inseparable with `Children` set takes precedence; **the same mode default now also drives Purchased**, 4.11), `Key` (`RollupKey.PartNumber` default | `FilePath`). **`ExcludePhantom` and `ExcludeReference` no longer exist** (replaced by `BomStructures`); a config that still contains them is rejected by strict loading.

**Who uses the top-level `Rollup` (important, this was a source of confusion):** the `bom` command uses all of it. Table outputs (4.13) use only its `Mode`, `Key` and `ExcludeSuppressed`, and **only as fallbacks**: an output's own `Mode` and `Key` win. A table forces `IncludeAssemblies`, `IncludeRoot` and `Inseparable = IncludeChildren` on, and takes `ContentCenter` and `BomStructures` from its task's `Selection`, because the task's selection decides what is listed.

`Rollup.Apply(snapshot, options, exclusions?)` takes an optional list that receives one `ExclusionRecord` per occurrence a rule left out (4.11). Order of checks per component: root (skipped unless `IncludeRoot`), BOM visibility (suppressed, own structure, parent excluded), Content Center, then `IncludeAssemblies`. **An assembly whose children are hidden (Inseparable or Purchased in Flat mode, or any structure with an explicit `Children: Exclude`) counts as a single item and stays listed even when `IncludeAssemblies` is false** (`KeptAsSingleItem`; verified against Inventor's Parts Only).

**Rollup key (decided, open question 6 resolved):**
- `PartNumber` (default) groups by the Design Tracking `Part Number`, like Inventor. A component with a missing or empty Part Number **falls back to its file path**, so it never merges with unrelated parts. `FilePath` lists each file separately.
- Implemented in `RollupOptions.KeyOf`; keys are prefixed `PN:` / `File:` so a Part Number can never collide with a path. Matching is case-sensitive; the Robot and Engine row counts match Inventor with this.
- For one line per component use `Mode: Occurrence`; for one line per file use `Key: FilePath`.

**Structured mode and placements that differ (known limit, owner decided 2026-11-04 to leave as is):** when the same sub-assembly is placed twice and the contents differ (for example a part suppressed in one placement only), `Structured` lists each child once per distinct parent chain, taking the quantity from the first placement it meets in the snapshot. Tested on the robot with two LODs: Structured stayed at 421 either way, and the owner checked both orders with the same result. In theory, if placements differ in the count of a child (2 bolts vs 1), the row's quantity could depend on snapshot order. Not built: using the largest count across placements (recommended if it ever matters). `Flat` and `Occurrence` count occurrences and are not affected.

`BomTotals` (`PartsOnlyRows`, `StructuredRows`, `PartsOnly`, `Structured`) are the shared definitions of the views compared against Inventor's BOM. They always use default rollup rules, not the config. Used by the CLI `totals` command and the fixture tests.

**Validated BOM rules (against Inventor 2027):**
- **Inseparable and Purchased assemblies depend on the BOM view (both verified).** Parts Only shows the assembly as a single item and hides its children; Structured lists the assembly and its children. **Implemented:** the mode default resolves to `AsSingleItem` for Flat and `IncludeChildren` for Structured, for both structures; covered by tests. The snapshot keeps all children and each assembly's `BomStructure`, so other behaviors can be configured.
- **Phantom** assemblies are not listed, but **their children are promoted into the parent** (verified: listed in both views at the top level). **Reference** assemblies are not listed and **their children are left out too** (verified). **Suppressed** excluded. Virtual components counted once.
- **Inventor merges BOM rows that share a Part Number**, even when the files differ. We do the same by default.
- Compare **total quantity and row counts** against Inventor's own numbers.
- If a number differs from Inventor, it is a finding (stale snapshot or an unseen BOM rule). Never paste the tool's number into a test.
- **Not verified:** whether Phantom children promoted into the same parent from two different phantom sub-assemblies merge into one row in Inventor (our Structured mode would keep them under each phantom's own chain; quantities in the test assembly matched).

### 4.5 Configuration (v1: complete)

The config file is the existing Core classes written as JSON, not a second model. **One config file holds many tasks** (decided and confirmed 2026-11-02: one task per deliverable, for example one per vendor). v1 is a single file (splitting into several files is possible later):

```
{ "SchemaVersion": 1,
  "Unresolved": "Block" | "Warn",            // optional, default Block (4.9)
  "Tasks": [ ExportTask... ],                // each task selects its own files by its own Selection
  "Properties": [ SchemaProperty... ],       // a CHECK, selects nothing (4.8)
  "Rollup": { RollupOptions },               // drives the bom command; fallback Mode/Key for tables (4.4)
  "BomColumns": [ { "Name": "...", "Set": "..." }... ] }   // default columns for bom and for tables
```

**What each section does (the owner was confused by this; keep this table accurate):**

| Section | What it does | Applies to |
|---|---|---|
| `Tasks[].Selection` | Picks which files a task exports and lists in its tables (document type, property filters, BOM structure rules, Content Center). | Per task |
| `Tasks[].Outputs` | What the task produces and where: files and tables, each with an optional `Folder`. | Per task |
| `Rollup` (top level) | How the `bom` command counts rows. For tables only `Mode`/`Key` as fallbacks. | `bom`, table fallbacks |
| `Properties` | A check that files have properties (e.g. Finish). Selects nothing. | `props` command only |
| `BomColumns` | Default property columns for `bom` and for tables without their own `Columns`. | `bom`, tables |

A shipped, tested example is `config/export.config.json` (Drawings task: PDF, DWG, DXF with relative ini paths, an Xlsx table with its own columns and a Csv `ERP_Import` table, both in a `Tables` folder; `Finish` property check; Flat parts-only BOM with Part Number, Description, Material; `"Unresolved": "Block"`). `ExampleConfigTests` loads it and requires: a task named `Drawings`, at least one ini file that resolves and exists, non-empty `BomColumns` and `Properties`. **A format change or a typo cannot be allowed to leave it broken.** Other example/experiment configs (not loaded by tests): `config/steponly-export.config.json`, `config/assy-export.config.json`, `config/test-export.config.json` (several tasks, includes a numeric filter), `config/warn-test.config.json` (the `Unresolved: Warn` test).

Decided rules:
- **Strict loading.** Unknown/misspelled names and unknown enum values are rejected (`UnmappedMemberHandling.Disallow`), never ignored. Enums are strings, names are case-insensitive, `//` comments and trailing commas are allowed. A missing or wrong `SchemaVersion` is rejected. Errors list every problem found.
- **Validated on load:** each task through `ExportTaskValidator` (including its `BomStructures` rules, output folders, table settings and filters); task names unique (ignoring case, because they become folder names); at least one task; `DocumentTypes` and `AppliesTo` against the fixed list in `DocumentTypes`; property and BOM column `Set` against the known sets (also for table `Columns`); no blank or duplicate property or column (same Set + Name); `Rollup.BomStructures` (unknown or duplicate structure).
- **Paths:** relative paths (e.g. `IniFile`) are relative to the **folder containing the config file**. **Absolute paths are allowed** (local, vault, shared directory, UNC). A missing ini file is a load error, so a bad path can never fall back to the translator's last-used sheet setting.
- **The loaded config holds resolved absolute paths. Never write it back to disk** (the config editor must keep its own unresolved copy, or relative paths would become absolute).
- `Properties`, `Rollup`, `BomColumns` and `Unresolved` are optional. No `Properties` = no property check; no `Rollup` = `RollupOptions` defaults; no `BomColumns` = only File Name, Qty and Status columns. `ExportConfig.ToPropertySchema()` feeds `PropertySchemaValidator`; `ExportConfig.ToCsvColumns()` feeds `CsvTableWriter`. `PropertySchema.SchemaVersion` is unused (the file's version covers it).
- **CLI wiring (done):** `plan`, `export`, `props` and `bom` load the config **before** collecting from Inventor, so a bad config fails fast. Default config: `config/export.config.json`, found by walking up from the exe (same as the ini files). `--config <file>` overrides. The interactive menu loads the default at startup (a problem is printed, never fatal), and menu item 10 loads or reloads a file; **a config that fails to load never replaces the one already loaded** (see the lesson in section 0). `snapshot` and `totals` do not use a config. `BomTotals` keeps default rollup rules on purpose.
- Removing or renaming a config field is a breaking change: note it as a migration note. Done twice, no schema bump either time because only the owner's configs were affected: (1) `ExcludePhantom`/`ExcludeReference` removed from `Rollup`, replaced by `BomStructures`; (2) v0.6: the short-lived `Package` section on a task was removed, replaced by `Csv`/`Xlsx` outputs (4.13). A config that still has `Package` is rejected. **Behavior change without a config change (2026-11-04):** the defaults for Reference and Purchased children changed (4.11); an old config with no `BomStructures` rules now behaves differently for assemblies of those structures.

### 4.6 Export tasks (implemented, tested)

A **task** is one named deliverable and one folder tree. It has:
- **Selection:** `Scope` (`TopLevelOnly` | `AllUniqueFiles`), `DocumentTypes` (empty = any), `Filters` (`PropertyFilter`: `Set`, `Property`, `Operator`, `Value`; AND-combined; text operators Equals, NotEquals, Contains, IsEmpty, IsNotEmpty (case-insensitive, missing counts as empty) and numeric operators LessThan, LessThanOrEqual, GreaterThan, GreaterThanOrEqual with units (4.14); `Set` defaults to Design Tracking), `BomStructures` (per-structure rules, 4.11) and `ContentCenter` (`Include` default | `Exclude` | `Only`, 4.10). Vendor-style selections work today as ordinary property filters (e.g. Vendor Equals "XXX"). There is **no OR** across values inside one task (use two tasks) and **no "everything the other tasks did not select"** yet (open questions 17, 18). The "rest" workaround is NotEquals filters for each other vendor.
- **Outputs:** list of `OutputSpec`: file formats Step/Pdf/Dwg/Dxf/Dwf (with `Source` Model/Drawing, `PdfSheets`, optional `IniFile` for DWG/DXF) and table formats Csv/Xlsx (4.13). Every output has an optional `Folder`.
- **FolderTemplate:** the task's **root folder** under the output root, with tokens `{task}`, `{format}`, `{type}`. Default `{task}`. Each output's `Folder` is a subfolder below that root (empty = the root itself). `{package}` is **not** a token (loading rejects it). `{type}` is rejected in a task that has a table, because a table covers the whole task. Example layout, built from the owner's wish:

  ```
  Package2/            (FolderTemplate "Package2")
    DWG/  *.dwg, Package2.csv      outputs with "Folder": "DWG"
    PDF/  *.pdf, Package2.csv      outputs with "Folder": "PDF"
  ```

Rules:
- Valid combinations: STEP from Model only; PDF/DWG/DXF from Drawing only; DWF from either; Csv/Xlsx need no `Source`. Invalid combinations, unknown tokens, unknown document types, rooted paths and `..` (in `FolderTemplate` or `Folder`) are rejected up front by `ExportTaskValidator`. **Two outputs with the same format and the same folder (and, for tables, the same name) in one task are rejected.**
- Selection works on **unique model files** (member suffix stripped), not occurrences. **A file is left out only if every one of its occurrences is rejected** (e.g. a bolt inside a Reference assembly is still exported if it also sits elsewhere in the assembly). Suppressed, virtual and unresolved components are never selected.
- **Defaults for file exports (task selection):** Phantom and Reference **items** are left out (same as the BOM and Inventor). **Children of Phantom, Normal, Purchased and Inseparable assemblies are kept; children of Reference assemblies are left out** (changed 2026-11-04 to match the verified BOM). Unlike the BOM, a Purchased or Inseparable assembly's children stay selected, because a file export is not a BOM view. To change any of this, add a `BomStructures` rule (`Include`, `Children`). The exclusion report shows what was left out.
- `ExportPlanner.Plan(snapshot, task, outRoot, exclusions?)` returns `ExportJob`s (`Planned` or `Conflict`) for **file outputs only** (it skips tables). Same output path twice within a task → second is `Conflict`. **`ExportPlanner.PlanAll(snapshot, tasks, outRoot, exclusions?)`** plans every task and also marks an output file two different tasks would both write as `Conflict`, so nothing is overwritten within a run. The CLI always uses `PlanAll`. **`ExportPlanner.PlanTables(tasks, outRoot)`** plans the table outputs (`TableJob`s) with the same Conflict rule across tasks. (Files left over from earlier runs are overwritten by `SaveCopyAs`.)
- **The same file may be delivered into several folders** (e.g. a vendor folder and an archive folder, via two outputs of the same format with different `Folder`): different paths, so no conflict. The Inventor translator runs once per output (copying the first result is a later optimization, **not built and not shown to be worth it**: see the timing in section 0).
- Sheet metal is identified by `DocumentType == "SheetMetal"` (`SubType` is not set by the traverser).
- Selection by BOM structure and Content Center is done. Selection by occurrence (e.g. only parts under a given sub-assembly) is not built (open question 10).

### 4.7 Job runner (implemented, verified)

`JobRunner.Run(jobs, onResult)` returns **exactly one `ExportResult` per file job**. It does not write tables (the CLI does, 4.13).
- `ExportResult.ForJob`: Conflict jobs stay `Conflict`; every other job starts as `Failed` ("Not run") so an unexecuted job can never look like a success (`Exported` is enum value 0).
- Drawing jobs: find drawing via `DrawingLocator`, verify it references the model (`DrawingMismatch` otherwise), `NoDrawing` if none found (the detail names the active project: `... (active project: Engine)`). Model jobs: file must exist.
- Uses the document if the user already has it open; otherwise opens invisibly and closes with `Close(true)`.
- **Written-file check:** after saving, looks for files in the output folder written since the job started (name = planned name or `<base>_Sheet*`). None → `Failed`; extra or renamed files → `Exported` with `Detail = "Files written: ..."`. This catches translators that ignore the planned file name.
- DWG/DXF: sheet 1 is activated before saving (translator exports the active sheet when `ALL SHEETS=No`) and the previous sheet is restored afterwards.
- `ExportResultLog`: `ToCsv` (people), `ToJson` (tools), `Summary` (counts per status), `TimingSummary` (per format: jobs, total, average, slowest, average time opening the file; only exported, timed jobs count).
- **Timing (added 2026-11-04):** `ExportResult.Seconds` (whole job) and `OpenSeconds` (time spent opening the source file, null if it was already open) are set for planned file jobs. `export-log.csv` has two extra last columns, `Seconds` and `OpenSeconds`; the JSON has the same properties. Console lines show `(N.N s)` per job, followed by `Timing:` lines per format and the total run time. Tables, conflicts and unreadable components are not timed.
- `ExportStatus`: `Exported, NoDrawing, DrawingMismatch, Failed, Conflict, Unreadable` (new values are always added at the end so `Exported` stays 0). `Unreadable` is not produced by the runner; the CLI adds one result per unreadable component under `Warn` (4.9) via `ExportResult.ForUnreadable`.
- **Tables in the log:** the CLI adds one result per table output with `ModelPath = "(table)"`, `OutputPath` = the table file, `Detail` = `"N row(s)"` on success, status `Exported`, `Failed` (write error) or `Conflict`. A table that fails to write therefore shows up like any other failure.
- **Failure reporting:** after a run the CLI prints, per task, `Task 'X': complete.` or `!! TASK 'X' IS INCOMPLETE: N output(s) not exported (see export-log.csv)` with the first 10 problems. Failed jobs do **not** change the exit code (only unreadable components give 2).

### 4.8 Property schema validation (implemented, read-only)

`SchemaProperty` (`Set`, `Name`, `DataType`, `Required`, `AppliesTo`) in a `PropertySchema`. `PropertySchemaValidator.Validate(snapshot, schema)` checks each unique model file once and returns `PropertyIssue`s: `Missing`, `Empty` (only when `Required`), `WrongType`. Suppressed, virtual and unresolved components are skipped (an unresolved file has no properties, so checking it would report false `Missing` findings). It reads the snapshot only, so it works after the trial. In the CLI, `props` reads the schema from the config's `Properties` (says so and does nothing if there are none). **It is a global check and selects nothing**; it is not per task (decided to keep it that way: "does every file have a Finish?" is a different question from "which files go to vendor A?").

**Property storage in the snapshot (schema v2):** `ComponentRecord.PropertySets` is `set name → property name → PropertyValue`, mirroring Inventor. Names only have to be unique within a set. Set names are constants in `PropertySetNames` (`Design Tracking Properties`, `Inventor User Defined Properties`). Use `FindProperty(set, name)`. `SetProperty` only changes the in-memory record. An existing but empty set is stored as empty; an unreadable/missing set is absent.

**Decided (2026-10-31): only the Design Tracking and Inventor User Defined property sets are read.** Summary Information and Document Summary Information (Title, Author, Last Saved By, Company, Manager, Thumbnail...) are out of scope: they are not needed for manufacturing exports, and they carry personal and company names. Reopen only with a written decision. `KnownSets` in `ExportConfigJson` and `setNames` in the traverser stay at two entries. Unverified: Design Tracking includes `Part Icon`, a picture; `ToPropertyValue` would store it as a useless string. Check a fixture for it and decide whether to skip picture-valued properties.

**Virtual components (verified 2026-11-04):** their properties are read through `VirtualComponentDefinition.PropertySets`, and both Design Tracking and User Defined properties come through like a normal part's (a custom property typed by the owner arrived intact; `Creation Time` correctly became null, the 1601 empty date). A virtual component's record has `FullPath = null`, `DocumentType = "Virtual"`, `FileName` = the occurrence name with the leading space trimmed (e.g. `ActualVirtualComponent:1`). A leading space in a stored Part Number comes from how the owner typed it in Inventor, not from the tool; property values are not trimmed.

### 4.9 Unresolved components, the export gate and the policy (implemented; Block and Warn both verified in Inventor)

**What an unresolved file looks like to the API:** `occ.ReferencedDocumentDescriptor.FullDocumentName` still reads (the path is known), but `occ.Definition.Document` throws `E_FAIL`. The traverser's error text names the step (`While reading properties: ...`).

**How the traverser records it:** the occurrence is added to `Components` with `IsUnresolved = true`, `DocumentType = "Unknown"`, `FullPath` from the descriptor and no properties, **and** to `Skipped` as `FileMissing` with the file path in `Detail`. Unresolved assemblies are recorded the same way; **their children cannot be seen and are not recorded** (confirmed). `FileMissing` is also used if the file name itself cannot be read.

**In lists:** rollups include unresolved components. The BOM CSV has a `Status` column that says `Unresolved` for them (XLSX: same text plus a light yellow row). Because their type is `Unknown`, an unresolved `.iam` also appears in Parts Only (`IncludeAssemblies = false` does not hide it); it is visible and flagged, which is accepted. An unresolved part inside a Purchased or Inseparable assembly is hidden in Parts Only like any other child (tested on the small unresolved fixture). Planner and property check skip them (4.6, 4.8). They are not reported in the exclusion report (they are handled here).

**Task tables do not list unresolved components (decided by the owner 2026-11-04, deliberate):** an unresolved file has no properties and the type `Unknown`, so the tool cannot tell whether a task would have selected it, and listing it in every task's table could put parts in the wrong vendor's table. It is reported by the banner, the `Unreadable` line in the export log and exit code 2 instead. The `bom` command is different: it lists unresolved rows, flagged.

**What makes the list incomplete:** `SnapshotChecks.BlockingProblems(snapshot)` returns `FileMissing` entries plus `Error` entries whose occurrence is **not** in `Components` (so it is absent from the list). Suppressed, virtual, reference and rule exclusions are deliberate and never count; an `Error` on a component that is still listed (e.g. unreadable virtual properties) does not count.

**Policy (decided 2026-11-02): `"Unresolved": "Block" | "Warn"` in the config, default `Block`.** A third `Skip` option was rejected: for exports it would equal `Warn`, and its only extra effect would be hiding the component from the BOM, which is the silent gap this tool exists to prevent.
- **Block:** `Actions.Export` throws before planning anything, with the first 10 problems listed (one-shot returns exit code 1). `plan` still works and prints a WARNING saying exports will refuse.
- **Warn:** exports run for everything that could be read. A banner `!! THE COMPONENT LIST IS INCOMPLETE !!` prints before the run and again after it, and **each unreadable component gets its own line in the export log** (`Status = Unreadable`, `TaskName = "(collection)"`, in both CSV and JSON). `plan` warns that exports will go ahead.
- The `bom` command prints the same banner whenever components were unreadable (it never blocks: a BOM with flagged rows is a complete output of what was read).
- **Exit codes (one-shot):** `0` clean, `1` error (including the Block refusal), `2` the export ran under `Warn` with unreadable components. `bom` returns 0. The menu ignores the code.

**Verified (2026-11-04, Inventor 2027):** on a copy of a small assembly with one part file renamed, a `Warn` run printed the banner before and after, exported STEP files for the readable parts, wrote one `Unreadable` line (file path and `E_FAIL`) to the export log, and exited with 2. The same config with `Block` refused with the problem list and exited with 1. **Still unverified:** whether our totals match Inventor's when unresolved parts are present: Inventor cannot save a BOM for such an assembly, so there is nothing to compare.

### 4.10 Content Center (implemented, verified flag; filter tested in Core)

`ComponentRecord.IsContentCenter` is `bool?`: `true`/`false` from `PartComponentDefinition.IsContentMember` for parts and sheet metal parts; `false` for virtual components; **null = not recorded** (assemblies, suppressed or unresolved occurrences, snapshots collected before v0.4.1, or Inventor would not say). Verified on an assembly with two Content Center parts added by the owner (the robot fixture now contains them). The folder path is not used to identify them (it is set per project, so fragile).

**Filter (decided):** `ContentCenter: Include | Exclude | Only`, default `Include` (no change for old configs), on a task's `Selection` and on `Rollup`. `Only` keeps just the flagged components, so assemblies are left out in that mode (the report shows them). **A null flag counts as NOT Content Center** (`Exclude` keeps it, `Only` drops it). It is judged per component, not per parent: parts have no children, so there is no child rule. `ContentCenterFilter.Passes/Describe/NotRecorded` hold the logic. When a Content Center rule is active, `plan`, `export` and `bom` print a NOTE with the count of parts that have no flag (suppressed, unresolved and non-part components are not counted, since their flag is null by design), so an old snapshot never misleads.

The summary line `Content Center parts: N (flag not recorded on M parts)` counts only parts and sheet metal in M, so a freshly collected snapshot should show 0 there.

### 4.11 BOM structure rules and the exclusion report (implemented, tested in Core; children defaults VERIFIED against Inventor 2027 on 2026-11-04)

**Rules (decided 2026-11-02):** instead of boolean flags, each task's `Selection` and the `Rollup` section take a list `BomStructures` of rules `{ "Structure": ..., "Include": bool?, "Children": "Include"|"Exclude"? }`. `Structure` is one of Normal, Inseparable, Phantom, Reference, Purchased (unknown names rejected on load, duplicates rejected). `Include` = whether the item itself is listed/exported; `Children` = whether what is inside it is kept. **A field left out keeps its default**, so `{ "Structure": "Phantom", "Children": "Exclude" }` does not turn phantoms on. Example (export a purchased assembly as a file, ignore its children): `"BomStructures": [ { "Structure": "Purchased", "Children": "Exclude" } ]`.

**Defaults (`BomStructurePolicy`, verified against Inventor's own BOM exports of a test assembly with one Phantom, one Reference and one Purchased sub-assembly, two parts each):**

| Structure | The item itself | Its children |
|---|---|---|
| Normal | listed | kept |
| Phantom | **left out** | **kept** (promoted into the parent) |
| Reference | **left out** | **left out** |
| Inseparable | listed | follows the mode: Flat (Parts Only) hides them and keeps the assembly as one item; Structured lists them |
| Purchased | listed | follows the mode, **same as Inseparable** (verified: Parts Only showed the assembly and not its children; Structured showed both) |

A structure Core does not recognize is listed, not dropped. Children are judged by walking up the ancestor chain: any ancestor whose structure says `Children: Exclude` removes the component (reason `ParentExcluded`). In `BomStructurePolicy`, `DefaultChildren(kind)` returns Exclude for Reference, null (= "use the mode default") for Inseparable and Purchased, Include otherwise; `ChildrenOf(kind, modeDefault)` applies an explicit rule first. File-export tasks pass `Include` as the mode default, so a Purchased or Inseparable assembly's children stay selected for export (4.6).

**Exclusion report (resolves open question 2):** the snapshot stays a factual record; applying the rules produces `ExclusionRecord`s (`Context` = "BOM" or the task name, `OccurrencePath`, `FileName`, `Reason`, `Detail`). `ExclusionReason`: `Suppressed, BomStructure, ParentExcluded, DocumentType, PropertyFilter, Scope, ContentCenter`. The BOM reports per occurrence; tasks report **once per unique file**, with the first rejection found. **Deliberately not reported:** the root, virtual and unresolved components, and assemblies hidden by `IncludeAssemblies: false` (a view choice, not a lost part). `Rollup.Apply` and `ExportPlanner.Plan/PlanAll` take an optional `List<ExclusionRecord>`. `BomVisibility` (internal) is the shared "is this component visible under the rules" logic. Files: `export-excluded.csv` (written by `export`) and `bom-<mode>-excluded.csv` (written by `bom`); with nothing excluded the old file is deleted so it cannot mislead. The CLI prints `Left out by rules: <context> / <reason>: <count>` lines after `plan`, `export` and `bom`. The XLSX writer can put the records on a second sheet "Left out" (only if there are any); the `bom` command's temporary XLSX line passes them.

### 4.12 Active project recording and check (implemented, verified)

**Why:** drawing lookup (`DrawingLocator.Find`, via `DesignProjectManager.ResolveFile`) depends on the **active Inventor project**. With the wrong project active, every drawing job reports `NoDrawing`. Collection and STEP model exports do not depend on it (verified).

`Snapshot.ProjectFile` (`string?`, additive, null = not recorded) stores the full file name of the active project at collection (`AssemblyTraverser.TryGetProjectFile`). `SnapshotSummary` shows `Project: <file>` or `(not recorded)`. `SnapshotChecks.ProjectMismatch(snapshot, activeProjectFile)` returns a WARNING string, or null when the projects match (ignoring case) or either side is unknown. `plan` and `export` print it **only if drawing jobs are planned**, and stay quiet when Inventor is not running (`plan --from` offline). Warning only, never blocks (decided: a block would be stricter than the problem deserves). The full path is stored, so two same-named projects in different folders are told apart. It may contain a Windows username: check it before committing a fixture.

### 4.13 Table outputs and XLSX (implemented v0.6, tested in Core; CLI path run by the owner)

**Design (decided 2026-11-02, after the first "package" design was thrown away):** a table is just another **output** of a task, placed in the task's folder tree like a PDF. There is no separate package section and no `packages/` folder.

```json
"Outputs": [
  { "Format": "Pdf",  "Source": "Drawing", "Folder": "PDF" },
  { "Format": "Xlsx", "Name": "CutList",     "Mode": "Flat",       "Columns": [ { "Name": "Part Number" }, { "Name": "Material" } ] },
  { "Format": "Csv",  "Name": "WeldmentBom", "Mode": "Structured" },
  { "Format": "Csv",  "Name": "ErpImport",   "Mode": "Occurrence", "Key": "FilePath", "Folder": "ERP" }
]
```

- `Format` `Csv` or `Xlsx` (appended to the `ExportFormat` enum). `Source` is ignored.
- `Folder`: subfolder of the task's root (empty = the root). `Name`: file name without extension (default: the task name; no invalid file-name characters). `Columns`: property columns after File Name and Qty (default: the config's `BomColumns`). `Mode` / `Key`: rollup mode and key for this table (default: the config's `Rollup.Mode` / `Rollup.Key`). **`Mode` and `Key` are per output because one task often needs several tables with different counting** (decided: cut lists Flat, weldment BOM Structured, ERP import Occurrence). `Name`, `Columns`, `Mode` and `Key` on a file output are rejected.
- **A table lists every file its task selects, wherever the table sits** (decided: no `Covers` field, no `Delivered` column). The CSV/XLSX is a clean manufacturing/MRP document with no delivery status. **What was actually delivered is the export log's job** (`export-log.csv/.json`, with one line per table too, 4.7). **Unresolved components are not listed in a task table** (4.9).
- **Rows (`TaskTable.Rows(snapshot, task, output, config)`):** `Rollup.Apply` with Mode/Key from the output (else the config), `IncludeAssemblies` and `IncludeRoot` on, `Inseparable = IncludeChildren`, the task's own `ContentCenter` and `BomStructures`, then kept only for rows whose file the task selected (`ExportPlanner.SelectedFiles`). So an assembly the task selects appears even if the config's `Rollup.IncludeAssemblies` is false, and the root appears only if the task selects it (e.g. `Scope: TopLevelOnly`). With `Key: PartNumber`, two selected files with the same Part Number share one row labelled with the first file's name. In `Structured` mode a file used under several parents gets a row per parent.
- `TaskTable.Columns(output, config)` gives the columns. `ExportPlanner.PlanTables` gives the paths and detects conflicts. The CLI (`Actions.WriteTables` in `Program.cs`) writes them after the file jobs run.
- **Writers:** `CsvTableWriter.Write(rows, columns)` (columns File Name, Qty, property columns, Status; UTF-8 with BOM is applied by the caller). `XlsxTableWriter.Write(path, rows, columns, exclusions?)` (Core, ClosedXML): sheet "BOM" with bold header, frozen first row, autofilter, auto-sized columns; cell type from the snapshot's `DataType` (Number → numeric cell, Date → date cell `yyyy-mm-dd`, Text/Boolean → text, so `"001"` keeps its zeros; Qty is numeric); unresolved rows light yellow with Status "Unresolved"; optional second sheet "Left out" with the exclusion records when given and non-empty.
- **File Name column and model states:** the snapshot keeps Inventor's raw `FileName`, which can carry a suffix such as `Double Acting Piston.iam<Model State1>` (7). The owner prefers vendors to see the clean name. **The proposed change** (both writers call `PathUtil.StripMemberSuffix(row.Component.FileName)`, plus a test `Table_ShowsTheFileNameWithoutTheModelStateSuffix`) was given at the end of the 2026-11-04 chat. **Owner to confirm it was applied.** Known side effect: with `Key: FilePath`, two model states of one file would show the same name on two rows. The snapshot itself is never stripped.
- **Zip: not built (decided).** When built it is one task-level option that zips the task's root folder (the tree already contains the table and the files). Belongs after the config editor discussion (open question 19). Do not hardcode zip logic in Core (`ZipFile` is not in `netstandard2.0`).
- Known limits: `Actions.WriteTables` and `PrintTaskFailures` live in `Program.cs` and make `Actions` large; move table writing into a small class (Core planner + a CLI-side or Inventor-free runner).

### 4.14 Property filters, numeric operators and units (implemented; units VERIFIED against Inventor 2027 on 2026-11-04)

Operators: text (`Equals`, `NotEquals`, `Contains`, `IsEmpty`, `IsNotEmpty`, case-insensitive) and numeric (`LessThan`, `LessThanOrEqual`, `GreaterThan`, `GreaterThanOrEqual`). Only ANDing exists inside a task (no OR, open question 18). A numeric operator whose `Value` is not a number (with an optional supported unit) is rejected on load; a filter with no `Property` is rejected on load.

**What Inventor stores (verified, snapshot values against what Inventor displayed):**

| Property | Displayed | Stored in the snapshot |
|---|---|---|
| Custom Number property (e.g. `Plain`) | 2 | `2`, Number, exactly as typed |
| `Mass` | 0.204 lbmass | `92.6197`, Number: **grams** |
| `Flat Pattern Width` / `Length` | 2 in / 3 in | `5.08` / `7.62`, Number: **centimetres** |
| Exported parameters (e.g. `Height`, `Width`) | 2.000 in, 50.800 mm | **Text with the display unit**: `"2.000 in"`, `"50.800 mm"` |
| `Sheet Metal Width` / `Length` | mm | Text with the unit: `"50.800 mm"` |
| `Sheet Metal Area` | mm² | Text: `"5913.365 mm^2"` (**area units not supported**) |

So the stored form depends on the property: bare numbers in internal units (cm, g) for built-ins, text with the document's display unit for parameters. A millimetre document and an inch document store parameters in the same `"<number> <unit>"` shape.

**How filters compare (`Units.cs`, `Units.TryCompare`):** both sides are read as a number plus an optional unit word (invariant culture, decimal point). Supported units: **mm, cm, m, in, ft** (length), **g, kg, lb** (mass; `lbmass`, Inventor's own display name, is also accepted in stored values). Rules:
- A filter `Value` with a unit (e.g. `"200 mm"`, `"0.25 lb"`) converts both sides to a base unit (cm or g) before comparing, so inch and millimetre documents compare correctly.
- A bare-number stored value gets a unit only for the built-ins in a small verified table: Design Tracking `Mass` (grams), `Flat Pattern Width` and `Flat Pattern Length` (centimetres). **Verified: `Mass` is in the Design Tracking set**. Any other bare number is a plain number with no unit.
- A filter `Value` **without** a unit compares the raw numbers (the stored value must then have no unit either, so `Mass < 100` means 100 grams and `Plain < 5` works as before).
- A mismatch **fails the filter and is reported** in the exclusion report with the reason, never guessed: a plain stored number against a filter with a unit, a stored value with a unit against a filter without one, a length against a mass, an unsupported unit on either side (e.g. `furlong`, `mm^2`), a non-numeric stored value.
- Missing or non-numeric stored values fail the filter, as before.

**Unverified:** Area and Volume (believed cm² and cm³ as bare numbers, and Area was seen as text with `^2` for sheet metal, so not supported); a regional setting with a decimal comma in stored text (the parser reads only a decimal point).

---

## 5. Current code map

```
OpenInventorExport/               (repo root; .slnx here)
├─ CLAUDE.md, README.md, LICENSE, .gitignore, .gitattributes
├─ Directory.Build.props          # InventorPublicAssemblies path (default: C:\Program Files\Autodesk\Inventor 2027\Bin\Public Assemblies)
├─ config/                        # export.config.json (shipped example, loaded by tests: keep valid), steponly-export.config.json, assy-export.config.json, test-export.config.json, warn-test.config.json (experiments, not loaded by tests), dwg-one-sheet.ini, dxf-one-sheet.ini; CLI looks here
├─ samples/snapshots/             # sanitized snapshot fixtures (*snapshot.json, schema v2): robot, engine, rotary, testing, unresolved-small; .gitignore has !samples/snapshots/*.json
├─ src/
│  ├─ OpenInventorExport.Core/        netstandard2.0, Nullable on. NO implicit usings (add explicit usings). References System.Text.Json and ClosedXML
│  │   Model.cs                   # PropertyDataType, SkipReason, PropertySetNames, PropertyValue, ComponentRecord (incl. IsContentCenter, IsUnresolved), SkippedRecord, Snapshot (incl. ProjectFile)
│  │   SnapshotJson.cs            # indented JSON, enums as strings, schema-version check
│  │   SnapshotSummary.cs         # counts by DocumentType/BomStructure/Virtual, unique files, project, Content Center count, warns on unrecognized document types
│  │   SnapshotChecks.cs          # BlockingProblems (export gate), ProjectMismatch (warning)
│  │   Rollup.cs                  # RollupMode, RollupKey, RollupOptions (+KeyOf), ExportRow, Rollup.Apply (with exclusions; KeptAsSingleItem)
│  │   BomStructurePolicy.cs      # BomStructureKind, ChildHandling, BomStructureRule, BomStructurePolicy (verified defaults, DefaultChildren, KindOf, Validate)
│  │   Exclusions.cs              # ExclusionReason, ExclusionRecord, ExclusionReport (CSV/summary), BomVisibility (internal)
│  │   ContentCenterFilter.cs     # ContentCenterMode, Passes/Describe/NotRecorded
│  │   Units.cs                   # unit-aware numeric comparison for filters: TryParse, TryCompare, built-in table (Mass g, Flat Pattern cm), SupportedList
│  │   BomTotals.cs               # PartsOnlyRows/StructuredRows and totals (match Inventor's views)
│  │   CsvTableWriter.cs          # CSV with escaping; columns are (Set, Name) pairs; last column Status
│  │   XlsxTableWriter.cs         # XLSX via ClosedXML: BOM sheet (+ optional "Left out" sheet), typed cells
│  │   TaskTable.cs               # rows and columns of a task's Csv/Xlsx output (4.13)
│  │   ExportResult.cs            # PdfSheetMode, ExportStatus, ExportResult (+Seconds, OpenSeconds), ForJob / ForUnreadable
│  │   ExportResultLog.cs         # results -> CSV (with Seconds, OpenSeconds) / JSON / summary / TimingSummary
│  │   PathUtil.cs                # StripMemberSuffix, DrawingBaseName
│  │   ExportTask.cs              # ExportFormat (Step, Pdf, Dwg, Dxf, Dwf, Csv, Xlsx), scope, filters (incl. numeric operators), selection (BomStructures, ContentCenter), OutputSpec (Folder, Name, Columns, Mode, Key), ExportTask
│  │   ExportTaskValidator.cs     # combos, folders, tokens, table settings, duplicates, filters (numeric value with optional unit), document types, BOM rules
│  │   ExportPlanner.cs           # JobState, ExportJob, TableJob, Plan(), PlanAll(), PlanTables(), selection per unique file, IsTable, IsNumeric, SelectedFiles
│  │   PropertySchema.cs          # SchemaProperty, PropertySchema, PropertyIssue, PropertySchemaValidator
│  │   DocumentTypes.cs           # fixed list: Part, SheetMetal, Assembly, Virtual, Unknown; IsKnown()
│  │   ExportTaskJson.cs          # strict JSON options (shared), ExportTask <-> JSON, validates
│  │   ExportConfig.cs            # ExportConfig (SchemaVersion, Unresolved, Tasks, Properties, Rollup, BomColumns), UnresolvedPolicy, BomColumn, ExportConfigJson
│  │   ExportConfigFile.cs        # Load(path): parse, validate, resolve ini paths against the config folder
│  ├─ OpenInventorExport.Inventor/    net8.0-windows (net48 pending), ImplicitUsings on
│  │   InventorConnection.cs      # P/Invoke GetActiveObject
│  │   AssemblyTraverser.cs       # recursive walk -> Snapshot (names the failing step in errors; flags unresolved, Content Center; records active project)
│  │   SnapshotCollector.cs       # facade for the CLI (+ GetActiveProjectFile, ReadPartsOnlyBom dev helper; sets PartsOnlyViewEnabled, do not save the assembly)
│  │   InventorProbe.cs           # connection test
│  │   DrawingLocator.cs          # drawing by model name via ResolveFile (.idw, .dwg); ActiveProjectName for messages
│  │   DrawingBatch.cs            # legacy per-model drawing loop; now only helpers (References, FindOpenDocument) are used
│  │   Translators.cs             # lookup + activate translator add-in by ID
│  │   StepExporter.cs, PdfExporter.cs, CadExporter.cs   # translator-based savers
│  │   JobRunner.cs               # executes ExportJobs (files only), one ExportResult each; times each job and the file-open step
│  └─ OpenInventorExport.Cli/         net8.0-windows exe
│      Program.cs                 # command-based CLI (below): Actions (incl. Collect with timer, SelectTasks, WriteTables, PrintTaskFailures), Interactive (incl. AskTask), Cli
├─ tests/OpenInventorExport.Core.Tests/   xUnit
│     SnapshotSerializationTests, RollupTests, ExportPlannerTests, ExportResultTests (incl. TimingSummary),
│     PathUtilTests, PropertySchemaTests, FixtureTotalsTests,
│     ExportTaskJsonTests, ExportConfigTests, ExportConfigFileTests, ConfigPropertiesTests, DocumentTypesTests,
│     ConfigRollupTests, ContentCenterFlagTests, UnresolvedTests,
│     ExampleConfigTests, ProjectCheckTests, BomStructureTests, ContentCenterAndPolicyTests,
│     XlsxTableWriterTests, TableOutputTests, NumericFilterTests, UnitFilterTests, UnresolvedFixtureTests
└─ docs/, schemas/                # planned
```

### CLI usage

**Run with no arguments: interactive menu** (the normal way; nothing to configure in launch settings). The header shows the output folder, loaded snapshot and loaded config. At startup it loads `config/export.config.json` if found. Menu: 1 collect from Inventor (prints the time taken and components per second), 2 load a saved snapshot, 3 summary, 4 plan (dry run; warns about unreadable components, project mismatch, Content Center flags, prints what rules left out, and lists planned files and tables), 5 run exports (asks for confirmation first; Block refuses while components are unresolved; prints per-job times and `Timing:` lines), 6 check properties, 7 write BOM CSV, 8 totals for saved snapshots (pick from `samples/snapshots` or paste a path), 9 change output folder, 10 load (or reload) a config file. Items 4 and 5 ask for a task name when the config has two or more tasks (Enter = all). The snapshot stays loaded between actions; collect again after changing the assembly. Options that need a config load it first (fail early), then the snapshot. **Each Collect overwrites `snapshot.json` in the output folder: copy it under another name if you want to keep it.**

**One-shot commands (for scripts)**: the first argument is the command.

```
totals <snapshot.json>...   Rollup totals from saved snapshots. No Inventor needed.
snapshot [outDir]           Collect from running Inventor, save snapshot.json, print summary.
plan     [outDir]           Collect, save snapshot, print the export plan. Exports nothing.
export   [outDir]           Collect, save snapshot, run the plan, write tables, export-log.csv/.json and export-excluded.csv.
props    [outDir]           Check properties against the config's Properties. Report only.
bom      [outDir]           Write bom-<mode>.csv (e.g. bom-flat.csv) and bom-<mode>-excluded.csv; rollup and columns from the config.
--from <snapshot.json>      (plan/export/props/bom) use a saved snapshot instead of collecting.
--config <file.json>        (plan/export/props/bom) config file; default config/export.config.json.
--only <task>               (plan/export) run just that task. A wrong name fails before Inventor is touched.
```

`outDir` defaults to `./output` (git-ignored). Commands that collect need Inventor running with the assembly as the **active document and the right project active**. Exit codes: 0 done, 1 error, 2 export ran under `Unresolved: Warn` with unreadable components. `Program.cs` has three parts: `Actions` (the work), `Interactive` (menu), `Cli` (one-shot arguments). The CLI project still needs the Inventor interop DLL to build, even for `totals`. The `bom` command has a **temporary** line that also writes `bom-<mode>.xlsx` next to the CSV (placeholder; BOM table formats are not yet configurable).

---

## 6. Domain model (as implemented)

```
Snapshot: SchemaVersion (2), CreatedUtc, InventorVersion, SourceFile, ProjectFile (string?, additive), ModelState, Components[], Skipped[]

ComponentRecord: OccurrencePath, ParentPath, FileName, FullPath, DocumentType (Part/SheetMetal/Assembly/Virtual/Unknown),
  SubType (unused), Quantity, Level, IsSuppressed, IsVirtual, IsContentCenter (bool?), IsUnresolved, BomStructure,
  PropertySets: { setName: { propertyName: PropertyValue } }

PropertyValue: DataType (Text/Number/Date/Boolean), Value (string?, invariant culture; dates ISO "yyyy-MM-ddTHH:mm:ss"; Inventor's 1601 "empty date" -> null)

SkippedRecord: OccurrencePath, Reason (Suppressed, Virtual, ReferenceOnly, FileMissing, Error, ExcludedByRule), Detail
```

`OccurrencePath` is built from occurrence names (e.g. `Top.iam/Sub:1/Bolt:3`); an **occurrence is not a file**. `FullPath` comes from `occ.ReferencedDocumentDescriptor.FullDocumentName` and **can carry a `<member or model state>` suffix** (verified: `Double Acting Piston.iam<Model State1>`); `FileName` is then taken from it, so it carries the suffix too. The snapshot keeps the raw value; stripping happens where a real path is needed (`PathUtil.StripMemberSuffix`) and, if applied, in the table writers (4.13). `BomStructure` is stored as Inventor's enum name (`kNormalBOMStructure`, `kInseparableBOMStructure`, `kPhantomBOMStructure`, `kReferenceBOMStructure`, `kPurchasedBOMStructure`), mapped by `BomStructurePolicy.KindOf`.

Traverser: records the top-level assembly as Level 0 (its `BomStructure` is **null by design**: the root is not an occurrence, nothing reads it, and a made-up value would be worse; `SnapshotSummary` labels it `(root)`); records suppressed occurrences (reason Suppressed) and does not recurse into them (their `DocumentType` shows "Unknown" because `occ.Definition` throws; cosmetic); `SheetMetal` set via `DocumentType`; flags virtual components and Content Center parts; records `BomStructure`; records the active project; reads Design Tracking + User Defined sets (all properties, ~60 per component; configurable column selection is later); records unresolved files as in 4.9; wraps each occurrence in try/catch (reason Error) and names the step that failed. A component that fails outright is only in `Skipped` as `Error` and **not** in `Components`; it counts as an unreadable component (4.9). Virtual component names are trimmed (leading space). The traverser records the active model state's name but **not** the active level of detail (4.3).

Document types the traverser emits are the list in `DocumentTypes` (Weldment is reported as Assembly). `SnapshotSummary` warns about any other value, and `FixtureTotalsTests` fails if a fixture contains one. Core cannot use Inventor's `ObjectTypeEnum` (no interop in Core), and that enum is far larger than the four document kinds we need.

---

## 7. Inventor API knowledge and gotchas (learned so far)

- Inventor collections are **1-indexed**.
- Interop interface types need **explicit casts** (`AssemblyDocument`/`DrawingDocument` → `Document`, CS1503).
- Our namespace `OpenInventorExport.Inventor` collides with Autodesk's `Inventor`: always alias `using Inv = global::Inventor;`.
- `PropertyDefinition` clashes with `System.Reflection.Metadata` under implicit usings; ours is `SchemaProperty`.
- The interop DLL is referenced by `HintPath` via `Directory.Build.props`; **never commit it**. A plain `<Reference>` does not flow to dependent projects (hence the facade rule).
- Model state lookup: `asm.ComponentDefinition.ModelStates.ActiveModelState.Name` (`[Primary]` on 2027). Assemblies placed in a non-primary state show a suffix like `<Model State1>` on the occurrence's referenced document name.
- Active project: `app.DesignProjectManager.ActiveDesignProject.FullFileName` (verified; used by `DrawingLocator` and the snapshot).
- **BOM structures (verified 2026-11-04, Inventor 2027):** Phantom: item not listed, children promoted into the parent. Reference: item and children not listed. Purchased: behaves like Inseparable (Parts Only lists the assembly and hides its children; Structured lists both). Normal sub-assemblies show their children in Structured and in Parts Only. See 4.4 and 4.11. Inventor's BOM export columns include `BOM Structure` (shows `Normal`, `Purchased`, ...), which is how a mis-set structure was caught in the first test round.
- Inventor's **Structured BOM lists each unique sub-assembly's contents once per distinct parent chain**; **Parts Only** multiplies through levels. Don't compare raw occurrence counts to either.
- Property gotchas: Windows "empty date" is 1601-01-01; values are `object`; read per property set. **Units (verified 2026-11-04):** custom Number properties are stored as typed; `Mass` as a bare number in **grams**; `Flat Pattern Width/Length` in **centimetres**; exported parameters (including sheet metal width/length) are **Text with the display unit** (`"2.000 in"`, `"50.800 mm"`); area came as text with `^2`. See 4.14.
- **Virtual components:** properties are read through `VirtualComponentDefinition.PropertySets` and come through like a part's (verified). The occurrence name has a leading space (trimmed for `FileName`).
- `FullDocumentName` **can carry a `<member>` or model-state suffix** (e.g. `Motor.ipt<No Cover>`). Strip it (`PathUtil.StripMemberSuffix`) before comparing with `Document.FullFileName` or passing to translators. STEP therefore exports the file's primary state, not that variant. Inventor's BOM compare must key virtual components separately (no `Document`).
- **Unresolved files:** the descriptor name reads, `occ.Definition.Document` throws `E_FAIL` (4.9). The bolted connections (Design Accelerator assemblies) in the rotary assembly are examples; the small test assembly with a renamed part shows the same. An unresolved assembly hides its children. Inventor cannot save a BOM for an assembly with unresolved files.
- **Content Center:** `PartComponentDefinition.IsContentMember` works (verified).
- **Active project and drawings (verified):** drawing lookup goes through the active project's search paths, so the wrong project active gives `NoDrawing` for every drawing job; model-only (STEP) exports are unaffected (4.12).
- **Levels of detail (observed 2026-11-04):** with a suppressed part in a second LOD of the robot, the snapshot had the same 542 components and the same model state name, but 8 suppressed occurrences instead of 6; Parts Only 444 vs 445, Structured 421 in both. The two newly suppressed parts sat in the second placement of a repeated assembly (the first placement was intact). The active LOD is not recorded (4.3). The API call to read it was not tried.
- **Not yet verified:** iPart / iAssembly members (parked); quantity overrides in Inventor's BOM; patterns; COM object release; picture-valued properties (`Part Icon`); Area/Volume storage; built-in numbers in millimetre documents; non-primary model states as such.
- **Drawings are not discoverable from the model.** Current strategy: same base name, any project path (`ResolveFile`). A configurable association strategy is still open.
- **Translators:** IDs confirmed for PDF `{0AC6FD96-2F4D-42CE-8BE0-8AEA580399E4}`, DWG `{C24E3AC2-122E-11D5-8E91-0010B541CD80}`, DXF `{C24E3AC4-122E-11D5-8E91-0010B541CD80}`, DWF `{0AC6FD95-2F4D-42CE-8BE0-8AEA580399E4}`, STEP `{90AF7F40-0C01-11D5-8E83-0010B541CD80}`. Use a fresh `NameValueMap` and set only needed options.
  - STEP: `ApplicationProtocolType=3` (AP214) after `HasSaveCopyAsOptions`.
  - PDF: `All_Color_AS_Black`, `Remove_Line_Weights`, `Vector_Resolution`, `Sheet_Range` (`AllInOne` and `FirstOnly` verified). `FirstOnly` activates sheet 1 and does not restore the previously active sheet (acceptable; owner decided restoring is not needed).
  - DWF: `Launch_Viewer=0`, `Publish_All_Component_Props=1`, `Publish_All_Physical_Props=1`; `Publish_3D_Models=1` for models.
  - DWG/DXF: `USE_TRANSMITTAL="No"`; `Export_Acad_IniFile` optional. **Do not call `HasSaveCopyAsOptions` for DWG/DXF** (E_INVALIDARG earlier).
  - **DWG/DXF sheets:** with `ALL SHEETS=Yes` (in the ini, `[EXPORT DESTINATION]`) the translator writes one file per sheet named `<name>_Sheet_N` and ignores the planned filename. With `ALL SHEETS=No` it exports the **active sheet** (verified), under the planned name. The setting is stored **per format** and is controlled only through the ini passed as `Export_Acad_IniFile` (not through `NameValueMap`). With no ini, the translator uses whatever was last saved from the dialog. Fix: pass one-sheet ini files (saved from each dialog via "Save Configuration...", `config/`), and the runner activates sheet 1 first. Check other ini settings when saving (e.g. `USE CUSTOMIZE`, `DESTINATION DXF` differs per format, `SHOW DESTINATION PAGE`).
- **`Application.SilentOperation = true`** during batch exports suppresses dialogs; always restore the previous value in `finally`.
- Drawings and models are opened invisibly (`Documents.Open(path, false)`) and closed with `Close(true)` unless the user already had them open. Opening a drawing took **0.1–0.2 s on average** in the robot export (4.7, section 0).

---

## 8. Roadmap

| # | Milestone | Status |
|---|---|---|
| 0 | Repo + solution skeleton | Done |
| 1 | Traverse & dump (`snapshot.json`) | Done |
| 2 | Rollup engine + basic CSV | Done, validated vs Inventor (totals and row counts) |
| 2.5 | Edge-case assembly review | Done |
| 7 | File export: translators, planner, runner, result log | **Done** (STEP, PDF, DWG, DXF, DWF verified; timing measured) |
| 4 | Property schema validation (read-only) | **Done** (Core). Snapshot property sets restructured (schema v2) |
| 2.9 | Fixtures + regression totals test | **Done** (v0.3.9) |
| 2.95 | Content Center flag; unresolved components + export gate | **Done** (v0.4.1, v0.4.2) |
| 3 | Config file v1 (tasks, schema, rollup, BOM columns from JSON, wired into the CLI) | **Done** (v0.3.10, v0.4, v0.5) |
| 3.5 | Active project recorded and checked | **Done** (v0.5) |
| 5 | Rules engine | **Done in Core and verified against Inventor** for BOM structure rules, exclusion report, Content Center, unresolved policy (Block and Warn) and numeric filters with units. Occurrence-level rules and `Remainder` not built |
| 6a | XLSX writer; table outputs (Csv/Xlsx) with folders, names, columns, per-output rollup; `--only` | **Done** (v0.6) |
| 6b | Zip option (zips a task's root folder) | Deferred (open question 19) |
| 8 | Config Editor UI (WPF) | Later |
| 9 | Polish, add-in launcher, `net48` build/testing on 2023/2024, installer, release | Later |

Resist building UI early; a good config *format* first.

---

## 9. Coding conventions

- Small classes, clear names, XML doc comments on public members.
- No Inventor types outside `OpenInventorExport.Inventor`.
- **Never write to Inventor documents.** Exports use `SaveCopyAs` and `Close(true)` only. Any temporary UI state change (e.g. active sheet) is restored where it matters.
- Errors on a single component are caught, logged, and recorded in `Skipped`; they never abort the collection. A component that cannot be read blocks the *export* instead, unless the config says `Unresolved: Warn` (4.9).
- Core: explicit `using` statements (netstandard2.0, no implicit usings). Inventor/Cli: `ImplicitUsings` enabled.
- Every Core feature gets a unit test (Arrange/Act/Assert). Inventor-dependent code gets a manual checklist in `docs/`. **A test must never assert a value that sits on a floating-point edge** (2026-11-04: `7.619999999999999` against `3 in` = 7.62 failed; use a clearly different value).
- Config schema changes require `schemaVersion` bump and migration note. Additive snapshot fields get an honest default for old data instead of a version bump (4.3). New enum values go at the end of the enum. Optional config settings that mean "inherit" are nullable (`RollupMode?`, `RollupKey?`) so "not set" differs from a real value.
- Config and other hand-edited JSON are loaded strictly (unknown names rejected), with every problem reported at once.
- Anything a rule leaves out must be reportable (exclusion report). Defaults reproduce Inventor's behavior unless the owner decides otherwise; where Inventor's behavior has been verified, the verified behavior is the default.
- Logging: planned `Microsoft.Extensions.Logging` to a file next to outputs.
- `Program.cs` stays thin: parse arguments, call Core/Inventor, print. (It has grown: see the limit noted in 4.13.)

---

## 10. Git workflow

- `main` is always buildable. One short-lived branch per task (`feature/...`, `fix/...`).
- Commit often, imperative messages. **Push after committing**, and check the GitHub page.
- Merge via Pull Request even when solo. Tag milestones **in order** (`v0.6.1` is the latest; the owner picks the next number).
- `.gitignore` includes `bin/`, `obj/`, `output/`, `snapshot*.json`, with `!samples/snapshots/*.json` for intentional fixtures. Note that `snapshot*.json` matches only file names that start with `snapshot`; fixture names ending in `-snapshot.json` are matched by the `!samples/snapshots/*.json` rule anyway.
- Never commit: Inventor DLLs, third-party or employer models, files with usernames or real names.

---

## 11. Open questions

1. Drawing association strategy for file exports (same folder/same name? naming rule?). Owner to describe typical workflow. Partly addressed: the active project is now recorded and checked (4.12), but association is still "same base name via the project paths".
2. ~~Excluded phantom/reference items silently dropped~~ **Resolved:** exclusion report (4.11); per-structure include/children rules are configurable per task and for the BOM.
3. ~~Write-back~~ **Resolved: read-only, never write to Inventor.**
4. File-export destination types: local folder, network share, zip? (Zip: see 19.)
5. Single user or shared team config location? Partly answered: absolute paths in a config are allowed (so a shared team config can point at a network share); the config location itself is still open.
6. ~~Rollup key default~~ **Resolved: Part Number default (empty falls back to file path), `FilePath` configurable** (see 4.4).
7. Add-in launcher timing, and who will test `net48` on 2023/2024.
8. ~~DWG/DXF multi-sheet~~ **Resolved:** `ALL SHEETS=Yes|No` in the ini, per format, only via `Export_Acad_IniFile`; runner activates sheet 1. Still open: per-export ini setting in config (partly done: `IniFile` is per output in the task).
9. Output collisions: reported as `Conflict` (within a task and across tasks via `PlanAll` / `PlanTables`). Different `Folder`s are different paths, so the same file in two folders is fine. Still open: a rule to disambiguate on request (suffix).
10. ~~Task selection by BOM structure / Content Center~~ **Resolved:** both are in `Selection` (4.10, 4.11). **Still open:** occurrence-level rules (e.g. only parts under a given sub-assembly); build only when a real need appears (additive config field).
11. ~~How should unresolved components be reported?~~ **Resolved** (4.9): listed in the BOM flagged, recorded as `FileMissing`, policy Block/Warn, not listed in task tables (owner decision 2026-11-04). Still unverified: totals against Inventor with unresolved items (no Inventor BOM exists to compare).
12. ~~Summary Information / Document Summary Information property sets~~ **Resolved: not read** (see 4.8).
13. ~~Unresolved-component policy~~ **Resolved:** `Unresolved: Block | Warn`, default Block, no Skip option (4.9). Exit code 2 for an incomplete Warn run. Both paths verified in Inventor.
14. ~~Children defaults for Phantom, Reference and Purchased assemblies~~ **Resolved 2026-11-04, verified in Inventor 2027:** Phantom item out and children kept; Reference item and children out; Purchased like Inseparable (4.11).
15. Should `bom` also return exit code 2 when components were unreadable? Currently 0 (it never blocks). Ask only if a script needs it. Same question for failed export jobs: currently they do not change the exit code.
16. Should assemblies hidden by `IncludeAssemblies: false` be reported in the exclusion report? Currently not (view choice, not a lost part).
17. **`Remainder` selection** (`"Selection": { "Remainder": true }` = every file no other task in the config selects). Recommended design: judged against **all** tasks in the config, not just the ones run with `--only`, so the result does not depend on how the tool is run; ordinary overlap between tasks stays allowed. Owner liked the idea but wants the overall export model settled first. Not built. Until then, use NotEquals filters for the other vendors.
18. **OR inside a task** (e.g. Vendor ABC or DEF): today needs two tasks. A possible `In` operator with a list of values; add only if the owner asks.
19. **Zip option** for a task's folder tree. Deferred to later (config editor era). Design when built: one task-level option that zips the task's root folder; zip code stays outside Core (`ZipFile` is not in `netstandard2.0`).
20. ~~Units for numeric filters~~ **Resolved 2026-11-04** (4.14): filters accept units (`"200 mm"`, `"0.25 lb"`); mm, cm, m, in, ft, g, kg, lb supported; mismatches fail and are reported. Area and Volume are not supported.
21. Per-task log file inside the task folder? Currently the one `export-log.csv/.json` in the output root has everything (with a task name on each line); a vendor probably should not receive the failure list. Add only if asked.
22. BOM table formats for the `bom` command (XLSX/CSV choice from config) are not configurable; the XLSX line in `Actions.Bom` is a temporary placeholder.
23. **Structured mode when placements of one assembly differ** (4.4): unknown qty behaviour. 
24. **Clean file name in tables** (4.13): code and test were proposed (strip the `<member>` suffix in the CSV and XLSX writers). Confirmed clean file names in tables. Open sub-question: two model states of one file would show the same name with `Key: FilePath`. Confirmed, same names. 
25. **`Part Icon` and other picture-valued properties** (4.8): not checked in a fixture yet.
26. **Stale wording to clean up:** the usage text in `Program.cs` still says `--only <task>   (plan/export) run just that task and its package.` and `ExportPlanner.SelectedFiles` has a comment mentioning "the package table". The "package" concept no longer exists.
27. **iPart / iAssembly members:** parked by the owner until the rest is done (section 0).
28. **Export speed optimizations (open-once per drawing, copy a result to a second folder):** measured and judged not worth building (section 0). Revisit only if a config delivers one file and format to many folders, or if reruns become painful (a possible lever then: skip jobs whose output is newer than the source, with the caveat that a drawing depends on every part in it).
29. **Phantom children from two phantom sub-assemblies under one parent:** does Inventor merge them into one row? Unverified (4.4).

---

## 12. Definition of done (any task)

- Builds without new warnings; `net48` target must build too once re-enabled
- Core logic unit-tested
- Tested manually against at least one real assembly (note which)
- Skipped/failed components reported, never hidden
- README/docs updated if behavior or config changed
- Committed **and pushed** with a clear message
