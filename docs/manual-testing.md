# Testing the extension in Visual Studio

Everything below `ReqnrollRunner.Core` is covered by automated tests and CI. The extension itself is
not, and cannot be: a classic VSSDK project needs Windows to build, and its behaviour needs a running
Visual Studio to observe. This is the manual pass that closes that gap — it is the acceptance
procedure for **M3 (VSIX run)** and **M4 (VSIX debug)** in [SPEC.md](../SPEC.md).

Expect it to take about fifteen minutes the first time.

## What you need

- **Windows** with **Visual Studio 2022** (17.x)
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

Also worth checking:

- **The command must not appear in a `.cs` file.** Open `Steps\CalculatorSteps.cs`, right-click —
  neither command should be there.
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

## Signing off

M3 and M4 are met when every row of the Run table produces its expected count, the breakpoint hits
under Debug, and the attach-timeout path fails cleanly without leaking a process. Note anything
surprising in an issue — the extension has no automated coverage, so this procedure is the only thing
standing between a regression and a user finding it.
