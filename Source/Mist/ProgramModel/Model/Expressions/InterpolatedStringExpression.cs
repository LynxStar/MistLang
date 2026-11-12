using Mist.ProgramModel.Contracts.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Expressions;

internal sealed class InterpolatedStringExpression(
    SyntaxNode node,
    IReadOnlyList<InterpolatedStringPart> parts,
    IMistType type) : Expression(node)
{
    public IReadOnlyList<InterpolatedStringPart> Parts { get; } = parts;

    public override IMistType Type { get; } = type;
}

internal abstract class InterpolatedStringPart;

internal sealed class InterpolatedStringText(string text) : InterpolatedStringPart
{
    public string Text { get; } = text;
}

internal sealed class InterpolatedStringHole(Expression expression) : InterpolatedStringPart
{
    public Expression Expression { get; } = expression;
}