using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// The positions with one rule each: <c>??=</c> anywhere but statement position, a readonly local
/// reassigned, a reference taken where one is taken -- a ref local, a by-ref argument, a ref return -- to
/// something that is not a storage location or, writably, through a readonly reference, a readonly
/// reference assigned to, a static lambda or local function reaching outside itself, and <c>?</c> applied
/// to a type that already holds void. A static lambda's own names are its parameters and whatever its
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
                case AssignmentExpression { IsCoalescing: true } coalescing when PositionDecision.Coalescing(statementLevel.Contains(coalescing)) is { } positional:
                    context.Report(positional, coalescing.Node, [coalescing.Target]);
                    break;

                case AssignmentExpression { Target: LocalReferenceExpression { Local.IsReadonly: true } target } assignment:
                    context.Report(PositionDecision.Reassignment(target.Local)!.Value, assignment.Node, [target], target.Local.Name);
                    break;

                case AssignmentExpression { Target: not LocalReferenceExpression and var target } assignment
                    when PositionDecision.ReadonlyReference(target) is { } through:
                    context.Report(ProgramAnalysisDiagnostics.ReadonlyReferenceWritten, assignment.Node, [target], through, "assigned to");
                    break;

                case LocalDeclarationStatement { Local: { IsRef: true } local, Initializer: { } aliased }:
                    JudgeReference(context, aliased, $"bound to ref local '{local.Name}'", writable: !local.IsReadonly);
                    break;

                case ReturnStatement { IsRef: true, Value: { } returned }:
                    JudgeReference(context, returned, "returned by ref", writable: true);
                    break;

                case InvocationExpression { Parameters: { } parameters } call:
                    JudgeArguments(context, call.Arguments, parameters);
                    break;

                case NewExpression { Constructor: { } constructor } creation:
                    JudgeArguments(context, creation.Arguments, constructor.ParametersList);
                    break;

                case LambdaExpression { IsStatic: true } lambda:
                    JudgeCaptures(
                        context, ProgramAnalysisDiagnostics.StaticLambdaCapture,
                        Captures.OwnNames(lambda.Parameters, Captures.Inside(lambda)), Captures.Inside(lambda));
                    break;

                case LocalFunctionStatement { Function.Modifiers.IsStatic: true } function:
                    JudgeCaptures(
                        context, ProgramAnalysisDiagnostics.StaticLocalFunctionCapture,
                        Captures.OwnNames(function.Parameters, Captures.Inside(function)), Captures.Inside(function));
                    break;
            }
        }

        foreach(var application in context.Model.OptionalApplications.Applications)
        {
            if(PositionDecision.Optional(application.Inner) is { } optional && context.IsInSource(application.Node))
            {
                context.Report(
                    optional, application.Node, [], TypeDisplay.Mist(application.Inner));
            }
        }
    }

    private static void JudgeArguments(AnalysisContext context, IReadOnlyList<Expression> arguments, IReadOnlyList<Parameter> parameters)
    {
        for(var i = 0; i < arguments.Count && i < parameters.Count; i++)
        {
            if(PositionDecision.ByReferenceArgument(parameters[i].Modifier) is { } position)
            {
                JudgeReference(context, arguments[i], position, writable: PositionDecision.WritableArgument(parameters[i].Modifier) is not null);
            }
        }
    }

    private static void JudgeReference(AnalysisContext context, Expression source, string position, bool writable)
    {
        if(StorageLocations.NotALocation(source) is { } what)
        {
            context.Report(ProgramAnalysisDiagnostics.ReferenceToNonLocation, source.Node, [source], what, position);
        }
        else if(writable && PositionDecision.ReadonlyReference(source) is { } through)
        {
            context.Report(ProgramAnalysisDiagnostics.ReadonlyReferenceWritten, source.Node, [source], through, position);
        }
    }

    private static void JudgeCaptures(
        AnalysisContext context, DiagnosticDescriptor descriptor, HashSet<LocalVariable> own, IReadOnlyList<object> inside)
    {
        foreach(var node in inside)
        {
            switch(node)
            {
                case LocalReferenceExpression reference when !own.Contains(reference.Local):
                    context.Report(descriptor, reference.Node, [reference], "outer variable", reference.Local.Name);
                    break;
                case ThisExpression self:
                    context.Report(descriptor, self.Node, [self], "instance member", "this");
                    break;
                case FieldReferenceExpression { Field.Modifiers.IsStatic: false } field:
                    context.Report(descriptor, field.Node, [field], "instance field", field.Field.Name);
                    break;
                case PropertyReferenceExpression { Property.Modifiers.IsStatic: false } property:
                    context.Report(descriptor, property.Node, [property], "instance property", property.Property.Name);
                    break;
            }
        }
    }
}