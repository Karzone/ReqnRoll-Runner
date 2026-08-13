using System.Collections.Generic;
using System.Linq;
using ReqnrollRunner.Core.Execution;
using ReqnrollRunner.Core.Model;
using Xunit;

namespace ReqnrollRunner.Core.Tests
{
    /// <summary>TRX parsing (SPEC §3.4, §6 case 9).</summary>
    public sealed class TrxParserTests
    {
        [Fact]
        public void Reads_a_real_passing_run()
        {
            // passed.trx was produced by actually running samples/SampleCalculator.NUnit.
            IReadOnlyList<TestCaseResult> results = TrxParser.ParseFile(Fixtures.Trx("passed.trx"));

            Assert.All(results, r => Assert.Equal(TestOutcome.Passed, r.Outcome));
            Assert.Equal(11, results.Count);
            Assert.Contains(results, r => r.DisplayName == "AddTwoNumbers");
        }

        [Fact]
        public void Reads_a_real_run_with_a_failure_and_an_undefined_step()
        {
            // mixed-real.trx came from a sample deliberately broken two ways: one wrong expectation
            // and one step with no matching definition.
            IReadOnlyList<TestCaseResult> results = TrxParser.ParseFile(Fixtures.Trx("mixed-real.trx"));

            Assert.Contains(results, r => r.Outcome == TestOutcome.Failed);
            Assert.Contains(results, r => r.Outcome == TestOutcome.Skipped);
            Assert.Contains(results, r => r.Outcome == TestOutcome.Passed);
        }

        [Fact]
        public void Captures_the_error_message_and_stack_trace_of_a_failure()
        {
            TestCaseResult failure = TrxParser.ParseFile(Fixtures.Trx("mixed-real.trx"))
                .First(r => r.Outcome == TestOutcome.Failed);

            Assert.False(string.IsNullOrWhiteSpace(failure.ErrorMessage));
            Assert.False(string.IsNullOrWhiteSpace(failure.StackTrace));
        }

        [Fact]
        public void Joins_each_result_to_its_fully_qualified_name()
        {
            TestCaseResult result = TrxParser.ParseFile(Fixtures.Trx("passed.trx"))
                .First(r => r.DisplayName == "AddTwoNumbers");

            Assert.Equal(
                "SampleCalculator.Features.CalculatorBasicMoreFeature.AddTwoNumbers",
                result.FullyQualifiedName);
        }

        [Fact]
        public void Records_duration()
        {
            IReadOnlyList<TestCaseResult> results = TrxParser.ParseFile(Fixtures.Trx("passed.trx"));

            Assert.Contains(results, r => r.Duration > System.TimeSpan.Zero);
        }

        [Theory]
        [InlineData("Passed", TestOutcome.Passed)]
        [InlineData("Failed", TestOutcome.Failed)]
        [InlineData("Error", TestOutcome.Failed)]
        [InlineData("Timeout", TestOutcome.Failed)]
        [InlineData("Aborted", TestOutcome.Failed)]
        [InlineData("NotExecuted", TestOutcome.Skipped)]
        [InlineData("Skipped", TestOutcome.Skipped)]
        [InlineData("Inconclusive", TestOutcome.Inconclusive)]
        [InlineData("None", TestOutcome.Inconclusive)]
        [InlineData("Pending", TestOutcome.Inconclusive)]
        [InlineData("NotRunnable", TestOutcome.NotExecuted)]
        [InlineData("passed", TestOutcome.Passed)]   // case insensitive
        [InlineData("SomethingNew", TestOutcome.Unknown)]
        [InlineData("", TestOutcome.Unknown)]
        [InlineData(null, TestOutcome.Unknown)]
        public void Maps_every_outcome_string(string? outcome, TestOutcome expected)
        {
            Assert.Equal(expected, TrxParser.ParseOutcome(outcome));
        }

        [Fact]
        public void Parses_a_document_covering_every_outcome()
        {
            IReadOnlyList<TestCaseResult> results = TrxParser.ParseFile(Fixtures.Trx("all-outcomes.trx"));

            Assert.Equal(12, results.Count);
            Assert.Equal(TestOutcome.Inconclusive, Find(results, "InconclusiveScenario").Outcome);
            Assert.Equal(TestOutcome.Skipped, Find(results, "SkippedScenario").Outcome);
            Assert.Equal(TestOutcome.Failed, Find(results, "TimedOutScenario").Outcome);
            Assert.Equal(TestOutcome.NotExecuted, Find(results, "NotRunnableScenario").Outcome);
            Assert.Equal(TestOutcome.Unknown, Find(results, "SomethingNewScenario").Outcome);
        }

        [Fact]
        public void Survives_a_result_with_no_matching_test_definition()
        {
            TestCaseResult orphan = Find(
                TrxParser.ParseFile(Fixtures.Trx("all-outcomes.trx")), "ResultWithNoTestDefinition");

            Assert.Null(orphan.FullyQualifiedName);
            Assert.Equal(TestOutcome.Passed, orphan.Outcome);
        }

        [Fact]
        public void Returns_nothing_for_a_missing_file()
        {
            Assert.Empty(TrxParser.ParseFile(Fixtures.Trx("does-not-exist.trx")));
        }

        [Fact]
        public void An_inconclusive_only_run_counts_as_skipped_not_passed()
        {
            // The point of parsing TRX at all: NUnit's console summary reports these as "Total: 0",
            // so a caller trusting stdout would think nothing ran.
            var results = new List<TestCaseResult>
            {
                new TestCaseResult("A", null, TestOutcome.Inconclusive, System.TimeSpan.Zero, null, null),
            };

            var run = new TestRunResult(results, 0, "filter", System.TimeSpan.Zero, false, null);

            Assert.Equal(0, run.Passed);
            Assert.Equal(0, run.Failed);
            Assert.Equal(1, run.Skipped);
            Assert.True(run.IsSuccess);
        }

        [Fact]
        public void A_run_that_matched_nothing_is_not_a_success()
        {
            var run = new TestRunResult(
                new List<TestCaseResult>(), 1, "filter", System.TimeSpan.Zero, zeroTestsMatched: true, null);

            Assert.False(run.IsSuccess);
        }

        [Fact]
        public void A_run_with_no_results_at_all_is_not_a_success()
        {
            var run = new TestRunResult(
                new List<TestCaseResult>(), 0, "filter", System.TimeSpan.Zero, zeroTestsMatched: false, null);

            Assert.False(run.IsSuccess);
        }

        private static TestCaseResult Find(IReadOnlyList<TestCaseResult> results, string displayName)
        {
            return results.First(r => r.DisplayName == displayName);
        }
    }
}
