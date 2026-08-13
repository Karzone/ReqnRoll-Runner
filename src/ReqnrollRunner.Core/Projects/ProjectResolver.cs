using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using ReqnrollRunner.Core.Model;

namespace ReqnrollRunner.Core.Projects
{
    /// <summary>
    /// Finds the test project that owns a <c>.feature</c> file and works out which unit test
    /// framework Reqnroll generates code for in it.
    /// </summary>
    /// <remarks>
    /// Deliberately a plain XML read rather than an MSBuild evaluation: Core targets
    /// netstandard2.0 and must run inside both a net472 VSIX and a net8.0 CLI without dragging in
    /// <c>Microsoft.Build</c>. We only need package identities, TFMs and the root namespace, all of
    /// which are literal in practice.
    /// </remarks>
    public sealed class ProjectResolver
    {
        /// <summary>Package id → runner, checked in the order SPEC §3.2 requires.</summary>
        private static readonly (string PackageId, RunnerKind Runner)[] RunnerPackages =
        {
            ("Reqnroll.NUnit", RunnerKind.NUnit),
            ("Reqnroll.xUnit", RunnerKind.XUnit),
            ("Reqnroll.MsTest", RunnerKind.MsTest),
        };

        /// <summary>Walks up from <paramref name="featurePath"/> to the nearest <c>.csproj</c>.</summary>
        /// <returns>The resolved project, or <see langword="null"/> with <paramref name="error"/> set.</returns>
        public TestProjectInfo? Resolve(string featurePath, out string? error)
        {
            if (featurePath == null)
            {
                throw new ArgumentNullException(nameof(featurePath));
            }

            string? projectPath = FindNearestProject(featurePath);
            if (projectPath == null)
            {
                error = "No .csproj found above '" + featurePath +
                        "'. A feature file must live inside a test project for us to know what to run.";
                return null;
            }

            var warnings = new List<string>();
            XDocument document;
            try
            {
                document = XDocument.Load(projectPath);
            }
            catch (Exception ex) when (ex is System.Xml.XmlException || ex is IOException)
            {
                error = "Could not read project file '" + projectPath + "': " + ex.Message;
                return null;
            }

            HashSet<string> packages = CollectPackageIds(document);
            foreach (string extra in CollectDirectoryPackageIds(Path.GetDirectoryName(projectPath)!))
            {
                packages.Add(extra);
            }

            RunnerKind runner = RunnerKind.Unknown;
            foreach ((string packageId, RunnerKind kind) in RunnerPackages)
            {
                if (packages.Contains(packageId))
                {
                    runner = kind;
                    break;
                }
            }

            bool hasReqnroll = packages.Any(p =>
                p.Equals("Reqnroll", StringComparison.OrdinalIgnoreCase) ||
                p.StartsWith("Reqnroll.", StringComparison.OrdinalIgnoreCase));

            if (!hasReqnroll)
            {
                error = "No Reqnroll.* package reference found in '" + projectPath +
                        "'. This does not look like a Reqnroll test project.";
                return null;
            }

            if (runner == RunnerKind.Unknown)
            {
                warnings.Add(
                    "Could not tell which test framework this project uses — no Reqnroll.NUnit, " +
                    "Reqnroll.xUnit or Reqnroll.MsTest reference in '" + Path.GetFileName(projectPath) +
                    "'. Falling back to a FullyQualifiedName filter, which works for all three.");
            }

            List<string> frameworks = ReadTargetFrameworks(document);
            if (frameworks.Count > 1)
            {
                warnings.Add(
                    "Project multi-targets (" + string.Join(", ", frameworks) +
                    "); defaulting to " + frameworks[0] + ".");
            }

            error = null;
            return new TestProjectInfo(
                projectPath,
                runner,
                frameworks,
                ReadRootNamespace(document, projectPath),
                hasReqnroll,
                warnings);
        }

        /// <summary>Nearest <c>.csproj</c> at or above the file's own directory.</summary>
        public static string? FindNearestProject(string featurePath)
        {
            string? directory = File.Exists(featurePath)
                ? Path.GetDirectoryName(Path.GetFullPath(featurePath))
                : Path.GetFullPath(featurePath);

            while (!string.IsNullOrEmpty(directory))
            {
                string[] projects;
                try
                {
                    projects = Directory.GetFiles(directory!, "*.csproj", SearchOption.TopDirectoryOnly);
                }
                catch (IOException)
                {
                    return null;
                }
                catch (UnauthorizedAccessException)
                {
                    return null;
                }

                if (projects.Length > 0)
                {
                    // Deterministic when a directory somehow holds more than one project file.
                    Array.Sort(projects, StringComparer.Ordinal);
                    return projects[0];
                }

                DirectoryInfo? parent = Directory.GetParent(directory!);
                directory = parent?.FullName;
            }

            return null;
        }

        private static HashSet<string> CollectPackageIds(XDocument document)
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (XElement element in document.Descendants())
            {
                string name = element.Name.LocalName;
                if (name != "PackageReference" && name != "GlobalPackageReference" && name != "PackageVersion")
                {
                    continue;
                }

                string? include = (string?)element.Attribute("Include") ?? (string?)element.Attribute("Update");
                if (!string.IsNullOrWhiteSpace(include))
                {
                    ids.Add(include!.Trim());
                }
            }

            return ids;
        }

        /// <summary>
        /// Central Package Management and shared props: a project can inherit its Reqnroll reference
        /// from <c>Directory.Packages.props</c> or <c>Directory.Build.props</c> further up the tree
        /// (SPEC §6 case 10), so those count as references too.
        /// </summary>
        private static IEnumerable<string> CollectDirectoryPackageIds(string startDirectory)
        {
            var found = new List<string>();
            string? directory = startDirectory;

            while (!string.IsNullOrEmpty(directory))
            {
                foreach (string fileName in new[] { "Directory.Packages.props", "Directory.Build.props" })
                {
                    string candidate = Path.Combine(directory!, fileName);
                    if (!File.Exists(candidate))
                    {
                        continue;
                    }

                    try
                    {
                        found.AddRange(CollectPackageIds(XDocument.Load(candidate)));
                    }
                    catch (Exception ex) when (ex is System.Xml.XmlException || ex is IOException)
                    {
                        // A malformed shared props file must not stop us resolving the project.
                    }
                }

                DirectoryInfo? parent = Directory.GetParent(directory!);
                directory = parent?.FullName;
            }

            return found;
        }

        private static List<string> ReadTargetFrameworks(XDocument document)
        {
            var frameworks = new List<string>();

            foreach (XElement element in document.Descendants().Where(e => e.Name.LocalName == "TargetFrameworks"))
            {
                foreach (string tfm in element.Value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string trimmed = tfm.Trim();
                    if (trimmed.Length > 0 && !trimmed.Contains("$(") && !frameworks.Contains(trimmed))
                    {
                        frameworks.Add(trimmed);
                    }
                }
            }

            if (frameworks.Count == 0)
            {
                foreach (XElement element in document.Descendants().Where(e => e.Name.LocalName == "TargetFramework"))
                {
                    string trimmed = element.Value.Trim();
                    if (trimmed.Length > 0 && !trimmed.Contains("$(") && !frameworks.Contains(trimmed))
                    {
                        frameworks.Add(trimmed);
                    }
                }
            }

            return frameworks;
        }

        private static string ReadRootNamespace(XDocument document, string projectPath)
        {
            XElement? element = document.Descendants().FirstOrDefault(e => e.Name.LocalName == "RootNamespace");
            string? value = element?.Value.Trim();

            // An unexpanded MSBuild property is useless to us; MSBuild's own default is the file name.
            if (string.IsNullOrEmpty(value) || value!.Contains("$("))
            {
                return Path.GetFileNameWithoutExtension(projectPath);
            }

            return value;
        }
    }
}
