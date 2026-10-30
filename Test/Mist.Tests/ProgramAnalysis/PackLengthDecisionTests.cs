using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

// A pack declared Type[N] is closed with exactly N types. The decision is one function over a hand-built
// parameter and argument (Tier 1); that a registered closing is reported at its own node is the
// analyzer's walk (Tier 2).
public sealed class PackLengthDecisionTests
{
    [Theory]
    [Trait("Tier", "1")]
    [InlineData(2, "A, B", true)]
    [InlineData(0, "", true)]
    [InlineData(2, "A", false)]
    [InlineData(2, "A, B, C", false)]
    [InlineData(2, "", false)]
    [InlineData(2, "Two...", true)]
    [InlineData(3, "A, Two...", true)]
    [InlineData(3, "Two...", false)]
    [InlineData(2, "Open...", false)]
    [InlineData(null, "A, B, C", true)]
    [InlineData(null, "Open...", true)]
    public void AFixedSizePack_IsFilledByExactlyItsDeclaredLength(int? declared, string elements, bool fills)
    {
        var parameter = Pack("TArgs", declared);

        Assert.Equal(fills, ConstraintAnalyzer.FillsDeclaredLength(parameter, Closing(elements)));
    }

    [Fact]
    [Trait("Tier", "1")]
    public void AnArgumentThatIsNotAPack_IsNotJudgedForLength()
    {
        Assert.True(ConstraintAnalyzer.FillsDeclaredLength(Pack("TArgs", 2), Pack("Forwarded", null)));
    }

    [Fact]
    [Trait("Tier", "2")]
    public void AClosingOfTheWrongLength_IsReportedAtItsOwnNode()
    {
        var fragment = new DeclarationFragments();
        var owner = fragment.Class("Pair");
        var pack = DeclarationFragments.Parameter(owner, owner.Scope, "TArgs");
        pack.PackKind = TypePackKind.Unbracketed;
        pack.PackLength = 2;

        fragment.Built.Model.ConstructorConstraintClosures.RecordType(owner, [Closing("A, B, C")], HandBuiltModel.Node(55));

        var finding = Assert.Single(fragment.Run(new ConstraintAnalyzer()));

        Assert.Equal("MIST0178", finding.Number.Value);
        Assert.Contains("'Type[2]' constraint on type parameter 'TArgs'", finding.Message);
        Assert.Equal(56, finding.Position().Start.Column);
    }

    private static TypeParameter Pack(string name, int? length)
    {
        return new TypeParameter(name, 0, new Scope(null), null) { PackKind = TypePackKind.Unbracketed, PackLength = length };
    }

    private static LiftedType Closing(string elements)
    {
        var written = elements.Split(", ", StringSplitOptions.RemoveEmptyEntries).Select<string, IMistType>(element => element switch
        {
            "Two..." => new SpreadType(Pack("Two", 2)),
            "Open..." => new SpreadType(Pack("Open", null)),
            _ => HandBuiltModel.Class(element),
        });

        return new LiftedType(HandBuiltModel.Class("Type"), new TypePackValue([.. written]));
    }
}