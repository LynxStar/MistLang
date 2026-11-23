using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class CoalescingAssignmentRefinementTests
{
    private readonly HandBuiltRefinements h = new();
    private readonly RefinedType small;
    private readonly IMistType optional;

    public CoalescingAssignmentRefinementTests()
    {
        small = h.Between(h.Int, 11L, 19L, "Small");
        optional = h.Built.Interner.GetOrCreateUnionType([small, h.Built.Type("Void")]);
    }

    [Theory]
    [InlineData("local", false)]
    [InlineData("local", true)]
    [InlineData("member-through-a-value", false)]
    [InlineData("member-through-a-value", true)]
    [InlineData("field-of-this", false)]
    [InlineData("field-of-this", true)]
    public void AnUnprovenValueIsRefusedAsAPlainAssignmentsValueIs(string target, bool coalescing)
    {
        var value = HandBuiltRefinements.Local(h.Int, "k", offset: 37);
        var body = HandBuiltModel.Block(Body(target, value, coalescing));

        var diagnostic = Assert.Single(h.Built.Analyze(new RefinementAnalyzer(), body));

        Assert.Equal("MIST0272", diagnostic.Number.Value);
        Assert.Contains("fails when k <= 10", diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(38, diagnostic.Position().Start.Column);
    }

    [Fact]
    public void AValueOfTheRefinedTypeIsAdmittedForACoalescingAssignment()
    {
        var body = HandBuiltModel.Block(Body("local", HandBuiltRefinements.Local(small, "k"), coalescing: true));

        Assert.Empty(h.Built.Analyze(new RefinementAnalyzer(), body));
    }

    [Fact]
    public void AValueProvenByAGuardIsAdmittedForACoalescingAssignment()
    {
        var value = HandBuiltRefinements.Local(h.Int, "k");
        var assign = Body("local", value, coalescing: true);
        var guard = h.And(h.Binary(value, BinaryOperatorKind.GreaterThan, h.Literal(10L)), h.Binary(value, BinaryOperatorKind.LessThan, h.Literal(20L)));

        Assert.Empty(h.Built.Analyze(new RefinementAnalyzer(), HandBuiltModel.Block(HandBuiltRefinements.If(guard, assign))));
    }

    [Fact]
    public void ALiteralInsideTheRefinementIsAdmittedForACoalescingAssignment()
    {
        var body = HandBuiltModel.Block(Body("local", h.Literal(15L), coalescing: true));

        Assert.Empty(h.Built.Analyze(new RefinementAnalyzer(), body));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnUnprovenIndexTargetIsRefusedOnceForAPlainOrACoalescingAssignment(bool coalescing)
    {
        var items = HandBuiltRefinements.Local(h.Sequence(), "items");
        var assignment = new AssignmentExpression(
            HandBuiltModel.Node(), h.At(items, HandBuiltRefinements.Local(h.Int, "slot")), h.Literal(5L), h.Int, coalescing);
        var body = HandBuiltModel.Block(HandBuiltRefinements.Give(items, h.Array(3L, erased: true)), HandBuiltRefinements.Evaluate(assignment));

        var diagnostic = Assert.Single(h.Built.Analyze(new RefinementAnalyzer(), body));

        Assert.Equal("MIST0275", diagnostic.Number.Value);
    }

    private Statement[] Body(string target, Expression value, bool coalescing)
    {
        var owner = HandBuiltModel.Class("Box");
        var field = new Field("V", optional, default, new MemberModifiers()) { DeclaringType = owner };
        var local = new LocalVariable("s", optional);
        Expression written = target switch
        {
            "local" => HandBuiltRefinements.Read(local),
            "member-through-a-value" => new MemberAccessExpression(
                HandBuiltModel.Node(), HandBuiltRefinements.Local(owner, "b"), "V", optional, member: new FieldResult(field, owner, [])),
            "field-of-this" => new FieldReferenceExpression(HandBuiltModel.Node(), field),
            _ => throw new InvalidOperationException(target),
        };
        var assignment = new AssignmentExpression(HandBuiltModel.Node(), written, value, optional, coalescing);
        Statement[] declared = target == "local" ? [new LocalDeclarationStatement(local, null)] : [];

        return [.. declared, HandBuiltRefinements.Evaluate(assignment)];
    }
}