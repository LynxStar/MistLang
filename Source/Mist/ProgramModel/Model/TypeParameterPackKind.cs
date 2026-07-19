namespace Mist.ProgramModel.Model;

/// <summary>
/// Classifies how a type-parameter pack is supplied at its use site, or
/// <see cref="None"/> when the type parameter is not a pack at all.
///
/// A pack is sugar for a dependent type parameter whose value domain is the
/// comptime metatype array <c>Type[]</c> (unknown length) or <c>Type[N]</c>
/// (fixed length). The two surface spellings <c>params TArgs</c> and
/// <c>Type[]:TArgs</c> collapse to this classification once the enclosing
/// type-parameter list is known.
/// </summary>
public enum TypeParameterPackKind
{
    /// <summary>
    /// Not a pack — an ordinary regular (<c>T</c>) or dependent (<c>N:int</c>)
    /// type parameter.
    /// </summary>
    None,

    /// <summary>
    /// The list's single bracket-free pack. Produced by the sugared
    /// <c>params TArgs</c> spelling, or by a lone <c>Type[]:TArgs</c> pack that is
    /// the only pack in its list (inverse sugar). Constructed without brackets at
    /// the use site.
    /// </summary>
    Unbracketed,

    /// <summary>
    /// An explicit <c>Type[]:TArgs</c> pack that is not its list's lone pack. Its
    /// arguments are supplied in brackets (<c>[...]</c>) at the use site.
    /// </summary>
    Bracketed,
}