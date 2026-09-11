using Mist.Common.Diagnostics;
using Mist.ProgramModel;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// The positions with one rule each: <c>??=</c> anywhere but statement position, a readonly local
/// reassigned, a static lambda reaching outside itself, and <c>?</c> applied to a type that already
/// holds void. Order 9 of the analysis register, the half whose subject is where something was
/// written rather than what it is. A static lambda's own names are its parameters and whatever its
/// body declares -- a local, a loop or deconstruction variable, a pattern binding, a nested lambda's
/// parameters -- and any other local, or any instance member, is a capture.
/// </summary>
internal sealed class PositionAnalyzer : IProgramAnalyzer
{
    public string Id => "operator-and-positional-checks";

    public void Analyze(AnalysisContext context)
    {
        var statementLevel = new HashSet<object>(ReferenceEqualityComparer.Instance);

        foreach(var node in context.Nodes)
        {
            if(node is ExpressionStatement { Expression: AssignmentExpression { IsCoalescing: true } assignment })
            {
                _ = statementLevel.Add(assignment);
            }
        }

        foreach(var node in context.Nodes)
        {
            switch(node)
            {
                case AssignmentExpression { IsCoalescing: true } coalescing when !statementLevel.Contains(coalescing):
                    context.Report(ProgramAnalysisDiagnostics.CoalescingAssignmentNotAStatement, coalescing.Node, [coalescing.Target]);
                    break;

                case AssignmentExpression { Target: LocalReferenceExpression { Local.IsReadonly: true } target } assignment:
                    context.Report(ProgramAnalysisDiagnostics.ReadonlyLocalReassigned, assignment.Node, [target], target.Local.Name);
                    break;

                case LambdaExpression { IsStatic: true } lambda:
                    JudgeCaptures(context, lambda);
                    break;
            }
        }

        foreach(var application in context.Model.OptionalApplications.Applications)
        {
            if(OptionalUnion.ContainsVoid(application.Inner) && context.IsInSource(application.Node))
            {
                context.Report(
                    ProgramAnalysisDiagnostics.OptionalOfOptional, application.Node, [], TypeDisplay.Mist(application.Inner));
            }
        }
    }

    private static void JudgeCaptures(AnalysisContext context, LambdaExpression lambda)
    {
        var body = (object?)lambda.Body ?? lambda.Result;

        if(body is null)
        {
            return;
        }

        var inside = BodyWalker.DescendantsAndSelf(body).ToList();
        var own = new HashSet<LocalVariable>(lambda.Parameters, ReferenceEqualityComparer.Instance);

        foreach(var node in inside)
        {
            switch(node)
            {
                case LocalDeclarationStatement declaration:
                    _ = own.Add(declaration.Local);
                    break;
                case ForEachStatement loop:
                    _ = own.Add(loop.Variable);
                    break;
                case TupleDeconstructionStatement deconstruction:
                    own.UnionWith(deconstruction.Targets);
                    break;
                case LambdaExpression nested:
                    own.UnionWith(nested.Parameters);
                    break;
                case Pattern { Binding: { } binding }:
                    _ = own.Add(binding);
                    break;
            }
        }

        foreach(var node in inside)
        {
            switch(node)
            {
                case LocalReferenceExpression reference when !own.Contains(reference.Local):
                    context.Report(ProgramAnalysisDiagnostics.StaticLambdaCapture, reference.Node, [reference], "outer variable", reference.Local.Name);
                    break;
                case ThisExpression self:
                    context.Report(ProgramAnalysisDiagnostics.StaticLambdaCapture, self.Node, [self], "instance member", "this");
                    break;
                case FieldReferenceExpression { Field.Modifiers.IsStatic: false } field:
                    context.Report(ProgramAnalysisDiagnostics.StaticLambdaCapture, field.Node, [field], "instance field", field.Field.Name);
                    break;
                case PropertyReferenceExpression { Property.Modifiers.IsStatic: false } property:
                    context.Report(ProgramAnalysisDiagnostics.StaticLambdaCapture, property.Node, [property], "instance property", property.Property.Name);
                    break;
            }
        }
    }
}