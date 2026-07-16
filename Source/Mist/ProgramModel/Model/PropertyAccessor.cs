namespace Mist.ProgramModel.Model;

/// <summary>
/// An accessor (getter or setter) on a property.
/// </summary>
internal sealed class PropertyAccessor
{
    /// <summary>
    /// Whether this accessor is auto-backed (no explicit body).
    /// </summary>
    public bool IsAuto { get; init; }

    /// <summary>
    /// Whether this is an init accessor (only meaningful on setters).
    /// </summary>
    public bool IsInit { get; init; }

    /// <summary>
    /// The accessibility of this accessor, when it differs from the property's accessibility.
    /// Defaults to <see cref="Accessibility.Public"/> (inherits property accessibility).
    /// </summary>
    public Accessibility Accessibility { get; init; }

    /// <summary>
    /// The block body of this accessor, if it has one.
    /// Null for auto or expression-bodied accessors.
    /// </summary>
    public Block? Body { get; init; }

    /// <summary>
    /// The expression body of this accessor, if it has one.
    /// Null for auto or block-bodied accessors.
    /// </summary>
    public Expression? ExpressionBody { get; init; }
}