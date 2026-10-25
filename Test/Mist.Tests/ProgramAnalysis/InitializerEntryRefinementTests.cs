using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Flow;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class InitializerEntryRefinementTests
{
    private readonly HandBuiltRefinements h = new();

    [Theory]
    [InlineData(-1L, "MIST_PA_CONSTRAINT_VIOLATED")]
    [InlineData(0L, "MIST_PA_CONSTRAINT_VIOLATED")]
    [InlineData(1L, null)]
    public void AnElementMeetsTheWherePredicateOfItsAdd(long element, string? expected)
    {
        var finding = Findings(Bag(Element(h.Literal(element, offset: 24), PositiveAdd()))).SingleOrDefault();

        Assert.Equal(expected, finding?.Number.Value);

        if(finding is not null)
        {
            Assert.Equal(25, finding.Location.Start.Column);
            Assert.Contains("parameter 'value'", finding.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AnElementALocalProvesPositiveMeetsIt()
    {
        var n = HandBuiltRefinements.Local(h.Int, "n");
        var body = HandBuiltModel.Block(
            HandBuiltRefinements.Give(n, h.Literal(5L)),
            HandBuiltRefinements.Evaluate(Bag(Element(HandBuiltRefinements.Read(n.Local), PositiveAdd()))));

        Assert.Empty(Findings(body));
    }

    [Fact]
    public void AnUnprovenElementGetsTheEstablishedProofDiagnostic()
    {
        var finding = Assert.Single(Findings(Bag(Element(HandBuiltRefinements.Local(h.Int, "m", offset: 24), PositiveAdd()))));

        Assert.Equal("MIST_PA_CONSTRAINT_UNPROVEN", finding.Number.Value);
        Assert.Equal(25, finding.Location.Start.Column);
    }

    [Fact]
    public void EachElementIsJudgedAgainstTheAddItSelected()
    {
        var text = new Method("Add", h.Built.Type("Void"), [new Parameter("text", h.String)], new MemberModifiers());

        var finding = Assert.Single(Findings(Bag(
            Element(h.Literal("a", offset: 10), text),
            Element(h.Literal(-3L, offset: 24), PositiveAdd()))));

        Assert.Equal("MIST_PA_CONSTRAINT_VIOLATED", finding.Number.Value);
        Assert.Equal(25, finding.Location.Start.Column);
    }

    [Fact]
    public void AGenericOwnersElementIsJudgedAgainstTheClosedParameter()
    {
        var owner = HandBuiltModel.Class("Bag");
        var item = new TypeParameter("T", 0, owner.Scope, owner);
        var add = new Method("Add", h.Built.Type("Void"), [new Parameter("item", item)], new MemberModifiers());
        var positive = h.Refine(h.Int, ComparisonKind.GreaterThan, 0L, "Positive");

        var finding = Assert.Single(Findings(Bag(Element(h.Literal(-1L, offset: 24), add, positive))));

        Assert.Equal("MIST_PA_REFINEMENT_VIOLATED", finding.Number.Value);
        Assert.Contains("'Positive'", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EachElementIsJudgedWhereItIsEvaluated()
    {
        var k = HandBuiltRefinements.Local(h.Int, "k");
        var reassigned = new AssignmentExpression(HandBuiltModel.Node(24), HandBuiltRefinements.Read(k.Local), h.Literal(-1L), h.Int);
        var body = HandBuiltModel.Block(
            HandBuiltRefinements.Give(k, h.Literal(1L)),
            HandBuiltRefinements.Evaluate(Bag(
                Element(HandBuiltRefinements.Read(k.Local, offset: 10), PositiveAdd()),
                Element(reassigned, PositiveAdd()))));

        var finding = Assert.Single(Findings(body));

        Assert.Equal(25, finding.Location.Start.Column);
    }

    [Theory]
    [InlineData("failed-value")]
    [InlineData("failed-add")]
    [InlineData("no-add")]
    [InlineData("failed-member")]
    public void AFailedEntryOwesNoProof(string failure)
    {
        var positive = h.Refine(h.Int, ComparisonKind.GreaterThan, 0L, "Positive");
        var negative = h.Literal(-1L);
        var entry = failure switch
        {
            "failed-value" => Element(new ErrorExpression(HandBuiltModel.Node()), PositiveAdd()),
            "failed-add" => new ObjectInitializerAssignment(HandBuiltModel.Node(), null, negative, null, PositiveAdd()) { IsTargetFailed = true },
            "no-add" => new ObjectInitializerAssignment(HandBuiltModel.Node(), null, negative, null),
            _ => new ObjectInitializerAssignment(HandBuiltModel.Node(), "Value", negative, positive) { IsTargetFailed = true },
        };

        Assert.Empty(Findings(Bag(entry)));
    }

    [Theory]
    [InlineData(-1L, "MIST_PA_REFINEMENT_VIOLATED")]
    [InlineData(4L, null)]
    public void ANamedEntryMeetsItsMembersRefinement(long value, string? expected)
    {
        var positive = h.Refine(h.Int, ComparisonKind.GreaterThan, 0L, "Positive");
        var entry = new ObjectInitializerAssignment(HandBuiltModel.Node(3), "Value", h.Literal(value, offset: 24), positive);

        var finding = Findings(Bag(entry)).SingleOrDefault();

        Assert.Equal(expected, finding?.Number.Value);
        Assert.Equal(expected is null ? null : 25, finding?.Location.Start.Column);
    }

    [Fact]
    public void ANestedConstructionsElementIsJudgedOnceInItsOwnBlock()
    {
        var inner = Bag(Element(h.Literal(-2L, offset: 24), PositiveAdd()));
        var outer = Bag(new ObjectInitializerAssignment(HandBuiltModel.Node(3), "Inner", inner, inner.Type));

        var finding = Assert.Single(Findings(outer));

        Assert.Equal("MIST_PA_CONSTRAINT_VIOLATED", finding.Number.Value);
        Assert.Equal(25, finding.Location.Start.Column);
    }

    [Fact]
    public void AnEntryIsEvaluatedAfterItsValueAndBeforeTheConstruction()
    {
        var first = Element(h.Literal(1L), PositiveAdd());
        var second = new ObjectInitializerAssignment(HandBuiltModel.Node(), "Count", h.Literal(2L), h.Int);
        var creation = Bag(first, second);

        var evaluated = FlowGraphBuilder.Build(creation).Blocks
            .SelectMany(block => block.Steps)
            .Where(step => step.Kind == FlowStepKind.Evaluate)
            .Select(step => step.Node)
            .ToList();

        Assert.Equal([first.Value, first, second.Value, second, creation], evaluated, ReferenceEqualityComparer.Instance);
    }

    private Method PositiveAdd()
    {
        return new Method("Add", h.Built.Type("Void"), [new Parameter("value", h.Refine(h.Int, ComparisonKind.GreaterThan, 0L, null))], new MemberModifiers());
    }

    private static ObjectInitializerAssignment Element(Expression value, Method add, IMistType? closed = null)
    {
        return new ObjectInitializerAssignment(value.Node, null, value, null, add) { AddParameterTypes = [closed ?? add.ParametersList[0].Type] };
    }

    private static NewExpression Bag(params ObjectInitializerAssignment[] entries)
    {
        return new NewExpression(HandBuiltModel.Node(3), HandBuiltModel.Class("Bag"), [], null, entries, []);
    }

    private IReadOnlyList<Diagnostic> Findings(object root)
    {
        return h.Built.Analyze(new RefinementAnalyzer(), root);
    }
}