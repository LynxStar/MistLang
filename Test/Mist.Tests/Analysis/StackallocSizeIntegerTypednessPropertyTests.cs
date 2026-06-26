// Feature: stack-only-types, Property 7: Stackalloc size must be integer-typed
//
// **Validates: Requirements 4.7, 4.8**
//
// In-process; assert accepted (w.r.t. size) iff size type is Int32 or implicitly
// convertible to Int32, else MIST_TC_0151 naming the size type.

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

namespace Mist.Tests.Analysis;

// ─── Scenario model ─────────────────────────────────────────────────────────

/// <summary>
/// Describes a stackalloc size-type scenario for property testing.
/// </summary>
public sealed record StackallocSizeTypeScenario(
    string Source,
    bool ShouldAccept,
    string? ExpectedSizeTypeName,
    string Description)
{
    public override string ToString() => Description;
}

// ─── Property Tests ─────────────────────────────────────────────────────────

/// <summary>
/// Property-based tests for stackalloc size integer-typedness (Property 7).
///
/// Verifies that <see cref="TypeCheckerAnalyzer"/> emits MIST_TC_0151 when the
/// size expression of a <c>stackalloc T[n]</c> is not integer-typed, and accepts
/// the expression when the size is <c>Int32</c>.
///
/// Scenarios:
/// - Int literal size (accepted): <c>stackalloc int[5]</c>
/// - Int variable size (accepted): <c>var n = 5; stackalloc int[n];</c>
/// - String variable size (rejected with MIST_TC_0151)
/// - Bool variable size (rejected with MIST_TC_0151)
/// - Float variable size (rejected with MIST_TC_0151)
///
/// Uses in-process compilation via a shared <see cref="CompilerDriver"/> instance.
/// Zero process spawns.
///
/// **Validates: Requirements 4.7, 4.8**
/// </summary>
public class StackallocSizeIntegerTypednessPropertyTests
{
    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    // ─── Stampable element types (for variety in generation) ─────────────

    private static readonly string[] ElementTypes =
        ["int", "byte", "float", "double", "bool", "char", "long", "short"];

    // ─── FsCheck generators ─────────────────────────────────────────────

    /// <summary>
    /// Generates a scenario with an integer literal as the size (accepted).
    /// </summary>
    private static Gen<StackallocSizeTypeScenario> GenIntLiteralSizeScenario()
    {
        return from typeIdx in Gen.Choose(0, ElementTypes.Length - 1)
               from size in Gen.Choose(1, 100)
               let elementType = ElementTypes[typeIdx]
               let source = WrapInProgram($"var s = stackalloc {elementType}[{size}];")
               select new StackallocSizeTypeScenario(
                   source,
                   ShouldAccept: true,
                   ExpectedSizeTypeName: null,
                   $"stackalloc {elementType}[{size}] — int literal size → accepted");
    }

    /// <summary>
    /// Generates a scenario with an int variable as the size (accepted).
    /// </summary>
    private static Gen<StackallocSizeTypeScenario> GenIntVariableSizeScenario()
    {
        return from typeIdx in Gen.Choose(0, ElementTypes.Length - 1)
               from sizeVal in Gen.Choose(1, 50)
               let elementType = ElementTypes[typeIdx]
               let source = WrapInProgram(
                   $"var n = {sizeVal};\n        var s = stackalloc {elementType}[n];")
               select new StackallocSizeTypeScenario(
                   source,
                   ShouldAccept: true,
                   ExpectedSizeTypeName: null,
                   $"stackalloc {elementType}[n] where n:int={sizeVal} → accepted");
    }

    /// <summary>
    /// Generates a scenario with a string variable as the size (rejected).
    /// </summary>
    private static Gen<StackallocSizeTypeScenario> GenStringVariableSizeScenario()
    {
        return from typeIdx in Gen.Choose(0, ElementTypes.Length - 1)
               let elementType = ElementTypes[typeIdx]
               let source = WrapInProgram(
                   "var n = \"hello\";\n        var s = stackalloc " + elementType + "[n];")
               select new StackallocSizeTypeScenario(
                   source,
                   ShouldAccept: false,
                   ExpectedSizeTypeName: "String",
                   $"stackalloc {elementType}[n] where n:String → rejected MIST_TC_0151");
    }

    /// <summary>
    /// Generates a scenario with a bool variable as the size (rejected).
    /// The compiler reports the type as 'Boolean' (the stdlib display name).
    /// </summary>
    private static Gen<StackallocSizeTypeScenario> GenBoolVariableSizeScenario()
    {
        return from typeIdx in Gen.Choose(0, ElementTypes.Length - 1)
               let elementType = ElementTypes[typeIdx]
               let source = WrapInProgram(
                   "var n = true;\n        var s = stackalloc " + elementType + "[n];")
               select new StackallocSizeTypeScenario(
                   source,
                   ShouldAccept: false,
                   ExpectedSizeTypeName: "Boolean",
                   $"stackalloc {elementType}[n] where n:Boolean → rejected MIST_TC_0151");
    }

    /// <summary>
    /// Generates a scenario with a float variable as the size (rejected).
    /// The compiler reports the type as 'Double' (the stdlib display name).
    /// </summary>
    private static Gen<StackallocSizeTypeScenario> GenFloatVariableSizeScenario()
    {
        return from typeIdx in Gen.Choose(0, ElementTypes.Length - 1)
               let elementType = ElementTypes[typeIdx]
               let source = WrapInProgram(
                   "var n = 3.14;\n        var s = stackalloc " + elementType + "[n];")
               select new StackallocSizeTypeScenario(
                   source,
                   ShouldAccept: false,
                   ExpectedSizeTypeName: "Double",
                   $"stackalloc {elementType}[n] where n:Double → rejected MIST_TC_0151");
    }

    /// <summary>
    /// Combined generator: all scenario types with equal probability.
    /// </summary>
    public static Arbitrary<StackallocSizeTypeScenario> ArbitraryScenario()
    {
        return Gen.OneOf(
            GenIntLiteralSizeScenario(),
            GenIntVariableSizeScenario(),
            GenStringVariableSizeScenario(),
            GenBoolVariableSizeScenario(),
            GenFloatVariableSizeScenario()).ToArbitrary();
    }

    // ─── Program wrapping ───────────────────────────────────────────────

    private static string WrapInProgram(string body)
    {
        return $$"""
            class Program
            {
                static void Main()
                {
                    {{body}}
                }
            }
            """;
    }

    // ─── Compilation helper ──────────────────────────────────────────────

    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"mist_pbt_{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(tempDir);
        try
        {
            var sourcePath = Path.Combine(tempDir, "Program.mist");
            File.WriteAllText(sourcePath, source);

            var options = new CompilerOptions(
                ProjectFolder: tempDir,
                SourcePaths: ImmutableArray.Create(sourcePath),
                Backend: new BackendId("csharp"),
                OutputDirectory: tempDir,
                BinDirectory: Path.Combine(tempDir, "bin"),
                ProjectName: "TestProject",
                NamespaceRoot: "TestProject",
                EnabledAnalyzerIds: ImmutableArray<string>.Empty,
                EmitToDisk: false);

            var result = SharedDriver.Compile(options);
            return result.Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort cleanup */ }
        }
    }

    /// <summary>
    /// Extracts MIST_TC_0151 diagnostics from a compilation result.
    /// </summary>
    private static List<Diagnostic> ExtractSizeTypeDiagnostics(ImmutableArray<Diagnostic> diagnostics)
    {
        return diagnostics
            .Where(d => d.Id.Value == "MIST_TC_0151")
            .ToList();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 7: Stackalloc size must be integer-typed
    //
    // **Validates: Requirements 4.7, 4.8**
    //
    // For any stackalloc expression stackalloc T[n]:
    // - If n is Int32-typed → no MIST_TC_0151 (accepted w.r.t. size)
    // - If n is not Int32-typed → MIST_TC_0151 naming the size type
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 7: A stackalloc expression is accepted (w.r.t. size) iff the size
    /// expression is of type Int32, and rejected with MIST_TC_0151 naming the size
    /// type otherwise.
    ///
    /// **Validates: Requirements 4.7, 4.8**
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(StackallocSizeIntegerTypednessPropertyTests) })]
    public Property StackallocSize_AcceptedIffIntegerTyped(StackallocSizeTypeScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);
        var sizeTypeDiags = ExtractSizeTypeDiagnostics(diagnostics);

        if(scenario.ShouldAccept)
        {
            // No MIST_TC_0151 should be emitted for integer-typed sizes.
            var passed = sizeTypeDiags.Count == 0;
            return passed.ToProperty()
                .Label(
                    $"Expected no MIST_TC_0151 but got {sizeTypeDiags.Count}.\n" +
                    $"Scenario: {scenario}\n" +
                    $"Diagnostics: [{string.Join(", ", sizeTypeDiags.Select(d => d.Message))}]\n" +
                    $"Source:\n{scenario.Source}");
        }
        else
        {
            // Exactly one MIST_TC_0151 should be emitted, naming the size type.
            var hasDiag = sizeTypeDiags.Count == 1;
            var mentionsType = hasDiag && scenario.ExpectedSizeTypeName is not null
                && sizeTypeDiags[0].Message.Contains(scenario.ExpectedSizeTypeName);

            var passed = hasDiag && mentionsType;
            return passed.ToProperty()
                .Label(
                    $"Expected exactly 1 MIST_TC_0151 mentioning '{scenario.ExpectedSizeTypeName}' " +
                    $"but got {sizeTypeDiags.Count} diagnostic(s).\n" +
                    $"Messages: [{string.Join(", ", sizeTypeDiags.Select(d => d.Message))}]\n" +
                    $"All errors: [{string.Join(", ", diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => $"{d.Id.Value}: {d.Message}"))}]\n" +
                    $"Scenario: {scenario}\n" +
                    $"Source:\n{scenario.Source}");
        }
    }
}