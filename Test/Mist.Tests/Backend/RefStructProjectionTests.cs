// csharp-interop-v2 (Option A): ref structs are PROJECTED, not excluded (in-process).
//
// **Validates: Requirements 8.2, 8.3, 8.4, 8.5, 9.1, 9.2, 9.3, 9.4, 9.5**
//
// These tests invoke VisibilityFilter and AssemblyTypeProvider directly (no process
// spawning). Under Option A, ref struct types (Span<T>/ReadOnlySpan<T> and any
// IsByRefLike type) are projected as Mist stack-only structs and members that use them
// stay visible. The downstream C# compiler is the safety backstop for ref-struct misuse.
// They exercise:
//   - A mixed Span/non-Span overload group: ALL siblings (byte[], Span<byte>,
//     ReadOnlySpan<byte>) are visible and projected (8.2, 9.2, 9.3).
//   - An all-Span overload group projects its overloads too (no longer all-hidden).
//   - Classify() on a Span overload reports Hidden=false / Reason=None (9.4).
//   - Real SDK overloads retained: Encoding.GetBytes(string), Socket.Send(byte[]),
//     Console.ReadLine(); their Span siblings are now ALSO visible (8.3, 8.4, 8.5).
//   - Ref-struct types ARE projected: ResolveType(Span<T>/ReadOnlySpan<T>) is non-null
//     and IsTypeExcluded(Span<>/ReadOnlySpan<>) is false (9.1, 9.5).

using System.Collections.Immutable;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;
using Mist.Tests.InteropFixtures;

namespace Mist.Tests.Backend;

/// <summary>
/// In-process tests asserting ref-struct projection (Option A) and per-overload
/// preservation: <see cref="Span{T}"/>/<see cref="ReadOnlySpan{T}"/> overloads are
/// projected alongside their non-Span siblings, ref-struct types are projected, and
/// <see cref="VisibilityFilter.Classify"/> reports such members as visible. Asserted
/// against both the fixture assembly and the real .NET SDK types.
/// Validates Requirements 8.2–8.5, 9.1–9.5.
/// </summary>
public sealed class RefStructProjectionTests : IDisposable
{
    private readonly VisibilityFilter filter = new();
    private readonly AssemblyTypeProvider provider;

    public RefStructProjectionTests()
    {
        var diagnostics = new DiagnosticBag();
        provider = new AssemblyTypeProvider(GetAssemblyPaths(), diagnostics);
    }

    public void Dispose()
    {
        provider.Dispose();
    }

    // ── Assembly path / member lookup helpers ────────────────────────────

    private static ImmutableArray<string> GetAssemblyPaths()
    {
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var runtimePaths = Directory.GetFiles(runtimeDir, "*.dll").ToImmutableArray();

        // Add the InteropFixtures assembly so MixedSpanOverloads is resolvable.
        var fixtureAssemblyPath = typeof(SimpleClass).Assembly.Location;
        return runtimePaths.Add(fixtureAssemblyPath);
    }

    /// <summary>
    /// Selects a single overload by name and exact parameter types from a CLR type,
    /// avoiding <c>GetMethod</c>'s ambiguous-match exception for overloaded names.
    /// </summary>
    private static MethodInfo GetOverload(Type type, string name, params Type[] parameterTypes)
    {
        var method = type
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Single(m => m.Name == name
                && m.GetParameters().Select(p => p.ParameterType).SequenceEqual(parameterTypes));
        return method;
    }

    private static int CountMethodSymbols(ImmutableArray<ISymbol> members, string name)
    {
        return members.Count(m => m.Kind == SymbolKind.Method && m.Name == name);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Fixture: mixed Span/non-Span overload group — ALL overloads visible
    // Requirements 8.2, 9.2, 9.3
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Write_ByteArrayOverload_IsKept()
    {
        var method = GetOverload(typeof(MixedSpanOverloads), nameof(MixedSpanOverloads.Write), typeof(byte[]));
        Assert.False(filter.IsMemberExcluded(method));
    }

    [Fact]
    public void Write_SpanOverload_IsNotExcluded()
    {
        var method = GetOverload(typeof(MixedSpanOverloads), nameof(MixedSpanOverloads.Write), typeof(Span<byte>));
        Assert.False(filter.IsMemberExcluded(method));
    }

    [Fact]
    public void Write_ReadOnlySpanOverload_IsNotExcluded()
    {
        var method = GetOverload(typeof(MixedSpanOverloads), nameof(MixedSpanOverloads.Write), typeof(ReadOnlySpan<byte>));
        Assert.False(filter.IsMemberExcluded(method));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Classify(): a Span overload is visible (ref structs are not excluded)
    // Requirement 9.4
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Classify_SpanOverload_IsVisibleWithNoOffendingNames()
    {
        var method = GetOverload(typeof(MixedSpanOverloads), nameof(MixedSpanOverloads.Write), typeof(Span<byte>));

        var classification = filter.Classify(method);

        Assert.False(classification.Hidden);
        Assert.Equal(MemberHiddenReason.None, classification.Reason);
        Assert.Empty(classification.OffendingTypeNames);
    }

    [Fact]
    public void Classify_ReadOnlySpanOverload_IsVisibleWithNoOffendingNames()
    {
        var method = GetOverload(typeof(MixedSpanOverloads), nameof(MixedSpanOverloads.Write), typeof(ReadOnlySpan<byte>));

        var classification = filter.Classify(method);

        Assert.False(classification.Hidden);
        Assert.Equal(MemberHiddenReason.None, classification.Reason);
        Assert.Empty(classification.OffendingTypeNames);
    }

    [Fact]
    public void Classify_ByteArrayOverload_IsVisibleWithNoOffendingNames()
    {
        var method = GetOverload(typeof(MixedSpanOverloads), nameof(MixedSpanOverloads.Write), typeof(byte[]));

        var classification = filter.Classify(method);

        Assert.False(classification.Hidden);
        Assert.Equal(MemberHiddenReason.None, classification.Reason);
        Assert.Empty(classification.OffendingTypeNames);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Provider projection: every overload (Span and non-Span) is projected
    // Requirements 8.2, 9.2, 9.3
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Provider_MixedOverloadGroup_ProjectsAllThreeWriteOverloads()
    {
        var type = provider.ResolveType("Mist.Tests.InteropFixtures", "MixedSpanOverloads");
        Assert.NotNull(type);

        var members = provider.GetMembers(type!);

        // Write(byte[]), Write(Span<byte>), Write(ReadOnlySpan<byte>) all project under Option A.
        Assert.Equal(3, CountMethodSymbols(members, "Write"));
    }

    [Fact]
    public void Provider_AllSpanOverloadGroup_ProjectsItsOverloads()
    {
        var type = provider.ResolveType("Mist.Tests.InteropFixtures", "MixedSpanOverloads");
        Assert.NotNull(type);

        var members = provider.GetMembers(type!);

        // Both ProcessAll(Span<byte>) and ProcessAll(ReadOnlySpan<byte>) project (ref structs
        // are no longer excluded), so this is no longer an all-hidden group.
        Assert.Equal(2, CountMethodSymbols(members, "ProcessAll"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Real SDK overloads retained alongside their (now also visible) Span siblings
    // Requirements 8.3, 8.4, 8.5
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Encoding_GetBytesString_IsRetained_AndSpanOverloadAlsoVisible()
    {
        var stringOverload = GetOverload(typeof(Encoding), nameof(Encoding.GetBytes), typeof(string));
        Assert.False(filter.IsMemberExcluded(stringOverload));

        // The ReadOnlySpan<char> -> Span<byte> overload is now ALSO visible (ref structs projected).
        var spanOverload = GetOverload(
            typeof(Encoding), nameof(Encoding.GetBytes), typeof(ReadOnlySpan<char>), typeof(Span<byte>));
        Assert.False(filter.IsMemberExcluded(spanOverload));

        // The provider projects Encoding with a GetBytes method symbol surviving.
        var encoding = provider.ResolveType("System.Text", "Encoding");
        Assert.NotNull(encoding);
        Assert.True(CountMethodSymbols(provider.GetMembers(encoding!), "GetBytes") > 0);
    }

    [Fact]
    public void Socket_SendByteArray_IsRetained_AndSpanOverloadAlsoVisible()
    {
        var byteArrayOverload = GetOverload(typeof(Socket), nameof(Socket.Send), typeof(byte[]));
        Assert.False(filter.IsMemberExcluded(byteArrayOverload));

        var spanOverload = GetOverload(typeof(Socket), nameof(Socket.Send), typeof(ReadOnlySpan<byte>));
        Assert.False(filter.IsMemberExcluded(spanOverload));

        var socket = provider.ResolveType("System.Net.Sockets", "Socket");
        Assert.NotNull(socket);
        Assert.True(CountMethodSymbols(provider.GetMembers(socket!), "Send") > 0);
    }

    [Fact]
    public void Console_ReadLine_IsRetained()
    {
        var readLine = GetOverload(typeof(Console), nameof(Console.ReadLine), Type.EmptyTypes);
        Assert.False(filter.IsMemberExcluded(readLine));

        var console = provider.ResolveType("System", "Console");
        Assert.NotNull(console);

        var readLineSymbols = provider.GetMembers(console!)
            .Where(m => m.Kind == SymbolKind.Method && m.Name == "ReadLine")
            .Cast<Symbol>()
            .ToArray();
        // A parameterless ReadLine overload is projected (returns nullable string).
        Assert.Contains(readLineSymbols, s => s.Parameters.Count == 0);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Ref-struct types ARE projected (Option A)
    // Requirements 9.1, 9.5
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Provider_ProjectsSpan()
    {
        Assert.NotNull(provider.ResolveType("System", "Span", 1));
    }

    [Fact]
    public void Provider_ProjectsReadOnlySpan()
    {
        Assert.NotNull(provider.ResolveType("System", "ReadOnlySpan", 1));
    }

    [Fact]
    public void ProjectedSpan_IsRefStruct()
    {
        var span = provider.ResolveType("System", "Span", 1);
        Assert.NotNull(span);
        var structType = Assert.IsType<StructType>(span);
        Assert.True(structType.IsRefStruct);
    }

    [Fact]
    public void Filter_DoesNotExcludeSpanAndReadOnlySpanTypes()
    {
        Assert.False(filter.IsTypeExcluded(typeof(Span<>)));
        Assert.False(filter.IsTypeExcluded(typeof(ReadOnlySpan<>)));
    }
}