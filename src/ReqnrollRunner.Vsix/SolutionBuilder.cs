using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;

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

        /// <summary>How long to wait for a build before giving up and saying so.</summary>
        private static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(30);

        /// <summary>How often to ask Visual Studio whether the build has finished.</summary>
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

        /// <summary>
        /// Builds the project containing <paramref name="projectPath"/>, without blocking the UI.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This used to call <c>BuildProject(…, WaitForBuildToFinish: true)</c> on the UI thread,
        /// which is the obvious way to write it and is a trap. It blocks Visual Studio's message
        /// pump for the whole build, and if the build then needs the UI thread for anything —
        /// a prompt, a project reload, a designer — nothing can ever complete. Visual Studio detects
        /// the state and offers "an operation is blocking user input… shut down anyway?", which is
        /// where a real user ended up: a hung IDE and no way out but Task Manager.
        /// </para>
        /// <para>
        /// So the build is kicked off asynchronously and awaited by polling <c>BuildState</c> from a
        /// background thread, hopping onto the UI thread only for each brief property read. The pump
        /// keeps running, the IDE stays responsive, cancelling the run cancels the build, and a build
        /// that never finishes times out with a sentence instead of a locked window.
        /// </para>
        /// <para>
        /// Polling rather than <c>IVsSolutionBuildManager.AdviseUpdateSolutionEvents</c> is a
        /// deliberate trade. The event is the more correct mechanism, but it means a COM event sink
        /// whose lifetime has to be got exactly right, and getting it wrong reintroduces a hang — the
        /// precise failure being fixed here. A quarter-second poll cannot hang.
        /// </para>
        /// </remarks>
        /// <returns><see langword="true"/> when the build succeeded, or was skipped harmlessly.</returns>
        public static async Task<bool> BuildProjectAsync(
            JoinableTaskFactory joinableTaskFactory,
            DTE2 dte,
            string projectPath,
            Action<string> log,
            CancellationToken cancellationToken)
        {
            await joinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

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
            string uniqueName;
            string projectName;
            try
            {
                configuration = solutionBuild.ActiveConfiguration.Name;
                uniqueName = project.UniqueName;
                projectName = project.Name;
            }
            catch (Exception ex)
            {
                // A solution with no active configuration, or one still loading. Not worth failing
                // the run over — dotnet test can find the existing build on its own.
                log("Could not read the active solution configuration (" + ex.GetType().Name +
                    "), so the project was not rebuilt. Running against the existing build.");
                return true;
            }

            log("Building " + projectName + " (" + configuration + ")…");

            try
            {
                // WaitForBuildToFinish: FALSE. This returns immediately and the build runs on
                // Visual Studio's own schedule; see the remarks above for why the alternative is a
                // hang rather than merely a pause.
                solutionBuild.BuildProject(configuration, uniqueName, WaitForBuildToFinish: false);
            }
            catch (Exception ex)
            {
                log("Visual Studio could not start the build: " + ex.Message);
                log("Build the solution yourself and try again, or turn on Tools → Options → " +
                    "Reqnroll Runner → Skip build before run.");
                return false;
            }

            return await WaitForBuildAsync(
                joinableTaskFactory, dte, solutionBuild, log, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Polls until the build finishes, keeping the UI thread free between reads.</summary>
        private static async Task<bool> WaitForBuildAsync(
            JoinableTaskFactory joinableTaskFactory,
            DTE2 dte,
            SolutionBuild solutionBuild,
            Action<string> log,
            CancellationToken cancellationToken)
        {
            var elapsed = Stopwatch.StartNew();
            bool everSawItRunning = false;

            while (true)
            {
                // Off the UI thread for the wait itself — this is the whole point.
                await TaskScheduler.Default;

                try
                {
                    await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    await CancelBuildAsync(joinableTaskFactory, dte, solutionBuild, log);
                    throw;
                }

                await joinableTaskFactory.SwitchToMainThreadAsync(CancellationToken.None);

                vsBuildState state;
                try
                {
                    state = solutionBuild.BuildState;
                }
                catch (Exception ex)
                {
                    log("Lost track of the build (" + ex.GetType().Name +
                        "). Running against whatever was produced.");
                    return true;
                }

                if (state == vsBuildState.vsBuildStateInProgress)
                {
                    everSawItRunning = true;
                }
                else if (everSawItRunning || elapsed.Elapsed > TimeSpan.FromSeconds(2))
                {
                    // The two-second grace covers the race between BuildProject returning and the
                    // build actually starting: without it, an up-to-date project that is still
                    // reporting vsBuildStateDone from the PREVIOUS build would be read as "finished"
                    // before this one had begun.
                    break;
                }

                if (elapsed.Elapsed > BuildTimeout)
                {
                    log("The build has not finished after " + (int)BuildTimeout.TotalMinutes +
                        " minutes, so the run was abandoned. Visual Studio is still building.");
                    return false;
                }
            }

            int failures;
            try
            {
                failures = solutionBuild.LastBuildInfo;
            }
            catch (Exception)
            {
                return true;
            }

            if (failures != 0)
            {
                log("Build failed (" + failures + " project(s) failed). See the Error List.");
                return false;
            }

            return true;
        }

        /// <summary>Stops an in-flight build when the run it belongs to is cancelled.</summary>
        /// <remarks>
        /// Through <c>ExecuteCommand</c> because <c>SolutionBuild</c> exposes no Cancel of its own —
        /// <c>Build.Cancel</c> is the same command the Build menu invokes.
        /// </remarks>
        private static async Task CancelBuildAsync(
            JoinableTaskFactory joinableTaskFactory,
            DTE2 dte,
            SolutionBuild solutionBuild,
            Action<string> log)
        {
            await joinableTaskFactory.SwitchToMainThreadAsync(CancellationToken.None);

            try
            {
                if (solutionBuild.BuildState == vsBuildState.vsBuildStateInProgress)
                {
                    dte.ExecuteCommand("Build.Cancel");
                    log("Build cancelled.");
                }
            }
            catch (Exception)
            {
                // Nothing useful to do — the run is being cancelled either way.
            }
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
