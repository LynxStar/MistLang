using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

// OverloadResolution.Select over hand-built candidates shaped like projected CLR members: trailing native
// defaults, `params` arrays and spans, by-ref twins and System.Object targets.
[Trait("Tier", "2")]
public sealed class NativeCallSelectionTests
{
    private readonly HandBuiltModel built = new();
    private readonly ClassType clrObject = new("Object", "System", new Scope(null), null, null);

    [Theory]
    [InlineData("Int32", "Serialize<T>(T, Options=)")]
    [InlineData("Int32, Options", "Serialize<T>(T, Options=)")]
    [InlineData("Int32, Info", "Serialize<T>(T, Info)")]
    [InlineData("Int32, Type", "Serialize(Object, Type, Options=)")]
    [InlineData("Int32, Type, Options", "Serialize(Object, Type, Options=)")]
    public void ACallOmittingTrailingDefaults_SelectsTheMemberThatSuppliesThem(string arguments, string expected)
    {
        var choice = Select(Serializers(), arguments);

        Assert.Equal(expected, Assert.IsType<Method>(choice.Selected).Name);
    }

    [Fact]
    public void AnInferredTypeArgument_ComesFromTheSuppliedArgumentsAlone()
    {
        var choice = Select(Serializers(), "Int32");
        var selected = Assert.IsType<Method>(choice.Selected);

        Assert.Same(built.Type("Int32"), choice.Substitution.Apply(selected.TypeParametersList[0], built.Interner));
    }

    [Fact]
    public void ACallMissingARequiredParameter_MatchesNoOverload()
    {
        var choice = Select(Serializers(), "");

        Assert.Null(choice.Selected);
        Assert.True(choice.NoArityMatch);
    }

    [Theory]
    [InlineData("String, String", "Combine(String, String)")]
    [InlineData("String, String, String, String, String", "Combine(params ReadOnlySpan<String>)")]
    [InlineData("String", "Combine(params ReadOnlySpan<String>)")]
    [InlineData("", "Combine(params ReadOnlySpan<String>)")]
    [InlineData("String[]", "Combine(params String[])")]
    public void AParamsCandidate_SelectsAsCSharpSelectsTheEmittedCall(string arguments, string expected)
    {
        Assert.Equal(expected, Assert.IsType<Method>(Select(Combiners(), arguments).Selected).Name);
    }

    [Theory]
    [InlineData("String, Int32")]
    [InlineData("String, String, Int32")]
    public void AnElementOfTheWrongType_MatchesNoOverload(string arguments)
    {
        var choice = Select(Combiners(), arguments);

        Assert.Null(choice.Selected);
        Assert.NotEmpty(choice.Unresolvable);
    }

    [Theory]
    [InlineData("Int32, Int32, Int32")]
    [InlineData("Int32[]")]
    public void AParamsArrayTeachesItsElementType(string arguments)
    {
        var element = new TypeParameter("T", 0, new Scope(null), null);
        var count = Method("Count", [new Parameter("xs", Array(element)) { IsFlattened = true }], element);

        var choice = Select([count], arguments);

        Assert.Same(count, choice.Selected);
        Assert.Same(built.Type("Int32"), choice.Substitution.Apply(element, built.Interner));
    }

    [Theory]
    [InlineData(true, "Take(ref Int32, String=)")]
    [InlineData(false, "Take(Int32, String=)")]
    public void APassingModeStillSeparatesTwinsThatOmitTheSameDefault(bool isRef, string expected)
    {
        var integer = built.Type("Int32");
        var byRef = new ParameterModifier(false, true, OwnershipKind.Unrestricted, ByRefDirection.Ref);
        var candidates = new List<Method>
        {
            Method("Take(ref Int32, String=)", [new Parameter("value", integer, byRef), Default("label", "String")]),
            Method("Take(Int32, String=)", [new Parameter("value", integer), Default("label", "String")]),
        };

        var choice = OverloadResolution.Select(candidates, Arguments("Int32"), null, null, built.Interner, argumentIsRef: [isRef]);

        Assert.Equal(expected, Assert.IsType<Method>(choice.Selected).Name);
    }

    [Fact]
    public void SupplyingEveryParameter_BeatsLeavingOneToItsDefault()
    {
        var candidates = new List<Method>
        {
            Method("Write(Int32, String=)", [new Parameter("value", built.Type("Int32")), Default("label", "String")]),
            Method("Write(Int32)", [new Parameter("value", built.Type("Int32"))]),
        };

        Assert.Equal("Write(Int32)", Assert.IsType<Method>(Select(candidates, "Int32").Selected).Name);
    }

    [Theory]
    [InlineData("String", "Write(String)")]
    [InlineData("Int32", "Write(Object)")]
    [InlineData("Probe", "Write(Object)")]
    public void AnObjectParameter_TakesWhatNoNarrowerCandidateTakes(string argument, string expected)
    {
        var candidates = new List<Method>
        {
            Method("Write(Object)", [new Parameter("value", clrObject)]),
            Method("Write(String)", [new Parameter("value", built.Type("String"))]),
        };

        Assert.Equal(expected, Assert.IsType<Method>(Select(candidates, argument).Selected).Name);
    }

    private OverloadChoice<Method> Select(List<Method> candidates, string arguments)
    {
        return OverloadResolution.Select(candidates, Arguments(arguments), null, null, built.Interner);
    }

    private List<Method> Serializers()
    {
        var generic = new TypeParameter("T", 0, new Scope(null), null);
        var other = new TypeParameter("T", 0, new Scope(null), null);
        var withOptions = Method("Serialize<T>(T, Options=)", [new Parameter("value", generic), Default("options", "Options")], generic);
        var withInfo = Method("Serialize<T>(T, Info)", [new Parameter("value", other), new Parameter("info", built.Type("Info"))], other);
        var untyped = Method(
            "Serialize(Object, Type, Options=)",
            [new Parameter("value", clrObject), new Parameter("type", built.Type("Type")), Default("options", "Options")]);

        return [withOptions, withInfo, untyped];
    }

    private List<Method> Combiners()
    {
        var text = built.Type("String");

        return
        [
            Method("Combine(String, String)", [new Parameter("first", text), new Parameter("second", text)]),
            Method("Combine(params String[])", [new Parameter("paths", Array(text)) { IsFlattened = true }]),
            Method("Combine(params ReadOnlySpan<String>)", [new Parameter("paths", Sized("ReadOnlySpan<,>", text)) { IsFlattened = true }]),
        ];
    }

    private Parameter Default(string name, string type)
    {
        return new Parameter(name, built.Type(type)) { HasNativeDefault = true };
    }

    private Method Method(string name, List<Parameter> parameters, TypeParameter? typeParameter = null)
    {
        var method = new Method(name, built.Type("Void"), parameters, new MemberModifiers { IsClrProjected = true });

        if(typeParameter is not null)
        {
            method.TypeParametersList.Add(typeParameter);
        }

        return method;
    }

    private List<Expression> Arguments(string arguments)
    {
        return arguments.Length == 0
            ? []
            : [.. arguments.Split(", ").Select(written => (Expression)HandBuiltModel.Literal(written switch
            {
                "Probe" => HandBuiltModel.Class("Probe"),
                _ when written.EndsWith("[]", StringComparison.Ordinal) => Array(built.Type(written[..^2])),
                _ => built.Type(written),
            }))];
    }

    private ConstructedType Array(IMistType element)
    {
        return Sized("Array<,>", element);
    }

    private ConstructedType Sized(string definition, IMistType element)
    {
        return built.Interner.GetOrCreateConstructedType(
            built.Type(definition), [element, built.Interner.GetOrCreateUnknownValue(built.Type("Int32"))]);
    }
}