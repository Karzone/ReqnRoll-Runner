using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gherkin;
using Gherkin.Ast;
using ReqnrollRunner.Core.Model;

namespace ReqnrollRunner.Core.Parsing
{
    /// <summary>Where a runnable thing sits in a feature file.</summary>
    public sealed class FeatureOutlineEntry
    {
        public FeatureOutlineEntry(int line, string name, TargetKind kind)
        {
            Line = line;
            Name = name;
            Kind = kind;
        }

        /// <summary>1-based keyword line.</summary>
        public int Line { get; }

        public string Name { get; }

        /// <summary><see cref="TargetKind.Scenario"/> or <see cref="TargetKind.ScenarioOutline"/>.</summary>
        public TargetKind Kind { get; }
    }

    /// <summary>
    /// Lists every scenario in a feature file, from <em>text</em> rather than from a path.
    /// </summary>
    /// <remarks>
    /// This exists for the editor adornments, which must reflect what is on screen — including
    /// unsaved edits — so they cannot read the file from disk. Keeping it in Core rather than in the
    /// VSIX means the parsing half of that feature is unit-testable even though the drawing half
    /// is not.
    /// </remarks>
    public static class FeatureOutline
    {
        /// <summary>
        /// Parses <paramref name="text"/> and returns every scenario and outline in line order.
        /// Returns an empty list if the text does not parse — which is the normal state of a file
        /// being typed into, and must never throw at the caller.
        /// </summary>
        public static IReadOnlyList<FeatureOutlineEntry> Parse(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return Array.Empty<FeatureOutlineEntry>();
            }

            GherkinDocument document;
            try
            {
                using (var reader = new StringReader(text))
                {
                    document = new Parser().Parse(reader);
                }
            }
            catch (CompositeParserException)
            {
                return Array.Empty<FeatureOutlineEntry>();
            }
            catch (NoSuchLanguageException)
            {
                return Array.Empty<FeatureOutlineEntry>();
            }

            if (document?.Feature == null)
            {
                return Array.Empty<FeatureOutlineEntry>();
            }

            var entries = new List<FeatureOutlineEntry>();
            foreach (Scenario scenario in ScenariosIn(document.Feature))
            {
                bool isOutline = scenario.Examples != null && scenario.Examples.Any();
                entries.Add(new FeatureOutlineEntry(
                    scenario.Location.Line,
                    scenario.Name ?? string.Empty,
                    isOutline ? TargetKind.ScenarioOutline : TargetKind.Scenario));
            }

            entries.Sort((a, b) => a.Line.CompareTo(b.Line));
            return entries;
        }

        /// <summary>Just the keyword lines, which is all the adornment layer needs.</summary>
        public static IReadOnlyList<int> ScenarioLines(string text)
        {
            return Parse(text).Select(e => e.Line).ToList();
        }

        private static IEnumerable<Scenario> ScenariosIn(Feature feature)
        {
            foreach (object child in feature.Children)
            {
                if (child is Scenario scenario)
                {
                    yield return scenario;
                }
                else if (child is Rule rule)
                {
                    foreach (object nested in rule.Children)
                    {
                        if (nested is Scenario nestedScenario)
                        {
                            yield return nestedScenario;
                        }
                    }
                }
            }
        }
    }
}
