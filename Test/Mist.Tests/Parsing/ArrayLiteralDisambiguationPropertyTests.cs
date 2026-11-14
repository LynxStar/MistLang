// Feature: array-literals, Property 3: Expression-position disambiguation
// **Validates: Requirements 3.1, 3.2, 3.3**

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;

namespace Mist.Tests.Parsing;

public sealed class DisambiguationScenario
{
    public string Name { get; }
    public int IndexExpr { get; }

    public DisambiguationScenario(string name, int indexExpr)
    {
        Name = name;
        IndexExpr = indexExpr;
    }

    public override string ToString()
    {
        return $"Name={Name}, IndexExpr={IndexExpr}";
    }
}

public class ArrayLiteralDisambiguationPropertyTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();

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

    private static CompilationUnitNode? ParseSource(string source)
    {
        var sourceText = new Mist.Common.Source.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var result = Parser.Parse(tokens, bag);

        var errors = bag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if(errors.Count > 0)
        {
            return null;
        }

        return result.Root as CompilationUnitNode;
    }

    /// <summary>
    /// Declares `name` first so it is a valid identifier to index into.
    /// </summary>
    private static string BuildIndexAccessSource(string name, int expr)
    {
        return $"class Test {{ void Run() {{ var {name} = [1, 2, 3]; var y = {name}[{expr}]; }} }}";
    }

    private static string BuildArrayLiteralSource(int expr)
    {
        return $"class Test {{ void Run() {{ var x = [{expr}]; }} }}";
    }

    private static PostfixNode? FindPostfixNode(ExpressionNode expr)
    {
        Storm.Runtime.SyntaxNode current = expr;

        for(var depth = 0; depth < 20; depth++)
        {
            if(current is PostfixNode postfix)
            {
                return postfix;
            }

            Storm.Runtime.SyntaxNode? next = null;
            foreach(var child in current.Children())
            {
                if(child is null)
                {
                    continue;
                }

                if(child is PostfixNode found)
                {
                    return found;
                }

                next ??= child;
            }

            if(next is null)
            {
                return null;
            }

            current = next;
        }

        return null;
    }

    private static PrimaryNode? UnwrapToPrimary(ExpressionNode expr)
    {
        Storm.Runtime.SyntaxNode current = expr;

        for(var depth = 0; depth < 20; depth++)
        {
            if(current is PrimaryNode primary)
            {
                return primary;
            }

            Storm.Runtime.SyntaxNode? next = null;
            foreach(var child in current.Children())
            {
                if(child is null)
                {
                    continue;
                }

                if(child is PrimaryNode childPrimary)
                {
                    return childPrimary;
                }

                next ??= child;
            }

            if(next is null)
            {
                return null;
            }

            current = next;
        }

        return null;
    }

    private static ExpressionNode? ExtractSecondVarInitializer(CompilationUnitNode cu)
    {
        if(cu.GetTypes()[0] is not BodyTypeTypeDeclarationNode typeDecl)
        {
            return null;
        }

        if(typeDecl.TypeBody.Members[0] is not MethodMemberMemberDeclarationNode methodMember)
        {
            return null;
        }

        var block = methodMember.Method.Body.GetBlock();
        if(block is null)
        {
            return null;
        }

        if(block.Statements.Length < 2)
        {
            return null;
        }

        if(block.Statements[1] is not VariableDeclarationStatementStatementNode stmt)
        {
            return null;
        }

        if(stmt.VariableDeclaration is not InferredVariableDeclarationVariableDeclarationStatementNode varDecl)
        {
            return null;
        }

        return varDecl.Initializer;
    }

    private static ArrayLiteralExpressionPrimaryNode? ExtractArrayLiteralFromFirstVar(CompilationUnitNode cu)
    {
        if(cu.GetTypes()[0] is not BodyTypeTypeDeclarationNode typeDecl)
        {
            return null;
        }

        if(typeDecl.TypeBody.Members[0] is not MethodMemberMemberDeclarationNode methodMember)
        {
            return null;
        }

        var block = methodMember.Method.Body.GetBlock();
        if(block is null)
        {
            return null;
        }

        if(block.Statements[0] is not VariableDeclarationStatementStatementNode stmt)
        {
            return null;
        }

        if(stmt.VariableDeclaration is not InferredVariableDeclarationVariableDeclarationStatementNode varDecl)
        {
            return null;
        }

        var primary = UnwrapToPrimary(varDecl.Initializer);
        return primary as ArrayLiteralExpressionPrimaryNode;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Property 3a: name[expr] produces IndexSuffix (not ArrayLiteralExpression)
    // ═════════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(ArrayLiteralDisambiguationPropertyTests) })]
    public Property IndexAccess_ProducesIndexSuffix(DisambiguationScenario scenario)
    {
        var source = BuildIndexAccessSource(scenario.Name, scenario.IndexExpr);
        var root = ParseSource(source);

        if(root is null)
        {
            return false.ToProperty().Label($"Failed to parse: {source}");
        }

        var initializer = ExtractSecondVarInitializer(root);
        if(initializer is null)
        {
            return false.ToProperty().Label($"Could not extract second var initializer from: {source}");
        }

        var postfix = FindPostfixNode(initializer);
        if(postfix is null)
        {
            return false.ToProperty().Label($"No PostfixNode found in: {source}");
        }

        if(postfix.Suffix.Length == 0)
        {
            return false.ToProperty().Label($"PostfixNode has no suffixes in: {source}");
        }

        var hasIndexSuffix = postfix.Suffix[0] is IndexSuffixPostfixSuffixNode;

        return hasIndexSuffix
            .ToProperty()
            .Label($"Expected IndexSuffixPostfixSuffixNode but got {postfix.Suffix[0].GetType().Name} for: {source}");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Property 3b: var x = [expr] produces ArrayLiteralExpressionPrimaryNode
    // ═════════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(ArrayLiteralDisambiguationPropertyTests) })]
    public Property ArrayLiteralInExpressionPosition_ProducesArrayLiteralNode(DisambiguationScenario scenario)
    {
        var source = BuildArrayLiteralSource(scenario.IndexExpr);
        var root = ParseSource(source);

        if(root is null)
        {
            return false.ToProperty().Label($"Failed to parse: {source}");
        }

        var arrayLiteral = ExtractArrayLiteralFromFirstVar(root);

        return (arrayLiteral is not null)
            .ToProperty()
            .Label($"Expected ArrayLiteralExpressionPrimaryNode but did not find one in: {source}");
    }
}