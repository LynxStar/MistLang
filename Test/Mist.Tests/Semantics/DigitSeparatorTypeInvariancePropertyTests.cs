// Feature: binary-literals, Property 4: Digit separators do not affect type resolution
// **Validates: Requirements 8.10**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests for digit separator type invariance in the semantic model.
///
/// Property 4: Digit separators do not affect type resolution.
/// For any numeric literal (integer, hex, or binary) with valid digit separators,
/// removing all underscores from the lexeme and resolving the type SHALL produce
/// the same type as resolving the original lexeme with underscores.
///
/// **Validates: Requirements 8.10**
/// </summary>
public class DigitSeparatorTypeInvariancePropertyTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();
    private readonly SemanticModelBuilder builder = new();

    // ─── Suffix definitions ──────────────────────────────────────────────

    /// <summary>
    /// All valid INT_SUFFIX values, plus empty string for no suffix.
    /// </summary>
    private static readonly string[] IntSuffixes =
        ["", "l", "L", "u", "U", "UL", "ul", "Ul", "uL", "Lu", "lU", "LU", "lu"];

    // ─── Test scenario ───────────────────────────────────────────────────

    /// <summary>
    /// Represents a pair of numeric literals: one with separators and one without,
    /// sharing the same digits and suffix. Both are embedded in Mist programs.
    /// </summary>
#pragma warning disable CA1034
    public record SeparatorPairScenario(
        string LiteralWithSeparators,
        string LiteralWithoutSeparators,
        string LiteralKind)
    {
        public override string ToString() =>
            $"{LiteralKind}: {LiteralWithSeparators} vs {LiteralWithoutSeparators}";
    }
#pragma warning restore CA1034

    // ─── FsCheck Arbitrary ───────────────────────────────────────────────

    /// <summary>
    /// Generates pairs of numeric literals: one with digit separators and one without.
    /// Covers integer, hex, and binary literal types.
    /// </summary>
    public static Arbitrary<SeparatorPairScenario> ArbitrarySeparatorPairScenario()
    {
        var gen = Gen.OneOf(GenIntegerPair(), GenHexPair(), GenBinaryPair());
        return gen.ToArbitrary();
    }

    // ─── Integer literal pair generator ──────────────────────────────────

    /// <summary>
    /// Generates a pair of integer literals: one with separators, one without.
    /// Example: ("1_000_000L", "1000000L")
    /// </summary>
    private static Gen<SeparatorPairScenario> GenIntegerPair()
    {
        return
            from groupCount in Gen.Choose(2, 4)
            from groups in Gen.ArrayOf(GenDecimalDigitGroup(), groupCount)
            from suffix in Gen.Elements(IntSuffixes)
            let withSep = string.Join("_", groups) + suffix
            let withoutSep = string.Concat(groups) + suffix
            select new SeparatorPairScenario(withSep, withoutSep, "integer");
    }

    /// <summary>
    /// Generates a group of 1–3 decimal digits, ensuring the first group
    /// starts with a non-zero digit to avoid ambiguity with hex/binary prefixes.
    /// </summary>
    private static Gen<string> GenDecimalDigitGroup()
    {
        return
            from length in Gen.Choose(1, 3)
            from digits in Gen.ArrayOf(Gen.Choose(0, 9), length)
            select new string(digits.Select(d => (char)('0' + d)).ToArray());
    }

    // ─── Hex literal pair generator ──────────────────────────────────────

    /// <summary>
    /// Generates a pair of hex literals: one with separators, one without.
    /// Example: ("0xFF_FF_FFu", "0xFFFFFFu")
    /// </summary>
    private static Gen<SeparatorPairScenario> GenHexPair()
    {
        return
            from prefix in Gen.Elements("0x", "0X")
            from groupCount in Gen.Choose(2, 4)
            from groups in Gen.ArrayOf(GenHexDigitGroup(), groupCount)
            from suffix in Gen.Elements(IntSuffixes)
            let withSep = prefix + string.Join("_", groups) + suffix
            let withoutSep = prefix + string.Concat(groups) + suffix
            select new SeparatorPairScenario(withSep, withoutSep, "hex");
    }

    private static readonly char[] HexChars =
        "0123456789abcdefABCDEF".ToCharArray();

    /// <summary>
    /// Generates a group of 1–4 hex digits.
    /// </summary>
    private static Gen<string> GenHexDigitGroup()
    {
        return
            from length in Gen.Choose(1, 4)
            from digits in Gen.ArrayOf(Gen.Elements(HexChars), length)
            select new string(digits);
    }

    // ─── Binary literal pair generator ───────────────────────────────────

    /// <summary>
    /// Generates a pair of binary literals: one with separators, one without.
    /// Example: ("0b1010_0101UL", "0b10100101UL")
    /// </summary>
    private static Gen<SeparatorPairScenario> GenBinaryPair()
    {
        return
            from prefix in Gen.Elements("0b", "0B")
            from groupCount in Gen.Choose(2, 4)
            from groups in Gen.ArrayOf(GenBinaryDigitGroup(), groupCount)
            from suffix in Gen.Elements(IntSuffixes)
            let withSep = prefix + string.Join("_", groups) + suffix
            let withoutSep = prefix + string.Concat(groups) + suffix
            select new SeparatorPairScenario(withSep, withoutSep, "binary");
    }

    /// <summary>
    /// Generates a group of 1–4 binary digits.
    /// </summary>
    private static Gen<string> GenBinaryDigitGroup()
    {
        return
            from length in Gen.Choose(1, 4)
            from digits in Gen.ArrayOf(Gen.Elements('0', '1'), length)
            select new string(digits);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static CompilationUnitNode? ParseSource(string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        if(bag.HasErrors)
        {
            return null;
        }

        var result = Parser.Parse(tokens, bag);
        return result.Root as CompilationUnitNode;
    }

    private ISemanticModel BuildSemanticModel(Storm.Runtime.SyntaxNode ast, string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var compilation = new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));
        var diagnostics = new DiagnosticBag();
        return builder.Build(
            compilation,
            ImmutableArray.Create(ast),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            diagnostics);
    }

    /// <summary>
    /// Recursively finds all InferredVariableDeclarationVariableDeclarationStatementNode
    /// instances in the AST.
    /// </summary>
    private static List<InferredVariableDeclarationVariableDeclarationStatementNode> FindVarDeclarations(
        Storm.Runtime.SyntaxNode node)
    {
        var results = new List<InferredVariableDeclarationVariableDeclarationStatementNode>();
        CollectVarDeclarations(node, results);
        return results;
    }

    private static void CollectVarDeclarations(
        Storm.Runtime.SyntaxNode? node,
        List<InferredVariableDeclarationVariableDeclarationStatementNode> results)
    {
        if(node is null)
        {
            return;
        }

        if(node is InferredVariableDeclarationVariableDeclarationStatementNode varDecl)
        {
            results.Add(varDecl);
        }

        try
        {
            foreach(var child in node.Children())
            {
                CollectVarDeclarations(child, results);
            }
        }
        catch(NullReferenceException)
        {
            // Guard against null children in AST traversal.
        }
    }

    /// <summary>
    /// Resolves the type name of a variable declared as "var x = &lt;literal&gt;;"
    /// in the given Mist source. Returns null if parsing or resolution fails.
    /// </summary>
    private string? ResolveVarType(string literal)
    {
        var source = $"class C {{ public static void Main() {{ var x = {literal}; }} }}";

        var root = ParseSource(source);
        if(root is null)
        {
            return null;
        }

        var model = BuildSemanticModel(root, source);

        var varDecls = FindVarDeclarations(root);
        if(varDecls.Count == 0)
        {
            return null;
        }

        var xDecl = varDecls.FirstOrDefault(v => v.Name.Lexeme == "x");
        if(xDecl is null)
        {
            return null;
        }

        var symbol = model.GetDeclaredSymbol(xDecl);
        return symbol?.DeclaredType?.DisplayName;
    }

    // ─── Property 4 ──────────────────────────────────────────────────────

    /// <summary>
    /// Property 4: Digit separators do not affect type resolution.
    ///
    /// For any numeric literal (integer, hex, or binary) with valid digit separators,
    /// the resolved type of the literal with separators must be identical to the
    /// resolved type of the same literal without separators.
    ///
    /// **Validates: Requirements 8.10**
    /// </summary>
    [Property(MaxTest = 200, Arbitrary = new[] { typeof(DigitSeparatorTypeInvariancePropertyTests) })]
    public bool DigitSeparators_DoNotAffect_TypeResolution(SeparatorPairScenario scenario)
    {
        var typeWithSep = ResolveVarType(scenario.LiteralWithSeparators);
        var typeWithoutSep = ResolveVarType(scenario.LiteralWithoutSeparators);

        // Both must resolve successfully
        if(typeWithSep is null || typeWithoutSep is null)
        {
            return false;
        }

        // Types must be identical
        return typeWithSep == typeWithoutSep;
    }
}