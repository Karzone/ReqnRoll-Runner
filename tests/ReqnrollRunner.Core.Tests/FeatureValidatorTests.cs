using System.Collections.Generic;
using System.Linq;
using ReqnrollRunner.Core.Model;
using ReqnrollRunner.Core.Validation;
using Xunit;

namespace ReqnrollRunner.Core.Tests
{
    /// <summary>
    /// The validator over <c>tests/fixtures/features/Questionable.feature</c>, which contains one
    /// scenario per defect plus two deliberately clean ones.
    /// </summary>
    public sealed class FeatureValidatorTests
    {
        private readonly FeatureValidator _validator = new FeatureValidator();

        private IReadOnlyList<ValidationDiagnostic> Diagnose(string fixture = "Questionable.feature")
        {
            ValidationResult result = _validator.Validate(Fixtures.Feature(fixture));
            Assert.True(result.Parsed, result.Error);
            return result.Diagnostics;
        }

        private ValidationDiagnostic Single(string code, string scenario)
        {
            return Assert.Single(Diagnose(), d => d.Code == code && d.ScenarioName == scenario);
        }

        [Fact]
        public void Finds_an_Examples_column_no_step_uses()
        {
            ValidationDiagnostic d = Single(FeatureValidator.UnusedExampleColumn, "An unused Examples column");

            Assert.Contains("'b'", d.Message);
            Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
        }

        [Fact]
        public void Finds_a_placeholder_with_no_matching_column()
        {
            ValidationDiagnostic d = Single(FeatureValidator.UndefinedPlaceholder, "A placeholder with no column");

            Assert.Contains("<nonexistent>", d.Message);
            // The message has to say what actually happens, not just that something is wrong.
            Assert.Contains("literal text", d.Message);
        }

        [Fact]
        public void Finds_a_plain_scenario_using_placeholders()
        {
            ValidationDiagnostic d = Single(FeatureValidator.ScenarioWithPlaceholders, "A plain scenario using placeholders");

            Assert.Contains("<a>", d.Message);
            Assert.Contains("Scenario Outline", d.Message);
        }

        [Fact]
        public void Finds_an_outline_that_substitutes_nothing()
        {
            Single(FeatureValidator.OutlineWithoutPlaceholders, "An outline that substitutes nothing");
        }

        [Fact]
        public void An_outline_that_substitutes_nothing_reports_once_not_once_per_column()
        {
            // Every column of such an outline is trivially unused, so the naive implementation
            // reports RR004 plus an RR001 for each column. One mistake, N+1 messages — the fastest
            // way to teach someone to ignore a validator.
            ValidationDiagnostic[] found = Diagnose()
                .Where(d => d.ScenarioName == "An outline that substitutes nothing").ToArray();

            Assert.Single(found);
            Assert.Equal(FeatureValidator.OutlineWithoutPlaceholders, found[0].Code);
        }

        // The two cases most likely to produce a false "unused column" report. Reqnroll substitutes
        // placeholders inside data tables and doc strings too, so a column referenced only there is
        // genuinely used — a validator that only read step text would be wrong here, and wrong in a
        // way that trains people to ignore it.
        [Theory]
        [InlineData("A column used only in a data table")]
        [InlineData("A column used only in a doc string")]
        public void Does_not_report_a_column_used_outside_the_step_text(string scenario)
        {
            Assert.DoesNotContain(Diagnose(), d => d.ScenarioName == scenario);
        }

        [Theory]
        [InlineData("A perfectly good outline")]
        [InlineData("A perfectly good scenario")]
        public void Reports_nothing_for_correct_scenarios(string scenario)
        {
            Assert.DoesNotContain(Diagnose(), d => d.ScenarioName == scenario);
        }

        [Fact]
        public void Reports_every_defect_and_nothing_else()
        {
            // Pins the total. Without this, a change that started reporting a sixth thing — or
            // stopped reporting one — would slip past the per-case tests above.
            IReadOnlyList<ValidationDiagnostic> all = Diagnose();

            Assert.Equal(4, all.Count);
            Assert.Equal(
                new[]
                {
                    FeatureValidator.UnusedExampleColumn,
                    FeatureValidator.UndefinedPlaceholder,
                    FeatureValidator.ScenarioWithPlaceholders,
                    FeatureValidator.OutlineWithoutPlaceholders,
                },
                all.Select(d => d.Code).OrderBy(c => c, System.StringComparer.Ordinal).ToArray());
        }

        [Fact]
        public void Diagnostics_are_ordered_by_line()
        {
            IReadOnlyList<ValidationDiagnostic> all = Diagnose();

            Assert.Equal(all.Select(d => d.Line).OrderBy(l => l).ToArray(), all.Select(d => d.Line).ToArray());
        }

        [Fact]
        public void Every_diagnostic_points_at_a_real_line()
        {
            int lineCount = System.IO.File.ReadAllLines(Fixtures.Feature("Questionable.feature")).Length;

            foreach (ValidationDiagnostic d in Diagnose())
            {
                Assert.InRange(d.Line, 1, lineCount);
            }
        }

        [Theory]
        [InlineData("Calculator.feature")]
        [InlineData("Rechner.de.feature")]
        [InlineData("RichSyntax.feature")]
        public void The_existing_fixtures_are_clean(string fixture)
        {
            // These were written before the validator existed, so they are an independent check that
            // it does not cry wolf on ordinary, correct feature files — including a localized one and
            // one full of doc strings, data tables and rules.
            Assert.Empty(Diagnose(fixture));
        }

        [Fact]
        public void Reports_a_parse_failure_rather_than_pretending_the_file_is_clean()
        {
            ValidationResult result = _validator.Validate(Fixtures.Feature("Malformed.feature"));

            Assert.False(result.Parsed);
            Assert.False(result.IsClean);
            Assert.NotNull(result.Error);
        }

        [Fact]
        public void Reports_a_missing_file()
        {
            ValidationResult result = _validator.Validate(Fixtures.Feature("NoSuchFile.feature"));

            Assert.False(result.Parsed);
            Assert.Contains("not found", result.Error);
        }

        [Fact]
        public void A_file_with_no_feature_header_cannot_be_validated()
        {
            ValidationResult result = _validator.Validate(Fixtures.Feature("NoFeatureHeader.feature"));

            Assert.False(result.Parsed);
        }

        // ---- doc strings ---------------------------------------------------------------------
        //
        // `<name>` is Gherkin's placeholder syntax and also ordinary XML. A doc string is exactly
        // where markup gets pasted, so the two collide there and nowhere else. Reqnroll really does
        // substitute inside doc strings, so they cannot be skipped outright — but a tag is not a
        // placeholder just because it is bracketed, and reporting `<order>` as an undefined column
        // would fire on any payload fixture, which is the fastest way to get a validator switched off.

        [Theory]
        [InlineData("An outline whose doc string contains XML")]
        [InlineData("A plain scenario whose doc string contains XML")]
        public void Xml_in_a_doc_string_is_not_mistaken_for_a_placeholder(string scenario)
        {
            Assert.DoesNotContain(Diagnose("DocStrings.feature"), d => d.ScenarioName == scenario);
        }

        [Fact]
        public void A_column_used_only_inside_a_doc_string_still_counts_as_used()
        {
            // The other half of the same rule. Ignoring doc strings entirely would fix the false
            // positive above by introducing one in the opposite direction.
            Assert.DoesNotContain(
                Diagnose("DocStrings.feature"),
                d => d.ScenarioName == "A column used only in a doc string, alongside XML");
        }

        [Fact]
        public void A_placeholder_in_step_text_is_still_reported_in_a_file_full_of_doc_strings()
        {
            // Vacuity guard: the assertions above would all pass if the validator quietly stopped
            // looking at this file at all.
            ValidationDiagnostic only = Assert.Single(Diagnose("DocStrings.feature"));

            Assert.Equal(FeatureValidator.UndefinedPlaceholder, only.Code);
            Assert.Contains("<nonexistent>", only.Message);
        }
    }
}
