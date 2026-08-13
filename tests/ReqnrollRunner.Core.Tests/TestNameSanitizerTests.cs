using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using ReqnrollRunner.Core.Mapping;
using Xunit;

namespace ReqnrollRunner.Core.Tests
{
    /// <summary>
    /// Pins <see cref="TestNameSanitizer"/> against real Reqnroll generator output.
    /// </summary>
    /// <remarks>
    /// <c>tests/fixtures/sanitizer-corpus.tsv</c> is not a hand-written expectation: every row was
    /// produced by building a feature file with the real Reqnroll 3.3.4 MSBuild generator and reading
    /// the identifiers out of the generated code-behind. So these are oracle tests — if Reqnroll ever
    /// changes its naming, regenerating the corpus makes them fail loudly instead of leaving the
    /// fallback quietly wrong.
    /// </remarks>
    public sealed class TestNameSanitizerTests
    {
        public static TheoryData<string, string> MethodNameCorpus => LoadCorpus("METHOD");

        public static TheoryData<string, string> FeatureClassCorpus => LoadCorpus("FEATURE_CLASS");

        [Theory]
        [MemberData(nameof(MethodNameCorpus))]
        public void Reproduces_the_generator_method_name(string title, string expected)
        {
            Assert.Equal(expected, TestNameSanitizer.MethodName(title));
        }

        [Theory]
        [MemberData(nameof(FeatureClassCorpus))]
        public void Reproduces_the_generator_feature_class_name(string title, string expected)
        {
            Assert.Equal(expected, TestNameSanitizer.FeatureClassName(title));
        }

        [Fact]
        public void The_corpus_is_not_empty()
        {
            // Guards against a silently-passing suite if the fixture goes missing or the parser breaks:
            // an empty TheoryData makes every Theory above vacuous.
            Assert.True(MethodNameCorpus.Count >= 25, "Expected the captured corpus to cover at least 25 titles.");
        }

        [Theory]
        // SPEC §6 case 1 — the specific characters the spec calls out.
        [InlineData("Ivan's \"quoted\" (tricky) & odd | title ~ = !", "IvansQuotedTrickyOddTitle")]
        [InlineData("1 plus 1", "_1Plus1")]
        [InlineData("snake_case and kebab-case", "Snake_CaseAndKebab_Case")]
        [InlineData("Ünïcödé — スカラー", "Unicodeスカラー")]
        [InlineData("  leading and trailing  ", "LeadingAndTrailing")]
        public void Handles_the_documented_edge_cases(string title, string expected)
        {
            Assert.Equal(expected, TestNameSanitizer.MethodName(title));
        }

        [Fact]
        public void A_title_with_nothing_usable_still_yields_a_legal_identifier()
        {
            // Not observed from the generator (such a title is pathological), so this pins OUR
            // behaviour rather than Reqnroll's: never return something that cannot be an identifier.
            string identifier = TestNameSanitizer.ToIdentifier("!!! ??? ***");

            Assert.NotEmpty(identifier);
            Assert.False(char.IsDigit(identifier[0]));
        }

        [Fact]
        public void Rejects_a_null_title()
        {
            Assert.Throws<ArgumentNullException>(() => TestNameSanitizer.ToIdentifier(null!));
        }

        private static TheoryData<string, string> LoadCorpus(string kind)
        {
            var data = new TheoryData<string, string>();

            foreach (string line in File.ReadAllLines(Fixtures.Path_("sanitizer-corpus.tsv")))
            {
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }

                string[] columns = line.Split('\t');
                if (columns.Length != 3 || columns[2] != kind)
                {
                    continue;
                }

                data.Add(JsonSerializer.Deserialize<string>(columns[0])!, columns[1]);
            }

            return data;
        }
    }
}
