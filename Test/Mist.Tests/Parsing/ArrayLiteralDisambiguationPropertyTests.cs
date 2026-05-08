// Feature: array-literals, Property 3: Expression-position disambiguation
// **Validates: Requirements 3.1, 3.2, 3.3**

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;

namespace Mist.Tests.Parsing;

/// <summary>
/// Represents a test scenario for disambiguation: an identifier name and
/// an integer expression used inside brackets.
/// </summary>
public sealed class DisambiguationScenario
{
    public string Name { get; }
    public int IndexExpr { get; }

    public DisambiguationScenario(string name, int indexExpr)
    {
        Name = name;
        IndexExpr = indexExpr;
    }

    public override string ToString() =>
        $"Name={Name}, IndexExpr={IndexExpr}";
}

/// <summary>
/// Property-based tests for expression-position disambiguation.
///
/// Property 3: For any identifier `name` and expression `expr`,
/// parsing `name[expr]` SHALL produce a Postfix node with an IndexSuffix
/// (not an ArrayLiteralExpression), while parsing `var x = [expr]` SHALL
/// produce a variable declaration whose initializer contains an
/// ArrayLiteralExpressionPrimaryNode.
/// </summary>
public class ArrayLiteralDisambiguationPropertyTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();

    // ─── Generator ──────────────────────────────────────────────────────────

    private static readonly string[] ValidIdentifiers =
        ["arr", "x", "data", "items", "list", "values", "nums", "result", "buf", "vec"];

    public static Arbitrary<DisambiguationScenario> ArbitraryScenario()
    {
        var gen =
            from nameIdx in Gen.Choose(0, ValidIdentifiers.Length - 1)
            from expr in Gen.Choose(0, 1000)
            select new DisambiguationScenario(ValidIdentifiers[nameIdx], expr);

        return gen.ToArbitrary();
    }

    // ─── Parsing helpers ────────────────────────────────────────────────────

    private static CompilationUnitNode? ParseSource(string source)
    {
        var sourceText = new Mist.Common.Text.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var result = Parser.Parse(tokens, bag);

        var errors = bag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if (errors.Count > 0)
            return null;

        return result.Root as CompilationUnitNode;
    }

    /// <summary>
    /// Builds source for index access: `name[expr]` after a primary expression.
    /// We declare the array first so `name` is a valid identifier to index into.
    /// </summary>
    private static string BuildIndexAccessSource(string name, int expr)
    {
        return $"class Test {{ void Run() {{ var {name} = [1, 2, 3]; var y = {name}[{expr}]; }} }}";
    }

    /// <summary>
    /// Builds source for array literal in expression position: `var x = [expr]`.
    /// </summary>
    private static string BuildArrayLiteralSource(int expr)
    {
        return $"class Test {{ void Run() {{ var x = [{expr}]; }} }}";
    }

    /// <summary>
    /// Finds the PostfixNode in an expression tree.
    /// </summary>
    private static PostfixNode? FindPostfixNode(ExpressionNode expr)
    {
        Storm.Runtime.SyntaxNode current = expr;

        for (var depth = 0; depth < 20; depth++)
        {
            if (current is PostfixNode postfix)
                return postfix;

            Storm.Runtime.SyntaxNode? next = null;
            foreach (var child in current.Children())
            {
                if (child is null) continue;
                if (child is PostfixNode found)
                    return found;
                next ??= child;
            }

            if (next is null)
                return null;

            current = next;
        }

        return null;
    }

    /// <summary>
    /// Unwraps a simple expression down to its primary node.
    /// </summary>
    private static PrimaryNode? UnwrapToPrimary(ExpressionNode expr)
    {
        Storm.Runtime.SyntaxNode current = expr;

        for (var depth = 0; depth < 20; depth++)
        {
            if (current is PrimaryNode primary)
                return primary;

            Storm.Runtime.SyntaxNode? next = null;
            foreach (var child in current.Children())
            {
                if (child is null) continue;
                if (child is PrimaryNode childPrimary)
                    return childPrimary;
                next ??= child;
            }

            if (next is null)
                return null;

            current = next;
        }

        return null;
    }

    /// <summary>
    /// Extracts the initializer expression from the second variable declaration statement.
    /// </summary>
    private static ExpressionNode? ExtractSecondVarInitializer(CompilationUnitNode cu)
    {
        var typeDecl = cu.GetTypes()[0] as ClassTypeTypeDeclarationNode;
        if (typeDecl is null) return null;

        var methodMember = typeDecl.ClassDeclaration.Members[0] as MethodMemberMemberDeclarationNode;
        if (methodMember is null) return null;

        var block = methodMember.Method.Body.GetBlock();
        if (block is null) return null;

        // Second statement (index 1): var y = name[expr];
        if (block.Statements.Length < 2) return null;

        var stmt = block.Statements[1] as VariableDeclarationStatementStatementNode;
        if (stmt is null) return null;

        var varDecl = stmt.VariableDeclaration as InferredVariableDeclarationVariableDeclarationStatementNode;
        if (varDecl is null) return null;

        return varDecl.Initializer;
    }

    /// <summary>
    /// Extracts the ArrayLiteralExpressionPrimaryNode from the first variable declaration.
    /// </summary>
    private static ArrayLiteralExpressionPrimaryNode? ExtractArrayLiteralFromFirstVar(CompilationUnitNode cu)
    {
        var typeDecl = cu.GetTypes()[0] as ClassTypeTypeDeclarationNode;
        if (typeDecl is null) return null;

        var methodMember = typeDecl.ClassDeclaration.Members[0] as MethodMemberMemberDeclarationNode;
        if (methodMember is null) return null;

        var block = methodMember.Method.Body.GetBlock();
        if (block is null) return null;

        var stmt = block.Statements[0] as VariableDeclarationStatementStatementNode;
        if (stmt is null) return null;

        var varDecl = stmt.VariableDeclaration as InferredVariableDeclarationVariableDeclarationStatementNode;
        if (varDecl is null) return null;

        var primary = UnwrapToPrimary(varDecl.Initializer);
        return primary as ArrayLiteralExpressionPrimaryNode;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Property 3a: name[expr] produces IndexSuffix (not ArrayLiteralExpression)
    //
    // For any identifier `name` and integer expression `expr`, parsing
    // `name[expr]` after a primary expression SHALL produce a PostfixNode
    // with an IndexSuffixPostfixSuffixNode.
    // ═════════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(ArrayLiteralDisambiguationPropertyTests) })]
    public Property IndexAccess_ProducesIndexSuffix(DisambiguationScenario scenario)
    {
        var source = BuildIndexAccessSource(scenario.Name, scenario.IndexExpr);
        var root = ParseSource(source);

        if (root is null)
            return false.ToProperty().Label($"Failed to parse: {source}");

        var initializer = ExtractSecondVarInitializer(root);
        if (initializer is null)
            return false.ToProperty().Label($"Could not extract second var initializer from: {source}");

        // Find the PostfixNode — it should have an IndexSuffix
        var postfix = FindPostfixNode(initializer);
        if (postfix is null)
            return false.ToProperty().Label($"No PostfixNode found in: {source}");

        if (postfix.Suffix.Length == 0)
            return false.ToProperty().Label($"PostfixNode has no suffixes in: {source}");

        var hasIndexSuffix = postfix.Suffix[0] is IndexSuffixPostfixSuffixNode;

        return hasIndexSuffix
            .ToProperty()
            .Label($"Expected IndexSuffixPostfixSuffixNode but got {postfix.Suffix[0].GetType().Name} for: {source}");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Property 3b: var x = [expr] produces ArrayLiteralExpressionPrimaryNode
    //
    // For any integer expression `expr`, parsing `var x = [expr]` SHALL
    // produce a variable declaration whose initializer contains an
    // ArrayLiteralExpressionPrimaryNode.
    // ═════════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(ArrayLiteralDisambiguationPropertyTests) })]
    public Property ArrayLiteralInExpressionPosition_ProducesArrayLiteralNode(DisambiguationScenario scenario)
    {
        var source = BuildArrayLiteralSource(scenario.IndexExpr);
        var root = ParseSource(source);

        if (root is null)
            return false.ToProperty().Label($"Failed to parse: {source}");

        var arrayLiteral = ExtractArrayLiteralFromFirstVar(root);

        return (arrayLiteral is not null)
            .ToProperty()
            .Label($"Expected ArrayLiteralExpressionPrimaryNode but did not find one in: {source}");
    }
}
