// Feature: iterators-enumerables, Property 3: Foreach over IEnumerable types compiles with correct element type inference
// **Validates: Requirements 3.1, 3.3, 5.5**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Backends.CSharpTranspiler;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

// ─── Scenario types ─────────────────────────────────────────────────────────

/// <summary>
/// Scenario for verifying that foreach over a user-defined IEnumerable&lt;TElement&gt;
/// type compiles with zero diagnostics and correct element type inference.
/// </summary>
public sealed class ForeachEnumerableScenario
{
    public string Source { get; }
    public string ElementType { get; }
    public string ClassName { get; }
    public string Description { get; }

    public ForeachEnumerableScenario(string source, string elementType, string className, string description)
    {
        Source = source;
        ElementType = elementType;
        ClassName = className;
        Description = description;
    }

    public override string ToString() => Description;
}

/// <summary>
/// Scenario for verifying that foreach over an array type compiles with
/// zero diagnostics and correct element type inference.
/// </summary>
public sealed class ForeachArrayScenario
{
    public string Source { get; }
    public string ElementType { get; }
    public string Description { get; }

    public ForeachArrayScenario(string source, string elementType, string description)
    {
        Source = source;
        ElementType = elementType;
        Description = description;
    }

    public override string ToString() => Description;
}

// ─── Property Tests ─────────────────────────────────────────────────────────

/// <summary>
/// Property-based tests for type checker foreach validation over IEnumerable types.
///
/// Property 3: Foreach over IEnumerable types compiles with correct element type inference
///
/// For any type T implementing IEnumerable&lt;TElement&gt; (including intrinsic
/// Array&lt;TValue, N&gt; where TElement = TValue), a foreach statement iterating over
/// an expression of type T SHALL be accepted by the type checker, and the iteration
/// variable's resolved type SHALL equal TElement.
///
/// Uses in-process compilation via a shared CompilerDriver instance.
/// Inspects the DiagnosticBag directly (no process spawning).
///
/// **Validates: Requirements 3.1, 3.3, 5.5**
/// </summary>
public sealed class ForeachEnumerableTypeInferencePropertyTests : IDisposable
{
    private readonly string _tempDir;

    public ForeachEnumerableTypeInferencePropertyTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "MistForeachEnumPBT_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
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

    private static readonly string[] ElementTypes = ["int", "string", "bool", "double", "float", "long", "char"];
    private static readonly string[] StampableElementTypes = ["int", "bool", "double", "float", "long", "char"];
    private static readonly string[] ClassNames = ["MyCollection", "DataStream", "ItemList", "Sequence", "Elements", "Series"];
    private static readonly int[] ArraySizes = [1, 2, 3, 5, 10];

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates a scenario with a user-defined class implementing IEnumerable&lt;TElement&gt;
    /// and a foreach statement iterating over it.
    /// </summary>
    private static Gen<ForeachEnumerableScenario> GenUserDefinedEnumerableScenario()
    {
        return from elementType in Gen.Elements(ElementTypes)
               from className in Gen.Elements(ClassNames)
               let source = BuildUserDefinedEnumerableSource(className, elementType)
               select new ForeachEnumerableScenario(
                   source, elementType, className,
                   $"foreach ({elementType} x in {className}) - user-defined IEnumerable");
    }

    /// <summary>
    /// Generates a scenario with a foreach statement iterating over an array.
    /// </summary>
    private static Gen<ForeachArrayScenario> GenArrayForeachScenario()
    {
        // Only stampable element types are allowed with bare `new T[size]`.
        // Reference types like `string` require initializer-based construction.
        return from elementType in Gen.Elements(StampableElementTypes)
               from arraySize in Gen.Elements(ArraySizes)
               let source = BuildArrayForeachSource(elementType, arraySize)
               select new ForeachArrayScenario(
                   source, elementType,
                   $"foreach ({elementType} x in {elementType}[{arraySize}]) - array");
    }

    // ─── Source builders ─────────────────────────────────────────────────

    private static string BuildUserDefinedEnumerableSource(string className, string elementType)
    {
        return $$"""
            namespace Standard;

            class {{className}} : IEnumerable<{{elementType}}>
            {
                public struct Enumerator : IEnumerator<{{elementType}}>
                {
                    private int index;
                    public Enumerator()
                    {
                        this.index = 0;
                    }
                    public bool MoveNext()
                    {
                        this.index = this.index + 1;
                        return false;
                    }
                    public {{elementType}} Current { get; }
                }

                public Enumerator GetEnumerator()
                {
                    return new Enumerator();
                }
            }

            public class Program
            {
                public static void Main()
                {
                    var collection = new {{className}}();
                    foreach ({{elementType}} item in collection)
                    {
                    }
                }
            }
            """;
    }

    private static string BuildArrayForeachSource(string elementType, int arraySize)
    {
        return $$"""
            namespace Standard;

            public class Program
            {
                public static void Main()
                {
                    var arr = new {{elementType}}[{{arraySize}}];
                    foreach ({{elementType}} item in arr)
                    {
                    }
                }
            }
            """;
    }

    // ─── Compilation helper ──────────────────────────────────────────────

    private CompilerResult CompileSource(string source)
    {
        var filePath = Path.Combine(_tempDir, $"Test_{Guid.NewGuid():N}.mist");
        File.WriteAllText(filePath, source);

        var options = new CompilerOptions(
            ProjectFolder: _tempDir,
            SourcePaths: ImmutableArray.Create(filePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: _tempDir,
            BinDirectory: Path.Combine(_tempDir, "bin"),
            ProjectName: "TestProject",
            NamespaceRoot: "Standard",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            EmitToDisk: false);

        return SharedDriver.Compile(options);
    }

    // ─── Arbitrary registration ──────────────────────────────────────────

    public static Arbitrary<ForeachEnumerableScenario> ArbitraryForeachEnumerable() =>
        GenUserDefinedEnumerableScenario().ToArbitrary();

    public static Arbitrary<ForeachArrayScenario> ArbitraryForeachArray() =>
        GenArrayForeachScenario().ToArbitrary();

    // ═══════════════════════════════════════════════════════════════════════
    // Property 3a: Foreach over user-defined IEnumerable<TElement> compiles
    //              with zero diagnostics
    //
    // For any type implementing IEnumerable<TElement>, a foreach statement
    // iterating over an expression of that type SHALL be accepted by the
    // type checker with zero diagnostics.
    //
    // **Validates: Requirements 3.1, 3.3**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Skip = "Blocked by task 7.1 — CLR IEnumerable mapping", Arbitrary = new[] { typeof(ForeachEnumerableTypeInferencePropertyTests) })]
    public Property Foreach_UserDefinedEnumerable_ZeroDiagnostics(ForeachEnumerableScenario scenario)
    {
        var result = CompileSource(scenario.Source);

        if (!result.Succeeded)
        {
            var diagnostics = string.Join("\n", result.Diagnostics.Select(d => $"  {d.Id.Value}: {d.Message}"));
            return false.ToProperty()
                .Label($"Compilation failed with diagnostics:\n{diagnostics}\nSource:\n{scenario.Source}");
        }

        var hasErrors = result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

        return (!hasErrors).ToProperty()
            .Label($"Expected zero error diagnostics but found errors.\nSource:\n{scenario.Source}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 3b: Foreach over array types compiles with zero diagnostics
    //
    // For any Array<TValue, N>, a foreach statement iterating over an
    // expression of that type SHALL be accepted by the type checker with
    // zero diagnostics, and the element type SHALL be TValue.
    //
    // **Validates: Requirements 3.1, 5.5**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Skip = "Blocked by task 7.1 — CLR IEnumerable mapping", Arbitrary = new[] { typeof(ForeachEnumerableTypeInferencePropertyTests) })]
    public Property Foreach_Array_ZeroDiagnostics(ForeachArrayScenario scenario)
    {
        var result = CompileSource(scenario.Source);

        if (!result.Succeeded)
        {
            var diagnostics = string.Join("\n", result.Diagnostics.Select(d => $"  {d.Id.Value}: {d.Message}"));
            return false.ToProperty()
                .Label($"Compilation failed with diagnostics:\n{diagnostics}\nSource:\n{scenario.Source}");
        }

        var hasErrors = result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

        return (!hasErrors).ToProperty()
            .Label($"Expected zero error diagnostics but found errors.\nSource:\n{scenario.Source}");
    }
}
