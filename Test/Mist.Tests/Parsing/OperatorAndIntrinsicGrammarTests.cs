using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;

namespace Mist.Tests.Parsing;

/// <summary>
/// Unit tests for the grammar extensions introduced by the Mist Standard
/// Library spec (Tasks 2, 3, 4):
///
/// * Task 2 — <c>Modifier</c> gains <c>intrinsic</c>, so it can appear at
///   both type level and member level (Requirements 5.1, 5.11).
/// * Task 3 — <c>OperatorDeclaration</c> is a new member shape:
///   <c>&lt;modifiers&gt; &lt;returnType&gt; operator &lt;symbol&gt;(&lt;params&gt;) &lt;body&gt;</c>.
///   Body is either a block or a trailing <c>;</c>. Fourteen operator symbols
///   are accepted (Requirements 4.1, 4.3, 4.4, 4.5).
/// * Task 4 — <c>MethodDeclaration</c> and <c>ConstructorDeclaration</c>
///   bodies may now be a block <strong>or</strong> a single <c>;</c>
///   (Requirement 5.2). Whether a <c>;</c>-body is valid on a given
///   declaration is a semantic check, not a grammar check.
/// </summary>
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

    // ─── Task 2.1 — intrinsic modifier at type level and member level ──────

    [Fact]
    public void Task2_1_IntrinsicModifier_AllowedAtTypeLevel_OnStruct()
    {
        // Requirement 5.11: `intrinsic public struct Int32 { ... }` is accepted;
        // the struct's modifier list contains an IntrinsicModifier node.
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

        // The modifier list contains both Intrinsic and Public modifiers.
        Assert.Contains(structDecl.Modifiers, m => m is IntrinsicModifierModifierNode);
        Assert.Contains(structDecl.Modifiers, m => m is PublicModifierModifierNode);
    }

    [Fact]
    public void Task2_1_IntrinsicModifier_AllowedAtMemberLevel_OnOperator()
    {
        // Requirement 5.1: member-level `intrinsic` is accepted as an operator
        // modifier. The member parse produces an OperatorDeclaration whose
        // modifier list contains an IntrinsicModifier node.
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

    // ─── Task 3.1 — OperatorDeclaration binary and unary with block body ───

    [Fact]
    public void Task3_1_BinaryOperatorWithBlockBody_Parses()
    {
        // Requirement 4.1, 4.3: a binary operator with a block body is
        // accepted and produces an OperatorDeclaration with PlusOp.
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

        // Symbol is PlusOp.
        _ = Assert.IsType<PlusOpOperatorSymbolNode>(opDecl.Symbol);

        // Body is a block body.
        _ = Assert.IsType<CallableBlockBodyCallableBodyNode>(opDecl.Body);

        // Parameter list has two parameters.
        Assert.NotNull(opDecl.Parameters);
        var paramCount = 1 + opDecl.Parameters!.Parameters.Length;
        Assert.Equal(2, paramCount);
    }

    [Fact]
    public void Task3_1_UnaryOperatorWithBlockBody_Parses()
    {
        // Requirement 4.4: a unary `-` operator with one parameter is accepted
        // and produces an OperatorDeclaration with MinusOp and a single-parameter
        // list. Parameter count disambiguates binary vs unary at the semantic
        // level; the grammar itself uses the same rule.
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
        // Requirements 4.3, 4.4, 4.5: all 14 operator symbols are grammar-accepted
        // and produce the corresponding OperatorSymbol variant node.
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

    // ─── Task 3.2 — OperatorDeclaration with no-body (semicolon-terminated) ─

    [Fact]
    public void Task3_2_OperatorNoBody_Parses()
    {
        // Requirements 4.1, 5.2: `operator +(...);` is grammar-accepted; the
        // OperatorBody variant is OperatorNoBody (a bare `;`, no block).
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

    // ─── Task 4.1 — Method and Constructor bodies accept `;` ────────────────

    [Fact]
    public void Task4_1_IntrinsicOperator_SemicolonBody_Parses()
    {
        // Requirement 5.2: an intrinsic operator is written with a trailing
        // `;` where a block body would normally go. The grammar accepts it;
        // the semantic model later checks that the `;` form is only valid on
        // an intrinsic declaration.
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
        // Requirement 5.2: an intrinsic constructor uses a trailing `;` in
        // place of a block. The grammar accepts it; the resulting
        // ConstructorDeclaration has a MethodNoBody body variant.
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
        // Regression guard: the common case of an ordinary method with a
        // block body still parses and the body variant is MethodBlockBody
        // (wrapping a BlockNode), so existing code paths continue to work.
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