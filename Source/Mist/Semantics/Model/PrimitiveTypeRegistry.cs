using System.Collections.Concurrent;
using Mist.Semantics.Contracts;

namespace Mist.Semantics.Model;

/// <summary>
/// Migration-window service locator that maps primitive keyword lexemes
/// (e.g., <c>"int"</c>, <c>"bool"</c>, <c>"string"</c>) to the
/// corresponding Standard Library <see cref="IType"/> instances once the
/// stdlib has been registered in a semantic model.
///
/// <para>
/// Task 32 of the Mist Standard Library spec introduces this registry as
/// the compat shim that lets the legacy <see cref="Types.PrimitiveType"/>
/// / <see cref="Types.StringType"/> accessors return the new stdlib
/// <see cref="IType"/> instances (preserving reference equality per
/// Requirement 8.5) while individual call sites are migrated in Stages 5
/// and 6. The registry exists for the duration of the migration; the
/// final Stage 6 cleanup (Tasks 49–52) deletes the legacy types, their
/// accessors, and this registry.
/// </para>
///
/// <para>
/// Population is performed by <see cref="SemanticModelBuilder"/> at the
/// end of Pass 1 once every stdlib type symbol has been registered into
/// the <c>Standard</c> namespace scope. Before population, the legacy
/// <see cref="Types.PrimitiveType"/> singletons remain the canonical
/// instances (used by unit tests that build a semantic model without
/// loading stdlib source). After population, the accessors return the
/// stdlib <see cref="Types.StructType"/> / <see cref="Types.ClassType"/>
/// instances so reference equality holds throughout the rest of the
/// pipeline.
/// </para>
///
/// <para>
/// Thread safety: backed by a <see cref="ConcurrentDictionary{TKey,TValue}"/>
/// so concurrent semantic-model builds (e.g., parallel test execution)
/// do not corrupt the mapping.
/// </para>
/// </summary>
internal static class PrimitiveTypeRegistry
{
    private static readonly ConcurrentDictionary<string, IType> Mapping =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Process-wide synchronisation handle for tests that need to assert
    /// invariants of the migration-window shim (e.g., reference equality
    /// between <see cref="Types.PrimitiveType.Int"/> and a model-scoped
    /// stdlib lookup). Parallel builds still execute concurrently —
    /// production code never takes this lock — but a test that
    /// specifically verifies registry state should hold this lock for the
    /// duration of the Build-plus-assertion critical section so a
    /// parallel <see cref="SemanticModelBuilder.Build"/> call does not
    /// reset the registry between the two steps.
    /// </summary>
    internal static readonly object Lock = new();

    /// <summary>
    /// Installs the stdlib <see cref="IType"/> for a primitive keyword.
    /// Called by <see cref="SemanticModelBuilder"/> at the end of Pass 1
    /// for each entry in <see cref="KeywordAliases.Map"/> whose stdlib
    /// type symbol was successfully registered.
    /// </summary>
    /// <param name="keyword">
    /// The primitive keyword lexeme (the key used by
    /// <see cref="KeywordAliases.Map"/>), e.g. <c>"int"</c>,
    /// <c>"bool"</c>, <c>"string"</c>.
    /// </param>
    /// <param name="type">
    /// The fully resolved stdlib <see cref="IType"/> instance (the
    /// <see cref="Types.StructType"/> or <see cref="Types.ClassType"/>
    /// registered for the mapped <c>Standard.*</c> type).
    /// </param>
    public static void Register(string keyword, IType type)
    {
        Mapping[keyword] = type;
    }

    /// <summary>
    /// Returns the stdlib <see cref="IType"/> registered for the given
    /// primitive keyword, or <c>null</c> when the stdlib has not yet been
    /// loaded for any in-flight compilation. A <c>null</c> return is the
    /// signal for the legacy <see cref="Types.PrimitiveType"/> /
    /// <see cref="Types.StringType"/> accessors to fall back to their
    /// private singletons.
    /// </summary>
    public static IType? TryGet(string keyword)
    {
        return Mapping.TryGetValue(keyword, out var type) ? type : null;
    }

    /// <summary>
    /// Clears all registered mappings. Intended for test hygiene —
    /// production code never calls this.
    /// </summary>
    internal static void Reset()
    {
        Mapping.Clear();
    }
}
