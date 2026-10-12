using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "1")]
public sealed class ConstructedBaseSubstitutionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AConstructedBaseBindsEveryConstructedEnclosingLevel(bool hasOwnArguments)
    {
        var built = new HandBuiltModel();
        var outer = Generic("Outer<>", null, "T");
        var middle = Generic("Middle<>", outer, "U");
        var inner = Generic(hasOwnArguments ? "Inner<>" : "Inner", middle, hasOwnArguments ? ["V"] : []);
        var outerConstruction = new ConstructedType(outer, [built.Type("Int32")]);
        var middleConstruction = new ConstructedType(middle, [built.Type("String")], outerConstruction);
        var baseConstruction = new ConstructedType(inner, hasOwnArguments ? [built.Type("Boolean")] : [], middleConstruction);

        var substitution = TypeSubstitution.ForPath(receiver: null, [baseConstruction], interner: null);

        Assert.Same(built.Type("Int32"), substitution.Apply(outer.TypeParametersList[0], interner: null));
        Assert.Same(built.Type("String"), substitution.Apply(middle.TypeParametersList[0], interner: null));
        if(hasOwnArguments)
        {
            Assert.Same(built.Type("Boolean"), substitution.Apply(inner.TypeParametersList[0], interner: null));
        }
    }

    [Fact]
    public void ABaseClosesItsEnclosingAndOwnArgumentsThroughTheReceiver()
    {
        var built = new HandBuiltModel();
        var receiverDefinition = Generic("Derived<,>", null, "A", "B");
        var outer = Generic("Outer<>", null, "T");
        var inner = Generic("Inner<>", outer, "U");
        var receiver = new ConstructedType(receiverDefinition, [built.Type("Int32"), built.Type("String")]);
        var outerConstruction = new ConstructedType(outer, [receiverDefinition.TypeParametersList[1]]);
        var baseConstruction = new ConstructedType(inner, [receiverDefinition.TypeParametersList[0]], outerConstruction);

        var substitution = TypeSubstitution.ForPath(receiver, [baseConstruction], interner: null);

        Assert.Same(built.Type("String"), substitution.Apply(outer.TypeParametersList[0], interner: null));
        Assert.Same(built.Type("Int32"), substitution.Apply(inner.TypeParametersList[0], interner: null));
    }

    [Fact]
    public void ABaseClosesItsEnclosingArgumentsThroughEarlierBasesOnThePath()
    {
        var built = new HandBuiltModel();
        var bridge = Generic("Bridge<,>", null, "A", "B");
        var outer = Generic("Outer<>", null, "T");
        var inner = Generic("Inner<>", outer, "U");
        var bridgeConstruction = new ConstructedType(bridge, [built.Type("String"), built.Type("Int32")]);
        var outerConstruction = new ConstructedType(outer, [bridge.TypeParametersList[0]]);
        var baseConstruction = new ConstructedType(inner, [bridge.TypeParametersList[1]], outerConstruction);

        var substitution = TypeSubstitution.ForPath(receiver: null, [bridgeConstruction, baseConstruction], interner: null);

        Assert.Same(built.Type("String"), substitution.Apply(outer.TypeParametersList[0], interner: null));
        Assert.Same(built.Type("Int32"), substitution.Apply(inner.TypeParametersList[0], interner: null));
    }

    private static ClassType Generic(string name, MistType? declaring, params string[] parameterNames)
    {
        var type = new ClassType(name, "Probe", declaring?.Scope ?? new Scope(null), declaring, null);
        for(var i = 0; i < parameterNames.Length; i++)
        {
            type.TypeParametersList.Add(new TypeParameter(parameterNames[i], i, type.Scope, type));
        }

        return type;
    }
}