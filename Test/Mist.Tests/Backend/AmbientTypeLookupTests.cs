using Mist.Backends.CSharpTranspiler.AmbientProvider;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class AmbientTypeLookupTests
{
    [Theory]
    [InlineData("System.Tuple", true, true)]
    [InlineData("System.Tuple`5", false, true)]
    [InlineData("System.Tuple", false, false)]
    [InlineData("Other.Tuple", true, false)]
    [InlineData("System.Tupl", true, false)]
    [InlineData("System.Missing", true, false)]
    public void LookupUsesOnlyTheExactNameUnlessArityIsOmitted(string name, bool ignoreArity, bool found)
    {
        var type = typeof(Tuple<,,,,>);
        var source = new LookupSource(type);

        Assert.Equal(found ? type : null, AmbientTypeLookup.Find(name, ignoreArity, source.Find, source.Names));
    }

    [Fact]
    public void AnExactNameWinsOverAGenericNameReturnedFirstByTheSource()
    {
        var source = new LookupSource(typeof(Tuple<,,,,>), typeof(Tuple));

        Assert.Same(typeof(Tuple), AmbientTypeLookup.Find("System.Tuple", true, source.Find, source.Names));
    }

    private sealed class LookupSource(params Type[] types)
    {
        public IEnumerable<string> Names(string namespaceName)
        {
            return types.Where(type => type.Namespace == namespaceName).Select(type => type.Name);
        }

        public Type? Find(string name)
        {
            return types.FirstOrDefault(type => type.FullName == name);
        }
    }
}