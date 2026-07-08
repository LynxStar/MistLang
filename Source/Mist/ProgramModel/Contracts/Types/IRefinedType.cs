namespace Mist.ProgramModel.Contracts.Types;

/// <summary>
/// A refinement type — a base type narrowed by a predicate constraint
/// (e.g., <c>int >= 0 and &lt;= 100</c>).
/// </summary>
public interface IRefinedType : IMistType
{
    /// <summary>
    /// The underlying type being refined.
    /// </summary>
    IMistType BaseType { get; }
}