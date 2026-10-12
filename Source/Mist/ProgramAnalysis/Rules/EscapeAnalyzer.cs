using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// What may not leave where it lives: a span over stack memory returned from the frame that
/// allocated it, and a binding a lambda or a local function would carry onto the heap -- a ref struct,
/// an owned value whose transfer a closure cannot honour, or a lease bounded to the scope it was taken
/// in, which for a readonly ref is also Roslyn's refusal of an `in` parameter inside a lambda.
/// </summary>
internal sealed class EscapeAnalyzer : IProgramAnalyzer
{
    public string Id => "ownership-borrow-checking";

    public void Analyze(AnalysisContext context)
    {
        foreach(var root in context.Bodies)
        {
            JudgeStackSpans(context, root.Root);
        }

        foreach(var node in context.Nodes)
        {
            switch(node)
            {
                case LambdaExpression { IsStatic: false } lambda:
                    JudgeCaptures(context, "lambda", Captures.OwnNames(lambda.Parameters, Captures.Inside(lambda)), Captures.Inside(lambda));
                    break;

                case LocalFunctionStatement { Function.Modifiers.IsStatic: false } function:
                    JudgeCaptures(context, "local function", Captures.OwnNames(function.Parameters, Captures.Inside(function)), Captures.Inside(function));
                    break;
            }
        }
    }

    private static void JudgeStackSpans(AnalysisContext context, object root)
    {
        var stackBacked = new HashSet<LocalVariable>(ReferenceEqualityComparer.Instance);
        var nodes = BodyWalker.DescendantsAndSelf(root).ToList();

        foreach(var declaration in nodes.OfType<LocalDeclarationStatement>())
        {
            if(declaration.Initializer is { } initializer && IsStackBacked(initializer, stackBacked))
            {
                _ = stackBacked.Add(declaration.Local);
            }
        }

        foreach(var returned in nodes.OfType<ReturnStatement>())
        {
            if(returned.Value is { } value && IsStackBacked(value, stackBacked))
            {
                context.Report(ProgramAnalysisDiagnostics.StackSpanReturned, value.Node, [value]);
            }
        }
    }

    private static bool IsStackBacked(Expression expression, HashSet<LocalVariable> stackBacked)
    {
        return expression switch
        {
            StackallocExpression => true,
            LocalReferenceExpression local => stackBacked.Contains(local.Local),
            InvocationExpression { Method.Name: "Slice", Receiver: MethodGroupExpression { Receiver: { } receiver } } => IsStackBacked(receiver, stackBacked),
            _ => false,
        };
    }

    private static void JudgeCaptures(
        AnalysisContext context, string closure, HashSet<LocalVariable> own, IReadOnlyList<object> inside)
    {
        foreach(var reference in inside.OfType<LocalReferenceExpression>())
        {
            if(own.Contains(reference.Local))
            {
                continue;
            }

            var (kind, reason) = Why(reference.Local);

            if(kind is not null)
            {
                context.Report(ProgramAnalysisDiagnostics.CapturedBinding, reference.Node, [reference], kind, reference.Local.Name, closure, reason);
            }
        }
    }

    private static (string? Kind, string Reason) Why(LocalVariable local)
    {
        if(RefStructs.Is(local.Type))
        {
            return ("ref struct", "a ref struct cannot live on the heap");
        }

        return local switch
        {
            { Ownership: OwnershipKind.Owned } => ("owned", "ownership transfer cannot be honoured by a closure"),
            { Ownership: OwnershipKind.Borrowed } => ("borrowed", "its lease is bounded to the current scope"),
            { Ownership: OwnershipKind.Frozen } => ("frozen", "its lease is bounded to the current scope"),
            { IsRef: true } => ("readonly ref", "a reference cannot outlive its frame"),
            _ => (null, ""),
        };
    }
}