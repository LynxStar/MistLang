namespace Mist.Tests.InteropFixtures;

/// <summary>
/// Fixture exercising per-overload projection with mixed
/// <see cref="Span{T}"/>/<see cref="ReadOnlySpan{T}"/> and non-Span overloads.
///
/// <para>
/// Under csharp-interop-v2 Option A, ref struct types are projected as Mist stack-only
/// structs rather than excluded, so the <c>Write</c> overload group projects all three
/// overloads — the <c>byte[]</c> overload and both ref-struct
/// (<c>Span&lt;byte&gt;</c>/<c>ReadOnlySpan&lt;byte&gt;</c>) overloads stay visible
/// (Requirements 8.2, 9.2, 9.3).
/// </para>
///
/// <para>
/// The <c>ProcessAll</c> overload group uses a ref struct in every overload; both overloads
/// are still projected (ref structs are no longer hidden), so the method name retains its
/// symbols.
/// </para>
/// </summary>
public class MixedSpanOverloads
{
    /// <summary>Supported overload — uses <c>byte[]</c>.</summary>
    public int Write(byte[] data)
    {
        return data.Length;
    }

    /// <summary>Projected overload — uses <c>Span&lt;byte&gt;</c> (a ref struct).</summary>
    public int Write(Span<byte> data)
    {
        return data.Length;
    }

    /// <summary>Projected overload — uses <c>ReadOnlySpan&lt;byte&gt;</c> (a ref struct).</summary>
    public int Write(ReadOnlySpan<byte> data)
    {
        return data.Length;
    }

    /// <summary>Projected overload — every <c>ProcessAll</c> overload uses a ref struct.</summary>
    public int ProcessAll(Span<byte> data)
    {
        return data.Length;
    }

    /// <summary>Projected overload — every <c>ProcessAll</c> overload uses a ref struct.</summary>
    public int ProcessAll(ReadOnlySpan<byte> data)
    {
        return data.Length;
    }
}