using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;

namespace Mist.Tests.Parsing;

/// Requirements: 1.1–1.7, 7.1–7.3
public class ReadonlyLocalParsingTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    private static ParseResult ParseSource(string source)
    {
        var sourceText = new Mist.Common.Source.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        return Parser.Parse(tokens, bag);
    }

    private static StatementNode GetFirstStatement(ParseResult result)
    {
        var cu = (CompilationUnitNode)result.Root!;
        var typeDecl = (BodyTypeTypeDeclarationNode)cu.GetTypes()[0];
        var method = ((MethodMemberMemberDeclarationNode)typeDecl.TypeBody.Members[0]).Method;
        return method.Body.GetBlock()!.Statements[0];
    }

    [Fact]
    public void ReadonlyVar_ParsesAsInferredVariableDeclaration_WithReadonlyModifier()
    {
        var source = "class T { public void M() { readonly var x = 7; } }";
        var result = ParseSource(source);

        Assert.Empty(result.Diagnostics);

        var stmt = Assert.IsType<VariableDeclarationStatementStatementNode>(GetFirstStatement(result));
        var inferred = Assert.IsType<InferredVariableDeclarationVariableDeclarationStatementNode>(stmt.VariableDeclaration);

        _ = Assert.NotNull(inferred.ReadonlyModifier);
        Assert.Equal("x", inferred.Name.Lexeme);
    }

    [Fact]
    public void ReadonlyTyped_ParsesAsTypedVariableDeclaration_WithReadonlyModifier()
    {
        var source = "class T { public void M() { readonly int x = 7; } }";
        var result = ParseSource(source);

        Assert.Empty(result.Diagnostics);

        var stmt = Assert.IsType<VariableDeclarationStatementStatementNode>(GetFirstStatement(result));
        var typed = Assert.IsType<TypedVariableDeclarationVariableDeclarationStatementNode>(stmt.VariableDeclaration);

        _ = Assert.NotNull(typed.ReadonlyModifier);
        Assert.Equal("x", typed.Name.Lexeme);
    }

    [Fact]
    public void ReadonlyVarTuple_ParsesAsVarTupleDeconstruction_WithReadonlyModifier()
    {
        var source = "class T { public void M() { readonly var (a, b) = (1, 2); } }";
        var result = ParseSource(source);

        Assert.Empty(result.Diagnostics);

        var stmt = Assert.IsType<VariableDeclarationStatementStatementNode>(GetFirstStatement(result));
        var varTuple = Assert.IsType<VarTupleDeconstructionVariableDeclarationStatementNode>(stmt.VariableDeclaration);

        _ = Assert.NotNull(varTuple.ReadonlyModifier);
        Assert.Equal("a", varTuple.FirstName.Lexeme);
        _ = Assert.Single(varTuple.RestNames);
        Assert.Equal("b", varTuple.RestNames[0].Lexeme);
    }

    [Fact]
    public void ReadonlyTypedTuple_ParsesAsTypedTupleDeconstruction_WithReadonlyModifier()
    {
        var source = """class T { public void M() { readonly (int a, string b) = (1, "hi"); } }""";
        var result = ParseSource(source);

        Assert.Empty(result.Diagnostics);

        var stmt = Assert.IsType<VariableDeclarationStatementStatementNode>(GetFirstStatement(result));
        var typedTuple = Assert.IsType<TypedTupleDeconstructionVariableDeclarationStatementNode>(stmt.VariableDeclaration);

        _ = Assert.NotNull(typedTuple.ReadonlyModifier);
        Assert.Equal("a", typedTuple.First.Name.Lexeme);
        Assert.Equal("b", typedTuple.Rest.Name.Lexeme);
    }

    [Fact]
    public void ReadonlyOwned_ParsesWithBothModifierAndOwnership()
    {
        var source = "class T { public void M() { readonly owned var x = 7; } }";
        var result = ParseSource(source);

        Assert.Empty(result.Diagnostics);

        var stmt = Assert.IsType<VariableDeclarationStatementStatementNode>(GetFirstStatement(result));
        var inferred = Assert.IsType<InferredVariableDeclarationVariableDeclarationStatementNode>(stmt.VariableDeclaration);

        _ = Assert.NotNull(inferred.ReadonlyModifier);
        _ = Assert.IsType<OwnedSpecifierOwnershipSpecifierNode>(inferred.OwnershipSpecifier);
        Assert.Equal("x", inferred.Name.Lexeme);
    }

    [Fact]
    public void ReadonlyBlock_StillParsesCorrectly_NoRegression()
    {
        var source = "class T { public void M() { readonly { int y = 1; } } }";
        var result = ParseSource(source);

        Assert.Empty(result.Diagnostics);

        var stmt = Assert.IsType<ReadonlyBlockStatementStatementNode>(GetFirstStatement(result));
        Assert.NotNull(stmt.ReadonlyBlock.Body);
        _ = Assert.Single(stmt.ReadonlyBlock.Body.Statements);
    }

    [Fact]
    public void NonReadonlyVar_HasNullReadonlyModifier()
    {
        var source = "class T { public void M() { var x = 7; } }";
        var result = ParseSource(source);

        Assert.Empty(result.Diagnostics);

        var stmt = Assert.IsType<VariableDeclarationStatementStatementNode>(GetFirstStatement(result));
        var inferred = Assert.IsType<InferredVariableDeclarationVariableDeclarationStatementNode>(stmt.VariableDeclaration);

        Assert.Null(inferred.ReadonlyModifier);
    }
}