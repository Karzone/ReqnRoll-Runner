using System.Collections.Generic;
using System.IO;
using System.Linq;
using ReqnrollRunner.Core.Model;
using ReqnrollRunner.Core.Parsing;
using Xunit;

namespace ReqnrollRunner.Core.Tests
{
    /// <summary>
    /// Listing scenarios from text rather than a path — what the editor adornments need, since they
    /// must reflect unsaved edits.
    /// </summary>
    /// <remarks>
    /// The adornments themselves cannot be tested outside Visual Studio. Putting the parsing in Core
    /// means the half that decides *where* the Run/Debug links appear is covered here, leaving only
    /// the drawing unverified.
    /// </remarks>
    public sealed class FeatureOutlineTests
    {
        private static string Text(string fixture) => File.ReadAllText(Fixtures.Feature(fixture));

        [Fact]
        public void Lists_every_scenario_with_its_line_and_kind()
        {
            IReadOnlyList<FeatureOutlineEntry> entries = FeatureOutline.Parse(Text("Calculator.feature"));

            Assert.Equal(6, entries.Count);
            Assert.Equal(new[] { 10, 16, 22, 28, 34, 52 }, entries.Select(e => e.Line).ToArray());
            Assert.Equal("Add two numbers", entries[0].Name);
            Assert.Equal(TargetKind.Scenario, entries[0].Kind);
        }

        [Fact]
        public void Distinguishes_an_outline_from_a_scenario()
        {
            IReadOnlyList<FeatureOutlineEntry> entries = FeatureOutline.Parse(Text("Calculator.feature"));

            Assert.Equal(TargetKind.ScenarioOutline, entries.Single(e => e.Line == 34).Kind);
            Assert.Equal(TargetKind.Scenario, entries.Single(e => e.Line == 10).Kind);
        }

        [Fact]
        public void Includes_scenarios_nested_in_a_rule()
        {
            // Line 52 is inside `Rule: Subtraction has its own rule block`. An adornment must appear
            // there too.
            Assert.Contains(FeatureOutline.Parse(Text("Calculator.feature")), e => e.Line == 52);
        }

        [Fact]
        public void Works_on_a_localized_feature()
        {
            IReadOnlyList<FeatureOutlineEntry> entries = FeatureOutline.Parse(Text("Rechner.de.feature"));

            Assert.Equal(new[] { 7, 13 }, entries.Select(e => e.Line).ToArray());
            Assert.Equal(TargetKind.ScenarioOutline, entries[1].Kind);
        }

        [Fact]
        public void Agrees_with_the_caret_parser()
        {
            // Cross-check: every line FeatureOutline reports must be one the caret parser also
            // resolves to a scenario at that same line. Two independent readers of the same file
            // that disagree would put Run links on the wrong lines.
            var parser = new FeatureFileParser();

            foreach (FeatureOutlineEntry entry in FeatureOutline.Parse(Text("Calculator.feature")))
            {
                FeatureParseResult resolved = parser.Resolve(Fixtures.Feature("Calculator.feature"), entry.Line);

                Assert.True(resolved.Success, resolved.Error);
                Assert.Equal(entry.Line, resolved.Target!.Line);
                Assert.Equal(entry.Name, resolved.Target.Name);
            }
        }

        // A file being typed into is not valid Gherkin most of the time. The adornment layer calls
        // this on every buffer change, so anything other than "return nothing" would either throw
        // into the editor or leave stale links on screen.
        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("Feature:")]
        [InlineData("Scenario: orphaned, with no Feature header")]
        [InlineData("Feature: one\nFeature: two")]
        [InlineData("this is not gherkin at all")]
        public void Returns_nothing_rather_than_throwing_on_unparseable_text(string text)
        {
            IReadOnlyList<FeatureOutlineEntry> entries = FeatureOutline.Parse(text);

            Assert.NotNull(entries);
        }

        [Fact]
        public void Handles_a_half_typed_scenario()
        {
            // Mid-keystroke: the keyword is there but the file is incomplete.
            IReadOnlyList<FeatureOutlineEntry> entries = FeatureOutline.Parse("Feature: f\n\nScenario: ha");

            Assert.Single(entries);
            Assert.Equal("ha", entries[0].Name);
        }

        [Fact]
        public void ScenarioLines_matches_Parse()
        {
            string text = Text("Calculator.feature");

            Assert.Equal(
                FeatureOutline.Parse(text).Select(e => e.Line).ToArray(),
                FeatureOutline.ScenarioLines(text).ToArray());
        }
    }
}
