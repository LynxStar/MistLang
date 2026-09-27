using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis;

internal readonly record struct Promise(bool Readonly, bool Local, bool Pure)
{
    public static Promise None => default;

    public static Promise All => new(true, true, true);

    public bool Readonly { get; init; } = Readonly || Pure;

    public string Display => this switch
    {
        { Pure: true } => "pure",
        { Local: true, Readonly: true } => "local readonly",
        { Local: true } => "local",
        { Readonly: true } => "readonly",
        _ => "unmarked",
    };

    public static Promise Declared(MemberModifiers modifiers)
    {
        return new(modifiers.IsReadonly || modifiers.IsPure, modifiers.IsLocal, modifiers.IsPure);
    }

    public Promise Meet(Promise other)
    {
        return new(Readonly && other.Readonly, Local && other.Local, Pure && other.Pure);
    }

    public Promise Join(Promise other)
    {
        return new(Readonly || other.Readonly, Local || other.Local, Pure || other.Pure);
    }
}

/// <summary>
/// Requirement 8's greatest fixpoint over every body in scope: each member, lambda and local function
/// starts with every promise, is re-judged each round under each promise it still holds given what its
/// callees currently hold, and loses what the judge refuses, until a round changes nothing. Readonly is
/// never judged here -- it is <see cref="ReadonlyInference"/>'s answer, and pure requires it -- so
/// Requirement 2.1 holds by construction; but that answer admits a call to a local member on an owned
/// receiver (Requirement 11), so it is asked afresh each round with the round's local answers, and the
/// last round's inference is the one every caller reads. A declared promise is trusted whatever the
/// body does, since the analyzer reports the body and a caller refused on top of that would be a second
/// complaint. A bodiless member keeps what it declares; an abstract member that declares nothing keeps
/// what every implementation in scope keeps; a body first met as a callee, such as a clone's, joins the
/// fixpoint then.
/// </summary>
internal sealed class PurityInference(AnalysisContext context)
{
    private readonly Dictionary<object, Promise> inferred = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, List<object>> roots = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, object> memberOfRoot = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, object> closureOf = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, object> enclosingOf = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<(object Scope, ScopeKind Kind), Origins> origins = [];
    private readonly Dictionary<PropertyAccessor, AccessorOwner> accessors = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, IReadOnlyList<object>> implementations = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ConcreteType, List<ConformanceAnalyzer.Filler>> fillers = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, Promise> settled = new(ReferenceEqualityComparer.Instance);
    private ReadonlyInference readonlyInference = new();
    private bool built;
    private bool running;
    private bool dirty;

    public Promise Of(object member)
    {
        return Settled(() => Answer(member));
    }

    public bool IsReadonly(Method method)
    {
        return Settled(() => readonlyInference.IsReadonly(method));
    }

    public bool IsReadonlyGetter(Property property)
    {
        return Settled(() => readonlyInference.IsReadonlyGetter(property));
    }

    public bool IsReadonlyGetter(Indexer indexer)
    {
        return Settled(() => readonlyInference.IsReadonlyGetter(indexer));
    }

    private T Settled<T>(Func<T> query)
    {
        if(!built)
        {
            built = true;
            Seed();
            Settle();
        }

        var answer = query();

        while(dirty && !running)
        {
            Settle();
            answer = query();
        }

        return answer;
    }

    private void Seed()
    {
        foreach(var type in context.Types.OfType<MemberType>())
        {
            foreach(var property in type.PropertiesList)
            {
                Note(property);
            }

            foreach(var indexer in type.IndexersList)
            {
                Note(indexer);
            }
        }

        foreach(var block in context.Extensions)
        {
            foreach(var property in block.PropertiesList)
            {
                Note(property);
            }
        }

        foreach(var root in context.Bodies)
        {
            object? member = root.Member switch
            {
                Method method when ReferenceEquals(method.Body, root.Root) => method,
                Constructor constructor when root.Root is Expression argument && constructor.ChainArgumentsList.Contains(argument) => constructor,
                Property property => AccessorOf(property.Getter, property.Setter, root.Root),
                Indexer indexer => AccessorOf(indexer.Getter, indexer.Setter, root.Root),
                _ => null,
            };

            if(member is not null)
            {
                Register(member, root.Root);
            }

            if(member is Constructor built && ReferenceEquals(built.Body, root.Root))
            {
                RegisterInitializers(built);
            }
        }
    }

    /// <summary>
    /// The instance initializers a constructor runs before its body -- every one of its type's, unless
    /// it chains to another constructor of the same type, which runs them instead -- so they are roots
    /// of its promise as its body is.
    /// </summary>
    public static IEnumerable<Expression> InstanceInitializers(Constructor constructor)
    {
        if(constructor.Modifiers.IsStatic || constructor.ChainKind == ConstructorChainKind.This || constructor.DeclaringType is not ConcreteType type)
        {
            yield break;
        }

        foreach(var field in type.FieldsList)
        {
            if(field is { Modifiers.IsStatic: false, Initializer: { } initializer })
            {
                yield return initializer;
            }
        }

        foreach(var property in type.PropertiesList)
        {
            if(property is { Initializer: { } initializer, HasBackingStorage: true, Modifiers.IsStatic: false })
            {
                yield return initializer;
            }
        }
    }

    private void RegisterInitializers(Constructor constructor)
    {
        foreach(var initializer in InstanceInitializers(constructor))
        {
            Register(constructor, initializer);
        }
    }

    private void Settle()
    {
        running = true;
        settled.Clear();
        var changed = true;

        while(changed || dirty)
        {
            changed = false;
            dirty = false;
            readonlyInference = new ReadonlyInference(callee => Answer(callee).Local);

            foreach(var member in inferred.Keys.ToList())
            {
                var current = inferred[member];

                if(current.Readonly && !ReadonlyBy(member))
                {
                    current = new Promise(false, current.Local, false);
                    changed = true;
                }

                if(current.Pure && Refused(member, ScopeKind.Pure))
                {
                    current = current with { Pure = false };
                    changed = true;
                }

                if(current.Local && Refused(member, current.Readonly ? ScopeKind.LocalReadonly : ScopeKind.Local))
                {
                    current = current with { Local = false };
                    changed = true;
                }

                inferred[member] = current;
            }
        }

        running = false;
    }

    private bool Refused(object member, ScopeKind kind)
    {
        var description = ScopeDescription.Of(kind, closureOf.TryGetValue(member, out var closure) ? closure : member);

        if(closure is not null)
        {
            return PurityJudge.Judge(closure, kind, description, OriginsFor(closure, kind), Answer).Any();
        }

        if(member is Constructor constructor && PurityJudge.JudgeChain(constructor, kind, description, Answer) is not null)
        {
            return true;
        }

        return roots[member].Exists(root => PurityJudge.Judge(root, kind, description, OriginsFor(root, kind), Answer).Any());
    }

    private Origins OriginsFor(object scope, ScopeKind kind)
    {
        if(origins.TryGetValue((scope, kind), out var known))
        {
            return known;
        }

        var made = scope switch
        {
            LambdaExpression lambda => new Origins(lambda, lambda, kind, OriginsFor(enclosingOf[lambda], kind), capturesAreInputs: kind is ScopeKind.Pure),
            LocalFunctionStatement function => new Origins(function, function.Function, kind, OriginsFor(enclosingOf[function], kind), capturesAreInputs: kind is ScopeKind.Pure),
            _ => new Origins(scope, memberOfRoot[scope], kind),
        };

        origins[(scope, kind)] = made;
        return made;
    }

    private Promise Answer(object member)
    {
        if(inferred.TryGetValue(member, out var current))
        {
            return DeclaredOf(member).Join(current);
        }

        if(settled.TryGetValue(member, out var known))
        {
            return known;
        }

        var answer = member switch
        {
            Method { Body: { } body } method => Discover(method, body),
            PropertyAccessor { Body: not null } accessor => Discover(accessor),
            PropertyAccessor { ExpressionBody: not null } accessor => Discover(accessor),
            Method method => Bodiless(method),
            PropertyAccessor accessor => Bodiless(accessor),
            LambdaExpression lambda => Promise.Declared(new MemberModifiers { IsPure = lambda.IsPure, IsReadonly = lambda.IsPure, IsLocal = lambda.IsLocal }),
            _ => Promise.None,
        };

        if(!running && !dirty)
        {
            settled[member] = answer;
        }

        return answer;
    }

    private Promise Discover(Method method, object body)
    {
        Register(method, body);

        if(method is Constructor constructor)
        {
            foreach(var argument in constructor.ChainArgumentsList)
            {
                Register(constructor, argument);
            }

            RegisterInitializers(constructor);
        }

        return Answer(method);
    }

    private Promise Discover(PropertyAccessor accessor)
    {
        if(accessor.Body is { } body)
        {
            Register(accessor, body);
        }

        if(accessor.ExpressionBody is { } expression)
        {
            Register(accessor, expression);
        }

        return Answer(accessor);
    }

    private Promise Bodiless(Method method)
    {
        if(method is CastOperator { CopiesClone: true })
        {
            return Promise.All;
        }

        var declared = Promise.Declared(method.Modifiers);

        if(method.DeclaringType is AbstractType abstractType && !method.Modifiers.IsStatic && declared == Promise.None)
        {
            return ImplementationsOf(method, abstractType).Select(Answer).Aggregate(Promise.All, (left, right) => left.Meet(right));
        }

        return declared;
    }

    private Promise Bodiless(PropertyAccessor accessor)
    {
        if(!accessors.TryGetValue(accessor, out var owner))
        {
            return Promise.None;
        }

        var declared = Promise.Declared(owner.Modifiers);

        if(owner.DeclaringType is AbstractType abstractType && !owner.Modifiers.IsStatic && !accessor.IsAuto)
        {
            return declared == Promise.None
                ? ImplementationsOf(accessor, abstractType).Select(Answer).Aggregate(Promise.All, (left, right) => left.Meet(right))
                : declared;
        }

        if(!accessor.IsAuto)
        {
            return declared;
        }

        if(owner.IsSetter)
        {
            return declared.Join(new Promise(false, true, false));
        }

        return declared.Join(StableStorage(owner) ? new Promise(true, false, true) : new Promise(true, true, false));
    }

    private static bool StableStorage(AccessorOwner owner)
    {
        var (setter, declaring) = owner.Property is { } property
            ? (property.Setter, property.DeclaringType)
            : (owner.Indexer!.Setter, owner.Indexer.DeclaringType);

        return setter is null or { IsInit: true } || (declaring is { } type && Declarations.IsReadonly(type));
    }

    private IReadOnlyList<object> ImplementationsOf(object slot, AbstractType abstractType)
    {
        if(implementations.TryGetValue(slot, out var known))
        {
            return known;
        }

        var interner = context.Model.TypeInterner;
        var found = new List<object>();

        foreach(var concrete in context.Types.OfType<ConcreteType>())
        {
            var reaches = concrete.BaseAbstractTypes.Concat(concrete.ConformancesList.Select(conformance => conformance.AbstractType));

            foreach(var (definition, substitution) in ConformanceAnalyzer.Reached(reaches, interner))
            {
                if(!ReferenceEquals(definition, abstractType))
                {
                    continue;
                }

                object? answering = slot switch
                {
                    Method method => AnsweringMethod(concrete, method, substitution, interner),
                    PropertyAccessor accessor => AnsweringAccessor(concrete, accessor, substitution, interner),
                    _ => null,
                };

                if(answering is not null)
                {
                    found.Add(answering);
                }
            }
        }

        implementations[slot] = found;
        return found;
    }

    private Method? AnsweringMethod(ConcreteType concrete, Method slot, TypeSubstitution substitution, TypeInterner interner)
    {
        var mapped = concrete.ConformancesList.SelectMany(conformance => conformance.Mappings).OfType<MethodMapping>()
            .FirstOrDefault(mapping => mapping.TargetName == slot.Name && SlotSignatures.Answers(slot, mapping.Source, substitution, interner));

        return mapped?.Source
            ?? FillersOf(concrete).Find(filler => filler.Method is { } method && method.Name == slot.Name
                && SlotSignatures.Answers(slot, method, substitution.Extend(filler.Substitution), interner))?.Method;
    }

    private PropertyAccessor? AnsweringAccessor(ConcreteType concrete, PropertyAccessor slot, TypeSubstitution substitution, TypeInterner interner)
    {
        var owner = accessors[slot];

        if(owner.Property is { } property)
        {
            var mapped = concrete.ConformancesList.SelectMany(conformance => conformance.Mappings).OfType<PropertyMapping>()
                .FirstOrDefault(mapping => mapping.TargetName == property.Name
                    && SlotSignatures.Agree(substitution.Apply(property.Type, interner), mapping.Source.Type));

            var answering = mapped?.Source
                ?? FillersOf(concrete).Find(filler => filler.Property is { } candidate && candidate.Name == property.Name
                    && SlotSignatures.Agree(substitution.Extend(filler.Substitution).Apply(property.Type, interner), candidate.Type))?.Property;

            return owner.IsSetter ? answering?.Setter : answering?.Getter;
        }

        var indexer = owner.Indexer!;

        for(var current = concrete; current is not null; current = current.BaseType as ConcreteType)
        {
            var answering = current.IndexersList.Find(candidate => candidate.ParametersList.Count == indexer.ParametersList.Count
                && SlotSignatures.Agree(substitution.Apply(indexer.Type, interner), candidate.Type)
                && candidate.ParametersList.Zip(indexer.ParametersList).All(pair => SlotSignatures.Agree(substitution.Apply(pair.Second.Type, interner), pair.First.Type)));

            if(answering is not null)
            {
                return owner.IsSetter ? answering.Setter : answering.Getter;
            }
        }

        return null;
    }

    private List<ConformanceAnalyzer.Filler> FillersOf(ConcreteType concrete)
    {
        if(!fillers.TryGetValue(concrete, out var known))
        {
            known = ConformanceAnalyzer.Fillers(concrete).ToList();
            fillers[concrete] = known;
        }

        return known;
    }

    private Promise DeclaredOf(object member)
    {
        return member switch
        {
            Method method => Promise.Declared(method.Modifiers),
            PropertyAccessor accessor => accessors.TryGetValue(accessor, out var owner) ? Promise.Declared(owner.Modifiers) : Promise.None,
            LambdaExpression lambda => Promise.Declared(new MemberModifiers { IsPure = lambda.IsPure, IsReadonly = lambda.IsPure, IsLocal = lambda.IsLocal }),
            _ => Promise.None,
        };
    }

    private bool ReadonlyBy(object member)
    {
        return member switch
        {
            Method method => readonlyInference.IsReadonly(method),
            PropertyAccessor accessor => accessors.TryGetValue(accessor, out var owner) && ReadonlyAccessor(accessor, owner),
            LambdaExpression lambda => lambda.IsPure || readonlyInference.IsReadonly(lambda),
            _ => false,
        };
    }

    private bool ReadonlyAccessor(PropertyAccessor accessor, AccessorOwner owner)
    {
        if(owner.IsSetter)
        {
            return owner.Modifiers.IsReadonly || readonlyInference.IsReadonlySetter(accessor);
        }

        return owner.Property is { } property ? readonlyInference.IsReadonlyGetter(property) : readonlyInference.IsReadonlyGetter(owner.Indexer!);
    }

    private void Register(object member, object root)
    {
        if(!roots.TryGetValue(member, out var known))
        {
            known = [];
            roots[member] = known;
        }

        if(known.Contains(root))
        {
            return;
        }

        known.Add(root);
        memberOfRoot[root] = member;
        dirty = true;
        _ = inferred.TryAdd(member, Promise.All);
        Collect(root, root);
    }

    private void Collect(object node, object scope)
    {
        NoteAccessed(node);

        foreach(var child in BodyWalker.ChildrenOf(node))
        {
            switch(child)
            {
                case LambdaExpression lambda:
                    enclosingOf[lambda] = scope;
                    closureOf[lambda] = lambda;
                    _ = inferred.TryAdd(lambda, Promise.All);
                    Collect(lambda, lambda);
                    break;

                case LocalFunctionStatement function:
                    enclosingOf[function] = scope;
                    closureOf[function.Function] = function;
                    _ = inferred.TryAdd(function.Function, Promise.All);
                    Collect(function, function);
                    break;

                default:
                    Collect(child, scope);
                    break;
            }
        }
    }

    private void NoteAccessed(object node)
    {
        switch(node)
        {
            case PropertyReferenceExpression reference:
                Note(reference.Property);
                break;

            case MemberAccessExpression { Member: PropertyResult found }:
                Note(found.Property);
                break;

            case ExtensionPropertyAccessExpression extension:
                Note(extension.Property);
                break;

            case ObjectInitializerAssignment { Member: PropertyResult found }:
                Note(found.Property);
                break;

            case PropertySubpattern { Member: PropertyResult found }:
                Note(found.Property);
                break;

            case IndexExpression { Indexer: { } indexer }:
                Note(indexer);
                break;
        }
    }

    private void Note(Property property)
    {
        if(property.Getter is { } getter)
        {
            accessors[getter] = new AccessorOwner(property.Modifiers, property.DeclaringType, IsSetter: false, property, null);
        }

        if(property.Setter is { } setter)
        {
            accessors[setter] = new AccessorOwner(property.Modifiers, property.DeclaringType, IsSetter: true, property, null);
        }
    }

    private void Note(Indexer indexer)
    {
        if(indexer.Getter is { } getter)
        {
            accessors[getter] = new AccessorOwner(indexer.Modifiers, indexer.DeclaringType, IsSetter: false, null, indexer);
        }

        if(indexer.Setter is { } setter)
        {
            accessors[setter] = new AccessorOwner(indexer.Modifiers, indexer.DeclaringType, IsSetter: true, null, indexer);
        }
    }

    private static PropertyAccessor? AccessorOf(PropertyAccessor? getter, PropertyAccessor? setter, object root)
    {
        if(getter is not null && (ReferenceEquals(getter.Body, root) || ReferenceEquals(getter.ExpressionBody, root)))
        {
            return getter;
        }

        if(setter is not null && (ReferenceEquals(setter.Body, root) || ReferenceEquals(setter.ExpressionBody, root)))
        {
            return setter;
        }

        return null;
    }

    private sealed record AccessorOwner(MemberModifiers Modifiers, MemberType? DeclaringType, bool IsSetter, Property? Property, Indexer? Indexer);
}