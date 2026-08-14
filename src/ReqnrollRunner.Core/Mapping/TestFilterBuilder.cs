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
        /// <item><b>NUnit</b> puts the arguments in the test name —
        /// <c>AddManyAAndB("1","2","3","4",null)</c> — which no VSTest <c>--filter</c> operator
        /// matches, but which NUnit's own <c>--where</c> language matches easily. Selected through
        /// <c>NUnit.Where</c> instead; see <see cref="NUnitWhereForRow"/>.</item>
        /// <item><b>xUnit</b> also names rows distinctly —
        /// <c>Add many &lt;a&gt; and &lt;b&gt;(a: "1", b: "2", …)</c> — and a <c>DisplayName~</c>
        /// filter does select one. It is not wired up yet because the display name embeds the
        /// generated PARAMETER names, which are not in the feature file. Issue #14.</item>
        /// </list>
        /// <para>
        /// An earlier version of this comment claimed NUnit and xUnit could not do this at all. That
        /// was wrong, and wrong for an avoidable reason: the shell used to measure it stripped the
        /// quote characters out of the filter before the test platform ever saw them, so every
        /// attempt returned zero and the runners took the blame. Only xUnit still widens.
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
            if (runner == RunnerKind.NUnit && rowValues.Count > 0)
            {
                widened = false;
                return new TestFilter(
                    // Empty on purpose. --filter and NUnit.Where do NOT intersect: when both are
                    // given, --filter wins and the adapter setting is ignored outright (measured —
                    // the combined form ran all three rows). The where clause carries the class name
                    // itself, so nothing is lost by dropping the --filter half.
                    string.Empty,
                    FilterStrategy.CodeBehind,
                    "Matched example row " + ordinal + " by its NUnit test name.",
                    NUnitWhereForRow(fullyQualifiedClassName, methodName, rowValues));
            }

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

        /// <summary>
        /// An <c>NUnit.Where</c> expression selecting one example row of one generated method.
        /// </summary>
        /// <remarks>
        /// <para>
        /// NUnit names a row by its arguments — <c>Ns.CalculatorFeature.AddManyAAndB("1","2","3","4",null)</c>
        /// — and a VSTest <c>--filter</c> cannot match that, which is why row selection was originally
        /// believed impossible here. NUnit's own <c>--where</c> language matches it easily. Verified
        /// against a real build: each of the three rows in the NUnit sample selects exactly itself.
        /// </para>
        /// <para>
        /// Two details of the generated expression are deliberate and look odd without the reason.
        /// </para>
        /// <para>
        /// <b>The quotes around each value are written as <c>.</c>, not as quotes.</b> The expression
        /// travels as a single command-line argument, and embedding a double quote inside an argument
        /// that is itself quoted is the kind of round trip that breaks differently on every shell and
        /// process launcher. A <c>.</c> matches the quote character without ever putting one in the
        /// argument. It stays precise because the whole chain is anchored at the opening bracket and
        /// every value ends at a comma: for values 1 and 2, the pattern rejects
        /// <c>AddManyAAndB("11","2",…)</c>, because after matching <c>1</c> it requires a comma where
        /// a quote actually is.
        /// </para>
        /// <para>
        /// <b>The bracket is <c>[(]</c> rather than <c>\(</c>.</b> NUnit's expression parser mangles a
        /// backslash inside a quoted string — <c>\(</c> arrives at the regex engine as <c>//(</c> and
        /// fails with "Not enough )'s". A character class escapes the bracket without a backslash.
        /// </para>
        /// </remarks>
        private static string NUnitWhereForRow(
            string fullyQualifiedClassName,
            string methodName,
            IReadOnlyList<string> rowValues)
        {
            var pattern = new StringBuilder();
            pattern.Append(EscapeForNUnitRegex(fullyQualifiedClassName + "." + methodName));
            pattern.Append("[(]");

            foreach (string value in rowValues)
            {
                // '.' for the opening quote, the value, '.' for the closing quote, then the comma
                // that anchors the next one.
                pattern.Append('.').Append(EscapeForNUnitRegex(value)).Append(".,");
            }

            // `test`, not `name`: `name` is the test name alone, so two feature classes that both
            // generated AddManyAAndB would both match. `test` is the fully-qualified name.
            return "NUnit.Where=test =~ '" + pattern + "'";
        }

        /// <summary>
        /// Escapes regex metacharacters for an <c>NUnit.Where</c> pattern, without using a backslash.
        /// </summary>
        /// <remarks>
        /// <c>Regex.Escape</c> is not usable here: it escapes with backslashes, and NUnit's expression
        /// parser corrupts those before the regex engine sees them. A single-character class is the
        /// portable equivalent — <c>[.]</c> means a literal dot to both parsers.
        /// </remarks>
        private static string EscapeForNUnitRegex(string value)
        {
            var builder = new StringBuilder(value.Length);

            foreach (char c in value)
            {
                if (".$^{[(|)*+?\\".IndexOf(c) >= 0)
                {
                    builder.Append('[').Append(c).Append(']');
                }
                else if (c == ']')
                {
                    // A ']' cannot be put inside a character class this way, and it is only special
                    // when a class is already open — which, given every other metacharacter is
                    // neutralised, it never is. Pass it through.
                    builder.Append(c);
                }
                else
                {
                    builder.Append(c);
                }
            }

            return builder.ToString();
        }

        /// <summary>Whether this runner can select one example row at all.</summary>
        /// <remarks>
        /// Exposed so the Visual Studio command can label itself honestly before the click, without
        /// duplicating the rule. NUnit goes through its own <c>--where</c>; MSTest through a VSTest
        /// <c>Name~</c> on the display name. xUnit is the one that cannot yet — see issue #14.
        /// </remarks>
        public static bool CanSelectSingleRow(RunnerKind runner)
        {
            return runner == RunnerKind.NUnit || runner == RunnerKind.MsTest;
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
