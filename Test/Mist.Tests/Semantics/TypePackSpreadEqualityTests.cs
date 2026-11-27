using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Semantics;

[Trait("Tier", "1")]
public sealed class TypePackSpreadEqualityTests
{
    [Fact]
    public void SeparateSpreadsOfTheSameDeclarationProduceEqualPackValues()
    {
        var operand = DeclarePack("First<>");
        var first = new TypePackValue([new SpreadType(operand)]);
        var second = new TypePackValue([new SpreadType(operand)]);

        Assert.True(first.Equals(second));
        Assert.True(first.Equals((object)second));
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void SpreadsOfSameNamedParametersFromDifferentDeclarationsProduceUnequalPackValues()
    {
        var first = new TypePackValue([new SpreadType(DeclarePack("First<>"))]);
        var second = new TypePackValue([new SpreadType(DeclarePack("Second<>"))]);

        Assert.False(first.Equals(second));
        Assert.False(first.Equals((object)second));
    }

    private static TypeParameter DeclarePack(string declaringTypeName)
    {
        var declaringType = new ClassType(declaringTypeName, "Probe", null, null, null);
        return new TypeParameter("TArgs", 0, declaringType.Scope, declaringType)
        {
            PackKind = TypePackKind.Unbracketed,
        };
    }
}