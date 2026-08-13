using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ReqnrollRunner.Core.Model;

namespace ReqnrollRunner.Core.Execution
{
    /// <summary>Runs <c>dotnet test</c> for a mapped target and parses what came back.</summary>
    public sealed class DotnetTestRunner
    {
        /// <summary>VSTest's exact wording when a filter selects nothing — the most common failure mode.</summary>
        internal const string ZeroMatchMarker = "No test matches the given testcase filter";

        /// <summary>
        /// Builds the <c>dotnet test</c> argument string. Separated out and kept deterministic so it
        /// can be asserted in unit tests without launching anything.
        /// </summary>
        public static string BuildArguments(string projectPath, string filterExpression, string trxFileName, RunOptions options)
        {
            if (projectPath == null)
            {
                throw new ArgumentNullException(nameof(projectPath));
            }

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            var arguments = new StringBuilder();
            arguments.Append("test ").Append(Quote(projectPath));

            if (options.NoBuild)
            {
                arguments.Append(" --no-build");
            }

            if (!string.IsNullOrWhiteSpace(options.Framework))
            {
                arguments.Append(" --framework ").Append(Quote(options.Framework!));
            }

            if (!string.IsNullOrWhiteSpace(filterExpression))
            {
                arguments.Append(" --filter ").Append(Quote(filterExpression));
            }

            arguments.Append(" --logger ").Append(Quote("trx;LogFileName=" + trxFileName));

            if (!string.IsNullOrWhiteSpace(options.ResultsDirectory))
            {
                arguments.Append(" --results-directory ").Append(Quote(options.ResultsDirectory!));
            }

            if (!string.IsNullOrWhiteSpace(options.ExtraArguments))
            {
                arguments.Append(' ').Append(options.ExtraArguments!.Trim());
            }

            return arguments.ToString();
        }

        /// <summary>Runs the mapped target to completion.</summary>
        /// <param name="mapping">A successful <see cref="MappingResult"/>.</param>
        /// <param name="options">Execution options.</param>
        /// <param name="onOutput">Called for every stdout/stderr line as it arrives.</param>
        /// <param name="cancellationToken">Cancelling kills the child process.</param>
        public async Task<TestRunResult> RunAsync(
            MappingResult mapping,
            RunOptions options,
            Action<string>? onOutput,
            CancellationToken cancellationToken)
        {
            if (mapping == null)
            {
                throw new ArgumentNullException(nameof(mapping));
            }

            if (!mapping.Success || mapping.Project == null || mapping.Filter == null)
            {
                return Failed(string.Empty, mapping?.Error ?? "Mapping failed, so there is nothing to run.");
            }

            options = options ?? new RunOptions();
            string filter = mapping.Filter.Expression;

            string resultsDirectory = options.ResultsDirectory ?? CreateTempResultsDirectory();
            string trxFileName = "reqnroll-runner-" + Guid.NewGuid().ToString("N") + ".trx";

            var effectiveOptions = new RunOptions
            {
                NoBuild = options.NoBuild,
                Framework = options.Framework ?? mapping.Project.ResolveFramework(options.Framework),
                ExtraArguments = options.ExtraArguments,
                ResultsDirectory = resultsDirectory,
                AttachTimeoutSeconds = options.AttachTimeoutSeconds,
                WorkingDirectory = options.WorkingDirectory,
            };

            string arguments = BuildArguments(mapping.Project.ProjectPath, filter, trxFileName, effectiveOptions);
            string workingDirectory = effectiveOptions.WorkingDirectory
                                      ?? Path.GetDirectoryName(mapping.Project.ProjectPath)!;

            onOutput?.Invoke("> dotnet " + arguments);

            var stopwatch = Stopwatch.StartNew();
            bool zeroMatched = false;

            ProcessRunResult processResult;
            try
            {
                processResult = await ProcessHost.RunAsync(
                    "dotnet",
                    arguments,
                    workingDirectory,
                    environment: null,
                    onOutput: line =>
                    {
                        if (line.IndexOf(ZeroMatchMarker, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            zeroMatched = true;
                        }

                        onOutput?.Invoke(line);
                    },
                    stopWhen: null,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (Win32Exception)
            {
                return Failed(
                    filter,
                    "Could not start 'dotnet'. Make sure the .NET SDK is installed and on PATH.");
            }
            catch (OperationCanceledException)
            {
                return Failed(filter, "Test run cancelled.");
            }

            stopwatch.Stop();

            string trxPath = Path.Combine(resultsDirectory, trxFileName);
            IReadOnlyList<TestCaseResult> results = TrxParser.ParseFile(trxPath);

            string? failureReason = null;
            if (!zeroMatched && results.Count == 0 && processResult.ExitCode != 0)
            {
                // No TRX and a non-zero exit means the run never got as far as executing tests —
                // almost always a build error. Surface the tail of the output rather than a bare code.
                failureReason = "dotnet test exited with code " + processResult.ExitCode +
                                " without producing any test results. " + FirstErrorLine(processResult.OutputLines);
            }

            return new TestRunResult(
                results,
                processResult.ExitCode,
                filter,
                stopwatch.Elapsed,
                zeroMatched,
                failureReason);
        }

        private static string FirstErrorLine(IReadOnlyList<string> lines)
        {
            foreach (string line in lines)
            {
                if (line.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return "First error: " + line.Trim();
                }
            }

            return "See the output above.";
        }

        private static TestRunResult Failed(string filter, string reason)
        {
            return new TestRunResult(new List<TestCaseResult>(), -1, filter, TimeSpan.Zero, false, reason);
        }

        private static string CreateTempResultsDirectory()
        {
            string path = Path.Combine(Path.GetTempPath(), "reqnroll-runner", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        internal static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }
}
