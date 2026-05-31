using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Unit tests for MIST_REF_0010: ref local requires initializer validation.
/// The current grammar enforces initializers for all local variable declarations,
/// so the check is defensive. These tests verify that valid ref locals (with initializers)
/// do NOT produce the diagnostic.
/// Validates: Requirements 5.7, 14.10
/// </summary>
public class RefLocalRequiresInitializerTests
{
    private readonly SemanticModelBuilder _builder = new();
    private readonly TypeCheckerAnalyzer _analyzer = new();

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static Storm.Runtime.Token Tok(string lexeme, int start = 0)
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

    private ISemanticModel BuildModel(Storm.Runtime.SyntaxNode ast)
    {
        var compilation = MakeCompilation();
        var diagnostics = new DiagnosticBag();
        return _builder.Build(compilation, ImmutableArray.Create(ast), ambientTypeResolver: null, namespaceRoot: "TestProject", projectFolder: "/test", diagnostics);
    }

    private ImmutableArray<Diagnostic> RunTypeChecker(ISemanticModel model)
    {
        var bag = new DiagnosticBag();
        _analyzer.Analyze(model, bag);
        return bag.ToImmutableArray();
    }

    private static ExpressionNode MakeIntLiteralExpression(string value = "0")
    {
        var primary = new IntegerLiteralExpressionPrimaryNode
        {
            IntegerLiteral = Tok(value),
        };
        return WrapPrimaryAsExpression(primary);
    }

    private static ExpressionNode MakeIdentifierExpression(string name)
    {
        var primary = new IdentifierExpressionPrimaryNode
        {
            Name = Tok(name),
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

    private static TypeReferenceNode MakeIntTypeRef()
    {
        return new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode
        {
            PrimitiveType = new IntTypePrimitiveTypeNode(),
        } };
    }

    private static MethodDeclarationNode MakeMainMethod(ImmutableArray<StatementNode> body)
    {
        return new MethodDeclarationNode
        {
            Name = Tok("Main"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = body } },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };
    }

    private static CompilationUnitNode MakeProgram(ImmutableArray<StatementNode> statements)
    {
        var mainMethod = MakeMainMethod(statements);
        var classDecl = new ClassDeclarationNode
        {
            Name = Tok("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainMethod }),
        };
        return new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };
    }

    // ─── Tests ───────────────────────────────────────────────────────────

    /// <summary>
    /// A ref local with an initializer does NOT produce MIST_REF_0010.
    /// Validates: Requirement 5.7 (positive case — initializer present)
    /// </summary>
    [Fact]
    public void RefLocal_WithInitializer_NoDiagnostic()
    {
        // ref int x = 0;
        var refLocalDecl = new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                OwnershipSpecifier = new RefSpecifierOwnershipSpecifierNode(),
                Type = MakeIntTypeRef(),
                Name = Tok("x"),
                Initializer = MakeIntLiteralExpression("42"),
            },
        };

        var ast = MakeProgram(ImmutableArray.Create<StatementNode>(refLocalDecl));
        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        var refErrors = diagnostics.Where(d => d.Id.Value == "MIST_REF_0010").ToList();
        Assert.Empty(refErrors);
    }

    /// <summary>
    /// A ref readonly local with an initializer does NOT produce MIST_REF_0010.
    /// Validates: Requirement 5.7 (positive case — readonly ref with initializer)
    /// </summary>
    [Fact]
    public void RefReadonlyLocal_WithInitializer_NoDiagnostic()
    {
        // readonly ref int x = 0;
        var refReadonlyLocalDecl = new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                OwnershipSpecifier = new ReadonlyRefSpecifierOwnershipSpecifierNode(),
                Type = MakeIntTypeRef(),
                Name = Tok("x"),
                Initializer = MakeIntLiteralExpression("42"),
            },
        };

        var ast = MakeProgram(ImmutableArray.Create<StatementNode>(refReadonlyLocalDecl));
        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        var refErrors = diagnostics.Where(d => d.Id.Value == "MIST_REF_0010").ToList();
        Assert.Empty(refErrors);
    }

    /// <summary>
    /// A non-ref local with an initializer does NOT produce MIST_REF_0010.
    /// Validates: Requirement 5.7 (non-ref locals are not affected)
    /// </summary>
    [Fact]
    public void NonRefLocal_WithInitializer_NoDiagnostic()
    {
        // int x = 0;
        var localDecl = new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                Type = MakeIntTypeRef(),
                Name = Tok("x"),
                Initializer = MakeIntLiteralExpression("42"),
            },
        };

        var ast = MakeProgram(ImmutableArray.Create<StatementNode>(localDecl));
        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        var refErrors = diagnostics.Where(d => d.Id.Value == "MIST_REF_0010").ToList();
        Assert.Empty(refErrors);
    }

    /// <summary>
    /// A ref var local (inferred type) with an initializer does NOT produce MIST_REF_0010.
    /// Validates: Requirement 5.7 (inferred ref local with initializer)
    /// </summary>
    [Fact]
    public void RefVarLocal_WithInitializer_NoDiagnostic()
    {
        // ref var x = someExpr;
        var refVarDecl = new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new InferredVariableDeclarationVariableDeclarationStatementNode
            {
                OwnershipSpecifier = new RefSpecifierOwnershipSpecifierNode(),
                Name = Tok("x"),
                Initializer = MakeIntLiteralExpression("42"),
            },
        };

        var ast = MakeProgram(ImmutableArray.Create<StatementNode>(refVarDecl));
        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        var refErrors = diagnostics.Where(d => d.Id.Value == "MIST_REF_0010").ToList();
        Assert.Empty(refErrors);
    }
}
