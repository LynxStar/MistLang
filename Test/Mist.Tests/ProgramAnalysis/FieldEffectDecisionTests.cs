using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class FieldEffectDecisionTests
{
    [Theory]
    [InlineData("declared-readonly-call", false)]
    [InlineData("declared-readonly-call-given-a-pure-function", false)]
    [InlineData("declared-readonly-static-call-on-a-type-without-static-initializers", false)]
    [InlineData("declared-readonly-abstract-slot", false)]
    [InlineData("unmarked-call-without-a-body", true)]
    [InlineData("delegate-call", true)]
    [InlineData("declared-readonly-call-given-a-ref-argument", true)]
    [InlineData("declared-readonly-call-given-an-impure-function", true)]
    [InlineData("declared-readonly-static-call-on-a-type-with-a-static-initializer", true)]
    [InlineData("unmarked-abstract-slot-with-a-default-body", true)]
    [InlineData("object-creation", true)]
    public void ACallKeepsFieldFactsOnlyThroughAReadonlyCalleeAndNothingElseItRuns(string form, bool writes)
    {
        var f = new FieldEffectFragments();
        var h = f.H;
        var readonlyModifiers = new MemberModifiers { IsReadonly = true };
        var slot = new AbstractType("Source", "Probe", null, null, null);
        var holder = HandBuiltModel.Class("Tables");

        Expression node = form switch
        {
            "declared-readonly-call" => f.Call(f.Method("Peek", f.Writes(), readonlyModifiers)),
            "declared-readonly-call-given-a-pure-function" or "declared-readonly-call-given-an-impure-function" =>
                f.Call(f.Method("Peek", null, readonlyModifiers, parameters: new Parameter("pick", new FunctionType([], h.Int, isPure: form.EndsWith("a-pure-function", StringComparison.Ordinal)))),
                    HandBuiltRefinements.Local(new FunctionType([], h.Int, isPure: form.EndsWith("a-pure-function", StringComparison.Ordinal)), "pick")),
            "declared-readonly-static-call-on-a-type-without-static-initializers" or "declared-readonly-static-call-on-a-type-with-a-static-initializer" =>
                f.Call(f.Method("Peek", null, readonlyModifiers with { IsStatic = true }, Initialized(holder, form.EndsWith("initializer", StringComparison.Ordinal), h))),
            "declared-readonly-abstract-slot" => f.Call(f.Method("Peek", null, readonlyModifiers, slot)),
            "unmarked-call-without-a-body" => f.Call(f.Method("Peek", null, new MemberModifiers { IsClrProjected = true })),
            "delegate-call" => new InvocationExpression(HandBuiltModel.Node(), h.Literal(0L), [], h.Int, null, []),
            "declared-readonly-call-given-a-ref-argument" =>
                f.Call(f.Method("Peek", null, readonlyModifiers, parameters: FieldEffectFragments.ByRef(h.Int)), HandBuiltRefinements.Local(h.Int, "slot")),
            "unmarked-abstract-slot-with-a-default-body" => f.Call(f.Method("Peek", f.Reads(), declaring: slot)),
            "object-creation" => new NewExpression(HandBuiltModel.Node(), f.Owner, [], null, [], []),
            _ => throw new InvalidOperationException(form),
        };

        Assert.Equal(writes, new FieldEffects().MayWrite(node));
    }

    [Theory]
    [InlineData("read-through-an-auto-getter", false)]
    [InlineData("read-through-a-declared-readonly-getter", false)]
    [InlineData("write-through-an-auto-setter", false)]
    [InlineData("read-through-an-unmarked-abstract-getter-with-a-default-body", true)]
    [InlineData("write-through-an-unmarked-abstract-setter-with-a-default-body", true)]
    public void AnAccessorKeepsFieldFactsOnlyWhenTheOneItRunsIsReadonly(string form, bool writes)
    {
        var f = new FieldEffectFragments();
        var slot = new AbstractType("Source", "Probe", null, null, null);

        Expression node = form switch
        {
            "read-through-an-auto-getter" => f.Read(f.Property(FieldEffectFragments.Accessor(isAuto: true), FieldEffectFragments.Accessor(f.Writes()))),
            "read-through-a-declared-readonly-getter" =>
                f.Read(f.Property(new PropertyAccessor { Body = f.Writes(), OwnModifiers = new MemberModifiers { IsReadonly = true } }, null)),
            "write-through-an-auto-setter" => f.Write(f.Property(FieldEffectFragments.Accessor(f.Writes()), FieldEffectFragments.Accessor(isAuto: true))),
            "read-through-an-unmarked-abstract-getter-with-a-default-body" => f.Read(f.Property(FieldEffectFragments.Accessor(f.Reads()), null, declaring: slot)),
            "write-through-an-unmarked-abstract-setter-with-a-default-body" =>
                f.Write(f.Property(null, FieldEffectFragments.Accessor(HandBuiltModel.Block()), declaring: slot)),
            _ => throw new InvalidOperationException(form),
        };

        Assert.Equal(writes, new FieldEffects().MayWrite(node));
    }

    private static ClassType Initialized(ClassType holder, bool initialized, HandBuiltRefinements h)
    {
        if(initialized)
        {
            holder.FieldsList.Add(new Field("table", h.Int, default, new MemberModifiers { IsStatic = true }) { Initializer = h.Literal(1L), DeclaringType = holder });
        }

        return holder;
    }
}