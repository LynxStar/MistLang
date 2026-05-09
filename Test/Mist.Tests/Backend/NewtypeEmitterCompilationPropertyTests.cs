// Feature: newtypes, Property 10: Transpiled C# compiles without errors
//
// **Validates: Requirements 5.4**
//
// For any valid newtype declaration (with a resolvable source type), the transpiled
// C# output SHALL compile without errors.
//
// Uses the batched pattern:
// 1. Generate 100 random valid Mist programs with newtypes (from primitives, structs, classes)
// 2. Compile the combined program in-process via CompilerDriver (shared instance)
// 3. Collect all emitted C# files into a single temp project
// 4. Run dotnet build once
// 5. Verify zero exit code
//
// Process spawns: 1 per test run (dotnet build).

using System.Collections.Immutable;
using System.Diagnostics;
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
/// all valid Mist programs containing newtype declarations.
///
/// Property 10: Transpiled C# compiles without errors
/// For any valid newtype declaration (from primitives, structs, classes), the
/// transpiled C# output SHALL compile without errors on .NET 10.
///
/// Uses the batched testing pattern:
/// 1. Generate 100 random valid Mist programs with newtypes
/// 2. Compile the combined program in-process via CompilerDriver (shared instance)
/// 3. Collect all emitted C# files into a single temp project
/// 4. Run dotnet build once
/// 5. Verify exit code 0
///
/// Process spawns: 1 per test run (dotnet build).
///
/// **Validates: Requirements 5.4**
/// </summary>
public sealed class NewtypeEmitterCompilationPropertyTests : IDisposable
{
    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private readonly string _outputDir;

    public NewtypeEmitterCompilationPropertyTests()
    {
        _outputDir = Path.Combine(
            Path.GetTempPath(),
            "mist_newtype_emit_prop_" + Guid.NewGuid().ToString("N"));
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

    private sealed class NewtypeScenario
    {
        public required string Description { get; init; }
        public required string MistSource { get; init; }
    }

    // ─── Name pools ──────────────────────────────────────────────────────

    private static readonly string[] PrimitiveTypes =
        ["int", "string", "bool", "float", "double", "long", "char", "byte", "decimal", "short"];

    private static readonly string[] FieldTypes = ["int", "string", "bool"];

    private static readonly string[] FieldNames =
        ["x", "y", "z", "w", "count", "value"];

    private static readonly string[] MethodNames =
        ["DoWork", "Process", "Execute", "Run", "Calculate", "Transform"];

    // ─── Scenario generators ─────────────────────────────────────────────

    private enum ScenarioKind { Primitive, Struct, Class }

    /// <summary>
    /// Generates a scenario kind with its structural parameters.
    /// The actual unique index is assigned later during batching to avoid name collisions.
    /// </summary>
    private sealed class ScenarioTemplate
    {
        public required ScenarioKind Kind { get; init; }
        public required string PrimitiveType { get; init; }
        public required int FieldCount { get; init; }
        public required int MethodCount { get; init; }
        public required int[] FieldTypeIndices { get; init; }
        public required int[] FieldNameIndices { get; init; }
        public required int[] MethodNameIndices { get; init; }
    }

    /// <summary>Generates a primitive newtype template.</summary>
    private static Gen<ScenarioTemplate> GenPrimitiveTemplate()
    {
        return from primitiveType in Gen.Elements(PrimitiveTypes)
               select new ScenarioTemplate
               {
                   Kind = ScenarioKind.Primitive,
                   PrimitiveType = primitiveType,
                   FieldCount = 0,
                   MethodCount = 0,
                   FieldTypeIndices = [],
                   FieldNameIndices = [],
                   MethodNameIndices = [],
               };
    }

    /// <summary>Generates a struct newtype template.</summary>
    private static Gen<ScenarioTemplate> GenStructTemplate()
    {
        return from fieldCount in Gen.Choose(1, 3)
               from methodCount in Gen.Choose(0, 2)
               from fieldTypeIdxs in Gen.ArrayOf(Gen.Choose(0, FieldTypes.Length - 1), fieldCount)
               from fieldNameIdxs in Gen.ArrayOf(Gen.Choose(0, FieldNames.Length - 1), fieldCount)
               from methodNameIdxs in Gen.ArrayOf(Gen.Choose(0, MethodNames.Length - 1), methodCount)
               select new ScenarioTemplate
               {
                   Kind = ScenarioKind.Struct,
                   PrimitiveType = "",
                   FieldCount = fieldCount,
                   MethodCount = methodCount,
                   FieldTypeIndices = fieldTypeIdxs,
                   FieldNameIndices = fieldNameIdxs,
                   MethodNameIndices = methodNameIdxs,
               };
    }

    /// <summary>Generates a class newtype template.</summary>
    private static Gen<ScenarioTemplate> GenClassTemplate()
    {
        return from fieldCount in Gen.Choose(1, 3)
               from methodCount in Gen.Choose(0, 2)
               from fieldTypeIdxs in Gen.ArrayOf(Gen.Choose(0, FieldTypes.Length - 1), fieldCount)
               from fieldNameIdxs in Gen.ArrayOf(Gen.Choose(0, FieldNames.Length - 1), fieldCount)
               from methodNameIdxs in Gen.ArrayOf(Gen.Choose(0, MethodNames.Length - 1), methodCount)
               select new ScenarioTemplate
               {
                   Kind = ScenarioKind.Class,
                   PrimitiveType = "",
                   FieldCount = fieldCount,
                   MethodCount = methodCount,
                   FieldTypeIndices = fieldTypeIdxs,
                   FieldNameIndices = fieldNameIdxs,
                   MethodNameIndices = methodNameIdxs,
               };
    }

    /// <summary>Combined generator producing any scenario template.</summary>
    private static Gen<ScenarioTemplate> GenTemplate()
    {
        return Gen.OneOf(
            GenPrimitiveTemplate(),
            GenStructTemplate(),
            GenClassTemplate());
    }

    /// <summary>
    /// Materializes a scenario template into Mist source using a guaranteed-unique index.
    /// </summary>
    private static NewtypeScenario MaterializeScenario(ScenarioTemplate template, int uniqueIdx)
    {
        switch (template.Kind)
        {
            case ScenarioKind.Primitive:
                var primName = $"Prim{uniqueIdx}";
                return new NewtypeScenario
                {
                    Description = $"type {primName} = new {template.PrimitiveType};",
                    MistSource = $"type {primName} = new {template.PrimitiveType};",
                };

            case ScenarioKind.Struct:
            {
                var structName = $"Src{uniqueIdx}";
                var newtypeName = $"Nt{uniqueIdx}";
                var fields = Enumerable.Range(0, template.FieldCount)
                    .Select(i => $"    public {FieldTypes[template.FieldTypeIndices[i]]} {FieldNames[template.FieldNameIndices[i]]}{uniqueIdx}_{i};")
                    .ToList();
                var methods = Enumerable.Range(0, template.MethodCount)
                    .Select(i => $"    public int {MethodNames[template.MethodNameIndices[i]]}{uniqueIdx}_{i}() {{ return 0; }}")
                    .ToList();
                var ctorParams = Enumerable.Range(0, template.FieldCount)
                    .Select(i => $"{FieldTypes[template.FieldTypeIndices[i]]} p{i}")
                    .ToList();
                var ctorBody = Enumerable.Range(0, template.FieldCount)
                    .Select(i => $"        this.{FieldNames[template.FieldNameIndices[i]]}{uniqueIdx}_{i} = p{i};")
                    .ToList();
                var ctor = $"    public {structName}({string.Join(", ", ctorParams)})\n    {{\n{string.Join("\n", ctorBody)}\n    }}";
                var allMembers = string.Join("\n", fields.Concat(methods).Append(ctor));
                var source = $"public struct {structName}\n{{\n{allMembers}\n}}\n\ntype {newtypeName} = new {structName};";
                return new NewtypeScenario
                {
                    Description = $"struct {structName} -> type {newtypeName}",
                    MistSource = source,
                };
            }

            case ScenarioKind.Class:
            {
                var className = $"Cls{uniqueIdx}";
                var newtypeName = $"NtCls{uniqueIdx}";
                var fields = Enumerable.Range(0, template.FieldCount)
                    .Select(i => $"    public {FieldTypes[template.FieldTypeIndices[i]]} {FieldNames[template.FieldNameIndices[i]]}{uniqueIdx}_{i};")
                    .ToList();
                var methods = Enumerable.Range(0, template.MethodCount)
                    .Select(i => $"    public int {MethodNames[template.MethodNameIndices[i]]}{uniqueIdx}_{i}() {{ return 0; }}")
                    .ToList();
                var ctorParams = Enumerable.Range(0, template.FieldCount)
                    .Select(i => $"{FieldTypes[template.FieldTypeIndices[i]]} p{i}")
                    .ToList();
                var ctorBody = Enumerable.Range(0, template.FieldCount)
                    .Select(i => $"        this.{FieldNames[template.FieldNameIndices[i]]}{uniqueIdx}_{i} = p{i};")
                    .ToList();
                var ctor = $"    public {className}({string.Join(", ", ctorParams)})\n    {{\n{string.Join("\n", ctorBody)}\n    }}";
                var allMembers = string.Join("\n", fields.Concat(methods).Append(ctor));
                var source = $"public class {className}\n{{\n{allMembers}\n}}\n\ntype {newtypeName} = new {className};";
                return new NewtypeScenario
                {
                    Description = $"class {className} -> type {newtypeName}",
                    MistSource = source,
                };
            }

            default:
                throw new InvalidOperationException($"Unknown scenario kind: {template.Kind}");
        }
    }

    // ─── Batched compilation ─────────────────────────────────────────────

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

    /// <summary>
    /// Builds a single combined Mist program from all scenario templates.
    /// Each template is materialized with a unique index to avoid name collisions.
    /// A single Program class with Main ties them together.
    /// </summary>
    private static (string Source, List<NewtypeScenario> Scenarios) BuildCombinedProgram(
        List<ScenarioTemplate> templates)
    {
        var scenarios = new List<NewtypeScenario>(templates.Count);
        var sb = new System.Text.StringBuilder();

        for (var i = 0; i < templates.Count; i++)
        {
            var scenario = MaterializeScenario(templates[i], i);
            scenarios.Add(scenario);
            sb.AppendLine(scenario.MistSource);
            sb.AppendLine();
        }

        // Add a single Program class with Main
        sb.AppendLine("class Program {");
        sb.AppendLine("    public static void Main() { }");
        sb.AppendLine("}");

        return (sb.ToString(), scenarios);
    }

    // ─── Test ────────────────────────────────────────────────────────────

    /// <summary>
    /// Property 10: Transpiled C# compiles without errors
    ///
    /// Batched pattern: generate 100 random valid Mist programs with newtypes
    /// (from primitives, structs, classes), compile the combined program in-process
    /// via CompilerDriver, collect all emitted C# into a single temp project,
    /// run dotnet build once, verify exit code 0.
    ///
    /// Process spawns: 1 (dotnet build).
    ///
    /// **Validates: Requirements 5.4**
    /// </summary>
    [Fact]
    public void TranspiledCSharp_CompilesWithoutErrors_BatchedScenarios()
    {
        // Step 1: Generate 100 random scenario templates using FsCheck
        var templates = GenTemplate().Sample(100).ToList();

        // Step 2: Build a single combined Mist program from all templates
        // Each template gets a unique index to avoid name collisions.
        var (combinedSource, scenarios) = BuildCombinedProgram(templates);

        // Step 3: Compile the combined program in-process via CompilerDriver
        var projectDir = Path.Combine(_outputDir, "combined");
        Directory.CreateDirectory(projectDir);

        var (files, error) = CompileInProcess(combinedSource, projectDir);

        Assert.True(files is not null,
            $"Combined Mist program failed in-process compilation:\n{error}\n\nSource (first 2000 chars):\n{combinedSource[..Math.Min(2000, combinedSource.Length)]}");

        // Step 4: Write emitted C# files into a temp project for dotnet build
        var buildProjectDir = Path.Combine(_outputDir, "build_project");
        Directory.CreateDirectory(buildProjectDir);

        // Write the .csproj file
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

        // Write all emitted C# files
        foreach (var (fileName, content) in files!)
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
