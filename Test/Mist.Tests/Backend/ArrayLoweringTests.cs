using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class ArrayLoweringTests
{
    public ArrayLoweringTests()
    {
        ArrayLowering.Reset();
    }

    [Fact]
    public void AnInitializerConstructionIsGeneratedFromTheElementSpellingAndTheLiftedLength()
    {
        var generated = ArrayLowering.Generated(SizedSequenceTypes.ArrayOf(SizedSequenceTypes.StringType, 3), 1);

        Assert.Equal(("string", 3L), generated);
        Assert.True(ArrayLowering.Used());
    }

    [Theory]
    [InlineData("Span")]
    [InlineData("ProbeArray")]
    public void AnotherOneArgumentLengthIndexedConstructionIsNotGeneratedAndLeavesTheHelperUnused(string shape)
    {
        var generated = ArrayLowering.Generated(LengthIndexed(shape), 1);

        Assert.Null(generated);
        Assert.False(ArrayLowering.Used());
    }

    [Fact]
    public void ResetForgetsAnEarlierGeneratedConstruction()
    {
        _ = ArrayLowering.Generated(SizedSequenceTypes.ArrayOf(SizedSequenceTypes.IntegerType, 2), 1);

        ArrayLowering.Reset();

        Assert.False(ArrayLowering.Used());
    }

    private static IMistType LengthIndexed(string shape)
    {
        if(shape == "Span")
        {
            return SizedSequenceTypes.SpanOf(SizedSequenceTypes.IntegerType, 3);
        }

        var probeArray = new ClassType("Array<,>", "Probe", new Scope(null), null, null);
        return new ConstructedType(probeArray, [SizedSequenceTypes.IntegerType, new LiftedType(SizedSequenceTypes.IntegerType, 3L)]);
    }
}