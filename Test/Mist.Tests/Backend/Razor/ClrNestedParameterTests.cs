using Mist.Backends.CSharpTranspiler.Razor;

namespace Mist.Tests.Backend.Razor;

[Trait("Tier", "2")]
public sealed class ClrNestedParameterTests
{
    [Theory]
    [InlineData(false, "T")]
    [InlineData(true, "int")]
    public void ANestedParameterTypeKeepsItsDeclaringArguments(bool constructed, string argument)
    {
        using var ambient = new ComponentTypeSource();
        var clr = new ClrComponents(ambient);
        var facts = constructed
            ? clr.FindBase("Vendor.NestedTextBase", ["int"])
            : ClrComponents.Read(ambient.FindClrType("Vendor.NestedTextBase`1")!);

        Assert.NotNull(facts);
        Assert.Equal(new ParameterFacts("Keys", "System.Collections.Generic.Dictionary<" + argument + ", string>.KeyCollection", ParameterShape.Plain),
            Assert.Single(facts.Parameters));
    }
}