// Feature: stack-only-types, Property 1: Alias resolution
//
// **Validates: Requirements 1.2, 2.2**
//
// For any element type T and kind K (Span or ReadOnlySpan), the arity-2 reference
// K<T, unknown> (= the form the arity-1 alias `type K<T> = K<T, unknown>` expands to)
// resolves to a SpanType with:
//   - ElementType matching T
//   - IsReadOnly == (K == ReadOnlySpan)
//   - KnownLength == null (unknown)
//
// All work runs in-process via SemanticModelBuilder — no external process spawns.
// Uses a CapturingSemanticModelBuilder injected into CompilerDriver to inspect
// the resolved type on local variable symbols after compilation.

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
using Mist.Semantics;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests verifying that <c>Span&lt;T, unknown&gt;</c> and
/// <c>ReadOnlySpan&lt;T, unknown&gt;</c> type references resolve to
/// <see cref="SpanType"/> with the correct element type, IsReadOnly flag,
/// and unknown length via the arity-2 reference routing in SemanticModelBuilder.
///
/// This is the routing that the arity-1 aliases (<c>type Span&lt;T&gt; = Span&lt;T, unknown&gt;</c>)
/// expand to. The property verifies the resolution that underpins the alias mechanism.
///
/// **Validates: Requirements 1.2, 2.2**
/// </summary>
public sealed class SpanAliasResolutionPropertyTests : IDisposable
{
    private readonly string scratchDir;

    public SpanAliasResolutionPropertyTests()
    {
        scratchDir = Path.Combine(
            Path.GetTempPath(),
            "mist_pbt_span_alias_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratchDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(scratchDir))
        {
            try { Directory.Delete(scratchDir, recursive: true); } catch { /* best effort */ }
        }
        GC.SuppressFinalize(this);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // CapturingSemanticModelBuilder — captures the model for type inspection
    // ═══════════════════════════════════════════════════════════════════════

    private sealed class CapturingSemanticModelBuilder : ISemanticModelBuilder
    {
        private readonly SemanticModelBuilder inner = new();
        public ISemanticModel? Captured { get; private set; }

        public ISemanticModel Build(
            MistCompilation compilation,
            ImmutableArray<Storm.Runtime.SyntaxNode> asts,
            IAmbientTypeResolver? ambientTypeResolver,
            string namespaceRoot,
            string projectFolder,
            DiagnosticBag diagnostics,
            ImmutableHashSet<string>? directReferencePaths = null)
        {
            var model = inner.Build(compilation, asts, ambientTypeResolver, namespaceRoot, projectFolder, diagnostics, directReferencePaths);
            Captured = model;
            return model;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Scenario model
    // ═══════════════════════════════════════════════════════════════════════

#pragma warning disable CA1034 // nested type is the FsCheck-friendly way to surface a scenario record

    /// <summary>
    /// Describes a single alias resolution scenario: a stampable element type T
    /// and a span kind (mutable Span or read-only ReadOnlySpan).
    /// </summary>
    public sealed record AliasResolutionScenario(
        string ElementTypeName,
        bool IsReadOnly)
    {
        /// <summary>The span keyword used in the Mist source: "Span" or "ReadOnlySpan".</summary>
        public string KindName => IsReadOnly ? "ReadOnlySpan" : "Span";

        public override string ToString() => $"{KindName}<{ElementTypeName}, unknown>";
    }

#pragma warning restore CA1034

    // ═══════════════════════════════════════════════════════════════════════
    // FsCheck generator
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Stampable primitive type names that can be used as span element types.
    /// These are Mist's primitive keywords (same as for array creation / stackalloc).
    /// </summary>
    private static readonly string[] StampableElements =
    [
        "int", "uint", "long", "ulong", "short", "ushort",
        "byte", "sbyte", "float", "double", "decimal", "bool", "char",
    ];

    /// <summary>
    /// Maps Mist primitive keywords to the expected resolved DisplayName.
    /// When stdlib is loaded, the keyword resolves to the Standard.* struct type
    /// whose DisplayName is the Pascal-case CLR name.
    /// </summary>
    private static readonly Dictionary<string, string> KeywordToDisplayName = new()
    {
        ["int"] = "Int32",
        ["uint"] = "UInt32",
        ["long"] = "Int64",
        ["ulong"] = "UInt64",
        ["short"] = "Int16",
        ["ushort"] = "UInt16",
        ["byte"] = "Byte",
        ["sbyte"] = "SByte",
        ["float"] = "Single",
        ["double"] = "Double",
        ["decimal"] = "Decimal",
        ["bool"] = "Boolean",
        ["char"] = "Char",
    };

    public static Arbitrary<AliasResolutionScenario> ArbitraryAliasResolutionScenario()
    {
        var gen = from element in Gen.Elements(StampableElements)
                  from isReadOnly in Gen.Elements(false, true)
                  select new AliasResolutionScenario(element, isReadOnly);
        return gen.ToArbitrary();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a Mist source program that declares a local variable with the
    /// arity-2 type reference <c>K&lt;T, unknown&gt;</c> initialized via stackalloc.
    /// This is the form the alias RHS expands to, and exercises the arity-2 reference
    /// routing in SemanticModelBuilder that creates the SpanType.
    /// </summary>
    private static string BuildSource(AliasResolutionScenario scenario)
    {
        return $$"""
            public class Program
            {
                public static void Main()
                {
                    {{scenario.KindName}}<{{scenario.ElementTypeName}}, unknown> x = stackalloc {{scenario.ElementTypeName}}[1];
                }
            }
            """;
    }

    /// <summary>
    /// Compiles the source in-process via CompilerDriver with a CapturingSemanticModelBuilder,
    /// returning both the compiler result and the captured semantic model.
    /// </summary>
    private (CompilerResult Result, ISemanticModel? Model) CompileAndCapture(string source)
    {
        var iterDir = Path.Combine(scratchDir, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(iterDir);

        var filePath = Path.Combine(iterDir, "Program.mist");
        File.WriteAllText(filePath, source);

        var capture = new CapturingSemanticModelBuilder();
        var driver = new CompilerDriver(
            new MistLexer(),
            new MistParser(),
            capture,
            ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
            ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

        var options = new CompilerOptions(
            ProjectFolder: iterDir,
            SourcePaths: ImmutableArray.Create(filePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: iterDir,
            BinDirectory: Path.Combine(iterDir, "bin"),
            ProjectName: "TestProject",
            NamespaceRoot: "TestProject",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            EmitToDisk: false,
            GlobalUsings: ImmutableArray.Create("Standard"));

        var result = driver.Compile(options);
        return (result, capture.Captured);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 1: Alias resolution
    //
    // **Validates: Requirements 1.2, 2.2**
    //
    // For any stampable element type T and kind K ∈ {Span, ReadOnlySpan},
    // the arity-2 reference K<T, unknown> (the form the alias expands to)
    // resolves to a SpanType with:
    //   - Element type whose DisplayName matches T
    //   - IsReadOnly == (K == ReadOnlySpan)
    //   - KnownLength == null (unknown length)
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(SpanAliasResolutionPropertyTests) })]
    public Property P1_AliasResolution_ResolvesToSpanTypeWithCorrectProperties(
        AliasResolutionScenario scenario)
    {
        var source = BuildSource(scenario);
        var (result, model) = CompileAndCapture(source);

        // The compilation should not have errors that prevent semantic model construction.
        if(model is null)
        {
            var diagSummary = string.Join("\n  ",
                result.Diagnostics.Select(d => $"[{d.Id.Value}] {d.Message}"));
            return false.Label(
                $"Semantic model was not captured for {scenario}.\nDiagnostics:\n  {diagSummary}");
        }

        // Find the local variable "x" in the semantic model.
        var localX = model.AllSymbols
            .OfType<Symbol>()
            .FirstOrDefault(s => s.Name == "x" && s.Kind == SymbolKind.LocalVariable);

        if(localX is null)
        {
            var diagSummary = string.Join("\n  ",
                result.Diagnostics.Select(d => $"[{d.Id.Value}] {d.Message}"));
            return false.Label(
                $"Local variable 'x' not found in model for {scenario}.\nDiagnostics:\n  {diagSummary}");
        }

        // Assert the declared type is a SpanType.
        if(localX.DeclaredType is not SpanType spanType)
        {
            return false.Label(
                $"Expected DeclaredType to be SpanType for {scenario}, " +
                $"but got {localX.DeclaredType?.GetType().Name ?? "null"} " +
                $"(DisplayName: {localX.DeclaredType?.DisplayName ?? "<null>"})");
        }

        // Assert IsReadOnly matches the scenario's kind.
        if(spanType.IsReadOnly != scenario.IsReadOnly)
        {
            return false.Label(
                $"Expected IsReadOnly == {scenario.IsReadOnly} for {scenario}, " +
                $"but got {spanType.IsReadOnly}");
        }

        // Assert unknown length (the alias form always produces unknown length).
        if(spanType.KnownLength is not null)
        {
            return false.Label(
                $"Expected KnownLength == null (unknown) for {scenario}, " +
                $"but got KnownLength == {spanType.KnownLength}");
        }

        // Assert the element type DisplayName matches the resolved form of the element keyword.
        // Mist primitive keywords resolve to stdlib types: "short" → "Int16", "int" → "Int32", etc.
        var elementDisplayName = spanType.ElementType.DisplayName;
        var expectedDisplayName = KeywordToDisplayName[scenario.ElementTypeName];
        if(elementDisplayName != expectedDisplayName)
        {
            return false.Label(
                $"Expected element type '{expectedDisplayName}' (from keyword '{scenario.ElementTypeName}') for {scenario}, " +
                $"but got '{elementDisplayName}'");
        }

        return true.Label($"OK: {scenario} → SpanType(element={elementDisplayName}, " +
                          $"isReadOnly={spanType.IsReadOnly}, length=unknown)");
    }
}