# Reqnroll Runner

Run or debug any [Reqnroll](https://reqnroll.net) scenario straight from the `.feature` file. Put the
caret on a scenario, right-click, pick **Run Reqnroll Scenario** or **Debug Reqnroll Scenario** — the
generated test executes, and breakpoints in your step definitions hit.

> **A community project.** Not affiliated with, endorsed by, or part of the official Reqnroll Visual
> Studio extension. It is designed to be installed *alongside* it: this extension adds no editor
> features of its own — no syntax highlighting, no IntelliSense, no Go To Definition.

## Why this exists

Requested in [Reqnroll discussion #270](https://github.com/reqnroll/Reqnroll/discussions/270)
("VS Extension: Run/Debug test from feature file", Sep 2024). The Reqnroll maintainer confirmed the
feature was wanted but explained the two dead ends:

1. Visual Studio's own run/debug-from-editor commands (<kbd>Ctrl</kbd>+<kbd>R</kbd>,<kbd>T</kbd>) are
   hard-coded inside VS to work for `.cs` and a handful of other file types. They cannot be extended.
2. A previous attempt to hook the native command, switch to the generated code-behind and replay the
   command there was "very brittle".

NCrunch solves it a third way: its own commands, its own execution path, requiring nothing from
Reqnroll. This project follows the same pattern with open tooling — the commands are ours, and
execution is a plain `dotnet test` invocation with a computed `--filter`.

## How it works

```
caret in Foo.feature, line 42
        ↓  Gherkin parser              → Scenario "Add two numbers", keyword line 40
        ↓  walk up to nearest .csproj  → Tests.csproj, Reqnroll.NUnit → NUnit
        ↓  read Foo.feature.cs         → #line 40 lives in method AddTwoNumbers
        ↓
dotnet test "Tests.csproj" --no-build \
  --filter "FullyQualifiedName~My.Tests.Features.FooFeature.AddTwoNumbers" \
  --logger "trx;LogFileName=…"
```

Two decisions carry most of the weight.

**The generated code-behind is the source of truth for names.** Reqnroll emits `#line <n>` as the
first statement of every generated test method, pointing at the scenario's keyword line in the
`.feature` file. That gives an exact scenario → method mapping that never has to guess how a title
was turned into an identifier — which matters, because some of those names are unguessable:
`Ünïcödé — スカラー` generates `Unicodeスカラー`. When no code-behind exists (the project has never
been built), `TestNameSanitizer` reconstructs the name as a documented best effort, and says so.

**One filter strategy works for all three runners.** NUnit, xUnit and MSTest all generate the same
`Namespace.FeatureClass.Method` fully-qualified name, so `FullyQualifiedName~` selects the right
tests in every case — measured, not assumed. This also sidesteps MSTest's friendly display names:
`[TestMethod("Add two numbers")]` changes `Name`, never `FullyQualifiedName`.

## Repository layout

| Path | What it is |
|---|---|
| `src/ReqnrollRunner.Core` | All the real logic. `netstandard2.0`, **zero Visual Studio dependencies**. |
| `src/ReqnrollRunner.Vsix` | The VS 2022 extension. Classic VSSDK, in-process, Windows-only build. |
| `src/ReqnrollRunner.Cli` | `reqnroll-runner` — a thin console over Core. |
| `tests/ReqnrollRunner.Core.Tests` | xUnit. 202 tests, the bulk of the coverage. |
| `tests/fixtures` | Feature files, captured TRX files and real generated code-behind. |
| `samples/SampleCalculator` | Working Reqnroll projects — NUnit, xUnit and MSTest variants. |
| `build/VsixCompileCheck` | Compiles the VSIX's C# on any platform so CI catches breakage. |

There are two solutions on purpose:

* **`ReqnrollRunner.sln`** — Core, CLI, tests, VSIX compile check. Builds anywhere, including Linux CI.
* **`ReqnrollRunner.VisualStudio.sln`** — Core + the VSIX. Windows, VS 2022, "Visual Studio extension
  development" workload.

## Using the CLI

The CLI is the engine's test harness and a genuinely useful tool when a filter misbehaves.

```bash
# Dry run: what would be executed, and why?
reqnroll-runner map --file Features/Calculator.feature --line 12

# Run it
reqnroll-runner run --file Features/Calculator.feature --line 12

# Start a debuggable run and print the test host pid to attach to
reqnroll-runner debug --file Features/Calculator.feature --line 12
```

`map` output:

```
Target       : Scenario 'Add two numbers'
Feature      : Calculator (basic) & more  (line 10)
Project      : /repo/samples/SampleCalculator/SampleCalculator.NUnit/SampleCalculator.NUnit.csproj
Runner       : NUnit
Frameworks   : net8.0
Test class   : SampleCalculator.Features.CalculatorBasicMoreFeature
Test method  : AddTwoNumbers
Filter       : FullyQualifiedName~SampleCalculator.Features.CalculatorBasicMoreFeature.AddTwoNumbers
Strategy     : CodeBehind — Matched the generated method 'AddTwoNumbers' in the built code-behind.
```

Add `--json` to any command for machine-readable output. Exit codes: `0` success, `1` mapping failed
or a test failed or nothing matched, `2` bad usage.

Run it from source with:

```bash
dotnet run --project src/ReqnrollRunner.Cli -- map --file <path> --line <n>
```

## Keyboard shortcuts

The extension deliberately does **not** override any Visual Studio default. Both commands appear in
the command palette (<kbd>Ctrl</kbd>+<kbd>Q</kbd>) and under **Tools**, so you can bind your own:

1. **Tools → Options → Environment → Keyboard**
2. Search for `Reqnroll Runner.Run Scenario` or `Reqnroll Runner.Debug Scenario`
3. Set **Use new shortcut in** to *Text Editor*, press your keys, and **Assign**

If you want the muscle memory, `Ctrl+R, T` and `Ctrl+R, Ctrl+T` scoped to *Text Editor* work — Visual
Studio's own bindings for those keep working in `.cs` files.

## Settings

**Tools → Options → Reqnroll Runner**

| Setting | Default | What it does |
|---|---|---|
| Skip build before run | off | Skips the Visual Studio build. `dotnet test` always runs with `--no-build`. |
| Extra `dotnet test` arguments | *(empty)* | Appended verbatim, last. |
| Preferred target framework | *(empty)* | Which TFM to run when the project multi-targets. |
| Test host attach timeout (seconds) | 30 | How long to wait for the test host to report its pid. |

## Scope

**v1 does:** run and debug a Scenario, a Scenario Outline (all example rows together), or a whole
Feature, for Reqnroll projects using NUnit, xUnit or MSTest.

**v1 does not:** integrate with the Test Explorer window, run individual Scenario Outline example
rows, show a discovery tree, support SpecFlow, or support VS Code. The feature file *is* the UI.

There is no telemetry and no network access at run time.

### VS Code

Planned as v2: a TypeScript extension shelling out to `reqnroll-runner --json` and projecting results
into VS Code's `TestController` API. Core and the CLI are kept free of Visual Studio types and
JSON-capable specifically so this stays possible.

## Building

```bash
# Everything except the VSIX packaging — works on Windows, macOS and Linux
dotnet build ReqnrollRunner.sln
dotnet test  ReqnrollRunner.sln

# The extension itself — Windows only
# open ReqnrollRunner.VisualStudio.sln in Visual Studio 2022 and press F5
# (launches an experimental VS instance with the extension loaded)
```

To exercise it by hand, open `samples/SampleCalculator/SampleCalculator.NUnit` in the experimental
instance, put the caret in `Features/Calculator.feature` and right-click.

## Contributing

Conventional commits. `ReqnrollRunner.Core` builds with warnings as errors and must never gain a
Visual Studio dependency — that constraint is what makes the logic testable without an IDE and keeps
the VS Code head possible.

If you change how scenario titles map to identifiers, regenerate the oracle:

```bash
./scripts/capture-sanitizer-corpus.sh
```

That rebuilds a corpus of titles through the **real** Reqnroll generator and rewrites
`tests/fixtures/sanitizer-corpus.tsv`, which the sanitizer tests assert against row by row.

## Licence

MIT. See [LICENSE](LICENSE).
