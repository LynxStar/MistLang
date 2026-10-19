using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "1")]
public sealed class VariadicMethodGroupClosingTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public void TargetClosesFlattenedPackAcrossItsParameterPositions(int width, bool stated)
    {
        var built = new HandBuiltModel();
        var method = Describe(built);
        var inputs = new IMistType[] { built.Type("Int32"), built.Type("String") }.Take(width).ToArray();
        var group = Group(method, stated ? [built.Interner.LiftPack(method.TypeParametersList[0], new TypePackValue(inputs))] : null);

        var converted = Assert.IsType<FunctionReferenceExpression>(MethodGroupConversion.Convert(
            group, new FunctionType(inputs, built.Type("Int32")), built.Interner));

        Assert.Same(method, converted.Method);
        var pack = Assert.IsType<TypePackValue>(Assert.IsType<LiftedType>(Assert.Single(converted.TypeArguments)).Value);
        Assert.Equal(inputs, pack.Elements);
    }

    [Fact]
    public void FixedEndsBindInwardAndContradictoryEndsRefuse()
    {
        var built = new HandBuiltModel();
        var method = Describe(built);
        method.ParametersList.Insert(0, new Parameter("first", built.Type("Boolean")));
        method.ParametersList.Add(new Parameter("last", built.Type("String")));
        var group = Group(method);
        var target = new FunctionType([built.Type("Boolean"), built.Type("Int32"), built.Type("String")], built.Type("Int32"));

        _ = Assert.IsType<FunctionReferenceExpression>(MethodGroupConversion.Convert(group, target, built.Interner));
        Assert.Null(MethodGroupConversion.Convert(group,
            new FunctionType([built.Type("Int32"), built.Type("String")], built.Type("Int32")), built.Interner));
        Assert.Null(MethodGroupConversion.Convert(group, new FunctionType([], built.Type("Int32")), built.Interner));
    }

    [Fact]
    public void ResultInferenceUsesTheClosedPackAndAmbiguousGroupsProduceNothing()
    {
        var built = new HandBuiltModel();
        var method = Describe(built);
        Assert.Same(built.Type("Int32"), MethodGroupConversion.ResultType(Group(method), [built.Type("String")], built.Interner));
        var ambiguous = new MethodGroupExpression(HandBuiltModel.Node(), null, "Describe", [method, Describe(built)], null);
        Assert.Null(MethodGroupConversion.Convert(ambiguous,
            new FunctionType([built.Type("String")], built.Type("Int32")), built.Interner, out var isAmbiguous));
        Assert.True(isAmbiguous);
    }

    internal static Method Describe(HandBuiltModel built, MemberType? owner = null)
    {
        var pack = new TypeParameter("Ts", 0, new Scope(null), null) { PackKind = TypePackKind.Unbracketed };
        var method = new Method("Describe", built.Type("Int32"),
            [new Parameter("args", new TupleType([new SpreadType(pack)])) { IsFlattened = true }],
            new MemberModifiers { IsStatic = true })
        { DeclaringType = owner };
        method.TypeParametersList.Add(pack);
        return method;
    }

    internal static MethodGroupExpression Group(Method method, IReadOnlyList<IMistType>? stated = null)
    {
        return new MethodGroupExpression(HandBuiltModel.Node(30), null, method.Name, [method], null) { StatedTypeArguments = stated };
    }
}