# Manual test script

Test cases for the Reqnroll Runner Visual Studio extension. Work through them in order and note the
result of each. Nothing here needs a developer background — if a step does not do what "Expected"
says, that is a bug worth reporting, whatever the reason turns out to be.

There are 24 test cases. Allow about 45 minutes.

Anything that fails: open an issue at
https://github.com/Karzone/ReqnRoll-Runner/issues with the test case number, what you saw, and a copy
of the **Reqnroll Runner** output pane.

---

## Before you start

**You need**

- Windows, with Visual Studio 2022 (version 17.x). Visual Studio 2026 is covered separately in TC-24.
- The .NET 8 SDK. To check: open a terminal and run `dotnet --version`. If that fails, install it
  from https://dotnet.microsoft.com/download before going further.
- The `.vsix` file — download it from the **Artifacts** section of the latest build at
  https://github.com/Karzone/ReqnRoll-Runner/actions?query=branch%3Amain, then unzip it.

You do **not** need to clone the repository or install any developer tooling.

**Install it**

1. Close every Visual Studio window.
2. Double-click the `.vsix` file and follow the installer.
3. Open Visual Studio.
4. Go to **Extensions → Manage Extensions → Installed** and find **Reqnroll Runner**.
5. Write down the version number shown. You will need it if anything looks out of date.

> **If you have installed this extension before**, read
> [Reinstalling and uninstalling](#reinstalling-and-uninstalling) at the bottom first. Visual Studio
> will not replace an extension with a build that has the same version number, and it does not tell
> you when it declines to.

**Open the test project**

1. Download the repository as a ZIP from
   https://github.com/Karzone/ReqnRoll-Runner (green **Code** button → Download ZIP) and unzip it.
2. In Visual Studio, **File → Open → Project/Solution**, and open
   `samples\SampleCalculator\SampleCalculator.NUnit\SampleCalculator.NUnit.csproj`.
3. **Build → Build Solution.** Wait for it to finish and confirm it succeeded.

   This build is required, not optional. Reqnroll generates the test code from the feature file
   during a build, and the extension reads what it generates. TC-22 covers what happens if you skip
   it.

4. Open `Features\Calculator.feature` from Solution Explorer. Leave it open — most test cases use it.

**How to read the test cases**

"Put the caret on line N" means click anywhere on that line so the text cursor is sitting in it. You
do not need to select anything. Line numbers are exact; turn line numbers on with
**Tools → Options → Text Editor → All Languages → Line numbers** if they are not showing.

"The output pane" means the **Reqnroll Runner** pane of the Output window. From TC-02 onwards it
should open by itself.

---

## Part 1 — Running scenarios

### TC-01 — The commands appear

**Steps**

1. Put the caret on line 10, `Scenario: Add two numbers`.
2. Right-click.

**Expected**

- Two new entries in the menu: **Run Scenario** and **Debug Scenario**.
- Each has a small icon to its left — a green play symbol for Run, a debug symbol for Debug.

If they are missing entirely, stop here and go to
[When the commands do not appear](#when-the-commands-do-not-appear).

### TC-02 — Run a single scenario

**Steps**

1. Caret on line 10.
2. Right-click → **Run Scenario**.

**Expected**

- The Output window opens by itself, showing the **Reqnroll Runner** pane.
- It reports the scenario name, the project, and the filter it is using.
- The project builds, then the test runs.
- It finishes with **1 passed**.

### TC-03 — Run a scenario outline

**Steps**

1. Caret on line 34, `Scenario Outline: Add many <a> and <b>`.
2. Right-click.

**Expected**

- The menu now reads **Run Scenario Outline**, not "Run Scenario".
- Running it gives **3 passed** — one per example row.

The menu text changing is itself part of the test. It means the extension worked out what your caret
is on before drawing the menu.

### TC-04 — Run from inside an Examples block

**Steps**

1. Caret on line 47, the `| a | b | result |` header row of the second Examples block.
2. Right-click → run it.

**Expected**

- Menu reads **Run Scenario Outline**.
- **3 passed** — a header row describes every row, so it runs the whole outline.

### TC-05 — Run the whole feature from the Background

**Steps**

1. Caret on line 6, `Background:`.
2. Right-click → run it.

**Expected**

- Menu reads **Run Feature**.
- **8 passed** — every test in the file.

### TC-06 — Run from a Rule

**Steps**

1. Caret on line 50, `Rule: Subtraction has its own rule block`.
2. Right-click → run it.

**Expected**

- Menu reads **Run Feature**.
- **8 passed**.

### TC-07 — Run a scenario inside a Rule

**Steps**

1. Caret on line 52, `Scenario: Subtract inside a rule`.
2. Right-click → run it.

**Expected**

- Menu reads **Run Scenario**.
- **1 passed**.

### TC-08 — Run from a tag line

**Steps**

1. Caret on line 9, the `@smoke` tag directly above the scenario.
2. Right-click → run it.

**Expected**

- Menu reads **Run Scenario**.
- **1 passed** — the tag belongs to the scenario underneath it.

### TC-09 — A scenario with accented and Japanese characters

**Steps**

1. Caret on line 28, `Scenario: Ünïcödé — スカラー`.
2. Right-click → run it.

**Expected**

- **1 passed**.

### TC-10 — A German feature file

**Steps**

1. Open `Features\Rechner.feature`.
2. Caret on line 7, run it. Then caret on line 13, run it.

**Expected**

- Line 7 → **1 passed**.
- Line 13 → **2 passed**.

### TC-11 — The commands stay out of other file types

**Steps**

1. Open `Steps\CalculatorSteps.cs`.
2. Right-click anywhere in it.

**Expected**

- **Neither** Run Scenario nor Debug Scenario appears. They belong to feature files only.

### TC-12 — A failing test reports clearly

**Steps**

1. In `Calculator.feature`, change line 14 from `Then the result should be 120` to
   `Then the result should be 999`.
2. Save, rebuild, and run the scenario on line 10.

**Expected**

- **FAILED — 1 failed, 0 passed**, with the failure message.
- The output pane comes to the front.

**Afterwards:** change 999 back to 120 and rebuild. Later test cases assume the file is correct.

---

## Part 2 — Running one example row

Only some test frameworks can run a single row of an Examples table. MSTest can; NUnit and xUnit
cannot, and the extension is supposed to say so rather than pretend. Both halves are tested here.

### TC-13 — MSTest runs exactly the row you picked

**Steps**

1. **File → Open → Project/Solution** and open
   `samples\SampleCalculator\SampleCalculator.MsTest\SampleCalculator.MsTest.csproj`.
2. **Build → Build Solution.**
3. Open its `Features\Calculator.feature`.
4. Caret on line 42, the row `| 1 | 2 | 3 |`. Right-click.

**Expected**

- Menu reads **Run Example Row 1**.
- **1 passed**, and the output names the test `Add many <a> and <b>(1,2,3,4)`.

### TC-14 — Each row picks itself

**Steps**

Repeat TC-13 for these two lines:

- line 43, `| 4 | 5 | 9 |`
- line 48, `| 10 | 20 | 30 |` (in the second Examples block)

**Expected**

- Line 43 → **Run Example Row 2**, 1 passed, test named `…(4,5,9,5)`.
- Line 48 → **Run Example Row 3**, 1 passed, test named `…(10,20,30,6)`.

The row numbers continue across both Examples blocks, so the third row is "Row 3" even though it is
the first row of the second block.

### TC-15 — NUnit says it cannot isolate a row

**Steps**

1. Go back to the **NUnit** project and its `Features\Calculator.feature`.
2. Caret on line 42, the row `| 1 | 2 | 3 |`. Right-click.

**Expected**

- Menu reads **Run Scenario Outline** — *not* "Run Example Row 1".
- Running it gives **3 passed**.
- The output pane contains a line starting `Warning:` that explains NUnit cannot filter to a single
  row.

A menu that offers "Run Example Row 1" here, or one that quietly runs 3 tests with no warning, is a
bug.

---

## Part 3 — Debugging

### TC-16 — A breakpoint in a step definition is hit

**Steps**

1. In the NUnit project, open `Steps\CalculatorSteps.cs`.
2. Click in the left margin of line 35, `ThenTheResultShouldBe`, to set a breakpoint (a red dot).
3. Back in `Calculator.feature`, caret on line 10.
4. Right-click → **Debug Scenario**.

**Expected**

- The output pane shows it finding and attaching to the test host.
- **Execution stops on your breakpoint.**
- Hovering over `expected` shows `120`.
- Press **F5** to continue. The run finishes and reports 1 passed.

### TC-17 — A debug timeout fails cleanly

**Steps**

1. **Tools → Options → Reqnroll Runner** and set **Test host attach timeout** to `1` second. OK.
2. Debug the scenario on line 10 again.
3. Open Task Manager and look at the list of processes.

**Expected**

- A clear message naming the timeout — not a hang and not a silent stop.
- **No `testhost.exe` left running** in Task Manager.

**Afterwards:** set the timeout back to `30`.

---

## Part 4 — Settings and everyday use

### TC-18 — Skip build

**Steps**

1. **Tools → Options → Reqnroll Runner** → turn **Skip build before run** on.
2. Run any scenario.

**Expected**

- No build happens; the run starts immediately.
- If the file has been edited since the last build, a warning says the generated code may be out of
  date. That warning is correct behaviour, not a fault.

**Afterwards:** turn it back off.

### TC-19 — Extra arguments

**Steps**

1. **Tools → Options → Reqnroll Runner** → set **Extra `dotnet test` arguments** to `--blame`.
2. Run any scenario.

**Expected**

- `--blame` appears at the end of the `> dotnet test …` line in the output pane.

**Afterwards:** clear the box.

### TC-20 — Release configuration

**Steps**

1. Change the configuration dropdown in the toolbar from **Debug** to **Release**.
2. Rebuild, then run any scenario.

**Expected**

- The `> dotnet test …` line says `--configuration Release`.
- The test still passes.

**Afterwards:** switch back to Debug.

### TC-21 — Unsaved edits do not run the wrong test

This one matters more than it looks. It is the only fault that produces a **passing run of the wrong
test**, which is worse than an obvious failure.

**Steps**

1. In `Calculator.feature`, put the caret at the very start of line 10 and press **Enter** three
   times, pushing the scenario down to line 13.
2. **Do not save.**
3. Right-click on the `Scenario: Add two numbers` line → **Run Scenario**.

**Expected**

- The output pane says `Saved Calculator.feature first.`
- It runs **Add two numbers** — the scenario you clicked on, not whatever used to be at that line
  number.

**Afterwards:** press Ctrl+Z three times and save.

### TC-22 — A project that has never been built

**Steps**

1. **Build → Clean Solution.**
2. Delete the `obj` folder from the project directory in File Explorer.
3. Run any scenario.

**Expected**

- A warning that no generated code was found and that names are being guessed.
- It still attempts the run rather than refusing.

**Afterwards:** rebuild the solution.

### TC-23 — Keyboard shortcut

**Steps**

1. **Tools → Options → Environment → Keyboard.**
2. In "Show commands containing", type `Reqnroll Runner.Run Scenario`.

**Expected**

- The command is listed and a shortcut can be assigned to it.
- Assign one, set **Use new shortcut in** to *Text Editor*, and confirm it runs the scenario at your
  caret.
- Confirm Visual Studio's own shortcuts still behave normally in a `.cs` file.

### TC-24 — Visual Studio 2026

Only if you have VS 2026 installed. It is accepted by the extension but nobody has confirmed it
works there.

**Steps**

1. Install the `.vsix` into VS 2026 and repeat TC-01, TC-02 and TC-16.

**Expected**

- The same results as on VS 2022.

Report anything different on
[issue #5](https://github.com/Karzone/ReqnRoll-Runner/issues/5), including whether the extension
loaded at all and whether the debugger attached.

---

## Also worth a look

Not numbered test cases, but tell us if any of these are wrong.

**The Run / Debug links above each scenario.** There should be small clickable **Run | Debug** links
above every scenario in the file, doing the same thing as the right-click menu. Two things to check:
do they appear at all, and do they sit on top of the line above them (line 9's `@smoke` tag is the
place to look)? Notes on
[issue #10](https://github.com/Karzone/ReqnRoll-Runner/issues/10).

**Does the right-click menu feel instant?** It does a small amount of work each time it opens. If
there is a perceptible pause before the menu appears, say so on
[issue #11](https://github.com/Karzone/ReqnRoll-Runner/issues/11).

**Alongside the official Reqnroll extension.** If you have it installed, confirm both sets of
commands appear and neither interferes with the other. Syntax highlighting, IntelliSense and Go To
Definition all come from the official extension; this one deliberately adds none of them.

---

## Reinstalling and uninstalling

### A new build seems identical to the old one

Visual Studio **will not replace an extension with a build that has the same version number**, and it
does not warn you — it simply keeps what is already installed. Every build before 14 August 2026 was
numbered `1.0.0`, so installing a newer one over it did nothing at all.

Check **Extensions → Manage Extensions → Installed → Reqnroll Runner** and read the version. Anything
showing `1.0.0` is an old build and will not have example rows, icons or the Run/Debug links, no
matter which file you downloaded. Newer builds are numbered `1.1.x` and install over each other
normally.

### Uninstall does not seem to do anything

It is queued, not immediate. Visual Studio cannot delete files it currently has open, so the removal
happens **after every Visual Studio window has closed**.

1. **Extensions → Manage Extensions → Installed → Reqnroll Runner → Uninstall.**
2. Close **every** Visual Studio window — including any second instance you may have running.
3. Open Task Manager and check no `devenv.exe` is still listed. End it if one is.
4. Reopen Visual Studio and confirm the extension is gone.

If it survives that, remove it by hand. With Visual Studio **closed**, open this folder in File
Explorer:

```
%LOCALAPPDATA%\Microsoft\VisualStudio\17.0_<something>\Extensions\
```

Paste that path into the address bar; `<something>` is a random-looking code and there may be more
than one folder. Inside are subfolders with random names — find the one containing
`ReqnrollRunner.Vsix.dll` and delete that whole subfolder. Then start Visual Studio once and it will
rebuild its menus.

### When the commands do not appear

This is the one failure that is silent, so work through it in order.

1. **Check the version** (above). An old build is by far the most common cause.
2. **Clear Visual Studio's menu cache.** Close VS. Open a terminal and run:

   ```
   devenv /updateconfiguration
   ```

   Then reopen Visual Studio. VS caches menu definitions and a stale cache can hide commands that are
   correctly installed.
3. **Check the file really is a `.feature` file.** The commands are deliberately hidden everywhere
   else (that is TC-11).
4. **Check the extension is enabled** in Extensions → Manage Extensions → Installed.

If none of that helps, open an issue and include the version number from step 1.

### The command appears but nothing happens

Open **View → Output** and choose **Reqnroll Runner** in the dropdown. Every failure writes a
sentence there. If that pane is genuinely empty after clicking Run, that is a bug in itself and worth
reporting.

### The debugger will not attach

The output pane lists every attach method that was tried and why each one failed. Copy that whole
block into an issue — it says exactly where it gave up.
