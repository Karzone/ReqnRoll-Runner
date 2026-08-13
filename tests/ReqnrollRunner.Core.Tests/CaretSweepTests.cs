using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gherkin.Ast;
using ReqnrollRunner.Core.Model;
using ReqnrollRunner.Core.Parsing;
using Xunit;

namespace ReqnrollRunner.Core.Tests
{
    /// <summary>
    /// Property tests that sweep <em>every</em> line of a feature file rather than spot-checking a
    /// handful.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="FeatureFileParserTests"/> asserts about thirty hand-picked positions, which is only
    /// as good as the positions someone thought to pick. These tests instead state properties that
    /// must hold at every line, and check them across the whole file.
    /// </para>
    /// <para>
    /// The important one is <see cref="Every_line_inside_a_scenario_resolves_to_that_scenario"/>. It
    /// derives each scenario's span from the <em>node locations in the Gherkin AST</em> — a different
    /// computation from the ordered-anchor list the parser uses internally — so the two have to agree.
    /// A differential test, not a restatement of the implementation.
    /// </para>
    /// </remarks>
    public sealed class CaretSweepTests
    {
        private readonly FeatureFileParser _parser = new FeatureFileParser();

        public static TheoryData<string> Features => new TheoryData<string>
        {
            "Calculator.feature",
            "Rechner.de.feature",
            "RichSyntax.feature",
        };

        [Theory]
        [MemberData(nameof(Features))]
        public void Every_line_inside_a_scenario_resolves_to_that_scenario(string fixture)
        {
            string path = Fixtures.Feature(fixture);
            int asserted = 0;
            int scenarios = 0;

            foreach (Scenario scenario in ScenariosIn(path))
            {
                scenarios++;
                int start = scenario.Location.Line;
                int end = LastLineOf(scenario);

                for (int line = start; line <= end; line++)
                {
                    FeatureParseResult result = _parser.Resolve(path, line);

                    Assert.True(result.Success, result.Error);
                    Assert.True(
                        result.Target!.Line == start,
                        $"{fixture} line {line} is inside '{scenario.Name}' (lines {start}-{end}) " +
                        $"but resolved to line {result.Target.Line} ('{result.Target.Name}').");

                    asserted++;
                }
            }

            // Vacuity guard: a fixture that stopped parsing would sweep zero lines and pass silently.
            // Every scenario is at least a keyword line plus a step, so twice the count is a floor
            // that means something rather than an arbitrary number.
            Assert.True(scenarios > 0, $"Found no scenarios at all in {fixture}.");
            Assert.True(
                asserted >= scenarios * 2,
                $"Swept only {asserted} lines across {scenarios} scenarios in {fixture}.");
        }

        [Theory]
        [MemberData(nameof(Features))]
        public void The_resolved_target_never_moves_backwards_as_the_caret_moves_down(string fixture)
        {
            // Monotonicity. The caret belongs to the last anchor at or before it, so walking down the
            // file can only ever move the target down too. Catches ordering and off-by-one bugs
            // anywhere in the anchor list, including in the gaps between scenarios that the span
            // sweep above deliberately does not assert on.
            string path = Fixtures.Feature(fixture);
            int lineCount = File.ReadAllLines(path).Length;
            int previous = 0;

            for (int line = 1; line <= lineCount; line++)
            {
                FeatureParseResult result = _parser.Resolve(path, line);

                Assert.True(result.Success, result.Error);
                Assert.True(
                    result.Target!.Line >= previous,
                    $"{fixture} line {line} resolved to line {result.Target.Line}, " +
                    $"which is above the previous line's target ({previous}).");

                previous = result.Target.Line;
            }
        }

        [Theory]
        [MemberData(nameof(Features))]
        public void A_caret_only_reaches_forward_across_tags_and_comments(string fixture)
        {
            // A caret CAN resolve to a scenario below it — that is deliberate, so that parking on
            // `@smoke` or on the `# explain this` comment above a scenario runs that scenario. What
            // must never happen is reaching forward across real content, which would mean the lead-in
            // had swallowed the tail of the scenario above.
            string path = Fixtures.Feature(fixture);
            string[] lines = File.ReadAllLines(path);

            for (int line = 1; line <= lines.Length; line++)
            {
                ScenarioTarget target = _parser.Resolve(path, line).Target!;

                if (target.Line <= line)
                {
                    continue;
                }

                for (int between = line; between < target.Line; between++)
                {
                    string text = lines[between - 1].Trim();

                    Assert.True(
                        text.Length == 0 || text.StartsWith("#") || text.StartsWith("@"),
                        $"{fixture} line {line} reached forward to line {target.Line} " +
                        $"('{target.Name}') across line {between}, which is content: '{text}'.");
                }
            }
        }

        [Theory]
        [MemberData(nameof(Features))]
        public void The_tags_and_comments_above_a_scenario_belong_to_it(string fixture)
        {
            // The mirror image of the forward-reach test: that one proves we never reach forward
            // across content, this one proves we DO reach forward across the lead-in. Without it a
            // regression that ignored tag lines entirely would slip through the sweep — verified by
            // mutating FeatureFileParser to do exactly that.
            string path = Fixtures.Feature(fixture);
            string[] lines = File.ReadAllLines(path);
            GherkinDocument document = new Gherkin.Parser().Parse(path);

            var commentLines = new HashSet<int>(document.Comments.Select(c => c.Location.Line));
            int checkedLines = 0;

            foreach (Scenario scenario in ScenariosIn(path))
            {
                int keyword = scenario.Location.Line;
                int leadIn = keyword;

                foreach (Tag tag in scenario.Tags)
                {
                    leadIn = System.Math.Min(leadIn, tag.Location.Line);
                }

                // …then up over any contiguous comment lines directly above the tags.
                while (leadIn > 1 && commentLines.Contains(leadIn - 1))
                {
                    leadIn--;
                }

                for (int line = leadIn; line < keyword; line++)
                {
                    ScenarioTarget target = _parser.Resolve(path, line).Target!;

                    Assert.True(
                        target.Line == keyword,
                        $"{fixture} line {line} is the lead-in for '{scenario.Name}' (line {keyword}) " +
                        $"but resolved to line {target.Line} ('{target.Name}'). Content: '{lines[line - 1].Trim()}'.");

                    checkedLines++;
                }
            }

            // Only Calculator and RichSyntax carry tags or comments above a scenario; the German
            // fixture legitimately has none, so this is reported rather than asserted.
            Assert.True(checkedLines >= 0);
        }

        [Fact]
        public void The_lead_in_sweep_actually_covers_something()
        {
            // Vacuity guard for the test above: if no fixture had tags or comments above a scenario,
            // it would pass without checking a single line.
            string path = Fixtures.Feature("Calculator.feature");

            // Line 9 is `@smoke`, directly above `Scenario: Add two numbers` on line 10.
            Assert.Equal(10, _parser.Resolve(path, 9).Target!.Line);

            // RichSyntax line 10 is a comment above the tags on 11, above the scenario on 12.
            Assert.Equal(12, _parser.Resolve(Fixtures.Feature("RichSyntax.feature"), 10).Target!.Line);
        }

        [Theory]
        [MemberData(nameof(Features))]
        public void Every_scenario_in_the_file_is_reachable(string fixture)
        {
            // Guards against a scenario that no caret position can ever select — the failure mode
            // where a whole scenario is silently unrunnable.
            string path = Fixtures.Feature(fixture);
            int lineCount = File.ReadAllLines(path).Length;

            var reachable = new HashSet<int>();
            for (int line = 1; line <= lineCount; line++)
            {
                reachable.Add(_parser.Resolve(path, line).Target!.Line);
            }

            foreach (Scenario scenario in ScenariosIn(path))
            {
                Assert.True(
                    reachable.Contains(scenario.Location.Line),
                    $"No caret position in {fixture} resolves to '{scenario.Name}' (line {scenario.Location.Line}).");
            }
        }

        [Fact]
        public void Every_scenario_maps_to_a_distinct_generated_method()
        {
            // The end-to-end version of the same idea: sweep the whole feature through the real
            // mapper and check that no two scenarios collide onto one method — which would silently
            // run the wrong test.
            string feature = Fixtures.Project("NUnitProject", "Features", "Calculator.feature");
            var mapper = new Core.Mapping.ScenarioMapper();

            var methods = new Dictionary<string, string>();

            foreach (Scenario scenario in ScenariosIn(feature))
            {
                MappingResult result = mapper.Map(feature, scenario.Location.Line);

                Assert.True(result.Success, result.Error);
                Assert.NotNull(result.GeneratedMethodName);

                Assert.False(
                    methods.ContainsKey(result.GeneratedMethodName!),
                    $"'{scenario.Name}' and '{methods.GetValueOrDefault(result.GeneratedMethodName!)}' " +
                    $"both map to {result.GeneratedMethodName}.");

                methods[result.GeneratedMethodName!] = scenario.Name ?? string.Empty;
            }

            Assert.Equal(6, methods.Count);
        }

        /// <summary>Every scenario in the document, including those nested inside a <c>Rule</c>.</summary>
        private static IEnumerable<Scenario> ScenariosIn(string path)
        {
            GherkinDocument document = new Gherkin.Parser().Parse(path);

            foreach (object child in document.Feature.Children)
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

        /// <summary>
        /// The last line the scenario itself owns, taken from its own child nodes' locations — its
        /// steps, its Examples headers, and every example table row.
        /// </summary>
        private static int LastLineOf(Scenario scenario)
        {
            int last = scenario.Location.Line;

            foreach (Step step in scenario.Steps)
            {
                last = System.Math.Max(last, step.Location.Line);

                // A doc string or data table extends the step past its own keyword line.
                if (step.DocString != null)
                {
                    // The AST records only the opening delimiter's line, so the closing delimiter is
                    // that plus the content lines plus one. Getting this right is what makes the
                    // sweep cover the lines inside a doc string — the ones that look like Gherkin
                    // keywords but are not.
                    int contentLines = step.DocString.Content.Length == 0
                        ? 0
                        : step.DocString.Content.Split('\n').Length;

                    last = System.Math.Max(last, step.DocString.Location.Line + contentLines + 1);
                }

                if (step.DataTable != null)
                {
                    foreach (TableRow row in step.DataTable.Rows)
                    {
                        last = System.Math.Max(last, row.Location.Line);
                    }
                }
            }

            foreach (Examples examples in scenario.Examples)
            {
                last = System.Math.Max(last, examples.Location.Line);

                if (examples.TableHeader != null)
                {
                    last = System.Math.Max(last, examples.TableHeader.Location.Line);
                }

                foreach (TableRow row in examples.TableBody)
                {
                    last = System.Math.Max(last, row.Location.Line);
                }
            }

            return last;
        }
    }
}
