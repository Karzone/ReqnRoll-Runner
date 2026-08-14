using ReqnrollRunner.Core.Mapping;
using ReqnrollRunner.Core.Model;
using Xunit;

namespace ReqnrollRunner.Core.Tests
{
    /// <summary>Filter construction and escaping (SPEC §3.3).</summary>
    public sealed class TestFilterBuilderTests
    {
        [Fact]
        public void Builds_a_fully_qualified_name_filter_for_a_scenario()
        {
            TestFilter filter = TestFilterBuilder.ForGeneratedMethod(
                "SampleCalculator.Features.CalculatorBasicMoreFeature", "AddTwoNumbers", TargetKind.Scenario);

            Assert.Equal(
                "FullyQualifiedName~SampleCalculator.Features.CalculatorBasicMoreFeature.AddTwoNumbers",
                filter.Expression);
            Assert.Equal(FilterStrategy.CodeBehind, filter.Strategy);
        }

        [Fact]
        public void An_outline_filters_on_the_method_so_every_example_row_runs()
        {
            // SPEC §3.3: never attempt row-level filtering in v1. One method covers all rows.
            TestFilter filter = TestFilterBuilder.ForGeneratedMethod(
                "Ns.CalculatorFeature", "AddManyAAndB", TargetKind.ScenarioOutline);

            Assert.Equal("FullyQualifiedName~Ns.CalculatorFeature.AddManyAAndB", filter.Expression);
            Assert.Contains("all example rows", filter.Explanation);
        }

        [Fact]
        public void A_whole_feature_filters_on_the_class_only()
        {
            TestFilter filter = TestFilterBuilder.ForFeatureClass(
                "Ns.CalculatorFeature", FilterStrategy.CodeBehind, "because");

            Assert.Equal("FullyQualifiedName~Ns.CalculatorFeature", filter.Expression);
        }

        [Fact]
        public void A_unicode_method_name_needs_no_escaping()
        {
            // "Ünïcödé — スカラー" generates Unicodeスカラー. Non-ASCII is fine in a filter; only the
            // operator characters are not.
            TestFilter filter = TestFilterBuilder.ForGeneratedMethod(
                "Ns.CalculatorFeature", "Unicodeスカラー", TargetKind.Scenario);

            Assert.Equal("FullyQualifiedName~Ns.CalculatorFeature.Unicodeスカラー", filter.Expression);
        }

        [Fact]
        public void Does_not_escape_a_comma()
        {
            // A comma is not a VSTest filter operator, and escaping it makes the filter match
            // NOTHING: `Name~Multiply\, two numbers` selects 0 tests against the sample where
            // `Name~Multiply, two numbers` selects 1. This shipped broken once.
            Assert.Equal("Multiply, two numbers", TestFilterBuilder.Escape("Multiply, two numbers"));
        }

        [Theory]
        [InlineData("plain", "plain")]
        [InlineData("has(parens)", @"has\(parens\)")]
        [InlineData("a&b", @"a\&b")]
        [InlineData("a|b", @"a\|b")]
        [InlineData("a=b", @"a\=b")]
        [InlineData("a!b", @"a\!b")]
        [InlineData("a~b", @"a\~b")]
        [InlineData(@"a\b", @"a\\b")]
        [InlineData("Ivan's (tricky) & odd | title ~ = !", @"Ivan's \(tricky\) \& odd \| title \~ \= \!")]
        public void Escapes_every_filter_operator(string input, string expected)
        {
            Assert.Equal(expected, TestFilterBuilder.Escape(input));
        }

        [Fact]
        public void The_sanitized_fallback_pairs_the_guess_with_the_raw_title()
        {
            // If the reconstructed method name is wrong, MSTest reports the title as Name and xUnit as
            // DisplayName, so the second clause is a real second chance rather than decoration.
            TestFilter filter = TestFilterBuilder.ForSanitizedGuess(
                "Ns.CalculatorFeature", "AddTwoNumbers", "Add two numbers", out bool dropped);

            Assert.False(dropped);
            Assert.Equal(
                "(FullyQualifiedName~Ns.CalculatorFeature.AddTwoNumbers)|(Name~Add two numbers)",
                filter.Expression);
            Assert.Equal(FilterStrategy.Sanitized, filter.Strategy);
        }

        [Theory]
        [InlineData("has \" quote")]
        [InlineData("has \n newline")]
        [InlineData("")]
        [InlineData("   ")]
        public void Drops_the_title_clause_when_the_title_cannot_be_expressed(string title)
        {
            // SPEC §3.3: where a title cannot be safely expressed in a filter, narrow the filter and
            // warn — never emit an expression that might match the wrong test.
            TestFilter filter = TestFilterBuilder.ForSanitizedGuess(
                "Ns.CalculatorFeature", "Guessed", title, out bool dropped);

            Assert.True(dropped);
            Assert.DoesNotContain("|(Name~", filter.Expression);
            Assert.Equal("FullyQualifiedName~Ns.CalculatorFeature.Guessed", filter.Expression);
        }

        [Theory]
        [InlineData("Add two numbers", true)]
        [InlineData("Ivan's (tricky) & odd", true)]
        [InlineData("with \" quote", false)]
        [InlineData(null, false)]
        public void Knows_which_titles_are_safe_to_embed(string? title, bool expected)
        {
            Assert.Equal(expected, TestFilterBuilder.CanExpressAsFilterValue(title!));
        }

        // ---- single example rows -------------------------------------------------------------

        [Fact]
        public void An_MSTest_example_row_matches_its_display_name()
        {
            // Reqnroll emits DisplayName="Add many <a> and <b>(1,2,3,4)" — the row's values followed
            // by its pickle index — so a prefix of "(1,2,3," pins the row without knowing the index.
            TestFilter filter = TestFilterBuilder.ForExampleRow(
                "Ns.CalculatorFeature", "AddManyAAndB", RunnerKind.MsTest,
                new[] { "1", "2", "3" }, 1, out bool widened);

            Assert.False(widened);
            Assert.Contains(@"Name~\(1,2,3,", filter.Expression);
            Assert.Equal(FilterStrategy.CodeBehind, filter.Strategy);
        }

        [Fact]
        public void An_MSTest_example_row_stays_inside_its_own_outline()
        {
            // `Name~` is a substring match over EVERY test in the run, and display names are not
            // unique across outlines: an outline with columns (a, b) and a row `| 1 | 2 |` produces
            // "(1,2,7)", which the prefix "(1,2," from a different outline matches exactly. Without a
            // FullyQualifiedName clause, running one row silently runs rows of unrelated outlines.
            TestFilter filter = TestFilterBuilder.ForExampleRow(
                "Ns.CalculatorFeature", "AddManyAAndB", RunnerKind.MsTest,
                new[] { "1", "2" }, 1, out _);

            Assert.Contains("FullyQualifiedName~Ns.CalculatorFeature.AddManyAAndB", filter.Expression);
            Assert.Contains(@"Name~\(1,2,", filter.Expression);
            Assert.Contains("&", filter.Expression);
        }

        [Theory]
        [InlineData("has \" quote")]
        [InlineData("has \n newline")]
        public void An_unquotable_row_value_widens_rather_than_building_a_broken_filter(string value)
        {
            // Same rule as a scenario title: a value that cannot survive the filter round trip must
            // widen to the whole outline, never produce an expression that might match the wrong test.
            TestFilter filter = TestFilterBuilder.ForExampleRow(
                "Ns.CalculatorFeature", "AddManyAAndB", RunnerKind.MsTest,
                new[] { "1", value }, 2, out bool widened);

            Assert.True(widened);
            Assert.Equal("FullyQualifiedName~Ns.CalculatorFeature.AddManyAAndB", filter.Expression);
            Assert.DoesNotContain("(Name~", filter.Expression);
        }

        [Fact]
        public void An_NUnit_example_row_goes_through_NUnits_own_where_language()
        {
            // NUnit names a row by its arguments — AddManyAAndB("1","2","3","4",null) — which no
            // VSTest --filter operator matches. NUnit's own --where does. Measured against a real
            // build: each of the three rows in the NUnit sample selects exactly itself.
            TestFilter filter = TestFilterBuilder.ForExampleRow(
                "Ns.CalculatorFeature", "AddManyAAndB", RunnerKind.NUnit,
                new[] { "1", "2", "3" }, 1, out bool widened);

            Assert.False(widened);
            Assert.Equal("NUnit.Where=test =~ 'Ns[.]CalculatorFeature[.]AddManyAAndB[(].1.,.2.,.3.,'", filter.RunSettings);
            Assert.Equal(FilterStrategy.CodeBehind, filter.Strategy);
        }

        [Fact]
        public void An_NUnit_row_leaves_the_vstest_filter_empty()
        {
            // --filter and NUnit.Where do NOT intersect: given both, --filter wins and the adapter
            // setting is ignored outright. Measured — the combined form ran all three rows. The
            // where clause carries the class name, so it is safe to be the only selector.
            TestFilter filter = TestFilterBuilder.ForExampleRow(
                "Ns.CalculatorFeature", "AddManyAAndB", RunnerKind.NUnit,
                new[] { "1", "2" }, 1, out _);

            Assert.Equal(string.Empty, filter.Expression);
            Assert.Contains("Ns[.]CalculatorFeature[.]AddManyAAndB", filter.RunSettings);
        }

        [Fact]
        public void An_NUnit_row_never_puts_a_backslash_or_a_quote_in_the_expression()
        {
            // Both would break, for different reasons. A double quote has to survive being embedded
            // in an already-quoted command-line argument, which is shell- and launcher-specific. A
            // backslash is mangled by NUnit's own expression parser before the regex engine sees it:
            // `\(` arrives as `//(` and fails with "Not enough )'s".
            TestFilter filter = TestFilterBuilder.ForExampleRow(
                "Ns.CalculatorFeature", "AddManyAAndB", RunnerKind.NUnit,
                new[] { "a.b", "c(d", "e*f" }, 1, out _);

            Assert.DoesNotContain("\\", filter.RunSettings);
            Assert.DoesNotContain("\"", filter.RunSettings);
            // Metacharacters neutralised as single-character classes instead.
            Assert.Contains("[.]", filter.RunSettings);
            Assert.Contains("[(]", filter.RunSettings);
            Assert.Contains("[*]", filter.RunSettings);
        }

        [Fact]
        public void The_NUnit_row_pattern_is_anchored_so_a_longer_value_cannot_match()
        {
            // The '.' standing in for each quote is only safe because the chain is anchored at the
            // opening bracket and every value is followed by a comma. Values 1 and 2 must not match
            // AddManyAAndB("11","2",…) — after the literal 1 the pattern requires a comma, and a
            // quote is what is actually there.
            TestFilter filter = TestFilterBuilder.ForExampleRow(
                "Ns.F", "M", RunnerKind.NUnit, new[] { "1", "2" }, 1, out _);

            var pattern = new System.Text.RegularExpressions.Regex(
                filter.RunSettings!.Substring(filter.RunSettings.IndexOf('\'') + 1).TrimEnd('\''));

            Assert.Matches(pattern, "Ns.F.M(\"1\",\"2\",\"7\",null)");
            Assert.DoesNotMatch(pattern, "Ns.F.M(\"11\",\"2\",\"7\",null)");
            Assert.DoesNotMatch(pattern, "Ns.F.M(\"1\",\"22\",\"7\",null)");
            Assert.DoesNotMatch(pattern, "Other.F.M(\"1\",\"2\",\"7\",null)");
        }

        [Theory]
        [InlineData(RunnerKind.XUnit)]
        [InlineData(RunnerKind.Unknown)]
        public void Runners_that_cannot_select_a_row_widen_to_the_whole_outline(RunnerKind runner)
        {
            // xUnit names rows distinctly enough to filter, but the display name embeds the
            // generated PARAMETER names, which are not in the feature file. Issue #14.
            TestFilter filter = TestFilterBuilder.ForExampleRow(
                "Ns.CalculatorFeature", "AddManyAAndB", runner, new[] { "1", "2", "3" }, 2, out bool widened);

            Assert.True(widened);
            Assert.Equal("FullyQualifiedName~Ns.CalculatorFeature.AddManyAAndB", filter.Expression);
            Assert.Equal(FilterStrategy.FeatureScopeFallback, filter.Strategy);
            // The explanation is shown to the user as a warning, so it has to name the row that was
            // asked for and say plainly that something else is happening.
            Assert.Contains("row 2", filter.Explanation);
        }

        [Theory]
        [InlineData(RunnerKind.NUnit, true)]
        [InlineData(RunnerKind.MsTest, true)]
        [InlineData(RunnerKind.XUnit, false)]
        [InlineData(RunnerKind.Unknown, false)]
        public void Knows_which_runners_can_select_a_row(RunnerKind runner, bool expected)
        {
            // The VS command labels itself from this, so it must agree with ForExampleRow exactly —
            // a menu that offers "Run Example Row 2" and then runs three tests is a broken promise.
            Assert.Equal(expected, TestFilterBuilder.CanSelectSingleRow(runner));

            TestFilterBuilder.ForExampleRow(
                "Ns.F", "M", runner, new[] { "1" }, 1, out bool widened);
            Assert.Equal(expected, !widened);
        }

        [Fact]
        public void A_row_with_no_values_widens_rather_than_matching_every_row()
        {
            // "(" alone is a prefix of every parameterised display name in the project.
            TestFilter filter = TestFilterBuilder.ForExampleRow(
                "Ns.CalculatorFeature", "AddManyAAndB", RunnerKind.MsTest,
                new string[0], 1, out bool widened);

            Assert.True(widened);
            Assert.Equal("FullyQualifiedName~Ns.CalculatorFeature.AddManyAAndB", filter.Expression);
            Assert.DoesNotContain("(Name~", filter.Expression);
        }
    }
}
