using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.ProgramAnalysis;

/// <summary>
/// The call graph structured-context Requirement 9 walks: every place a callable is run from, with the
/// context keys ambient there. A key is HELD at a site by a <c>with</c> declaration or block whose body
/// the site sits in, or by a <c>with</c> clause of the member the site is written in, which that
/// member's own callers answer for. A site that holds no such key passes the question to whoever runs
/// the callable it is in (9.3), and <see cref="Unmet"/> follows that to the first site where nobody
/// can be asked: the entry point (9.2), a body that is not a method's or a constructor's and so has
/// no call to be asked through, or a member the runtime or the language runs with no call written --
/// one answering a slot Standard or the CLR declares, a Drop, a Clone. A callable nothing calls
/// reaches no such site and is not refused. A lambda's body is judged where the
/// lambda is written, and a method named as a value where it is named, a function value carrying no
/// clause for its eventual caller to answer. A key naming a type parameter stops where it is, since
/// what it becomes is each caller's type arguments' to say. Callers are followed breadth first and
/// each callable once, which is what ends a cycle (9.8). An instance initializer and a default block
/// entry are left out altogether: <see cref="PurityAnalyzer"/> judges each a pure scope with no
/// inputs and refuses every ambient read reachable from one, so a finding here would be a second
/// complaint about that mistake.
/// </summary>
internal sealed class ContextCalls
{
    private readonly Dictionary<Method, List<ContextSite>> callers = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Method, List<Method>> answered = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Method> unseen = new(ReferenceEqualityComparer.Instance);
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

            var within = root.Member is Method and not (Operator or CastOperator) ? (Method)root.Member : null;
            Walk(root.Root, new Frame(within, HeldBy(within), root.Root));
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
                var answering = Declared(answer.Member);

                if(!answered.TryGetValue(answering, out var slots))
                {
                    slots = [];
                    answered[answering] = slots;
                }

                slots.Add(slot);

                if(abstractType.Assembly.Kind is MistAssemblyKind.Standard or MistAssemblyKind.Ambient)
                {
                    _ = unseen.Add(answering);
                }
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
            ForEachStatement loop => loop.GetEnumerator is { ContextBindingsList.Count: > 0 } || loop.MoveNext is { ContextBindingsList.Count: > 0 },
            ObjectInitializerAssignment { Add.ContextBindingsList.Count: > 0 } => true,
            ConcreteType type => Chains(type).Any(chain => chain.Keys.Count > 0),
            _ => false,
        };
    }

    public static IEnumerable<(AbstractType Abstract, Method Slot, ConformanceAnswers.MethodAnswer Answer, SyntaxNode? At)> Answers(
        ConcreteType concrete, TypeInterner interner)
    {
        var fillers = ConformanceAnalyzer.Fillers(concrete).ToList();
        var reaches = concrete.BaseAbstractTypes.Select(named => (Named: (IMistType)named, Mappings: (ConformanceDeclaration?)null))
            .Concat(concrete.ConformancesList.Select(conformance => (Named: conformance.AbstractType, Mappings: (ConformanceDeclaration?)conformance)));

        foreach(var (named, mappings) in reaches)
        {
            foreach(var (abstractType, substitution) in ConformanceAnalyzer.Reached([named], interner))
            {
                foreach(var slot in abstractType.MethodsList.Where(method => !method.Modifiers.IsStatic))
                {
                    if(ConformanceAnswers.Method(slot, substitution, fillers, mappings, interner) is { } answer)
                    {
                        yield return (abstractType, slot, answer, mappings?.DeclarationNode ?? concrete.DeclarationNode);
                    }
                }
            }
        }
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

        var visited = new HashSet<Method>(ReferenceEqualityComparer.Instance) { within };
        var pending = new Queue<Method>();
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

    private IEnumerable<ContextSite> CallersOf(Method callable)
    {
        var direct = callers.GetValueOrDefault(callable) ?? [];

        return answered.TryGetValue(callable, out var slots)
            ? direct.Concat(slots.SelectMany(slot => callers.GetValueOrDefault(slot) ?? []))
            : direct;
    }

    private bool Lifts(Method method)
    {
        return local.Contains(method)
            || !(unseen.Contains(method)
                || method is Constructor { Modifiers.IsStatic: true }
                || method is { Modifiers.IsStatic: true, Name: "Main", ParametersList.Count: <= 1 }
                || method is { Modifiers.IsStatic: false, Name: "Drop" or "Clone", ParametersList.Count: 0 });
    }

    private void Walk(object root, Frame entered)
    {
        var pending = new Stack<(object Node, Frame Frame)>();
        pending.Push((root, entered));

        while(pending.TryPop(out var visit))
        {
            var (node, frame) = visit;

            switch(node)
            {
                case WithBlockStatement scope:
                    pending.Push((scope.Body, frame with { Held = new ContextHeld(scope.KeyType, frame.Held) }));
                    pending.Push((scope.Value, frame));
                    continue;

                case LocalFunctionStatement function:
                    _ = local.Add(function.Function);
                    pending.Push((function.Body, frame with { Within = function.Function }));
                    continue;

                case InvocationExpression { Method: { } callee } call:
                    Record(call.Node, call, callee, call.ContextTypes, frame);
                    break;

                case NewExpression { Constructor: { } constructor } allocation:
                    Record(allocation.Node, allocation, constructor, allocation.ContextTypes, frame);
                    break;

                case FunctionReferenceExpression reference:
                    Record(reference.Node, reference, reference.Method, reference.ContextTypes, frame);
                    break;

                case ForEachStatement loop:
                    foreach(var called in new[] { loop.GetEnumerator, loop.MoveNext })
                    {
                        if(called is not null)
                        {
                            Record(loop.Collection.Node, loop.Collection, called, ContextBinding.TypesOf(called), frame);
                        }
                    }

                    break;

                case ObjectInitializerAssignment { Add: { } add } element:
                    Record(element.Node, element.Value, add, ContextBinding.TypesOf(add), frame);
                    break;
            }

            foreach(var child in BodyWalker.ChildrenOf(node).Reverse())
            {
                pending.Push((child, frame));
            }
        }
    }

    private static IEnumerable<(Constructor From, Constructor Chained, IReadOnlyList<IMistType> Keys)> Chains(ConcreteType type)
    {
        var throughBase = TypeSubstitution.ForReceiver(type.BaseType as ConstructedType);

        foreach(var constructor in type.ConstructorsList)
        {
            if(!constructor.Modifiers.IsStatic && PurityJudge.ChainedOf(constructor) is Constructor chained)
            {
                var closing = constructor.ChainKind == ConstructorChainKind.This ? TypeSubstitution.Empty : throughBase;

                yield return (constructor, chained, ContextBinding.TypesOf(chained, key => closing.Apply(key, interner: null)));
            }
        }
    }

    private void Record(SyntaxNode? node, Expression? about, Method callee, IReadOnlyList<IMistType> keys, Frame frame)
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

    private static Method Declared(Method method)
    {
        return method.SourceDeclaration ?? method;
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

    private sealed record Frame(Method? Within, ContextHeld? Held, object Home);
}

internal sealed record ContextHeld(IMistType Key, ContextHeld? Outer);

internal sealed record ContextSite(
    SyntaxNode? Node, Expression? About, Method Callee, IReadOnlyList<IMistType> Keys, Method? Within, ContextHeld? Held, object Home)
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