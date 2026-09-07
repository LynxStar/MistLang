using Mist.ProgramModel.Contracts.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Expressions;

/// <summary>
/// A backtick pattern: <c>`\d+`</c>.
/// <para>
/// Its own node rather than a <see cref="LiteralExpression"/> holding a string, because the two
/// differ in what they mean by the same characters. A string literal IS its characters; a regex
/// literal is a compiled matcher built FROM them, so the target has to construct where a string
/// only has to be quoted.
/// </para>
/// </summary>
internal sealed class RegexLiteralExpression(SyntaxNode node, string pattern, IMistType type) : Expression(node)
{
    /// <summary>
    /// The pattern as a STRING LITERAL'S BODY -- the characters that would sit between quotes --
    /// which is the same form <see cref="LiteralExpression"/> holds a string in.
    /// <para>
    /// One form for two spellings, and the conversion happens where they differ. Backticked text is
    /// raw regex, so a lone backslash there means a backslash and has to be doubled to survive being
    /// read back as a string; a quoted shorthand is already written that way.
    /// </para>
    /// </summary>
    public string Pattern { get; } = pattern;

    public override IMistType Type { get; } = type;
}