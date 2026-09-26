using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramModel;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// Which generic declarations are emitted once per instantiation instead of once as a C# generic, and
/// at which instantiations.
/// <para>
/// A UNION OVER A TYPE PARAMETER HAS NO C# SPELLING THAT HOLDS ACROSS INSTANTIATIONS. Mist sorts a
/// union's constituents and merges duplicates after substitution -- <c>T | R</c> is <c>int | string</c>
/// at both <c>&lt;int, string&gt;</c> and <c>&lt;string, int&gt;</c>, and <c>int</c> at
/// <c>&lt;int, int&gt;</c> -- while C# fills a generic's parameters into one declaration by position, so
/// no carrier written once in terms of T and R agrees with all three. A conversion cannot bridge them
/// where the union sits inside another type, since converting a <c>Cell&lt;...&gt;</c> would mean
/// copying an object. A declaration whose own types write such a union is therefore emitted per
/// instantiation, where every union is closed and spelled exactly as closed code spells it. An optional
/// over a parameter constrained by the <c>struct</c> or <c>class</c> keyword is left alone: Nullable of
/// K, or the bare reference, already coincides with every closed spelling.
/// </para>
/// <para>
/// THE SET IS READ OFF THE PROGRAM, as the variadic widths are (see <see cref="MonomorphSet"/>): a Mist
/// program is compiled whole and nothing outside it instantiates its generics, so every instantiation
/// the emitted code names is either interned already or reached by substituting one that is. A
/// declaration naming a specialized one over its own parameters is specialized too, having no C# name
/// for that construction otherwise; each instance's closed unions are INTERNED rather than only spelled,
/// the carrier file being written from the interner; and names are built from the arguments' qualified
/// names, so same-named types from two namespaces cannot share a specialization. A TYPE's
/// parameters are specialized, and so are a generic METHOD's, from the type arguments each call was bound
/// with; a method is emitted once per instance of its declaring type where that type is specialized too.
/// </para>
/// <para>
/// OVERLOADS AN INSTANCE MAKES IDENTICAL are emitted under distinct names: `Take(K?)` beside `Take(int?)`
/// is one C# signature twice in `Lifted<int>`, and Mist has already chosen which one each call means,
/// so the call is named for its choice rather than left for C# to resolve between two equals. The one
/// that answers a slot of an abstract the type conforms to keeps its name, C# finding an implementation
/// by name alone; a call through a derived receiver is named at the base construction it reaches.
/// </para>
/// </summary>
internal sealed class UnionSpecialization
{
    private const int MaximumInstancesPerDeclaration = 256;

    [ThreadStatic]
    private static UnionSpecialization? active;

    [ThreadStatic]
    private static ConstructedType? current;

    [ThreadStatic]
    private static MethodInstance? currentMethod;

    private readonly Dictionary<MemberType, List<ConstructedType>> types;

    private readonly Dictionary<Method, List<IReadOnlyList<IMistType>>> methods;

    private readonly Dictionary<TypeParameter, Method> methodsByFirstParameter;

    private readonly TypeInterner interner;

    private UnionSpecialization(
        Dictionary<MemberType, List<ConstructedType>> types,
        Dictionary<Method, List<IReadOnlyList<IMistType>>> methods,
        TypeInterner interner)
    {
        this.types = types;
        this.methods = methods;
        this.interner = interner;
        methodsByFirstParameter = methods.Keys.ToDictionary(method => method.TypeParametersList[0]);
    }

    public static IDisposable Enter(ProgramModelClass model, DiagnosticBag diagnostics)
    {
        var enclosing = active;
        active = Discover(model, diagnostics);
        return new Scope(() => active = enclosing);
    }

    public static bool Specializes(IMistType definition)
    {
        return definition is MemberType member && active?.types.ContainsKey(member) == true;
    }

    public static bool Specializes(Method method)
    {
        return active?.methods.ContainsKey(method) == true;
    }

    public static IReadOnlyList<ConstructedType> InstancesOf(MemberType definition)
    {
        return active?.types.GetValueOrDefault(definition) ?? [];
    }

    public static IReadOnlyList<IReadOnlyList<IMistType>> InstancesOf(Method method)
    {
        return active?.methods.GetValueOrDefault(method) ?? [];
    }

    public static Method? MethodDeclaring(IReadOnlyList<TypeParameter>? parameters)
    {
        return parameters is [var first, ..] ? active?.methodsByFirstParameter.GetValueOrDefault(first) : null;
    }

    public static IDisposable For(ConstructedType instance)
    {
        var enclosing = current;
        current = instance;
        return new Scope(() => current = enclosing);
    }

    public static IDisposable For(Method method, IReadOnlyList<IMistType> arguments)
    {
        var enclosing = currentMethod;
        currentMethod = new MethodInstance(method, arguments);
        return new Scope(() => currentMethod = enclosing);
    }

    public static ConstructedType? Current()
    {
        return current;
    }

    public static MethodInstance? CurrentMethod()
    {
        return currentMethod;
    }

    public static string? NameOf(ConstructedType constructed)
    {
        return Specializes(constructed.Definition) && TypeParameterReach.Collect(constructed).Count == 0
            ? Name(constructed)
            : null
            ;
    }

    public static string Name(ConstructedType instance)
    {
        return Name(MistType.StripArity(instance.Definition.Name), instance.TypeArguments);
    }

    public static string Name(string declaredName, IReadOnlyList<IMistType> arguments)
    {
        var parts = arguments.Select(argument => Identifier(argument.FullyQualifiedName));

        return $"{declaredName}__{string.Join("__", parts)}";
    }

    public static string? CollidingName(Method method, IMistType receiver)
    {
        if(method.DeclaringType is not MemberType type
            || !Specializes(type)
            || DeclaringConstruction(receiver, type) is not { } instance)
        {
            return null;
        }

        var substitution = TypeSubstitution.ForReceiver(instance);

        string signature(Method candidate)
        {
            var parameters = candidate.ParametersList.Select(
                parameter => substitution.Apply(parameter.Type, interner: null).FullyQualifiedName);

            return $"{candidate.Name}`{candidate.TypeParametersList.Count}({string.Join(",", parameters)})";
        }

        var own = signature(method);

        return type.MethodsList.Count(candidate => signature(candidate) == own) > 1 && !FillsSlot(method, type)
            ? $"{method.Name}__o{type.MethodsList.IndexOf(method)}"
            : null
            ;
    }

    private static ConstructedType? DeclaringConstruction(IMistType receiver, MemberType declaring)
    {
        for(var current = (IMistType?)receiver; current is not null; current = BaseOf(current))
        {
            if(current is ConstructedType constructed && ReferenceEquals(constructed.Definition, declaring))
            {
                return constructed;
            }
        }

        return null;
    }

    private static IMistType? BaseOf(IMistType type)
    {
        return (type, type is ConstructedType constructed ? constructed.Definition : type) switch
        {
            (ConstructedType construction, ConcreteType { BaseType: { } baseType })
                => TypeSubstitution.ForReceiver(construction).Apply(baseType, interner: null),
            (_, ConcreteType { BaseType: var baseType }) => baseType,
            _ => null,
        };
    }

    private static bool FillsSlot(Method method, MemberType type)
    {
        var interner = active!.interner;
        var pending = new Queue<IMistType>(type.BaseAbstractTypes);
        var visited = new HashSet<string>(StringComparer.Ordinal);

        while(pending.Count > 0)
        {
            var reference = pending.Dequeue();
            var definition = reference is ConstructedType constructed ? constructed.Definition : reference;

            if(definition is not AbstractType abstractType || !visited.Add(abstractType.FullyQualifiedName))
            {
                continue;
            }

            var substitution = TypeSubstitution.ForReceiver(reference as ConstructedType);

            if(abstractType.MethodsList.Exists(slot => slot.Body is null
                && !slot.Modifiers.IsStatic
                && slot.Name == method.Name
                && SlotSignatures.Answers(slot, method, substitution, interner)))
            {
                return true;
            }

            foreach(var baseAbstract in abstractType.BaseAbstractTypes)
            {
                pending.Enqueue(substitution.Apply(baseAbstract, interner));
            }
        }

        return false;
    }

    public static bool IsClosed(IReadOnlyList<IMistType> arguments)
    {
        return arguments.All(argument => TypeParameterReach.Collect(argument).Count == 0);
    }

    private static UnionSpecialization Discover(ProgramModelClass model, DiagnosticBag diagnostics)
    {
        var interner = model.TypeInterner;
        var typeOwners = new Dictionary<TypeParameter, MemberType>();
        var methodOwners = new Dictionary<TypeParameter, Method>();

        foreach(var type in model.TypesByFQN.Values.OfType<MemberType>())
        {
            foreach(var parameter in type.TypeParametersList)
            {
                _ = typeOwners.TryAdd(parameter, type);
            }

            foreach(var method in type.MethodsList)
            {
                foreach(var parameter in method.TypeParametersList)
                {
                    _ = methodOwners.TryAdd(parameter, method);
                }
            }
        }

        var calls = model.GenericCalls.All().ToList();
        var requiredTypes = new HashSet<MemberType>();
        var requiredMethods = new HashSet<Method>();

        void require(IMistType type)
        {
            foreach(var parameter in TypeParameterReach.Collect(type))
            {
                if(typeOwners.TryGetValue(parameter, out var owner))
                {
                    _ = requiredTypes.Add(owner);
                }
                else if(methodOwners.TryGetValue(parameter, out var method))
                {
                    _ = requiredMethods.Add(method);
                }
            }
        }

        foreach(var union in interner.UnionTypes.ToList())
        {
            if(NeedsSpecialization(union))
            {
                require(union);
            }
        }

        var before = -1;

        while(requiredTypes.Count + requiredMethods.Count != before)
        {
            before = requiredTypes.Count + requiredMethods.Count;

            foreach(var constructed in interner.ConstructedTypes.ToList())
            {
                if(constructed.Definition is MemberType definition && requiredTypes.Contains(definition))
                {
                    require(constructed);
                }
            }

            foreach(var (method, arguments) in calls)
            {
                if(requiredMethods.Contains(method))
                {
                    arguments.ToList().ForEach(require);
                }
            }
        }

        var types = requiredTypes.ToDictionary(type => type, _ => new List<ConstructedType>());
        var methods = requiredMethods.ToDictionary(method => method, _ => new List<IReadOnlyList<IMistType>>());
        var seenByDeclaration = new Dictionary<object, HashSet<string>>();
        var units = new Queue<(TypeSubstitution Substitution, IReadOnlyList<TypeParameter> Own)>();
        var unbounded = new HashSet<string>(StringComparer.Ordinal);

        HashSet<string> seen(object declaration)
        {
            if(!seenByDeclaration.TryGetValue(declaration, out var names))
            {
                names = new HashSet<string>(StringComparer.Ordinal);
                seenByDeclaration[declaration] = names;
            }

            return names;
        }

        bool room<T>(List<T> list, string declaration)
        {
            if(list.Count < MaximumInstancesPerDeclaration)
            {
                return true;
            }

            _ = unbounded.Add(declaration);
            return false;
        }

        void admitType(ConstructedType candidate)
        {
            if(candidate.Definition is not MemberType definition
                || !types.TryGetValue(definition, out var list)
                || TypeParameterReach.Collect(candidate).Count > 0
                || !room(list, MistType.StripArity(definition.Name))
                || !seen(definition).Add(Name(candidate)))
            {
                return;
            }

            list.Add(candidate);

            var substitution = TypeSubstitution.ForReceiver(candidate);
            units.Enqueue((substitution, definition.TypeParametersList));

            foreach(var method in definition.MethodsList)
            {
                foreach(var arguments in methods.GetValueOrDefault(method) ?? [])
                {
                    units.Enqueue((
                        substitution.Extend(TypeSubstitution.ForMethod(method, arguments)),
                        [.. definition.TypeParametersList, .. method.TypeParametersList]));
                }
            }
        }

        void admitMethod(Method method, IReadOnlyList<IMistType> arguments)
        {
            if(!methods.TryGetValue(method, out var list)
                || !IsClosed(arguments)
                || !room(list, method.Name)
                || !seen(method).Add(Name(method.Name, arguments)))
            {
                return;
            }

            list.Add(arguments);

            var own = TypeSubstitution.ForMethod(method, arguments);

            if(method.DeclaringType is MemberType declaring && types.TryGetValue(declaring, out var instances))
            {
                foreach(var instance in instances)
                {
                    units.Enqueue((
                        TypeSubstitution.ForReceiver(instance).Extend(own),
                        [.. declaring.TypeParametersList, .. method.TypeParametersList]));
                }
            }
            else
            {
                units.Enqueue((own, method.TypeParametersList));
            }
        }

        foreach(var constructed in interner.ConstructedTypes.ToList())
        {
            admitType(constructed);
        }

        foreach(var (method, arguments) in calls)
        {
            admitMethod(method, arguments);
        }

        while(units.Count > 0)
        {
            var (substitution, own) = units.Dequeue();

            foreach(var union in interner.UnionTypes.ToList())
            {
                if(Mentions(union, own))
                {
                    _ = substitution.Apply(union, interner);
                }
            }

            foreach(var constructed in interner.ConstructedTypes.ToList())
            {
                if(Mentions(constructed, own) && substitution.Apply(constructed, interner) is ConstructedType closed)
                {
                    admitType(closed);
                }
            }

            foreach(var (method, arguments) in calls)
            {
                if(methods.ContainsKey(method) && arguments.Any(argument => Mentions(argument, own)))
                {
                    admitMethod(method, [.. arguments.Select(argument => substitution.Apply(argument, interner))]);
                }
            }
        }

        foreach(var declaration in unbounded)
        {
            diagnostics.Report(
                BackendDiagnostics.UnboundedSpecialization, default, string.Empty, declaration, MaximumInstancesPerDeclaration);
        }

        return new UnionSpecialization(types, methods, interner);
    }

    private static bool NeedsSpecialization(UnionType union)
    {
        if(TypeParameterReach.Collect(union).Count == 0)
        {
            return false;
        }

        return OptionalUnion.InnerType(union) is not { } inner
            || (inner is TypeParameter parameter && !HasKindConstraint(parameter))
            ;
    }

    private static bool HasKindConstraint(TypeParameter parameter)
    {
        return parameter.ConstraintsList.Exists(constraint => constraint is KeywordConstraint
        {
            Kind: TypeParameterConstraintKind.Struct or TypeParameterConstraintKind.Class or TypeParameterConstraintKind.Stampable,
        });
    }

    private static bool Mentions(IMistType type, IReadOnlyList<TypeParameter> parameters)
    {
        return TypeParameterReach.Collect(type).Any(parameters.Contains);
    }

    private static string Identifier(string name)
    {
        return new string([.. name.Select(character => char.IsLetterOrDigit(character) ? character : '_')]);
    }

    internal sealed record MethodInstance(Method Method, IReadOnlyList<IMistType> Arguments);

    private sealed class Scope(Action restore) : IDisposable
    {
        public void Dispose()
        {
            restore();
        }
    }
}