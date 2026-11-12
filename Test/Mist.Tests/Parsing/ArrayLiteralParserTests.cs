// Feature: array-literals
// **Validates: Requirements 1.1, 1.2, 1.3, 2.1, 3.1, 3.2, 3.3, 4.1, 4.2, 4.3, 11.1, 11.2, 11.3, 11.4, 11.5**

using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;

namespace Mist.Tests.Parsing;

public class ArrayLiteralParserTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    private static CompilationUnitNode ParseOrFail(string source)
    {
        var sourceText = new Mist.Common.Source.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var result = Parser.Parse(tokens, bag);

        var errors = bag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.True(errors.Count == 0,
            $"Unexpected parse errors:\n  {string.Join("\n  ", errors.Select(e => e.Message))}\nSource:\n{source}");

        return Assert.IsType<CompilationUnitNode>(result.Root);
    }

    private static ExpressionNode GetInitializerExpression(string source)
    {
        var cu = ParseOrFail(source);
        var typeDecl = Assert.IsType<BodyTypeTypeDeclarationNode>(cu.GetTypes()[0]);
        var methodMember = Assert.IsType<MethodMemberMemberDeclarationNode>(typeDecl.TypeBody.Members[0]);
        var block = methodMember.Method.Body.GetBlock();
        Assert.NotNull(block);
        var stmt = Assert.IsType<VariableDeclarationStatementStatementNode>(block!.Statements[0]);
        var varDecl = Assert.IsType<InferredVariableDeclarationVariableDeclarationStatementNode>(stmt.VariableDeclaration);
        return varDecl.Initializer;
    }

    private static ArrayLiteralExpressionPrimaryNode GetArrayLiteralFromInitializer(string source)
    {
        var expr = GetInitializerExpression(source);
        var primary = UnwrapToPrimary(expr);
        return Assert.IsType<ArrayLiteralExpressionPrimaryNode>(primary);
    }

    [Fact]
    public void SingleElementArrayLiteral_ProducesNodeWith1Element()
    {
        const string source = """
            class Test {
              void Run() {
                var x = [1];
              }
            }
            """;

        var arrayLiteral = GetArrayLiteralFromInitializer(source);

        _ = Assert.Single(arrayLiteral.GetElements());
    }

    [Fact]
    public void MultiElementArrayLiteral_ProducesNodeWith3Elements()
    {
        const string source = """
            class Test {
              void Run() {
                var x = [1, 2, 3];
              }
            }
            """;

        var arrayLiteral = GetArrayLiteralFromInitializer(source);

        Assert.Equal(3, arrayLiteral.GetElements().Length);
    }

    [Fact]
    public void TrailingComma_ProducesSameNodeAsWithout()
    {
        const string sourceWithTrailing = """
            class Test {
              void Run() {
                var x = [1, 2,];
              }
            }
            """;

        const string sourceWithout = """
            class Test {
              void Run() {
                var x = [1, 2];
              }
            }
            """;

        var withTrailing = GetArrayLiteralFromInitializer(sourceWithTrailing);
        var without = GetArrayLiteralFromInitializer(sourceWithout);

        Assert.Equal(2, withTrailing.GetElements().Length);
        Assert.Equal(2, without.GetElements().Length);
    }

    [Fact]
    public void NestedArrayLiteral_ProducesNestedNodes()
    {
        const string source = """
            class Test {
              void Run() {
                var x = [[1, 2], [3, 4]];
              }
            }
            """;

        var outerArray = GetArrayLiteralFromInitializer(source);

        var outerElements = outerArray.GetElements();
        Assert.Equal(2, outerElements.Length);

        var firstInner = UnwrapToPrimary(outerElements[0]);
        var firstInnerArray = Assert.IsType<ArrayLiteralExpressionPrimaryNode>(firstInner);
        Assert.Equal(2, firstInnerArray.GetElements().Length);

        var secondInner = UnwrapToPrimary(outerElements[1]);
        var secondInnerArray = Assert.IsType<ArrayLiteralExpressionPrimaryNode>(secondInner);
        Assert.Equal(2, secondInnerArray.GetElements().Length);
    }

    [Fact]
    public void ArrayLiteralAsVariableInitializer_ParsesCorrectly()
    {
        const string source = """
            class Test {
              void Run() {
                var x = [1, 2, 3];
              }
            }
            """;

        var arrayLiteral = GetArrayLiteralFromInitializer(source);
        Assert.NotNull(arrayLiteral);
        Assert.Equal(3, arrayLiteral.GetElements().Length);
    }

    [Fact]
    public void ArrayLiteralAsMethodArgument_ParsesCorrectly()
    {
        const string source = """
            class Test {
              void Foo(int[] arr) { }
              void Run() {
                Foo([1, 2, 3]);
              }
            }
            """;

        var cu = ParseOrFail(source);
        var typeDecl = Assert.IsType<BodyTypeTypeDeclarationNode>(cu.GetTypes()[0]);
        var methodMember = Assert.IsType<MethodMemberMemberDeclarationNode>(typeDecl.TypeBody.Members[1]);
        var block = methodMember.Method.Body.GetBlock();
        Assert.NotNull(block);

        var stmt = Assert.IsType<ExpressionStatementStatementNode>(block!.Statements[0]);
        Assert.NotNull(stmt);
        // If it parsed without errors, the array literal was accepted as an argument
    }

    [Fact]
    public void ArrayLiteralAsReturnValue_ParsesCorrectly()
    {
        const string source = """
            class Test {
              int[] GetNumbers() {
                return [1, 2, 3];
              }
            }
            """;

        var cu = ParseOrFail(source);
        var typeDecl = Assert.IsType<BodyTypeTypeDeclarationNode>(cu.GetTypes()[0]);
        var methodMember = Assert.IsType<MethodMemberMemberDeclarationNode>(typeDecl.TypeBody.Members[0]);
        var block = methodMember.Method.Body.GetBlock();
        Assert.NotNull(block);

        var returnStmt = Assert.IsType<ReturnStatementStatementNode>(block!.Statements[0]);
        Assert.NotNull(returnStmt.ReturnStatement.Value);

        var primary = UnwrapToPrimary(returnStmt.ReturnStatement.Value!);
        _ = Assert.IsType<ArrayLiteralExpressionPrimaryNode>(primary);
    }

    [Fact]
    public void ArrayLiteralAsAssignmentRHS_ParsesCorrectly()
    {
        const string source = """
            class Test {
              void Run() {
                var x = [1];
                x = [4, 5, 6];
              }
            }
            """;

        var cu = ParseOrFail(source);
        var typeDecl = Assert.IsType<BodyTypeTypeDeclarationNode>(cu.GetTypes()[0]);
        var methodMember = Assert.IsType<MethodMemberMemberDeclarationNode>(typeDecl.TypeBody.Members[0]);
        var block = methodMember.Method.Body.GetBlock();
        Assert.NotNull(block);

        Assert.True(block!.Statements.Length >= 2);
        // If it parsed without errors, the array literal was accepted as assignment RHS
    }

    [Fact]
    public void IndexAccess_ParsesAsIndexSuffix_NotArrayLiteral()
    {
        const string source = """
            class Test {
              void Run() {
                var arr = [1, 2, 3];
                var x = arr[0];
              }
            }
            """;

        var cu = ParseOrFail(source);
        var typeDecl = Assert.IsType<BodyTypeTypeDeclarationNode>(cu.GetTypes()[0]);
        var methodMember = Assert.IsType<MethodMemberMemberDeclarationNode>(typeDecl.TypeBody.Members[0]);
        var block = methodMember.Method.Body.GetBlock();
        Assert.NotNull(block);

        var stmt = Assert.IsType<VariableDeclarationStatementStatementNode>(block!.Statements[1]);
        var varDecl = Assert.IsType<InferredVariableDeclarationVariableDeclarationStatementNode>(stmt.VariableDeclaration);

        var primary = UnwrapToPrimary(varDecl.Initializer);

        Assert.IsNotType<ArrayLiteralExpressionPrimaryNode>(primary);
    }

    [Fact]
    public void IndexAccess_HasIndexSuffixInPostfix()
    {
        const string source = """
            class Test {
              void Run() {
                var arr = [1, 2, 3];
                var x = arr[0];
              }
            }
            """;

        var cu = ParseOrFail(source);
        var typeDecl = Assert.IsType<BodyTypeTypeDeclarationNode>(cu.GetTypes()[0]);
        var methodMember = Assert.IsType<MethodMemberMemberDeclarationNode>(typeDecl.TypeBody.Members[0]);
        var block = methodMember.Method.Body.GetBlock();
        Assert.NotNull(block);

        var stmt = Assert.IsType<VariableDeclarationStatementStatementNode>(block!.Statements[1]);
        var varDecl = Assert.IsType<InferredVariableDeclarationVariableDeclarationStatementNode>(stmt.VariableDeclaration);

        var postfix = FindPostfixNode(varDecl.Initializer);
        Assert.NotNull(postfix);
        Assert.True(postfix!.Suffix.Length > 0, "arr[0] should have at least one postfix suffix");
        _ = Assert.IsType<IndexSuffixPostfixSuffixNode>(postfix.Suffix[0]);
    }

    [Fact]
    public void IsExpression_WithBrackets_ParsesAsListPattern_NotArrayLiteral()
    {
        const string source = """
            class Test {
              void Run() {
                var arr = [1, 2, 3];
                var result = arr is [> 0, ..];
              }
            }
            """;

        var cu = ParseOrFail(source);
        var typeDecl = Assert.IsType<BodyTypeTypeDeclarationNode>(cu.GetTypes()[0]);
        var methodMember = Assert.IsType<MethodMemberMemberDeclarationNode>(typeDecl.TypeBody.Members[0]);
        var block = methodMember.Method.Body.GetBlock();
        Assert.NotNull(block);

        var stmt = Assert.IsType<VariableDeclarationStatementStatementNode>(block!.Statements[1]);
        var varDecl = Assert.IsType<InferredVariableDeclarationVariableDeclarationStatementNode>(stmt.VariableDeclaration);

        var isExpr = FindIsExpression(varDecl.Initializer);
        Assert.NotNull(isExpr);

        var pattern = isExpr!.Pattern;
        var primaryPattern = UnwrapToPrimaryPattern(pattern);
        _ = Assert.IsType<ListPatternPrimaryPatternNode>(primaryPattern);
    }

    [Fact]
    public void EmptyArrayLiteral_ParsesAsArrayLiteralWithNoElementList()
    {
        const string source = """
            class Test {
              void Run() {
                int[] x = [];
              }
            }
            """;

        var cu = ParseOrFail(source);
        var typeDecl = Assert.IsType<BodyTypeTypeDeclarationNode>(cu.GetTypes()[0]);
        var methodMember = Assert.IsType<MethodMemberMemberDeclarationNode>(typeDecl.TypeBody.Members[0]);
        var block = methodMember.Method.Body.GetBlock();
        Assert.NotNull(block);
        var stmt = Assert.IsType<VariableDeclarationStatementStatementNode>(block!.Statements[0]);
        var varDecl = Assert.IsType<TypedVariableDeclarationVariableDeclarationStatementNode>(stmt.VariableDeclaration);
        var primary = UnwrapToPrimary(varDecl.Initializer);

        var arrayLiteral = Assert.IsType<ArrayLiteralExpressionPrimaryNode>(primary);
        Assert.Null(arrayLiteral.Elements);
        Assert.Empty(arrayLiteral.GetElements());
    }

    [Fact]
    public void EmptyArrayLiteralWithVar_ParsesAsArrayLiteralWithNoElementList()
    {
        const string source = """
            class Test {
              void Run() {
                var foo = [];
              }
            }
            """;

        var cu = ParseOrFail(source);
        var typeDecl = Assert.IsType<BodyTypeTypeDeclarationNode>(cu.GetTypes()[0]);
        var methodMember = Assert.IsType<MethodMemberMemberDeclarationNode>(typeDecl.TypeBody.Members[0]);
        var block = methodMember.Method.Body.GetBlock();
        Assert.NotNull(block);
        var stmt = Assert.IsType<VariableDeclarationStatementStatementNode>(block!.Statements[0]);
        var varDecl = Assert.IsType<InferredVariableDeclarationVariableDeclarationStatementNode>(stmt.VariableDeclaration);
        var primary = UnwrapToPrimary(varDecl.Initializer);

        var arrayLiteral = Assert.IsType<ArrayLiteralExpressionPrimaryNode>(primary);
        Assert.Null(arrayLiteral.Elements);
        Assert.Empty(arrayLiteral.GetElements());
    }

    [Fact]
    public void IntArrayInExpressionPosition_ParsesAsTypeLiteralExpression_NotEmptyArray()
    {
        const string source = """
            class Test {
              void Run() {
                int[] x = int[];
              }
            }
            """;

        var cu = ParseOrFail(source);
        var typeDecl = Assert.IsType<BodyTypeTypeDeclarationNode>(cu.GetTypes()[0]);
        var methodMember = Assert.IsType<MethodMemberMemberDeclarationNode>(typeDecl.TypeBody.Members[0]);
        var block = methodMember.Method.Body.GetBlock();
        Assert.NotNull(block);
        var stmt = Assert.IsType<VariableDeclarationStatementStatementNode>(block!.Statements[0]);
        var varDecl = Assert.IsType<TypedVariableDeclarationVariableDeclarationStatementNode>(stmt.VariableDeclaration);
        var primary = UnwrapToPrimary(varDecl.Initializer);
        _ = Assert.IsType<TypeLiteralExpressionPrimaryNode>(primary);
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
            var childCount = 0;
            foreach(var child in current.Children())
            {
                if(child is null)
                {
                    continue;
                }

                childCount++;
                if(child is PrimaryNode childPrimary)
                {
                    return childPrimary;
                }

                next ??= child;
            }

            if(childCount > 1 || next is null)
            {
                return null;
            }

            current = next;
        }

        return null;
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

    private static IsExpressionRelationalNode? FindIsExpression(ExpressionNode expr)
    {
        return FindNode<IsExpressionRelationalNode>(expr);
    }

    private static PrimaryPatternNode? UnwrapToPrimaryPattern(PatternNode pattern)
    {
        Storm.Runtime.SyntaxNode current = pattern;

        for(var depth = 0; depth < 20; depth++)
        {
            if(current is PrimaryPatternNode primaryPattern)
            {
                return primaryPattern;
            }

            Storm.Runtime.SyntaxNode? next = null;
            foreach(var child in current.Children())
            {
                if(child is null)
                {
                    continue;
                }

                if(child is PrimaryPatternNode found)
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

    private static T? FindNode<T>(Storm.Runtime.SyntaxNode root) where T : Storm.Runtime.SyntaxNode
    {
        if(root is T target)
        {
            return target;
        }

        foreach(var child in root.Children())
        {
            if(child is null)
            {
                continue;
            }

            var found = FindNode<T>(child);
            if(found is not null)
            {
                return found;
            }
        }

        return null;
    }
}