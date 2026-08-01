using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Binding;

/// <summary>
/// Whether a value of one type is accepted where another is expected, by identity, numeric widening,
/// or inheritance.
/// <para>
/// KNOWINGLY INCOMPLETE, and safe only because of how callers are required to use it. User-defined
/// cast operators and variance are not modelled at all, and generic inference is deliberately declined
/// (see <see cref="IsIndeterminate"/>). So a false answer means "no conversion is KNOWN", which is not
/// the claim "no conversion exists", and the two must not be conflated. A caller may use this to
/// PREFER one candidate over another; none may use it to refuse a program. Refusing on an unknown
/// conversion is how a relation this partial would start rejecting code that compiles.
/// </para>
/// </summary>
internal static class ConversionRelation
{
    /// <summary>
    /// The implicit numeric conversions, source type to every type it widens into. Mist lowers to C#,
    /// so this is C#'s table: it has to agree with what Roslyn will do to the emitted call, or
    /// selection and emission would disagree about the same program.
    /// <para>
    /// Written out rather than derived from a size ordering because the signed and unsigned families
    /// do not form one: <c>int</c> and <c>uint</c> convert to neither each other, so the widths alone
    /// would not say which of the two a value may reach.
    /// </para>
    /// <para>
    /// TEMPORARY, and a duplicate of the standard library. <c>Source/Standard/Bootstrap/</c> declares
    /// every conversion below as an <c>implicit operator</c> member -- <c>Int32</c> to <c>Int64</c>,
    /// <c>Single</c>, <c>Double</c> and <c>Decimal</c>, and so on for each numeric type -- written
    /// transitively closed exactly as they are here, so <c>ConcreteType.CastOperatorsList</c> already
    /// holds the whole table. This exists only because cast operators are not consulted yet. Delete it
    /// when they are: scanning them reproduces these entries and admits user-defined conversions in
    /// the same step. Until then nothing forces the two to agree, which is the cost of keeping both.
    /// </para>
    /// </summary>
    private static readonly Dictionary<string, string[]> NumericWidening = BuildNumericWidening();

    /// <summary>
    /// Whether <paramref name="from"/> is accepted where <paramref name="to"/> is expected. False
    /// where nothing here can establish it -- see the type-level note on what that does and does not
    /// mean.
    /// </summary>
    public static bool IsImplicit(IMistType from, IMistType to)
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
    /// Two types are the same, compared by fully qualified name rather than by reference, since
    /// interning holds for most pairs but not for every construction path.
    /// <para>
    /// A name is not a reliable identity everywhere, which is why <see cref="IsIndeterminate"/>
    /// excludes the types whose names collide outright. One case survives that guard: a constructed
    /// type interpolates its arguments' names, so the same lifted value over an <c>int</c> and over
    /// a <c>byte</c> produces the same name and compares equal here. Separating those needs
    /// structural comparison.
    /// </para>
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
    /// A type this relation declines to reason about, each for its own reason. An open type parameter
    /// awaits generic inference. An error type has no identity worth comparing. <c>unknown</c> admits
    /// everything, which makes it useless as a discriminator, and an unknown dependent VALUE is not a
    /// type of value at all. A <see cref="MethodGroupType"/> is refused because conversion is the
    /// wrong question to put to a group: it holds several signatures, and choosing among them against
    /// a target is selection rather than conversion, so answering here at all would mean answering
    /// for one signature picked arbitrarily.
    /// <para>
    /// <see cref="LiftedType"/> and <see cref="RefinedType"/> are here for a different reason: their
    /// names are not identities. A lifted type is named for its value alone, so <c>255</c> as an
    /// <c>int</c> and as a <c>byte</c> share a name; a refined type's name omits its predicate
    /// entirely, so every refinement of <c>int</c> is spelled alike. Comparing either by name would
    /// call two different types the same, which is the one direction of error this relation must not
    /// make -- a false identity is a false APPLICABILITY, and applicability is what the ambiguity
    /// report rests on.
    /// </para>
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
    /// Whether <paramref name="from"/> reaches <paramref name="to"/> through its base chain, walking
    /// concrete bases and the abstract types each level declares.
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