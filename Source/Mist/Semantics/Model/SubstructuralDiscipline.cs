namespace Mist.Semantics.Model;

/// <summary>
/// The substructural discipline of a type declaration.
/// Determines compiler enforcement rules for Drop() invocation.
/// </summary>
public enum SubstructuralDiscipline
{
    /// <summary>No substructural modifier. Drop only in with-blocks (0 to many).</summary>
    None,

    /// <summary>affine — Drop only in with-blocks, at most once. Implies owned.</summary>
    Affine,

    /// <summary>linear — Drop always at scope exit, exactly once. Implies owned.</summary>
    Linear,

    /// <summary>relevant — Drop always at scope exit for every reference (1+, idempotent). Aliasable.</summary>
    Relevant,
}
