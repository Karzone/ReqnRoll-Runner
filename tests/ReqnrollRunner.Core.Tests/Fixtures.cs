using System;
using System.IO;

namespace ReqnrollRunner.Core.Tests
{
    /// <summary>
    /// Locates <c>tests/fixtures</c> at run time.
    /// </summary>
    /// <remarks>
    /// Fixtures are read from the repository rather than copied into the test output, because several
    /// of them are whole project trees (<c>.csproj</c> + <c>Directory.Packages.props</c> + feature
    /// files) whose <em>directory layout</em> is the thing under test — <c>ProjectResolver</c> walks
    /// up from a feature file looking for a project, so flattening them into an output folder would
    /// destroy exactly what the fixture exists to exercise.
    /// </remarks>
    internal static class Fixtures
    {
        private static readonly Lazy<string> RootPath = new Lazy<string>(FindRepositoryRoot);

        /// <summary>Absolute path of the repository root.</summary>
        public static string RepositoryRoot => RootPath.Value;

        /// <summary>Absolute path of <c>tests/fixtures</c>.</summary>
        public static string Directory => Path.Combine(RepositoryRoot, "tests", "fixtures");

        public static string Path_(params string[] segments)
        {
            string path = Directory;
            foreach (string segment in segments)
            {
                path = Path.Combine(path, segment);
            }

            return path;
        }

        public static string Feature(string name) => Path_("features", name);

        public static string CodeBehind(string name) => Path_("codebehind", name);

        public static string Trx(string name) => Path_("trx", name);

        public static string Project(params string[] segments)
        {
            string[] all = new string[segments.Length + 1];
            all[0] = "projects";
            Array.Copy(segments, 0, all, 1, segments.Length);
            return Path_(all);
        }

        public static string ReadText(string path) => File.ReadAllText(path);

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "ReqnrollRunner.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new InvalidOperationException(
                "Could not locate the repository root (no ReqnrollRunner.sln above " +
                AppContext.BaseDirectory + ").");
        }
    }
}
