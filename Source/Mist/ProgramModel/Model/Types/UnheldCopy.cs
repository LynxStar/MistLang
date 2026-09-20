using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Model.Types;

/// <summary>
/// The first slot a newtype does not copy from its source field for field, because the slot names the
/// source and so answers the newtype in the clone (newtypes spec Requirement 10.3). Recorded only when
/// the source defines no <c>Clone</c> to convert through instead, and read by the rule that refuses the
/// conversion, which is the only thing this stops.
/// </summary>
internal sealed record UnheldCopy(string Member, IMistType Renamed, IMistType Declared);