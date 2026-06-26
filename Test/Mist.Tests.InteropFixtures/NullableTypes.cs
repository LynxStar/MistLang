namespace Mist.Tests.InteropFixtures;

/// <summary>
/// Type with nullable return values and Nullable&lt;T&gt; parameters
/// for testing nullable projection.
/// </summary>
public class HasNullableMembers
{
    /// <summary>Returns a nullable int (Nullable&lt;int&gt;).</summary>
    public int? GetNullableInt(bool returnNull) => returnNull ? null : 42;

    /// <summary>Returns a nullable string (reference type, always projected as T|void).</summary>
    public string? GetNullableString(bool returnNull) => returnNull ? null : "hello";

    /// <summary>Accepts a Nullable&lt;int&gt; parameter.</summary>
    public int DoubleOrDefault(int? value) => value.HasValue ? value.Value * 2 : 0;

    /// <summary>Accepts a nullable reference type parameter.</summary>
    public string ValueOrFallback(string? value, string fallback)
        => value ?? fallback;

    /// <summary>Returns a non-nullable value type (should NOT be projected as T|void).</summary>
    public int GetPlainInt() => 42;

    /// <summary>Returns a non-nullable struct (should NOT be projected as T|void).</summary>
    public SimpleStruct GetPlainStruct() => new(1, 2);
}