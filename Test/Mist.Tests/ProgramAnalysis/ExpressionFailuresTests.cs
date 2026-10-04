using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.ProgramAnalysis;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class ExpressionFailuresTests
{
    [Fact]
    public void AnOwningRefusalSuppressesItsFallbackThroughNestedCallsAndALocal()
    {
        var failures = new ExpressionFailures();
        var bag = new DiagnosticBag();
        var owner = new UnresolvedExpression(HandBuiltModel.Node(10));
        var inner = Pending("INNER", 20, owner);
        var local = new LocalVariable("value", ErrorType.Instance) { FailureSource = inner };
        var outer = Pending("OUTER", 30, new LocalReferenceExpression(HandBuiltModel.Node(25), local));
        failures.Record(owner);

        failures.Resolve(outer, bag);

        Assert.Empty(bag.ToImmutableArray());
        Assert.True(failures.Of(inner));
        Assert.True(failures.Of(outer));
        Assert.False(local.IsFailed);
    }

    [Fact]
    public void AnUnownedInnerFallbackReportsOnceAtItsOwnSourceBeforeSuppressingItsParent()
    {
        var failures = new ExpressionFailures();
        var bag = new DiagnosticBag();
        var inner = Pending("INNER", 20, new UnresolvedExpression(HandBuiltModel.Node(10)));
        var outer = Pending("OUTER", 30, inner);

        failures.Resolve(outer, bag);
        failures.Resolve(inner, bag);
        failures.Resolve(outer, bag);

        var diagnostic = Assert.Single(bag.ToImmutableArray());
        Assert.Equal("INNER", diagnostic.Id.Value);
        Assert.Equal("hand-built.mist", diagnostic.CompilationUnitPath);
        Assert.Equal(new LinePositionSpan(new LinePosition(1, 21), new LinePosition(1, 22)), diagnostic.Location);
    }

    [Fact]
    public void IndependentUnownedChildrenBothKeepTheirFallbacks()
    {
        var failures = new ExpressionFailures();
        var bag = new DiagnosticBag();
        var left = Pending("LEFT", 10, new UnresolvedExpression(HandBuiltModel.Node(1)));
        var right = Pending("RIGHT", 20, new LocalReferenceExpression(HandBuiltModel.Node(2), new LocalVariable("unfinished", ErrorType.Instance)));
        var outer = Pending("OUTER", 30, left, right);

        failures.Resolve(outer, bag);

        var diagnostics = bag.ToImmutableArray();
        Assert.Equal(["LEFT", "RIGHT"], diagnostics.Select(diagnostic => diagnostic.Id.Value));
        Assert.Equal([11, 21], diagnostics.Select(diagnostic => diagnostic.Location.Start.Column));
    }

    [Fact]
    public void AReportedChildDoesNotConsumeTheFallbackOfAnIndependentChild()
    {
        var failures = new ExpressionFailures();
        var bag = new DiagnosticBag();
        var independent = Pending("INDEPENDENT", 20, new UnresolvedExpression(HandBuiltModel.Node(2)));
        var outer = Pending("OUTER", 30, new ErrorExpression(HandBuiltModel.Node(1)), independent);

        failures.Resolve(outer, bag);

        var diagnostic = Assert.Single(bag.ToImmutableArray());
        Assert.Equal("INDEPENDENT", diagnostic.Id.Value);
        Assert.Equal(21, diagnostic.Location.Start.Column);
    }

    private static ErrorExpression Pending(string id, int offset, params Expression[] children)
    {
        var node = HandBuiltModel.Node(offset);
        return new ErrorExpression(node, children, new Diagnostic(new DiagnosticId(id), DiagnosticSeverity.Error,
            PipelineStage.SemanticModelConstruction, "Deferred failure.",
            new LinePositionSpan(new LinePosition(1, offset + 1), new LinePosition(1, offset + 2)), "hand-built.mist", null));
    }
}