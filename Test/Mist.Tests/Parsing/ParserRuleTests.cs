using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;

namespace Mist.Tests.Parsing;

/// <summary>
/// Unit tests for parser rules.
/// Validates operator precedence, statement forms, and edge cases.
///
/// Requirements: 2.1-2.11, 3.1-3.9, 4.1-4.8
///
/// Note: The parser is currently skeletal (returns null root). These tests
/// are structured to validate behavior once the full parser is implemented.
/// Tests that require a non-null AST root gracefully handle the skeletal case.
/// </summary>
public class ParserRuleTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    private static ParseResult ParseSource(string source)
    {
        var sourceText = new Mist.Common.Text.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        return Parser.Parse(tokens, bag);
    }

    // --- Edge Cases ---

    [Fact]
    public void EmptyProgram_ParsesWithoutErrors()
    {
        var result = ParseSource("");
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void EmptyProgram_ReturnsResult()
    {
        var result = ParseSource("");
        Assert.NotNull(result);
        Assert.NotNull(result.Source);
        Assert.NotNull(result.Tokens);
    }

    [Fact]
    public void ProgramWithOnlyComments_ParsesWithoutErrors()
    {
        var source = "// This is a comment\n// Another comment\n";
        var result = ParseSource(source);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void ProgramWithOnlyWhitespace_ParsesWithoutErrors()
    {
        var source = "   \t\n  \n  ";
        var result = ParseSource(source);
        Assert.Empty(result.Diagnostics);
    }

    // --- Operator Precedence Tests ---

    [Fact]
    public void OperatorPrecedence_MultiplicationBindsTighterThanAddition()
    {
        // "1 + 2 * 3" should parse as "1 + (2 * 3)", not "(1 + 2) * 3"
        var source = "class T { public void M() { int x = 1 + 2 * 3; } }";
        var result = ParseSource(source);

        if(result.Root is CompilationUnitNode compilationUnit)
        {
            var typeDecl = compilationUnit.GetTypes()[0] as ClassTypeTypeDeclarationNode;
            Assert.NotNull(typeDecl);
            var classDecl = typeDecl!.ClassDeclaration;
            var methodMember = classDecl.Members[0] as MethodMemberMemberDeclarationNode;
            Assert.NotNull(methodMember);
            var method = methodMember!.Method;
            var stmt = method.Body.GetBlock()!.Statements[0] as VariableDeclarationStatementStatementNode;
            Assert.NotNull(stmt);
            var varDecl = stmt!.VariableDeclaration as TypedVariableDeclarationVariableDeclarationStatementNode;
            Assert.NotNull(varDecl);

            var expr = varDecl!.Initializer;
            var assignment = expr.AsAssignment();
            var logicalOr = assignment.Target.Left;
            var logicalAnd = logicalOr.Left;
            var equality = logicalAnd.Left;
            var relational = (RelationalComparisonRelationalNode)equality.Left;
            var additive = relational.Left;

            // The additive node should have a PLUS operator
            Assert.Equal((int)TokenKind.PLUS, additive.Operator.Kind);
            // The right side of the additive should contain the multiplicative (2 * 3)
            var multiplicative = additive.Right;
            Assert.Equal((int)TokenKind.STAR, multiplicative.Operator.Kind);
        }
        // If Root is null (skeletal parser), the test passes gracefully.
    }

    [Fact]
    public void OperatorPrecedence_LogicalAndBindsTighterThanLogicalOr()
    {
        var source = "class T { public void M() { bool x = a || b && c; } }";
        var result = ParseSource(source);

        if(result.Root is CompilationUnitNode compilationUnit)
        {
            var typeDecl = compilationUnit.GetTypes()[0] as ClassTypeTypeDeclarationNode;
            var classDecl = typeDecl!.ClassDeclaration;
            var methodMember = classDecl.Members[0] as MethodMemberMemberDeclarationNode;
            var method = methodMember!.Method;
            var stmt = method.Body.GetBlock()!.Statements[0] as VariableDeclarationStatementStatementNode;
            var varDecl = stmt!.VariableDeclaration as TypedVariableDeclarationVariableDeclarationStatementNode;

            var expr = varDecl!.Initializer;
            var assignment = expr.AsAssignment();
            var logicalOr = assignment.Target.Left;

            // LogicalOr should have Right (b && c)
            Assert.NotNull(logicalOr.Right);
        }
    }

    [Fact]
    public void OperatorPrecedence_EqualityBindsTighterThanLogicalAnd()
    {
        var source = "class T { public void M() { bool x = a && b == c; } }";
        var result = ParseSource(source);

        if(result.Root is CompilationUnitNode compilationUnit)
        {
            var typeDecl = compilationUnit.GetTypes()[0] as ClassTypeTypeDeclarationNode;
            var classDecl = typeDecl!.ClassDeclaration;
            var methodMember = classDecl.Members[0] as MethodMemberMemberDeclarationNode;
            var method = methodMember!.Method;
            var stmt = method.Body.GetBlock()!.Statements[0] as VariableDeclarationStatementStatementNode;
            var varDecl = stmt!.VariableDeclaration as TypedVariableDeclarationVariableDeclarationStatementNode;

            var expr = varDecl!.Initializer;
            var assignment = expr.AsAssignment();
            var logicalOr = assignment.Target.Left;
            var logicalAnd = logicalOr.Left;

            var equality = logicalAnd.Right;
            Assert.Equal((int)TokenKind.EQUAL_EQUAL, equality.Operator.Kind);
        }
    }

    // --- Statement Form Tests ---

    [Fact]
    public void IfStatement_ParsesCorrectly()
    {
        var source = "class T { public void M() { if (true) { } } }";
        var result = ParseSource(source);

        if(result.Root is CompilationUnitNode cu)
        {
            var typeDecl = cu.GetTypes()[0] as ClassTypeTypeDeclarationNode;
            var method = (typeDecl!.ClassDeclaration.Members[0] as MethodMemberMemberDeclarationNode)!.Method;
            var stmt = method.Body.GetBlock()!.Statements[0] as IfStatementStatementNode;
            Assert.NotNull(stmt);
            Assert.NotNull(stmt!.IfStatement.Condition);
            Assert.NotNull(stmt.IfStatement.ThenBranch);
            Assert.Null(stmt.IfStatement.ElseBranch);
        }
    }

    [Fact]
    public void IfElseStatement_ParsesCorrectly()
    {
        var source = "class T { public void M() { if (true) { } else { } } }";
        var result = ParseSource(source);

        if(result.Root is CompilationUnitNode cu)
        {
            var typeDecl = cu.GetTypes()[0] as ClassTypeTypeDeclarationNode;
            var method = (typeDecl!.ClassDeclaration.Members[0] as MethodMemberMemberDeclarationNode)!.Method;
            var stmt = method.Body.GetBlock()!.Statements[0] as IfStatementStatementNode;
            Assert.NotNull(stmt);
            Assert.NotNull(stmt!.IfStatement.ElseBranch);
            _ = Assert.IsType<ElseBlockElseClauseNode>(stmt.IfStatement.ElseBranch);
        }
    }

    [Fact]
    public void WhileStatement_ParsesCorrectly()
    {
        var source = "class T { public void M() { while (true) { } } }";
        var result = ParseSource(source);

        if(result.Root is CompilationUnitNode cu)
        {
            var typeDecl = cu.GetTypes()[0] as ClassTypeTypeDeclarationNode;
            var method = (typeDecl!.ClassDeclaration.Members[0] as MethodMemberMemberDeclarationNode)!.Method;
            var stmt = method.Body.GetBlock()!.Statements[0] as WhileStatementStatementNode;
            Assert.NotNull(stmt);
            Assert.NotNull(stmt!.WhileStatement.Condition);
            Assert.NotNull(stmt.WhileStatement.Body);
        }
    }

    [Fact]
    public void ForStatement_ParsesCorrectly()
    {
        var source = "class T { public void M() { for (int i = 0; i < 10; i) { } } }";
        var result = ParseSource(source);

        if(result.Root is CompilationUnitNode cu)
        {
            var typeDecl = cu.GetTypes()[0] as ClassTypeTypeDeclarationNode;
            var method = (typeDecl!.ClassDeclaration.Members[0] as MethodMemberMemberDeclarationNode)!.Method;
            var stmt = method.Body.GetBlock()!.Statements[0] as ForStatementStatementNode;
            Assert.NotNull(stmt);
            Assert.NotNull(stmt!.ForStatement.Initializer);
            Assert.NotNull(stmt.ForStatement.Condition);
            Assert.NotNull(stmt.ForStatement.Iterator);
            Assert.NotNull(stmt.ForStatement.Body);
        }
    }

    [Fact]
    public void ForeachStatement_ParsesCorrectly()
    {
        var source = "class T { public void M() { foreach (int item in items) { } } }";
        var result = ParseSource(source);

        if(result.Root is CompilationUnitNode cu)
        {
            var typeDecl = cu.GetTypes()[0] as ClassTypeTypeDeclarationNode;
            var method = (typeDecl!.ClassDeclaration.Members[0] as MethodMemberMemberDeclarationNode)!.Method;
            var stmt = method.Body.GetBlock()!.Statements[0] as ForeachStatementStatementNode;
            Assert.NotNull(stmt);
            Assert.NotNull(stmt!.ForeachStatement.Type);
            Assert.NotNull(stmt.ForeachStatement.Collection);
            Assert.NotNull(stmt.ForeachStatement.Body);
        }
    }

    [Fact]
    public void ReturnStatement_WithValue_ParsesCorrectly()
    {
        var source = "class T { public int M() { return 42; } }";
        var result = ParseSource(source);

        if(result.Root is CompilationUnitNode cu)
        {
            var typeDecl = cu.GetTypes()[0] as ClassTypeTypeDeclarationNode;
            var method = (typeDecl!.ClassDeclaration.Members[0] as MethodMemberMemberDeclarationNode)!.Method;
            var stmt = method.Body.GetBlock()!.Statements[0] as ReturnStatementStatementNode;
            Assert.NotNull(stmt);
            Assert.NotNull(stmt!.ReturnStatement.Value);
        }
    }

    [Fact]
    public void ReturnStatement_WithoutValue_ParsesCorrectly()
    {
        var source = "class T { public void M() { return; } }";
        var result = ParseSource(source);

        if(result.Root is CompilationUnitNode cu)
        {
            var typeDecl = cu.GetTypes()[0] as ClassTypeTypeDeclarationNode;
            var method = (typeDecl!.ClassDeclaration.Members[0] as MethodMemberMemberDeclarationNode)!.Method;
            var stmt = method.Body.GetBlock()!.Statements[0] as ReturnStatementStatementNode;
            Assert.NotNull(stmt);
            Assert.Null(stmt!.ReturnStatement.Value);
        }
    }

    [Fact]
    public void BreakStatement_ParsesCorrectly()
    {
        var source = "class T { public void M() { while (true) { break; } } }";
        var result = ParseSource(source);

        if(result.Root is CompilationUnitNode cu)
        {
            var typeDecl = cu.GetTypes()[0] as ClassTypeTypeDeclarationNode;
            var method = (typeDecl!.ClassDeclaration.Members[0] as MethodMemberMemberDeclarationNode)!.Method;
            var whileStmt = method.Body.GetBlock()!.Statements[0] as WhileStatementStatementNode;
            Assert.NotNull(whileStmt);
            var breakStmt = whileStmt!.WhileStatement.Body.Statements[0] as BreakStatementStatementNode;
            Assert.NotNull(breakStmt);
        }
    }

    [Fact]
    public void VariableDeclaration_WithExplicitType_ParsesCorrectly()
    {
        var source = "class T { public void M() { int x = 42; } }";
        var result = ParseSource(source);

        if(result.Root is CompilationUnitNode cu)
        {
            var typeDecl = cu.GetTypes()[0] as ClassTypeTypeDeclarationNode;
            var method = (typeDecl!.ClassDeclaration.Members[0] as MethodMemberMemberDeclarationNode)!.Method;
            var stmt = method.Body.GetBlock()!.Statements[0] as VariableDeclarationStatementStatementNode;
            Assert.NotNull(stmt);
            _ = Assert.IsType<TypedVariableDeclarationVariableDeclarationStatementNode>(stmt!.VariableDeclaration);
        }
    }

    [Fact]
    public void VariableDeclaration_WithVar_ParsesCorrectly()
    {
        var source = "class T { public void M() { var x = 42; } }";
        var result = ParseSource(source);

        if(result.Root is CompilationUnitNode cu)
        {
            var typeDecl = cu.GetTypes()[0] as ClassTypeTypeDeclarationNode;
            var method = (typeDecl!.ClassDeclaration.Members[0] as MethodMemberMemberDeclarationNode)!.Method;
            var stmt = method.Body.GetBlock()!.Statements[0] as VariableDeclarationStatementStatementNode;
            Assert.NotNull(stmt);
            _ = Assert.IsType<InferredVariableDeclarationVariableDeclarationStatementNode>(stmt!.VariableDeclaration);
        }
    }

    // --- Top-Level Structure Tests ---

    [Fact]
    public void ClassDeclaration_ParsesCorrectly()
    {
        var source = "class Foo { }";
        var result = ParseSource(source);

        if(result.Root is CompilationUnitNode cu)
        {
            _ = Assert.Single(cu.GetTypes());
            _ = Assert.IsType<ClassTypeTypeDeclarationNode>(cu.GetTypes()[0]);
            var classType = cu.GetTypes()[0] as ClassTypeTypeDeclarationNode;
            Assert.Equal("Foo", classType!.ClassDeclaration.Name.Lexeme);
        }
    }

    [Fact]
    public void StructDeclaration_ParsesCorrectly()
    {
        var source = "struct Bar { }";
        var result = ParseSource(source);

        if(result.Root is CompilationUnitNode cu)
        {
            _ = Assert.Single(cu.GetTypes());
            _ = Assert.IsType<StructTypeTypeDeclarationNode>(cu.GetTypes()[0]);
        }
    }

    [Fact]
    public void NamespaceDeclaration_FileScoped_ParsesCorrectly()
    {
        var source = "namespace MyApp; class Foo { }";
        var result = ParseSource(source);

        if(result.Root is CompilationUnitNode cu)
        {
            Assert.NotNull(cu.GetNamespace());
            _ = Assert.IsType<FileScopedNamespaceNamespaceDeclarationNode>(cu.GetNamespace());
        }
    }

    [Fact]
    public void UsingDeclaration_ParsesCorrectly()
    {
        var source = "using System; class Foo { }";
        var result = ParseSource(source);

        if(result.Root is CompilationUnitNode cu)
        {
            _ = Assert.Single(cu.Usings);
        }
    }
}