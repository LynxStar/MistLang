using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.RuntimeDependentValues;

/// <summary>
/// A closing that fills <c>A</c> with a run-time value and <c>B</c>, which <c>where B >= A</c> relates to it, with a
/// constant, judged by the refinement analyzer over hand-built bodies at the step the capture is declared
/// (adhoc-refinement-constraints Requirements 5.2 and 5.4).
/// </summary>
[Trait("Tier", "2")]
public sealed class RelationalClosingFlowTests
{
    private readonly HandBuiltSlots slots = new();
    private readonly MemberType pair;
    private readonly LocalVariable a;

    public RelationalClosingFlowTests()
    {
        pair = HandBuiltSlots.Generic(HandBuiltModel.Class("P<,>"), "A", slots.Int);
        var low = pair.TypeParametersList[0];
        var high = new TypeParameter("B", 1, pair.Scope, pair) { IsDependent = true, Domain = (MistType)slots.Int };
        pair.TypeParametersList.Add(high);
        high.RefinementNode = HandBuiltModel.Node();
        high.Refinement = new ComparisonPattern(HandBuiltModel.Node(), slots.Int, ComparisonKind.GreaterThanOrEqual, new TypeReferenceExpression(HandBuiltModel.Node(), low));
        a = new LocalVariable("a", slots.Int);
    }

    private IReadOnlyList<Diagnostic> Analyze(Expression condition)
    {
        var made = slots.Capture(a);
        var creation = new NewExpression(HandBuiltModel.Node(), slots.Closed(pair, made.Value, slots.Literal(1)), [], null, [], []) { Captures = [made] };

        return slots.Refinements.Built.Analyze(new RefinementAnalyzer(), HandBuiltModel.Block(HandBuiltRefinements.If(condition, HandBuiltRefinements.Evaluate(creation))));
    }

    private Expression AtLeast(long floor)
    {
        return slots.Refinements.Binary(HandBuiltRefinements.Read(a), BinaryOperatorKind.GreaterThanOrEqual, slots.Refinements.Literal(floor), slots.Refinements.Bool);
    }

    private Expression AtMost(long ceiling)
    {
        return slots.Refinements.Binary(HandBuiltRefinements.Read(a), BinaryOperatorKind.LessThanOrEqual, slots.Refinements.Literal(ceiling), slots.Refinements.Bool);
    }

    [Fact]
    public void AConstantTheRunTimeSlotItIsRelatedToMayExceedIsRefusedAtTheClosing()
    {
        var refused = Assert.Single(Analyze(AtLeast(0)));

        Assert.Equal("MIST0278", refused.Number.Value);
        Assert.Contains("Dependent type argument '1' does not satisfy '>= A' on parameter 'B'", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AConstantTheFactsProveAtLeastTheRunTimeSlotIsAdmitted()
    {
        Assert.Empty(Analyze(slots.Refinements.And(AtLeast(0), AtMost(1))));
    }
}