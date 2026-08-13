using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ReqnrollRunner.Core.Model;

namespace ReqnrollRunner.Core.Execution
{
    /// <summary>The test host process a debugger should attach to.</summary>
    public sealed class DebugTarget
    {
        public DebugTarget(int processId, string processName, Process process, string trxPath)
        {
            ProcessId = processId;
            ProcessName = processName;
            Process = process;
            TrxPath = trxPath;
        }

        public int ProcessId { get; }

        /// <summary>As announced by VSTest — <c>testhost</c> on Windows, <c>dotnet</c> on Linux/macOS.</summary>
        public string ProcessName { get; }

        /// <summary>The live <c>dotnet test</c> process. The caller owns it and must dispose or kill it.</summary>
        public Process Process { get; }

        /// <summary>Where the TRX will land once the run finishes.</summary>
        public string TrxPath { get; }
    }

    /// <summary>Outcome of trying to start a debuggable run.</summary>
    public sealed class DebugLaunchResult
    {
        private DebugLaunchResult(DebugTarget? target, string? error)
        {
            Target = target;
            Error = error;
        }

        public DebugTarget? Target { get; }

        public string? Error { get; }

        public bool Success => Target != null;

        public static DebugLaunchResult Ok(DebugTarget target)
        {
            return new DebugLaunchResult(target, null);
        }

        public static DebugLaunchResult Fail(string error)
        {
            return new DebugLaunchResult(null, error);
        }
    }

    /// <summary>
    /// Starts <c>dotnet test</c> with host debugging enabled and reports the test host's process id.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Setting <c>VSTEST_HOST_DEBUG=1</c> makes the test host print its own pid and then block until a
    /// debugger attaches. Core's entire job is to launch, find that pid and hand it back; performing
    /// the attach is the host's problem, because attaching is the one genuinely IDE-specific step
    /// (SPEC §3.5). That split is what keeps Core free of any Visual Studio dependency and leaves the
    /// door open for the VS Code head in v2.
    /// </para>
    /// <para>
    /// The announcement line is verbatim <c>Process Id: 23840, Name: dotnet</c>, verified against
    /// VSTest 17.8. It is printed more than once; only the first is reported.
    /// </para>
    /// </remarks>
    public sealed class DebugSessionLauncher
    {
        private static readonly Regex ProcessIdRegex = new Regex(
            @"Process Id:\s*(?<pid>\d+)(?:\s*,\s*Name:\s*(?<name>[^\s,]+))?",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>Extracts the announced process id from one output line, or <see langword="null"/>.</summary>
        public static (int ProcessId, string ProcessName)? TryParseProcessId(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return null;
            }

            Match match = ProcessIdRegex.Match(line);
            if (!match.Success)
            {
                return null;
            }

            if (!int.TryParse(match.Groups["pid"].Value, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out int pid))
            {
                return null;
            }

            string name = match.Groups["name"].Success ? match.Groups["name"].Value : "testhost";
            return (pid, name);
        }

        /// <summary>
        /// Launches a debuggable run and waits for the test host to announce itself.
        /// </summary>
        /// <returns>
        /// The target to attach to. On timeout or early exit the child process is killed and an
        /// explanatory error is returned — never a silent no-op.
        /// </returns>
        public async Task<DebugLaunchResult> LaunchAsync(
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
                return DebugLaunchResult.Fail(mapping?.Error ?? "Mapping failed, so there is nothing to debug.");
            }

            options = options ?? new RunOptions();

            string resultsDirectory = options.ResultsDirectory
                                      ?? Path.Combine(Path.GetTempPath(), "reqnroll-runner", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(resultsDirectory);

            string trxFileName = "reqnroll-runner-" + Guid.NewGuid().ToString("N") + ".trx";

            var effectiveOptions = new RunOptions
            {
                NoBuild = options.NoBuild,
                Framework = options.Framework ?? mapping.Project.ResolveFramework(options.Framework),
                ExtraArguments = options.ExtraArguments,
                ResultsDirectory = resultsDirectory,
                WorkingDirectory = options.WorkingDirectory,
            };

            string arguments = DotnetTestRunner.BuildArguments(
                mapping.Project.ProjectPath, mapping.Filter.Expression, trxFileName, effectiveOptions);

            string workingDirectory = effectiveOptions.WorkingDirectory
                                      ?? Path.GetDirectoryName(mapping.Project.ProjectPath)!;

            onOutput?.Invoke("> VSTEST_HOST_DEBUG=1 dotnet " + arguments);

            (int ProcessId, string ProcessName)? found = null;

            Process process;
            Task<bool> matched;
            try
            {
                process = ProcessHost.Start(
                    "dotnet",
                    arguments,
                    workingDirectory,
                    new Dictionary<string, string> { { "VSTEST_HOST_DEBUG", "1" } },
                    onOutput,
                    line =>
                    {
                        if (found != null)
                        {
                            return false;
                        }

                        found = TryParseProcessId(line);
                        return found != null;
                    },
                    out matched);
            }
            catch (Win32Exception)
            {
                return DebugLaunchResult.Fail(
                    "Could not start 'dotnet'. Make sure the .NET SDK is installed and on PATH.");
            }

            int timeoutSeconds = options.AttachTimeoutSeconds > 0 ? options.AttachTimeoutSeconds : 30;

            using (var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                Task delay = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), timeoutSource.Token);
                Task winner = await Task.WhenAny(matched, delay).ConfigureAwait(false);
                timeoutSource.Cancel();

                if (winner != matched || !matched.Result || found == null)
                {
                    ProcessHost.TryKill(process);
                    process.Dispose();

                    if (cancellationToken.IsCancellationRequested)
                    {
                        return DebugLaunchResult.Fail("Debug launch cancelled.");
                    }

                    if (winner == matched)
                    {
                        return DebugLaunchResult.Fail(
                            "The test process exited before announcing a test host to attach to. " +
                            "This usually means the build failed or the filter matched no tests — check the output above.");
                    }

                    return DebugLaunchResult.Fail(
                        "Timed out after " + timeoutSeconds + "s waiting for the test host to report its process id. " +
                        "Increase the attach timeout in Tools → Options → Reqnroll Runner if this project is slow to start.");
                }
            }

            return DebugLaunchResult.Ok(new DebugTarget(
                found.Value.ProcessId,
                found.Value.ProcessName,
                process,
                Path.Combine(resultsDirectory, trxFileName)));
        }
    }
}
