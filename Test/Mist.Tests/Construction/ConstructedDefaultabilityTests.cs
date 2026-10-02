using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using static Mist.Tests.Helpers.GenericDefaultShapes;

namespace Mist.Tests.Construction;

[Trait("Tier", "1")]
public sealed class ConstructedDefaultabilityTests
{
    private readonly GenericDefaultShapes shapes = new();

    [Theory]
    [InlineData("intrinsic", true)]
    [InlineData("constructorless-class", true)]
    [InlineData("default-constrained-parameter", true)]
    [InlineData("connection", false)]
    [InlineData("abstract", false)]
    [InlineData("construction-of-an-intrinsic", false)]
    [InlineData("unconstrained-parameter", false)]
    public void Construction_HasTheDefaultItsDefinitionHasUnderItsArgument(string argument, bool expected)
    {
        var box = shapes.Box();

        Assert.Equal(expected, Defaultability.HasDefault(Construct(box, Argument(argument))));
    }

    [Fact]
    public void OpenDefinition_IsDefaultableForSomeArgumentAndJudgedPerInstantiation()
    {
        var box = shapes.Box();

        Assert.True(Defaultability.IsDefaultable(box));
        Assert.True(Defaultability.HasDefault(Construct(box, shapes.Integer)));
        Assert.False(Defaultability.HasDefault(Construct(box, shapes.Connection())));
    }

    [Fact]
    public void NestedConstructionOfOneDefinition_IsNotACycle()
    {
        var box = shapes.Box();

        Assert.True(Defaultability.HasDefault(Construct(box, Construct(box, shapes.Inner()))));
    }

    [Theory]
    [InlineData("intrinsic", true)]
    [InlineData("connection", false)]
    [InlineData("holder-parameter", true)]
    public void SlotOfAConstruction_IsAnsweredUnderItsArgumentAndLeftOpenOverTheHoldersParameter(string argument, bool answered)
    {
        var box = shapes.Box();
        var holder = shapes.Type("Holder");
        var filled = argument == "holder-parameter" ? Parameter(holder, "U") : Argument(argument);
        var slot = Slot(holder, "Held", Construct(box, filled));

        Assert.Equal(answered, Defaultability.StorageHasDefault(holder, Assert.Single(InstanceStorage.Of(holder))));
        Assert.Equal(answered ? [] : [slot], Defaultability.StorageWithoutDefault(holder).Select(storage => storage.Field));
    }

    [Theory]
    [InlineData("constructorless", "intrinsic", false, true)]
    [InlineData("constructor-and-a-block", "intrinsic", false, true)]
    [InlineData("constructor-and-a-block", "intrinsic", true, true)]
    [InlineData("constructor", "intrinsic", false, false)]
    [InlineData("constructorless", "connection", false, false)]
    [InlineData("constructor-and-a-block", "connection", true, false)]
    public void ConstructedBase_IsJudgedUnderItsArgumentsAsAMistBaseIs(string shape, string argument, bool isStruct, bool defaultable)
    {
        var baseType = Construct(shapes.Box(shape, isStruct), Argument(argument));
        var derived = shapes.Type("IntBox", isStruct, baseType);

        Assert.Equal(defaultable, Defaultability.IsDefaultable(derived));
        Assert.Equal(defaultable ? null : baseType, Defaultability.BaseWithoutDefault(derived));
    }

    [Fact]
    public void ConstructedIntrinsicBase_LeavesTheDerivedTypeNonDefaultableWithoutNamingTheBase()
    {
        var derived = shapes.Type("Bag", baseType: (ConstructedType)Argument("construction-of-an-intrinsic"));

        Assert.False(Defaultability.IsDefaultable(derived));
        Assert.Null(Defaultability.BaseWithoutDefault(derived));
    }

    [Theory]
    [InlineData("itself", false)]
    [InlineData("itself-over-a-growing-argument", false)]
    [InlineData("itself-through-an-argument-its-holder-never-zeroes", true)]
    public void DeclarationReachingItsOwnDefinition_HasNoDefaultForAnyArgument(string shape, bool defaultable)
    {
        var node = shapes.Type("Node");
        var parameter = Parameter(node);
        IMistType next = shape switch
        {
            "itself" => Construct(node, parameter),
            "itself-over-a-growing-argument" => Construct(node, Construct(shapes.Box(), parameter)),
            _ => Construct(Tag(), Construct(node, parameter)),
        };
        _ = Slot(node, "Next", next);

        Assert.Equal(defaultable, Defaultability.IsDefaultable(node));
        Assert.Equal(defaultable, Defaultability.HasDefault(Construct(node, shapes.Integer)));
    }

    [Theory]
    [InlineData("slot", "T")]
    [InlineData("initialized-slot", "")]
    [InlineData("through-a-construction", "T")]
    [InlineData("through-an-argument-never-zeroed", "")]
    [InlineData("through-a-base", "T")]
    public void ZeroNeeded_NamesTheParametersWhoseZeroTheDefaultReads(string shape, string expected)
    {
        var box = shapes.Box();
        var holder = shapes.Type("Holder");
        var parameter = Parameter(holder);

        switch(shape)
        {
            case "slot":
                _ = Slot(holder, "Held", parameter);
                break;
            case "initialized-slot":
                _ = Slot(holder, "Held", parameter, HandBuiltModel.Literal(parameter));
                break;
            case "through-a-construction":
                _ = Slot(holder, "Held", Construct(box, parameter));
                break;
            case "through-an-argument-never-zeroed":
                _ = Slot(holder, "Held", Construct(Tag(), parameter));
                break;
            default:
                holder.BaseType = Construct(box, parameter);
                break;
        }

        Assert.Equal(expected, string.Concat(Defaultability.ZeroNeeded(holder).Select(needed => needed.Name)));
    }

    [Fact]
    public void AnsweredThrough_NamesTheDefinitionOfAConstruction()
    {
        var box = shapes.Box();
        var holder = shapes.Type("Holder");
        _ = Slot(holder, "Held", Construct(box, shapes.Connection()));

        Assert.Same(box, Defaultability.AnsweredThrough(holder, Assert.Single(InstanceStorage.Of(holder))));
    }

    private IMistType Argument(string kind)
    {
        return kind switch
        {
            "intrinsic" => shapes.Integer,
            "constructorless-class" => shapes.Inner(),
            "connection" => shapes.Connection(),
            "abstract" => new AbstractType("IShape", "Probe", new Scope(null), null, null),
            "construction-of-an-intrinsic" => Construct(IntrinsicList(), shapes.Integer),
            "default-constrained-parameter" => Parameter(shapes.Type("Maker"), defaultConstrained: true),
            _ => Parameter(shapes.Type("Maker")),
        };
    }

    private ConcreteType IntrinsicList()
    {
        var list = shapes.Type("List", isIntrinsic: true);
        _ = Parameter(list);
        return list;
    }

    private ConcreteType Tag()
    {
        var tag = shapes.Type("Tag");
        _ = Parameter(tag);
        _ = Slot(tag, "Count", shapes.Integer);
        return tag;
    }
}