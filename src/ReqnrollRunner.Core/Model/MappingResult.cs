using System.Collections.Generic;

namespace ReqnrollRunner.Core.Model
{
    /// <summary>
    /// The complete answer to "what should I run for this caret position?" — the output of
    /// <c>reqnroll-runner map</c> and the input to every execution path.
    /// </summary>
    public sealed class MappingResult
    {
        private MappingResult(
            bool success,
            string featurePath,
            int line,
            ScenarioTarget? target,
            TestProjectInfo? project,
            TestFilter? filter,
            string? generatedTypeName,
            string? generatedMethodName,
            IReadOnlyList<string> warnings,
            string? error)
        {
            Success = success;
            FeaturePath = featurePath;
            Line = line;
            Target = target;
            Project = project;
            Filter = filter;
            GeneratedTypeName = generatedTypeName;
            GeneratedMethodName = generatedMethodName;
            Warnings = warnings;
            Error = error;
        }

        public bool Success { get; }

        public string FeaturePath { get; }

        public int Line { get; }

        public ScenarioTarget? Target { get; }

        public TestProjectInfo? Project { get; }

        public TestFilter? Filter { get; }

        /// <summary>Fully qualified generated fixture type, when known.</summary>
        public string? GeneratedTypeName { get; }

        /// <summary>Generated test method, when a single scenario was addressed.</summary>
        public string? GeneratedMethodName { get; }

        public IReadOnlyList<string> Warnings { get; }

        /// <summary>Human-readable reason mapping failed. Non-null exactly when <see cref="Success"/> is false.</summary>
        public string? Error { get; }

        public static MappingResult Ok(
            string featurePath,
            int line,
            ScenarioTarget target,
            TestProjectInfo project,
            TestFilter filter,
            string? generatedTypeName,
            string? generatedMethodName,
            IReadOnlyList<string> warnings)
        {
            return new MappingResult(
                true, featurePath, line, target, project, filter,
                generatedTypeName, generatedMethodName, warnings, null);
        }

        public static MappingResult Fail(string featurePath, int line, string error, IReadOnlyList<string>? warnings = null)
        {
            return new MappingResult(
                false, featurePath, line, null, null, null, null, null,
                warnings ?? new List<string>(), error);
        }
    }
}
