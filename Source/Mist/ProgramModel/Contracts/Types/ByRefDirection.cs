namespace Mist.ProgramModel.Contracts.Types;

/// <summary>
/// The by-reference calling convention a parameter requires AT A CALL SITE.
/// <para>
/// Distinct from the <c>ref</c> and <c>readonly</c> modifiers, which say what the DECLARATION is.
/// Mist has one by-ref spelling and C# has three, so a call site cannot carry the answer and the
/// callee is the only record of it -- and for a CLR parameter the convention is not derivable from
/// the modifiers at all. <c>[In] ref</c> and a genuine <c>in</c> reach the model as the same pair of
/// flags and demand opposite keywords, which is what this enum exists to keep apart.
/// </para>
/// </summary>
public enum ByRefDirection
{
    /// <summary>Passed by value. Takes no keyword.</summary>
    None,

    /// <summary>
    /// Mutable by-reference: C# <c>ref</c>, and also <c>[In] ref</c> and <c>[In, Out] ref</c>, which
    /// are interop annotations on a parameter that is still an ordinary mutable <c>ref</c>. Requires
    /// <c>ref</c> and rejects both alternatives.
    /// </summary>
    Ref,

    /// <summary>
    /// Readonly by-reference: C# <c>in</c> and <c>ref readonly</c>, and Mist's <c>readonly ref</c>.
    /// Takes <c>in</c>, which both accept; <c>ref</c> is refused outright against a readonly
    /// location, so this is a correctness distinction rather than a stylistic one.
    /// </summary>
    In,

    /// <summary>
    /// The callee assigns: C# <c>out</c>. Requires <c>out</c>, since the caller's prior value is not
    /// live and C# refuses <c>ref</c> in its place.
    /// </summary>
    Out,
}