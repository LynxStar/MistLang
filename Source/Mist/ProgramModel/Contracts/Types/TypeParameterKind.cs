namespace Mist.ProgramModel.Contracts.Types;

/// <summary>
/// Distinguishes the kind of a type parameter on a generic declaration.
/// </summary>
public enum TypeParameterKind
{
    /// <summary>
    /// A regular type parameter that carries a type (e.g., <c>T</c> in <c>List&lt;T&gt;</c>).
    /// </summary>
    Regular,

    /// <summary>
    /// A dependent type parameter that carries a compile-time value
    /// (e.g., <c>N:int</c> in <c>Buffer&lt;N:int&gt;</c>).
    /// </summary>
    Dependent,
}