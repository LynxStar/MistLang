using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// The structured-context discipline, the spec's Context_Checker: a call owed a context of some type has
/// one wherever it can be run from (Requirements 3.6, 3.7, 9 and 14.1, over <see cref="ContextCalls"/>),
/// a member filling an abstract's slot declares the slot's <c>with</c> clauses exactly, since a call
/// through the abstract answers for those and no others (8), one member binds a key once (14.3), and a
/// name standing for an ambient value is never given another (4.1, 4.2, 7.5), by assignment or by a
/// reference it could be assigned through. What a key IS has one home, <see cref="ContextKeys"/>. A
/// type nobody can name keys nothing a clause could ask for, which is the one case 14.5 leaves this
/// stage: every other untyped expression is the binder's to refuse.
/// </summary>
internal sealed class ContextAnalyzer : IProgramAnalyzer
{
    public string Id => "structured-context-checking";

    public void Analyze(AnalysisContext context)
    {
        foreach(var node in context.Nodes)
        {
            JudgeNode(context, node);
        }

        if(!DeclaresAClause(context.Program))
        {
            return;
        }

        JudgeDeclarations(context);

        if(context.Nodes.Any(ContextCalls.Owes) || context.Types.Any(ContextCalls.Owes) || ContextCalls.OwesADrop(context.Program))
        {
            JudgeAvailability(context);
        }
    }

    private static bool DeclaresAClause(AnalysisScope program)
    {
        return program.Types.OfType<MemberType>().SelectMany(type => type.EnumerateDelegates())
            .Concat(program.Extensions.SelectMany(block => block.MethodsList))
            .Any(method => method.ContextBindingsList.Count > 0);
    }

    private static void JudgeDeclarations(AnalysisContext context)
    {
        foreach(var type in context.Types.OfType<MemberType>())
        {
            foreach(var method in type.EnumerateDelegates())
            {
                JudgeRepeats(context, method);
            }

            if(type is ConcreteType concrete)
            {
                JudgeContract(context, concrete);
            }
        }

        foreach(var method in context.Extensions.SelectMany(block => block.MethodsList))
        {
            JudgeRepeats(context, method);
        }
    }

    private static void JudgeRepeats(AnalysisContext context, Method method)
    {
        foreach(var (earlier, later) in ContextDecision.Repeats(method.ContextBindingsList))
        {
            if((later.Node ?? method.DeclarationNode) is { } at)
            {
                context.ReportRelated(
                    ProgramAnalysisDiagnostics.DuplicateContextBinding, at, [],
                    [new RelatedLocation(RelatedRole.PreviousDeclaration, DiagnosticHelper.ResolveLocation(earlier.Node ?? at).Location)],
                    new EnumValue(Kind(method)), new NameValue(method.Name), DiagnosticSnapshot.Type(later.ContextType), new NameValue(earlier.Identifier));
            }
        }
    }

    private static void JudgeContract(AnalysisContext context, ConcreteType concrete)
    {
        var interner = context.Model.TypeInterner;
        var judged = new HashSet<(Method Slot, Method Member)>();

        foreach(var (abstractType, slot, answer, at) in ContextCalls.Answers(concrete, interner))
        {
            var differences = ContextDecision.Contract(
                ContextBinding.TypesOf(slot, key => answer.Target.Apply(key, interner)),
                ContextBinding.TypesOf(answer.Member, key => answer.Source.Apply(key, interner)));

            if(differences is not null && judged.Add((slot, answer.Member)) && (answer.Member.DeclarationNode ?? at) is { } node)
            {
                context.Report(
                    ProgramAnalysisDiagnostics.ContextContractNotMet, node, [],
                    new NameValue(answer.Member.Name), DiagnosticSnapshot.Type(concrete), DiagnosticSnapshot.Type(abstractType), new NameValue(differences));
            }
        }
    }

    private static void JudgeNode(AnalysisContext context, object node)
    {
        switch(node)
        {
            case AssignmentExpression { Target: LocalReferenceExpression { Local.IsContextBound: true } target } assignment:
                context.Report(ProgramAnalysisDiagnostics.ContextVariableNotReassignable, assignment.Node, [target], new NameValue(target.Local.Name), new NameValue(""));
                break;

            case LocalDeclarationStatement { Local: { IsRef: true, IsReadonly: false } alias, Initializer: LocalReferenceExpression { Local.IsContextBound: true } aliased }:
                context.Report(
                    ProgramAnalysisDiagnostics.ContextVariableNotReassignable, aliased.Node, [aliased],
                    new NameValue(aliased.Local.Name), new NameValue($", so it cannot be bound to ref local '{alias.Name}'"));
                break;

            case InvocationExpression { Method: { } callee } call:
                JudgeArguments(context, call.Arguments, callee.ParametersList);
                break;

            case NewExpression { Constructor: { } constructor } creation:
                JudgeArguments(context, creation.Arguments, constructor.ParametersList);
                break;

            case WithBlockStatement scope when !ContextKeys.IsNameable(scope.KeyType):
                context.Report(
                    ProgramAnalysisDiagnostics.ContextTypeNotNameable, scope.Value.Node, [scope.Value], DiagnosticSnapshot.Type(scope.KeyType));
                break;
        }
    }

    private static void JudgeArguments(AnalysisContext context, IReadOnlyList<Expression> arguments, IReadOnlyList<Parameter> parameters)
    {
        for(var i = 0; i < arguments.Count && i < parameters.Count; i++)
        {
            if(arguments[i] is LocalReferenceExpression { Local.IsContextBound: true } passed
                && PositionDecision.WritableArgument(parameters[i].Modifier) is { } position)
            {
                context.Report(
                    ProgramAnalysisDiagnostics.ContextVariableNotReassignable, passed.Node, [passed],
                    new NameValue(passed.Local.Name), new NameValue($", so it cannot be {position}"));
            }
        }
    }

    private static void JudgeAvailability(AnalysisContext context)
    {
        var calls = new ContextCalls(context.Program, context.Model.TypeInterner);
        var judged = new HashSet<object>(ReferenceEqualityComparer.Instance);
        judged.UnionWith(context.Bodies.Select(body => body.Root));
        judged.UnionWith(context.Types);

        foreach(var site in calls.Sites)
        {
            if(site.Node is null || site.Callee is not Method callee || !judged.Contains(site.Home))
            {
                continue;
            }

            for(var i = 0; i < site.Keys.Count; i++)
            {
                var key = site.Keys[i];

                if(key is ErrorType || site.Keys.Take(i).Any(earlier => ContextKeys.Same(earlier, key)) || calls.Unmet(site, key) is not { } exposed)
                {
                    continue;
                }

                context.ReportRelated(
                    ProgramAnalysisDiagnostics.MissingContext, site.Node, site.About is null ? [] : [site.About],
                    [new RelatedLocation(RelatedRole.Instantiation, (exposed.Node is null ? site : exposed).Location())],
                    new EnumValue(Kind(callee)), new NameValue(callee.Name), DiagnosticSnapshot.Type(key));
            }
        }
    }

    private static string Kind(Method callee)
    {
        return callee is Constructor ? "Constructor" : "Method";
    }
}