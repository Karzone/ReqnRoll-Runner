# Reqnroll Runner — Implementation Spec

> **This is the original v1 specification, kept as written.** Where implementation found reality to
> differ — Reqnroll rejects duplicate scenario titles rather than deduping them; generated identifiers
> are PascalCase, not underscore-separated; one `FullyQualifiedName` filter turned out to cover all
> three runners — the corrections are recorded in [`docs/architecture.md`](docs/architecture.md)
> rather than edited in here, so the original intent stays legible.

Working name: ReqnrollRunner (rename freely; avoid implying it is the official Reqnroll extension)
License: MIT
Author: Karthik (solo)
Status: v1 spec — Visual Studio primary target, VS Code as a later thin head over the same core

## 1. What we are building and why

A companion Visual Studio extension (VSIX) that lets a user run or debug any Reqnroll scenario
directly from the `.feature` file — cursor on a scenario (or right-click), choose Run Scenario /
Debug Scenario, and the corresponding generated test executes, with debug attaching so breakpoints in
step definitions hit.

### Background (read this before coding)

* This feature was requested in Reqnroll discussion #270 ("VS Extension: Run/Debug test from feature
  file", Sep 2024). The maintainer (Gáspár Nagy) confirmed it is wanted but explained why it was
  never built:
  1. Visual Studio's native run/debug-from-editor test commands (Ctrl+R,T / Ctrl+R,Ctrl+T) are
     hard-coded inside VS to work only for `.cs` and a few other file types. Do not attempt to unlock
     or extend the native commands.
  2. A previous attempt to hook the native command, switch to the generated code-behind file, and
     replay the command there was "very brittle". Do not use command-replay on the code-behind.
* NCrunch (commercial runner) proves the viable pattern: it hooks run/debug into feature files using
  its own commands and its own execution path, requiring nothing from Reqnroll. We follow the same
  pattern with open tooling.
* This is a companion to the official Reqnroll Visual Studio extension, not a fork or replacement. It
  must coexist with it. We do not reimplement syntax highlighting, IntelliSense, Go To Definition, etc.

### Non-goals for v1

* No VS Code support in v1 (v2 — see §8; architecture must allow it).
* No native Test Explorer window integration (`Microsoft.VisualStudio.TestWindow.Extensibility`) in
  v1 — investigate for v1.1, do not block on it.
* No per-example-row execution for Scenario Outlines in v1 (run the whole outline).
* No test discovery tree UI. The feature file IS the UI.
* No SpecFlow support (Reqnroll only), no non-.NET Gherkin.
* No AI, no network calls, no telemetry.

## 2. Repository layout

```
ReqnrollRunner/
├── src/
│   ├── ReqnrollRunner.Core/          # netstandard2.0 class library — ALL real logic lives here
│   ├── ReqnrollRunner.Vsix/          # VS 2022 extension (net472 / net48, classic VSSDK, in-proc)
│   └── ReqnrollRunner.Cli/           # thin console wrapper over Core (net8.0)
├── tests/
│   ├── ReqnrollRunner.Core.Tests/    # xUnit; the bulk of automated coverage
│   └── fixtures/                     # sample .feature files + sample TRX files
├── samples/
│   └── SampleCalculator/             # minimal Reqnroll solution (NUnit)
├── docs/
│   └── architecture.md
├── README.md
├── LICENSE
└── SPEC.md                           # this file
```

**Hard rule:** `ReqnrollRunner.Core` must have zero Visual Studio dependencies. Everything
VS-specific (DTE, IVs\* services, menus, output window) lives only in `ReqnrollRunner.Vsix`. Core
targets `netstandard2.0` so both the net472 VSIX and the net8.0 CLI can consume it.

## 3. Core library (`ReqnrollRunner.Core`) — functional spec

### 3.1 Feature parsing

* Use the official `Gherkin` NuGet package (the Cucumber Gherkin parser for .NET) to parse `.feature`
  files.
* Given a file path + cursor line number, resolve the target: the `Scenario` or `Scenario Outline`
  whose span contains the line. If the cursor is on/inside `Feature:` header or `Background:`, the
  target is the whole feature. If inside a `Rule:` but not a scenario, target is all scenarios under
  that rule (v1 may simplify to whole feature — acceptable).
* Must handle: `Scenario`, `Scenario Outline`/`Scenario Template`, `Examples` blocks, `Rule:` blocks,
  tags, `#` comments, docstrings, data tables, and localized Gherkin keywords (the Gherkin package
  handles `# language:` headers — do not hand-roll keyword matching).

### 3.2 Project resolution

* Given the `.feature` file path, find the containing test project: walk up directories to the
  nearest `.csproj`.
* Read the `.csproj` (plus `Directory.Packages.props` for CPM) to detect the test runner via package
  references, checked in this order:
  * `Reqnroll.NUnit` → NUnit
  * `Reqnroll.xUnit` → xUnit
  * `Reqnroll.MsTest` → MSTest
  * If only `Reqnroll` is referenced or nothing matches → runner `Unknown`; fall back to the generic
    filter strategy (§3.3) and surface a warning.
* Also capture target framework(s) — if multi-targeted, default to the first and expose an option later.

### 3.3 Scenario → test filter mapping (the heart of the project)

Produce a `dotnet test` invocation that runs exactly the chosen scenario(s):

```
dotnet test "<csproj path>" --no-build --filter "<expression>" --logger "trx;LogFileName=<temp>.trx"
```

(`--no-build` is a user setting, default ON with a build performed via VS first — see §4.4.)

**Filter strategy per runner.** Reqnroll's generator derives test names from scenario titles; the
mapping must replicate its sanitization (spaces and invalid identifier characters → underscores
etc.). Implement as a small, heavily unit-tested `TestNameSanitizer` and verify against real
generated `.feature.cs` output from the sample project — when in doubt, read the code-behind
`.feature.cs` file next to the feature file at runtime and extract the actual generated method/class
names from it; that file is the ground truth and is present in obj/ (or via FileCodeBehind) after
build. **Reading the generated file is the preferred, most robust strategy**; sanitizer-based
construction is the fallback when the file can't be located.

* **NUnit:** generated test methods carry `[Description("<original scenario title>")]` and the class
  `[TestFixture]` with the feature name. Filter with `FullyQualifiedName~<SanitizedFeatureClass>`
  combined with `Name~<SanitizedScenarioMethod>`.
* **xUnit:** methods are facts/theories named with sanitized titles; same FQN+Name strategy.
* **MSTest:** beware — recent Reqnroll versions emit friendly display names for MsTest; `Name` may
  match the display name rather than the sanitized method. Support both by generating an OR filter:
  `(Name~<sanitized>)|(DisplayName~<original title, escaped>)` where the runner supports `DisplayName`.
* **Whole feature:** filter by the generated feature class only (`FullyQualifiedName~<Namespace>.<FeatureClass>`).
* **Scenario Outline:** filter by the outline's method/base name so all example rows run. Never
  attempt row-level filtering in v1.
* Escape filter special characters (`)`, `(`, `&`, `|`, `=`, `~`, `!`) in titles; where a title cannot
  be safely expressed in a filter, fall back to feature-class scope and warn.

### 3.4 Execution + TRX result parsing

* Run `dotnet test` as a child process, streaming stdout/stderr to a callback (the VSIX pipes this to
  an Output pane).
* Parse the produced TRX: per-test outcome (Passed/Failed/Skipped), duration, error message + stack
  trace. Expose a `TestRunResult` model.
* Exit gracefully and with a clear message when: build errors, zero tests matched the filter (most
  common failure — always echo the filter used), `dotnet` not on PATH.

### 3.5 Debug session support

* To debug: launch the same `dotnet test` invocation with environment variable `VSTEST_HOST_DEBUG=1`.
  The test host then prints its process id and waits for a debugger.
* Core's job: launch, parse stdout for the testhost PID (`Process Id: <pid>`), and surface
  `(pid, processName)` via callback. The attach itself is done by the VSIX (§4.3).
* Timeout (configurable, default 30 s) if no PID line appears; kill the child process on cancel/timeout.

## 4. Visual Studio extension (`ReqnrollRunner.Vsix`) — functional spec

Target Visual Studio 2022 (17.x), amd64 + arm64, classic VSSDK (in-process) — required for editor
context menus and DTE debugger attach. (VS 2026 support is a fast-follow; note in README.)

### 4.1 Commands

Two commands, registered in a `.vsct`:

* Reqnroll: Run Scenario (`ReqnrollRunner.RunScenario`)
* Reqnroll: Debug Scenario (`ReqnrollRunner.DebugScenario`)

Surfaced in:

1. The editor context menu, only when the active document is a `.feature` file (visibility via a UI
   context / file-extension rule — commands must not appear in other file types).
2. The command palette (Ctrl+Q) / Tools menu, so users can bind their own keyboard shortcuts
   (document in README how to bind Ctrl+R,T equivalents; do not override VS defaults).

Command label adapts to target: "Run Scenario", "Run Scenario Outline (all examples)", or "Run
Feature" depending on cursor position (nice-to-have; static labels acceptable for first cut).

### 4.2 Run flow

1. Capture active document path + caret line from the text view.
2. Call Core: resolve target → project → runner → filter.
3. Ensure the project is built: trigger a VS build of the containing project via DTE
   (`SolutionBuild.BuildProject`) and await success, unless user setting "skip build" is on.
4. Execute via Core; stream output to a dedicated "Reqnroll Runner" Output window pane; on completion
   write a one-line summary (`✅ 1 passed in 2.3s` / `❌ Failed: <scenario>` + error excerpt) and pop
   the pane on failure.
5. Status bar text while running; command is cancellable (kill child process).

### 4.3 Debug flow

Same as run, except:

1. Core launches with `VSTEST_HOST_DEBUG=1` and reports the testhost PID.
2. VSIX attaches using DTE: iterate `DTE.Debugger.LocalProcesses`, match PID, call `.Attach()` with
   the managed engine. (DTE attach is old, stable, documented — deliberately chosen over TestWindow
   extensibility for v1.)
3. After attach, the test host continues automatically once a debugger is attached (vstest behavior);
   breakpoints in step definition classes now hit.
4. On session end, clean up temp TRX and child processes.

### 4.4 Settings (Tools → Options → Reqnroll Runner)

* Skip build before run (default: off)
* Extra `dotnet test` arguments (string, appended verbatim)
* Testhost attach timeout seconds (default 30)
* Preferred target framework for multi-targeted projects (default: first)

### 4.5 Error UX (be loud and specific)

Every failure mode gets a human message in the Output pane, never a silent no-op. Minimum set: not a
Reqnroll project ("no Reqnroll.\* package reference found in \<csproj\>"), no scenario at cursor,
filter matched zero tests (echo the exact filter + suggest checking the generated .feature.cs), build
failed, dotnet missing, attach timeout.

## 5. CLI (`ReqnrollRunner.Cli`)

Thin veneer over Core, primarily to make Core testable end-to-end and to serve as the engine for the
future VS Code head:

```
reqnroll-runner run   --file <path.feature> --line <n> [--no-build] [--json]
reqnroll-runner debug --file <path.feature> --line <n>   # prints testhost PID, waits
reqnroll-runner map   --file <path.feature> --line <n>   # prints resolved project, runner, filter
```

`--json` emits machine-readable results. Ship as a dotnet global tool later; not required for v1.0
marketplace release but `map` must exist from day 1 (it is the primary manual test harness for §3.3).

## 6. Edge cases that MUST have unit-test fixtures

1. Scenario titles with: apostrophes, parentheses, `&`/`|`/`~`/`=`/`!`, unicode, leading/trailing
   spaces, duplicate titles within one feature (Reqnroll dedupes generated names with suffixes —
   resolve via code-behind reading).
2. Scenario Outline with multiple Examples blocks; Examples with tags.
3. `Rule:` blocks containing scenarios.
4. `# language: de` (or any non-English) feature file.
5. Feature files not part of any project / project without Reqnroll packages.
6. Multi-project solution where two projects contain identically named features (must scope
   `dotnet test` to the resolved csproj, never the solution).
7. MSTest friendly-name display names vs sanitized method names.
8. Background-only cursor position (→ whole feature).
9. TRX with skipped/inconclusive outcomes.
10. Solution using Central Package Management (`Directory.Packages.props`).

## 7. Milestones & acceptance criteria

**M1 — Core mapping** (build first, everything depends on it)
`ReqnrollRunner.Cli map` returns correct project/runner/filter for every fixture in §6, proven by unit
tests. Sanitizer verified against real generated `.feature.cs` from `samples/SampleCalculator`.

**M2 — CLI run**
`reqnroll-runner run` executes a single scenario in SampleCalculator and reports parsed TRX results.
Zero-match case produces the diagnostic message.

**M3 — VSIX run**
Right-click a scenario in VS 2022 → Run Scenario → output pane shows streaming log + summary. Works
for NUnit, xUnit, MSTest sample variants.

**M4 — VSIX debug**
Debug Scenario attaches automatically; a breakpoint inside a step definition hits. Attach timeout path
verified.

**M5 — polish & publish**
Settings page, cancellation, error UX complete; README with GIF; VSIX published to Marketplace as
"Reqnroll Runner (community companion)"; `map`/`run` covered in docs.

**Suggested v1.1 backlog** (do not build now): Test Explorer window integration, per-example-row runs,
CodeLens-style adornments above scenarios, VS 2026 package, TestAtlas integration.

## 8. v2 direction (design constraint only — do not implement)

A VS Code extension (TypeScript) that shells out to `ReqnrollRunner.Cli --json` and projects results
into VS Code's `TestController` API. This is why Core/Cli must stay VS-free and JSON-capable. Nothing
else about v2 constrains v1.

## 9. Engineering conventions

* C# latest LTS language version; nullable enabled; warnings as errors in Core.
* xUnit for tests; no mocking framework needed if Core stays functional (prefer pure functions: parse
  → resolve → map are all deterministic).
* No network access at runtime. No telemetry. Deterministic behavior throughout (same inputs → same
  filter).
* Public README must state: community project, not affiliated with the official Reqnroll extension;
  link to discussion #270 as origin story.
* Conventional commits; CI via GitHub Actions: build + unit tests on push (VSIX packaging can be a
  later CI stage).
