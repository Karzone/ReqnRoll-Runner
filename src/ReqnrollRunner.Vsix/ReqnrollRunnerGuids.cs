using System;

namespace ReqnrollRunner.Vsix
{
    /// <summary>Identity constants shared between the C# code and <c>ReqnrollRunnerPackage.vsct</c>.</summary>
    /// <remarks>
    /// These values are duplicated in the .vsct's Symbols section. If you change one, change both —
    /// a mismatch produces a command that silently never appears, with no build error.
    /// </remarks>
    internal static class ReqnrollRunnerGuids
    {
        public const string PackageGuidString = "da7f88d0-dc3e-49a7-b3e8-d7e088ba9205";

        public const string CommandSetGuidString = "b3946a18-f113-4e10-be68-2d394f82eb6e";

        /// <summary>
        /// UI context that is active only while a <c>.feature</c> file is the active document.
        /// Both commands are gated on it, so they never appear in a .cs file's context menu.
        /// </summary>
        public const string FeatureFileContextGuidString = "15d8f166-4fb2-4e82-b1b1-beb829088adf";

        public const string OptionsPageGuidString = "5720f27e-a0a2-4577-a4a4-183f7ee7a9db";

        public static readonly Guid CommandSet = new Guid(CommandSetGuidString);

        /// <summary>Command ids. Must match the .vsct.</summary>
        public const int RunScenarioCommandId = 0x0100;

        public const int DebugScenarioCommandId = 0x0101;
    }
}
