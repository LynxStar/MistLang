using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "1")]
public sealed class RuntimeIdentityTests
{
    private readonly HandBuiltModel built = new();

    [Fact]
    public void TupleElementNamesInsideATypeArgument_AreNotPartOfItsIdentity()
    {
        var list = built.Type("List<>");
        var named = Named(Tuple("String", "Int32"), "Name", "Score");

        Assert.True(ConversionRelation.IsIdentity(Constructed(list, Tuple("String", "Int32")), Constructed(list, named)));
        Assert.Equal(ConversionKind.Identity, ConversionClassifier.Classify(Constructed(list, named), Constructed(list, Tuple("String", "Int32"))));
    }

    [Fact]
    public void TupleElementNamesInsideATuple_AreNotPartOfItsIdentity()
    {
        var inner = Named(Tuple("Int32", "Int32"), "X", "Y");

        Assert.True(ConversionRelation.IsIdentity(
            built.Interner.GetOrCreateTupleType([inner, built.Type("String")]),
            built.Interner.GetOrCreateTupleType([Tuple("Int32", "Int32"), built.Type("String")])));
    }

    [Fact]
    public void DifferentElementTypes_StayDifferentWhateverTheirNames()
    {
        var list = built.Type("List<>");

        Assert.False(ConversionRelation.IsIdentity(
            Constructed(list, Named(Tuple("String", "Int32"), "Name", "Score")), Constructed(list, Tuple("String", "Int64"))));
        Assert.False(ConversionRelation.IsIdentity(Constructed(list, built.Type("Int32")), Constructed(built.Type("Set<>"), built.Type("Int32"))));
    }

    [Fact]
    public void TwoDeclarationsOfOneRuntimeType_AreOneType()
    {
        var intrinsic = Runtime("List<>", "Standard", "System.Collections.Generic.List`1");
        var projected = Runtime("List<>", "System.Collections.Generic", "System.Collections.Generic.List`1");

        Assert.True(ConversionRelation.IsIdentity(intrinsic, projected));
        Assert.True(ConversionRelation.IsIdentity(
            Constructed(intrinsic, Tuple("String", "Int32")), Constructed(projected, Named(Tuple("String", "Int32"), "Name", "Score"))));
    }

    [Theory]
    [InlineData("System.Type")]
    [InlineData(null)]
    public void ARuntimeNameUnknownOrDifferent_KeepsTheDeclarationsApart(string? other)
    {
        var intrinsic = Runtime("Type", "Standard", "System.Type");
        var projected = Runtime("Type", "Probe", other is null ? null : "System.Reflection.TypeInfo");

        Assert.False(ConversionRelation.IsIdentity(intrinsic, projected));
    }

    private static ClassType Runtime(string name, string namespaceName, string? runtimeName)
    {
        return new ClassType(name, namespaceName, new Scope(null), null, null) { ClrMetadataName = runtimeName };
    }

    private TupleType Tuple(params string[] elements)
    {
        return built.Interner.GetOrCreateTupleType([.. elements.Select(built.Type)]);
    }

    private static NamedTupleType Named(TupleType underlying, params string[] names)
    {
        return new NamedTupleType(underlying, names);
    }

    private ConstructedType Constructed(IMistType definition, IMistType argument)
    {
        return built.Interner.GetOrCreateConstructedType(definition, [argument]);
    }
}