using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace ReqnrollRunner.Core.Execution
{
    /// <summary>Result of a completed child process.</summary>
    public sealed class ProcessRunResult
    {
        public ProcessRunResult(int exitCode, IReadOnlyList<string> outputLines, bool stoppedEarly)
        {
            ExitCode = exitCode;
            OutputLines = outputLines;
            StoppedEarly = stoppedEarly;
        }

        public int ExitCode { get; }

        /// <summary>Every stdout and stderr line, interleaved in arrival order.</summary>
        public IReadOnlyList<string> OutputLines { get; }

        /// <summary>True when a <c>stopWhen</c> predicate ended the wait before the process exited.</summary>
        public bool StoppedEarly { get; }
    }

    /// <summary>
    /// Minimal async child-process host: streams output line by line, supports cancellation, and can
    /// stop waiting early once an expected line appears (used to catch the test host's process id
    /// while it sits waiting for a debugger).
    /// </summary>
    internal static class ProcessHost
    {
        public static async Task<ProcessRunResult> RunAsync(
            string fileName,
            string arguments,
            string workingDirectory,
            IDictionary<string, string>? environment,
            Action<string>? onOutput,
            Func<string, bool>? stopWhen,
            CancellationToken cancellationToken)
        {
            var startInfo = new ProcessStartInfo(fileName, arguments)
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            if (environment != null)
            {
                foreach (KeyValuePair<string, string> pair in environment)
                {
                    startInfo.EnvironmentVariables[pair.Key] = pair.Value;
                }
            }

            var lines = new List<string>();
            var exited = new TaskCompletionSource<int>();
            var stopRequested = new TaskCompletionSource<bool>();

            using (var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true })
            {
                void Handle(string? data)
                {
                    if (data == null)
                    {
                        return;
                    }

                    lock (lines)
                    {
                        lines.Add(data);
                    }

                    onOutput?.Invoke(data);

                    if (stopWhen != null && stopWhen(data))
                    {
                        stopRequested.TrySetResult(true);
                    }
                }

                process.OutputDataReceived += (_, e) => Handle(e.Data);
                process.ErrorDataReceived += (_, e) => Handle(e.Data);
                process.Exited += (_, _) => exited.TrySetResult(process.ExitCode);

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                using (cancellationToken.Register(() => TryKill(process)))
                {
                    Task completed = await Task.WhenAny(exited.Task, stopRequested.Task).ConfigureAwait(false);

                    if (completed == stopRequested.Task)
                    {
                        IReadOnlyList<string> snapshot;
                        lock (lines)
                        {
                            snapshot = lines.ToArray();
                        }

                        // The caller owns the process from here (it is parked waiting for a debugger).
                        return new ProcessRunResult(0, snapshot, stoppedEarly: true);
                    }

                    int exitCode = await exited.Task.ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();

                    IReadOnlyList<string> allLines;
                    lock (lines)
                    {
                        allLines = lines.ToArray();
                    }

                    return new ProcessRunResult(exitCode, allLines, stoppedEarly: false);
                }
            }
        }

        /// <summary>
        /// Starts a process and hands the live <see cref="Process"/> back once
        /// <paramref name="stopWhen"/> matches, so the caller can keep streaming and later kill it.
        /// </summary>
        public static Process Start(
            string fileName,
            string arguments,
            string workingDirectory,
            IDictionary<string, string>? environment,
            Action<string>? onOutput,
            Func<string, bool>? stopWhen,
            out Task<bool> matched)
        {
            var startInfo = new ProcessStartInfo(fileName, arguments)
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            if (environment != null)
            {
                foreach (KeyValuePair<string, string> pair in environment)
                {
                    startInfo.EnvironmentVariables[pair.Key] = pair.Value;
                }
            }

            var matchSource = new TaskCompletionSource<bool>();
            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

            void Handle(string? data)
            {
                if (data == null)
                {
                    return;
                }

                onOutput?.Invoke(data);

                if (stopWhen != null && stopWhen(data))
                {
                    matchSource.TrySetResult(true);
                }
            }

            process.OutputDataReceived += (_, e) => Handle(e.Data);
            process.ErrorDataReceived += (_, e) => Handle(e.Data);

            // If the process dies before the expected line shows up, unblock the waiter with a
            // negative result rather than leaving it to time out.
            process.Exited += (_, _) => matchSource.TrySetResult(false);

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            matched = matchSource.Task;
            return process;
        }

        public static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch (InvalidOperationException)
            {
                // Already gone.
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Access denied / already exiting — nothing useful we can do.
            }
        }
    }
}
