namespace Mist.ProgramModel.Contracts.TypeReferences;

/// <summary>
/// A span type (e.g., Span&lt;int&gt;).
/// </summary>
public interface ISpanTypeReference : ITypeReference
{
    /// <summary>
    /// The element type of the span.
    /// </summary>
    ITypeReference ElementType { get; }
}