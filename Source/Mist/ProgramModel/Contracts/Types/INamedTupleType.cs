namespace Mist.ProgramModel.Contracts.Types;

/// <summary>
/// A tuple carrying element names from one declaration site, such as <c>(int x, bool flag)</c>. The names
/// are cosmetic and do NOT affect identity: <see cref="Underlying"/> is what interns, compares and
/// converts. Not interned itself, since a naming occurs exactly once, where it is written.
/// </summary>
public interface INamedTupleType : ITupleType
{
    ITupleType Underlying { get; }

    /// <summary>
    /// Aligned by index with <see cref="ITupleType.ElementTypes"/>; null for an unnamed element.
    /// </summary>
    IReadOnlyList<string?> ElementNames { get; }
}