using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ReqnrollRunner.Core.Mapping
{
    /// <summary>
    /// Reconstructs the identifier Reqnroll's generator derives from a Feature or Scenario title.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the <em>fallback</em> path. The primary path is <see cref="CodeBehindReader"/>, which
    /// reads the real names out of the generated <c>.feature.cs</c>; this class only runs when that
    /// file is missing (typically the project has never been built).
    /// </para>
    /// <para>
    /// The rules below were derived empirically by running a 32-title corpus through the real
    /// Reqnroll 3.3.4 generator and diffing the output — see
    /// <c>tests/fixtures/sanitizer-corpus.tsv</c>, which is that generator's actual output and is
    /// asserted against case-by-case. They are NOT guesses, but they are also not a guarantee: a
    /// future Reqnroll release can change them, which is exactly why the code-behind is preferred.
    /// </para>
    /// <para>The rules, in order:</para>
    /// <list type="number">
    /// <item>Apostrophes are deleted outright, joining the word: <c>Ivan's</c> → <c>Ivans</c>.</item>
    /// <item>Diacritics are stripped via Unicode decomposition: <c>café</c> → <c>Cafe</c>.</item>
    /// <item>A handful of non-decomposable Latin letters are folded through a lookup table
    /// (<c>Æ</c> → <c>AE</c>, <c>Ø</c> → <c>O</c>, <c>ß</c> → <c>B</c>). Letters outside it — <c>Œ</c>,
    /// <c>Þ</c>, Cyrillic, CJK — are kept verbatim.</item>
    /// <item>Every remaining character that is not a letter, digit or underscore is a word separator
    /// and capitalises the next letter. <c>.</c>, <c>-</c> and <c>_</c> additionally emit an
    /// underscore: <c>kebab-case</c> → <c>Kebab_Case</c>.</item>
    /// <item>A leading digit gets an underscore prefix, since identifiers cannot start with one.</item>
    /// </list>
    /// </remarks>
    public static class TestNameSanitizer
    {
        /// <summary>
        /// Latin letters with no Unicode decomposition that Reqnroll still folds to ASCII.
        /// Deliberately mirrors observed generator behaviour rather than "correct" transliteration —
        /// <c>ß</c> really does become <c>B</c>, not <c>ss</c>.
        /// </summary>
        private static readonly Dictionary<char, string> AsciiFolds = new Dictionary<char, string>
        {
            { 'Æ', "AE" }, { 'æ', "ae" },
            { 'Ø', "O" }, { 'ø', "o" },
            { 'Đ', "D" }, { 'đ', "d" },
            { 'Ð', "D" }, { 'ð', "d" },
            { 'Ł', "L" }, { 'ł', "l" },
            { 'ß', "B" },
        };

        /// <summary>The generated fixture class name for a feature title, e.g. <c>CalculatorFeature</c>.</summary>
        public static string FeatureClassName(string featureTitle)
        {
            return ToIdentifier(featureTitle) + "Feature";
        }

        /// <summary>The generated test method name for a scenario or outline title.</summary>
        public static string MethodName(string scenarioTitle)
        {
            return ToIdentifier(scenarioTitle);
        }

        /// <summary>Applies the full rule set to an arbitrary title.</summary>
        public static string ToIdentifier(string title)
        {
            if (title == null)
            {
                throw new ArgumentNullException(nameof(title));
            }

            string prepared = StripDiacritics(RemoveApostrophes(title));

            var builder = new StringBuilder(prepared.Length + 1);
            bool capitaliseNext = true;

            foreach (char raw in prepared)
            {
                if (AsciiFolds.TryGetValue(raw, out string? folded))
                {
                    Append(builder, folded!, ref capitaliseNext);
                    continue;
                }

                if (char.IsLetterOrDigit(raw) || raw == '_')
                {
                    // '_' is both a legal identifier character and a separator: it survives into the
                    // output *and* capitalises what follows.
                    if (raw == '_')
                    {
                        builder.Append('_');
                        capitaliseNext = true;
                        continue;
                    }

                    Append(builder, raw.ToString(), ref capitaliseNext);
                    continue;
                }

                if (raw == '.' || raw == '-')
                {
                    builder.Append('_');
                    capitaliseNext = true;
                    continue;
                }

                // Any other punctuation, symbol or whitespace: dropped, but still a word boundary.
                capitaliseNext = true;
            }

            string identifier = builder.ToString();

            if (identifier.Length == 0)
            {
                // A title made entirely of dropped characters leaves nothing to name a method after.
                return "_";
            }

            if (char.IsDigit(identifier[0]))
            {
                return "_" + identifier;
            }

            return identifier;
        }

        private static void Append(StringBuilder builder, string text, ref bool capitaliseNext)
        {
            foreach (char c in text)
            {
                if (capitaliseNext && char.IsLetter(c))
                {
                    builder.Append(char.ToUpperInvariant(c));
                    capitaliseNext = false;
                }
                else
                {
                    builder.Append(c);
                    // A digit consumes the boundary too: "50%" stays "50", it does not capitalise
                    // anything later in the same run.
                    capitaliseNext = false;
                }
            }
        }

        private static string RemoveApostrophes(string value)
        {
            // Straight and typographic apostrophes join words rather than splitting them, so they are
            // removed before tokenising: "Ivan's" -> "Ivans", not "IvanS".
            return value.Replace("'", string.Empty).Replace("’", string.Empty);
        }

        /// <summary>
        /// Folds accented <em>Latin</em> letters to their base form, and nothing else.
        /// </summary>
        /// <remarks>
        /// The narrowing matters. A blanket <c>FormD</c> decomposition also decomposes Cyrillic
        /// <c>й</c> (U+0439) into <c>и</c> plus a combining breve, so stripping combining marks
        /// globally would turn <c>русский</c> into <c>русскии</c>. The real generator leaves it as
        /// <c>Русский</c> while still folding <c>café</c> to <c>Cafe</c>, so decomposition is applied
        /// per character and only within the Latin supplement and extended blocks.
        /// </remarks>
        private static string StripDiacritics(string value)
        {
            var builder = new StringBuilder(value.Length);

            foreach (char c in value)
            {
                if (c < ' ' || c > 'ɏ')
                {
                    builder.Append(c);
                    continue;
                }

                foreach (char part in c.ToString().Normalize(NormalizationForm.FormD))
                {
                    if (CharUnicodeInfo.GetUnicodeCategory(part) != UnicodeCategory.NonSpacingMark)
                    {
                        builder.Append(part);
                    }
                }
            }

            return builder.ToString();
        }
    }
}
