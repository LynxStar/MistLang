using Mist.ProgramAnalysis.Flow;
using Mist.ProgramModel.Model.Enums;
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
                foreach(var operation in new[] { "initialize", "assign", "owned-argument", "owned-field", "return", "drop" })
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
    public void AnAffineLoanKeepsTheWithRelease(string shape)
    {
        var f = new LifetimeFragments();
        var type = f.DisciplinedValue(shape, SubstructuralDiscipline.Affine);
        var source = f.Local("source", type);
        var passed = Ref(source);
        var keep = new Method("Keep", f.Void, [Owned("value", type, OwnershipKind.Frozen)], new MemberModifiers { IsStatic = true }) { DeclaringType = f.Sink };
        var scope = With(Ref(source), HandBuiltModel.Block(Run(f.Call(keep, passed))));
        var plan = LocalLifetimes.Plan(HandBuiltModel.Block(Declare(source, Fresh(type)), scope));

        Assert.Null(plan.ReleaseAt(scope));
        Assert.Empty(plan.MarksAfter(passed));
        Assert.Equal(LifetimeRelease.None, plan.ReleaseOf(source));
    }
}