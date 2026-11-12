using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using Mist.Generated;

namespace Mist.Tests.Generators;

/// Output is grammatical but not necessarily well-typed; <see cref="MistProgramGenerator"/> builds deliberately well- or ill-typed programs.
public static class MistAstGenerator
{
    private static Storm.Runtime.Token Tok(string lexeme)
    {
        return MistTypeGenerator.MakeToken(lexeme);
    }

    public static ExpressionNode WrapPrimary(PrimaryNode primary)
    {
        var postfix = new PostfixNode
        {
            Target = primary,
            Suffix = ImmutableArray<PostfixSuffixNode>.Empty,
        };
        var unary = new PostfixExpressionUnaryNode { Primary = postfix };
        var mult = new MultiplicativeNode { Left = new NonRangeRangeNode { Operand = unary } };
        var add = new AdditiveNode { Left = mult };
        var rel = new RelationalComparisonRelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        return new AssignmentFormExpressionNode { Assignment = assign };
    }

    public static UnaryNode ExtractUnary(ExpressionNode expr)
    {
        var logOr = expr.AsAssignment().Target.Left;
        var logAnd = logOr.Left;
        var equality = logAnd.Left;
        var relational = (RelationalComparisonRelationalNode)equality.Left;
        var additive = relational.Left;
        var multiplicative = additive.Left;

        // Generated compound expressions always use the pass-through (NonRange) range
        // form, so the inner unary is always present.
        return multiplicative.Left.UnwrapRange()!;
    }

    public static Gen<ExpressionNode> GenIntLiteral()
    {
        return Gen.Choose(0, 1000).Select(n =>
            WrapPrimary(new IntegerLiteralExpressionPrimaryNode
            {
                IntegerLiteral = Tok(n.ToString()),
            }));
    }

    public static Gen<ExpressionNode> GenBoolLiteral()
    {
        return Gen.Elements(true, false).Select(b =>
            WrapPrimary(b
                ? new TrueLiteralExpressionPrimaryNode()
                : new FalseLiteralExpressionPrimaryNode()));
    }

    public static Gen<ExpressionNode> GenStringLiteral()
    {
        return Gen.Elements("\"hello\"", "\"world\"", "\"test\"", "\"\"").Select(s =>
            WrapPrimary(new StringLiteralExpressionPrimaryNode
            {
                StringLiteral = Tok(s),
            }));
    }

    public static Gen<ExpressionNode> GenCharLiteral()
    {
        return Gen.Elements("'a'", "'b'", "'z'", "'0'").Select(c =>
            WrapPrimary(new CharLiteralExpressionPrimaryNode
            {
                CharLiteral = Tok(c),
            }));
    }

    public static Gen<ExpressionNode> GenFloatLiteral()
    {
        return Gen.Elements("1.0", "3.14", "0.5", "99.9").Select(f =>
            WrapPrimary(new FloatLiteralExpressionPrimaryNode
            {
                FloatLiteral = Tok(f),
            }));
    }

    public static ExpressionNode MakeIdentifierExpr(string name)
    {
        return WrapPrimary(new IdentifierExpressionPrimaryNode { Name = Tok(name) });
    }

    public static Gen<ExpressionNode> GenIdentifierExpr()
    {
        return Gen.Elements("x", "y", "z", "a", "b").Select(MakeIdentifierExpr);
    }

    public static ExpressionNode MakeAdditive(ExpressionNode left, ExpressionNode right, string op = "+")
    {
        var leftUnary = ExtractUnary(left);
        var rightUnary = ExtractUnary(right);
        var leftMult = new MultiplicativeNode { Left = new NonRangeRangeNode { Operand = leftUnary } };
        var rightMult = new MultiplicativeNode { Left = new NonRangeRangeNode { Operand = rightUnary } };
        var add = new AdditiveNode { Left = leftMult, Group0 = ImmutableArray.Create(new AdditiveNodeGroup0 { Operator = Tok(op), Right = rightMult }) };
        var rel = new RelationalComparisonRelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        return new AssignmentFormExpressionNode { Assignment = assign };
    }

    public static ExpressionNode MakeMultiplicative(ExpressionNode left, ExpressionNode right, string op = "*")
    {
        var leftUnary = ExtractUnary(left);
        var rightUnary = ExtractUnary(right);
        var mult = new MultiplicativeNode { Left = new NonRangeRangeNode { Operand = leftUnary }, Group0 = ImmutableArray.Create(new MultiplicativeNodeGroup0 { Operator = Tok(op), Right = new NonRangeRangeNode { Operand = rightUnary } }) };
        var add = new AdditiveNode { Left = mult };
        var rel = new RelationalComparisonRelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        return new AssignmentFormExpressionNode { Assignment = assign };
    }

    public static ExpressionNode MakeRelational(ExpressionNode left, ExpressionNode right, string op = "<")
    {
        var leftUnary = ExtractUnary(left);
        var rightUnary = ExtractUnary(right);
        var leftMult = new MultiplicativeNode { Left = new NonRangeRangeNode { Operand = leftUnary } };
        var rightMult = new MultiplicativeNode { Left = new NonRangeRangeNode { Operand = rightUnary } };
        var leftAdd = new AdditiveNode { Left = leftMult };
        var rightAdd = new AdditiveNode { Left = rightMult };
        var rel = new RelationalComparisonRelationalNode { Left = leftAdd, Group0 = ImmutableArray.Create(new RelationalComparisonRelationalNodeGroup0 { Operator = Tok(op), Right = rightAdd }) };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        return new AssignmentFormExpressionNode { Assignment = assign };
    }

    public static ExpressionNode MakeLogicalAnd(ExpressionNode left, ExpressionNode right)
    {
        var leftUnary = ExtractUnary(left);
        var rightUnary = ExtractUnary(right);
        var leftMult = new MultiplicativeNode { Left = new NonRangeRangeNode { Operand = leftUnary } };
        var rightMult = new MultiplicativeNode { Left = new NonRangeRangeNode { Operand = rightUnary } };
        var leftAdd = new AdditiveNode { Left = leftMult };
        var rightAdd = new AdditiveNode { Left = rightMult };
        var leftRel = new RelationalComparisonRelationalNode { Left = leftAdd };
        var rightRel = new RelationalComparisonRelationalNode { Left = rightAdd };
        var leftEq = new EqualityNode { Left = leftRel };
        var rightEq = new EqualityNode { Left = rightRel };
        var logAnd = new LogicalAndNode { Left = leftEq, Equalitys = [rightEq] };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        return new AssignmentFormExpressionNode { Assignment = assign };
    }

    public static ExpressionNode MakeLogicalOr(ExpressionNode left, ExpressionNode right)
    {
        var leftUnary = ExtractUnary(left);
        var rightUnary = ExtractUnary(right);
        var leftMult = new MultiplicativeNode { Left = new NonRangeRangeNode { Operand = leftUnary } };
        var rightMult = new MultiplicativeNode { Left = new NonRangeRangeNode { Operand = rightUnary } };
        var leftAdd = new AdditiveNode { Left = leftMult };
        var rightAdd = new AdditiveNode { Left = rightMult };
        var leftRel = new RelationalComparisonRelationalNode { Left = leftAdd };
        var rightRel = new RelationalComparisonRelationalNode { Left = rightAdd };
        var leftEq = new EqualityNode { Left = leftRel };
        var rightEq = new EqualityNode { Left = rightRel };
        var leftLogAnd = new LogicalAndNode { Left = leftEq };
        var rightLogAnd = new LogicalAndNode { Left = rightEq };
        var logOr = new LogicalOrNode { Left = leftLogAnd, LogicalAnds = [rightLogAnd] };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        return new AssignmentFormExpressionNode { Assignment = assign };
    }

    public static ExpressionNode MakeLogicalNot(ExpressionNode operand)
    {
        var innerUnary = ExtractUnary(operand);
        var notUnary = new LogicalNotUnaryNode { Operand = innerUnary };
        var mult = new MultiplicativeNode { Left = new NonRangeRangeNode { Operand = notUnary } };
        var add = new AdditiveNode { Left = mult };
        var rel = new RelationalComparisonRelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        return new AssignmentFormExpressionNode { Assignment = assign };
    }

    public static ExpressionNode MakeNegate(ExpressionNode operand)
    {
        var innerUnary = ExtractUnary(operand);
        var negUnary = new NegateUnaryNode { Operand = innerUnary };
        var mult = new MultiplicativeNode { Left = new NonRangeRangeNode { Operand = negUnary } };
        var add = new AdditiveNode { Left = mult };
        var rel = new RelationalComparisonRelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        return new AssignmentFormExpressionNode { Assignment = assign };
    }

    public static Gen<ExpressionNode> GenExpression(int depth = 2)
    {
        if(depth <= 0)
        {
            return Gen.OneOf(
                GenIntLiteral(),
                GenBoolLiteral(),
                GenStringLiteral());
        }

        var leaf = Gen.OneOf(GenIntLiteral(), GenBoolLiteral(), GenStringLiteral());
        var sub = GenExpression(depth - 1);

        var additive = from l in sub
                       from r in sub
                       select MakeAdditive(l, r, "+");

        var multiplicative = from l in sub
                             from r in sub
                             select MakeMultiplicative(l, r, "*");

        var relational = from l in sub
                         from r in sub
                         select MakeRelational(l, r, "<");

        var logicalAnd = from l in sub
                         from r in sub
                         select MakeLogicalAnd(l, r);

        var logicalNot = from e in sub
                         select MakeLogicalNot(e);

        return Gen.OneOf(leaf, additive, multiplicative, relational, logicalAnd, logicalNot);
    }

    public static StatementNode MakeVarDecl(TypeReferenceNode type, string name, ExpressionNode initializer)
    {
        return new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                Type = type,
                Name = Tok(name),
                Initializer = initializer,
            },
        };
    }

    public static StatementNode MakeIntVarDecl(string name, string value = "0")
    {
        return MakeVarDecl(
            new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = new IntTypePrimitiveTypeNode() } },
            name,
            WrapPrimary(new IntegerLiteralExpressionPrimaryNode { IntegerLiteral = Tok(value) }));
    }

    public static StatementNode MakeBoolVarDecl(string name, bool value = true)
    {
        PrimaryNode primary = value
            ? new TrueLiteralExpressionPrimaryNode()
            : new FalseLiteralExpressionPrimaryNode();
        return MakeVarDecl(
            new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = new BoolTypePrimitiveTypeNode() } },
            name,
            WrapPrimary(primary));
    }

    public static StatementNode MakeStringVarDecl(string name, string value = "\"hello\"")
    {
        return MakeVarDecl(
            new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = new StringTypePrimitiveTypeNode() } },
            name,
            WrapPrimary(new StringLiteralExpressionPrimaryNode { StringLiteral = Tok(value) }));
    }

    public static StatementNode MakeExpressionStatement(ExpressionNode expr)
    {
        return new ExpressionStatementStatementNode
        {
            ExpressionStatement = new ExpressionStatementNode { Expression = expr },
        };
    }

    public static StatementNode MakeReturnStatement(ExpressionNode? value = null)
    {
        return new ReturnStatementStatementNode
        {
            ReturnStatement = new ReturnStatementNode { Value = value },
        };
    }

    public static StatementNode MakeIfStatement(ExpressionNode condition, ImmutableArray<StatementNode> thenBody)
    {
        return new IfStatementStatementNode
        {
            IfStatement = new IfStatementNode
            {
                Condition = condition,
                ThenBranch = new BlockNode { Statements = thenBody },
            },
        };
    }

    public static StatementNode MakeWhileStatement(ExpressionNode condition, ImmutableArray<StatementNode> body)
    {
        return new WhileStatementStatementNode
        {
            WhileStatement = new WhileStatementNode
            {
                Condition = condition,
                Body = new BlockNode { Statements = body },
            },
        };
    }

    public static Gen<StatementNode> GenStatement(int depth = 1)
    {
        var varNames = new[] { "v0", "v1", "v2", "v3", "v4" };

        var intVarDecl = from name in Gen.Elements(varNames)
                         from val in Gen.Choose(0, 100)
                         select MakeIntVarDecl(name, val.ToString());

        var boolVarDecl = from name in Gen.Elements(varNames)
                          from val in Gen.Elements(true, false)
                          select MakeBoolVarDecl(name, val);

        var stringVarDecl = from name in Gen.Elements(varNames)
                            select MakeStringVarDecl(name);

        var returnStmt = Gen.Constant(MakeReturnStatement());

        var exprStmt = from e in GenIntLiteral()
                       select MakeExpressionStatement(e);

        if(depth <= 0)
        {
            return Gen.OneOf(intVarDecl, boolVarDecl, stringVarDecl, exprStmt);
        }

        var ifStmt = from inner in GenStatement(depth - 1)
                     select MakeIfStatement(
                         WrapPrimary(new TrueLiteralExpressionPrimaryNode()),
                         ImmutableArray.Create(inner));

        var whileStmt = Gen.Constant(
            MakeWhileStatement(
                WrapPrimary(new FalseLiteralExpressionPrimaryNode()),
                ImmutableArray<StatementNode>.Empty));

        return Gen.OneOf(intVarDecl, boolVarDecl, stringVarDecl, exprStmt, ifStmt, whileStmt);
    }

    public static MethodDeclarationNode MakeMethod(
        string name,
        TypeReferenceNode returnType,
        ImmutableArray<StatementNode> body,
        ImmutableArray<ModifierNode>? modifiers = null,
        ParameterListNode? parameters = null)
    {
        return new MethodDeclarationNode
        {
            Name = Tok(name),
            ReturnType = returnType,
            Parameters = parameters,
            Body = new CallableBlockBodyCallableBodyNode { Block = new BlockNode { Statements = body } },
            Modifiers = modifiers ?? ImmutableArray<ModifierNode>.Empty,
        };
    }

    public static FieldDeclarationNode MakeField(
        string name,
        TypeReferenceNode type,
        ImmutableArray<ModifierNode>? modifiers = null)
    {
        return new FieldDeclarationNode
        {
            Name = Tok(name),
            Type = type,
            Modifiers = modifiers ?? ImmutableArray<ModifierNode>.Empty,
        };
    }

    public static Gen<MethodDeclarationNode> GenMethodDeclaration()
    {
        var methodNames = new[] { "DoWork", "Process", "Calculate", "Execute", "Run" };

        return from name in Gen.Elements(methodNames)
               from stmt in GenStatement(0)
               select MakeMethod(
                   name,
                   new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
                   ImmutableArray.Create(stmt),
                   ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()));
    }

    public static Gen<TypeBodyDeclarationNode> GenClassDeclaration()
    {
        var classNames = new[] { "Foo", "Bar", "Baz", "Widget", "Helper" };

        return from name in Gen.Elements(classNames)
               from method in GenMethodDeclaration()
               select new TypeBodyDeclarationNode
               {
                   Keyword = new ClassKeywordTypeKeywordNode(),
                   Name = Tok(name),
                   Members = ImmutableArray.Create<MemberDeclarationNode>(
                       new MethodMemberMemberDeclarationNode { Method = method }),
               };
    }
}