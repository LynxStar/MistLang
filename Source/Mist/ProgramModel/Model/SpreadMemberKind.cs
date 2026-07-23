namespace Mist.ProgramModel.Model;

/// <summary>
/// Whether a <see cref="SpreadMember"/> family surfaces as fields or as properties.
/// The kind mirrors the member kind of the spread declaration: <c>public TArgs... Item;</c>
/// is a <see cref="Field"/> spread, while <c>public TArgs... Slot { get; }</c> is a
/// <see cref="Property"/> spread. The aggregate member and every element projection share
/// this one kind (Requirement 10.1).
/// </summary>
public enum SpreadMemberKind
{
    /// <summary>A field spread: the aggregate and its projections are fields.</summary>
    Field,

    /// <summary>A property spread: the aggregate and its projections are properties.</summary>
    Property,
}