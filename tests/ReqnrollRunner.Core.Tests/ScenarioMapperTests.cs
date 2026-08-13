using ReqnrollRunner.Core.Mapping;
using ReqnrollRunner.Core.Model;
using Xunit;

namespace ReqnrollRunner.Core.Tests
{
    /// <summary>
    /// End-to-end mapping: caret position → filter, over the fixture project trees. This is what
    /// <c>reqnroll-runner map</c> prints, and it is the acceptance criterion for M1.
    /// </summary>
    public sealed class ScenarioMapperTests
    {
        private readonly ScenarioMapper _mapper = new ScenarioMapper();

        [Theory]
        [InlineData("NUnitProject", "SampleCalculator.Features")]
        [InlineData("XUnitProject", "SampleCalculator.XUnit.Features")]
        [InlineData("MsTestProject", "SampleCalculator.MsTest.Features")]
        public void Maps_a_scenario_to_an_exact_method_filter_for_every_runner(string project, string ns)
        {
            MappingResult result = _mapper.Map(Feature(project), 10);

            Assert.True(result.Success, result.Error);
            Assert.Equal(TargetKind.Scenario, result.Target!.Kind);
            Assert.Equal("AddTwoNumbers", result.GeneratedMethodName);
            Assert.Equal(FilterStrategy.CodeBehind, result.Filter!.Strategy);
            Assert.Equal(
                "FullyQualifiedName~" + ns + ".CalculatorBasicMoreFeature.AddTwoNumbers",
                result.Filter.Expression);
        }

        [Fact]
        public void Maps_an_outline_to_one_method_so_all_rows_run()
        {
            MappingResult result = _mapper.Map(Feature("NUnitProject"), 34);

            Assert.Equal(TargetKind.ScenarioOutline, result.Target!.Kind);
            Assert.Equal("AddManyAAndB", result.GeneratedMethodName);
        }

        [Fact]
        public void A_caret_in_the_second_Examples_block_still_maps_to_the_outline()
        {
            // SPEC §6 case 2: multiple Examples blocks, the second one tagged.
            MappingResult result = _mapper.Map(Feature("NUnitProject"), 46);

            Assert.Equal("AddManyAAndB", result.GeneratedMethodName);
        }

        [Fact]
        public void A_caret_in_Background_maps_to_the_whole_feature()
        {
            // SPEC §6 case 8.
            MappingResult result = _mapper.Map(Feature("NUnitProject"), 6);

            Assert.Equal(TargetKind.Feature, result.Target!.Kind);
            Assert.Null(result.GeneratedMethodName);
            Assert.Equal(
                "FullyQualifiedName~SampleCalculator.Features.CalculatorBasicMoreFeature",
                result.Filter!.Expression);
        }

        [Fact]
        public void A_caret_on_a_Rule_header_maps_to_the_whole_feature_in_v1()
        {
            // SPEC §6 case 3 and §3.1's explicit v1 simplification.
            MappingResult result = _mapper.Map(Feature("NUnitProject"), 50);

            Assert.Equal(TargetKind.Rule, result.Target!.Kind);
            Assert.Null(result.GeneratedMethodName);
            Assert.Contains("Rule", result.Filter!.Explanation);
        }

        [Fact]
        public void A_scenario_inside_a_Rule_maps_to_its_own_method()
        {
            MappingResult result = _mapper.Map(Feature("NUnitProject"), 52);

            Assert.Equal("SubtractInsideARule", result.GeneratedMethodName);
        }

        [Fact]
        public void A_title_full_of_filter_operators_produces_a_clean_identifier_filter()
        {
            // SPEC §6 case 1. Because the name comes from the code-behind, none of the title's
            // punctuation reaches the filter at all — there is nothing left to escape.
            MappingResult result = _mapper.Map(Feature("NUnitProject"), 22);

            Assert.Equal("IvansQuotedTrickyOddTitle", result.GeneratedMethodName);
            Assert.DoesNotContain("\\", result.Filter!.Expression);
            Assert.DoesNotContain("|", result.Filter.Expression);
        }

        [Fact]
        public void A_unicode_title_maps_through_the_code_behind_not_a_guess()
        {
            // "Ünïcödé — スカラー" generates Unicodeスカラー — the kind of name no reasonable
            // reconstruction would produce, which is the argument for reading the code-behind.
            MappingResult result = _mapper.Map(Feature("NUnitProject"), 28);

            Assert.Equal("Unicodeスカラー", result.GeneratedMethodName);
            Assert.Equal(FilterStrategy.CodeBehind, result.Filter!.Strategy);
        }

        // SPEC §6 case 6.
        [Fact]
        public void Identically_named_features_in_two_projects_map_to_different_assemblies()
        {
            MappingResult nunit = _mapper.Map(Feature("NUnitProject"), 10);
            MappingResult xunit = _mapper.Map(Feature("XUnitProject"), 10);

            Assert.NotEqual(nunit.Project!.ProjectPath, xunit.Project!.ProjectPath);
            Assert.NotEqual(nunit.Filter!.Expression, xunit.Filter!.Expression);
        }

        [Fact]
        public void Falls_back_to_reconstructed_names_when_the_project_has_not_been_built()
        {
            // MultiTargetProject has a feature file but no generated code-behind.
            MappingResult result = _mapper.Map(
                Fixtures.Project("MultiTargetProject", "Features", "Calculator.feature"), 10);

            Assert.True(result.Success);
            Assert.Equal(FilterStrategy.Sanitized, result.Filter!.Strategy);
            Assert.Equal("AddTwoNumbers", result.GeneratedMethodName);
            Assert.Contains(result.Warnings, w => w.Contains("No generated code-behind"));
        }

        [Fact]
        public void The_reconstructed_namespace_includes_the_feature_folder()
        {
            MappingResult result = _mapper.Map(
                Fixtures.Project("MultiTargetProject", "Features", "Calculator.feature"), 10);

            // Root namespace (project file name) + the Features folder + the generated class.
            Assert.Equal(
                "MultiTargetProject.Features.CalculatorBasicMoreFeature",
                result.GeneratedTypeName);
        }

        [Fact]
        public void Refuses_a_project_without_Reqnroll()
        {
            MappingResult result = _mapper.Map(
                Fixtures.Project("NoReqnrollProject", "Features", "Calculator.feature"), 10);

            Assert.False(result.Success);
            Assert.Contains("No Reqnroll.* package reference found", result.Error);
        }

        [Fact]
        public void Refuses_a_feature_file_outside_any_project()
        {
            MappingResult result = _mapper.Map(Fixtures.Project("Orphan", "Orphan.feature"), 10);

            Assert.False(result.Success);
            Assert.Contains("No .csproj found above", result.Error);
        }

        [Fact]
        public void Reports_a_parse_failure_without_touching_the_project()
        {
            MappingResult result = _mapper.Map(Fixtures.Feature("Malformed.feature"), 3);

            Assert.False(result.Success);
            Assert.Null(result.Project);
        }

        [Fact]
        public void An_unknown_runner_still_gets_a_usable_filter()
        {
            // A FullyQualifiedName filter works for all three runners, so an unidentified runner is a
            // warning rather than a failure.
            MappingResult result = _mapper.Map(
                Fixtures.Project("UnknownRunnerProject", "Features", "Calculator.feature"), 10);

            Assert.True(result.Success);
            Assert.Equal(RunnerKind.Unknown, result.Project!.Runner);
            Assert.Contains("FullyQualifiedName~", result.Filter!.Expression);
            Assert.Contains(result.Warnings, w => w.Contains("Could not tell which test framework"));
        }

        [Fact]
        public void A_caret_on_a_step_maps_to_its_owning_scenario()
        {
            // Line 13 is a step of "Add two numbers" — the target's keyword line is 10.
            MappingResult result = _mapper.Map(Feature("NUnitProject"), 13);

            Assert.Equal("AddTwoNumbers", result.GeneratedMethodName);
        }

        [Fact]
        public void A_stale_code_behind_widens_to_the_feature_rather_than_running_the_wrong_test()
        {
            // StaleProject's feature has a scenario appended after the code-behind was generated, so
            // line 58 exists in the .feature but has no generated method. Running a stale method name
            // would silently run a different scenario; widening to the class is wrong-but-visible.
            MappingResult result = _mapper.Map(
                Fixtures.Project("StaleProject", "Features", "Calculator.feature"), 58);

            Assert.True(result.Success);
            Assert.Equal("Added after the last build", result.Target!.Name);
            Assert.Null(result.GeneratedMethodName);
            Assert.Equal(FilterStrategy.FeatureScopeFallback, result.Filter!.Strategy);
            Assert.Contains(result.Warnings, w => w.Contains("out of date"));
        }

        [Fact]
        public void A_scenario_the_stale_code_behind_does_know_about_still_maps_exactly()
        {
            MappingResult result = _mapper.Map(
                Fixtures.Project("StaleProject", "Features", "Calculator.feature"), 10);

            Assert.Equal("AddTwoNumbers", result.GeneratedMethodName);
            Assert.Equal(FilterStrategy.CodeBehind, result.Filter!.Strategy);
        }

        private static string Feature(string project)
        {
            return Fixtures.Project(project, "Features", "Calculator.feature");
        }
    }
}
