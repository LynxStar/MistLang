namespace Mist.ProgramModel.Model.Enums;

/// <summary>
/// Which constructor a constructor delegates to before running its own body.
/// </summary>
public enum ConstructorChainKind
{
    None,

    This,

    Base,
}