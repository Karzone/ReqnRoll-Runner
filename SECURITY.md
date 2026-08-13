# Security Policy

## Reporting a vulnerability

Please report security issues **privately**, not as a public issue, using GitHub's
[private vulnerability reporting](https://github.com/Karzone/ReqnRoll-Runner/security/advisories/new).
That opens a draft advisory visible only to you and the maintainer.

If that link is not available to you for any reason, open a public issue saying only that you have
found a security problem and would like a private channel — please do not include the details.

This is a single-maintainer hobby project, so please allow a reasonable window for a reply before
disclosing publicly.

## Supported versions

Only the latest release is supported. There are no long-term support branches.

## What this project does and does not do

Worth stating plainly, because it bounds the realistic attack surface:

- **No network access at run time.** The extension and CLI make no HTTP requests of any kind.
- **No telemetry**, no analytics, no crash reporting, nothing phoned home.
- **No credentials** are read, stored or transmitted.

What it *does* do is execute code on your machine. Specifically, it shells out to `dotnet test`
against a project resolved by walking up from the `.feature` file you have open, with a filter
derived from the scenario under your caret. That means:

- **It runs your test project's code**, exactly as running the tests any other way would. Opening a
  feature file from an untrusted repository and clicking Run Scenario carries the same risk as
  opening that repository's solution and running its tests.
- **The "Extra `dotnet test` arguments" setting is passed through verbatim.** It is your own setting,
  but treat it the way you would treat a command line, because that is what it becomes.

If you find a way to make the runner execute something the user did not intend — for example a
crafted feature file, project file or generated code-behind that escapes the argument construction in
`DotnetTestRunner.BuildArguments` — that is a genuine vulnerability and I would like to hear about it.
