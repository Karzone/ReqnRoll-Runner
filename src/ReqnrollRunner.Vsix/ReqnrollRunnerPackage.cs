using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

namespace ReqnrollRunner.Vsix
{
    /// <summary>
    /// The extension package. Loads on the <c>.feature</c> UI context, registers the two commands and
    /// the options page.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a classic in-process VSSDK extension rather than the out-of-process
    /// <c>VisualStudio.Extensibility</c> model, because v1 needs two things only the in-process API
    /// offers: an editor context menu gated on file type, and <c>DTE.Debugger</c> attach-to-process.
    /// </para>
    /// <para>
    /// It is a companion to the official Reqnroll Visual Studio extension, not a replacement. It adds
    /// no editor features — no syntax highlighting, no IntelliSense, no Go To Definition — and is
    /// designed to be installed alongside it.
    /// </para>
    /// </remarks>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration("Reqnroll Runner", "Run and debug Reqnroll scenarios from the feature file.", "1.0")]
    [Guid(ReqnrollRunnerGuids.PackageGuidString)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideOptionPage(typeof(ReqnrollRunnerOptions), "Reqnroll Runner", "General", 0, 0, supportsAutomation: true)]

    // Load once a solution is open, in the background. Command visibility is then decided per
    // right-click by ScenarioCommandHandler.BeforeQueryStatus — see the note in the .vsct for why the
    // file-type gate is not a UI context.
    [ProvideAutoLoad(Microsoft.VisualStudio.VSConstants.UICONTEXT.SolutionExists_string, PackageAutoLoadFlags.BackgroundLoad)]
    public sealed class ReqnrollRunnerPackage : AsyncPackage
    {
        /// <summary>The single output pane every command writes to.</summary>
        internal RunnerOutputPane OutputPane { get; private set; } = null!;

        /// <summary>Current settings from Tools → Options → Reqnroll Runner.</summary>
        internal ReqnrollRunnerOptions Options =>
            (ReqnrollRunnerOptions)GetDialogPage(typeof(ReqnrollRunnerOptions));

        protected override async Task InitializeAsync(
            CancellationToken cancellationToken,
            IProgress<ServiceProgressData> progress)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            OutputPane = await RunnerOutputPane.CreateAsync(this, cancellationToken);

            await ScenarioCommandHandler.InitializeAsync(this, cancellationToken);
        }
    }
}
