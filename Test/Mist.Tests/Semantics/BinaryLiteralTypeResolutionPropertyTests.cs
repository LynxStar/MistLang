// Feature: binary-literals, Property 3: Binary literal type resolution follows suffix rules
// **Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5**

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
/// Property-based tests for binary literal type resolution in the semantic model.
///
/// Property 3: Binary literal type resolution follows suffix rules.
/// For any binary literal with any valid suffix (none, L, l, U, u, UL, ul, Lu, lU, LU, lu),
/// the semantic model SHALL resolve the same type as for a decimal integer literal with the
/// same suffix: no suffix → int, L/l → long, U/u → uint, UL-variants → ulong.
///
/// **Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5**
/// </summary>
public class BinaryLiteralTypeResolutionPropertyTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();
    private readonly SemanticModelBuilder builder = new();

    // ─── Suffix definitions ──────────────────────────────────────────────

    /// <summary>
    /// All valid INT_SUFFIX values, plus empty string for no suffix.
    /// </summary>
    private static readonly string[] Suffixes =
        ["", "l", "L", "u", "U", "UL", "ul", "Ul", "uL", "Lu", "lU", "LU", "lu"];

    // ─── Test scenario ───────────────────────────────────────────────────

    /// <summary>
    /// Represents a test scenario: a binary literal with a specific suffix,
    /// embedded in a complete Mist program as a var initializer.
    /// </summary>
#pragma warning disable CA1034
    public record BinaryLiteralTypeScenario(string Literal, string Suffix, string ExpectedTypeName)
    {
        public override string ToString()
        {
            return $"var x = {Literal}; → {ExpectedTypeName}";
        }
    }
#pragma warning restore CA1034

    // ─── FsCheck Arbitrary ───────────────────────────────────────────────

    /// <summary>
    /// Generates a random valid binary literal with a random suffix, paired with
    /// the expected type name based on the suffix rules.
    /// </summary>
    public static Arbitrary<BinaryLiteralTypeScenario> ArbitraryBinaryLiteralTypeScenario()
    {
        var gen =
            from prefix in Gen.Elements("0b", "0B")
            from firstGroup in GenBinaryDigitGroup()
            from separatorGroups in GenSeparatorGroups()
            from suffix in Gen.Elements(Suffixes)
            let literal = prefix + firstGroup + separatorGroups + suffix
            let expectedType = ExpectedTypeForSuffix(suffix)
            select new BinaryLiteralTypeScenario(literal, suffix, expectedType);

        return gen.ToArbitrary();
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

    /// <summary>
    /// Generates zero or more "_" + digit-group segments.
    /// </summary>
    private static Gen<string> GenSeparatorGroups()
    {
        return
            from count in Gen.Choose(0, 3)
            from groups in Gen.ArrayOf(GenBinaryDigitGroup(), count)
            select string.Concat(groups.Select(g => "_" + g));
    }

    /// <summary>
    /// Returns the expected type display name for a given suffix.
    /// </summary>
    private static string ExpectedTypeForSuffix(string suffix)
    {
        var upper = suffix.ToUpperInvariant();
        if(upper is "UL" or "LU")
        {
            return "ulong";
        }

        if(upper == "L")
        {
            return "long";
        }

        if(upper == "U")
        {
            return "uint";
        }

        return "int";
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

    // ─── Property 3 ──────────────────────────────────────────────────────

    /// <summary>
    /// Property 3: Binary literal type resolution follows suffix rules.
    ///
    /// For any binary literal with any valid suffix, the semantic model resolves
    /// the variable type to the expected primitive type:
    ///   no suffix → int, L/l → long, U/u → uint, UL-variants → ulong.
    ///
    /// **Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5**
    /// </summary>
    [Property(MaxTest = 200, Arbitrary = new[] { typeof(BinaryLiteralTypeResolutionPropertyTests) })]
    public bool BinaryLiteral_TypeResolution_FollowsSuffixRules(BinaryLiteralTypeScenario scenario)
    {
        var source = $"class C {{ public static void Main() {{ var x = {scenario.Literal}; }} }}";

        var root = ParseSource(source);
        if(root is null)
        {
            return false;
        }

        var model = BuildSemanticModel(root, source);

        // Find the "var x = <literal>;" declaration
        var varDecls = FindVarDeclarations(root);
        if(varDecls.Count == 0)
        {
            return false;
        }

        var xDecl = varDecls.FirstOrDefault(v => v.Name.Lexeme == "x");
        if(xDecl is null)
        {
            return false;
        }

        var symbol = model.GetDeclaredSymbol(xDecl);
        if(symbol is null)
        {
            return false;
        }

        // The inferred type's DisplayName must match the expected type
        return symbol.DeclaredType?.DisplayName == scenario.ExpectedTypeName;
    }
}