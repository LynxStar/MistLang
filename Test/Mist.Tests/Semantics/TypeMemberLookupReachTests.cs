using Mist.Generated;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

// What TypeMemberLookup reaches beyond a class's own declarations and its class bases: the types it
// walks as bases (a newtype's source, a type parameter's constraints), the accessibility that admits
// a member or nested type found past depth 0, and the names a fixed-arity spread projects. The
// expectations come from the lookup's own documentation, newtypes Requirement 3.8, full-accessibility
// Requirements 7 and 11 and variadics Requirement 10.1.
[Trait("Tier", "1")]
public sealed class TypeMemberLookupReachTests
{
    private static readonly ClassType Int32 = HandBuiltModel.Class("Int32");

    [Fact]
    public void ANewtypeReachesItsSourcesMembersThroughTheConstructionButNotItsPrivateOnes()
    {
        var box = HandBuiltModel.Class("Box<>");
        box.TypeParametersList.Add(new TypeParameter("T", 0, box.Scope, box));
        var content = Field("Content", Accessibility.Public);
        var bits = Field("bits", Accessibility.Private);
        box.FieldsList.AddRange([content, bits]);
        var describe = Method("Describe", Accessibility.Public);
        box.MethodsList.Add(describe);
        var construction = new ConstructedType(box, [Int32]);
        var crate = HandBuiltModel.Newtype("Crate", construction);

        var field = Assert.IsType<FieldResult>(Member(crate, "Content"));
        var group = Assert.IsType<MethodGroupResult>(Member(crate, "Describe"));

        Assert.Same(content, field.Field);
        Assert.Same(box, field.DeclaringType);
        Assert.Same(construction, Assert.Single(field.BaseConstructions));
        Assert.Same(describe, Assert.Single(group.Methods));
        _ = Assert.IsType<NotFoundResult>(Member(crate, "bits"));
    }

    [Fact]
    public void AnIntrinsicSourcedNewtypeAnswersValueWithItsOwnField()
    {
        var age = HandBuiltModel.Newtype("Age", Int32);
        var value = new Field("Value", Int32, OwnershipKind.Unrestricted,
            new MemberModifiers { Accessibility = Accessibility.Public, IsReadonly = true });
        age.ValueField = value;

        var found = Assert.IsType<FieldResult>(Member(age, "Value"));

        Assert.Same(value, found.Field);
        Assert.Same(age, found.DeclaringType);
        Assert.Empty(found.BaseConstructions);
    }

    [Fact]
    public void ATypeParametersTypeConstraintsAreItsBases()
    {
        var shape = new AbstractType("IShape<>", "Probe", new Scope(null), null, null);
        shape.TypeParametersList.Add(new TypeParameter("U", 0, shape.Scope, shape));
        var area = Method("Area", Accessibility.Public);
        shape.MethodsList.Add(area);
        var constraint = new ConstructedType(shape, [Int32]);
        var parameter = new TypeParameter("T", 0, new Scope(null), null);
        parameter.ConstraintsList.Add(new KeywordConstraint(TypeParameterConstraintKind.Struct));
        parameter.ConstraintsList.Add(new TypeConstraint(constraint));

        var group = Assert.IsType<MethodGroupResult>(Member(parameter, "Area"));

        Assert.Same(area, Assert.Single(group.Methods));
        Assert.Same(constraint, Assert.Single(group.Paths![area]));
    }

    /// <summary>
    /// Coarser than C#, by the lookup's own account: with no assembly boundary in the model, only
    /// private stops at the declaring type, so a derived type reaches a protected or internal member
    /// of every kind lookup gathers.
    /// </summary>
    [Theory]
    [InlineData(Accessibility.Protected)]
    [InlineData(Accessibility.Internal)]
    public void ANonPrivateBaseMemberOfEveryKindIsFoundFromADerivedType(Accessibility accessibility)
    {
        var baseType = HandBuiltModel.Class("Base");
        var field = Field("Count", accessibility);
        baseType.FieldsList.Add(field);
        var property = new Property("Label", ErrorType.Instance, new MemberModifiers { Accessibility = accessibility });
        baseType.PropertiesList.Add(property);
        var method = Method("Reset", accessibility);
        baseType.MethodsList.Add(method);
        var derived = HandBuiltModel.Class("Derived", baseType);

        Assert.Same(field, Assert.IsType<FieldResult>(Member(derived, "Count")).Field);
        Assert.Same(property, Assert.IsType<PropertyResult>(Member(derived, "Label")).Property);
        Assert.Same(method, Assert.Single(Assert.IsType<MethodGroupResult>(Member(derived, "Reset")).Methods));
    }

    /// <summary>
    /// Each kind of nested type carries its accessibility on a modifiers property of its own, so each
    /// is read separately; a private one is its declaring type's alone.
    /// </summary>
    [Theory]
    [InlineData("class")]
    [InlineData("enum")]
    [InlineData("newtype")]
    [InlineData("alias")]
    public void APrivateNestedTypeOfABaseIsHiddenFromADerivedType(string kind)
    {
        var baseType = HandBuiltModel.Class("Base");
        var nested = PrivateNested(kind);
        baseType.NestedTypes.Add(nested);
        var derived = HandBuiltModel.Class("Derived", baseType);

        Assert.Same(nested, Assert.IsType<TypeResult>(TypeName(baseType, "Inner")).Type);
        _ = Assert.IsType<NotFoundResult>(TypeName(derived, "Inner"));
    }

    /// <summary>
    /// The projections are <c>S1</c> through <c>SN</c>, one-based, so a zero suffix names none.
    /// </summary>
    [Fact]
    public void AFixedArityPackProjectsNoZerothElement()
    {
        var (bundle, _) = Bundle();

        _ = Assert.IsType<NotFoundResult>(Member(bundle, "Item0"));
    }

    [Fact]
    public void AFixedArityPropertyProjectionKeepsTheAggregatesAccessorsAndDeclaringType()
    {
        var (bundle, pack) = Bundle();
        var getter = new PropertyAccessor();
        var setter = new PropertyAccessor();
        var slot = new Property("Slot", new TupleType([new SpreadType(pack)]), new MemberModifiers { Accessibility = Accessibility.Public })
        {
            DeclaringType = bundle,
            Getter = getter,
            Setter = setter,
        };
        bundle.PropertiesList.Add(slot);

        var found = Assert.IsType<PropertyResult>(Member(bundle, "Slot2"));

        Assert.Same(pack.ElementAt(1), found.Property.Type);
        Assert.Same(getter, found.Property.Getter);
        Assert.Same(setter, found.Property.Setter);
        Assert.Same(bundle, found.Property.DeclaringType);
    }

    private static (StructType Bundle, TypeParameter Pack) Bundle()
    {
        var bundle = HandBuiltModel.Struct("Bundle<>");
        var pack = new TypeParameter("Ts", 0, bundle.Scope, bundle) { PackKind = TypePackKind.Unbracketed, PackLength = 2 };
        bundle.TypeParametersList.Add(pack);
        bundle.FieldsList.Add(new Field("Item", new TupleType([new SpreadType(pack)]), default,
            new MemberModifiers { Accessibility = Accessibility.Public }));
        return (bundle, pack);
    }

    private static MistType PrivateNested(string kind)
    {
        var modifiers = new MemberModifiers { Accessibility = Accessibility.Private };

        return kind switch
        {
            "class" => new ClassType("Inner", "Probe", new Scope(null), null, null) { Modifiers = modifiers },
            "enum" => new EnumType("Inner", "Probe", new Scope(null), null, null) { Modifiers = modifiers },
            "newtype" => new Newtype("Inner", "Probe", new Scope(null), null, new NewtypeDeclarationNode()) { Modifiers = modifiers },
            _ => new TypeAlias("Inner", "Probe", new Scope(null), null, new TypeAliasDeclarationNode()) { Modifiers = modifiers },
        };
    }

    private static Field Field(string name, Accessibility accessibility)
    {
        return new Field(name, Int32, OwnershipKind.Unrestricted, new MemberModifiers { Accessibility = accessibility });
    }

    private static Method Method(string name, Accessibility accessibility)
    {
        return new Method(name, ErrorType.Instance, [], new MemberModifiers { Accessibility = accessibility });
    }

    private static LookupResult Member(MistType type, string name)
    {
        return TypeMemberLookup.Find(type, LookupRequest.Member(name, MemberVisibility.InstanceOnly, null));
    }

    private static LookupResult TypeName(MistType type, string name)
    {
        return TypeMemberLookup.Find(type, new LookupRequest(name, MemberVisibility.None, null));
    }
}