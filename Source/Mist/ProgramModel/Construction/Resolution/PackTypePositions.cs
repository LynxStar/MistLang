using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Resolution;

/// <summary>
/// What a pack is in the type positions that are not a bare spread (variadics design, "Position
/// Semantics of a Pack Identifier"). Kept beside <see cref="ScopeResolver"/> rather than in it: each
/// is one decision about a pack, taken after the name has been resolved.
/// </summary>
internal static class PackTypePositions
{
    public static IMistType Parenthesized(IMistType inner, TypeInterner interner)
    {
        return inner is SpreadType ? interner.GetOrCreateTupleType([inner]) : inner;
    }
}