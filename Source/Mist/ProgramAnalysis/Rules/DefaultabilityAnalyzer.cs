using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// Generic declarations are refused only for failures common to every closing; argument-dependent
/// defaults are checked where needed, including implicit construction of constructorless types.
/// A holder defers to a held type's own default refusal to avoid repeating it along the chain, except
/// when that type has no slot or base of its own to report (only a CLR base or type parameter), or
/// reaches the holder again in a cycle. <see cref="Defaultability"/> supplies the default decisions.
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
            if(node is DefaultExpression { Type: ConcreteType { IsIntrinsic: false } or ConstructedType or TypeParameter } defaulted
                && !Explained(Defaultability.DeclarationOf(defaulted.Type), judged)
                && !Defaultability.HasDefault(defaulted.Type))
            {
                context.Report(ProgramAnalysisDiagnostics.NotDefaultable, defaulted.Node, [defaulted], DiagnosticSnapshot.Type(defaulted.Type));
            }
            else if(node is NewExpression { Constructor: null, Type: ConstructedType constructed } allocation
                && Defaultability.DeclarationOf(constructed) is { } declaration
                && !Defaultability.NeedsSentinel(declaration)
                && !Explained(declaration, judged)
                && !Defaultability.HasImplicitConstructor(constructed))
            {
                context.Report(ProgramAnalysisDiagnostics.NotDefaultable, allocation.Node, [allocation], DiagnosticSnapshot.Type(constructed));
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
                failures.Add(new Failure(slot, Base: null, Defaultability.AnsweredThrough(type, slot) as ConcreteType, ZeroRefused: false));
            }
            else if(StorageDefaults.ZeroRefused(type, slot))
            {
                failures.Add(new Failure(slot, Base: null, Through: null, ZeroRefused: true));
            }
        }

        if(Defaultability.BaseWithoutDefault(type) is { } baseType)
        {
            failures.Add(new Failure(Slot: null, baseType, Defaultability.DeclarationOf(baseType), ZeroRefused: false));
        }

        return failures;
    }

    private static bool IsOwn(Failure failure, ConcreteType holder, Dictionary<ConcreteType, List<Failure>> judged)
    {
        return failure.Through is not { } through
            || !Explained(through, judged)
            || Reaches(through, holder, judged, new HashSet<ConcreteType>(ReferenceEqualityComparer.Instance));
    }

    private static bool Explained(ConcreteType? type, Dictionary<ConcreteType, List<Failure>> judged)
    {
        return type is not null && judged.TryGetValue(type, out var failures)
            && failures.Exists(failure => !failure.ZeroRefused && (failure.Slot?.DeclarationNode ?? type.DeclarationNode) is not null);
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
                    DiagnosticSnapshot.Type(type), new EnumValue(declares), DiagnosticSnapshot.Type(failure.Base!));
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
                new NameValue(slot.Name), DiagnosticSnapshot.Type(type), new EnumValue(declares), DiagnosticSnapshot.Type(slot.Type), DiagnosticSnapshot.Type(Requirement.Unrefined(slot.Type)));
            return;
        }

        context.Report(
            slot.Property is null ? ProgramAnalysisDiagnostics.FieldHasNoDefault : ProgramAnalysisDiagnostics.PropertyHasNoDefault, at, [],
            new NameValue(slot.Name), DiagnosticSnapshot.Type(type), new EnumValue(declares), DiagnosticSnapshot.Type(slot.Type));
    }

    private readonly record struct Failure(InstanceStorage? Slot, IMistType? Base, ConcreteType? Through, bool ZeroRefused);
}