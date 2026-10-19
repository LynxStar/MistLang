using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

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
    public static Monomorph? TypeSpecialization(MemberType type, IReadOnlyList<int> widths)
    {
        return Monomorph.For(type.TypeParametersList, widths, ContainedParameters(type));
    }

    private static IEnumerable<TypeParameter> ContainedParameters(MistType type)
    {
        if(type is IHasTypeParameters generic)
        {
            foreach(var parameter in generic.TypeParametersList)
            {
                yield return parameter;
            }
        }

        if(type is MemberType member)
        {
            foreach(var method in member.EnumerateDelegates())
            {
                foreach(var parameter in method.TypeParametersList)
                {
                    yield return parameter;
                }
            }
        }

        foreach(var nested in type.NestedTypes)
        {
            foreach(var parameter in ContainedParameters(nested))
            {
                yield return parameter;
            }
        }
    }

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
            if(Monomorph.For(method.TypeParametersList, widths, EnclosingParameters(method)) is { } monomorph)
            {
                yield return monomorph;
            }
        }
    }

    private static IEnumerable<TypeParameter> EnclosingParameters(Method method)
    {
        if(method.DeclaringBlock is { } block)
        {
            foreach(var parameter in block.TypeParametersList)
            {
                yield return parameter;
            }
        }

        for(MistType? type = method.DeclaringType; type is not null; type = type.DeclaringType)
        {
            if(type is IHasTypeParameters generic)
            {
                foreach(var parameter in generic.TypeParametersList)
                {
                    yield return parameter;
                }
            }
        }
    }

    /// <summary>
    /// Every (variadic definition, widths) pair the program constructs, ordered so emission is
    /// deterministic, and beside it the widths every variadic method is called at.
    /// </summary>
    public static (IReadOnlyList<(MistType Definition, IReadOnlyList<int> Widths)> Types, VariadicUseRegistry Methods) Discover(
        ProgramModelClass model)
    {
        var found = new SortedSet<(string Name, string Widths)>();
        var definitions = new Dictionary<string, MistType>(StringComparer.Ordinal);
        var entries = new Dictionary<(string, string), IReadOnlyList<int>>();
        var methods = new VariadicUseRegistry();
        var pending = new Queue<Monomorph>();

        void enqueue(IReadOnlyList<TypeParameter> parameters, IReadOnlyList<int> widths)
        {
            if(Monomorph.For(parameters, widths) is { } monomorph)
            {
                pending.Enqueue(monomorph);
            }
        }

        void require(ConstructedType constructed, Monomorph? monomorph)
        {
            if(constructed.Definition is not MistType definition
                || definition is not IHasTypeParameters generic
                || Widths(generic.TypeParametersList, constructed.TypeArguments, monomorph) is not { } widths)
            {
                return;
            }

            var key = (definition.FullyQualifiedName, string.Join("_", widths));

            if(found.Add(key))
            {
                definitions[definition.FullyQualifiedName] = definition;
                entries[key] = widths;
                enqueue(generic.TypeParametersList, widths);
            }
        }

        var constructions = model.TypeInterner.ConstructedTypes.ToList();
        var calls = model.GenericCalls.All().ToList();

        foreach(var constructed in constructions)
        {
            require(constructed, monomorph: null);
        }

        foreach(var (method, widths) in model.VariadicUses.All())
        {
            if(methods.Record(method, widths))
            {
                enqueue(method.TypeParametersList, widths);
            }
        }

        // A SPECIALIZATION ASKS FOR MORE. `Outer<params TArgs> { Inner<TArgs...> nested; }` at width
        // two needs Inner at width two, and nothing constructs Inner directly -- the only
        // construction of it is symbolic and names no width until Outer's is fixed. A call forwarding
        // the pack asks the same of the method it calls. So the set is closed rather than collected:
        // each specialization is read against every construction and call that names its packs,
        // wherever in the declaration they were written, until nothing new appears. Finite because
        // widths come from a finite program and each pair is enqueued once.
        while(pending.Count > 0)
        {
            var monomorph = pending.Dequeue();

            foreach(var constructed in constructions)
            {
                require(constructed, monomorph);
            }

            foreach(var (method, typeArguments) in calls)
            {
                if(Widths(method.TypeParametersList, typeArguments, monomorph) is { } widths && methods.Record(method, widths))
                {
                    enqueue(method.TypeParametersList, widths);
                }
            }
        }

        return ([.. found.Select(entry => (definitions[entry.Name], entries[entry]))], methods);
    }

    /// <summary>
    /// How many types each of the definition's packs holds in this construction, or null when it
    /// has no pack or one the construction left open -- a construction inside the family's own
    /// declaration among them, which names no width and asks for no specialization unless the
    /// specialization in scope supplies one.
    /// </summary>
    private static IReadOnlyList<int>? Widths(
        IReadOnlyList<TypeParameter> parameters, IReadOnlyList<IMistType> arguments, Monomorph? monomorph)
    {
        var widths = new List<int>();

        for(var i = 0; i < parameters.Count && i < arguments.Count; i++)
        {
            if(parameters[i].PackKind == TypePackKind.None)
            {
                continue;
            }

            if(arguments[i] is not LiftedType { Value: TypePackValue pack }
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