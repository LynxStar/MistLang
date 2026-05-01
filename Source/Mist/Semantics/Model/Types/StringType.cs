using Mist.Semantics.Contracts;

namespace Mist.Semantics.Model.Types;

/// <summary>
/// The string type. String is a class (reference type) in .NET.
///
/// <para>
/// <b>Migration status (Task 32 of the Mist Standard Library spec):</b>
/// <see cref="Instance"/> has been widened from a <c>StringType</c> field
/// to an <see cref="IType"/> property. After
/// <see cref="SemanticModelBuilder"/> finishes Pass 1,
/// <c>Standard.String</c> is registered into
/// <see cref="PrimitiveTypeRegistry"/> and <see cref="Instance"/> returns
/// that stdlib <see cref="ClassType"/> instance. Before registration — or
/// in test harnesses that never load the stdlib — the accessor falls back
/// to the legacy singleton so existing code paths continue to function.
/// </para>
/// <para>
/// Reference equality (Requirement 8.5) is preserved: once the stdlib
/// <c>Standard.String</c> has been registered, every access to
/// <see cref="Instance"/> returns the same <see cref="IType"/> instance.
/// </para>
/// <para>
/// Stage 6 of the migration (Task 50) deletes this class entirely.
/// </para>
/// </summary>
public sealed class StringType : IType
{
    /// <summary>
    /// The pre-migration canonical singleton. Used as the fallback when
    /// <see cref="PrimitiveTypeRegistry"/> has no entry for <c>"string"</c>
    /// (i.e., during Pass 1 bootstrap, or in tests that do not load the
    /// stdlib).
    /// </summary>
    internal static readonly StringType LegacyInstance = new();

    /// <summary>
    /// The Mist string type. Returns the stdlib <c>Standard.String</c>
    /// <see cref="IType"/> instance once registered (post Task 32); falls
    /// back to the legacy singleton otherwise.
    /// </summary>
    public static IType Instance => PrimitiveTypeRegistry.TryGet("string") ?? LegacyInstance;

    public string DisplayName => "string";
    public TypeKind Kind => TypeKind.Class;

    private StringType() { }
}
