// Feature: cast-operators, Property 1: Cast operator and cast expression round-trip
// **Validates: Requirements 2.1, 2.2, 2.3, 2.4, 2.5, 3.1, 3.2, 3.3, 4.1, 4.2, 4.3, 4.4, 4.5**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using Mist.Generated;

namespace Mist.Tests.Generators;

public static class MistCastOperatorGenerator
{
    private static Storm.Runtime.Token Tok(string lexeme)
    {
        return MistTypeGenerator.MakeToken(lexeme);
    }

    private static readonly string[] ClassNames =
        ["Banana", "Weight", "Temperature", "Distance", "Velocity", "Duration", "Angle"];

    private static readonly string[] ParamNames =
        ["value", "source", "input", "item", "arg"];

    public static Gen<CastOperatorKindNode> GenCastOperatorKind()
    {
        return Gen.Elements<CastOperatorKindNode>(
            new ImplicitCastCastOperatorKindNode(),
            new ExplicitCastCastOperatorKindNode());
    }

    public static Gen<CallableBodyNode> GenBlockBody()
    {
        return from paramName in Gen.Elements(ParamNames)
               select (CallableBodyNode)new CallableBlockBodyCallableBodyNode
               {
                   Block = new BlockNode
                   {
                       Statements = ImmutableArray.Create(
                           MistAstGenerator.MakeReturnStatement(
                               MistAstGenerator.MakeIdentifierExpr(paramName))),
                   },
               };
    }

    public static Gen<CallableBodyNode> GenNoBody()
    {
        return Gen.Constant((CallableBodyNode)new CallableNoBodyCallableBodyNode());
    }

    public static Gen<CallableBodyNode> GenOperatorBody()
    {
        return Gen.OneOf(GenBlockBody(), GenNoBody());
    }

    public static ImmutableArray<ModifierNode> MakePublicStaticModifiers()
    {
        return ImmutableArray.Create<ModifierNode>(
            new PublicModifierModifierNode(),
            new StaticModifierModifierNode());
    }

    public static ImmutableArray<ModifierNode> MakeIntrinsicPublicStaticModifiers()
    {
        return ImmutableArray.Create<ModifierNode>(
            new IntrinsicModifierModifierNode(),
            new PublicModifierModifierNode(),
            new StaticModifierModifierNode());
    }

    public static Gen<TypeReferenceNode> GenNamedTypeRef()
    {
        return Gen.Elements(ClassNames).Select(name =>
            new TypeReferenceNode
            {
                First = new NamedSingleTypeRefSingleTypeRefNode
                {
                    Name = new QualifiedIdentifierNode { IDENTIFIER = Tok(name) },
                }
            });
    }

    public static Gen<TypeReferenceNode> GenCastTypeRef()
    {
        return Gen.OneOf(
            GenNamedTypeRef(),
            MistTypeGenerator.GenPrimitiveTypeRef());
    }

    public static Gen<CastOperatorDeclarationNode> GenBlockBodyCastOperator()
    {
        return from castKind in GenCastOperatorKind()
               from targetType in GenCastTypeRef()
               from paramType in GenCastTypeRef()
               from paramName in Gen.Elements(ParamNames)
               from body in GenBlockBody()
               select new CastOperatorDeclarationNode
               {
                   Modifiers = MakePublicStaticModifiers(),
                   CastKind = castKind,
                   TargetType = targetType,
                   Parameters = new ParameterListNode
                   {
                       Parameter = new ParameterNode
                       {
                           Type = paramType,
                           Name = Tok(paramName),
                       },
                   },
                   Body = body,
               };
    }

    public static Gen<CastOperatorDeclarationNode> GenNoBodyCastOperator()
    {
        return from castKind in GenCastOperatorKind()
               from targetType in GenCastTypeRef()
               from paramType in GenCastTypeRef()
               from paramName in Gen.Elements(ParamNames)
               select new CastOperatorDeclarationNode
               {
                   Modifiers = MakeIntrinsicPublicStaticModifiers(),
                   CastKind = castKind,
                   TargetType = targetType,
                   Parameters = new ParameterListNode
                   {
                       Parameter = new ParameterNode
                       {
                           Type = paramType,
                           Name = Tok(paramName),
                       },
                   },
                   Body = new CallableNoBodyCallableBodyNode(),
               };
    }

    public static Gen<CastOperatorDeclarationNode> GenCastOperatorDeclaration()
    {
        return Gen.OneOf(GenBlockBodyCastOperator(), GenNoBodyCastOperator());
    }

    public static Gen<MemberDeclarationNode> GenCastOperatorMember()
    {
        return GenCastOperatorDeclaration().Select(castOp =>
            (MemberDeclarationNode)new CastOperatorMemberMemberDeclarationNode
            {
                CastOperator = castOp,
            });
    }

    public static CastExpressionUnaryNode MakeCastExpression(
        TypeReferenceNode targetType, UnaryNode operand)
    {
        return new CastExpressionUnaryNode
        {
            TargetType = targetType,
            Operand = operand,
        };
    }

    public static ExpressionNode WrapCastExpression(CastExpressionUnaryNode castExpr)
    {
        var mult = new MultiplicativeNode { Left = new NonRangeRangeNode { Operand = castExpr } };
        var add = new AdditiveNode { Left = mult };
        var rel = new RelationalComparisonRelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        return new AssignmentFormExpressionNode { Assignment = assign };
    }

    /// Operands are identifiers or literals only, to avoid parse ambiguity.
    public static Gen<ExpressionNode> GenCastExpression()
    {
        var operandGen = Gen.OneOf(
            MistAstGenerator.GenIdentifierExpr(),
            MistAstGenerator.GenIntLiteral(),
            MistAstGenerator.GenBoolLiteral());

        return from targetType in GenCastTypeRef()
               from operand in operandGen
               select WrapCastExpression(
                   MakeCastExpression(targetType, MistAstGenerator.ExtractUnary(operand)));
    }

    public static Gen<StatementNode> GenCastExpressionStatement()
    {
        return GenCastExpression().Select(MistAstGenerator.MakeExpressionStatement);
    }

    public static Gen<CompilationUnitNode> GenClassWithCastOperator()
    {
        return from className in Gen.Elements(ClassNames)
               from castOp in GenCastOperatorMember()
               select new CompilationUnitNode
               {
                   Items = ImmutableArray.Create<TypeDeclarationNode>(
                       new BodyTypeTypeDeclarationNode
                       {
                           TypeBody = new TypeBodyDeclarationNode
                           {
                               Keyword = new ClassKeywordTypeKeywordNode(),
                               Name = Tok(className),
                               Modifiers = ImmutableArray.Create<ModifierNode>(
                                   new PublicModifierModifierNode()),
                               Members = ImmutableArray.Create(castOp),
                           },
                       }).AsTopLevelItems(),
               };
    }

    public static Gen<CompilationUnitNode> GenClassWithMultipleCastOperators()
    {
        return from className in Gen.Elements(ClassNames)
               from castOps in GenCastOperatorMember().ListOf()
                   .Where(list => list.Count is >= 2 and <= 4)
               select new CompilationUnitNode
               {
                   Items = ImmutableArray.Create<TypeDeclarationNode>(
                       new BodyTypeTypeDeclarationNode
                       {
                           TypeBody = new TypeBodyDeclarationNode
                           {
                               Keyword = new ClassKeywordTypeKeywordNode(),
                               Name = Tok(className),
                               Modifiers = ImmutableArray.Create<ModifierNode>(
                                   new PublicModifierModifierNode()),
                               Members = castOps.ToImmutableArray(),
                           },
                       }).AsTopLevelItems(),
               };
    }

    public static Gen<CompilationUnitNode> GenClassWithCastOperatorAndExpression()
    {
        return from className in Gen.Elements(ClassNames)
               from castOp in GenCastOperatorMember()
               from castExprStmt in GenCastExpressionStatement()
               select new CompilationUnitNode
               {
                   Items = ImmutableArray.Create<TypeDeclarationNode>(
                       new BodyTypeTypeDeclarationNode
                       {
                           TypeBody = new TypeBodyDeclarationNode
                           {
                               Keyword = new ClassKeywordTypeKeywordNode(),
                               Name = Tok(className),
                               Modifiers = ImmutableArray.Create<ModifierNode>(
                                   new PublicModifierModifierNode()),
                               Members = ImmutableArray.Create(
                                   castOp,
                                   new MethodMemberMemberDeclarationNode
                                   {
                                       Method = new MethodDeclarationNode
                                       {
                                           Name = Tok("Test"),
                                           ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
                                           Modifiers = ImmutableArray.Create<ModifierNode>(
                                               new PublicModifierModifierNode()),
                                           Body = new CallableBlockBodyCallableBodyNode
                                           {
                                               Block = new BlockNode
                                               {
                                                   Statements = ImmutableArray.Create(castExprStmt),
                                               },
                                           },
                                       },
                                   }),
                           },
                       }).AsTopLevelItems(),
               };
    }

    public static Gen<CompilationUnitNode> GenStructWithCastOperator()
    {
        return from structName in Gen.Elements(ClassNames)
               from castOp in GenCastOperatorMember()
               select new CompilationUnitNode
               {
                   Items = ImmutableArray.Create<TypeDeclarationNode>(
                       new BodyTypeTypeDeclarationNode
                       {
                           TypeBody = new TypeBodyDeclarationNode
                           {
                               Keyword = new StructKeywordTypeKeywordNode(),
                               Name = Tok(structName),
                               Modifiers = ImmutableArray.Create<ModifierNode>(
                                   new PublicModifierModifierNode()),
                               Members = ImmutableArray.Create(castOp),
                           },
                       }).AsTopLevelItems(),
               };
    }

    public static Gen<CompilationUnitNode> GenClassWithCastExpressionOnly()
    {
        return from className in Gen.Elements(ClassNames)
               from castExprStmts in GenCastExpressionStatement().ListOf()
                   .Where(list => list.Count is >= 1 and <= 3)
               select new CompilationUnitNode
               {
                   Items = ImmutableArray.Create<TypeDeclarationNode>(
                       new BodyTypeTypeDeclarationNode
                       {
                           TypeBody = new TypeBodyDeclarationNode
                           {
                               Keyword = new ClassKeywordTypeKeywordNode(),
                               Name = Tok(className),
                               Modifiers = ImmutableArray.Create<ModifierNode>(
                                   new PublicModifierModifierNode()),
                               Members = ImmutableArray.Create<MemberDeclarationNode>(
                                   new MethodMemberMemberDeclarationNode
                                   {
                                       Method = new MethodDeclarationNode
                                       {
                                           Name = Tok("Test"),
                                           ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
                                           Modifiers = ImmutableArray.Create<ModifierNode>(
                                               new PublicModifierModifierNode()),
                                           Body = new CallableBlockBodyCallableBodyNode
                                           {
                                               Block = new BlockNode
                                               {
                                                   Statements = castExprStmts.ToImmutableArray(),
                                               },
                                           },
                                       },
                                   }),
                           },
                       }).AsTopLevelItems(),
               };
    }

    public static Gen<CompilationUnitNode> GenCastOperatorProgram()
    {
        return Gen.OneOf(
            GenClassWithCastOperator(),
            GenClassWithMultipleCastOperators(),
            GenClassWithCastOperatorAndExpression(),
            GenStructWithCastOperator(),
            GenClassWithCastExpressionOnly());
    }

    public static Arbitrary<CompilationUnitNode> ArbitraryCastOperatorProgram()
    {
        return GenCastOperatorProgram().ToArbitrary();
    }
}