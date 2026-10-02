using Mist.ProgramAnalysis;
using Mist.Tests.Helpers;
using static Mist.Tests.Helpers.GenericDefaultShapes;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class ConstructedBaseChainTests
{
    private readonly GenericDefaultShapes shapes = new();

    [Theory]
    [InlineData("constructor-and-a-block", true)]
    [InlineData("constructor", false)]
    public void ConstructorlessTypeOverAConstructedBase_ChainsToItsDefaultOnlyWhereItHasOne(string shape, bool defaulted)
    {
        var box = shapes.Box(shape);
        var intBox = shapes.Type("IntBox", baseType: Construct(box, shapes.Integer));

        Assert.Same(defaulted ? box : box.ConstructorsList[0], PurityJudge.ChainedOf(intBox));
    }
}