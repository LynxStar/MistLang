// Feature: void-value-literal, Property 4: Context-sensitive void emission
// **Validates: Requirements 6.1, 6.2, 6.4**

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
using Mist.Semantics.Model;

namespace Mist.Tests.Backend;

// ─── Scenario types ─────────────────────────────────────────────────────────

/// <summary>
/// Represents the context in which a void literal appears for emission testing.
/// </summary>
public enum VoidEmissionContext
{
    /// <summary>void literal with target type void → should emit MistVoid.Instance</summary>
    ValueLiteral,

    /// <summary>void literal with target type Type → should emit typeof(...)</summary>
    TypeLiteral,

    /// <summary>void literal in optional context (T | void) → should emit null</summary>
    OptionalContext,
}

/// <summary>
/// Scenario for Property 4: Context-sensitive void emission.
/// A void literal in a specific context, with expected emission pattern.
/// </summary>
public sealed class VoidEmissionScenario
{
    public string Source { get; }
    public VoidEmissionContext Context { get; }
    public string ExpectedPattern { get; }
    public string ForbiddenPattern { get; }
    public string Description { get; }

    public VoidEmissionScenario(string source, VoidEmissionContext context, string expectedPattern, string forbiddenPattern, string description)
    {
        Source = source;
        Context = context;
        ExpectedPattern = expectedPattern;
        ForbiddenPattern = forbiddenPattern;
        Description = description;
    }

    public override string ToString() => Description;
}

// ─── Generators ─────────────────────────────────────────────────────────────

public static class VoidEmissionGenerators
{
    private static readonly string[] VarNames = ["x", "y", "result", "value", "temp", "unit", "v"];
    private static readonly string[] MethodNames = ["DoWork", "Process", "Execute", "Run", "Handle"];

    /// <summary>
    /// Generates scenarios for void emission in value-literal and type-literal contexts.
    /// </summary>
    public static Gen<VoidEmissionScenario> GenVoidEmission()
    {
        return Gen.OneOf(
            GenValueLiteralVariable(),
            GenValueLiteralReturn(),
            GenValueLiteralArgument(),
            GenTypeLiteralVariable(),
            GenOptionalContextVariable());
    }

    private static Gen<VoidEmissionScenario> GenValueLiteralVariable()
    {
        return from varName in Gen.Elements(VarNames)
               let source = $@"namespace TestProject;

class Program {{
    static void Main() {{
        void {varName} = void;
    }}
}}"
               select new VoidEmissionScenario(source, VoidEmissionContext.ValueLiteral,
                   "MistVoid.Instance", "typeof(MistVoid)",
                   $"void {varName} = void; → value literal, expect MistVoid.Instance");
    }

    private static Gen<VoidEmissionScenario> GenValueLiteralReturn()
    {
        return from methodName in Gen.Elements(MethodNames)
               let source = $@"namespace TestProject;

class Program {{
    static void Main() {{ }}

    void {methodName}() {{
        return void;
    }}
}}"
               select new VoidEmissionScenario(source, VoidEmissionContext.ValueLiteral,
                   "MistVoid.Instance", "typeof(MistVoid)",
                   $"return void; in void method '{methodName}' → value literal, expect MistVoid.Instance");
    }

    private static Gen<VoidEmissionScenario> GenValueLiteralArgument()
    {
        return from methodName in Gen.Elements(MethodNames)
               from varName in Gen.Elements(VarNames)
               let source = $@"namespace TestProject;

class Program {{
    static void Main() {{
        void {varName} = void;
    }}

    static void {methodName}() {{
        return void;
    }}
}}"
               select new VoidEmissionScenario(source, VoidEmissionContext.ValueLiteral,
                   "MistVoid.Instance", "typeof(MistVoid)",
                   $"void {varName} = void; + return void in '{methodName}' → value literal, expect MistVoid.Instance");
    }

    private static Gen<VoidEmissionScenario> GenTypeLiteralVariable()
    {
        return from varName in Gen.Elements(VarNames)
               let source = $@"using Standard;

namespace TestProject;

class Program {{
    static void Main() {{
        Type {varName} = void;
    }}
}}"
               select new VoidEmissionScenario(source, VoidEmissionContext.TypeLiteral,
                   "typeof(", "MistVoid.Instance",
                   $"Type {varName} = void; → type literal, expect typeof(...)");
    }

    private static Gen<VoidEmissionScenario> GenOptionalContextVariable()
    {
        return from varName in Gen.Elements(VarNames)
               let source = $@"namespace TestProject;

class Program {{
    static void Main() {{
        int? {varName} = Void.Void;
    }}
}}"
               select new VoidEmissionScenario(source, VoidEmissionContext.OptionalContext,
                   "null", "MistVoid.Instance",
                   $"int? {varName} = Void.Void; → optional context, expect null");
    }
}

// ─── Property Tests ─────────────────────────────────────────────────────────

/// <summary>
/// Property-based tests for context-sensitive void emission in the C# backend.
///
/// Property 4 from the void-value-literal design document.
/// Uses in-process compilation via CompilerDriver with EmitToDisk: false to
/// inspect emitted C# content. Zero process spawns.
/// </summary>
public sealed class VoidValueLiteralEmissionPropertyTests : IDisposable
{
    private readonly string _outputDir;

    public VoidValueLiteralEmissionPropertyTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), "MistVoidEmitPBT_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(_outputDir))
        {
            try { Directory.Delete(_outputDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ─── Arbitrary registration ─────────────────────────────────────────

    public static Arbitrary<VoidEmissionScenario> ArbitraryVoidEmission() =>
        VoidEmissionGenerators.GenVoidEmission().ToArbitrary();

    // ─── Helpers ────────────────────────────────────────────────────────

    private CompilerResult CompileSource(string source)
    {
        var filePath = Path.Combine(_outputDir, $"Test_{Guid.NewGuid():N}.mist");
        File.WriteAllText(filePath, source);

        var driver = new CompilerDriver(
            new Mist.Lexing.MistLexer(),
            new Mist.Parsing.MistParser(),
            new SemanticModelBuilder(),
            ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
            ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

        var options = new CompilerOptions(
            ProjectFolder: _outputDir,
            SourcePaths: ImmutableArray.Create(filePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: _outputDir,
            BinDirectory: Path.Combine(_outputDir, "bin"),
            ProjectName: "TestProject",
            NamespaceRoot: "TestProject",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            EmitToDisk: false);

        return driver.Compile(options);
    }

    /// <summary>
    /// Gets the emitted C# content from the user source file.
    /// </summary>
    private static string? GetEmittedCSharp(CompilerResult result)
    {
        if(result.EmittedFiles is null)
        {
            return null;
        }

        // Look for the test file's emitted output
        foreach(var (fileName, fileContent) in result.EmittedFiles)
        {
            if(fileContent.Contains("namespace TestProject"))
            {
                return fileContent;
            }
        }

        // Fallback: return the first non-helper .cs file
        foreach(var (fileName, fileContent) in result.EmittedFiles)
        {
            if(fileName.EndsWith(".cs", StringComparison.Ordinal)
                && !fileName.EndsWith(".g.cs", StringComparison.Ordinal)
                && !fileName.EndsWith(".csproj", StringComparison.Ordinal))
            {
                return fileContent;
            }
        }

        return null;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 4: Context-sensitive void emission
    //
    // **Validates: Requirements 6.1, 6.2, 6.4**
    //
    // For any program containing `void` in expression position, the emitted
    // C# SHALL contain `MistVoid.Instance` (or `null` in optional union
    // context) when the expression resolved as a value literal (VoidType),
    // and `typeof(MistVoid)` when the expression resolved as a type literal
    // (Standard.Type).
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(VoidValueLiteralEmissionPropertyTests) })]
    public Property ContextSensitiveVoidEmission(VoidEmissionScenario scenario)
    {
        var result = CompileSource(scenario.Source);

        // Should compile without errors (warnings like MIST_SM_0023 are acceptable)
        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if(errors.Count > 0)
        {
            return false.ToProperty()
                .Label($"Compilation failed with errors:\n{string.Join("\n", errors.Select(e => $"  {e.Id.Value}: {e.Message}"))}\nSource:\n{scenario.Source}");
        }

        var csContent = GetEmittedCSharp(result);

        if(csContent is null)
        {
            return false.ToProperty()
                .Label($"No emitted C# content found.\nEmitted files: {(result.EmittedFiles is null ? "null" : string.Join(", ", result.EmittedFiles.Keys))}\nSource:\n{scenario.Source}");
        }

        var containsExpected = csContent.Contains(scenario.ExpectedPattern);
        var containsForbidden = !string.IsNullOrEmpty(scenario.ForbiddenPattern)
            && csContent.Contains(scenario.ForbiddenPattern);

        if(!containsExpected)
        {
            return false.ToProperty()
                .Label($"Expected emitted C# to contain '{scenario.ExpectedPattern}' but it did not.\nContext: {scenario.Context}\nEmitted:\n{csContent}\nSource:\n{scenario.Source}");
        }

        if(containsForbidden)
        {
            return false.ToProperty()
                .Label($"Emitted C# should NOT contain '{scenario.ForbiddenPattern}' but it does.\nContext: {scenario.Context}\nEmitted:\n{csContent}\nSource:\n{scenario.Source}");
        }

        return true.ToProperty();
    }
}