// **Validates: Requirements 10.1, 10.2, 10.3, 10.4**

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Backends.CSharpTranspiler;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Analysis;

/// <summary>
/// Integration tests for refinement types in union type contexts.
///
/// Verifies that:
/// - The semantic model creates a <see cref="UnionType"/> with <see cref="RefinementType"/>
///   constituents when a type alias defines a union with refinements on each constituent.
/// - Literal assignment to a union-of-refinements is accepted if the literal satisfies
///   any constituent's base type and refinement predicate.
/// - Literal assignment violating all constituents is rejected.
/// - Pattern matching narrows to the specific <see cref="RefinementType"/> constituent.
///
/// Uses the <c>SharedDriver</c> pattern with full compilation for tests that need
/// operator resolution and pattern matching.
///
/// **Validates: Requirements 10.1, 10.2, 10.3, 10.4**
/// </summary>
public class RefinementUnionTests
{
    // ─── Shared infrastructure ───────────────────────────────────────────

    private static readonly ILexer Lexer = new MistLexer();
    private static readonly IParser Parser = new MistParser();
    private static readonly SemanticModelBuilder Builder = new();

    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<Mist.Analysis.Contracts.IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    /// <summary>
    /// Builds the semantic model from source and returns the model and diagnostics.
    /// Does NOT run the type checker — use for semantic model structure tests only.
    /// </summary>
    private static (ISemanticModel? Model, ImmutableArray<Diagnostic> Diagnostics) BuildModel(string source)
    {
        var sourceText = new SourceText("test.mist", source);

        var lexBag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexBag);
        Assert.False(lexBag.HasErrors, "Lex should not produce errors");

        var parseBag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseBag);
        Assert.False(parseBag.HasErrors, "Parse should not produce errors");
        Assert.NotNull(parseResult.Root);

        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));
        var semBag = new DiagnosticBag();
        var model = Builder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root!),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            semBag);

        return (model, semBag.ToImmutableArray());
    }

    /// <summary>
    /// Compiles Mist source through the full pipeline (lexer → parser → semantic model
    /// → readonly inference → type checker) and returns all diagnostics.
    /// </summary>
    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"mist_union_ref_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
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
                ProjectName: "TestProgram",
                NamespaceRoot: "TestProgram",
                EnabledAnalyzerIds: ImmutableArray<string>.Empty,
                EmitToDisk: false);

            var result = SharedDriver.Compile(options);
            return result.Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static ImmutableArray<Diagnostic> WithId(
        ImmutableArray<Diagnostic> diagnostics, string id)
    {
        return diagnostics.Where(d => d.Id.Value == id).ToImmutableArray();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 10.1: Semantic model creates UnionType with RefinementType
    //                   constituents
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void UnionWithRefinements_CreatesUnionType_WithRefinementConstituents()
    {
        const string source = "type AgeRange = int >= 3 and <= 8 | byte >= 0 and < 64;";

        var (model, diagnostics) = BuildModel(source);
        Assert.NotNull(model);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var aliasSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == "AgeRange" && s.Kind == SymbolKind.TypeAlias);
        Assert.NotNull(aliasSymbol);

        var declaredType = aliasSymbol.DeclaredType;
        Assert.NotNull(declaredType);
        Assert.IsType<UnionType>(declaredType);

        var unionType = (UnionType)declaredType;
        Assert.Equal(2, unionType.Constituents.Count);

        // Both constituents should be RefinementType instances.
        Assert.All(unionType.Constituents, c => Assert.IsType<RefinementType>(c));
    }

    [Fact]
    public void UnionWithRefinements_EachConstituent_HasCorrectBaseType()
    {
        const string source = "type AgeRange = int >= 3 and <= 8 | byte >= 0 and < 64;";

        var (model, _) = BuildModel(source);
        Assert.NotNull(model);

        var aliasSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == "AgeRange" && s.Kind == SymbolKind.TypeAlias);
        Assert.NotNull(aliasSymbol);

        var unionType = aliasSymbol.DeclaredType as UnionType;
        Assert.NotNull(unionType);

        var refinementConstituents = unionType.Constituents.OfType<RefinementType>().ToList();
        Assert.Equal(2, refinementConstituents.Count);

        // Verify base types are int and byte.
        var baseTypeNames = refinementConstituents.Select(r => r.BaseType.DisplayName).OrderBy(n => n).ToList();
        Assert.Contains("byte", baseTypeNames);
        Assert.Contains("int", baseTypeNames);
    }

    [Fact]
    public void UnionWithRefinements_EachConstituent_HasCorrectPredicate()
    {
        const string source = "type AgeRange = int >= 3 and <= 8 | byte >= 0 and < 64;";

        var (model, _) = BuildModel(source);
        Assert.NotNull(model);

        var aliasSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == "AgeRange" && s.Kind == SymbolKind.TypeAlias);
        Assert.NotNull(aliasSymbol);

        var unionType = aliasSymbol.DeclaredType as UnionType;
        Assert.NotNull(unionType);

        var intConstituent = unionType.Constituents
            .OfType<RefinementType>()
            .FirstOrDefault(r => r.BaseType.DisplayName == "int");
        var byteConstituent = unionType.Constituents
            .OfType<RefinementType>()
            .FirstOrDefault(r => r.BaseType.DisplayName == "byte");

        Assert.NotNull(intConstituent);
        Assert.NotNull(byteConstituent);

        // int constituent: >= 3 and <= 8
        Assert.True(intConstituent.Predicate.Evaluate(5));
        Assert.False(intConstituent.Predicate.Evaluate(2));
        Assert.False(intConstituent.Predicate.Evaluate(9));

        // byte constituent: >= 0 and < 64
        Assert.True(byteConstituent.Predicate.Evaluate(0));
        Assert.True(byteConstituent.Predicate.Evaluate(63));
        Assert.False(byteConstituent.Predicate.Evaluate(64));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 10.2: Literal assignment to union-of-refinements accepted
    //                   if satisfies any constituent
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void LiteralSatisfyingFirstConstituent_IsAccepted()
    {
        // 5 satisfies int >= 3 and <= 8 (first constituent)
        const string source = """
            type AgeRange = int >= 3 and <= 8 | byte >= 0 and < 64;
            class Program {
                public static void Main(string[] args) {
                    AgeRange x = 5;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        // No refinement violation diagnostics expected — 5 satisfies the first constituent.
        Assert.Empty(WithId(diagnostics, "MIST_TC_0097"));
        Assert.Empty(WithId(diagnostics, "MIST_TC_0098"));
    }

    [Fact]
    public void LiteralSatisfyingSecondConstituent_IsAccepted()
    {
        // 10 satisfies byte >= 0 and < 64 (second constituent) but NOT int >= 3 and <= 8
        // Note: 10 also satisfies int >= 3 and <= 8 since 3 <= 10 is false (10 > 8).
        // Use a value that only satisfies the byte constituent: e.g. 50
        // Actually 50 satisfies both: int >= 3 and <= 8? No, 50 > 8.
        // 50 satisfies byte >= 0 and < 64? Yes.
        const string source = """
            type AgeRange = int >= 3 and <= 8 | byte >= 0 and < 64;
            class Program {
                public static void Main(string[] args) {
                    AgeRange x = 50;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        // 50 satisfies the byte constituent (>= 0 and < 64).
        // No refinement violation diagnostics expected.
        Assert.Empty(WithId(diagnostics, "MIST_TC_0097"));
        Assert.Empty(WithId(diagnostics, "MIST_TC_0098"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 10.2/10.4: Literal violating all constituents is rejected
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void LiteralViolatingAllConstituents_IsRejected()
    {
        // 200 violates both: int >= 3 and <= 8 (200 > 8) and byte >= 0 and < 64 (200 >= 64)
        const string source = """
            type AgeRange = int >= 3 and <= 8 | byte >= 0 and < 64;
            class Program {
                public static void Main(string[] args) {
                    AgeRange x = 200;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        // 200 violates all constituents — should produce a diagnostic.
        // The exact diagnostic depends on how the type checker handles union-of-refinements.
        // It may produce MIST_TC_0097 (literal violation) or a union assignability error.
        var hasRefinementViolation = WithId(diagnostics, "MIST_TC_0097").Length > 0;
        var hasUnionError = WithId(diagnostics, "MIST_TC_0070").Length > 0;
        var hasTypeMismatch = WithId(diagnostics, "MIST_TC_0001").Length > 0;

        Assert.True(
            hasRefinementViolation || hasUnionError || hasTypeMismatch,
            "Expected a diagnostic when literal violates all union constituents. " +
            $"Got diagnostics: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }

    [Fact]
    public void LiteralViolatingAllConstituents_NegativeValue_IsRejected()
    {
        // -1 violates both: int >= 3 and <= 8 (-1 < 3) and byte >= 0 and < 64 (-1 < 0)
        const string source = """
            type PositiveRange = int >= 1 and <= 100 | byte >= 10 and <= 50;
            class Program {
                public static void Main(string[] args) {
                    PositiveRange x = 0;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        // 0 violates both: int >= 1 and <= 100 (0 < 1) and byte >= 10 and <= 50 (0 < 10)
        var hasRefinementViolation = WithId(diagnostics, "MIST_TC_0097").Length > 0;
        var hasUnionError = WithId(diagnostics, "MIST_TC_0070").Length > 0;
        var hasTypeMismatch = WithId(diagnostics, "MIST_TC_0001").Length > 0;

        Assert.True(
            hasRefinementViolation || hasUnionError || hasTypeMismatch,
            "Expected a diagnostic when literal violates all union constituents. " +
            $"Got diagnostics: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 10.3: Pattern matching narrows to specific constituent
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void PatternMatch_NarrowsToSpecificConstituent()
    {
        // Pattern matching with `is` against a specific refinement type constituent
        // should narrow the variable to that constituent's type.
        const string source = """
            type SmallInt = int >= 0 and <= 10;
            type LargeInt = int >= 100 and <= 200;
            type NumberRange = SmallInt | LargeInt;
            class Program {
                public static void Main(string[] args) {
                    int x = 5;
                    if (x is SmallInt small) {
                        SmallInt y = small;
                    }
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        // After pattern matching, `small` should have type SmallInt,
        // so assigning to another SmallInt variable should produce no errors.
        Assert.Empty(WithId(diagnostics, "MIST_TC_0097"));
        Assert.Empty(WithId(diagnostics, "MIST_TC_0098"));
    }

    [Fact]
    public void PatternMatch_WithUnionVariable_NarrowsToConstituent()
    {
        // When a union-typed variable is pattern matched against a specific
        // refinement type, the match variable should have that refinement type.
        const string source = """
            type SmallInt = int >= 0 and <= 10;
            type LargeInt = int >= 100 and <= 200;
            type NumberRange = SmallInt | LargeInt;
            class Program {
                public static void Main(string[] args) {
                    NumberRange nr = 5;
                    if (nr is SmallInt s) {
                        SmallInt confirmed = s;
                    }
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        // After pattern matching `nr is SmallInt s`, `s` should have type SmallInt.
        // Assigning to SmallInt should produce no refinement errors.
        Assert.Empty(WithId(diagnostics, "MIST_TC_0097"));
        Assert.Empty(WithId(diagnostics, "MIST_TC_0098"));
    }

    [Fact]
    public void PatternMatch_SwitchExpression_WithRefinementConstituents()
    {
        // Switch expression with refinement type patterns on a union-of-refinements.
        const string source = """
            type SmallInt = int >= 0 and <= 10;
            type LargeInt = int >= 100 and <= 200;
            class Program {
                public static void Main(string[] args) {
                    int x = 150;
                    string result = x switch {
                        SmallInt s => "small",
                        LargeInt l => "large",
                        _ => "other"
                    };
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        // Switch expression with refinement type patterns should be valid.
        Assert.Empty(WithId(diagnostics, "MIST_TC_0101"));
        Assert.Empty(WithId(diagnostics, "MIST_TC_0050"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 10.4: Same compile-time enforcement rules apply to each
    //                   constituent of a union-of-refinements
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void NonConstant_AssignedToUnionOfRefinements_ProducesDiagnostic()
    {
        // Assigning a non-constant int to a union-of-refinements without pattern
        // matching should produce a diagnostic (either refinement or union-specific).
        const string source = """
            type SmallInt = int >= 0 and <= 10;
            type LargeInt = int >= 100 and <= 200;
            type NumberRange = SmallInt | LargeInt;
            class Program {
                public static void Main(string[] args) {
                    int x = 5;
                    NumberRange nr = x;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        // Non-constant assignment to a union-of-refinements should be rejected.
        // The exact diagnostic depends on how the type checker handles this case.
        var hasRefinementNonConstant = WithId(diagnostics, "MIST_TC_0098").Length > 0;
        var hasUnionError = WithId(diagnostics, "MIST_TC_0070").Length > 0;
        var hasTypeMismatch = WithId(diagnostics, "MIST_TC_0001").Length > 0;

        Assert.True(
            hasRefinementNonConstant || hasUnionError || hasTypeMismatch,
            "Expected a diagnostic when non-constant is assigned to union-of-refinements. " +
            $"Got diagnostics: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }
}
