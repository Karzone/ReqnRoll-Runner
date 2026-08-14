namespace ReqnrollRunner.Core.Model
{
    /// <summary>How the filter expression was arrived at. Surfaced to the user because the two
    /// strategies have very different reliability.</summary>
    public enum FilterStrategy
    {
        /// <summary>
        /// Names were read out of the generated <c>.feature.cs</c>. Exact — this is ground truth.
        /// </summary>
        CodeBehind = 0,

        /// <summary>
        /// Names were reconstructed by <c>TestNameSanitizer</c> because no code-behind was found.
        /// Best effort; the project probably has not been built yet.
        /// </summary>
        Sanitized = 1,

        /// <summary>
        /// Scoped to the feature class only, because the scenario could not be addressed precisely.
        /// </summary>
        FeatureScopeFallback = 2,
    }

    /// <summary>How to select the target, plus the provenance the UI needs to explain it.</summary>
    public sealed class TestFilter
    {
        public TestFilter(string expression, FilterStrategy strategy, string explanation, string? runSettings = null)
        {
            Expression = expression;
            Strategy = strategy;
            Explanation = explanation;
            RunSettings = runSettings;
        }

        /// <summary>
        /// The expression passed verbatim to <c>dotnet test --filter</c>. Empty when
        /// <see cref="RunSettings"/> is doing the selecting instead.
        /// </summary>
        public string Expression { get; }

        /// <summary>
        /// An adapter-specific setting passed after the <c>--</c> separator, e.g.
        /// <c>NUnit.Where=test =~ '…'</c>. <see langword="null"/> for the common case.
        /// </summary>
        /// <remarks>
        /// This exists because VSTest's <c>--filter</c> cannot express everything the adapters can.
        /// NUnit puts an outline row's arguments in the test name in a form no <c>--filter</c>
        /// operator matches, but its own <c>--where</c> language matches it easily — so selecting a
        /// single example row on NUnit means going through the adapter rather than through VSTest.
        ///
        /// The two are mutually exclusive in practice, and not by choice: when both are supplied,
        /// <c>--filter</c> wins and the adapter setting is silently ignored (measured — the combined
        /// form ran all three rows). So a filter that sets this leaves <see cref="Expression"/>
        /// empty and makes the setting carry the whole selection, including the class name.
        /// </remarks>
        public string? RunSettings { get; }

        /// <summary>What was actually asked of the test platform, for logs and error messages.</summary>
        public string Describe()
        {
            if (string.IsNullOrEmpty(Expression))
            {
                return RunSettings ?? string.Empty;
            }

            return string.IsNullOrEmpty(RunSettings)
                ? Expression
                : Expression + "  [" + RunSettings + "]";
        }

        public FilterStrategy Strategy { get; }

        /// <summary>One sentence saying how this expression was derived, for the output pane.</summary>
        public string Explanation { get; }
    }
}
