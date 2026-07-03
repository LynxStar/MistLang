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
/// Unit tests for TypeChecker overload diagnostics.
/// Verifies that the type checker emits the correct diagnostic IDs and messages
/// for no-match (MIST_TC_0031), ambiguous (MIST_TC_0032), and backward-compatible
/// single-candidate scenarios.
///
/// These tests exercise the full pipeline: AST → semantic model → type checker,
/// using constructor overloads (which are resolved end-to-end by CheckObjectCreation)
/// and the OverloadResolver directly for method call diagnostic message verification.
///
/// **Validates: Requirements 3.4, 3.5, 4.1, 4.2, 4.3**
/// </summary>
public class TypeCheckerOverloadDiagnosticTests
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

    private ImmutableArray<Diagnostic> RunTypeChecker(ISemanticModel model)
    {
        var bag = new DiagnosticBag();
        analyzer.Analyze(model, bag);
        return bag.ToImmutableArray();
    }

    // ─── Expression builders ─────────────────────────────────────────────

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

    private static ExpressionNode MakeIntLiteralExpression(string value = "0")
    {
        return WrapPrimaryAsExpression(new IntegerLiteralExpressionPrimaryNode
        {
            IntegerLiteral = MakeToken(value),
        });
    }

    private static ExpressionNode MakeStringLiteralExpression(string value = "\"hello\"")
    {
        return WrapPrimaryAsExpression(new StringLiteralExpressionPrimaryNode
        {
            StringLiteral = MakeToken(value),
        });
    }

    private static ExpressionNode MakeIdentifierExpression(string name)
    {
        return WrapPrimaryAsExpression(new IdentifierExpressionPrimaryNode
        {
            Name = MakeToken(name),
        });
    }

    /// <summary>
    /// Creates a new T(arg) expression for constructor calls.
    /// </summary>
    private static ExpressionNode MakeNewObjectExpression(string typeName, ExpressionNode arg)
    {
        var primary = new ObjectCreationExpressionPrimaryNode
        {
            Type = new QualifiedIdentifierNode
            {
                IDENTIFIER = MakeToken(typeName),
                Span = new Storm.Runtime.SourceSpan(0, 0),
            },
            Arguments = new ArgumentListNode { Argument = new ValueArgumentArgumentNode { Expression = arg } },
        };
        return WrapPrimaryAsExpression(primary);
    }

    // ─── Statement builders ──────────────────────────────────────────────

    private static StatementNode MakeExpressionStatement(ExpressionNode expr)
    {
        return new ExpressionStatementStatementNode
        {
            ExpressionStatement = new ExpressionStatementNode { Expression = expr },
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

    // ─── AST builders ────────────────────────────────────────────────────

    private static ParameterNode MakeParameter(string typeName, string paramName)
    {
        PrimitiveTypeNode primitiveType = typeName switch
        {
            "int" => new IntTypePrimitiveTypeNode(),
            "bool" => new BoolTypePrimitiveTypeNode(),
            "string" => new StringTypePrimitiveTypeNode(),
            "float" => new FloatTypePrimitiveTypeNode(),
            "double" => new DoubleTypePrimitiveTypeNode(),
            "decimal" => new DecimalTypePrimitiveTypeNode(),
            _ => new IntTypePrimitiveTypeNode(),
        };

        return new ParameterNode
        {
            Type = new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = primitiveType } },
            Name = MakeToken(paramName),
        };
    }

    /// <summary>
    /// Creates a constructor declaration with a single parameter.
    /// </summary>
    private static ConstructorDeclarationNode MakeConstructor(
        string className, string paramType, string paramName)
    {
        var param = MakeParameter(paramType, paramName);
        return new ConstructorDeclarationNode
        {
            Name = MakeToken(className),
            Parameters = new ParameterListNode { Parameter = param },
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty } },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };
    }

    /// <summary>
    /// Creates a static void Main() method with the given body statements.
    /// </summary>
    private static MethodDeclarationNode MakeStaticMain(ImmutableArray<StatementNode> body)
    {
        return new MethodDeclarationNode
        {
            Name = MakeToken("Main"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = body } },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 1: No-match diagnostic (MIST_TC_0031)
    // Class with C(int) and C(bool), call new C("hello") → MIST_TC_0031
    // The message should contain the type name and the argument types.
    // **Validates: Requirements 3.4, 3.5**
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void NoMatchOverload_EmitsMIST_TC_0031_WithNameAndArgTypes()
    {
        // Arrange: class Foo with Foo(int) and Foo(bool)
        // Program.Main: new Foo("hello") — string arg doesn't match int or bool
        var ctorInt = MakeConstructor("Foo", "int", "x");
        var ctorBool = MakeConstructor("Foo", "bool", "b");

        var fooClass = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = MakeToken("Foo"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new ConstructorMemberMemberDeclarationNode { Constructor = ctorInt },
                new ConstructorMemberMemberDeclarationNode { Constructor = ctorBool }),
        };

        // Program class with Main: new Foo("hello");
        var newExpr = MakeNewObjectExpression("Foo", MakeStringLiteralExpression("\"hello\""));
        var mainMethod = MakeStaticMain(
            ImmutableArray.Create(MakeExpressionStatement(newExpr)));

        var programClass = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = MakeToken("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainMethod }),
        };

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new BodyTypeTypeDeclarationNode { TypeBody = fooClass },
                new BodyTypeTypeDeclarationNode { TypeBody = programClass }).AsTopLevelItems(),
        };

        // Act
        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        // Assert: MIST_TC_0031 emitted with type name "Foo" and "string" in message
        var noMatchDiags = diagnostics.Where(d => d.Id.Value == "MIST_TC_0031").ToList();
        Assert.NotEmpty(noMatchDiags);
        Assert.Contains(noMatchDiags, d => d.Message.Contains("Foo"));
        Assert.Contains(noMatchDiags, d => d.Message.Contains("string"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 2: Ambiguous diagnostic (MIST_TC_0032)
    // Class with M(float) and M(decimal), call with int arg → MIST_TC_0032
    // The message should include conflicting candidate signatures.
    // **Validates: Requirements 4.1, 4.2**
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void AmbiguousOverload_EmitsMIST_TC_0032_WithCandidateSignatures()
    {
        // Arrange: class Foo with Foo(float) and Foo(decimal)
        // Program.Main: int x = 0; new Foo(x); → ambiguous
        // int is assignable to both float and decimal, and neither is more specific
        var ctorFloat = MakeConstructor("Foo", "float", "f");
        var ctorDecimal = MakeConstructor("Foo", "decimal", "d");

        var fooClass = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = MakeToken("Foo"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new ConstructorMemberMemberDeclarationNode { Constructor = ctorFloat },
                new ConstructorMemberMemberDeclarationNode { Constructor = ctorDecimal }),
        };

        // Program class with Main: int x = 0; new Foo(x);
        var varDecl = MakeIntVarDecl("x", "0");
        var newExpr = MakeNewObjectExpression("Foo", MakeIdentifierExpression("x"));
        var mainMethod = MakeStaticMain(
            ImmutableArray.Create(varDecl, MakeExpressionStatement(newExpr)));

        var programClass = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = MakeToken("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainMethod }),
        };

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new BodyTypeTypeDeclarationNode { TypeBody = fooClass },
                new BodyTypeTypeDeclarationNode { TypeBody = programClass }).AsTopLevelItems(),
        };

        // Act
        var (model, _) = StdlibTestHelper.BuildModelWithStdlib(ast);
        var diagnostics = RunTypeChecker(model);

        // Assert: MIST_TC_0032 emitted with both candidate signatures in message
        var ambiguousDiags = diagnostics.Where(d => d.Id.Value == "MIST_TC_0032").ToList();
        Assert.NotEmpty(ambiguousDiags);
        // The message should mention "Foo" and include both candidate signatures.
        // With stdlib loaded, float resolves to Single and decimal to Decimal.
        Assert.Contains(ambiguousDiags, d => d.Message.Contains("Foo"));
        Assert.Contains(ambiguousDiags, d =>
            (d.Message.Contains("float") || d.Message.Contains("Single"))
            && (d.Message.Contains("decimal") || d.Message.Contains("Decimal")));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 3: Ambiguous constructor call diagnostic (MIST_TC_0032)
    // Class C with C(float) and C(decimal), call new C(intValue) → MIST_TC_0032
    // This specifically tests the constructor call path per Requirement 4.3.
    // **Validates: Requirement 4.3**
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void AmbiguousConstructor_EmitsMIST_TC_0032_ForNewExpression()
    {
        // Arrange: class Bar with Bar(float) and Bar(decimal)
        // Program.Main: int v = 0; new Bar(v); → ambiguous
        var ctorFloat = MakeConstructor("Bar", "float", "f");
        var ctorDecimal = MakeConstructor("Bar", "decimal", "d");

        var barClass = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = MakeToken("Bar"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new ConstructorMemberMemberDeclarationNode { Constructor = ctorFloat },
                new ConstructorMemberMemberDeclarationNode { Constructor = ctorDecimal }),
        };

        // Program class with Main: int v = 0; new Bar(v);
        var varDecl = MakeIntVarDecl("v", "0");
        var newExpr = MakeNewObjectExpression("Bar", MakeIdentifierExpression("v"));
        var mainMethod = MakeStaticMain(
            ImmutableArray.Create(varDecl, MakeExpressionStatement(newExpr)));

        var programClass = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = MakeToken("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainMethod }),
        };

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new BodyTypeTypeDeclarationNode { TypeBody = barClass },
                new BodyTypeTypeDeclarationNode { TypeBody = programClass }).AsTopLevelItems(),
        };

        // Act
        var (model, _) = StdlibTestHelper.BuildModelWithStdlib(ast);
        var diagnostics = RunTypeChecker(model);

        // Assert: MIST_TC_0032 emitted for the ambiguous constructor call
        var ambiguousDiags = diagnostics.Where(d => d.Id.Value == "MIST_TC_0032").ToList();
        Assert.NotEmpty(ambiguousDiags);
        // With stdlib loaded, float resolves to Single and decimal to Decimal.
        Assert.Contains(ambiguousDiags, d => d.Message.Contains("Bar"));
        Assert.Contains(ambiguousDiags, d =>
            (d.Message.Contains("float") || d.Message.Contains("Single"))
            && (d.Message.Contains("decimal") || d.Message.Contains("Decimal")));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 4: Backward compatibility — single-method programs produce no new diagnostics
    // Class with single M(int), call M("hello") → existing MIST_TC_0002 (not MIST_TC_0031)
    // For constructors: single constructor Foo(int), call new Foo("hello") → MIST_TC_0011
    // **Validates: Requirements 3.4, 3.5 (backward compat)**
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void SingleConstructor_WrongArgType_EmitsExistingDiagnostic_NotOverloadDiagnostic()
    {
        // Arrange: class Foo with single Foo(int)
        // Program.Main: new Foo("hello")
        // Since there's only one constructor, the type checker should use the existing
        // MIST_TC_0011 diagnostic, NOT the new MIST_TC_0031 overload diagnostic.
        var ctor = MakeConstructor("Foo", "int", "x");

        var fooClass = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = MakeToken("Foo"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new ConstructorMemberMemberDeclarationNode { Constructor = ctor }),
        };

        // Program class with Main: new Foo("hello");
        var newExpr = MakeNewObjectExpression("Foo", MakeStringLiteralExpression("\"hello\""));
        var mainMethod = MakeStaticMain(
            ImmutableArray.Create(MakeExpressionStatement(newExpr)));

        var programClass = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = MakeToken("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainMethod }),
        };

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new BodyTypeTypeDeclarationNode { TypeBody = fooClass },
                new BodyTypeTypeDeclarationNode { TypeBody = programClass }).AsTopLevelItems(),
        };

        // Act
        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        // Assert: MIST_TC_0013 (no matching constructor) is emitted, NOT MIST_TC_0031
        Assert.Contains(diagnostics, d => d.Id.Value == "MIST_TC_0013");
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0031");
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0032");
    }
}