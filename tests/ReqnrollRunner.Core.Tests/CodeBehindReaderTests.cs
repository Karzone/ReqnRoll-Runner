using System.IO;
using System.Linq;
using ReqnrollRunner.Core.Mapping;
using Xunit;

namespace ReqnrollRunner.Core.Tests
{
    /// <summary>
    /// The primary name-resolution path, exercised against code-behind files captured verbatim from
    /// real Reqnroll builds of all three runners.
    /// </summary>
    public sealed class CodeBehindReaderTests
    {
        private readonly CodeBehindReader _reader = new CodeBehindReader();

        public static TheoryData<string, string> Runners => new TheoryData<string, string>
        {
            { "Calculator.NUnit.feature.cs", "SampleCalculator.Features" },
            { "Calculator.XUnit.feature.cs", "SampleCalculator.XUnit.Features" },
            { "Calculator.MsTest.feature.cs", "SampleCalculator.MsTest.Features" },
        };

        [Theory]
        [MemberData(nameof(Runners))]
        public void Reads_the_namespace_and_feature_class(string fixture, string expectedNamespace)
        {
            CodeBehindInfo? info = _reader.Read(Fixtures.CodeBehind(fixture));

            Assert.NotNull(info);
            Assert.Equal(expectedNamespace, info!.Namespace);
            Assert.Equal("CalculatorBasicMoreFeature", info.ClassName);
            Assert.Equal(expectedNamespace + ".CalculatorBasicMoreFeature", info.FullyQualifiedClassName);
        }

        [Theory]
        [MemberData(nameof(Runners))]
        public void Finds_every_scenario_and_ignores_fixture_plumbing(string fixture, string expectedNamespace)
        {
            _ = expectedNamespace;

            CodeBehindInfo? info = _reader.Read(Fixtures.CodeBehind(fixture));

            // Six scenarios in Calculator.feature (the outline counts once — it is a single method).
            Assert.Equal(6, info!.Methods.Count);

            // TestInitializeAsync / ScenarioStartAsync and friends carry #line directives too, so a
            // reader that keyed purely on those would pick them up.
            Assert.DoesNotContain(info.Methods, m => m.MethodName.EndsWith("Async"));
            Assert.DoesNotContain(info.Methods, m => m.MethodName == "ScenarioInitialize");
        }

        // The whole design rests on this: the first #line directive inside a generated test method is
        // the scenario's keyword line in the .feature file. These are the real line numbers in
        // tests/fixtures/features/Calculator.feature.
        [Theory]
        [MemberData(nameof(Runners))]
        public void Maps_each_generated_method_to_its_feature_line(string fixture, string expectedNamespace)
        {
            _ = expectedNamespace;

            CodeBehindInfo? info = _reader.Read(Fixtures.CodeBehind(fixture));

            Assert.Equal("AddTwoNumbers", info!.FindByFeatureLine(10)!.MethodName);
            Assert.Equal("MultiplyTwoNumbers", info.FindByFeatureLine(16)!.MethodName);
            Assert.Equal("IvansQuotedTrickyOddTitle", info.FindByFeatureLine(22)!.MethodName);
            Assert.Equal("Unicodeスカラー", info.FindByFeatureLine(28)!.MethodName);
            Assert.Equal("AddManyAAndB", info.FindByFeatureLine(34)!.MethodName);
            Assert.Equal("SubtractInsideARule", info.FindByFeatureLine(52)!.MethodName);
        }

        [Theory]
        [MemberData(nameof(Runners))]
        public void Recognises_the_outline_as_parameterised(string fixture, string expectedNamespace)
        {
            _ = expectedNamespace;

            CodeBehindInfo? info = _reader.Read(Fixtures.CodeBehind(fixture));

            Assert.True(info!.FindByFeatureLine(34)!.IsParameterised);
            Assert.False(info.FindByFeatureLine(10)!.IsParameterised);
        }

        [Fact]
        public void All_three_runners_generate_the_same_method_names()
        {
            // This is why the filter builder can use one FullyQualifiedName strategy for every runner
            // instead of the per-runner strategies the spec originally sketched.
            string[][] names = Runners
                .Select(row => _reader.Read(Fixtures.CodeBehind((string)row[0]))!.Methods
                    .Select(m => m.MethodName).OrderBy(n => n, System.StringComparer.Ordinal).ToArray())
                .ToArray();

            Assert.Equal(names[0], names[1]);
            Assert.Equal(names[0], names[2]);
        }

        [Fact]
        public void Returns_null_for_a_line_with_no_generated_test()
        {
            CodeBehindInfo? info = _reader.Read(Fixtures.CodeBehind("Calculator.NUnit.feature.cs"));

            // Line 6 is the Background, which generates no test of its own.
            Assert.Null(info!.FindByFeatureLine(6));
        }

        [Fact]
        public void Returns_null_for_a_file_that_is_not_a_code_behind()
        {
            Assert.Null(_reader.Parse("// just a comment, no class here", "fake.cs"));
        }

        [Fact]
        public void Returns_null_for_a_missing_file()
        {
            Assert.Null(_reader.Read(Fixtures.CodeBehind("NoSuchFile.feature.cs")));
        }

        [Fact]
        public void Finds_a_sibling_code_behind_next_to_the_feature_file()
        {
            string feature = Fixtures.Project("NUnitProject", "Features", "Calculator.feature");
            string projectDirectory = Fixtures.Project("NUnitProject");

            string? found = CodeBehindReader.FindCodeBehindPath(feature, projectDirectory);

            Assert.NotNull(found);
            Assert.Equal("Calculator.feature.cs", Path.GetFileName(found));
        }

        [Fact]
        public void Reports_no_code_behind_when_the_project_has_never_been_built()
        {
            string feature = Fixtures.Project("MultiTargetProject", "Features", "Calculator.feature");
            string projectDirectory = Fixtures.Project("MultiTargetProject");

            Assert.Null(CodeBehindReader.FindCodeBehindPath(feature, projectDirectory));
        }
    }
}
