namespace Mist.ProgramModel.Contracts.Types;

/// <summary>
/// A constructed (closed) generic type — a type definition applied to concrete type arguments
/// (e.g., <c>List&lt;int&gt;</c>, <c>Dictionary&lt;string, int&gt;</c>).
/// </summary>
public interface IConstructedType : IMistType
{
    /// <summary>
    /// The unparameterized type definition (e.g., <c>List</c>).
    /// </summary>
    IMistType Definition { get; }

    /// <summary>
    /// The resolved type arguments applied to the definition.
    /// </summary>
    IReadOnlyList<IMistType> TypeArguments { get; }
}