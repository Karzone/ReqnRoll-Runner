using ReqnrollRunner.Core.Model;
using ReqnrollRunner.Core.Parsing;
using Xunit;

namespace ReqnrollRunner.Core.Tests
{
    /// <summary>Caret position → target resolution (SPEC §3.1).</summary>
    public sealed class FeatureFileParserTests
    {
        private readonly FeatureFileParser _parser = new FeatureFileParser();

        // Line numbers refer to tests/fixtures/features/Calculator.feature:
        //   1 Feature | 6 Background | 9 @smoke | 10 Scenario Add two numbers
        //   16 Multiply | 22 Ivan's | 28 Ünïcödé | 34 Scenario Outline | 41 Examples | 46 @extra
        //   50 Rule | 52 Scenario Subtract inside a rule
        [Theory]
        [InlineData(1, TargetKind.Feature, null)]                                  // the Feature: line
        [InlineData(3, TargetKind.Feature, null)]                                  // free-text description
        [InlineData(6, TargetKind.Feature, null)]                                  // Background — SPEC §6 case 8
        [InlineData(7, TargetKind.Feature, null)]                                  // a Background step
        [InlineData(9, TargetKind.Scenario, "Add two numbers")]                    // the @smoke tag line
        [InlineData(10, TargetKind.Scenario, "Add two numbers")]                   // the Scenario: line
        [InlineData(13, TargetKind.Scenario, "Add two numbers")]                   // a step
        [InlineData(22, TargetKind.Scenario, "Ivan's \"quoted\" (tricky) & odd | title ~ = !")]
        [InlineData(28, TargetKind.Scenario, "Ünïcödé — スカラー")]
        [InlineData(34, TargetKind.ScenarioOutline, "Add many <a> and <b>")]       // the Scenario Outline: line
        [InlineData(41, TargetKind.ScenarioOutline, "Add many <a> and <b>")]       // inside the first Examples
        [InlineData(46, TargetKind.ScenarioOutline, "Add many <a> and <b>")]       // inside the second Examples
        [InlineData(50, TargetKind.Rule, "Subtraction has its own rule block")]    // the Rule: line
        [InlineData(52, TargetKind.Scenario, "Subtract inside a rule")]            // a scenario inside a Rule
        [InlineData(500, TargetKind.Scenario, "Subtract inside a rule")]           // past the end of the file
        public void Resolves_the_caret_to_the_right_target(int line, TargetKind expectedKind, string? expectedName)
        {
            FeatureParseResult result = _parser.Resolve(Fixtures.Feature("Calculator.feature"), line);

            Assert.True(result.Success, result.Error);
            Assert.Equal(expectedKind, result.Target!.Kind);
            Assert.Equal(expectedName, result.Target.Name);
        }

        [Fact]
        public void Reports_the_scenario_keyword_line_not_the_tag_line()
        {
            // The join to the generated code-behind is by scenario keyword line, so a caret on the
            // @smoke tag must still report line 10, not 9.
            FeatureParseResult result = _parser.Resolve(Fixtures.Feature("Calculator.feature"), 9);

            Assert.Equal(10, result.Target!.Line);
        }

        [Fact]
        public void Reads_tags_declared_on_the_scenario()
        {
            FeatureParseResult result = _parser.Resolve(Fixtures.Feature("Calculator.feature"), 10);

            Assert.Equal(new[] { "@smoke" }, result.Target!.Tags);
        }

        // SPEC §6 case 4 — a non-English feature file. Lines in Rechner.de.feature:
        //   1 # language: de | 2 Funktionalität | 4 Grundlage | 7 Szenario
        //   13 Szenariogrundriss | 19 Beispiele
        [Theory]
        [InlineData(2, TargetKind.Feature, null)]
        [InlineData(5, TargetKind.Feature, null)]                                   // Grundlage (Background)
        [InlineData(7, TargetKind.Scenario, "Zwei Zahlen addieren")]
        [InlineData(11, TargetKind.Scenario, "Zwei Zahlen addieren")]
        [InlineData(13, TargetKind.ScenarioOutline, "Mehrere Zahlen addieren")]
        [InlineData(19, TargetKind.ScenarioOutline, "Mehrere Zahlen addieren")]     // the Beispiele: keyword
        [InlineData(20, TargetKind.ScenarioOutline, "Mehrere Zahlen addieren")]     // the header row
        public void Handles_localized_keywords(int line, TargetKind expectedKind, string? expectedName)
        {
            FeatureParseResult result = _parser.Resolve(Fixtures.Feature("Rechner.de.feature"), line);

            Assert.True(result.Success, result.Error);
            Assert.Equal(expectedKind, result.Target!.Kind);
            Assert.Equal(expectedName, result.Target.Name);
        }

        [Fact]
        public void An_outline_is_detected_without_matching_the_localized_keyword()
        {
            // "Szenariogrundriss" shares no words with "Scenario Outline"; detection is by the
            // presence of Examples, which is language independent.
            FeatureParseResult result = _parser.Resolve(Fixtures.Feature("Rechner.de.feature"), 13);

            Assert.Equal(TargetKind.ScenarioOutline, result.Target!.Kind);
        }

        // RichSyntax.feature: 1 @feature-level | 2 Feature | 7 Background | 11 @slow @wip
        // 12 data-table scenario | 19 doc-string scenario | 29 Rule | 31 rule Background
        // 34 First inside the rule | 37 Second inside the rule
        [Theory]
        [InlineData(4, TargetKind.Feature, null)]                              // description text
        [InlineData(8, TargetKind.Feature, null)]                              // Background step
        [InlineData(10, TargetKind.Scenario, "A scenario with a data table")]  // a comment above the tags
        [InlineData(15, TargetKind.Scenario, "A scenario with a data table")]  // inside the data table
        [InlineData(23, TargetKind.Scenario, "A scenario with a doc string")]  // a "Scenario" line INSIDE a doc string
        [InlineData(29, TargetKind.Rule, "A rule with two scenarios")]
        [InlineData(32, TargetKind.Rule, "A rule with two scenarios")]         // the rule-level Background
        [InlineData(34, TargetKind.Scenario, "First inside the rule")]
        [InlineData(37, TargetKind.Scenario, "Second inside the rule")]
        public void Handles_doc_strings_data_tables_and_nested_rules(int line, TargetKind expectedKind, string? expectedName)
        {
            FeatureParseResult result = _parser.Resolve(Fixtures.Feature("RichSyntax.feature"), line);

            Assert.True(result.Success, result.Error);
            Assert.Equal(expectedKind, result.Target!.Kind);
            Assert.Equal(expectedName, result.Target.Name);
        }

        [Fact]
        public void A_keyword_inside_a_doc_string_is_not_a_scenario()
        {
            // Line 23 is `"Scenario": "this line looks like a keyword..."` inside a JSON doc string.
            // Hand-rolled keyword matching would break here; the Gherkin grammar does not.
            FeatureParseResult result = _parser.Resolve(Fixtures.Feature("RichSyntax.feature"), 23);

            Assert.Equal("A scenario with a doc string", result.Target!.Name);
        }


        // Examples rows — a caret on a body row resolves to that row, not the whole outline.
        // Calculator.feature: 40 Examples:, 41 header, 42-43 rows; 46 Examples:, 47 header, 48 row.
        [Theory]
        [InlineData(42, 1)]
        [InlineData(43, 2)]
        [InlineData(48, 3)]   // second Examples block; the ordinal keeps counting across blocks
        public void Resolves_a_caret_on_an_Examples_row_to_that_row(int line, int expectedOrdinal)
        {
            FeatureParseResult result = _parser.Resolve(Fixtures.Feature("Calculator.feature"), line);

            Assert.Equal(TargetKind.ExampleRow, result.Target!.Kind);
            Assert.NotNull(result.Target.ExampleRow);
            Assert.Equal(expectedOrdinal, result.Target.ExampleRow!.OrdinalWithinOutline);
            Assert.Equal(line, result.Target.ExampleRow.Line);
        }

        [Fact]
        public void A_row_target_still_reports_the_outlines_line_as_its_join_key()
        {
            // Line stays the OUTLINE's keyword line even for a row, because that is what joins to the
            // generated code-behind — every row of an outline shares one generated method.
            FeatureParseResult result = _parser.Resolve(Fixtures.Feature("Calculator.feature"), 42);

            Assert.Equal(34, result.Target!.Line);
            Assert.Equal(42, result.Target.ExampleRow!.Line);
        }

        [Fact]
        public void A_row_target_carries_its_values_and_columns()
        {
            FeatureParseResult result = _parser.Resolve(Fixtures.Feature("Calculator.feature"), 42);

            Assert.Equal(new[] { "1", "2", "3" }, result.Target!.ExampleRow!.Values);
            Assert.Equal(new[] { "a", "b", "result" }, result.Target.ExampleRow.Columns);
        }

        [Theory]
        [InlineData(40)]   // the Examples: keyword
        [InlineData(41)]   // the header row — describes every row, not any one of them
        public void A_caret_on_the_Examples_header_stays_on_the_whole_outline(int line)
        {
            FeatureParseResult result = _parser.Resolve(Fixtures.Feature("Calculator.feature"), line);

            Assert.Equal(TargetKind.ScenarioOutline, result.Target!.Kind);
            Assert.Null(result.Target.ExampleRow);
        }

        [Fact]
        public void Resolves_rows_in_a_localized_feature_too()
        {
            FeatureParseResult result = _parser.Resolve(Fixtures.Feature("Rechner.de.feature"), 21);

            Assert.Equal(TargetKind.ExampleRow, result.Target!.Kind);
            Assert.Equal(1, result.Target.ExampleRow!.OrdinalWithinOutline);
        }

        [Fact]
        public void Reports_a_parse_error_rather_than_guessing()
        {
            FeatureParseResult result = _parser.Resolve(Fixtures.Feature("Malformed.feature"), 3);

            Assert.False(result.Success);
            Assert.Contains("Malformed.feature", result.Error);
        }

        [Fact]
        public void Reports_a_file_with_no_Feature_header()
        {
            FeatureParseResult result = _parser.Resolve(Fixtures.Feature("NoFeatureHeader.feature"), 1);

            Assert.False(result.Success);
            Assert.Contains("no Feature: header", result.Error);
        }

        [Fact]
        public void Reports_a_missing_file()
        {
            FeatureParseResult result = _parser.Resolve(Fixtures.Feature("DoesNotExist.feature"), 1);

            Assert.False(result.Success);
            Assert.Contains("not found", result.Error);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-42)]
        public void Clamps_a_nonsensical_line_number_instead_of_throwing(int line)
        {
            FeatureParseResult result = _parser.Resolve(Fixtures.Feature("Calculator.feature"), line);

            Assert.True(result.Success);
            Assert.Equal(TargetKind.Feature, result.Target!.Kind);
        }
    }
}
