using System.Globalization;
using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// Names for the synthesized union carrier structs, shared by the generator and every
/// use-site spelling so the two agree by construction: <c>Union_</c> plus the constituent
/// names sorted ordinally, e.g. <c>Union_Int32_String</c>. Refinements erase to their base
/// and a function type is named by its shape alone, without its purity, because the carrier
/// exists at the CLR level where neither survives. A lifted value does not erase, an array's
/// length among them: <c>int[3]</c> and <c>int[5]</c> are distinct constituents that one carrier
/// may hold side by side, and two unions differing only in a length are distinct carriers.
/// </summary>
internal static class UnionCarrierNames
{
    /// <summary>
    /// The accessor a pattern narrows THROUGH, answering the constituent or absent. A test needs to
    /// discriminate and bind at once, and C# can bind inside a pattern but not inside a property test --
    /// so the tag check has to be the thing that produces a pattern-testable value rather than a bool.
    /// </summary>
    public static string NarrowingAccessor(IMistType constituent)
    {
        return $"As{ConstituentName(constituent)}OrNull";
    }

    public static string CarrierName(UnionType union)
    {
        var names = union.Constituents
            .Select(ConstituentName)
            .OrderBy(name => name, StringComparer.Ordinal);

        return "Union_" + string.Join("_", names);
    }

    public static string EntryFactory(IMistType constituent)
    {
        return $"From{ConstituentName(constituent)}";
    }

    public static string ConstituentName(IMistType type)
    {
        return type switch
        {
            RefinedType refined => ConstituentName(refined.BaseType),
            ConstructedType constructed when IsArray(constructed)
                => $"{ConstituentName(constructed.TypeArguments[0])}Array"
                    + (LengthErasure.ErasedLength(constructed)?.ToString(CultureInfo.InvariantCulture) ?? ""),
            ConstructedType constructed
                => $"{MistType.StripArity(constructed.Definition.Name)}_{constructed.TypeArguments.Count}_"
                    + string.Join("_", constructed.TypeArguments.Select(ConstituentName)),
            LiftedType { Value: TypePackValue pack }
                => $"Pack_{pack.Elements.Length}_" + string.Join("_", pack.Elements.Select(ConstituentName)),
            LiftedType lifted => DependentCarrierNames.Carrier(lifted.Value) ?? ConstituentName(lifted.Type),
            UnknownValueType unknownValue => ConstituentName(unknownValue.Domain),
            IFunctionType function
                => $"Func_{function.ParameterTypes.Count + 1}_"
                    + string.Join("_", function.ParameterTypes.Append(function.ReturnType).Select(ConstituentName)),
            _ => MistType.StripArity(type.Name),
        };
    }

    private static bool IsArray(ConstructedType constructed)
    {
        return constructed.Definition.FullyQualifiedName == WellKnownTypes.Array.FullyQualifiedName;
    }
}