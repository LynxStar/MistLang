using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Backend;

public sealed class ConstructedStampabilityTests
{
    [Fact]
    public void ConcretePrimitiveStorageIsInferredStampable()
    {
        var concrete = Struct("Concrete");
        AddField(concrete, "item", Primitive());

        Assert.True(Stampability.IsStampable(concrete));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClosedGenericStorageUsesTheActualArgument(bool referenceArgument)
    {
        var box = Box();
        var argument = referenceArgument ? (IMistType)Reference() : Primitive();
        var closed = new ConstructedType(box, [argument]);

        Assert.Equal(!referenceArgument, Stampability.IsStampable(closed));
    }

    [Fact]
    public void ClosedReferenceStorageReportsTheSubstitutedType()
    {
        var closed = new ConstructedType(Box(), [Reference()]);

        var reason = Stampability.WhyNotStampable(closed);

        Assert.NotNull(reason);
        Assert.Contains("Owner", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void UnusedReferenceArgumentsDoNotAffectStampability()
    {
        var marker = Generic("Marker<>");
        AddField(marker, "item", Primitive());

        Assert.True(Stampability.IsStampable(new ConstructedType(marker, [Reference()])));
    }

    [Fact]
    public void UnconstrainedOpenStorageIsNotStampable()
    {
        Assert.False(Stampability.IsStampable(Box()));
    }

    [Fact]
    public void StampableConstraintProvesOpenInstanceStorage()
    {
        var box = Box();
        var parameter = box.TypeParametersList[0];
        parameter.ConstraintsList.Add(new KeywordConstraint(TypeParameterConstraintKind.Stampable));

        Assert.True(Stampability.IsStampable(parameter));
        Assert.True(Stampability.IsStampable(box));
    }

    [Fact]
    public void StampableArgumentConstraintProvesAConstructedOpenType()
    {
        var scope = Generic("Scope<>");
        var parameter = scope.TypeParametersList[0];
        parameter.ConstraintsList.Add(new KeywordConstraint(TypeParameterConstraintKind.Stampable));

        Assert.True(Stampability.IsStampable(new ConstructedType(Box(), [parameter])));
    }

    [Fact]
    public void DeclarationConstraintsDoNotReplaceTheActualClosedArgument()
    {
        var box = Box();
        box.TypeParametersList[0].ConstraintsList.Add(new KeywordConstraint(TypeParameterConstraintKind.Stampable));

        Assert.False(Stampability.IsStampable(new ConstructedType(box, [Reference()])));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StampableConstraintDoesNotTurnAValueOrPackParameterIntoAnElementType(bool pack)
    {
        var parameter = Generic("Scope<>").TypeParametersList[0];
        parameter.IsDependent = !pack;
        parameter.PackKind = pack ? TypePackKind.Unbracketed : TypePackKind.None;
        parameter.ConstraintsList.Add(new KeywordConstraint(TypeParameterConstraintKind.Stampable));

        Assert.False(Stampability.IsStampable(parameter));
    }

    [Theory]
    [InlineData(TypeParameterConstraintKind.Struct)]
    [InlineData(TypeParameterConstraintKind.Default)]
    [InlineData(TypeParameterConstraintKind.Readonly)]
    public void OtherKeywordConstraintsDoNotProveStampability(TypeParameterConstraintKind kind)
    {
        var box = Box();
        box.TypeParametersList[0].ConstraintsList.Add(new KeywordConstraint(kind));

        Assert.False(Stampability.IsStampable(box));
        Assert.False(Stampability.IsStampable(box.TypeParametersList[0]));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(20)]
    public void FiniteNestedInstancesOfTheSameDefinitionRemainStampable(int depth)
    {
        var box = Box();
        IMistType nested = Primitive();
        for(var i = 0; i < depth; i++)
        {
            nested = new ConstructedType(box, [nested]);
        }

        Assert.True(Stampability.IsStampable(nested));
    }

    [Fact]
    public void FiniteNestedInstancesStillInspectTheirInnermostStorage()
    {
        var box = Box();
        var nested = new ConstructedType(box, [new ConstructedType(box, [Reference()])]);

        Assert.False(Stampability.IsStampable(nested));
    }

    [Fact]
    public void FiniteNestingThroughATupleRemainsStampable()
    {
        var box = Box();
        var inner = new ConstructedType(box, [Primitive()]);
        var nested = new ConstructedType(box, [new TupleType([inner, Primitive()])]);

        Assert.True(Stampability.IsStampable(nested));
    }

    [Fact]
    public void RepeatedClosedStorageIsNotStampable()
    {
        var loop = Generic("Loop<>");
        AddField(loop, "next", new ConstructedType(loop, [loop.TypeParametersList[0]]));

        Assert.False(Stampability.IsStampable(new ConstructedType(loop, [Primitive()])));
    }

    [Fact]
    public void ExpandingGenericStorageTerminatesAsNonStampable()
    {
        var loop = Generic("Loop<>");
        var nested = new ConstructedType(Box(), [loop.TypeParametersList[0]]);
        AddField(loop, "next", new ConstructedType(loop, [nested]));

        Assert.False(Stampability.IsStampable(new ConstructedType(loop, [Primitive()])));
    }

    [Fact]
    public void MutuallyExpandingGenericStorageTerminatesAsNonStampable()
    {
        var first = Generic("First<>");
        var second = Generic("Second<>");
        AddField(first, "next", new ConstructedType(second, [first.TypeParametersList[0]]));
        AddField(second, "next", new ConstructedType(first, [new ConstructedType(Box(), [second.TypeParametersList[0]])]));

        Assert.False(Stampability.IsStampable(new ConstructedType(first, [Primitive()])));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GenericBaseStorageComposesSubstitutionsThroughEveryLevel(bool referenceArgument)
    {
        var ancestor = Box();
        var middle = Generic("Middle<>");
        middle.BaseType = new ConstructedType(ancestor, [middle.TypeParametersList[0]]);
        var derived = Generic("Derived<>");
        derived.BaseType = new ConstructedType(middle, [derived.TypeParametersList[0]]);
        var argument = referenceArgument ? (IMistType)Reference() : Primitive();

        Assert.Equal(!referenceArgument, Stampability.IsStampable(new ConstructedType(derived, [argument])));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NestedStorageUsesTheEnclosingInstantiation(bool referenceArgument)
    {
        var outer = Generic("Outer<>");
        var inner = new StructType("Inner", "Probe", outer.Scope, outer, null);
        AddField(inner, "item", outer.TypeParametersList[0]);
        var argument = referenceArgument ? (IMistType)Reference() : Primitive();
        var enclosing = new ConstructedType(outer, [argument]);

        Assert.Equal(!referenceArgument, Stampability.IsStampable(new ConstructedType(inner, [], enclosing)));
    }

    [Fact]
    public void ClosedGenericTupleStorageIsStampable()
    {
        var box = Generic("TupleBox<>");
        AddField(box, "item", new TupleType([box.TypeParametersList[0], Primitive()]));

        Assert.True(Stampability.IsStampable(new ConstructedType(box, [Primitive()])));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NamedTupleStorageHasTheSameVerdictAsAnUnnamedTuple(bool referenceArgument)
    {
        var argument = referenceArgument ? (IMistType)Reference() : Primitive();
        var tuple = new NamedTupleType(new TupleType([Primitive(), argument]), ["first", "second"]);

        Assert.Equal(!referenceArgument, Stampability.IsStampable(tuple));
    }

    [Fact]
    public void AliasesPreserveClosedGenericStampability()
    {
        var alias = new TypeAlias("ClosedBox", "Probe", null, null, null!)
        {
            AliasedType = new ConstructedType(Box(), [Primitive()]),
        };

        Assert.True(Stampability.IsStampable(alias));
    }

    [Fact]
    public void ConstructedAliasesSubstituteTheirOwnParameters()
    {
        var alias = new TypeAlias("Alias<>", "Probe", null, null, null!);
        var parameter = new TypeParameter("T", 0, alias.Scope, alias);
        alias.TypeParametersList.Add(parameter);
        alias.AliasedType = new ConstructedType(Box(), [parameter]);

        Assert.True(Stampability.IsStampable(new ConstructedType(alias, [Primitive()])));
        Assert.False(Stampability.IsStampable(new ConstructedType(alias, [Reference()])));
    }

    [Fact]
    public void AliasCyclesTerminateAsNonStampable()
    {
        var first = new TypeAlias("First", "Probe", null, null, null!);
        var second = new TypeAlias("Second", "Probe", null, null, null!) { AliasedType = first };
        first.AliasedType = second;

        Assert.False(Stampability.IsStampable(first));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AutoPropertyStorageParticipatesInStampability(bool referenceArgument)
    {
        var box = Generic("PropertyBox<>");
        box.PropertiesList.Add(new Property("Item", box.TypeParametersList[0], new MemberModifiers())
        {
            Getter = new PropertyAccessor { IsAuto = true },
            Setter = new PropertyAccessor { IsAuto = true },
            DeclaringType = box,
        });
        var argument = referenceArgument ? (IMistType)Reference() : Primitive();

        Assert.Equal(!referenceArgument, Stampability.IsStampable(new ConstructedType(box, [argument])));
    }

    [Fact]
    public void ComputedReferencePropertyDoesNotAddInstanceStorage()
    {
        var concrete = Struct("Computed");
        concrete.PropertiesList.Add(new Property("Item", Reference(), new MemberModifiers())
        {
            Getter = new PropertyAccessor { Body = new Block(null!) },
            DeclaringType = concrete,
        });

        Assert.True(Stampability.IsStampable(concrete));
    }

    [Fact]
    public void ExplicitBackingFieldStorageParticipatesInStampability()
    {
        var concrete = Struct("ExplicitStorage");
        concrete.PropertiesList.Add(new Property("Item", Reference(), new MemberModifiers())
        {
            Getter = new PropertyAccessor { Body = new Block(null!) },
            UsesBackingField = true,
            DeclaringType = concrete,
        });

        Assert.False(Stampability.IsStampable(concrete));
    }

    [Fact]
    public void StaticAutoPropertyDoesNotAddInstanceStorage()
    {
        var concrete = Struct("Static");
        concrete.PropertiesList.Add(new Property("Item", Reference(), new MemberModifiers { IsStatic = true })
        {
            Getter = new PropertyAccessor { IsAuto = true },
            DeclaringType = concrete,
        });

        Assert.True(Stampability.IsStampable(concrete));
    }

    [Fact]
    public void InheritedAutoPropertyStorageIsInspected()
    {
        var ancestor = Struct("Ancestor");
        ancestor.PropertiesList.Add(new Property("Item", Reference(), new MemberModifiers())
        {
            Getter = new PropertyAccessor { IsAuto = true },
            DeclaringType = ancestor,
        });
        var derived = Struct("Derived");
        derived.BaseType = ancestor;

        Assert.False(Stampability.IsStampable(derived));
    }

    [Fact]
    public void ClosedRefStructsRemainNonStampable()
    {
        var box = Box();
        box.Modifiers = new MemberModifiers { IsRef = true };

        Assert.False(Stampability.IsStampable(new ConstructedType(box, [Primitive()])));
    }

    private static StructType Struct(string name)
    {
        return new StructType(name, "Probe", null, null, null);
    }

    private static StructType Generic(string name)
    {
        var type = Struct(name);
        type.TypeParametersList.Add(new TypeParameter("T", 0, type.Scope, type));
        return type;
    }

    private static StructType Box()
    {
        var type = Generic("Box<>");
        AddField(type, "item", type.TypeParametersList[0]);
        return type;
    }

    private static StructType Primitive()
    {
        return new StructType("Int32", "Standard", null, null, null)
        {
            Modifiers = new MemberModifiers { IsIntrinsic = true },
        };
    }

    private static ClassType Reference()
    {
        return new ClassType("Owner", "Probe", null, null, null);
    }

    private static void AddField(StructType type, string name, IMistType fieldType)
    {
        type.FieldsList.Add(new Field(name, fieldType, OwnershipKind.Unrestricted, new MemberModifiers())
        {
            DeclaringType = type,
        });
    }
}