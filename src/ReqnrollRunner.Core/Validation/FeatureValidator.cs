using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Gherkin;
using Gherkin.Ast;
using ReqnrollRunner.Core.Model;

namespace ReqnrollRunner.Core.Validation
{
    /// <summary>
    /// Static analysis of a <c>.feature</c> file: authoring mistakes that are legal Gherkin, so
    /// nothing else complains about them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything checked here compiles and runs. Reqnroll generates code for all of it without a
    /// murmur. The failure shows up later — a placeholder passed through literally so a step fails
    /// with a baffling argument, or an Examples column that quietly does nothing after a rename.
    /// </para>
    /// <para>
    /// A pure function over the Gherkin AST: no project, no build, no Visual Studio. That makes it
    /// fully unit-testable, and it is why this could be built and verified without a Windows machine.
    /// </para>
    /// </remarks>
    public sealed class FeatureValidator
    {
        /// <summary>An Examples column reference, e.g. <c>&lt;count&gt;</c>.</summary>
        private static readonly Regex PlaceholderRegex = new Regex(
            @"<([^<>\r\n]+)>",
            RegexOptions.Compiled);

        public const string UnusedExampleColumn = "RR001";
        public const string UndefinedPlaceholder = "RR002";
        public const string ScenarioWithPlaceholders = "RR003";
        public const string OutlineWithoutPlaceholders = "RR004";
        public const string OutlineWithoutExamples = "RR005";

        /// <summary>Validates the file at <paramref name="featurePath"/>.</summary>
        public ValidationResult Validate(string featurePath)
        {
            if (featurePath == null)
            {
                throw new ArgumentNullException(nameof(featurePath));
            }

            if (!File.Exists(featurePath))
            {
                return ValidationResult.Fail("Feature file not found: " + featurePath);
            }

            try
            {
                using (var reader = new StreamReader(featurePath, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
                {
                    return Validate(new Parser().Parse(reader));
                }
            }
            catch (CompositeParserException ex)
            {
                return ValidationResult.Fail("Could not parse " + Path.GetFileName(featurePath) + ": " + ex.Message);
            }
            catch (NoSuchLanguageException ex)
            {
                return ValidationResult.Fail("Unsupported '# language:' header: " + ex.Message);
            }
            catch (IOException ex)
            {
                return ValidationResult.Fail("Could not read " + featurePath + ": " + ex.Message);
            }
        }

        /// <summary>Validates an already-parsed document. Exposed so tests can build documents in memory.</summary>
        public ValidationResult Validate(GherkinDocument document)
        {
            if (document?.Feature == null)
            {
                return ValidationResult.Fail("The file contains no Feature: header, so there is nothing to validate.");
            }

            var diagnostics = new List<ValidationDiagnostic>();

            foreach (Scenario scenario in ScenariosIn(document.Feature))
            {
                Inspect(scenario, diagnostics);
            }

            diagnostics.Sort((a, b) => a.Line != b.Line ? a.Line.CompareTo(b.Line) : string.CompareOrdinal(a.Code, b.Code));
            return ValidationResult.Ok(diagnostics);
        }

        private static void Inspect(Scenario scenario, List<ValidationDiagnostic> diagnostics)
        {
            string name = scenario.Name ?? string.Empty;
            bool isOutline = scenario.Examples != null && scenario.Examples.Any();

            // Placeholders can appear in a step's text, in its data table cells, and inside a doc
            // string — Reqnroll substitutes in all three, so all three count as "used".
            //
            // Doc strings are kept in a SEPARATE bucket, because `<name>` is also ordinary XML and a
            // doc string is exactly where markup gets pasted. A payload of
            // `<order><id>7</id></order>` yields four "placeholders", none of them intended, and
            // reporting them as undefined columns fires on every fixture anyone writes. So a
            // doc-string reference can only ever *confirm* a declared column (suppressing RR001); it
            // is never itself reported as undefined. What that gives up is the genuine case of a
            // typo'd placeholder inside a doc string — a fair trade against crying wolf on markup,
            // which is far more common and is what gets a validator switched off.
            var used = new Dictionary<string, int>(StringComparer.Ordinal);
            var inDocStrings = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (Step step in scenario.Steps)
            {
                CollectPlaceholders(step.Text, step.Location.Line, used);

                if (step.DataTable != null)
                {
                    foreach (TableRow row in step.DataTable.Rows)
                    {
                        foreach (TableCell cell in row.Cells)
                        {
                            CollectPlaceholders(cell.Value, row.Location.Line, used);
                        }
                    }
                }

                if (step.DocString != null)
                {
                    CollectPlaceholders(step.DocString.Content, step.DocString.Location.Line, inDocStrings);
                }
            }

            if (!isOutline)
            {
                // RR003 — a plain Scenario with <placeholders>. There are no Examples to substitute
                // from, so the text is passed through literally.
                foreach (KeyValuePair<string, int> placeholder in used)
                {
                    diagnostics.Add(new ValidationDiagnostic(
                        ScenarioWithPlaceholders,
                        DiagnosticSeverity.Warning,
                        "Scenario '" + name + "' uses the placeholder <" + placeholder.Key +
                        "> but is not a Scenario Outline, so nothing will be substituted and the step " +
                        "will receive the literal text '<" + placeholder.Key + ">'. Did you mean to " +
                        "make this a Scenario Outline with an Examples block?",
                        placeholder.Value,
                        1,
                        name));
                }

                return;
            }

            // Declared columns, and where each was declared.
            var declared = new Dictionary<string, int>(StringComparer.Ordinal);
            bool anyExamplesBlock = false;
            bool anyHeader = false;

            foreach (Examples examples in scenario.Examples ?? Enumerable.Empty<Examples>())
            {
                anyExamplesBlock = true;
                if (examples.TableHeader == null)
                {
                    continue;
                }

                anyHeader = true;
                foreach (TableCell cell in examples.TableHeader.Cells)
                {
                    string column = (cell.Value ?? string.Empty).Trim();
                    if (column.Length > 0 && !declared.ContainsKey(column))
                    {
                        declared[column] = examples.TableHeader.Location.Line;
                    }
                }
            }

            // RR005 — an outline whose Examples block has no header row at all generates nothing
            // useful. (Gherkin rejects an outline with no Examples keyword outright, so the case
            // that reaches us is the empty block.)
            if (!anyExamplesBlock || !anyHeader)
            {
                diagnostics.Add(new ValidationDiagnostic(
                    OutlineWithoutExamples,
                    DiagnosticSeverity.Warning,
                    "Scenario Outline '" + name + "' has no Examples table, so it generates no runnable rows.",
                    scenario.Location.Line,
                    1,
                    name));
                return;
            }

            // A doc-string reference counts as a real use only when it names a declared column.
            // Anything else in there is markup, and treating markup as a use would suppress RR001
            // for a column that genuinely does nothing.
            foreach (KeyValuePair<string, int> candidate in inDocStrings)
            {
                if (declared.ContainsKey(candidate.Key) && !used.ContainsKey(candidate.Key))
                {
                    used[candidate.Key] = candidate.Value;
                }
            }

            // RR004 — an outline that substitutes nothing. Every row runs an identical test.
            if (used.Count == 0)
            {
                diagnostics.Add(new ValidationDiagnostic(
                    OutlineWithoutPlaceholders,
                    DiagnosticSeverity.Warning,
                    "Scenario Outline '" + name + "' uses no placeholders, so every example row runs " +
                    "an identical test. Either reference a column as <name>, or make this a plain Scenario.",
                    scenario.Location.Line,
                    1,
                    name));

                // Every column is trivially unused here, but saying so once per column on top of
                // RR004 is noise: the broader diagnostic already covers it, and a validator that
                // reports five things about one mistake trains people to ignore it.
                return;
            }

            // RR001 — declared but never referenced. Usually a leftover after a rename.
            foreach (KeyValuePair<string, int> column in declared)
            {
                if (!used.ContainsKey(column.Key))
                {
                    diagnostics.Add(new ValidationDiagnostic(
                        UnusedExampleColumn,
                        DiagnosticSeverity.Warning,
                        "Examples column '" + column.Key + "' in Scenario Outline '" + name +
                        "' is never used by any step. It has no effect — remove it, or reference it as <" +
                        column.Key + ">.",
                        column.Value,
                        1,
                        name));
                }
            }

            // RR002 — referenced but never declared. Reqnroll passes '<name>' through verbatim, so
            // this fails at run time with an argument nobody expects.
            foreach (KeyValuePair<string, int> placeholder in used)
            {
                if (!declared.ContainsKey(placeholder.Key))
                {
                    diagnostics.Add(new ValidationDiagnostic(
                        UndefinedPlaceholder,
                        DiagnosticSeverity.Warning,
                        "Placeholder <" + placeholder.Key + "> in Scenario Outline '" + name +
                        "' matches no Examples column, so the step will receive the literal text '<" +
                        placeholder.Key + ">' at run time.",
                        placeholder.Value,
                        1,
                        name));
                }
            }
        }

        private static void CollectPlaceholders(string? text, int line, IDictionary<string, int> into)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            foreach (Match match in PlaceholderRegex.Matches(text))
            {
                string name = match.Groups[1].Value.Trim();
                if (name.Length > 0 && !into.ContainsKey(name))
                {
                    into[name] = line;
                }
            }
        }

        private static IEnumerable<Scenario> ScenariosIn(Feature feature)
        {
            foreach (object child in feature.Children)
            {
                if (child is Scenario scenario)
                {
                    yield return scenario;
                }
                else if (child is Rule rule)
                {
                    foreach (object nested in rule.Children)
                    {
                        if (nested is Scenario nestedScenario)
                        {
                            yield return nestedScenario;
                        }
                    }
                }
            }
        }
    }
}
