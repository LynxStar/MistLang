namespace Mist.ProgramModel.Contracts.Types;

/// <summary>
/// Its extension blocks are active only where it is imported. Its types live in the flat
/// <see cref="IProgramModel.TypesByFQN"/>.
/// </summary>
public interface INamespace
{
    string FullyQualifiedName { get; }

    IReadOnlyDictionary<IMistType, IReadOnlyList<IExtensionBlock>> Extensions { get; }
}