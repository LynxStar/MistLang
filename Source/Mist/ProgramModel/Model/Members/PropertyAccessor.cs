using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;

namespace Mist.ProgramModel.Model.Members;

internal sealed class PropertyAccessor
{
    public bool IsAuto { get; init; }

    public bool IsInit { get; init; }

    /// <summary>
    /// The default, <see cref="Accessibility.Public"/>, inherits the property's accessibility.
    /// </summary>
    public Accessibility Accessibility { get; init; }

    /// <summary>
    /// The promise the accessor states for itself (<c>{ local readonly get; local set; }</c>), which
    /// replaces its declaration's wherever a promise is read; null when it states none.
    /// </summary>
    public MemberModifiers? OwnModifiers { get; init; }

    public Block? Body { get; init; }

    /// <summary>
    /// Settable for the same reason <see cref="Property.Initializer"/> is. A block body needs no
    /// equivalent, because <see cref="Block"/> is filled in place.
    /// </summary>
    public Expression? ExpressionBody { get; internal set; }
}