using System.Collections.Immutable;
using System.Reflection;
using Mist.Backends.CSharpTranspiler;
using Mist.Driver;

namespace Mist.Tests.Backend;

/// <summary>
/// Unit tests for <see cref="AmbientExtensionScanner"/>.
/// Validates detection of LINQ extension methods, exclusion of non-extension methods,
/// caching behavior, and receiver type identification.
///
/// Validates: Requirements 1.1–1.5, 11.1, 11.2.
/// </summary>
public sealed class AmbientExtensionScannerTests : IDisposable
{
    private readonly MetadataLoadContext metadataContext;
    private readonly AmbientExtensionScanner scanner;

    public AmbientExtensionScannerTests()
    {
        // Load all runtime assemblies via MetadataLoadContext — same approach as
        // AssemblyTypeProvider uses in production.
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var assemblyPaths = Directory.GetFiles(runtimeDir, "*.dll").ToImmutableArray();

        var resolver = new PathAssemblyResolver(assemblyPaths);
        metadataContext = new MetadataLoadContext(resolver);

        // Build LoadedAssemblyInfo for each assembly that loads successfully.
        var loaded = new List<LoadedAssemblyInfo>();
        foreach(var path in assemblyPaths)
        {
            try
            {
                var assembly = metadataContext.LoadFromAssemblyPath(path);
                loaded.Add(new LoadedAssemblyInfo(path, assembly));
            }
            catch
            {
                // Skip assemblies that fail to load (same as production code).
            }
        }

        scanner = new AmbientExtensionScanner(loaded);
    }

    public void Dispose()
    {
        metadataContext.Dispose();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Extension Method Detection (Requirements 1.1, 1.2, 1.3)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void GetExtensionsForNamespace_SystemLinq_DetectsWhereMethod()
    {
        var extensions = scanner.GetExtensionsForNamespace("System.Linq");

        var whereMethods = extensions.Where(e => e.MethodName == "Where").ToList();
        Assert.NotEmpty(whereMethods);
    }

    [Fact]
    public void GetExtensionsForNamespace_SystemLinq_DetectsCountMethod()
    {
        var extensions = scanner.GetExtensionsForNamespace("System.Linq");

        // Count<T>() has a parameterless overload (only receiver param in CLR terms)
        var countMethods = extensions.Where(e => e.MethodName == "Count").ToList();
        Assert.NotEmpty(countMethods);

        // Verify there's an overload with zero remaining parameters (the parameterless one)
        var parameterlessCount = countMethods.FirstOrDefault(e => e.RemainingParameters.Count == 0);
        Assert.NotNull(parameterlessCount);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Non-Extension Method Exclusion (Requirements 1.4, 1.5)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void GetExtensionsForNamespace_SystemLinq_ExcludesNonExtensionStaticMethods()
    {
        var extensions = scanner.GetExtensionsForNamespace("System.Linq");

        // Enumerable.Range and Enumerable.Repeat are public static methods in
        // System.Linq.Enumerable but are NOT extension methods (no ExtensionAttribute
        // on the method itself, and they don't have a receiver parameter pattern).
        var rangeMethod = extensions.FirstOrDefault(e => e.MethodName == "Range");
        var repeatMethod = extensions.FirstOrDefault(e => e.MethodName == "Repeat");

        Assert.Null(rangeMethod);
        Assert.Null(repeatMethod);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Caching Behavior (Requirement 11.2)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void GetExtensionsForNamespace_CalledTwice_ReturnsSameReference()
    {
        var first = scanner.GetExtensionsForNamespace("System.Linq");
        var second = scanner.GetExtensionsForNamespace("System.Linq");

        // Reference equality — the cache returns the exact same list instance.
        Assert.Same(first, second);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Receiver Type Identification
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void GetExtensionsForNamespace_WhereMethod_HasIEnumerableReceiverType()
    {
        var extensions = scanner.GetExtensionsForNamespace("System.Linq");

        // Where<TSource>(IEnumerable<TSource>, Func<TSource, bool>) — receiver is IEnumerable<TSource>
        // Filter to System.Linq.Enumerable specifically (not ParallelEnumerable or AsyncEnumerable).
        var whereMethod = extensions.First(e =>
            e.MethodName == "Where" &&
            e.ContainingClassName == "System.Linq.Enumerable");

        // The receiver type should be a generic IEnumerable (IEnumerable`1)
        Assert.Contains("IEnumerable", whereMethod.ReceiverClrType.Name);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Generic Method Detection (Requirement 1.3)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void GetExtensionsForNamespace_WhereMethod_IsGenericWithOneTypeParameter()
    {
        var extensions = scanner.GetExtensionsForNamespace("System.Linq");

        // Filter to System.Linq.Enumerable specifically.
        var whereMethod = extensions.First(e =>
            e.MethodName == "Where" &&
            e.ContainingClassName == "System.Linq.Enumerable");

        Assert.True(whereMethod.IsGenericMethod);
        // Where<TSource> has 1 type parameter: TSource
        _ = Assert.Single(whereMethod.MethodTypeParameters);
        Assert.Equal("TSource", whereMethod.MethodTypeParameters[0].Name);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Metadata Completeness (Requirement 1.3)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void GetExtensionsForNamespace_WhereMethod_HasCorrectMetadata()
    {
        var extensions = scanner.GetExtensionsForNamespace("System.Linq");

        // Filter to System.Linq.Enumerable specifically.
        var whereMethod = extensions.First(e =>
            e.MethodName == "Where" &&
            e.ContainingClassName == "System.Linq.Enumerable");

        Assert.Equal("System.Linq", whereMethod.Namespace);
        Assert.Equal("System.Linq.Enumerable", whereMethod.ContainingClassName);
        Assert.NotNull(whereMethod.MethodInfo);
        Assert.NotNull(whereMethod.ReturnClrType);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Empty Namespace (Requirement 11.1 — lazy discovery)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void GetExtensionsForNamespace_NonexistentNamespace_ReturnsEmptyList()
    {
        var extensions = scanner.GetExtensionsForNamespace("Nonexistent.Namespace.That.Does.Not.Exist");

        Assert.Empty(extensions);
    }

    [Fact]
    public void GetExtensionsForNamespace_OnlyReturnsMethodsFromQueriedNamespace()
    {
        var extensions = scanner.GetExtensionsForNamespace("System.Linq");

        // All returned methods must have Namespace == "System.Linq"
        Assert.All(extensions, e => Assert.Equal("System.Linq", e.Namespace));
    }
}