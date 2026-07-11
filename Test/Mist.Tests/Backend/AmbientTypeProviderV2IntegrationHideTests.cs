// ambient-type-provider-v2, Property 10: Unresolvable constraints hide type
// ambient-type-provider-v2, Property 17: Unresolvable inheritance hides type
//
// **Validates: Requirements 10.3, 17.3**
//
// Tests that types with unresolvable base classes, interfaces, or constraints
// are hidden (null return) rather than partially projected into the ProgramModel.
// Uses TypeProjectorV2 methods directly with controlled callbacks. No process spawning.

using Mist.Backends.CSharpTranspiler;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Backend;

/// <summary>
/// Integration tests verifying that types with unresolvable base classes, interfaces,
/// or type parameter constraints are hidden from the ProgramModel (ResolveBaseType/
/// ResolveInterfaces/ProjectTypeParameters return false, causing the provider to return null).
/// </summary>
public sealed class AmbientTypeProviderV2IntegrationHideTests
{
    private readonly TypeProjectorV2 projector = new(new VisibilityFilterV2());

    // ═══════════════════════════════════════════════════════════════════════
    // Unresolvable base class hides type (Property 17)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ResolveBaseType_UnresolvableBase_ReturnsFalse_TypeHidden()
    {
        // MemoryStream inherits from Stream. If Stream cannot be resolved, the type is hidden.
        var shell = projector.CreateTypeShell(typeof(MemoryStream), "System.IO");
        Assert.NotNull(shell);

        var success = TypeProjectorV2.ResolveBaseType(shell!, typeof(MemoryStream), (_, _) => null);

        Assert.False(success);
    }

    [Fact]
    public void ResolveBaseType_SelectivelyUnresolvableBase_ReturnsFalse()
    {
        // ArgumentException inherits from SystemException → Exception → Object.
        // If the direct base (SystemException in same assembly) cannot be resolved, type is hidden.
        var shell = projector.CreateTypeShell(typeof(ArgumentException), "System");
        Assert.NotNull(shell);

        // Callback returns null for everything — simulating the base being in an unloaded assembly
        var success = TypeProjectorV2.ResolveBaseType(shell!, typeof(ArgumentException), (_, _) => null);

        Assert.False(success);
    }

    [Fact]
    public void ResolveBaseType_ResolvableBase_ReturnsTrue()
    {
        // Control: when the base IS resolvable, the method succeeds.
        var streamShell = projector.CreateTypeShell(typeof(Stream), "System.IO");
        var memoryStreamShell = projector.CreateTypeShell(typeof(MemoryStream), "System.IO");
        Assert.NotNull(streamShell);
        Assert.NotNull(memoryStreamShell);

        var success = TypeProjectorV2.ResolveBaseType(
            memoryStreamShell!, typeof(MemoryStream),
            (ns, name) => ns == "System.IO" && name == "Stream" ? streamShell : null);

        Assert.True(success);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Unresolvable interface hides type (Property 17)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ResolveInterfaces_AnyUnresolvableInterface_ReturnsFalse_TypeHidden()
    {
        // List<T> implements several interfaces. If any public interface is unresolvable, type is hidden.
        var clrType = typeof(List<>);
        var shell = projector.CreateTypeShell(clrType, "System.Collections.Generic");
        Assert.NotNull(shell);

        // Return null for all interfaces -- simulating unloaded assembly
        var success = TypeProjectorV2.ResolveInterfaces(shell!, clrType, (_, _) => null);

        Assert.False(success);
    }

    [Fact]
    public void ResolveInterfaces_PartiallyUnresolvableInterfaces_ReturnsFalse()
    {
        // Resolve some interfaces but fail on one -- still hides the type.
        var clrType = typeof(List<>);
        var shell = projector.CreateTypeShell(clrType, "System.Collections.Generic");
        Assert.NotNull(shell);

        var callCount = 0;
        var success = TypeProjectorV2.ResolveInterfaces(shell!, clrType, (ns, name) =>
        {
            callCount++;
            // Resolve the first interface, fail on the second
            if(callCount == 1)
            {
                return new AbstractType(name, ns, new Scope(null), null, declarationNode: null);
            }

            return null;
        });

        Assert.False(success);
    }

    [Fact]
    public void ResolveInterfaces_AllResolvable_ReturnsTrue()
    {
        // Control: when all interfaces resolve, the method succeeds.
        var clrType = typeof(List<>);
        var shell = projector.CreateTypeShell(clrType, "System.Collections.Generic");
        Assert.NotNull(shell);

        var success = TypeProjectorV2.ResolveInterfaces(
            shell!, clrType,
            (ns, name) => new AbstractType(name, ns, new Scope(null), null, declarationNode: null));

        Assert.True(success);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Unresolvable constraints hide type (Property 10)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ProjectTypeParameters_UnresolvableConstraint_ReturnsFalse_TypeHidden()
    {
        // IParsable<TSelf> has `where TSelf : IParsable<TSelf>` -- a self-referential
        // type constraint. If the constraint type cannot be resolved, the type is hidden.
        var clrType = typeof(IParsable<>);
        var shell = projector.CreateTypeShell(clrType, "System");
        Assert.NotNull(shell);

        // Callback returns null for all types -- simulating unresolvable constraint types
        var success = TypeProjectorV2.ProjectTypeParameters(shell!, clrType, (_, _) => null);

        Assert.False(success);
    }

    [Fact]
    public void ProjectTypeParameters_AllConstraintsResolvable_ReturnsTrue()
    {
        // Control: when all constraint types resolve, the method succeeds.
        var clrType = typeof(IParsable<>);
        var shell = projector.CreateTypeShell(clrType, "System");
        Assert.NotNull(shell);

        var success = TypeProjectorV2.ProjectTypeParameters(
            shell!, clrType,
            (ns, name) => new AbstractType(name, ns, new Scope(null), null, declarationNode: null));

        Assert.True(success);
    }

    [Fact]
    public void ProjectTypeParameters_NoTypeConstraints_ReturnsTrue()
    {
        // Types with type parameters but no type constraints (only keyword constraints
        // like class/struct/new()) should always succeed regardless of callback.
        // List<T> has no type constraints -- just `T` with no `where T : SomeType`.
        var clrType = typeof(List<>);
        var shell = projector.CreateTypeShell(clrType, "System.Collections.Generic");
        Assert.NotNull(shell);

        // Callback returns null -- but should never be called since List<T> has no type constraints
        var success = TypeProjectorV2.ProjectTypeParameters(shell!, clrType, (_, _) => null);

        Assert.True(success);
    }

    [Fact]
    public void ProjectTypeParameters_NonGenericType_ReturnsTrue()
    {
        // Non-generic types have no type parameters to project -- always succeeds.
        var shell = projector.CreateTypeShell(typeof(string), "System");
        Assert.NotNull(shell);

        var success = TypeProjectorV2.ProjectTypeParameters(
            shell!, typeof(string),
            (_, _) => throw new InvalidOperationException("Should not be called for non-generic type"));

        Assert.True(success);
    }
}