using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// How a construction of a variadic family is written in C#: the specialization for its arity, with
/// the pack's elements standing where the pack did.
/// </summary>
internal static class MonomorphReference
{
    /// <summary>
    /// The C# spelling, or null when this construction is not of a variadic family or its pack is
    /// still symbolic -- the latter being a reference from inside the family's own declaration,
    /// which the specialization already answers with its own parameters.
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
        var arity = -1;

        for(var i = 0; i < parameters.Count && i < constructed.TypeArguments.Count; i++)
        {
            if(parameters[i].PackKind == TypePackKind.None)
            {
                arguments.Add(emitType(constructed.TypeArguments[i]));
                continue;
            }

            if(constructed.TypeArguments[i] is not LiftedType { Value: TypePackValue pack }
                || pack.Elements.Any(element => element is SpreadType))
            {
                return null;
            }

            arity = pack.Elements.Length;
            arguments.AddRange(pack.Elements.Select(emitType));
        }

        if(arity < 0)
        {
            return null;
        }

        var name = MonomorphSet.SpecializedName(definition, arity);
        return arguments.Count == 0 ? name : $"{name}<{string.Join(", ", arguments)}>";
    }
}