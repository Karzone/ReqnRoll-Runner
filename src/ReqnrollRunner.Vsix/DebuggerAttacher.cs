using System;
using System.Collections.Generic;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;

namespace ReqnrollRunner.Vsix
{
    /// <summary>Attaches the Visual Studio debugger to the test host process (SPEC §4.3).</summary>
    /// <remarks>
    /// <para>
    /// DTE's attach API is old, stable and documented, which is exactly why it was chosen over
    /// <c>Microsoft.VisualStudio.TestWindow.Extensibility</c> for v1. Core has already done the hard
    /// part — launching with <c>VSTEST_HOST_DEBUG=1</c> and reading back the process id — so all that
    /// is left here is matching the pid and calling Attach.
    /// </para>
    /// <para>
    /// Once attached, the test host stops waiting and runs on by itself, so breakpoints in step
    /// definition classes hit without any further prompting.
    /// </para>
    /// </remarks>
    internal static class DebuggerAttacher
    {
        /// <summary>
        /// Debug engines to try, in order. The right one depends on whether the test project targets
        /// .NET (CoreCLR) or .NET Framework, which we do not always know up front, so we try the
        /// modern one first and fall back.
        /// </summary>
        private static readonly string[] Engines =
        {
            "Managed (CoreCLR)",
            "Managed (v4.6, v4.5, v4.0)",
            "Managed",
        };

        /// <summary>Attaches to <paramref name="processId"/>.</summary>
        /// <returns><see langword="true"/> on success; otherwise <paramref name="error"/> explains why not.</returns>
        public static bool TryAttach(DTE2 dte, int processId, Action<string> log, out string? error)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            Process? target = FindProcess(dte, processId);
            if (target == null)
            {
                error = "Could not find process " + processId +
                        " in the debugger's process list. It may have exited already.";
                return false;
            }

            var failures = new List<string>();

            if (target is Process2 process2)
            {
                foreach (string engine in Engines)
                {
                    try
                    {
                        process2.Attach2(engine);
                        log("Attached to process " + processId + " using the '" + engine + "' engine.");
                        error = null;
                        return true;
                    }
                    catch (Exception ex)
                    {
                        failures.Add(engine + ": " + ex.Message);
                    }
                }
            }

            // Last resort: let Visual Studio pick the engine itself.
            try
            {
                target.Attach();
                log("Attached to process " + processId + ".");
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                failures.Add("automatic: " + ex.Message);
            }

            error = "Could not attach the debugger to process " + processId + ". Tried: " +
                    string.Join("; ", failures.ToArray());
            return false;
        }

        private static Process? FindProcess(DTE2 dte, int processId)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            foreach (Process process in dte.Debugger.LocalProcesses)
            {
                if (process.ProcessID == processId)
                {
                    return process;
                }
            }

            return null;
        }
    }
}
