using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Semantics;

/// <summary>
/// Unit tests for void type identity properties and void variable inference.
/// Validates Requirements 1.1, 1.2, 1.3, 4.2, 4.3.
/// </summary>
public class VoidTypeIdentityTests
{
    private readonly SemanticModelBuilder _builder = new();

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

    private (ISemanticModel Model, ImmutableArray<Diagnostic> Diagnostics) BuildModel(
        Storm.Runtime.SyntaxNode ast,
        string source = "")
    {
        var compilation = MakeCompilation(source);
        var diagnostics = new DiagnosticBag();
        var model = _builder.Build(compilation, ImmutableArray.Create(ast), ambientTypeResolver: null, namespaceRoot: "TestProject", projectFolder: "/test", diagnostics);
        return (model, diagnostics.ToImmutableArray());
    }

    // ─── AST construction helpers ────────────────────────────────────────

    /// <summary>
    /// Creates the expression `Void.Void` as an AST node chain.
    /// Returns the full ExpressionNode and the PostfixNode for type verification.
    /// </summary>
    private static (ExpressionNode Expression, PostfixNode Postfix) MakeVoidDotVoidExpression()
    {
        var primary = new IdentifierExpressionPrimaryNode
        {
            Name = MakeToken("Void"),
        };
        var suffix = new MemberAccessSuffixPostfixSuffixNode
        {
            Name = MakeToken("Void"),
        };
        var postfix = new PostfixNode
        {
            Target = primary,
            Suffix = ImmutableArray.Create<PostfixSuffixNode>(suffix),
        };
        var unary = new PostfixExpressionUnaryNode { Primary = postfix };
        var mult = new MultiplicativeNode { Left = unary };
        var add = new AdditiveNode { Left = mult };
        var rel = new RelationalComparisonRelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        var expr = new ExpressionNode { Assignment = assign };
        return (expr, postfix);
    }

    private static MethodDeclarationNode MakeMainMethod(ImmutableArray<StatementNode> body)
    {
        return new MethodDeclarationNode
        {
            Name = MakeToken("Main"),
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = body } },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };
    }

    private static CompilationUnitNode MakeCompilationUnit(params TypeDeclarationNode[] typeDecls)
    {
        return new CompilationUnitNode { Items = typeDecls.ToImmutableArray().AsTopLevelItems() };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 1.1: Void type SHALL have TypeKind.Struct
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void VoidType_Kind_IsStruct()
    {
        Assert.Equal(TypeKind.Struct, LegacyPrimitiveTypes.Void.Kind);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 1.2: Void type SHALL expose a single static instance
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void VoidType_Instance_IsSingleton()
    {
        var a = LegacyPrimitiveTypes.Void;
        var b = LegacyPrimitiveTypes.Void;
        Assert.Same(a, b);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 1.3: Void type SHALL have display name "void"
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void VoidType_DisplayName_IsVoid()
    {
        Assert.Equal("void", LegacyPrimitiveTypes.Void.DisplayName);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 4.3: var x = Void.Void; infers void type
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void VarInference_VoidDotVoid_InfersVoidType()
    {
        var (voidExpr, _) = MakeVoidDotVoidExpression();

        var varDecl = new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new InferredVariableDeclarationVariableDeclarationStatementNode
            {
                Name = MakeToken("x"),
                Initializer = voidExpr,
            },
        };

        var mainMethod = MakeMainMethod(ImmutableArray.Create<StatementNode>(varDecl));
        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainMethod }),
        };

        var ast = MakeCompilationUnit(new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl });
        var (model, _) = BuildModel(ast);

        var varSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == "x" && s.Kind == SymbolKind.LocalVariable);

        Assert.NotNull(varSymbol);
        Assert.NotNull(varSymbol!.DeclaredType);
        Assert.True(varSymbol.DeclaredType!.IsVoid());
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 4.2: void x = Void.Void; accepted without error
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TypedVoidVariable_VoidDotVoid_Accepted()
    {
        var (voidExpr, _) = MakeVoidDotVoidExpression();

        var varDecl = new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                Type = new VoidTypeRefTypeReferenceNode(),
                Name = MakeToken("x"),
                Initializer = voidExpr,
            },
        };

        var mainMethod = MakeMainMethod(ImmutableArray.Create<StatementNode>(varDecl));
        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainMethod }),
        };

        var ast = MakeCompilationUnit(new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl });
        var (model, diagnostics) = BuildModel(ast);

        // Variable should resolve to void type
        var varSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == "x" && s.Kind == SymbolKind.LocalVariable);

        Assert.NotNull(varSymbol);
        Assert.NotNull(varSymbol!.DeclaredType);
        Assert.True(varSymbol.DeclaredType!.IsVoid());

        // No type mismatch diagnostics
        Assert.DoesNotContain(diagnostics, d => d.Id.Value.Contains("MIST_TC"));
    }
}
