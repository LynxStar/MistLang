namespace Mist.ProgramModel.Contracts.Types;

/// <summary>
/// Classes and structs.
/// </summary>
public interface IConcreteType : IMemberType
{
    StorageLayout? ImportedStorage { get; }

    bool IsSealed { get; }

    /// <summary>
    /// A class's base is a class and a struct's a struct.
    /// </summary>
    IMistType? BaseType { get; }

    IReadOnlyList<IField> Fields { get; }

    IReadOnlyList<IConstructor> Constructors { get; }

    IReadOnlyList<ICastOperator> CastOperators { get; }
}