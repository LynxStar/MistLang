using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Tests.Helpers;

namespace Mist.Tests.Analysis;

/// <summary>
/// Unit tests for type checker edge cases covering specific diagnostic messages,
/// ErrorType error suppression, numeric widening conversions, and string concatenation.
/// </summary>
public class TypeCheckerEdgeCaseTests
{
    private readonly SemanticModelBuilder builder = new();
    private readonly TypeCheckerAnalyzer analyzer = new();

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static Storm.Runtime.Token MakeToken(string lexeme, int start = 0)
    {
        return new Storm.Runtime.Token(
            kind: 0,
            span: new Storm.Runtime.SourceSpan(start, lexeme.Length),
            start: new Storm.Runtime.LinePosition(1, 1),
            end: new Storm.Runtime.LinePosition(1, 1 + lexeme.Length),
            channel: Storm.Runtime.TokenChannel.Default,
            lexeme: lexeme);
    }

    private static MistCompilation MakeCompilation(string source = "")
    {
        var sourceText = new SourceText("test.mist", source);
        return new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));
    }

    private ISemanticModel BuildModel(Storm.Runtime.SyntaxNode ast, string source = "")
    {
        var compilation = MakeCompilation(source);
        var diagnostics = new DiagnosticBag();
        return builder.Build(compilation, ImmutableArray.Create(ast), ambientTypeResolver: null, namespaceRoot: "TestProject", projectFolder: "/test", diagnostics);
    }

    private (ISemanticModel Model, ImmutableArray<Diagnostic> SemanticDiagnostics) BuildModelWithDiagnostics(
        Storm.Runtime.SyntaxNode ast, string source = "")
    {
        var compilation = MakeCompilation(source);
        var diagnostics = new DiagnosticBag();
        var model = builder.Build(compilation, ImmutableArray.Create(ast), ambientTypeResolver: null, namespaceRoot: "TestProject", projectFolder: "/test", diagnostics);
        return (model, diagnostics.ToImmutableArray());
    }

    private ImmutableArray<Diagnostic> RunTypeChecker(ISemanticModel model)
    {
        var bag = new DiagnosticBag();
        analyzer.Analyze(model, bag);
        return bag.ToImmutableArray();
    }

    // ─── Expression builders ─────────────────────────────────────────────

    private static ExpressionNode MakeIntLiteralExpression(string value = "0")
    {
        var primary = new IntegerLiteralExpressionPrimaryNode
        {
            IntegerLiteral = MakeToken(value),
        };
        return WrapPrimaryAsExpression(primary);
    }

    private static ExpressionNode MakeBoolLiteralExpression(bool value)
    {
        PrimaryNode primary = value
            ? new TrueLiteralExpressionPrimaryNode()
            : new FalseLiteralExpressionPrimaryNode();
        return WrapPrimaryAsExpression(primary);
    }

    private static ExpressionNode MakeStringLiteralExpression(string value = "\"hello\"")
    {
        var primary = new StringLiteralExpressionPrimaryNode
        {
            StringLiteral = MakeToken(value),
        };
        return WrapPrimaryAsExpression(primary);
    }

    private static ExpressionNode MakeIdentifierExpression(string name)
    {
        var primary = new IdentifierExpressionPrimaryNode
        {
            Name = MakeToken(name),
        };
        return WrapPrimaryAsExpression(primary);
    }

    private static ExpressionNode WrapPrimaryAsExpression(PrimaryNode primary)
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

    private static UnaryNode ExtractUnary(ExpressionNode expr)
    {
        var logOr = expr.AsAssignment().Target.Left;
        var logAnd = logOr.Left;
        var equality = logAnd.Left;
        var relational = (RelationalComparisonRelationalNode)equality.Left;
        var additive = relational.Left;
        var multiplicative = additive.Left;
        return multiplicative.Left;
    }

    /// <summary>
    /// Creates an additive expression: left + right or left - right.
    /// </summary>
    private static ExpressionNode MakeAdditiveExpression(
        ExpressionNode left, ExpressionNode right, string op = "+")
    {
        var leftUnary = ExtractUnary(left);
        var rightUnary = ExtractUnary(right);

        var leftMult = new MultiplicativeNode { Left = leftUnary };
        var rightMult = new MultiplicativeNode { Left = rightUnary };

        var add = new AdditiveNode
        {
            Left = leftMult,
            Operator = MakeToken(op),
            Right = rightMult,
        };
        var rel = new RelationalComparisonRelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        return new AssignmentFormExpressionNode { Assignment = assign };
    }

    /// <summary>
    /// Creates a multiplicative expression: left * right.
    /// </summary>
    private static ExpressionNode MakeMultiplicativeExpression(
        ExpressionNode left, ExpressionNode right, string op = "*")
    {
        var leftUnary = ExtractUnary(left);
        var rightUnary = ExtractUnary(right);

        var mult = new MultiplicativeNode
        {
            Left = leftUnary,
            Operator = MakeToken(op),
            Right = rightUnary,
        };
        var add = new AdditiveNode { Left = mult };
        var rel = new RelationalComparisonRelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        return new AssignmentFormExpressionNode { Assignment = assign };
    }

    /// <summary>
    /// Creates an assignment expression: target = value.
    /// </summary>
    private static ExpressionNode MakeAssignmentExpression(string targetName, ExpressionNode value)
    {
        var targetPrimary = new IdentifierExpressionPrimaryNode
        {
            Name = MakeToken(targetName),
        };
        var targetPostfix = new PostfixNode
        {
            Target = targetPrimary,
            Suffix = ImmutableArray<PostfixSuffixNode>.Empty,
        };
        var targetUnary = new PostfixExpressionUnaryNode { Primary = targetPostfix };
        var targetMult = new MultiplicativeNode { Left = targetUnary };
        var targetAdd = new AdditiveNode { Left = targetMult };
        var targetRel = new RelationalComparisonRelationalNode { Left = targetAdd };
        var targetEq = new EqualityNode { Left = targetRel };
        var targetLogAnd = new LogicalAndNode { Left = targetEq };
        var targetLogOr = new LogicalOrNode { Left = targetLogAnd };

        var assign = new AssignmentNode
        {
            Target = new VoidCoalescingNode { Left = targetLogOr },
            Value = value,
        };
        return new AssignmentFormExpressionNode { Assignment = assign };
    }

    // ─── Statement builders ──────────────────────────────────────────────

    private static StatementNode MakeExpressionStatement(ExpressionNode expr)
    {
        return new ExpressionStatementStatementNode
        {
            ExpressionStatement = new ExpressionStatementNode
            {
                Expression = expr,
            },
        };
    }

    private static StatementNode MakeReturnStatement(ExpressionNode? value = null)
    {
        return new ReturnStatementStatementNode
        {
            ReturnStatement = new ReturnStatementNode
            {
                Value = value,
            },
        };
    }

    private static StatementNode MakeTypedVarDecl(TypeReferenceNode typeRef, string name, ExpressionNode initializer)
    {
        return new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                Type = typeRef,
                Name = MakeToken(name),
                Initializer = initializer,
            },
        };
    }

    private static StatementNode MakeIntVarDecl(string name, string value = "0")
    {
        return MakeTypedVarDecl(
            new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = new IntTypePrimitiveTypeNode() } },
            name, MakeIntLiteralExpression(value));
    }

    private static StatementNode MakeStringVarDecl(string name, string value = "\"hello\"")
    {
        return MakeTypedVarDecl(
            new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = new StringTypePrimitiveTypeNode() } },
            name, MakeStringLiteralExpression(value));
    }

    // ─── AST builders ────────────────────────────────────────────────────

    /// <summary>
    /// Creates a program with a static void Main() containing the given statements.
    /// </summary>
    private static CompilationUnitNode MakeProgram(ImmutableArray<StatementNode> mainStatements)
    {
        var mainMethod = new MethodDeclarationNode
        {
            Name = MakeToken("Main"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = mainStatements } },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainMethod }),
        };

        return new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };
    }

    /// <summary>
    /// Creates a program with Main and an additional method.
    /// </summary>
    private static CompilationUnitNode MakeProgramWithMethod(
        string methodName,
        TypeReferenceNode returnType,
        ImmutableArray<StatementNode> methodStatements,
        ParameterListNode? parameters = null)
    {
        var method = new MethodDeclarationNode
        {
            Name = MakeToken(methodName),
            ReturnType = returnType,
            Parameters = parameters,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = methodStatements } },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        var mainMethod = new MethodDeclarationNode
        {
            Name = MakeToken("Main"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode() },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = method },
                new MethodMemberMemberDeclarationNode { Method = mainMethod }),
        };

        return new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Diagnostic message pattern tests (MIST_TC_0001 through MIST_TC_0018)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MIST_TC_0001_AssignmentTypeMismatch()
    {
        // int x = 0; bool b = true; x = b; — assigning bool to int via assignment
        // Build: int x = 0; bool b = true; x = b;
        var assignExpr = MakeAssignmentExpression("x", MakeIdentifierExpression("b"));

        var ast = MakeProgram(ImmutableArray.Create(
            MakeIntVarDecl("x", "0"),
            MakeTypedVarDecl(
                new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = new BoolTypePrimitiveTypeNode() } },
                "b",
                MakeBoolLiteralExpression(true)),
            MakeExpressionStatement(assignExpr)));

        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        Assert.Contains(diagnostics, d =>
            d.Id.Value == "MIST_TC_0001" && d.Message.Contains("Cannot assign"));
    }

    [Fact]
    public void MIST_TC_0003_ArithmeticOnNonNumeric()
    {
        // bool a = true; bool b = false; a + b;
        var ast = MakeProgram(ImmutableArray.Create(
            MakeTypedVarDecl(
                new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = new BoolTypePrimitiveTypeNode() } },
                "a", MakeBoolLiteralExpression(true)),
            MakeTypedVarDecl(
                new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = new BoolTypePrimitiveTypeNode() } },
                "b", MakeBoolLiteralExpression(false)),
            MakeExpressionStatement(MakeAdditiveExpression(
                MakeIdentifierExpression("a"), MakeIdentifierExpression("b"), "+"))));

        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        Assert.Contains(diagnostics, d =>
            d.Id.Value == "MIST_TC_0003" && d.Message.Contains("Operator"));
    }

    [Fact]
    public void MIST_TC_0004_ComparisonOnNonNumeric()
    {
        // bool a = true; bool b = false; a < b;
        var leftUnary = ExtractUnary(MakeIdentifierExpression("a"));
        var rightUnary = ExtractUnary(MakeIdentifierExpression("b"));

        var leftMult = new MultiplicativeNode { Left = leftUnary };
        var rightMult = new MultiplicativeNode { Left = rightUnary };
        var leftAdd = new AdditiveNode { Left = leftMult };
        var rightAdd = new AdditiveNode { Left = rightMult };

        var rel = new RelationalComparisonRelationalNode
        {
            Left = leftAdd,
            Operator = MakeToken("<"),
            Right = rightAdd,
        };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        var expr = new AssignmentFormExpressionNode { Assignment = assign };

        var ast = MakeProgram(ImmutableArray.Create(
            MakeTypedVarDecl(
                new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = new BoolTypePrimitiveTypeNode() } },
                "a", MakeBoolLiteralExpression(true)),
            MakeTypedVarDecl(
                new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = new BoolTypePrimitiveTypeNode() } },
                "b", MakeBoolLiteralExpression(false)),
            MakeExpressionStatement(expr)));

        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        Assert.Contains(diagnostics, d =>
            d.Id.Value == "MIST_TC_0004" && d.Message.Contains("Operator"));
    }

    [Fact]
    public void MIST_TC_0006_LogicalOnNonBool()
    {
        // int a = 1; int b = 2; a && b;
        var leftUnary = ExtractUnary(MakeIdentifierExpression("a"));
        var rightUnary = ExtractUnary(MakeIdentifierExpression("b"));

        var leftMult = new MultiplicativeNode { Left = leftUnary };
        var rightMult = new MultiplicativeNode { Left = rightUnary };
        var leftAdd = new AdditiveNode { Left = leftMult };
        var rightAdd = new AdditiveNode { Left = rightMult };
        var leftRel = new RelationalComparisonRelationalNode { Left = leftAdd };
        var rightRel = new RelationalComparisonRelationalNode { Left = rightAdd };
        var leftEq = new EqualityNode { Left = leftRel };
        var rightEq = new EqualityNode { Left = rightRel };

        var logAnd = new LogicalAndNode
        {
            Left = leftEq,
            Right = rightEq,
        };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assignNode = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        var expr = new AssignmentFormExpressionNode { Assignment = assignNode };

        var ast = MakeProgram(ImmutableArray.Create(
            MakeIntVarDecl("a", "1"),
            MakeIntVarDecl("b", "2"),
            MakeExpressionStatement(expr)));

        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        // After Task 33, operator type checking flows through overload
        // resolution. The diagnostic message no longer mentions "bool" —
        // it reports the operator and the actual operand types.
        Assert.Contains(diagnostics, d =>
            d.Id.Value == "MIST_TC_0006" && d.Message.Contains("Operator"));
    }

    [Fact]
    public void MIST_TC_0007_NonBoolCondition()
    {
        // if (1) { }
        var ast = MakeProgram(ImmutableArray.Create(
            MakeIntVarDecl("x", "1"),
            new IfStatementStatementNode
            {
                IfStatement = new IfStatementNode
                {
                    Condition = MakeIdentifierExpression("x"),
                    ThenBranch = new BlockNode(),
                },
            }));

        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        Assert.Contains(diagnostics, d =>
            d.Id.Value == "MIST_TC_0007" && d.Message.Contains("bool"));
    }

    [Fact]
    public void MIST_TC_0008_ReturnTypeMismatch()
    {
        // int GetValue() { return "hello"; }
        var ast = MakeProgramWithMethod(
            "GetValue",
            new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = new IntTypePrimitiveTypeNode() } },
            ImmutableArray.Create(MakeReturnStatement(MakeStringLiteralExpression("\"hello\""))));

        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        Assert.Contains(diagnostics, d =>
            d.Id.Value == "MIST_TC_0008" && d.Message.Contains("return"));
    }

    [Fact]
    public void MIST_TC_0008_VoidMethodReturnsValue()
    {
        // void M() { return 42; }
        var ast = MakeProgram(ImmutableArray.Create(
            MakeReturnStatement(MakeIntLiteralExpression("42"))));

        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        Assert.Contains(diagnostics, d =>
            d.Id.Value == "MIST_TC_0008" && d.Message.Contains("void"));
    }

    [Fact]
    public void MIST_TC_0017_NoEntryPoint()
    {
        // class C { void Run() { } }
        var method = new MethodDeclarationNode
        {
            Name = MakeToken("Run"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode() },
        };
        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("C"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = method }),
        };
        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };

        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        Assert.Contains(diagnostics, d =>
            d.Id.Value == "MIST_TC_0017" && d.Message.Contains("entry point"));
    }

    [Fact]
    public void MIST_TC_0018_MultipleEntryPoints()
    {
        var mainA = new MethodDeclarationNode
        {
            Name = MakeToken("Main"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode() },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new StaticModifierModifierNode()),
        };
        var mainB = new MethodDeclarationNode
        {
            Name = MakeToken("Main"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode() },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new StaticModifierModifierNode()),
        };

        var classA = new ClassDeclarationNode
        {
            Name = MakeToken("A"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainA }),
        };
        var classB = new ClassDeclarationNode
        {
            Name = MakeToken("B"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainB }),
        };

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classA },
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classB }).AsTopLevelItems(),
        };

        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        Assert.Contains(diagnostics, d =>
            d.Id.Value == "MIST_TC_0018" && d.Message.Contains("multiple entry points"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ErrorType suppresses cascading errors
    // ═══════════════════════════════════════════════════════════════════════

    // Phase B will reintroduce UnknownType as the user-facing top type, distinct from this ErrorType sentinel.
    [Fact]
    public void ErrorType_SuppressesCascadingErrors()
    {
        // Reference an undefined variable, then use it in arithmetic.
        // The undefined variable gets ErrorType, and the arithmetic should NOT
        // produce an additional type error.
        var ast = MakeProgram(ImmutableArray.Create(
            MakeExpressionStatement(MakeAdditiveExpression(
                MakeIdentifierExpression("undefinedVar"),
                MakeIntLiteralExpression("1"), "+"))));

        var (model, semanticDiagnostics) = BuildModelWithDiagnostics(ast);
        var typeCheckerDiagnostics = RunTypeChecker(model);

        // The semantic model should have an undefined identifier diagnostic.
        // But the type checker should NOT produce MIST_TC_0003 for the arithmetic,
        // because the undefined variable has ErrorType which suppresses cascading.
        Assert.DoesNotContain(typeCheckerDiagnostics, d => d.Id.Value == "MIST_TC_0003");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Numeric widening conversions in assignability
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("byte", "int")]
    [InlineData("byte", "long")]
    [InlineData("byte", "float")]
    [InlineData("byte", "double")]
    [InlineData("short", "int")]
    [InlineData("short", "long")]
    [InlineData("int", "long")]
    [InlineData("int", "float")]
    [InlineData("int", "double")]
    [InlineData("int", "decimal")]
    [InlineData("float", "double")]
    public void NumericWidening_AssignmentIsValid(string sourceType, string targetType)
    {
        // sourceType src = 0; targetType dst = 0; dst = src; — widening should be valid
        var sourceTypeRef = MakePrimitiveTypeRef(sourceType);
        var targetTypeRef = MakePrimitiveTypeRef(targetType);

        var assignExpr = MakeAssignmentExpression("dst", MakeIdentifierExpression("src"));

        var ast = MakeProgram(ImmutableArray.Create(
            MakeTypedVarDecl(sourceTypeRef, "src", MakeIntLiteralExpression("0")),
            MakeTypedVarDecl(targetTypeRef, "dst", MakeIntLiteralExpression("0")),
            MakeExpressionStatement(assignExpr)));

        var (model, _) = StdlibTestHelper.BuildModelWithStdlib(ast);
        var diagnostics = RunTypeChecker(model);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0001");
    }

    [Theory]
    [InlineData("int", "byte")]
    [InlineData("long", "int")]
    [InlineData("double", "float")]
    [InlineData("double", "int")]
    public void NumericNarrowing_AssignmentProducesError(string sourceType, string targetType)
    {
        // sourceType src = 0; targetType dst = 0; dst = src; — narrowing should produce error
        var sourceTypeRef = MakePrimitiveTypeRef(sourceType);
        var targetTypeRef = MakePrimitiveTypeRef(targetType);

        var assignExpr = MakeAssignmentExpression("dst", MakeIdentifierExpression("src"));

        var ast = MakeProgram(ImmutableArray.Create(
            MakeTypedVarDecl(sourceTypeRef, "src", MakeIntLiteralExpression("0")),
            MakeTypedVarDecl(targetTypeRef, "dst", MakeIntLiteralExpression("0")),
            MakeExpressionStatement(assignExpr)));

        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        Assert.Contains(diagnostics, d => d.Id.Value == "MIST_TC_0001");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // String concatenation with + operator
    //
    // After Task 33, string concatenation flows through operator overload
    // resolution against Standard.String.operator+ (Requirement 6.8).
    // These unit tests build ASTs manually without loading the stdlib, so
    // the legacy StringType / PrimitiveType instances have no operator
    // members. Operator resolution therefore finds no candidates and emits
    // MIST_TC_0003. The tests below verify that behavior — the "no error"
    // path is now an integration concern tested by the sample regression
    // guard (Task 35) and the full-pipeline integration tests.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void StringConcatenation_StringPlusString_WithoutStdlib_ProducesError()
    {
        // Without stdlib loaded, string + string has no operator member to
        // resolve — overload resolution emits MIST_TC_0003.
        var ast = MakeProgram(ImmutableArray.Create(
            MakeStringVarDecl("a", "\"hello\""),
            MakeStringVarDecl("b", "\" world\""),
            MakeExpressionStatement(MakeAdditiveExpression(
                MakeIdentifierExpression("a"), MakeIdentifierExpression("b"), "+"))));

        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        Assert.Contains(diagnostics, d => d.Id.Value == "MIST_TC_0003");
    }

    [Fact]
    public void StringConcatenation_StringPlusInt_WithoutStdlib_ProducesError()
    {
        // Without stdlib loaded, string + int has no operator member to
        // resolve — overload resolution emits MIST_TC_0003.
        var ast = MakeProgram(ImmutableArray.Create(
            MakeStringVarDecl("a", "\"count: \""),
            MakeIntVarDecl("b", "42"),
            MakeExpressionStatement(MakeAdditiveExpression(
                MakeIdentifierExpression("a"), MakeIdentifierExpression("b"), "+"))));

        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        Assert.Contains(diagnostics, d => d.Id.Value == "MIST_TC_0003");
    }

    [Fact]
    public void StringSubtraction_ProducesError()
    {
        // string - string is not valid
        var ast = MakeProgram(ImmutableArray.Create(
            MakeStringVarDecl("a", "\"hello\""),
            MakeStringVarDecl("b", "\" world\""),
            MakeExpressionStatement(MakeAdditiveExpression(
                MakeIdentifierExpression("a"), MakeIdentifierExpression("b"), "-"))));

        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        Assert.Contains(diagnostics, d => d.Id.Value == "MIST_TC_0003");
    }

    [Fact]
    public void StringMultiplication_ProducesError()
    {
        // string * string is not valid
        var ast = MakeProgram(ImmutableArray.Create(
            MakeStringVarDecl("a", "\"hello\""),
            MakeStringVarDecl("b", "\" world\""),
            MakeExpressionStatement(MakeMultiplicativeExpression(
                MakeIdentifierExpression("a"), MakeIdentifierExpression("b"), "*"))));

        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        Assert.Contains(diagnostics, d => d.Id.Value == "MIST_TC_0003");
    }

    // ─── Helper: create PrimitiveTypeRef from type name ──────────────────

    private static TypeReferenceNode MakePrimitiveTypeRef(string typeName)
    {
        PrimitiveTypeNode primitiveNode = typeName switch
        {
            "int" => new IntTypePrimitiveTypeNode(),
            "uint" => new UintTypePrimitiveTypeNode(),
            "long" => new LongTypePrimitiveTypeNode(),
            "ulong" => new UlongTypePrimitiveTypeNode(),
            "short" => new ShortTypePrimitiveTypeNode(),
            "ushort" => new UshortTypePrimitiveTypeNode(),
            "byte" => new ByteTypePrimitiveTypeNode(),
            "sbyte" => new SbyteTypePrimitiveTypeNode(),
            "float" => new FloatTypePrimitiveTypeNode(),
            "double" => new DoubleTypePrimitiveTypeNode(),
            "decimal" => new DecimalTypePrimitiveTypeNode(),
            "bool" => new BoolTypePrimitiveTypeNode(),
            "char" => new CharTypePrimitiveTypeNode(),
            "string" => new StringTypePrimitiveTypeNode(),
            _ => throw new ArgumentException($"Unknown primitive type: {typeName}"),
        };

        return new TypeReferenceNode
        {
            First = new PrimitiveSingleTypeRefSingleTypeRefNode
            {
                PrimitiveType = primitiveNode,
            }
        };
    }
}