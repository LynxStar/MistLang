using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// What may not leave where it lives: a ref struct tied to its frame by <see cref="FrameScopes"/> --
/// returned, stored where it outlives the frame, or lent to a call beside storage that does -- a
/// reference returned to storage the call does not outlive, and a binding a lambda or a local function
/// would carry onto the heap -- a ref struct, an owned value whose transfer a closure cannot honour, or a
/// lease bounded to the scope it was taken in, which for a readonly ref is also Roslyn's refusal of an
/// `in` parameter inside a lambda.
/// </summary>
internal sealed partial class EscapeAnalyzer : IProgramAnalyzer
{
    public string Id => "ownership-borrow-checking";

    public void Analyze(AnalysisContext context)
    {
        foreach(var root in context.Bodies)
        {
            JudgeFrames(context, root);
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