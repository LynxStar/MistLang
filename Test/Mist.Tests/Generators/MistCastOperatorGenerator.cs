// Generators for Mist cast operator declaration and cast expression AST nodes.
// Produces random but structurally valid cast operator declarations and cast expressions
// for property-based testing.
// Feature: cast-operators, Property 1: Cast operator and cast expression round-trip
// **Validates: Requirements 2.1, 2.2, 2.3, 2.4, 2.5, 3.1, 3.2, 3.3, 4.1, 4.2, 4.3, 4.4, 4.5**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using Mist.Generated;

namespace Mist.Tests.Generators;

/// <summary>
/// FsCheck generators for Mist cast operator declaration and cast expression AST nodes.
/// Generates random cast operator declarations with varying implicit/explicit kinds,
/// target types, parameter types, block bodies, and semicolon-terminated bodies.
/// Also generates cast expressions with varying target types and operand expressions.
/// </summary>
public static class MistCastOperatorGenerator
{
    private static Storm.Runtime.Token Tok(string lexeme)
    {
        return MistTypeGenerator.MakeToken(lexeme);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Name pools
    // ═══════════════════════════════════════════════════════════════════════

    private static readonly string[] ClassNames =
        ["Banana", "Weight", "Temperature", "Distance", "Velocity", "Duration", "Angle"];

    private static readonly string[] ParamNames =
        ["value", "source", "input", "item", "arg"];

    // ═══════════════════════════════════════════════════════════════════════
    // Cast operator kind generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a random cast operator kind (implicit or explicit).
    /// </summary>
    public static Gen<CastOperatorKindNode> GenCastOperatorKind()
    {
        return Gen.Elements<CastOperatorKindNode>(
            new ImplicitCastCastOperatorKindNode(),
            new ExplicitCastCastOperatorKindNode());
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Cast operator body generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a block body with a return statement for a cast operator.
    /// </summary>
    public static Gen<OperatorBodyNode> GenBlockBody()
    {
        return from paramName in Gen.Elements(ParamNames)
               select (OperatorBodyNode)new OperatorBlockBodyOperatorBodyNode
               {
                   Block = new BlockNode
                   {
                       Statements = ImmutableArray.Create(
                           MistAstGenerator.MakeReturnStatement(
                               MistAstGenerator.MakeIdentifierExpr(paramName))),
                   },
               };
    }

    /// <summary>
    /// Generates a no-body (semicolon-terminated) operator body for intrinsic cast operators.
    /// </summary>
    public static Gen<OperatorBodyNode> GenNoBody()
    {
        return Gen.Constant((OperatorBodyNode)new OperatorNoBodyOperatorBodyNode());
    }

    /// <summary>
    /// Generates a random operator body (block or no-body).
    /// </summary>
    public static Gen<OperatorBodyNode> GenOperatorBody()
    {
        return Gen.OneOf(GenBlockBody(), GenNoBody());
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Modifier generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates modifiers for a cast operator with a block body: public static.
    /// </summary>
    public static ImmutableArray<ModifierNode> MakePublicStaticModifiers()
    {
        return ImmutableArray.Create<ModifierNode>(
            new PublicModifierModifierNode(),
            new StaticModifierModifierNode());
    }

    /// <summary>
    /// Generates modifiers for an intrinsic cast operator: intrinsic public static.
    /// </summary>
    public static ImmutableArray<ModifierNode> MakeIntrinsicPublicStaticModifiers()
    {
        return ImmutableArray.Create<ModifierNode>(
            new IntrinsicModifierModifierNode(),
            new PublicModifierModifierNode(),
            new StaticModifierModifierNode());
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Named type reference generator
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a named type reference from the class name pool.
    /// </summary>
    public static Gen<TypeReferenceNode> GenNamedTypeRef()
    {
        return Gen.Elements(ClassNames).Select(name =>
            (TypeReferenceNode)new TypeReferenceNode
            {
                First = new NamedSingleTypeRefSingleTypeRefNode
                {
                    Name = new QualifiedIdentifierNode { IDENTIFIER = Tok(name) },
                }
            });
    }

    /// <summary>
    /// Generates a type reference suitable for cast operator target/parameter types.
    /// Includes both named types and primitive types.
    /// </summary>
    public static Gen<TypeReferenceNode> GenCastTypeRef()
    {
        return Gen.OneOf(
            GenNamedTypeRef(),
            MistTypeGenerator.GenPrimitiveTypeRef());
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Cast operator declaration generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a cast operator declaration with a block body (non-intrinsic).
    /// </summary>
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

    /// <summary>
    /// Generates a cast operator declaration with a semicolon-terminated body (intrinsic).
    /// </summary>
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
                   Body = new OperatorNoBodyOperatorBodyNode(),
               };
    }

    /// <summary>
    /// Generates a random cast operator declaration (block body or no-body).
    /// </summary>
    public static Gen<CastOperatorDeclarationNode> GenCastOperatorDeclaration()
    {
        return Gen.OneOf(GenBlockBodyCastOperator(), GenNoBodyCastOperator());
    }

    /// <summary>
    /// Wraps a cast operator declaration as a member declaration node.
    /// </summary>
    public static Gen<MemberDeclarationNode> GenCastOperatorMember()
    {
        return GenCastOperatorDeclaration().Select(castOp =>
            (MemberDeclarationNode)new CastOperatorMemberMemberDeclarationNode
            {
                CastOperator = castOp,
            });
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Cast expression generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates a cast expression UnaryNode: (TargetType)operand.
    /// </summary>
    public static CastExpressionUnaryNode MakeCastExpression(
        TypeReferenceNode targetType, UnaryNode operand)
    {
        return new CastExpressionUnaryNode
        {
            TargetType = targetType,
            Operand = operand,
        };
    }

    /// <summary>
    /// Wraps a CastExpressionUnaryNode into a full ExpressionNode through the precedence chain.
    /// </summary>
    public static ExpressionNode WrapCastExpression(CastExpressionUnaryNode castExpr)
    {
        var mult = new MultiplicativeNode { Left = castExpr };
        var add = new AdditiveNode { Left = mult };
        var rel = new RelationalComparisonRelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        return new AssignmentFormExpressionNode { Assignment = assign };
    }

    /// <summary>
    /// Generates a cast expression with a random target type and operand.
    /// Operands are simple identifiers or literals to avoid ambiguity.
    /// </summary>
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

    /// <summary>
    /// Generates a cast expression statement: (TargetType)expr;
    /// </summary>
    public static Gen<StatementNode> GenCastExpressionStatement()
    {
        return GenCastExpression().Select(expr =>
            MistAstGenerator.MakeExpressionStatement(expr));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Complete program generators (with cast operators and cast expressions)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a class with a single cast operator member.
    /// </summary>
    public static Gen<CompilationUnitNode> GenClassWithCastOperator()
    {
        return from className in Gen.Elements(ClassNames)
               from castOp in GenCastOperatorMember()
               select new CompilationUnitNode
               {
                   Items = ImmutableArray.Create<TopLevelItemNode>(
                       new TypeItemTopLevelItemNode
                       {
                           TypeDeclaration = new ClassTypeTypeDeclarationNode
                           {
                               ClassDeclaration = new ClassDeclarationNode
                               {
                                   Name = Tok(className),
                                   Modifiers = ImmutableArray.Create<ModifierNode>(
                                       new PublicModifierModifierNode()),
                                   Members = ImmutableArray.Create(castOp),
                               },
                           },
                       }),
               };
    }

    /// <summary>
    /// Generates a class with multiple cast operator members.
    /// </summary>
    public static Gen<CompilationUnitNode> GenClassWithMultipleCastOperators()
    {
        return from className in Gen.Elements(ClassNames)
               from castOps in GenCastOperatorMember().ListOf()
                   .Where(list => list.Count >= 2 && list.Count <= 4)
               select new CompilationUnitNode
               {
                   Items = ImmutableArray.Create<TopLevelItemNode>(
                       new TypeItemTopLevelItemNode
                       {
                           TypeDeclaration = new ClassTypeTypeDeclarationNode
                           {
                               ClassDeclaration = new ClassDeclarationNode
                               {
                                   Name = Tok(className),
                                   Modifiers = ImmutableArray.Create<ModifierNode>(
                                       new PublicModifierModifierNode()),
                                   Members = castOps.ToImmutableArray(),
                               },
                           },
                       }),
               };
    }

    /// <summary>
    /// Generates a class with a cast operator and a method containing a cast expression.
    /// </summary>
    public static Gen<CompilationUnitNode> GenClassWithCastOperatorAndExpression()
    {
        return from className in Gen.Elements(ClassNames)
               from castOp in GenCastOperatorMember()
               from castExprStmt in GenCastExpressionStatement()
               select new CompilationUnitNode
               {
                   Items = ImmutableArray.Create<TopLevelItemNode>(
                       new TypeItemTopLevelItemNode
                       {
                           TypeDeclaration = new ClassTypeTypeDeclarationNode
                           {
                               ClassDeclaration = new ClassDeclarationNode
                               {
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
                                               Body = new MethodBlockBodyMethodBodyNode
                                               {
                                                   Block = new BlockNode
                                                   {
                                                       Statements = ImmutableArray.Create(castExprStmt),
                                                   },
                                               },
                                           },
                                       }),
                               },
                           },
                       }),
               };
    }

    /// <summary>
    /// Generates a struct with a cast operator member.
    /// </summary>
    public static Gen<CompilationUnitNode> GenStructWithCastOperator()
    {
        return from structName in Gen.Elements(ClassNames)
               from castOp in GenCastOperatorMember()
               select new CompilationUnitNode
               {
                   Items = ImmutableArray.Create<TopLevelItemNode>(
                       new TypeItemTopLevelItemNode
                       {
                           TypeDeclaration = new StructTypeTypeDeclarationNode
                           {
                               StructDeclaration = new StructDeclarationNode
                               {
                                   Name = Tok(structName),
                                   Modifiers = ImmutableArray.Create<ModifierNode>(
                                       new PublicModifierModifierNode()),
                                   Members = ImmutableArray.Create(castOp),
                               },
                           },
                       }),
               };
    }

    /// <summary>
    /// Generates a class with a method that contains only cast expression statements.
    /// </summary>
    public static Gen<CompilationUnitNode> GenClassWithCastExpressionOnly()
    {
        return from className in Gen.Elements(ClassNames)
               from castExprStmts in GenCastExpressionStatement().ListOf()
                   .Where(list => list.Count >= 1 && list.Count <= 3)
               select new CompilationUnitNode
               {
                   Items = ImmutableArray.Create<TopLevelItemNode>(
                       new TypeItemTopLevelItemNode
                       {
                           TypeDeclaration = new ClassTypeTypeDeclarationNode
                           {
                               ClassDeclaration = new ClassDeclarationNode
                               {
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
                                               Body = new MethodBlockBodyMethodBodyNode
                                               {
                                                   Block = new BlockNode
                                                   {
                                                       Statements = castExprStmts.ToImmutableArray(),
                                                   },
                                               },
                                           },
                                       }),
                               },
                           },
                       }),
               };
    }

    /// <summary>
    /// Generates a random CompilationUnitNode with cast operator-related declarations.
    /// </summary>
    public static Gen<CompilationUnitNode> GenCastOperatorProgram()
    {
        return Gen.OneOf(
            GenClassWithCastOperator(),
            GenClassWithMultipleCastOperators(),
            GenClassWithCastOperatorAndExpression(),
            GenStructWithCastOperator(),
            GenClassWithCastExpressionOnly());
    }

    // ═══════════════════════════════════════════════════════════════════════
    // FsCheck Arbitrary registrations
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>FsCheck Arbitrary for cast operator programs.</summary>
    public static Arbitrary<CompilationUnitNode> ArbitraryCastOperatorProgram()
    {
        return GenCastOperatorProgram().ToArbitrary();
    }
}