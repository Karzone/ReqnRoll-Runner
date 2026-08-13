using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using ReqnrollRunner.Core.Model;

namespace ReqnrollRunner.Core.Execution
{
    /// <summary>Reads a VSTest TRX file into <see cref="TestCaseResult"/>s.</summary>
    /// <remarks>
    /// TRX rather than stdout scraping because the console summary is not trustworthy: NUnit reports
    /// inconclusive results (what an undefined-step scenario produces) as <c>Total: 0</c> even though
    /// the tests genuinely ran. The TRX always carries the individual results.
    /// </remarks>
    public static class TrxParser
    {
        private static readonly XNamespace Trx = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

        /// <summary>Parses a TRX file. Returns an empty list if the file is missing or unreadable.</summary>
        public static IReadOnlyList<TestCaseResult> ParseFile(string trxPath)
        {
            if (!File.Exists(trxPath))
            {
                return new List<TestCaseResult>();
            }

            try
            {
                return Parse(XDocument.Load(trxPath));
            }
            catch (Exception ex) when (ex is System.Xml.XmlException || ex is IOException)
            {
                return new List<TestCaseResult>();
            }
        }

        /// <summary>Parses a loaded TRX document. Exposed for fixture-driven tests.</summary>
        public static IReadOnlyList<TestCaseResult> Parse(XDocument document)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            // testId -> fully qualified name, from the TestDefinitions section.
            var fullyQualifiedNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (XElement unitTest in document.Descendants(Trx + "UnitTest"))
            {
                string? id = (string?)unitTest.Attribute("id");
                XElement? testMethod = unitTest.Element(Trx + "TestMethod");
                if (id == null || testMethod == null)
                {
                    continue;
                }

                string className = (string?)testMethod.Attribute("className") ?? string.Empty;
                string methodName = (string?)testMethod.Attribute("name") ?? string.Empty;

                fullyQualifiedNames[id] = className.Length > 0 ? className + "." + methodName : methodName;
            }

            var results = new List<TestCaseResult>();
            foreach (XElement result in document.Descendants(Trx + "UnitTestResult"))
            {
                string displayName = (string?)result.Attribute("testName") ?? string.Empty;
                string? testId = (string?)result.Attribute("testId");

                string? fullyQualifiedName = null;
                if (testId != null && fullyQualifiedNames.TryGetValue(testId, out string? mapped))
                {
                    fullyQualifiedName = mapped;
                }

                XElement? errorInfo = result.Descendants(Trx + "ErrorInfo").FirstOrDefault();

                results.Add(new TestCaseResult(
                    displayName,
                    fullyQualifiedName,
                    ParseOutcome((string?)result.Attribute("outcome")),
                    ParseDuration((string?)result.Attribute("duration")),
                    errorInfo?.Element(Trx + "Message")?.Value,
                    errorInfo?.Element(Trx + "StackTrace")?.Value));
            }

            return results;
        }

        /// <summary>Maps a TRX outcome string onto our model.</summary>
        public static TestOutcome ParseOutcome(string? outcome)
        {
            if (string.IsNullOrWhiteSpace(outcome))
            {
                return TestOutcome.Unknown;
            }

            switch (outcome!.Trim().ToLowerInvariant())
            {
                case "passed":
                    return TestOutcome.Passed;

                case "failed":
                case "error":
                case "timeout":
                case "aborted":
                    return TestOutcome.Failed;

                // MSTest and xUnit report an ignored/skipped test this way.
                case "notexecuted":
                case "skipped":
                case "disconnected":
                    return TestOutcome.Skipped;

                // NUnit's inconclusive — a scenario with undefined steps lands here.
                case "inconclusive":
                case "none":
                case "pending":
                    return TestOutcome.Inconclusive;

                case "notrunnable":
                    return TestOutcome.NotExecuted;

                default:
                    return TestOutcome.Unknown;
            }
        }

        private static TimeSpan ParseDuration(string? duration)
        {
            if (string.IsNullOrWhiteSpace(duration))
            {
                return TimeSpan.Zero;
            }

            return TimeSpan.TryParse(duration, CultureInfo.InvariantCulture, out TimeSpan parsed)
                ? parsed
                : TimeSpan.Zero;
        }
    }
}
