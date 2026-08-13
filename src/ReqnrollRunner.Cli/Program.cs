using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ReqnrollRunner.Core.Execution;
using ReqnrollRunner.Core.Mapping;
using ReqnrollRunner.Core.Model;

namespace ReqnrollRunner.Cli
{
    /// <summary>
    /// A thin veneer over <c>ReqnrollRunner.Core</c>. It exists for two reasons: <c>map</c> is the
    /// primary manual harness for the scenario → filter mapping, and <c>--json</c> is the seam the
    /// planned VS Code head will speak to (SPEC §8).
    /// </summary>
    internal static class Program
    {
        private const int ExitSuccess = 0;
        private const int ExitFailure = 1;
        private const int ExitUsage = 2;

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,

            // camelCase because this is the wire contract the planned VS Code head consumes
            // (SPEC §8), and it is idiomatic there.
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,

            // Scenario titles and generated method names carry non-ASCII (Unicodeスカラー); escaping
            // it would make the output unreadable for no benefit.
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        internal static async Task<int> Main(string[] args)
        {
            CommandLineOptions options = CommandLineOptions.Parse(args);

            if (options.Command == CommandKind.Help)
            {
                Console.WriteLine(CommandLineOptions.Usage);
                return ExitSuccess;
            }

            if (options.Error != null)
            {
                Console.Error.WriteLine(options.Error);
                Console.Error.WriteLine();
                Console.Error.WriteLine(CommandLineOptions.Usage);
                return ExitUsage;
            }

            MappingResult mapping = new ScenarioMapper().Map(options.File!, options.Line);

            switch (options.Command)
            {
                case CommandKind.Map:
                    return ReportMapping(mapping, options.Json);

                case CommandKind.Run:
                    return await RunAsync(mapping, options).ConfigureAwait(false);

                case CommandKind.Debug:
                    return await DebugAsync(mapping, options).ConfigureAwait(false);

                default:
                    Console.Error.WriteLine(CommandLineOptions.Usage);
                    return ExitUsage;
            }
        }

        private static int ReportMapping(MappingResult mapping, bool json)
        {
            if (json)
            {
                Console.WriteLine(JsonSerializer.Serialize(MappingDto.From(mapping), JsonOptions));
                return mapping.Success ? ExitSuccess : ExitFailure;
            }

            if (!mapping.Success)
            {
                Console.Error.WriteLine("✗ " + mapping.Error);
                return ExitFailure;
            }

            ScenarioTarget target = mapping.Target!;
            TestProjectInfo project = mapping.Project!;

            Console.WriteLine("Target       : " + target.Describe());
            Console.WriteLine("Feature      : " + target.FeatureName + "  (line " + target.Line + ")");
            Console.WriteLine("Project      : " + project.ProjectPath);
            Console.WriteLine("Runner       : " + project.Runner);
            Console.WriteLine("Frameworks   : " + (project.TargetFrameworks.Count == 0
                ? "(none declared)"
                : string.Join(", ", project.TargetFrameworks)));

            if (mapping.GeneratedTypeName != null)
            {
                Console.WriteLine("Test class   : " + mapping.GeneratedTypeName);
            }

            if (mapping.GeneratedMethodName != null)
            {
                Console.WriteLine("Test method  : " + mapping.GeneratedMethodName);
            }

            Console.WriteLine("Filter       : " + mapping.Filter!.Expression);
            Console.WriteLine("Strategy     : " + mapping.Filter.Strategy + " — " + mapping.Filter.Explanation);

            WriteWarnings(mapping.Warnings);
            return ExitSuccess;
        }

        private static async Task<int> RunAsync(MappingResult mapping, CommandLineOptions options)
        {
            if (!mapping.Success)
            {
                return ReportMapping(mapping, options.Json);
            }

            var runOptions = new RunOptions
            {
                // The CLI defaults to building, unlike the VSIX which builds through the IDE first.
                NoBuild = options.NoBuild,
                Framework = options.Framework,
                ExtraArguments = options.ExtraArguments,
            };

            using var cancellation = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cancellation.Cancel();
            };

            Action<string>? onOutput = options.Json ? null : Console.WriteLine;

            TestRunResult result = await new DotnetTestRunner()
                .RunAsync(mapping, runOptions, onOutput, cancellation.Token)
                .ConfigureAwait(false);

            if (options.Json)
            {
                Console.WriteLine(JsonSerializer.Serialize(RunDto.From(mapping, result), JsonOptions));
                return result.IsSuccess ? ExitSuccess : ExitFailure;
            }

            Console.WriteLine();
            Console.WriteLine(Summarise(mapping, result));
            WriteWarnings(mapping.Warnings);

            return result.IsSuccess ? ExitSuccess : ExitFailure;
        }

        private static async Task<int> DebugAsync(MappingResult mapping, CommandLineOptions options)
        {
            if (!mapping.Success)
            {
                return ReportMapping(mapping, options.Json);
            }

            var runOptions = new RunOptions
            {
                NoBuild = options.NoBuild,
                Framework = options.Framework,
                ExtraArguments = options.ExtraArguments,
                AttachTimeoutSeconds = options.TimeoutSeconds,
            };

            using var cancellation = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cancellation.Cancel();
            };

            DebugLaunchResult launch = await new DebugSessionLauncher()
                .LaunchAsync(mapping, runOptions, Console.WriteLine, cancellation.Token)
                .ConfigureAwait(false);

            if (!launch.Success)
            {
                Console.Error.WriteLine("✗ " + launch.Error);
                return ExitFailure;
            }

            DebugTarget target = launch.Target!;
            Console.WriteLine();
            Console.WriteLine("Attach a debugger to process " + target.ProcessId + " (" + target.ProcessName + ").");
            Console.WriteLine("The test host is parked until you do. Press Ctrl+C to abandon the run.");

            try
            {
                // Park until the test process finishes or the user gives up.
                while (!target.Process.HasExited && !cancellation.Token.IsCancellationRequested)
                {
                    await Task.Delay(250, cancellation.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Falls through to cleanup.
            }

            if (!target.Process.HasExited)
            {
                target.Process.Kill();
            }

            target.Process.Dispose();
            return ExitSuccess;
        }

        private static string Summarise(MappingResult mapping, TestRunResult result)
        {
            if (result.FailureReason != null)
            {
                return "✗ " + result.FailureReason;
            }

            if (result.ZeroTestsMatched)
            {
                return "✗ No tests matched the filter.\n" +
                       "    Filter used: " + result.FilterUsed + "\n" +
                       "    Check the generated code-behind next to " +
                       System.IO.Path.GetFileName(mapping.FeaturePath) +
                       " — if it is missing or stale, rebuild the project.";
            }

            string duration = result.Duration.TotalSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);

            if (result.Failed > 0)
            {
                var lines = new List<string>
                {
                    "✗ " + result.Failed + " failed, " + result.Passed + " passed in " + duration + "s",
                };

                foreach (TestCaseResult test in result.Results)
                {
                    if (test.Outcome == TestOutcome.Failed)
                    {
                        lines.Add("    " + test.DisplayName + ": " + FirstLine(test.ErrorMessage));
                    }
                }

                return string.Join(Environment.NewLine, lines);
            }

            if (result.Results.Count == 0)
            {
                return "✗ The run produced no test results at all. See the output above.";
            }

            string summary = "✓ " + result.Passed + " passed in " + duration + "s";
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

        private static void WriteWarnings(IReadOnlyList<string> warnings)
        {
            foreach (string warning in warnings)
            {
                Console.WriteLine("⚠ " + warning);
            }
        }
    }
}
