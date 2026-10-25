using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "1")]
public sealed class PriorFailureConversionTests
{
    [Theory]
    [InlineData("reported-expression", true)]
    [InlineData("reported-local", true)]
    [InlineData("unfinished-expression", false)]
    [InlineData("deferred-expression", false)]
    [InlineData("unfinished-local", false)]
    public void AConversionPreservesOnlyPositiveOperandFailure(string source, bool expected)
    {
        var node = HandBuiltModel.Node();
        Expression operand = source switch
        {
            "reported-expression" => new ErrorExpression(node),
            "unfinished-expression" => new UnresolvedExpression(node),
            "deferred-expression" => new ErrorExpression(node, deferredDiagnostic: Deferred()),
            _ => new LocalReferenceExpression(node, new LocalVariable("value", ErrorType.Instance)
            {
                IsFailed = source == "reported-local",
            }),
        };
        var conversion = new ImplicitConversionExpression(node, operand, new HandBuiltModel().Type("Int32"), []);

        Assert.Equal(expected, PriorFailure.Of(conversion));
    }

    [Fact]
    public void ADeferredFailureDoesNotBecomeReportedThroughAReceiverOrLocal()
    {
        var node = HandBuiltModel.Node();
        var deferred = new ErrorExpression(node, [new UnresolvedExpression(node)], Deferred());
        var member = new MemberAccessExpression(node, deferred, "Length", ErrorType.Instance);
        var local = new LocalVariable("value", ErrorType.Instance) { FailureSource = deferred };

        Assert.False(deferred.IsReported);
        Assert.False(PriorFailure.Of(deferred));
        Assert.False(PriorFailure.Of(member));
        Assert.False(PriorFailure.Of(new LocalReferenceExpression(node, local)));
        Assert.False(local.IsFailed);
    }

    private static Diagnostic Deferred()
    {
        return DiagnosticFixtures.Occurrence("MIST_PM_UNRESOLVABLE_OVERLOAD", DiagnosticSeverity.Error,
            PipelineStage.SemanticModelConstruction, "No matching overload.", default, "hand-built.mist", null);
    }
}