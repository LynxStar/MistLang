using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Binding;

/// <summary>
/// Whether a value of one type is accepted where another is expected.
/// <para>
/// A false answer means no conversion is KNOWN, not that none exists -- variance, constructed generics
/// and open type parameters are all declined. So a caller may use this to PREFER one candidate over
/// another, and none may use it to refuse a program.
/// </para>
/// </summary>
internal static class ConversionRelation
{
    /// <summary>
    /// Source type to every type it widens into. Must agree with what Roslyn does to the emitted code,
    /// and is spelled out rather than derived from widths because the signed and unsigned families do
    /// not form one ordering.
    /// <para>
    /// DO NOT DELETE in favour of the equivalent operators the stdlib declares. <see cref="IsWithin"/>
    /// has to hold without consulting an operator, so a numeric widening must be expressible as
    /// containment; removing this fails four overload tests.
    /// </para>
    /// </summary>
    private static readonly Dictionary<string, string[]> NumericWidening = BuildNumericWidening();

    public static bool IsImplicit(IMistType from, IMistType to)
    {
        return IsWithin(from, to) || FindOperator(from, to, includeExplicit: false) is not null;
    }

    /// <summary>
    /// Whether every value of <paramref name="from"/> is already a value of <paramref name="to"/>. Not a
    /// weaker <see cref="IsImplicit"/>: an <c>implicit operator</c> may be lossy or a change of units, so
    /// it declares reachability without asserting containment, and cannot rank two candidate types.
    /// </summary>
    public static bool IsWithin(IMistType from, IMistType to)
    {
        if(IsIndeterminate(from) || IsIndeterminate(to))
        {
            return false;
        }

        return IsIdentity(from, to)
            || WidensNumerically(from, to)
            || Inherits(from, to)
            ;
    }

    /// <summary>
    /// Searches BOTH ends and each end's base chain, since either type may declare the conversion. The
    /// ends are bracketed by <see cref="IsWithin"/> rather than <see cref="IsImplicit"/>, so a match
    /// cannot chain a second user-declared conversion. Null means none was found, not that one is built in.
    /// </summary>
    public static CastOperator? FindOperator(IMistType from, IMistType to, bool includeExplicit)
    {
        return DeclaredBy(from, from, to, includeExplicit)
            ?? DeclaredBy(to, from, to, includeExplicit)
            ;
    }

    private static CastOperator? DeclaredBy(IMistType declaring, IMistType from, IMistType to, bool includeExplicit)
    {
        var current = declaring as ConcreteType;

        while(current is not null)
        {
            foreach(var castOperator in current.CastOperatorsList)
            {
                if((castOperator.IsImplicit || includeExplicit)
                    && IsWithin(from, castOperator.SourceParameter.Type)
                    && IsWithin(castOperator.TargetType, to))
                {
                    return castOperator;
                }
            }

            current = current.BaseType as ConcreteType;
        }

        return null;
    }

    /// <summary>
    /// Compared by name rather than by reference, since interning holds for most pairs but not every
    /// construction path. One hole survives <see cref="IsIndeterminate"/>: a constructed type
    /// interpolates its arguments' names, so the same lifted value over an int and over a byte compares
    /// equal here.
    /// </summary>
    public static bool IsIdentity(IMistType left, IMistType right)
    {
        if(IsIndeterminate(left) || IsIndeterminate(right))
        {
            return false;
        }

        return string.Equals(left.FullyQualifiedName, right.FullyQualifiedName, StringComparison.Ordinal);
    }

    /// <summary>
    /// Types this relation declines to reason about. <see cref="LiftedType"/> and
    /// <see cref="RefinedType"/> are here because their names are not identities -- a lift is named for
    /// its value alone, a refinement omits its predicate -- and a false identity is a false
    /// applicability. A <see cref="MethodGroupType"/> holds several signatures, so choosing among them
    /// against a target is selection rather than conversion.
    /// </summary>
    private static bool IsIndeterminate(IMistType type)
    {
        return type is TypeParameter
            or ErrorType
            or UnknownType
            or UnknownValueType
            or NotProjectedType
            or LiftedType
            or RefinedType
            or MethodGroupType
            ;
    }

    private static bool WidensNumerically(IMistType from, IMistType to)
    {
        return NumericWidening.TryGetValue(from.FullyQualifiedName, out var targets)
            && Array.IndexOf(targets, to.FullyQualifiedName) >= 0
            ;
    }

    /// <summary>
    /// A RETROACTIVE conformance counts here alongside a declared one, and belongs under
    /// <see cref="IsWithin"/> rather than beside a cast operator: a with block asserts that the type
    /// IS an instance of the abstract, so every value of it is already a value of the abstract. That
    /// is a subtyping claim of the same strength as naming the abstract in the base list, unlike an
    /// <c>implicit operator</c>, which may be lossy and so cannot rank anything.
    /// </summary>
    private static bool Inherits(IMistType from, IMistType to)
    {
        var current = from as MemberType;

        while(current is not null)
        {
            foreach(var abstractBase in current.BaseAbstractTypes)
            {
                if(IsIdentity(abstractBase, to) || Inherits(abstractBase, to))
                {
                    return true;
                }
            }

            foreach(var conformance in (current as ConcreteType)?.ConformancesList ?? [])
            {
                if(IsIdentity(conformance.AbstractType, to) || Inherits(conformance.AbstractType, to))
                {
                    return true;
                }
            }

            var next = (current as ConcreteType)?.BaseType;

            if(next is not null && IsIdentity(next, to))
            {
                return true;
            }

            current = next as MemberType;
        }

        return false;
    }

    private static Dictionary<string, string[]> BuildNumericWidening()
    {
        var sbyteT = WellKnownTypes.SByte.FullyQualifiedName;
        var byteT = WellKnownTypes.Byte.FullyQualifiedName;
        var shortT = WellKnownTypes.Int16.FullyQualifiedName;
        var ushortT = WellKnownTypes.UInt16.FullyQualifiedName;
        var intT = WellKnownTypes.Int32.FullyQualifiedName;
        var uintT = WellKnownTypes.UInt32.FullyQualifiedName;
        var longT = WellKnownTypes.Int64.FullyQualifiedName;
        var ulongT = WellKnownTypes.UInt64.FullyQualifiedName;
        var charT = WellKnownTypes.Char.FullyQualifiedName;
        var floatT = WellKnownTypes.Single.FullyQualifiedName;
        var doubleT = WellKnownTypes.Double.FullyQualifiedName;
        var decimalT = WellKnownTypes.Decimal.FullyQualifiedName;

        return new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [sbyteT] = [shortT, intT, longT, floatT, doubleT, decimalT],
            [byteT] = [shortT, ushortT, intT, uintT, longT, ulongT, floatT, doubleT, decimalT],
            [shortT] = [intT, longT, floatT, doubleT, decimalT],
            [ushortT] = [intT, uintT, longT, ulongT, floatT, doubleT, decimalT],
            [intT] = [longT, floatT, doubleT, decimalT],
            [uintT] = [longT, ulongT, floatT, doubleT, decimalT],
            [longT] = [floatT, doubleT, decimalT],
            [ulongT] = [floatT, doubleT, decimalT],
            [charT] = [ushortT, intT, uintT, longT, ulongT, floatT, doubleT, decimalT],
            [floatT] = [doubleT],
        };
    }
}