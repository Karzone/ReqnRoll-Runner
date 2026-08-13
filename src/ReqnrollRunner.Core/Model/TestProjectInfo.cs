using System.Collections.Generic;

namespace ReqnrollRunner.Core.Model
{
    /// <summary>The test project that owns a <c>.feature</c> file.</summary>
    public sealed class TestProjectInfo
    {
        public TestProjectInfo(
            string projectPath,
            RunnerKind runner,
            IReadOnlyList<string> targetFrameworks,
            string rootNamespace,
            bool hasReqnrollReference,
            IReadOnlyList<string> warnings)
        {
            ProjectPath = projectPath;
            Runner = runner;
            TargetFrameworks = targetFrameworks;
            RootNamespace = rootNamespace;
            HasReqnrollReference = hasReqnrollReference;
            Warnings = warnings;
        }

        /// <summary>Absolute path of the <c>.csproj</c>.</summary>
        public string ProjectPath { get; }

        public RunnerKind Runner { get; }

        /// <summary>
        /// Every TFM declared by the project, in declaration order. Empty when the project
        /// declares none we could read.
        /// </summary>
        public IReadOnlyList<string> TargetFrameworks { get; }

        /// <summary>
        /// Explicit <c>&lt;RootNamespace&gt;</c>, or the project file name when absent —
        /// matching MSBuild's own default.
        /// </summary>
        public string RootNamespace { get; }

        /// <summary>True when any <c>Reqnroll*</c> package is referenced at all.</summary>
        public bool HasReqnrollReference { get; }

        public IReadOnlyList<string> Warnings { get; }

        /// <summary>
        /// The TFM to run under, honouring an explicit preference when the project multi-targets.
        /// Returns <see langword="null"/> for a single-targeted project — <c>dotnet test</c> then
        /// needs no <c>--framework</c> at all.
        /// </summary>
        public string? ResolveFramework(string? preferred)
        {
            if (TargetFrameworks.Count <= 1)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(preferred))
            {
                foreach (string tfm in TargetFrameworks)
                {
                    if (string.Equals(tfm, preferred, System.StringComparison.OrdinalIgnoreCase))
                    {
                        return tfm;
                    }
                }
            }

            return TargetFrameworks[0];
        }
    }
}
