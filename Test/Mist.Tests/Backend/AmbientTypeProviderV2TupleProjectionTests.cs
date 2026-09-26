using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.InteropFixtures;

namespace Mist.Tests.Backend;

public sealed class AmbientTypeProviderV2TupleProjectionTests(AmbientTypeProviderV2InteropFixture fixture)
    : IClassFixture<AmbientTypeProviderV2InteropFixture>
{
    private const string Int = "Standard.Int32";
    private const string Text = "Standard.String";

    [Fact]
    public void UnnamedSignatures_ProjectOneInternedStructuralTuple()
    {
        var made = Assert.IsType<TupleType>(Result(nameof(TupleSignatures.MakePair)));

        Assert.Same(made, Parameter(nameof(TupleSignatures.DescribePair)).Type);
        Assert.Equal([Int, Text], Names(made.ElementTypes));
    }

    [Fact]
    public void ElementNames_DecorateTheStructuralTupleWithoutChangingIt()
    {
        var divided = Assert.IsType<NamedTupleType>(Result(nameof(TupleSignatures.Divide)));
        var size = Assert.IsType<NamedTupleType>(Parameter(nameof(TupleSignatures.Area)).Type);
        var partly = Assert.IsType<NamedTupleType>(Result(nameof(TupleSignatures.PartlyNamed)));

        Assert.Equal(["Quotient", "Remainder"], divided.ElementNames);
        Assert.Equal(["Width", "Height"], size.ElementNames);
        Assert.Same(divided.Underlying, size.Underlying);
        Assert.Equal([null, "Label"], partly.ElementNames);
        Assert.Equal([Int, Text], Names(partly.ElementTypes));
    }

    [Fact]
    public void NestedTuples_TakeTheirOwnNames()
    {
        var located = Assert.IsType<NamedTupleType>(Result(nameof(TupleSignatures.Locate)));
        var origin = Assert.IsType<NamedTupleType>(located.ElementTypes[0]);

        Assert.Equal(["Origin", "Label"], located.ElementNames);
        Assert.Equal(["X", "Y"], origin.ElementNames);
        Assert.Equal([Int, Int], Names(origin.ElementTypes));
        Assert.Equal(Text, located.ElementTypes[1].FullyQualifiedName);
        Assert.Equal(located.Name, Parameter(nameof(TupleSignatures.DescribeLocation)).Type.Name);
    }

    [Fact]
    public void LongTuples_FlattenEveryRestIntoOneTuple()
    {
        var octet = Assert.IsType<NamedTupleType>(Result(nameof(TupleSignatures.Octet)));
        Assert.Equal(Enumerable.Repeat(Int, 8), Names(octet.ElementTypes));
        Assert.Equal(["One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight"], octet.ElementNames);

        var nonet = Assert.IsType<NamedTupleType>(Result(nameof(TupleSignatures.Nonet)));
        Assert.Equal([.. Enumerable.Repeat(Int, 8), Text], Names(nonet.ElementTypes));
        Assert.Equal(["A", "B", "C", "D", "E", "F", "G", "H", "Tail"], nonet.ElementNames);
        Assert.Same(nonet.Underlying, Parameter(nameof(TupleSignatures.DescribeNonet)).Type);

        var sixteen = Assert.IsType<NamedTupleType>(Result(nameof(TupleSignatures.Sixteen)));
        Assert.Equal(16, sixteen.ElementTypes.Count);
        Assert.Equal([.. Enumerable.Repeat<string?>(null, 14), "Pair", "Last"], sixteen.ElementNames);
        var pair = Assert.IsType<NamedTupleType>(sixteen.ElementTypes[14]);
        Assert.Equal(["Inner", "Label"], pair.ElementNames);
        Assert.Equal(Text, sixteen.ElementTypes[15].FullyQualifiedName);

        var described = Assert.IsType<TupleType>(Parameter(nameof(TupleSignatures.DescribeSixteen)).Type);
        Assert.Equal(16, described.ElementTypes.Count);
        Assert.Same(pair.Underlying, described.ElementTypes[14]);
    }

    [Fact]
    public void TuplesInsideOtherTypes_KeepTheirNamesInSignatureOrder()
    {
        var scores = Assert.IsType<ConstructedType>(Result(nameof(TupleSignatures.Scores)));
        Assert.Equal(["Name", "Score"], Assert.IsType<NamedTupleType>(scores.TypeArguments[0]).ElementNames);

        var grid = Assert.IsType<ConstructedType>(OptionalInner(Result(nameof(TupleSignatures.Grid))));
        Assert.Equal(["Row", "Column"], Assert.IsType<NamedTupleType>(grid.TypeArguments[0]).ElementNames);
        Assert.Equal(["Name", "Occupied"], Assert.IsType<NamedTupleType>(grid.TypeArguments[1]).ElementNames);

        var pairs = Assert.IsType<ConstructedType>(OptionalInner(Result(nameof(TupleSignatures.Pairs))));
        Assert.Equal([Int, Int], Names(Assert.IsType<TupleType>(pairs.TypeArguments[0]).ElementTypes));

        var span = Assert.IsType<NamedTupleType>(OptionalInner(Assert.IsType<UnionType>(Result(nameof(TupleSignatures.Span)))));
        Assert.Equal(["Low", "High"], span.ElementNames);

        var swapped = Parameter(nameof(TupleSignatures.Swap));
        Assert.True(swapped.Modifier.IsRef);
        Assert.Equal(["Left", "Right"], Assert.IsType<NamedTupleType>(swapped.Type).ElementNames);

        var author = Signatures.PropertiesList.Single(property => property.Name == nameof(TupleSignatures.Author));
        Assert.Equal(["First", "Last"], Assert.IsType<NamedTupleType>(author.Type).ElementNames);

        var bounds = Signatures.FieldsList.Single(field => field.Name == nameof(TupleSignatures.Bounds));
        Assert.Equal(["Min", "Max"], Assert.IsType<NamedTupleType>(bounds.Type).ElementNames);
    }

    [Fact]
    public void DelegateInsideATuple_SkipsTheNamesItsInvokeSignatureCannotUse()
    {
        var filter = Assert.IsType<NamedTupleType>(Result(nameof(TupleSignatures.Filter)));
        var test = Assert.IsType<FunctionType>(filter.ElementTypes[0]);

        Assert.Equal(["Test", "Window"], filter.ElementNames);
        _ = Assert.IsType<TupleType>(Assert.Single(test.ParameterTypes));
        Assert.Equal(["Low", "High"], Assert.IsType<NamedTupleType>(filter.ElementTypes[1]).ElementNames);
    }

    [Fact]
    public void OneElementTuple_StaysNominalBecauseMistTuplesHaveTwoOrMore()
    {
        var single = Assert.IsType<ConstructedType>(Result(nameof(TupleSignatures.Single)));

        Assert.Equal("System.ValueTuple<>", single.Definition.FullyQualifiedName);
        Assert.Equal(Int, single.TypeArguments[0].FullyQualifiedName);
    }

    private ClassType Signatures => fixture.Type(nameof(TupleSignatures));

    private IMistType Result(string method)
    {
        return Method(method).ReturnType;
    }

    private Parameter Parameter(string method)
    {
        return Method(method).ParametersList.Single();
    }

    private Method Method(string name)
    {
        return Signatures.MethodsList.Single(method => method.Name == name);
    }

    private static IMistType OptionalInner(IMistType type)
    {
        return type is UnionType union
            ? Assert.Single(union.Constituents, constituent => constituent.FullyQualifiedName != "Standard.Void")
            : type;
    }

    private static IEnumerable<string> Names(IEnumerable<IMistType> types)
    {
        return types.Select(type => type.FullyQualifiedName);
    }
}