using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

// Tier 1: ValuePack.Expand over `(TFirst first, params TArgs args, String last)`, with the pack bound to
// the elements a case names. A parameter is rendered by its type, and by `params` where it still absorbs.
[Trait("Tier", "1")]
public sealed class ValuePackExpansionTests
{
    private readonly HandBuiltModel built = new();
    private readonly TypeParameter first = new("TFirst", 0, new Scope(null), null);
    private readonly TypeParameter pack = new("TArgs", 1, new Scope(null), null) { PackKind = TypePackKind.Unbracketed };
    private readonly TypeParameter rest = new("TRest", 0, new Scope(null), null) { PackKind = TypePackKind.Unbracketed };

    [Theory]
    [InlineData("Int32, Boolean", "Double, Int32, Boolean, String")]
    [InlineData("Int32", "Double, Int32, String")]
    [InlineData("", "Double, String")]
    [InlineData("TRest...", "Double, params (TRest...), String")]
    [InlineData("Int32, TRest...", "Double, Int32, params (TRest...), String")]
    [InlineData("TRest..., Int32", "Double, params (TRest...), Int32, String")]
    public void ABoundPackBecomesOneParameterPerElement_AndAPackPerSpread(string bound, string expected)
    {
        var elements = bound.Length == 0 ? [] : bound.Split(", ").Select(Named).ToList();
        var substitution = TypeSubstitution.ForParameters(
            [first, pack], [built.Type("Double"), new LiftedType(built.Type("Type"), new TypePackValue(elements))]);

        Assert.Equal(expected, Render(ValuePack.Expand(Declared(), substitution, interner: null)));
    }

    [Fact]
    public void AnUnboundPackStaysTheOneParameterItWasDeclaredAs()
    {
        var substitution = TypeSubstitution.ForParameters([first, pack], [built.Type("Double"), null]);

        Assert.Equal("Double, params (TArgs...), String", Render(ValuePack.Expand(Declared(), substitution, interner: null)));
    }

    [Fact]
    public void AHomogeneousParamsArrayIsNotAPack()
    {
        var array = HandBuiltModel.Class("Int32[]");
        var declared = new List<Parameter> { new("xs", array) { IsFlattened = true } };

        Assert.Same(declared[0], Assert.Single(ValuePack.Expand(declared, TypeSubstitution.Empty, interner: null)));
    }

    private List<Parameter> Declared()
    {
        return
        [
            new Parameter("first", first),
            new Parameter("args", new TupleType([new SpreadType(pack)])) { IsFlattened = true },
            new Parameter("last", built.Type("String")),
        ];
    }

    private IMistType Named(string name)
    {
        return name == "TRest..." ? new SpreadType(rest) : built.Type(name);
    }

    private static string Render(IReadOnlyList<Parameter> parameters)
    {
        return string.Join(", ", parameters.Select(parameter => parameter.IsFlattened ? $"params {parameter.Type.Name}" : parameter.Type.Name));
    }
}