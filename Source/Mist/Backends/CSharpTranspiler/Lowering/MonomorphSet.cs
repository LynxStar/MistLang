using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// The widths each variadic family is actually used at, which is what a C# emission has to be
/// specialized to: the language has no pack, so one declaration becomes one type per combination of
/// widths its packs were bound to.
/// <para>
/// PER WIDTH AND NOT PER CONSTRUCTION, which is the whole economy of it. <c>Box&lt;int, string&gt;</c>
/// and <c>Box&lt;bool, char&gt;</c> want the same C# declaration -- a generic of two parameters --
/// and differ only in what they pass it. Specializing per construction would emit a type per
/// distinct argument list and lose genericity that C# is perfectly able to express.
/// </para>
/// <para>
/// The set is FINITE because it is read off the program: a pack is only ever bound by a use site,
/// and a program has finitely many. Nothing here searches for widths that could exist.
/// </para>
/// </summary>
internal static class MonomorphSet
{
    /// <summary>
    /// The C# name a family takes at one combination of widths.
    /// <para>
    /// SUFFIXED TO KEEP IT OFF AN ORDINARY GENERIC'S NAME, not to tell the specializations apart --
    /// C# overloads generic types on arity, so those are already distinct without help. What is not
    /// distinct is a specialization against an unrelated same-named generic: Mist names a family by
    /// its SLOT count, so `Box&lt;params T&gt;` and `Box&lt;A, B&gt;` are Box&lt;&gt; and Box&lt;,&gt;
    /// and may both be declared -- while the first, specialized to arity two, wants exactly the C#
    /// name the second has. Reachable through a bracketed construction, `Box&lt;[int, string]&gt;`,
    /// which resolves to the variadic rather than to the exact-arity ordinary one.
    /// </para>
    /// </summary>
    public static string SpecializedName(string declaredName, IReadOnlyList<int> widths)
    {
        return $"{MistType.StripArity(declaredName)}_{string.Join("_", widths)}";
    }

    /// <summary>
    /// One specialization per way a variadic METHOD was called, or none when it declares no pack. A
    /// method never called is never specialized, which is correct: nothing names it.
    /// </summary>
    public static IEnumerable<Monomorph> MethodSpecializations(Method method, VariadicUseRegistry uses)
    {
        foreach(var widths in uses.WidthsOf(method))
        {
            if(Monomorph.For(method.TypeParametersList, widths) is { } monomorph)
            {
                yield return monomorph;
            }
        }
    }

    /// <summary>
    /// Every (variadic definition, widths) pair the program constructs, ordered so emission is
    /// deterministic.
    /// </summary>
    public static IReadOnlyList<(MistType Definition, IReadOnlyList<int> Widths)> Discover(TypeInterner interner)
    {
        var found = new SortedSet<(string Name, string Widths)>();
        var definitions = new Dictionary<string, MistType>(StringComparer.Ordinal);
        var entries = new Dictionary<(string, string), IReadOnlyList<int>>();
        var pending = new Queue<(MistType Definition, IReadOnlyList<int> Widths)>();

        void enqueue(MistType definition, IReadOnlyList<int> widths)
        {
            var key = (definition.FullyQualifiedName, string.Join("_", widths));

            if(found.Add(key))
            {
                definitions[definition.FullyQualifiedName] = definition;
                entries[key] = widths;
                pending.Enqueue((definition, widths));
            }
        }

        foreach(var constructed in interner.ConstructedTypes)
        {
            if(constructed.Definition is MistType definition
                && definition is IHasTypeParameters generic
                && Widths(generic, constructed) is { } widths)
            {
                enqueue(definition, widths);
            }
        }

        // A SPECIALIZATION ASKS FOR MORE. `Outer<params TArgs> { Inner<TArgs...> nested; }` at width
        // two needs Inner at width two, and nothing constructs Inner directly -- the only
        // construction of it is symbolic and names no width until Outer's is fixed. So the set is
        // closed rather than collected: each specialization is walked for the constructions its own
        // widths imply, until nothing new appears. Finite because widths come from a finite program
        // and each pair is enqueued once.
        while(pending.Count > 0)
        {
            var (definition, widths) = pending.Dequeue();

            if(definition is not MemberType member
                || definition is not IHasTypeParameters generic
                || Monomorph.For(generic.TypeParametersList, widths) is not { } monomorph)
            {
                continue;
            }

            using(monomorph.Enter())
            {
                foreach(var implied in ImpliedConstructions(member, monomorph))
                {
                    enqueue(implied.Definition, implied.Widths);
                }
            }
        }

        return [.. found.Select(entry => (definitions[entry.Name], entries[entry]))];
    }

    private static IEnumerable<(MistType Definition, IReadOnlyList<int> Widths)> ImpliedConstructions(
        MemberType member, Monomorph monomorph)
    {
        foreach(var signature in SignatureTypes(member))
        {
            foreach(var constructed in Constructions(signature, new HashSet<IMistType>()))
            {
                if(constructed.Definition is MistType definition
                    && definition is IHasTypeParameters generic
                    && WidthsUnder(generic, constructed, monomorph) is { } widths)
                {
                    yield return (definition, widths);
                }
            }
        }
    }

    private static IReadOnlyList<int>? WidthsUnder(
        IHasTypeParameters generic, ConstructedType constructed, Monomorph monomorph)
    {
        return Widths(generic, constructed, monomorph);
    }

    private static IEnumerable<ConstructedType> Constructions(IMistType type, HashSet<IMistType> seen)
    {
        if(!seen.Add(type))
        {
            yield break;
        }

        if(type is ConstructedType constructed)
        {
            yield return constructed;

            foreach(var argument in constructed.TypeArguments)
            {
                foreach(var nested in Constructions(argument, seen))
                {
                    yield return nested;
                }
            }
        }

        var children = type switch
        {
            LiftedType { Value: TypePackValue pack } => pack.Elements,
            TupleType tuple => tuple.ElementTypes,
            NamedTupleType named => named.ElementTypes,
            UnionType union => union.Constituents,
            _ => null,
        };

        foreach(var child in children ?? [])
        {
            foreach(var nested in Constructions(child, seen))
            {
                yield return nested;
            }
        }
    }

    private static IEnumerable<IMistType> SignatureTypes(MemberType member)
    {
        foreach(var method in member.MethodsList)
        {
            yield return method.ReturnType;

            foreach(var parameter in method.ParametersList)
            {
                yield return parameter.Type;
            }
        }

        foreach(var property in member.PropertiesList)
        {
            yield return property.Type;
        }

        foreach(var baseType in member.BaseAbstractTypes)
        {
            yield return baseType;
        }

        if(member is not ConcreteType concrete)
        {
            yield break;
        }

        foreach(var field in concrete.FieldsList)
        {
            yield return field.Type;
        }

        foreach(var constructor in concrete.ConstructorsList)
        {
            foreach(var parameter in constructor.ParametersList)
            {
                yield return parameter.Type;
            }
        }
    }

    /// <summary>
    /// How many types each of the definition's packs holds in this construction, or null when it
    /// has no pack or one the construction left open -- a construction inside the family's own
    /// declaration among them, which names no width and asks for no specialization unless the
    /// specialization in scope supplies one.
    /// </summary>
    private static IReadOnlyList<int>? Widths(
        IHasTypeParameters generic, ConstructedType constructed, Monomorph? monomorph = null)
    {
        var parameters = generic.TypeParametersList;
        var widths = new List<int>();

        for(var i = 0; i < parameters.Count && i < constructed.TypeArguments.Count; i++)
        {
            if(parameters[i].PackKind == TypePackKind.None)
            {
                continue;
            }

            if(constructed.TypeArguments[i] is not LiftedType { Value: TypePackValue pack }
                || Width(pack, monomorph) is not { } width)
            {
                return null;
            }

            widths.Add(width);
        }

        return widths.Count == 0 ? null : widths;
    }

    private static int? Width(TypePackValue pack, Monomorph? monomorph)
    {
        var width = 0;

        foreach(var element in pack.Elements)
        {
            if(element is not SpreadType spread)
            {
                width++;
                continue;
            }

            if(monomorph is null || spread.Operand is not TypeParameter operand || !monomorph.Binds(operand))
            {
                return null;
            }

            width += monomorph.Expansion(operand).Count;
        }

        return width;
    }
}