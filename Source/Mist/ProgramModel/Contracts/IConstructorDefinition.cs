namespace Mist.ProgramModel.Contracts;

/// <summary>
/// A constructor declared on a type.
/// </summary>
public interface IConstructorDefinition
{
    /// <summary>The constructor parameters.</summary>
    IReadOnlyList<IParameterDefinition> Parameters { get; }
}