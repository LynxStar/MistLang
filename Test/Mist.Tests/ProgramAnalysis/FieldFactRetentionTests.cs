using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class FieldFactRetentionTests
{
    [Theory]
    [InlineData("inferred-readonly-call", null)]
    [InlineData("inferred-readonly-getter", null)]
    [InlineData("readonly-getter-beside-a-writing-setter", null)]
    [InlineData("setter-that-writes-nothing", null)]
    [InlineData("inferred-readonly-indexer-getter", null)]
    [InlineData("mutator-call", "MIST_PA_INDEX_UNPROVEN")]
    [InlineData("readonly-looking-call-to-a-mutator", "MIST_PA_INDEX_UNPROVEN")]
    [InlineData("inferred-readonly-call-given-a-ref-argument", "MIST_PA_INDEX_UNPROVEN")]
    [InlineData("delegate-call", "MIST_PA_INDEX_UNPROVEN")]
    [InlineData("object-creation", "MIST_PA_INDEX_UNPROVEN")]
    [InlineData("getter-that-writes", "MIST_PA_INDEX_UNPROVEN")]
    [InlineData("setter-that-writes", "MIST_PA_INDEX_UNPROVEN")]
    [InlineData("indexer-getter-that-writes", "MIST_PA_INDEX_UNPROVEN")]
    public void AFieldLengthGuardSurvivesOnlyAStepProvenNotToWriteAField(string between, string? expected)
    {
        var f = new FieldEffectFragments();
        var guard = f.H.Binary(f.H.Length(f.ItemsRead), BinaryOperatorKind.GreaterThan, f.H.Literal(0L));
        var body = HandBuiltModel.Block(HandBuiltRefinements.If(guard, Between(f, between), HandBuiltRefinements.Evaluate(f.H.At(f.ItemsRead, f.H.Literal(0L)))));

        Assert.Equal(expected, f.H.Built.Analyze(new RefinementAnalyzer(), body).SingleOrDefault()?.Id.Value);
    }

    [Theory]
    [InlineData("inferred-readonly-call", null)]
    [InlineData("mutator-call", "MIST_PA_INDEX_UNPROVEN")]
    public void ASymbolicFieldBoundSurvivesOnlyAStepProvenNotToWriteAField(string between, string? expected)
    {
        var f = new FieldEffectFragments();
        var slot = HandBuiltRefinements.Local(f.H.Int, "slot");
        var guard = f.H.And(f.H.Binary(slot, BinaryOperatorKind.GreaterThanOrEqual, f.H.Literal(0L)),
            f.H.Binary(slot, BinaryOperatorKind.LessThan, f.H.Length(f.ItemsRead)));
        var body = HandBuiltModel.Block(HandBuiltRefinements.If(guard, Between(f, between), HandBuiltRefinements.Evaluate(f.H.At(f.ItemsRead, slot))));

        Assert.Equal(expected, f.H.Built.Analyze(new RefinementAnalyzer(), body).SingleOrDefault()?.Id.Value);
    }

    private static Statement Between(FieldEffectFragments f, string between)
    {
        Expression step = between switch
        {
            "inferred-readonly-call" => f.Call(f.Method("Peek", f.Reads())),
            "mutator-call" => f.Call(f.Method("Peek", f.Writes())),
            "readonly-looking-call-to-a-mutator" =>
                f.Call(f.Method("Outer", HandBuiltModel.Block(new ReturnStatement(f.Call(f.Method("Peek", f.Writes())), f.H.Int)))),
            "inferred-readonly-call-given-a-ref-argument" =>
                f.Call(f.Method("Peek", f.Reads(), parameters: FieldEffectFragments.ByRef(f.H.Int)), HandBuiltRefinements.Local(f.H.Int, "spare")),
            "delegate-call" => new InvocationExpression(HandBuiltModel.Node(), f.H.Literal(0L), [], f.H.Int, null, []),
            "object-creation" => new NewExpression(HandBuiltModel.Node(), f.Owner, [], null, [], []),
            "inferred-readonly-getter" => f.Read(f.Property(FieldEffectFragments.Accessor(f.Reads()), null)),
            "getter-that-writes" => f.Read(f.Property(FieldEffectFragments.Accessor(f.Writes()), null)),
            "readonly-getter-beside-a-writing-setter" =>
                f.Read(f.Property(FieldEffectFragments.Accessor(f.Reads()), FieldEffectFragments.Accessor(HandBuiltModel.Block(Counted(f))))),
            "setter-that-writes-nothing" => f.Write(f.Property(FieldEffectFragments.Accessor(f.Writes()), FieldEffectFragments.Accessor(HandBuiltModel.Block()))),
            "setter-that-writes" => f.Write(f.Property(FieldEffectFragments.Accessor(f.Reads()), FieldEffectFragments.Accessor(HandBuiltModel.Block(Counted(f))))),
            "inferred-readonly-indexer-getter" => f.Read(f.Indexer(FieldEffectFragments.Accessor(f.Reads()))),
            "indexer-getter-that-writes" => f.Read(f.Indexer(FieldEffectFragments.Accessor(f.Writes()))),
            _ => throw new InvalidOperationException(between),
        };

        return HandBuiltRefinements.Evaluate(step);
    }

    private static Statement Counted(FieldEffectFragments f)
    {
        return HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(new FieldReferenceExpression(HandBuiltModel.Node(), f.Count), f.H.Literal(2L)));
    }
}