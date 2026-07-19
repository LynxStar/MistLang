using Mist.ProgramModel.Contracts.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Expressions;

/// <summary>
/// An interpolated string expression (<c>$"text {expr} text"</c>).
/// Contains a sequence of parts that are either literal text or embedded expressions.
/// </summary>
internal sealed class InterpolatedStringExpression(
    SyntaxNode node,
    IReadOnlyList<InterpolatedStringPart> parts,
    IMistType type) : Expression(node)
{
    public IReadOnlyList<InterpolatedStringPart> Parts { get; } = parts;

    public override IMistType Type { get; } = type;
}

/// <summary>
/// A part of an interpolated string.
/// </summary>
internal abstract class InterpolatedStringPart;

/// <summary>
/// A literal text segment within an interpolated string.
/// </summary>
internal sealed class InterpolatedStringText(string text) : InterpolatedStringPart
{
    public string Text { get; } = text;
}

/// <summary>
/// An embedded expression within an interpolated string (<c>{expr}</c>).
/// </summary>
internal sealed class InterpolatedStringHole(Expression expression) : InterpolatedStringPart
{
    public Expression Expression { get; } = expression;
}