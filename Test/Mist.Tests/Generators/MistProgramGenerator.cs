using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using Mist.Generated;

namespace Mist.Tests.Generators;

public static class MistProgramGenerator
{
    private static Storm.Runtime.Token Tok(string lexeme)
    {
        return MistTypeGenerator.MakeToken(lexeme);
    }

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
            Body = new CallableBlockBodyCallableBodyNode { Block = new BlockNode { Statements = mainStatements } },
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
            Body = new CallableBlockBodyCallableBodyNode { Block = new BlockNode { Statements = methodStatements } },
            Modifiers = methodModifiers.ToImmutable(),
        };

        var additionalMembers = ImmutableArray.Create<MemberDeclarationNode>(
            new MethodMemberMemberDeclarationNode { Method = method });

        return MakeProgram(
            mainStatements ?? ImmutableArray<StatementNode>.Empty,
            additionalMembers);
    }

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

    public static Gen<CompilationUnitNode> GenWellTypedIfStatement()
    {
        return Gen.Elements(true, false).Select(cond =>
            MakeProgram(ImmutableArray.Create(
                MistAstGenerator.MakeBoolVarDecl("cond", cond),
                MistAstGenerator.MakeIfStatement(
                    MistAstGenerator.MakeIdentifierExpr("cond"),
                    ImmutableArray.Create(MistAstGenerator.MakeIntVarDecl("inner", "42"))))));
    }

    public static Gen<CompilationUnitNode> GenWellTypedWhileStatement()
    {
        return Gen.Constant(
            MakeProgram(ImmutableArray.Create(
                MistAstGenerator.MakeBoolVarDecl("running", false),
                MistAstGenerator.MakeWhileStatement(
                    MistAstGenerator.MakeIdentifierExpr("running"),
                    ImmutableArray<StatementNode>.Empty))));
    }

    public static Gen<CompilationUnitNode> GenWellTypedVoidReturn()
    {
        return Gen.Constant(
            MakeProgram(ImmutableArray.Create(
                MistAstGenerator.MakeReturnStatement())));
    }

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

    public static Gen<CompilationUnitNode> GenWellTypedStringVar()
    {
        return Gen.Elements("\"hello\"", "\"world\"", "\"test\"").Select(s =>
            MakeProgram(ImmutableArray.Create(
                MistAstGenerator.MakeStringVarDecl("msg", s))));
    }

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

    public static Gen<CompilationUnitNode> GenWellTypedLogicalNot()
    {
        return Gen.Elements(true, false).Select(v =>
            MakeProgram(ImmutableArray.Create(
                MistAstGenerator.MakeBoolVarDecl("flag", v),
                MistAstGenerator.MakeExpressionStatement(
                    MistAstGenerator.MakeLogicalNot(
                        MistAstGenerator.MakeIdentifierExpr("flag"))))));
    }

    public static Gen<CompilationUnitNode> GenWellTypedEmptyMain()
    {
        return Gen.Constant(MakeProgram(ImmutableArray<StatementNode>.Empty));
    }

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

    public static Gen<CompilationUnitNode> GenIllTypedNonBoolConditionIf()
    {
        return Gen.Choose(0, 100).Select(n =>
            MakeProgram(ImmutableArray.Create(
                MistAstGenerator.MakeIntVarDecl("x", n.ToString()),
                MistAstGenerator.MakeIfStatement(
                    MistAstGenerator.MakeIdentifierExpr("x"),
                    ImmutableArray<StatementNode>.Empty))));
    }

    public static Gen<CompilationUnitNode> GenIllTypedNonBoolConditionWhile()
    {
        return Gen.Choose(0, 100).Select(n =>
            MakeProgram(ImmutableArray.Create(
                MistAstGenerator.MakeIntVarDecl("x", n.ToString()),
                MistAstGenerator.MakeWhileStatement(
                    MistAstGenerator.MakeIdentifierExpr("x"),
                    ImmutableArray<StatementNode>.Empty))));
    }

    public static Gen<CompilationUnitNode> GenIllTypedLogicalNotOnInt()
    {
        return Gen.Choose(0, 100).Select(n =>
            MakeProgram(ImmutableArray.Create(
                MistAstGenerator.MakeIntVarDecl("x", n.ToString()),
                MistAstGenerator.MakeExpressionStatement(
                    MistAstGenerator.MakeLogicalNot(
                        MistAstGenerator.MakeIdentifierExpr("x"))))));
    }

    public static Gen<CompilationUnitNode> GenIllTypedReturnMismatch()
    {
        return Gen.Choose(0, 1000).Select(n =>
            MakeProgram(ImmutableArray.Create(
                MistAstGenerator.MakeReturnStatement(
                    MistAstGenerator.WrapPrimary(
                        new IntegerLiteralExpressionPrimaryNode { IntegerLiteral = Tok(n.ToString()) })))));
    }

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

    public static Arbitrary<CompilationUnitNode> ArbitraryWellTypedProgram()
    {
        return GenWellTypedProgram().ToArbitrary();
    }
}