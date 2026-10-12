using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class NativeDefaultsTests
{
    private readonly HandBuiltModel built = new();

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void ASlotsOwnDeclaration_DecidesWhereNothingRestatesIt(bool hasBody, bool answered)
    {
        var (owner, slot) = Slot(hasBody);

        Assert.Equal(answered, NativeDefaults.Answer(slot, owner, [owner]));
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, true, false)]
    public void ADerivedRestatement_OverridesTheDeclaration(bool declaredBody, bool reabstracted, bool answered)
    {
        var (owner, slot) = Slot(declaredBody);
        var derived = Interface("IDerived", owner);
        derived.NativeSlotOverridesList.Add((slot, reabstracted));

        Assert.Equal(answered, NativeDefaults.Answer(slot, owner, [derived, owner]));
    }

    [Fact]
    public void TheMostDerivedRestatement_Decides()
    {
        var (owner, slot) = Slot(hasBody: true);
        var middle = Interface("IMiddle", owner);
        var leaf = Interface("ILeaf", middle);
        middle.NativeSlotOverridesList.Add((slot, true));
        leaf.NativeSlotOverridesList.Add((slot, false));

        Assert.True(NativeDefaults.Answer(slot, owner, [leaf, middle, owner]));
        Assert.False(NativeDefaults.Answer(slot, owner, [middle, owner]));
    }

    [Fact]
    public void TwoRestatementsNeitherDerivingFromTheOther_AnswerNothing()
    {
        var (owner, slot) = Slot(hasBody: true);
        var left = Interface("ILeft", owner);
        var right = Interface("IRight", owner);
        left.NativeSlotOverridesList.Add((slot, false));
        right.NativeSlotOverridesList.Add((slot, false));

        Assert.False(NativeDefaults.Answer(slot, owner, [left, right, owner]));
    }

    [Fact]
    public void ARestatementTheConformerDoesNotReach_IsNotConsulted()
    {
        var (owner, slot) = Slot(hasBody: true);
        var elsewhere = Interface("IElsewhere", owner);
        elsewhere.NativeSlotOverridesList.Add((slot, true));

        Assert.True(NativeDefaults.Answer(slot, owner, [owner]));
    }

    [Fact]
    public void ARestatementOfAnotherSlot_IsNotConsulted()
    {
        var (owner, slot) = Slot(hasBody: true);
        var other = new Method("Other", built.Type("String"), [], default) { DeclaringType = owner };
        var derived = Interface("IDerived", owner);
        derived.NativeSlotOverridesList.Add((other, true));

        Assert.True(NativeDefaults.Answer(slot, owner, [derived, owner]));
    }

    private (AbstractType Owner, Method Slot) Slot(bool hasBody)
    {
        var owner = Interface("IGreeter");
        var slot = new Method("Greet", built.Type("String"), [], default) { DeclaringType = owner, HasNativeBody = hasBody };
        owner.MethodsList.Add(slot);

        return (owner, slot);
    }

    private static AbstractType Interface(string name, AbstractType? inherits = null)
    {
        var type = new AbstractType(name, "Native", new Scope(null), null, null);

        if(inherits is not null)
        {
            type.BaseAbstractTypes.Add(inherits);
        }

        return type;
    }
}