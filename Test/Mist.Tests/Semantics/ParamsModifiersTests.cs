using Mist.ProgramModel.Construction.Members;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

// Tier 1: ParamsModifiers over directly built parameter lists. A list is written as its parameters'
// kinds: `pack` and `array` are the two `params` forms, `fixed` a parameter without the modifier.
[Trait("Tier", "1")]
public sealed class ParamsModifiersTests
{
    private readonly HandBuiltModel built = new();
    private readonly TypeParameter pack = new("TArgs", 0, new Scope(null), null) { PackKind = TypePackKind.Unbracketed };
    private readonly TypeParameter plain = new("T", 0, new Scope(null), null);

    [Theory]
    [InlineData("pack, pack", "1")]
    [InlineData("pack, array", "1")]
    [InlineData("array, pack", "1")]
    [InlineData("array, array", "1")]
    [InlineData("pack, fixed, array", "2")]
    [InlineData("pack, pack, pack", "1, 2")]
    [InlineData("fixed, pack, fixed, array, array", "3, 4")]
    public void EveryParamsAfterTheFirstIsSurplus(string list, string expected)
    {
        Assert.Equal(expected, string.Join(", ", ParamsModifiers.Surplus(Parameters(list))));
    }

    [Theory]
    [InlineData("")]
    [InlineData("fixed, fixed")]
    [InlineData("pack")]
    [InlineData("array")]
    [InlineData("fixed, pack, fixed")]
    [InlineData("fixed, array")]
    public void AListWithOneParamsOrNoneHasNoSurplus(string list)
    {
        Assert.Empty(ParamsModifiers.Surplus(Parameters(list)));
    }

    [Theory]
    [InlineData("scalar", "its type 'int' cannot collect a flat argument list")]
    [InlineData("type-parameter", "its type 'T' cannot collect a flat argument list")]
    [InlineData("optional-array", "cannot collect a flat argument list")]
    [InlineData("ref-array", "it is passed by reference, where the arguments it collects are values")]
    [InlineData("ref-scalar", "its type 'int' cannot collect a flat argument list")]
    public void AParamsThatCannotCollectAFlatListOfValuesIsMalformed(string shape, string reason)
    {
        Assert.Contains(reason, ParamsModifiers.WhyMalformed(Shaped(shape, isParams: true)));
    }

    [Theory]
    [InlineData("pack")]
    [InlineData("tuple")]
    [InlineData("array")]
    [InlineData("failed-type")]
    public void AParamsOverATupleOrAnArrayIsWellFormed(string shape)
    {
        Assert.Null(ParamsModifiers.WhyMalformed(Shaped(shape, isParams: true)));
    }

    [Theory]
    [InlineData("scalar")]
    [InlineData("ref-array")]
    public void AParameterWithoutTheModifierIsNotJudged(string shape)
    {
        Assert.Null(ParamsModifiers.WhyMalformed(Shaped(shape, isParams: false)));
    }

    private List<Parameter> Parameters(string list)
    {
        return list.Length == 0
            ? []
            : [.. list.Split(", ").Select(kind => Shaped(kind == "fixed" ? "scalar" : kind, isParams: kind != "fixed"))];
    }

    private Parameter Shaped(string shape, bool isParams)
    {
        var integer = built.Type("Int32");
        var array = built.Interner.GetOrCreateConstructedType(built.Type("Array<,>"), [integer, built.Interner.GetOrCreateUnknownValue(integer)]);

        var type = shape switch
        {
            "pack" => new TupleType([new SpreadType(pack)]),
            "tuple" => built.Interner.GetOrCreateTupleType([integer, integer]),
            "array" or "ref-array" => array,
            "optional-array" => built.Interner.GetOrCreateUnionType([array, built.Type("Void")]),
            "scalar" or "ref-scalar" => integer,
            "type-parameter" => plain,
            "failed-type" => ErrorType.Instance,
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        var isRef = shape.StartsWith("ref-", StringComparison.Ordinal);

        return new Parameter("p", type, new ParameterModifier(IsReadonly: false, IsRef: isRef, OwnershipKind.Unrestricted))
        {
            IsFlattened = isParams,
        };
    }
}