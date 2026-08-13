# Contributing

Thanks for looking. This is a small, single-maintainer community project — issues, bug reports and
pull requests are all welcome.

By taking part you agree to abide by the [Code of Conduct](CODE_OF_CONDUCT.md).

## Before you start

Two constraints shape almost every decision in this repository. Please read them before opening a PR
that touches the architecture:

1. **`ReqnrollRunner.Core` must never gain a Visual Studio dependency.** It targets `netstandard2.0`
   and knows nothing about DTE, `IVs*` services, menus or output windows. That is what makes the
   whole mapping layer testable without an IDE, lets the same assembly serve both the `net472` VSIX
   and the `net8.0` CLI, and keeps the planned VS Code head possible. Anything VS-specific belongs in
   `src/ReqnrollRunner.Vsix`.
2. **The generated code-behind is the source of truth for test names.** Reqnroll emits
   `#line <n>` as the first statement of every generated test method, pointing at the scenario's
   keyword line. Reading that is exact; reconstructing a name from the scenario title is a fallback.
   Please do not invert that order — see [docs/architecture.md](docs/architecture.md) for why.

## Getting set up

```bash
dotnet build ReqnrollRunner.sln
dotnet test  ReqnrollRunner.sln
```

That builds Core, the CLI, the tests, and — via `build/VsixCompileCheck` — the extension's C#. It
works on Windows, macOS and Linux.

The extension itself needs Windows, Visual Studio 2022, and the **Visual Studio extension
development** workload. Open `ReqnrollRunner.VisualStudio.sln` and press F5 to launch an experimental
VS instance with the extension loaded.

## The two solutions

| Solution | Contains | Builds on |
|---|---|---|
| `ReqnrollRunner.sln` | Core, CLI, tests, VSIX compile check | anywhere, including CI |
| `ReqnrollRunner.VisualStudio.sln` | Core + the VSIX | Windows with the VS workload |

This split is deliberate. `src/ReqnrollRunner.Vsix` is a classic (non-SDK) VSSDK project, which only
MSBuild-on-Windows can build, so keeping it out of the main solution is what lets CI run at all.

## Debugging a mapping problem

`map` is a dry run — it prints the resolved target, project, runner and the exact filter without
executing anything. It is the fastest way to see what the extension would have done:

```bash
dotnet run --project src/ReqnrollRunner.Cli -- map --file path/to/My.feature --line 42
```

If you are filing a bug about the wrong test running, please include that output.

## If you change how titles map to identifiers

`tests/fixtures/sanitizer-corpus.tsv` is an **oracle**, not a hand-written expectation: every row was
produced by running a corpus of awkward titles through the real Reqnroll MSBuild generator.
Regenerate it rather than editing it:

```bash
./scripts/capture-sanitizer-corpus.sh
dotnet test ReqnrollRunner.sln
```

The same applies to `tests/fixtures/codebehind/*` and the two captured `.trx` files — they are real
tool output, so refresh them by re-running the tools rather than by hand.

## What a good pull request looks like

- **A test that fails before the change.** Every behaviour change should carry one. If you genuinely
  cannot write one, say so in the PR and explain how you verified it instead.
- **Not vacuous.** Assert the behaviour, not that something rendered. A test that still passes with
  the feature disabled is a defect. The caret sweeps in `CaretSweepTests` were mutation-tested for
  exactly this reason, and the first draft of them missed a real bug class.
- **Green build with no warnings.** Core and the CLI build with warnings as errors.
- **Docs updated in the same commit** when you change documented behaviour —
  [docs/architecture.md](docs/architecture.md) explains *why* things are the way they are, and it
  should never describe a mechanism that no longer exists.

Conventional commit messages, please (`feat:`, `fix:`, `test:`, `docs:`, `chore:`). Explain *why* in
the body; the diff already shows the *what*.

## Scope

v1 is deliberately narrow: run and debug a Scenario, a Scenario Outline (all rows), or a whole
Feature, for Reqnroll projects using NUnit, xUnit or MSTest. Out of scope for now — Test Explorer
integration, per-example-row runs, a discovery tree, SpecFlow, and VS Code (that is v2, and it will
be a thin head over the existing CLI rather than a rewrite).

There is no telemetry and no network access at run time, and there should not be.
