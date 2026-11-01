using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class CapabilityAdapterTests
{
    private readonly HandBuiltModel built = new();

    [Fact]
    public void NullableParsingIsSuppliedByTheNonthrowingAdapter()
    {
        var number = built.Type("Int32");
        var method = Parse(number, Optional(number), Optional(built.Type("String")));
        Assert.True(CapabilityAdapters.Supplies(method));
        Assert.Equal("Mist.Backends.CSharpTranspiler.Runtime.NumericParsing.ParseInt32(input)",
            CapabilityAdapters.Rewrite(method, null, null, ["input"]));
    }

    [Fact]
    public void AParsingAdapterDoesNotClaimOtherSignatures()
    {
        var number = built.Type("Int32");
        Assert.False(CapabilityAdapters.Supplies(Parse(number, number, Optional(built.Type("String")))));
        Assert.False(CapabilityAdapters.Supplies(Parse(number, Optional(number), number)));
        Assert.False(CapabilityAdapters.Supplies(Parse(built.Type("Int64"), Optional(built.Type("Int64")), Optional(built.Type("String")))));
    }

    [Theory]
    [InlineData("IndexOf")]
    [InlineData("Contains")]
    [InlineData("Sort")]
    [InlineData("Find")]
    [InlineData("Any")]
    public void ArrayAdaptersSupplyTheDeclaredShapeAndRejectAnExtraArgument(string name)
    {
        var array = new ClassType("Array<,>", "Standard", null, null, null);
        var element = new TypeParameter("TValue", 0, new Scope(null), array);
        array.TypeParametersList.Add(element);
        var comparer = new AbstractType("IComparer<>", "System.Collections.Generic", null, null, null);
        var boolean = built.Type("Boolean");
        IMistType input = name switch
        {
            "Sort" => new ConstructedType(comparer, [element]),
            "Find" or "Any" => new FunctionType([element], boolean),
            _ => element,
        };
        IMistType result = name switch
        {
            "IndexOf" => Optional(built.Type("Int32")),
            "Find" => Optional(element),
            "Sort" => built.Type("Void"),
            _ => boolean,
        };
        var method = new Method(name, result, [new Parameter("input", input)],
            new MemberModifiers { IsIntrinsic = true })
        { DeclaringType = array };
        Assert.True(CapabilityAdapters.Supplies(method));
        method.ParametersList.Add(new Parameter("extra", input));
        Assert.False(CapabilityAdapters.Supplies(method));
    }

    [Fact]
    public void TheParameterlessArrayAnyRemainsSupplied()
    {
        var array = new ClassType("Array<,>", "Standard", null, null, null);
        var method = new Method("Any", built.Type("Boolean"), [], new MemberModifiers { IsIntrinsic = true })
        { DeclaringType = array };
        Assert.True(CapabilityAdapters.Supplies(method));
        var receiver = new ConstructedType(array, [built.Type("Int32")]);
        Assert.Equal("Mist.Backends.CSharpTranspiler.Runtime.ArrayOperations.Any(values)",
            CapabilityAdapters.Rewrite(method, "values", receiver, []));
    }

    private static Method Parse(ConcreteType owner, IMistType result, IMistType input)
    {
        return new Method("Parse", result, [new Parameter("input", input)],
            new MemberModifiers { IsStatic = true, IsIntrinsic = true })
        { DeclaringType = owner };
    }

    private UnionType Optional(IMistType type)
    {
        return new UnionType([type, built.Type("Void")]);
    }
}