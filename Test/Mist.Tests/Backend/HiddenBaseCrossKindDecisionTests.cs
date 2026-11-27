using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class HiddenBaseCrossKindDecisionTests
{
    private static readonly MemberModifiers Public = new() { Accessibility = Accessibility.Public };

    private readonly ClassType declaringType = HandBuiltModel.Class("Base");
    private readonly ClassType intermediate;
    private readonly ClassType receiver;
    private readonly Method selected;

    public HiddenBaseCrossKindDecisionTests()
    {
        intermediate = HandBuiltModel.Class("Intermediate", declaringType);
        receiver = HandBuiltModel.Class("Derived", intermediate);
        selected = new Method("Read", HandBuiltModel.Class("Value"), [], Public) { DeclaringType = declaringType };
        declaringType.MethodsList.Add(selected);
    }

    [Fact]
    public void SameNamePropertyRequiresTheSelectedDeclaringType()
    {
        AddProperty("Read");

        Assert.Same(declaringType, HiddenBaseCall.ReceiverType(receiver, selected));
    }

    [Fact]
    public void SameNameFieldRequiresTheSelectedDeclaringType()
    {
        AddField("Read");

        Assert.Same(declaringType, HiddenBaseCall.ReceiverType(receiver, selected));
    }

    [Fact]
    public void DifferentlyNamedPropertyAndFieldRequireNoCast()
    {
        AddProperty("Count");
        AddField("Value");

        Assert.Null(HiddenBaseCall.ReceiverType(receiver, selected));
    }

    private void AddProperty(string name)
    {
        intermediate.PropertiesList.Add(new Property(name, selected.ReturnType, Public)
        {
            DeclaringType = intermediate,
            Getter = new PropertyAccessor { IsAuto = true },
        });
    }

    private void AddField(string name)
    {
        intermediate.FieldsList.Add(new Field(name, selected.ReturnType, OwnershipKind.Unrestricted, Public)
        {
            DeclaringType = intermediate,
        });
    }
}