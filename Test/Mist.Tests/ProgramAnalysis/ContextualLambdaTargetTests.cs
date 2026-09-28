using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class ContextualLambdaTargetTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void PresentSignaturePreservesParameterResultAndPurity(bool optional, bool pure)
    {
        var built = new HandBuiltModel();
        var signature = new FunctionType([built.Type("String"), built.Type("Boolean")], built.Type("Int32"), pure);
        var target = optional ? new UnionType([built.Type("Void"), signature]) : (IMistType)signature;

        Assert.Same(signature, ContextualLambdaTarget.Of(target, built.Interner));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClrDelegateSignatureRemainsAvailable(bool optional)
    {
        var built = new HandBuiltModel();
        var multicast = new ClassType("MulticastDelegate", "System", new Scope(null), null, null);
        var callable = HandBuiltModel.Class("Transform", multicast);
        callable.MethodsList.Add(new Method("Invoke", built.Type("Boolean"),
            [new Parameter("text", built.Type("String"))], new MemberModifiers()));
        var target = optional ? new UnionType([callable, built.Type("Void")]) : (IMistType)callable;

        var signature = Assert.IsAssignableFrom<IFunctionType>(ContextualLambdaTarget.Of(target, built.Interner));

        Assert.Same(built.Type("String"), Assert.Single(signature.ParameterTypes));
        Assert.Same(built.Type("Boolean"), signature.ReturnType);
        Assert.False(signature.IsPure);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConstructedOptionalAliasExpandsBeforeSelectingTheSignature(bool pure)
    {
        var built = new HandBuiltModel();
        var signature = built.Interner.GetOrCreateFunctionType([built.Type("String")], built.Type("Boolean"), pure);
        var alias = new TypeAlias("Optional", "Probe", new Scope(null), null, new TypeAliasDeclarationNode());
        var parameter = new TypeParameter("T", 0, alias.Scope, null);
        alias.TypeParametersList.Add(parameter);
        alias.AliasedType = new UnionType([parameter, built.Type("Void")]);
        var target = built.Interner.GetOrCreateConstructedType(alias, [signature]);

        Assert.Same(signature, ContextualLambdaTarget.Of(target, built.Interner));
    }

    [Fact]
    public void CallableAliasInsideAnOptionalExpandsToItsPureSignature()
    {
        var built = new HandBuiltModel();
        var signature = built.Interner.GetOrCreateFunctionType([built.Type("String")], built.Type("String"), isPure: true);
        var alias = new TypeAlias("Transform", "Probe", new Scope(null), null, new TypeAliasDeclarationNode()) { AliasedType = signature };
        var present = built.Interner.GetOrCreateConstructedType(alias, []);

        Assert.Same(signature, ContextualLambdaTarget.Of(new UnionType([present, built.Type("Void")]), built.Interner));
    }

    [Theory]
    [InlineData("none")]
    [InlineData("error")]
    [InlineData("void")]
    [InlineData("noncallable-optional")]
    [InlineData("two-callables")]
    [InlineData("two-callables-and-void")]
    [InlineData("callable-noncallable-and-void")]
    public void NoncallableTargetsAndGeneralUnionsSupplyNoSignature(string shape)
    {
        var built = new HandBuiltModel();
        var first = new FunctionType([built.Type("String")], built.Type("String"));
        var second = new FunctionType([built.Type("Boolean")], built.Type("Boolean"));
        IMistType? target = shape switch
        {
            "none" => null,
            "error" => ErrorType.Instance,
            "void" => built.Type("Void"),
            "noncallable-optional" => new UnionType([built.Type("String"), built.Type("Void")]),
            "two-callables" => new UnionType([first, second]),
            "two-callables-and-void" => new UnionType([first, second, built.Type("Void")]),
            _ => new UnionType([first, built.Type("String"), built.Type("Void")]),
        };

        Assert.Null(ContextualLambdaTarget.Of(target, built.Interner));
    }

    [Fact]
    public void SelectingAContextualSignatureDoesNotMakeAnOptionalValueCallable()
    {
        var built = new HandBuiltModel();
        var signature = new FunctionType([built.Type("String")], built.Type("String"));
        var optional = new UnionType([signature, built.Type("Void")]);

        Assert.Same(signature, ContextualLambdaTarget.Of(optional, built.Interner));
        Assert.Null(DelegateShape.Of(optional));
    }
}