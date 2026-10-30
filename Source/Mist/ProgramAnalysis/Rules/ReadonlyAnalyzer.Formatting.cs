using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// What a readonly context formats. A formatter it reaches is judged where it is reached, whether the
/// context formats the value itself or calls an unmarked member that formats it through a closing. A type
/// parameter it formats is a promise about whatever closes that parameter (readonly-contexts Requirement
/// 12.6), so it is judged at every closing the binder recorded, at each call whose receiver closes a generic
/// extension block, recorded only on the call's closed copy, and through a closing by another parameter:
/// <c>Read(box)</c> is refused where it is written, not admitted because <c>Read</c> was proven for an open
/// <c>T</c>.
/// </summary>
internal sealed partial class ReadonlyAnalyzer
{
    private static void JudgeFormatting(Expression at, Frame frame, AnalysisContext context, PurityInference inference, FormatPromises promises)
    {
        foreach(var formatted in FormattingCallbacks.Sites(at))
        {
            JudgeSteps(inference.Formatting.Reach(formatted.Type), formatted, frame, context, inference, promises);
        }

        foreach(var use in FormattingCallbacks.Uses(at, frame.ScopeType()))
        {
            if(inference.Of(use.Callee).Readonly)
            {
                JudgeSteps(inference.Formatting.Through(use), use.At, frame, context, inference, promises);
            }
        }
    }

    private static void JudgeSteps(
        IEnumerable<FormatStep> steps, Expression at, Frame frame, AnalysisContext context, PurityInference inference, FormatPromises promises)
    {
        var reported = false;

        foreach(var step in steps)
        {
            if(step.Open is { } open)
            {
                if(FormattingCallbacks.InScope(open, frame.Member, frame.ScopeType()))
                {
                    _ = promises.Promise(open, frame.Describe());
                }
            }
            else if(!reported && !IsReadonlyFormatter(step, inference))
            {
                context.Report(ProgramAnalysisDiagnostics.ReadonlyContextCallsMutable, at.Node, [at], new NameValue("ToString"), new NameValue(frame.Describe()));
                reported = true;
            }
        }
    }

    private static void JudgeClosings(AnalysisContext context, PurityInference inference, FormatPromises promises)
    {
        var registry = context.Model.ConstructorConstraintClosures;
        var closings = registry.Closings.Concat(registry.OpenClosings).Concat(promises.BlockClosings)
            .ToLookup(closing => closing.Parameter, ReferenceEqualityComparer.Instance);
        var pending = new Queue<TypeParameter>(promises.Parameters);

        while(pending.TryDequeue(out var parameter))
        {
            var by = promises.By(parameter);

            foreach(var closing in closings[parameter])
            {
                var reported = false;

                foreach(var step in inference.Formatting.Reach(closing.Argument))
                {
                    if(step.Open is { } open)
                    {
                        if(promises.Promise(open, by))
                        {
                            pending.Enqueue(open);
                        }
                    }
                    else if(!reported && !IsReadonlyFormatter(step, inference))
                    {
                        context.Report(
                            ProgramAnalysisDiagnostics.ReadonlyContextFormatsMutable, closing.Node, [],
                            DiagnosticSnapshot.Type(closing.Argument), new NameValue(parameter.Name), new NameValue(by), DiagnosticSnapshot.Type(step.Formatted));
                        reported = true;
                    }
                }
            }
        }
    }

    private static bool IsReadonlyFormatter(FormatStep step, PurityInference inference)
    {
        return step.Formatter is { } toString && inference.IsReadonly(toString);
    }

    private sealed class FormatPromises
    {
        private readonly Dictionary<TypeParameter, string> promised = new(ReferenceEqualityComparer.Instance);
        private readonly List<TypeArgumentClosing> blockClosings = [];

        public IEnumerable<TypeParameter> Parameters => [.. promised.Keys];

        public IReadOnlyList<TypeArgumentClosing> BlockClosings => blockClosings;

        public void Observe(object node)
        {
            if(node is InvocationExpression { Method: { ClosedBlock: { } block, BlockClosing: var closing } } call)
            {
                foreach(var parameter in block.TypeParametersList)
                {
                    blockClosings.Add(new TypeArgumentClosing(parameter, closing.Apply(parameter, interner: null), call.Node, closing));
                }
            }
        }

        public bool Promise(TypeParameter parameter, string by)
        {
            return promised.TryAdd(parameter, by);
        }

        public string By(TypeParameter parameter)
        {
            return promised[parameter];
        }
    }
}