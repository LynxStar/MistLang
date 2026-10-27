using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Binding;

/// <summary>
/// Turns a failed <see cref="OverloadChoice{T}"/> into the diagnostic it stands for.
/// </summary>
internal static class OverloadFailure
{
    public static ErrorExpression Retain<T>(
        OverloadChoice<T> choice,
        IReadOnlyList<T> candidates,
        IReadOnlyList<Expression> arguments,
        string description,
        Storm.Runtime.SyntaxNode node,
        DiagnosticBag diagnostics,
        IReadOnlyList<Expression>? children = null,
        ImmutableArray<Frame> frames = default)
        where T : class, IOverload
    {
        var retained = children ?? arguments;
        if(retained.Any(PriorFailure.Of))
        {
            return new ErrorExpression(node, retained);
        }

        if(retained.Any(FailureDependency.NeedsAnalysis))
        {
            var pending = diagnostics.Withhold(() => Report(choice, candidates, arguments, description, node, diagnostics, frames));
            var diagnostic = pending.Occurrences.Single();
            pending.Discard();
            return new ErrorExpression(node, retained, diagnostic);
        }

        Report(choice, candidates, arguments, description, node, diagnostics, frames);
        return new ErrorExpression(node, retained);
    }

    /// <summary>
    /// Reports whichever failure a null <see cref="OverloadChoice{T}.Selected"/> stands for. Called only
    /// where selection failed; with a winner in hand there is nothing here to say.
    /// <para>
    /// ONE PLACE FOR ALL OF THEM, deliberately, rather than a test per call site: every failure member
    /// but the one that fired is empty, so a site examining only some of them reports a tie between no
    /// candidates at all.
    /// </para>
    /// </summary>
    /// <param name="description">
    /// Names the callee, and appears mid-sentence in every message, so it is lower case and carries
    /// its own kind: <c>method 'Format'</c>.
    /// </param>
    public static void Report<T>(
        OverloadChoice<T> choice,
        IReadOnlyList<T> candidates,
        IReadOnlyList<Expression> arguments,
        string description,
        Storm.Runtime.SyntaxNode node,
        DiagnosticBag diagnostics,
        ImmutableArray<Frame> frames = default)
        where T : class, IOverload
    {
        // AN ARGUMENT THAT ALREADY FAILED MAKES THIS FAILURE DERIVED, so it is not reported: no
        // overload could have matched, and saying so adds a second complaint about one mistake.
        if(arguments.Any(PriorFailure.Of)
            || (candidates.Count > 0 && candidates.All(candidate => candidate.ParametersList.Any(parameter => parameter.IsFailed))))
        {
            return;
        }

        frames = frames.IsDefault ? [] : frames;

        if(choice.NoArityMatch)
        {
            ProjectedWithholding.Report(diagnostics,
                ProgramModelDiagnostics.NoApplicableOverload, node, frames,
                SnapshotCallee(candidates, description), new QuantityValue(arguments.Count),
                new SequenceValue(SlotKind.Quantity, [.. candidates.Select(candidate => candidate.ParametersList.Count).Distinct().Order().Select(arity => (SlotValue)new QuantityValue(arity))]));

            return;
        }

        if(choice.TypeArgumentMismatch.Count > 0)
        {
            ProjectedWithholding.Report(diagnostics,
                ProgramModelDiagnostics.TypeArgumentCountMismatch, node, frames,
                SnapshotCallee(candidates, description),
                new SequenceValue(SlotKind.Quantity, [.. choice.TypeArgumentMismatch.Select(candidate => candidate.TypeParametersList.Count).Distinct().Order().Select(arity => (SlotValue)new QuantityValue(arity))]));

            return;
        }

        if(choice.UninferredConflict is { } conflict)
        {
            var declared = choice.Uninferred[0].TypeParametersList;
            var spelled = TypeDisplay.Distinguished([conflict.Parameter, .. conflict.Bounds, .. declared]);
            ProjectedWithholding.Report(diagnostics,
                ProgramModelDiagnostics.TypeArgumentsConflict, node, frames,
                SnapshotCallee(candidates, description), DiagnosticSnapshot.Type(conflict.Parameter, spelled[0]),
                new SequenceValue(SlotKind.Type, [.. conflict.Bounds.Select((type, index) => (SlotValue)DiagnosticSnapshot.Type(type, spelled[index + 1]))], ItemPrefix: "'", ItemSuffix: "'"),
                new SequenceValue(SlotKind.Type, [.. declared.Select((type, index) => (SlotValue)DiagnosticSnapshot.Type(type, spelled[index + 1 + conflict.Bounds.Count]))], ItemPrefix: "'", ItemSuffix: "'"));

            return;
        }

        if(choice.Uninferred.Count > 0)
        {
            ProjectedWithholding.Report(diagnostics,
                ProgramModelDiagnostics.TypeArgumentsNotInferred, node, frames,
                SnapshotCallee(candidates, description),
                new SequenceValue(SlotKind.Type, [.. choice.Uninferred[0].TypeParametersList.Select(parameter => (SlotValue)DiagnosticSnapshot.Type(parameter, parameter.Name))], ItemPrefix: "'", ItemSuffix: "'"));

            return;
        }

        if(choice.Unresolvable.Count > 0)
        {
            ProjectedWithholding.Report(diagnostics,
                ProgramModelDiagnostics.UnresolvableOverload, node, frames,
                SnapshotCallee(candidates, description), SnapshotSignatures(choice.Unresolvable));

            return;
        }

        ProjectedWithholding.Report(diagnostics,
            ProgramModelDiagnostics.AmbiguousOverload, node, frames,
            SnapshotCallee(candidates, description), SnapshotSignatures(choice.Ambiguous));
    }

    /// <summary>
    /// The tied candidates as parameter lists, spelled the way source spells types, so the reader can
    /// match them against what they wrote. Internal rather than private because
    /// <see cref="OverloadResolution"/> decides whether to refuse an ambiguous call by asking whether
    /// this renders two of them alike.
    /// </summary>
    internal static string DescribeSignatures<T>(IReadOnlyList<T> candidates)
        where T : class, IOverload
    {
        var signatures = candidates
            .Select(c => $"({string.Join(", ", c.ParametersList.Select(p => TypeDisplay.Mist(p.Type)))})");

        return string.Join(", ", signatures);
    }

    private static SymbolValue SnapshotCallee<T>(IReadOnlyList<T> candidates, string description)
        where T : class, IOverload
    {
        return new SymbolValue(string.Join("|", candidates.Select(candidate => DiagnosticSnapshot.Symbol(candidate, "").Identity)), description);
    }

    private static SequenceValue SnapshotSignatures<T>(IReadOnlyList<T> candidates)
        where T : class, IOverload
    {
        return new SequenceValue(SlotKind.Symbol,
            [.. candidates.Select(candidate => DiagnosticSnapshot.Symbol(candidate,
                $"({string.Join(", ", candidate.ParametersList.Select(parameter => TypeDisplay.Mist(parameter.Type)))})"))]);
    }
}