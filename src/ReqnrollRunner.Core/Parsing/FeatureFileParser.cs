using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gherkin;
using Gherkin.Ast;
using ReqnrollRunner.Core.Model;

namespace ReqnrollRunner.Core.Parsing
{
    /// <summary>
    /// Resolves a caret position in a <c>.feature</c> file to the thing that should run.
    /// </summary>
    /// <remarks>
    /// Parsing is delegated entirely to the official Cucumber <c>Gherkin</c> package, so localized
    /// keywords, <c># language:</c> headers, doc strings, data tables and comments are handled by the
    /// same grammar Reqnroll itself uses. Nothing in here matches keywords by hand.
    /// </remarks>
    public sealed class FeatureFileParser
    {
        /// <summary>Parses <paramref name="featurePath"/> and resolves the target at <paramref name="line"/>.</summary>
        /// <param name="featurePath">Absolute path to a <c>.feature</c> file.</param>
        /// <param name="line">1-based caret line. Values below 1 are clamped to 1.</param>
        public FeatureParseResult Resolve(string featurePath, int line)
        {
            if (featurePath == null)
            {
                throw new ArgumentNullException(nameof(featurePath));
            }

            if (!File.Exists(featurePath))
            {
                return FeatureParseResult.Fail("Feature file not found: " + featurePath);
            }

            GherkinDocument document;
            try
            {
                // Read the text ourselves so a UTF-8 BOM and the file's own encoding are handled
                // consistently on every platform.
                using (var reader = new StreamReader(featurePath, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
                {
                    document = new Parser().Parse(reader);
                }
            }
            catch (CompositeParserException ex)
            {
                return FeatureParseResult.Fail("Could not parse " + Path.GetFileName(featurePath) + ": " + ex.Message);
            }
            catch (NoSuchLanguageException ex)
            {
                return FeatureParseResult.Fail("Unsupported '# language:' header in " + Path.GetFileName(featurePath) + ": " + ex.Message);
            }
            catch (IOException ex)
            {
                return FeatureParseResult.Fail("Could not read " + featurePath + ": " + ex.Message);
            }

            Feature? feature = document.Feature;
            if (feature == null)
            {
                return FeatureParseResult.Fail(
                    Path.GetFileName(featurePath) + " contains no Feature: header, so there is nothing to run.");
            }

            if (line < 1)
            {
                line = 1;
            }

            List<Anchor> anchors = BuildAnchors(feature, document);
            Anchor? hit = null;
            foreach (Anchor anchor in anchors)
            {
                if (anchor.StartLine <= line)
                {
                    hit = anchor;
                }
                else
                {
                    break;
                }
            }

            ScenarioTarget target = hit == null
                ? WholeFeature(feature)
                : hit.ToTarget(feature, line);

            return FeatureParseResult.Ok(target, feature.Name ?? string.Empty);
        }

        /// <summary>
        /// Flattens the document into the ordered list of positions the caret can "land in".
        /// A caret belongs to the last anchor that starts at or before it — which gives blank lines,
        /// trailing comments and Examples blocks the intuitive owner for free.
        /// </summary>
        private static List<Anchor> BuildAnchors(Feature feature, GherkinDocument document)
        {
            var anchors = new List<Anchor>();

            foreach (object child in feature.Children)
            {
                AddAnchor(anchors, child, ownerRule: null);
            }

            anchors.Sort((a, b) => a.StartLine.CompareTo(b.StartLine));
            ExtendOverLeadingComments(anchors, document);
            return anchors;
        }

        /// <summary>
        /// Pulls each anchor's start up over the comment lines directly above it.
        /// </summary>
        /// <remarks>
        /// Comments are not AST children, so without this a caret parked on the <c># explain this
        /// scenario</c> line immediately above a scenario would resolve to whatever came *before* it —
        /// usually the previous scenario, which is the wrong test to run. A contiguous run of comments
        /// belongs to what it introduces.
        /// </remarks>
        private static void ExtendOverLeadingComments(List<Anchor> anchors, GherkinDocument document)
        {
            if (document.Comments == null || anchors.Count == 0)
            {
                return;
            }

            var commentLines = new HashSet<int>();
            foreach (Comment comment in document.Comments)
            {
                commentLines.Add(comment.Location.Line);
            }

            if (commentLines.Count == 0)
            {
                return;
            }

            for (int i = 0; i < anchors.Count; i++)
            {
                int previousEnd = i == 0 ? 0 : anchors[i - 1].StartLine;
                int start = anchors[i].StartLine;

                while (start - 1 > previousEnd && commentLines.Contains(start - 1))
                {
                    start--;
                }

                anchors[i].StartLine = start;
            }
        }

        private static void AddAnchor(List<Anchor> anchors, object child, Rule? ownerRule)
        {
            if (child is Rule rule)
            {
                // The Rule header itself is an anchor: a caret on `Rule:` (before its first scenario)
                // targets the rule, not the scenario above it.
                anchors.Add(Anchor.ForRule(rule));
                foreach (object grandchild in rule.Children)
                {
                    AddAnchor(anchors, grandchild, rule);
                }

                return;
            }

            if (child is Scenario scenario)
            {
                anchors.Add(Anchor.ForScenario(scenario));
                return;
            }

            // Everything else that carries a location — most importantly Background — anchors to the
            // whole feature (SPEC §6 case 8). A Background declared *inside* a Rule anchors to that
            // rule instead, so a caret there does not escape the rule it is visibly inside.
            if (child is IHasLocation located)
            {
                anchors.Add(ownerRule == null
                    ? Anchor.ForFeatureScope(located.Location.Line)
                    : Anchor.ForRuleScope(located.Location.Line, ownerRule));
            }
        }

        private static ScenarioTarget WholeFeature(Feature feature)
        {
            return new ScenarioTarget(
                TargetKind.Feature,
                feature.Name ?? string.Empty,
                null,
                feature.Location.Line,
                TagNames(feature.Tags));
        }

        private static IReadOnlyList<string> TagNames(IEnumerable<Tag>? tags)
        {
            if (tags == null)
            {
                return new List<string>();
            }

            return tags.Select(t => t.Name).ToList();
        }

        /// <summary>A position in the document that the caret can resolve to.</summary>
        private sealed class Anchor
        {
            private readonly Scenario? _scenario;
            private readonly Rule? _rule;

            private Anchor(int startLine, Scenario? scenario, Rule? rule)
            {
                StartLine = startLine;
                _scenario = scenario;
                _rule = rule;
            }

            /// <summary>
            /// First line that belongs to this anchor — its first tag line when tagged, otherwise its
            /// keyword line, so a caret parked on <c>@smoke</c> runs the scenario below it. Later
            /// extended upwards over any leading comment lines.
            /// </summary>
            public int StartLine { get; set; }

            public static Anchor ForScenario(Scenario scenario)
            {
                return new Anchor(FirstLine(scenario.Location, scenario.Tags), scenario, null);
            }

            public static Anchor ForRule(Rule rule)
            {
                return new Anchor(FirstLine(rule.Location, rule.Tags), null, rule);
            }

            public static Anchor ForFeatureScope(int line)
            {
                return new Anchor(line, null, null);
            }

            /// <summary>A position inside a rule that is not a scenario — the rule's own Background.</summary>
            public static Anchor ForRuleScope(int line, Rule rule)
            {
                return new Anchor(line, null, rule);
            }

            public ScenarioTarget ToTarget(Feature feature, int caretLine)
            {
                if (_scenario != null)
                {
                    // Language-independent: an outline is a scenario that has Examples, whatever the
                    // localized keyword happens to be.
                    bool isOutline = _scenario.Examples != null && _scenario.Examples.Any();

                    if (isOutline)
                    {
                        ExampleRowInfo? row = FindExampleRow(_scenario, caretLine);
                        if (row != null)
                        {
                            // Line stays the OUTLINE's keyword line — it is the join key to the
                            // generated method, and every row shares that one method.
                            return new ScenarioTarget(
                                TargetKind.ExampleRow,
                                feature.Name ?? string.Empty,
                                _scenario.Name ?? string.Empty,
                                _scenario.Location.Line,
                                TagNames(_scenario.Tags),
                                row);
                        }
                    }

                    return new ScenarioTarget(
                        isOutline ? TargetKind.ScenarioOutline : TargetKind.Scenario,
                        feature.Name ?? string.Empty,
                        _scenario.Name ?? string.Empty,
                        _scenario.Location.Line,
                        TagNames(_scenario.Tags));
                }

                if (_rule != null)
                {
                    return new ScenarioTarget(
                        TargetKind.Rule,
                        feature.Name ?? string.Empty,
                        _rule.Name ?? string.Empty,
                        _rule.Location.Line,
                        TagNames(_rule.Tags));
                }

                return WholeFeature(feature);
            }


            /// <summary>
            /// The Examples body row on <paramref name="caretLine"/>, or <see langword="null"/> when
            /// the caret is elsewhere in the outline — including on a header row, which describes
            /// every row rather than any one of them.
            /// </summary>
            private static ExampleRowInfo? FindExampleRow(Scenario scenario, int caretLine)
            {
                int ordinal = 0;

                foreach (Examples examples in scenario.Examples)
                {
                    var columns = new List<string>();
                    if (examples.TableHeader != null)
                    {
                        foreach (TableCell cell in examples.TableHeader.Cells)
                        {
                            columns.Add((cell.Value ?? string.Empty).Trim());
                        }
                    }

                    foreach (TableRow bodyRow in examples.TableBody)
                    {
                        // Counted across every Examples block, because that is the order Reqnroll
                        // generates the rows in.
                        ordinal++;

                        if (bodyRow.Location.Line != caretLine)
                        {
                            continue;
                        }

                        var values = new List<string>();
                        foreach (TableCell cell in bodyRow.Cells)
                        {
                            values.Add((cell.Value ?? string.Empty).Trim());
                        }

                        return new ExampleRowInfo(bodyRow.Location.Line, ordinal, values, columns);
                    }
                }

                return null;
            }

            private static int FirstLine(Location location, IEnumerable<Tag>? tags)
            {
                int first = location.Line;
                if (tags != null)
                {
                    foreach (Tag tag in tags)
                    {
                        if (tag.Location.Line < first)
                        {
                            first = tag.Location.Line;
                        }
                    }
                }

                return first;
            }
        }
    }

    /// <summary>Outcome of resolving a caret position.</summary>
    public sealed class FeatureParseResult
    {
        private FeatureParseResult(bool success, ScenarioTarget? target, string featureName, string? error)
        {
            Success = success;
            Target = target;
            FeatureName = featureName;
            Error = error;
        }

        public bool Success { get; }

        public ScenarioTarget? Target { get; }

        public string FeatureName { get; }

        public string? Error { get; }

        public static FeatureParseResult Ok(ScenarioTarget target, string featureName)
        {
            return new FeatureParseResult(true, target, featureName, null);
        }

        public static FeatureParseResult Fail(string error)
        {
            return new FeatureParseResult(false, null, string.Empty, error);
        }
    }
}
