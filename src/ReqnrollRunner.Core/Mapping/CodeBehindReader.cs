using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace ReqnrollRunner.Core.Mapping
{
    /// <summary>One generated test method and the feature line it came from.</summary>
    public sealed class GeneratedTestMethod
    {
        public GeneratedTestMethod(string methodName, int featureLine, bool isParameterised)
        {
            MethodName = methodName;
            FeatureLine = featureLine;
            IsParameterised = isParameterised;
        }

        public string MethodName { get; }

        /// <summary>
        /// 1-based line of the scenario in the <c>.feature</c> file, taken from the first
        /// <c>#line</c> directive in the method body.
        /// </summary>
        public int FeatureLine { get; }

        /// <summary>True for Scenario Outlines — one method, many example rows.</summary>
        public bool IsParameterised { get; }
    }

    /// <summary>The facts we can read out of a generated <c>.feature.cs</c>.</summary>
    public sealed class CodeBehindInfo
    {
        public CodeBehindInfo(string path, string namespaceName, string className, IReadOnlyList<GeneratedTestMethod> methods)
        {
            Path = path;
            Namespace = namespaceName;
            ClassName = className;
            Methods = methods;
        }

        public string Path { get; }

        public string Namespace { get; }

        public string ClassName { get; }

        public IReadOnlyList<GeneratedTestMethod> Methods { get; }

        public string FullyQualifiedClassName =>
            string.IsNullOrEmpty(Namespace) ? ClassName : Namespace + "." + ClassName;

        /// <summary>The generated method for the scenario declared at <paramref name="featureLine"/>.</summary>
        public GeneratedTestMethod? FindByFeatureLine(int featureLine)
        {
            foreach (GeneratedTestMethod method in Methods)
            {
                if (method.FeatureLine == featureLine)
                {
                    return method;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Reads the Reqnroll-generated code-behind for a feature file. This is the project's ground
    /// truth for test names — the generated file is what actually compiles into the test assembly,
    /// so reading it beats reconstructing names from the scenario title.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The join is the <c>#line</c> directive: Reqnroll emits <c>#line &lt;n&gt;</c> as the first
    /// statement of every generated test method, pointing at the scenario's keyword line in the
    /// <c>.feature</c> file. That gives an exact, title-independent scenario → method mapping,
    /// including for localized keywords and titles whose sanitized form is unguessable
    /// (<c>Ünïcödé — スカラー</c> generates <c>Unicodeスカラー</c>).
    /// </para>
    /// <para>
    /// Regex rather than Roslyn on purpose: this is machine-generated code with a fixed shape, Core
    /// has to stay netstandard2.0 and dependency-light, and the reader is pinned by tests against
    /// real generated output from all three runners in <c>samples/</c>.
    /// </para>
    /// </remarks>
    public sealed class CodeBehindReader
    {
        private static readonly Regex NamespaceRegex = new Regex(
            @"^\s*namespace\s+(?<ns>[\w\.]+)\s*[{;]?\s*$",
            RegexOptions.Multiline | RegexOptions.Compiled);

        private static readonly Regex ClassRegex = new Regex(
            @"public\s+partial\s+class\s+(?<name>\w+)",
            RegexOptions.Compiled);

        /// <summary>
        /// A method declaration on its own line. Reqnroll emits the opening brace on the next line,
        /// so anchoring to the end of the line is safe and keeps this a single-line match.
        /// </summary>
        private static readonly Regex MethodRegex = new Regex(
            @"^public\s+(?:async\s+)?(?:virtual\s+)?[\w\.:<>\[\]]+\s+(?<name>\w+)\s*\((?<args>[^)]*)\)\s*$",
            RegexOptions.Compiled);

        private static readonly Regex FirstLineDirectiveRegex = new Regex(
            @"#line\s+(?<n>\d+)",
            RegexOptions.Compiled);

        /// <summary>Attribute markers that identify a generated *test* method, across all three runners.</summary>
        private static readonly string[] TestAttributeMarkers =
        {
            "TestAttribute(",        // NUnit
            "TestCaseAttribute(",    // NUnit outline
            "FactAttribute(",        // xUnit (incl. SkippableFactAttribute)
            "TheoryAttribute(",      // xUnit outline (incl. SkippableTheoryAttribute)
            "TestMethodAttribute(",  // MSTest
        };

        /// <summary>Fixture plumbing that must never be mistaken for a scenario.</summary>
        private static readonly string[] NonTestAttributeMarkers =
        {
            "TestInitializeAttribute(",
            "TestCleanupAttribute(",
            "SetUpAttribute(",
            "TearDownAttribute(",
            "OneTimeSetUpAttribute(",
            "OneTimeTearDownAttribute(",
            "ClassInitializeAttribute(",
            "ClassCleanupAttribute(",
        };

        /// <summary>
        /// Finds the generated code-behind for <paramref name="featurePath"/>, or
        /// <see langword="null"/> if the project has not been built (or generates elsewhere).
        /// </summary>
        public static string? FindCodeBehindPath(string featurePath, string projectDirectory)
        {
            // 1. Reqnroll's default: a sibling <name>.feature.cs next to the feature file.
            string sibling = featurePath + ".cs";
            if (File.Exists(sibling))
            {
                return sibling;
            }

            // 2. Some setups redirect generated code into obj/. Search there, preferring the deepest
            //    match on file name so a multi-targeted project still resolves.
            string objDirectory = Path.Combine(projectDirectory, "obj");
            if (!Directory.Exists(objDirectory))
            {
                return null;
            }

            string wanted = Path.GetFileName(featurePath) + ".cs";
            try
            {
                string[] candidates = Directory.GetFiles(objDirectory, wanted, SearchOption.AllDirectories);
                if (candidates.Length == 0)
                {
                    return null;
                }

                Array.Sort(candidates, StringComparer.Ordinal);
                return candidates[0];
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>Parses a generated code-behind file. Returns <see langword="null"/> if it does not look like one.</summary>
        public CodeBehindInfo? Read(string codeBehindPath)
        {
            string source;
            try
            {
                source = File.ReadAllText(codeBehindPath);
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }

            return Parse(source, codeBehindPath);
        }

        /// <summary>Parses generated source text. Exposed separately so tests can run on fixtures in memory.</summary>
        public CodeBehindInfo? Parse(string source, string path)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            Match classMatch = ClassRegex.Match(source);
            if (!classMatch.Success)
            {
                return null;
            }

            Match namespaceMatch = NamespaceRegex.Match(source);
            string namespaceName = namespaceMatch.Success ? namespaceMatch.Groups["ns"].Value : string.Empty;

            string[] lines = source.Split('\n');

            // Pass 1: every method declaration, by line index.
            var declarations = new List<(int LineIndex, string Name, bool Parameterised)>();
            for (int i = 0; i < lines.Length; i++)
            {
                Match match = MethodRegex.Match(lines[i].Trim('\r', ' ', '\t'));
                if (match.Success)
                {
                    declarations.Add((i, match.Groups["name"].Value, match.Groups["args"].Value.Trim().Length > 0));
                }
            }

            // Pass 2: for each declaration, read its attribute block upwards and its #line downwards.
            var methods = new List<GeneratedTestMethod>();
            for (int d = 0; d < declarations.Count; d++)
            {
                (int lineIndex, string name, bool parameterised) = declarations[d];

                if (!IsTestMethod(ReadAttributeBlock(lines, lineIndex)))
                {
                    continue;
                }

                // The first #line inside the body is the scenario's own line. Stop at the next
                // declaration so a method without a directive cannot borrow the following method's.
                int limit = d + 1 < declarations.Count ? declarations[d + 1].LineIndex : lines.Length;
                int? featureLine = FindFirstLineDirective(lines, lineIndex + 1, limit);
                if (featureLine == null)
                {
                    continue;
                }

                methods.Add(new GeneratedTestMethod(name, featureLine.Value, parameterised));
            }

            return new CodeBehindInfo(path, namespaceName, classMatch.Groups["name"].Value, methods);
        }

        /// <summary>
        /// The attribute lines immediately above a declaration. Generated members are separated by a
        /// blank line, so scanning up to the first blank line captures the whole block — including
        /// attributes that wrap across lines, such as NUnit's multi-line <c>[TestCase(...)]</c>.
        /// </summary>
        private static string ReadAttributeBlock(string[] lines, int declarationIndex)
        {
            var block = new System.Text.StringBuilder();

            for (int i = declarationIndex - 1; i >= 0; i--)
            {
                string line = lines[i];
                if (line.Trim().Length == 0)
                {
                    break;
                }

                block.Insert(0, line + "\n");
            }

            return block.ToString();
        }

        private static int? FindFirstLineDirective(string[] lines, int start, int end)
        {
            for (int i = start; i < end && i < lines.Length; i++)
            {
                Match match = FirstLineDirectiveRegex.Match(lines[i]);
                if (match.Success)
                {
                    return int.Parse(match.Groups["n"].Value, System.Globalization.CultureInfo.InvariantCulture);
                }
            }

            return null;
        }

        private static bool IsTestMethod(string attributes)
        {
            foreach (string marker in NonTestAttributeMarkers)
            {
                if (attributes.IndexOf(marker, StringComparison.Ordinal) >= 0)
                {
                    return false;
                }
            }

            foreach (string marker in TestAttributeMarkers)
            {
                if (attributes.IndexOf(marker, StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
