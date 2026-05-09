// Feature: refinement-types, Property 7: Transpilation correctness
//
// **Validates: Requirements 12.1, 12.3, 12.5**
//
// For any valid Mist program containing refinement type declarations and usages
// (literal assignments, widening, pattern matching), transpiling to C# SHALL
// produce zero compilation errors and non-empty C# output.
//
// Uses the batched testing pattern:
// 1. Generate 100 random valid Mist programs with refinement types
// 2. Compile the combined program in-process via CompilerDriver (shared instance)
// 3. Write emitted C# files into a single temp project
// 4. Run dotnet build once
// 5. Verify exit code 0
//
// Process spawns: 1 per test run (dotnet build).

using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using FsCheck;
using FsCheck.Fluent;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Semantics.Model;

namespace Mist.Tests.Backend;

/// <summary>
/// Property-based test verifying that the C# emitter produces compilable C# for
/// all valid Mist programs containing refinement type declarations and usages.
///
/// Property 7: Transpilation correctness
/// For any valid Mist program containing refinement type declarations and usages
/// (literal assignments, widening, pattern matching), transpiling to C# SHALL
/// produce zero compilation errors and non-empty C# output.
///
/// Uses the batched testing pattern:
/// 1. Generate 100 random valid Mist programs with refinement types
/// 2. Compile the combined program in-process via CompilerDriver (shared instance)
/// 3. Write emitted C# files into a single temp project
/// 4. Run dotnet build once
/// 5. Verify exit code 0
///
/// Process spawns: 1 per test run (dotnet build).
///
/// **Validates: Requirements 12.1, 12.3, 12.5**
/// </summary>
public sealed class RefinementTypeEmissionPropertyTests : IDisposable
{
    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private readonly string _outputDir;

    public RefinementTypeEmissionPropertyTests()
    {
        _outputDir = Path.Combine(
            Path.GetTempPath(),
            "mist_reftype_emit_prop_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_outputDir))
        {
            try { Directory.Delete(_outputDir, recursive: true); } catch { /* best effort */ }
        }
        GC.SuppressFinalize(this);
    }

    // ─── Scenario model ──────────────────────────────────────────────────

    private sealed class RefinementEmissionScenario
    {
        public required string Description { get; init; }
        public required string MistSource { get; init; }
    }

    // ─── Scenario generators ─────────────────────────────────────────────

    /// <summary>
    /// Generates a literal assignment scenario: declares a refinement type and
    /// assigns a literal that satisfies the predicate.
    /// </summary>
    private static Gen<RefinementEmissionScenario> GenLiteralAssignment()
    {
        return from threshold in Gen.Choose(0, 100)
               from offset in Gen.Choose(0, 100)
               from idx in Gen.Choose(0, 99999)
               let literal = threshold + offset
               let typeName = $"MinVal{idx}"
               select new RefinementEmissionScenario
               {
                   Description = $"literal: type {typeName} = int >= {threshold}; {typeName} x = {literal};",
                   MistSource = $"type {typeName} = int >= {threshold};\n" +
                                $"class LitAssign{idx} {{\n" +
                                $"    public static void Run() {{\n" +
                                $"        {typeName} x = {literal};\n" +
                                $"    }}\n" +
                                $"}}",
               };
    }

    /// <summary>
    /// Generates a compound predicate literal assignment scenario:
    /// <c>type T = int >= Lo and &lt;= Hi;</c> with a literal in range.
    /// </summary>
    private static Gen<RefinementEmissionScenario> GenCompoundLiteralAssignment()
    {
        return from lo in Gen.Choose(0, 50)
               from range in Gen.Choose(1, 50)
               from offsetInRange in Gen.Choose(0, 50)
               from idx in Gen.Choose(0, 99999)
               let hi = lo + range
               let literal = lo + (offsetInRange % (range + 1))
               let typeName = $"Range{idx}"
               select new RefinementEmissionScenario
               {
                   Description = $"compound: type {typeName} = int >= {lo} and <= {hi}; {typeName} x = {literal};",
                   MistSource = $"type {typeName} = int >= {lo} and <= {hi};\n" +
                                $"class CompAssign{idx} {{\n" +
                                $"    public static void Run() {{\n" +
                                $"        {typeName} x = {literal};\n" +
                                $"    }}\n" +
                                $"}}",
               };
    }

    /// <summary>
    /// Generates a widening scenario: assigns a refinement-typed value to a
    /// base-typed (int) variable.
    /// </summary>
    private static Gen<RefinementEmissionScenario> GenWidening()
    {
        return from threshold in Gen.Choose(0, 100)
               from offset in Gen.Choose(0, 100)
               from idx in Gen.Choose(0, 99999)
               let literal = threshold + offset
               let typeName = $"Widen{idx}"
               select new RefinementEmissionScenario
               {
                   Description = $"widening: type {typeName} = int >= {threshold}; int y = x;",
                   MistSource = $"type {typeName} = int >= {threshold};\n" +
                                $"class WideAssign{idx} {{\n" +
                                $"    public static void Run() {{\n" +
                                $"        {typeName} x = {literal};\n" +
                                $"        int y = x;\n" +
                                $"    }}\n" +
                                $"}}",
               };
    }

    /// <summary>
    /// Generates a pattern matching scenario: tests a base-typed value against
    /// a refinement type using <c>is RefinementType varName</c>.
    /// </summary>
    private static Gen<RefinementEmissionScenario> GenPatternMatch()
    {
        return from threshold in Gen.Choose(0, 100)
               from testValue in Gen.Choose(0, 200)
               from idx in Gen.Choose(0, 99999)
               let typeName = $"Check{idx}"
               select new RefinementEmissionScenario
               {
                   Description = $"pattern: if (val is {typeName} matched) where {typeName} = int >= {threshold}",
                   MistSource = $"type {typeName} = int >= {threshold};\n" +
                                $"class PatMatch{idx} {{\n" +
                                $"    public static void Run() {{\n" +
                                $"        int val = {testValue};\n" +
                                $"        if (val is {typeName} matched) {{\n" +
                                $"            int result = matched;\n" +
                                $"        }}\n" +
                                $"    }}\n" +
                                $"}}",
               };
    }

    /// <summary>
    /// Generates a compound predicate pattern matching scenario.
    /// </summary>
    private static Gen<RefinementEmissionScenario> GenCompoundPatternMatch()
    {
        return from lo in Gen.Choose(0, 50)
               from range in Gen.Choose(1, 50)
               from testValue in Gen.Choose(0, 150)
               from idx in Gen.Choose(0, 99999)
               let hi = lo + range
               let typeName = $"Bounded{idx}"
               select new RefinementEmissionScenario
               {
                   Description = $"compound-pattern: if (val is {typeName} m) where {typeName} = int >= {lo} and <= {hi}",
                   MistSource = $"type {typeName} = int >= {lo} and <= {hi};\n" +
                                $"class CompPat{idx} {{\n" +
                                $"    public static void Run() {{\n" +
                                $"        int val = {testValue};\n" +
                                $"        if (val is {typeName} m) {{\n" +
                                $"            int result = m;\n" +
                                $"        }}\n" +
                                $"    }}\n" +
                                $"}}",
               };
    }

    /// <summary>Combined generator producing any scenario kind.</summary>
    private static Gen<RefinementEmissionScenario> GenScenario()
    {
        return Gen.OneOf(
            GenLiteralAssignment(),
            GenCompoundLiteralAssignment(),
            GenWidening(),
            GenPatternMatch(),
            GenCompoundPatternMatch());
    }

    // ─── Batched compilation ─────────────────────────────────────────────

    /// <summary>
    /// Builds a single combined Mist program from all scenarios.
    /// Each scenario contributes its type declarations and classes with unique names.
    /// A single Program class with Main ties them together.
    /// </summary>
    private static string BuildCombinedProgram(List<RefinementEmissionScenario> scenarios)
    {
        var sb = new StringBuilder();
        var usedTypeNames = new HashSet<string>();

        foreach (var scenario in scenarios)
        {
            // Extract the type name from the source to detect duplicates.
            // Type declarations follow the pattern: type <Name> = ...
            var match = System.Text.RegularExpressions.Regex.Match(
                scenario.MistSource, @"^type\s+(\w+)\s*=", System.Text.RegularExpressions.RegexOptions.Multiline);
            if (match.Success && !usedTypeNames.Add(match.Groups[1].Value))
            {
                // Skip duplicate type names to avoid MIST_SB_0061 errors
                continue;
            }

            sb.AppendLine(scenario.MistSource);
        }

        // Add a single Program class with Main
        sb.AppendLine("class Program {");
        sb.AppendLine("    public static void Main() { }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    /// <summary>
    /// Compiles a single Mist source in-process via CompilerDriver.
    /// Returns the emitted C# files or null if compilation fails.
    /// </summary>
    private static (IReadOnlyDictionary<string, string>? Files, string? Error) CompileInProcess(
        string mistSource, string projectDir)
    {
        var sourcePath = Path.Combine(projectDir, "Program.mist");
        File.WriteAllText(sourcePath, mistSource);

        var options = new CompilerOptions(
            ProjectFolder: projectDir,
            SourcePaths: ImmutableArray.Create(sourcePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: projectDir,
            BinDirectory: Path.Combine(projectDir, "bin"),
            ProjectName: "TestProgram",
            NamespaceRoot: "TestProgram",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            EmitToDisk: false);

        var result = SharedDriver.Compile(options);

        if (!result.Succeeded)
        {
            var diagnosticText = string.Join("\n",
                result.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => $"  {d.Id.Value}: {d.Message}"));
            return (null, diagnosticText);
        }

        return (result.EmittedFiles, null);
    }

    // ─── Test ────────────────────────────────────────────────────────────

    /// <summary>
    /// Property 7: Transpilation correctness
    ///
    /// Batched pattern: generate 100 random valid Mist programs with refinement types,
    /// compile the combined program in-process via CompilerDriver, write emitted C#
    /// into a single temp project, run dotnet build once, verify exit code 0.
    ///
    /// Process spawns: 1 (dotnet build).
    ///
    /// **Validates: Requirements 12.1, 12.3, 12.5**
    /// </summary>
    [Fact]
    public void EmittedCSharp_CompilesWithoutErrors_BatchedRefinementScenarios()
    {
        // Step 1: Generate 100 random scenarios using FsCheck
        var scenarios = GenScenario().Sample(100).ToList();

        // Step 2: Build a single combined Mist program from all scenarios
        var combinedSource = BuildCombinedProgram(scenarios);

        // Step 3: Compile the combined program in-process via CompilerDriver
        var projectDir = Path.Combine(_outputDir, "combined");
        Directory.CreateDirectory(projectDir);

        var (files, error) = CompileInProcess(combinedSource, projectDir);

        Assert.True(files is not null,
            $"Combined Mist program failed in-process compilation:\n{error}\n\n" +
            $"Source (first 2000 chars):\n{combinedSource[..Math.Min(2000, combinedSource.Length)]}");

        // Verify non-empty C# output
        Assert.True(files!.Count > 0, "Emitter produced zero C# files");
        Assert.True(files.Values.Any(content => !string.IsNullOrWhiteSpace(content)),
            "All emitted C# files are empty");

        // Step 4: Write emitted C# files into a temp project for dotnet build
        var buildProjectDir = Path.Combine(_outputDir, "build_project");
        Directory.CreateDirectory(buildProjectDir);

        var csproj = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Library</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """;
        File.WriteAllText(Path.Combine(buildProjectDir, "TestBatch.csproj"), csproj);

        foreach (var (fileName, content) in files)
        {
            File.WriteAllText(Path.Combine(buildProjectDir, fileName), content);
        }

        // Step 5: Run dotnet build once
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = "build --nologo -v q",
            WorkingDirectory = buildProjectDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi);
        Assert.NotNull(process);

        var stdout = process!.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();

        process.WaitForExit(60_000);

        // Step 6: Verify exit code 0
        Assert.True(process.ExitCode == 0,
            $"dotnet build failed (exit code {process.ExitCode}).\n" +
            $"Stdout:\n{stdout}\n" +
            $"Stderr:\n{stderr}\n" +
            $"Scenarios: {scenarios.Count}\n" +
            $"Files emitted: {files.Count}");
    }
}
