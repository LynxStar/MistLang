using Mist.ProgramAnalysis.Flow;
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
public sealed class LocalStorageDecisionTests
{
    [Fact]
    public void TupleFieldAfterFixedSpreadUsesItsExpandedStorageIndex()
    {
        var pack = new TypeParameter("Ts", 0, new Scope(null), null)
        {
            PackKind = TypePackKind.Unbracketed,
            PackLength = 2,
        };
        var tuple = new TupleType([new SpreadType(pack), HandBuiltModel.Class("Reference")]);
        var variable = new LocalVariable("value", tuple);
        var receiver = new LocalReferenceExpression(HandBuiltModel.Node(), variable);
        var field = tuple.PositionalFields[1];
        var access = new MemberAccessExpression(HandBuiltModel.Node(), receiver, field.Name, field.Type,
            member: new FieldResult(field, tuple, []));

        Assert.Equal(2, LocalStoragePath.Of(access)!.ElementIndex);
    }

    [Fact]
    public void NestedClosedWrappersOfEmptyStorageNeedNoAssignment()
    {
        var empty = HandBuiltModel.Struct("Empty");
        var wrapper = HandBuiltModel.Struct("Wrapper<>");
        var parameter = new TypeParameter("T", 0, wrapper.Scope, wrapper);
        wrapper.TypeParametersList.Add(parameter);
        wrapper.FieldsList.Add(new Field("item", parameter, default, default));
        var inner = new ConstructedType(wrapper, [empty]);
        var outer = new ConstructedType(wrapper, [inner]);

        Assert.True(LocalAssignmentSlots.IsEmpty(outer));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RecursiveOrExpandingStorageNeverProvesEmpty(bool expanding)
    {
        var recursive = HandBuiltModel.Struct("Recursive<>");
        var parameter = new TypeParameter("T", 0, recursive.Scope, recursive);
        recursive.TypeParametersList.Add(parameter);
        var self = new ConstructedType(recursive, [parameter]);
        recursive.FieldsList.Add(new Field("next", expanding ? new ConstructedType(recursive, [self]) : self, default, default));

        Assert.False(LocalAssignmentSlots.IsEmpty(new ConstructedType(recursive, [HandBuiltModel.Struct("Empty")])));
    }

    [Fact]
    public void MutuallyRecursiveLayoutsNeverProveEmpty()
    {
        var first = HandBuiltModel.Struct("First");
        var second = HandBuiltModel.Struct("Second");
        first.FieldsList.Add(new Field("next", second, default, default));
        second.FieldsList.Add(new Field("next", first, default, default));

        Assert.False(LocalAssignmentSlots.IsEmpty(first));
    }

    [Theory]
    [InlineData("complete", true)]
    [InlineData("unknown", false)]
    [InlineData("hidden-empty", true)]
    [InlineData("hidden-reference", false)]
    [InlineData("unresolved-base", false)]
    [InlineData("auto-property", false)]
    public void EmptyProofRequiresCompleteStorage(string shape, bool expected)
    {
        var type = HandBuiltModel.Struct("Storage");
        type.ImportedStorage = shape switch
        {
            "complete" => new StorageLayout(true, []),
            "unknown" => new StorageLayout(false, []),
            "hidden-empty" => new StorageLayout(true, [new StorageField("hidden", HandBuiltModel.Struct("Empty"))]),
            "hidden-reference" => new StorageLayout(true, [new StorageField("hidden", HandBuiltModel.Class("Reference"))]),
            _ => null,
        };
        type.BaseUnresolved = shape == "unresolved-base";
        if(shape == "auto-property")
        {
            type.PropertiesList.Add(new Property("Item", HandBuiltModel.Class("Reference"), default)
            {
                Getter = new PropertyAccessor { IsAuto = true },
            });
        }

        Assert.Equal(expected, LocalAssignmentSlots.IsEmpty(type));
    }
}