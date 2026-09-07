using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Members;

/// <summary>
/// A member declared with a spread (<c>public TArgs... Item;</c>), which stands for a family: the
/// aggregate holding the whole pack, and one projection per element.
/// </summary>
internal static class SpreadMember
{
    /// <summary>
    /// The type such a member actually has. A bare spread is not a type a member can hold -- it
    /// stands for several -- so in member position it becomes the TUPLE of what it spreads, which
    /// is one value and which substitution later fills in.
    /// </summary>
    public static IMistType AggregateType(IMistType declared, TypeInterner interner)
    {
        return declared is SpreadType ? interner.GetOrCreateTupleType([declared]) : declared;
    }

    /// <summary>
    /// Whether a member was declared with a spread, read off the shape its type was given rather
    /// than recorded separately -- there is no other way for a member to be a one-element tuple of
    /// a spread.
    /// </summary>
    public static bool IsSpread(IMistType type)
    {
        return type is TupleType { ElementTypes: [SpreadType] };
    }
}