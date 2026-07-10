using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Contracts;

/// <summary>
/// A retroactive conformance declaration (with Foo : IBar { ... }).
/// Declares that a type conforms to an abstract with explicit member mappings.
/// Scoped to its declaring namespace -- only active when that namespace is imported.
/// </summary>
public interface IConformanceDeclaration
{
    /// <summary>
    /// The type that is declared to conform.
    /// </summary>
    IMistType ConformingType { get; }

    /// <summary>
    /// The abstract type being conformed to.
    /// </summary>
    IMistType AbstractType { get; }

    /// <summary>
    /// The namespace this conformance was declared in.
    /// </summary>
    string NamespaceName { get; }

    /// <summary>
    /// The member mappings that satisfy the abstract's requirements.
    /// </summary>
    IReadOnlyList<IMemberMapping> Mappings { get; }
}

/// <summary>
/// A single member mapping within a conformance declaration.
/// </summary>
public interface IMemberMapping
{
    /// <summary>
    /// The name of the member on the conforming type.
    /// </summary>
    string SourceName { get; }

    /// <summary>
    /// The name of the slot on the abstract type being satisfied.
    /// </summary>
    string TargetName { get; }
}