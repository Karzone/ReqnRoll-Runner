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
            IReadOnlyList<string> tags)
        {
            Kind = kind;
            FeatureName = featureName;
            Name = name;
            Line = line;
            Tags = tags;
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
                case TargetKind.Rule:
                    return "Rule '" + Name + "' (runs the whole feature in v1)";
                default:
                    return "Feature '" + FeatureName + "'";
            }
        }
    }
}
