using System.Threading;
using System.Threading.Tasks;
using ReqnrollRunner.Core.Execution;
using ReqnrollRunner.Core.Mapping;
using ReqnrollRunner.Core.Model;
using Xunit;

namespace ReqnrollRunner.Core.Tests
{
    /// <summary>
    /// The deterministic parts of the execution layer: argument construction and the debug
    /// announcement parser. Actually running <c>dotnet test</c> is covered by the sample projects and
    /// the CLI, not here.
    /// </summary>
    public sealed class ExecutionTests
    {
        [Fact]
        public void Builds_a_dotnet_test_command_line()
        {
            string arguments = DotnetTestRunner.BuildArguments(
                "/repo/Tests.csproj",
                "FullyQualifiedName~Ns.CalculatorFeature.AddTwoNumbers",
                "results.trx",
                new RunOptions { NoBuild = true });

            Assert.Equal(
                "test \"/repo/Tests.csproj\" --no-build " +
                "--filter \"FullyQualifiedName~Ns.CalculatorFeature.AddTwoNumbers\" " +
                "--logger \"trx;LogFileName=results.trx\"",
                arguments);
        }

        [Fact]
        public void Omits_no_build_when_the_caller_wants_a_build()
        {
            string arguments = DotnetTestRunner.BuildArguments(
                "/repo/Tests.csproj", "f", "r.trx", new RunOptions { NoBuild = false });

            Assert.DoesNotContain("--no-build", arguments);
        }

        [Fact]
        public void Includes_the_framework_only_when_one_is_chosen()
        {
            string withFramework = DotnetTestRunner.BuildArguments(
                "/repo/Tests.csproj", "f", "r.trx", new RunOptions { Framework = "net8.0" });
            string without = DotnetTestRunner.BuildArguments(
                "/repo/Tests.csproj", "f", "r.trx", new RunOptions());

            Assert.Contains("--framework \"net8.0\"", withFramework);
            Assert.DoesNotContain("--framework", without);
        }

        [Fact]
        public void Appends_extra_arguments_verbatim_and_last()
        {
            string arguments = DotnetTestRunner.BuildArguments(
                "/repo/Tests.csproj", "f", "r.trx",
                new RunOptions { ExtraArguments = "--blame --diag log.txt" });

            Assert.EndsWith("--blame --diag log.txt", arguments);
        }

        [Fact]
        public void Quotes_a_path_containing_spaces()
        {
            string arguments = DotnetTestRunner.BuildArguments(
                "/my repo/My Tests.csproj", "f", "r.trx", new RunOptions());

            Assert.Contains("\"/my repo/My Tests.csproj\"", arguments);
        }

        [Fact]
        public void Passes_the_results_directory_when_one_is_set()
        {
            string arguments = DotnetTestRunner.BuildArguments(
                "/repo/Tests.csproj", "f", "r.trx", new RunOptions { ResultsDirectory = "/tmp/out" });

            Assert.Contains("--results-directory \"/tmp/out\"", arguments);
        }

        [Fact]
        public async Task Refuses_to_run_a_failed_mapping()
        {
            MappingResult mapping = MappingResult.Fail("/x.feature", 1, "nope");

            TestRunResult result = await new DotnetTestRunner()
                .RunAsync(mapping, new RunOptions(), null, CancellationToken.None);

            Assert.Equal("nope", result.FailureReason);
            Assert.False(result.IsSuccess);
        }

        [Fact]
        public async Task Refuses_to_debug_a_failed_mapping()
        {
            MappingResult mapping = MappingResult.Fail("/x.feature", 1, "nope");

            DebugLaunchResult result = await new DebugSessionLauncher()
                .LaunchAsync(mapping, new RunOptions(), null, CancellationToken.None);

            Assert.False(result.Success);
            Assert.Equal("nope", result.Error);
        }

        // The exact line VSTest prints under VSTEST_HOST_DEBUG=1, verified against VSTest 17.8 on
        // both Windows (Name: testhost) and Linux (Name: dotnet).
        [Theory]
        [InlineData("Process Id: 23840, Name: dotnet", 23840, "dotnet")]
        [InlineData("Process Id: 4242, Name: testhost", 4242, "testhost")]
        [InlineData("Process Id: 7, Name: testhost.x86", 7, "testhost.x86")]
        [InlineData("Process Id: 99", 99, "testhost")]
        [InlineData("  Process Id:   123 ,  Name:  dotnet ", 123, "dotnet")]
        public void Parses_the_test_host_announcement(string line, int expectedPid, string expectedName)
        {
            var parsed = DebugSessionLauncher.TryParseProcessId(line);

            Assert.NotNull(parsed);
            Assert.Equal(expectedPid, parsed!.Value.ProcessId);
            Assert.Equal(expectedName, parsed.Value.ProcessName);
        }

        [Theory]
        [InlineData("")]
        [InlineData("Starting test execution, please wait...")]
        [InlineData("Host debugging is enabled. Please attach debugger to testhost process to continue.")]
        [InlineData("Process Id: not-a-number")]
        public void Ignores_lines_that_are_not_the_announcement(string line)
        {
            Assert.Null(DebugSessionLauncher.TryParseProcessId(line));
        }

        [Fact]
        public void Knows_the_zero_match_marker_vstest_actually_prints()
        {
            // Guards the one string we scrape from stdout. If VSTest reworded this, the runner would
            // silently report "0 tests passed" instead of the diagnostic the spec asks for.
            Assert.Contains(
                DotnetTestRunner.ZeroMatchMarker,
                "No test matches the given testcase filter `FullyQualifiedName~X` in /path/Tests.dll");
        }
    }
}
