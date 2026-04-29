using Mist.Generated;

namespace Mist.Semantics.Contracts;

/// <summary>
/// Metadata about a property declaration, stored on the property symbol.
/// </summary>
public sealed class PropertyInfo
{
    public bool HasGet { get; init; }
    public bool HasSet { get; init; }
    public bool HasInit { get; init; }
    public bool IsComputed { get; init; }
    public bool IsStatic { get; init; }
    public bool IsExpressionBodied { get; init; }
    public AccessorInfo? GetAccessor { get; init; }
    public AccessorInfo? SetAccessor { get; init; }
    public AccessorInfo? InitAccessor { get; init; }
    public ISymbol? BackingField { get; init; }
}

/// <summary>
/// Metadata about a single accessor (get, set, or init) within a property.
/// </summary>
public sealed class AccessorInfo
{
    public AccessorKind Kind { get; init; }
    public AccessorBodyKind BodyKind { get; init; }
    public ModifierNode? Modifier { get; init; }
}

/// <summary>
/// The kind of a property accessor.
/// </summary>
public enum AccessorKind
{
    Get,
    Set,
    Init,
}

/// <summary>
/// The body form of a property accessor.
/// </summary>
public enum AccessorBodyKind
{
    Auto,
    Block,
    Expression,
}
