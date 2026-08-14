using System;
using System.Threading;
using Microsoft;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using System.Threading.Tasks;

namespace ReqnrollRunner.Vsix
{
    /// <summary>
    /// The dedicated "Reqnroll Runner" pane in the Output window. Every command writes here, and
    /// every failure mode gets a sentence in it — never a silent no-op (SPEC §4.5).
    /// </summary>
    internal sealed class RunnerOutputPane
    {
        private static readonly Guid PaneGuid = new Guid("2f2fdb2c-0d1b-4d54-9d19-5b6a5f1cbe11");

        private readonly IVsOutputWindowPane _pane;
        private readonly JoinableTaskFactory _joinableTaskFactory;

        private RunnerOutputPane(IVsOutputWindowPane pane, JoinableTaskFactory joinableTaskFactory)
        {
            _pane = pane;
            _joinableTaskFactory = joinableTaskFactory;
        }

        public static async Task<RunnerOutputPane> CreateAsync(AsyncPackage package, CancellationToken cancellationToken)
        {
            await package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            var outputWindow = (IVsOutputWindow)await package.GetServiceAsync(typeof(SVsOutputWindow));
            Assumes.Present(outputWindow);

            Guid paneGuid = PaneGuid;
            outputWindow.CreatePane(ref paneGuid, "Reqnroll Runner", fInitVisible: 1, fClearWithSolution: 0);
            outputWindow.GetPane(ref paneGuid, out IVsOutputWindowPane pane);

            return new RunnerOutputPane(pane, package.JoinableTaskFactory);
        }

        /// <summary>Writes a line. Safe to call from any thread.</summary>
        public void WriteLine(string message)
        {
            _joinableTaskFactory.RunAsync(async () =>
            {
                await _joinableTaskFactory.SwitchToMainThreadAsync();
                _pane.OutputStringThreadSafe(message + Environment.NewLine);
            }).FileAndForget("reqnrollrunner/output/write");
        }

        public void WriteBlankLine() => WriteLine(string.Empty);

        /// <summary>Clears the pane, brings it forward, and starts a run with a header line.</summary>
        /// <remarks>
        /// The pane is activated HERE, at the start, not only on failure.
        ///
        /// `CreatePane(fInitVisible: 1)` makes the pane exist; it does not open the Output window or
        /// select the pane inside it. So a run that succeeded wrote its header, its filter, its
        /// `dotnet test` line and its summary into a pane nobody was looking at, and the only other
        /// feedback — the status bar — is cleared the moment the run ends. Right-click, Run, and to
        /// all appearances nothing happened. That is exactly how the first real user described it.
        ///
        /// SPEC §4.2 asks for the pane to pop on failure, which it still does. Popping it on start as
        /// well is strictly more informative and is what Build and Test Explorer both do — the run is
        /// visible while it runs, rather than only when it goes wrong.
        /// </remarks>
        public void StartRun(string header)
        {
            _joinableTaskFactory.RunAsync(async () =>
            {
                await _joinableTaskFactory.SwitchToMainThreadAsync();
                _pane.Clear();
                _pane.OutputStringThreadSafe("── " + header + Environment.NewLine);
                _pane.Activate();
            }).FileAndForget("reqnrollrunner/output/start");
        }

        /// <summary>Brings the pane forward. Used on failure so the user is not left guessing.</summary>
        public void Activate()
        {
            _joinableTaskFactory.RunAsync(async () =>
            {
                await _joinableTaskFactory.SwitchToMainThreadAsync();
                _pane.Activate();
            }).FileAndForget("reqnrollrunner/output/activate");
        }
    }
}
