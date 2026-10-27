# Inventor Export Tool — Project Guide

> This file is the source of truth for Claude (and any human contributor) working on this project.
> Read it fully before suggesting code or architecture. Update it whenever a decision changes.

---

## 1. Purpose

An open-source tool that exports Autodesk Inventor assemblies into **reliable, table-based component lists** (BOM-style) plus the **associated files** (drawings, models, neutral formats).

**Why it exists:** Inventor's built-in export misses components depending on component state (suppressed, virtual, etc.), level of detail / model state, configuration, and other factors. This tool traverses the assembly itself and is explicit about what it includes and why.

**Core promise:** Every occurrence is either exported or explicitly reported as skipped with a reason. Nothing is silently missing.

---

## 2. About the project owner (how Claude should behave)

- The owner is an experienced Inventor user and has built something similar before, but is **not a software engineer**.
- Claude acts as **architect and mentor**, not just a code generator:
  - Explain *why* before *how*. Keep explanations short and plain-language.
  - Prefer the simplest approach that works. Push back on over-engineering.
  - Work in **small, testable steps**. Never dump a whole application at once.
  - Tell the owner exactly which file to create/edit and where it goes.
  - Flag risks early (licensing, Inventor API quirks, scope creep).
  - When the owner proposes something that will cause pain later, say so kindly and offer an alternative.
  - Remind the owner to commit to git at sensible checkpoints.
  - Define jargon the first time it is used.
- **Do not reuse or reconstruct code from the owner's previous employer.** This project is built clean-room from the owner's own knowledge of the *problem*, not from prior source code.

---

## 3. Scope

### In scope
1. **Collect** — traverse an Inventor assembly and gather every occurrence with configured properties.
2. **Export tables** — CSV, XLSX, database (SQLite first), JSON; extensible to others.
3. **Export files** — per component: DWG, DXF, STEP, PDF, others via a task system.
4. **Property schema configuration** — define custom iProperties (name, type, default, where they live) and apply/validate them in Inventor.
5. **Export configuration** — define how many exports, what each contains, rules for inclusion, file naming, destinations.
6. **Rules engine** — include/exclude/route components based on model type, file name, and properties.

### Out of scope (for now)
- Vault / PDM integration (design so it can be added later)
- Cloud/web hosting
- Non-Windows platforms (Inventor is Windows-only)
- Editing geometry

---

## 4. Architecture decisions

### 4.1 Technology stack

| Concern | Decision | Reason |
|---|---|---|
| Language | **C#** | Best-supported Inventor API language, strong tooling |
| Runtime | **Multi-target.** Inventor 2024 and older run add-ins on .NET Framework 4.8; Inventor 2025+ runs add-ins on .NET 8 (2027 keeps .NET 8 add-ins working without recompiling). `InvExport.Core` targets `netstandard2.0` (loads in both). `InvExport.Inventor` and `InvExport.AddIn` multi-target `net48` and `net8.0-windows`. `InvExport.Cli` is an out-of-process exe and is not tied to Inventor's .NET version | A single .NET Framework 4.8 add-in is not reliable on 2025+ (not fully compatible with .NET 8), so one build cannot cover all versions |
| Supported versions | **Inventor 2023 and newer** (decided). 2023–2024 use the `net48` build; 2025+ use the `net8.0-windows` build. Do not add older versions without a written decision in `docs/decisions/` | Each version added multiplies testing; 2023 already includes Model States |
| IDE | Visual Studio Community (free) | Add-in templates, debugging attached to Inventor |
| Config format | **JSON** files + JSON Schema | Human-readable, diffable in git, shareable |
| XLSX writer | **ClosedXML** (MIT) | Do not use EPPlus (restrictive license for an open-source project) |
| Database | **SQLite** first (Microsoft.Data.Sqlite), others later | Zero setup |
| Config UI | **WPF desktop app** (separate from Inventor) | Windows-only anyway; mature; works without Inventor running |
| Tests | xUnit | Standard |
| License | **MIT or Apache-2.0** (decide before first public commit) | Permissive, open-source friendly |

### 4.2 Shape of the system

Three layers, kept strictly separate:

```
┌──────────────────────────────────────────────────────────────┐
│  FRONT ENDS (thin — no business logic)                       │
│  • Inventor Add-in  (ribbon button: "Run Export")            │
│  • Config Editor    (WPF app: edit export + property config) │
│  • CLI runner       (headless / batch / scheduled)           │
└───────────────┬──────────────────────────────────────────────┘
                │
┌───────────────▼──────────────────────────────────────────────┐
│  INVENTOR ADAPTER (only layer that touches Inventor API)     │
│  • AssemblyTraverser   • PropertyReader/Writer               │
│  • Translators (DWG/DXF/STEP/PDF exporters)                  │
└───────────────┬──────────────────────────────────────────────┘
                │ produces / consumes plain data objects
┌───────────────▼──────────────────────────────────────────────┐
│  CORE (pure C#, NO Inventor references, fully unit-testable) │
│  • Config models + validation   • Rules engine               │
│  • Snapshot model (components)  • Table writers (CSV/XLSX/DB)│
│  • Export task planning/queue                                │
└──────────────────────────────────────────────────────────────┘
```

**Key rule:** `Core` must never reference `Autodesk.Inventor.Interop`. This is what makes the project testable and keeps it maintainable.

### 4.3 Two-phase pipeline (non-negotiable)

1. **Phase 1 — Collect.** Traverse the assembly, read all configured properties, produce an in-memory **Snapshot** (list of `ComponentRecord`s plus a list of `SkippedRecord`s with reasons). Can be saved as `snapshot.json`.
2. **Phase 2 — Export.** From the Snapshot only (no more traversal): apply rules → write tables → plan and run file-export tasks.

Why: the snapshot can be saved, diffed, reviewed, and used in tests *without Inventor installed*. Table export bugs and traversal bugs become separable.

### 4.4 Where configuration lives

Two config domains, both JSON, both stored in a user-chosen folder (ideally inside a git repo or shared drive):

- **`properties.schema.json`** — the *Inventor property configuration*: which custom iProperties exist, their data types, defaults, which document types they apply to, required/optional, mapping/aliases.
- **`exports/*.export.json`** — one file per *export definition*: sources, columns, rules, file outputs, destinations.

The **Config Editor (WPF)** edits these files. The **Add-in** and **CLI** only *read* them and run. This keeps the Inventor add-in tiny.

---

## 5. Domain model (starting point — refine as we go)

```
ComponentRecord
  Id (stable, e.g. full occurrence path)
  OccurrencePath        // "Top/Sub1:1/Part7:2"
  FileName, FullPath
  DocumentType          // Part, Assembly, Drawing(n/a), Presentation
  SubType               // SheetMetal, Weldment, iPart, Content Center, Virtual, Derived…
  Quantity
  Level                 // depth in tree
  ParentId
  State                 // Suppressed, Visible, Virtual, Reference, etc.
  Properties            // Dictionary<string, PropertyValue>
  AssociatedFiles       // resolved drawing path, etc.

SkippedRecord
  OccurrencePath, Reason (enum + text)

PropertyDefinition
  Name, PropertySet (Summary / DocSummary / Design Tracking / User Defined)
  DataType (Text, Number, Date, Boolean)
  Default, Required, AppliesTo (doc types), Format

ExportDefinition
  Name, Enabled
  Source (assembly path or "active document")
  TraversalOptions (levels, suppressed handling, virtual handling, model state…)
  Columns (property → header, order, formatting)
  Rules[] (conditions → include/exclude/route/override)
  TableOutputs[] (csv, xlsx, sqlite…, with destination + naming)
  FileOutputs[] (dwg, dxf, step, pdf…, with rules, naming, destination)

Rule
  Conditions: model type, file name (glob/regex), property comparisons (equals, contains, empty, etc.)
  Action: Include | Exclude | Tag | RouteToOutput(name)
```

---

## 6. Inventor API considerations (read before writing traversal code)

These are the usual reasons exports "miss components" and must be handled **explicitly and configurably**:

- **Suppressed components** — include/exclude option; always log.
- **Level of Detail / Model State / Representations** — traversal result depends on the active state. Make the state an explicit config option and record which was used in the output.
- **Virtual components** — no file on disk; have properties but no path.
- **Content Center / standard parts, iParts, iAssemblies** — members vs factories; filename ≠ part number.
- **Reference / phantom / derived components** — decide per export.
- **Patterns and sub-assembly instances** — quantity counting (same file, many occurrences).
- **Weldments, frame members, sheet metal** — special subtypes with extra properties.
- **Referenced documents not loaded / missing** — report as `SkippedRecord`, never crash.
- **Drawings are not discoverable from the model.** A drawing references a model, not the other way. Drawing association needs a configurable strategy: same-name-in-same-folder, search paths, naming rules, or (later) Vault.
- **Property sets** — Summary Information, Document Summary, Design Tracking, User Defined (custom). Custom properties are typed; reading blindly as text causes bad data.
- **Parameter-linked properties** — a custom property can be linked to a parameter; handle read-only/linked cases.
- **COM object lifetime** — release COM references; avoid holding large collections; test on large assemblies.
- **Performance** — opening documents is slow. Consider Apprentice Server for read-only collection (note: verify what it can/can't do before relying on it). Open documents invisibly where possible.
- **Add-in vs. out-of-process** — add-in runs inside Inventor (fast, simple UI hooks). CLI/standalone connects to a running Inventor via COM.
- **Translators** — DWG/DXF/STEP/PDF use Inventor's translator add-ins with option maps (`NameValueMap`). Each format needs its own options in config.
- **The Inventor interop DLL** (`Autodesk.Inventor.Interop.dll`) ships with Inventor. **Do not commit it to the repo.** Reference it from the install path (via a build property) and document this in the README.

---

## 7. Repository layout

```
InventorExportTool/
├─ README.md                  # what it is, how to build, how to use
├─ CLAUDE.md                  # this file
├─ LICENSE
├─ .gitignore                 # Visual Studio / .NET template
├─ docs/
│  ├─ decisions/              # short "ADR" notes: why we chose X
│  └─ config-reference.md
├─ src/
│  ├─ InvExport.Core/         # pure C#: models, rules, writers, task planner
│  ├─ InvExport.Inventor/     # Inventor adapter: traversal, properties, translators
│  ├─ InvExport.AddIn/        # Inventor add-in front end
│  ├─ InvExport.Cli/          # command-line runner
│  └─ InvExport.ConfigEditor/ # WPF configuration UI
├─ tests/
│  ├─ InvExport.Core.Tests/
│  └─ InvExport.Inventor.Tests/   # manual/integration (needs Inventor)
├─ samples/
│  ├─ configs/                # example property + export configs
│  └─ snapshots/              # example snapshot.json files
└─ schemas/                   # JSON Schema for config files
```

---

## 8. Roadmap (build in this order)

Each milestone should be working, committed, and tagged before the next begins.

| # | Milestone | Done when |
|---|---|---|
| 0 | **Repo + solution skeleton** | Builds empty; README, LICENSE, .gitignore committed |
| 1 | **Traverse & dump** | CLI/add-in walks active assembly, writes `snapshot.json` incl. skipped list |
| 2 | **Basic CSV export** | Snapshot → CSV with hardcoded columns |
| 3 | **Config file v1** | Columns + traversal options come from JSON export definition |
| 4 | **Property schema** | Define custom properties; validate/create them on documents |
| 5 | **Rules engine** | Include/exclude by type, filename, property |
| 6 | **More table outputs** | XLSX, SQLite, multiple exports per run |
| 7 | **File export tasks** | PDF, DWG, DXF, STEP with naming/destination rules; task log |
| 8 | **Config Editor UI** | Edit both config types without hand-editing JSON |
| 9 | **Polish** | Error reporting, progress, docs, installer, release |

> Resist building the UI early. A good config *format* first; the UI is just an editor for it.

---

## 9. Coding conventions

- Small classes, clear names, XML doc comments on public members.
- No Inventor types leaking out of `InvExport.Inventor`.
- All errors on a single component are caught, logged, and recorded — they must not abort the whole export.
- Use dependency injection lightly (constructor injection; no heavy frameworks needed initially).
- Logging via `Microsoft.Extensions.Logging` (or Serilog) to a file next to the export output.
- Every Core feature gets a unit test. Inventor-dependent code gets a manual test checklist in `docs/`.
- Config changes that break old files require a `schemaVersion` bump and a migration note.

---

## 10. Git workflow (keep it simple)

- `main` is always buildable.
- One short-lived branch per task: `feature/csv-export`, `fix/virtual-components`.
- Commit often, with messages in the form: `Add CSV writer for snapshot` (imperative, specific).
- Merge to `main` via Pull Request on GitHub even when working alone (gives a review checkpoint and history).
- Tag milestones: `v0.1-traverse`, `v0.2-csv`…
- Never commit: Inventor DLLs, customer/employer files, real part data, secrets, `bin/` `obj/`.

---

## 11. Open questions (resolve before/during the relevant milestone)

1. ~~Which Inventor version(s)?~~ **Resolved: 2023 and newer**, via multi-targeting (net48 + net8.0-windows). Prefer the CLI (out-of-process) as the primary engine so one build works across versions; the add-in is a thin launcher. Project/repo name: **OpenInventorExport** (use `OpenInventorExport.*` as the namespace/project prefix instead of `InvExport.*` used in the diagrams above).
2. Add-in only, or also standalone CLI in v1?
3. How are **drawings matched** to models in the owner's typical workflow?
4. Should **quantities** be rolled up (flat BOM) or listed per occurrence (structured)? Likely both, as options.
5. Should the tool **write back** to Inventor (create/repair custom properties) in v1, or only read?
6. File-export **destination** types: local folder, network share, zip — anything else?
7. Single user or shared team config location?

---

## 12. Definition of done (for any task)

- Builds with no warnings introduced
- Core logic covered by unit tests
- Tested manually against at least one real assembly (note which)
- Skipped/failed components are reported, not hidden
- README/docs updated if behavior or config changed
- Committed with a clear message
