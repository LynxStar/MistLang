using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "1")]
public sealed class AsConversionClassificationTests
{
    [Theory]
    [InlineData("identity", "Identity", null)]
    [InlineData("derived-to-base", "Widening", null)]
    [InlineData("constituent-to-union", "Widening", null)]
    [InlineData("union-to-constituent", "UnionExtraction", "Int32")]
    [InlineData("base-to-derived", "Narrowing", null)]
    [InlineData("unknown-to-concrete", "Narrowing", null)]
    public void EachRelatedPairIsClassifiedByTheAsOperatorSpec(string caseName, string expected, string? constituent)
    {
        var (source, target) = Case(caseName);

        var (kind, reached) = OperatorBinder.Classify(source, target);

        Assert.True(kind?.ToString() == expected, $"{caseName}: {kind?.ToString() ?? "unrelated"}");
        Assert.Equal(constituent, reached?.Name);
    }

    [Fact]
    public void SiblingsUnderOneBaseHaveNoRelationship()
    {
        var animal = HandBuiltModel.Class("Animal");

        var (kind, _) = OperatorBinder.Classify(HandBuiltModel.Class("Cat", animal), HandBuiltModel.Class("Dog", animal));

        Assert.Null(kind);
    }

    private static (IMistType Source, IMistType Target) Case(string name)
    {
        var model = new HandBuiltModel();
        var integer = model.Type("Int32");
        var union = model.Interner.GetOrCreateUnionType([integer, model.Type("String")]);
        var animal = HandBuiltModel.Class("Animal");
        var dog = HandBuiltModel.Class("Dog", animal);

        return name switch
        {
            "identity" => (dog, dog),
            "derived-to-base" => (dog, animal),
            "constituent-to-union" => (integer, union),
            "union-to-constituent" => (union, integer),
            "base-to-derived" => (animal, dog),
            "unknown-to-concrete" => (UnknownType.Instance, integer),
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
        };
    }
}