using Mist.ProgramModel.Model.Expressions;

namespace Mist.ProgramModel.Model.Statements;

/// <summary>
/// A local variable declaration (<c>T x = init;</c> or <c>var x = init;</c>).
/// </summary>
internal sealed class LocalDeclarationStatement(LocalVariable local, Expression? initializer) : Statement
{
    /// <summary>
    /// The declared local variable.
    /// </summary>
    public LocalVariable Local { get; } = local;

    /// <summary>
    /// The resolved initializer expression, or null for an uninitialized declaration.
    /// </summary>
    public Expression? Initializer { get; } = initializer;
}