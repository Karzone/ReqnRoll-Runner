using System.Collections.Generic;
using System.IO;
using ReqnrollRunner.Core.Model;
using ReqnrollRunner.Core.Parsing;
using ReqnrollRunner.Core.Projects;

namespace ReqnrollRunner.Core.Mapping
{
    /// <summary>
    /// The heart of the project: caret position in a <c>.feature</c> file → the exact
    /// <c>dotnet test --filter</c> expression that runs it.
    /// </summary>
    /// <remarks>
    /// Deterministic and side-effect free — it reads files and returns a value. That is what makes
    /// <c>reqnroll-runner map</c> a usable dry-run harness and what keeps the whole mapping layer
    /// unit-testable without Visual Studio.
    /// </remarks>
    public sealed class ScenarioMapper
    {
        private readonly FeatureFileParser _parser;
        private readonly ProjectResolver _projectResolver;
        private readonly CodeBehindReader _codeBehindReader;

        public ScenarioMapper()
            : this(new FeatureFileParser(), new ProjectResolver(), new CodeBehindReader())
        {
        }

        public ScenarioMapper(FeatureFileParser parser, ProjectResolver projectResolver, CodeBehindReader codeBehindReader)
        {
            _parser = parser;
            _projectResolver = projectResolver;
            _codeBehindReader = codeBehindReader;
        }

        /// <summary>Resolves everything needed to run the target at <paramref name="line"/>.</summary>
        /// <param name="featurePath">Path to the <c>.feature</c> file.</param>
        /// <param name="line">1-based caret line.</param>
        public MappingResult Map(string featurePath, int line)
        {
            string fullPath = Path.GetFullPath(featurePath);

            FeatureParseResult parsed = _parser.Resolve(fullPath, line);
            if (!parsed.Success || parsed.Target == null)
            {
                return MappingResult.Fail(fullPath, line, parsed.Error ?? "Could not resolve a target in the feature file.");
            }

            ScenarioTarget target = parsed.Target;

            TestProjectInfo? project = _projectResolver.Resolve(fullPath, out string? projectError);
            if (project == null)
            {
                return MappingResult.Fail(fullPath, line, projectError ?? "Could not resolve the test project.");
            }

            var warnings = new List<string>(project.Warnings);

            string projectDirectory = Path.GetDirectoryName(project.ProjectPath)!;
            string? codeBehindPath = CodeBehindReader.FindCodeBehindPath(fullPath, projectDirectory);
            CodeBehindInfo? codeBehind = codeBehindPath == null ? null : _codeBehindReader.Read(codeBehindPath);

            if (codeBehind != null)
            {
                return MapFromCodeBehind(fullPath, line, target, project, codeBehind, warnings);
            }

            warnings.Add(
                "No generated code-behind found for " + Path.GetFileName(fullPath) +
                " — build the project for an exact match. Falling back to names reconstructed from the titles.");

            return MapFromSanitizedNames(fullPath, line, target, project, warnings);
        }

        /// <summary>
        /// The exact path. Names come from the file Reqnroll actually compiled, joined to the
        /// scenario by the <c>#line</c> directive rather than by title, so localization and
        /// unguessable sanitization are both non-issues.
        /// </summary>
        private static MappingResult MapFromCodeBehind(
            string featurePath,
            int line,
            ScenarioTarget target,
            TestProjectInfo project,
            CodeBehindInfo codeBehind,
            List<string> warnings)
        {
            string className = codeBehind.FullyQualifiedClassName;

            if (target.RunsWholeFeature)
            {
                string reason = target.Kind == TargetKind.Rule
                    ? "Caret is on a Rule; v1 runs every scenario in the feature class '" + codeBehind.ClassName + "'."
                    : "Running every scenario in the generated feature class '" + codeBehind.ClassName + "'.";

                return MappingResult.Ok(
                    featurePath, line, target, project,
                    TestFilterBuilder.ForFeatureClass(className, FilterStrategy.CodeBehind, reason),
                    className, null, warnings);
            }

            GeneratedTestMethod? rowMethod = codeBehind.FindByFeatureLine(target.Line);
            if (target.Kind == TargetKind.ExampleRow && target.ExampleRow != null && rowMethod != null)
            {
                TestFilter rowFilter = TestFilterBuilder.ForExampleRow(
                    className,
                    rowMethod.MethodName,
                    project.Runner,
                    target.ExampleRow.Values,
                    target.ExampleRow.OrdinalWithinOutline,
                    out bool widened);

                if (widened)
                {
                    warnings.Add(rowFilter.Explanation);
                }

                return MappingResult.Ok(
                    featurePath, line, target, project, rowFilter, className, rowMethod.MethodName, warnings);
            }

            GeneratedTestMethod? method = codeBehind.FindByFeatureLine(target.Line);
            if (method == null)
            {
                // The code-behind exists but predates an edit to the feature file. Running the whole
                // feature is wrong-but-safe; running a stale method name would silently run the wrong
                // scenario, which is worse.
                warnings.Add(
                    "The generated code-behind has no test for the scenario on line " + target.Line +
                    " — it is probably out of date. Rebuild the project. Running the whole feature instead.");

                return MappingResult.Ok(
                    featurePath, line, target, project,
                    TestFilterBuilder.ForFeatureClass(
                        className,
                        FilterStrategy.FeatureScopeFallback,
                        "Stale code-behind; widened to the whole feature class '" + codeBehind.ClassName + "'."),
                    className, null, warnings);
            }

            return MappingResult.Ok(
                featurePath, line, target, project,
                TestFilterBuilder.ForGeneratedMethod(className, method.MethodName, target.Kind),
                className, method.MethodName, warnings);
        }

        /// <summary>The best-effort path, used when the project has never been built.</summary>
        private static MappingResult MapFromSanitizedNames(
            string featurePath,
            int line,
            ScenarioTarget target,
            TestProjectInfo project,
            List<string> warnings)
        {
            string className = GuessFullyQualifiedClassName(featurePath, project, target.FeatureName);

            if (target.RunsWholeFeature)
            {
                return MappingResult.Ok(
                    featurePath, line, target, project,
                    TestFilterBuilder.ForFeatureClass(
                        className,
                        FilterStrategy.Sanitized,
                        "Feature class name reconstructed from the feature title."),
                    className, null, warnings);
            }

            string method = TestNameSanitizer.MethodName(target.Name ?? string.Empty);
            TestFilter filter = TestFilterBuilder.ForSanitizedGuess(
                className, method, target.Name ?? string.Empty, out bool titleClauseDropped);

            if (titleClauseDropped)
            {
                warnings.Add(
                    "The scenario title could not be safely embedded in a filter, so only the " +
                    "reconstructed method name is matched. Build the project for an exact filter.");
            }

            return MappingResult.Ok(featurePath, line, target, project, filter, className, method, warnings);
        }

        /// <summary>
        /// Reproduces the namespace Reqnroll generates: root namespace plus the feature file's
        /// folder path relative to the project.
        /// </summary>
        private static string GuessFullyQualifiedClassName(string featurePath, TestProjectInfo project, string featureName)
        {
            string className = TestNameSanitizer.FeatureClassName(featureName);
            string projectDirectory = Path.GetDirectoryName(project.ProjectPath)!;
            string featureDirectory = Path.GetDirectoryName(featurePath)!;

            var segments = new List<string> { project.RootNamespace };

            if (featureDirectory.Length > projectDirectory.Length &&
                featureDirectory.StartsWith(projectDirectory, System.StringComparison.OrdinalIgnoreCase))
            {
                string relative = featureDirectory.Substring(projectDirectory.Length)
                    .Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                foreach (string part in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                {
                    if (part.Length > 0)
                    {
                        segments.Add(TestNameSanitizer.ToIdentifier(part));
                    }
                }
            }

            segments.Add(className);
            return string.Join(".", segments.ToArray());
        }
    }
}
