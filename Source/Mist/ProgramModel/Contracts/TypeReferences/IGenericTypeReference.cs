namespace Mist.ProgramModel.Contracts.TypeReferences;

/// <summary>
/// A generic instantiation (e.g., List&lt;int&gt;, Dictionary&lt;string, int&gt;).
/// </summary>
public interface IGenericTypeReference : ITypeReference
{
    /// <summary>
    /// The generic type definition being instantiated.
    /// </summary>
    ITypeDefinition Definition { get; }

    /// <summary>
    /// The type arguments substituted for the type parameters.
    /// </summary>
    IReadOnlyList<ITypeReference> TypeArguments { get; }
}