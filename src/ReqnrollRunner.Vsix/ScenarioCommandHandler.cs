using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE80;
using Microsoft;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using ReqnrollRunner.Core.Execution;
using ReqnrollRunner.Core.Mapping;
using ReqnrollRunner.Core.Model;
using Task = System.Threading.Tasks.Task;

namespace ReqnrollRunner.Vsix
{
    /// <summary>
    /// Implements both commands. Everything interesting happens in
    /// <c>ReqnrollRunner.Core</c>; this class is the Visual Studio shell around it — read the caret,
    /// build, stream output, attach.
    /// </summary>
    internal sealed class ScenarioCommandHandler
    {
        private readonly ReqnrollRunnerPackage _package;
        private readonly DTE2 _dte;
        private readonly ScenarioMapper _mapper = new ScenarioMapper();

        private CancellationTokenSource? _currentRun;

        private ScenarioCommandHandler(ReqnrollRunnerPackage package, DTE2 dte)
        {
            _package = package;
            _dte = dte;
        }

        public static async Task InitializeAsync(ReqnrollRunnerPackage package, CancellationToken cancellationToken)
        {
            await package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            var dte = (DTE2)await package.GetServiceAsync(typeof(EnvDTE.DTE));
            Assumes.Present(dte);

            var commandService = (OleMenuCommandService)await package.GetServiceAsync(typeof(IMenuCommandService));
            Assumes.Present(commandService);

            var handler = new ScenarioCommandHandler(package, dte);

            commandService.AddCommand(handler.CreateCommand(
                ReqnrollRunnerGuids.RunScenarioCommandId, debug: false));
            commandService.AddCommand(handler.CreateCommand(
                ReqnrollRunnerGuids.DebugScenarioCommandId, debug: true));
        }

        private OleMenuCommand CreateCommand(int commandId, bool debug)
        {
            // Visual Studio raises both of these on the UI thread; asserting it here is what lets the
            // handlers below touch DTE directly, and satisfies the vs-threading analyzers.
            var command = new OleMenuCommand(
                (sender, _) =>
                {
                    ThreadHelper.ThrowIfNotOnUIThread();
                    Execute(debug);
                },
                new CommandID(ReqnrollRunnerGuids.CommandSet, commandId));

            command.BeforeQueryStatus += (sender, _) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                UpdateStatus((OleMenuCommand)sender, debug);
            };

            return command;
        }

        /// <summary>
        /// Shows the command only for <c>.feature</c> files, and labels it for what the caret is
        /// actually on — "Run Scenario" vs "Run Scenario Outline (all examples)" vs "Run Feature".
        /// </summary>
        private void UpdateStatus(OleMenuCommand command, bool debug)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            command.Visible = false;
            command.Enabled = false;

            if (!EditorContext.IsFeatureFileActive(_dte))
            {
                return;
            }

            command.Visible = true;
            command.Enabled = true;

            string verb = debug ? "Debug" : "Run";
            command.Text = verb + " " + DescribeTargetForMenu() + "  (Reqnroll)";
        }

        /// <summary>
        /// A cheap parse of just the caret's target so the menu text can be specific. Deliberately
        /// tolerant: menu text is not worth failing a right-click over.
        /// </summary>
        private string DescribeTargetForMenu()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                CaretPosition? caret = EditorContext.GetCaretPosition(_dte);
                if (caret == null)
                {
                    return "Scenario";
                }

                var parser = new Core.Parsing.FeatureFileParser();
                Core.Parsing.FeatureParseResult parsed = parser.Resolve(caret.FilePath, caret.Line);
                if (!parsed.Success || parsed.Target == null)
                {
                    return "Scenario";
                }

                switch (parsed.Target.Kind)
                {
                    case TargetKind.ScenarioOutline:
                        return "Scenario Outline (all examples)";
                    case TargetKind.Feature:
                    case TargetKind.Rule:
                        return "Feature";
                    default:
                        return "Scenario";
                }
            }
            catch (Exception)
            {
                return "Scenario";
            }
        }

        private void Execute(bool debug)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // A second invocation cancels the run in flight rather than starting a competing one.
            if (_currentRun != null)
            {
                _package.OutputPane.WriteLine("Cancelling the run already in progress…");
                _currentRun.Cancel();
                return;
            }

            CaretPosition? caret = EditorContext.GetCaretPosition(_dte);
            if (caret == null)
            {
                _package.OutputPane.StartRun("Reqnroll Runner");
                _package.OutputPane.WriteLine("There is no active document, so there is no scenario to run.");
                _package.OutputPane.Activate();
                return;
            }

            var cancellation = new CancellationTokenSource();
            _currentRun = cancellation;

            _package.JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    await ExecuteAsync(caret, debug, cancellation.Token);
                }
                finally
                {
                    cancellation.Dispose();
                    _currentRun = null;
                    await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
                    _dte.StatusBar.Text = string.Empty;
                }
            }).FileAndForget("reqnrollrunner/command/execute");
        }

        private async Task ExecuteAsync(CaretPosition caret, bool debug, CancellationToken cancellationToken)
        {
            RunnerOutputPane output = _package.OutputPane;
            ReqnrollRunnerOptions options = _package.Options;

            output.StartRun((debug ? "Debug" : "Run") + " — " + System.IO.Path.GetFileName(caret.FilePath) +
                           " line " + caret.Line);

            MappingResult mapping = _mapper.Map(caret.FilePath, caret.Line);

            if (!mapping.Success)
            {
                output.WriteLine(mapping.Error!);
                output.Activate();
                return;
            }

            output.WriteLine("Target : " + mapping.Target!.Describe());
            output.WriteLine("Project: " + mapping.Project!.ProjectPath + "  [" + mapping.Project.Runner + "]");
            output.WriteLine("Filter : " + mapping.Filter!.Expression);

            foreach (string warning in mapping.Warnings)
            {
                output.WriteLine("Warning: " + warning);
            }

            output.WriteBlankLine();

            await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            // Read this before building: whatever Visual Studio builds is what dotnet test must run,
            // and dotnet test defaults to Debug regardless of the IDE's selection.
            string? configuration = SolutionBuilder.GetActiveConfiguration(_dte);

            if (!options.SkipBuild)
            {
                _dte.StatusBar.Text = "Reqnroll Runner: building…";

                if (!SolutionBuilder.BuildProject(_dte, mapping.Project.ProjectPath, output.WriteLine))
                {
                    output.Activate();
                    return;
                }

                output.WriteBlankLine();
            }

            var runOptions = new RunOptions
            {
                // The IDE has just built (or the user opted out), so never build again inside dotnet test.
                NoBuild = true,
                Configuration = configuration,
                ExtraArguments = string.IsNullOrWhiteSpace(options.ExtraArguments) ? null : options.ExtraArguments,
                Framework = string.IsNullOrWhiteSpace(options.PreferredTargetFramework)
                    ? null
                    : options.PreferredTargetFramework,
                AttachTimeoutSeconds = options.AttachTimeoutSeconds,
            };

            await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            _dte.StatusBar.Text = "Reqnroll Runner: " + (debug ? "starting debug session…" : "running tests…");
            await TaskScheduler.Default;

            if (debug)
            {
                await DebugAsync(mapping, runOptions, output, cancellationToken);
                return;
            }

            TestRunResult result = await new DotnetTestRunner()
                .RunAsync(mapping, runOptions, output.WriteLine, cancellationToken)
                .ConfigureAwait(false);

            output.WriteBlankLine();
            output.WriteLine(Summarise(mapping, result));

            if (!result.IsSuccess)
            {
                output.Activate();
            }
        }

        private async Task DebugAsync(
            MappingResult mapping,
            RunOptions runOptions,
            RunnerOutputPane output,
            CancellationToken cancellationToken)
        {
            DebugLaunchResult launch = await new DebugSessionLauncher()
                .LaunchAsync(mapping, runOptions, output.WriteLine, cancellationToken)
                .ConfigureAwait(false);

            if (!launch.Success)
            {
                output.WriteBlankLine();
                output.WriteLine(launch.Error!);
                output.Activate();
                return;
            }

            DebugTarget target = launch.Target!;

            await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            if (!DebuggerAttacher.TryAttach(_dte, target.ProcessId, output.WriteLine, out string? attachError))
            {
                output.WriteLine(attachError!);
                output.Activate();

                // Leaving a test host parked forever waiting for a debugger nobody is going to attach
                // is worse than losing the run.
                try
                {
                    if (!target.Process.HasExited)
                    {
                        target.Process.Kill();
                    }
                }
                catch (Exception)
                {
                    // Nothing useful to do if it is already gone.
                }

                target.Process.Dispose();
                return;
            }

            output.WriteBlankLine();
            output.WriteLine("Debugging. Breakpoints in your step definitions will now hit.");

            await TaskScheduler.Default;

            try
            {
                while (!target.Process.HasExited && !cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(250, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Falls through to cleanup below.
            }

            try
            {
                if (!target.Process.HasExited)
                {
                    target.Process.Kill();
                }
            }
            catch (Exception)
            {
                // Already gone.
            }

            target.Process.Dispose();

            IReadOnlyList<TestCaseResult> results = TrxParser.ParseFile(target.TrxPath);
            output.WriteBlankLine();
            output.WriteLine(results.Count == 0
                ? "Debug session ended."
                : "Debug session ended — " + Describe(results) + ".");
        }

        private static string Describe(IReadOnlyList<TestCaseResult> results)
        {
            int passed = 0;
            int failed = 0;

            foreach (TestCaseResult result in results)
            {
                if (result.Outcome == TestOutcome.Passed)
                {
                    passed++;
                }
                else if (result.Outcome == TestOutcome.Failed)
                {
                    failed++;
                }
            }

            return passed + " passed, " + failed + " failed";
        }

        /// <summary>The one-line verdict written at the end of every run (SPEC §4.2 step 4).</summary>
        private static string Summarise(MappingResult mapping, TestRunResult result)
        {
            if (result.FailureReason != null)
            {
                return "FAILED — " + result.FailureReason;
            }

            if (result.ZeroTestsMatched)
            {
                return "FAILED — no tests matched the filter." + Environment.NewLine +
                       "    Filter used: " + result.FilterUsed + Environment.NewLine +
                       "    Check the generated code-behind next to " +
                       System.IO.Path.GetFileName(mapping.FeaturePath) +
                       " (" + System.IO.Path.GetFileName(mapping.FeaturePath) + ".cs). If it is " +
                       "missing or out of date, rebuild the project and try again.";
            }

            string seconds = result.Duration.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture);

            if (result.Failed > 0)
            {
                var lines = new System.Text.StringBuilder();
                lines.Append("FAILED — ").Append(result.Failed).Append(" failed, ")
                     .Append(result.Passed).Append(" passed in ").Append(seconds).Append('s');

                foreach (TestCaseResult test in result.Results)
                {
                    if (test.Outcome != TestOutcome.Failed)
                    {
                        continue;
                    }

                    lines.AppendLine().Append("    ").Append(test.DisplayName).Append(": ")
                         .Append(FirstLine(test.ErrorMessage));
                }

                return lines.ToString();
            }

            if (result.Results.Count == 0)
            {
                return "FAILED — the run produced no test results at all. See the output above.";
            }

            string summary = "PASSED — " + result.Passed + " passed in " + seconds + "s";
            if (result.Skipped > 0)
            {
                summary += " (" + result.Skipped + " skipped or inconclusive)";
            }

            return summary;
        }

        private static string FirstLine(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "(no message)";
            }

            int newline = text!.IndexOfAny(new[] { '\r', '\n' });
            return newline < 0 ? text.Trim() : text.Substring(0, newline).Trim();
        }
    }
}
