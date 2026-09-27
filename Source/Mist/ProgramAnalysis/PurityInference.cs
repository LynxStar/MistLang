using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
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
/// Requirement 8's greatest fixpoint over every body in scope, as the purity design's 'PurityInference'
/// lays it out. Readonly is never judged here -- it is <see cref="ReadonlyInference"/>'s answer -- but
/// that answer admits a call to a local member on an owned receiver (Requirement 11), so it is asked
/// afresh each round with the round's local answers, and the last round's inference is the one every
/// caller reads. A declared promise is trusted whatever the body does, since the analyzer reports the
/// body and a caller refused on top of that would be a second complaint. An abstract member that
/// declares nothing keeps what every implementation in scope keeps, a default body meeting the members
/// that replace it -- unless the CLR can implement it too, a projected interface's or Standard's
/// enumerables', whose implementers the meet never sees, so it keeps only what it declares. Reached
/// through a slot, an implementation that reads a context the slot does not declare is neither pure
/// nor local, since that input is one the slot's caller never supplies. A body first met as a callee,
/// such as a clone's, joins the fixpoint then. A constructor's roots are its body and the instance
/// initializers it runs, which one chaining to a sibling constructor leaves to that one; a type's
/// implicit constructor is a member here too, keyed by the type: its body is the type's instance
/// initializers, and an allocation through it is a call to it.
/// </summary>
internal sealed partial class PurityInference(AnalysisContext context)
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
    private readonly HashSet<Method> shadowed = new(ReferenceEqualityComparer.Instance);
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
        return Settled(() => Answer(method).Readonly);
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

    private void Settle()
    {
        running = true;
        settled.Clear();
        var changed = true;

        while(changed || dirty)
        {
            changed = false;
            dirty = false;
            readonlyInference = new ReadonlyInference(Answer);

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

        if(member is Constructor or ConcreteType && PurityJudge.JudgeChain(member, kind, description, Answer) is not null)
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
            LambdaExpression lambda when enclosingOf.TryGetValue(lambda, out var outer) =>
                new Origins(lambda, lambda, kind, OriginsFor(outer, kind), capturesAreInputs: kind is ScopeKind.Pure),
            LocalFunctionStatement function when enclosingOf.TryGetValue(function, out var outer) =>
                new Origins(function, function.Function, kind, OriginsFor(outer, kind), capturesAreInputs: kind is ScopeKind.Pure),
            _ => new Origins(scope, OriginsMember(memberOfRoot.GetValueOrDefault(scope)), kind),
        };

        origins[(scope, kind)] = made;
        return made;
    }

    private Promise Answer(object member)
    {
        if(member is Method extension && (shadowed.Contains(extension) || (extension.ClosedBlock is not null && RunsTheObjectsOwn(extension))))
        {
            return Promise.None;
        }

        if(inferred.TryGetValue(member, out var current))
        {
            var declared = DeclaredOf(member);
            var kept = declared.Join(current);

            return member is Method { DeclaringType: AbstractType abstractType, Modifiers.IsStatic: false } slot
                ? ImplementationsOf(slot, abstractType).Select(filler => Through(slot, filler)).Aggregate(kept, (left, right) => left.Meet(right)).Join(declared)
                : kept;
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
            ConcreteType type => Discover(type),
            TypeInitializer initializer => Discover(initializer),
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

    private Promise Discover(ConcreteType type)
    {
        if(!roots.ContainsKey(type))
        {
            roots[type] = [];
            _ = inferred.TryAdd(type, Promise.All);
            dirty = true;

            foreach(var initializer in ImplicitConstructorInitializers(type))
            {
                Register(type, initializer);
            }
        }

        return Answer(type);
    }

    private Promise Discover(TypeInitializer initializer)
    {
        if(!roots.ContainsKey(initializer))
        {
            roots[initializer] = [];
            _ = inferred.TryAdd(initializer, Promise.All);
            dirty = true;

            foreach(var root in initializer.Initializers())
            {
                Register(initializer, root);
            }
        }

        return Answer(initializer);
    }

    private Promise Bodiless(Method method)
    {
        if(method is CastOperator { CopiesClone: true })
        {
            return Promise.All;
        }

        var declared = Promise.Declared(method.Modifiers);

        if(method.DeclaringType is AbstractType abstractType && !method.Modifiers.IsStatic && declared == Promise.None && !ClrImplements(abstractType, method.Modifiers))
        {
            return ImplementationsOf(method, abstractType).Select(filler => Through(method, filler)).Aggregate(Promise.All, (left, right) => left.Meet(right));
        }

        return declared;
    }

    public Promise ThroughSlot(Method slot, object filler)
    {
        return Settled(() => Through(slot, filler));
    }

    private Promise Through(Method slot, object filler)
    {
        var kept = Answer(filler);

        return filler is Method implementation
            && implementation.ContextBindingsList.Exists(needed => !slot.ContextBindingsList.Exists(binding => binding.ContextType.FullyQualifiedName == needed.ContextType.FullyQualifiedName))
                ? new Promise(kept.Readonly, false, false)
                : kept;
    }

    private Promise Bodiless(PropertyAccessor accessor)
    {
        if(!accessors.TryGetValue(accessor, out var owner))
        {
            return Promise.None;
        }

        var declared = Promise.Declared(accessor.OwnModifiers ?? owner.Modifiers);

        if(owner.DeclaringType is AbstractType abstractType && !owner.Modifiers.IsStatic)
        {
            return declared == Promise.None && !ClrImplements(abstractType, owner.Modifiers)
                ? ImplementationsOf(accessor, abstractType).Select(Answer).Aggregate(Promise.All, (left, right) => left.Meet(right))
                : declared;
        }

        if(!accessor.IsAuto || owner.Modifiers is { IsIntrinsic: true } or { IsClrProjected: true })
        {
            return declared;
        }

        if(owner.IsSetter)
        {
            return declared.Join(new Promise(false, true, false));
        }

        return declared.Join(StableStorage(owner) ? new Promise(true, false, true) : new Promise(true, true, false));
    }

    private static bool ClrImplements(AbstractType abstractType, MemberModifiers slot)
    {
        return slot.IsClrProjected
            || abstractType.FullyQualifiedName == WellKnownTypes.IEnumerable.FullyQualifiedName
            || abstractType.FullyQualifiedName == WellKnownTypes.IEnumerator.FullyQualifiedName;
    }

    private static bool StableStorage(AccessorOwner owner)
    {
        var setter = owner.Property is { } property ? property.Setter : owner.Indexer!.Setter;

        return setter is null or { IsInit: true };
    }

    private Promise DeclaredOf(object member)
    {
        return member switch
        {
            Method method => Promise.Declared(method.Modifiers),
            PropertyAccessor accessor => accessors.TryGetValue(accessor, out var owner) ? Promise.Declared(accessor.OwnModifiers ?? owner.Modifiers) : Promise.None,
            LambdaExpression lambda => Promise.Declared(new MemberModifiers { IsPure = lambda.IsPure, IsReadonly = lambda.IsPure, IsLocal = lambda.IsLocal }),
            ConcreteType type => new Promise(type.Modifiers.IsReadonly || type.Modifiers.IsPure, false, type.Modifiers.IsPure),
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
            ConcreteType type => readonlyInference.IsReadonly(type),
            TypeInitializer initializer => readonlyInference.IsReadonly(initializer),
            _ => false,
        };
    }

    private bool ReadonlyAccessor(PropertyAccessor accessor, AccessorOwner owner)
    {
        if(owner.IsSetter)
        {
            return (accessor.OwnModifiers ?? owner.Modifiers).IsReadonly || readonlyInference.IsReadonlySetter(accessor);
        }

        return owner.Property is { } property ? readonlyInference.IsReadonlyGetter(property) : readonlyInference.IsReadonlyGetter(owner.Indexer!);
    }

    private sealed record AccessorOwner(MemberModifiers Modifiers, MemberType? DeclaringType, bool IsSetter, Property? Property, Indexer? Indexer);
}