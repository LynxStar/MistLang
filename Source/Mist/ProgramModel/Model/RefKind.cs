namespace Mist.ProgramModel.Model;

/// <summary>
/// The ref kind of a parameter or return type.
/// Orthogonal to ownership — a parameter has exactly one of each.
/// </summary>
internal enum RefKind
{
    /// <summary>No ref annotation. Standard pass-by-value / return-by-value.</summary>
    None,

    /// <summary>ref — mutable reference to a storage location.</summary>
    Ref,

    /// <summary>ref readonly — readonly reference to a storage location.</summary>
    RefReadonly,
}