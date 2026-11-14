// Feature: variadics — Unit tests for grammar parsing of variadic surface forms (Task 1.4)
// **Validates: Requirements 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7, 1.8**

using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;

namespace Mist.Tests.Parsing;

public class VariadicsGrammarParsingTests
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

    private static CompilationUnitNode ParseOrFail(string source)
    {
        var result = ParseSource(source);
        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.True(errors.Count == 0,
            $"Unexpected parse errors:\n  {string.Join("\n  ", errors.Select(e => e.Message))}\nSource:\n{source}");
        return Assert.IsType<CompilationUnitNode>(result.Root);
    }

    private static TypeBodyDeclarationNode GetTypeBody(CompilationUnitNode cu, int index = 0)
    {
        var types = cu.GetTypes();
        Assert.True(types.Length > index, $"Expected at least {index + 1} type declaration(s).");
        var bodyType = Assert.IsType<BodyTypeTypeDeclarationNode>(types[index]);
        return bodyType.TypeBody;
    }

    private static TypeParameterListNode GetTypeParams(CompilationUnitNode cu)
    {
        var typeParams = GetTypeBody(cu).TypeParams;
        Assert.NotNull(typeParams);
        return typeParams!;
    }

    private static MethodDeclarationNode GetFirstMethod(CompilationUnitNode cu)
    {
        var member = Assert.IsType<MethodMemberMemberDeclarationNode>(GetTypeBody(cu).Members[0]);
        return member.Method;
    }

    private static FieldDeclarationNode GetFirstField(CompilationUnitNode cu)
    {
        var member = Assert.IsType<FieldMemberMemberDeclarationNode>(GetTypeBody(cu).Members[0]);
        return member.Field;
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

    [Fact]
    public void ParamsPack_TypeParameter_ProducesParamsPackNode()
    {
        var cu = ParseOrFail("class Foo<params TArgs> { }");

        var pack = Assert.IsType<ParamsPackTypeParameterTypeParameterNode>(GetTypeParams(cu).TypeParameter);
        Assert.Equal("TArgs", pack.Name.Lexeme);
    }

    [Fact]
    public void ExplicitUnsizedPack_TypeParameter_ProducesExplicitPackNodeWithUnsizedRank()
    {
        var cu = ParseOrFail("class Foo<Type[]:TArgs> { }");

        var pack = Assert.IsType<ExplicitPackTypeParameterTypeParameterNode>(GetTypeParams(cu).TypeParameter);
        Assert.Equal("Type", pack.DomainName.Lexeme);
        Assert.Equal("TArgs", pack.Name.Lexeme);
        _ = Assert.IsType<UnsizedArrayRankArrayRankSuffixNode>(pack.ArrayRank);
    }

    [Fact]
    public void ExplicitFixedSizePack_TypeParameter_ProducesExplicitPackNodeWithSizedRank()
    {
        var cu = ParseOrFail("class Foo<Type[2]:TArgs> { }");

        var pack = Assert.IsType<ExplicitPackTypeParameterTypeParameterNode>(GetTypeParams(cu).TypeParameter);
        Assert.Equal("Type", pack.DomainName.Lexeme);
        Assert.Equal("TArgs", pack.Name.Lexeme);
        var sized = Assert.IsType<SizedArrayRankArrayRankSuffixNode>(pack.ArrayRank);
        Assert.Equal("2", sized.Size.Lexeme);
    }

    [Fact]
    public void Spread_InTypeArgumentList_ProducesSpreadSingleTypeRef()
    {
        var cu = ParseOrFail("class C<params TArgs> { Holder<TArgs...> h; }");

        var named = Assert.IsType<NamedSingleTypeRefSingleTypeRefNode>(GetFirstField(cu).Type.First);
        Assert.NotNull(named.TypeArgs);
        var arg = Assert.IsType<TypeReferenceTypeArgumentTypeArgumentNode>(named.TypeArgs!.TypeArgument);
        var spread = Assert.IsType<SpreadSingleTypeRefSingleTypeRefNode>(arg.Type.First);
        Assert.Equal("TArgs", spread.Name.IDENTIFIER.Lexeme);
    }

    [Fact]
    public void Spread_InMemberDeclarationType_ProducesSpreadSingleTypeRef()
    {
        var cu = ParseOrFail("class Bundle<params TArgs> { public TArgs... Item; }");

        var field = GetFirstField(cu);
        var spread = Assert.IsType<SpreadSingleTypeRefSingleTypeRefNode>(field.Type.First);
        Assert.Equal("TArgs", spread.Name.IDENTIFIER.Lexeme);
        Assert.Equal("Item", field.Name.Lexeme);
    }

    [Fact]
    public void Spread_InBaseTypeList_SetsBaseTypeRefSpreadToken()
    {
        var cu = ParseOrFail("class Foo<params TArgs> : TArgs... { }");

        var baseList = GetTypeBody(cu).BaseList;
        Assert.NotNull(baseList);
        var baseRef = baseList!.First;
        _ = Assert.NotNull(baseRef.Spread);
        Assert.Equal("TArgs", baseRef.Name.IDENTIFIER.Lexeme);
        Assert.Null(baseRef.TypeArgs);
    }

    [Fact]
    public void Spread_InTupleTypeElementList_ProducesSpreadSingleTypeRef()
    {
        var cu = ParseOrFail("class C<params TArgs> { (int, TArgs...) M() { } }");

        var tuple = Assert.IsType<TupleSingleTypeRefSingleTypeRefNode>(GetFirstMethod(cu).ReturnType.First);
        var spread = Assert.IsType<SpreadSingleTypeRefSingleTypeRefNode>(tuple.Rest.Type.First);
        Assert.Equal("TArgs", spread.Name.IDENTIFIER.Lexeme);
    }

    [Fact]
    public void Spread_InArgumentList_ProducesSpreadSuffix()
    {
        var cu = ParseOrFail("class C<params TArgs> { void M(params TArgs args) { Consume(args...); } }");

        var spreadSuffix = FindNode<SpreadSuffixPostfixSuffixNode>(GetFirstMethod(cu));
        Assert.NotNull(spreadSuffix);
    }

    [Fact]
    public void ValuePack_Parameter_SetsParamsModifierWithPackType()
    {
        var cu = ParseOrFail("class C<params TArgs> { void M(params TArgs args) { } }");

        var method = GetFirstMethod(cu);
        Assert.NotNull(method.Parameters);
        var param = method.Parameters!.Parameter;
        _ = Assert.NotNull(param.ParamsModifier);
        var named = Assert.IsType<NamedSingleTypeRefSingleTypeRefNode>(param.Type.First);
        Assert.Equal("TArgs", named.Name.IDENTIFIER.Lexeme);
        Assert.Equal("args", param.Name.Lexeme);
    }

    [Fact]
    public void ValueLevelParams_Parameter_SetsParamsModifierWithArrayType()
    {
        var cu = ParseOrFail("class C { void M(params string[] parts) { } }");

        var method = GetFirstMethod(cu);
        Assert.NotNull(method.Parameters);
        var param = method.Parameters!.Parameter;
        _ = Assert.NotNull(param.ParamsModifier);
        var prim = Assert.IsType<PrimitiveSingleTypeRefSingleTypeRefNode>(param.Type.First);
        _ = Assert.IsType<StringTypePrimitiveTypeNode>(prim.PrimitiveType);
        _ = Assert.IsType<UnsizedArrayRankArrayRankSuffixNode>(prim.ArrayRank);
        Assert.Equal("parts", param.Name.Lexeme);
    }

    [Fact]
    public void MethodLevelPack_And_ValuePack_AreDisambiguatedByPosition()
    {
        var cu = ParseOrFail("class C { void M<params TArgs>(params TArgs args) { } }");

        var method = GetFirstMethod(cu);

        Assert.NotNull(method.TypeParams);
        var typePack = Assert.IsType<ParamsPackTypeParameterTypeParameterNode>(method.TypeParams!.TypeParameter);
        Assert.Equal("TArgs", typePack.Name.Lexeme);

        Assert.NotNull(method.Parameters);
        _ = Assert.NotNull(method.Parameters!.Parameter.ParamsModifier);
    }

    [Fact]
    public void FlatTypeArgumentList_ProducesFlatTypeReferenceArguments()
    {
        var cu = ParseOrFail("class C { Foo<int, string> f; }");

        var named = Assert.IsType<NamedSingleTypeRefSingleTypeRefNode>(GetFirstField(cu).Type.First);
        Assert.NotNull(named.TypeArgs);

        _ = Assert.IsType<TypeReferenceTypeArgumentTypeArgumentNode>(named.TypeArgs!.TypeArgument);

        // Second argument `string` is also a flat type reference.
        _ = Assert.Single(named.TypeArgs.TypeArguments);
        _ = Assert.IsType<TypeReferenceTypeArgumentTypeArgumentNode>(named.TypeArgs.TypeArguments[0]);
    }

    [Fact]
    public void BracketedPack_TypeArgument_ProducesBracketedPackNodeWithElements()
    {
        var cu = ParseOrFail("class C { Foo<[int, string]> f; }");

        var named = Assert.IsType<NamedSingleTypeRefSingleTypeRefNode>(GetFirstField(cu).Type.First);
        Assert.NotNull(named.TypeArgs);
        var bracketed = Assert.IsType<BracketedPackTypeArgumentTypeArgumentNode>(named.TypeArgs!.TypeArgument);
        Assert.NotNull(bracketed.PackElements);

        // Two elements: the first TypeReference plus one more.
        _ = Assert.Single(bracketed.PackElements!.TypeReferences);
    }

    [Fact]
    public void EmptyBracketedPack_TypeArgument_ProducesBracketedPackNodeWithNullElements()
    {
        var cu = ParseOrFail("class C { Foo<[]> f; }");

        var named = Assert.IsType<NamedSingleTypeRefSingleTypeRefNode>(GetFirstField(cu).Type.First);
        Assert.NotNull(named.TypeArgs);
        var bracketed = Assert.IsType<BracketedPackTypeArgumentTypeArgumentNode>(named.TypeArgs!.TypeArgument);
        Assert.Null(bracketed.PackElements);
    }

    // The at-most-one `params` check (MIST0404) belongs to the program model, not the parser.
    [Fact]
    public void MultipleParams_InTypeParameterList_ParsesWithoutParseError()
    {
        var cu = ParseOrFail("class Foo<params A, params B> { }");

        var typeParams = GetTypeParams(cu);
        _ = Assert.IsType<ParamsPackTypeParameterTypeParameterNode>(typeParams.TypeParameter);
        _ = Assert.Single(typeParams.TypeParameters);
        _ = Assert.IsType<ParamsPackTypeParameterTypeParameterNode>(typeParams.TypeParameters[0]);
    }

    [Fact]
    public void MultipleParams_InValueParameterList_ParsesWithoutParseError()
    {
        var cu = ParseOrFail("class C<params TArgs> { void M(params TArgs a, params TArgs b) { } }");

        var method = GetFirstMethod(cu);
        Assert.NotNull(method.Parameters);
        _ = Assert.NotNull(method.Parameters!.Parameter.ParamsModifier);
        _ = Assert.Single(method.Parameters.Parameters);
        _ = Assert.NotNull(method.Parameters.Parameters[0].ParamsModifier);
    }
}