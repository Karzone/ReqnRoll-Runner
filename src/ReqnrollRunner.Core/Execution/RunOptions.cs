namespace ReqnrollRunner.Core.Execution
{
    /// <summary>Knobs for a <c>dotnet test</c> invocation. Mirrors the VSIX options page (SPEC §4.4).</summary>
    public sealed class RunOptions
    {
        /// <summary>
        /// Pass <c>--no-build</c>. Default <see langword="true"/>: the VSIX builds through Visual
        /// Studio first so the user gets the IDE's error list, and re-building inside
        /// <c>dotnet test</c> would duplicate that work. The CLI defaults it off.
        /// </summary>
        public bool NoBuild { get; set; } = true;

        /// <summary>TFM to run when the project multi-targets. <see langword="null"/> omits <c>--framework</c>.</summary>
        public string? Framework { get; set; }

        /// <summary>
        /// Build configuration to run, e.g. <c>Debug</c> or <c>Release</c>. <see langword="null"/>
        /// omits <c>--configuration</c>, which leaves <c>dotnet test</c> on its own default of Debug.
        /// </summary>
        /// <remarks>
        /// This has to be passed explicitly whenever the caller built something other than Debug.
        /// With <see cref="NoBuild"/> set, <c>dotnet test</c> looks for
        /// <c>bin/&lt;configuration&gt;/&lt;tfm&gt;/…dll</c> and fails with a genuinely unhelpful
        /// message if it is not there — "The argument …dll is invalid" — so the VSIX passes Visual
        /// Studio's active solution configuration and the CLI exposes <c>--configuration</c>.
        /// </remarks>
        public string? Configuration { get; set; }

        /// <summary>Appended verbatim after everything else, so a user can pass anything we do not model.</summary>
        public string? ExtraArguments { get; set; }

        /// <summary>Where the TRX is written. <see langword="null"/> uses a fresh temp directory.</summary>
        public string? ResultsDirectory { get; set; }

        /// <summary>How long to wait for the test host to announce its process id when debugging.</summary>
        public int AttachTimeoutSeconds { get; set; } = 30;

        /// <summary>Working directory for the child process. Defaults to the project's directory.</summary>
        public string? WorkingDirectory { get; set; }
    }
}
