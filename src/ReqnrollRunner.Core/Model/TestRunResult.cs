using System;
using System.Collections.Generic;

namespace ReqnrollRunner.Core.Model
{
    /// <summary>Outcome of one generated test, as reported by the TRX logger.</summary>
    public enum TestOutcome
    {
        /// <summary>TRX carried an outcome we do not model.</summary>
        Unknown = 0,
        Passed = 1,
        Failed = 2,
        /// <summary>Explicitly skipped / ignored.</summary>
        Skipped = 3,
        /// <summary>
        /// NUnit's "None"/inconclusive — what an undefined-step scenario produces. Distinct from
        /// <see cref="Skipped"/> because it usually means the step definitions are missing, not that
        /// the author disabled the test.
        /// </summary>
        Inconclusive = 4,
        /// <summary>Discovered but never run (e.g. an aborted run).</summary>
        NotExecuted = 5,
    }

    /// <summary>One test result parsed out of a TRX file.</summary>
    public sealed class TestCaseResult
    {
        public TestCaseResult(
            string displayName,
            string? fullyQualifiedName,
            TestOutcome outcome,
            TimeSpan duration,
            string? errorMessage,
            string? stackTrace)
        {
            DisplayName = displayName;
            FullyQualifiedName = fullyQualifiedName;
            Outcome = outcome;
            Duration = duration;
            ErrorMessage = errorMessage;
            StackTrace = stackTrace;
        }

        /// <summary>What the runner calls it — for MSTest this is the original scenario title.</summary>
        public string DisplayName { get; }

        /// <summary><c>Namespace.FeatureClass.Method</c>, when the TRX carried a test definition for it.</summary>
        public string? FullyQualifiedName { get; }

        public TestOutcome Outcome { get; }

        public TimeSpan Duration { get; }

        public string? ErrorMessage { get; }

        public string? StackTrace { get; }
    }

    /// <summary>Everything a caller needs to report on a completed <c>dotnet test</c> invocation.</summary>
    public sealed class TestRunResult
    {
        public TestRunResult(
            IReadOnlyList<TestCaseResult> results,
            int exitCode,
            string filterUsed,
            TimeSpan duration,
            bool zeroTestsMatched,
            string? failureReason)
        {
            Results = results;
            ExitCode = exitCode;
            FilterUsed = filterUsed;
            Duration = duration;
            ZeroTestsMatched = zeroTestsMatched;
            FailureReason = failureReason;
        }

        public IReadOnlyList<TestCaseResult> Results { get; }

        public int ExitCode { get; }

        /// <summary>Echoed back so a zero-match message can always show the exact filter.</summary>
        public string FilterUsed { get; }

        public TimeSpan Duration { get; }

        /// <summary>
        /// True when VSTest reported that the filter matched nothing. This is checked from the
        /// process output rather than inferred from counts: NUnit reports inconclusive tests as
        /// <c>Total: 0</c> in its summary line even though tests really did run.
        /// </summary>
        public bool ZeroTestsMatched { get; }

        /// <summary>Set when the run could not even start (build error, <c>dotnet</c> missing, cancelled).</summary>
        public string? FailureReason { get; }

        public int Passed => Count(TestOutcome.Passed);

        public int Failed => Count(TestOutcome.Failed);

        public int Skipped => Count(TestOutcome.Skipped) + Count(TestOutcome.Inconclusive) + Count(TestOutcome.NotExecuted);

        /// <summary>
        /// True when nothing failed and the run itself started cleanly. An empty result set is NOT
        /// success — a filter that matched nothing is the single most common failure mode.
        /// </summary>
        public bool IsSuccess =>
            FailureReason == null && !ZeroTestsMatched && Failed == 0 && Results.Count > 0;

        private int Count(TestOutcome outcome)
        {
            int n = 0;
            foreach (TestCaseResult r in Results)
            {
                if (r.Outcome == outcome)
                {
                    n++;
                }
            }

            return n;
        }
    }
}
