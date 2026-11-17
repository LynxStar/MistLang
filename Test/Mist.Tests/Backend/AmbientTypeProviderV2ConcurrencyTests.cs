using Mist.Tests.InteropFixtures;
using Mist.Tests.InteropFixtures.ConstraintProjection;

namespace Mist.Tests.Backend;

// ambient-type-provider-v2 Property 5, concurrent variant.
[Trait("Tier", "2")]
public sealed class AmbientTypeProviderV2ConcurrencyTests(AmbientTypeProviderV2InteropFixture fixture)
    : IClassFixture<AmbientTypeProviderV2InteropFixture>
{
    private const string Fixtures = "Mist.Tests.InteropFixtures";

    [Fact]
    public async Task ConcurrentLookupsOfOneType_AllReceiveTheRegisteredInstance()
    {
        const int parallelism = 10;
        var name = $"{Fixtures}.{nameof(SimpleClass)}";

        var results = await Task.WhenAll(
            Enumerable.Range(0, parallelism).Select(_ => Task.Run(() => fixture.Projected(name))));

        var registered = fixture.Model.TypesByFQN[name];
        Assert.All(results, result => Assert.Same(registered, result));
    }

    [Fact]
    public async Task ConcurrentLookupsOfDifferentTypes_EachReceivesItsOwnRegisteredInstance()
    {
        var requests = new (string ClrName, string MistName)[]
        {
            ($"{Fixtures}.{nameof(SimpleClass)}", $"{Fixtures}.{nameof(SimpleClass)}"),
            ($"{Fixtures}.{nameof(SimpleStruct)}", $"{Fixtures}.{nameof(SimpleStruct)}"),
            ($"{Fixtures}.{nameof(ISimpleInterface)}", $"{Fixtures}.{nameof(ISimpleInterface)}"),
            ($"{Fixtures}.{nameof(SimpleEnum)}", $"{Fixtures}.{nameof(SimpleEnum)}"),
            ($"{Fixtures}.Bag`1", $"{Fixtures}.Bag<>"),
            ($"{Fixtures}.ConstraintProjection.{nameof(Methods)}", $"{Fixtures}.ConstraintProjection.{nameof(Methods)}"),
        };

        var results = await Task.WhenAll(
            requests.Select(request => Task.Run(() => fixture.Projected(request.ClrName))));

        for(var i = 0; i < requests.Length; i++)
        {
            Assert.Same(fixture.Model.TypesByFQN[requests[i].MistName], results[i]);
        }
    }
}