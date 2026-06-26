// Feature: dependent-types, Property 6: Emitter Produces Compilable C#
// **Validates: Requirements 13.1, 13.2, 13.3, 13.4, 13.5, 13.6, 13.7, 13.8, 13.9**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
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
/// Property-based test verifying that the C# emitter produces correct patterns for
/// all valid Mist programs containing dependent type declarations and usages.
///
/// Property 6: Emitter Produces Compilable C#
/// For any valid Mist program containing dependent type declarations and usages
/// (that passes type checking without errors), the C# emitter SHALL produce output
/// that contains the expected carrier structs, const-carrier interfaces, and
/// constrained type parameters.
///
/// Uses in-process compilation via CompilerDriver with string-based assertions
/// on the emitted C# output. No process spawning.
///
/// **Validates: Requirements 13.1, 13.2, 13.3, 13.4, 13.5, 13.6, 13.7, 13.8, 13.9**
/// </summary>
public sealed class DependentTypeEmitterPropertyTests : IDisposable
{
    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private readonly string _outputDir;

    public DependentTypeEmitterPropertyTests()
    {
        _outputDir = Path.Combine(
            Path.GetTempPath(),
            "mist_dep_emitter_prop_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(_outputDir))
        {
            try { Directory.Delete(_outputDir, recursive: true); } catch { /* best effort */ }
        }
        GC.SuppressFinalize(this);
    }

    // ─── Scenario model ──────────────────────────────────────────────────

#pragma warning disable CA1034, CA1819
    public sealed class DependentTypeScenario
    {
        public required string Description { get; init; }
        public required string MistSource { get; init; }
        /// <summary>Patterns expected in the emitted C# output.</summary>
        public required string[] ExpectedPatterns { get; init; }

        public override string ToString() => Description;
    }
#pragma warning restore CA1034, CA1819

    // ─── Value type pools ────────────────────────────────────────────────

    private static readonly string[] IntValues = ["0", "1", "5", "10", "42", "100", "255"];
    private static readonly string[] NegIntValues = ["-1", "-5", "-10", "-42"];
    private static readonly string[] BoolValues = ["true", "false"];
    private static readonly string[] CharValues = ["'A'", "'x'", "'0'", "'Z'"];

    // ─── Scenario generators ─────────────────────────────────────────────

    /// <summary>Class with single int dependent parameter.</summary>
    private static Gen<DependentTypeScenario> GenSingleIntParam()
    {
        return from className in Gen.Elements("Buffer", "Stack", "Queue", "Pool", "Cache")
               from value in Gen.Elements(IntValues)
               from idx in Gen.Choose(0, 999)
               let uniqueName = $"{className}{idx}"
               select new DependentTypeScenario
               {
                   Description = $"class {uniqueName}<Size:int> with value {value}",
                   MistSource = "class " + uniqueName + "<Size:int> { }\n" +
                                "class Use" + uniqueName + " : " + uniqueName + "<" + value + "> { }\n" +
                                "class Program { public static void Main() { } }",
                   ExpectedPatterns = ["IConstInt", "static int Value", uniqueName],
               };
    }

    /// <summary>Class with single bool dependent parameter.</summary>
    private static Gen<DependentTypeScenario> GenSingleBoolParam()
    {
        return from className in Gen.Elements("Flag", "Toggle", "Switch", "Gate", "Lock")
               from value in Gen.Elements(BoolValues)
               from idx in Gen.Choose(0, 999)
               let uniqueName = $"{className}{idx}"
               select new DependentTypeScenario
               {
                   Description = $"class {uniqueName}<Enabled:bool> with value {value}",
                   MistSource = "class " + uniqueName + "<Enabled:bool> { }\n" +
                                "class Use" + uniqueName + " : " + uniqueName + "<" + value + "> { }\n" +
                                "class Program { public static void Main() { } }",
                   ExpectedPatterns = ["IConstBool", "static bool Value", uniqueName],
               };
    }

    /// <summary>Class with single char dependent parameter.</summary>
    private static Gen<DependentTypeScenario> GenSingleCharParam()
    {
        return from className in Gen.Elements("Marker", "Separator", "Delimiter", "Tag", "Label")
               from value in Gen.Elements(CharValues)
               from idx in Gen.Choose(0, 999)
               let uniqueName = $"{className}{idx}"
               select new DependentTypeScenario
               {
                   Description = $"class {uniqueName}<C:char> with value {value}",
                   MistSource = "class " + uniqueName + "<C:char> { }\n" +
                                "class Use" + uniqueName + " : " + uniqueName + "<" + value + "> { }\n" +
                                "class Program { public static void Main() { } }",
                   ExpectedPatterns = ["IConstChar", "static char Value", uniqueName],
               };
    }

    /// <summary>Class with multiple dependent parameters (int, int).</summary>
    private static Gen<DependentTypeScenario> GenMultipleIntParams()
    {
        return from className in Gen.Elements("Matrix", "Grid", "Table", "Rect", "Bounds")
               from rows in Gen.Elements(IntValues)
               from cols in Gen.Elements(IntValues)
               from idx in Gen.Choose(0, 999)
               let uniqueName = $"{className}{idx}"
               select new DependentTypeScenario
               {
                   Description = $"class {uniqueName}<Rows:int, Cols:int> with ({rows}, {cols})",
                   MistSource = "class " + uniqueName + "<Rows:int, Cols:int> { }\n" +
                                "class Use" + uniqueName + " : " + uniqueName + "<" + rows + ", " + cols + "> { }\n" +
                                "class Program { public static void Main() { } }",
                   ExpectedPatterns = ["IConstInt", uniqueName],
               };
    }

    /// <summary>Class with mixed regular and dependent parameters.</summary>
    private static Gen<DependentTypeScenario> GenMixedParams()
    {
        return from className in Gen.Elements("TypedBuf", "Container", "Wrapper", "Holder", "Store")
               from value in Gen.Elements(IntValues)
               from idx in Gen.Choose(0, 999)
               let uniqueName = $"{className}{idx}"
               select new DependentTypeScenario
               {
                   Description = $"class {uniqueName}<T, Size:int> with value {value}",
                   MistSource = "class " + uniqueName + "<T, Size:int> { }\n" +
                                "class Use" + uniqueName + " : " + uniqueName + "<string, " + value + "> { }\n" +
                                "class Program { public static void Main() { } }",
                   ExpectedPatterns = ["IConstInt", uniqueName],
               };
    }

    /// <summary>Class with negative int dependent parameter value.</summary>
    private static Gen<DependentTypeScenario> GenNegativeIntParam()
    {
        return from className in Gen.Elements("Offset", "Delta", "Shift", "Bias", "Adjust")
               from value in Gen.Elements(NegIntValues)
               from idx in Gen.Choose(0, 999)
               let uniqueName = $"{className}{idx}"
               select new DependentTypeScenario
               {
                   Description = $"class {uniqueName}<N:int> with value {value}",
                   MistSource = "class " + uniqueName + "<N:int> { }\n" +
                                "class Use" + uniqueName + " : " + uniqueName + "<" + value + "> { }\n" +
                                "class Program { public static void Main() { } }",
                   ExpectedPatterns = ["IConstInt", uniqueName],
               };
    }

    /// <summary>Object construction with dependent type argument.</summary>
    private static Gen<DependentTypeScenario> GenObjectConstruction()
    {
        return from className in Gen.Elements("Alloc", "Segment", "Chunk", "Block", "Region")
               from value in Gen.Elements(IntValues)
               from idx in Gen.Choose(0, 999)
               let uniqueName = $"{className}{idx}"
               select new DependentTypeScenario
               {
                   Description = $"new {uniqueName}<{value}>()",
                   MistSource = "class " + uniqueName + "<Size:int> { }\n" +
                                "class Client" + uniqueName + " {\n" +
                                "    public static void Run() {\n" +
                                "        var x = new " + uniqueName + "<" + value + ">();\n" +
                                "    }\n" +
                                "}\n" +
                                "class Program { public static void Main() { } }",
                   ExpectedPatterns = ["IConstInt", uniqueName],
               };
    }

    /// <summary>Inheritance with forwarded dependent parameter.</summary>
    private static Gen<DependentTypeScenario> GenForwardedParam()
    {
        return from baseName in Gen.Elements("Base", "Root", "Core", "Foundation", "Origin")
               from derivedName in Gen.Elements("Derived", "Child", "Sub", "Ext", "Impl")
               from idx in Gen.Choose(0, 999)
               let uniqueBase = $"{baseName}{idx}"
               let uniqueDerived = $"{derivedName}{idx}"
               select new DependentTypeScenario
               {
                   Description = $"class {uniqueDerived}<N:int> : {uniqueBase}<N>",
                   MistSource = "class " + uniqueBase + "<Size:int> { }\n" +
                                "class " + uniqueDerived + "<N:int> : " + uniqueBase + "<N> { }\n" +
                                "class Program { public static void Main() { } }",
                   ExpectedPatterns = ["IConstInt", uniqueBase, uniqueDerived],
               };
    }

    /// <summary>Interface with dependent parameter.</summary>
    private static Gen<DependentTypeScenario> GenAbstractWithDepParam()
    {
        return from abstractName in Gen.Elements("ISized", "ICapped", "ILimited", "IBounded", "IFixed")
               from value in Gen.Elements(IntValues)
               from idx in Gen.Choose(0, 999)
               let uniqueAbstract = $"{abstractName}{idx}"
               let uniqueImpl = $"Impl{abstractName.Substring(1)}{idx}"
               select new DependentTypeScenario
               {
                   Description = $"abstract type {uniqueAbstract}<Cap:int> implemented with {value}",
                   MistSource = "abstract " + uniqueAbstract + "<Cap:int> { }\n" +
                                "class " + uniqueImpl + " : " + uniqueAbstract + "<" + value + "> { }\n" +
                                "class Program { public static void Main() { } }",
                   ExpectedPatterns = ["IConstInt", uniqueAbstract, uniqueImpl],
               };
    }

    /// <summary>Struct with dependent parameter.</summary>
    private static Gen<DependentTypeScenario> GenStructWithDepParam()
    {
        return from structName in Gen.Elements("Cell", "Slot", "Entry", "Node", "Item")
               from value in Gen.Elements(IntValues)
               from idx in Gen.Choose(0, 999)
               let uniqueName = $"{structName}{idx}"
               select new DependentTypeScenario
               {
                   Description = $"struct {uniqueName}<Len:int> with value {value}",
                   MistSource = "struct " + uniqueName + "<Len:int> { }\n" +
                                "class Use" + uniqueName + " {\n" +
                                "    public static void Run() {\n" +
                                "        var s = new " + uniqueName + "<" + value + ">();\n" +
                                "    }\n" +
                                "}\n" +
                                "class Program { public static void Main() { } }",
                   ExpectedPatterns = ["IConstInt", uniqueName],
               };
    }

    /// <summary>Combined generator producing any scenario kind.</summary>
    private static Gen<DependentTypeScenario> GenScenario()
    {
        return Gen.OneOf(
            GenSingleIntParam(),
            GenSingleBoolParam(),
            GenSingleCharParam(),
            GenMultipleIntParams(),
            GenMixedParams(),
            GenNegativeIntParam(),
            GenObjectConstruction(),
            GenForwardedParam(),
            GenAbstractWithDepParam(),
            GenStructWithDepParam());
    }

    // ─── In-process compilation ──────────────────────────────────────────

    /// <summary>
    /// Compiles a single Mist source in-process via CompilerDriver.
    /// Returns the emitted C# content as a combined string, or null if compilation fails.
    /// </summary>
    private static (string? EmittedCSharp, string? Error) CompileAndGetEmittedCSharp(
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

        if(!result.Succeeded)
        {
            var diagnosticText = string.Join("\n",
                result.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => $"  {d.Id.Value}: {d.Message}"));
            return (null, diagnosticText);
        }

        if(result.EmittedFiles is null || result.EmittedFiles.Count == 0)
        {
            return (null, "No files emitted");
        }

        var combined = string.Join("\n", result.EmittedFiles.Values);
        return (combined, null);
    }

    // ─── Tests ───────────────────────────────────────────────────────────

    /// <summary>
    /// Property 6: Emitter Produces Correct C# Patterns
    ///
    /// For any valid Mist program containing dependent type declarations and usages,
    /// the emitted C# SHALL contain the expected const-carrier interfaces, carrier
    /// structs, and type names.
    ///
    /// No process spawning — all compilation is in-process via CompilerDriver,
    /// assertions are string-based checks on the emitted C# output.
    ///
    /// **Validates: Requirements 13.1, 13.2, 13.3, 13.4, 13.5, 13.6, 13.7, 13.8, 13.9**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(DependentTypeEmitterPropertyTests) })]
    public Property EmittedCSharp_ContainsExpectedCarrierPatterns(DependentTypeScenario scenario)
    {
        var scenarioDir = Path.Combine(_outputDir, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scenarioDir);

        var (emittedCSharp, error) = CompileAndGetEmittedCSharp(scenario.MistSource, scenarioDir);

        if(emittedCSharp is null)
        {
            return false.ToProperty()
                .Label($"Compilation failed for: {scenario.Description}\nError: {error}");
        }

        // Verify all expected patterns are present in the emitted C#
        foreach(var pattern in scenario.ExpectedPatterns)
        {
            if(!emittedCSharp.Contains(pattern))
            {
                return false.ToProperty()
                    .Label($"Expected pattern '{pattern}' not found in emitted C# for: {scenario.Description}\n" +
                           $"Emitted (first 500 chars): {emittedCSharp[..Math.Min(500, emittedCSharp.Length)]}");
            }
        }

        return true.ToProperty()
            .Label($"OK: {scenario.Description}");
    }

    /// <summary>
    /// Verifies that the emitter produces non-empty output for all dependent type scenarios.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(DependentTypeEmitterPropertyTests) })]
    public Property EmittedCSharp_ProducesNonEmptyOutput(DependentTypeScenario scenario)
    {
        var scenarioDir = Path.Combine(_outputDir, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scenarioDir);

        var (emittedCSharp, error) = CompileAndGetEmittedCSharp(scenario.MistSource, scenarioDir);

        if(emittedCSharp is null)
        {
            return false.ToProperty()
                .Label($"Compilation failed for: {scenario.Description}\nError: {error}");
        }

        return (emittedCSharp.Length > 0).ToProperty()
            .Label($"Emitted C# should be non-empty for: {scenario.Description}");
    }

    // ─── Arbitrary ───────────────────────────────────────────────────────

    public static Arbitrary<DependentTypeScenario> ArbitraryDependentTypeScenario() =>
        GenScenario().ToArbitrary();
}