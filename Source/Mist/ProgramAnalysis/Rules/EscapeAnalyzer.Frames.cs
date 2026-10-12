using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Flow;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

internal sealed partial class EscapeAnalyzer
{
    private static void JudgeFrames(AnalysisContext context, SourceRoot root)
    {
        var nodes = BodyWalker.DescendantsAndSelf(root.Root).ToList();

        if(!nodes.Any(node => node is ReturnStatement { IsRef: true } || (node is Expression expression && RefStructs.Is(expression.Type))))
        {
            return;
        }

        var lattice = new FrameLifetimes(FrameLifetimes.ScrutineesIn(nodes));
        var returned = root.Root is Expression body && IsGetterBody(root.Member, body) ? body : null;
        JudgeFrames(context, lattice, FlowGraphBuilder.Build(root.Root), FrameLifetimes.Nothing, returned);
    }

    private static void JudgeFrames(
        AnalysisContext context, FrameLifetimes lattice, FlowGraph graph, ImmutableDictionary<LocalVariable, LocalFrame> entry, Expression? returned)
    {
        var solution = FlowSolution<ImmutableDictionary<LocalVariable, LocalFrame>>.Solve(graph, lattice, entry);
        solution.Replay((state, step) => JudgeFrameStep(context, lattice, graph, state, step));

        if(returned is not null && solution.ReachesExit)
        {
            JudgeReturned(context, returned, solution.AtExit);
        }
    }

    private static void JudgeFrameStep(
        AnalysisContext context, FrameLifetimes lattice, FlowGraph graph, ImmutableDictionary<LocalVariable, LocalFrame> state, FlowStep step)
    {
        if(step.Kind != FlowStepKind.Evaluate)
        {
            return;
        }

        switch(step.Node)
        {
            case ReturnStatement { IsRef: true, Value: { } value }
                when StorageLocations.NotALocation(value) is null && PositionDecision.ReadonlyReference(value) is null
                    && FrameScopes.Referent(value, state) is { } origin:
                context.Report(ProgramAnalysisDiagnostics.RefReturnEscapes, value.Node, [value], Describe(value), origin);
                break;

            case ReturnStatement { IsRef: false, Value: { } value }:
                JudgeReturned(context, value, state);
                break;

            case AssignmentExpression { IsCoalescing: false } assignment
                when RefStructs.Is(assignment.Target.Type) && FrameScopes.Value(assignment.Value, state) is { } origin
                    && FrameScopes.Destination(assignment.Target, state) is { } destination:
                context.Report(
                    ProgramAnalysisDiagnostics.StackSpanStored, assignment.Value.Node, [assignment.Value],
                    TypeDisplay.Mist(assignment.Value.Type), origin, destination.Where, destination.Why);
                break;

            case InvocationExpression call:
                JudgeLent(context, call.Signature, ThisTiers.ReceiverOf(call), call.Arguments, call.Parameters, state);
                break;

            case NewExpression creation:
                JudgeLent(context, creation.Constructor, null, creation.Arguments, creation.Constructor?.ParametersList, state);
                break;

            case LambdaExpression lambda when graph.Nested.TryGetValue(lambda, out var nested):
                JudgeFrames(context, lattice, nested, state, lambda.Result);
                break;

            case LocalFunctionStatement or AsyncBlockExpression when graph.Nested.TryGetValue(step.Node, out var nested):
                JudgeFrames(context, lattice, nested, state, returned: null);
                break;
        }
    }

    private static void JudgeReturned(AnalysisContext context, Expression value, ImmutableDictionary<LocalVariable, LocalFrame> state)
    {
        if(RefStructs.Is(value.Type) && FrameScopes.Value(value, state) is { } origin)
        {
            context.Report(ProgramAnalysisDiagnostics.StackSpanReturned, value.Node, [value], TypeDisplay.Mist(value.Type), origin);
        }
    }

    /// <summary>
    /// A call whose receiver or a ref argument is ref struct storage outliving the frame could store any
    /// other ref struct it is given there, so a frame-bound one may not be given beside it -- C#'s
    /// method-arguments-must-match rule. A readonly member cannot write its receiver.
    /// </summary>
    private static void JudgeLent(
        AnalysisContext context, Method? callee, Expression? receiver, IReadOnlyList<Expression> arguments,
        IReadOnlyList<Parameter>? parameters, ImmutableDictionary<LocalVariable, LocalFrame> state)
    {
        var destinations = new List<(Expression Site, string Where, string Why)>();

        if(receiver is not null && callee is { Modifiers: { IsStatic: false, IsReadonly: false }, DeclaringType: not StructType { Modifiers.IsReadonly: true } }
            && RefStructs.Is(receiver.Type) && FrameScopes.Destination(receiver, state) is { } held)
        {
            destinations.Add((receiver, held.Where, held.Why));
        }

        for(var i = 0; i < arguments.Count && parameters is not null && i < parameters.Count; i++)
        {
            if(parameters[i].Modifier is { IsRef: true, IsReadonly: false } && RefStructs.Is(arguments[i].Type)
                && FrameScopes.Destination(arguments[i], state) is { } written)
            {
                destinations.Add((arguments[i], written.Where, written.Why));
            }
        }

        if(destinations is not [var destination, ..])
        {
            return;
        }

        foreach(var argument in arguments.Where(argument => !destinations.Exists(each => ReferenceEquals(each.Site, argument))))
        {
            if(RefStructs.Is(argument.Type) && FrameScopes.Value(argument, state) is { } origin)
            {
                context.Report(
                    ProgramAnalysisDiagnostics.StackSpanPassed, argument.Node, [argument],
                    TypeDisplay.Mist(argument.Type), origin, callee!.Name, destination.Where, destination.Why);
            }
        }
    }

    private static bool IsGetterBody(object? member, Expression body)
    {
        return member switch
        {
            Property property => ReferenceEquals(property.Getter?.ExpressionBody, body),
            Indexer indexer => ReferenceEquals(indexer.Getter?.ExpressionBody, body),
            _ => false,
        };
    }

    private static string Describe(Expression value)
    {
        return value switch
        {
            LocalReferenceExpression local => $"'{local.Local.Name}'",
            FieldReferenceExpression field => $"field '{field.Field.Name}'",
            MemberAccessExpression { Member: FieldResult found } => $"field '{found.Field.Name}'",
            IndexExpression { Receiver: LocalReferenceExpression receiver } => $"an element of '{receiver.Local.Name}'",
            IndexExpression => "an element",
            InvocationExpression { Signature: { } method } => $"the result of '{method.Name}'",
            ThisExpression => "'this'",
            ImplicitConversionExpression conversion => Describe(conversion.Operand),
            _ => "this reference",
        };
    }
}