using Mist.ProgramModel.Model.Expressions;

namespace Mist.ProgramModel.Model.Statements;

/// <summary>
/// A statement consisting of a single expression (e.g., a method call like
/// <c>Console.WriteLine("hello");</c>).
/// </summary>
internal sealed class ExpressionStatement(Expression expression) : Statement
{
    /// <summary>
    /// The bound expression.
    /// </summary>
    public Expression Expression { get; } = expression;
}