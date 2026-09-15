using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Binding;

/// <summary>
/// Whether two overloads have the same signature: as many type parameters of their own, and position by
/// position the same passing and the same parameter type. Hiding across a derivation and a duplicate
/// within one type are both this question, so both ask it here. A refinement reads as its base, as
/// applicability reads it; a member's own type parameter reads as its position, so a Take over its own
/// T repeats a Take over its own U while a parameter of the declaring type stays itself; and passing is
/// only by reference or by value, since `ref` against `readonly ref` is one signature to C#. The type
/// is walked rather than read as one qualified name, because a construction spells its arguments by
/// simple name, and <see cref="OverloadFailure.DescribeSignatures"/> spells every type that way. A
/// signature naming a type that did not resolve is null, and so repeats nothing: that mistake was
/// reported where the type was written.
/// </summary>
internal static class OverloadSignature
{
    public static string? Of(IOverload overload, TypeSubstitution substitution, TypeInterner? interner)
    {
        var own = overload.TypeParametersList;

        if(Each(overload.ParametersList.Select(parameter => substitution.Apply(parameter.Type, interner)), own) is not { } types)
        {
            return null;
        }

        var parameters = types.Select((type, i) => overload.ParametersList[i].Modifier.IsRef ? $"ref {type}" : type);

        return $"{own.Count}({string.Join(", ", parameters)})";
    }

    public static bool Repeats(IOverload later, IOverload earlier)
    {
        return later.DeclaringType is not null
            && ReferenceEquals(later.DeclaringType, earlier.DeclaringType)
            && later.DeclarationNode is not null
            && earlier.DeclarationNode is not null
            && Of(later, TypeSubstitution.Empty, null) is { } signature
            && signature == Of(earlier, TypeSubstitution.Empty, null);
    }

    private static string? Render(IMistType type, List<TypeParameter> own)
    {
        return ConversionRelation.Unrefined(type) switch
        {
            ErrorType => null,
            TypeParameter parameter when own.FindIndex(declared => ReferenceEquals(declared, parameter)) is var position and >= 0 => $"#{position}",
            NamedTupleType named => Render(named.Underlying, own),
            TupleType tuple => Each(tuple.ElementTypes, own) is { } elements ? $"({string.Join(", ", elements)})" : null,
            UnionType union => Each(union.Constituents, own) is { } constituents ? string.Join(" | ", constituents.Order(StringComparer.Ordinal)) : null,
            FunctionType function => Each(function.TypeArgumentsInSourceOrder(), own) is { } arguments ? $"func<{string.Join(", ", arguments)}>" : null,
            ConstructedType { Enclosing: { } enclosing } nested => Render(enclosing, own) is { } outer ? Constructed($"{outer}.{MistType.StripArity(nested.Definition.Name)}", nested, own) : null,
            ConstructedType constructed => Constructed(constructed.Definition.FullyQualifiedName, constructed, own),
            var unrefined => unrefined.FullyQualifiedName,
        };
    }

    private static string? Constructed(string name, ConstructedType constructed, List<TypeParameter> own)
    {
        return constructed.TypeArguments.Count == 0 ? name
            : Each(constructed.TypeArguments, own) is { } arguments ? $"{name}<{string.Join(", ", arguments)}>"
            : null;
    }

    private static List<string>? Each(IEnumerable<IMistType> types, List<TypeParameter> own)
    {
        var rendered = new List<string>();

        foreach(var type in types)
        {
            if(Render(type, own) is not { } one)
            {
                return null;
            }

            rendered.Add(one);
        }

        return rendered;
    }
}