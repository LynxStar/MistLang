using Mist.Generated;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class EnumRefinementFlowTests
{
    [Theory]
    [InlineData(-20L, "MIST_PA_REFINEMENT_VIOLATED")]
    [InlineData(10L, null)]
    [InlineData(100L, null)]
    public void AliasesAndLocalCopiesCarryEnumFacts(long number, string? expected)
    {
        var model = new EnumRefinementModel();
        var color = model.SourceEnum();
        var green = EnumRefinementModel.Member(color, "Green", model.Number(color, 10L));
        var value = EnumRefinementModel.Member(color, "Value", model.Number(color, number));
        var baseAlias = Alias("Palette", color);
        var targetAlias = Alias("WarmAlias", model.AtLeast(color, green));
        var first = HandBuiltRefinements.Local(baseAlias, "first");
        var copy = HandBuiltRefinements.Local(baseAlias, "copy");
        var body = HandBuiltModel.Block(
            HandBuiltRefinements.Give(first, value),
            HandBuiltRefinements.Give(copy, first),
            HandBuiltRefinements.Need(copy, targetAlias));

        AssertFinding(model, body, expected);
    }

    [Theory]
    [InlineData(10L, 100L, null)]
    [InlineData(-20L, 100L, "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData(-20L, -20L, "MIST_PA_REFINEMENT_VIOLATED")]
    public void JoinsRequireEveryIncomingEnumFactToSatisfyThePredicate(long left, long right, string? expected)
    {
        var model = new EnumRefinementModel();
        var color = model.SourceEnum();
        var green = EnumRefinementModel.Member(color, "Green", model.Number(color, 10L));
        var first = EnumRefinementModel.Member(color, "First", model.Number(color, left));
        var second = EnumRefinementModel.Member(color, "Second", model.Number(color, right));
        var local = HandBuiltRefinements.Local(color);
        var body = HandBuiltModel.Block(
            HandBuiltRefinements.Give(local, first),
            new IfStatement(HandBuiltRefinements.Local(model.Facts.Bool, "flag"),
                HandBuiltModel.Block(HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(local, second))), null),
            HandBuiltRefinements.Need(local, model.AtLeast(color, green)));

        AssertFinding(model, body, expected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReassignmentOrAnUnknownJoinPathLosesTheProof(bool join)
    {
        var model = new EnumRefinementModel();
        var color = model.SourceEnum();
        var green = EnumRefinementModel.Member(color, "Green", model.Number(color, 10L));
        var local = HandBuiltRefinements.Local(color);
        var reset = HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(local, HandBuiltRefinements.Local(color, "unknown")));
        var body = HandBuiltModel.Block(
            HandBuiltRefinements.Give(local, green),
            join ? HandBuiltRefinements.If(HandBuiltRefinements.Local(model.Facts.Bool, "flag"), reset) : reset,
            HandBuiltRefinements.Need(local, model.AtLeast(color, green)));

        AssertFinding(model, body, "MIST_PA_REFINEMENT_UNPROVEN");
    }

    [Theory]
    [InlineData(false, false, null)]
    [InlineData(false, true, "MIST_PA_REFINEMENT_UNPROVEN")]
    [InlineData(true, false, "MIST_PA_REFINEMENT_UNPROVEN")]
    public void OnlyTheBuiltInGuardProvesItsThenBranch(bool userOperator, bool elseBranch, string? expected)
    {
        var model = new EnumRefinementModel();
        var color = model.SourceEnum();
        var green = EnumRefinementModel.Member(color, "Green", model.Number(color, 10L));
        var local = HandBuiltRefinements.Local(color);
        var condition = new BinaryExpression(HandBuiltModel.Node(), local, BinaryOperatorKind.GreaterThanOrEqual, green, model.Facts.Bool)
        {
            Operator = userOperator ? new Operator(OperatorKind.GreaterThanOrEqual, model.Facts.Bool,
                [new Parameter("left", color), new Parameter("right", color)], new MemberModifiers()) : null,
        };
        var need = HandBuiltRefinements.Need(local, model.AtLeast(color, green));
        var body = HandBuiltModel.Block(new IfStatement(condition,
            elseBranch ? HandBuiltModel.Block() : HandBuiltModel.Block(need),
            elseBranch ? HandBuiltModel.Block(need) : null));

        AssertFinding(model, body, expected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UserOperatorsAndConversionsDoNotCopyAnEnumConstant(bool conversion)
    {
        var model = new EnumRefinementModel();
        var h = model.Facts;
        var color = model.SourceEnum();
        var green = EnumRefinementModel.Member(color, "Green", model.Number(color, 10L));
        Expression result;
        if(conversion)
        {
            var called = new CastOperator(h.Int, new Parameter("value", color), true, new MemberModifiers());
            result = new ImplicitConversionExpression(HandBuiltModel.Node(), green, h.Int,
                [new ConversionFunction(called, color, h.Int)]);
        }
        else
        {
            result = new UnaryExpression(HandBuiltModel.Node(), UnaryOperatorKind.Negate, green, h.Int)
            {
                Operator = new Operator(OperatorKind.Minus, h.Int, [new Parameter("value", color)], new MemberModifiers()),
            };
        }

        var local = HandBuiltRefinements.Local(h.Int);
        var body = HandBuiltModel.Block(HandBuiltRefinements.Give(local, result),
            HandBuiltRefinements.Need(local, h.Refine(h.Int, ComparisonKind.GreaterThanOrEqual, 0L)));

        AssertFinding(model, body, "MIST_PA_REFINEMENT_UNPROVEN");
    }

    [Fact]
    public void AnOutOfRangeUnsignedValueStaysUnknownAfterCopiesAndJoins()
    {
        var model = new EnumRefinementModel();
        var color = model.SourceEnum(underlying: model.Facts.Built.Type("UInt64"));
        var high = EnumRefinementModel.Member(color, "High", model.Number(color, long.MaxValue));
        var outside = EnumRefinementModel.Member(color, "Outside", model.Number(color, ulong.MaxValue));
        var target = model.Facts.Refine(color, ComparisonKind.LessThanOrEqual, high);
        var first = HandBuiltRefinements.Local(color, "first");
        var copy = HandBuiltRefinements.Local(color, "copy");
        var body = HandBuiltModel.Block(HandBuiltRefinements.Give(first, outside),
            HandBuiltRefinements.Give(copy, first),
            HandBuiltRefinements.If(HandBuiltRefinements.Local(model.Facts.Bool, "flag"),
                HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(copy, high))),
            HandBuiltRefinements.Need(copy, target));

        AssertFinding(model, body, "MIST_PA_REFINEMENT_UNPROVEN");
    }

    [Fact]
    public void UserArithmeticDoesNotShiftTheStoredEnumFact()
    {
        var model = new EnumRefinementModel();
        var color = model.SourceEnum();
        var first = EnumRefinementModel.Member(color, "First", model.Number(color, 10L));
        var step = EnumRefinementModel.Member(color, "Step", model.Number(color, -100L));
        var local = HandBuiltRefinements.Local(color);
        var sum = new BinaryExpression(HandBuiltModel.Node(), local, BinaryOperatorKind.Add, step, color)
        {
            Operator = new Operator(OperatorKind.Plus, color,
                [new Parameter("left", color), new Parameter("right", color)], new MemberModifiers()),
        };
        var body = HandBuiltModel.Block(HandBuiltRefinements.Give(local, first),
            HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(local, sum)),
            HandBuiltRefinements.Need(local, model.AtLeast(color, first)));

        AssertFinding(model, body, "MIST_PA_REFINEMENT_UNPROVEN");
    }

    private static TypeAlias Alias(string name, IMistType type)
    {
        return new TypeAlias(name, "Probe", new Scope(null), null, new TypeAliasDeclarationNode()) { AliasedType = type };
    }

    private static void AssertFinding(EnumRefinementModel model, Block body, string? expected)
    {
        var findings = model.Facts.Built.Analyze(new RefinementAnalyzer(), body);
        if(expected is null)
        {
            Assert.Empty(findings);
        }
        else
        {
            Assert.Equal(expected, Assert.Single(findings).Id.Value);
        }
    }
}