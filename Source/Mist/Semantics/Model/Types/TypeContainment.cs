using Mist.Semantics.Contracts;

namespace Mist.Semantics.Model.Types;

/// <summary>
/// Structural-containment helpers on the Mist <see cref="IType"/> model.
/// </summary>
/// <remarks>
/// <para>
/// These helpers implement the <c>ContainsType</c> / <c>IsStorageBanned</c>
/// predicates described in the design document for the <c>unknown-type</c>
/// feature (see <c>.kiro/specs/unknown-type/design.md</c>, Phase B "Storage
/// prohibition cascade" and Phase C "Transitive type containment"). The
/// design calls out that <c>IsStorageBanned</c> is a specialization of the
/// more general <c>ContainsType(haystack, needle)</c> query, with the needle
/// pinned to <see cref="UnknownType.Instance"/>. Extracting the shared
/// helper here lets Phase C's <c>ContainsTAtStoragePosition</c> reuse the
/// same structural walk against a <see cref="TypeParameterType"/> needle.
/// </para>
/// <para>
/// The recursion mirrors the design's formulation exactly:
/// </para>
/// <code>
/// ContainsType(haystack, needle) =
///     match haystack with
///     | haystack == needle       → true
///     | ArrayType(element)       → ContainsType(element, needle)
///     | ConstructedType(_, args) → exists i: ContainsType(args[i], needle)
///     | otherwise                → false
/// </code>
/// <para>
/// <b>Why recurse on <c>args[i]</c> directly rather than on the
/// post-substitution <c>SubstituteAtPosition(def, args, i)</c>:</b> every
/// <see cref="ConstructedType"/> produced by <c>ResolveTypeReference</c>
/// already carries <i>substituted</i> type arguments — if the user writes
/// <c>List&lt;unknown&gt;</c>, argument 0 <i>is</i>
/// <see cref="UnknownType.Instance"/>. A fresh substitution step would be
/// redundant and would obscure the structural meaning of the check.
/// </para>
/// <para>
/// <b>Equality semantics:</b> two cases are recognized.
/// <list type="bullet">
/// <item>
/// <description>
/// Reference equality (<see cref="object.ReferenceEquals(object?, object?)"/>):
/// handles singletons such as <see cref="UnknownType.Instance"/> and the
/// <c>ClassType</c>/<c>StructType</c>/<c>InterfaceType</c>/<c>TraitType</c>
/// instances the <c>SemanticModelBuilder</c> uniques.
/// </description>
/// </item>
/// <item>
/// <description>
/// <see cref="TypeParameterType"/> equivalence by <c>(Name, Ordinal)</c>:
/// the Type Checker's <c>TypesEqual</c> helper already uses this rule for
/// the same reason — type parameter identities can be recreated across
/// contexts (for example when a base-clause reference is re-resolved), so
/// reference equality is too strict for the Phase C use case where the
/// needle will be a <see cref="TypeParameterType"/>.
/// </description>
/// </item>
/// </list>
/// </para>
/// </remarks>
public static class TypeContainment
{
    /// <summary>
    /// Tests whether <paramref name="haystack"/> structurally contains
    /// <paramref name="needle"/> at any depth.
    /// </summary>
    /// <param name="haystack">The type to search within.</param>
    /// <param name="needle">The type to look for. Commonly a singleton
    /// (<see cref="UnknownType.Instance"/>) or a
    /// <see cref="TypeParameterType"/>.</param>
    /// <returns>
    /// <c>true</c> iff the structural walk encounters
    /// <paramref name="needle"/> at the root of <paramref name="haystack"/>,
    /// inside an <see cref="ArrayType"/> element, or at any type-argument
    /// position of a <see cref="ConstructedType"/>.
    /// </returns>
    /// <remarks>
    /// The walk deliberately does <i>not</i> descend into declared members,
    /// base types, or interfaces. It answers only "does this type-expression
    /// tree mention <paramref name="needle"/>?", which is exactly what the
    /// storage-ban and reachability-at-storage-position rules need.
    /// </remarks>
    public static bool ContainsType(IType haystack, IType needle)
    {
        ArgumentNullException.ThrowIfNull(haystack);
        ArgumentNullException.ThrowIfNull(needle);

        if (TypesMatch(haystack, needle))
            return true;

        switch (haystack)
        {
            case ArrayType array:
                return ContainsType(array.ElementType, needle);

            case ConstructedType constructed:
                // Recurse on each type argument. Each argument is already
                // the substituted form (see class-level remarks), so we do
                // not re-run SubstituteAtPosition here.
                for (var i = 0; i < constructed.TypeArguments.Count; i++)
                {
                    if (ContainsType(constructed.TypeArguments[i], needle))
                        return true;
                }
                return false;

            default:
                return false;
        }
    }

    /// <summary>
    /// Tests whether <paramref name="haystack"/> mentions the given type
    /// parameter <paramref name="needle"/> at a storage position — i.e.,
    /// whether assigning a value through a slot of this declared type would
    /// write through <paramref name="needle"/>'s layout (Requirements 9.2,
    /// 9.8, 9.9).
    /// </summary>
    /// <param name="haystack">The declared type of a field, parameter,
    /// assignment target, or similar slot.</param>
    /// <param name="needle">The type parameter being analyzed (typically a
    /// parameter of the enclosing generic type).</param>
    /// <returns>
    /// <c>true</c> iff <paramref name="haystack"/> is
    /// <paramref name="needle"/>, an array whose element type contains
    /// <paramref name="needle"/> at a storage position, or a constructed type
    /// with at least one argument that contains <paramref name="needle"/> at a
    /// storage position. <c>false</c> otherwise.
    /// </returns>
    /// <remarks>
    /// This is the Phase C counterpart of <see cref="IsStorageBanned"/>: both
    /// are specializations of <see cref="ContainsType"/>, one pinning the
    /// needle to <see cref="UnknownType.Instance"/> and the other accepting an
    /// arbitrary <see cref="TypeParameterType"/>. The named wrapper exists so
    /// call sites in the Reachability Analyzer read as "does this slot
    /// contain T at a storage position?" rather than as a generic structural
    /// containment query.
    /// </remarks>
    public static bool ContainsTAtStoragePosition(IType haystack, TypeParameterType needle)
    {
        ArgumentNullException.ThrowIfNull(haystack);
        ArgumentNullException.ThrowIfNull(needle);
        return ContainsType(haystack, needle);
    }

    /// <summary>
    /// Tests whether <paramref name="type"/> is banned from appearing as the
    /// declared type of writable storage under the <c>unknown-type</c> rules
    /// (Requirements 5.1–5.3 and 7.5–7.6).
    /// </summary>
    /// <param name="type">The declared type of a field, property, indexer
    /// element, or other storage slot.</param>
    /// <returns>
    /// <c>true</c> iff <paramref name="type"/> is
    /// <see cref="UnknownType.Instance"/>, an array whose element type is
    /// storage-banned, or a constructed type with at least one storage-banned
    /// type argument at any depth. <c>false</c> otherwise.
    /// </returns>
    /// <remarks>
    /// This is the specialization of <see cref="ContainsType"/> with the
    /// needle pinned to <see cref="UnknownType.Instance"/>. Every call site
    /// that previously wrote "does this type bottom out at <c>unknown</c>?"
    /// should go through this helper so the cascade rule is defined in a
    /// single place.
    /// </remarks>
    public static bool IsStorageBanned(IType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return ContainsType(type, UnknownType.Instance);
    }

    /// <summary>
    /// Equality for the structural walk. Reference equality handles every
    /// singleton (<see cref="UnknownType"/>, user-defined <c>ClassType</c> /
    /// <c>StructType</c> / <c>InterfaceType</c> / <c>TraitType</c>
    /// instances). <see cref="TypeParameterType"/> is compared by
    /// <c>(Name, Ordinal)</c> to stay consistent with the Type Checker's
    /// <c>TypesEqual</c> helper.
    /// </summary>
    private static bool TypesMatch(IType a, IType b)
    {
        if (ReferenceEquals(a, b))
            return true;

        if (a is TypeParameterType aTp && b is TypeParameterType bTp)
            return aTp.Name == bTp.Name && aTp.Ordinal == bTp.Ordinal;

        return false;
    }
}
