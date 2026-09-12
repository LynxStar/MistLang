// Tier 4 (Integration): the boundary between what Standard promises with `intrinsic` and what the C#
// backend supplies. Every intrinsic declaration in the real Standard sources is classified against real
// CLR metadata, so a promise the backend cannot keep fails here rather than as a Roslyn error in a
// user's build. Metadata is read from the runtime the tests run on; no Roslyn compile, no process.

using System.Collections.Immutable;
using System.Reflection;
using Mist.Backends.CSharpTranspiler.AmbientProvider;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

public sealed class IntrinsicSupplyTests : IDisposable
{
    private readonly MetadataLoadContext metadataContext;
    private readonly ImmutableArray<LoadedAssemblyInfo> loaded;

    public IntrinsicSupplyTests()
    {
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var assemblyPaths = Directory.GetFiles(runtimeDir, "*.dll").ToImmutableArray();

        metadataContext = new MetadataLoadContext(new PathAssemblyResolver(assemblyPaths));
        loaded = AmbientTypeProviderV2Factory.LoadAssemblies(assemblyPaths, metadataContext, new DiagnosticBag());
    }

    public void Dispose()
    {
        metadataContext.Dispose();
    }

    private Type? FindClrType(string fullName)
    {
        return loaded.Select(assembly => assembly.MetadataAssembly.GetType(fullName)).FirstOrDefault(type => type is not null);
    }

    [Fact]
    public void EveryIntrinsicStandardDeclaresIsSupplied()
    {
        var audit = IntrinsicSupply.Audit(StandardModelProbe.Build("", out _), FindClrType);
        var missing = audit.Where(entry => entry.Kind == IntrinsicSupply.Kind.Missing).Select(entry => entry.Description).ToList();

        Assert.NotEmpty(audit);
        Assert.True(missing.Count == 0, "Intrinsic members the backend does not supply:\n" + string.Join("\n", missing));
    }

    [Theory]
    [InlineData("Standard.Int32.operator +(int, int)", "Structural")]
    [InlineData("Standard.Int32.operator double(int)", "Structural")]
    [InlineData("Standard.Array<TValue, Length>.this[]", "Structural")]
    [InlineData("Standard.Math.Abs(int)", "Metadata")]
    [InlineData("Standard.Console.WriteLine(unknown)", "Metadata")]
    [InlineData("Standard.String.Substring(int)", "Metadata")]
    [InlineData("Standard.Type.Name", "Metadata")]
    [InlineData("Standard.Task<TResult>.Delay(int)", "Metadata")]
    [InlineData("Standard.Span<T, Length>.new(T[])", "Metadata")]
    [InlineData("Standard.Span<T, Length>.GetEnumerator()", "Metadata")]
    [InlineData("Standard.Span<T, Length>.Enumerator.MoveNext()", "Metadata")]
    [InlineData("Standard.Array<TValue, Length>.Enumerator.MoveNext()", "Respelled")]
    [InlineData("Standard.Array<TValue, Length>.Enumerator.Current", "Respelled")]
    [InlineData("Standard.Array<TValue, Length>.GetEnumerator()", "Respelled")]
    [InlineData("Standard.Type.Of(unknown)", "Respelled")]
    [InlineData("Standard.String.Clone()", "Respelled")]
    [InlineData("Standard.Task<TResult>.Drop()", "Respelled")]
    [InlineData("Standard.ValueTask<TResult>.Drop()", "Respelled")]
    [InlineData("Standard.Task<TResult>.WhenAll(Task<TResult>[])", "Respelled")]
    [InlineData("Standard.Regex.IsFullMatch(string)", "Respelled")]
    [InlineData("Standard.Regex.Pattern", "Respelled")]
    [InlineData("Standard.Array<TValue, Length>.new(func)", "Respelled")]
    public void ReadsEachKindOfSupply(string member, string kind)
    {
        var audit = IntrinsicSupply.Audit(StandardModelProbe.Build("", out _), FindClrType);
        var entry = Assert.Single(audit, candidate => candidate.Description == member);

        Assert.Equal(Enum.Parse<IntrinsicSupply.Kind>(kind), entry.Kind);
    }
}