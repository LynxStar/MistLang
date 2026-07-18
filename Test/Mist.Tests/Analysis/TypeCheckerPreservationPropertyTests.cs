// Bugfix: type-checker-constructor-chain-bugs
// Property 2: Preservation — Genuine Constructor Chain Errors and Non-Chain Diagnostics Unchanged
//
// These tests capture EXISTING CORRECT behavior on the UNFIXED code.
// They must PASS on unfixed code, confirming the baseline behavior to preserve.
// After the fix, they must CONTINUE to pass, confirming no regressions.
//
// **Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Preservation property tests for the TypeCheckerAnalyzer constructor chain bugfix.
/// These tests verify that existing correct behavior is maintained after the fix:
/// - Genuine MIST_TC_0013 errors still fire for truly mismatched constructor chains
/// - Single-type valid constructor chaining produces no false positives
/// - Non-constructor-chain diagnostics (MIST_TC_0001, MIST_TC_0008, MIST_TC_0011) are unchanged
/// - FindDeclaringType usage in CheckMethodBody/CheckConstructorBody continues to resolve correctly
/// </summary>
public class TypeCheckerPreservationPropertyTests
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

    private static ExpressionNode WrapPrimaryAsExpression(PrimaryNode primary)
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

    private static ExpressionNode MakeIntLiteralExpression(string value = "0")
    {
        var primary = new IntegerLiteralExpressionPrimaryNode
        {
            IntegerLiteral = MakeToken(value),
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

    private static StatementNode MakeExpressionStatement(ExpressionNode expr)
    {
        return new ExpressionStatementStatementNode
        {
            ExpressionStatement = new ExpressionStatementNode { Expression = expr },
        };
    }

    private static StatementNode MakeReturnStatement(ExpressionNode? value = null)
    {
        return new ReturnStatementStatementNode
        {
            ReturnStatement = new ReturnStatementNode { Value = value },
        };
    }

    private static QualifiedIdentifierNode MakeQualifiedId(string name)
    {
        return new QualifiedIdentifierNode
        {
            IDENTIFIER = MakeToken(name),
            Span = new Storm.Runtime.SourceSpan(0, 0),
        };
    }

    private ImmutableArray<Diagnostic> CompileAndTypeCheck(
        MistCompilation compilation,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts)
    {
        var buildDiagnostics = new DiagnosticBag();
        var model = builder.Build(compilation, asts, ambientTypeResolver: null, namespaceRoot: "TestProject", projectFolder: "/test", buildDiagnostics);
        var tcDiagnostics = new DiagnosticBag();
        analyzer.Analyze(model, tcDiagnostics);
        return tcDiagnostics.ToImmutableArray();
    }

    private ImmutableArray<Diagnostic> CompileSingleAstAndTypeCheck(
        Storm.Runtime.SyntaxNode ast, string source = "")
    {
        var compilation = new MistCompilation(ImmutableArray.Create(
            new CompilationUnit(new SourceText("test.mist", source))));
        return CompileAndTypeCheck(compilation, ImmutableArray.Create(ast));
    }

    // ─── AST builders ────────────────────────────────────────────────────

    /// <summary>
    /// Builds a single-type class with a parameterized constructor and a default
    /// constructor that chains to it. Used for both valid and invalid chaining tests.
    /// </summary>
    private static CompilationUnitNode BuildSingleTypeWithChaining(
        string typeName,
        bool chainMatches,
        string namespaceName = "Test")
    {
        // Primary constructor: TypeName(int x) { }
        var paramX = new ParameterNode
        {
            Type = new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = new IntTypePrimitiveTypeNode() } },
            Name = MakeToken("x"),
        };
        var primaryCtor = new ConstructorDeclarationNode
        {
            Name = MakeToken(typeName),
            Parameters = new ParameterListNode { Parameter = paramX },
            Body = new CallableBlockBodyCallableBodyNode { Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty } },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        // Default constructor with chain:
        // If chainMatches: TypeName() : this(0) { }  — matches TypeName(int)
        // If !chainMatches: TypeName() : this(true) { } — bool doesn't match int
        var chainArg = chainMatches
            ? MakeIntLiteralExpression("0")
            : WrapPrimaryAsExpression(new TrueLiteralExpressionPrimaryNode());

        var defaultCtor = new ConstructorDeclarationNode
        {
            Name = MakeToken(typeName),
            Parameters = null,
            Chain = new ThisChainConstructorChainNode
            {
                Arguments = new ArgumentListNode { Argument = new ValueArgumentArgumentNode { Expression = chainArg } },
                Span = new Storm.Runtime.SourceSpan(50, 15),
            },
            Body = new CallableBlockBodyCallableBodyNode { Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty } },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        var mainMethod = new MethodDeclarationNode
        {
            Name = MakeToken("Main"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new CallableBlockBodyCallableBodyNode { Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty } },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };

        var classDecl = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = MakeToken(typeName),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new ConstructorMemberMemberDeclarationNode { Constructor = primaryCtor },
                new ConstructorMemberMemberDeclarationNode { Constructor = defaultCtor },
                new MethodMemberMemberDeclarationNode { Method = mainMethod }),
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        if(namespaceName.Length > 0)
        {
            return new CompilationUnitNode
            {
                Usings = ImmutableArray<UsingDeclarationNode>.Empty,
                Items = ImmutableArray.Create<TopLevelItemNode>(new NamespaceItemTopLevelItemNode
                {
                    NamespaceDeclaration = new FileScopedNamespaceNamespaceDeclarationNode
                    {
                        Name = MakeQualifiedId(namespaceName),
                        Body = ImmutableArray.Create<TypeDeclarationNode>(
                        new BodyTypeTypeDeclarationNode { TypeBody = classDecl }).AsNamespaceBodyItems(),
                        Span = new Storm.Runtime.SourceSpan(0, 100),
                    }
                }),
            };
        }

        return new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new BodyTypeTypeDeclarationNode { TypeBody = classDecl }).AsTopLevelItems(),
        };
    }

    /// <summary>
    /// Builds a program with a return type mismatch (returns int from void method).
    /// Triggers MIST_TC_0008.
    /// </summary>
    private static CompilationUnitNode BuildReturnTypeMismatchProgram()
    {
        var mainMethod = new MethodDeclarationNode
        {
            Name = MakeToken("Main"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new CallableBlockBodyCallableBodyNode
            {
                Block = new BlockNode
                {
                    Statements = ImmutableArray.Create(
                    MakeReturnStatement(MakeIntLiteralExpression("42"))),
                }
            },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };

        var classDecl = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = MakeToken("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainMethod }),
        };

        return new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new BodyTypeTypeDeclarationNode { TypeBody = classDecl }).AsTopLevelItems(),
        };
    }

    /// <summary>
    /// Builds a program with an assignment type mismatch (bool assigned to int variable).
    /// Triggers MIST_TC_0001.
    /// </summary>
    private static CompilationUnitNode BuildAssignmentMismatchProgram()
    {
        var varDecl = new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                Type = new TypeReferenceNode
                {
                    First = new PrimitiveSingleTypeRefSingleTypeRefNode
                    {
                        PrimitiveType = new IntTypePrimitiveTypeNode(),
                    }
                },
                Name = MakeToken("x"),
                Initializer = WrapPrimaryAsExpression(new TrueLiteralExpressionPrimaryNode()),
            },
        };

        var mainMethod = new MethodDeclarationNode
        {
            Name = MakeToken("Main"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new CallableBlockBodyCallableBodyNode
            {
                Block = new BlockNode
                {
                    Statements = ImmutableArray.Create<StatementNode>(varDecl),
                }
            },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };

        var classDecl = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = MakeToken("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainMethod }),
        };

        return new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new BodyTypeTypeDeclarationNode { TypeBody = classDecl }).AsTopLevelItems(),
        };
    }

    /// <summary>
    /// Builds a program with an object creation mismatch (new Foo(true) but Foo only has Foo(int)).
    /// Triggers MIST_TC_0011.
    /// </summary>
    private static CompilationUnitNode BuildObjectCreationMismatchProgram()
    {
        // Foo class with Foo(int x) constructor
        var paramX = new ParameterNode
        {
            Type = new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = new IntTypePrimitiveTypeNode() } },
            Name = MakeToken("x"),
        };
        var fooCtor = new ConstructorDeclarationNode
        {
            Name = MakeToken("Foo"),
            Parameters = new ParameterListNode { Parameter = paramX },
            Body = new CallableBlockBodyCallableBodyNode { Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty } },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        // new Foo(true) — bool arg, but Foo(int) expected
        var newFooExpr = new ObjectCreationExpressionPrimaryNode
        {
            Type = MakeQualifiedId("Foo"),
            Arguments = new ArgumentListNode
            {
                Argument = new ValueArgumentArgumentNode { Expression = WrapPrimaryAsExpression(new TrueLiteralExpressionPrimaryNode()) },
            },
        };

        var varDecl = new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new InferredVariableDeclarationVariableDeclarationStatementNode
            {
                Name = MakeToken("f"),
                Initializer = WrapPrimaryAsExpression(newFooExpr),
            },
        };

        var mainMethod = new MethodDeclarationNode
        {
            Name = MakeToken("Main"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new CallableBlockBodyCallableBodyNode
            {
                Block = new BlockNode
                {
                    Statements = ImmutableArray.Create<StatementNode>(varDecl),
                }
            },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };

        var fooClass = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = MakeToken("Foo"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new ConstructorMemberMemberDeclarationNode { Constructor = fooCtor },
                new MethodMemberMemberDeclarationNode { Method = mainMethod }),
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        return new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new BodyTypeTypeDeclarationNode { TypeBody = fooClass }).AsTopLevelItems(),
        };
    }

    /// <summary>
    /// Builds a program with a method that uses an operator on non-numeric types.
    /// Triggers MIST_TC_0003.
    /// </summary>
    private static CompilationUnitNode BuildOperatorMismatchProgram()
    {
        var varA = new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                Type = new TypeReferenceNode
                {
                    First = new PrimitiveSingleTypeRefSingleTypeRefNode
                    {
                        PrimitiveType = new BoolTypePrimitiveTypeNode(),
                    }
                },
                Name = MakeToken("a"),
                Initializer = WrapPrimaryAsExpression(new TrueLiteralExpressionPrimaryNode()),
            },
        };

        var varB = new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                Type = new TypeReferenceNode
                {
                    First = new PrimitiveSingleTypeRefSingleTypeRefNode
                    {
                        PrimitiveType = new BoolTypePrimitiveTypeNode(),
                    }
                },
                Name = MakeToken("b"),
                Initializer = WrapPrimaryAsExpression(new FalseLiteralExpressionPrimaryNode()),
            },
        };

        // a + b — bool + bool is not valid
        var leftUnary = ExtractUnary(MakeIdentifierExpression("a"));
        var rightUnary = ExtractUnary(MakeIdentifierExpression("b"));
        var leftMult = new MultiplicativeNode { Left = new NonRangeRangeNode { Operand = leftUnary } };
        var rightMult = new MultiplicativeNode { Left = new NonRangeRangeNode { Operand = rightUnary } };
        var add = new AdditiveNode
        {
            Left = leftMult,
            Operator = MakeToken("+"),
            Right = rightMult,
        };
        var rel = new RelationalComparisonRelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        var addExpr = new AssignmentFormExpressionNode { Assignment = assign };

        var mainMethod = new MethodDeclarationNode
        {
            Name = MakeToken("Main"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new CallableBlockBodyCallableBodyNode
            {
                Block = new BlockNode
                {
                    Statements = ImmutableArray.Create(
                    varA, varB,
                    MakeExpressionStatement(addExpr)),
                }
            },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };

        var classDecl = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = MakeToken("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainMethod }),
        };

        return new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new BodyTypeTypeDeclarationNode { TypeBody = classDecl }).AsTopLevelItems(),
        };
    }

    private static UnaryNode ExtractUnary(ExpressionNode expr)
    {
        var logOr = expr.AsAssignment().Target.Left;
        var logAnd = logOr.Left;
        var equality = logAnd.Left;
        var relational = (RelationalComparisonRelationalNode)equality.Left;
        var additive = relational.Left;
        var multiplicative = additive.Left;

        // Constructed expressions always use the pass-through (NonRange) range form.
        return multiplicative.Left.UnwrapRange()!;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 3: Preservation — Genuine Constructor Chain Errors Still Fire
    // **Validates: Requirements 3.1**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Represents a scenario for genuine constructor chain mismatch testing.
    /// </summary>
#pragma warning disable CA1034
    public record GenuineMismatchScenario(string Label, string TypeName)
    {
        public override string ToString()
        {
            return Label;
        }
    }
#pragma warning restore CA1034

    private static readonly GenuineMismatchScenario[] GenuineMismatchScenarios =
    [
        new("Foo_BoolToInt_Mismatch", "Foo"),
        new("Bar_BoolToInt_Mismatch", "Bar"),
        new("Widget_BoolToInt_Mismatch", "Widget"),
    ];

    public static Arbitrary<GenuineMismatchScenario> ArbitraryGenuineMismatchScenarios()
    {
        var gen = Gen.Elements(GenuineMismatchScenarios);
        return gen.ToArbitrary();
    }

    /// <summary>
    /// Property 3: For all constructor chains where the declaring type genuinely has no
    /// matching constructor, MIST_TC_0013 fires with the correct type name.
    ///
    /// On UNFIXED code: PASSES — single-type programs correctly detect mismatched chains.
    /// After fix: MUST STILL PASS — genuine errors are preserved.
    /// </summary>
    [Property(Arbitrary = new[] { typeof(TypeCheckerPreservationPropertyTests) }, MaxTest = 9)]
    public bool GenuineConstructorChainMismatch_EmitsMIST_TC_0013_WithCorrectTypeName(
        GenuineMismatchScenario scenario)
    {
        // Build a single-type program where the chain does NOT match
        var ast = BuildSingleTypeWithChaining(scenario.TypeName, chainMatches: false);
        var diagnostics = CompileSingleAstAndTypeCheck(ast);

        // MIST_TC_0013 must fire
        var tc0013 = diagnostics
            .Where(d => d.Id.Value == "MIST_TC_0013")
            .ToList();

        if(tc0013.Count == 0)
        {
            return false;
        }

        // The error message must reference the correct type name
        return tc0013.All(d => d.Message.Contains(scenario.TypeName));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 3 (continued): Single-type valid chaining produces no false positive
    // **Validates: Requirements 3.1**
    // ═══════════════════════════════════════════════════════════════════════

#pragma warning disable CA1034
    public record ValidChainingScenario(string Label, string TypeName)
    {
        public override string ToString()
        {
            return Label;
        }
    }
#pragma warning restore CA1034

    private static readonly ValidChainingScenario[] ValidChainingScenarios =
    [
        new("Foo_ValidChain", "Foo"),
        new("Bar_ValidChain", "Bar"),
        new("Widget_ValidChain", "Widget"),
    ];

    public static Arbitrary<ValidChainingScenario> ArbitraryValidChainingScenarios()
    {
        var gen = Gen.Elements(ValidChainingScenarios);
        return gen.ToArbitrary();
    }

    /// <summary>
    /// Property 3 (continued): For all single-type compilations with valid constructor
    /// chaining, no MIST_TC_0013 is emitted.
    ///
    /// On UNFIXED code: PASSES — single-type programs with valid chains work correctly.
    /// After fix: MUST STILL PASS — valid chains remain valid.
    /// </summary>
    [Property(Arbitrary = new[] { typeof(TypeCheckerPreservationPropertyTests) }, MaxTest = 9)]
    public bool SingleType_ValidConstructorChaining_NoMIST_TC_0013(
        ValidChainingScenario scenario)
    {
        // Build a single-type program where the chain DOES match
        var ast = BuildSingleTypeWithChaining(scenario.TypeName, chainMatches: true);
        var diagnostics = CompileSingleAstAndTypeCheck(ast);

        // No MIST_TC_0013 should be emitted
        var tc0013 = diagnostics
            .Where(d => d.Id.Value == "MIST_TC_0013")
            .ToList();

        return tc0013.Count == 0;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 4: Preservation — Non-Constructor-Chain Diagnostics Unchanged
    // **Validates: Requirements 3.2, 3.3, 3.4, 3.5**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Represents a non-constructor-chain diagnostic scenario.
    /// </summary>
#pragma warning disable CA1034
    public record NonChainDiagnosticScenario(
        string Label,
        Func<CompilationUnitNode> AstBuilder,
        string ExpectedDiagnosticId,
        string ExpectedMessageFragment)
    {
        public override string ToString()
        {
            return Label;
        }
    }
#pragma warning restore CA1034

    private static readonly NonChainDiagnosticScenario[] NonChainDiagnosticScenarios =
    [
        // MIST_TC_0001: Assignment type mismatch (bool → int)
        new("AssignmentMismatch_BoolToInt",
            BuildAssignmentMismatchProgram,
            "MIST_TC_0001",
            "Cannot implicitly convert"),

        // MIST_TC_0008: Return type mismatch (int from void method)
        new("ReturnTypeMismatch_IntFromVoid",
            BuildReturnTypeMismatchProgram,
            "MIST_TC_0008",
            "Cannot return"),

        // MIST_TC_0013: Object creation mismatch (new Foo(true) but Foo(int))
        new("ObjectCreationMismatch_BoolToInt",
            BuildObjectCreationMismatchProgram,
            "MIST_TC_0013",
            "No constructor"),

        // MIST_TC_0003: Operator type mismatch (bool + bool)
        new("OperatorMismatch_BoolAddition",
            BuildOperatorMismatchProgram,
            "MIST_TC_0003",
            "Operator"),
    ];

    public static Arbitrary<NonChainDiagnosticScenario> ArbitraryNonChainDiagnosticScenarios()
    {
        var gen = Gen.Elements(NonChainDiagnosticScenarios);
        return gen.ToArbitrary();
    }

    /// <summary>
    /// Property 4: For all non-constructor-chain type checking (method bodies, field
    /// declarations, object creation, operators, assignments, entry points), the same
    /// set of diagnostic IDs and messages are produced.
    ///
    /// On UNFIXED code: PASSES — these diagnostics work correctly.
    /// After fix: MUST STILL PASS — non-chain diagnostics are unchanged.
    /// </summary>
    [Property(Arbitrary = new[] { typeof(TypeCheckerPreservationPropertyTests) }, MaxTest = 12)]
    public bool NonConstructorChainDiagnostics_ProduceExpectedIdAndMessage(
        NonChainDiagnosticScenario scenario)
    {
        var ast = scenario.AstBuilder();
        var diagnostics = CompileSingleAstAndTypeCheck(ast);

        // The expected diagnostic ID must be present
        var matchingDiagnostics = diagnostics
            .Where(d => d.Id.Value == scenario.ExpectedDiagnosticId)
            .ToList();

        if(matchingDiagnostics.Count == 0)
        {
            return false;
        }

        // The message must contain the expected fragment
        return matchingDiagnostics.Any(d => d.Message.Contains(scenario.ExpectedMessageFragment));
    }

    /// <summary>
    /// Property 4 (continued): All non-constructor-chain diagnostics are emitted by
    /// the TypeChecker analyzer (AnalyzerName == "TypeChecker").
    ///
    /// On UNFIXED code: PASSES.
    /// After fix: MUST STILL PASS.
    /// </summary>
    [Property(Arbitrary = new[] { typeof(TypeCheckerPreservationPropertyTests) }, MaxTest = 12)]
    public bool NonConstructorChainDiagnostics_HaveCorrectAnalyzerName(
        NonChainDiagnosticScenario scenario)
    {
        var ast = scenario.AstBuilder();
        var diagnostics = CompileSingleAstAndTypeCheck(ast);

        var matchingDiagnostics = diagnostics
            .Where(d => d.Id.Value == scenario.ExpectedDiagnosticId)
            .ToList();

        if(matchingDiagnostics.Count == 0)
        {
            return false;
        }

        return matchingDiagnostics.All(d => d.AnalyzerName == "TypeChecker");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 4 (continued): FindDeclaringType in CheckMethodBody/CheckConstructorBody
    // **Validates: Requirements 3.5**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Represents a scenario verifying FindDeclaringType resolves correctly
    /// for method bodies and constructor bodies in single-type programs.
    /// </summary>
#pragma warning disable CA1034
    public record FindDeclaringTypeScenario(string Label, string TypeName)
    {
        public override string ToString()
        {
            return Label;
        }
    }
#pragma warning restore CA1034

    private static readonly FindDeclaringTypeScenario[] FindDeclaringTypeScenarios =
    [
        new("Foo_MethodAndCtor", "Foo"),
        new("Bar_MethodAndCtor", "Bar"),
    ];

    public static Arbitrary<FindDeclaringTypeScenario> ArbitraryFindDeclaringTypeScenarios()
    {
        var gen = Gen.Elements(FindDeclaringTypeScenarios);
        return gen.ToArbitrary();
    }

    /// <summary>
    /// Property 4 (continued): FindDeclaringType usage in CheckMethodBody and
    /// CheckConstructorBody continues to resolve correctly. A well-typed single-type
    /// program with a constructor and method body produces no type checker errors.
    ///
    /// On UNFIXED code: PASSES — FindDeclaringType works correctly for single-type programs.
    /// After fix: MUST STILL PASS — FindDeclaringType behavior is unchanged.
    /// </summary>
    [Property(Arbitrary = new[] { typeof(TypeCheckerPreservationPropertyTests) }, MaxTest = 6)]
    public bool FindDeclaringType_ResolvesCorrectly_InMethodAndConstructorBodies(
        FindDeclaringTypeScenario scenario)
    {
        // Build a well-typed single-type program with constructor chaining and a method
        var ast = BuildSingleTypeWithChaining(scenario.TypeName, chainMatches: true);
        var diagnostics = CompileSingleAstAndTypeCheck(ast);

        // No type checker errors should be emitted for a well-typed program
        var errors = diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error
                        && d.AnalyzerName == "TypeChecker")
            .ToList();

        return errors.Count == 0;
    }
}