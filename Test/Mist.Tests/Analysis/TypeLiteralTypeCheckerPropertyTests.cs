// Feature: type-literals, Property 7: Type mismatch when assigning type literal to non-Type variable

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests for type checker validation of type literal assignments.
///
/// Property 7: Type mismatch when assigning type literal to non-Type variable.
/// For any primitive type keyword T and any non-Type declared type D,
/// the declaration `D x = T;` SHALL produce a type mismatch diagnostic (MIST_TC_0001).
///
/// **Validates: Requirements 4.4**
/// </summary>
public class TypeLiteralTypeCheckerPropertyTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();
    private readonly SemanticModelBuilder _builder = new();
    private readonly TypeCheckerAnalyzer _analyzer = new();

    // ─── Primitive type keywords (used as type literals on the RHS) ──────

    private static readonly string[] TypeLiteralKeywords =
    [
        "int", "uint", "long", "ulong", "short", "ushort",
        "byte", "sbyte", "float", "double", "decimal",
        "bool", "char", "string"
    ];

    // ─── Non-Type declared types (used as the variable's declared type) ──

    private static readonly string[] NonTypeDeclarations =
    [
        "int", "uint", "long", "ulong", "short", "ushort",
        "byte", "sbyte", "float", "double", "decimal",
        "bool", "char", "string"
    ];

    // ─── Test input model ────────────────────────────────────────────────

    /// <summary>
    /// Represents a test case: a typed variable declaration where the declared
    /// type D is a non-Type primitive and the initializer is a type literal T.
    /// E.g., `int x = bool;` should produce MIST_TC_0001.
    /// </summary>
#pragma warning disable CA1034
    public record TypeMismatchTestCase(string DeclaredType, string TypeLiteral, string Source)
    {
        public override string ToString() => $"{DeclaredType} x = {TypeLiteral};";
    }
#pragma warning restore CA1034

    // ─── FsCheck Arbitrary ───────────────────────────────────────────────

    public static Arbitrary<TypeMismatchTestCase> ArbitraryTypeMismatchTestCase()
    {
        var gen =
            from declaredType in Gen.Elements(NonTypeDeclarations)
            from typeLiteral in Gen.Elements(TypeLiteralKeywords)
            let source = $"class Program {{ public static void Main() {{ {declaredType} x = {typeLiteral}; }} }}"
            select new TypeMismatchTestCase(declaredType, typeLiteral, source);

        return gen.ToArbitrary();
    }

    // ─── Pipeline helpers ────────────────────────────────────────────────

    private static CompilationUnitNode? ParseSource(string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var result = Parser.Parse(tokens, bag);
        return result.Root as CompilationUnitNode;
    }

    private (ImmutableArray<Diagnostic> SemanticDiagnostics, Mist.Semantics.Contracts.ISemanticModel? Model)
        BuildSemanticModel(Storm.Runtime.SyntaxNode ast, string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var compilation = new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));
        var diagnostics = new DiagnosticBag();
        var model = _builder.Build(compilation, ImmutableArray.Create(ast), ambientTypeResolver: null, diagnostics);
        return (diagnostics.ToImmutableArray(), model);
    }

    private ImmutableArray<Diagnostic> RunTypeChecker(Mist.Semantics.Contracts.ISemanticModel model)
    {
        var bag = new DiagnosticBag();
        _analyzer.Analyze(model, bag);
        return bag.ToImmutableArray();
    }

    // ═════════════════════════════════════════════════════════════════════
    // Property 7: Type mismatch when assigning type literal to non-Type
    //             variable
    //
    // **Validates: Requirements 4.4**
    //
    // For any primitive type keyword T and any non-Type declared type D,
    // the declaration `D x = T;` SHALL produce MIST_TC_0001 because the
    // RHS is a TypeType value being assigned to a non-Type variable.
    // ═════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(TypeLiteralTypeCheckerPropertyTests) })]
    public bool TypeLiteralAssignedToNonTypeVariable_ProducesTypeMismatch(
        TypeMismatchTestCase testCase)
    {
        // 1. Parse the source
        var root = ParseSource(testCase.Source);
        if (root is null) return false;

        // 2. Build semantic model
        var (_, model) = BuildSemanticModel(root, testCase.Source);
        if (model is null) return false;

        // 3. Run type checker
        var diagnostics = RunTypeChecker(model);

        // 4. Verify MIST_TC_0001 is emitted (type mismatch: Type → D)
        return diagnostics.Any(d => d.Id.Value == "MIST_TC_0001");
    }
}
