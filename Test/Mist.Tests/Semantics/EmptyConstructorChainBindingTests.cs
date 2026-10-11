using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class EmptyConstructorChainBindingTests
{
    [Theory]
    [InlineData(ConstructorChainKind.Base, Accessibility.Private)]
    [InlineData(ConstructorChainKind.Base, Accessibility.Protected)]
    [InlineData(ConstructorChainKind.Base, Accessibility.Public)]
    [InlineData(ConstructorChainKind.This, Accessibility.Private)]
    public void EmptyWrittenChainRetainsItsSelectedConstructorForAnalysis(ConstructorChainKind kind, Accessibility access)
    {
        var fragment = new BindingFragments();
        var owner = HandBuiltModel.Class("Derived");
        var targetOwner = kind == ConstructorChainKind.Base ? HandBuiltModel.Class("Base") : owner;
        owner.BaseType = kind == ConstructorChainKind.Base ? targetOwner : null;
        var target = new Constructor(targetOwner.Name, targetOwner, [], DeclarationFragments.Public with { Accessibility = access })
        {
            DeclaringType = targetOwner,
        };
        targetOwner.ConstructorsList.Add(target);
        var constructor = new Constructor(owner.Name, owner, [new Parameter("value", fragment.Built.Type("Int32"))], DeclarationFragments.Public)
        {
            DeclaringType = owner,
            Scope = fragment.Scope,
            Body = HandBuiltModel.Block(),
            ChainKind = kind,
            ChainNode = kind == ConstructorChainKind.This
                ? BindingFragments.At<ThisChainConstructorChainNode>(40)
                : BindingFragments.At<BaseChainConstructorChainNode>(40),
        };
        owner.ConstructorsList.Add(constructor);

        BodyBinder.BindDelegate(constructor, fragment.Builder);

        Assert.Same(target, constructor.ChainedConstructor);
        Assert.Empty(constructor.ChainArgumentsList);
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<IMistType>>(constructor.ChainParameterTypes));
        Assert.Empty(fragment.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void EmptyChainToAConstructorlessBaseHasNoSelectionOrFailure()
    {
        var fragment = new BindingFragments();
        var owner = HandBuiltModel.Class("Derived", HandBuiltModel.Class("Base"));
        var constructor = new Constructor(owner.Name, owner, [], DeclarationFragments.Public)
        {
            DeclaringType = owner,
            Scope = fragment.Scope,
            Body = HandBuiltModel.Block(),
            ChainKind = ConstructorChainKind.Base,
            ChainNode = BindingFragments.At<BaseChainConstructorChainNode>(40),
        };

        BodyBinder.BindDelegate(constructor, fragment.Builder);

        Assert.Null(constructor.ChainedConstructor);
        Assert.Null(constructor.ChainParameterTypes);
        Assert.Empty(constructor.ChainArgumentsList);
        Assert.Empty(fragment.Diagnostics.ToImmutableArray());
    }
}