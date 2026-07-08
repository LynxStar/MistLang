namespace Mist.ProgramModel.Contracts.Types;

/// <summary>
/// A tuple type reference that carries element names from a specific declaration
/// site (e.g., <c>(int x, bool flag)</c>). Names are cosmetic use-site metadata
/// and do NOT affect type identity — the underlying structural <see cref="ITupleType"/>
/// is what participates in interning, equality, and conversions.
/// <para>
/// Instances are created per declaration site and are intentionally NOT interned,
/// since a given naming occurs exactly once where it is written.
/// </para>
/// </summary>
public interface INamedTupleType : ITupleType
{
    /// <summary>
    /// The underlying structural tuple type (interned, name-free).
    /// This is the identity-bearing type; use it wherever type identity matters.
    /// </summary>
    ITupleType Underlying { get; }

    /// <summary>
    /// Element names in positional order. Null entries indicate unnamed elements.
    /// Aligned by index with <see cref="ITupleType.ElementTypes"/>.
    /// </summary>
    IReadOnlyList<string?> ElementNames { get; }
}