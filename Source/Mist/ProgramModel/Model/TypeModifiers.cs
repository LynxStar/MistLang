namespace Mist.ProgramModel.Model;

/// <summary>
/// Modifiers that can be applied to a type definition.
/// </summary>
[Flags]
public enum TypeModifiers
{
    None = 0,
    Readonly = 1 << 0,
    Owned = 1 << 1,
    Intrinsic = 1 << 2,
    RefStruct = 1 << 3,
    Sealed = 1 << 4,
    Abstract = 1 << 5,
}