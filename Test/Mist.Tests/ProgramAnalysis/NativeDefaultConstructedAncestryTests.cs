using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class NativeDefaultConstructedAncestryTests
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void TheMostSpecificRestatementThroughAConstructedInterface_Decides(bool leafIsAbstract, bool expected)
    {
        var integer = new StructType("Int32", "System", null, null, null);
        var owner = new AbstractType("IRoot", "Native", null, null, null);
        var slot = new Method("Read", integer, [], default) { DeclaringType = owner, HasNativeBody = true };
        owner.MethodsList.Add(slot);

        var middle = new AbstractType("IMiddle<>", "Native", null, null, null);
        middle.TypeParametersList.Add(new TypeParameter("T", 0, middle.Scope, middle));
        middle.BaseAbstractTypes.Add(owner);
        middle.NativeSlotOverridesList.Add((slot, false));

        var leaf = new AbstractType("ILeaf", "Native", null, null, null);
        leaf.BaseAbstractTypes.Add(new ConstructedType(middle, [integer]));
        leaf.NativeSlotOverridesList.Add((slot, leafIsAbstract));

        Assert.Equal(expected, NativeDefaults.Answer(slot, owner, [leaf, middle, owner]));
    }
}