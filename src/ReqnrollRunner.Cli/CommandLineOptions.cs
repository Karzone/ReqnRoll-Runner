using System;
using System.Collections.Generic;

namespace ReqnrollRunner.Cli
{
    internal enum CommandKind
    {
        None,
        Map,
        Run,
        Debug,
        Lint,
        Help,
    }

    /// <summary>
    /// Hand-rolled argument parsing. A parser dependency would be the CLI's only NuGet reference and
    /// this surface is three commands wide, so it is not worth it.
    /// </summary>
    internal sealed class CommandLineOptions
    {
        public CommandKind Command { get; private set; } = CommandKind.None;

        public string? File { get; private set; }

        public int Line { get; private set; } = 1;

        public bool Json { get; private set; }

        public bool NoBuild { get; private set; }

        public string? Framework { get; private set; }

        public string? Configuration { get; private set; }

        public string? ExtraArguments { get; private set; }

        public int TimeoutSeconds { get; private set; } = 30;

        public string? Error { get; private set; }

        public static CommandLineOptions Parse(IReadOnlyList<string> args)
        {
            var options = new CommandLineOptions();

            if (args.Count == 0)
            {
                options.Command = CommandKind.Help;
                return options;
            }

            switch (args[0].ToLowerInvariant())
            {
                case "map":
                    options.Command = CommandKind.Map;
                    break;
                case "run":
                    options.Command = CommandKind.Run;
                    break;
                case "debug":
                    options.Command = CommandKind.Debug;
                    break;
                case "lint":
                    options.Command = CommandKind.Lint;
                    break;
                case "-h":
                case "--help":
                case "help":
                    options.Command = CommandKind.Help;
                    return options;
                default:
                    options.Error = "Unknown command '" + args[0] + "'.";
                    return options;
            }

            for (int i = 1; i < args.Count; i++)
            {
                string arg = args[i];
                switch (arg.ToLowerInvariant())
                {
                    case "--file":
                    case "-f":
                        if (!TryTakeValue(args, ref i, out string? file))
                        {
                            options.Error = "--file needs a path.";
                            return options;
                        }

                        options.File = file;
                        break;

                    case "--line":
                    case "-l":
                        if (!TryTakeValue(args, ref i, out string? lineText) ||
                            !int.TryParse(lineText, out int line))
                        {
                            options.Error = "--line needs an integer.";
                            return options;
                        }

                        options.Line = line;
                        break;

                    case "--framework":
                        if (!TryTakeValue(args, ref i, out string? framework))
                        {
                            options.Error = "--framework needs a target framework moniker.";
                            return options;
                        }

                        options.Framework = framework;
                        break;

                    case "--configuration":
                    case "-c":
                        if (!TryTakeValue(args, ref i, out string? configuration))
                        {
                            options.Error = "--configuration needs a value, e.g. Release.";
                            return options;
                        }

                        options.Configuration = configuration;
                        break;

                    case "--args":
                        if (!TryTakeValue(args, ref i, out string? extra))
                        {
                            options.Error = "--args needs a value.";
                            return options;
                        }

                        options.ExtraArguments = extra;
                        break;

                    case "--timeout":
                        if (!TryTakeValue(args, ref i, out string? timeoutText) ||
                            !int.TryParse(timeoutText, out int timeout))
                        {
                            options.Error = "--timeout needs an integer number of seconds.";
                            return options;
                        }

                        options.TimeoutSeconds = timeout;
                        break;

                    case "--json":
                        options.Json = true;
                        break;

                    case "--no-build":
                        options.NoBuild = true;
                        break;

                    default:
                        options.Error = "Unknown option '" + arg + "'.";
                        return options;
                }
            }

            if (string.IsNullOrWhiteSpace(options.File))
            {
                options.Error = "--file is required.";
            }

            return options;
        }

        private static bool TryTakeValue(IReadOnlyList<string> args, ref int index, out string? value)
        {
            if (index + 1 >= args.Count)
            {
                value = null;
                return false;
            }

            index++;
            value = args[index];
            return true;
        }

        public const string Usage = @"reqnroll-runner — run and debug Reqnroll scenarios from a feature file position.

USAGE
  reqnroll-runner map   --file <path.feature> --line <n> [--json]
  reqnroll-runner run   --file <path.feature> --line <n> [--no-build] [--json]
                        [--configuration <name>] [--framework <tfm>]
                        [--args ""<extra dotnet test args>""]
  reqnroll-runner debug --file <path.feature> --line <n> [--timeout <seconds>]
  reqnroll-runner lint  --file <path.feature> [--json]

COMMANDS
  map     Dry run. Prints the resolved target, project, runner and the exact
          --filter expression, without executing anything.
  run     Executes the mapped scenario and reports parsed TRX results.
  debug   Starts the run with VSTEST_HOST_DEBUG=1, prints the test host process
          id to attach to, then waits.
  lint    Reports authoring mistakes that are legal Gherkin, so nothing else
          complains about them: an Examples column no step uses, a <placeholder>
          with no matching column, a plain Scenario using placeholders, and an
          Outline that substitutes nothing. Needs no project and no build.

OPTIONS
  -f, --file       Path to the .feature file.
  -l, --line       1-based caret line. Defaults to 1 (the Feature: header).
      --json       Emit machine-readable JSON instead of text.
      --no-build   Pass --no-build to dotnet test.
  -c, --configuration
                   Build configuration to run, e.g. Release. Omitted means Debug,
                   which is dotnet test's own default. With --no-build this must
                   match the configuration you actually built, or there will be
                   no test assembly to run.
      --framework  Target framework to use when the project multi-targets.
      --args       Extra arguments appended verbatim to dotnet test.
      --timeout    Seconds to wait for the test host process id. Default 30.

EXIT CODES
  0  success
  1  mapping failed, no tests matched, or a test failed
  2  bad usage
";
    }
}
