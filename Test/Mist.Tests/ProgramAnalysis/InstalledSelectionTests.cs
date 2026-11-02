using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.Tests.Helpers;
using static Mist.Tests.Helpers.LifetimeFragments;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class InstalledSelectionTests
{
    private readonly LifetimeFragments f = new();

    public static TheoryData<string, string> Consumptions
    {
        get
        {
            var cases = new TheoryData<string, string>();
            foreach(var selection in new[] { "switch", "coalesce" })
            {
                foreach(var action in new[] { "plain", "binding", "drop", "return" })
                {
                    cases.Add(selection, action);
                }
            }

            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(Consumptions))]
    public void ASelectionCannotHideInstalledConsumption(string selection, string action)
    {
        var r = f.Local("r");
        var value = f.Selected(selection, Ref(r), Ref(r));
        var consume = new Method("Consume", f.Void, [Owned("value", f.Token, OwnershipKind.Unrestricted)], new MemberModifiers { IsStatic = true }) { DeclaringType = f.Sink };
        Statement consuming = action switch
        {
            "plain" => Run(f.Call(consume, value)),
            "binding" => Declare(f.Local("alias"), value),
            "drop" => Run(DropOf(value)),
            _ => Return(value),
        };
        var body = HandBuiltModel.Block(Declare(r, f.New(1)), With(Ref(r), HandBuiltModel.Block(consuming)));

        Assert.Equal("MIST0479", Assert.Single(f.Model.Analyze(new OwnershipAnalyzer(), body)).Number.Value);
    }

    [Theory]
    [InlineData("switch")]
    [InlineData("coalesce")]
    public void ASelectedInstallationProtectsTheOriginalNameAndReleasesItAtPop(string selection)
    {
        var r = f.Local("r");
        var body = HandBuiltModel.Block(Declare(r, f.New(1)),
            With(f.Selected(selection, Ref(r), Ref(r)), HandBuiltModel.Block(Run(Drop(r)))), Run(Ref(r)));

        var diagnostics = f.Model.Analyze(new OwnershipAnalyzer(), body);

        Assert.Equal(["MIST0479", "MIST0227"], diagnostics.Select(diagnostic => diagnostic.Number.Value));
    }

    [Theory]
    [InlineData("switch")]
    [InlineData("coalesce")]
    public void AFreshArmLaunderingAnInstalledValueStillRefusesTheAlias(string selection)
    {
        var r = f.Local("r");
        var alias = f.Local("alias");
        var body = HandBuiltModel.Block(Declare(r, f.New(1)), With(Ref(r), HandBuiltModel.Block(
            Declare(alias, f.Selected(selection, Ref(r), f.New(2))), Run(Drop(alias)))));

        Assert.Equal("MIST0479", Assert.Single(f.Model.Analyze(new OwnershipAnalyzer(), body)).Number.Value);
    }

    [Theory]
    [InlineData("switch")]
    [InlineData("coalesce")]
    public void ASelectionOfFreshUnrelatedInstancesStaysIndependent(string selection)
    {
        var alias = f.Local("alias");
        var body = HandBuiltModel.Block(Declare(alias, f.Selected(selection, f.New(1), f.New(2))), Run(Drop(alias)));

        Assert.Empty(f.Model.Analyze(new OwnershipAnalyzer(), body));
    }

    [Theory]
    [InlineData("switch")]
    [InlineData("coalesce")]
    public void SelectingAScalarReadOfTheInstalledValueIsAdmitted(string selection)
    {
        var type = f.DisciplinedValue("class", SubstructuralDiscipline.Linear);
        var r = f.Local("r", type);
        var body = HandBuiltModel.Block(Declare(r, LifetimeFragments.Fresh(type)), With(Ref(r), HandBuiltModel.Block(
            Declare(f.Local("n", f.Int32), f.Selected(selection, f.Count(r), f.Count(r))))));

        Assert.Empty(f.Model.Analyze(new OwnershipAnalyzer(), body));
    }

    [Theory]
    [InlineData("switch")]
    [InlineData("coalesce")]
    public void ASelectedExplicitLoanPermitsNonOwningNestedInstallationAndLending(string selection)
    {
        var r = f.Local("r");
        var lease = f.Local("lease", ownership: OwnershipKind.Frozen);
        var inspect = new Method("Inspect", f.Void, [Owned("value", f.Token, OwnershipKind.Frozen)], new MemberModifiers { IsStatic = true }) { DeclaringType = f.Sink };
        var body = HandBuiltModel.Block(Declare(r, f.New(1)), With(Ref(r), HandBuiltModel.Block(
            Declare(lease, f.Selected(selection, Ref(r), Ref(r))),
            With(f.Selected(selection, Ref(lease), Ref(lease)), HandBuiltModel.Block(Run(f.Call(inspect, f.Selected(selection, Ref(lease), Ref(lease)))))))));

        Assert.Empty(f.Model.Analyze(new OwnershipAnalyzer(), body));
        Assert.Empty(f.Model.Analyze(new TierAnalyzer(), body));
    }
}
