# Architecture

## The one structural rule

`ReqnrollRunner.Core` has **zero Visual Studio dependencies** and targets `netstandard2.0`. Every
piece of real logic lives there; `ReqnrollRunner.Vsix` is a shell around it and
`ReqnrollRunner.Cli` is a veneer over it.

That is not tidiness for its own sake. It buys three things:

* the whole mapping layer is unit-testable without an IDE, on any platform;
* `netstandard2.0` is consumable by both the `net472` in-process VSIX and the `net8.0` CLI;
* the planned VS Code head (v2) needs no rewrite — it shells out to `reqnroll-runner --json`.

## The pipeline

```
                    caret: (file path, 1-based line)
                                  │
   ┌──────────────────────────────▼───────────────────────────────┐
   │ FeatureFileParser        Gherkin (official Cucumber package) │
   │   → ScenarioTarget { Kind, Name, Line, Tags }                │
   └──────────────────────────────┬───────────────────────────────┘
                                  │
   ┌──────────────────────────────▼───────────────────────────────┐
   │ ProjectResolver          walk up to nearest .csproj          │
   │   → TestProjectInfo { Runner, TFMs, RootNamespace }          │
   └──────────────────────────────┬───────────────────────────────┘
                                  │
   ┌──────────────────────────────▼───────────────────────────────┐
   │ CodeBehindReader         <feature>.feature.cs, else obj/     │
   │   → (namespace, class, [(method, #line)])                    │
   │ TestNameSanitizer        fallback when there is no build     │
   └──────────────────────────────┬───────────────────────────────┘
                                  │
   ┌──────────────────────────────▼───────────────────────────────┐
   │ TestFilterBuilder        → MappingResult { Filter, … }       │
   └──────────────────────────────┬───────────────────────────────┘
                                  │
              ┌───────────────────┴───────────────────┐
              ▼                                       ▼
      DotnetTestRunner                        DebugSessionLauncher
      → TRX → TestRunResult                   → (pid, name) → host attaches
```

Everything down to `MappingResult` is a pure function of files on disk: same inputs, same filter.
That is what makes `reqnroll-runner map` a usable dry-run harness.

## Why the code-behind is read instead of the title being sanitized

Reqnroll's generator turns a scenario title into a C# identifier. Reconstructing that transformation
is possible but fragile, and it is not needed: the generated file is sitting right there, and it is
what actually compiled into the test assembly.

The join is the `#line` directive. Reqnroll emits it as the first statement of every generated test
method, pointing at the scenario's keyword line in the `.feature` file:

```csharp
[global::NUnit.Framework.TestAttribute()]
[global::NUnit.Framework.DescriptionAttribute("Add two numbers")]
public async global::System.Threading.Tasks.Task AddTwoNumbers()
{
    …
#line 10                     // ← Calculator.feature line 10 is `Scenario: Add two numbers`
```

So the mapping is `scenario keyword line → generated method`, with the title never involved. This is
independent of localized keywords, and it survives titles whose identifier is genuinely unguessable:

| Scenario title | Generated method |
|---|---|
| `Ünïcödé — スカラー` | `Unicodeスカラー` |
| `Ivan's "quoted" (tricky) & odd \| title ~ = !` | `IvansQuotedTrickyOddTitle` |
| `Straße größer` | `StraBeGroBer` |
| `русский текст` | `РусскийТекст` |

`TestNameSanitizer` still exists, for the case where the project has never been built and there is no
code-behind at all. Its rules were derived empirically (see below) and it reports itself as a
`Sanitized` strategy so the UI can say the match is best-effort.

## Why one filter strategy covers all three runners

The spec sketched per-runner filter strategies, with a specific warning about MSTest's friendly
display names. Measurement made that unnecessary.

The same feature file was built under `Reqnroll.NUnit`, `Reqnroll.xUnit` and `Reqnroll.MsTest`. All
three generate the **same class name and the same method names**, differing only in attributes:

| Runner | Test attribute | Outline attribute |
|---|---|---|
| NUnit | `[Test]` + `[Description("…")]` | `[TestCase(...)]` per row |
| xUnit | `[SkippableFact(DisplayName="…")]` | `[SkippableTheory]` + `[InlineData]` |
| MSTest | `[TestMethod("…")]` + `[Description("…")]` | `[DataRow]` |

`FullyQualifiedName` is `Namespace.FeatureClass.Method` in every case. Running
`dotnet test --filter "FullyQualifiedName~<class>.<method>"` selected exactly the intended tests in
all three (1 for a scenario, 3 for the outline, 8 for the whole feature).

MSTest's `[TestMethod("Add two numbers")]` changes what `Name` and the console output show — it does
not change the fully-qualified name. Filtering on FQN means we never have to care.

The `Name~<title>` alternation is therefore used only where the fully-qualified name is not enough:
the sanitized fallback, where the reconstructed method name might be wrong and the raw title is a
useful second chance, and single example rows, below.

## Why a single example row works on MSTest and nowhere else

A whole outline is one generated method, so every row shares one fully-qualified name. Selecting a
single row therefore needs something *other* than the FQN — and only one of the three runners
exposes anything usable. Established by running filters against real builds, not by reading docs:

| Runner | What identifies a row | Can a VSTest filter select it? |
|---|---|---|
| MSTest | `DisplayName="Add many <a> and <b>(1,2,3,4)"` on each `[DataRow]` | **Yes** — `Name~` matches it |
| NUnit | the arguments are in the FQN: `…AddManyAAndB("1","2","3",…)` | No. `~` and `=`, escaped and unescaped, with and without the namespace, all return zero tests — the `(` and `"` defeat the filter parser |
| xUnit | nothing; every row has the same FQN | No. `DisplayName~` returns zero |

So MSTest gets a real row filter and the other two widen to the whole outline, with the reason
surfaced *before* the click — the context-menu item relabels itself to "Run Scenario Outline (all
examples — this runner cannot isolate a row)" rather than letting someone ask for row 2 and then
notice three tests ran.

The MSTest filter matches a **prefix** of the display name, `(1,2,3,` — the values followed by a
comma — because the final component is Reqnroll's pickle index, a counter over the whole feature
that a caller cannot know. Two details matter:

* it is **conjoined with the FQN**: `(FullyQualifiedName~…AddManyAAndB)&(Name~\(1,2,3,)`. `Name~` is
  a substring match over every test in the run, and display names are not unique across outlines —
  a two-column outline with the row `| 1 | 2 |` produces `(1,2,7)`, which the prefix `(1,2,` from an
  entirely different outline matches exactly. Alone, "run this row" would quietly run rows of
  unrelated scenarios;
* a row whose values contain a quote or a line break **widens** instead, rather than emitting a
  filter that cannot survive the argument round trip — the same rule the sanitized fallback applies
  to titles.

Measured against `SampleCalculator.MsTest`: lines 42, 43 and 48 of `Calculator.feature` select
`(1,2,3,4)`, `(4,5,9,5)` and `(10,20,30,6)` respectively — one test each, the right one each time.

## Why TRX is parsed rather than stdout

`dotnet test`'s summary line is not trustworthy for our purposes. A scenario with undefined steps
produces an NUnit *inconclusive* result, and NUnit's summary reports:

```
None     - Failed: 0, Passed: 0, Skipped: 0, Total: 0
```

…even though the test really did run. Trusting that would make "your steps aren't implemented"
indistinguishable from "the filter matched nothing" — and those need very different messages.

So results come from the TRX, and the zero-match case is detected from VSTest's own sentence
(`No test matches the given testcase filter`) rather than inferred from counters.

## Why the debugger attach lives in the VSIX, not Core

Core launches `dotnet test` with `VSTEST_HOST_DEBUG=1`, watches stdout for

```
Process Id: 23840, Name: dotnet
```

and hands back `(pid, name)`. That is all. Performing the attach is the host's job, because attaching
is the one genuinely IDE-specific step — the VSIX uses `DTE.Debugger.LocalProcesses`, and a future VS
Code head would use its own debug adapter. Keeping the split here is what lets Core stay VS-free.

DTE attach was chosen over `Microsoft.VisualStudio.TestWindow.Extensibility` deliberately for v1: it
is old, stable and documented. `DebuggerAttacher` tries `Process2.Attach2` with `Managed (CoreCLR)`,
then the .NET Framework engine, then plain `Attach()`, because the right engine depends on the test
project's target framework.

## Findings that contradict the original spec

Recorded because they were surprises, and because SPEC.md still describes the world as it was assumed
to be.

**Duplicate scenario titles cannot reach us.** SPEC §6 case 1 says "duplicate titles within one
feature (Reqnroll dedupes generated names with suffixes)". Reqnroll 3.3.4 does not dedupe — it
refuses:

```
Calculator.feature(12,1): error : Feature file already contains a scenario with name 'Add two numbers'
```

Sanitization collisions are also a hard error, reported by the C# compiler rather than the generator:
`Add two numbers` and `Add two, numbers` both generate `AddTwoNumbers`, giving `CS0111`. So a
project that *builds* can never present us with ambiguous names, and no disambiguation logic is
needed.

**Generated identifiers are PascalCase, not underscore-separated.** SPEC §3.3 anticipated "spaces and
invalid identifier characters → underscores etc." — that is SpecFlow's older style. Reqnroll
concatenates PascalCased words: `Add two numbers` → `AddTwoNumbers`.

**The generated file is a sibling, not an `obj/` artefact.** Reqnroll writes
`Features/Calculator.feature.cs` next to the feature file by default. `CodeBehindReader` checks the
sibling first and falls back to searching `obj/`.

## The sanitizer rules, and how they were established

`TestNameSanitizer` is only the fallback path, but its rules are still evidence-based rather than
guessed. A 32-title corpus was run through the real Reqnroll 3.3.4 MSBuild generator, and the rules
were derived from the output:

1. Apostrophes are deleted outright, joining the word (`Ivan's` → `Ivans`) — every *other*
   punctuation character is a separator that capitalises what follows (`[brackets]` → `Brackets`).
2. Accented **Latin** letters fold to their base form (`café` → `Cafe`). This is applied per
   character within the Latin blocks only: a blanket Unicode decomposition would also decompose
   Cyrillic `й`, turning `русский` into `русскии`, which the real generator does not do.
3. A few non-decomposable Latin letters fold through a lookup table: `Æ`→`AE`, `Ø`→`O`, `Đ`→`D`,
   `Ł`→`L`, `ß`→`B`. Letters outside it — `Œ`, `Þ`, Cyrillic, CJK — are kept verbatim. The table is
   modelled on observed behaviour, not on correct transliteration: `ß` really does become `B`.
4. `.`, `-` and `_` are separators that also **emit** an underscore: `kebab-case` → `Kebab_Case`.
5. A leading digit gets an underscore prefix: `99 bottles` → `_99Bottles`.

That corpus is checked in as `tests/fixtures/sanitizer-corpus.tsv` and asserted row by row, so a
future Reqnroll naming change surfaces as a test failure rather than as a silently wrong fallback.
Regenerate it with `scripts/capture-sanitizer-corpus.sh`.

## Caret resolution

The document is flattened into an ordered list of *anchors* — the Feature, each Background, each Rule
header, each Scenario. A caret belongs to the last anchor starting at or before it. Blank lines,
`Examples` blocks and step bodies then get the intuitive owner for free.

Two adjustments on top:

* an anchor's start is pulled up to its **first tag line**, so a caret on `@smoke` runs the scenario
  below it;
* and then further up over any **contiguous comment lines**, so a caret on `# explain this scenario`
  runs the scenario it introduces rather than the previous one.

A caret on a `Rule:` header resolves to `TargetKind.Rule`, which v1 executes as the whole feature —
reported honestly in the UI rather than silently widened.

## Testing strategy

* **Unit tests are hermetic.** Fixtures live in `tests/fixtures` and are read from the repository, not
  copied to the output directory, because several are whole project trees whose *directory layout* is
  what is under test (`ProjectResolver` walks up from a feature file looking for a `.csproj`).
* **Fixtures are captured, not invented.** The code-behind files and two of the three TRX files are
  real generator and runner output. The third TRX is hand-written to cover outcome strings a real
  Reqnroll run does not produce.
* **Property sweeps over every line.** `CaretSweepTests` does not spot-check positions — it walks
  every line of every fixture feature and asserts four properties: a line inside a scenario resolves
  to that scenario (with the span derived from the *Gherkin AST's own node locations*, a different
  computation from the parser's anchor list, making it a differential test); the target never moves
  backwards as the caret moves down; a caret only ever reaches *forward* across tags and comments,
  never across content; and the tags and comments above a scenario do belong to it.
* **Vacuity guards.** `TestNameSanitizerTests` asserts the corpus has at least 25 rows;
  `ProjectResolverTests` asserts every fixture project still exists; the caret sweeps assert they
  swept at least two lines per scenario. All exist because a moved fixture would otherwise turn a
  theory into a silently-passing no-op.
* **The sweeps were mutation-tested.** Three deliberate bugs were introduced into
  `FeatureFileParser` to check the sweeps actually bite: an off-by-one in anchor selection (caught,
  4 failures), removing the leading-comment extension (caught), and ignoring tag lines when
  anchoring (**not** caught at first — which is why
  `The_tags_and_comments_above_a_scenario_belong_to_it` exists; it now fails with a precise message).
* **The VSIX is compiled by CI.** `build/VsixCompileCheck` links the same `.cs` files and builds them
  against VSSDK reference assemblies from NuGet, on Linux. It does not produce a VSIX and cannot
  verify the `.vsct`, packaging, or run-time behaviour — but it does stop the extension silently
  rotting when Core changes. The vs-threading analyzers run as part of it.
* **CI proves execution, not just mapping.** Unit tests can only show the filter is *well-formed*; a
  filter that matches nothing passes every one of them. So per runner, CI builds a real Reqnroll
  project and asserts the exact number of tests that ran — 1 for a scenario, 3 for the outline, 8 for
  the feature — then sweeps every scenario individually, checking each maps to a **distinct**
  generated method and that the per-scenario counts sum back to the same 8.
* **A Reqnroll version matrix guards the one assumption everything rests on.** `#line` being emitted
  first in each generated method is observed behaviour, not a documented contract. CI therefore
  builds the sample against 3.1.2 and 3.3.4 (both required, both verified working) and against a
  floating `*` as a non-blocking canary, asserting each still resolves via `CodeBehind` rather than
  falling back to the sanitizer — which is precisely how a generator change would first show up.
