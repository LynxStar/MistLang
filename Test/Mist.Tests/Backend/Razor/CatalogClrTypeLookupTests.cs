using Mist.Backends.CSharpTranspiler.Razor;

namespace Mist.Tests.Backend.Razor;

[Trait("Tier", "1")]
public sealed class CatalogClrTypeLookupTests
{
    public static TheoryData<string, int?, Type?> Rows => new()
    {
        { "System.Environment", null, typeof(Environment) },
        { "System.Environment.SpecialFolder", null, typeof(Environment.SpecialFolder) },
        { "System.Environment.SpecialFolder", 0, typeof(Environment.SpecialFolder) },
        { "System.Environment.SpecialFolder", 1, null },
        { "System.Environment.Missing", null, null },
        { "System.Environment.Missing.SpecialFolder", null, null },
        { "Vendor.Environment.SpecialFolder", null, null },
        { "SpecialFolder", null, null },
        { "System.Collections.Generic.Dictionary", 2, typeof(Dictionary<,>) },
        { "System.Collections.Generic.Dictionary", 3, null },
        { "System.Collections.Generic.Dictionary.KeyCollection", null, typeof(Dictionary<,>.KeyCollection) },
        { "System.Collections.Generic.Dictionary.KeyCollection", 2, typeof(Dictionary<,>.KeyCollection) },
        { "System.Collections.Generic.Dictionary.KeyCollection", 1, null },
        { "System.Collections.Generic.List.Enumerator", null, typeof(List<>.Enumerator) },
        { "System.Collections.Generic.List.Enumerator", 1, typeof(List<>.Enumerator) },
        { "System.Collections.Generic.List.Enumerator", 0, null },
        { "System.Collections.Generic.Dictionary.ValueCollection", 2, typeof(Dictionary<,>.ValueCollection) },
    };

    [Theory]
    [MemberData(nameof(Rows))]
    public void DottedNamesTraverseContainingTypesWithoutChangingTheNamespaceOrArity(string name, int? arity, Type? expected)
    {
        Assert.Equal(expected, CatalogClrTypeLookup.Find(name, Find, arity));
    }

    [Theory]
    [InlineData(null, "Vendor.Outer.Inner", true)]
    [InlineData(2, "Vendor.Outer.Inner`2", false)]
    public void AnAmbientMatchWinsWithoutTryingContainingTypes(int? arity, string metadataName, bool ignoreArity)
    {
        var calls = new List<(string Name, bool IgnoreArity)>();
        var type = CatalogClrTypeLookup.Find("Vendor.Outer.Inner", (name, ignore) =>
        {
            calls.Add((name, ignore));
            return typeof(string);
        }, arity);

        Assert.Same(typeof(string), type);
        Assert.Equal([(metadataName, ignoreArity)], calls);
    }

    private static Type? Find(string fullName, bool ignoreArity)
    {
        Type[] types = [typeof(Environment), typeof(Dictionary<,>), typeof(List<>)];
        return types.FirstOrDefault(type => type.FullName == fullName
            || (ignoreArity && type.FullName!.Split('`')[0] == fullName));
    }
}