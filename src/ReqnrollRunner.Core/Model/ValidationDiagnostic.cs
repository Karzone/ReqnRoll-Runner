using System.Collections.Generic;

namespace ReqnrollRunner.Core.Model
{
    /// <summary>How much a diagnostic should worry the author.</summary>
    public enum DiagnosticSeverity
    {
        /// <summary>Legal Gherkin that is very unlikely to be what the author meant.</summary>
        Warning = 0,

        /// <summary>Legal and harmless, but worth knowing.</summary>
        Info = 1,
    }

    /// <summary>One finding from <c>FeatureValidator</c>.</summary>
    /// <remarks>
    /// Everything reported here is <em>valid Gherkin</em> — Reqnroll will happily generate code for
    /// all of it. These are authoring mistakes that only show up as a confusing failure at run time,
    /// or as a test that silently does nothing.
    /// </remarks>
    public sealed class ValidationDiagnostic
    {
        public ValidationDiagnostic(
            string code,
            DiagnosticSeverity severity,
            string message,
            int line,
            int column,
            string? scenarioName)
        {
            Code = code;
            Severity = severity;
            Message = message;
            Line = line;
            Column = column;
            ScenarioName = scenarioName;
        }

        /// <summary>Stable identifier, e.g. <c>RR001</c>. Safe to suppress or filter on.</summary>
        public string Code { get; }

        public DiagnosticSeverity Severity { get; }

        /// <summary>One sentence, saying what is wrong and what it will do at run time.</summary>
        public string Message { get; }

        /// <summary>1-based line the diagnostic points at.</summary>
        public int Line { get; }

        /// <summary>1-based column, or 1 when only the line is meaningful.</summary>
        public int Column { get; }

        /// <summary>The scenario it was found in, when applicable.</summary>
        public string? ScenarioName { get; }

        public override string ToString()
        {
            return Line + ":" + Column + " " + Code + " " + Message;
        }
    }

    /// <summary>Outcome of validating one feature file.</summary>
    public sealed class ValidationResult
    {
        private ValidationResult(bool parsed, IReadOnlyList<ValidationDiagnostic> diagnostics, string? error)
        {
            Parsed = parsed;
            Diagnostics = diagnostics;
            Error = error;
        }

        /// <summary>False when the file could not be parsed at all, so no checks could run.</summary>
        public bool Parsed { get; }

        public IReadOnlyList<ValidationDiagnostic> Diagnostics { get; }

        /// <summary>Why parsing failed. Non-null exactly when <see cref="Parsed"/> is false.</summary>
        public string? Error { get; }

        /// <summary>True when the file parsed and nothing was found.</summary>
        public bool IsClean => Parsed && Diagnostics.Count == 0;

        public static ValidationResult Ok(IReadOnlyList<ValidationDiagnostic> diagnostics)
        {
            return new ValidationResult(true, diagnostics, null);
        }

        public static ValidationResult Fail(string error)
        {
            return new ValidationResult(false, new List<ValidationDiagnostic>(), error);
        }
    }
}
