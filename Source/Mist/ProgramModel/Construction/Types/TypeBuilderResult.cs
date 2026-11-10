using System.Collections.Immutable;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Types;

/// <param name="Types">
/// Top-level declarations only, of every kind. Nested types are reachable through their declaring
/// type instead, since emitting one means emitting it inside that type.
/// </param>
internal sealed record TypeBuilderResult(
    ImmutableArray<DeclaredType> Types,
    Method? TopLevelMethod);