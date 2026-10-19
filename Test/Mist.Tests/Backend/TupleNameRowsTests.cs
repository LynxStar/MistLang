using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using Mist.Backends.CSharpTranspiler.AmbientProvider;
using Mist.Tests.InteropFixtures.HeritageTupleNames;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class TupleNameRowsTests
{
    private const string Fixtures = "Mist.Tests.InteropFixtures.HeritageTupleNames.";

    public static IEnumerable<object[]> Keys()
    {
        yield return [typeof(IPairs<(int, string)>), Fixtures + "IPairs`1<System.ValueTuple`2<System.Int32,System.String>>"];
        yield return [typeof(GenericNamedInterface<,>).GetGenericArguments()[1], "!1"];
        yield return [typeof(NamedMethodConstraint).GetMethod(nameof(NamedMethodConstraint.Take))!.GetGenericArguments()[0], "!!0"];
        yield return [typeof(Container<>.Items), Fixtures + "Container`1+Items"];
        yield return [typeof(Container<int>.Items), Fixtures + "Container`1+Items<System.Int32>"];
        yield return [typeof(int[]), "System.Int32[]"];
        yield return [typeof(int[,]), "System.Int32[,]"];
    }

    [Theory]
    [MemberData(nameof(Keys))]
    public void ReflectedTypesAreKeyedAsTheirSignaturesSpellThem(Type type, string expected)
    {
        Assert.Equal(expected, TupleNameRows.Key(type));
    }

    [Fact]
    public void OnlyASignatureExactlyOneRowCarriesYieldsNames()
    {
        (string, string?[]?)[] rows = [("A", ["X", "Y"]), ("B", ["Low", "High"]), ("C", null), ("D", ["One"]), ("D", ["Two"])];

        Expect(["Low", "High"], TupleNameRows.Matching("B", rows));
        Assert.Null(TupleNameRows.Matching("C", rows));
        Assert.Null(TupleNameRows.Matching("D", rows));
        Assert.Null(TupleNameRows.Matching("E", rows));
    }

    [Fact]
    public void AttributeValuesDecodeEveryNameAndEveryAbsentOne()
    {
        Expect(["Key", "Value"], Decode(Value(1, ["Key", "Value"])));
        Expect([null, "Second"], Decode(Value(1, [null, "Second"])));
        Expect([], Decode(Value(1, [])));
    }

    [Fact]
    public void MalformedAttributeValuesYieldNoNames()
    {
        Assert.Null(Decode(Value(1, null)));
        Assert.Null(Decode(Value(2, ["Key"])));
        Assert.Null(Decode(value => value.WriteUInt16(1)));
        Assert.Null(Decode(value =>
        {
            value.WriteUInt16(1);
            value.WriteInt32(3);
            value.WriteSerializedString("Only");
        }));
    }

    private static void Expect(string?[] expected, string?[]? actual)
    {
        Assert.Equal<IEnumerable<string?>>(expected, Assert.IsType<string?[]>(actual));
    }

    private static Action<BlobBuilder> Value(ushort prolog, string?[]? names)
    {
        return value =>
        {
            value.WriteUInt16(prolog);
            value.WriteInt32(names?.Length ?? -1);
            foreach(var name in names ?? [])
            {
                value.WriteSerializedString(name);
            }

            value.WriteUInt16(0);
        };
    }

    private static string?[]? Decode(Action<BlobBuilder> write)
    {
        var value = new BlobBuilder();
        write(value);
        var metadata = new MetadataBuilder();
        _ = metadata.AddModule(0, metadata.GetOrAddString("Probe"), metadata.GetOrAddGuid(Guid.Empty), default, default);
        var handle = metadata.GetOrAddBlob(value);
        var image = new BlobBuilder();
        new MetadataRootBuilder(metadata).Serialize(image, 0, 0);

        using var provider = MetadataReaderProvider.FromMetadataImage(image.ToImmutableArray());
        return TupleNameRows.Names(provider.GetMetadataReader().GetBlobReader(handle));
    }
}