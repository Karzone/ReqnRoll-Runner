#!/usr/bin/env bash
#
# Regenerates tests/fixtures/sanitizer-corpus.tsv.
#
# The corpus is an ORACLE, not a hand-written expectation: it runs a set of awkward scenario titles
# through the REAL Reqnroll MSBuild generator and records the identifiers it produced. TestNameSanitizer
# is then asserted against those rows one by one, so if a future Reqnroll release changes its naming
# rules, regenerating this file makes the tests fail loudly instead of leaving the fallback path
# quietly wrong.
#
# Requires: the .NET SDK, and network access to restore Reqnroll from NuGet.

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

project="$work/CorpusProbe"
mkdir -p "$project/Features"

cat > "$project/CorpusProbe.csproj" <<'CSPROJ'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <RootNamespace>CorpusProbe</RootNamespace>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Reqnroll.NUnit" Version="3.3.4" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="NUnit" Version="4.2.2" />
    <PackageReference Include="NUnit3TestAdapter" Version="4.6.0" />
  </ItemGroup>
</Project>
CSPROJ

python3 - "$project" <<'PY'
import sys, pathlib

# Every title here earns its place: each one probes a distinct rule (apostrophes joining words,
# '.'/'-' becoming '_', diacritic folding, ligatures that are NOT folded, scripts that are left
# alone, leading digits). Add to it rather than replacing it.
titles = [
    "Add two numbers", "1 plus 1", "snake_case and kebab-case", "alreadyCamelCase hasWords",
    "UPPER lower MiXeD", "café naïve Ærø", "a  b   c", "99 bottles of beer",
    "Ivan's \"quoted\" (tricky) & odd | title ~ = !", "Ünïcödé — スカラー", "Add many <a>",
    "Straße größer", "Ølsen Ægir Đorđe", "Œuvre œuf", "Łódź", "ĐorĐe", "þorn ð eth",
    "trailing space ", "  leading space", "tabs\tinside", "dots.and.commas,here",
    "percent 50% and #hash", "semi;colon:and/slash\\back", "plus+minus-equals=",
    "[brackets] {braces}", "emoji 🎉 party", "中文 测试", "русский текст", "only!!!", "___",
    "a-b-c", "x_1_2",
]

lines = ["Feature: Sanitizer corpus", ""]
for t in titles:
    lines += ["Scenario: " + t, "\tGiven I entered 1", ""]

pathlib.Path(sys.argv[1], "Features", "Corpus.feature").write_text("\n".join(lines), encoding="utf-8")
PY

echo "Building the probe project with the real Reqnroll generator…"
dotnet build "$project" -v q --nologo

python3 - "$project/Features/Corpus.feature.cs" "$repo_root/tests/fixtures/sanitizer-corpus.tsv" <<'PY'
import json, re, sys

# Fixture plumbing that carries #line directives but is not a scenario.
SKIP = {"ScenarioStartAsync", "ScenarioCleanupAsync", "TestInitializeAsync",
        "TestTearDownAsync", "ScenarioInitialize", "FeatureSetupAsync", "FeatureTearDownAsync"}

source = open(sys.argv[1], encoding="utf-8-sig").read()

declarations = [(m.start(), m.group(1))
                for m in re.finditer(r'public async global::System\.Threading\.Tasks\.Task (\w+)\(', source)]

rows = []
for i, (position, name) in enumerate(declarations):
    if name in SKIP:
        continue
    end = declarations[i + 1][0] if i + 1 < len(declarations) else len(source)
    # ScenarioInfo carries the title exactly as Reqnroll sees it at run time.
    match = re.search(r'ScenarioInfo\("((?:[^"\\]|\\.)*)"', source[position:end])
    if not match:
        continue
    title = (match.group(1)
             .replace('\\"', '"').replace("\\'", "'")
             .replace('\\t', '\t').replace('\\r', '\r').replace('\\n', '\n')
             .replace('\\\\', '\\'))
    rows.append((title, name))

feature_class = re.search(r'public partial class (\w+)', source).group(1)

out = [
    "# Reqnroll 3.3.4 generator output, captured verbatim from a sample built with the real",
    "# Reqnroll.Tools.MsBuild.Generation targets. This file is an ORACLE, not a hand-written",
    "# expectation: TestNameSanitizer is asserted against it row by row.",
    "# Columns: <JSON-encoded title>\\t<generated identifier>\\t<FEATURE_CLASS|METHOD>",
    "# Regenerate with: scripts/capture-sanitizer-corpus.sh",
    "\t".join([json.dumps("Sanitizer corpus", ensure_ascii=False), feature_class, "FEATURE_CLASS"]),
]
out += ["\t".join([json.dumps(t, ensure_ascii=False), n, "METHOD"]) for t, n in rows]

open(sys.argv[2], "w", encoding="utf-8").write("\n".join(out) + "\n")
print(f"Wrote {len(rows)} method rows to {sys.argv[2]}")
PY

echo "Done. Review the diff, then run: dotnet test ReqnrollRunner.sln"
