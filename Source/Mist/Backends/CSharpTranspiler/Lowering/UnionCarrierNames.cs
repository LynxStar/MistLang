using System.Globalization;
using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// Names for the synthesized union carrier structs, shared by the generator and every
/// use-site spelling so the two agree by construction: <c>Union_</c> plus the constituent
/// names sorted ordinally, e.g. <c>Union_Int32_String</c>. A function type is named by its
/// shape alone, without its purity, because the carrier exists at the CLR level where purity
/// does not survive. A lifted value does not erase, an array's length among them: <c>int[3]</c>
/// and <c>int[5]</c> are distinct constituents that one carrier may hold side by side, and two
/// unions differing only in a length are distinct carriers. A refinement keeps its identity only
/// where erasing it would give two constituents one name, because a union's printed text is still
/// its carrier's name: an int refinement beside a byte one prints as <c>Union_Byte_Int32</c>.
/// </summary>
internal static class UnionCarrierNames
{
    /// <summary>
    /// The accessor a pattern narrows THROUGH, answering the constituent or absent. A test needs to
    /// discriminate and bind at once, and C# can bind inside a pattern but not inside a property test --
    /// so the tag check has to be the thing that produces a pattern-testable value rather than a bool.
    /// </summary>
    public static string NarrowingAccessor(IMistType constituent, UnionType union)
    {
        return $"As{ConstituentName(constituent, union)}OrNull";
    }

    public static string CarrierName(UnionType union)
    {
        var names = union.Constituents
            .Select(constituent => ConstituentName(constituent, union))
            .OrderBy(name => name, StringComparer.Ordinal);

        return "Union_" + string.Join("_", names);
    }

    public static string EntryFactory(IMistType constituent, UnionType union)
    {
        return $"From{ConstituentName(constituent, union)}";
    }

    public static string ConstituentName(IMistType constituent, UnionType union)
    {
        var erased = union.Constituents.Select(ConstituentName).Distinct(StringComparer.Ordinal).Count() == union.Constituents.Count;

        return erased ? ConstituentName(constituent) : Spelled(constituent, keepsRefinements: true);
    }

    public static string ConstituentName(IMistType type)
    {
        return Spelled(type, keepsRefinements: false);
    }

    public static string Spelled(IMistType type, bool keepsRefinements)
    {
        string spelled(IMistType part)
        {
            return Spelled(part, keepsRefinements);
        }

        return type switch
        {
            RefinedType refined when keepsRefinements => $"{spelled(refined.BaseType)}_{RefinedConstituentNames.Of(refined)}",
            RefinedType refined => spelled(refined.BaseType),
            ConstructedType constructed when IsArray(constructed)
                => $"{spelled(constructed.TypeArguments[0])}Array"
                    + (LengthErasure.ErasedLength(constructed)?.ToString(CultureInfo.InvariantCulture) ?? ""),
            ConstructedType constructed
                => $"{MistType.StripArity(constructed.Definition.Name)}_{constructed.TypeArguments.Count}_"
                    + string.Join("_", constructed.TypeArguments.Select(spelled)),
            LiftedType { Value: TypePackValue pack }
                => $"Pack_{pack.Elements.Length}_" + string.Join("_", pack.Elements.Select(spelled)),
            LiftedType lifted => DependentCarrierNames.Carrier(lifted.Value) ?? spelled(lifted.Type),
            UnknownValueType unknownValue => spelled(unknownValue.Domain),
            IFunctionType function
                => $"Func_{function.ParameterTypes.Count + 1}_"
                    + string.Join("_", function.ParameterTypes.Append(function.ReturnType).Select(spelled)),
            _ => MistType.StripArity(type.Name),
        };
    }

    private static bool IsArray(ConstructedType constructed)
    {
        return constructed.Definition.FullyQualifiedName == WellKnownTypes.Array.FullyQualifiedName;
    }
}