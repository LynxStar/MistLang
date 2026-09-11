using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// The three field writes classic refuses, measured: inside a readonly method or a method of a
/// readonly type, through a readonly parameter, and to a readonly field anywhere but a constructor
/// of its own type -- the last of which classic leaves to Roslyn outside the declaring type. A
/// readonly block, and a call to a mutable member from a readonly context, are admitted as classic
/// admits them; the spec's stricter rules are recorded on order 11 of the register.
/// </summary>
internal sealed class ReadonlyAnalyzer : IProgramAnalyzer
{
    public string Id => "ownership-borrow-checking";

    public void Analyze(AnalysisContext context)
    {
        foreach(var node in context.Nodes)
        {
            if(node is not AssignmentExpression { IsCoalescing: false } assignment || Written(assignment.Target) is not { } field)
            {
                continue;
            }

            var member = context.MemberOf(assignment);
            var owner = context.OwnerOf(assignment);

            if(assignment.Target is MemberAccessExpression { Receiver: LocalReferenceExpression { Local.Parameter.Modifier.IsReadonly: true } parameter })
            {
                context.Report(ProgramAnalysisDiagnostics.FieldAssignedOnReadonlyParameter, assignment.Target.Node, [assignment.Target], field.Name, parameter.Local.Name);
            }
            else if(member is Method and not Constructor && field.Modifiers.IsReadonly)
            {
                context.Report(ProgramAnalysisDiagnostics.ReadonlyFieldAssigned, assignment.Target.Node, [assignment.Target], field.Name, field.DeclaringType?.Name ?? "?");
            }
            else if(member is Method { Modifiers.IsReadonly: true } or Method { DeclaringType: { } } && member is Method readonlyMember and not Constructor
                && (readonlyMember.Modifiers.IsReadonly || (owner is not null && Declarations.IsReadonly(owner))))
            {
                context.Report(ProgramAnalysisDiagnostics.FieldAssignedInReadonlyMember, assignment.Target.Node, [assignment.Target], field.Name, readonlyMember.Name);
            }
        }
    }

    private static Field? Written(Expression target)
    {
        return target switch
        {
            FieldReferenceExpression field => field.Field,
            MemberAccessExpression { Member: FieldResult found } => found.Field,
            _ => null,
        };
    }
}