using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace ReqnrollRunner.Vsix
{
    /// <summary>Tools → Options → Reqnroll Runner (SPEC §4.4).</summary>
    [Guid(ReqnrollRunnerGuids.OptionsPageGuidString)]
    [ComVisible(true)]
    public sealed class ReqnrollRunnerOptions : DialogPage
    {
        [Category("Execution")]
        [DisplayName("Skip build before run")]
        [Description(
            "Run the test without building the project first. Off by default: Visual Studio builds " +
            "the containing project so compile errors land in the Error List, and dotnet test is then " +
            "invoked with --no-build. Turn this on only if you are certain the build is current.")]
        public bool SkipBuild { get; set; }

        [Category("Execution")]
        [DisplayName("Extra dotnet test arguments")]
        [Description("Appended verbatim to the dotnet test command line, after everything else.")]
        public string ExtraArguments { get; set; } = string.Empty;

        [Category("Execution")]
        [DisplayName("Preferred target framework")]
        [Description(
            "Which target framework to run when the test project multi-targets. Leave empty to use " +
            "the first one the project declares.")]
        public string PreferredTargetFramework { get; set; } = string.Empty;

        [Category("Debugging")]
        [DisplayName("Test host attach timeout (seconds)")]
        [Description(
            "How long to wait for the test host to report its process id before giving up. Increase " +
            "this for projects that are slow to start.")]
        public int AttachTimeoutSeconds { get; set; } = 30;
    }
}
