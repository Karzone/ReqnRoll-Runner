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
    }
}
