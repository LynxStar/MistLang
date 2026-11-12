namespace Mist.ProgramModel.Model.Enums;

/// <summary>
/// Determines how the compiler enforces Drop() invocation.
/// </summary>
public enum SubstructuralDiscipline
{
    /// <summary>No substructural modifier. Drop only in with-blocks (0 to many).</summary>
    None,

    /// <summary>Drop only in with-blocks, at most once. Implies owned.</summary>
    Affine,

    /// <summary>Drop always at scope exit, exactly once. Implies owned.</summary>
    Linear,

    /// <summary>Drop always at scope exit for every reference (1+, idempotent). Aliasable.</summary>
    Relevant,
}