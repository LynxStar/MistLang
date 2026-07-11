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
    IMistType? ResolveType(string namespaceName, string typeName);

    /// <summary>
    /// Arity-aware overload. Distinguishes generic types sharing the same base
    /// name (e.g., Func`1 vs Func`2). typeParameterCount is the number of
    /// generic parameters on the CLR type definition.
    /// </summary>
    IMistType? ResolveType(string namespaceName, string typeName, int typeParameterCount);

    /// <summary>
    /// Returns all type names available in a namespace from loaded assemblies.
    /// Includes CLR arity-suffixed names (e.g., List`1, Dictionary`2).
    /// Used for ambiguity detection and name-existence checks without triggering
    /// full type projection.
    /// </summary>
    IReadOnlyList<string> GetTypeNamesInNamespace(string namespaceName);

    /// <summary>
    /// Returns extension method descriptors declared in the given namespace.
    /// Extension methods are collected during assembly scanning and cached
    /// per-namespace for subsequent calls.
    /// </summary>
    IReadOnlyList<AmbientExtensionMethodInfo> GetExtensionMethodsInNamespace(
        string namespaceName);
}