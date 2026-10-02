using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Binding;

/// <summary>
/// Whether two overloads have the same signature: as many type parameters of their own, and position
/// by position the same passing and the same parameter type. A duplicate within one type, a
/// redeclaration of an inherited member and hiding across a derivation are all this question, so all
/// three ask it here; WHICH two members are compared belongs to each caller, and an inherited one is
/// read through the substitution that reaches it, since a member hides what the deriving type sees. A refinement reads as its base, as applicability reads it; a member's own
/// type parameter reads as its position, so a Take over its own T repeats a Take over its own U while
/// a parameter of the declaring type stays itself; and passing is only by reference or by value, since
/// `ref` against `readonly ref` is one signature to C#. The type is walked rather than read as one
/// qualified name, because a construction spells its arguments by simple name, and
/// <see cref="OverloadFailure.DescribeSignatures"/> spells every type that way. A signature naming a
/// type that did not resolve is null, and so repeats nothing: that mistake was reported where the type
/// was written.
/// </summary>
internal static class OverloadSignature
{
    public static string? Of(IOverload overload, TypeSubstitution substitution, TypeInterner? interner)
    {
        var own = overload.TypeParametersList;
        var closed = ValuePack.Expand(overload.ParametersList, substitution, interner);

        if(Each(closed.Select(parameter => parameter.Type), own) is not { } types)
        {
            return null;
        }

        var parameters = types.Select((type, i) => closed[i].Modifier.IsRef ? $"ref {type}" : type);

        return $"{own.Count}({string.Join(", ", parameters)})";
    }

    public static bool Repeats(
        IOverload later, IOverload earlier, TypeSubstitution? inherited = null, TypeInterner? interner = null)
    {
        return later.DeclarationNode is not null
            && earlier.DeclarationNode is not null
            && Of(later, TypeSubstitution.Empty, null) is { } signature
            && signature == Of(earlier, inherited ?? TypeSubstitution.Empty, interner);
    }

    public static string? ExtensionReceiver(ExtensionBlock block)
    {
        return block.ResolvedReceiverType is { } receiver
            && Render(receiver, [], block.TypeParametersList) is { } rendered
            ? rendered
            : null;
    }

    public static string? ExtensionMethod(ExtensionBlock block, IOverload method)
    {
        if(Each(method.ParametersList.Select(parameter => parameter.Type), method.TypeParametersList, block.TypeParametersList) is not { } types)
        {
            return null;
        }

        var parameters = types.Select((type, i) => method.ParametersList[i].Modifier.IsRef ? $"ref {type}" : type);
        return $"{method.TypeParametersList.Count}({string.Join(", ", parameters)})";
    }

    public static string? TypeOf(IMistType type, List<TypeParameter>? own = null)
    {
        return Render(type, own ?? []);
    }

    private static string? Render(IMistType type, List<TypeParameter> own, List<TypeParameter>? block = null)
    {
        return ConversionRelation.Unrefined(type) switch
        {
            ErrorType => null,
            TypeParameter parameter when own.FindIndex(declared => ReferenceEquals(declared, parameter)) is var position and >= 0 => block is null ? $"#{position}" : $"#m{position}",
            TypeParameter parameter when block is not null && block.FindIndex(declared => ReferenceEquals(declared, parameter)) is var position and >= 0 => $"#b{position}",
            TypeParameter when block is not null => null,
            NamedTupleType named => Render(named.Underlying, own, block),
            TupleType tuple => Each(tuple.ElementTypes, own, block) is { } elements ? $"({string.Join(", ", elements)})" : null,
            UnionType union => Each(union.Constituents, own, block) is { } constituents ? string.Join(" | ", constituents.Order(StringComparer.Ordinal)) : null,
            FunctionType function => Each(function.TypeArgumentsInSourceOrder(), own, block) is { } arguments ? $"func<{string.Join(", ", arguments)}>" : null,
            ConstructedType { Enclosing: { } enclosing } nested => Render(enclosing, own, block) is { } outer ? Constructed($"{outer}.{MistType.StripArity(nested.Definition.Name)}", nested, own, block) : null,
            ConstructedType constructed => Constructed(constructed.Definition.FullyQualifiedName, constructed, own, block),
            var unrefined => unrefined.FullyQualifiedName,
        };
    }

    private static string? Constructed(string name, ConstructedType constructed, List<TypeParameter> own, List<TypeParameter>? block = null)
    {
        return constructed.TypeArguments.Count == 0 ? name
            : Each(constructed.TypeArguments, own, block) is { } arguments ? $"{name}<{string.Join(", ", arguments)}>"
            : null;
    }

    private static List<string>? Each(IEnumerable<IMistType> types, List<TypeParameter> own, List<TypeParameter>? block = null)
    {
        var rendered = new List<string>();

        foreach(var type in types)
        {
            if(Render(type, own, block) is not { } one)
            {
                return null;
            }

            rendered.Add(one);
        }

        return rendered;
    }
}