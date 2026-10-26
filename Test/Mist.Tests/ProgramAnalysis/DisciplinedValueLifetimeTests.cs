using Mist.ProgramAnalysis.Flow;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.Tests.Helpers;
using static Mist.Tests.Helpers.LifetimeFragments;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class DisciplinedValueLifetimeTests
{
    public static TheoryData<string, string> Transfers
    {
        get
        {
            var cases = new TheoryData<string, string>();
            foreach(var shape in new[] { "struct", "ref-struct", "constructed-struct", "class" })
            {
                foreach(var operation in new[] { "initialize", "assign", "owned-argument", "owned-field", "array", "return", "drop" })
                {
                    cases.Add(shape, operation);
                }
            }

            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(Transfers))]
    public void ALinearTransferLeavesOnlyItsDestinationOwingARelease(string shape, string operation)
    {
        var f = new LifetimeFragments();
        var type = f.DisciplinedValue(shape, SubstructuralDiscipline.Linear);
        var source = f.Local("source", type);
        var destination = f.Local("destination", type);
        var body = operation == "assign"
            ? HandBuiltModel.Block(Declare(source, Fresh(type)), Declare(destination, null), f.MoveValue(operation, source, destination))
            : HandBuiltModel.Block(Declare(source, Fresh(type)), f.MoveValue(operation, source, destination));
        var plan = LocalLifetimes.Plan(body);

        Assert.Equal(LifetimeRelease.None, plan.ReleaseOf(source));
        var expected = operation switch
        {
            "initialize" => LifetimeRelease.AtExit,
            "assign" => LifetimeRelease.Guarded,
            _ => LifetimeRelease.None,
        };
        Assert.Equal(expected, plan.ReleaseOf(destination));
    }

    [Theory]
    [InlineData("struct")]
    [InlineData("ref-struct")]
    [InlineData("constructed-struct")]
    [InlineData("class")]
    public void ALiveLinearValueStillOwesItsScopeExitRelease(string shape)
    {
        var f = new LifetimeFragments();
        var source = f.Local("source", f.DisciplinedValue(shape, SubstructuralDiscipline.Linear));
        var plan = LocalLifetimes.Plan(HandBuiltModel.Block(Declare(source, Fresh(source.Type))));

        Assert.Equal(LifetimeRelease.AtExit, plan.ReleaseOf(source));
    }

    [Theory]
    [InlineData("struct")]
    [InlineData("ref-struct")]
    [InlineData("constructed-struct")]
    [InlineData("class")]
    public void AnAffineScopeGuardsAConditionalDropWithoutCreatingAnAutomaticRelease(string shape)
    {
        var f = new LifetimeFragments();
        var source = f.Local("source", f.DisciplinedValue(shape, SubstructuralDiscipline.Affine));
        var drop = Drop(source);
        var scope = With(Ref(source), HandBuiltModel.Block(f.If(HandBuiltModel.Block(Run(drop)))));
        var plan = LocalLifetimes.Plan(HandBuiltModel.Block(Declare(source, Fresh(source.Type)), scope));

        Assert.Equal(LifetimeRelease.None, plan.ReleaseOf(source));
        Assert.Equal((source, LifetimeRelease.Guarded), Assert.NotNull(plan.ReleaseAt(scope)));
        Assert.True(plan.NeedsFlag(source));
        Assert.Equal([new LifetimeMark(source, Owned: false)], plan.MarksAfter(((MemberAccessExpression)drop.Receiver).Receiver));
    }

    [Fact]
    public void AnAffineConditionalCallKeepsItsScopeGuardWhenTheReceiverMayBeAbsent()
    {
        var f = new LifetimeFragments();
        var type = f.DisciplinedValue("struct", SubstructuralDiscipline.Affine);
        var source = f.Local("source", type);
        var handed = Ref(source);
        var take = new Method("Take", f.Void, [Owned("value", type)], default) { DeclaringType = f.Box };
        var callee = new MemberAccessExpression(HandBuiltModel.Node(), Ref(f.Local("box", f.Box)), take.Name, f.Void);
        var call = new InvocationExpression(HandBuiltModel.Node(), callee, [handed], f.Void, take, [type]);
        var scope = With(Ref(source), HandBuiltModel.Block(Run(call)));
        var plan = LocalLifetimes.Plan(HandBuiltModel.Block(Declare(source, Fresh(type)), scope));

        Assert.Equal(LifetimeRelease.None, plan.ReleaseOf(source));
        Assert.True(plan.NeedsFlag(source));
        Assert.Equal([new LifetimeMark(source, Owned: false, call)], plan.MarksAfter(handed));
    }

    [Theory]
    [InlineData("struct")]
    [InlineData("ref-struct")]
    [InlineData("constructed-struct")]
    [InlineData("class")]
    [Trait("Regression", "MIST-122")]
    public void AnAffinePlainParameterPassKeepsTheExistingWithRelease(string shape)
    {
        var f = new LifetimeFragments();
        var type = f.DisciplinedValue(shape, SubstructuralDiscipline.Affine);
        var source = f.Local("source", type);
        var passed = Ref(source);
        var keep = new Method("Keep", f.Void, [new Parameter("value", type)], new MemberModifiers { IsStatic = true }) { DeclaringType = f.Sink };
        var scope = With(Ref(source), HandBuiltModel.Block(Run(f.Call(keep, passed))));
        var plan = LocalLifetimes.Plan(HandBuiltModel.Block(Declare(source, Fresh(type)), scope));

        Assert.Equal((source, LifetimeRelease.AtExit), Assert.NotNull(plan.ReleaseAt(scope)));
        Assert.Empty(plan.MarksAfter(passed));
        Assert.Equal(LifetimeRelease.None, plan.ReleaseOf(source));
    }
}