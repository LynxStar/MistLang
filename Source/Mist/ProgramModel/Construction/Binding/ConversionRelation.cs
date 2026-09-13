using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Binding;

/// <summary>
/// Whether a value of one type is accepted where another is expected.
/// <para>
/// A false answer means no conversion is KNOWN, not that none exists -- variance and open type
/// parameters are declined. So a caller may use this to PREFER one candidate over
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

    private static readonly string[] BaseDelegateNames = ["System.Delegate", "System.MulticastDelegate"];

    public static bool IsImplicit(IMistType from, IMistType to)
    {
        return IsWithin(Unrefined(from), Unrefined(to)) || FindOperator(from, to) is not null;
    }

    /// <summary>
    /// A refinement read as the type it narrows, named or a parameter's `where`. A refined value is
    /// always a value of its base, and a base value reaches a refined target -- whether its predicate
    /// holds is the refinement prover's to discharge, not applicability's to refuse -- so a call is
    /// selected as if the refinement were absent and judged afterwards. <see cref="IsWithin"/> keeps the
    /// refinement, since containment into a narrower set is exactly what it cannot assert.
    /// </summary>
    private static IMistType Unrefined(IMistType type)
    {
        return type switch
        {
            RefinedType refined => Unrefined(refined.BaseType),
            TypeAlias { AliasedType: RefinedType refined } => Unrefined(refined),
            _ => type,
        };
    }

    /// <summary>
    /// Whether every value of <paramref name="from"/> is already a value of <paramref name="to"/>. Not a
    /// weaker <see cref="IsImplicit"/>: an <c>implicit operator</c> may be lossy or a change of units, so
    /// it declares reachability without asserting containment, and cannot rank two candidate types.
    /// </summary>
    public static bool IsWithin(IMistType from, IMistType to)
    {
        from = Structural(from);
        to = Structural(to);

        if(WidensToUnknown(from, to))
        {
            return true;
        }

        if(IsIndeterminate(from) || IsIndeterminate(to))
        {
            return false;
        }

        return IsIdentity(from, to)
            || WidensNumerically(from, to)
            || Inherits(from, to)
            || WidensIntoUnion(from, to)
            || WidensElementwise(from, to)
            || WidensToUnknownArguments(from, to)
            || WidensToBaseDelegate(from, to)
            ;
    }

    /// <summary>
    /// Every function value IS a delegate once emitted, so the CLR root of the delegate family
    /// contains all of them. Named by its CLR name because Mist has no spelling of its own for it: a
    /// function type is structural and reaches <c>System.Delegate</c> only through what it emits as.
    /// </summary>
    private static bool WidensToBaseDelegate(IMistType from, IMistType to)
    {
        return from is IFunctionType && Array.IndexOf(BaseDelegateNames, to.FullyQualifiedName) >= 0;
    }

    /// <summary>
    /// Searches BOTH ends and each end's base chain, since either type may declare the conversion. The
    /// ends are bracketed by <see cref="IsWithin"/> rather than <see cref="IsImplicit"/>, so a match
    /// cannot chain a second user-declared conversion. Null means none was found, not that one is built in.
    /// </summary>
    public static CastOperator? FindOperator(IMistType from, IMistType to)
    {
        return DeclaredOn(from).Concat(DeclaredOn(to)).FirstOrDefault(castOperator => castOperator.IsImplicit
            && IsWithin(from, castOperator.SourceParameter.Type)
            && IsWithin(castOperator.TargetType, to));
    }

    public static IEnumerable<CastOperator> DeclaredOn(IMistType type)
    {
        for(var current = type as ConcreteType; current is not null; current = current.BaseType as ConcreteType)
        {
            foreach(var castOperator in current.CastOperatorsList)
            {
                yield return castOperator;
            }
        }
    }

    /// <summary>
    /// Compared by name rather than by reference, since interning holds for most pairs but not every
    /// construction path. One hole survives <see cref="IsIndeterminate"/>: a constructed type
    /// interpolates its arguments' names, so the same lifted value over an int and over a byte compares
    /// equal here.
    /// </summary>
    public static bool IsIdentity(IMistType left, IMistType right)
    {
        left = Structural(left);
        right = Structural(right);

        if(IsIndeterminate(left) || IsIndeterminate(right))
        {
            return false;
        }

        return string.Equals(left.FullyQualifiedName, right.FullyQualifiedName, StringComparison.Ordinal);
    }

    /// <summary>
    /// A named tuple replaced by the structural tuple it decorates, and anything else returned as it is.
    /// Applied at the ENTRY of both relations rather than as an arm of either, because element names
    /// reach the name this compares by: two declarations of <c>(int x, int y)</c> share one interned
    /// underlying tuple but are separate non-interned decorations, so comparing them as they arrive makes
    /// a tuple unassignable to its own shape.
    /// </summary>
    private static IMistType Structural(IMistType type)
    {
        return type is INamedTupleType named ? named.Underlying : type;
    }

    /// <summary>
    /// <c>MyList&lt;int&gt;</c> into <c>MyList&lt;unknown&gt;</c>. Covariance keyed on the TOP TYPE and on
    /// nothing else: an argument position may only widen to <c>unknown</c>, never to another supertype,
    /// so <c>MyList&lt;int&gt;</c> stays outside <c>MyList&lt;long&gt;</c> -- general covariance would be
    /// unsound without a declared variance annotation, which Mist has no syntax for.
    /// <para>
    /// A CLASS definition only. A struct generic widened this way would have to box to reach the
    /// <c>dynamic</c> its emission uses, losing the value semantics that made it a struct.
    /// </para>
    /// </summary>
    private static bool WidensToUnknownArguments(IMistType from, IMistType to)
    {
        if(from is not ConstructedType source
            || to is not ConstructedType target
            || source.Definition is not ClassType
            || !IsIdentity(source.Definition, target.Definition)
            || source.TypeArguments.Count != target.TypeArguments.Count)
        {
            return false;
        }

        for(var i = 0; i < source.TypeArguments.Count; i++)
        {
            if(target.TypeArguments[i] is not UnknownType
                && !IsIdentity(source.TypeArguments[i], target.TypeArguments[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool WidensElementwise(IMistType from, IMistType to)
    {
        if(from is not ITupleType fromTuple || to is not ITupleType toTuple
            || fromTuple.ElementTypes.Count != toTuple.ElementTypes.Count)
        {
            return false;
        }

        for(var i = 0; i < fromTuple.ElementTypes.Count; i++)
        {
            if(!IsWithin(fromTuple.ElementTypes[i], toTuple.ElementTypes[i]))
            {
                return false;
            }
        }

        return true;
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

    /// <summary>
    /// Every value is a value of <c>unknown</c>, so containment INTO it holds for anything that is a
    /// value at all. Answered ahead of <see cref="IsIndeterminate"/> rather than as an arm of the
    /// switch below, because unknown is itself indeterminate and the bail would otherwise reject this
    /// before it was asked.
    /// <para>
    /// APPLICABILITY IS NOT IDENTITY, which is why <see cref="IsIdentity"/> must keep excluding it.
    /// Unknown admits everything, so it discriminates nothing and cannot rank two candidates; but the
    /// question a parameter asks is whether the argument FITS, and for unknown the answer is always
    /// yes. Excluding it there refused <c>Console.WriteLine(7)</c>, the most common call in the
    /// language, which classic prints.
    /// </para>
    /// <para>
    /// The exclusions are the things that are not values: void has one inhabitant and no
    /// representation to widen, an error type would launder a failure into a resolution, and a method
    /// group is a set of signatures awaiting selection rather than something to convert.
    /// </para>
    /// </summary>
    private static bool WidensToUnknown(IMistType from, IMistType to)
    {
        return to is UnknownType
            && from is not (ErrorType or MethodGroupType or NotProjectedType)
            && !OptionalUnion.IsVoid(from)
            ;
    }

    private static bool WidensNumerically(IMistType from, IMistType to)
    {
        return NumericWidening.TryGetValue(from.FullyQualifiedName, out var targets)
            && Array.IndexOf(targets, to.FullyQualifiedName) >= 0
            ;
    }

    /// <summary>
    /// Containment into a union: a value of a constituent is already a value of the union, and
    /// a union is within another whose constituents cover its own. Each arm recurses through
    /// <see cref="IsWithin"/> so numeric widening and inheritance reach INTO the constituent
    /// test -- a byte is within <c>int | string</c>.
    /// </summary>
    private static bool WidensIntoUnion(IMistType from, IMistType to)
    {
        if(to is not UnionType toUnion)
        {
            return false;
        }

        if(from is UnionType fromUnion)
        {
            return fromUnion.Constituents.All(
                constituent => toUnion.Constituents.Any(target => IsWithin(constituent, target)));
        }

        return toUnion.Constituents.Any(constituent => IsWithin(from, constituent));
    }

    /// <summary>
    /// A with block's conformance counts, though member lookup skips it
    /// (<see cref="ConcreteType.ConformancesList"/>): asserting the type IS an instance of the abstract
    /// is containment, with or without a callable slot.
    /// </summary>
    private static bool Inherits(IMistType from, IMistType to)
    {
        return Inherits(from, to, new HashSet<string>(StringComparer.Ordinal));
    }

    private static bool Inherits(IMistType from, IMistType to, HashSet<string> visited)
    {
        foreach(var baseType in Bases(from))
        {
            if(IsIdentity(baseType, to) || (visited.Add(baseType.FullyQualifiedName) && Inherits(baseType, to, visited)))
            {
                return true;
            }
        }

        return false;
    }

    public static IEnumerable<IMistType> Bases(IMistType type)
    {
        var (declaring, substitution) = type switch
        {
            ConstructedType { Definition: MemberType definition } constructed
                => (definition, TypeSubstitution.ForReceiver(constructed)),
            MemberType member => (member, TypeSubstitution.Empty),
            _ => (null, TypeSubstitution.Empty),
        };

        if(declaring is null)
        {
            yield break;
        }

        if(declaring is ConcreteType { BaseType: { } baseType })
        {
            yield return substitution.Apply(baseType, interner: null);
        }

        foreach(var abstractBase in declaring.BaseAbstractTypes)
        {
            yield return substitution.Apply(abstractBase, interner: null);
        }

        foreach(var conformance in (declaring as ConcreteType)?.ConformancesList ?? [])
        {
            yield return substitution.Apply(conformance.AbstractType, interner: null);
        }
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