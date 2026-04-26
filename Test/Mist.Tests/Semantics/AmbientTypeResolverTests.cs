using Mist.Backends.CSharpTranspiler;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Semantics;

public class AmbientTypeResolverTests
{
    private readonly CSharpAmbientTypeResolver _resolver = new();

    // --- HasNamespace ---

    [Fact]
    public void HasNamespace_ReturnsTrue_ForSystem()
    {
        Assert.True(_resolver.HasNamespace("System"));
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
    [InlineData("System", "String")]
    [InlineData("System", "Math")]
    [InlineData("System", "Array")]
    [InlineData("System", "Convert")]
    public void ResolveType_ReturnsNonNull_ForKnownTypes(string ns, string typeName)
    {
        var type = _resolver.ResolveType(ns, typeName);
        Assert.NotNull(type);
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

    [Theory]
    [InlineData("System", "Console", "Console")]
    [InlineData("System", "String", "String")]
    [InlineData("System", "Math", "Math")]
    [InlineData("System", "Array", "Array")]
    [InlineData("System", "Convert", "Convert")]
    public void ResolveType_ReturnsCorrectDisplayName(string ns, string typeName, string expectedDisplayName)
    {
        var type = _resolver.ResolveType(ns, typeName);
        Assert.NotNull(type);
        Assert.Equal(expectedDisplayName, type.DisplayName);
    }

    [Theory]
    [InlineData("System", "Console")]
    [InlineData("System", "String")]
    [InlineData("System", "Math")]
    [InlineData("System", "Array")]
    [InlineData("System", "Convert")]
    public void ResolveType_ReturnsClassType(string ns, string typeName)
    {
        var type = _resolver.ResolveType(ns, typeName);
        Assert.IsType<ClassType>(type);
        Assert.Equal(TypeKind.Class, type!.Kind);
    }

    // --- GetMembers ---

    [Fact]
    public void GetMembers_ReturnsExpectedMembers_ForConsole()
    {
        var type = _resolver.ResolveType("System", "Console")!;
        var members = _resolver.GetMembers(type);

        Assert.NotEmpty(members);
        Assert.Contains(members, m => m.Name == "WriteLine");
        Assert.Contains(members, m => m.Name == "ReadLine");
        Assert.Contains(members, m => m.Name == "Write");
    }

    [Fact]
    public void GetMembers_ReturnsExpectedMembers_ForString()
    {
        var type = _resolver.ResolveType("System", "String")!;
        var members = _resolver.GetMembers(type);

        Assert.NotEmpty(members);
        Assert.Contains(members, m => m.Name == "Length");
        Assert.Contains(members, m => m.Name == "Substring");
        Assert.Contains(members, m => m.Name == "Contains");
    }

    [Fact]
    public void GetMembers_ReturnsExpectedMembers_ForMath()
    {
        var type = _resolver.ResolveType("System", "Math")!;
        var members = _resolver.GetMembers(type);

        Assert.NotEmpty(members);
        Assert.Contains(members, m => m.Name == "Abs");
        Assert.Contains(members, m => m.Name == "Max");
        Assert.Contains(members, m => m.Name == "Min");
    }

    [Fact]
    public void GetMembers_ReturnsExpectedMembers_ForConvert()
    {
        var type = _resolver.ResolveType("System", "Convert")!;
        var members = _resolver.GetMembers(type);

        Assert.NotEmpty(members);
        Assert.Contains(members, m => m.Name == "ToInt32");
        Assert.Contains(members, m => m.Name == "ToString");
    }

    [Fact]
    public void GetMembers_AllMembersHaveBackendOrigin()
    {
        var type = _resolver.ResolveType("System", "Console")!;
        var members = _resolver.GetMembers(type);

        Assert.All(members, m => Assert.Equal(SymbolOrigin.Backend, m.Origin));
    }

    [Fact]
    public void GetMembers_AllMembersHaveMethodKind()
    {
        var type = _resolver.ResolveType("System", "Console")!;
        var members = _resolver.GetMembers(type);

        Assert.All(members, m => Assert.Equal(SymbolKind.Method, m.Kind));
    }

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
    public void GetTypeNamesInNamespace_ReturnsExpectedNames_ForSystem()
    {
        var names = _resolver.GetTypeNamesInNamespace("System");

        Assert.Contains("Console", names);
        Assert.Contains("String", names);
        Assert.Contains("Math", names);
        Assert.Contains("Array", names);
        Assert.Contains("Convert", names);
        Assert.Equal(5, names.Length);
    }

    [Fact]
    public void GetTypeNamesInNamespace_ReturnsEmpty_ForUnknownNamespace()
    {
        var names = _resolver.GetTypeNamesInNamespace("Nonexistent");
        Assert.True(names.IsEmpty);
    }

    // --- Caching: calling ResolveType twice returns the same instance ---

    [Theory]
    [InlineData("System", "Console")]
    [InlineData("System", "String")]
    [InlineData("System", "Math")]
    [InlineData("System", "Array")]
    [InlineData("System", "Convert")]
    public void ResolveType_ReturnsSameInstance_WhenCalledTwice(string ns, string typeName)
    {
        var first = _resolver.ResolveType(ns, typeName);
        var second = _resolver.ResolveType(ns, typeName);

        Assert.NotNull(first);
        Assert.Same(first, second);
    }

    // --- Lazy materialization: unreferenced types are not in the cache ---

    [Fact]
    public void LazyMaterialization_UnreferencedTypesAreNotResolved()
    {
        // Create a fresh resolver so nothing is cached yet
        var freshResolver = new CSharpAmbientTypeResolver();

        // Resolve only Console
        var console = freshResolver.ResolveType("System", "Console");
        Assert.NotNull(console);

        // Math was never resolved — GetMembers should return empty for a type
        // that was not resolved through this resolver
        var scope = new Mist.Semantics.Model.Scope(parent: null);
        var fakeMath = new ClassType("Math", scope, "System");
        var members = freshResolver.GetMembers(fakeMath);
        Assert.True(members.IsEmpty);

        // Now resolve Math through the resolver
        var math = freshResolver.ResolveType("System", "Math");
        Assert.NotNull(math);

        // GetMembers should now return members for the resolved Math type
        var mathMembers = freshResolver.GetMembers(math!);
        Assert.NotEmpty(mathMembers);
    }
}
