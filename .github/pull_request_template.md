## What this changes

<!-- The why, not the what — the diff already shows the what. -->

## How it was verified

<!--
Please be specific. "Tests pass" is not verification; which test would have failed before?
If a behaviour change genuinely cannot carry a failing-before test, say so and explain how you
checked it instead.
-->

- [ ] `dotnet build ReqnrollRunner.sln` — green, no warnings
- [ ] `dotnet test ReqnrollRunner.sln` — green
- [ ] A test that fails on `main` and passes here (name it): 
- [ ] Tried by hand against `samples/SampleCalculator` (say which runner and scenario):

## Checklist

- [ ] `ReqnrollRunner.Core` still has no Visual Studio dependency
- [ ] If title-to-identifier mapping changed, `scripts/capture-sanitizer-corpus.sh` was re-run
- [ ] `docs/architecture.md` updated if documented behaviour changed
- [ ] Loaded in Visual Studio and exercised, if the VSIX was touched (CI cannot check this)
