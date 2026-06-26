namespace Mist.Semantics.Contracts;

/// <summary>
/// Provides lookup of Standard Library types by their simple name
/// (e.g., <c>"Int32"</c>, <c>"String"</c>, <c>"Boolean"</c>).
///
/// <para>
/// Consumed by <c>CSharpAmbientTypeResolver</c> (Task 42) so that
/// ambient-type signatures reference stdlib <see cref="IType"/> instances
/// rather than legacy fallback singletons. This ensures the semantic model sees only Mist types —
/// never target-language types — per Requirement 9.9 and 9.10.
/// </para>
///
/// <para>
/// The provider is typically backed by the <c>Standard</c> namespace scope
/// of a built <see cref="ISemanticModel"/>. When the stdlib has not been
/// loaded (e.g., in unit tests that don't load stdlib sources), the
/// provider falls back to the legacy fallback singletons
/// so existing tests continue to work.
/// </para>
/// </summary>
public interface IStdlibTypeProvider
{
    /// <summary>
    /// Returns the stdlib <see cref="IType"/> for the given simple name
    /// (e.g., <c>"Int32"</c>, <c>"String"</c>, <c>"Boolean"</c>).
    /// </summary>
    /// <param name="simpleName">
    /// The unqualified type name within the <c>Standard</c> namespace.
    /// </param>
    /// <returns>
    /// The corresponding <see cref="IType"/> instance, or a legacy
    /// fallback when the stdlib is not loaded.
    /// </returns>
    IType LookupStandardType(string simpleName);

    /// <summary>
    /// Returns the stdlib <see cref="IType"/> for the given simple name, disambiguating
    /// between same-named generic families of different arity (e.g., the
    /// <c>Standard.Func</c> family, which declares 17 variants from <c>Func&lt;TResult&gt;</c>
    /// through <c>Func&lt;T1, ..., T16, TResult&gt;</c>).
    /// </summary>
    /// <param name="simpleName">
    /// The unqualified type name within the <c>Standard</c> namespace.
    /// </param>
    /// <param name="typeArgumentCount">
    /// The number of type parameters the requested variant declares. When negative,
    /// arity is ignored and the first match is returned (legacy behavior).
    /// </param>
    /// <returns>
    /// The corresponding <see cref="IType"/> instance with the requested arity, or a
    /// legacy fallback when the stdlib is not loaded.
    /// </returns>
    IType LookupStandardType(string simpleName, int typeArgumentCount) => LookupStandardType(simpleName);
}