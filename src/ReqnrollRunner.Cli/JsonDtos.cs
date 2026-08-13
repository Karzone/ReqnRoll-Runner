using System.Collections.Generic;
using System.Linq;
using ReqnrollRunner.Core.Model;

namespace ReqnrollRunner.Cli
{
    /// <summary>
    /// The <c>--json</c> shape. Kept as explicit DTOs rather than serialising the Core model so the
    /// wire contract is versionable independently — the VS Code head in v2 consumes this (SPEC §8).
    /// </summary>
    internal sealed class MappingDto
    {
        public bool Success { get; set; }

        public string? Error { get; set; }

        public string FeaturePath { get; set; } = string.Empty;

        public int Line { get; set; }

        public TargetDto? Target { get; set; }

        public ProjectDto? Project { get; set; }

        public FilterDto? Filter { get; set; }

        public string? TestClass { get; set; }

        public string? TestMethod { get; set; }

        public IReadOnlyList<string> Warnings { get; set; } = new List<string>();

        public static MappingDto From(MappingResult mapping)
        {
            return new MappingDto
            {
                Success = mapping.Success,
                Error = mapping.Error,
                FeaturePath = mapping.FeaturePath,
                Line = mapping.Line,
                Target = mapping.Target == null ? null : new TargetDto
                {
                    Kind = mapping.Target.Kind.ToString(),
                    Name = mapping.Target.Name,
                    FeatureName = mapping.Target.FeatureName,
                    Line = mapping.Target.Line,
                    Tags = mapping.Target.Tags,
                    Description = mapping.Target.Describe(),
                },
                Project = mapping.Project == null ? null : new ProjectDto
                {
                    Path = mapping.Project.ProjectPath,
                    Runner = mapping.Project.Runner.ToString(),
                    TargetFrameworks = mapping.Project.TargetFrameworks,
                    RootNamespace = mapping.Project.RootNamespace,
                },
                Filter = mapping.Filter == null ? null : new FilterDto
                {
                    Expression = mapping.Filter.Expression,
                    Strategy = mapping.Filter.Strategy.ToString(),
                    Explanation = mapping.Filter.Explanation,
                },
                TestClass = mapping.GeneratedTypeName,
                TestMethod = mapping.GeneratedMethodName,
                Warnings = mapping.Warnings,
            };
        }
    }

    internal sealed class TargetDto
    {
        public string Kind { get; set; } = string.Empty;

        public string? Name { get; set; }

        public string FeatureName { get; set; } = string.Empty;

        public int Line { get; set; }

        public IReadOnlyList<string> Tags { get; set; } = new List<string>();

        public string Description { get; set; } = string.Empty;
    }

    internal sealed class ProjectDto
    {
        public string Path { get; set; } = string.Empty;

        public string Runner { get; set; } = string.Empty;

        public IReadOnlyList<string> TargetFrameworks { get; set; } = new List<string>();

        public string RootNamespace { get; set; } = string.Empty;
    }

    internal sealed class FilterDto
    {
        public string Expression { get; set; } = string.Empty;

        public string Strategy { get; set; } = string.Empty;

        public string Explanation { get; set; } = string.Empty;
    }

    internal sealed class RunDto
    {
        public MappingDto Mapping { get; set; } = new MappingDto();

        public bool Success { get; set; }

        public int ExitCode { get; set; }

        public bool ZeroTestsMatched { get; set; }

        public string? FailureReason { get; set; }

        public double DurationSeconds { get; set; }

        public int Passed { get; set; }

        public int Failed { get; set; }

        public int Skipped { get; set; }

        public IReadOnlyList<TestResultDto> Results { get; set; } = new List<TestResultDto>();

        public static RunDto From(MappingResult mapping, TestRunResult result)
        {
            return new RunDto
            {
                Mapping = MappingDto.From(mapping),
                Success = result.IsSuccess,
                ExitCode = result.ExitCode,
                ZeroTestsMatched = result.ZeroTestsMatched,
                FailureReason = result.FailureReason,
                DurationSeconds = result.Duration.TotalSeconds,
                Passed = result.Passed,
                Failed = result.Failed,
                Skipped = result.Skipped,
                Results = result.Results.Select(r => new TestResultDto
                {
                    DisplayName = r.DisplayName,
                    FullyQualifiedName = r.FullyQualifiedName,
                    Outcome = r.Outcome.ToString(),
                    DurationSeconds = r.Duration.TotalSeconds,
                    ErrorMessage = r.ErrorMessage,
                    StackTrace = r.StackTrace,
                }).ToList(),
            };
        }
    }

    internal sealed class TestResultDto
    {
        public string DisplayName { get; set; } = string.Empty;

        public string? FullyQualifiedName { get; set; }

        public string Outcome { get; set; } = string.Empty;

        public double DurationSeconds { get; set; }

        public string? ErrorMessage { get; set; }

        public string? StackTrace { get; set; }
    }

    /// <summary>The <c>lint --json</c> shape.</summary>
    internal sealed class LintDto
    {
        public string File { get; set; } = string.Empty;

        public bool Parsed { get; set; }

        public bool Clean { get; set; }

        public string? Error { get; set; }

        public IReadOnlyList<DiagnosticDto> Diagnostics { get; set; } = new List<DiagnosticDto>();

        public static LintDto From(string file, ValidationResult result)
        {
            return new LintDto
            {
                File = file,
                Parsed = result.Parsed,
                Clean = result.IsClean,
                Error = result.Error,
                Diagnostics = result.Diagnostics.Select(d => new DiagnosticDto
                {
                    Code = d.Code,
                    Severity = d.Severity.ToString(),
                    Message = d.Message,
                    Line = d.Line,
                    Column = d.Column,
                    Scenario = d.ScenarioName,
                }).ToList(),
            };
        }
    }

    internal sealed class DiagnosticDto
    {
        public string Code { get; set; } = string.Empty;

        public string Severity { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;

        public int Line { get; set; }

        public int Column { get; set; }

        public string? Scenario { get; set; }
    }
}
