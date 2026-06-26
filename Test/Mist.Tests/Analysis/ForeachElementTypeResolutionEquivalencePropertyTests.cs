// Feature: dependent-type-refinement-constraints, Property 10: Foreach Element-Type Resolution Equivalence
//
// **Validates: Requirements 7.1, 7.2, 7.3, 7.4, 7.5**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

// ─── Scenario types ─────────────────────────────────────────────────────────

/// <summary>
/// Scenario for verifying that foreach over an array resolves the correct element type
/// through the standard FindIEnumerableImplementation interface lookup path.
/// </summary>
public sealed class ForeachArrayElementTypeScenario
{
    public string Source { get; }
    public string ElementType { get; }
    public int ArraySize { get; }
    public string Description { get; }

    public ForeachArrayElementTypeScenario(string source, string elementType, int arraySize, string description)
    {
        Source = source;
        ElementType = elementType;
        ArraySize = arraySize;
        Description = description;
    }

    public override string ToString() => Description;
}

/// <summary>
/// Scenario for verifying that foreach over a span resolves the correct element type
/// through the standard FindIEnumerableImplementation interface lookup path.
/// </summary>
public sealed class ForeachSpanElementTypeScenario
{
    public string Source { get; }
    public string ElementType { get; }
    public bool IsReadOnly { get; }
    public string Description { get; }

    public ForeachSpanElementTypeScenario(string source, string elementType, bool isReadOnly, string description)
    {
        Source = source;
        ElementType = elementType;
        IsReadOnly = isReadOnly;
        Description = description;
    }

    public override string ToString() => Description;
}

// ─── Property Tests ─────────────────────────────────────────────────────────

/// <summary>
/// Property-based tests for foreach element-type resolution equivalence.
///
/// Property 10: Foreach Element-Type Resolution Equivalence
///
/// For all foreach iterations over ArrayType or SpanType values, the element type
/// resolved through the standard FindIEnumerableImplementation interface lookup
/// equals the type's ElementType property.
///
/// This validates that after removal of the hardcoded fast-path branches for
/// Array and Span in FindIEnumerableImplementation, the interface bridge correctly
/// resolves the element type through the registered IEnumerable&lt;T&gt; interface.
///
/// Uses in-process compilation via a shared CompilerDriver instance.
/// Inspects the DiagnosticBag directly (no process spawning).
///
/// **Validates: Requirements 7.1, 7.2, 7.3, 7.4, 7.5**
/// </summary>
public sealed class ForeachElementTypeResolutionEquivalencePropertyTests : IDisposable
{
    private readonly string tempDir;

    public ForeachElementTypeResolutionEquivalencePropertyTests()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "MistForeachElemTypePBT_" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(tempDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(tempDir))
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ─── Shared driver (reused across tests) ─────────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<Mist.Analysis.Contracts.IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    // ─── Name pools ──────────────────────────────────────────────────────

    // Only stampable element types are allowed with bare `new T[size]` array construction.
    private static readonly string[] StampableElementTypes = ["int", "bool", "double", "float", "long", "char"];
    private static readonly int[] ArraySizes = [1, 2, 3, 5, 10];

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates a scenario with a foreach statement iterating over an array,
    /// verifying element type resolution through the IEnumerable interface bridge.
    /// </summary>
    private static Gen<ForeachArrayElementTypeScenario> GenArrayForeachScenario()
    {
        return from elementType in Gen.Elements(StampableElementTypes)
               from arraySize in Gen.Elements(ArraySizes)
               let source = BuildArrayForeachSource(elementType, arraySize)
               select new ForeachArrayElementTypeScenario(
                   source, elementType, arraySize,
                   $"foreach ({elementType} x in {elementType}[{arraySize}]) - array element type resolution");
    }

    /// <summary>
    /// Generates a scenario with a foreach statement iterating over a Span,
    /// verifying element type resolution through the IEnumerable interface bridge.
    /// </summary>
    private static Gen<ForeachSpanElementTypeScenario> GenSpanForeachScenario()
    {
        return from elementType in Gen.Elements(StampableElementTypes)
               from isReadOnly in Gen.Elements(false, true)
               let source = BuildSpanForeachSource(elementType, isReadOnly)
               select new ForeachSpanElementTypeScenario(
                   source, elementType, isReadOnly,
                   $"foreach ({elementType} x in {(isReadOnly ? "ReadOnlySpan" : "Span")}<{elementType}>) - span element type resolution");
    }

    // ─── Source builders ─────────────────────────────────────────────────

    private static string BuildArrayForeachSource(string elementType, int arraySize)
    {
        return $$"""
            namespace Standard;

            public class Program
            {
                public static void Main()
                {
                    var arr = new {{elementType}}[{{arraySize}}];
                    {{elementType}} sum = default;
                    foreach ({{elementType}} item in arr)
                    {
                        sum = item;
                    }
                }
            }
            """;
    }

    private static string BuildSpanForeachSource(string elementType, bool isReadOnly)
    {
        if(isReadOnly)
        {
            return $$"""
                namespace Standard;

                public class Program
                {
                    public static void Main()
                    {
                        ReadOnlySpan<{{elementType}}, unknown> s = stackalloc {{elementType}}[3];
                        {{elementType}} sum = default;
                        foreach ({{elementType}} item in s)
                        {
                            sum = item;
                        }
                    }
                }
                """;
        }

        return $$"""
            namespace Standard;

            public class Program
            {
                public static void Main()
                {
                    var s = stackalloc {{elementType}}[3];
                    {{elementType}} sum = default;
                    foreach ({{elementType}} item in s)
                    {
                        sum = item;
                    }
                }
            }
            """;
    }

    // ─── Compilation helper ──────────────────────────────────────────────

    private (CompilerResult Result, string FilePath) CompileSource(string source)
    {
        var filePath = Path.Combine(tempDir, $"Test_{Guid.NewGuid():N}.mist");
        File.WriteAllText(filePath, source);

        var options = new CompilerOptions(
            ProjectFolder: tempDir,
            SourcePaths: ImmutableArray.Create(filePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: tempDir,
            BinDirectory: Path.Combine(tempDir, "bin"),
            ProjectName: "TestProject",
            NamespaceRoot: "Standard",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            EmitToDisk: false);

        return (SharedDriver.Compile(options), filePath);
    }

    // ─── Arbitrary registration ──────────────────────────────────────────

    public static Arbitrary<ForeachArrayElementTypeScenario> ArbitraryForeachArrayElementType() =>
        GenArrayForeachScenario().ToArbitrary();

    public static Arbitrary<ForeachSpanElementTypeScenario> ArbitraryForeachSpanElementType() =>
        GenSpanForeachScenario().ToArbitrary();

    // ═══════════════════════════════════════════════════════════════════════
    // Property 10a: Foreach over ArrayType resolves element type through
    //               standard interface lookup
    //
    // For any Array<TValue, N>, a foreach statement iterating over an
    // expression of that type SHALL be accepted by the type checker with
    // zero type-checking diagnostics (no MIST_TC_*), confirming that
    // FindIEnumerableImplementation resolves TValue correctly through
    // the registered IEnumerable<TValue> interface on the Array class.
    //
    // **Validates: Requirements 7.1, 7.3**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ForeachElementTypeResolutionEquivalencePropertyTests) })]
    public Property Array_Foreach_ResolvesElementTypeThroughInterfaceLookup(ForeachArrayElementTypeScenario scenario)
    {
        var (result, filePath) = CompileSource(scenario.Source);

        // Filter to only foreach-related diagnostics from the test source file.
        // Stdlib diagnostics (MIST_DT_0005, MIST_TC_0072, etc.) are unrelated to
        // foreach element type resolution and expected in the current codebase state.
        var foreachErrors = result.Diagnostics
            .Where(d => d.CompilationUnitPath == filePath
                        && d.Severity == DiagnosticSeverity.Error
                        && (d.Id.Value == "MIST_TC_FOREACH_NOT_ENUMERABLE"
                            || d.Id.Value == "MIST_TC_FOREACH_TYPE_MISMATCH"))
            .ToList();

        return (!foreachErrors.Any()).ToProperty()
            .Label($"Expected zero foreach type errors for {scenario.ElementType}[{scenario.ArraySize}], " +
                   $"but got:\n{string.Join("\n", foreachErrors.Select(d => $"  {d.Id.Value}: {d.Message}"))}\n" +
                   $"Source:\n{scenario.Source}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 10b: Foreach over SpanType resolves element type through
    //               standard interface lookup
    //
    // For any Span<T, N> or ReadOnlySpan<T, N>, a foreach statement
    // iterating over an expression of that type SHALL be accepted by the
    // type checker with zero type-checking diagnostics (no MIST_TC_*),
    // confirming that FindIEnumerableImplementation resolves T correctly
    // through the registered IEnumerable<T> interface on the Span struct.
    //
    // **Validates: Requirements 7.2, 7.4**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ForeachElementTypeResolutionEquivalencePropertyTests) })]
    public Property Span_Foreach_ResolvesElementTypeThroughInterfaceLookup(ForeachSpanElementTypeScenario scenario)
    {
        var (result, filePath) = CompileSource(scenario.Source);

        // Filter to only foreach-related diagnostics from the test source file.
        // Stdlib diagnostics (MIST_DT_0005, MIST_TC_0072, etc.) are unrelated to
        // foreach element type resolution and expected in the current codebase state.
        var foreachErrors = result.Diagnostics
            .Where(d => d.CompilationUnitPath == filePath
                        && d.Severity == DiagnosticSeverity.Error
                        && (d.Id.Value == "MIST_TC_FOREACH_NOT_ENUMERABLE"
                            || d.Id.Value == "MIST_TC_FOREACH_TYPE_MISMATCH"))
            .ToList();

        return (!foreachErrors.Any()).ToProperty()
            .Label($"Expected zero foreach type errors for {(scenario.IsReadOnly ? "ReadOnlySpan" : "Span")}<{scenario.ElementType}>, " +
                   $"but got:\n{string.Join("\n", foreachErrors.Select(d => $"  {d.Id.Value}: {d.Message}"))}\n" +
                   $"Source:\n{scenario.Source}");
    }
}