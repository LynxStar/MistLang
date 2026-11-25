using System.Reflection;
using Mist.Backends.CSharpTranspiler.AmbientProvider;
using Mist.Tests.InteropFixtures.HeritageTupleNames;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class TupleNameRowsShapeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RowsThatDifferOnlyInArrayRankEachKeepTheirOwnNames(bool metadataOnly)
    {
        using var metadata = Context();
        var implementer = Fixture(metadata, typeof(RankedInterfaces), metadataOnly);
        var rows = new TupleNameRows();

        var named = implementer.GetInterfaces().ToDictionary(
            iface => iface.GetGenericArguments()[0].GetArrayRank(),
            iface => rows.OfInterface(implementer, iface));

        Named("Flat", named[1]);
        Named("Grid", named[2]);
        Named("Cube", named[3]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NestedElementTypesAreKeyedThroughTheirDeclaringTypes(bool metadataOnly)
    {
        using var metadata = Context();
        var implementer = Fixture(metadata, typeof(NestedElementInterfaces), metadataOnly);
        var rows = new TupleNameRows();

        var named = implementer.GetInterfaces().ToDictionary(
            iface => iface.GetGenericArguments()[0].GetGenericArguments()[0].Name,
            iface => rows.OfInterface(implementer, iface));

        Named("Here", named[nameof(NestedElementInterfaces.Local)]);
        Named("There", named[nameof(Environment.SpecialFolder)]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PlainInterfaceRowsBesideATupleRowLeaveItsNames(bool metadataOnly)
    {
        using var metadata = Context();
        var implementer = Fixture(metadata, typeof(PlainSiblingInterfaces), metadataOnly);

        Assert.Equal(3, implementer.GetInterfaces().Length);
        Named("Kept", new TupleNameRows().OfInterface(implementer, implementer.GetInterfaces().Single(iface => iface.IsGenericType)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MethodParameterRowsAreToldApartFromTypeParameterRows(bool metadataOnly)
    {
        using var metadata = Context();
        var pick = Fixture(metadata, typeof(MethodParameterKeys<>), metadataOnly)
            .GetMethod(nameof(MethodParameterKeys<int>.Pick))!;
        var second = pick.GetGenericArguments()[1];
        var rows = new TupleNameRows();

        var named = second.GetGenericParameterConstraints().ToDictionary(
            constraint => constraint.GetGenericArguments()[0].GetGenericArguments()[0].Name,
            constraint => rows.OfConstraint(second, constraint));

        Named("Outer", named["TOuter"]);
        Named("Inner", named["TFirst"]);
    }

    private static MetadataLoadContext Context()
    {
        return AmbientProviderTestHelper.RuntimeResolvingContext(typeof(IPairs<>).Assembly.Location);
    }

    private static Type Fixture(MetadataLoadContext metadata, Type fixture, bool metadataOnly)
    {
        return metadataOnly
            ? metadata.LoadFromAssemblyPath(fixture.Assembly.Location).GetType(fixture.FullName!)!
            : fixture;
    }

    private static void Named(string first, TupleElementNames? names)
    {
        Assert.Equal<IEnumerable<string?>>([first, null], Assert.IsType<TupleElementNames>(names).Take(2));
    }
}