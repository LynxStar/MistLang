using Mist.ProgramModel.Model.Expressions;

namespace Mist.ProgramModel.Model.Statements;

/// <summary>
/// A foreach loop: <c>foreach (T name in collection) { body }</c>.
/// </summary>
internal sealed class ForEachStatement(
    LocalVariable variable,
    Expression collection,
    Block body) : Statement
{
    public LocalVariable Variable { get; } = variable;

    public Expression Collection { get; } = collection;

    public Block Body { get; } = body;
}