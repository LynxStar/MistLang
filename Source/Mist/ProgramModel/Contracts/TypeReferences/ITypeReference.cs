namespace Mist.ProgramModel.Contracts.TypeReferences;

/// <summary>
/// Base interface for all resolved type references. Use pattern matching to
/// determine the specific form (named, array, union, generic, etc.).
/// </summary>
public interface ITypeReference
{
    /// <summary>
    /// The display name of this type (e.g., "int", "List&lt;string&gt;", "int | void").
    /// </summary>
    string DisplayName { get; }
}