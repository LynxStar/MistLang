using Mist.ProgramModel.Model.Enums;
using Mist.Tests.Helpers;
using static Mist.Tests.Helpers.LifetimeFragments;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class ConvertedWithCleanupTests
{
    private readonly LifetimeFragments f = new();

    [Theory]
    [InlineData("as", false)]
    [InlineData("cast", false)]
    [InlineData("implicit", false)]
    [InlineData("as", true)]
    [InlineData("cast", true)]
    [InlineData("implicit", true)]
    public void AConvertedInstallationTransfersCleanupOfOnlyTheInstalledInstance(string conversion, bool reassign)
    {
        var r = f.Local("r");
        var contents = reassign ? HandBuiltModel.Block(Run(Assign(r, f.New(2)))) : HandBuiltModel.Block();
        var emitted = Emitted(HandBuiltModel.Block(Declare(r, f.New(1)), With(Converted(Ref(r), conversion), contents)));

        Assert.Equal(reassign ? 2 : 1, emitted.Split(".Drop()", StringSplitOptions.None).Length - 1);
        if(reassign)
        {
            Assert.Contains("MistOwnership.Taken(r =", emitted, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("as")]
    [InlineData("cast")]
    [InlineData("implicit")]
    public void AConvertedExplicitLoanAcquiresNoNestedCleanup(string conversion)
    {
        var r = f.Local("r");
        var lease = f.Local("lease", ownership: OwnershipKind.Frozen);
        var body = HandBuiltModel.Block(Declare(r, f.New(1)), With(Ref(r), HandBuiltModel.Block(
            Declare(lease, Ref(r)), With(Converted(Ref(lease), conversion), HandBuiltModel.Block()))));

        Assert.Equal(1, Emitted(body).Split(".Drop()", StringSplitOptions.None).Length - 1);
    }
}