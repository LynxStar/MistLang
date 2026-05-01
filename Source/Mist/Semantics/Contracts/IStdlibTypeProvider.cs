namespace Mist.Semantics.Contracts;

/// <summary>
/// Provides lookup of Standard Library types by their simple name
/// (e.g., <c>"Int32"</c>, <c>"String"</c>, <c>"Boolean"</c>).
///
/// <para>
/// Consumed by <c>CSharpAmbientTypeResolver</c> (Task 42) so that
/// ambient-type signatures reference stdlib <see cref="IType"/> instances
/// rather than legacy <c>PrimitiveType.*</c> / <c>StringType.Instance</c>
/// singletons. This ensures the semantic model sees only Mist types —
/// never target-language types — per Requirement 9.9 and 9.10.
/// </para>
///
/// <para>
/// The provider is typically backed by the <c>Standard</c> namespace scope
/// of a built <see cref="ISemanticModel"/>. When the stdlib has not been
/// loaded (e.g., in unit tests that don't load stdlib sources), the
/// provider falls back to the legacy <c>PrimitiveType.*</c> singletons
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
}
