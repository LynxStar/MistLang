using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// A type that needs a default and has none, and a <c>default</c> of a class or struct with none. A type
/// needs one when it declares a default block or no constructor, its only value then being its default:
/// it is refused at its declaration, naming each slot of storage and the base without one, and a
/// <c>default</c> of it is not refused a second time. Order 6 of the analysis register;
/// <see cref="Defaultability"/> is the decision.
/// <para>
/// One mistake, one complaint: a slot or base whose default would come from another type that needs a
/// default and has none is left to that type's own refusal, which a chain of holders would otherwise
/// repeat at every link. Two exceptions keep that sound. A type that holds no slot or base of its own to
/// refuse -- one without a default only through a CLR base or a type parameter -- refuses nothing, so the
/// holder is refused instead. And a type that reaches the holder again forms a cycle, which nothing below
/// it explains, so each slot or base closing the cycle is refused.
/// </para>
/// </summary>
internal sealed class DefaultabilityAnalyzer : IProgramAnalyzer
{
    public string Id => "declaration-shape-checks";

    public void Analyze(AnalysisContext context)
    {
        var judged = new Dictionary<ConcreteType, List<Failure>>(ReferenceEqualityComparer.Instance);

        foreach(var type in context.Types)
        {
            if(type is ConcreteType { IsIntrinsic: false, CloneSource: null } concrete && Defaultability.StatesAZero(concrete))
            {
                judged[concrete] = Failures(concrete);
            }
        }

        foreach(var (type, failures) in judged)
        {
            foreach(var failure in failures.Where(failure => IsOwn(failure, type, judged)))
            {
                Report(context, type, failure);
            }
        }

        foreach(var node in context.Nodes)
        {
            if(node is DefaultExpression { Type: ConcreteType { IsIntrinsic: false } type } defaulted
                && !Explained(type, judged)
                && !Defaultability.IsDefaultable(type))
            {
                context.Report(ProgramAnalysisDiagnostics.NotDefaultable, defaulted.Node, [defaulted], TypeDisplay.Mist(type));
            }
        }
    }

    private static List<Failure> Failures(ConcreteType type)
    {
        var failures = new List<Failure>();

        foreach(var slot in InstanceStorage.Of(type).Where(slot => !Defaultability.IsUndecided(slot.Type)))
        {
            if(!Defaultability.StorageHasDefault(type, slot))
            {
                failures.Add(new Failure(slot, Defaultability.AnsweredThrough(type, slot) as ConcreteType, ZeroRefused: false));
            }
            else if(StorageDefaults.ZeroRefused(type, slot))
            {
                failures.Add(new Failure(slot, Through: null, ZeroRefused: true));
            }
        }

        if(Defaultability.BaseWithoutDefault(type) is { } baseType)
        {
            failures.Add(new Failure(Slot: null, baseType, ZeroRefused: false));
        }

        return failures;
    }

    private static bool IsOwn(Failure failure, ConcreteType holder, Dictionary<ConcreteType, List<Failure>> judged)
    {
        return failure.Through is not { } through
            || !Explained(through, judged)
            || Reaches(through, holder, judged, new HashSet<ConcreteType>(ReferenceEqualityComparer.Instance));
    }

    private static bool Explained(ConcreteType type, Dictionary<ConcreteType, List<Failure>> judged)
    {
        return judged.TryGetValue(type, out var failures) && failures.Exists(failure => !failure.ZeroRefused);
    }

    private static bool Reaches(
        ConcreteType from, ConcreteType target, Dictionary<ConcreteType, List<Failure>> judged, HashSet<ConcreteType> visited)
    {
        if(ReferenceEquals(from, target))
        {
            return true;
        }

        return visited.Add(from)
            && judged.TryGetValue(from, out var failures)
            && failures.Exists(failure => failure.Through is { } next && Reaches(next, target, judged, visited));
    }

    private static void Report(AnalysisContext context, ConcreteType type, Failure failure)
    {
        var declares = type.DefaultBlock is null ? "no constructor" : "a default block";

        if(failure.Slot is not { } slot)
        {
            if(type.DeclarationNode is { } declaration)
            {
                context.Report(
                    ProgramAnalysisDiagnostics.BaseNotDefaultable, declaration, [],
                    TypeDisplay.Mist(type), declares, TypeDisplay.Mist(failure.Through!));
            }

            return;
        }

        if((slot.DeclarationNode ?? type.DeclarationNode) is not { } at)
        {
            return;
        }

        if(failure.ZeroRefused)
        {
            context.Report(
                ProgramAnalysisDiagnostics.RefinementZeroNotAdmitted, at, [],
                slot.Name, TypeDisplay.Mist(type), declares, TypeDisplay.Mist(slot.Type), TypeDisplay.Mist(Requirement.Unrefined(slot.Type)));
            return;
        }

        context.Report(
            slot.Property is null ? ProgramAnalysisDiagnostics.FieldHasNoDefault : ProgramAnalysisDiagnostics.PropertyHasNoDefault, at, [],
            slot.Name, TypeDisplay.Mist(type), declares, TypeDisplay.Mist(slot.Type));
    }

    private readonly record struct Failure(InstanceStorage? Slot, ConcreteType? Through, bool ZeroRefused);
}