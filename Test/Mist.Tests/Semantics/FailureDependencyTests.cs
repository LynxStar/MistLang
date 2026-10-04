using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "1")]
public sealed class FailureDependencyTests
{
    [Theory]
    [InlineData("unknown-member", true)]
    [InlineData("reported-member", false)]
    [InlineData("conditional-member", true)]
    [InlineData("invalid-binary", true)]
    [InlineData("unfinished-expression", false)]
    [InlineData("unfinished-local", false)]
    [InlineData("reported-expression", false)]
    public void OnlyAnInputAwaitingAnOwningDecisionDefersAConsequentFailure(string shape, bool expected)
    {
        var f = new HandBuiltModel();
        var node = HandBuiltModel.Node();
        var unknown = new LocalReferenceExpression(node, new LocalVariable("u", UnknownType.Instance));
        Expression input = shape switch
        {
            "unknown-member" => new MemberAccessExpression(node, unknown, "Length", ErrorType.Instance),
            "reported-member" => new MemberAccessExpression(node, HandBuiltModel.Literal(f.Type("Int32")), "Missing",
                ErrorType.Instance, member: new NotFoundResult(), isFailed: true),
            "conditional-member" => new MemberAccessExpression(node, HandBuiltModel.Literal(f.Type("Int32")), "Name",
                ErrorType.Instance, isConditional: true),
            "invalid-binary" => new BinaryExpression(node, HandBuiltModel.Literal(HandBuiltModel.Class("Box")), BinaryOperatorKind.Add,
                HandBuiltModel.Literal(f.Type("Int32")), ErrorType.Instance),
            "unfinished-expression" => new UnresolvedExpression(node),
            "unfinished-local" => new LocalReferenceExpression(node, new LocalVariable("value", ErrorType.Instance)),
            _ => new ErrorExpression(node),
        };

        Assert.Equal(expected, FailureDependency.NeedsAnalysis(input));
    }

    [Fact]
    public void DeferredOwnershipSurvivesNestedRetentionAndLocalInitializerProvenance()
    {
        var node = HandBuiltModel.Node();
        var pending = new Diagnostic(new DiagnosticId("MIST_PM_UNRESOLVABLE_OVERLOAD"), DiagnosticSeverity.Error,
            PipelineStage.SemanticModelConstruction, "No matching overload.", default, "hand-built.mist", null);
        var inner = new ErrorExpression(node, [new UnresolvedExpression(node)], pending);
        var local = new LocalVariable("result", ErrorType.Instance) { FailureSource = inner };
        var outer = new ErrorExpression(node, [new LocalReferenceExpression(node, local)]);

        Assert.True(FailureDependency.NeedsAnalysis(outer));
        Assert.False(local.IsFailed);
        Assert.False(PriorFailure.Of(inner));
    }
}