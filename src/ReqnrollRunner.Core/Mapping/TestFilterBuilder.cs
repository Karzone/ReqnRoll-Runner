using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ReqnrollRunner.Core.Model;

namespace ReqnrollRunner.Core.Mapping
{
    /// <summary>
    /// Turns a resolved target plus known generated names into a VSTest <c>--filter</c> expression.
    /// </summary>
    /// <remarks>
    /// <para>
    /// v1 filters on <c>FullyQualifiedName</c> for every runner. That is a deliberate simplification
    /// of the per-runner strategies sketched in SPEC §3.3, and it is backed by measurement rather
    /// than assumption: running the same feature under Reqnroll.NUnit, Reqnroll.xUnit and
    /// Reqnroll.MsTest showed all three produce the identical
    /// <c>&lt;Namespace&gt;.&lt;FeatureClass&gt;.&lt;Method&gt;</c> FQN, and a <c>FullyQualifiedName~</c>
    /// filter selected exactly the intended 1 / 3 / 9 tests in each.
    /// </para>
    /// <para>
    /// This also dissolves the MSTest display-name problem the spec flags. MSTest emits
    /// <c>[TestMethod("Add two numbers")]</c>, so <c>Name</c> is the original title there and the
    /// sanitized method name elsewhere — but <c>FullyQualifiedName</c> is the method either way, so
    /// we never have to care. The <c>Name</c>/<c>DisplayName</c> alternation is kept only for the
    /// sanitized fallback, where the reconstructed method name might be wrong and the raw title is a
    /// useful second chance.
    /// </para>
    /// </remarks>
    public static class TestFilterBuilder
    {
        /// <summary>
        /// Characters VSTest treats as filter operators; a literal one must be backslash-escaped.
        /// </summary>
        /// <remarks>
        /// Note the absence of the comma. It was originally included as a defensive measure and that
        /// was wrong: a comma is NOT an operator, and escaping it makes the filter match nothing —
        /// <c>Name~Multiply\, two numbers</c> selects 0 tests where <c>Name~Multiply, two numbers</c>
        /// selects 1. Measured against a real run; do not "tidy" it back in.
        /// </remarks>
        private static readonly char[] FilterOperators = { '\\', '(', ')', '&', '|', '=', '!', '~' };

        /// <summary>
        /// Filter for a single scenario or outline whose generated name came from the code-behind.
        /// Exact.
        /// </summary>
        public static TestFilter ForGeneratedMethod(string fullyQualifiedClassName, string methodName, TargetKind kind)
        {
            string expression = "FullyQualifiedName~" + Escape(fullyQualifiedClassName + "." + methodName);
            string explanation = kind == TargetKind.ScenarioOutline
                ? "Matched the generated method '" + methodName + "' in the built code-behind; all example rows run."
                : "Matched the generated method '" + methodName + "' in the built code-behind.";

            return new TestFilter(expression, FilterStrategy.CodeBehind, explanation);
        }


        /// <summary>
        /// Filter for a single example row — where the runner allows it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is the one place the uniform <c>FullyQualifiedName</c> strategy breaks down, and it
        /// was measured rather than assumed:
        /// </para>
        /// <list type="bullet">
        /// <item><b>MSTest</b> reports each row as <c>Name</c> = <c>Add many &lt;a&gt; and &lt;b&gt;(1,2,3,4)</c>,
        /// and a <c>Name~</c> filter on that selects exactly one row.</item>
        /// <item><b>NUnit</b> puts the arguments in the fully-qualified name, but every filter over
        /// them returns zero tests — <c>~</c> and <c>=</c>, escaped and unescaped, with and without
        /// the namespace. The <c>(</c> and <c>"</c> characters defeat the VSTest filter parser.</item>
        /// <item><b>xUnit</b> gives every row the same fully-qualified name, and
        /// <c>DisplayName~</c> filters return zero.</item>
        /// </list>
        /// <para>
        /// So NUnit and xUnit widen to the whole outline. The caller is told, so the UI can say so
        /// before the click rather than leaving the user to notice three tests ran instead of one.
        /// </para>
        /// </remarks>
        /// <param name="fullyQualifiedClassName">Generated fixture class.</param>
        /// <param name="methodName">Generated method for the outline.</param>
        /// <param name="runner">Which test framework the project uses.</param>
        /// <param name="rowValues">The row's cell values, in column order.</param>
        /// <param name="ordinal">1-based row position across all Examples blocks.</param>
        /// <param name="widened">Set when the runner cannot select a single row.</param>
        public static TestFilter ForExampleRow(
            string fullyQualifiedClassName,
            string methodName,
            RunnerKind runner,
            IReadOnlyList<string> rowValues,
            int ordinal,
            out bool widened)
        {
            if (runner == RunnerKind.MsTest && rowValues.Count > 0)
            {
                // MSTest's display name is the row's values followed by Reqnroll's pickle index, so
                // matching on a prefix of "(v1,v2,…" pins the row without needing the index.
                string prefix = "(" + string.Join(",", rowValues.ToArray()) + ",";

                // A value carrying a quote or a newline cannot survive the argument round trip, and a
                // filter that is merely malformed is worse than one that is honestly wide: it either
                // matches nothing or matches something else. Same rule as ForSanitizedGuess.
                if (CanExpressAsFilterValue(prefix))
                {
                    // Conjoined with the outline's own method, NOT `Name~` on its own. `Name~` is a
                    // substring match across every test in the run, and display names are not unique
                    // across outlines — a two-column outline with the row `| 1 | 2 |` produces
                    // "(1,2,7)", which the prefix "(1,2," from a completely different outline
                    // matches. Alone, "run this row" would quietly run rows of unrelated scenarios.
                    string expression =
                        "(FullyQualifiedName~" + Escape(fullyQualifiedClassName + "." + methodName) + ")" +
                        "&(Name~" + Escape(prefix) + ")";

                    widened = false;
                    return new TestFilter(
                        expression,
                        FilterStrategy.CodeBehind,
                        "Matched example row " + ordinal + " by its MSTest display name.");
                }
            }

            widened = true;
            return new TestFilter(
                "FullyQualifiedName~" + Escape(fullyQualifiedClassName + "." + methodName),
                FilterStrategy.FeatureScopeFallback,
                DescribeWidening(runner, ordinal));
        }

        /// <summary>Why a single row could not be selected, in the user's terms.</summary>
        private static string DescribeWidening(RunnerKind runner, int ordinal)
        {
            if (runner == RunnerKind.MsTest)
            {
                // Reached only when the row itself defeats the filter — MSTest is the one runner that
                // CAN select a row, so blaming the runner here would send someone looking in the
                // wrong place.
                return "Example row " + ordinal + " cannot be expressed in a test filter — it is " +
                       "empty, or one of its values contains a quote or a line break. Running all " +
                       "rows of the outline instead.";
            }

            switch (runner)
            {
                case RunnerKind.NUnit:
                    return "NUnit cannot filter to a single example row — it puts the row's arguments " +
                           "in the test name, but VSTest filters cannot match them. Running all rows " +
                           "of the outline instead of row " + ordinal + ". Tip: tagging an Examples " +
                           "block lets you select it with a category filter.";
                case RunnerKind.XUnit:
                    return "xUnit gives every example row the same fully-qualified name, so a single " +
                           "row cannot be selected. Running all rows of the outline instead of row " +
                           ordinal + ".";
                default:
                    return "This runner cannot filter to a single example row. Running all rows of " +
                           "the outline instead of row " + ordinal + ".";
            }
        }

        /// <summary>Filter for a whole feature — every test in the generated fixture class.</summary>
        public static TestFilter ForFeatureClass(string fullyQualifiedClassName, FilterStrategy strategy, string explanation)
        {
            return new TestFilter(
                "FullyQualifiedName~" + Escape(fullyQualifiedClassName),
                strategy,
                explanation);
        }

        /// <summary>
        /// Filter for a scenario whose generated name had to be reconstructed because no code-behind
        /// was available. Pairs the reconstructed FQN with the raw title, so a runner that reports the
        /// title as its <c>Name</c> (MSTest) or <c>DisplayName</c> (xUnit) still matches if the
        /// reconstruction was wrong.
        /// </summary>
        public static TestFilter ForSanitizedGuess(
            string fullyQualifiedClassName,
            string guessedMethodName,
            string originalTitle,
            out bool titleClauseDropped)
        {
            string fqnClause = "FullyQualifiedName~" + Escape(fullyQualifiedClassName + "." + guessedMethodName);

            if (!CanExpressAsFilterValue(originalTitle))
            {
                titleClauseDropped = true;
                return new TestFilter(
                    fqnClause,
                    FilterStrategy.Sanitized,
                    "The project has no generated code-behind yet, so the method name '" + guessedMethodName +
                    "' was reconstructed from the scenario title. The title itself could not be expressed " +
                    "safely in a filter, so only the reconstructed name is matched.");
            }

            titleClauseDropped = false;
            string expression = "(" + fqnClause + ")|(Name~" + Escape(originalTitle) + ")";

            return new TestFilter(
                expression,
                FilterStrategy.Sanitized,
                "The project has no generated code-behind yet, so the method name '" + guessedMethodName +
                "' was reconstructed from the scenario title, with the title itself as a fallback match. " +
                "Build the project for an exact match.");
        }

        /// <summary>
        /// Escapes a literal value for use inside a VSTest filter expression. Generated identifiers
        /// never need this, but scenario titles do and it costs nothing to be uniform.
        /// </summary>
        public static string Escape(string value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            var builder = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                if (Array.IndexOf(FilterOperators, c) >= 0)
                {
                    builder.Append('\\');
                }

                builder.Append(c);
            }

            return builder.ToString();
        }

        /// <summary>
        /// Whether a title can survive being embedded in a filter. Quotes and newlines break the
        /// shell/argument round trip in ways escaping does not fix, so those titles lose the clause
        /// rather than produce a filter that silently matches the wrong thing.
        /// </summary>
        public static bool CanExpressAsFilterValue(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return false;
            }

            foreach (char c in title)
            {
                if (c == '"' || c == '\r' || c == '\n')
                {
                    return false;
                }
            }

            return true;
        }
    }
}
