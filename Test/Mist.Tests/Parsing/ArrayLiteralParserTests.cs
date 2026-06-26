// Feature: array-literals — Unit tests for parser handling of array literal expressions
// **Validates: Requirements 1.1, 1.2, 1.3, 3.1, 3.2, 3.3, 4.1, 4.2, 4.3, 11.1, 11.2, 11.3, 11.4, 11.5**

using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;

namespace Mist.Tests.Parsing;

/// <summary>
/// Unit tests verifying that the parser correctly handles array literal expressions
/// in various positions, and disambiguates them from index suffixes and list patterns.
/// </summary>
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
        var typeDecl = Assert.IsType<ClassTypeTypeDeclarationNode>(cu.GetTypes()[0]);
        var methodMember = Assert.IsType<MethodMemberMemberDeclarationNode>(typeDecl.ClassDeclaration.Members[0]);
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

    // ─── Requirement 1.1: Single-element array literal ───────────────────

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

        // Total elements = 1 (First) + Expressions.Length
        Assert.NotNull(arrayLiteral.First);
        Assert.Empty(arrayLiteral.Expressions);
    }

    // ─── Requirement 1.1: Multi-element array literal ────────────────────

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

        // Total elements = 1 (First) + 2 (Expressions) = 3
        Assert.NotNull(arrayLiteral.First);
        Assert.Equal(2, arrayLiteral.Expressions.Length);
    }

    // ─── Requirement 1.2: Trailing comma ─────────────────────────────────

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

        // Both should have 2 elements total: First + 1 in Expressions
        _ = Assert.Single(withTrailing.Expressions);
        _ = Assert.Single(without.Expressions);
    }

    // ─── Requirement 1.3: Nested array literals ──────────────────────────

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

        // Outer array has 2 elements: First + 1 in Expressions
        _ = Assert.Single(outerArray.Expressions);

        // First element should itself be an ArrayLiteralExpressionPrimaryNode
        var firstInner = UnwrapToPrimary(outerArray.First);
        _ = Assert.IsType<ArrayLiteralExpressionPrimaryNode>(firstInner);
        var firstInnerArray = (ArrayLiteralExpressionPrimaryNode)firstInner!;
        _ = Assert.Single(firstInnerArray.Expressions); // [1, 2] = First + 1

        // Second element should also be an ArrayLiteralExpressionPrimaryNode
        var secondInner = UnwrapToPrimary(outerArray.Expressions[0]);
        _ = Assert.IsType<ArrayLiteralExpressionPrimaryNode>(secondInner);
        var secondInnerArray = (ArrayLiteralExpressionPrimaryNode)secondInner!;
        _ = Assert.Single(secondInnerArray.Expressions); // [3, 4] = First + 1
    }

    // ─── Requirement 11.1: Array literal as variable initializer ─────────

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
        Assert.Equal(2, arrayLiteral.Expressions.Length);
    }

    // ─── Requirement 11.2: Array literal as method argument ──────────────

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
        var typeDecl = Assert.IsType<ClassTypeTypeDeclarationNode>(cu.GetTypes()[0]);
        var methodMember = Assert.IsType<MethodMemberMemberDeclarationNode>(typeDecl.ClassDeclaration.Members[1]);
        var block = methodMember.Method.Body.GetBlock();
        Assert.NotNull(block);

        // The statement is an expression statement containing a method call
        var stmt = Assert.IsType<ExpressionStatementStatementNode>(block!.Statements[0]);
        Assert.NotNull(stmt);
        // If it parsed without errors, the array literal was accepted as an argument
    }

    // ─── Requirement 11.3: Array literal as return value ─────────────────

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
        var typeDecl = Assert.IsType<ClassTypeTypeDeclarationNode>(cu.GetTypes()[0]);
        var methodMember = Assert.IsType<MethodMemberMemberDeclarationNode>(typeDecl.ClassDeclaration.Members[0]);
        var block = methodMember.Method.Body.GetBlock();
        Assert.NotNull(block);

        var returnStmt = Assert.IsType<ReturnStatementStatementNode>(block!.Statements[0]);
        Assert.NotNull(returnStmt.ReturnStatement.Value);

        // Unwrap the return value to find the array literal
        var primary = UnwrapToPrimary(returnStmt.ReturnStatement.Value!);
        _ = Assert.IsType<ArrayLiteralExpressionPrimaryNode>(primary);
    }

    // ─── Requirement 11.5: Array literal as assignment RHS ───────────────

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
        var typeDecl = Assert.IsType<ClassTypeTypeDeclarationNode>(cu.GetTypes()[0]);
        var methodMember = Assert.IsType<MethodMemberMemberDeclarationNode>(typeDecl.ClassDeclaration.Members[0]);
        var block = methodMember.Method.Body.GetBlock();
        Assert.NotNull(block);

        // Second statement is the assignment
        Assert.True(block!.Statements.Length >= 2);
        // If it parsed without errors, the array literal was accepted as assignment RHS
    }

    // ─── Requirement 3.1, 3.2, 3.3: Disambiguation from IndexSuffix ─────

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
        var typeDecl = Assert.IsType<ClassTypeTypeDeclarationNode>(cu.GetTypes()[0]);
        var methodMember = Assert.IsType<MethodMemberMemberDeclarationNode>(typeDecl.ClassDeclaration.Members[0]);
        var block = methodMember.Method.Body.GetBlock();
        Assert.NotNull(block);

        // Second statement: var x = arr[0];
        var stmt = Assert.IsType<VariableDeclarationStatementStatementNode>(block!.Statements[1]);
        var varDecl = Assert.IsType<InferredVariableDeclarationVariableDeclarationStatementNode>(stmt.VariableDeclaration);

        // The initializer should contain a PostfixNode with an IndexSuffix,
        // not an ArrayLiteralExpressionPrimaryNode
        var primary = UnwrapToPrimary(varDecl.Initializer);

        // arr[0] should NOT be an array literal
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
        var typeDecl = Assert.IsType<ClassTypeTypeDeclarationNode>(cu.GetTypes()[0]);
        var methodMember = Assert.IsType<MethodMemberMemberDeclarationNode>(typeDecl.ClassDeclaration.Members[0]);
        var block = methodMember.Method.Body.GetBlock();
        Assert.NotNull(block);

        // Second statement: var x = arr[0];
        var stmt = Assert.IsType<VariableDeclarationStatementStatementNode>(block!.Statements[1]);
        var varDecl = Assert.IsType<InferredVariableDeclarationVariableDeclarationStatementNode>(stmt.VariableDeclaration);

        // Walk the expression tree to find the PostfixNode with IndexSuffix
        var postfix = FindPostfixNode(varDecl.Initializer);
        Assert.NotNull(postfix);
        Assert.True(postfix!.Suffix.Length > 0, "arr[0] should have at least one postfix suffix");
        _ = Assert.IsType<IndexSuffixPostfixSuffixNode>(postfix.Suffix[0]);
    }

    // ─── Requirement 4.1, 4.2, 4.3: Disambiguation from ListPattern ─────

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
        var typeDecl = Assert.IsType<ClassTypeTypeDeclarationNode>(cu.GetTypes()[0]);
        var methodMember = Assert.IsType<MethodMemberMemberDeclarationNode>(typeDecl.ClassDeclaration.Members[0]);
        var block = methodMember.Method.Body.GetBlock();
        Assert.NotNull(block);

        // Second statement: var result = arr is [> 0, ..];
        var stmt = Assert.IsType<VariableDeclarationStatementStatementNode>(block!.Statements[1]);
        var varDecl = Assert.IsType<InferredVariableDeclarationVariableDeclarationStatementNode>(stmt.VariableDeclaration);

        // Find the IsExpression in the expression tree
        var isExpr = FindIsExpression(varDecl.Initializer);
        Assert.NotNull(isExpr);

        // The pattern should be a ListPattern, not an ArrayLiteralExpression
        var pattern = isExpr!.Pattern;
        var primaryPattern = UnwrapToPrimaryPattern(pattern);
        _ = Assert.IsType<ListPatternPrimaryPatternNode>(primaryPattern);
    }

    // ─── Requirement 2.1: Empty array literal [] ──────────────────────────

    [Fact]
    public void EmptyArrayLiteral_ParsesAsEmptyArrayLiteralExpressionPrimaryNode()
    {
        const string source = """
            class Test {
              void Run() {
                int[] x = [];
              }
            }
            """;

        var cu = ParseOrFail(source);
        var typeDecl = Assert.IsType<ClassTypeTypeDeclarationNode>(cu.GetTypes()[0]);
        var methodMember = Assert.IsType<MethodMemberMemberDeclarationNode>(typeDecl.ClassDeclaration.Members[0]);
        var block = methodMember.Method.Body.GetBlock();
        Assert.NotNull(block);
        var stmt = Assert.IsType<VariableDeclarationStatementStatementNode>(block!.Statements[0]);
        var varDecl = Assert.IsType<TypedVariableDeclarationVariableDeclarationStatementNode>(stmt.VariableDeclaration);
        var primary = UnwrapToPrimary(varDecl.Initializer);
        _ = Assert.IsType<EmptyArrayLiteralExpressionPrimaryNode>(primary);
    }

    [Fact]
    public void EmptyArrayLiteralWithVar_ParsesAsEmptyArrayLiteralExpressionPrimaryNode()
    {
        const string source = """
            class Test {
              void Run() {
                var foo = [];
              }
            }
            """;

        var cu = ParseOrFail(source);
        var typeDecl = Assert.IsType<ClassTypeTypeDeclarationNode>(cu.GetTypes()[0]);
        var methodMember = Assert.IsType<MethodMemberMemberDeclarationNode>(typeDecl.ClassDeclaration.Members[0]);
        var block = methodMember.Method.Body.GetBlock();
        Assert.NotNull(block);
        var stmt = Assert.IsType<VariableDeclarationStatementStatementNode>(block!.Statements[0]);
        var varDecl = Assert.IsType<InferredVariableDeclarationVariableDeclarationStatementNode>(stmt.VariableDeclaration);
        var primary = UnwrapToPrimary(varDecl.Initializer);
        _ = Assert.IsType<EmptyArrayLiteralExpressionPrimaryNode>(primary);
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
        var typeDecl = Assert.IsType<ClassTypeTypeDeclarationNode>(cu.GetTypes()[0]);
        var methodMember = Assert.IsType<MethodMemberMemberDeclarationNode>(typeDecl.ClassDeclaration.Members[0]);
        var block = methodMember.Method.Body.GetBlock();
        Assert.NotNull(block);
        var stmt = Assert.IsType<VariableDeclarationStatementStatementNode>(block!.Statements[0]);
        var varDecl = Assert.IsType<TypedVariableDeclarationVariableDeclarationStatementNode>(stmt.VariableDeclaration);
        var primary = UnwrapToPrimary(varDecl.Initializer);
        _ = Assert.IsType<TypeLiteralExpressionPrimaryNode>(primary);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Unwraps a simple expression down to its primary node.
    /// </summary>
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

    /// <summary>
    /// Finds the PostfixNode in an expression tree.
    /// </summary>
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

    /// <summary>
    /// Finds an IsExpressionRelationalNode in the expression tree.
    /// </summary>
    private static IsExpressionRelationalNode? FindIsExpression(ExpressionNode expr)
    {
        return FindNode<IsExpressionRelationalNode>(expr);
    }

    /// <summary>
    /// Unwraps a PatternNode to its PrimaryPatternNode.
    /// </summary>
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

    /// <summary>
    /// Generic depth-first search for a node of type T in the expression tree.
    /// </summary>
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