using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Contracts;

public interface IProgramModel
{
    /// <summary>
    /// The primary type registry: all name resolution ultimately resolves to its keys.
    /// </summary>
    IReadOnlyDictionary<string, IMistType> TypesByFQN { get; }

    IReadOnlyList<IConformanceDeclaration> Conformances { get; }

    IReadOnlyList<IAssembly> Assemblies { get; }

    /// <summary>
    /// Null only for a model built without a compilation to describe.
    /// </summary>
    IAssembly? SelfAssembly { get; }
}