using Mist.Generated;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "1")]
public sealed class CloneDerivationTests
{
    private readonly HandBuiltModel model = new();
    private readonly StructType int32 = new("Int32", "Standard", new Scope(null), null, null);
    private readonly ClassType text = new("String", "Standard", new Scope(null), null, null);
    private readonly AbstractType clonable = new("IClonable<>", "Standard", new Scope(null), null, null);
    private readonly ClassType stringBuilder = new("StringBuilder", "System.Text", new Scope(null), null, null);
    private readonly StructType nothing = new("Void", "Standard", new Scope(null), null, null);
    private readonly List<MistType> declared = [];

    [Fact]
    public void AClassWhoseFieldsAreAllUnchangeableDerives()
    {
        var point = Class("Point");
        Hold(point, "X", int32);
        Hold(point, "Label", text);

        Assert.Equal(CloneKind.Derived, Judge(point).Kind);
        Assert.True(Clones().Derives(point));
    }

    [Theory]
    [InlineData("declared")]
    [InlineData("conformance")]
    [InlineData("derived")]
    public void AnOwnedFieldIsMetByATypeThatImplementsIClonableHoweverItDoes(string how)
    {
        IMistType held = how switch
        {
            "declared" => WithDeclaredClone(Class("Config")),
            "conformance" => Conforming(new ClassType("Shell", "System", new Scope(null), null, null)),
            _ => Style(),
        };
        var page = Class("Page");
        Hold(page, "Title", held, owned: true);

        Assert.Equal(CloneKind.Derived, Judge(page).Kind);
    }

    [Fact]
    public void EveryFieldOfAnOwnedTypeCountsAsOwned()
    {
        var page = Class("Page");
        page.Modifiers = DeclarationFragments.Public with { IsOwned = true };
        Hold(page, "Title", Style());

        Assert.Equal(CloneKind.Derived, Judge(page).Kind);
    }

    [Fact]
    public void APlainStructFieldIsMetWhenEachFieldOfTheStructMeetsTheCriterion()
    {
        var margin = Struct("Margin");
        Hold(margin, "Rule", Style(), owned: true);
        Hold(margin, "Inset", int32);
        var page = Class("Page");
        Hold(page, "Edge", margin);

        Assert.Equal(CloneKind.Derived, Judge(page).Kind);
        Assert.Equal(CloneKind.Derived, Judge(margin).Kind);
    }

    [Fact]
    public void AStructHoldingAPlainChangeableReferenceIsRefused()
    {
        var pin = Struct("Pin");
        Hold(pin, "Target", Style());
        var page = Class("Page");
        Hold(page, "Pin", pin);

        Assert.Equal("field 'Target' of 'Pin' is not owned and holds 'Style', which can change", Judge(pin).Refusal?.Why);
        Assert.Equal("field 'Pin' of 'Page' is not owned and holds 'Pin', which can change", Judge(page).Refusal?.Why);
    }

    [Theory]
    [InlineData(false, false, nameof(CloneKind.Derived))]
    [InlineData(true, false, nameof(CloneKind.Refused))]
    [InlineData(true, true, nameof(CloneKind.Derived))]
    public void ATupleFieldIsJudgedElementByElementWithTheFieldsOwnership(bool changeable, bool owned, string expected)
    {
        var page = Class("Page");
        Hold(page, "Pair", new TupleType([int32, changeable ? Style() : text]), owned);

        Assert.Equal(expected, Judge(page).Kind.ToString());
    }

    [Theory]
    [InlineData("optional-int", false, nameof(CloneKind.Derived))]
    [InlineData("optional-style", false, nameof(CloneKind.Refused))]
    [InlineData("optional-style", true, nameof(CloneKind.Derived))]
    [InlineData("union", true, nameof(CloneKind.Derived))]
    [InlineData("union-with-builder", true, nameof(CloneKind.Refused))]
    public void AnOptionalOrUnionFieldIsJudgedConstituentByConstituent(string shape, bool owned, string expected)
    {
        var style = Style();
        IMistType held = shape switch
        {
            "optional-int" => new UnionType([int32, nothing]),
            "optional-style" => new UnionType([style, nothing]),
            "union" => new UnionType([style, int32, nothing]),
            _ => new UnionType([style, stringBuilder]),
        };
        var page = Class("Page");
        Hold(page, "Note", held, owned);

        Assert.Equal(expected, Judge(page).Kind.ToString());
    }

    [Fact]
    public void AFieldThatIsNotOwnedAndHoldsAChangeableObjectPreventsDerivation()
    {
        var person = Class("Person");
        Hold(person, "Age", int32);
        Hold(person, "Home", Style());

        var verdict = Judge(person);

        Assert.Equal(CloneKind.Refused, verdict.Kind);
        Assert.Equal(
            "'Person' cannot be cloned: field 'Home' of 'Person' is not owned and holds 'Style', which can change; make that field 'owned' or give the type a 'Clone()' of its own",
            CloneDerivation.Describe(person, verdict.Refusal!));
        Assert.False(Clones().Derives(person));
    }

    [Fact]
    public void AnOwnedFieldWhoseTypeCannotBeClonedPreventsDerivationAndSaysWhy()
    {
        var page = Class("Page");
        Hold(page, "Buffer", stringBuilder, owned: true);

        Assert.Equal(
            "field 'Buffer' of 'Page' is owned but holds 'StringBuilder', which cannot be cloned (it implements no 'IClonable<T>')",
            Judge(page).Refusal?.Why);
    }

    [Theory]
    [InlineData(true, nameof(CloneKind.Derived))]
    [InlineData(false, nameof(CloneKind.Refused))]
    public void ATypeHoldingItselfDerivesThroughAnOwnedFieldButNotThroughAPlainOne(bool owned, string expected)
    {
        var node = Class("Node");
        Hold(node, "Value", int32);
        Hold(node, "Next", new UnionType([node, nothing]), owned);

        Assert.Equal(expected, Judge(node).Kind.ToString());
    }

    [Fact]
    public void AGenericIsJudgedWithItsArgumentsSubstituted()
    {
        var box = Class("Box");
        Hold(box, "Value", DeclarationFragments.Parameter(box, box.Scope, "T"));

        Assert.Equal(CloneKind.Derived, Judge(model.Interner.GetOrCreateConstructedType(box, [int32])).Kind);
        Assert.Equal(
            "field 'Value' of 'Box' is not owned and holds 'Style', which can change",
            Judge(model.Interner.GetOrCreateConstructedType(box, [Style()])).Refusal?.Why);
        Assert.True(Clones().Derives(box));
    }

    [Theory]
    [InlineData(false, false, nameof(CloneKind.Refused))]
    [InlineData(true, false, nameof(CloneKind.Refused))]
    [InlineData(true, true, nameof(CloneKind.Derived))]
    public void AnUnsubstitutedTypeParameterMeetsTheCriterionOnlyWhereItsConstraintsMakeEveryArgumentMeetIt(
        bool constrained, bool owned, string expected)
    {
        var holder = Class("Holder");
        var item = DeclarationFragments.Parameter(holder, holder.Scope, "T");
        Hold(holder, "Item", item, owned);
        var user = Class("User");
        var u = DeclarationFragments.Parameter(user, user.Scope, "U");

        if(constrained)
        {
            u.ConstraintsList.Add(new TypeConstraint(model.Interner.GetOrCreateConstructedType(clonable, [u])));
        }

        Assert.Equal(expected, Judge(model.Interner.GetOrCreateConstructedType(holder, [u])).Kind.ToString());
    }

    [Fact]
    public void AClassDerivesOnlyWhenEverySubclassMeetsTheCriterionAndARefusalNamesTheSubclass()
    {
        var animal = Class("Animal");
        Hold(animal, "Legs", int32);
        var dog = Class("Dog", animal);
        Hold(dog, "Name", text);

        Assert.Equal(CloneKind.Derived, Judge(animal).Kind);

        var cat = Class("Cat", animal);
        Hold(cat, "Friend", animal);

        Assert.Equal(
            "'Animal' cannot be cloned: its subclass 'Cat' cannot be copied, because field 'Friend' of 'Cat' is not owned and holds 'Animal', which can change; make that field 'owned' or give the type a 'Clone()' of its own",
            CloneDerivation.Describe(animal, Judge(animal).Refusal!));
        Assert.Equal(CloneKind.Derived, Judge(dog).Kind);
    }

    [Fact]
    public void AnInheritedFieldIsJudgedAsTheSubclasssOwn()
    {
        var animal = Class("Animal");
        Hold(animal, "Collar", Style());
        var dog = Class("Dog", animal);

        Assert.Equal("field 'Collar' of 'Animal' is not owned and holds 'Style', which can change", Judge(dog).Refusal?.Why);
    }

    [Fact]
    public void ADeclaredCloneIsUsedAndStopsDerivation()
    {
        var config = WithDeclaredClone(Class("Config"));
        Hold(config, "Buffer", stringBuilder);

        Assert.Equal(CloneKind.Declared, Judge(config).Kind);
        Assert.False(Clones().Derives(config));
    }

    [Fact]
    public void AClassWhoseBaseDeclaresCloneDoesNotDerive()
    {
        var animal = WithDeclaredClone(Class("Animal"));
        Hold(animal, "Legs", int32);
        var dog = Class("Dog", animal);
        Hold(dog, "Name", text);

        var verdict = Judge(dog);

        Assert.Equal(
            "'Dog' cannot be cloned: its base class 'Animal' declares 'Clone()', which copies every value as class 'Animal', and which 'Dog' cannot declare again",
            CloneDerivation.Describe(dog, verdict.Refusal!));
        Assert.False(Clones().Derives(dog));
    }

    [Fact]
    public void ASubclassDeclaringItsOwnClonePreventsItsBasesDerivation()
    {
        var animal = Class("Animal");
        Hold(animal, "Legs", int32);
        _ = WithDeclaredClone(Class("Dog", animal));

        Assert.Equal("its subclass 'Dog' declares a 'Clone()' of its own", Judge(animal).Refusal?.Why);
    }

    [Theory]
    [InlineData(SubstructuralDiscipline.None, true, false)]
    [InlineData(SubstructuralDiscipline.Linear, false, false)]
    [InlineData(SubstructuralDiscipline.Affine, true, false)]
    [InlineData(SubstructuralDiscipline.Linear, false, true)]
    public void DropAndSubstructuralModifiersNeitherCauseNorPreventDerivation(SubstructuralDiscipline discipline, bool drops, bool changeable)
    {
        var socket = Class("Socket");
        socket.Modifiers = DeclarationFragments.Public with { Discipline = discipline };
        Hold(socket, "Socket", changeable ? Style() : int32);

        if(drops)
        {
            socket.MethodsList.Add(new Method("Drop", nothing, [], DeclarationFragments.Public) { DeclaringType = socket });
        }

        Assert.Equal(changeable ? CloneKind.Refused : CloneKind.Derived, Judge(socket).Kind);
    }

    [Fact]
    public void ABaseClassNotDeclaredInMistPreventsDerivation()
    {
        var error = Class("ParseError", new ClassType("Exception", "System", new Scope(null), null, null));

        Assert.Equal("its base class 'Exception' is not declared in Mist, so what it holds cannot be seen", Judge(error).Refusal?.Why);
    }

    [Theory]
    [InlineData(false, nameof(CloneKind.Unchangeable))]
    [InlineData(true, nameof(CloneKind.Derived))]
    public void AValueTypeIsUnchangeableOnlyWhenNothingItHoldsCanChange(bool holdsAnObject, string expected)
    {
        var margin = Struct("Margin");
        Hold(margin, "Inset", int32);

        if(holdsAnObject)
        {
            Hold(margin, "Rule", Style(), owned: true);
        }

        Assert.Equal(expected, Judge(margin).Kind.ToString());
        Assert.True(Clones().Derives(margin));
    }

    private CloneVerdict Judge(IMistType type)
    {
        return Clones().Judge(type);
    }

    private CloneDerivation Clones()
    {
        return new CloneDerivation(declared);
    }

    private ClassType Style()
    {
        var style = declared.OfType<ClassType>().FirstOrDefault(type => type.Name == "Style") ?? Class("Style");

        if(style.FieldsList.Count == 0)
        {
            Hold(style, "Font", text);
            Hold(style, "Size", int32);
        }

        return style;
    }

    private ClassType Class(string name, MistType? baseType = null)
    {
        var type = new ClassType(name, "Probe", new Scope(null), null, DeclarationFragments.At<BodyTypeTypeDeclarationNode>())
        {
            Modifiers = DeclarationFragments.Public,
            BaseType = baseType,
        };
        declared.Add(type);
        return type;
    }

    private StructType Struct(string name)
    {
        var type = new StructType(name, "Probe", new Scope(null), null, DeclarationFragments.At<BodyTypeTypeDeclarationNode>())
        {
            Modifiers = DeclarationFragments.Public,
        };
        declared.Add(type);
        return type;
    }

    private static ClassType WithDeclaredClone(ClassType type)
    {
        type.MethodsList.Add(new Method("Clone", type, [], DeclarationFragments.Public with { IsOwned = true }) { DeclaringType = type });
        return type;
    }

    private ClassType Conforming(ClassType type)
    {
        type.BaseAbstractTypes.Add(model.Interner.GetOrCreateConstructedType(clonable, [type]));
        return type;
    }

    private static void Hold(ConcreteType holder, string name, IMistType type, bool owned = false)
    {
        holder.FieldsList.Add(new Field(name, type, owned ? OwnershipKind.Owned : OwnershipKind.Unrestricted, default) { DeclaringType = holder });
    }
}