using System.Collections.Generic;

namespace ReqnrollRunner.Core.Model
{
    /// <summary>
    /// The thing the caret is pointing at. Purely a description of the <c>.feature</c> file —
    /// it knows nothing about projects, runners or filters.
    /// </summary>
    public sealed class ScenarioTarget
    {
        public ScenarioTarget(
            TargetKind kind,
            string featureName,
            string? name,
            int line,
            IReadOnlyList<string> tags,
            ExampleRowInfo? exampleRow = null)
        {
            Kind = kind;
            FeatureName = featureName;
            Name = name;
            Line = line;
            Tags = tags;
            ExampleRow = exampleRow;
        }

        public TargetKind Kind { get; }

        /// <summary>Title on the <c>Feature:</c> line, verbatim.</summary>
        public string FeatureName { get; }

        /// <summary>
        /// Title of the scenario / outline / rule, verbatim. <see langword="null"/> when
        /// <see cref="Kind"/> is <see cref="TargetKind.Feature"/>.
        /// </summary>
        public string? Name { get; }

        /// <summary>
        /// 1-based line of the target's keyword line (the <c>Scenario:</c> / <c>Feature:</c> line itself,
        /// not its tags). This is the join key against the generated code-behind's <c>#line</c> directives.
        /// </summary>
        public int Line { get; }

        /// <summary>Tags declared directly on the target (not inherited).</summary>
        public IReadOnlyList<string> Tags { get; }

        /// <summary>
        /// The example row the caret is on, when <see cref="Kind"/> is
        /// <see cref="TargetKind.ExampleRow"/>; otherwise <see langword="null"/>.
        /// </summary>
        /// <remarks>
        /// Note that <see cref="Line"/> still reports the <em>outline's</em> keyword line even for a
        /// row target. That is deliberate: <see cref="Line"/> is the join key against the generated
        /// code-behind's <c>#line</c> directives, and every row of an outline belongs to the one
        /// generated method. The row's own line is <see cref="ExampleRowInfo.Line"/>.
        /// </remarks>
        public ExampleRowInfo? ExampleRow { get; }

        /// <summary>
        /// True when executing this target means running every test in the feature class.
        /// v1 treats <see cref="TargetKind.Rule"/> this way — see SPEC §3.1.
        /// </summary>
        public bool RunsWholeFeature => Kind == TargetKind.Feature || Kind == TargetKind.Rule;

        /// <summary>Human label for command text and log lines.</summary>
        public string Describe()
        {
            switch (Kind)
            {
                case TargetKind.Scenario:
                    return "Scenario '" + Name + "'";
                case TargetKind.ScenarioOutline:
                    return "Scenario Outline '" + Name + "' (all examples)";
                case TargetKind.ExampleRow:
                    return "Example row " + (ExampleRow?.OrdinalWithinOutline ?? 0) +
                           " of Scenario Outline '" + Name + "'" +
                           (ExampleRow == null ? string.Empty : " (" + string.Join(", ", ExampleRow.Values) + ")");
                case TargetKind.Rule:
                    return "Rule '" + Name + "' (runs the whole feature in v1)";
                default:
                    return "Feature '" + FeatureName + "'";
            }
        }
    }

    /// <summary>One row of an <c>Examples:</c> table.</summary>
    public sealed class ExampleRowInfo
    {
        public ExampleRowInfo(int line, int ordinalWithinOutline, IReadOnlyList<string> values, IReadOnlyList<string> columns)
        {
            Line = line;
            OrdinalWithinOutline = ordinalWithinOutline;
            Values = values;
            Columns = columns;
        }

        /// <summary>1-based line of the row itself.</summary>
        public int Line { get; }

        /// <summary>
        /// 1-based position among all body rows of the outline, counting across every
        /// <c>Examples:</c> block, which is the order Reqnroll generates them in.
        /// </summary>
        public int OrdinalWithinOutline { get; }

        /// <summary>The row's cell values, in column order.</summary>
        public IReadOnlyList<string> Values { get; }

        /// <summary>The column names from the block's header row.</summary>
        public IReadOnlyList<string> Columns { get; }
    }
}
