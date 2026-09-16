using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Semantics;

/// <summary>
/// Unit tests for member lookup on a type and its bases -- the single question behind both an
/// unqualified name arriving at a <see cref="TypeScope"/> and a qualified <c>receiver.X</c>.
/// <para>
/// The rules under test: the whole base graph is searched rather than stopping at the first level
/// that contributes; a nearer declaration of the same KIND shadows a further one; methods instead
/// accumulate into one overload set across depths with only identical signatures shadowed; two
/// different kinds at the same name conflict; private members belong to their own type; and
/// staticness is filtered by how the lookup arrived.
/// </para>
/// </summary>
public sealed class TypeMemberLookupTests
{
    private static MemberModifiers Mods(bool isStatic = false, Accessibility access = Accessibility.Public)
    {
        return new MemberModifiers { IsStatic = isStatic, Accessibility = access };
    }

    private static ClassType NewClass(string name)
    {
        return new ClassType(name, "Test", parentScope: null, declaringType: null, declarationNode: null);
    }

    private static Field NewField(string name, bool isStatic = false, Accessibility access = Accessibility.Public)
    {
        return new Field(name, ErrorType.Instance, OwnershipKind.Unrestricted, Mods(isStatic, access));
    }

    private static Property NewProperty(string name, bool isStatic = false)
    {
        return new Property(name, ErrorType.Instance, Mods(isStatic));
    }

    /// <summary>
    /// A method whose signature is distinguished by its parameter count, which is all the shadowing
    /// rule needs to tell one overload from another.
    /// </summary>
    private static Method NewMethod(
        string name, int arity, bool isStatic = false, Accessibility access = Accessibility.Public, IMistType? parameterType = null)
    {
        var parameters = new List<Parameter>();
        for(var i = 0; i < arity; i++)
        {
            parameters.Add(new Parameter($"p{i}", parameterType ?? ErrorType.Instance));
        }

        return new Method(name, ErrorType.Instance, parameters, Mods(isStatic, access));
    }

    private static LookupResult Find(MistType type, string name, MemberVisibility visibility = MemberVisibility.InstanceAndStatic)
    {
        return TypeMemberLookup.Find(type, new LookupRequest(name, visibility, null));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Reaching into the base chain
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A base's field is found from the derived type. Nothing at depth 0 matches, so the walk keeps
    /// going -- which is the whole point of the base chain.
    /// </summary>
    [Fact]
    public void BaseField_IsFoundFromDerived()
    {
        var baseType = NewClass("Base");
        var inherited = NewField("Value");
        baseType.FieldsList.Add(inherited);

        var derived = NewClass("Derived");
        derived.BaseType = baseType;

        var field = Assert.IsType<FieldResult>(Find(derived, "Value"));
        Assert.Same(inherited, field.Field);
        Assert.Same(baseType, field.DeclaringType);
    }

    /// <summary>
    /// Two levels up still resolves, so the search is not one hop.
    /// </summary>
    [Fact]
    public void GrandparentField_IsFoundFromDerived()
    {
        var grandparent = NewClass("A");
        var inherited = NewField("Value");
        grandparent.FieldsList.Add(inherited);

        var parent = NewClass("B");
        parent.BaseType = grandparent;

        var derived = NewClass("C");
        derived.BaseType = parent;

        var field = Assert.IsType<FieldResult>(Find(derived, "Value"));
        Assert.Same(inherited, field.Field);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Shadowing by depth, for kinds that shadow
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A derived field hides a base field of the same name. Same kind, nearer declaration -- that is
    /// shadowing, not a conflict, and silent by design since Mist has no hiding keyword.
    /// </summary>
    [Fact]
    public void DerivedField_ShadowsBaseField_NotAmbiguous()
    {
        var baseType = NewClass("Base");
        baseType.FieldsList.Add(NewField("Value"));

        var derived = NewClass("Derived");
        var own = NewField("Value");
        derived.FieldsList.Add(own);
        derived.BaseType = baseType;

        var field = Assert.IsType<FieldResult>(Find(derived, "Value"));
        Assert.Same(own, field.Field);
        Assert.Same(derived, field.DeclaringType);
    }

    /// <summary>
    /// Across depths the nearest kind answers, rather than conflicting with a farther one: the pair is
    /// refused where it is declared, since a derived member may not hide what its type inherits, and
    /// reporting it again at every read would be a second complaint about that one mistake.
    /// </summary>
    [Fact]
    public void DerivedField_AndBaseMethod_SameName_ResolvesToTheNearest()
    {
        var baseType = NewClass("Base");
        baseType.MethodsList.Add(NewMethod("Foo", arity: 0));

        var derived = NewClass("Derived");
        var own = NewField("Foo");
        derived.FieldsList.Add(own);
        derived.BaseType = baseType;

        var field = Assert.IsType<FieldResult>(Find(derived, "Foo"));
        Assert.Same(own, field.Field);
    }

    /// <summary>
    /// Two kinds declared at the same depth conflict for the same reason.
    /// </summary>
    [Fact]
    public void FieldAndProperty_SameNameSameType_IsAmbiguous()
    {
        var type = NewClass("Both");
        type.FieldsList.Add(NewField("Foo"));
        type.PropertiesList.Add(NewProperty("Foo"));

        _ = Assert.IsType<AmbiguousResult>(Find(type, "Foo"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Methods accumulate instead of shadowing
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Overloads split across two levels form ONE group. Stopping at the nearest level that had a
    /// method would silently drop the base's overload and leave a call to it unresolvable.
    /// </summary>
    [Fact]
    public void Overloads_SplitAcrossDepths_MergeIntoOneGroup()
    {
        var baseType = NewClass("Base");
        var baseOverload = NewMethod("Bar", arity: 1);
        baseType.MethodsList.Add(baseOverload);

        var derived = NewClass("Derived");
        var derivedOverload = NewMethod("Bar", arity: 2);
        derived.MethodsList.Add(derivedOverload);
        derived.BaseType = baseType;

        var group = Assert.IsType<MethodGroupResult>(Find(derived, "Bar"));

        Assert.Equal(2, group.Methods.Count);
        Assert.Contains(derivedOverload, group.Methods);
        Assert.Contains(baseOverload, group.Methods);
    }

    /// <summary>
    /// One identical signature resolves to ONE declaration, and between two classes it is the
    /// INHERITED one: Mist has no override and no hiding, so the deriving declaration -- refused where
    /// it is written -- answers nothing, and a use keeps the meaning it has once that line is deleted.
    /// The parameter is a real type because a signature naming an unresolved one has no identity to
    /// coincide with.
    /// </summary>
    [Fact]
    public void SameSignatureAtGreaterDepth_KeepsTheInheritedDeclaration()
    {
        var argument = NewClass("Arg");

        var baseType = NewClass("Base");
        var inherited = NewMethod("Bar", arity: 1, parameterType: argument);
        baseType.MethodsList.Add(inherited);

        var derived = NewClass("Derived");
        derived.MethodsList.Add(NewMethod("Bar", arity: 1, parameterType: argument));
        derived.BaseType = baseType;

        var group = Assert.IsType<MethodGroupResult>(Find(derived, "Bar"));

        Assert.Same(inherited, Assert.Single(group.Methods));
    }

    /// <summary>
    /// A diamond reaches a shared base once. Visiting it twice would turn one inherited member into
    /// a self-conflict.
    /// </summary>
    [Fact]
    public void DiamondInheritance_VisitsSharedBaseOnce()
    {
        var shared = NewClass("Shared");
        var inherited = NewMethod("Common", arity: 0);
        shared.MethodsList.Add(inherited);

        var left = NewClass("Left");
        left.BaseAbstractTypes.Add(shared);

        var right = NewClass("Right");
        right.BaseAbstractTypes.Add(shared);

        var derived = NewClass("Derived");
        derived.BaseAbstractTypes.Add(left);
        derived.BaseAbstractTypes.Add(right);

        var group = Assert.IsType<MethodGroupResult>(Find(derived, "Common"));

        Assert.Same(inherited, Assert.Single(group.Methods));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Accessibility
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A private base member is not reachable from a derived type. It was reachable before member
    /// lookup consulted accessibility at all.
    /// </summary>
    [Fact]
    public void PrivateBaseField_IsNotFoundFromDerived()
    {
        var baseType = NewClass("Base");
        baseType.FieldsList.Add(NewField("Hidden", access: Accessibility.Private));

        var derived = NewClass("Derived");
        derived.BaseType = baseType;

        _ = Assert.IsType<NotFoundResult>(Find(derived, "Hidden"));
    }

    /// <summary>
    /// The same member is reachable on the type that declared it.
    /// </summary>
    [Fact]
    public void PrivateField_IsFoundOnItsOwnType()
    {
        var type = NewClass("Owner");
        var own = NewField("Hidden", access: Accessibility.Private);
        type.FieldsList.Add(own);

        var field = Assert.IsType<FieldResult>(Find(type, "Hidden"));
        Assert.Same(own, field.Field);
    }

    /// <summary>
    /// A private base overload drops out of the group while the reachable one stays.
    /// </summary>
    [Fact]
    public void PrivateBaseOverload_IsExcludedFromGroup()
    {
        var baseType = NewClass("Base");
        baseType.MethodsList.Add(NewMethod("Bar", arity: 1, access: Accessibility.Private));

        var derived = NewClass("Derived");
        var reachable = NewMethod("Bar", arity: 2);
        derived.MethodsList.Add(reachable);
        derived.BaseType = baseType;

        var group = Assert.IsType<MethodGroupResult>(Find(derived, "Bar"));

        Assert.Same(reachable, Assert.Single(group.Methods));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Staticness follows how the lookup arrived
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Naming the type reaches only its statics, which is what <c>Console.WriteLine</c> needs and
    /// what stops <c>SomeType.InstanceMethod</c>.
    /// </summary>
    [Fact]
    public void StaticOnly_ExcludesInstanceMembers()
    {
        var type = NewClass("Target");
        type.FieldsList.Add(NewField("Instance"));
        var shared = NewField("Shared", isStatic: true);
        type.FieldsList.Add(shared);

        _ = Assert.IsType<NotFoundResult>(Find(type, "Instance", MemberVisibility.StaticOnly));

        var field = Assert.IsType<FieldResult>(Find(type, "Shared", MemberVisibility.StaticOnly));
        Assert.Same(shared, field.Field);
    }

    /// <summary>
    /// Supplying a value reaches only instance members: a static one needs the type named instead.
    /// </summary>
    [Fact]
    public void InstanceOnly_ExcludesStaticMembers()
    {
        var type = NewClass("Target");
        var instance = NewField("Instance");
        type.FieldsList.Add(instance);
        type.FieldsList.Add(NewField("Shared", isStatic: true));

        _ = Assert.IsType<NotFoundResult>(Find(type, "Shared", MemberVisibility.InstanceOnly));

        var field = Assert.IsType<FieldResult>(Find(type, "Instance", MemberVisibility.InstanceOnly));
        Assert.Same(instance, field.Field);
    }

    /// <summary>
    /// A type-reference position wants no members at all, only names.
    /// </summary>
    [Fact]
    public void None_ExcludesEveryMemberKind()
    {
        var type = NewClass("Target");
        type.FieldsList.Add(NewField("Value"));
        type.PropertiesList.Add(NewProperty("Prop"));
        type.MethodsList.Add(NewMethod("Method", arity: 0));

        _ = Assert.IsType<NotFoundResult>(Find(type, "Value", MemberVisibility.None));
        _ = Assert.IsType<NotFoundResult>(Find(type, "Prop", MemberVisibility.None));
        _ = Assert.IsType<NotFoundResult>(Find(type, "Method", MemberVisibility.None));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Type names
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A nested type is nameable, and inherited, since it needs no receiver.
    /// </summary>
    [Fact]
    public void NestedType_IsFoundAndInherited()
    {
        var baseType = NewClass("Base");
        var nested = NewClass("Inner");
        baseType.NestedTypes.Add(nested);

        var derived = NewClass("Derived");
        derived.BaseType = baseType;

        var found = Assert.IsType<TypeResult>(Find(derived, "Inner", MemberVisibility.None));
        Assert.Same(nested, found.Type);
    }

    /// <summary>
    /// A type parameter belongs to the declaration that introduced it, so a base's is not a name in
    /// the derived type -- in <c>class Derived&lt;T&gt; : Base&lt;T&gt;</c> the <c>T</c> in scope is
    /// Derived's own.
    /// </summary>
    [Fact]
    public void BaseTypeParameter_IsNotVisibleInDerived()
    {
        var baseType = NewClass("Base");
        baseType.TypeParametersList.Add(new TypeParameter("T", 0, baseType.Scope, baseType));

        var derived = NewClass("Derived");
        derived.BaseType = baseType;

        _ = Assert.IsType<NotFoundResult>(Find(derived, "T", MemberVisibility.None));
    }

    /// <summary>
    /// Its own type parameter is.
    /// </summary>
    [Fact]
    public void OwnTypeParameter_IsVisible()
    {
        var type = NewClass("Owner");
        var parameter = new TypeParameter("T", 0, type.Scope, type);
        type.TypeParametersList.Add(parameter);

        var found = Assert.IsType<TypeResult>(Find(type, "T", MemberVisibility.None));
        Assert.Same(parameter, found.Type);
    }

    /// <summary>
    /// Nothing matching is a miss, not an error.
    /// </summary>
    [Fact]
    public void NoMatch_ReturnsNotFound()
    {
        var type = NewClass("Empty");

        _ = Assert.IsType<NotFoundResult>(Find(type, "Nothing"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Kinds reached by the same syntax but not stored as ordinary members
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// An enum variant resolves, and its type is the enum. It is not a field -- there is no instance
    /// to read it from -- so it needs its own result, and it answers to a static request because
    /// that is how <c>Color.Red</c> arrives.
    /// </summary>
    [Fact]
    public void EnumVariant_IsFoundThroughTheEnumType()
    {
        var color = new EnumType("Color", "Test", parentScope: null, declaringType: null, declarationNode: null);
        var red = new EnumMember("Red", isDefault: false, value: null);
        color.MembersList.Add(red);

        var found = Assert.IsType<EnumMemberResult>(Find(color, "Red", MemberVisibility.StaticOnly));

        Assert.Same(red, found.Member);
        Assert.Same(color, found.DeclaringType);
    }

    /// <summary>
    /// A tuple's positional element resolves. <see cref="TupleType"/> is not a
    /// <see cref="ConcreteType"/>, so its synthesized fields need finding explicitly -- and without
    /// that, a caller reporting its misses would call <c>Item1</c> missing.
    /// </summary>
    [Fact]
    public void TuplePositionalElement_IsFound()
    {
        var tuple = new TupleType([ErrorType.Instance, ErrorType.Instance]);

        var found = Assert.IsType<FieldResult>(Find(tuple, "Item1", MemberVisibility.InstanceOnly));

        Assert.Equal("Item1", found.Field.Name);
    }

    /// <summary>
    /// A named tuple element resolves under its name as well as its position.
    /// </summary>
    [Fact]
    public void NamedTupleElement_IsFoundByName()
    {
        var underlying = new TupleType([ErrorType.Instance, ErrorType.Instance]);
        var named = new NamedTupleType(underlying, ["x", "flag"]);

        var byName = Assert.IsType<FieldResult>(Find(named, "x", MemberVisibility.InstanceOnly));
        Assert.Equal("x", byName.Field.Name);

        var byPosition = Assert.IsType<FieldResult>(Find(named, "Item1", MemberVisibility.InstanceOnly));
        Assert.Equal("Item1", byPosition.Field.Name);
    }
}