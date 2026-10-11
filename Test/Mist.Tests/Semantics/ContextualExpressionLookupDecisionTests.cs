using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "1")]
public sealed class ContextualExpressionLookupDecisionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ACloserCandidateWinsOverAFartherSameKindAmbiguity(bool reversed)
    {
        var built = new HandBuiltModel();
        var owner = HandBuiltModel.Class("Widget");
        var closer = new TypeResult(HandBuiltModel.Class("Value"));
        var farther = new[]
        {
            new PropertyResult(new Property("Value", built.Type("Int32"), DeclarationFragments.Public), owner, []),
            new PropertyResult(new Property("Value", built.Type("Int32"), DeclarationFragments.Public), owner, []),
        };
        var candidates = new CandidateSet();
        candidates.Add(closer, depth: 0);
        foreach(var candidate in reversed ? farther.Reverse() : farther)
        {
            candidates.Add(candidate, depth: 1);
        }

        Assert.Same(closer, candidates.Resolve());
    }

    [Fact]
    public void EquallyClosePropertiesRemainAnAmbiguity()
    {
        var built = new HandBuiltModel();
        var first = new PropertyResult(new Property("Value", built.Type("Int32"), DeclarationFragments.Public), HandBuiltModel.Class("First"), []);
        var second = new PropertyResult(new Property("Value", built.Type("Int32"), DeclarationFragments.Public), HandBuiltModel.Class("Second"), []);
        var candidates = new CandidateSet();
        candidates.Add(first, depth: 1);
        candidates.Add(second, depth: 1);

        Assert.Equal(new LookupResult[] { first, second }, Assert.IsType<AmbiguousResult>(candidates.Resolve()).Candidates);
    }

    [Theory]
    [InlineData("ordinary", true)]
    [InlineData("error", false)]
    [InlineData("unknown", false)]
    [InlineData("unprojected", false)]
    [InlineData("array", false)]
    [InlineData("indexer", false)]
    public void OnlyASoundReceiverWithNoElementContractIsRefused(string shape, bool refused)
    {
        var built = new HandBuiltModel();
        var integer = built.Type("Int32");
        var receiver = shape switch
        {
            "error" => (IMistType)ErrorType.Instance,
            "unknown" => UnknownType.Instance,
            "unprojected" => NotProjectedType.Instance,
            "array" => built.Interner.GetOrCreateConstructedType(built.Type("Array<,>"), [integer, new UnknownValueType(integer)]),
            _ => integer,
        };
        var indexers = shape == "indexer"
            ? new[] { new Indexer(integer, [new Parameter("position", integer)], DeclarationFragments.Public) }
            : Array.Empty<Indexer>();

        Assert.Equal(refused, Indexability.RefusesElement(receiver, indexers));
    }
}