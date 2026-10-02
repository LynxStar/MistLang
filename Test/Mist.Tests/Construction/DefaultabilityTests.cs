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
    [InlineData("constructorless", false, true)]
    [InlineData("parameterless-constructor-and-a-block", false, true)]
    [InlineData("argument-taking-constructor-and-a-block", false, true)]
    [InlineData("argument-taking-constructor-and-a-block", true, true)]
    [InlineData("parameterless-constructor", false, false)]
    [InlineData("argument-taking-constructor", true, false)]
    public void ConstructorlessDerivedType_DefaultsExactlyWhenItsBaseHasADefault(string parent, bool isStruct, bool expected)
    {
        ConcreteType derived = isStruct ? HandBuiltModel.Struct("Derived") : HandBuiltModel.Class("Derived");
        derived.BaseType = Shaped(parent, isStruct);
        derived.FieldsList.Add(FieldOf("extra", integer));

        Assert.Equal(expected, Defaultability.HasDefault(derived));
    }

    [Theory]
    [InlineData("parameterless-constructor", true)]
    [InlineData("argument-taking-constructor-and-a-block", false)]
    [InlineData("ambient", false)]
    [InlineData("intrinsic", false)]
    public void BaseWithoutDefault_NamesOnlyAMistBaseThatHasNone(string parent, bool named)
    {
        var shaped = Shaped(parent is "ambient" or "intrinsic" ? "parameterless-constructor" : parent, isStruct: false);

        if(parent == "ambient")
        {
            shaped.Assembly = MistAssembly.Ambient;
        }
        else if(parent == "intrinsic")
        {
            shaped.Modifiers = Public with { IsIntrinsic = true };
        }

        var derived = HandBuiltModel.Class("Derived", shaped);

        Assert.Equal(named ? shaped : null, Defaultability.BaseWithoutDefault(derived));
    }

    [Theory]
    [InlineData("type-with-a-constructor", true)]
    [InlineData("initialized", false)]
    [InlineData("type-parameter", false)]
    [InlineData("intrinsic", false)]
    public void FieldsWithoutDefault_NameOnlyAFieldWithNoAnswer(string shape, bool named)
    {
        var holder = HandBuiltModel.Class("Holder");
        var connection = Shaped("argument-taking-constructor", isStruct: false);
        var field = shape switch
        {
            "initialized" => FieldOf("link", connection, new NewExpression(HandBuiltModel.Node(), connection, [], null, [], [])),
            "type-parameter" => FieldOf("link", new TypeParameter("T", 0, holder.Scope, holder)),
            "intrinsic" => FieldOf("link", integer),
            _ => FieldOf("link", connection),
        };
        holder.FieldsList.Add(field);
        List<Field> expected = named ? [field] : [];

        Assert.Equal(expected, Defaultability.StorageWithoutDefault(holder).Select(slot => slot.Field));
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

        Assert.Equal(initialized, Defaultability.StorageHasDefault(holder, Assert.Single(InstanceStorage.Of(holder))));
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

    [Theory]
    [InlineData("auto", true)]
    [InlineData("get-only-auto", true)]
    [InlineData("init-only-auto", true)]
    [InlineData("auto-getter-bodied-setter", true)]
    [InlineData("bodied-accessors-naming-field", true)]
    [InlineData("bodied-getter-alone", false)]
    [InlineData("bodied-getter-alone-naming-field", false)]
    [InlineData("static-auto", false)]
    [InlineData("intrinsic-auto", false)]
    public void InstanceStorage_HoldsAPropertyExactlyWhenItHasABackingField(string shape, bool held)
    {
        var holder = HandBuiltModel.Class("Holder");
        var property = PropertyOf(shape, "Value", integer);
        holder.PropertiesList.Add(property);
        List<Property?> expected = held ? [property] : [];

        Assert.Equal(expected, InstanceStorage.Of(holder).Select(slot => slot.Property));
    }

    [Theory]
    [InlineData("defaultable", true)]
    [InlineData("initialized", true)]
    [InlineData("no-default", false)]
    [InlineData("static", true)]
    public void AutoProperty_AnswersForItsHolderAsAFieldWould(string shape, bool defaultable)
    {
        var holder = HandBuiltModel.Class("Holder");
        var connection = Shaped("argument-taking-constructor", isStruct: false);
        var property = shape switch
        {
            "defaultable" => PropertyOf("auto", "Link", Counter(isStruct: false)),
            "static" => PropertyOf("static-auto", "Link", connection),
            _ => PropertyOf("auto", "Link", connection),
        };
        property.Initializer = shape == "initialized" ? new NewExpression(HandBuiltModel.Node(), connection, [], null, [], []) : null;
        holder.PropertiesList.Add(property);
        List<Property?> unanswered = defaultable ? [] : [property];

        Assert.Equal(defaultable, Defaultability.HasDefault(holder));
        Assert.Equal(unanswered, Defaultability.StorageWithoutDefault(holder).Select(slot => slot.Property));
    }

    [Theory]
    [InlineData("intrinsic", true)]
    [InlineData("type-with-a-constructor", false)]
    public void RefinementSlot_StartsFromItsBasesDefaultWhateverItsPredicate(string beneath, bool answered)
    {
        IMistType baseType = beneath == "intrinsic" ? integer : Shaped("argument-taking-constructor", isStruct: false);
        var refined = HandBuiltModel.Refined(baseType, "Refined");
        var holder = HandBuiltModel.Class("Holder");
        holder.FieldsList.Add(FieldOf("held", refined));

        Assert.Equal(answered, Defaultability.StorageHasDefault(holder, Assert.Single(InstanceStorage.Of(holder))));
        Assert.Equal(answered, Defaultability.HasDefault(holder));
        Assert.False(Defaultability.HasDefault(refined));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EnumSlot_IsAnsweredByItsDesignatedVariant(bool designates)
    {
        var direction = HandBuiltModel.Enum("Direction", integer);
        direction.MembersList.Add(new EnumMember("North", designates, null));
        var holder = HandBuiltModel.Class("Holder");
        holder.FieldsList.Add(FieldOf("heading", direction));

        Assert.Equal(designates, Defaultability.HasDefault(holder));
    }

    [Theory]
    [InlineData("type-with-a-constructor", "Counter")]
    [InlineData("refinement-of-it", "Counter")]
    [InlineData("binding", "Square")]
    [InlineData("initialized", null)]
    [InlineData("intrinsic", "Int32")]
    public void AnsweredThrough_NamesTheTypeWhoseDefaultAnswersTheSlot(string shape, string? expected)
    {
        var holder = HandBuiltModel.Class("Holder");
        var connection = Shaped("argument-taking-constructor", isStruct: false);
        var field = shape switch
        {
            "refinement-of-it" => FieldOf("link", HandBuiltModel.Refined(connection, "Linked")),
            "initialized" => FieldOf("link", connection, new NewExpression(HandBuiltModel.Node(), connection, [], null, [], [])),
            "intrinsic" => FieldOf("link", integer),
            _ => FieldOf("link", connection),
        };
        holder.FieldsList.Add(field);

        if(shape == "binding")
        {
            holder.DefaultBlock = new DefaultBlock(
                [new DefaultBindingEntry("link", HandBuiltModel.Class("Square"), [], new DefaultTypeBindingDefaultFieldInitializerNode())],
                new DefaultBlockDeclarationNode());
        }

        Assert.Equal(expected, Defaultability.AnsweredThrough(holder, Assert.Single(InstanceStorage.Of(holder)))?.Name);
    }

    private ConcreteType Counter(bool isStruct)
    {
        ConcreteType counter = isStruct ? HandBuiltModel.Struct("Counter") : HandBuiltModel.Class("Counter");
        counter.FieldsList.Add(FieldOf("start", integer, HandBuiltModel.Literal(integer, 1L)));
        counter.FieldsList.Add(FieldOf("step", integer));
        return counter;
    }

    private ConcreteType Shaped(string shape, bool isStruct)
    {
        var shaped = Counter(isStruct);

        if(shape != "constructorless")
        {
            _ = shape.StartsWith("argument-taking", StringComparison.Ordinal) ? Declare(shaped, integer) : Declare(shaped);
        }

        if(shape.EndsWith("and-a-block", StringComparison.Ordinal))
        {
            AddDefaultBlock(shaped);
        }

        return shaped;
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

    private static Property PropertyOf(string shape, string name, IMistType type)
    {
        var auto = new PropertyAccessor { IsAuto = true };
        var bodied = new PropertyAccessor { Body = HandBuiltModel.Block() };
        var modifiers = shape switch
        {
            "static-auto" => Public with { IsStatic = true },
            "intrinsic-auto" => Public with { IsIntrinsic = true },
            _ => Public,
        };

        return new Property(name, type, modifiers)
        {
            Getter = shape.StartsWith("bodied", StringComparison.Ordinal) ? bodied : auto,
            Setter = shape switch
            {
                "get-only-auto" or "bodied-getter-alone" or "bodied-getter-alone-naming-field" => null,
                "init-only-auto" => new PropertyAccessor { IsAuto = true, IsInit = true },
                "auto-getter-bodied-setter" or "bodied-accessors-naming-field" => bodied,
                _ => auto,
            },
            UsesBackingField = shape.EndsWith("naming-field", StringComparison.Ordinal) || shape == "auto-getter-bodied-setter",
        };
    }
}