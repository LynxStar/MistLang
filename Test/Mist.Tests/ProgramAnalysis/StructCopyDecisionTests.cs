using Mist.Generated;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class StructCopyDecisionTests
{
    [Theory]
    [InlineData("mixed")]
    [InlineData("nested")]
    [InlineData("nested-tuple")]
    [InlineData("tuple")]
    [InlineData("optional")]
    [InlineData("generic")]
    [InlineData("generic-nested")]
    [InlineData("private-readonly")]
    public void ASharedCopyProtectsOwnedFieldsWithoutRequiringThePlainFieldsToClone(string shape)
    {
        var (type, clones) = Mixed(shape);
        var source = new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable("m", type));

        var copy = OwnedProvenance.At(clones, OwnershipKind.Owned, source);

        Assert.Same(source, Assert.IsType<CloneExpression>(copy).Source);
        var plan = OwnedStructCopies.Of(type, clones);
        Assert.True(plan.Required);
        Assert.Null(plan.Refusal);
        Assert.DoesNotContain(plan.Slots, slot => slot.Storage.Name == "Plain");
    }

    [Theory]
    [InlineData(OwnershipKind.Unrestricted)]
    [InlineData(OwnershipKind.Borrowed)]
    [InlineData(OwnershipKind.Frozen)]
    public void ANonOwnedDestinationKeepsTheExistingCopyBehavior(OwnershipKind destination)
    {
        var (type, clones) = Mixed("mixed");
        var source = new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable("m", type));

        Assert.Same(source, OwnedProvenance.At(clones, destination, source));
    }

    [Fact]
    public void AnOwnedSourceMovesWithoutAClone()
    {
        var (type, clones) = Mixed("mixed");
        var source = new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable("m", type) { Ownership = OwnershipKind.Owned });

        Assert.Same(source, OwnedProvenance.At(clones, OwnershipKind.Owned, source));
    }

    [Fact]
    public void APlainFieldMayShareItsObject()
    {
        var (type, clones) = Mixed("plain");
        var source = new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable("m", type));

        Assert.Same(source, OwnedProvenance.At(clones, OwnershipKind.Owned, source));
    }

    [Fact]
    public void ARequiredOwnedFieldCloneCarriesItsRefusal()
    {
        var (type, clones) = Mixed("mixed");
        var structure = Assert.IsType<StructType>(type);
        var refused = HandBuiltModel.Class("StringBuilder");
        structure.FieldsList[1] = new Field("Own", refused, OwnershipKind.Owned, default) { DeclaringType = structure };

        var plan = OwnedStructCopies.Of(type, clones);

        var field = Assert.Single(plan.Slots);
        Assert.Equal("Own", field.Storage.Name);
        Assert.Equal(OwnedCopyKind.Clone, field.Copy.Kind);
        Assert.Contains("holds 'StringBuilder', which cannot be cloned", plan.Refusal!.Why);
    }

    internal static (IMistType Type, CloneDerivation Clones) Mixed(string shape)
    {
        var scope = new Scope(null);
        var style = new ClassType("Style", "Probe", scope, null, DeclarationFragments.At<BodyTypeTypeDeclarationNode>());
        var integer = new StructType("Int32", "Standard", scope, null, null);
        Hold(style, "Size", integer);
        var mixed = new StructType("Mixed", "Probe", scope, null, DeclarationFragments.At<BodyTypeTypeDeclarationNode>());
        Hold(mixed, "Plain", style);
        if(shape != "plain")
        {
            IMistType owned = style;
            if(shape == "tuple")
            {
                owned = new TupleType([style, integer]);
            }
            else if(shape == "optional")
            {
                owned = new UnionType([style, new StructType("Void", "Standard", scope, null, null)]);
            }
            else if(shape == "generic")
            {
                owned = DeclarationFragments.Parameter(mixed, scope, "T");
            }

            mixed.FieldsList.Add(new Field("Own", owned, OwnershipKind.Owned, DeclarationFragments.Public with { IsReadonly = shape == "private-readonly", Accessibility = shape == "private-readonly" ? Accessibility.Private : Accessibility.Public }) { DeclaringType = mixed });
        }

        IMistType result = mixed;
        if(shape is "nested" or "nested-tuple")
        {
            var wrapper = new StructType("Wrapper", "Probe", scope, null, DeclarationFragments.At<BodyTypeTypeDeclarationNode>());
            Hold(wrapper, "Inner", shape == "nested-tuple" ? new TupleType([mixed, integer]) : mixed);
            result = wrapper;
        }
        else if(shape == "generic")
        {
            result = new ConstructedType(mixed, [style]);
        }
        else if(shape == "generic-nested")
        {
            var wrapper = new StructType("Wrapper<>", "Probe", scope, null, DeclarationFragments.At<BodyTypeTypeDeclarationNode>());
            Hold(wrapper, "Inner", DeclarationFragments.Parameter(wrapper, scope, "T"));
            Hold(wrapper, "Plain", style);
            result = new ConstructedType(wrapper, [mixed]);
        }

        return (result, new CloneDerivation([style, mixed]));
    }

    private static void Hold(ConcreteType type, string name, IMistType held)
    {
        type.FieldsList.Add(new Field(name, held, OwnershipKind.Unrestricted, DeclarationFragments.Public) { DeclaringType = type });
    }
}