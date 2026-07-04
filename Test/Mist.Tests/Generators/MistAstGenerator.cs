// Generators for Mist AST nodes (expressions, statements, members).
// Used by property-based tests to produce random but structurally valid AST fragments.

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using Mist.Generated;

namespace Mist.Tests.Generators;

/// <summary>
/// FsCheck generators for Mist AST nodes: expressions, statements, and member declarations.
/// All generated ASTs are structurally valid (they respect the grammar rules) but are not
/// necessarily well-typed — use <see cref="MistProgramGenerator"/> for well-typed or
/// ill-typed complete programs.
/// </summary>
public static class MistAstGenerator
{
    private static Storm.Runtime.Token Tok(string lexeme)
    {
        return MistTypeGenerator.MakeToken(lexeme);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Expression generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Wraps a PrimaryNode into a full ExpressionNode through the precedence chain.</summary>
    public static ExpressionNode WrapPrimary(PrimaryNode primary)
    {
        var postfix = new PostfixNode
        {
            Target = primary,
            Suffix = ImmutableArray<PostfixSuffixNode>.Empty,
        };
        var unary = new PostfixExpressionUnaryNode { Primary = postfix };
        var mult = new MultiplicativeNode { Left = unary };
        var add = new AdditiveNode { Left = mult };
        var rel = new RelationalComparisonRelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        return new AssignmentFormExpressionNode { Assignment = assign };
    }

    /// <summary>Extracts the UnaryNode from an ExpressionNode for reuse in compound expressions.</summary>
    public static UnaryNode ExtractUnary(ExpressionNode expr)
    {
        var logOr = expr.AsAssignment().Target.Left;
        var logAnd = logOr.Left;
        var equality = logAnd.Left;
        var relational = (RelationalComparisonRelationalNode)equality.Left;
        var additive = relational.Left;
        var multiplicative = additive.Left;
        return multiplicative.Left;
    }

    // ─── Literal expressions ─────────────────────────────────────────────

    /// <summary>Generates an integer literal expression with a random non-negative value.</summary>
    public static Gen<ExpressionNode> GenIntLiteral()
    {
        return Gen.Choose(0, 1000).Select(n =>
            WrapPrimary(new IntegerLiteralExpressionPrimaryNode
            {
                IntegerLiteral = Tok(n.ToString()),
            }));
    }

    /// <summary>Generates a bool literal expression.</summary>
    public static Gen<ExpressionNode> GenBoolLiteral()
    {
        return Gen.Elements(true, false).Select(b =>
            WrapPrimary(b
                ? new TrueLiteralExpressionPrimaryNode()
                : new FalseLiteralExpressionPrimaryNode()));
    }

    /// <summary>Generates a string literal expression.</summary>
    public static Gen<ExpressionNode> GenStringLiteral()
    {
        return Gen.Elements("\"hello\"", "\"world\"", "\"test\"", "\"\"").Select(s =>
            WrapPrimary(new StringLiteralExpressionPrimaryNode
            {
                StringLiteral = Tok(s),
            }));
    }

    /// <summary>Generates a char literal expression.</summary>
    public static Gen<ExpressionNode> GenCharLiteral()
    {
        return Gen.Elements("'a'", "'b'", "'z'", "'0'").Select(c =>
            WrapPrimary(new CharLiteralExpressionPrimaryNode
            {
                CharLiteral = Tok(c),
            }));
    }

    /// <summary>Generates a float literal expression.</summary>
    public static Gen<ExpressionNode> GenFloatLiteral()
    {
        return Gen.Elements("1.0", "3.14", "0.5", "99.9").Select(f =>
            WrapPrimary(new FloatLiteralExpressionPrimaryNode
            {
                FloatLiteral = Tok(f),
            }));
    }

    /// <summary>Generates an identifier expression referencing a given name.</summary>
    public static ExpressionNode MakeIdentifierExpr(string name)
    {
        return WrapPrimary(new IdentifierExpressionPrimaryNode { Name = Tok(name) });
    }

    /// <summary>Generates a random identifier expression from a pool of names.</summary>
    public static Gen<ExpressionNode> GenIdentifierExpr()
    {
        return Gen.Elements("x", "y", "z", "a", "b").Select(MakeIdentifierExpr);
    }

    // ─── Compound expressions ────────────────────────────────────────────

    /// <summary>Creates an additive expression: left op right.</summary>
    public static ExpressionNode MakeAdditive(ExpressionNode left, ExpressionNode right, string op = "+")
    {
        var leftUnary = ExtractUnary(left);
        var rightUnary = ExtractUnary(right);
        var leftMult = new MultiplicativeNode { Left = leftUnary };
        var rightMult = new MultiplicativeNode { Left = rightUnary };
        var add = new AdditiveNode { Left = leftMult, Operator = Tok(op), Right = rightMult };
        var rel = new RelationalComparisonRelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        return new AssignmentFormExpressionNode { Assignment = assign };
    }

    /// <summary>Creates a multiplicative expression: left op right.</summary>
    public static ExpressionNode MakeMultiplicative(ExpressionNode left, ExpressionNode right, string op = "*")
    {
        var leftUnary = ExtractUnary(left);
        var rightUnary = ExtractUnary(right);
        var mult = new MultiplicativeNode { Left = leftUnary, Operator = Tok(op), Right = rightUnary };
        var add = new AdditiveNode { Left = mult };
        var rel = new RelationalComparisonRelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        return new AssignmentFormExpressionNode { Assignment = assign };
    }

    /// <summary>Creates a relational expression: left op right.</summary>
    public static ExpressionNode MakeRelational(ExpressionNode left, ExpressionNode right, string op = "<")
    {
        var leftUnary = ExtractUnary(left);
        var rightUnary = ExtractUnary(right);
        var leftMult = new MultiplicativeNode { Left = leftUnary };
        var rightMult = new MultiplicativeNode { Left = rightUnary };
        var leftAdd = new AdditiveNode { Left = leftMult };
        var rightAdd = new AdditiveNode { Left = rightMult };
        var rel = new RelationalComparisonRelationalNode { Left = leftAdd, Operator = Tok(op), Right = rightAdd };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        return new AssignmentFormExpressionNode { Assignment = assign };
    }

    /// <summary>Creates a logical AND expression: left && right.</summary>
    public static ExpressionNode MakeLogicalAnd(ExpressionNode left, ExpressionNode right)
    {
        var leftUnary = ExtractUnary(left);
        var rightUnary = ExtractUnary(right);
        var leftMult = new MultiplicativeNode { Left = leftUnary };
        var rightMult = new MultiplicativeNode { Left = rightUnary };
        var leftAdd = new AdditiveNode { Left = leftMult };
        var rightAdd = new AdditiveNode { Left = rightMult };
        var leftRel = new RelationalComparisonRelationalNode { Left = leftAdd };
        var rightRel = new RelationalComparisonRelationalNode { Left = rightAdd };
        var leftEq = new EqualityNode { Left = leftRel };
        var rightEq = new EqualityNode { Left = rightRel };
        var logAnd = new LogicalAndNode { Left = leftEq, Right = rightEq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        return new AssignmentFormExpressionNode { Assignment = assign };
    }

    /// <summary>Creates a logical OR expression: left || right.</summary>
    public static ExpressionNode MakeLogicalOr(ExpressionNode left, ExpressionNode right)
    {
        var leftUnary = ExtractUnary(left);
        var rightUnary = ExtractUnary(right);
        var leftMult = new MultiplicativeNode { Left = leftUnary };
        var rightMult = new MultiplicativeNode { Left = rightUnary };
        var leftAdd = new AdditiveNode { Left = leftMult };
        var rightAdd = new AdditiveNode { Left = rightMult };
        var leftRel = new RelationalComparisonRelationalNode { Left = leftAdd };
        var rightRel = new RelationalComparisonRelationalNode { Left = rightAdd };
        var leftEq = new EqualityNode { Left = leftRel };
        var rightEq = new EqualityNode { Left = rightRel };
        var leftLogAnd = new LogicalAndNode { Left = leftEq };
        var rightLogAnd = new LogicalAndNode { Left = rightEq };
        var logOr = new LogicalOrNode { Left = leftLogAnd, Right = rightLogAnd };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        return new AssignmentFormExpressionNode { Assignment = assign };
    }

    /// <summary>Creates a logical NOT expression: !operand.</summary>
    public static ExpressionNode MakeLogicalNot(ExpressionNode operand)
    {
        var innerUnary = ExtractUnary(operand);
        var notUnary = new LogicalNotUnaryNode { Operand = innerUnary };
        var mult = new MultiplicativeNode { Left = notUnary };
        var add = new AdditiveNode { Left = mult };
        var rel = new RelationalComparisonRelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        return new AssignmentFormExpressionNode { Assignment = assign };
    }

    /// <summary>Creates a negate expression: -operand.</summary>
    public static ExpressionNode MakeNegate(ExpressionNode operand)
    {
        var innerUnary = ExtractUnary(operand);
        var negUnary = new NegateUnaryNode { Operand = innerUnary };
        var mult = new MultiplicativeNode { Left = negUnary };
        var add = new AdditiveNode { Left = mult };
        var rel = new RelationalComparisonRelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        return new AssignmentFormExpressionNode { Assignment = assign };
    }

    // ─── Depth-bounded expression generator ──────────────────────────────

    /// <summary>
    /// Generates a random expression with bounded depth.
    /// At depth 0, only literals are produced. At higher depths, compound expressions
    /// (binary ops, unary ops) may be generated.
    /// </summary>
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

    // ═══════════════════════════════════════════════════════════════════════
    // Statement generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Creates a typed variable declaration statement.</summary>
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

    /// <summary>Creates an int variable declaration: int name = value;</summary>
    public static StatementNode MakeIntVarDecl(string name, string value = "0")
    {
        return MakeVarDecl(
            new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = new IntTypePrimitiveTypeNode() } },
            name,
            WrapPrimary(new IntegerLiteralExpressionPrimaryNode { IntegerLiteral = Tok(value) }));
    }

    /// <summary>Creates a bool variable declaration: bool name = value;</summary>
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

    /// <summary>Creates a string variable declaration: string name = value;</summary>
    public static StatementNode MakeStringVarDecl(string name, string value = "\"hello\"")
    {
        return MakeVarDecl(
            new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = new StringTypePrimitiveTypeNode() } },
            name,
            WrapPrimary(new StringLiteralExpressionPrimaryNode { StringLiteral = Tok(value) }));
    }

    /// <summary>Creates an expression statement.</summary>
    public static StatementNode MakeExpressionStatement(ExpressionNode expr)
    {
        return new ExpressionStatementStatementNode
        {
            ExpressionStatement = new ExpressionStatementNode { Expression = expr },
        };
    }

    /// <summary>Creates a return statement with an optional value.</summary>
    public static StatementNode MakeReturnStatement(ExpressionNode? value = null)
    {
        return new ReturnStatementStatementNode
        {
            ReturnStatement = new ReturnStatementNode { Value = value },
        };
    }

    /// <summary>Creates an if statement with a bool condition and a then-branch block.</summary>
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

    /// <summary>Creates a while statement with a bool condition and a body block.</summary>
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

    /// <summary>
    /// Generates a random statement with bounded depth.
    /// At depth 0, only simple statements (var decl, return, expression) are produced.
    /// At higher depths, control flow statements (if, while) may be generated.
    /// </summary>
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

    // ═══════════════════════════════════════════════════════════════════════
    // Member declaration generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Creates a method declaration node.</summary>
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

    /// <summary>Creates a field declaration node.</summary>
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

    /// <summary>
    /// Generates a random method declaration with a void return type and simple body.
    /// </summary>
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

    /// <summary>
    /// Generates a random class declaration with a few members.
    /// </summary>
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