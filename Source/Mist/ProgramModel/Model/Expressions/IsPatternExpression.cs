using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Patterns;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Expressions;

/// <summary>
/// A pattern test written in source, as <c>value is int i</c>. An expression rather than a statement
/// form, so a binding it introduces is usable in the rest of the condition that introduced it.
/// </summary>
internal sealed class IsPatternExpression(
    SyntaxNode node, Expression scrutinee, Pattern pattern, IMistType booleanType) : Expression(node)
{
    public Expression Scrutinee { get; } = scrutinee;

    public Pattern Pattern { get; } = pattern;

    public override IMistType Type { get; } = booleanType;
}