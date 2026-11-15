using System.Globalization;
using System.Text;
using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// The C# names that keep apart members of one name whose emitted parameter lists coincide because a slot
/// left the C# type (runtime-dependent-values design, <em>Identity by value: the first delivery</em>). The
/// binder has already chosen the overload, so only the C# names have to differ. Decided once over the whole
/// program, and keyed by declaration so a closed copy the binder makes of a member reads the same name,
/// because a member lands in more C# types than the one declaring it: a struct holds its flattened
/// ancestors' members, a conformer restates an abstract's defaults, and an interface sees the members of
/// the interfaces it extends. A class also meets its base classes' members, Mist, intrinsic or CLR, because
/// C# hides a base member under a derived one with the same parameters, and a method group, an indexer read
/// or an interface answer through the derived type would reach the wrong one. Every member reached through a
/// base, an ancestor or an abstract is read at the closing written for it, and a type's own type parameter
/// meets a type carrying an erased slot, since at a closing to that type C# prefers the member that names it
/// outright; a method's own type parameter meets nothing, because every call spells its type arguments. An abstract's member and every member answering it by name are
/// one name group, since C# implements an interface member by name. A name is the group's root member's
/// Mist name with that member's own Mist signature appended, so adding or removing another overload never
/// changes it, and it is taken only by a group that coincides with another. A group holding a member a CLR
/// slot names, or one with no Mist source, keeps its name and the others are renamed around it; two such
/// groups coinciding, or one slot answered at two closings, is one interface implemented at two values of a
/// slot, which nothing can name apart, and stays <see cref="BackendDiagnostics.CoincidingOverloads"/>. C# cannot rename a constructor or an
/// indexer: a renamed constructor takes an empty struct of its new name, nested in its type, as a hidden
/// leading parameter that every call fills with its default, and a renamed indexer is emitted as accessor
/// methods whose setter returns the value, so an assignment through it is still an expression.
/// </summary>
internal sealed class OverloadNames : IDisposable
{
    private const string Conversions = "conversion";
    private const string Constructors = ".ctor";
    private const string Indexers = "this[]";

    [ThreadStatic]
    private static OverloadNames? current;

    private readonly IReadOnlyDictionary<SyntaxNode, string> renamed;

    private OverloadNames(IReadOnlyDictionary<SyntaxNode, string> renamed)
    {
        this.renamed = renamed;
    }

    public static IDisposable Enter(ProgramModelClass model, DiagnosticBag diagnostics)
    {
        return Enter(Decide(SourceTypes.Of(model).OfType<MemberType>(), SourceTypes.ExtensionsOf(model), model.TypeInterner), diagnostics);
    }

    public static IDisposable Enter(OverloadDecision decision, DiagnosticBag diagnostics)
    {
        foreach(var (earlier, later) in decision.Unnamed)
        {
            Report(earlier, later, diagnostics);
        }

        return Enter(decision.Renamed);
    }

    public static IDisposable Enter(IReadOnlyDictionary<SyntaxNode, string> renamed)
    {
        current = new OverloadNames(renamed);
        return current;
    }

    public void Dispose()
    {
        current = null;
    }

    public static string Of(Method method)
    {
        return Renamed(method) ?? method.Name;
    }

    public static string? Renamed(Method method)
    {
        return Lookup(method.DeclarationNode);
    }

    public static string? Renamed(Indexer indexer)
    {
        return Lookup(indexer.DeclarationNode);
    }

    public static string? MarkerParameter(Constructor constructor)
    {
        return Renamed(constructor) is { } marker ? $"{marker} __overload" : null;
    }

    public static string? MarkerDeclaration(Constructor constructor)
    {
        return Renamed(constructor) is { } marker ? $"public readonly struct {marker} {{ }}" : null;
    }

    public static string? Marker(Method? constructor, IMistType created)
    {
        return constructor is Constructor && Renamed(constructor) is { } marker ? $"default({TypeNameEmitter.Emit(created)}.{marker})" : null;
    }

    public static string? Marker(Method? constructor)
    {
        return constructor is Constructor && Renamed(constructor) is { } marker ? $"default({marker})" : null;
    }

    public static Constructor? ConstructorFor(IMistType product, IReadOnlyList<IMistType> signature)
    {
        if(current is null || Defaultability.DeclarationOf(product) is not ConcreteType declaration)
        {
            return null;
        }

        var closing = TypeSubstitution.ForReceiver(product as ConstructedType);
        var constructor = declaration.ConstructorsList.FirstOrDefault(candidate => !candidate.Modifiers.IsStatic
            && candidate.ParametersList.Count == signature.Count
            && candidate.ParametersList.Zip(signature).All(pair =>
                closing.Apply(pair.First.Type, interner: null).FullyQualifiedName == pair.Second.FullyQualifiedName));

        return constructor;
    }

    public static string Getter(string renamed)
    {
        return $"get_{renamed}";
    }

    public static string Setter(string renamed)
    {
        return $"set_{renamed}";
    }

    private static string? Lookup(SyntaxNode? node)
    {
        return node is not null && current is { } names && names.renamed.TryGetValue(node, out var name) ? name : null;
    }

    internal static OverloadDecision Decide(IEnumerable<MemberType> types, IEnumerable<ExtensionBlock> blocks, TypeInterner? interner)
    {
        using var oblivious = TypeNameEmitter.Oblivious();
        var declared = types.ToList();
        var groups = new NameGroups();

        foreach(var concrete in declared.OfType<ConcreteType>())
        {
            Link(concrete, groups, interner);
        }

        var pairs = new List<(object Earlier, object Later)>();
        var unnamed = new List<(Method Earlier, Method Later)>();
        var closings = new Dictionary<object, object>(ReferenceEqualityComparer.Instance);

        foreach(var scope in declared.SelectMany(type => Scopes(type, closings)).Concat(blocks.Select(block => block.MethodsList.Cast<object>().ToList())))
        {
            Mark(scope, closings, groups, pairs, unnamed);
        }

        pairs = [.. pairs.Distinct()];
        unnamed = [.. unnamed.Distinct()];

        var renamed = new Dictionary<SyntaxNode, string>();
        var pinned = new HashSet<object>(ReferenceEqualityComparer.Instance);

        foreach(var group in groups.All(pairs.SelectMany(pair => new[] { pair.Earlier, pair.Later })))
        {
            if(group.Any(Pinned) || Root(group) is not { } root)
            {
                pinned.UnionWith(group);
                continue;
            }

            var name = NameFor(root);

            foreach(var member in group)
            {
                renamed[Node(member)!] = name;
            }
        }

        foreach(var (earlier, later) in pairs)
        {
            if(pinned.Contains(earlier) && pinned.Contains(later) && (earlier, later) is (Method first, Method second))
            {
                unnamed.Add((first, second));
            }
        }

        return new OverloadDecision(renamed, unnamed);
    }

    private static void Report(Method earlier, Method later, DiagnosticBag diagnostics)
    {
        if((later.DeclarationNode ?? earlier.DeclarationNode) is not { } node || ValueRouteEmitter.ErasedDifference(earlier, later) is not var (slot, type))
        {
            return;
        }

        var typeParameters = earlier.TypeParametersList.Count == 0 ? "" : $"<{string.Join(", ", earlier.TypeParametersList.Select(parameter => parameter.Name))}>";
        var display = $"{earlier.Name}{typeParameters}({string.Join(", ", earlier.ParametersList.Select(parameter => TypeDisplay.Mist(parameter.Type)))})";

        diagnostics.ReportAtNodeTyped(
            BackendDiagnostics.CoincidingOverloads, node, null, new NameValue(later.Name), new NameValue(display), new NameValue(slot.Name), type);
    }

    internal static string NameFor(object member)
    {
        var (name, arity, parameters, target) = member switch
        {
            CastOperator conversion => (conversion.Name, 0, conversion.ParametersList, conversion.TargetType),
            Constructor constructor => ("__Ctor", constructor.TypeParametersList.Count, constructor.ParametersList, null),
            Method method => (method.Name, method.TypeParametersList.Count, method.ParametersList, null),
            Indexer indexer => ("Item", 0, indexer.ParametersList, null),
            _ => throw new InvalidOperationException($"ICE: '{member}' is not a member that can be renamed."),
        };
        var parts = new List<string>();

        if(arity > 0)
        {
            parts.Add($"_g{arity.ToString(CultureInfo.InvariantCulture)}");
        }

        parts.AddRange(parameters.Select(parameter =>
            (parameter.Modifier.IsRef ? parameter.Modifier.IsReadonly ? "_n" : "_m" : "") + Escaped(parameter.Type.FullyQualifiedName)));

        if(target is not null)
        {
            parts.Add("_t" + Escaped(target.FullyQualifiedName));
        }

        return $"{name}__{string.Join("_p", parts)}";
    }

    private static string Escaped(string name)
    {
        var escaped = new StringBuilder();

        foreach(var character in name)
        {
            _ = character switch
            {
                _ when char.IsAsciiLetterOrDigit(character) => escaped.Append(character),
                '_' => escaped.Append("_u"),
                '.' => escaped.Append("_d"),
                '<' => escaped.Append("_l"),
                '>' => escaped.Append("_r"),
                ',' => escaped.Append("_c"),
                ' ' => escaped.Append("_s"),
                '[' => escaped.Append("_b"),
                ']' => escaped.Append("_e"),
                '(' => escaped.Append("_o"),
                ')' => escaped.Append("_z"),
                '?' => escaped.Append("_q"),
                '|' => escaped.Append("_v"),
                _ => escaped.Append(CultureInfo.InvariantCulture, $"_x{(int)character:X4}"),
            };
        }

        return escaped.ToString();
    }

    private static void Link(ConcreteType concrete, NameGroups groups, TypeInterner? interner)
    {
        if(concrete.DeclarationNode is null || interner is null)
        {
            return;
        }

        var fillers = ConformanceAnalyzer.Fillers(concrete).ToList();
        var reached = ConformanceAnalyzer.Reached(concrete.BaseAbstractTypes, interner, concrete)
            .Select(each => (each.Abstract, each.Substitution, Mappings: (ConformanceDeclaration?)null))
            .Concat(concrete.ConformancesList.SelectMany(conformance => ConformanceAnalyzer.Reached([conformance.AbstractType], interner, concrete)
                .Select(each => (each.Abstract, each.Substitution, Mappings: (ConformanceDeclaration?)conformance))));

        foreach(var (definition, substitution, mappings) in reached)
        {
            foreach(var slot in definition.MethodsList.Where(method => !method.Modifiers.IsStatic))
            {
                if(ConformanceAnswers.Method(slot, substitution, fillers, mappings, interner) is { } answer && answer.Member.Name == slot.Name)
                {
                    groups.Join(slot, answer.Member.SourceDeclaration ?? answer.Member);
                }
            }

            foreach(var slot in definition.IndexersList.Where(indexer => !indexer.Modifiers.IsStatic))
            {
                if(ConformanceAnswers.Indexer(slot, substitution, fillers, interner) is { } answer)
                {
                    groups.Join(slot, answer);
                }
            }
        }
    }

    private static IEnumerable<List<object>> Scopes(MemberType type, Dictionary<object, object> closings)
    {
        if(type.DeclarationNode is null)
        {
            yield break;
        }

        var methods = new List<object>(type.MethodsList);
        var indexers = new List<object>(type.IndexersList);

        if(type is StructType flattened)
        {
            foreach(var (ancestor, path) in DefaultSynthesis.FlattenedAncestors(flattened))
            {
                var closing = TypeSubstitution.ForPath(null, path, interner: null);
                methods.AddRange(ancestor.MethodsList.Select(method => Closed(method, closing, closings)));
            }
        }
        else if(type is ConcreteType concreteType)
        {
            var inherited = Inherited(concreteType, closings).ToList();
            methods.AddRange(inherited.Where(member => member is Method));
            indexers.AddRange(inherited.Where(member => member is Indexer));
        }

        foreach(var (_, definition, substitution) in AbstractInheritance.Reached(type.BaseAbstractTypes, interner: null))
        {
            methods.AddRange((type is AbstractType ? definition.MethodsList : Restated(definition))
                .Select(method => Closed(method, substitution, closings)));

            if(type is AbstractType)
            {
                indexers.AddRange(definition.IndexersList.Select(indexer => Closed(indexer, substitution, closings)));
            }
        }

        yield return [.. methods.Distinct(ReferenceEqualityComparer.Instance)];
        yield return [.. indexers.Distinct(ReferenceEqualityComparer.Instance)];
        yield return [.. type.OperatorsList];

        if(type is ConcreteType concrete)
        {
            yield return [.. concrete.ConstructorsList.Where(constructor => !constructor.Modifiers.IsStatic)];
            yield return [.. concrete.CastOperatorsList.Where(conversion => conversion is { ClonesThrough: null, CopiesClone: false })];
        }
    }

    private static IEnumerable<object> Inherited(ConcreteType type, Dictionary<object, object> closings)
    {
        var seen = new HashSet<ConcreteType>(ReferenceEqualityComparer.Instance) { type };

        for(IMistType? written = type.BaseType;
            written is not null && (written is ConstructedType constructed ? constructed.Definition : written) is ConcreteType ancestor and not StructType && seen.Add(ancestor);)
        {
            var closing = TypeSubstitution.ForReceiver(written as ConstructedType);

            foreach(var method in ancestor.MethodsList)
            {
                yield return Closed(method, closing, closings);
            }

            foreach(var indexer in ancestor.IndexersList)
            {
                yield return Closed(indexer, closing, closings);
            }

            foreach(var (_, definition, substitution) in AbstractInheritance.Reached(ancestor.BaseAbstractTypes.Select(each => closing.Apply(each, interner: null)), interner: null))
            {
                foreach(var method in Restated(definition))
                {
                    yield return Closed(method, substitution, closings);
                }
            }

            written = ancestor.BaseType is { } next ? closing.Apply(next, interner: null) : null;
        }
    }

    private static IEnumerable<Method> Restated(AbstractType definition)
    {
        return definition.MethodsList.Where(method => method.Body is not null && !method.Modifiers.IsStatic);
    }

    private static object Closed(Method method, TypeSubstitution closing, Dictionary<object, object> closings)
    {
        if(ReferenceEquals(closing, TypeSubstitution.Empty))
        {
            return method;
        }

        var closed = new Method(method.Name, closing.Apply(method.ReturnType, interner: null), Closed(method.ParametersList, closing), method.Modifiers)
        {
            DeclaringType = method.DeclaringType,
            DeclaringBlock = method.DeclaringBlock,
            DeclarationNode = method.DeclarationNode,
            SourceDeclaration = method.SourceDeclaration ?? method,
        };
        closed.TypeParametersList.AddRange(method.TypeParametersList);
        closed.AttributesList.AddRange(method.AttributesList);
        closings[closed] = method;
        return closed;
    }

    private static object Closed(Indexer indexer, TypeSubstitution closing, Dictionary<object, object> closings)
    {
        if(ReferenceEquals(closing, TypeSubstitution.Empty))
        {
            return indexer;
        }

        var closed = new Indexer(closing.Apply(indexer.Type, interner: null), Closed(indexer.ParametersList, closing), indexer.Modifiers)
        {
            DeclaringType = indexer.DeclaringType,
            DeclarationNode = indexer.DeclarationNode,
            SlotKind = indexer.SlotKind,
        };
        closings[closed] = indexer;
        return closed;
    }

    private static List<Parameter> Closed(List<Parameter> parameters, TypeSubstitution closing)
    {
        return parameters.ConvertAll(parameter =>
        {
            var closed = new Parameter(parameter.Name, closing.Apply(parameter.Type, interner: null), parameter.Modifier)
            {
                IsFlattened = parameter.IsFlattened,
                IsFailed = parameter.IsFailed,
                HasNativeDefault = parameter.HasNativeDefault,
            };
            closed.AttributesList.AddRange(parameter.AttributesList);
            closed.DeclaredAttributesList.AddRange(parameter.DeclaredAttributesList);
            return closed;
        });
    }

    private static void Mark(
        List<object> scope, Dictionary<object, object> closings, NameGroups groups, List<(object Earlier, object Later)> pairs, List<(Method Earlier, Method Later)> unnamed)
    {
        foreach(var named in scope.Where(member => Node(member) is not null || closings.ContainsKey(member)).GroupBy(Key, StringComparer.Ordinal))
        {
            var members = named.ToList();

            if(members.Count < 2)
            {
                continue;
            }

            var spelled = members.ConvertAll(Spelled);

            for(var later = 1; later < members.Count; later++)
            {
                for(var earlier = 0; earlier < later; earlier++)
                {
                    if(!(spelled[earlier] == spelled[later] ? Erased(members[earlier], members[later]) : Meets(members[earlier], members[later])))
                    {
                        continue;
                    }

                    var (one, other) = (closings.GetValueOrDefault(members[earlier], members[earlier]), closings.GetValueOrDefault(members[later], members[later]));

                    if(ReferenceEquals(one, other) || (spelled[earlier] != spelled[later] && Pinned(one) && Pinned(other)))
                    {
                        continue;
                    }

                    if(!groups.Same(one, other))
                    {
                        pairs.Add((one, other));
                    }
                    else if((one, other) is (Method { DeclaringType: ConcreteType } first, Method { DeclaringType: ConcreteType } second))
                    {
                        unnamed.Add((first, second));
                    }
                }
            }
        }
    }

    private static string Key(object member)
    {
        return member switch
        {
            CastOperator => Conversions,
            Constructor => Constructors,
            Method method => method.Name,
            _ => Indexers,
        };
    }

    private static string Spelled(object member)
    {
        return member switch
        {
            CastOperator conversion => $"{TypeNameEmitter.Emit(conversion.TargetType)}|{ValueRouteEmitter.Spelled(conversion)}",
            Method method => ValueRouteEmitter.Spelled(method),
            Indexer indexer => string.Join(",", indexer.ParametersList.Select(parameter =>
                RefKeywords.For(parameter.Modifier.IsRef, parameter.Modifier.IsReadonly) + ParameterEmitter.SpelledType(parameter, slotDeclaresNullable: false))),
            _ => "",
        };
    }

    private static bool Erased(object earlier, object later)
    {
        return (earlier, later) switch
        {
            (CastOperator first, CastOperator second) => ValueRouteEmitter.Erased(first.TargetType, second.TargetType) is not null
                || ValueRouteEmitter.ErasedDifference(first, second) is not null,
            (Method first, Method second) => ValueRouteEmitter.ErasedDifference(first, second) is not null,
            (Indexer first, Indexer second) => first.ParametersList.Zip(second.ParametersList)
                .Any(pair => ValueRouteEmitter.Erased(pair.First.Type, pair.Second.Type) is not null),
            _ => false,
        };
    }

    private static bool Meets(object earlier, object later)
    {
        if(Plain(earlier) is not { } first || Plain(later) is not { } second || first.Count != second.Count)
        {
            return false;
        }

        var bound = new Dictionary<TypeParameter, IMistType>(ReferenceEqualityComparer.Instance);
        var throughSlot = false;

        for(var i = 0; i < first.Count; i++)
        {
            if(first[i].Modifier.IsRef != second[i].Modifier.IsRef || first[i].Modifier.IsReadonly != second[i].Modifier.IsReadonly
                || !Unify(first[i].Type, second[i].Type, bound, ref throughSlot, depth: 0))
            {
                return false;
            }
        }

        return throughSlot;
    }

    private static List<Parameter>? Plain(object member)
    {
        return member switch
        {
            CastOperator => null,
            Method { DeclaringBlock: null } method when ValueRouteEmitter.Spelled(method) == $"{method.TypeParametersList.Count(parameter => !SlotRoutes.IsValueRouted(parameter))}:{string.Join(",", method.ParametersList.Select(parameter =>
                RefKeywords.For(parameter.Modifier.IsRef, parameter.Modifier.IsReadonly) + ParameterEmitter.SpelledType(parameter, NativeSlotNullability.Declares(parameter))))}" => method.ParametersList,
            Indexer indexer => indexer.ParametersList,
            _ => null,
        };
    }

    private static bool Unify(IMistType one, IMistType other, Dictionary<TypeParameter, IMistType> bound, ref bool throughSlot, int depth)
    {
        if(depth > 16)
        {
            return false;
        }

        if((Variable(one) ?? Variable(other)) is { } variable)
        {
            var (_, type) = ReferenceEquals(variable, Variable(one)) ? (one, other) : (other, one);

            if(bound.TryGetValue(variable, out var earlier))
            {
                return Unify(earlier, type, bound, ref throughSlot, depth + 1);
            }

            if(Variable(type) is { } another)
            {
                return ReferenceEquals(another, variable);
            }

            bound[variable] = type;
            throughSlot |= CarriesSlot(type, depth);
            return true;
        }

        if(TypeNameEmitter.Closed(one) is ConstructedType first && TypeNameEmitter.Closed(other) is ConstructedType second
            && ReferenceEquals(first.Definition, second.Definition) && first.Definition is IHasTypeParameters generic
            && first.TypeArguments.Count == second.TypeArguments.Count)
        {
            foreach(var slot in generic.TypeParametersList.Where(slot => slot.Ordinal < first.TypeArguments.Count))
            {
                var (mine, theirs) = (first.TypeArguments[slot.Ordinal], second.TypeArguments[slot.Ordinal]);

                if(!slot.IsDependent)
                {
                    if(!Unify(mine, theirs, bound, ref throughSlot, depth + 1))
                    {
                        return false;
                    }
                }
                else if(mine.FullyQualifiedName != theirs.FullyQualifiedName)
                {
                    if(!SlotRoutes.IsValueRouted(slot) && !LengthErasure.IsLengthIndexed(first))
                    {
                        return false;
                    }

                    throughSlot = true;
                }
            }

            return true;
        }

        return TypeNameEmitter.Emit(one) == TypeNameEmitter.Emit(other);
    }

    private static TypeParameter? Variable(IMistType type)
    {
        return type is TypeParameter { IsDependent: false, IsPack: false, DeclaringMethod: null } variable and not PackElementType ? variable : null;
    }

    private static bool CarriesSlot(IMistType type, int depth)
    {
        return depth <= 16 && TypeNameEmitter.Closed(type) is ConstructedType constructed && constructed.Definition is IHasTypeParameters generic
            && generic.TypeParametersList.Any(slot => slot.Ordinal < constructed.TypeArguments.Count
                && (slot.IsDependent
                    ? SlotRoutes.IsValueRouted(slot) || LengthErasure.IsLengthIndexed(constructed)
                    : CarriesSlot(constructed.TypeArguments[slot.Ordinal], depth + 1)));
    }

    private static bool Pinned(object member)
    {
        return Node(member) is null
            || NativeSlotFills.Fills(member)
            || member is Method { Modifiers.IsIntrinsic: true } or Indexer { Modifiers.IsIntrinsic: true };
    }

    private static object? Root(List<object> group)
    {
        var declaredOnAbstract = group.Where(member => DeclaringType(member) is AbstractType).OrderBy(NameFor, StringComparer.Ordinal).FirstOrDefault();

        return declaredOnAbstract ?? group.FirstOrDefault();
    }

    private static MemberType? DeclaringType(object member)
    {
        return member switch
        {
            Method method => method.DeclaringType,
            Indexer indexer => indexer.DeclaringType,
            _ => null,
        };
    }

    private static SyntaxNode? Node(object member)
    {
        return member switch
        {
            Method method => method.DeclarationNode,
            Indexer indexer => indexer.DeclarationNode,
            _ => null,
        };
    }

    private sealed class NameGroups
    {
        private readonly Dictionary<object, object> parents = new(ReferenceEqualityComparer.Instance);

        public void Join(object one, object other)
        {
            var (first, second) = (Find(one), Find(other));

            if(!ReferenceEquals(first, second))
            {
                parents[first] = second;
            }
        }

        public bool Same(object one, object other)
        {
            return ReferenceEquals(Find(one), Find(other));
        }

        public IEnumerable<List<object>> All(IEnumerable<object> seeds)
        {
            var members = parents.Keys.Concat(parents.Values).Concat(seeds).Distinct(ReferenceEqualityComparer.Instance).ToList();
            var marked = seeds.Select(Find).ToHashSet(ReferenceEqualityComparer.Instance);

            return members.GroupBy(Find, ReferenceEqualityComparer.Instance)
                .Where(group => marked.Contains(group.Key))
                .Select(group => group.ToList());
        }

        private object Find(object member)
        {
            var root = member;

            while(parents.TryGetValue(root, out var parent) && !ReferenceEquals(parent, root))
            {
                root = parent;
            }

            return root;
        }
    }
}

internal sealed record OverloadDecision(IReadOnlyDictionary<SyntaxNode, string> Renamed, IReadOnlyList<(Method Earlier, Method Later)> Unnamed);