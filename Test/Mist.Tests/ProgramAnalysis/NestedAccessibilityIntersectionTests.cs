using Mist.ProgramAnalysis;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class NestedAccessibilityIntersectionTests
{
    private readonly ClassType outer;
    private readonly ClassType nested;

    public NestedAccessibilityIntersectionTests()
    {
        outer = new ClassType("Outer", "", parentScope: null, declaringType: null, declarationNode: null)
        {
            Assembly = new MistAssembly("Library", MistAssemblyKind.Reference),
            Modifiers = new() { Accessibility = Accessibility.Internal },
        };
        nested = new ClassType("Nested", "", outer.Scope, outer, declarationNode: null)
        {
            Assembly = outer.Assembly,
            Modifiers = new() { Accessibility = Accessibility.Protected },
        };
    }

    [Fact]
    public void OtherAssemblyDerivedOwnerIsBlockedByInternalEnclosingType()
    {
        var owner = CreateDerivedOwner(new MistAssembly("Consumer", MistAssemblyKind.Reference));

        Assert.Same(outer, AccessDomains.Blocking(owner, owner.Assembly, nested));
    }

    [Fact]
    public void SameAssemblyDerivedOwnerIsAdmitted()
    {
        var owner = CreateDerivedOwner(outer.Assembly);

        Assert.Null(AccessDomains.Blocking(owner, owner.Assembly, nested));
    }

    [Fact]
    public void SameAssemblyUnrelatedOwnerIsBlockedByProtectedNestedType()
    {
        var owner = new ClassType("Unrelated", "", parentScope: null, declaringType: null, declarationNode: null)
        {
            Assembly = outer.Assembly,
            Modifiers = new() { Accessibility = Accessibility.Internal },
        };

        Assert.Same(nested, AccessDomains.Blocking(owner, owner.Assembly, nested));
    }

    private ClassType CreateDerivedOwner(MistAssembly assembly)
    {
        return new ClassType("Derived", "", parentScope: null, declaringType: null, declarationNode: null)
        {
            Assembly = assembly,
            Modifiers = new() { Accessibility = Accessibility.Internal },
            BaseType = outer,
        };
    }
}