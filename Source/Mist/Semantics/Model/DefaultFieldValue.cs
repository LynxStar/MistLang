using Mist.Semantics.Contracts;

namespace Mist.Semantics.Model;

/// <summary>
/// Represents the computed default value for a single field within a defaultable type.
/// </summary>
public sealed class DefaultFieldValue
{
    /// <summary>Name of the field.</summary>
    public required string FieldName { get; init; }

    /// <summary>Resolved type of the field.</summary>
    public required IType FieldType { get; init; }

    /// <summary>How this default value was determined.</summary>
    public required DefaultValueKind ValueKind { get; init; }

    /// <summary>
    /// The compile-time constant value for primitive and string fields.
    /// Null for reference-type fields (which use <see cref="ConcreteType"/> instead).
    /// </summary>
    public object? ConstantValue { get; init; }

    /// <summary>
    /// For type bindings: the concrete type whose default is used.
    /// Null for primitive/string fields.
    /// </summary>
    public IType? ConcreteType { get; init; }

    /// <summary>
    /// For type bindings with nested overrides: the field overrides
    /// applied to the concrete type's default.
    /// </summary>
    public IReadOnlyDictionary<string, DefaultFieldValue>? NestedOverrides { get; init; }
}