// Feature: iterators-enumerables, Property 5: Explicit element type annotation compatibility
// **Validates: Requirements 3.4, 3.5**

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
/// Scenario for verifying that foreach with a compatible explicit type annotation
/// compiles with zero diagnostics.
/// </summary>
public sealed class CompatibleAnnotationScenario
{
    public string Source { get; }
    public string ElementType { get; }
    public string DeclaredType { get; }
    public string Description { get; }

    public CompatibleAnnotationScenario(string source, string elementType, string declaredType, string description)
    {
        Source = source;
        ElementType = elementType;
        DeclaredType = declaredType;
        Description = description;
    }

    public override string ToString() => Description;
}

/// <summary>
/// Scenario for verifying that foreach with an incompatible explicit type annotation
/// produces the <c>MIST_TC_FOREACH_TYPE_MISMATCH</c> diagnostic.
/// </summary>
public sealed class IncompatibleAnnotationScenario
{
    public string Source { get; }
    public string ElementType { get; }
    public string DeclaredType { get; }
    public string Description { get; }

    public IncompatibleAnnotationScenario(string source, string elementType, string declaredType, string description)
    {
        Source = source;
        ElementType = elementType;
        DeclaredType = declaredType;
        Description = description;
    }

    public override string ToString() => Description;
}

// ─── Property Tests ─────────────────────────────────────────────────────────

/// <summary>
/// Property-based tests for type checker foreach explicit type annotation validation.
///
/// Property 5: Explicit element type annotation compatibility
///
/// For any foreach statement with an explicit element type annotation, if the declared
/// type is assignable from the resolved TElement, the type checker SHALL accept it
/// without diagnostics. If the declared type is NOT assignable from TElement, the type
/// checker SHALL emit diagnostic <c>MIST_TC_FOREACH_TYPE_MISMATCH</c>.
///
/// Uses in-process compilation via a shared CompilerDriver instance.
///
/// **Validates: Requirements 3.4, 3.5**
/// </summary>
public sealed class ForeachExplicitTypeAnnotationPropertyTests : IDisposable
{
    private readonly string _tempDir;

    public ForeachExplicitTypeAnnotationPropertyTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "MistForeachTypePBT_" + Guid.NewGuid().ToString("N"));
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

    // ─── Type pools ─────────────────────────────────────────────────────

    /// <summary>
    /// Types that can be used as TElement in IEnumerable&lt;TElement&gt; and as
    /// explicit annotations. These types are NOT assignable to each other.
    /// </summary>
    private static readonly string[] DisjointTypes = ["int", "string", "bool", "double"];

    private static readonly string[] ClassNames =
        ["TypedCollection", "DataSource", "ItemStream", "ValueSet", "TypedList", "ElementBag"];

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates a scenario where the explicit type annotation matches the resolved TElement.
    /// The declared type equals TElement, so it is always assignable.
    /// </summary>
    private static Gen<CompatibleAnnotationScenario> GenCompatibleAnnotationScenario()
    {
        return from elementType in Gen.Elements(DisjointTypes)
               from className in Gen.Elements(ClassNames)
               let source = BuildEnumerableWithForeach(className, elementType, elementType)
               select new CompatibleAnnotationScenario(
                   source, elementType, elementType,
                   $"foreach ({elementType} x in {className}<{elementType}>) - compatible");
    }

    /// <summary>
    /// Generates a scenario where the explicit type annotation does NOT match the resolved TElement.
    /// Picks two distinct types from the disjoint pool so they are never assignable.
    /// </summary>
    private static Gen<IncompatibleAnnotationScenario> GenIncompatibleAnnotationScenario()
    {
        return from elementType in Gen.Elements(DisjointTypes)
               from declaredType in Gen.Elements(DisjointTypes)
               where elementType != declaredType
               from className in Gen.Elements(ClassNames)
               let source = BuildEnumerableWithForeach(className, elementType, declaredType)
               select new IncompatibleAnnotationScenario(
                   source, elementType, declaredType,
                   $"foreach ({declaredType} x in {className}<{elementType}>) - incompatible");
    }

    // ─── Source builders ─────────────────────────────────────────────────

    private static string BuildEnumerableWithForeach(string className, string elementType, string declaredType)
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
                    foreach ({{declaredType}} item in collection)
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

    public static Arbitrary<CompatibleAnnotationScenario> ArbitraryCompatibleAnnotation() =>
        GenCompatibleAnnotationScenario().ToArbitrary();

    public static Arbitrary<IncompatibleAnnotationScenario> ArbitraryIncompatibleAnnotation() =>
        GenIncompatibleAnnotationScenario().ToArbitrary();

    // ═══════════════════════════════════════════════════════════════════════
    // Property 5a: Compatible explicit type annotations produce zero diagnostics
    //
    // For any foreach statement with an explicit element type annotation where
    // the declared type is assignable from the resolved TElement, the type
    // checker SHALL accept it without diagnostics.
    //
    // **Validates: Requirements 3.4**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ForeachExplicitTypeAnnotationPropertyTests) })]
    public Property CompatibleAnnotation_ZeroDiagnostics(CompatibleAnnotationScenario scenario)
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
            .Label($"Expected zero error diagnostics for compatible annotation " +
                   $"(declared={scenario.DeclaredType}, element={scenario.ElementType}) " +
                   $"but found errors.\nSource:\n{scenario.Source}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 5b: Incompatible explicit type annotations produce
    //              MIST_TC_FOREACH_TYPE_MISMATCH diagnostic
    //
    // For any foreach statement with an explicit element type annotation where
    // the declared type is NOT assignable from the resolved TElement, the type
    // checker SHALL emit diagnostic MIST_TC_FOREACH_TYPE_MISMATCH.
    //
    // **Validates: Requirements 3.5**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ForeachExplicitTypeAnnotationPropertyTests) })]
    public Property IncompatibleAnnotation_ProducesTypeMismatchDiagnostic(IncompatibleAnnotationScenario scenario)
    {
        var result = CompileSource(scenario.Source);

        var hasTypeMismatchDiagnostic = result.Diagnostics.Any(d =>
            d.Id.Value == "MIST_TC_FOREACH_TYPE_MISMATCH");

        return hasTypeMismatchDiagnostic.ToProperty()
            .Label($"Expected diagnostic MIST_TC_FOREACH_TYPE_MISMATCH for incompatible annotation " +
                   $"(declared={scenario.DeclaredType}, element={scenario.ElementType}) " +
                   $"but it was not found.\n" +
                   $"Diagnostics: [{string.Join(", ", result.Diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]\n" +
                   $"Source:\n{scenario.Source}");
    }
}
