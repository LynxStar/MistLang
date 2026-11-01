using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using CompilationUnit = Mist.ProgramModel.Model.CompilationUnit;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class TextCallDecisionTests
{
    private readonly HandBuiltModel built = new();

    [Theory]
    [InlineData("IndexOf", "char")]
    [InlineData("IndexOf", "char,start")]
    [InlineData("IndexOf", "char,comparison")]
    [InlineData("IndexOf", "string,comparison")]
    [InlineData("LastIndexOf", "char")]
    [InlineData("LastIndexOf", "string,comparison")]
    [InlineData("IndexOfAny", "chars")]
    [InlineData("IndexOfAny", "chars,start")]
    public void NullableSearchesAreSuppliedWithoutAClrReturnTypeMatch(string name, string shape)
    {
        var method = Search(name, shape);
        var standard = built.Model.AddStandardAssembly();
        var owner = Assert.IsType<ClassType>(method.DeclaringType);
        owner.Assembly = standard;
        owner.Modifiers = new MemberModifiers { IsIntrinsic = true };
        owner.MethodsList.Add(method);
        var unit = new CompilationUnit { SourcePath = "String.mist" };
        unit.Types.Add(owner);
        standard.CompilationUnits.Add(unit);

        var supply = Assert.Single(IntrinsicSupply.Audit(built.Model, _ => null));

        Assert.Equal(IntrinsicSupply.Kind.Respelled, supply.Kind);
        Assert.Same(method, supply.Member);
    }

    [Theory]
    [InlineData(false, "MistText.Index(Fresh().IndexOf(Next()))")]
    [InlineData(true, "MistText.Index(Fresh()?.IndexOf(Next()))")]
    public void ASearchEvaluatesItsReceiverAndArgumentsOnce(bool conditional, string expected)
    {
        var method = Search("IndexOf", "char");

        Assert.Equal(expected, TextCalls.Rewrite(method, "Fresh()", ["Next()"], conditional));
    }

    [Fact]
    public void SearchRuntimePreservesZeroAndConditionalAbsence()
    {
        TextCalls.Reset();
        _ = TextCalls.Rewrite(Search("IndexOf", "char"), "text", ["value"], false);
        var writer = new CSharpWriter();
        TextCalls.Emit(writer);

        Assert.Contains("Index(int? index)", writer.ToString());
        Assert.Contains("return index >= 0 ? index : null;", writer.ToString());
        Assert.DoesNotContain("Strings<T>", writer.ToString());
    }

    [Theory]
    [InlineData("return")]
    [InlineData("parameter")]
    [InlineData("owner")]
    [InlineData("intrinsic")]
    public void ASearchAdapterDoesNotClaimAnUnrecognizedContract(string changed)
    {
        var method = Search("IndexOf", "char");
        var invalid = new Method(method.Name,
            changed == "return" ? built.Type("Int32") : method.ReturnType,
            changed == "parameter" ? [new Parameter("value", built.Type("Boolean"))] : method.ParametersList,
            new MemberModifiers { IsIntrinsic = changed != "intrinsic" })
        {
            DeclaringType = changed == "owner" ? HandBuiltModel.Class("Other") : method.DeclaringType,
        };

        Assert.False(TextCalls.Respells(invalid));
    }

    [Theory]
    [InlineData("StartsWith")]
    [InlineData("EndsWith")]
    public void DefaultTextComparisonsNameInvariantCulture(string name)
    {
        var method = Method(name, built.Type("Boolean"), [built.Type("String")]);

        Assert.Equal($"text.{name}(value, System.StringComparison.InvariantCulture)", TextCalls.Rewrite(method, "text", ["value"], false));
    }

    [Fact]
    public void AnExplicitComparisonPassesThroughWithoutAnAdapter()
    {
        var method = Method("StartsWith", built.Type("Boolean"), [built.Type("String"), Ambient("StringComparison")]);

        Assert.False(TextCalls.Respells(method));
    }

    [Fact]
    public void RuntimeFormattingKeepsTheArgumentListAndDefaultsOnlyAnAbsentProvider()
    {
        var provider = new UnionType([Ambient("IFormatProvider"), built.Type("Void")]);
        var method = Method("Format", built.Type("String"), [provider, built.Type("String"), ArrayOf(UnknownType.Instance)], isStatic: true);

        Assert.True(IntrinsicCalls.Respells(method));
        Assert.Equal("string.Format(provider ?? System.Globalization.CultureInfo.InvariantCulture, template, first, second)",
            TextCalls.Rewrite(method, null, ["provider", "template", "first", "second"], false));
        Assert.Equal("string.Format(provider ?? System.Globalization.CultureInfo.InvariantCulture, template, args)",
            TextCalls.Rewrite(method, null, ["provider", "template", "args"], false));
    }

    [Theory]
    [InlineData("Join", "string.Join(separator, MistText.Strings(values))")]
    [InlineData("Concat", "string.Concat(MistText.Strings(values))")]
    public void GenericSequencesUseTheInvariantFormatter(string name, string expected)
    {
        TextCalls.Reset();
        InvariantCulture.Reset();
        var parameter = new TypeParameter("T", 0, new Scope(null), null);
        var sequence = new ConstructedType(built.Type("IEnumerable<>"), [parameter]);
        var method = Method(name, built.Type("String"), name == "Join" ? [built.Type("String"), sequence] : [sequence], isStatic: true);
        method.TypeParametersList.Add(parameter);
        var arguments = name == "Join" ? new[] { "separator", "values" } : ["values"];

        Assert.True(TextCalls.Respells(method));
        Assert.Equal(expected, TextCalls.Rewrite(method, null, arguments, false));
        var writer = new CSharpWriter();
        TextCalls.Emit(writer);
        Assert.Contains("value => MistInvariant.Text(value)", writer.ToString());
        Assert.True(InvariantCulture.Used());
        Assert.DoesNotContain("Index(int?", writer.ToString());
    }

    private Method Search(string name, string shape)
    {
        var types = shape.Split(',').Select<string, IMistType>(part => part switch
        {
            "char" => built.Type("Char"),
            "string" => built.Type("String"),
            "start" => built.Type("Int32"),
            "chars" => ArrayOf(built.Type("Char")),
            _ => Ambient("StringComparison"),
        }).ToArray();
        return Method(name, new UnionType([built.Type("Int32"), built.Type("Void")]), types);
    }

    private Method Method(string name, IMistType result, IReadOnlyList<IMistType> types, bool isStatic = false)
    {
        return new Method(name, result, types.Select((type, index) => new Parameter($"p{index}", type)).ToList(),
            new MemberModifiers { IsIntrinsic = true, IsStatic = isStatic })
        { DeclaringType = built.Type("String") };
    }

    private ConstructedType ArrayOf(IMistType element)
    {
        return new ConstructedType(built.Type("Array<,>"), [element, built.Type("Int32")]);
    }

    private static ClassType Ambient(string name)
    {
        return new ClassType(name, "System", new Scope(null), null, null);
    }
}