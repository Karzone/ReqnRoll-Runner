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

            // Logged BEFORE the search, not after. The search walks the whole solution through COM,
            // and if it dies there the user needs to know the runner got this far — a pane that
            // stops dead after the filter line says nothing about which step failed.
            log("Locating " + Path.GetFileName(projectPath) + " in the solution…");

            Project? project = FindProject(dte, projectPath, log);
            if (project == null)
            {
                // Better to run against whatever was last built than to refuse outright — the run
                // itself will report a stale or missing assembly clearly enough.
                log("Not found in the open solution, so it was not rebuilt. Running against the existing build.");
                return true;
            }

            SolutionBuild solutionBuild = dte.Solution.SolutionBuild;

            string configuration;
            try
            {
                configuration = solutionBuild.ActiveConfiguration.Name;
            }
            catch (Exception ex)
            {
                // A solution with no active configuration, or one still loading. Not worth failing
                // the run over — dotnet test can find the existing build on its own.
                log("Could not read the active solution configuration (" + ex.GetType().Name +
                    "), so the project was not rebuilt. Running against the existing build.");
                return true;
            }

            log("Building " + project.Name + " (" + configuration + ")…");

            try
            {
                // waitForBuildToFinish: true — synchronous, so the caller can trust LastBuildInfo
                // below. Note this blocks the UI thread for the duration; see issue #13.
                solutionBuild.BuildProject(configuration, project.UniqueName, WaitForBuildToFinish: true);
            }
            catch (Exception ex)
            {
                log("Visual Studio could not build the project: " + ex.Message);
                log("Build the solution yourself and try again, or turn on Tools → Options → " +
                    "Reqnroll Runner → Skip build before run.");
                return false;
            }

            int failures = solutionBuild.LastBuildInfo;
            if (failures != 0)
            {
                log("Build failed (" + failures + " project(s) failed). See the Error List.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Finds the open project whose file is <paramref name="projectPath"/>, or
        /// <see langword="null"/> if the solution does not contain it.
        /// </summary>
        /// <remarks>
        /// Every COM call in here is treated as capable of throwing, because in a real solution they
        /// are. <c>Solution.Projects</c>, <c>Project.ProjectItems</c> and <c>ProjectItem.SubProject</c>
        /// all throw for some project types — shared projects, database projects, unloaded projects,
        /// anything still loading — and the automation model does not document which. One such
        /// project anywhere in the solution used to abort the whole walk with an exception that
        /// reached a fire-and-forget task and vanished, so the run stopped after printing its filter
        /// and said nothing more. Reported against a real multi-project solution.
        ///
        /// A project we cannot inspect is skipped, not fatal: the worst case is that the build is
        /// missed and the run uses the existing binaries, which is what the caller already does when
        /// the project is not in the solution at all.
        /// </remarks>
        private static Project? FindProject(DTE2 dte, string projectPath, Action<string> log)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            string full;
            try
            {
                full = Path.GetFullPath(projectPath);
            }
            catch (Exception)
            {
                return null;
            }

            foreach (Project project in EnumerateProjects(dte, log))
            {
                string? fileName = TryGetFullName(project);
                if (fileName == null)
                {
                    continue;
                }

                string resolved;
                try
                {
                    resolved = Path.GetFullPath(fileName);
                }
                catch (Exception)
                {
                    continue;
                }

                if (string.Equals(resolved, full, StringComparison.OrdinalIgnoreCase))
                {
                    return project;
                }
            }

            return null;
        }

        /// <summary>Flattens solution folders, which nest real projects inside ProjectItems.</summary>
        private static System.Collections.Generic.IEnumerable<Project> EnumerateProjects(DTE2 dte, Action<string> log)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            Projects? projects;
            try
            {
                projects = dte.Solution?.Projects;
            }
            catch (Exception ex)
            {
                log("Could not enumerate the solution's projects (" + ex.GetType().Name + ").");
                yield break;
            }

            if (projects == null)
            {
                yield break;
            }

            // Materialised eagerly and defensively: a `foreach` straight over the COM collection
            // cannot be wrapped in try/catch around a `yield return`, and it is the enumeration
            // itself that throws.
            var roots = new System.Collections.Generic.List<Project>();
            try
            {
                foreach (Project project in projects)
                {
                    roots.Add(project);
                }
            }
            catch (Exception ex)
            {
                log("Stopped enumerating the solution's projects early (" + ex.GetType().Name +
                    "); " + roots.Count + " found so far.");
            }

            foreach (Project root in roots)
            {
                foreach (Project nested in Flatten(root))
                {
                    yield return nested;
                }
            }
        }

        private static System.Collections.Generic.IEnumerable<Project> Flatten(Project project)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            yield return project;

            foreach (Project nested in Children(project))
            {
                foreach (Project deeper in Flatten(nested))
                {
                    yield return deeper;
                }
            }
        }

        /// <summary>Sub-projects nested inside a solution folder. Never throws.</summary>
        private static System.Collections.Generic.List<Project> Children(Project project)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var children = new System.Collections.Generic.List<Project>();

            try
            {
                ProjectItems? items = project.ProjectItems;
                if (items == null)
                {
                    return children;
                }

                foreach (ProjectItem item in items)
                {
                    try
                    {
                        Project? sub = item.SubProject;
                        if (sub != null)
                        {
                            children.Add(sub);
                        }
                    }
                    catch (Exception)
                    {
                        // This one item cannot report a sub-project. The rest still can.
                    }
                }
            }
            catch (Exception)
            {
                // Project types that do not implement ProjectItems at all.
            }

            return children;
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
