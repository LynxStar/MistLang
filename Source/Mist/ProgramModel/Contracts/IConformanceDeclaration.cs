using Mist.ProgramModel.Contracts.TypeReferences;

namespace Mist.ProgramModel.Contracts;

/// <summary>
/// A with-block conformance declaration that establishes a type satisfies an
/// abstract type obligation via explicit member mappings.
/// </summary>
public interface IConformanceDeclaration
{
    /// <summary>
    /// The concrete type that conforms.
    /// </summary>
    ITypeReference ConcreteType { get; }

    /// <summary>
    /// The abstract type being satisfied.
    /// </summary>
    ITypeReference AbstractType { get; }

    /// <summary>
    /// The member mappings from concrete type members to abstract type slots.
    /// </summary>
    IReadOnlyList<IMemberMapping> Mappings { get; }

    /// <summary>
    /// The source location where this conformance was declared.
    /// </summary>
    SourceOrigin? Origin { get; }
}