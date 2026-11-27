// Every intrinsic declaration in the real Standard sources is classified against real CLR metadata, so a
// promise the backend cannot keep fails here rather than as a Roslyn error in a user's build.

using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "4")]
public sealed class IntrinsicSupplyTests(StandardIntrinsicFixture fixture) : IClassFixture<StandardIntrinsicFixture>
{
    [Fact]
    public void AMistBodiedMemberOfAnIntrinsicTypeIsCarriedOnTheBackingType()
    {
        var int32 = fixture.Int32;
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

        StandardDeclarationEmissionTests.AssertCarries(carrier, "Int32_op_Addition(Standard.Result<int, Standard.DivisionError> a, int b)");
    }

    [Fact]
    public void EveryIntrinsicStandardDeclaresIsSupplied()
    {
        var audit = fixture.Audit;
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
        var audit = fixture.Audit;
        var entry = Assert.Single(audit, candidate => candidate.Description == member);

        Assert.Equal(Enum.Parse<IntrinsicSupply.Kind>(kind), entry.Kind);
    }
}

public sealed class StandardIntrinsicFixture
{
    internal ConcreteType Int32 { get; }
    internal ImmutableArray<(string Description, IntrinsicSupply.Kind Kind)> Audit { get; }

    public StandardIntrinsicFixture()
    {
        var model = StandardModelProbe.Build("", out _);
        Int32 = Assert.IsAssignableFrom<ConcreteType>(model.TypesByFQN["Standard.Int32"]);

        using var metadata = AmbientProviderTestHelper.ProviderOver(
            new DiagnosticBag(),
            typeof(object).Assembly.Location,
            typeof(Console).Assembly.Location,
            typeof(Regex).Assembly.Location);
        Audit = [.. IntrinsicSupply.Audit(model, metadata.FindClrType).Select(entry => (entry.Description, entry.Kind))];
    }
}