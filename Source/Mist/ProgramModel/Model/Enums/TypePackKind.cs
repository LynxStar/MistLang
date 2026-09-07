namespace Mist.ProgramModel.Model.Enums;

/// <summary>
/// How a type parameter's pack, if it has one, is supplied at the use site.
/// </summary>
public enum TypePackKind
{
    None,

    /// <summary>
    /// Supplied bracket-free, absorbing whatever the fixed slots around it do not claim. At most one
    /// per parameter list: two of them could divide a flat argument list at any point, so a list
    /// carrying both has no reading rather than an unsupported one.
    /// </summary>
    Unbracketed,

    Bracketed,
}