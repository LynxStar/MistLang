namespace Mist.Semantics.Model;

/// <summary>
/// Whether a <see cref="SemanticSpreadMember"/> family surfaces as fields or as properties
/// (classic pipeline). The kind mirrors the member kind of the spread declaration:
/// <c>public TArgs... Item;</c> is a <see cref="Field"/> spread, while
/// <c>public TArgs... Slot { get; }</c> is a <see cref="Property"/> spread. The aggregate
/// member and every element projection share this one kind (Requirement 10.1).
/// <para>
/// This is the classic-pipeline counterpart of the ProgramModel track's
/// <c>SpreadMemberKind</c>; the two are intentionally separate types so the emitting
/// <c>Mist.Semantics</c> model carries its own representation without depending on the
/// ProgramModel track.
/// </para>
/// </summary>
public enum SpreadMemberKind
{
    /// <summary>A field spread: the aggregate and its projections are fields.</summary>
    Field,

    /// <summary>A property spread: the aggregate and its projections are properties.</summary>
    Property,
}