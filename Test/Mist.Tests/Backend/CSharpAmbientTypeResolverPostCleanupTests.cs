using Mist.Backends.CSharpTranspiler;

namespace Mist.Tests.Backend;

/// <summary>
/// Verifies that <see cref="CSharpAmbientTypeResolver"/> no longer resolves
/// Console, Math, or Convert after their migration to the Standard Library.
/// The resolver infrastructure remains intact for future use.
/// </summary>
public class CSharpAmbientTypeResolverPostCleanupTests
{
    private readonly CSharpAmbientTypeResolver _resolver = new();

    // --- ResolveType returns null for migrated types ---

    [Fact]
    public void ResolveType_Console_ReturnsNull()
    {
        var result = _resolver.ResolveType("System", "Console");
        Assert.Null(result);
    }

    [Fact]
    public void ResolveType_Math_ReturnsNull()
    {
        var result = _resolver.ResolveType("System", "Math");
        Assert.Null(result);
    }

    [Fact]
    public void ResolveType_Convert_ReturnsNull()
    {
        var result = _resolver.ResolveType("System", "Convert");
        Assert.Null(result);
    }

    // --- HasNamespace returns false for System ---

    [Fact]
    public void HasNamespace_System_ReturnsFalse()
    {
        Assert.False(_resolver.HasNamespace("System"));
    }

    // --- Resolver can be instantiated (future-use preservation) ---

    [Fact]
    public void Resolver_CanBeInstantiated_WithoutErrors()
    {
        var resolver = new CSharpAmbientTypeResolver();
        Assert.NotNull(resolver);
    }
}