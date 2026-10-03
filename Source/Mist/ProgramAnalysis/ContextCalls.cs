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
/// The call graph structured-context Requirement 9 walks: every place a callable is run from, with the
/// context keys ambient there. A callable is a method, a constructor, a local function, an operator, a
/// conversion or a property or indexer accessor, and it is run from a call, a <c>new</c>, a constructor
/// chain, a method named as a value, an operator applied, a conversion taken, a property or element read
/// or written, a pattern's property test, a loop's enumerator calls, a collection element's Add, and the
/// drop the language makes at the end of the scope that owns a value: a local's or a parameter's at its
/// body's exit, a <c>with</c> block's value after its pop, a Drop's droppable fields at its own end. A key
/// is HELD at a site by a <c>with</c> declaration or block whose body the site sits in, or by a <c>with</c>
/// clause of the member the site is written in, which that member's own callers answer for. A site that
/// holds no such key passes the question to whoever runs the callable it is in (9.3), and <see cref="Unmet"/>
/// follows that to the first site where nobody can be asked: the entry point (9.2), a body that is no
/// callable's -- a static initializer, an enum value -- or a member the runtime runs with no call written,
/// a Clone or an answer to a slot Standard or the CLR declares. IDisposable's Dispose is the exception
/// among those slots: the language disposes a value only where this graph records it, so a Dispose is
/// transparent, and a value handed to a CLR API that disposes it is a run the program never shows. A
/// callable nothing calls reaches no such site and is not refused. A lambda's body is judged where the
/// lambda is written, and a method named as a value where it is named, a function value carrying no
/// clause for its eventual caller to answer. A key naming a type parameter stops where it is, since
/// what it becomes is each caller's type arguments' to say. Callers are followed breadth first and
/// each callable once, which is what ends a cycle (9.8). An instance initializer and a default block
/// entry are left out altogether: <see cref="PurityAnalyzer"/> judges each a pure scope with no
/// inputs and refuses every ambient read reachable from one, so a finding here would be a second
/// complaint about that mistake.
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

    public string Location()
    {
        if(Node is null)
        {
            return "<unknown>";
        }

        var (path, location, _) = DiagnosticHelper.ResolveLocation(Node);

        return $"{path}:{location.Start.Line}:{location.Start.Column}";
    }
}