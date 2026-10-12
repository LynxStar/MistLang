// Tier 4 (Integration): the boundary between what Standard promises with `intrinsic` and what the C#
// backend supplies. Every intrinsic declaration in the real Standard sources is classified against real
// CLR metadata, so a promise the backend cannot keep fails here rather than as a Roslyn error in a
// user's build. Metadata is read from the three runtime assemblies that back Standard's intrinsic types,
// loaded and never projected; no Roslyn compile, no process.

using System.Text.RegularExpressions;
using Mist.Backends.CSharpTranspiler.AmbientProvider;
using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "4")]
public sealed class IntrinsicSupplyTests : IDisposable
{
    private readonly AmbientTypeProviderV2 metadata;

    public IntrinsicSupplyTests()
    {
        metadata = AmbientProviderTestHelper.ProviderOver(
            new DiagnosticBag(),
            typeof(object).Assembly.Location,
            typeof(Console).Assembly.Location,
            typeof(Regex).Assembly.Location);
    }

    public void Dispose()
    {
        metadata.Dispose();
    }

    private Type? FindClrType(string fullName)
    {
        return metadata.FindClrType(fullName);
    }

    [Fact]
    public void AMistBodiedMemberOfAnIntrinsicTypeIsCarriedOnTheBackingType()
    {
        var model = StandardModelProbe.Build("", out _);
        var int32 = Assert.IsAssignableFrom<ConcreteType>(model.TypesByFQN["Standard.Int32"]);
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();

        Assert.True(IntrinsicMemberEmitter.Carries(int32));
        IntrinsicMemberEmitter.Emit([int32], writer, diagnostics);
        var carrier = writer.ToString();

        Assert.Empty(diagnostics.ToImmutableArray());
        StandardDeclarationEmissionTests.AssertCarries(carrier, "internal static class MistIntrinsics");
        StandardDeclarationEmissionTests.AssertCarries(carrier, "extension(int @this)");
        StandardDeclarationEmissionTests.AssertCarries(carrier, "public bool IsEven()");
        StandardDeclarationEmissionTests.AssertCarries(carrier, "return @this % 2 == 0;");
        Assert.DoesNotContain("operator", carrier);
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
    [InlineData("Standard.Array<TValue, Length>.new(func<int, TValue>)", "Respelled")]
    public void ReadsEachKindOfSupply(string member, string kind)
    {
        var audit = IntrinsicSupply.Audit(StandardModelProbe.Build("", out _), FindClrType);
        var entry = Assert.Single(audit, candidate => candidate.Description == member);

        Assert.Equal(Enum.Parse<IntrinsicSupply.Kind>(kind), entry.Kind);
    }
}