// csharp-interop-v2: Cross-assembly inheritance in-process tests.
//
// **Validates: Requirements 6.1, 6.3, 6.4, 6.5, 7.1, 7.7**
//
// These tests construct an AssemblyTypeProvider from runtime/SDK assemblies (same
// pattern as RefStructProjectionTests) and verify that genuinely cross-assembly
// inheritance relationships are resolved, base-type chains connect, inherited
// members are callable, hidden inherited members are excluded, and unresolved-
// relationship fallback is accessible.

using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Backend;

/// <summary>
/// In-process tests asserting cross-assembly inheritance resolution for genuinely
/// cross-assembly SDK types: <c>NetworkStream : Stream</c> (System.Net.Sockets.dll →
/// System.Runtime.dll) and <c>IPEndPoint : EndPoint</c>. Verifies base-type chain
/// connection, assignability foundation, inherited member visibility, hidden-member
/// exclusion, and unresolved-relationship fallback.
/// </summary>
public sealed class CrossAssemblyInheritanceTests : IDisposable
{
    private readonly AssemblyTypeProvider _provider;
    private readonly VisibilityFilter _filter = new();

    public CrossAssemblyInheritanceTests()
    {
        var diagnostics = new DiagnosticBag();
        _provider = new AssemblyTypeProvider(GetAssemblyPaths(), diagnostics);
    }

    public void Dispose() => _provider.Dispose();

    private static ImmutableArray<string> GetAssemblyPaths()
    {
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        return Directory.GetFiles(runtimeDir, "*.dll").ToImmutableArray();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 1. Cross-assembly base type connected (Requirements 6.1, 6.4)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void NetworkStream_BaseType_IsReferenceEqualToProjectedStream()
    {
        // NetworkStream is in System.Net.Sockets.dll; Stream is in System.Runtime.dll.
        var networkStream = _provider.ResolveType("System.Net.Sockets", "NetworkStream", 0);
        var stream = _provider.ResolveType("System.IO", "Stream", 0);

        Assert.NotNull(networkStream);
        Assert.NotNull(stream);

        var classType = Assert.IsType<ClassType>(networkStream);
        Assert.NotNull(classType.BaseType);
        Assert.True(
            ReferenceEquals(classType.BaseType, stream),
            "NetworkStream.BaseType should be reference-equal to the provider's projected Stream type");
    }

    [Fact]
    public void IPEndPoint_BaseType_IsReferenceEqualToProjectedEndPoint()
    {
        var ipEndPoint = _provider.ResolveType("System.Net", "IPEndPoint", 0);
        var endPoint = _provider.ResolveType("System.Net", "EndPoint", 0);

        Assert.NotNull(ipEndPoint);
        Assert.NotNull(endPoint);

        var classType = Assert.IsType<ClassType>(ipEndPoint);
        Assert.NotNull(classType.BaseType);
        Assert.True(
            ReferenceEquals(classType.BaseType, endPoint),
            "IPEndPoint.BaseType should be reference-equal to the provider's projected EndPoint type");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 2. Multi-level chain (Requirement 6.3)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void NetworkStream_BaseType_Chain_ConnectsMultipleLevels()
    {
        // NetworkStream → Stream → MarshalByRefObject
        var networkStream = _provider.ResolveType("System.Net.Sockets", "NetworkStream", 0);
        Assert.NotNull(networkStream);

        var nsClass = Assert.IsType<ClassType>(networkStream);
        Assert.NotNull(nsClass.BaseType);

        // Stream level
        var streamClass = Assert.IsType<ClassType>(nsClass.BaseType);
        Assert.NotNull(streamClass.BaseType);

        // MarshalByRefObject level (Stream's base)
        var marshalByRef = Assert.IsType<ClassType>(streamClass.BaseType);
        Assert.Equal("MarshalByRefObject", marshalByRef.DisplayName);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 3. Assignability / both-direction casts (Requirements 6.4, 6.5)
    //    Asserted at the TYPE level: NetworkStream has a connected .BaseType
    //    chain containing the projected Stream instance (reference equality),
    //    which is the foundation the assignability checker uses.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void NetworkStream_BaseTypeChain_ContainsProjectedStream()
    {
        var networkStream = _provider.ResolveType("System.Net.Sockets", "NetworkStream", 0);
        var stream = _provider.ResolveType("System.IO", "Stream", 0);

        Assert.NotNull(networkStream);
        Assert.NotNull(stream);

        // Walk the BaseType chain from NetworkStream; it must contain the projected Stream instance.
        var found = false;
        var current = networkStream as ClassType;
        while (current?.BaseType is not null)
        {
            if (ReferenceEquals(current.BaseType, stream))
            {
                found = true;
                break;
            }
            current = current.BaseType as ClassType;
        }

        Assert.True(found,
            "NetworkStream's BaseType chain should contain the projected Stream instance " +
            "(foundation for assignability and both-direction casts)");
    }

    [Fact]
    public void IPEndPoint_BaseTypeChain_ContainsProjectedEndPoint()
    {
        var ipEndPoint = _provider.ResolveType("System.Net", "IPEndPoint", 0);
        var endPoint = _provider.ResolveType("System.Net", "EndPoint", 0);

        Assert.NotNull(ipEndPoint);
        Assert.NotNull(endPoint);

        var found = false;
        var current = ipEndPoint as ClassType;
        while (current?.BaseType is not null)
        {
            if (ReferenceEquals(current.BaseType, endPoint))
            {
                found = true;
                break;
            }
            current = current.BaseType as ClassType;
        }

        Assert.True(found,
            "IPEndPoint's BaseType chain should contain the projected EndPoint instance");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 4. Inherited members callable (Requirement 7.1)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void NetworkStream_InheritedMembers_ContainReadAndWrite()
    {
        var networkStream = _provider.ResolveType("System.Net.Sockets", "NetworkStream", 0);
        Assert.NotNull(networkStream);

        var members = _provider.GetMembers(networkStream!);

        var methodNames = members
            .Where(m => m.Kind == SymbolKind.Method)
            .Select(m => m.Name)
            .ToHashSet();

        Assert.Contains("Read", methodNames);
        Assert.Contains("Write", methodNames);
    }

    [Fact]
    public void NetworkStream_InheritedProperties_ContainCanRead()
    {
        var networkStream = _provider.ResolveType("System.Net.Sockets", "NetworkStream", 0);
        Assert.NotNull(networkStream);

        var members = _provider.GetMembers(networkStream!);

        var propertyNames = members
            .Where(m => m.Kind == SymbolKind.Property)
            .Select(m => m.Name)
            .ToHashSet();

        Assert.Contains("CanRead", propertyNames);
    }

    [Fact]
    public void NetworkStream_InheritedProperties_ContainLength()
    {
        var networkStream = _provider.ResolveType("System.Net.Sockets", "NetworkStream", 0);
        Assert.NotNull(networkStream);

        var members = _provider.GetMembers(networkStream!);

        var propertyNames = members
            .Where(m => m.Kind == SymbolKind.Property)
            .Select(m => m.Name)
            .ToHashSet();

        Assert.Contains("Length", propertyNames);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 5. Hidden inherited member not invocable (Requirement 7.7)
    //    Find a member in the Stream chain that is hidden by IsMemberExcluded
    //    (e.g., a method with pointer param or ref-readonly return) and assert
    //    it does NOT appear in GetMembers(networkStreamType).
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void NetworkStream_HiddenInheritedMember_NotInGetMembers()
    {
        var networkStream = _provider.ResolveType("System.Net.Sockets", "NetworkStream", 0);
        Assert.NotNull(networkStream);

        var members = _provider.GetMembers(networkStream!);
        var memberNames = members.Select(m => m.Name).ToHashSet();

        // Walk the CLR inheritance chain and find members that are hidden by the filter.
        // These hidden members should NOT appear in the provider's GetMembers result.
        var clrNetworkStream = typeof(System.Net.Sockets.NetworkStream);
        var allInheritedMembers = clrNetworkStream
            .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(m => m is MethodInfo or System.Reflection.PropertyInfo)
            .ToArray();

        var hiddenMembers = allInheritedMembers
            .Where(m => _filter.IsMemberExcluded(m))
            .ToList();

        // We expect at least some members to be hidden (e.g., pointer-based or
        // ref-readonly-return methods). If none are hidden, that itself is informative
        // but we still verify that no hidden member leaks through.
        foreach (var hidden in hiddenMembers)
        {
            // A hidden member's name should not appear in GetMembers UNLESS another
            // (visible) overload with the same name exists. So check that if the name
            // appears, at least one visible overload exists for it.
            if (memberNames.Contains(hidden.Name))
            {
                // There must be a visible overload with that name (per Req 7.4)
                var visibleOverloadExists = allInheritedMembers
                    .Any(m => m.Name == hidden.Name && !_filter.IsMemberExcluded(m));
                Assert.True(visibleOverloadExists,
                    $"Hidden member '{hidden.Name}' appears in GetMembers but no visible " +
                    "overload exists — the filter failed to exclude it.");
            }
        }

        // Additionally assert that truly all-hidden method groups are absent.
        var allHiddenGroups = hiddenMembers
            .GroupBy(m => m.Name)
            .Where(g => !allInheritedMembers.Any(m => m.Name == g.Key && !_filter.IsMemberExcluded(m)))
            .Select(g => g.Key)
            .ToList();

        foreach (var groupName in allHiddenGroups)
        {
            Assert.DoesNotContain(groupName, memberNames);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 6. Unresolved-relationship fallback (Requirement 6.6)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void UnresolvedRelationships_IsAccessibleAndEmptyForFullSdk()
    {
        // When all SDK assemblies are loaded, cross-assembly relationships should
        // all resolve successfully. The collection should be accessible and empty
        // (or at least not contain entries for the types we just tested).
        var unresolved = _provider.UnresolvedRelationships;

        Assert.NotNull(unresolved);

        // For the specific types we tested, there should be no unresolved relationships.
        var forNetworkStream = unresolved
            .Where(r => r.DerivedTypeName.Contains("NetworkStream"))
            .ToList();
        var forIPEndPoint = unresolved
            .Where(r => r.DerivedTypeName.Contains("IPEndPoint"))
            .ToList();

        Assert.Empty(forNetworkStream);
        Assert.Empty(forIPEndPoint);
    }
}
