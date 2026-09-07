using Mist.Common.Diagnostics;
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

    /// <summary>
    /// Reports every member name a spread already owns. The reservation covers the stem itself, the
    /// stem followed by an index, and another stem that prefixes it -- all three because a
    /// projection name is not knowable at the declaration, so nothing may occupy a name the family
    /// could later claim.
    /// </summary>
    public static void ValidateStems(ConcreteType type, Storm.Runtime.SyntaxNode locationNode, DiagnosticBag diagnostics)
    {
        var stems = type.FieldsList
            .Where(f => IsSpread(f.Type))
            .Select(f => f.Name)
            .Concat(type.PropertiesList.Where(p => IsSpread(p.Type)).Select(p => p.Name))
            .ToList();

        if(stems.Count == 0)
        {
            return;
        }

        var names = type.FieldsList.Select(f => (f.Name, Spread: IsSpread(f.Type)))
            .Concat(type.PropertiesList.Select(p => (p.Name, Spread: IsSpread(p.Type))))
            .Concat(type.MethodsList.Select(m => (m.Name, Spread: false)));

        foreach(var (name, isSpread) in names)
        {
            foreach(var stem in stems)
            {
                if(Claims(stem, name, isSpread))
                {
                    diagnostics.ReportAtNode(
                        ProgramModelDiagnostics.SpreadStemCollision, locationNode, null, name, stem);
                }
            }
        }
    }

    private static bool Claims(string stem, string name, bool nameIsSpread)
    {
        if(name == stem)
        {
            return false;
        }

        // Another spread prefixing this one is a collision whatever follows, since both families
        // are open-ended; an ordinary member only collides on the stem plus an index.
        return name.StartsWith(stem, StringComparison.Ordinal)
            && (nameIsSpread || int.TryParse(name.AsSpan(stem.Length), out _));
    }
}