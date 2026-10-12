using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

// A signature is written as its parameter types, `params` on a collection taking a flat list and `=` after
// one the CLR lets a call omit; a call as its argument types.
[Trait("Tier", "1")]
public sealed class NativeCallFormTests
{
    private readonly HandBuiltModel built = new();
    private readonly AbstractType enumerable = new("IEnumerable<>", "Standard", new Scope(null), null, null);
    private readonly ClassType list;

    public NativeCallFormTests()
    {
        list = new ClassType("List<>", "Standard", new Scope(null), null, null);
        var element = new TypeParameter("T", 0, list.Scope, list);
        list.TypeParametersList.Add(element);
        list.BaseAbstractTypes.Add(built.Interner.GetOrCreateConstructedType(enumerable, [element]));
        list.MethodsList.Add(new Method("Add", built.Type("Void"), [new Parameter("item", element)], default) { DeclaringType = list });
    }

    [Theory]
    [InlineData("Int32, String=", 1, true)]
    [InlineData("Int32, String=", 2, true)]
    [InlineData("Int32, String=", 0, false)]
    [InlineData("Int32, String=", 3, false)]
    [InlineData("Int32, String=, Boolean=", 1, true)]
    [InlineData("Int32=, String=", 0, true)]
    [InlineData("Int32=, String", 1, false)]
    [InlineData("params Int32[]", 0, true)]
    [InlineData("params Int32[]", 5, true)]
    [InlineData("String, params Int32[]", 0, false)]
    [InlineData("String=, params Int32[]", 0, true)]
    [InlineData("String, Int32=, params Int32[]", 1, true)]
    [InlineData("String, Int32=, params Int32[]", 4, true)]
    public void AnArgumentCount_IsAcceptedWhereOnlyNativeDefaultsOrTheCollectionGoUnfilled(string signature, int count, bool accepted)
    {
        Assert.Equal(accepted, ValuePack.AcceptsCount(Signature(signature), count));
    }

    [Theory]
    [InlineData("Int32, String=, Boolean=", 1, 2)]
    [InlineData("Int32, String=, Boolean=", 3, 0)]
    [InlineData("String, Int32=, params Int32[]", 1, 1)]
    [InlineData("String, Int32=, params Int32[]", 2, 0)]
    [InlineData("String, Int32=, params Int32[]", 5, 0)]
    public void TheParametersLeftToTheirDefaults_AreCountedBeforeTheCollection(string signature, int count, int omitted)
    {
        Assert.Equal(omitted, ValuePack.OmittedDefaults(Signature(signature), count));
    }

    [Theory]
    [InlineData("params Int32[]", "Int32", true, 1)]
    [InlineData("params Int32[]", "Int32[]", false, 1)]
    [InlineData("params Int32[]", "", true, 0)]
    [InlineData("params Int32[]", "Int32, Int32", true, 2)]
    [InlineData("String, params Int32[]", "String, Int32, Int32", true, 2)]
    [InlineData("Int32, Int32", "Int32, Int32", false, 0)]
    public void ACollectionTakesItsArgumentsExpandedUnlessOneAlreadyIsTheCollection(
        string signature, string arguments, bool expanded, int collected)
    {
        var form = CallForm.Of(Signature(signature), Arguments(arguments));

        Assert.Equal((expanded, collected), (form.Expanded, form.Collected));
    }

    [Theory]
    [InlineData("Int32, Int32", "params Int32[]", "Int32, Int32", -1)]
    [InlineData("params Int32[]", "Int32", "Int32", 1)]
    [InlineData("Int32, params Int32[]", "params Int32[]", "Int32, Int32, Int32", -1)]
    [InlineData("params ReadOnlySpan<Int32>", "params Int32[]", "Int32, Int32", -1)]
    [InlineData("params ReadOnlySpan<Int32>", "params Span<Int32>", "Int32", -1)]
    [InlineData("params Span<Int32>", "params IEnumerable<Int32>", "Int32", -1)]
    [InlineData("params List<Int32>", "params IEnumerable<Int32>", "Int32", -1)]
    [InlineData("params Int32[]", "params List<Int32>", "Int32", 0)]
    [InlineData("params ReadOnlySpan<Int32>", "params List<Int32>", "Int32", 0)]
    [InlineData("Int32", "Int32, String=", "Int32", -1)]
    [InlineData("Int32, String=", "Int32, Boolean=", "Int32", 0)]
    [InlineData("Int32, String", "Int32, String", "Int32, String", 0)]
    public void TwoCandidatesTheArgumentsCannotSeparate_AreOrderedAsCSharpOrdersTheEmittedCall(
        string left, string right, string arguments, int expected)
    {
        var call = Arguments(arguments);
        var forward = CallForm.Prefer(CallForm.Of(Signature(left), call), CallForm.Of(Signature(right), call));
        var backward = CallForm.Prefer(CallForm.Of(Signature(right), call), CallForm.Of(Signature(left), call));

        Assert.Equal((expected, -expected), (forward, backward));
    }

    private List<Parameter> Signature(string signature)
    {
        return [.. Split(signature).Select((written, i) =>
        {
            var isParams = written.StartsWith("params ", StringComparison.Ordinal);
            var hasDefault = written.EndsWith('=');

            return new Parameter($"p{i}", Type(isParams ? written[7..] : hasDefault ? written[..^1] : written))
            {
                IsFlattened = isParams,
                HasNativeDefault = hasDefault,
            };
        })];
    }

    private List<Expression> Arguments(string arguments)
    {
        return [.. Split(arguments).Select(written => (Expression)HandBuiltModel.Literal(Type(written)))];
    }

    private IMistType Type(string written)
    {
        if(written.EndsWith("[]", StringComparison.Ordinal))
        {
            return Constructed(built.Type("Array<,>"), Type(written[..^2]), sized: true);
        }

        if(written.IndexOf('<') is var open and > 0)
        {
            var element = Type(written[(open + 1)..^1]);

            return written[..open] switch
            {
                "List" => Constructed(list, element, sized: false),
                "IEnumerable" => Constructed(enumerable, element, sized: false),
                var span => Constructed(built.Type($"{span}<,>"), element, sized: true),
            };
        }

        return built.Type(written);
    }

    private ConstructedType Constructed(MistType definition, IMistType element, bool sized)
    {
        return built.Interner.GetOrCreateConstructedType(
            definition, sized ? [element, built.Interner.GetOrCreateUnknownValue(built.Type("Int32"))] : [element]);
    }

    private static string[] Split(string list)
    {
        return list.Length == 0 ? [] : list.Split(", ");
    }
}