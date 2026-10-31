using System.Collections.Immutable;
using Mist.Backends.CSharpTranspiler.Razor;

namespace Mist.Tests.Backend.Razor;

[Trait("Tier", "1")]
public sealed class CatalogBaseLookupTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void AnEnclosingBaseMapsArgumentsToItsInheritedNestedType(bool clr, bool transitive)
    {
        var box = Declaration("App.Box", [], [["T"]]);
        var nested = Declaration("App.Box.Base", [], [["T"], []]);
        var baseText = clr ? "Vendor.MatrixBox<M>" : "Box<M>";
        var middle = Declaration("App.Middle", [baseText], [["M"]]);
        var enclosing = Declaration("App.Sub", [transitive ? "Middle<S>" : baseText.Replace("<M>", "<S>", StringComparison.Ordinal)], [["S"]]);
        var declarations = new[] { box, nested, middle, enclosing }.ToDictionary(declaration => declaration.FullName);
        using var ambient = new ComponentTypeSource();
        var lookup = new CatalogBaseLookup(declarations, name => CatalogClrTypeLookup.Find(name, ambient.FindClrType));

        var resolved = lookup.Resolve(clr ? "Inner" : "Base", ["App.Sub", "App"]);

        Assert.NotNull(resolved);
        Assert.Equal(clr ? "Vendor.MatrixBox.Inner" : "App.Box.Base", resolved.FullName);
        Assert.Equal<string>(["S"], resolved.Arguments);
        Assert.Equal(clr, resolved.Clr is not null);
    }

    [Fact]
    public void AClrBaseChainMapsArgumentsBeforeLookingForItsNestedType()
    {
        var enclosing = Declaration("App.Sub", ["ClrDerived<S>"], [["S"]]);
        Type[] types = [typeof(ClrDerived<>), typeof(ClrBox<>)];
        var lookup = new CatalogBaseLookup(new Dictionary<string, MistClass> { [enclosing.FullName] = enclosing },
            name => CatalogClrTypeLookup.Find(name, (candidate, _) => types.FirstOrDefault(type =>
                ClrTypeText.Name(type) == candidate || type.Name.Split('`')[0] == candidate)));

        var resolved = lookup.Resolve("Inner", ["App.Sub", "App"]);

        Assert.NotNull(resolved);
        Assert.Equal(typeof(ClrBox<>.Inner), resolved.Clr);
        Assert.Equal<string>(["S"], resolved.Arguments);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AMissingNestedTypeEndsAtAnEmptyOrCyclicBaseChain(bool cyclic)
    {
        var enclosing = Declaration("App.Sub", cyclic ? ["Sub<S>"] : [], [["S"]]);
        var lookup = new CatalogBaseLookup(new Dictionary<string, MistClass> { [enclosing.FullName] = enclosing }, _ => null);

        Assert.Null(lookup.Resolve("Missing", ["App.Sub", "App"]));
    }

    internal sealed class ClrDerived<T> : ClrBox<T>;

    internal class ClrBox<T>
    {
        public sealed class Inner;
    }

    private static MistClass Declaration(string name, ImmutableArray<string> bases, ImmutableArray<ImmutableArray<string>> typeParameters)
    {
        return new MistClass(name, bases, ["App"], [], typeParameters, IsComponentClass: false);
    }
}