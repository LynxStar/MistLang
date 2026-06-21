namespace Mist.Semantics.Model;

/// <summary>
/// Classifies how an <c>as</c> expression conversion is handled at compile time.
/// Recorded on the AST node by the type checker so the emitter knows whether to
/// emit a direct conversion or a runtime check.
/// </summary>
public enum AsConversionKind
{
    /// <summary>
    /// The conversion is provably safe at compile time (identity, widening, interface
    /// satisfaction). Result type is <c>T</c> — no runtime check needed.
    /// </summary>
    Provable,

    /// <summary>
    /// The conversion cannot be proven safe at compile time (narrowing, union extraction,
    /// unknown narrowing). Result type is <c>T | void</c> — runtime check emitted.
    /// </summary>
    NonProvable,
}
