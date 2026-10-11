using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using static Mist.Tests.Backend.SizedSequenceTypes;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class OptionalSpellingTests
{
    private static readonly StructType Number = new("Int32", "Standard", new Scope(null), null, null);
    private static readonly ClassType Text = new HandBuiltModel().Type("String");

    [Fact]
    public void AReferenceKind_IsAnnotatedWhereAValueKindIsNullable()
    {
        Assert.Equal("string?", TypeNameEmitter.Emit(OptionalOf(Text)));
        Assert.Equal("int?", TypeNameEmitter.Emit(OptionalOf(Number)));
        Assert.Equal("int[]?", TypeNameEmitter.Emit(OptionalOf(ArrayOf(Number, 3))));
        Assert.Equal("System.Func<string>?", TypeNameEmitter.Emit(OptionalOf(new FunctionType([], Text))));
        Assert.Equal("object?", TypeNameEmitter.Emit(OptionalOf(UnknownType.Instance)));
    }

    [Fact]
    public void ARequiredReference_StaysUnannotated()
    {
        Assert.Equal("string", TypeNameEmitter.Emit(Text));
        Assert.Equal("(string?, int)", TypeNameEmitter.Emit(TupleOf(OptionalOf(Text), Number)));
    }

    [Theory]
    [InlineData(TypeParameterConstraintKind.Class, "T?")]
    [InlineData(TypeParameterConstraintKind.Struct, "T?")]
    [InlineData(null, "T")]
    public void ATypeParameter_IsAnnotatedOnlyWhereItsConstraintFixesItsKind(
        TypeParameterConstraintKind? constraint, string expected)
    {
        var parameter = new TypeParameter("T", 0, new Scope(null), null);

        if(constraint is { } kind)
        {
            parameter.ConstraintsList.Add(new KeywordConstraint(kind));
        }

        Assert.Equal(expected, TypeNameEmitter.Emit(OptionalOf(parameter)));
    }

    [Fact]
    public void AnInterfaceConstraint_DoesNotMakeAParameterAReference()
    {
        var parameter = new TypeParameter("T", 0, new Scope(null), null);
        parameter.ConstraintsList.Add(new TypeConstraint(new AbstractType("Shape", "Probe", new Scope(null), null, null)));

        Assert.Equal("T", TypeNameEmitter.Emit(OptionalOf(parameter)));
    }

    [Fact]
    public void AContextKey_KeepsItsMarkerAndCarriesNoAnnotation()
    {
        Assert.Equal("MistContext.Optional<string>", TypeNameEmitter.EmitContextKey(OptionalOf(Text)));
        Assert.Equal("MistContext.Optional<int[]>", TypeNameEmitter.EmitContextKey(OptionalOf(ArrayOf(Number, 3))));
    }

    [Fact]
    public void AnObliviousScope_DropsReferenceAnnotationsAndOnlyForItsDuration()
    {
        using(TypeNameEmitter.Oblivious())
        {
            Assert.Equal("string", TypeNameEmitter.Emit(OptionalOf(Text)));
            Assert.Equal("(string, int?)", TypeNameEmitter.Emit(TupleOf(OptionalOf(Text), OptionalOf(Number))));
            Assert.Equal("MistContext.Optional<string>", TypeNameEmitter.EmitContextKey(OptionalOf(Text)));
        }

        Assert.Equal("string?", TypeNameEmitter.Emit(OptionalOf(Text)));
    }

    [Fact]
    public void TheUnannotatedSpelling_DropsOnlyAReferenceOptionalsAnnotation()
    {
        Assert.Equal("string", TypeNameEmitter.EmitUnannotated(OptionalOf(Text)));
        Assert.Equal("string", TypeNameEmitter.EmitUnannotated(AliasOf("Maybe", OptionalOf(Text))));
        Assert.Equal("int?", TypeNameEmitter.EmitUnannotated(OptionalOf(Number)));
        Assert.Equal("System.Collections.Generic.List<string?>", TypeNameEmitter.EmitUnannotated(ListOf(OptionalOf(Text))));
        Assert.Equal("string", TypeNameEmitter.EmitAsPatternType(OptionalOf(Text)));
    }

    private static ConstructedType ListOf(IMistType element)
    {
        var list = new ClassType("List<>", "System.Collections.Generic", new Scope(null), null, null!);
        return new ConstructedType(list, [element]);
    }
}