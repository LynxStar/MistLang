using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

/// <summary>
/// Both sides are built with constructors and never the interner, because that is how a bound arrives:
/// <see cref="ConversionRelation.Bases"/> substitutes a base clause without one, and names, spreads and
/// closed refinements are separate objects at every mention, so equal types are rarely the same object.
/// </summary>
[Trait("Tier", "1")]
public sealed class ConstraintEntailmentSamenessTests
{
    private const string Refused = "MIST0178";

    private readonly HandBuiltModel built = new();

    private readonly AbstractType rank = Generic(new AbstractType("IRank", "Entailment", new Scope(null), null, null));

    private ClassType Integer => built.Type("Int32");

    private ClassType Text => built.Type("String");

    private ClassType Flag => built.Type("Boolean");

    [Theory]
    [InlineData("nothing", null)]
    [InlineData("element names", null)]
    [InlineData("element names on the requirement", null)]
    [InlineData("element order", Refused)]
    [InlineData("a missing element", Refused)]
    [InlineData("an extra element", Refused)]
    public void TupleIsTheRequiredTupleWhenItsElementsAgreeInOrderWhateverTheyAreNamed(string difference, string? expected)
    {
        IMistType required = new TupleType([Integer, Text, Flag]);
        IMistType offered = difference switch
        {
            "element names" => new NamedTupleType(new TupleType([Integer, Text, Flag]), ["count", "label", null]),
            "element order" => new TupleType([Text, Integer, Flag]),
            "a missing element" => new TupleType([Integer, Text]),
            "an extra element" => new TupleType([Integer, Text, Flag, Flag]),
            _ => new TupleType([Integer, Text, Flag]),
        };

        if(difference == "element names on the requirement")
        {
            required = new NamedTupleType(new TupleType([Integer, Text, Flag]), ["count", "label", null]);
        }

        Assert.Equal(expected, Entailed(offered, required));
    }

    [Theory]
    [InlineData("nothing", null)]
    [InlineData("constituent order", null)]
    [InlineData("a missing constituent", Refused)]
    [InlineData("an extra constituent", Refused)]
    [InlineData("another constituent", Refused)]
    public void UnionIsTheRequiredUnionWhenItHoldsTheSameConstituentsInAnyOrder(string difference, string? expected)
    {
        var real = built.Type("Double");
        var offered = difference switch
        {
            "constituent order" => new UnionType([Flag, Integer, Text]),
            "a missing constituent" => new UnionType([Integer, Text]),
            "an extra constituent" => new UnionType([Integer, Text, Flag, real]),
            "another constituent" => new UnionType([Integer, Text, real]),
            _ => new UnionType([Integer, Text, Flag]),
        };

        Assert.Equal(expected, Entailed(offered, new UnionType([Integer, Text, Flag])));
    }

    [Theory]
    [InlineData("nothing", null)]
    [InlineData("purity", Refused)]
    [InlineData("return type", Refused)]
    [InlineData("parameter order", Refused)]
    [InlineData("a missing parameter", Refused)]
    public void FunctionIsTheRequiredFunctionWhenPurityReturnAndParametersAgree(string difference, string? expected)
    {
        var offered = difference switch
        {
            "purity" => new FunctionType([Integer, Text], Flag, isPure: false),
            "return type" => new FunctionType([Integer, Text], Integer, isPure: true),
            "parameter order" => new FunctionType([Text, Integer], Flag, isPure: true),
            "a missing parameter" => new FunctionType([Integer], Flag, isPure: true),
            _ => new FunctionType([Integer, Text], Flag, isPure: true),
        };

        Assert.Equal(expected, Entailed(offered, new FunctionType([Integer, Text], Flag, isPure: true)));
    }

    [Theory]
    [InlineData("nothing", null)]
    [InlineData("element order", Refused)]
    [InlineData("a missing element", Refused)]
    [InlineData("an extra element", Refused)]
    public void PackIsTheRequiredPackWhenItsElementsAgreeInOrder(string difference, string? expected)
    {
        var offered = difference switch
        {
            "element order" => Pack(Flag, new TupleType([Integer, Text]), Integer),
            "a missing element" => Pack(Integer, new TupleType([Integer, Text])),
            "an extra element" => Pack(Integer, new TupleType([Integer, Text]), Flag, Flag),
            _ => Pack(Integer, new TupleType([Integer, Text]), Flag),
        };

        Assert.Equal(expected, Entailed(offered, Pack(Integer, new TupleType([Integer, Text]), Flag)));
    }

    [Theory]
    [InlineData("nothing", null)]
    [InlineData("value", Refused)]
    [InlineData("value type", Refused)]
    public void LiftedValueIsTheRequiredValueWhenTypeAndValueAgree(string difference, string? expected)
    {
        var offered = difference switch
        {
            "value" => new LiftedType(Integer, 5L),
            "value type" => new LiftedType(built.Type("Byte"), 4L),
            _ => new LiftedType(Integer, 4L),
        };

        Assert.Equal(expected, Entailed(offered, new LiftedType(Integer, 4L)));
    }

    [Theory]
    [InlineData("nothing", null)]
    [InlineData("base type", Refused)]
    [InlineData("declaration", Refused)]
    [InlineData("no refinement", Refused)]
    [InlineData("a refinement the requirement lacks", Refused)]
    public void RefinementIsTheRequiredRefinementWhenDeclarationAndBaseAgree(string difference, string? expected)
    {
        var positive = HandBuiltModel.Refined(new TypeParameter("T", 0, new Scope(null), null), "Positive");
        var impostor = HandBuiltModel.Refined(new TypeParameter("T", 0, new Scope(null), null), "Positive");
        IMistType required = new RefinedType(positive, Integer);
        IMistType offered = difference switch
        {
            "base type" => new RefinedType(positive, built.Type("Int64")),
            "declaration" => new RefinedType(impostor, Integer),
            "no refinement" => Integer,
            _ => new RefinedType(positive, Integer),
        };

        if(difference == "a refinement the requirement lacks")
        {
            required = Integer;
        }

        Assert.Equal(expected, Entailed(offered, required));
    }

    [Theory]
    [InlineData("nothing", null)]
    [InlineData("the pack spread", Refused)]
    [InlineData("no spread", Refused)]
    public void SpreadIsTheRequiredSpreadWhenItSpreadsTheSamePack(string difference, string? expected)
    {
        var pack = PackParameter("Ts");
        IMistType offered = difference switch
        {
            "the pack spread" => new SpreadType(PackParameter("Ts")),
            "no spread" => pack,
            _ => new SpreadType(pack),
        };

        Assert.Equal(expected, Entailed(offered, new SpreadType(pack)));
    }

    [Theory]
    [InlineData("nothing", null)]
    [InlineData("index", Refused)]
    [InlineData("pack", Refused)]
    public void PackElementIsTheRequiredElementWhenPackAndIndexAgree(string difference, string? expected)
    {
        var pack = PackParameter("Ts");
        var offered = difference switch
        {
            "index" => pack.ElementAt(1),
            "pack" => PackParameter("Ts").ElementAt(0),
            _ => pack.ElementAt(0),
        };

        Assert.Equal(expected, Entailed(offered, pack.ElementAt(0)));
    }

    [Theory]
    [InlineData("Int32", "Int32", null)]
    [InlineData("String", "Int32", Refused)]
    [InlineData(null, "Int32", Refused)]
    [InlineData("Int32", null, Refused)]
    public void NestedTypeIsTheRequiredNestedTypeWhenTheEnclosingConstructionsAgree(string? offered, string? required, string? expected)
    {
        var outer = Generic(HandBuiltModel.Class("Outer"));
        var inner = Generic(HandBuiltModel.Class("Inner"));

        ConstructedType nested(string? enclosingArgument)
        {
            return new ConstructedType(inner, [Flag], enclosingArgument is null ? null : new ConstructedType(outer, [built.Type(enclosingArgument)]));
        }

        Assert.Equal(expected, Entailed(nested(offered), nested(required)));
    }

    private string? Entailed(IMistType offered, IMistType required)
    {
        var parameter = new TypeParameter("T", 0, new Scope(null), null);
        var forwarded = new TypeParameter("U", 0, new Scope(null), null);
        forwarded.ConstraintsList.Add(new TypeConstraint(new ConstructedType(rank, [offered])));
        var closing = new TypeArgumentClosing(parameter, forwarded, HandBuiltModel.Node(), TypeSubstitution.Empty.With(parameter, forwarded));

        return ConstraintEntailment.Decide(closing, new TypeConstraint(new ConstructedType(rank, [required])), built.Interner)?.Number.Value;
    }

    private LiftedType Pack(params IMistType[] elements)
    {
        return new LiftedType(built.Type("Array"), new TypePackValue(elements));
    }

    private static TypeParameter PackParameter(string name)
    {
        return new TypeParameter(name, 0, new Scope(null), null) { PackKind = TypePackKind.Unbracketed, PackLength = 2 };
    }

    private static T Generic<T>(T definition)
        where T : MemberType
    {
        _ = DeclarationFragments.Parameter(definition, definition.Scope, "Item");
        return definition;
    }
}