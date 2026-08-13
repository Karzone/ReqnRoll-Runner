using System;
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
        /// <summary>Characters VSTest treats as filter operators; a literal one must be backslash-escaped.</summary>
        private static readonly char[] FilterOperators = { '\\', '(', ')', '&', '|', '=', '!', '~', ',' };

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
