using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// Mist's <c>regex</c> IS System.Text.RegularExpressions.Regex, and what Mist spells differently is
/// rewritten at the use site.
/// <para>
/// NEVER A WRAPPER. An intrinsic declaration says the target already has this and the emitter is to
/// reach it -- a Mist-side duplicate would be a type no framework API accepts, and the same reasoning
/// applied to String or Int32 would have the backend shipping its own copy of every primitive.
/// <see cref="ClrProjection"/> states it for the Standard abstracts and it holds here.
/// </para>
/// </summary>
internal static class RegexLowering
{
    public static readonly string ClrType = ClrSpelling.Spelling(WellKnownTypes.Regex)!;

    public static bool Is(IMistType? type)
    {
        return type is not null && type.FullyQualifiedName == WellKnownTypes.Regex.FullyQualifiedName;
    }

    /// <summary>
    /// A pattern as a C# string literal. The model already holds it as a literal's BODY, so this
    /// only puts the quotes back.
    /// </summary>
    public static string Quote(string patternBody)
    {
        return $"\"{patternBody}\"";
    }

    /// <summary>
    /// Compiled because a Mist regex is built from a pattern that is constant at nearly every site,
    /// so the cost is paid once at construction rather than per match.
    /// </summary>
    public static string Construct(string csharpPatternExpression)
    {
        return $"new {ClrType}({csharpPatternExpression}, {ClrType}Options.Compiled | {InvariantCulture.RegexOption})";
    }

    /// <summary>
    /// The pattern Mist's <c>Pattern</c> reads, which the CLR spells as ToString.
    /// </summary>
    public static string PatternOf(string receiverExpression)
    {
        return $"{receiverExpression}.ToString()";
    }

    /// <summary>
    /// The pattern a FULL match needs, which Mist derives rather than calls: the CLR has no
    /// full-match API, so the pattern is wrapped and matched partially. Grouped with <c>(?:</c> so a
    /// top-level alternation is anchored as a whole rather than only its first branch. Anchored with
    /// <c>\A</c> and <c>\z</c> because <c>$</c> also matches before a final newline, and both
    /// <c>^</c> and <c>$</c> match at every line under multiline mode. Closed through
    /// <c>(?#</c>, a newline and <c>(?#)</c> because a pattern under <c>(?x)</c> can end inside a
    /// <c>#</c> comment, which would swallow a bare <c>)</c>: the newline ends that comment and the
    /// second <c>(?#</c> reopens one, while a pattern ending anywhere else reads the whole run as one
    /// comment. The pattern must parse alone, which analysis's <c>RegexSyntax</c> explains and enforces
    /// for a written one; a constructed regex has parsed already.
    /// </summary>
    public static string FullMatchPattern(string patternExpression)
    {
        return $@"""\\A(?:"" + {patternExpression} + ""(?#\n(?#))\\z""";
    }

    public static string IsFullMatch(string inputExpression, string patternExpression)
    {
        return $"{ClrType}.IsMatch({inputExpression}, {FullMatchPattern(patternExpression)}, {InvariantCulture.RegexOption})";
    }
}