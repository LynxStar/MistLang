using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Generated;
using Mist.ProgramAnalysis;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class RetainedFailureAnalysisTests
{
    [Theory]
    [InlineData("call")]
    [InlineData("constructor")]
    [InlineData("index")]
    [InlineData("await")]
    public void RetainedInputsReceiveTheOwningRefusalBeforeTheFallbackIsResolved(string shape)
    {
        var f = new HandBuiltModel();
        var access = UnknownMember(11);
        var receiver = HandBuiltModel.Literal(SourceClass("Holder"));
        var node = HandBuiltModel.Node(40);
        Expression retained = shape switch
        {
            "call" => new InvocationExpression(node, receiver, [access], ErrorType.Instance, null, null),
            "constructor" => new NewExpression(node, receiver.Type, [access], null, [], null),
            "index" => new IndexExpression(node, receiver, access, ErrorType.Instance),
            _ => access,
        };
        var root = Pending(shape == "await" ? "MIST_PM_AWAIT_OF_NON_TASK" : "MIST_PM_UNRESOLVABLE_OVERLOAD", 40, retained);

        var diagnostics = Analyze(f, root);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("MIST_PA_UNKNOWN_HAS_NO_MEMBERS", diagnostic.Id.Value);
        Assert.Equal("hand-built.mist", diagnostic.CompilationUnitPath);
        Assert.Equal(12, diagnostic.Location.Start.Column);
        Assert.Equal("operator-and-positional-checks", diagnostic.AnalyzerName);
    }

    [Fact]
    public void IndependentRetainedOperatorAndMemberMistakesBothReport()
    {
        var f = new HandBuiltModel();
        var binary = new BinaryExpression(HandBuiltModel.Node(11), HandBuiltModel.Literal(SourceClass("Box")), BinaryOperatorKind.Add,
            HandBuiltModel.Literal(f.Type("Int32")), ErrorType.Instance);
        var member = UnknownMember(21);
        var root = Pending("MIST_PM_UNRESOLVABLE_OVERLOAD", 40, new ErrorExpression(HandBuiltModel.Node(30)), binary, member);

        var diagnostics = Analyze(f, root);

        Assert.Equal(["MIST_PA_OPERATOR_NOT_APPLICABLE", "MIST_PA_UNKNOWN_HAS_NO_MEMBERS"], diagnostics.Select(diagnostic => diagnostic.Id.Value));
        Assert.Equal([12, 22], diagnostics.Select(diagnostic => diagnostic.Location.Start.Column));
    }

    [Theory]
    [InlineData("sound", "MIST_PA_CONDITIONAL_ACCESS_ON_NON_OPTIONAL")]
    [InlineData("unknown", "MIST_PA_UNKNOWN_HAS_NO_MEMBERS")]
    [InlineData("unfinished", "MIST_PM_UNRESOLVABLE_OVERLOAD")]
    [InlineData("optional", "MIST_PM_UNRESOLVABLE_OVERLOAD")]
    public void ConditionalAccessKeepsUnknownErrorAndOptionalOwnershipDistinct(string shape, string expected)
    {
        var f = new HandBuiltModel();
        var sound = SourceClass("Box");
        IMistType type = shape switch
        {
            "unknown" => UnknownType.Instance,
            "unfinished" => ErrorType.Instance,
            "optional" => new UnionType([sound, f.Type("Void")]),
            _ => sound,
        };
        var access = new MemberAccessExpression(HandBuiltModel.Node(11), HandBuiltModel.Literal(type), "Length", ErrorType.Instance, isConditional: true);
        var root = Pending("MIST_PM_UNRESOLVABLE_OVERLOAD", 40, access);

        var diagnostic = Assert.Single(Analyze(f, root));

        Assert.Equal(expected, diagnostic.Id.Value);
        Assert.Equal(shape is "optional" or "unfinished" ? 41 : 12, diagnostic.Location.Start.Column);
    }

    [Fact]
    public void AnInitializerRefusalSurvivesNestedFallbacksAndLocalReferences()
    {
        var f = new HandBuiltModel();
        var source = Pending("INNER", 20, UnknownMember(11));
        var local = new LocalVariable("value", ErrorType.Instance) { FailureSource = source };
        var root = HandBuiltModel.Block(
            new ExpressionStatement(Pending("OUTER", 40, new LocalReferenceExpression(HandBuiltModel.Node(30), local))),
            new ExpressionStatement(source));

        var diagnostic = Assert.Single(Analyze(f, root));

        Assert.Equal("MIST_PA_UNKNOWN_HAS_NO_MEMBERS", diagnostic.Id.Value);
        Assert.Equal(12, diagnostic.Location.Start.Column);
        Assert.False(local.IsFailed);
    }

    [Fact]
    public void AUnaryWrapperKeepsItsReportedOperandAsTheOwner()
    {
        var f = new HandBuiltModel();
        var unary = new UnaryExpression(HandBuiltModel.Node(20), UnaryOperatorKind.LogicalNot, UnknownMember(11), ErrorType.Instance);
        var root = Pending("MIST_PM_AWAIT_OF_NON_TASK", 40, unary);

        var diagnostic = Assert.Single(Analyze(f, root));

        Assert.Equal("MIST_PA_UNKNOWN_HAS_NO_MEMBERS", diagnostic.Id.Value);
        Assert.Equal(12, diagnostic.Location.Start.Column);
    }

    [Fact]
    public void AnUnjudgedBinaryAndUnboundLambdaKeepTheConstructorFallback()
    {
        var f = new HandBuiltModel();
        var unfinished = new BinaryExpression(HandBuiltModel.Node(11), new UnresolvedExpression(HandBuiltModel.Node(10)),
            BinaryOperatorKind.Add, HandBuiltModel.Literal(f.Type("Int32")), ErrorType.Instance);
        var lambda = new UnboundLambdaExpression(new ExpressionLambdaLambdaExpressionNode());
        var creation = new NewExpression(HandBuiltModel.Node(30), SourceClass("Holder"), [unfinished, lambda], null, [], null);
        var root = Pending("MIST_PM_UNRESOLVABLE_OVERLOAD", 40, creation);

        var diagnostic = Assert.Single(Analyze(f, root));

        Assert.Equal("MIST_PM_UNRESOLVABLE_OVERLOAD", diagnostic.Id.Value);
        Assert.Equal(41, diagnostic.Location.Start.Column);
    }

    private static IReadOnlyList<Diagnostic> Analyze(HandBuiltModel f, object root)
    {
        var diagnostics = new DiagnosticBag();
        var failures = new ExpressionFailures();
        var operators = new OperatorAnalyzer();
        operators.Analyze(f.Context(operators, diagnostics, root, failures: failures));
        var fallback = new FailedExpressionAnalyzer();
        fallback.Analyze(f.Context(fallback, diagnostics, root, failures: failures));
        return diagnostics.ToImmutableArray();
    }

    private static ClassType SourceClass(string name)
    {
        var type = HandBuiltModel.Class(name);
        type.Assembly = new MistAssembly("Probe", MistAssemblyKind.Self);
        return type;
    }

    private static MemberAccessExpression UnknownMember(int offset)
    {
        return new MemberAccessExpression(HandBuiltModel.Node(offset), HandBuiltModel.Literal(UnknownType.Instance), "Length", ErrorType.Instance);
    }

    private static ErrorExpression Pending(string id, int offset, params Expression[] children)
    {
        var node = HandBuiltModel.Node(offset);
        return new ErrorExpression(node, children, new Diagnostic(new DiagnosticId(id), DiagnosticSeverity.Error,
            PipelineStage.SemanticModelConstruction, "Deferred failure.",
            new LinePositionSpan(new LinePosition(1, offset + 1), new LinePosition(1, offset + 2)),
            "hand-built.mist", null));
    }
}