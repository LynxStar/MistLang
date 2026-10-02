using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// How a construction of a variadic family is written in C#: the specialization for its widths,
/// with each pack's elements standing where the pack did.
/// </summary>
internal static class MonomorphReference
{
    private static bool Expand(TypePackValue pack, out List<IMistType> elements)
    {
        elements = [];

        foreach(var element in pack.Elements)
        {
            if(element is not SpreadType spread)
            {
                elements.Add(element);
                continue;
            }

            if(Monomorph.InScope() is not { } monomorph
                || spread.Operand is not TypeParameter operand
                || !monomorph.Binds(operand))
            {
                return false;
            }

            elements.AddRange(monomorph.Expansion(operand));
        }

        return true;
    }

    /// <summary>
    /// The types a pack holds where it is being emitted, read off whatever closed it there: the
    /// specialization in scope for a pack that is still its own parameter, or the elements a base list
    /// wrote for it, which is all a member restated into a conformer has. Null while it is open.
    /// </summary>
    public static IReadOnlyList<IMistType>? Elements(IMistType pack)
    {
        return pack switch
        {
            TypeParameter open when Monomorph.InScope() is { } monomorph && monomorph.Binds(open) => monomorph.Expansion(open),
            LiftedType { Value: TypePackValue closed } when Expand(closed, out var elements) => elements,
            _ => null,
        };
    }

    public static int? Width(IMistType pack)
    {
        return Elements(pack)?.Count;
    }

    /// <summary>
    /// The widths a call bound a method's packs to, or null when the callee has no pack or one is
    /// still symbolic -- a call made from inside another variadic declaration, outside any
    /// specialization of it.
    /// </summary>
    public static IReadOnlyList<int>? MethodWidths(
        IReadOnlyList<IMistType> typeArguments, IReadOnlyList<TypeParameter>? parameters)
    {
        if(parameters is null)
        {
            return null;
        }

        var widths = new List<int>();

        for(var i = 0; i < parameters.Count && i < typeArguments.Count; i++)
        {
            if(parameters[i].PackKind == TypePackKind.None)
            {
                continue;
            }

            if(typeArguments[i] is not LiftedType { Value: TypePackValue pack } || !Expand(pack, out var elements))
            {
                return null;
            }

            widths.Add(elements.Count);
        }

        return widths.Count == 0 ? null : widths;
    }

    /// <summary>
    /// A call's type arguments with each pack replaced by its elements, which is what the
    /// specialization declares in their place.
    /// </summary>
    public static string SpecializedArguments(
        IReadOnlyList<IMistType> typeArguments,
        IReadOnlyList<TypeParameter> parameters,
        Func<IMistType, string> emitType)
    {
        var written = new List<string>();

        for(var i = 0; i < parameters.Count && i < typeArguments.Count; i++)
        {
            if(parameters[i].PackKind != TypePackKind.None
                && typeArguments[i] is LiftedType { Value: TypePackValue pack }
                && Expand(pack, out var elements))
            {
                written.AddRange(elements.Select(emitType));
                continue;
            }

            written.Add(emitType(typeArguments[i]));
        }

        return written.Count == 0 ? "" : $"<{string.Join(", ", written)}>";
    }

    /// <summary>
    /// The C# spelling, or null when this construction is not of a variadic family or one of its
    /// packs is still symbolic -- the latter being a reference from inside the family's own
    /// declaration, which the specialization already answers with its own parameters.
    /// </summary>
    public static string? Emit(ConstructedType constructed, Func<IMistType, string> emitType)
    {
        if(constructed.Definition is not MistType definition
            || definition is not IHasTypeParameters generic)
        {
            return null;
        }

        var parameters = generic.TypeParametersList;
        var arguments = new List<string>();
        var widths = new List<int>();

        for(var i = 0; i < parameters.Count && i < constructed.TypeArguments.Count; i++)
        {
            if(parameters[i].PackKind == TypePackKind.None)
            {
                arguments.Add(emitType(constructed.TypeArguments[i]));
                continue;
            }

            if(constructed.TypeArguments[i] is not LiftedType { Value: TypePackValue pack })
            {
                return null;
            }

            // A spread among the elements is this declaration's OWN pack, written through to
            // another family -- `Inner<TArgs...>` inside `Outer<params TArgs>`. The specialization
            // in scope is what says how wide it is, so it expands here rather than defeating the
            // whole reference and falling back to a spelling of the pack's domain.
            if(Expand(pack, out var elements) is not true)
            {
                return null;
            }

            widths.Add(elements.Count);
            arguments.AddRange(elements.Select(emitType));
        }

        if(widths.Count == 0)
        {
            return null;
        }

        var name = MonomorphSet.SpecializedName(definition.Name, widths);
        return arguments.Count == 0 ? name : $"{name}<{string.Join(", ", arguments)}>";
    }
}