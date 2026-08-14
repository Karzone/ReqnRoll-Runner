# Testing the extension in Visual Studio

Everything below `ReqnrollRunner.Core` is covered by automated tests and CI. The extension itself is
not, and cannot be: a classic VSSDK project needs Windows to build, and its behaviour needs a running
Visual Studio to observe. This is the manual pass that closes that gap — it is the acceptance
procedure for **M3 (VSIX run)** and **M4 (VSIX debug)** in [SPEC.md](../SPEC.md).

Expect it to take about fifteen minutes the first time.

## What you need

- **Windows** with **Visual Studio 2022** (17.x). VS 2026 (18.x) is accepted by the manifest but
  unverified — see the VS 2026 section at the end.
- The **Visual Studio extension development** workload — Visual Studio Installer → Modify →
  Workloads. Without it, `ReqnrollRunner.Vsix.csproj` will not load at all.
- The **.NET 8 SDK**, and `dotnet` on `PATH` (`dotnet --version` in a terminal).

The official Reqnroll extension is *not* required. Installing it in the experimental instance is
worth doing once, though — coexisting with it is a design claim, so it deserves checking.

## Launching

```
git clone https://github.com/Karzone/ReqnRoll-Runner
cd ReqnRoll-Runner
```

Open **`ReqnrollRunner.VisualStudio.sln`** — not `ReqnrollRunner.sln`, which deliberately excludes the
VSIX so CI can build it on Linux.

Press <kbd>F5</kbd>. That builds the VSIX, installs it into the **experimental instance** (a separate
VS hive, so your normal setup is untouched) and launches it. The first launch is slow.

In the experimental instance, open
`samples\SampleCalculator\SampleCalculator.NUnit\SampleCalculator.NUnit.csproj` and build it once —
the runner reads the generated `Features\Calculator.feature.cs`, which only exists after a build.

## M3 — Run

Open `Features\Calculator.feature` and work through these. Line numbers are exact.

| Put the caret on | Right-click shows | Expect |
|---|---|---|
| line 10, `Scenario: Add two numbers` | Run Scenario | 1 passed |
| line 34, `Scenario Outline: Add many <a> and <b>` | Run **Scenario Outline (all examples)** | 3 passed |
| line 46, inside the second `Examples:` block | Run Scenario Outline (all examples) | 3 passed |
| line 6, `Background:` | Run **Feature** | 8 passed |
| line 50, `Rule:` | Run Feature | 8 passed |
| line 52, `Scenario: Subtract inside a rule` | Run Scenario | 1 passed |
| line 9, the `@smoke` tag | Run Scenario | 1 passed |
| line 28, `Scenario: Ünïcödé — スカラー` | Run Scenario | 1 passed |

**The menu text adapting is itself a check** — it means the caret was resolved before the menu was
drawn. If it always says "Scenario", the target resolution is not running.

A run writes to the **Reqnroll Runner** pane in the Output window (View → Output → drop-down):

```
── Run — Calculator.feature line 10
Target : Scenario 'Add two numbers'
Project: C:\…\SampleCalculator.NUnit.csproj  [NUnit]
Filter : FullyQualifiedName~SampleCalculator.Features.CalculatorBasicMoreFeature.AddTwoNumbers

Building SampleCalculator.NUnit (Debug)…

> dotnet test "C:\…\SampleCalculator.NUnit.csproj" --no-build --configuration Debug --filter "…"
…
PASSED — 1 passed in 2.4s
```

### Example rows

Row-level filtering only works on MSTest — the other two runners give a row no identity a VSTest
filter can match, so they widen to the whole outline and say so. Both halves need checking, because
the honest widening is the part that is easy to get wrong.

Open `samples\SampleCalculator\SampleCalculator.MsTest` and build it, then in its
`Features\Calculator.feature`:

| Caret on | Menu should read | Expect |
|---|---|---|
| line 42, `| 1 | 2 | 3 |` | Run **Example Row 1** | 1 passed — `Add many <a> and <b>(1,2,3,4)` |
| line 43, `| 4 | 5 | 9 |` | Run Example Row 2 | 1 passed — `(4,5,9,5)` |
| line 48, in the second `Examples:` block | Run Example Row 3 | 1 passed — `(10,20,30,6)` |
| line 41, the header row | Run **Scenario Outline (all examples)** | 3 passed |

Then the same three lines in the **NUnit** sample. The menu must read **"Run Scenario Outline (all
examples — this runner cannot isolate a row)"** and produce 3 passed, with the reason echoed as a
`Warning:` line in the output pane. A menu that still offers "Run Example Row 2" there, or one that
runs 3 tests without saying why, is the bug.

The filter is printed on every run, so it can be checked directly. For an MSTest row it must have
**both** clauses:

```
Filter : (FullyQualifiedName~SampleCalculator.MsTest.Features.CalculatorBasicMoreFeature.AddManyAAndB)&(Name~\(1,2,3,)
```

The `Name~` half alone would match rows of unrelated outlines whose values start the same way.

Also worth checking:

- **The command must not appear in a `.cs` file.** Open `Steps\CalculatorSteps.cs`, right-click —
  neither command should be there.
- **Unsaved edits.** Put the caret on line 10 and press <kbd>Enter</kbd> three times *above* it
  without saving, so the scenario is now on line 13 in the buffer and line 10 on disk. Run it. The
  pane should say `Saved Calculator.feature first.` and run **Add two numbers** — not whatever used
  to live at line 13. This is the one failure mode that produces a green run of the wrong test, so
  it is worth doing deliberately.
- **The Run | Debug links above each scenario.** They should appear on every scenario keyword line,
  including the one inside the `Rule:` block, and clicking them should do exactly what the context
  menu does. Watch for them overlapping the line above — they are drawn in that line's space, so a
  scenario immediately preceded by a tag or a step is the case to look at.
- **Release configuration.** Switch the solution to Release, rebuild, run a scenario. The `dotnet
  test` line should say `--configuration Release`. This is the bug CI caught on Linux; it is worth
  confirming the fix works through the IDE path too.
- **A failing test.** Change `Then the result should be 120` to `999` on line 14, rebuild, run it.
  Expect `FAILED — 1 failed, 0 passed` plus the message, and the Output pane should come to the
  front by itself.
- **The German feature.** `Features\Rechner.feature`, line 7 → 1 passed; line 13 → 2 passed.

## M4 — Debug

1. Open `Steps\CalculatorSteps.cs` and put a breakpoint on **line 35**,
   `ThenTheResultShouldBe`.
2. Back in `Calculator.feature`, caret on line 10, right-click → **Debug Scenario**.
3. The Output pane should show the test host being found and attached:

```
> VSTEST_HOST_DEBUG=1 dotnet test "…"
Host debugging is enabled. Please attach debugger to testhost process to continue.
Process Id: 23840, Name: testhost
Attached to process 23840 using the 'Managed (CoreCLR)' engine.

Debugging. Breakpoints in your step definitions will now hit.
```

4. **The breakpoint should hit**, with `expected` inspectable as `120`. Step through, then continue —
   the run finishes and the pane reports `Debug session ended — 1 passed, 0 failed.`

Then the timeout path: set Tools → Options → Reqnroll Runner → **Test host attach timeout** to `1`
second and debug again. Expect a clear message naming the timeout, and **no orphaned `testhost.exe`**
left behind (check Task Manager).

## Settings

Tools → Options → **Reqnroll Runner**:

- **Skip build before run** — turn on, edit the feature, run. It should *not* rebuild, and may then
  report a stale-code-behind warning. That warning is the correct behaviour.
- **Extra `dotnet test` arguments** — set to `--blame`. It should appear at the end of the
  `> dotnet test …` line.
- **Test host attach timeout** — covered above.

## Keyboard shortcuts

Tools → Options → Environment → Keyboard, search `Reqnroll Runner.Run Scenario`. It should be there
and bindable. Bind it, scope it to *Text Editor*, and confirm <kbd>Ctrl</kbd>+<kbd>R</kbd>,<kbd>T</kbd>
still does its normal thing in a `.cs` file.

## Installing a newer build over an older one

**Visual Studio will not replace an extension with a build carrying the same version number.**
`VSIXInstaller` compares the `Identity` version, sees it is not higher, and keeps what is already
installed — with no error. Every build before 2026-08-14 shipped as `1.0.0`, so downloading a fresh
artifact and double-clicking it did nothing at all, and the symptom was simply that new commands,
icons and adornments never appeared.

CI now stamps `1.1.<run number>` into the manifest before packaging, so each artifact is strictly
newer than the last and installs over it normally.

**To confirm which build you are running:** Extensions → Manage Extensions → Installed, find
*Reqnroll Runner*, read the version. `1.0.0` is a pre-2026-08-14 build and does not have example
rows, icons or the editor adornments no matter what the release notes say.

### Uninstall appears to do nothing

It is queued, not immediate. Visual Studio cannot delete files it has loaded, so the uninstall is
performed by a separate process **once every VS window has closed** — including any experimental
instance left running from an F5, and any `devenv.exe` still alive in Task Manager after the windows
have gone.

In order:

1. Extensions → Manage Extensions → Installed → *Reqnroll Runner* → **Uninstall**.
2. Close **every** Visual Studio window. Check Task Manager for a surviving `devenv.exe` and end it.
3. Reopen VS. The extension should be gone. Now install the new `.vsix`.

If it survives that, remove it by hand — the extension is a folder, and deleting it is safe:

```
%LOCALAPPDATA%\Microsoft\VisualStudio\17.0_<hash>\Extensions\
```

With VS closed, find the subfolder containing `ReqnrollRunner.Vsix.dll` and delete it, then start VS
once with `devenv /updateconfiguration` so the command table is rebuilt. A stale menu that still
shows removed commands is the cache, not the extension; deleting the `17.0_<hash>Exp` hive (or
running `/updateconfiguration`) clears it.

## If something is wrong

**The commands do not appear at all.** This is the most likely failure and the only one that fails
*silently* — everything else errors loudly. In order:

1. Is the package loading? Close the experimental instance, run
   `devenv /rootsuffix Exp /log`, reproduce, then read
   `%APPDATA%\Microsoft\VisualStudio\17.0_*Exp\ActivityLog.xml` for load errors.
2. Do the GUIDs agree? `ReqnrollRunnerGuids.cs` and the `<Symbols>` block in
   `ReqnrollRunnerPackage.vsct` hold the same values in two places. A mismatch produces exactly this
   symptom with no build error.
3. Is the command table current? Delete the experimental hive
   (`%LOCALAPPDATA%\Microsoft\VisualStudio\17.0_*Exp`) and re-launch — VS caches menu definitions,
   and a stale cache can hide a fixed `.vsct`.

**The command appears but nothing happens.** Check the Reqnroll Runner output pane. Every failure
path writes a sentence there; a genuinely silent failure is itself a bug worth reporting.

**"No tests matched the filter."** The pane echoes the exact filter used. Compare it against what the
CLI resolves independently:

```
dotnet run --project src\ReqnrollRunner.Cli -- map --file <path>.feature --line <n>
```

If the CLI produces the right filter and the extension does not, the bug is in the VSIX. If both are
wrong, it is in Core — and Core is testable without Visual Studio, which makes it much cheaper to fix.

**The debugger will not attach.** The pane lists every engine that was tried and why each failed.
`DebuggerAttacher` tries `Managed (CoreCLR)`, then the .NET Framework engine, then plain `Attach()`.
A .NET Framework test project failing is the case most likely to need a different engine name.

## Visual Studio 2026 (18.x)

The manifest accepts `[17.0,19.0)`, so the `.vsix` will install into VS 2026 — but installing is not
the same as working. If you have 2026 available, the whole Run and Debug tables above need repeating there, and
these two are the ones to watch:

- **Does the package load at all?** VS 2026 continues to support in-process VSSDK extensions, but
  Microsoft is steering extensions toward out-of-process `VisualStudio.Extensibility`. If the package
  fails to load, `ActivityLog.xml` will say so and that is a real port, not a manifest tweak.
- **Does the debugger attach?** `DebuggerAttacher` asks DTE for `LocalProcesses` and calls `Attach2`
  with a named engine. Engine names are the sort of thing that changes between major versions; the
  output pane lists every engine tried and why each failed, so a failure here should be legible.

If either breaks, say so on #5 rather than widening or narrowing the range again — the manifest is
not where that problem lives.

## Signing off

M3 and M4 are met when every row of the Run table produces its expected count, the breakpoint hits
under Debug, and the attach-timeout path fails cleanly without leaking a process. Note anything
surprising in an issue — the extension has no automated coverage, so this procedure is the only thing
standing between a regression and a user finding it.
