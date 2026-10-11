using Mist.Generated;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class ConstructorStorageDefaultsDecisionTests
{
    private static readonly MemberModifiers Public = new() { Accessibility = Accessibility.Public };

    private readonly HandBuiltModel model = new();

    [Theory]
    [InlineData("intrinsic", true)]
    [InlineData("class", true)]
    [InlineData("struct", true)]
    [InlineData("designated-enum", true)]
    [InlineData("unmarked-enum", false)]
    [InlineData("optional", true)]
    [InlineData("closed-defaultable", true)]
    [InlineData("closed-nondefaultable", false)]
    [InlineData("nondefaultable", false)]
    [InlineData("non-negative", true)]
    [InlineData("positive", false)]
    public void ConstructorEntry_UsesTheSlotsSemanticDefault(string shape, bool defaulted)
    {
        var owner = HandBuiltModel.Class("Holder");
        owner.FieldsList.Add(new Field("Item", Shaped(shape), OwnershipKind.Unrestricted, Public));

        Assert.Equal(defaulted, StorageDefaults.HasConstructorDefault(Assert.Single(InstanceStorage.Of(owner))));
    }

    [Theory]
    [InlineData("nondefaultable", false)]
    [InlineData("nondefaultable", true)]
    [InlineData("positive", false)]
    [InlineData("positive", true)]
    public void OwnerDefaultBlock_AnswersOnlySynthesisUnlessStorageHasAnInitializer(string shape, bool initialized)
    {
        var owner = HandBuiltModel.Class("Holder");
        var type = Shaped(shape);
        var value = HandBuiltModel.Literal(type, 7L);
        var field = new Field("Item", type, OwnershipKind.Unrestricted, Public) { Initializer = initialized ? value : null };
        owner.FieldsList.Add(field);
        owner.DefaultBlock = new DefaultBlock([new DefaultValueEntry(field.Name, value)], new DefaultBlockDeclarationNode());
        var slot = Assert.Single(InstanceStorage.Of(owner));

        Assert.True(Defaultability.StorageHasDefault(owner, slot));
        Assert.False(StorageDefaults.ZeroRefused(owner, slot));
        Assert.Equal(initialized, StorageDefaults.HasConstructorDefault(slot));
    }

    [Fact]
    public void DefaultBlockTypeBinding_DoesNotSupplyConstructorStorage()
    {
        var owner = HandBuiltModel.Class("Holder");
        var field = new Field("Item", Shaped("nondefaultable"), OwnershipKind.Unrestricted, Public);
        owner.FieldsList.Add(field);
        owner.DefaultBlock = new DefaultBlock(
            [new DefaultBindingEntry(field.Name, HandBuiltModel.Class("DefaultNeed"), [], new DefaultTypeBindingDefaultFieldInitializerNode())],
            new DefaultBlockDeclarationNode());
        var slot = Assert.Single(InstanceStorage.Of(owner));

        Assert.True(Defaultability.StorageHasDefault(owner, slot));
        Assert.False(StorageDefaults.HasConstructorDefault(slot));
    }

    private IMistType Shaped(string shape)
    {
        var integer = model.Type("Int32");
        integer.Modifiers = Public with { IsIntrinsic = true };
        var need = HandBuiltModel.Class("Need");
        need.ConstructorsList.Add(new Constructor(need.Name, need, [new Parameter("value", integer)], Public));

        if(shape is "designated-enum" or "unmarked-enum")
        {
            var variant = HandBuiltModel.Enum("Mode", integer);
            variant.MembersList.Add(new EnumMember("Ready", shape == "designated-enum", HandBuiltModel.Literal(integer, 7L)));
            return variant;
        }

        if(shape is "closed-defaultable" or "closed-nondefaultable")
        {
            var box = HandBuiltModel.Class("Box");
            var parameter = new TypeParameter("T", 0, box.Scope, box);
            box.TypeParametersList.Add(parameter);
            box.FieldsList.Add(new Field("Value", parameter, OwnershipKind.Unrestricted, Public));
            return new ConstructedType(box, [shape == "closed-defaultable" ? integer : need]);
        }

        if(shape is "positive" or "non-negative")
        {
            return new RefinedType(integer, HandBuiltModel.Node(), shape)
            {
                Predicate = new ComparisonPattern(HandBuiltModel.Node(), integer,
                    shape == "positive" ? ComparisonKind.GreaterThan : ComparisonKind.GreaterThanOrEqual, HandBuiltModel.Literal(integer, 0L)),
            };
        }

        return shape switch
        {
            "class" => HandBuiltModel.Class("Inner"),
            "struct" => HandBuiltModel.Struct("Inner"),
            "optional" => new UnionType([need, model.Type("Void")]),
            "nondefaultable" => need,
            _ => integer,
        };
    }
}