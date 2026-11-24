using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.Tests.Helpers;
using static Mist.Tests.Helpers.LendFragments;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class LentEscapeDecisionTests
{
    private readonly LendFragments f = new();

    [Theory]
    [InlineData("unrestricted", true)]
    [InlineData("borrowed", false)]
    [InlineData("frozen", false)]
    [InlineData("readonly ref", false)]
    [InlineData("owned", false)]
    [InlineData("ref", false)]
    [InlineData("out", false)]
    public void OnlyAnUnrestrictedParameterKeepsWhatItIsPassed(string tier, bool keeps)
    {
        Assert.Equal(keeps, OwnershipFlow.Keeps(Tier(tier)));
    }

    [Theory]
    [InlineData("static field", "in 'Hook.S'")]
    [InlineData("field of this", "in 'this.In'")]
    [InlineData("implicit field of this", "in 'In'")]
    [InlineData("field of a parameter", "in 'h.In'")]
    [InlineData("array element", "in an element of 'a'")]
    [InlineData("out parameter", "in parameter 'y', which its caller holds")]
    [InlineData("ref parameter", "in parameter 'y', which its caller holds")]
    [InlineData("ref local naming a field", "in 'r', which refers to 'Hook.S'")]
    [InlineData("ref local naming a local", null)]
    [InlineData("local", null)]
    [InlineData("owned field", null)]
    public void AStoreLetsALentValueOutOnlyWhereTheStorageOutlivesTheScope(string target, string? destination)
    {
        var alias = new LocalVariable("r", f.Box) { IsRef = true };
        var named = new LocalVariable("n", f.Box);
        Expression written = target switch
        {
            "static field" => Static(f.S),
            "field of this" => Access(new ThisExpression(HandBuiltModel.Node(), f.Holder), f.In),
            "implicit field of this" => new FieldReferenceExpression(HandBuiltModel.Node(), f.In),
            "field of a parameter" => Access(Ref(Parameter("h", f.Holder, default)), f.In),
            "array element" => f.Element(Ref(new LocalVariable("a", f.BoxArray))),
            "out parameter" => Ref(Parameter("y", f.Box, Tier("out"))),
            "ref parameter" => Ref(Parameter("y", f.Box, Tier("ref"))),
            "ref local naming a field" or "ref local naming a local" => Ref(alias),
            "local" => Ref(named),
            _ => Access(Ref(new LocalVariable("p", f.Holder)), f.Kept),
        };

        Expression? referent(LocalVariable local)
        {
            return ReferenceEquals(local, alias) ? target == "ref local naming a field" ? Static(f.S) : Ref(named) : null;
        }

        Assert.Equal(destination, OwnershipFlow.StoredIn(written, referent));
    }
}