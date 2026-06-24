namespace Mist.Semantics.Model;

/// <summary>
/// Identifies the CLR origin of a nullable projection created by the Assembly Type Provider.
/// Used by the CSharp emitter to produce the correct CLR-level signature on emission.
/// </summary>
public enum NullableOrigin
{
    /// <summary>Originated from <c>Nullable&lt;T&gt;</c> (value type wrapper).</summary>
    NullableValueType,

    /// <summary>Originated from a reference type (class, abstract type, string, array, delegate).</summary>
    ReferenceType,
}
