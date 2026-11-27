using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "1")]
public sealed class WrappedTypeParameterOccurrenceTests
{
    [Fact]
    public void AConstructedNestedTypeMentionsItsEnclosingArgumentByIdentity()
    {
        var outer = UnknownViewDecisionTests.Generic("Outer<>", null, "Length");
        var inner = UnknownViewDecisionTests.Generic("Inner<>", outer, "T");
        var caller = UnknownViewDecisionTests.Generic("Caller<,>", null, "Length", "T");
        var domain = new HandBuiltModel().Type("Int32");
        var outerFormal = outer.TypeParametersList[0];
        outerFormal.IsDependent = true;
        outerFormal.Domain = domain;
        var length = caller.TypeParametersList[0];
        length.IsDependent = true;
        length.Domain = domain;
        var element = caller.TypeParametersList[1];
        var enclosing = new ConstructedType(outer, [length]);
        var nested = new ConstructedType(inner, [element], enclosing);

        var occurrences = TypeParameterOccurrences.In(nested);

        Assert.Equal(2, occurrences.Count);
        Assert.Contains(occurrences, parameter => ReferenceEquals(parameter, length));
        Assert.Contains(occurrences, parameter => ReferenceEquals(parameter, element));
        Assert.True(TypeParameterOccurrences.Mentions(nested, length));
        Assert.False(TypeParameterOccurrences.Mentions(nested, outerFormal));
    }

    [Fact]
    public void AnIndexedPackElementMentionsThePackItComesFrom()
    {
        var owner = UnknownViewDecisionTests.Generic("Owner<>", null, "TArgs");
        var pack = owner.TypeParametersList[0];
        pack.IsDependent = true;
        pack.PackKind = TypePackKind.Unbracketed;
        pack.PackLength = 2;
        var box = UnknownViewDecisionTests.Generic("Box<>", null, "T");
        var indexed = new ConstructedType(box, [pack.ElementAt(1)]);

        Assert.Same(pack, Assert.Single(TypeParameterOccurrences.In(indexed)));
    }

    [Fact]
    public void ANamedTupleContainingAnOpenSpreadMentionsTheSpreadPack()
    {
        var owner = UnknownViewDecisionTests.Generic("Owner<>", null, "TArgs");
        var pack = owner.TypeParametersList[0];
        pack.IsDependent = true;
        pack.PackKind = TypePackKind.Unbracketed;
        var spread = new TupleType([new SpreadType(pack)]);
        var tuple = new NamedTupleType(new TupleType([spread, new HandBuiltModel().Type("Int32")]), ["items", "count"]);

        Assert.Same(pack, Assert.Single(TypeParameterOccurrences.In(tuple)));
    }
}