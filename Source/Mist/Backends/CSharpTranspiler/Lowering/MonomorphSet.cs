using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// The arities each variadic family is actually used at, which is what a C# emission has to be
/// specialized to: the language has no pack, so one declaration becomes one type per arity.
/// <para>
/// PER ARITY AND NOT PER CONSTRUCTION, which is the whole economy of it. <c>Box&lt;int, string&gt;</c>
/// and <c>Box&lt;bool, char&gt;</c> want the same C# declaration -- a generic of two parameters --
/// and differ only in what they pass it. Specializing per construction would emit a type per
/// distinct argument list and lose genericity that C# is perfectly able to express.
/// </para>
/// <para>
/// The set is FINITE because it is read off the program: a pack is only ever bound by a use site,
/// and a program has finitely many. Nothing here searches for arities that could exist.
/// </para>
/// </summary>
internal static class MonomorphSet
{
    /// <summary>
    /// Every (variadic definition, arity) pair the program constructs, ordered so emission is
    /// deterministic.
    /// </summary>
    public static IReadOnlyList<(MistType Definition, int Arity)> Discover(TypeInterner interner)
    {
        var found = new SortedSet<(string Name, int Arity)>();
        var definitions = new Dictionary<string, MistType>(StringComparer.Ordinal);

        foreach(var constructed in interner.ConstructedTypes)
        {
            if(constructed.Definition is not MistType definition
                || definition is not IHasTypeParameters generic
                || Arity(generic, constructed) is not { } arity)
            {
                continue;
            }

            _ = found.Add((definition.FullyQualifiedName, arity));
            definitions[definition.FullyQualifiedName] = definition;
        }

        return [.. found.Select(entry => (definitions[entry.Name], entry.Arity))];
    }

    /// <summary>
    /// How many types the construction's pack holds, or null when the definition has no pack or the
    /// construction did not bind one -- an open construction inside the family's own declaration
    /// among them, which names no arity and asks for no specialization.
    /// </summary>
    private static int? Arity(IHasTypeParameters generic, ConstructedType constructed)
    {
        var parameters = generic.TypeParametersList;

        for(var i = 0; i < parameters.Count && i < constructed.TypeArguments.Count; i++)
        {
            if(parameters[i].PackKind == TypePackKind.None)
            {
                continue;
            }

            return constructed.TypeArguments[i] is LiftedType { Value: TypePackValue pack }
                && !pack.Elements.Any(element => element is SpreadType)
                    ? pack.Elements.Length
                    : null;
        }

        return null;
    }
}