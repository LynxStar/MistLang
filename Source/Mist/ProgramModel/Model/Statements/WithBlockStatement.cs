using Mist.ProgramModel.Model.Expressions;

namespace Mist.ProgramModel.Model.Statements;

/// <summary>
/// A context scope: <c>with(expr) { body }</c>, which makes a value ambient for the block and drops
/// it on the way out.
/// <para>
/// The value is evaluated ONCE, before the block, and the same instance is both pushed and dropped.
/// Holding it as an expression rather than a local is what keeps that a fact about the model: how
/// the single evaluation is achieved is the target's problem, and C# needs a temporary for it where
/// another backend might not.
/// </para>
/// </summary>
internal sealed class WithBlockStatement(Expression value, Block body) : Statement
{
    public Expression Value { get; } = value;

    public Block Body { get; } = body;
}