using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;

namespace Mist.Tests.Parsing;

/// <summary>
/// Parse-level smoke tests for the new structured context grammar rules.
/// Verifies that each new with-statement variant produces the correct AST node type.
///
/// Requirements: 1.5, 1.6, 1.7, 1.8, 2.3, 3.3, 13.1, 13.3
/// </summary>
public class WithStatementParsingTests
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

    /// <summary>
    /// Helper to extract the first statement from a method body inside a class.
    /// </summary>
    private static StatementNode? GetFirstStatement(ParseResult result)
    {
        if (result.Root is not CompilationUnitNode cu) return null;
        var typeDecl = cu.GetTypes()[0] as ClassTypeTypeDeclarationNode;
        if (typeDecl is null) return null;
        var methodMember = typeDecl.ClassDeclaration.Members[0] as MethodMemberMemberDeclarationNode;
        if (methodMember is null) return null;
        var block = methodMember.Method.Body.GetBlock();
        if (block is null || block.Statements.Length == 0) return null;
        return block.Statements[0];
    }

    // ─── Test 1: with var x = someExpr; ─────────────────────────────────

    [Fact]
    public void WithVarDeclaration_ParsesCorrectly()
    {
        var source = "class T { public void M() { with var x = 42; } }";
        var result = ParseSource(source);

        Assert.Empty(result.Diagnostics);
        var stmt = GetFirstStatement(result);
        var withVarStmt = Assert.IsType<WithVarDeclarationStatementStatementNode>(stmt);
        Assert.Equal("x", withVarStmt.WithVarDeclaration.Name.Lexeme);
        Assert.NotNull(withVarStmt.WithVarDeclaration.Initializer);
    }

    [Fact]
    public void WithVarDeclaration_WithComplexExpression_ParsesCorrectly()
    {
        var source = "class T { public void M() { with var ctx = new MyContext(); } }";
        var result = ParseSource(source);

        Assert.Empty(result.Diagnostics);
        var stmt = GetFirstStatement(result);
        Assert.IsType<WithVarDeclarationStatementStatementNode>(stmt);
    }

    // ─── Test 2: with MyType x = someExpr; ──────────────────────────────

    [Fact]
    public void WithTypedDeclaration_ParsesCorrectly()
    {
        var source = "class T { public void M() { with MyType x = 42; } }";
        var result = ParseSource(source);

        Assert.Empty(result.Diagnostics);
        var stmt = GetFirstStatement(result);
        var withTypedStmt = Assert.IsType<WithTypedDeclarationStatementStatementNode>(stmt);
        Assert.Equal("x", withTypedStmt.WithTypedDeclaration.Name.Lexeme);
        Assert.NotNull(withTypedStmt.WithTypedDeclaration.Type);
        Assert.NotNull(withTypedStmt.WithTypedDeclaration.Initializer);
    }

    [Fact]
    public void WithTypedDeclaration_PrimitiveType_ParsesCorrectly()
    {
        var source = "class T { public void M() { with int count = 5; } }";
        var result = ParseSource(source);

        Assert.Empty(result.Diagnostics);
        var stmt = GetFirstStatement(result);
        var withTypedStmt = Assert.IsType<WithTypedDeclarationStatementStatementNode>(stmt);
        Assert.Equal("count", withTypedStmt.WithTypedDeclaration.Name.Lexeme);
        Assert.IsType<PrimitiveSingleTypeRefSingleTypeRefNode>(withTypedStmt.WithTypedDeclaration.Type.First);
    }

    // ─── Test 3: with MyType _ = someExpr; ──────────────────────────────

    [Fact]
    public void WithDiscardDeclaration_ParsesCorrectly()
    {
        var source = "class T { public void M() { with MyType _ = 42; } }";
        var result = ParseSource(source);

        Assert.Empty(result.Diagnostics);
        var stmt = GetFirstStatement(result);
        var withDiscardStmt = Assert.IsType<WithDiscardDeclarationStatementStatementNode>(stmt);
        Assert.NotNull(withDiscardStmt.WithDiscardDeclaration.Type);
        Assert.NotNull(withDiscardStmt.WithDiscardDeclaration.Initializer);
    }

    [Fact]
    public void WithDiscardDeclaration_PrimitiveType_ParsesCorrectly()
    {
        var source = "class T { public void M() { with string _ = \"hello\"; } }";
        var result = ParseSource(source);

        Assert.Empty(result.Diagnostics);
        var stmt = GetFirstStatement(result);
        Assert.IsType<WithDiscardDeclarationStatementStatementNode>(stmt);
    }

    // ─── Test 4: with someExpr; ─────────────────────────────────────────

    [Fact]
    public void WithPushStatement_ParsesCorrectly()
    {
        var source = "class T { public void M() { with someExpr; } }";
        var result = ParseSource(source);

        Assert.Empty(result.Diagnostics);
        var stmt = GetFirstStatement(result);
        var withPushStmt = Assert.IsType<WithPushStatementStatementNode>(stmt);
        Assert.NotNull(withPushStmt.WithPushStatement.Value);
    }

    [Fact]
    public void WithPushStatement_MethodCall_ParsesCorrectly()
    {
        var source = "class T { public void M() { with getContext(); } }";
        var result = ParseSource(source);

        Assert.Empty(result.Diagnostics);
        var stmt = GetFirstStatement(result);
        Assert.IsType<WithPushStatementStatementNode>(stmt);
    }

    // ─── Test 5: with(someExpr) { ... } ─────────────────────────────────

    [Fact]
    public void WithBlockStatement_ParsesCorrectly()
    {
        var source = "class T { public void M() { with(someExpr) { } } }";
        var result = ParseSource(source);

        Assert.Empty(result.Diagnostics);
        var stmt = GetFirstStatement(result);
        var withBlockStmt = Assert.IsType<WithBlockStatementStatementNode>(stmt);
        Assert.NotNull(withBlockStmt.WithBlockStatement.Expression);
        Assert.NotNull(withBlockStmt.WithBlockStatement.Body);
    }

    [Fact]
    public void WithBlockStatement_WithBody_ParsesCorrectly()
    {
        var source = "class T { public void M() { with(ctx) { int x = 1; } } }";
        var result = ParseSource(source);

        Assert.Empty(result.Diagnostics);
        var stmt = GetFirstStatement(result);
        var withBlockStmt = Assert.IsType<WithBlockStatementStatementNode>(stmt);
        Assert.NotNull(withBlockStmt.WithBlockStatement.Body);
        Assert.Single(withBlockStmt.WithBlockStatement.Body.Statements);
    }

    // ─── Test 6: Method with WithClause ─────────────────────────────────

    [Fact]
    public void MethodWithClause_ParsesCorrectly()
    {
        var source = "class T { public void M() with MyType ctx { } }";
        var result = ParseSource(source);

        Assert.Empty(result.Diagnostics);
        if (result.Root is CompilationUnitNode cu)
        {
            var typeDecl = Assert.IsType<ClassTypeTypeDeclarationNode>(cu.GetTypes()[0]);
            var methodMember = Assert.IsType<MethodMemberMemberDeclarationNode>(typeDecl.ClassDeclaration.Members[0]);
            var method = methodMember.Method;

            Assert.Single(method.WithClauses);
            var clause = Assert.IsType<ContextWithClauseWithClauseNode>(method.WithClauses[0]);
            Assert.NotNull(clause.Type);
            Assert.Equal("ctx", clause.Name.Lexeme);
        }
    }

    [Fact]
    public void MethodWithMultipleClauses_ParsesCorrectly()
    {
        var source = "class T { public void M() with MyType ctx with OtherType other { } }";
        var result = ParseSource(source);

        Assert.Empty(result.Diagnostics);
        if (result.Root is CompilationUnitNode cu)
        {
            var typeDecl = Assert.IsType<ClassTypeTypeDeclarationNode>(cu.GetTypes()[0]);
            var methodMember = Assert.IsType<MethodMemberMemberDeclarationNode>(typeDecl.ClassDeclaration.Members[0]);
            var method = methodMember.Method;

            Assert.Equal(2, method.WithClauses.Length);
            Assert.Equal("ctx", method.WithClauses[0] is ContextWithClauseWithClauseNode ctx0 ? ctx0.Name.Lexeme : "");
            Assert.Equal("other", method.WithClauses[1] is ContextWithClauseWithClauseNode ctx1 ? ctx1.Name.Lexeme : "");
        }
    }

    [Fact]
    public void AbstractMethodWithClause_ParsesCorrectly()
    {
        var source = "abstract I { void M() with MyType ctx; }";
        var result = ParseSource(source);

        Assert.Empty(result.Diagnostics);
        if (result.Root is CompilationUnitNode cu)
        {
            var typeDecl = Assert.IsType<AbstractTypeTypeDeclarationNode>(cu.GetTypes()[0]);
            var methodMember = Assert.IsType<AbstractMethodMemberAbstractMemberDeclarationNode>(
                typeDecl.AbstractDeclaration.Members[0]);
            var method = methodMember.Method;

            Assert.Single(method.WithClauses);
            Assert.Equal("ctx", method.WithClauses[0] is ContextWithClauseWithClauseNode ctx0 ? ctx0.Name.Lexeme : "");
        }
    }

    [Fact]
    public void ConstructorWithClause_ParsesCorrectly()
    {
        var source = "class T { T() with MyType ctx { } }";
        var result = ParseSource(source);

        Assert.Empty(result.Diagnostics);
        if (result.Root is CompilationUnitNode cu)
        {
            var typeDecl = Assert.IsType<ClassTypeTypeDeclarationNode>(cu.GetTypes()[0]);
            var ctorMember = Assert.IsType<ConstructorMemberMemberDeclarationNode>(
                typeDecl.ClassDeclaration.Members[0]);
            var ctor = ctorMember.Constructor;

            Assert.Single(ctor.WithClauses);
            Assert.Equal("ctx", ctor.WithClauses[0] is ContextWithClauseWithClauseNode ctx0 ? ctx0.Name.Lexeme : "");
        }
    }

}
