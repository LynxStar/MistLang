using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

/// <summary>
/// The public static property a dependent parameter projects its value through, as member lookup finds it
/// on hand-built types: by qualification on the type or anything deriving from it, never on an instance,
/// and never in place of the parameter itself inside the declaring type's own body. An ordinary parameter
/// projects nothing, and an intrinsic type keeps answering its parameter's name as its target does.
/// </summary>
[Trait("Tier", "1")]
public sealed class DependentValueLookupTests
{
    private static readonly ClassType Int32 = new("Int32", "Standard", new Scope(null), null, null);

    [Fact]
    public void AQualifiedStaticReadFindsTheProjectedProperty()
    {
        var (box, size) = Declare("Box");

        var found = Assert.IsType<PropertyResult>(Qualified(box, "N", MemberVisibility.StaticOnly));

        Assert.Same(size, found.Property.ProjectedParameter);
        Assert.Empty(found.BaseConstructions);
    }

    [Fact]
    public void TheDeclaringTypesOwnBodyReadsTheParameterItself()
    {
        var (box, size) = Declare("Box");

        Assert.Same(size, Assert.IsType<TypeResult>(Unqualified(box, "N")).Type);
    }

    [Fact]
    public void AnInstanceReadFindsNothing()
    {
        var (box, _) = Declare("Box");

        _ = Assert.IsType<NotFoundResult>(Qualified(box, "N", MemberVisibility.InstanceOnly));
    }

    [Fact]
    public void ADerivingTypeFindsTheBasesProjectionThroughTheConstructionItNamed()
    {
        var (box, size) = Declare("Box");
        var seven = new ConstructedType(box, [new LiftedType(Int32, 7L)]);
        var child = new ClassType("Child", "Probe", new Scope(null), null, null) { BaseType = seven };

        var qualified = Assert.IsType<PropertyResult>(Qualified(child, "N", MemberVisibility.StaticOnly));
        var unqualified = Assert.IsType<PropertyResult>(Unqualified(child, "N"));

        Assert.Same(size, qualified.Property.ProjectedParameter);
        Assert.Same(seven, Assert.Single(qualified.BaseConstructions));
        Assert.Same(size, unqualified.Property.ProjectedParameter);
    }

    [Fact]
    public void AnAbstractBasesProjectionIsFoundAsAClassBasesIs()
    {
        var shape = new AbstractType("IFixed<>", "Probe", new Scope(null), null, null);
        var size = Dependent(shape, "Size");
        TypeParameterDomainResolver.ProjectValues(shape);
        var tiny = new ClassType("Tiny", "Probe", new Scope(null), null, null);
        tiny.BaseAbstractTypes.Add(new ConstructedType(shape, [new LiftedType(Int32, 8L)]));

        Assert.Same(size, Assert.IsType<PropertyResult>(Qualified(tiny, "Size", MemberVisibility.StaticOnly)).Property.ProjectedParameter);
    }

    [Fact]
    public void ADerivingTypesOwnParameterShadowsTheBasesOfTheSameName()
    {
        var (box, _) = Declare("Box");
        var derived = new ClassType("Derived<>", "Probe", new Scope(null), null, null);
        var own = Dependent(derived, "N");
        derived.BaseType = new ConstructedType(box, [own]);
        TypeParameterDomainResolver.ProjectValues(derived);

        Assert.Same(own, Assert.IsType<PropertyResult>(Qualified(derived, "N", MemberVisibility.StaticOnly)).Property.ProjectedParameter);
    }

    [Fact]
    public void AnOrdinaryParameterProjectsNothingAndStaysLexical()
    {
        var holder = new ClassType("Holder<>", "Probe", new Scope(null), null, null);
        var element = new TypeParameter("T", 0, holder.Scope, holder);
        holder.TypeParametersList.Add(element);
        TypeParameterDomainResolver.ProjectValues(holder);

        Assert.Null(element.ValueProperty);
        _ = Assert.IsType<NotFoundResult>(Qualified(holder, "T", MemberVisibility.StaticOnly));
        Assert.Same(element, Assert.IsType<TypeResult>(Unqualified(holder, "T")).Type);
    }

    [Fact]
    public void AnIntrinsicTypeProjectsNothingAndItsTargetKeepsTheName()
    {
        var array = new ClassType("Array<,>", "Standard", new Scope(null), null, null)
        {
            Modifiers = new MemberModifiers { Accessibility = Accessibility.Public, IsIntrinsic = true },
        };
        var length = Dependent(array, "Length");
        TypeParameterDomainResolver.ProjectValues(array);

        Assert.Null(length.ValueProperty);
        Assert.Same(length, Assert.IsType<TypeResult>(Qualified(array, "Length", MemberVisibility.InstanceOnly)).Type);
    }

    [Fact]
    public void TheProjectionIsAPublicStaticGetOnlyPropertyOfTheDeclaredDomain()
    {
        var refined = HandBuiltModel.Refined(Int32, "Positive");
        var box = new ClassType("Box<>", "Probe", new Scope(null), null, null);
        var size = Dependent(box, "N", refined);
        TypeParameterDomainResolver.ProjectValues(box);

        var property = size.ValueProperty!;
        Assert.Same(refined, property.Type);
        Assert.Same(box, property.DeclaringType);
        Assert.True(property.Modifiers.IsStatic);
        Assert.Equal(Accessibility.Public, property.Modifiers.Accessibility);
        Assert.NotNull(property.Getter);
        Assert.Null(property.Setter);
        Assert.DoesNotContain(property, box.PropertiesList);
    }

    [Fact]
    public void APackProjectsNothing()
    {
        var family = new ClassType("Family<>", "Probe", new Scope(null), null, null);
        var pack = Dependent(family, "Ts");
        pack.PackKind = TypePackKind.Unbracketed;
        TypeParameterDomainResolver.ProjectValues(family);

        Assert.Null(pack.ValueProperty);
    }

    [Fact]
    public void AnAliasProjectsEachDependentParameterAndNothingElseWithNoDeclaringType()
    {
        var alias = new TypeAlias("Sized<,,>", "Probe", new Scope(null), null, null!);
        var count = new TypeParameter("Count", 0, alias.Scope, alias) { IsDependent = true, Domain = Int32 };
        var element = new TypeParameter("T", 1, alias.Scope, alias);
        var pack = new TypeParameter("Ts", 2, alias.Scope, alias) { IsDependent = true, PackKind = TypePackKind.Unbracketed, Domain = Int32 };
        alias.TypeParametersList.AddRange([count, element, pack]);

        TypeParameterDomainResolver.ProjectValues(alias);

        var property = count.ValueProperty!;
        Assert.Same(count, property.ProjectedParameter);
        Assert.Null(property.DeclaringType);
        Assert.True(property.Modifiers.IsStatic);
        Assert.Null(property.Setter);
        Assert.Null(element.ValueProperty);
        Assert.Null(pack.ValueProperty);
    }

    private static (ClassType Box, TypeParameter Size) Declare(string name)
    {
        var box = new ClassType($"{name}<>", "Probe", new Scope(null), null, null);
        var size = Dependent(box, "N");
        TypeParameterDomainResolver.ProjectValues(box);
        return (box, size);
    }

    private static TypeParameter Dependent(MemberType owner, string name, MistType? domain = null)
    {
        var parameter = new TypeParameter(name, owner.TypeParametersList.Count, owner.Scope, owner) { IsDependent = true, Domain = domain ?? Int32 };
        owner.TypeParametersList.Add(parameter);
        return parameter;
    }

    private static LookupResult Qualified(MistType type, string name, MemberVisibility visibility)
    {
        return TypeMemberLookup.Find(type, LookupRequest.Member(name, visibility, null));
    }

    private static LookupResult Unqualified(MistType type, string name)
    {
        return TypeMemberLookup.Find(type, new LookupRequest(name, MemberVisibility.InstanceAndStatic, null));
    }
}