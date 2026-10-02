using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

// Tier 1: ParamsCollection over directly built types. A collection class is written by what it declares:
// `bag` conforms to IEnumerable<Int32> and by default has what C# asks of one, an implicit constructor
// and an Add of its element, each option after it taking one of those away or changing it; `List` is a
// generic one closed at the element written.
[Trait("Tier", "1")]
public sealed class ParamsCollectionTests
{
    private readonly HandBuiltModel built = new();
    private readonly TypeParameter plain = new("T", 0, new Scope(null), null);

    [Theory]
    [InlineData("Int32[]")]
    [InlineData("Int32[]?")]
    [InlineData("Span<Int32>")]
    [InlineData("ReadOnlySpan<Int32>")]
    [InlineData("IEnumerable<Int32>")]
    [InlineData("IReadOnlyCollection<Int32>")]
    [InlineData("IReadOnlyList<Int32>")]
    [InlineData("ICollection<Int32>")]
    [InlineData("IList<Int32>")]
    [InlineData("List<Int32>")]
    [InlineData("List<Int32>?")]
    [InlineData("alias List<Int32>")]
    [InlineData("bag")]
    [InlineData("bag ctor:none")]
    [InlineData("bag add:Int64")]
    [InlineData("bag struct ctor:Int32")]
    public void ACollectionCSharpGathersAFlatListInto_YieldsItsElement(string shape)
    {
        Assert.Same(built.Type("Int32"), ParamsCollection.ElementOf(Type(shape)));
    }

    [Fact]
    public void AGenericCollection_YieldsTheElementItIsClosedWith()
    {
        Assert.Same(plain, ParamsCollection.ElementOf(Type("List<T>")));
    }

    [Theory]
    [InlineData("Int32")]
    [InlineData("T")]
    [InlineData("String")]
    [InlineData("(Int32, Int32)")]
    [InlineData("error")]
    [InlineData("ISet<Int32>")]
    [InlineData("bag add:none")]
    [InlineData("bag add:String")]
    [InlineData("bag add:static")]
    [InlineData("bag add:two")]
    [InlineData("bag add:interface")]
    [InlineData("bag ctor:Int32")]
    public void AnythingElse_YieldsNoElement(string shape)
    {
        Assert.Null(ParamsCollection.ElementOf(Type(shape)));
    }

    [Theory]
    [InlineData("Int32[]", true)]
    [InlineData("List<Int32>", true)]
    [InlineData("bag add:none", true)]
    [InlineData("Int32", false)]
    [InlineData("String", false)]
    [InlineData("(Int32, Int32)", false)]
    [InlineData("Int32[]?", false)]
    public void ALoneArgumentIsHandedWhole_WhenItIsItselfASequence(string shape, bool whole)
    {
        Assert.Equal(whole, ParamsCollection.IsHandedWhole(Type(shape)));
    }

    private IMistType Type(string shape)
    {
        var integer = built.Type("Int32");

        if(shape.EndsWith('?'))
        {
            return built.Interner.GetOrCreateUnionType([Type(shape[..^1]), built.Type("Void")]);
        }

        if(shape.StartsWith("alias ", StringComparison.Ordinal))
        {
            return new TypeAlias("Named", "Probe", new Scope(null), null, null!) { AliasedType = Type(shape[6..]) };
        }

        if(shape.StartsWith("bag", StringComparison.Ordinal))
        {
            return Bag(shape);
        }

        return shape switch
        {
            "Int32" => integer,
            "T" => plain,
            "String" => built.Type("String"),
            "error" => ErrorType.Instance,
            "(Int32, Int32)" => built.Interner.GetOrCreateTupleType([integer, integer]),
            "Int32[]" => LengthIndexed("Array<,>", integer),
            "Span<Int32>" => LengthIndexed("Span<,>", integer),
            "ReadOnlySpan<Int32>" => LengthIndexed("ReadOnlySpan<,>", integer),
            "List<Int32>" => built.Interner.GetOrCreateConstructedType(List(), [integer]),
            "List<T>" => built.Interner.GetOrCreateConstructedType(List(), [plain]),
            _ => built.Interner.GetOrCreateConstructedType(Interface(shape[..shape.IndexOf('<')]), [integer]),
        };
    }

    private IMistType LengthIndexed(string definition, IMistType element)
    {
        return built.Interner.GetOrCreateConstructedType(
            built.Type(definition), [element, built.Interner.GetOrCreateUnknownValue(built.Type("Int32"))]);
    }

    private static AbstractType Interface(string name)
    {
        return new AbstractType($"{name}<>", name == "IEnumerable" ? "Standard" : "System.Collections.Generic", new Scope(null), null, null);
    }

    private ClassType List()
    {
        var list = new ClassType("List<>", "Standard", new Scope(null), null, null);
        var element = new TypeParameter("T", 0, list.Scope, list);
        list.TypeParametersList.Add(element);
        list.BaseAbstractTypes.Add(built.Interner.GetOrCreateConstructedType(Interface("IEnumerable"), [element]));
        list.MethodsList.Add(Add(list, element));

        return list;
    }

    private ConcreteType Bag(string shape)
    {
        var integer = built.Type("Int32");
        var options = shape.Split(' ').Skip(1).ToList();
        ConcreteType bag = options.Contains("struct") ? HandBuiltModel.Struct("Bag") : HandBuiltModel.Class("Bag");
        var enumerable = built.Interner.GetOrCreateConstructedType(Interface("IEnumerable"), [integer]);
        bag.BaseAbstractTypes.Add(enumerable);

        var ctor = options.Find(option => option.StartsWith("ctor:", StringComparison.Ordinal))?[5..];
        var add = options.Find(option => option.StartsWith("add:", StringComparison.Ordinal))?[4..];

        if(ctor is not null)
        {
            bag.ConstructorsList.Add(new Constructor("Bag", bag, ctor == "none" ? [] : [new Parameter("capacity", built.Type(ctor))], default));
        }

        switch(add)
        {
            case null:
                bag.MethodsList.Add(Add(bag, integer));
                break;
            case "none":
                break;
            case "static":
                bag.MethodsList.Add(Add(bag, integer, isStatic: true));
                break;
            case "two":
                bag.MethodsList.Add(new Method("Add", built.Type("Void"), [new Parameter("key", integer), new Parameter("value", integer)], default) { DeclaringType = bag });
                break;
            case "interface":
                ((AbstractType)enumerable.Definition).MethodsList.Add(Add((MemberType)enumerable.Definition, integer));
                break;
            default:
                bag.MethodsList.Add(Add(bag, built.Type(add)));
                break;
        }

        return bag;
    }

    private Method Add(MemberType owner, IMistType item, bool isStatic = false)
    {
        return new Method("Add", built.Type("Void"), [new Parameter("item", item)], new MemberModifiers { IsStatic = isStatic, Accessibility = Accessibility.Public })
        {
            DeclaringType = owner,
        };
    }
}