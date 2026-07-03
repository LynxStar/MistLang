namespace Mist.ProgramModel.Contracts;

/// <summary>
/// A constructor declared on a type.
/// </summary>
public interface IConstructor
{
    /// <summary>The constructor parameters.</summary>
    IReadOnlyList<IParameter> Parameters { get; }

    /// <summary>The modifiers declared on this constructor.</summary>
    MemberModifiers Modifiers { get; }
}