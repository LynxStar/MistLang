// Generators for complete Mist programs (CompilationUnitNode).
// Produces well-typed and ill-typed programs for property-based testing.

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using Mist.Generated;

namespace Mist.Tests.Generators;

/// <summary>
/// FsCheck generators for complete Mist programs (<see cref="CompilationUnitNode"/>).
/// Includes generators for well-typed programs (Property 5) and ill-typed programs (Property 6).
/// </summary>
public static class MistProgramGenerator
{
    private static Storm.Runtime.Token Tok(string lexeme)
    {
        return MistTypeGenerator.MakeToken(lexeme);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Program structure helpers
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates a CompilationUnitNode with a single class containing a static void Main()
    /// entry point. The Main method body contains the given statements.
    /// </summary>
    public static CompilationUnitNode MakeProgram(
        ImmutableArray<StatementNode> mainStatements,
        ImmutableArray<MemberDeclarationNode>? additionalMembers = null)
    {
        var members = ImmutableArray.CreateBuilder<MemberDeclarationNode>();

        if(additionalMembers != null)
        {
            members.AddRange(additionalMembers.Value);
        }

        var mainMethod = new MethodDeclarationNode
        {
            Name = Tok("Main"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = mainStatements } },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };
        members.Add(new MethodMemberMemberDeclarationNode { Method = mainMethod });

        var classDecl = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = Tok("Program"),
            Members = members.ToImmutable(),
        };

        return new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new BodyTypeTypeDeclarationNode { TypeBody = classDecl }).AsTopLevelItems(),
        };
    }

    /// <summary>
    /// Creates a program with a class containing a static void Main() and an additional method.
    /// </summary>
    public static CompilationUnitNode MakeProgramWithMethod(
        string methodName,
        TypeReferenceNode returnType,
        ImmutableArray<StatementNode> methodStatements,
        ImmutableArray<StatementNode>? mainStatements = null,
        ParameterListNode? parameters = null,
        bool isStatic = false)
    {
        var methodModifiers = ImmutableArray.CreateBuilder<ModifierNode>();
        methodModifiers.Add(new PublicModifierModifierNode());
        if(isStatic)
        {
            methodModifiers.Add(new StaticModifierModifierNode());
        }

        var method = new MethodDeclarationNode
        {
            Name = Tok(methodName),
            ReturnType = returnType,
            Parameters = parameters,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = methodStatements } },
            Modifiers = methodModifiers.ToImmutable(),
        };

        var additionalMembers = ImmutableArray.Create<MemberDeclarationNode>(
            new MethodMemberMemberDeclarationNode { Method = method });

        return MakeProgram(
            mainStatements ?? ImmutableArray<StatementNode>.Empty,
            additionalMembers);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Well-typed program generators (Property 5)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a well-typed program with int arithmetic in Main.
    /// Pattern: int a = X; int b = Y; a + b;
    /// </summary>
    public static Gen<CompilationUnitNode> GenWellTypedIntArithmetic()
    {
        return from a in Gen.Choose(0, 1000)
               from b in Gen.Choose(0, 1000)
               select MakeProgram(ImmutableArray.Create(
                   MistAstGenerator.MakeIntVarDecl("a", a.ToString()),
                   MistAstGenerator.MakeIntVarDecl("b", b.ToString()),
                   MistAstGenerator.MakeExpressionStatement(
                       MistAstGenerator.MakeAdditive(
                           MistAstGenerator.MakeIdentifierExpr("a"),
                           MistAstGenerator.MakeIdentifierExpr("b"), "+"))));
    }

    /// <summary>
    /// Generates a well-typed program with bool logic in Main.
    /// Pattern: bool x = V1; bool y = V2; x && y;
    /// </summary>
    public static Gen<CompilationUnitNode> GenWellTypedBoolLogic()
    {
        return from x in Gen.Elements(true, false)
               from y in Gen.Elements(true, false)
               select MakeProgram(ImmutableArray.Create(
                   MistAstGenerator.MakeBoolVarDecl("x", x),
                   MistAstGenerator.MakeBoolVarDecl("y", y),
                   MistAstGenerator.MakeExpressionStatement(
                       MistAstGenerator.MakeLogicalAnd(
                           MistAstGenerator.MakeIdentifierExpr("x"),
                           MistAstGenerator.MakeIdentifierExpr("y")))));
    }

    /// <summary>
    /// Generates a well-typed program with an if statement using a bool condition.
    /// Pattern: bool cond = V; if (cond) { int inner = 0; }
    /// </summary>
    public static Gen<CompilationUnitNode> GenWellTypedIfStatement()
    {
        return Gen.Elements(true, false).Select(cond =>
            MakeProgram(ImmutableArray.Create(
                MistAstGenerator.MakeBoolVarDecl("cond", cond),
                MistAstGenerator.MakeIfStatement(
                    MistAstGenerator.MakeIdentifierExpr("cond"),
                    ImmutableArray.Create(MistAstGenerator.MakeIntVarDecl("inner", "42"))))));
    }

    /// <summary>
    /// Generates a well-typed program with a while loop using a bool condition.
    /// Pattern: bool running = false; while (running) { }
    /// </summary>
    public static Gen<CompilationUnitNode> GenWellTypedWhileStatement()
    {
        return Gen.Constant(
            MakeProgram(ImmutableArray.Create(
                MistAstGenerator.MakeBoolVarDecl("running", false),
                MistAstGenerator.MakeWhileStatement(
                    MistAstGenerator.MakeIdentifierExpr("running"),
                    ImmutableArray<StatementNode>.Empty))));
    }

    /// <summary>
    /// Generates a well-typed program with a void return in Main.
    /// </summary>
    public static Gen<CompilationUnitNode> GenWellTypedVoidReturn()
    {
        return Gen.Constant(
            MakeProgram(ImmutableArray.Create(
                MistAstGenerator.MakeReturnStatement())));
    }

    /// <summary>
    /// Generates a well-typed program with a method that returns int.
    /// Pattern: public int GetValue() { return N; }
    /// </summary>
    public static Gen<CompilationUnitNode> GenWellTypedIntReturn()
    {
        return Gen.Choose(0, 1000).Select(n =>
            MakeProgramWithMethod(
                "GetValue",
                new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = new IntTypePrimitiveTypeNode() } },
                ImmutableArray.Create(
                    MistAstGenerator.MakeReturnStatement(
                        MistAstGenerator.WrapPrimary(
                            new IntegerLiteralExpressionPrimaryNode { IntegerLiteral = Tok(n.ToString()) })))));
    }

    /// <summary>
    /// Generates a well-typed program with string variable declarations.
    /// </summary>
    public static Gen<CompilationUnitNode> GenWellTypedStringVar()
    {
        return Gen.Elements("\"hello\"", "\"world\"", "\"test\"").Select(s =>
            MakeProgram(ImmutableArray.Create(
                MistAstGenerator.MakeStringVarDecl("msg", s))));
    }

    /// <summary>
    /// Generates a well-typed program with int comparison.
    /// Pattern: int a = X; int b = Y; a < b;
    /// </summary>
    public static Gen<CompilationUnitNode> GenWellTypedIntComparison()
    {
        return from a in Gen.Choose(0, 100)
               from b in Gen.Choose(0, 100)
               select MakeProgram(ImmutableArray.Create(
                   MistAstGenerator.MakeIntVarDecl("a", a.ToString()),
                   MistAstGenerator.MakeIntVarDecl("b", b.ToString()),
                   MistAstGenerator.MakeExpressionStatement(
                       MistAstGenerator.MakeRelational(
                           MistAstGenerator.MakeIdentifierExpr("a"),
                           MistAstGenerator.MakeIdentifierExpr("b"), "<"))));
    }

    /// <summary>
    /// Generates a well-typed program with logical NOT.
    /// Pattern: bool flag = V; !flag;
    /// </summary>
    public static Gen<CompilationUnitNode> GenWellTypedLogicalNot()
    {
        return Gen.Elements(true, false).Select(v =>
            MakeProgram(ImmutableArray.Create(
                MistAstGenerator.MakeBoolVarDecl("flag", v),
                MistAstGenerator.MakeExpressionStatement(
                    MistAstGenerator.MakeLogicalNot(
                        MistAstGenerator.MakeIdentifierExpr("flag"))))));
    }

    /// <summary>
    /// Generates a well-typed program with an empty Main method.
    /// </summary>
    public static Gen<CompilationUnitNode> GenWellTypedEmptyMain()
    {
        return Gen.Constant(MakeProgram(ImmutableArray<StatementNode>.Empty));
    }

    /// <summary>
    /// Generates a random well-typed program by choosing from all well-typed patterns.
    /// Each generated program has a valid entry point and correct type usage.
    /// </summary>
    public static Gen<CompilationUnitNode> GenWellTypedProgram()
    {
        return Gen.OneOf(
            GenWellTypedIntArithmetic(),
            GenWellTypedBoolLogic(),
            GenWellTypedIfStatement(),
            GenWellTypedWhileStatement(),
            GenWellTypedVoidReturn(),
            GenWellTypedIntReturn(),
            GenWellTypedStringVar(),
            GenWellTypedIntComparison(),
            GenWellTypedLogicalNot(),
            GenWellTypedEmptyMain());
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Ill-typed program generators (Property 6)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates an ill-typed program: arithmetic on bool operands.
    /// Pattern: bool a = V1; bool b = V2; a + b;
    /// </summary>
    public static Gen<CompilationUnitNode> GenIllTypedArithmeticOnBool()
    {
        return from a in Gen.Elements(true, false)
               from b in Gen.Elements(true, false)
               select MakeProgram(ImmutableArray.Create(
                   MistAstGenerator.MakeBoolVarDecl("a", a),
                   MistAstGenerator.MakeBoolVarDecl("b", b),
                   MistAstGenerator.MakeExpressionStatement(
                       MistAstGenerator.MakeAdditive(
                           MistAstGenerator.MakeIdentifierExpr("a"),
                           MistAstGenerator.MakeIdentifierExpr("b"), "+"))));
    }

    /// <summary>
    /// Generates an ill-typed program: logical AND on int operands.
    /// Pattern: int a = X; int b = Y; a && b;
    /// </summary>
    public static Gen<CompilationUnitNode> GenIllTypedLogicalOnInt()
    {
        return from a in Gen.Choose(0, 100)
               from b in Gen.Choose(0, 100)
               select MakeProgram(ImmutableArray.Create(
                   MistAstGenerator.MakeIntVarDecl("a", a.ToString()),
                   MistAstGenerator.MakeIntVarDecl("b", b.ToString()),
                   MistAstGenerator.MakeExpressionStatement(
                       MistAstGenerator.MakeLogicalAnd(
                           MistAstGenerator.MakeIdentifierExpr("a"),
                           MistAstGenerator.MakeIdentifierExpr("b")))));
    }

    /// <summary>
    /// Generates an ill-typed program: comparison on bool operands.
    /// Pattern: bool a = V1; bool b = V2; a < b;
    /// </summary>
    public static Gen<CompilationUnitNode> GenIllTypedComparisonOnBool()
    {
        return from a in Gen.Elements(true, false)
               from b in Gen.Elements(true, false)
               select MakeProgram(ImmutableArray.Create(
                   MistAstGenerator.MakeBoolVarDecl("a", a),
                   MistAstGenerator.MakeBoolVarDecl("b", b),
                   MistAstGenerator.MakeExpressionStatement(
                       MistAstGenerator.MakeRelational(
                           MistAstGenerator.MakeIdentifierExpr("a"),
                           MistAstGenerator.MakeIdentifierExpr("b"), "<"))));
    }

    /// <summary>
    /// Generates an ill-typed program: non-bool condition in if statement.
    /// Pattern: int x = N; if (x) { }
    /// </summary>
    public static Gen<CompilationUnitNode> GenIllTypedNonBoolConditionIf()
    {
        return Gen.Choose(0, 100).Select(n =>
            MakeProgram(ImmutableArray.Create(
                MistAstGenerator.MakeIntVarDecl("x", n.ToString()),
                MistAstGenerator.MakeIfStatement(
                    MistAstGenerator.MakeIdentifierExpr("x"),
                    ImmutableArray<StatementNode>.Empty))));
    }

    /// <summary>
    /// Generates an ill-typed program: non-bool condition in while statement.
    /// Pattern: int x = N; while (x) { }
    /// </summary>
    public static Gen<CompilationUnitNode> GenIllTypedNonBoolConditionWhile()
    {
        return Gen.Choose(0, 100).Select(n =>
            MakeProgram(ImmutableArray.Create(
                MistAstGenerator.MakeIntVarDecl("x", n.ToString()),
                MistAstGenerator.MakeWhileStatement(
                    MistAstGenerator.MakeIdentifierExpr("x"),
                    ImmutableArray<StatementNode>.Empty))));
    }

    /// <summary>
    /// Generates an ill-typed program: logical NOT on int operand.
    /// Pattern: int x = N; !x;
    /// </summary>
    public static Gen<CompilationUnitNode> GenIllTypedLogicalNotOnInt()
    {
        return Gen.Choose(0, 100).Select(n =>
            MakeProgram(ImmutableArray.Create(
                MistAstGenerator.MakeIntVarDecl("x", n.ToString()),
                MistAstGenerator.MakeExpressionStatement(
                    MistAstGenerator.MakeLogicalNot(
                        MistAstGenerator.MakeIdentifierExpr("x"))))));
    }

    /// <summary>
    /// Generates an ill-typed program: returning int from void Main.
    /// Pattern: return 42;  (in void Main)
    /// </summary>
    public static Gen<CompilationUnitNode> GenIllTypedReturnMismatch()
    {
        return Gen.Choose(0, 1000).Select(n =>
            MakeProgram(ImmutableArray.Create(
                MistAstGenerator.MakeReturnStatement(
                    MistAstGenerator.WrapPrimary(
                        new IntegerLiteralExpressionPrimaryNode { IntegerLiteral = Tok(n.ToString()) })))));
    }

    /// <summary>
    /// Generates a random ill-typed program by choosing from all ill-typed patterns.
    /// Each generated program has a valid entry point but contains at least one type error.
    /// </summary>
    public static Gen<CompilationUnitNode> GenIllTypedProgram()
    {
        return Gen.OneOf(
            GenIllTypedArithmeticOnBool(),
            GenIllTypedLogicalOnInt(),
            GenIllTypedComparisonOnBool(),
            GenIllTypedNonBoolConditionIf(),
            GenIllTypedNonBoolConditionWhile(),
            GenIllTypedLogicalNotOnInt(),
            GenIllTypedReturnMismatch());
    }

    // ═══════════════════════════════════════════════════════════════════════
    // FsCheck Arbitrary registrations
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>FsCheck Arbitrary for well-typed programs.</summary>
    public static Arbitrary<CompilationUnitNode> ArbitraryWellTypedProgram()
    {
        return GenWellTypedProgram().ToArbitrary();
    }
}