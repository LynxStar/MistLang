using Mist.ProgramModel.Contracts.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Patterns;

/// <summary>
/// A pattern that matches when the scrutinee matches a regex, in FULL: <c>s is `^[a-z]+$`</c>.
/// <para>
/// A PREDICATE WEARING A PATTERN'S CLOTHES, which is what makes it unlike every other pattern here.
/// The others describe a shape a target language can test structurally; this one can only be
/// answered by calling something, so emission cannot leave it in pattern position -- see
/// <c>BodyEmitter.EmitPattern</c>, which binds a temporary and hoists the call into a guard.
/// </para>
/// </summary>
internal sealed class RegexPattern(SyntaxNode node, IMistType scrutineeType, string pattern)
    : Pattern(node)
{
    /// <summary>
    /// The pattern as a string literal's body, the form
    /// <see cref="Expressions.RegexLiteralExpression.Pattern"/> holds. Full-match anchoring is the
    /// BACKEND's and is not recorded here: how a partial-match engine is asked a full-match question
    /// is a fact about that engine.
    /// </summary>
    public string Pattern { get; } = pattern;

    public override IMistType NarrowedType { get; } = scrutineeType;

    public override LocalVariable? Binding => null;
}