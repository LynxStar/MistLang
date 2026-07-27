namespace Mist.ProgramModel.Model.Enums;

/// <summary>
/// Which constructor a constructor delegates to before running its own body.
/// </summary>
public enum ConstructorChainKind
{
    /// <summary>No chain: the constructor delegates to nothing.</summary>
    None,

    /// <summary>Delegates to another constructor on the same type (<c>: this(...)</c>).</summary>
    This,

    /// <summary>Delegates to a base type constructor (<c>: base(...)</c>).</summary>
    Base,
}