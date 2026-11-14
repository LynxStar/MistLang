using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.ProgramAnalysis;

/// <summary>
/// Structured-context Requirement 9 follows ambient requirements through callers to an entry
/// point, non-callable body, Clone or runtime-invoked Standard/CLR slot. IDisposable.Dispose
/// is transparent because language disposal sites are recorded; CLR APIs that dispose values
/// remain unseen callers. Uncalled members have no boundary to refuse.
/// Lambdas are judged where written and method values where named: function values carry no
/// context clause. Open keys stop at their declaration because callers determine their types.
/// Instance initializers and default entries are excluded: <see cref="PurityAnalyzer"/>
/// refuses their reachable ambient reads, so this graph would duplicate those findings.
/// </summary>
internal sealed partial class ContextCalls
{
    private readonly Dictionary<object, List<ContextSite>> callers = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, List<object>> answered = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<object> unseen = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Method> local = new(ReferenceEqualityComparer.Instance);
    private readonly List<ContextSite> sites = [];

    public ContextCalls(AnalysisScope program, TypeInterner interner)
    {
        foreach(var root in program.Bodies)
        {
            if(IsDefault(root))
            {
                continue;
            }

            var within = CallableOf(root);
            Walk(root, new Frame(within, HeldBy(within as Method), root.Root));
        }

        foreach(var type in program.Types.OfType<ConcreteType>())
        {
            foreach(var (constructor, chained, keys) in Chains(type))
            {
                Record(
                    (SyntaxNode?)constructor.ChainNode ?? constructor.DeclarationNode, about: null, chained, keys,
                    new Frame(constructor, HeldBy(constructor), type));
            }

            foreach(var (abstractType, slot, answer, _) in Answers(type, interner))
            {
                Answer(abstractType, slot, answer.Member);
            }

            foreach(var (abstractType, slot, answering) in AccessorAnswers(type, interner))
            {
                Answer(abstractType, slot, answering);
            }
        }
    }

    public IReadOnlyList<ContextSite> Sites => sites;

    public static bool Owes(object node)
    {
        return node switch
        {
            InvocationExpression call => call.ContextTypes.Count > 0,
            NewExpression allocation => allocation.ContextTypes.Count > 0,
            FunctionReferenceExpression reference => reference.ContextTypes.Count > 0,
            ForEachStatement loop => loop.GetEnumerator is { ContextBindingsList.Count: > 0 }
                || loop.MoveNext is { ContextBindingsList.Count: > 0 }
                || ClrCallbacks.DisposeOf(loop) is { ContextBindingsList.Count: > 0 },
            ObjectInitializerAssignment { Add.ContextBindingsList.Count: > 0 } => true,
            ConcreteType type => Chains(type).Any(chain => chain.Keys.Count > 0),
            _ => false,
        };
    }

    public static bool OwesADrop(AnalysisScope program)
    {
        return program.Types.OfType<ConcreteType>().SelectMany(type => type.MethodsList)
            .Any(method => method is { Name: "Drop" or "Dispose", ParametersList.Count: 0, Modifiers.IsStatic: false, ContextBindingsList.Count: > 0 });
    }

    public ContextSite? Unmet(ContextSite site, IMistType key)
    {
        if(site.Holds(key))
        {
            return null;
        }

        if(site.Within is not { } within || ContextKeys.IsOpen(key) || !Lifts(within))
        {
            return site;
        }

        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance) { within };
        var pending = new Queue<object>();
        pending.Enqueue(within);

        while(pending.TryDequeue(out var callable))
        {
            foreach(var caller in CallersOf(callable))
            {
                if(caller.Holds(key))
                {
                    continue;
                }

                if(caller.Within is not { } outer || !Lifts(outer))
                {
                    return caller;
                }

                if(visited.Add(outer))
                {
                    pending.Enqueue(outer);
                }
            }
        }

        return null;
    }

    private static bool IsDefault(SourceRoot root)
    {
        return root.Member switch
        {
            Field field => !field.Modifiers.IsStatic,
            Property property => !property.Modifiers.IsStatic && ReferenceEquals(property.Initializer, root.Root),
            null => root.Owner is ConcreteType,
            _ => false,
        };
    }

    private static object? CallableOf(SourceRoot root)
    {
        return root.Member switch
        {
            Method method => method,
            Property property => AccessorOf(root.Root, property.Getter, property.Setter),
            Indexer indexer => AccessorOf(root.Root, indexer.Getter, indexer.Setter),
            _ => null,
        };
    }

    private static PropertyAccessor? AccessorOf(object root, PropertyAccessor? getter, PropertyAccessor? setter)
    {
        return Holds(getter, root) ? getter : Holds(setter, root) ? setter : null;
    }

    private static bool Holds(PropertyAccessor? accessor, object root)
    {
        return accessor is not null && (ReferenceEquals(accessor.Body, root) || ReferenceEquals(accessor.ExpressionBody, root));
    }

    private void Answer(AbstractType abstractType, object slot, object answering)
    {
        var declared = Declared(answering);

        if(!answered.TryGetValue(declared, out var slots))
        {
            slots = [];
            answered[declared] = slots;
        }

        slots.Add(slot);

        if(abstractType.Assembly.Kind is MistAssemblyKind.Standard or MistAssemblyKind.Ambient && abstractType.FullyQualifiedName != "System.IDisposable")
        {
            _ = unseen.Add(declared);
        }
    }

    private IEnumerable<ContextSite> CallersOf(object callable)
    {
        var direct = callers.GetValueOrDefault(callable) ?? [];

        return answered.TryGetValue(callable, out var slots)
            ? direct.Concat(slots.SelectMany(slot => callers.GetValueOrDefault(slot) ?? []))
            : direct;
    }

    private bool Lifts(object callable)
    {
        return callable is not Method method
            || local.Contains(method)
            || !(unseen.Contains(method)
                || method is Constructor { Modifiers.IsStatic: true }
                || method is { Modifiers.IsStatic: true, Name: "Main", ParametersList.Count: <= 1 }
                || method is { Modifiers.IsStatic: false, Name: "Clone", ParametersList.Count: 0 });
    }

    private void Record(SyntaxNode? node, Expression? about, object callee, IReadOnlyList<IMistType> keys, Frame frame)
    {
        var site = new ContextSite(node, about, Declared(callee), keys, frame.Within, frame.Held, frame.Home);

        if(!callers.TryGetValue(site.Callee, out var known))
        {
            known = [];
            callers[site.Callee] = known;
        }

        known.Add(site);
        sites.Add(site);
    }

    private static object Declared(object callable)
    {
        return callable is Method { SourceDeclaration: { } declared } ? declared : callable;
    }

    private static ContextHeld? HeldBy(Method? member)
    {
        ContextHeld? held = null;

        foreach(var binding in member?.ContextBindingsList ?? [])
        {
            held = new ContextHeld(binding.ContextType, held);
        }

        return held;
    }

    private sealed record Frame(object? Within, ContextHeld? Held, object Home);
}

internal sealed record ContextHeld(IMistType Key, ContextHeld? Outer);

internal sealed record ContextSite(
    SyntaxNode? Node, Expression? About, object Callee, IReadOnlyList<IMistType> Keys, object? Within, ContextHeld? Held, object Home)
{
    public bool Holds(IMistType key)
    {
        for(var held = Held; held is not null; held = held.Outer)
        {
            if(ContextKeys.Same(held.Key, key))
            {
                return true;
            }
        }

        return false;
    }

    public Location Location()
    {
        return Node is null ? new Absent(AbsenceReason.Unavailable) : DiagnosticHelper.ResolveLocation(Node).Location;
    }
}