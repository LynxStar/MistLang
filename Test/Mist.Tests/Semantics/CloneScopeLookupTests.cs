using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "1")]
public sealed class CloneScopeLookupTests
{
    [Fact]
    public void ANonGenericSourcesOwnNameAnswersWithTheClone()
    {
        var source = HandBuiltModel.Class("Tiger");
        var clone = HandBuiltModel.Class("Jaguar");
        var scope = new CloneScope(Declaring(("Tiger", source)), source, new Scope(null)) { Itself = clone };

        Assert.Same(clone, Find(scope, "Tiger"));
    }

    [Fact]
    public void AGenericSourcesOwnNameIsLeftToTheScopeThatDeclaresIt()
    {
        var source = Generic("Box<>", null, "T");
        var clone = Generic("Crate<>", null, "V");
        var scope = new CloneScope(Declaring(("Box<>", source)), source, new Scope(null))
        {
            Itself = new ConstructedType(clone, [clone.TypeParametersList[0]]),
            Arguments = [clone.TypeParametersList[0]],
        };

        Assert.Same(source, Find(scope, "Box<>"));
    }

    [Fact]
    public void EachTypeParameterAnswersTheArgumentAtItsOwnOrdinal()
    {
        var source = Generic("Pair<,>", null, "K", "V");
        var key = HandBuiltModel.Class("Key");
        var value = HandBuiltModel.Class("Value");
        var scope = new CloneScope(new Scope(null), source, new Scope(null)) { Arguments = [key, value] };

        Assert.Same(key, Find(scope, "K"));
        Assert.Same(value, Find(scope, "V"));
    }

    [Fact]
    public void TheParametersOfEveryEnclosingConstructionAnswerTheirArguments()
    {
        var outer = Generic("Outer<>", null, "U");
        var middle = Generic("Middle<>", outer, "W");
        var source = new ClassType("Leaf", "Probe", middle.Scope, middle, null);
        var outerArgument = HandBuiltModel.Class("Int32");
        var middleArgument = HandBuiltModel.Class("String");
        var scope = new CloneScope(new Scope(null), source, new Scope(null))
        {
            Arguments = [],
            Enclosing = new ConstructedType(middle, [middleArgument], new ConstructedType(outer, [outerArgument])),
        };

        Assert.Same(middleArgument, Find(scope, "W"));
        Assert.Same(outerArgument, Find(scope, "U"));
    }

    [Fact]
    public void ASourcesOwnParameterShadowsAnEnclosingOneOfTheSameName()
    {
        var outer = Generic("Outer<>", null, "T");
        var source = Generic("Inner<>", outer, "T");
        var own = HandBuiltModel.Class("Own");
        var scope = new CloneScope(new Scope(null), source, new Scope(null))
        {
            Arguments = [own],
            Enclosing = new ConstructedType(outer, [HandBuiltModel.Class("Enclosing")]),
        };

        Assert.Same(own, Find(scope, "T"));
    }

    [Fact]
    public void TheSourcesNestedTypesAnswerBeforeItsLexicalParent()
    {
        var source = HandBuiltModel.Class("Tree");
        var node = new ClassType("Node", "Probe", source.Scope, source, null);
        source.NestedTypes.Add(node);
        var elsewhere = HandBuiltModel.Class("Elsewhere");
        var parent = Declaring(("Node", HandBuiltModel.Class("Node")), ("Elsewhere", elsewhere));
        var scope = new CloneScope(parent, source, new Scope(null)) { Itself = HandBuiltModel.Class("Forest") };

        Assert.Same(node, Find(scope, "Node"));
        Assert.Same(elsewhere, Find(scope, "Elsewhere"));
    }

    [Fact]
    public void AConstructionAnswersItsParametersAndRoutesWhatItsParentFindsThroughItself()
    {
        var box = Generic("Box<>", null, "T");
        var argument = HandBuiltModel.Class("Int32");
        var construction = new ConstructedType(box, [argument]);
        var field = new Field("value", box.TypeParametersList[0], OwnershipKind.Unrestricted, new MemberModifiers());
        var scope = new ConstructionScope(new Answering(new FieldResult(field, box, [])), construction);

        Assert.Same(argument, Find(scope, "T"));
        var found = Assert.IsType<FieldResult>(scope.Lookup(new LookupRequest("value", MemberVisibility.InstanceAndStatic, null)));
        Assert.Same(field, found.Field);
        Assert.Same(construction, Assert.Single(found.BaseConstructions));
    }

    [Fact]
    public void ThroughPrependsTheConstructionToFieldAndPropertyPaths()
    {
        var (construction, inherited) = Constructions();
        var holder = HandBuiltModel.Class("Holder");
        var field = new FieldResult(
            new Field("count", holder, OwnershipKind.Unrestricted, new MemberModifiers()), holder, [inherited]);
        var property = new PropertyResult(new Property("Size", holder, new MemberModifiers()), holder, []);

        var throughField = Assert.IsType<FieldResult>(ConstructionScope.Through(field, construction));
        var throughProperty = Assert.IsType<PropertyResult>(ConstructionScope.Through(property, construction));

        Assert.Same(field.Field, throughField.Field);
        Assert.Equal<ConstructedType>([construction, inherited], throughField.BaseConstructions);
        Assert.Same(property.Property, throughProperty.Property);
        Assert.Equal<ConstructedType>([construction], throughProperty.BaseConstructions);
    }

    [Fact]
    public void ThroughPrependsTheConstructionToEveryMethodsPath()
    {
        var (construction, inherited) = Constructions();
        var holder = HandBuiltModel.Class("Holder");
        var own = DeclarationFragments.Method(holder, "Get", holder);
        var reached = DeclarationFragments.Method(holder, "Get", holder, holder);
        var group = new MethodGroupResult([own, reached], new Dictionary<Method, IReadOnlyList<ConstructedType>> { [reached] = [inherited] });

        var through = Assert.IsType<MethodGroupResult>(ConstructionScope.Through(group, construction));

        Assert.Equal<Method>([own, reached], through.Methods);
        Assert.NotNull(through.Paths);
        Assert.Equal<ConstructedType>([construction], through.Paths[own]);
        Assert.Equal<ConstructedType>([construction, inherited], through.Paths[reached]);
    }

    private static (ConstructedType Construction, ConstructedType Inherited) Constructions()
    {
        var argument = HandBuiltModel.Class("Int32");
        return (new ConstructedType(Generic("Box<>", null, "T"), [argument]), new ConstructedType(Generic("Base<>", null, "B"), [argument]));
    }

    private static ClassType Generic(string name, ClassType? declaring, params string[] parameters)
    {
        var type = new ClassType(name, "Probe", declaring?.Scope ?? new Scope(null), declaring, null);
        foreach(var parameter in parameters)
        {
            type.TypeParametersList.Add(new TypeParameter(parameter, type.TypeParametersList.Count, type.Scope, type));
        }

        return type;
    }

    private static MemberScope Declaring(params (string Name, IMistType Type)[] types)
    {
        var scope = new MemberScope(null);
        foreach(var (name, type) in types)
        {
            scope.DeclareType(name, type);
        }

        return scope;
    }

    private static IMistType Find(Scope scope, string name)
    {
        return Assert.IsType<TypeResult>(scope.Lookup(new LookupRequest(name, MemberVisibility.None, null))).Type;
    }

    private sealed class Answering(LookupResult answer) : Scope(null)
    {
        protected override LookupResult LookupSelf(LookupRequest request)
        {
            return answer;
        }
    }
}