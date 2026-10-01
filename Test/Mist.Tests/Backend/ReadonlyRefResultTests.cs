using System.Reflection;
using Mist.Backends.CSharpTranspiler.AmbientProvider;
using Mist.Tests.InteropFixtures;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class ReadonlyRefResultTests
{
    [Theory]
    [InlineData(typeof(RefReadonlyReturnFixture), nameof(RefReadonlyReturnFixture.GetReadonlyIntRef), true)]
    [InlineData(typeof(RefReadonlyReturnFixture), nameof(RefReadonlyReturnFixture.GetReadonlyStringRef), true)]
    [InlineData(typeof(MutableRefReturnFixture), nameof(MutableRefReturnFixture.GetIntRef), false)]
    [InlineData(typeof(RefReadonlyReturnFixture), nameof(RefReadonlyReturnFixture.GetIntValue), false)]
    public void OnlyARefReadonlyResultIsReadonly(Type declaring, string method, bool expected)
    {
        var result = declaring.GetMethod(method, BindingFlags.Public | BindingFlags.Instance)!.ReturnParameter;

        Assert.Equal(expected, ParameterProjector.IsReadonlyRef(result));
    }

    [Fact]
    public void NoResultIsNotReadonly()
    {
        Assert.False(ParameterProjector.IsReadonlyRef(null));
    }
}