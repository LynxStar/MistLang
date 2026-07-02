namespace Mist.ProgramModel.Contracts;

/// <summary>
/// A type definition that can have member declarations and base types.
/// This is the common interface for classes, structs, and abstracts —
/// types that participate in inheritance and declare methods/properties.
/// </summary>
public interface IMemberTypeDefinition : ITypeDefinition
{
    /// <summary>
    /// Abstracts that this type implements or refines.
    /// </summary>
    IReadOnlyList<ITypeDefinition> BaseAbstractTypes { get; }

    /// <summary>
    /// Methods declared on this type.
    /// </summary>
    IReadOnlyList<IMethodDefinition> Methods { get; }

    /// <summary>
    /// Properties declared on this type.
    /// </summary>
    IReadOnlyList<IPropertyDefinition> Properties { get; }
}