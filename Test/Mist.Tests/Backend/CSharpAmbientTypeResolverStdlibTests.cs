using Mist.Backends.CSharpTranspiler;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Backend;

/// <summary>
/// Task 42 unit tests for the rewritten <see cref="CSharpAmbientTypeResolver"/>
/// that consumes an <see cref="IStdlibTypeProvider"/>.
///
/// Validates: Requirements 9.3, 9.4, 9.5, 9.6, 9.9, 9.10, 9.12, 9.13.
/// </summary>
public sealed class CSharpAmbientTypeResolverStdlibTests
{
    /// <summary>
    /// A test-only <see cref="IStdlibTypeProvider"/> that returns known
    /// <see cref="IType"/> instances for each stdlib simple name, allowing
    /// reference-equality assertions.
    /// </summary>
    private sealed class TestStdlibTypeProvider : IStdlibTypeProvider
    {
        private readonly Dictionary<string, IType> _types = new();

        public TestStdlibTypeProvider()
        {
            // Create distinct IType instances for each stdlib type so we
            // can assert reference equality in the tests.
            _types["Int32"] = new StructType("Int32", new Scope(null), "Standard", isBackendBacked: true);
            _types["Boolean"] = new StructType("Boolean", new Scope(null), "Standard", isBackendBacked: true);
            _types["Double"] = new StructType("Double", new Scope(null), "Standard", isBackendBacked: true);
            _types["Single"] = new StructType("Single", new Scope(null), "Standard", isBackendBacked: true);
            _types["Int64"] = new StructType("Int64", new Scope(null), "Standard", isBackendBacked: true);
            _types["Char"] = new StructType("Char", new Scope(null), "Standard", isBackendBacked: true);
            _types["Decimal"] = new StructType("Decimal", new Scope(null), "Standard", isBackendBacked: true);
            _types["String"] = new ClassType("String", new Scope(null), "Standard", isBackendBacked: true);
            _types["Void"] = new StructType("Void", new Scope(null), "Standard", isBackendBacked: true);
        }

        public IType LookupStandardType(string simpleName) => _types[simpleName];

        public IType GetType(string simpleName) => _types[simpleName];
    }

    private readonly TestStdlibTypeProvider _stdlib = new();
    private readonly CSharpAmbientTypeResolver _resolver;

    public CSharpAmbientTypeResolverStdlibTests()
    {
        _resolver = new CSharpAmbientTypeResolver(_stdlib);
    }

    // ── Task 42.1: Console.WriteLine(string) parameter type is stdlib String ──

    /// <summary>
    /// Task 42.1: <c>CSharpAmbientTypeResolver.ResolveType("System", "Console")</c>
    /// returns a type whose <c>ReadLine()</c> member has return type equal to
    /// <c>stdlib.LookupStandardType("String")</c> (reference equality).
    ///
    /// We check ReadLine's return type because it directly returns String.
    /// The WriteLine members return void, but the resolver's internal
    /// construction uses stdlib types for all type references.
    /// </summary>
    [Fact]
    public void Console_ReadLine_ReturnType_Is_StdlibString_ReferenceEqual()
    {
        var consoleType = _resolver.ResolveType("System", "Console");
        Assert.NotNull(consoleType);

        var members = _resolver.GetMembers(consoleType!);
        var readLine = members.FirstOrDefault(m => m.Name == "ReadLine");
        Assert.NotNull(readLine);

        // Reference equality: the return type of ReadLine is the exact
        // same IType instance as stdlib.LookupStandardType("String").
        Assert.Same(_stdlib.GetType("String"), readLine!.DeclaredType);
    }

    // ── Task 42.2: System.String is no longer exposed ──

    /// <summary>
    /// Task 42.2: <c>CSharpAmbientTypeResolver.ResolveType("System", "String")</c>
    /// returns <c>null</c> — String is no longer an ambient type per
    /// Requirement 9.6.
    /// </summary>
    [Fact]
    public void ResolveType_System_String_ReturnsNull()
    {
        var result = _resolver.ResolveType("System", "String");
        Assert.Null(result);
    }

    // ── Additional coverage: System.Array is no longer exposed ──

    [Fact]
    public void ResolveType_System_Array_ReturnsNull()
    {
        var result = _resolver.ResolveType("System", "Array");
        Assert.Null(result);
    }

    // ── Console, Math, Convert remain as ambient types (Req 9.3, 9.4, 9.5) ──

    [Theory]
    [InlineData("System", "Console")]
    [InlineData("System", "Math")]
    [InlineData("System", "Convert")]
    public void ResolveType_ReturnsNonNull_ForRemainingAmbientTypes(string ns, string typeName)
    {
        var type = _resolver.ResolveType(ns, typeName);
        Assert.NotNull(type);
    }

    // ── Math members reference stdlib Int32 (Req 9.10) ──

    [Fact]
    public void Math_Abs_ReturnType_Is_StdlibInt32_ReferenceEqual()
    {
        var mathType = _resolver.ResolveType("System", "Math")!;
        var members = _resolver.GetMembers(mathType);
        var abs = members.FirstOrDefault(m => m.Name == "Abs");
        Assert.NotNull(abs);
        Assert.Same(_stdlib.GetType("Int32"), abs!.DeclaredType);
    }

    [Fact]
    public void Math_Max_ReturnType_Is_StdlibInt32_ReferenceEqual()
    {
        var mathType = _resolver.ResolveType("System", "Math")!;
        var members = _resolver.GetMembers(mathType);
        var max = members.FirstOrDefault(m => m.Name == "Max");
        Assert.NotNull(max);
        Assert.Same(_stdlib.GetType("Int32"), max!.DeclaredType);
    }

    // ── Convert members reference stdlib types (Req 9.10) ──

    [Fact]
    public void Convert_ToInt32_ReturnType_Is_StdlibInt32_ReferenceEqual()
    {
        var convertType = _resolver.ResolveType("System", "Convert")!;
        var members = _resolver.GetMembers(convertType);
        var toInt32 = members.FirstOrDefault(m => m.Name == "ToInt32");
        Assert.NotNull(toInt32);
        Assert.Same(_stdlib.GetType("Int32"), toInt32!.DeclaredType);
    }

    [Fact]
    public void Convert_ToString_ReturnType_Is_StdlibString_ReferenceEqual()
    {
        var convertType = _resolver.ResolveType("System", "Convert")!;
        var members = _resolver.GetMembers(convertType);
        var toString = members.FirstOrDefault(m => m.Name == "ToString");
        Assert.NotNull(toString);
        Assert.Same(_stdlib.GetType("String"), toString!.DeclaredType);
    }

    // ── GetTypeNamesInNamespace only returns Console, Math, Convert ──

    [Fact]
    public void GetTypeNamesInNamespace_System_Returns_Only_Console_Math_Convert()
    {
        var names = _resolver.GetTypeNamesInNamespace("System");
        Assert.Equal(3, names.Length);
        Assert.Contains("Console", names);
        Assert.Contains("Math", names);
        Assert.Contains("Convert", names);
        // String and Array are no longer present
        Assert.DoesNotContain("String", names);
        Assert.DoesNotContain("Array", names);
    }

    // ── Fallback: null stdlib provider uses legacy singletons ──

    [Fact]
    public void NullStdlibProvider_FallsBackToLegacySingletons()
    {
        // When no stdlib provider is given, the resolver uses
        // FallbackStdlibTypeProvider which returns legacy fallback types.
        var fallbackResolver = new CSharpAmbientTypeResolver(null);
        var mathType = fallbackResolver.ResolveType("System", "Math");
        Assert.NotNull(mathType);

        var members = fallbackResolver.GetMembers(mathType!);
        var abs = members.FirstOrDefault(m => m.Name == "Abs");
        Assert.NotNull(abs);
        // The return type should be LegacyPrimitiveTypes.Int (or its registry-backed equivalent)
        Assert.NotNull(abs!.DeclaredType);
    }

    // ── Default constructor (no args) also works ──

    [Fact]
    public void DefaultConstructor_CreatesResolverWithFallback()
    {
        var resolver = new CSharpAmbientTypeResolver();
        var console = resolver.ResolveType("System", "Console");
        Assert.NotNull(console);

        // String and Array should not be present
        Assert.Null(resolver.ResolveType("System", "String"));
        Assert.Null(resolver.ResolveType("System", "Array"));
    }
}
