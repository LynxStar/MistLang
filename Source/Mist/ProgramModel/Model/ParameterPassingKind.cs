namespace Mist.ProgramModel.Model;

/// <summary>
/// How a parameter is passed to a method.
/// </summary>
public enum ParameterPassingKind
{
    /// <summary>Standard pass-by-value.</summary>
    ByValue,

    /// <summary>Pass by mutable reference.</summary>
    ByRef,

    /// <summary>Pass by readonly reference.</summary>
    ByRefReadonly,
}