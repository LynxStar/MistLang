using System.Reflection;
using System.Runtime.InteropServices;
using Mist.Backends.CSharpTranspiler.AmbientProvider;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class ClrCallShapeTests
{
    [Theory]
    [InlineData(nameof(IFixture.Defaults), "text")]
    [InlineData(nameof(IFixture.Defaults), "count")]
    [InlineData(nameof(IFixture.Defaults), "day")]
    [InlineData(nameof(IFixture.Attributed), "missing")]
    public void AParameterTheMetadataMarksOptional_HasANativeDefault(string method, string name)
    {
        Assert.True(ParameterProjector.HasDefault(Parameter(method, name)));
    }

    [Fact]
    public void ARequiredParameter_HasNoNativeDefault()
    {
        Assert.False(ParameterProjector.HasDefault(Parameter(nameof(IFixture.Defaults), "required")));
    }

    [Theory]
    [InlineData(nameof(IFixture.Array))]
    [InlineData(nameof(IFixture.Span))]
    public void AParamsCollection_IsParamsAndNotADefault(string method)
    {
        var parameter = Parameter(method, "values");

        Assert.True(ParameterProjector.IsParams(parameter));
        Assert.False(ParameterProjector.HasDefault(parameter));
    }

    [Theory]
    [InlineData(nameof(IFixture.Defaults), "required")]
    [InlineData(nameof(IFixture.Plain), "values")]
    [InlineData(nameof(IFixture.ByRef), "value")]
    [InlineData(nameof(IFixture.ByRef), "result")]
    public void AnythingElse_IsNotParams(string method, string name)
    {
        Assert.False(ParameterProjector.IsParams(Parameter(method, name)));
    }

    private static ParameterInfo Parameter(string method, string name)
    {
        return typeof(IFixture).GetMethod(method)!.GetParameters().Single(parameter => parameter.Name == name);
    }

    private interface IFixture
    {
        void Defaults(int required, string? text = null, int count = 3, DayOfWeek day = DayOfWeek.Friday);

        void Attributed([Optional] object? missing);

        void Array(int head, params int[] values);

        void Span(params ReadOnlySpan<int> values);

        void Plain(int[] values);

        void ByRef(ref int value, out int result);
    }
}