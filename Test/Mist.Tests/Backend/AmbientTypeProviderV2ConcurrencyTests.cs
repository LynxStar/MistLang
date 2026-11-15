using Mist.Backends.CSharpTranspiler.AmbientProvider;
using Mist.ProgramModel.Contracts.Types;
using Mist.Tests.InteropFixtures;
using Mist.Tests.InteropFixtures.ConstraintProjection;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class AmbientTypeProviderV2ConcurrencyTests(AmbientTypeProviderV2InteropFixture fixture)
    : IClassFixture<AmbientTypeProviderV2InteropFixture>
{
    private const string Fixtures = "Mist.Tests.InteropFixtures";

    private IMistType? LookupType(string namespaceName, string typeName)
    {
        if(WellKnownTypeMap.Entries.TryGetValue((namespaceName, typeName), out var mapping))
        {
            if(fixture.Model.TypesByFQN.TryGetValue($"{mapping.MistNamespace}.{mapping.MistTypeName}", out var stdType))
            {
                return stdType;
            }

            return null;
        }

        var mistName = typeName;
        var backtickIndex = typeName.IndexOf('`');
        if(backtickIndex >= 0)
        {
            var baseName = typeName[..backtickIndex];
            var arityStr = typeName[(backtickIndex + 1)..];
            if(int.TryParse(arityStr, out var arity) && arity >= 1)
            {
                var commas = new string(',', arity - 1);
                mistName = $"{baseName}<{commas}>";
            }
        }

        if(fixture.Model.TypesByFQN.TryGetValue($"{namespaceName}.{mistName}", out var type))
        {
            return type;
        }

        return null;
    }

    [Fact]
    public async Task ResolveType_ConcurrentAccess_AllReceiveSameReference()
    {
        const int parallelism = 10;
        var results = new IMistType?[parallelism];

        var tasks = Enumerable.Range(0, parallelism).Select(i => Task.Run(() =>
        {
            results[i] = LookupType(Fixtures, nameof(SimpleClass));
        }));

        await Task.WhenAll(tasks);

        Assert.All(results, Assert.NotNull);

        var first = results[0];
        for(var i = 1; i < parallelism; i++)
        {
            Assert.True(ReferenceEquals(first, results[i]),
                $"Result {i} should be reference-equal to result 0");
        }
    }

    [Fact]
    public async Task ResolveType_ConcurrentAccessDifferentTypes_EachGetsDistinctReference()
    {
        var typeRequests = new (string Namespace, string TypeName)[]
        {
            (Fixtures, nameof(SimpleClass)),
            (Fixtures, nameof(SimpleStruct)),
            (Fixtures, nameof(ISimpleInterface)),
            (Fixtures, nameof(SimpleEnum)),
            ($"{Fixtures}.ConstraintProjection", nameof(Methods)),
        };

        var results = new IMistType?[typeRequests.Length];

        var tasks = Enumerable.Range(0, typeRequests.Length).Select(i => Task.Run(() =>
        {
            var (ns, name) = typeRequests[i];
            results[i] = LookupType(ns, name);
        }));

        await Task.WhenAll(tasks);

        Assert.All(results, Assert.NotNull);

        for(var i = 0; i < results.Length; i++)
        {
            for(var j = i + 1; j < results.Length; j++)
            {
                Assert.False(ReferenceEquals(results[i], results[j]),
                    $"Result {i} ({typeRequests[i].TypeName}) should NOT be reference-equal " +
                    $"to result {j} ({typeRequests[j].TypeName})");
            }
        }
    }
}