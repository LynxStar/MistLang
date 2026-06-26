using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Semantics;

/// <summary>
/// Unit tests for refinement type semantic model registration.
///
/// **Validates: Requirements 4.1, 4.2, 4.5, 10.1**
///
/// Verifies that:
/// - Simple refinement types create a <see cref="RefinementType"/> with the correct base type.
/// - Compound refinement types store the predicate correctly.
/// - Union types with refinements create a <see cref="UnionType"/> of <see cref="RefinementType"/> constituents.
/// - Incompatible predicates (e.g., relational on string) produce <c>MIST_TC_0072</c>.
/// </summary>
public class RefinementTypeRegistrationTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();
    private readonly SemanticModelBuilder builder = new();

    /// <summary>
    /// Lexes and parses <paramref name="source"/>, runs the semantic model
    /// builder, and returns the model and diagnostics.
    /// </summary>
    private (ISemanticModel? Model, ImmutableArray<Diagnostic> Diagnostics) BuildModel(string source)
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
        var model = builder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root!),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            semBag);

        return (model, semBag.ToImmutableArray());
    }

    private static ImmutableArray<Diagnostic> WithId(
        ImmutableArray<Diagnostic> diagnostics, string id)
    {
        return diagnostics.Where(d => d.Id.Value == id).ToImmutableArray();
    }

    // ─── Simple refinement type creates RefinementType with correct base type ──

    [Fact]
    public void SimpleRefinement_CreatesRefinementType_WithCorrectBaseType()
    {
        // type AdultAge = int >= 18;
        var source = "type AdultAge = int >= 18;";

        var (model, diagnostics) = BuildModel(source);
        Assert.NotNull(model);
        Assert.Empty(WithId(diagnostics, "MIST_TC_0072"));

        var aliasSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == "AdultAge" && s.Kind == SymbolKind.TypeAlias);
        Assert.NotNull(aliasSymbol);

        var declaredType = aliasSymbol.DeclaredType;
        Assert.NotNull(declaredType);
        _ = Assert.IsType<RefinementType>(declaredType);

        var refinementType = (RefinementType)declaredType;
        Assert.Equal("AdultAge", refinementType.Name);
        Assert.Equal("int", refinementType.BaseType.DisplayName);
        Assert.NotNull(refinementType.Predicate);
        Assert.Contains(">=", refinementType.Predicate.Description);
        Assert.Contains("18", refinementType.Predicate.Description);
    }

    [Fact]
    public void SimpleRefinement_PredicateEvaluatesCorrectly()
    {
        // type AdultAge = int >= 18;
        var source = "type AdultAge = int >= 18;";

        var (model, _) = BuildModel(source);
        Assert.NotNull(model);

        var aliasSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == "AdultAge" && s.Kind == SymbolKind.TypeAlias);
        Assert.NotNull(aliasSymbol);

        var refinementType = aliasSymbol.DeclaredType as RefinementType;
        Assert.NotNull(refinementType);

        // 18 satisfies >= 18
        Assert.True(refinementType.Predicate.Evaluate(18));
        // 19 satisfies >= 18
        Assert.True(refinementType.Predicate.Evaluate(19));
        // 17 does NOT satisfy >= 18
        Assert.False(refinementType.Predicate.Evaluate(17));
        // 0 does NOT satisfy >= 18
        Assert.False(refinementType.Predicate.Evaluate(0));
    }

    // ─── Compound refinement type stores predicate correctly ─────────────────

    [Fact]
    public void CompoundRefinement_StoresPredicateCorrectly()
    {
        // type Percentage = int >= 0 and <= 100;
        var source = "type Percentage = int >= 0 and <= 100;";

        var (model, diagnostics) = BuildModel(source);
        Assert.NotNull(model);
        Assert.Empty(WithId(diagnostics, "MIST_TC_0072"));

        var aliasSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == "Percentage" && s.Kind == SymbolKind.TypeAlias);
        Assert.NotNull(aliasSymbol);

        var declaredType = aliasSymbol.DeclaredType;
        Assert.NotNull(declaredType);
        _ = Assert.IsType<RefinementType>(declaredType);

        var refinementType = (RefinementType)declaredType;
        Assert.Equal("Percentage", refinementType.Name);
        Assert.Equal("int", refinementType.BaseType.DisplayName);
        Assert.NotNull(refinementType.Predicate);

        // The description should contain both parts of the compound predicate.
        Assert.Contains(">=", refinementType.Predicate.Description);
        Assert.Contains("0", refinementType.Predicate.Description);
        Assert.Contains("<=", refinementType.Predicate.Description);
        Assert.Contains("100", refinementType.Predicate.Description);
    }

    [Fact]
    public void CompoundRefinement_PredicateEvaluatesCorrectly()
    {
        // type Percentage = int >= 0 and <= 100;
        var source = "type Percentage = int >= 0 and <= 100;";

        var (model, _) = BuildModel(source);
        Assert.NotNull(model);

        var aliasSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == "Percentage" && s.Kind == SymbolKind.TypeAlias);
        Assert.NotNull(aliasSymbol);

        var refinementType = aliasSymbol.DeclaredType as RefinementType;
        Assert.NotNull(refinementType);

        // Values within range satisfy the predicate.
        Assert.True(refinementType.Predicate.Evaluate(0));
        Assert.True(refinementType.Predicate.Evaluate(50));
        Assert.True(refinementType.Predicate.Evaluate(100));

        // Values outside range do NOT satisfy the predicate.
        Assert.False(refinementType.Predicate.Evaluate(-1));
        Assert.False(refinementType.Predicate.Evaluate(101));
    }

    // ─── Union with refinements creates UnionType of RefinementType constituents ─

    [Fact]
    public void UnionWithRefinements_CreatesUnionType_OfRefinementTypeConstituents()
    {
        // type AgeRange = int >= 3 and <= 8 | byte >= 0 and < 64;
        var source = "type AgeRange = int >= 3 and <= 8 | byte >= 0 and < 64;";

        var (model, diagnostics) = BuildModel(source);
        Assert.NotNull(model);
        Assert.Empty(WithId(diagnostics, "MIST_TC_0072"));

        var aliasSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == "AgeRange" && s.Kind == SymbolKind.TypeAlias);
        Assert.NotNull(aliasSymbol);

        var declaredType = aliasSymbol.DeclaredType;
        Assert.NotNull(declaredType);
        _ = Assert.IsType<UnionType>(declaredType);

        var unionType = (UnionType)declaredType;
        Assert.Equal(2, unionType.Constituents.Count);

        // Both constituents should be RefinementType instances.
        Assert.All(unionType.Constituents, c => Assert.IsType<RefinementType>(c));

        // Find the int-based and byte-based constituents.
        var intConstituent = unionType.Constituents
            .OfType<RefinementType>()
            .FirstOrDefault(r => r.BaseType.DisplayName == "int");
        var byteConstituent = unionType.Constituents
            .OfType<RefinementType>()
            .FirstOrDefault(r => r.BaseType.DisplayName == "byte");

        Assert.NotNull(intConstituent);
        Assert.NotNull(byteConstituent);

        // Verify the int constituent's predicate (>= 3 and <= 8).
        Assert.Contains(">=", intConstituent.Predicate.Description);
        Assert.Contains("3", intConstituent.Predicate.Description);
        Assert.Contains("<=", intConstituent.Predicate.Description);
        Assert.Contains("8", intConstituent.Predicate.Description);

        // Verify the byte constituent's predicate (>= 0 and < 64).
        Assert.Contains(">=", byteConstituent.Predicate.Description);
        Assert.Contains("0", byteConstituent.Predicate.Description);
        Assert.Contains("<", byteConstituent.Predicate.Description);
        Assert.Contains("64", byteConstituent.Predicate.Description);
    }

    // ─── Incompatible predicate produces MIST_TC_0072 ────────────────────────

    [Fact]
    public void IncompatiblePredicate_OnString_ProducesDiagnostic()
    {
        // type Bad = string >= 18;
        // Relational patterns require a comparable type; string is not comparable.
        var source = "type Bad = string >= 18;";

        var (model, diagnostics) = BuildModel(source);
        Assert.NotNull(model);

        var incompatibleDiags = WithId(diagnostics, "MIST_TC_0072");
        _ = Assert.Single(incompatibleDiags);

        var d = incompatibleDiags[0];
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
        Assert.Contains("string", d.Message);
        Assert.Contains("Relational patterns require a comparable type", d.Message);
    }

    [Fact]
    public void IncompatiblePredicate_OnBool_ProducesDiagnostic()
    {
        // type Bad = bool >= 1;
        // Relational patterns require a comparable type; bool is not comparable.
        var source = "type Bad = bool >= 1;";

        var (model, diagnostics) = BuildModel(source);
        Assert.NotNull(model);

        var incompatibleDiags = WithId(diagnostics, "MIST_TC_0072");
        _ = Assert.Single(incompatibleDiags);

        var d = incompatibleDiags[0];
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
        Assert.Contains("bool", d.Message);
    }

    [Fact]
    public void CompatiblePredicate_OnInt_ProducesNoDiagnostic()
    {
        // type Good = int >= 0;
        // int is a comparable type — no diagnostic expected.
        var source = "type Good = int >= 0;";

        var (_, diagnostics) = BuildModel(source);
        Assert.Empty(WithId(diagnostics, "MIST_TC_0072"));
    }

    [Fact]
    public void CompatiblePredicate_OnByte_ProducesNoDiagnostic()
    {
        // type Good = byte >= 0;
        // byte is a comparable type — no diagnostic expected.
        var source = "type Good = byte >= 0;";

        var (_, diagnostics) = BuildModel(source);
        Assert.Empty(WithId(diagnostics, "MIST_TC_0072"));
    }
}