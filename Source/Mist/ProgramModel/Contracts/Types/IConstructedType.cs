namespace Mist.ProgramModel.Contracts.Types;

public interface IConstructedType : IMistType
{
    IMistType Definition { get; }

    IReadOnlyList<IMistType> TypeArguments { get; }

    /// <summary>
    /// The instantiation of the declaring type this nested type is reached through; null for a
    /// construction of a top-level definition.
    /// </summary>
    IConstructedType? Enclosing { get; }
}