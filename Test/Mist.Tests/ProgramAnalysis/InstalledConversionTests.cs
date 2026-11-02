using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.Tests.Helpers;
using static Mist.Tests.Helpers.LifetimeFragments;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class InstalledConversionTests
{
    private readonly LifetimeFragments f = new();

    public static TheoryData<string, string> Consumptions
    {
        get
        {
            var cases = new TheoryData<string, string>();
            foreach(var conversion in new[] { "as", "cast", "implicit" })
            {
                foreach(var action in new[] { "plain", "binding", "drop", "return" })
                {
                    cases.Add(conversion, action);
                }
            }

            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(Consumptions))]
    public void ATransparentConversionCannotHideInstalledConsumption(string conversion, string action)
    {
        var r = f.Local("r");
        var value = Converted(Ref(r), conversion);
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
    [InlineData("as")]
    [InlineData("cast")]
    [InlineData("implicit")]
    public void AConvertedInstallationProtectsTheOriginalNameAndReleasesItAtPop(string conversion)
    {
        var r = f.Local("r");
        var body = HandBuiltModel.Block(Declare(r, f.New(1)),
            With(Converted(Ref(r), conversion), HandBuiltModel.Block(Run(Drop(r)))), Run(Ref(r)));

        var diagnostics = f.Model.Analyze(new OwnershipAnalyzer(), body);

        Assert.Equal(["MIST0479", "MIST0227"], diagnostics.Select(diagnostic => diagnostic.Number.Value));
    }

    [Theory]
    [InlineData("as")]
    [InlineData("cast")]
    [InlineData("implicit")]
    public void AConvertedExplicitLoanPermitsNonOwningNestedInstallationAndLending(string conversion)
    {
        var r = f.Local("r");
        var lease = f.Local("lease", ownership: OwnershipKind.Frozen);
        var inspect = new Method("Inspect", f.Void, [Owned("value", f.Token, OwnershipKind.Frozen)], new MemberModifiers { IsStatic = true }) { DeclaringType = f.Sink };
        var body = HandBuiltModel.Block(Declare(r, f.New(1)), With(Ref(r), HandBuiltModel.Block(
            Declare(lease, Converted(Ref(r), conversion)),
            With(Converted(Ref(lease), conversion), HandBuiltModel.Block(Run(f.Call(inspect, Converted(Ref(lease), conversion))))))));

        Assert.Empty(f.Model.Analyze(new OwnershipAnalyzer(), body));
        Assert.Empty(f.Model.Analyze(new TierAnalyzer(), body));
    }
}