using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Patterns;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Expressions;

/// <summary>
/// A <c>scrutinee switch { ... }</c> expression, whose type is the one type every arm produces.
/// <para>
/// <see cref="Arms"/> is in SOURCE ORDER, which is load-bearing rather than incidental: the first arm
/// whose pattern matches and whose guard holds wins, so reordering two overlapping arms -- two
/// relational ones especially -- changes what the expression means.
/// </para>
/// </summary>
internal sealed class SwitchExpression(
    SyntaxNode node,
    Expression scrutinee,
    IReadOnlyList<SwitchArm> arms,
    IMistType resultType) : Expression(node)
{
    public Expression Scrutinee { get; } = scrutinee;

    public IReadOnlyList<SwitchArm> Arms { get; } = arms;

    public override IMistType Type { get; } = resultType;
}

internal sealed class SwitchArm(SyntaxNode node, Pattern pattern, Expression? guard, Expression result)
{
    public SyntaxNode Node { get; } = node;

    public Pattern Pattern { get; } = pattern;

    /// <summary>
    /// The <c>when</c> condition, or null where the arm has none. Evaluated only after the pattern
    /// matched, which is what lets it read the pattern's bindings -- and what will stop a guarded arm
    /// counting toward coverage, since holding is not something the pattern alone decides.
    /// </summary>
    public Expression? Guard { get; } = guard;

    public Expression Result { get; } = result;
}