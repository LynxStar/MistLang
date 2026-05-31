// Feature: value-tuples — Unit tests for C# emitter output
// **Validates: Requirements 6.5, 6.6, 6.7**

using System.Collections.Immutable;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Backend;

/// <summary>
/// Unit tests verifying that the C# emitter produces correct output for
/// tuple member access, var deconstruction, and typed deconstruction.
/// </summary>
public sealed class TupleEmitterOutputTests : IDisposable
{
    private readonly SemanticModelBuilder _builder = new();
    private readonly string _outputDir;

    public TupleEmitterOutputTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), "MistTupleEmitterTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_outputDir))
        {
            try { Directory.Delete(_outputDir, recursive: true); } catch { /* best effort */ }
        }
        GC.SuppressFinalize(this);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static Storm.Runtime.Token MakeToken(string lexeme, int start = 0) =>
        new(kind: 0,
            span: new Storm.Runtime.SourceSpan(start, lexeme.Length),
            start: new Storm.Runtime.LinePosition(1, 1),
            end: new Storm.Runtime.LinePosition(1, 1 + lexeme.Length),
            channel: Storm.Runtime.TokenChannel.Default,
            lexeme: lexeme);

    private ISemanticModel BuildModel(CompilationUnitNode ast, string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var compilation = new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));
        var diagnostics = new DiagnosticBag();
        return _builder.Build(
            compilation,
            ImmutableArray.Create<Storm.Runtime.SyntaxNode>(ast),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            diagnostics);
    }

    private string EmitAndGetCsContent(ISemanticModel model)
    {
        var diagnostics = new DiagnosticBag();
        CSharpEmitter.Emit(model, _outputDir, "TestProject", diagnostics);
        var csFile = Directory.GetFiles(_outputDir, "*.cs").FirstOrDefault();
        return csFile is not null ? File.ReadAllText(csFile) : "";
    }

    private void CleanOutputDir()
    {
        foreach (var file in Directory.GetFiles(_outputDir))
            File.Delete(file);
    }

    private static ExpressionNode WrapPrimaryAsExpression(PrimaryNode primary)
    {
        var postfix = new PostfixNode { Target = primary, Suffix = ImmutableArray<PostfixSuffixNode>.Empty };
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

    private static ExpressionNode MakeIdentifierExpression(string name) =>
        WrapPrimaryAsExpression(new IdentifierExpressionPrimaryNode { Name = MakeToken(name) });

    private static ExpressionNode MakeIntLiteralExpression(string value = "0") =>
        WrapPrimaryAsExpression(new IntegerLiteralExpressionPrimaryNode { IntegerLiteral = MakeToken(value) });

    private static ExpressionNode MakeMemberAccessExpression(string target, string member)
    {
        var targetPrimary = new IdentifierExpressionPrimaryNode { Name = MakeToken(target) };
        var postfix = new PostfixNode
        {
            Target = targetPrimary,
            Suffix = ImmutableArray.Create<PostfixSuffixNode>(
                new MemberAccessSuffixPostfixSuffixNode { Name = MakeToken(member) }),
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

    private static CompilationUnitNode WrapInClass(string className, params StatementNode[] statements)
    {
        var block = new BlockNode { Statements = ImmutableArray.CreateRange(statements) };
        var method = new MethodDeclarationNode
        {
            Name = MakeToken("Run"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = block },
            Modifiers = ImmutableArray<ModifierNode>.Empty,
        };
        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken(className),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = method }),
        };
        return new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TopLevelItemNode>(
                new TypeItemTopLevelItemNode
                {
                    TypeDeclaration = new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }
                }),
        };
    }

    // ─── Requirement 6.5: Tuple member access ────────────────────────────

    [Fact]
    public void EmitterTupleMemberAccess_Item1_EmitsCorrectly()
    {
        CleanOutputDir();

        // var x = tuple.Item1;
        var varDecl = new InferredVariableDeclarationVariableDeclarationStatementNode
        {
            Name = MakeToken("x"),
            Initializer = MakeMemberAccessExpression("tuple", "Item1"),
        };
        var stmt = new VariableDeclarationStatementStatementNode { VariableDeclaration = varDecl };

        var ast = WrapInClass("Test", stmt);
        var model = BuildModel(ast, "");
        var csContent = EmitAndGetCsContent(model);

        Assert.Contains("tuple.Item1", csContent);
    }

    [Fact]
    public void EmitterTupleMemberAccess_NamedMember_EmitsCorrectly()
    {
        CleanOutputDir();

        // var x = tuple.name;
        var varDecl = new InferredVariableDeclarationVariableDeclarationStatementNode
        {
            Name = MakeToken("x"),
            Initializer = MakeMemberAccessExpression("tuple", "name"),
        };
        var stmt = new VariableDeclarationStatementStatementNode { VariableDeclaration = varDecl };

        var ast = WrapInClass("Test", stmt);
        var model = BuildModel(ast, "");
        var csContent = EmitAndGetCsContent(model);

        Assert.Contains("tuple.name", csContent);
    }

    // ─── Requirement 6.6: Var tuple deconstruction ───────────────────────

    [Fact]
    public void EmitterVarDeconstruction_TwoTargets_EmitsCorrectly()
    {
        CleanOutputDir();

        // var (a, b) = (1, 2);
        var tupleLit = new TupleLiteralExpressionPrimaryNode
        {
            First = new UnnamedTupleLiteralElementTupleLiteralElementNode
                { Value = MakeIntLiteralExpression("1") },
            Rest = new UnnamedTupleLiteralElementTupleLiteralElementNode
                { Value = MakeIntLiteralExpression("2") },
            TupleLiteralElements = ImmutableArray<TupleLiteralElementNode>.Empty,
        };

        var varDeconstruction = new VarTupleDeconstructionVariableDeclarationStatementNode
        {
            FirstName = MakeToken("a"),
            RestNames = ImmutableArray.Create(MakeToken("b")),
            Initializer = WrapPrimaryAsExpression(tupleLit),
        };
        var stmt = new VariableDeclarationStatementStatementNode { VariableDeclaration = varDeconstruction };

        var ast = WrapInClass("Test", stmt);
        var model = BuildModel(ast, "");
        var csContent = EmitAndGetCsContent(model);

        Assert.Contains("var (a, b) =", csContent);
    }

    // ─── Requirement 6.7: Typed tuple deconstruction ─────────────────────

    [Fact]
    public void EmitterTypedDeconstruction_TwoTargets_EmitsCorrectly()
    {
        CleanOutputDir();

        // (int a, bool b) = (1, true);
        var tupleLit = new TupleLiteralExpressionPrimaryNode
        {
            First = new UnnamedTupleLiteralElementTupleLiteralElementNode
                { Value = MakeIntLiteralExpression("1") },
            Rest = new UnnamedTupleLiteralElementTupleLiteralElementNode
                { Value = WrapPrimaryAsExpression(new TrueLiteralExpressionPrimaryNode()) },
            TupleLiteralElements = ImmutableArray<TupleLiteralElementNode>.Empty,
        };

        var typedDeconstruction = new TypedTupleDeconstructionVariableDeclarationStatementNode
        {
            First = new TupleDeconstructionElementNode
            {
                Type = new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = new IntTypePrimitiveTypeNode() } },
                Name = MakeToken("a"),
            },
            Rest = new TupleDeconstructionElementNode
            {
                Type = new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = new BoolTypePrimitiveTypeNode() } },
                Name = MakeToken("b"),
            },
            TupleDeconstructionElements = ImmutableArray<TupleDeconstructionElementNode>.Empty,
            Initializer = WrapPrimaryAsExpression(tupleLit),
        };
        var stmt = new VariableDeclarationStatementStatementNode { VariableDeclaration = typedDeconstruction };

        var ast = WrapInClass("Test", stmt);
        var model = BuildModel(ast, "");
        var csContent = EmitAndGetCsContent(model);

        Assert.Contains("(int a, bool b) =", csContent);
    }
}
