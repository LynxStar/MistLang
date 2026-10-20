using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using static Mist.Tests.Backend.SizedSequenceTypes;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class NativeSlotParameterSpellingTests
{
    private static readonly StructType Number = new("Int32", "Standard", new Scope(null), null, null);
    private static readonly ClassType Text = new HandBuiltModel().Type("String");

    [Fact]
    public void AReference_IsAnnotatedOnlyWhereTheSlotDeclaresItNullable()
    {
        Assert.Equal("string?", ParameterEmitter.SpelledType(new Parameter("format", Text), slotDeclaresNullable: true));
        Assert.Equal("string", ParameterEmitter.SpelledType(new Parameter("format", Text), slotDeclaresNullable: false));
    }

    [Fact]
    public void AnInterface_IsAReference()
    {
        var provider = new AbstractType("IFormatProvider", "System", new Scope(null), null, null);

        Assert.EndsWith("IFormatProvider?", ParameterEmitter.SpelledType(new Parameter("provider", provider), slotDeclaresNullable: true));
    }

    [Fact]
    public void AValueKind_KeepsItsType()
    {
        Assert.Equal("int", ParameterEmitter.SpelledType(new Parameter("other", Number), slotDeclaresNullable: true));
    }

    [Fact]
    public void AnOptional_IsNotAnnotatedTwice()
    {
        Assert.Equal("string?", ParameterEmitter.SpelledType(new Parameter("format", OptionalOf(Text)), slotDeclaresNullable: true));
    }

    [Theory]
    [InlineData(ByRefDirection.Ref)]
    [InlineData(ByRefDirection.In)]
    [InlineData(ByRefDirection.Out)]
    public void AByRefParameter_KeepsItsSpelling(ByRefDirection direction)
    {
        var modifier = new ParameterModifier(direction == ByRefDirection.In, IsRef: true, OwnershipKind.Unrestricted, direction);

        Assert.Equal("string", ParameterEmitter.SpelledType(new Parameter("text", Text, modifier), slotDeclaresNullable: true));
        Assert.Equal("T", ParameterEmitter.SpelledType(new Parameter("other", TypeParameter(constraint: null), modifier), slotDeclaresNullable: true));
    }

    [Theory]
    [InlineData(TypeParameterConstraintKind.Class, true, "T?")]
    [InlineData(TypeParameterConstraintKind.Struct, true, "T")]
    [InlineData(TypeParameterConstraintKind.Stampable, true, "T")]
    [InlineData(null, true, "T?")]
    [InlineData(null, false, "T")]
    [InlineData(TypeParameterConstraintKind.Class, false, "T")]
    public void ATypeParameter_IsAnnotatedOnlyForANullableSlotUnlessItIsAValueKind(
        TypeParameterConstraintKind? constraint, bool slotDeclaresNullable, string expected)
    {
        Assert.Equal(expected, ParameterEmitter.SpelledType(new Parameter("other", TypeParameter(constraint)), slotDeclaresNullable));
    }

    [Fact]
    public void ASubstitutedTypeParameter_IsJudgedByItsArgument()
    {
        var parameter = TypeParameter(constraint: null);
        var other = new Parameter("other", parameter);

        using(TypeNameEmitter.Substituting(TypeSubstitution.ForParameters([parameter], [Number])))
        {
            Assert.Equal("int", ParameterEmitter.SpelledType(other, slotDeclaresNullable: true));
        }

        using(TypeNameEmitter.Substituting(TypeSubstitution.ForParameters([parameter], [Text])))
        {
            Assert.Equal("string?", ParameterEmitter.SpelledType(other, slotDeclaresNullable: true));
        }
    }

    [Fact]
    public void AnObliviousScope_WritesNoAnnotation()
    {
        using(TypeNameEmitter.Oblivious())
        {
            Assert.Equal("string", ParameterEmitter.SpelledType(new Parameter("format", Text), slotDeclaresNullable: true));
            Assert.Equal("T", ParameterEmitter.SpelledType(new Parameter("other", TypeParameter(constraint: null)), slotDeclaresNullable: true));
        }
    }

    [Fact]
    public void OutsideAnEmission_OnlyANativeParameterDeclaresItself()
    {
        Assert.True(NativeSlotNullability.Declares(new Parameter("format", Text) { ClrDeclaresNullable = true }));
        Assert.False(NativeSlotNullability.Declares(new Parameter("format", Text)));
    }

    private static TypeParameter TypeParameter(TypeParameterConstraintKind? constraint)
    {
        var parameter = new TypeParameter("T", 0, new Scope(null), null);

        if(constraint is { } kind)
        {
            parameter.ConstraintsList.Add(new KeywordConstraint(kind));
        }

        return parameter;
    }
}