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

    /// <summary>A VSTest <c>--filter</c> expression plus the provenance the UI needs to explain it.</summary>
    public sealed class TestFilter
    {
        public TestFilter(string expression, FilterStrategy strategy, string explanation)
        {
            Expression = expression;
            Strategy = strategy;
            Explanation = explanation;
        }

        /// <summary>The expression passed verbatim to <c>dotnet test --filter</c>.</summary>
        public string Expression { get; }

        public FilterStrategy Strategy { get; }

        /// <summary>One sentence saying how this expression was derived, for the output pane.</summary>
        public string Explanation { get; }
    }
}
