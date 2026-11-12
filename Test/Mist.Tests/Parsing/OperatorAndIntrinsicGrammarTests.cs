using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;

namespace Mist.Tests.Parsing;

/// The grammar accepts a <c>;</c> body on any operator, method or constructor; whether one is legal there is a semantic check.
/// **Validates: Requirements 4.1, 4.3, 4.4, 4.5, 5.1, 5.2, 5.11**
public class OperatorAndIntrinsicGrammarTests
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

    private static TypeBodyDeclarationNode? GetStructDeclaration(CompilationUnitNode cu)
    {
        var types = cu.GetTypes();
        if(types.Length == 0)
        {
            return null;
        }

        return (types[0] as BodyTypeTypeDeclarationNode)?.TypeBody;
    }

    private static TypeBodyDeclarationNode? GetClassDeclaration(CompilationUnitNode cu)
    {
        var types = cu.GetTypes();
        if(types.Length == 0)
        {
            return null;
        }

        return (types[0] as BodyTypeTypeDeclarationNode)?.TypeBody;
    }

    [Fact]
    public void Task2_1_IntrinsicModifier_AllowedAtTypeLevel_OnStruct()
    {
        var source = "intrinsic public struct Int32 { }";
        var result = ParseSource(source);

        Assert.Empty(result.Diagnostics);

        if(result.Root is not CompilationUnitNode cu)
        {
            return; // skeletal parser guard, see existing convention
        }

        var structDecl = GetStructDeclaration(cu);
        Assert.NotNull(structDecl);
        Assert.Equal("Int32", structDecl!.Name.Lexeme);

        Assert.Contains(structDecl.Modifiers, m => m is IntrinsicModifierModifierNode);
        Assert.Contains(structDecl.Modifiers, m => m is PublicModifierModifierNode);
    }

    [Fact]
    public void Task2_1_IntrinsicModifier_AllowedAtMemberLevel_OnOperator()
    {
        var source = "intrinsic public struct Int32 { " +
                     "intrinsic public static Int32 operator +(Int32 a, Int32 b); }";
        var result = ParseSource(source);

        Assert.Empty(result.Diagnostics);

        if(result.Root is not CompilationUnitNode cu)
        {
            return;
        }

        var structDecl = GetStructDeclaration(cu);
        Assert.NotNull(structDecl);
        _ = Assert.Single(structDecl!.Members);

        var opMember = structDecl.Members[0] as OperatorMemberMemberDeclarationNode;
        Assert.NotNull(opMember);
        var opDecl = opMember!.Operator;
        Assert.Contains(opDecl.Modifiers, m => m is IntrinsicModifierModifierNode);
        Assert.Contains(opDecl.Modifiers, m => m is PublicModifierModifierNode);
        Assert.Contains(opDecl.Modifiers, m => m is StaticModifierModifierNode);
    }

    [Fact]
    public void Task3_1_BinaryOperatorWithBlockBody_Parses()
    {
        var source =
            "public struct Vec { " +
            "public static Int32 operator +(Int32 a, Int32 b) { return a; } " +
            "}";
        var result = ParseSource(source);

        Assert.Empty(result.Diagnostics);

        if(result.Root is not CompilationUnitNode cu)
        {
            return;
        }

        var structDecl = GetStructDeclaration(cu);
        Assert.NotNull(structDecl);
        var opMember = structDecl!.Members[0] as OperatorMemberMemberDeclarationNode;
        Assert.NotNull(opMember);
        var opDecl = opMember!.Operator;

        _ = Assert.IsType<PlusOpOperatorSymbolNode>(opDecl.Symbol);

        _ = Assert.IsType<CallableBlockBodyCallableBodyNode>(opDecl.Body);

        Assert.NotNull(opDecl.Parameters);
        var paramCount = 1 + opDecl.Parameters!.Parameters.Length;
        Assert.Equal(2, paramCount);
    }

    [Fact]
    public void Task3_1_UnaryOperatorWithBlockBody_Parses()
    {
        var source =
            "public struct Vec { " +
            "public static Int32 operator -(Int32 a) { return a; } " +
            "}";
        var result = ParseSource(source);

        Assert.Empty(result.Diagnostics);

        if(result.Root is not CompilationUnitNode cu)
        {
            return;
        }

        var structDecl = GetStructDeclaration(cu);
        Assert.NotNull(structDecl);
        var opMember = structDecl!.Members[0] as OperatorMemberMemberDeclarationNode;
        Assert.NotNull(opMember);
        var opDecl = opMember!.Operator;

        _ = Assert.IsType<MinusOpOperatorSymbolNode>(opDecl.Symbol);
        _ = Assert.IsType<CallableBlockBodyCallableBodyNode>(opDecl.Body);

        Assert.NotNull(opDecl.Parameters);
        var paramCount = 1 + opDecl.Parameters!.Parameters.Length;
        Assert.Equal(1, paramCount);
    }

    [Theory]
    [InlineData("+", typeof(PlusOpOperatorSymbolNode))]
    [InlineData("-", typeof(MinusOpOperatorSymbolNode))]
    [InlineData("*", typeof(StarOpOperatorSymbolNode))]
    [InlineData("/", typeof(SlashOpOperatorSymbolNode))]
    [InlineData("%", typeof(ModuloOpOperatorSymbolNode))]
    [InlineData("==", typeof(EqualEqualOpOperatorSymbolNode))]
    [InlineData("!=", typeof(NotEqualOpOperatorSymbolNode))]
    [InlineData("<", typeof(LessThanOpOperatorSymbolNode))]
    [InlineData(">", typeof(GreaterThanOpOperatorSymbolNode))]
    [InlineData("<=", typeof(LessThanOrEqualOpOperatorSymbolNode))]
    [InlineData(">=", typeof(GreaterThanOrEqualOpOperatorSymbolNode))]
    [InlineData("&&", typeof(LogicalAndOpOperatorSymbolNode))]
    [InlineData("||", typeof(LogicalOrOpOperatorSymbolNode))]
    [InlineData("!", typeof(ExclamationOpOperatorSymbolNode))]
    public void Task3_1_AllFourteenOperatorSymbols_ParseToCorrectVariant(string opText, Type expectedVariant)
    {
        var source =
            "public struct S { " +
            $"public static S operator {opText}(S a, S b); " +
            "}";
        var result = ParseSource(source);

        Assert.Empty(result.Diagnostics);

        if(result.Root is not CompilationUnitNode cu)
        {
            return;
        }

        var structDecl = GetStructDeclaration(cu);
        Assert.NotNull(structDecl);
        var opMember = structDecl!.Members[0] as OperatorMemberMemberDeclarationNode;
        Assert.NotNull(opMember);
        Assert.IsType(expectedVariant, opMember!.Operator.Symbol);
    }

    [Fact]
    public void Task3_2_OperatorNoBody_Parses()
    {
        var source =
            "public struct S { " +
            "public static S operator +(S a, S b); " +
            "}";
        var result = ParseSource(source);

        Assert.Empty(result.Diagnostics);

        if(result.Root is not CompilationUnitNode cu)
        {
            return;
        }

        var structDecl = GetStructDeclaration(cu);
        Assert.NotNull(structDecl);
        var opMember = structDecl!.Members[0] as OperatorMemberMemberDeclarationNode;
        Assert.NotNull(opMember);
        _ = Assert.IsType<CallableNoBodyCallableBodyNode>(opMember!.Operator.Body);
    }

    [Fact]
    public void Task4_1_IntrinsicOperator_SemicolonBody_Parses()
    {
        var source =
            "intrinsic public struct Int32 { " +
            "intrinsic public static Int32 operator +(Int32 a, Int32 b); " +
            "}";
        var result = ParseSource(source);

        Assert.Empty(result.Diagnostics);

        if(result.Root is not CompilationUnitNode cu)
        {
            return;
        }

        var structDecl = GetStructDeclaration(cu);
        Assert.NotNull(structDecl);
        var opMember = structDecl!.Members[0] as OperatorMemberMemberDeclarationNode;
        Assert.NotNull(opMember);

        _ = Assert.IsType<CallableNoBodyCallableBodyNode>(opMember!.Operator.Body);
        Assert.Contains(opMember.Operator.Modifiers, m => m is IntrinsicModifierModifierNode);
    }

    [Fact]
    public void Task4_1_IntrinsicConstructor_SemicolonBody_Parses()
    {
        var source =
            "intrinsic public struct Int32 { " +
            "intrinsic public Int32(); " +
            "}";
        var result = ParseSource(source);

        Assert.Empty(result.Diagnostics);

        if(result.Root is not CompilationUnitNode cu)
        {
            return;
        }

        var structDecl = GetStructDeclaration(cu);
        Assert.NotNull(structDecl);
        var ctorMember = structDecl!.Members[0] as ConstructorMemberMemberDeclarationNode;
        Assert.NotNull(ctorMember);

        _ = Assert.IsType<CallableNoBodyCallableBodyNode>(ctorMember!.Constructor.Body);
        Assert.Contains(ctorMember.Constructor.Modifiers, m => m is IntrinsicModifierModifierNode);
    }

    [Fact]
    public void Task4_1_OrdinaryMethodBlockBody_StillParses()
    {
        var source = "public class T { public void M() { } }";
        var result = ParseSource(source);

        Assert.Empty(result.Diagnostics);

        if(result.Root is not CompilationUnitNode cu)
        {
            return;
        }

        var classDecl = GetClassDeclaration(cu);
        Assert.NotNull(classDecl);
        var methodMember = classDecl!.Members[0] as MethodMemberMemberDeclarationNode;
        Assert.NotNull(methodMember);
        _ = Assert.IsType<CallableBlockBodyCallableBodyNode>(methodMember!.Method.Body);
    }
}