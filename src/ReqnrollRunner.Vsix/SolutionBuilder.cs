using System;
using System.IO;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;

namespace ReqnrollRunner.Vsix
{
    /// <summary>Builds the project that owns a feature file, through Visual Studio (SPEC §4.2 step 3).</summary>
    /// <remarks>
    /// Building through the IDE rather than letting <c>dotnet test</c> do it is a deliberate choice:
    /// compile errors land in the Error List with clickable squiggles, which is the whole reason a
    /// user is in Visual Studio. <c>dotnet test</c> is then invoked with <c>--no-build</c>, so the
    /// work is not done twice.
    /// </remarks>
    internal static class SolutionBuilder
    {
        /// <summary>
        /// Visual Studio's active solution configuration — <c>Debug</c>, <c>Release</c>, or whatever
        /// the user has selected.
        /// </summary>
        /// <remarks>
        /// This has to be handed to <c>dotnet test</c>. It defaults to Debug on its own, so a user
        /// with Release selected in the IDE would otherwise get "The argument …/bin/Debug/….dll is
        /// invalid" — a message that says nothing about the actual problem.
        /// </remarks>
        public static string? GetActiveConfiguration(DTE2 dte)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                return dte.Solution?.SolutionBuild?.ActiveConfiguration?.Name;
            }
            catch (Exception)
            {
                // No solution open, or a solution type that does not expose configurations.
                return null;
            }
        }

        /// <summary>
        /// Builds the project containing <paramref name="projectPath"/> and waits for it to finish.
        /// </summary>
        /// <returns><see langword="true"/> when the build succeeded.</returns>
        public static bool BuildProject(DTE2 dte, string projectPath, Action<string> log)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            Project? project = FindProject(dte, projectPath);
            if (project == null)
            {
                // Better to run against whatever was last built than to refuse outright — the run
                // itself will report a stale or missing assembly clearly enough.
                log("Could not find '" + Path.GetFileName(projectPath) +
                    "' in the open solution, so it was not rebuilt. Running against the existing build.");
                return true;
            }

            SolutionBuild solutionBuild = dte.Solution.SolutionBuild;
            string configuration = solutionBuild.ActiveConfiguration.Name;

            log("Building " + project.Name + " (" + configuration + ")…");

            // waitForBuildToFinish: true — synchronous, so the caller can trust LastBuildInfo below.
            solutionBuild.BuildProject(configuration, project.UniqueName, WaitForBuildToFinish: true);

            int failures = solutionBuild.LastBuildInfo;
            if (failures != 0)
            {
                log("Build failed (" + failures + " project(s) failed). See the Error List.");
                return false;
            }

            return true;
        }

        private static Project? FindProject(DTE2 dte, string projectPath)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            string full = Path.GetFullPath(projectPath);

            foreach (Project project in EnumerateProjects(dte))
            {
                string? fileName = TryGetFullName(project);
                if (fileName != null &&
                    string.Equals(Path.GetFullPath(fileName), full, StringComparison.OrdinalIgnoreCase))
                {
                    return project;
                }
            }

            return null;
        }

        /// <summary>Flattens solution folders, which nest real projects inside ProjectItems.</summary>
        private static System.Collections.Generic.IEnumerable<Project> EnumerateProjects(DTE2 dte)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            foreach (Project project in dte.Solution.Projects)
            {
                foreach (Project nested in Flatten(project))
                {
                    yield return nested;
                }
            }
        }

        private static System.Collections.Generic.IEnumerable<Project> Flatten(Project project)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            yield return project;

            if (project.ProjectItems == null)
            {
                yield break;
            }

            foreach (ProjectItem item in project.ProjectItems)
            {
                if (item.SubProject == null)
                {
                    continue;
                }

                foreach (Project nested in Flatten(item.SubProject))
                {
                    yield return nested;
                }
            }
        }

        private static string? TryGetFullName(Project project)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                return project.FullName;
            }
            catch (Exception)
            {
                // Solution folders and some project types throw rather than returning empty.
                return null;
            }
        }
    }
}
