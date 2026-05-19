using Mist.Backends.CSharpTranspiler;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Semantics;

/// <summary>
/// Tests for CSharpAmbientTypeResolver after the stdlib migration.
/// Console, Math, and Convert have been migrated to the Standard Library.
/// The resolver remains in the codebase for future use but has no registered types.
/// </summary>
public class AmbientTypeResolverTests
{
    private readonly CSharpAmbientTypeResolver _resolver = new();

    // --- HasNamespace ---

    [Fact]
    public void HasNamespace_ReturnsFalse_ForSystem()
    {
        // After stdlib migration, no types are registered in the System namespace.
        Assert.False(_resolver.HasNamespace("System"));
    }

    [Fact]
    public void HasNamespace_ReturnsFalse_ForUnknownNamespace()
    {
        Assert.False(_resolver.HasNamespace("Nonexistent.Namespace"));
    }

    [Fact]
    public void HasNamespace_ReturnsFalse_ForEmptyString()
    {
        Assert.False(_resolver.HasNamespace(""));
    }

    // --- ResolveType ---

    [Theory]
    [InlineData("System", "Console")]
    [InlineData("System", "Math")]
    [InlineData("System", "Convert")]
    public void ResolveType_ReturnsNull_ForMigratedTypes(string ns, string typeName)
    {
        // Console, Math, and Convert have been migrated to stdlib.
        var type = _resolver.ResolveType(ns, typeName);
        Assert.Null(type);
    }

    [Theory]
    [InlineData("System", "String")]
    [InlineData("System", "Array")]
    public void ResolveType_ReturnsNull_ForRemovedAmbientTypes(string ns, string typeName)
    {
        var type = _resolver.ResolveType(ns, typeName);
        Assert.Null(type);
    }

    [Fact]
    public void ResolveType_ReturnsNull_ForUnknownType()
    {
        var type = _resolver.ResolveType("System", "UnknownType");
        Assert.Null(type);
    }

    [Fact]
    public void ResolveType_ReturnsNull_ForUnknownNamespace()
    {
        var type = _resolver.ResolveType("Nonexistent", "Console");
        Assert.Null(type);
    }

    // --- GetMembers ---

    [Fact]
    public void GetMembers_ReturnsEmpty_ForUnresolvedType()
    {
        // Create a type that was never resolved through the resolver
        var scope = new Mist.Semantics.Model.Scope(parent: null);
        var fakeType = new ClassType("Fake", scope, "Fake");

        var members = _resolver.GetMembers(fakeType);
        Assert.True(members.IsEmpty);
    }

    // --- GetTypeNamesInNamespace ---

    [Fact]
    public void GetTypeNamesInNamespace_ReturnsEmpty_ForSystem()
    {
        // After stdlib migration, no types are registered in the System namespace.
        var names = _resolver.GetTypeNamesInNamespace("System");
        Assert.True(names.IsEmpty);
    }

    [Fact]
    public void GetTypeNamesInNamespace_ReturnsEmpty_ForUnknownNamespace()
    {
        var names = _resolver.GetTypeNamesInNamespace("Nonexistent");
        Assert.True(names.IsEmpty);
    }

    // --- Resolver can still be instantiated (future-use preservation) ---

    [Fact]
    public void Resolver_CanBeInstantiated_ForFutureUse()
    {
        var resolver = new CSharpAmbientTypeResolver();
        Assert.NotNull(resolver);
        Assert.False(resolver.HasNamespace("System"));
    }
}
