using System.Collections.Immutable;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Types;

/// <summary>
/// The output of <see cref="TypeBuilder.Build"/> for a single compilation unit.
/// </summary>
/// <param name="Types">
/// Top-level <see cref="MemberType"/>s only. Nested types are reachable through their
/// declaring type, and enums, newtypes and type aliases are absent because the emitter
/// cannot emit them yet -- adding them here without emitter support would produce a unit
/// claiming to describe declarations that never reach the output.
/// </param>
/// <param name="TopLevelMethod">The synthetic TLS <c>Main</c>, or null when the file has none.</param>
internal sealed record TypeBuilderResult(
    ImmutableArray<MemberType> Types,
    Method? TopLevelMethod);