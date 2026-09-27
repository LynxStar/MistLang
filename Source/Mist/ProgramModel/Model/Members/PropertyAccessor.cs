using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;

namespace Mist.ProgramModel.Model.Members;

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
    /// The promise the accessor states for itself (<c>{ local readonly get; local set; }</c>), which
    /// replaces its declaration's wherever a promise is read; null when it states none.
    /// </summary>
    public MemberModifiers? OwnModifiers { get; init; }

    /// <summary>
    /// The block body of this accessor, if it has one.
    /// Null for auto or expression-bodied accessors.
    /// </summary>
    public Block? Body { get; init; }

    /// <summary>
    /// The expression body of this accessor, if it has one.
    /// Null for auto or block-bodied accessors.
    /// <para>
    /// Settable for the same reason <see cref="Property.Initializer"/> is: Phase 2 parks the
    /// unbound expression, Phase 3 replaces it. A block body needs no equivalent, because
    /// <see cref="Block"/> is filled in place.
    /// </para>
    /// </summary>
    public Expression? ExpressionBody { get; internal set; }
}