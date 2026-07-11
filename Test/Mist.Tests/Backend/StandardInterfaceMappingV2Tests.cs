// ambient-type-provider-v2, Property 4: Standard interface mapping
//
// **Validates: Requirements 5.1, 5.2, 5.3**
//
// For types referencing IEnumerable<T> or IEnumerator<T>, verify Mist Standard equivalents used.
// Uses TypeProjectorV2.ResolveInterfaces directly with mock callbacks. No process spawning.

using FsCheck;
using FsCheck.Fluent;
using Mist.Backends.CSharpTranspiler;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Backend;

/// <summary>
/// Property test: when <see cref="TypeProjectorV2.ResolveInterfaces"/> encounters
/// <c>System.Collections.Generic.IEnumerable`1</c> or <c>IEnumerator`1</c>,
/// it maps them to <c>Standard.IEnumerable&lt;&gt;</c> or <c>Standard.IEnumerator&lt;&gt;</c>.
/// </summary>
public sealed class StandardInterfaceMappingV2Tests
{
    private readonly TypeProjectorV2 projector = new(new VisibilityFilterV2());

    /// <summary>
    /// CLR types that implement <c>IEnumerable&lt;T&gt;</c> (and therefore also <c>IEnumerator&lt;T&gt;</c>
    /// via their enumerator). Used as the type pool for property-based testing.
    /// </summary>
    private static readonly Type[] EnumerableTypes =
    [
        typeof(List<>),
        typeof(HashSet<>),
        typeof(LinkedList<>),
        typeof(Queue<>),
        typeof(Stack<>),
        typeof(SortedSet<>),
        typeof(Dictionary<,>),
    ];

    private static Gen<Type> GenEnumerableType()
    {
        return Gen.Choose(0, EnumerableTypes.Length - 1)
            .Select(idx => EnumerableTypes[idx]);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 4a: IEnumerable`1 maps to Standard.IEnumerable<>
    //
    // For any CLR type implementing IEnumerable<T>, ResolveInterfaces
    // SHALL request ("Standard", "IEnumerable<>") from the resolve callback.
    //
    // **Validates: Requirements 5.1, 5.3**
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ResolveInterfaces_IEnumerable1_MapsToStandardIEnumerable()
    {
        var samples = GenEnumerableType().Sample(50).ToList();

        for(var idx = 0; idx < samples.Count; idx++)
        {
            var clrType = samples[idx];
            var shell = projector.CreateTypeShell(clrType, clrType.Namespace ?? string.Empty);
            Assert.NotNull(shell);

            var resolvedRequests = new List<(string ns, string name)>();
            var mockShells = new Dictionary<(string, string), IMistType>();

            var success = TypeProjectorV2.ResolveInterfaces(shell!, clrType, (ns, name) =>
            {
                resolvedRequests.Add((ns, name));

                if(mockShells.TryGetValue((ns, name), out var existing))
                {
                    return existing;
                }

                var mock = new AbstractType(name, ns, new Scope(null), null, declarationNode: null);
                mockShells[(ns, name)] = mock;
                return mock;
            });

            Assert.True(success, $"ResolveInterfaces failed for {clrType.FullName}");

            // IEnumerable`1 should be requested from "Standard" namespace as "IEnumerable<>"
            Assert.True(
                resolvedRequests.Any(r => r.ns == "Standard" && r.name == "IEnumerable<>"),
                $"Sample {idx}: CLR type '{clrType.FullName}' should request " +
                $"(\"Standard\", \"IEnumerable<>\") but got: [{string.Join(", ", resolvedRequests.Select(r => $"(\"{r.ns}\", \"{r.name}\")"))}]");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 4b: IEnumerator`1 maps to Standard.IEnumerator<>
    //
    // For any CLR type implementing IEnumerator<T>, ResolveInterfaces
    // SHALL request ("Standard", "IEnumerator<>") from the resolve callback.
    //
    // **Validates: Requirements 5.2, 5.3**
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ResolveInterfaces_IEnumerator1_MapsToStandardIEnumerator()
    {
        // Use List<>.Enumerator which implements IEnumerator<T>
        var clrType = typeof(List<>.Enumerator);
        var shell = projector.CreateTypeShell(clrType, "System.Collections.Generic");
        Assert.NotNull(shell);

        var resolvedRequests = new List<(string ns, string name)>();
        var mockShells = new Dictionary<(string, string), IMistType>();

        var success = TypeProjectorV2.ResolveInterfaces(shell!, clrType, (ns, name) =>
        {
            resolvedRequests.Add((ns, name));

            if(mockShells.TryGetValue((ns, name), out var existing))
            {
                return existing;
            }

            var mock = new AbstractType(name, ns, new Scope(null), null, declarationNode: null);
            mockShells[(ns, name)] = mock;
            return mock;
        });

        Assert.True(success, "ResolveInterfaces failed for List<>.Enumerator");

        // IEnumerator`1 should be requested from "Standard" namespace as "IEnumerator<>"
        Assert.True(
            resolvedRequests.Any(r => r.ns == "Standard" && r.name == "IEnumerator<>"),
            $"List<>.Enumerator should request (\"Standard\", \"IEnumerator<>\") but got: " +
            $"[{string.Join(", ", resolvedRequests.Select(r => $"(\"{r.ns}\", \"{r.name}\")"))}]");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 4c: Non-IEnumerable/IEnumerator interfaces stay in their own namespace
    //
    // For interfaces that are NOT IEnumerable`1 or IEnumerator`1,
    // ResolveInterfaces SHALL NOT map them to the "Standard" namespace.
    //
    // **Validates: Requirements 5.1, 5.2 (negative case)**
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ResolveInterfaces_NonEnumerableInterfaces_NotMappedToStandard()
    {
        var samples = GenEnumerableType().Sample(50).ToList();

        for(var idx = 0; idx < samples.Count; idx++)
        {
            var clrType = samples[idx];
            var shell = projector.CreateTypeShell(clrType, clrType.Namespace ?? string.Empty);
            Assert.NotNull(shell);

            var resolvedRequests = new List<(string ns, string name)>();
            var mockShells = new Dictionary<(string, string), IMistType>();

            var success = TypeProjectorV2.ResolveInterfaces(shell!, clrType, (ns, name) =>
            {
                resolvedRequests.Add((ns, name));

                if(mockShells.TryGetValue((ns, name), out var existing))
                {
                    return existing;
                }

                var mock = new AbstractType(name, ns, new Scope(null), null, declarationNode: null);
                mockShells[(ns, name)] = mock;
                return mock;
            });

            Assert.True(success, $"ResolveInterfaces failed for {clrType.FullName}");

            // All requests to "Standard" should be ONLY for IEnumerable<> or IEnumerator<>
            var standardRequests = resolvedRequests.Where(r => r.ns == "Standard").ToList();
            foreach(var req in standardRequests)
            {
                Assert.True(
                    req.name is "IEnumerable<>" or "IEnumerator<>",
                    $"Sample {idx}: CLR type '{clrType.FullName}' mapped non-enumerable interface " +
                    $"to Standard: (\"{req.ns}\", \"{req.name}\"). Only IEnumerable<> and IEnumerator<> " +
                    $"should be mapped to Standard.");
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 4d: Resolved Standard interfaces appear in BaseAbstractTypes
    //
    // For types implementing IEnumerable<T>, the BaseAbstractTypes list on the
    // shell SHALL contain entries from the "Standard" namespace.
    //
    // **Validates: Requirement 5.3**
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ResolveInterfaces_StandardInterfaces_AppearInBaseAbstractTypes()
    {
        var clrType = typeof(List<>);
        var shell = projector.CreateTypeShell(clrType, "System.Collections.Generic");
        Assert.NotNull(shell);

        var mockShells = new Dictionary<(string, string), IMistType>();

        var success = TypeProjectorV2.ResolveInterfaces(shell!, clrType, (ns, name) =>
        {
            if(mockShells.TryGetValue((ns, name), out var existing))
            {
                return existing;
            }

            var mock = new AbstractType(name, ns, new Scope(null), null, declarationNode: null);
            mockShells[(ns, name)] = mock;
            return mock;
        });

        Assert.True(success);

        var memberShell = (MemberType)shell!;

        // Verify that BaseAbstractTypes contains entries with namespace "Standard"
        var standardEntries = memberShell.BaseAbstractTypes
            .Where(t => t.NamespaceName == "Standard")
            .ToList();

        Assert.True(standardEntries.Count > 0,
            "List<> should have at least one Standard interface in BaseAbstractTypes");

        // Specifically, Standard.IEnumerable<> should be present
        Assert.Contains(memberShell.BaseAbstractTypes,
            t => t.NamespaceName == "Standard" && t.Name == "IEnumerable<>");
    }
}