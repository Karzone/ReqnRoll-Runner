using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace ReqnrollRunner.Core.Tests
{
    /// <summary>
    /// Source-level guards on the Visual Studio extension.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The VSIX cannot be exercised by any automated test — it needs a running Visual Studio, and CI
    /// can only compile its C#. That leaves a class of defect with no safety net at all: code that
    /// compiles perfectly, passes every test, and hangs the IDE.
    /// </para>
    /// <para>
    /// One such defect shipped. <c>SolutionBuild.BuildProject(…, WaitForBuildToFinish: true)</c> on
    /// the UI thread blocks Visual Studio's message pump for the whole build; when the build then
    /// needed the UI thread, nothing could complete, and the user was left with "Visual Studio has
    /// detected that an operation is blocking user input… shut down anyway?" and Task Manager. It
    /// was undetectable on Linux and looked entirely reasonable in review.
    /// </para>
    /// <para>
    /// So the rule is enforced against the source text, from disk. This is a blunt instrument and it
    /// only catches the literal form — but the literal form is what someone reaches for, because it
    /// is the obvious way to write it.
    /// </para>
    /// </remarks>
    public sealed class VsixThreadingRulesTests
    {
        private static IReadOnlyList<(string Path, string Text)> VsixSources()
        {
            string root = Path.Combine(Fixtures.RepositoryRoot, "src", "ReqnrollRunner.Vsix");

            return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                .Where(p => !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) &&
                            !p.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
                .Select(p => (Path: p, Text: File.ReadAllText(p)))
                .ToList();
        }

        /// <summary>The same files with comments removed, for rules about what the code does.</summary>
        /// <remarks>
        /// Necessary, not fastidious: the first version of the build rule below failed on
        /// <c>SolutionBuilder.cs</c> — not because of the call, which had been replaced, but because
        /// the doc comment above the replacement *names the forbidden pattern while explaining why it
        /// is forbidden*. A guard that makes the bug undocumentable is worse than no guard.
        /// </remarks>
        private static IReadOnlyList<(string Path, string Text)> VsixCodeWithoutComments()
        {
            return VsixSources().Select(s => (s.Path, Text: StripComments(s.Text))).ToList();
        }

        private static string StripComments(string source)
        {
            var kept = new System.Text.StringBuilder(source.Length);
            bool inBlockComment = false;

            foreach (string line in source.Split('\n'))
            {
                string remaining = line;

                while (true)
                {
                    if (inBlockComment)
                    {
                        int end = remaining.IndexOf("*/", StringComparison.Ordinal);
                        if (end < 0)
                        {
                            remaining = string.Empty;
                            break;
                        }

                        inBlockComment = false;
                        remaining = remaining.Substring(end + 2);
                        continue;
                    }

                    int block = remaining.IndexOf("/*", StringComparison.Ordinal);
                    int slash = remaining.IndexOf("//", StringComparison.Ordinal);

                    if (slash >= 0 && (block < 0 || slash < block))
                    {
                        remaining = remaining.Substring(0, slash);
                        break;
                    }

                    if (block >= 0)
                    {
                        inBlockComment = true;
                        remaining = remaining.Substring(0, block) + remaining.Substring(block + 2);
                        continue;
                    }

                    break;
                }

                kept.Append(remaining).Append('\n');
            }

            return kept.ToString();
        }

        [Fact]
        public void The_extension_has_sources_to_check()
        {
            // Vacuity guard. Every assertion below is "no file contains X", which passes trivially if
            // the glob stopped finding files — exactly how a compile check in this repository once
            // went green while checking nothing at all.
            IReadOnlyList<(string Path, string Text)> sources = VsixSources();

            Assert.True(sources.Count >= 8, "expected the VSIX to have at least 8 source files, found " + sources.Count);
            Assert.Contains(sources, s => Path.GetFileName(s.Path) == "SolutionBuilder.cs");

            // And that stripping comments leaves real code behind rather than blanking the file —
            // the rules below scan the stripped text, so an over-eager stripper would silence them.
            string stripped = VsixCodeWithoutComments()
                .Single(s => Path.GetFileName(s.Path) == "SolutionBuilder.cs").Text;

            Assert.Contains("BuildProjectAsync", stripped);
            Assert.DoesNotContain("Logged BEFORE the search", stripped);
        }

        [Fact]
        public void Nothing_waits_for_a_build_on_the_calling_thread()
        {
            // `WaitForBuildToFinish: true` blocks whichever thread calls it, and every caller here is
            // on the UI thread. See SolutionBuilder.BuildProjectAsync for what to do instead.
            string[] offenders = VsixCodeWithoutComments()
                .Where(s => s.Text.Replace(" ", string.Empty).Contains("WaitForBuildToFinish:true"))
                .Select(s => Path.GetFileName(s.Path))
                .ToArray();

            Assert.True(
                offenders.Length == 0,
                "WaitForBuildToFinish: true blocks the Visual Studio message pump and can deadlock the " +
                "IDE outright. Kick the build off with false and await it by polling BuildState off " +
                "the UI thread, as SolutionBuilder.BuildProjectAsync does. Found in: " +
                string.Join(", ", offenders));
        }

        [Fact]
        public void No_synchronous_blocking_on_a_task()
        {
            // The other half of the same failure. `.Result`, `.Wait()` and `.GetAwaiter().GetResult()`
            // on the UI thread deadlock against any continuation that needs the UI thread back —
            // which, in this codebase, is most of them. JoinableTaskFactory.Run exists for the cases
            // that genuinely cannot be async.
            string[] patterns = { ".GetAwaiter().GetResult()", ".Wait()", ".Result;" };

            var offenders = new List<string>();
            foreach ((string path, string text) in VsixCodeWithoutComments())
            {
                foreach (string pattern in patterns)
                {
                    if (text.Contains(pattern))
                    {
                        offenders.Add(Path.GetFileName(path) + " → " + pattern);
                    }
                }
            }

            Assert.True(
                offenders.Count == 0,
                "Blocking on a Task from the UI thread deadlocks against continuations that need the " +
                "UI thread. Await it, or use JoinableTaskFactory.Run. Found: " +
                string.Join(", ", offenders));
        }

        [Fact]
        public void A_run_started_from_a_command_cannot_fail_silently()
        {
            // The fire-and-forget task that runs a scenario must catch. Without a catch the exception
            // goes to the activity log and the user sees a menu item that does nothing at all —
            // indistinguishable from a command that was never wired up, and unreportable.
            string handler = VsixCodeWithoutComments()
                .Single(s => Path.GetFileName(s.Path) == "ScenarioCommandHandler.cs").Text;

            int fireAndForget = CountOccurrences(handler, "FileAndForget(");
            int catches = CountOccurrences(handler, "catch (Exception");

            Assert.True(fireAndForget > 0, "expected the command handler to start fire-and-forget work");
            Assert.True(
                catches > 0,
                "every fire-and-forget run must catch and report, or a crash looks like a dead menu item");
        }

        private static int CountOccurrences(string text, string value)
        {
            int count = 0;
            int index = 0;

            while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += value.Length;
            }

            return count;
        }
    }
}
