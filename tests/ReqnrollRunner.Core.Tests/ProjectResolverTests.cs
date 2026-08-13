using System.IO;
using System.Linq;
using ReqnrollRunner.Core.Model;
using ReqnrollRunner.Core.Projects;
using Xunit;

namespace ReqnrollRunner.Core.Tests
{
    /// <summary>Feature file → owning test project and its runner (SPEC §3.2).</summary>
    public sealed class ProjectResolverTests
    {
        private readonly ProjectResolver _resolver = new ProjectResolver();

        [Theory]
        [InlineData("NUnitProject", RunnerKind.NUnit)]
        [InlineData("XUnitProject", RunnerKind.XUnit)]
        [InlineData("MsTestProject", RunnerKind.MsTest)]
        public void Detects_the_runner_from_the_package_reference(string project, RunnerKind expected)
        {
            TestProjectInfo? info = _resolver.Resolve(
                Fixtures.Project(project, "Features", "Calculator.feature"), out string? error);

            Assert.Null(error);
            Assert.Equal(expected, info!.Runner);
        }

        [Fact]
        public void Walks_up_from_a_nested_feature_file_to_the_nearest_project()
        {
            TestProjectInfo? info = _resolver.Resolve(
                Fixtures.Project("NUnitProject", "Features", "Calculator.feature"), out _);

            Assert.Equal("NUnitProject.csproj", Path.GetFileName(info!.ProjectPath));
        }

        // SPEC §6 case 5.
        [Fact]
        public void Refuses_a_project_with_no_Reqnroll_reference()
        {
            TestProjectInfo? info = _resolver.Resolve(
                Fixtures.Project("NoReqnrollProject", "Features", "Calculator.feature"), out string? error);

            Assert.Null(info);
            Assert.Contains("No Reqnroll.* package reference found", error);
            Assert.Contains("NoReqnrollProject.csproj", error);
        }

        [Fact]
        public void Falls_back_to_Unknown_when_only_the_base_Reqnroll_package_is_referenced()
        {
            TestProjectInfo? info = _resolver.Resolve(
                Fixtures.Project("UnknownRunnerProject", "Features", "Calculator.feature"), out string? error);

            Assert.Null(error);
            Assert.Equal(RunnerKind.Unknown, info!.Runner);
            Assert.Contains(info.Warnings, w => w.Contains("Could not tell which test framework"));
        }

        // SPEC §6 case 10.
        [Fact]
        public void Finds_a_Reqnroll_reference_declared_by_central_package_management()
        {
            // The csproj here carries a version-less PackageReference; the identity that proves this
            // is a Reqnroll NUnit project lives in ../Directory.Packages.props.
            TestProjectInfo? info = _resolver.Resolve(
                Fixtures.Project("CpmSolution", "CpmTests", "Features", "Cpm.feature"), out string? error);

            Assert.Null(error);
            Assert.Equal(RunnerKind.NUnit, info!.Runner);
        }

        [Fact]
        public void Reads_all_target_frameworks_and_warns_when_multi_targeted()
        {
            TestProjectInfo? info = _resolver.Resolve(
                Fixtures.Project("MultiTargetProject", "Features", "Calculator.feature"), out _);

            Assert.Equal(new[] { "net8.0", "net9.0", "net472" }, info!.TargetFrameworks);
            Assert.Contains(info.Warnings, w => w.Contains("multi-targets"));
        }

        [Theory]
        [InlineData(null, "net8.0")]      // no preference: first wins
        [InlineData("net472", "net472")]  // honoured
        [InlineData("net10.0", "net8.0")] // preference not offered by the project: first wins
        public void Resolves_the_framework_to_run_under(string? preferred, string expected)
        {
            TestProjectInfo? info = _resolver.Resolve(
                Fixtures.Project("MultiTargetProject", "Features", "Calculator.feature"), out _);

            Assert.Equal(expected, info!.ResolveFramework(preferred));
        }

        [Fact]
        public void A_single_targeted_project_needs_no_framework_argument()
        {
            TestProjectInfo? info = _resolver.Resolve(
                Fixtures.Project("NUnitProject", "Features", "Calculator.feature"), out _);

            Assert.Null(info!.ResolveFramework(null));
        }

        [Fact]
        public void Uses_the_explicit_root_namespace()
        {
            TestProjectInfo? info = _resolver.Resolve(
                Fixtures.Project("XUnitProject", "Features", "Calculator.feature"), out _);

            Assert.Equal("SampleCalculator.XUnit", info!.RootNamespace);
        }

        [Fact]
        public void Defaults_the_root_namespace_to_the_project_file_name()
        {
            // MultiTargetProject.csproj declares no <RootNamespace>, which is what MSBuild does too.
            TestProjectInfo? info = _resolver.Resolve(
                Fixtures.Project("MultiTargetProject", "Features", "Calculator.feature"), out _);

            Assert.Equal("MultiTargetProject", info!.RootNamespace);
        }

        // SPEC §6 case 5, second half: a feature file that belongs to no project at all.
        [Fact]
        public void Reports_a_feature_file_with_no_project_above_it()
        {
            TestProjectInfo? info = _resolver.Resolve(
                Fixtures.Project("Orphan", "Orphan.feature"), out string? error);

            Assert.Null(info);
            Assert.Contains("No .csproj found above", error);
        }

        // SPEC §6 case 6: two projects each containing a feature with the same name and title. The
        // resolved project must be the one that owns the file on disk, never the other.
        [Fact]
        public void Identically_named_features_in_different_projects_resolve_independently()
        {
            TestProjectInfo? nunit = _resolver.Resolve(
                Fixtures.Project("NUnitProject", "Features", "Calculator.feature"), out _);
            TestProjectInfo? xunit = _resolver.Resolve(
                Fixtures.Project("XUnitProject", "Features", "Calculator.feature"), out _);

            Assert.NotEqual(nunit!.ProjectPath, xunit!.ProjectPath);
            Assert.Equal(RunnerKind.NUnit, nunit.Runner);
            Assert.Equal(RunnerKind.XUnit, xunit.Runner);
        }

        [Fact]
        public void Every_fixture_project_is_present()
        {
            // Vacuity guard: if the fixture tree moved, the theories above would silently stop
            // proving anything about real project files.
            string[] expected =
            {
                "NUnitProject", "XUnitProject", "MsTestProject", "MultiTargetProject",
                "NoReqnrollProject", "UnknownRunnerProject",
            };

            foreach (string name in expected)
            {
                Assert.True(
                    Directory.EnumerateFiles(Fixtures.Project(name), "*.csproj").Any(),
                    "Missing fixture project " + name);
            }
        }
    }
}
