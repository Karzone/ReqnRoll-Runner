# Reqnroll Runner

Run or debug any [Reqnroll](https://reqnroll.net) scenario straight from the `.feature` file. Put the
caret on a scenario, right-click, pick **Run Reqnroll Scenario** or **Debug Reqnroll Scenario** — the
generated test executes, and breakpoints in your step definitions hit.

> **A community project.** Not affiliated with, endorsed by, or part of the official Reqnroll Visual
> Studio extension. It is designed to be installed *alongside* it: this extension adds no editor
> features of its own — no syntax highlighting, no IntelliSense, no Go To Definition.

## Installing

There is no Marketplace release yet. Until there is, every push to `main` builds the extension on
Windows and publishes it as a build artifact:

1. Open the [latest CI run](https://github.com/Karzone/ReqnRoll-Runner/actions?query=branch%3Amain)
2. Scroll to **Artifacts** → download **`ReqnrollRunner-vsix`**
3. Unzip it and double-click the `.vsix`, then restart Visual Studio

Requires Visual Studio 2022 (17.x) or 2026 (18.x). No development environment or clone needed — the artifact is a
complete, installable extension, and CI checks each build actually contains its payload rather than
merely compiling.

Then, in your own solution:

- the test project needs a `Reqnroll.NUnit`, `Reqnroll.xUnit` or `Reqnroll.MsTest` reference — the
  runner says so plainly if it does not;
- **build it once**, so Reqnroll generates the `.feature.cs` the runner reads. After that the
  extension builds for you on each run;
- open a `.feature` file, put the caret on a scenario, right-click.

> **Status:** the mapping engine is thoroughly tested and CI packages the extension on every push,
> but the extension has not yet been exercised in a real Visual Studio session. The manifest
> accepts VS 2026 (18.x); that widening is unverified — nobody has loaded it there yet. See
> [docs/manual-testing.md](docs/manual-testing.md). If something misbehaves, please
> [open an issue](https://github.com/Karzone/ReqnRoll-Runner/issues) — include the output of
> `reqnroll-runner map`, which shows what was resolved without running anything.

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
| `src/ReqnrollRunner.Vsix` | The extension. VS 2022 and 2026, classic VSSDK, in-process, Windows-only build. |
| `src/ReqnrollRunner.Cli` | `reqnroll-runner` — a thin console over Core. |
| `tests/ReqnrollRunner.Core.Tests` | xUnit. 276 tests, the bulk of the coverage. |
| `tests/fixtures` | Feature files, captured TRX files and real generated code-behind. |
| `samples/SampleCalculator` | Working Reqnroll projects — NUnit, xUnit and MSTest variants. |
| `build/VsixCompileCheck` | Compiles the VSIX's C# on any platform so CI catches breakage. |

There are two solutions on purpose:

* **`ReqnrollRunner.sln`** — Core, CLI, tests, VSIX compile check. Builds anywhere, including Linux CI.
* **`ReqnrollRunner.VisualStudio.sln`** — Core + the VSIX. Windows, VS 2022 or later, "Visual Studio
  extension development" workload.

## Using the CLI

The CLI is the engine's test harness and a genuinely useful tool when a filter misbehaves.

```bash
# Dry run: what would be executed, and why?
reqnroll-runner map --file Features/Calculator.feature --line 12

# Run it
reqnroll-runner run --file Features/Calculator.feature --line 12

# Run against a Release build you have already made
reqnroll-runner run --file Features/Calculator.feature --line 12 --no-build --configuration Release

# Start a debuggable run and print the test host pid to attach to
reqnroll-runner debug --file Features/Calculator.feature --line 12

# Check a feature file for authoring mistakes that are legal Gherkin
reqnroll-runner lint --file Features/Calculator.feature
```

> `--configuration` matters whenever you pass `--no-build`: `dotnet test` defaults to Debug, so
> running a Release build without it finds no test assembly. The extension handles this for you by
> passing Visual Studio's active solution configuration.

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

**v1 does:** run and debug a Scenario, a Scenario Outline (all example rows together), a single
`Examples` row, or a whole Feature, for Reqnroll projects using NUnit, xUnit or MSTest. It also
flags authoring mistakes that are legal Gherkin — an `Examples` column no step uses, a `<placeholder>`
with no column behind it, an outline that substitutes nothing.

**Single example rows are MSTest-only**, and that is a limit of the runners rather than a shortcut
here: only MSTest gives a row an identity a VSTest filter can match. On NUnit and xUnit the command
says so and runs the whole outline. See
[docs/architecture.md](docs/architecture.md#why-a-single-example-row-works-on-mstest-and-nowhere-else).

**v1 does not:** integrate with the Test Explorer window, show a discovery tree, support SpecFlow, or
support VS Code. The feature file *is* the UI.

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

**[docs/manual-testing.md](docs/manual-testing.md) is the full procedure** — exact caret positions and
the count each should produce, the debug walkthrough, and what to check first when a command does not
appear. The extension has no automated coverage (CI compiles its C# but cannot run Visual Studio), so
that pass is the only thing standing between a regression and a user finding it.

## Contributing

Issues and pull requests are welcome. See **[CONTRIBUTING.md](CONTRIBUTING.md)** for how to get set
up, what a good PR looks like, and the two constraints that shape the design.

The short version: conventional commits; `ReqnrollRunner.Core` builds with warnings as errors and
must never gain a Visual Studio dependency — that constraint is what makes the logic testable without
an IDE and keeps the VS Code head possible.

If you change how scenario titles map to identifiers, regenerate the oracle:

```bash
./scripts/capture-sanitizer-corpus.sh
```

That rebuilds a corpus of titles through the **real** Reqnroll generator and rewrites
`tests/fixtures/sanitizer-corpus.tsv`, which the sanitizer tests assert against row by row.

Everyone taking part is expected to follow the [Code of Conduct](CODE_OF_CONDUCT.md). Security issues
should be reported privately — see [SECURITY.md](SECURITY.md).

## Licence

MIT. See [LICENSE](LICENSE).
