using Mist.Generated;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Construction;

[Trait("Tier", "1")]
public sealed class DefaultabilityTests
{
    private static readonly MemberModifiers Public = new() { Accessibility = Accessibility.Public };

    private readonly ClassType integer = new("Int32", "Standard", new Scope(null), null, null)
    {
        Modifiers = Public with { IsIntrinsic = true },
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ParameterlessConstructor_LeavesATypeWithoutABlockNonDefaultable(bool isStruct)
    {
        var counter = Counter(isStruct);
        _ = Declare(counter);

        Assert.False(Defaultability.IsDefaultable(counter));
        Assert.False(Defaultability.HasDefault(counter));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ArgumentTakingConstructor_LeavesATypeWithoutABlockNonDefaultable(bool isStruct)
    {
        var counter = Counter(isStruct);
        _ = Declare(counter, integer);

        Assert.False(Defaultability.HasDefault(counter));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void DefaultBlock_MakesATypeWithAConstructorDefaultable(bool isStruct, bool takesArgument)
    {
        var counter = Counter(isStruct);
        _ = takesArgument ? Declare(counter, integer) : Declare(counter);
        AddDefaultBlock(counter);

        Assert.True(Defaultability.HasDefault(counter));
    }

    [Fact]
    public void DefaultBlock_LeavesAFieldWithNoAnswerUnanswered()
    {
        var counter = Counter(isStruct: false);
        _ = Declare(counter);
        AddDefaultBlock(counter);
        counter.FieldsList.Add(FieldOf("shape", new AbstractType("IShape", "Probe", new Scope(null), null, null)));

        Assert.False(Defaultability.HasDefault(counter));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConstructorlessType_InfersItsDefault(bool isStruct)
    {
        Assert.True(Defaultability.HasDefault(Counter(isStruct)));
    }

    [Fact]
    public void StaticConstructor_LeavesTheImplicitOneStanding()
    {
        var counter = Counter(isStruct: false);
        counter.ConstructorsList.Add(new Constructor("Counter", counter, [], Public with { IsStatic = true }));

        Assert.True(Defaultability.HasDefault(counter));
    }

    [Theory]
    [InlineData("constructorless", true)]
    [InlineData("parameterless-constructor-and-a-block", true)]
    [InlineData("parameterless-constructor", false)]
    public void ConstructorlessDerivedType_DefaultsOnlyOverADefaultableBase(string parent, bool expected)
    {
        var counter = Counter(isStruct: false);

        if(parent != "constructorless")
        {
            _ = Declare(counter);
        }

        if(parent == "parameterless-constructor-and-a-block")
        {
            AddDefaultBlock(counter);
        }

        var derived = HandBuiltModel.Class("Derived", counter);
        derived.FieldsList.Add(FieldOf("extra", integer));

        Assert.Equal(expected, Defaultability.HasDefault(derived));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FieldOfATypeWithAParameterlessConstructor_IsAnsweredOnlyByItsInitializer(bool initialized)
    {
        var counter = Counter(isStruct: false);
        var constructor = Declare(counter);
        var holder = HandBuiltModel.Class("Holder");
        var field = FieldOf("counter", counter,
            initialized ? new NewExpression(HandBuiltModel.Node(), counter, [], constructor, [], []) : null);
        holder.FieldsList.Add(field);

        Assert.Equal(initialized, Defaultability.FieldHasDefault(holder, field));
        Assert.Equal(initialized, Defaultability.HasDefault(holder));
    }

    [Fact]
    public void FieldOfAConstructorlessType_IsAnswerEnough()
    {
        var outer = HandBuiltModel.Class("Outer");
        outer.FieldsList.Add(FieldOf("child", Counter(isStruct: false)));

        Assert.True(Defaultability.HasDefault(outer));
    }

    [Fact]
    public void FieldHoldingItsOwnType_LeavesTheTypeNonDefaultable()
    {
        var node = HandBuiltModel.Class("Node");
        node.FieldsList.Add(FieldOf("value", integer));
        node.FieldsList.Add(FieldOf("next", node));

        Assert.False(Defaultability.HasDefault(node));
    }

    private ConcreteType Counter(bool isStruct)
    {
        ConcreteType counter = isStruct ? HandBuiltModel.Struct("Counter") : HandBuiltModel.Class("Counter");
        counter.FieldsList.Add(FieldOf("start", integer, HandBuiltModel.Literal(integer, 1L)));
        counter.FieldsList.Add(FieldOf("step", integer));
        return counter;
    }

    private static Constructor Declare(ConcreteType owner, params IMistType[] parameters)
    {
        var constructor = new Constructor(
            owner.Name, owner, parameters.Select((type, i) => new Parameter($"p{i}", type)).ToList(), Public);
        owner.ConstructorsList.Add(constructor);
        return constructor;
    }

    private void AddDefaultBlock(ConcreteType owner)
    {
        owner.DefaultBlock = new DefaultBlock(
            [new DefaultValueEntry("step", HandBuiltModel.Literal(integer, 2L))], new DefaultBlockDeclarationNode());
    }

    private static Field FieldOf(string name, IMistType type, Expression? initializer = null)
    {
        return new Field(name, type, OwnershipKind.Unrestricted, Public) { Initializer = initializer };
    }
}