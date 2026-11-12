using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;

namespace Mist.Tests.Parsing;

/// <summary>
/// Validates: Requirements 1.3, 1.4 of the unknown-type feature.
/// </summary>
public class UnknownTypeParserTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    private static (CompilationUnitNode? Root, bool HasErrors) ParseSource(string source)
    {
        var sourceText = new Mist.Common.Source.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var result = Parser.Parse(tokens, bag);

        var hasErrors = bag.ToImmutableArray()
            .Any(d => d.Severity == DiagnosticSeverity.Error);

        return (result.Root as CompilationUnitNode, hasErrors);
    }

    private static TypedVariableDeclarationVariableDeclarationStatementNode GetFirstLocalDecl(CompilationUnitNode cu)
    {
        var typeDecl = Assert.IsType<BodyTypeTypeDeclarationNode>(cu.GetTypes()[0]);
        var methodMember = Assert.IsType<MethodMemberMemberDeclarationNode>(typeDecl.TypeBody.Members[0]);
        var block = methodMember.Method.Body.GetBlock();
        Assert.NotNull(block);
        var stmt = Assert.IsType<VariableDeclarationStatementStatementNode>(block!.Statements[0]);
        return Assert.IsType<TypedVariableDeclarationVariableDeclarationStatementNode>(stmt.VariableDeclaration);
    }

    private static IReadOnlyList<TypeReferenceNode> GetTypeArgs(TypeArgumentListNode list)
    {
        var result = new List<TypeReferenceNode> { list.GetTypeReference() };
        result.AddRange(list.GetTypeReferences());
        return result;
    }

    [Fact]
    public void Parses_Unknown_As_Local_Variable_Type()
    {
        var source = "class T { public void M() { unknown x = 1; } }";
        var (cu, hasErrors) = ParseSource(source);

        Assert.False(hasErrors);
        Assert.NotNull(cu);

        var varDecl = GetFirstLocalDecl(cu!);
        _ = Assert.IsType<UnknownSingleTypeRefSingleTypeRefNode>(varDecl.Type.First);
    }

    [Fact]
    public void Parses_Unknown_As_Type_Argument_In_List()
    {
        var source = "class T { public void M() { List<unknown> xs = 0; } }";
        var (cu, hasErrors) = ParseSource(source);

        Assert.False(hasErrors);
        Assert.NotNull(cu);

        var varDecl = GetFirstLocalDecl(cu!);
        var named = Assert.IsType<NamedSingleTypeRefSingleTypeRefNode>(varDecl.Type.First);
        Assert.NotNull(named.TypeArgs);
        var typeArgs = GetTypeArgs(named.TypeArgs!);
        _ = Assert.Single(typeArgs);
        _ = Assert.IsType<UnknownSingleTypeRefSingleTypeRefNode>(typeArgs[0].First);
    }

    [Fact]
    public void Parses_Unknown_In_Map_Second_Type_Argument()
    {
        var source = "class T { public void M() { Map<string, unknown> m = 0; } }";
        var (cu, hasErrors) = ParseSource(source);

        Assert.False(hasErrors);
        Assert.NotNull(cu);

        var varDecl = GetFirstLocalDecl(cu!);
        var named = Assert.IsType<NamedSingleTypeRefSingleTypeRefNode>(varDecl.Type.First);
        Assert.NotNull(named.TypeArgs);
        var typeArgs = GetTypeArgs(named.TypeArgs!);
        Assert.Equal(2, typeArgs.Count);

        var firstPrim = Assert.IsType<PrimitiveSingleTypeRefSingleTypeRefNode>(typeArgs[0].First);
        _ = Assert.IsType<StringTypePrimitiveTypeNode>(firstPrim.PrimitiveType);

        _ = Assert.IsType<UnknownSingleTypeRefSingleTypeRefNode>(typeArgs[1].First);
    }

    [Fact]
    public void Parses_Nested_Unknown_In_Optional_List()
    {
        var source = "class T { public void M() { Optional<List<unknown>> o = 0; } }";
        var (cu, hasErrors) = ParseSource(source);

        Assert.False(hasErrors);
        Assert.NotNull(cu);

        var varDecl = GetFirstLocalDecl(cu!);
        var outer = Assert.IsType<NamedSingleTypeRefSingleTypeRefNode>(varDecl.Type.First);
        Assert.NotNull(outer.TypeArgs);
        var outerArgs = GetTypeArgs(outer.TypeArgs!);
        _ = Assert.Single(outerArgs);

        var inner = Assert.IsType<NamedSingleTypeRefSingleTypeRefNode>(outerArgs[0].First);
        Assert.NotNull(inner.TypeArgs);
        var innerArgs = GetTypeArgs(inner.TypeArgs!);
        _ = Assert.Single(innerArgs);

        _ = Assert.IsType<UnknownSingleTypeRefSingleTypeRefNode>(innerArgs[0].First);
    }

    // The parser does not report every syntactic failure: a failed alternative backtracks silently and
    // the enclosing construct is not produced. Each negative test therefore asserts that construct is
    // absent where a non-keyword analog produces it.

    [Fact]
    public void Rejects_Class_Named_Unknown()
    {
        var (cuInvalid, _) = ParseSource("class unknown { }");
        Assert.NotNull(cuInvalid);
        Assert.Empty(cuInvalid!.GetTypes());

        var (cuValid, _) = ParseSource("class Foo { }");
        Assert.NotNull(cuValid);
        _ = Assert.Single(cuValid!.GetTypes());
    }

    [Fact]
    public void Rejects_Local_Variable_Named_Unknown()
    {
        var (cuInvalid, _) = ParseSource("class T { public void M() { var unknown = 1; } }");
        Assert.NotNull(cuInvalid);
        Assert.Empty(cuInvalid!.GetTypes());

        var (cuValid, _) = ParseSource("class T { public void M() { var x = 1; } }");
        Assert.NotNull(cuValid);
        _ = Assert.Single(cuValid!.GetTypes());
    }

    [Fact]
    public void Rejects_Method_Named_Unknown()
    {
        var (cuInvalid, _) = ParseSource("class T { public void unknown() { } }");
        Assert.NotNull(cuInvalid);
        Assert.Empty(cuInvalid!.GetTypes());

        var (cuValid, _) = ParseSource("class T { public void M() { } }");
        Assert.NotNull(cuValid);
        _ = Assert.Single(cuValid!.GetTypes());
    }

    [Fact]
    public void Accepts_Namespace_Named_Unknown()
    {
        var (cuUnknown, _) = ParseSource("namespace unknown;");
        Assert.NotNull(cuUnknown);
        Assert.NotNull(cuUnknown!.GetNamespace());

        var (cuValid, _) = ParseSource("namespace Geometry;");
        Assert.NotNull(cuValid);
        Assert.NotNull(cuValid!.GetNamespace());
    }

    [Fact]
    public void Rejects_Generic_Parameter_Named_Unknown()
    {
        var (cuInvalid, _) = ParseSource("class Foo<unknown> { }");
        Assert.NotNull(cuInvalid);
        Assert.Empty(cuInvalid!.GetTypes());

        var (cuValid, _) = ParseSource("class Foo<T> { }");
        Assert.NotNull(cuValid);
        _ = Assert.Single(cuValid!.GetTypes());
    }
}