using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Semantics;

[Trait("Tier", "1")]
public sealed class TypeScopeLookupTests
{
    private static readonly MemberModifiers Public = new() { Accessibility = Accessibility.Public };

    [Fact]
    public void AFieldAnswersBeforeTheSameNamedTypeItsParentSees()
    {
        var (namespaceScope, gauge) = Visible("Gauge");
        var holder = new ClassType("Holder", "Probe", namespaceScope, null, null);
        var field = Field(holder, "Gauge", gauge);

        var found = Assert.IsType<FieldResult>(Lookup(holder, "Gauge"));

        Assert.Same(field, found.Field);
        Assert.Same(holder, found.DeclaringType);
    }

    [Fact]
    public void AnInheritedFieldAnswersBeforeTheSameNamedTypeItsParentSees()
    {
        var (namespaceScope, gauge) = Visible("Gauge");
        var baseType = new ClassType("Base", "Probe", namespaceScope, null, null);
        var field = Field(baseType, "Gauge", gauge);
        var derived = new ClassType("Derived", "Probe", namespaceScope, null, null) { BaseType = baseType };

        var found = Assert.IsType<FieldResult>(Lookup(derived, "Gauge"));

        Assert.Same(field, found.Field);
        Assert.Same(baseType, found.DeclaringType);
    }

    [Fact]
    public void WithNoSuchField_TheParentsTypeAnswers()
    {
        var (namespaceScope, gauge) = Visible("Gauge");
        var holder = new ClassType("Holder", "Probe", namespaceScope, null, null);
        _ = Field(holder, "Reading", gauge);

        Assert.Same(gauge, Assert.IsType<TypeResult>(Lookup(holder, "Gauge")).Type);
    }

    private static (MemberScope Scope, ClassType Type) Visible(string name)
    {
        var scope = new MemberScope(new Scope(null));
        var type = new ClassType(name, "Probe", new Scope(null), null, null);
        scope.DeclareType(name, type);
        return (scope, type);
    }

    private static Field Field(ClassType owner, string name, ClassType type)
    {
        var field = new Field(name, type, OwnershipKind.Unrestricted, Public) { DeclaringType = owner };
        owner.FieldsList.Add(field);
        return field;
    }

    private static LookupResult Lookup(ClassType type, string name)
    {
        return new BodyScope(new MethodScope(type.Scope)).Lookup(new LookupRequest(name, MemberVisibility.InstanceAndStatic, null));
    }
}