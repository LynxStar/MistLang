using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Contracts;

/// <summary>
/// Resolves types from loaded .NET assemblies into IMistType instances
/// that live in the ProgramModel alongside source-declared types.
/// </summary>
public interface IAmbientTypeProvider
{
    /// <summary>
    /// Attempts to resolve a type by namespace and name.
    /// Returns the projected IMistType if found, null otherwise.
    /// If the type maps to an existing Mist Standard Library type (alias),
    /// returns that instance silently without creating a new one.
    /// </summary>
    IMistType? ResolveType(string namespaceName, string typeName, Model.ProgramModel programModel);
}
