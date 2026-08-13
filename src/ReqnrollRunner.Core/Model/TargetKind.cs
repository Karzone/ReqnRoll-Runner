namespace ReqnrollRunner.Core.Model
{
    /// <summary>What the caret resolved to inside a <c>.feature</c> file.</summary>
    public enum TargetKind
    {
        /// <summary>A single <c>Scenario</c>.</summary>
        Scenario = 0,

        /// <summary>A <c>Scenario Outline</c> / <c>Scenario Template</c> — all example rows run together.</summary>
        ScenarioOutline = 1,

        /// <summary>
        /// The whole feature. Produced by a caret on the <c>Feature:</c> header, in <c>Background:</c>,
        /// or anywhere outside a scenario body.
        /// </summary>
        Feature = 2,

        /// <summary>
        /// A single row of an <c>Examples:</c> table.
        /// </summary>
        /// <remarks>
        /// Whether this can actually be executed on its own depends on the runner — see
        /// <c>TestFilterBuilder.ForExampleRow</c>. The target resolves the same way regardless, so
        /// the UI can always say what the caret is on even when execution has to widen.
        /// </remarks>
        ExampleRow = 4,

        /// <summary>
        /// A <c>Rule:</c> header. v1 widens this to the whole feature when running (see
        /// <see cref="ScenarioTarget.RunsWholeFeature"/>) but reports the rule so the UI can say so.
        /// </summary>
        Rule = 3,
    }
}
