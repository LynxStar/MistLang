using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
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
    /// The C# name a family takes at one arity.
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
    public static string SpecializedName(string declaredName, int arity)
    {
        return $"{MistType.StripArity(declaredName)}_{arity}";
    }

    /// <summary>
    /// One specialization per width a variadic METHOD was called at, or none when it declares no
    /// pack. A method never called is never specialized, which is correct: nothing names it.
    /// </summary>
    public static IEnumerable<Monomorph> MethodSpecializations(Method method, VariadicUseRegistry uses)
    {
        if(Monomorph.PackIn(method.TypeParametersList) is not { } pack)
        {
            yield break;
        }

        foreach(var width in uses.WidthsOf(method))
        {
            if(Monomorph.For(pack, width) is { } monomorph)
            {
                yield return monomorph;
            }
        }
    }

    /// <summary>
    /// Every (variadic definition, arity) pair the program constructs, ordered so emission is
    /// deterministic.
    /// </summary>
    public static IReadOnlyList<(MistType Definition, int Arity)> Discover(TypeInterner interner)
    {
        var found = new SortedSet<(string Name, int Arity)>();
        var definitions = new Dictionary<string, MistType>(StringComparer.Ordinal);
        var pending = new Queue<(MistType Definition, int Arity)>();

        void enqueue(MistType definition, int arity)
        {
            if(found.Add((definition.FullyQualifiedName, arity)))
            {
                definitions[definition.FullyQualifiedName] = definition;
                pending.Enqueue((definition, arity));
            }
        }

        foreach(var constructed in interner.ConstructedTypes)
        {
            if(constructed.Definition is MistType definition
                && definition is IHasTypeParameters generic
                && Arity(generic, constructed) is { } arity)
            {
                enqueue(definition, arity);
            }
        }

        // A SPECIALIZATION ASKS FOR MORE. `Outer<params TArgs> { Inner<TArgs...> nested; }` at width
        // two needs Inner at width two, and nothing constructs Inner directly -- the only
        // construction of it is symbolic and names no arity until Outer's is fixed. So the set is
        // closed rather than collected: each specialization is walked for the constructions its own
        // width implies, until nothing new appears. Finite because widths come from a finite
        // program and each pair is enqueued once.
        while(pending.Count > 0)
        {
            var (definition, arity) = pending.Dequeue();

            if(definition is not MemberType member || Monomorph.For(PackIn(definition), arity) is not { } monomorph)
            {
                continue;
            }

            using(monomorph.Enter())
            {
                foreach(var implied in ImpliedConstructions(member, monomorph))
                {
                    enqueue(implied.Definition, implied.Arity);
                }
            }
        }

        return [.. found.Select(entry => (definitions[entry.Name], entry.Arity))];
    }

    private static TypeParameter? PackIn(MistType definition)
    {
        return definition is IHasTypeParameters generic ? Monomorph.PackIn(generic.TypeParametersList) : null;
    }

    private static IEnumerable<(MistType Definition, int Arity)> ImpliedConstructions(
        MemberType member, Monomorph monomorph)
    {
        foreach(var signature in SignatureTypes(member))
        {
            foreach(var constructed in Constructions(signature, new HashSet<IMistType>()))
            {
                if(constructed.Definition is MistType definition
                    && definition is IHasTypeParameters generic
                    && WidthUnder(generic, constructed, monomorph) is { } arity)
                {
                    yield return (definition, arity);
                }
            }
        }
    }

    private static int? WidthUnder(IHasTypeParameters generic, ConstructedType constructed, Monomorph monomorph)
    {
        var parameters = generic.TypeParametersList;

        for(var i = 0; i < parameters.Count && i < constructed.TypeArguments.Count; i++)
        {
            if(parameters[i].PackKind == TypePackKind.None)
            {
                continue;
            }

            if(constructed.TypeArguments[i] is not LiftedType { Value: TypePackValue pack })
            {
                return null;
            }

            var width = 0;

            foreach(var element in pack.Elements)
            {
                if(element is not SpreadType spread)
                {
                    width++;
                    continue;
                }

                if(!ReferenceEquals(spread.Operand, monomorph.Pack))
                {
                    return null;
                }

                width += monomorph.Expansion.Count;
            }

            return width;
        }

        return null;
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