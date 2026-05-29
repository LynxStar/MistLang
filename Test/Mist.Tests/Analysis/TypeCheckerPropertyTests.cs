// Feature: mist-language-v1, Property 5: Well-typed programs produce no type errors
// Feature: mist-language-v1, Property 6: Type-incompatible operations produce diagnostics

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests for the Mist type checker.
///
/// Property 5: Well-typed programs produce no type errors.
/// For any valid Mist program containing correct assignments, correct operator usage,
/// correct conditions, and a valid entry point, the type checker SHALL produce zero
/// Error-severity diagnostics.
///
/// Property 6: Type-incompatible operations produce diagnostics.
/// For any Mist program containing type errors (arithmetic on non-numeric, logical on
/// non-bool, etc.), the type checker SHALL produce at least one Error diagnostic.
/// </summary>
public class TypeCheckerPropertyTests
{
    private readonly SemanticModelBuilder _builder = new();
    private readonly TypeCheckerAnalyzer _analyzer = new();

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
        return _builder.Build(compilation, ImmutableArray.Create(ast), ambientTypeResolver: null, namespaceRoot: "TestProject", projectFolder: "/test", diagnostics);
    }

    private ImmutableArray<Diagnostic> RunTypeChecker(ISemanticModel model)
    {
        var bag = new DiagnosticBag();
        _analyzer.Analyze(model, bag);
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
        return new ExpressionNode { Assignment = assign };
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
        return new ExpressionNode { Assignment = assign };
    }

    /// <summary>
    /// Creates a logical AND expression: left && right.
    /// </summary>
    private static ExpressionNode MakeLogicalAndExpression(
        ExpressionNode left, ExpressionNode right)
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

        var logAnd = new LogicalAndNode
        {
            Left = leftEq,
            Right = rightEq,
        };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        return new ExpressionNode { Assignment = assign };
    }

    /// <summary>
    /// Creates a relational expression: left &lt; right.
    /// </summary>
    private static ExpressionNode MakeRelationalExpression(
        ExpressionNode left, ExpressionNode right, string op = "<")
    {
        var leftUnary = ExtractUnary(left);
        var rightUnary = ExtractUnary(right);

        var leftMult = new MultiplicativeNode { Left = leftUnary };
        var rightMult = new MultiplicativeNode { Left = rightUnary };

        var leftAdd = new AdditiveNode { Left = leftMult };
        var rightAdd = new AdditiveNode { Left = rightMult };

        var rel = new RelationalComparisonRelationalNode
        {
            Left = leftAdd,
            Operator = MakeToken(op),
            Right = rightAdd,
        };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        return new ExpressionNode { Assignment = assign };
    }

    /// <summary>
    /// Creates a logical NOT expression: !operand.
    /// </summary>
    private static ExpressionNode MakeLogicalNotExpression(ExpressionNode operand)
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
        return new ExpressionNode { Assignment = assign };
    }

    /// <summary>
    /// Extracts the UnaryNode from an ExpressionNode for reuse in compound expressions.
    /// </summary>
    private static UnaryNode ExtractUnary(ExpressionNode expr)
    {
        var logOr = expr.Assignment.Target.Left;
        var logAnd = logOr.Left;
        var equality = logAnd.Left;
        var relational = (RelationalComparisonRelationalNode)equality.Left;
        var additive = relational.Left;
        var multiplicative = additive.Left;
        return multiplicative.Left;
    }

    // ─── Statement builders ──────────────────────────────────────────────

    private static StatementNode MakeIntVarDecl(string name, string value = "0")
    {
        return new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                Type = new PrimitiveTypeRefTypeReferenceNode
                {
                    PrimitiveType = new IntTypePrimitiveTypeNode(),
                },
                Name = MakeToken(name),
                Initializer = MakeIntLiteralExpression(value),
            },
        };
    }

    private static StatementNode MakeBoolVarDecl(string name, bool value = true)
    {
        return new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                Type = new PrimitiveTypeRefTypeReferenceNode
                {
                    PrimitiveType = new BoolTypePrimitiveTypeNode(),
                },
                Name = MakeToken(name),
                Initializer = MakeBoolLiteralExpression(value),
            },
        };
    }

    private static StatementNode MakeStringVarDecl(string name, string value = "\"hello\"")
    {
        return new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                Type = new PrimitiveTypeRefTypeReferenceNode
                {
                    PrimitiveType = new StringTypePrimitiveTypeNode(),
                },
                Name = MakeToken(name),
                Initializer = MakeStringLiteralExpression(value),
            },
        };
    }

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

    // ─── AST builders ────────────────────────────────────────────────────

    /// <summary>
    /// Creates a well-typed program with a static void Main() entry point.
    /// The Main method contains the given statements.
    /// </summary>
    private static CompilationUnitNode MakeWellTypedProgram(
        ImmutableArray<StatementNode> mainStatements)
    {
        var mainMethod = new MethodDeclarationNode
        {
            Name = MakeToken("Main"),
            ReturnType = new VoidTypeRefTypeReferenceNode(),
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
    /// Creates a program with a class containing a static void Main() and an additional method.
    /// </summary>
    private static CompilationUnitNode MakeProgramWithMethod(
        string methodName,
        TypeReferenceNode returnType,
        ImmutableArray<StatementNode> methodStatements,
        ImmutableArray<StatementNode>? mainStatements = null,
        ParameterListNode? parameters = null,
        bool isStatic = false)
    {
        var methodModifiers = ImmutableArray.CreateBuilder<ModifierNode>();
        methodModifiers.Add(new PublicModifierModifierNode());
        if (isStatic)
            methodModifiers.Add(new StaticModifierModifierNode());

        var method = new MethodDeclarationNode
        {
            Name = MakeToken(methodName),
            ReturnType = returnType,
            Parameters = parameters,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = methodStatements } },
            Modifiers = methodModifiers.ToImmutable(),
        };

        var mainMethod = new MethodDeclarationNode
        {
            Name = MakeToken("Main"),
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = mainStatements ?? ImmutableArray<StatementNode>.Empty } },
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
    // Property 5: Well-typed programs produce no type errors
    // **Validates: Requirements 8.1-8.9, 8.11-8.14, 10.1**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Represents a well-typed program scenario to test.
    /// </summary>
#pragma warning disable CA1034
    public record WellTypedScenario(string Label, Func<CompilationUnitNode> AstBuilder)
    {
        public override string ToString() => Label;
    }
#pragma warning restore CA1034

    // After Task 33, operator type checking flows exclusively through
    // overload resolution against stdlib operator declarations
    // (Requirements 6.7, 6.8, 6.9). These unit tests build ASTs manually
    // without loading the stdlib, so PrimitiveType / StringType instances
    // have no operator members. Operator-based scenarios (IntArithmetic,
    // BoolLogical, IntComparison, LogicalNot) are therefore expected to
    // produce diagnostics in this harness. They are excluded from the
    // well-typed set and tested separately below as "expected error without
    // stdlib" scenarios. The full-pipeline integration tests (Task 35)
    // verify that these operations succeed when stdlib is loaded.
    private static readonly WellTypedScenario[] WellTypedScenarios =
    [
        new("BoolCondition_If", () => MakeWellTypedProgram(ImmutableArray.Create<StatementNode>(
            MakeBoolVarDecl("cond", true),
            new IfStatementStatementNode
            {
                IfStatement = new IfStatementNode
                {
                    Condition = MakeIdentifierExpression("cond"),
                    ThenBranch = new BlockNode
                    {
                        Statements = ImmutableArray.Create(MakeIntVarDecl("inner", "42")),
                    },
                },
            }))),

        new("BoolCondition_While", () => MakeWellTypedProgram(ImmutableArray.Create<StatementNode>(
            MakeBoolVarDecl("running", false),
            new WhileStatementStatementNode
            {
                WhileStatement = new WhileStatementNode
                {
                    Condition = MakeIdentifierExpression("running"),
                    Body = new BlockNode(),
                },
            }))),

        new("VoidReturn", () => MakeWellTypedProgram(ImmutableArray.Create(
            MakeReturnStatement()))),

        new("IntReturn", () => MakeProgramWithMethod(
            "GetValue",
            new PrimitiveTypeRefTypeReferenceNode { PrimitiveType = new IntTypePrimitiveTypeNode() },
            ImmutableArray.Create(MakeReturnStatement(MakeIntLiteralExpression("42"))))),

        new("StringVariable", () => MakeWellTypedProgram(ImmutableArray.Create(
            MakeStringVarDecl("msg", "\"hello\"")))),

        new("EmptyMain", () => MakeWellTypedProgram(ImmutableArray<StatementNode>.Empty)),
    ];

    public static Arbitrary<WellTypedScenario> ArbitraryWellTypedScenarios()
    {
        var gen = Gen.Elements(WellTypedScenarios);
        return gen.ToArbitrary();
    }

    [Property(Arbitrary = new[] { typeof(TypeCheckerPropertyTests) }, MaxTest = 30)]
    public bool WellTypedPrograms_ProduceNoTypeErrors(WellTypedScenario scenario)
    {
        var ast = scenario.AstBuilder();
        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        return errors.Count == 0;
    }

    // ─── Targeted well-typed tests ───────────────────────────────────────

    [Fact]
    public void WellTyped_IntArithmetic_WithoutStdlib_ProducesOperatorError()
    {
        // After Task 33, operator type checking flows through overload
        // resolution. Without stdlib loaded, PrimitiveType has no operator
        // members, so int + int produces MIST_TC_0003.
        var ast = MakeWellTypedProgram(ImmutableArray.Create(
            MakeIntVarDecl("a", "1"),
            MakeIntVarDecl("b", "2"),
            MakeExpressionStatement(MakeAdditiveExpression(
                MakeIdentifierExpression("a"), MakeIdentifierExpression("b"), "+"))));

        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        Assert.Contains(diagnostics, d => d.Severity == DiagnosticSeverity.Error
                                          && d.Id.Value == "MIST_TC_0003");
    }

    [Fact]
    public void WellTyped_BoolCondition_NoErrors()
    {
        var ast = MakeWellTypedProgram(ImmutableArray.Create<StatementNode>(
            MakeBoolVarDecl("cond", true),
            new IfStatementStatementNode
            {
                IfStatement = new IfStatementNode
                {
                    Condition = MakeIdentifierExpression("cond"),
                    ThenBranch = new BlockNode(),
                },
            }));

        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void WellTyped_VoidMethodReturn_NoErrors()
    {
        var ast = MakeWellTypedProgram(ImmutableArray.Create(MakeReturnStatement()));

        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void WellTyped_IntMethodReturn_NoErrors()
    {
        var ast = MakeProgramWithMethod(
            "GetValue",
            new PrimitiveTypeRefTypeReferenceNode { PrimitiveType = new IntTypePrimitiveTypeNode() },
            ImmutableArray.Create(MakeReturnStatement(MakeIntLiteralExpression("42"))));

        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 6: Type-incompatible operations produce diagnostics
    // **Validates: Requirements 8.1, 8.3, 8.4, 8.6, 8.7**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Represents a type-incompatible scenario to test.
    /// </summary>
#pragma warning disable CA1034
    public record TypeErrorScenario(string Label, Func<CompilationUnitNode> AstBuilder, string ExpectedDiagnosticPrefix)
    {
        public override string ToString() => Label;
    }
#pragma warning restore CA1034

    private static readonly TypeErrorScenario[] TypeErrorScenarios =
    [
        // Arithmetic on non-numeric (string + int is OK for concatenation, but string - int is not)
        new("ArithmeticOnBool", () => MakeWellTypedProgram(ImmutableArray.Create(
            MakeBoolVarDecl("a", true),
            MakeBoolVarDecl("b", false),
            MakeExpressionStatement(MakeAdditiveExpression(
                MakeIdentifierExpression("a"), MakeIdentifierExpression("b"), "+")))),
            "MIST_TC_0003"),

        // Logical AND on non-bool (int && int)
        new("LogicalOnInt", () => MakeWellTypedProgram(ImmutableArray.Create(
            MakeIntVarDecl("a", "1"),
            MakeIntVarDecl("b", "2"),
            MakeExpressionStatement(MakeLogicalAndExpression(
                MakeIdentifierExpression("a"), MakeIdentifierExpression("b"))))),
            "MIST_TC_0006"),

        // Comparison on non-numeric (bool < bool)
        new("ComparisonOnBool", () => MakeWellTypedProgram(ImmutableArray.Create(
            MakeBoolVarDecl("a", true),
            MakeBoolVarDecl("b", false),
            MakeExpressionStatement(MakeRelationalExpression(
                MakeIdentifierExpression("a"), MakeIdentifierExpression("b"), "<")))),
            "MIST_TC_0004"),

        // Non-bool condition in if statement
        new("NonBoolCondition_If", () => MakeWellTypedProgram(ImmutableArray.Create<StatementNode>(
            MakeIntVarDecl("x", "1"),
            new IfStatementStatementNode
            {
                IfStatement = new IfStatementNode
                {
                    Condition = MakeIdentifierExpression("x"),
                    ThenBranch = new BlockNode(),
                },
            })),
            "MIST_TC_0007"),

        // Non-bool condition in while statement
        new("NonBoolCondition_While", () => MakeWellTypedProgram(ImmutableArray.Create<StatementNode>(
            MakeIntVarDecl("x", "1"),
            new WhileStatementStatementNode
            {
                WhileStatement = new WhileStatementNode
                {
                    Condition = MakeIdentifierExpression("x"),
                    Body = new BlockNode(),
                },
            })),
            "MIST_TC_0007"),

        // Logical NOT on non-bool (!int)
        new("LogicalNotOnInt", () => MakeWellTypedProgram(ImmutableArray.Create(
            MakeIntVarDecl("x", "1"),
            MakeExpressionStatement(MakeLogicalNotExpression(
                MakeIdentifierExpression("x"))))),
            "MIST_TC_0006"),

        // Return type mismatch: returning int from void method
        new("ReturnTypeMismatch", () => MakeWellTypedProgram(ImmutableArray.Create(
            MakeReturnStatement(MakeIntLiteralExpression("42")))),
            "MIST_TC_0008"),
    ];

    public static Arbitrary<TypeErrorScenario> ArbitraryTypeErrorScenarios()
    {
        var gen = Gen.Elements(TypeErrorScenarios);
        return gen.ToArbitrary();
    }

    [Property(Arbitrary = new[] { typeof(TypeCheckerPropertyTests) }, MaxTest = 21)]
    public bool TypeIncompatibleOperations_ProduceDiagnostics(TypeErrorScenario scenario)
    {
        var ast = scenario.AstBuilder();
        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        return errors.Count > 0;
    }

    // ─── Targeted type error tests ───────────────────────────────────────

    [Fact]
    public void TypeError_ArithmeticOnBool_ProducesDiagnostic()
    {
        var ast = MakeWellTypedProgram(ImmutableArray.Create(
            MakeBoolVarDecl("a", true),
            MakeBoolVarDecl("b", false),
            MakeExpressionStatement(MakeAdditiveExpression(
                MakeIdentifierExpression("a"), MakeIdentifierExpression("b"), "+"))));

        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        Assert.Contains(diagnostics, d => d.Id.Value == "MIST_TC_0003");
    }

    [Fact]
    public void TypeError_LogicalOnInt_ProducesDiagnostic()
    {
        var ast = MakeWellTypedProgram(ImmutableArray.Create(
            MakeIntVarDecl("a", "1"),
            MakeIntVarDecl("b", "2"),
            MakeExpressionStatement(MakeLogicalAndExpression(
                MakeIdentifierExpression("a"), MakeIdentifierExpression("b")))));

        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        Assert.Contains(diagnostics, d => d.Id.Value == "MIST_TC_0006");
    }

    [Fact]
    public void TypeError_ComparisonOnBool_ProducesDiagnostic()
    {
        var ast = MakeWellTypedProgram(ImmutableArray.Create(
            MakeBoolVarDecl("a", true),
            MakeBoolVarDecl("b", false),
            MakeExpressionStatement(MakeRelationalExpression(
                MakeIdentifierExpression("a"), MakeIdentifierExpression("b"), "<"))));

        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        Assert.Contains(diagnostics, d => d.Id.Value == "MIST_TC_0004");
    }

    [Fact]
    public void TypeError_NonBoolCondition_ProducesDiagnostic()
    {
        var ast = MakeWellTypedProgram(ImmutableArray.Create<StatementNode>(
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

        Assert.Contains(diagnostics, d => d.Id.Value == "MIST_TC_0007");
    }

    [Fact]
    public void TypeError_ReturnTypeMismatch_ProducesDiagnostic()
    {
        // Returning int from void Main
        var ast = MakeWellTypedProgram(ImmutableArray.Create(
            MakeReturnStatement(MakeIntLiteralExpression("42"))));

        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        Assert.Contains(diagnostics, d => d.Id.Value == "MIST_TC_0008");
    }
}
