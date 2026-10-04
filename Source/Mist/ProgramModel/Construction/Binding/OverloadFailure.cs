using Mist.Common.Diagnostics;
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
        IReadOnlyList<Expression>? children = null)
        where T : class, IOverload
    {
        var retained = children ?? arguments;
        if(retained.Any(PriorFailure.Of))
        {
            return new ErrorExpression(node, retained);
        }

        if(retained.Any(FailureDependency.NeedsAnalysis))
        {
            var pending = diagnostics.Withhold(() => Report(choice, candidates, arguments, description, node, diagnostics));
            return new ErrorExpression(node, retained, pending.Single());
        }

        Report(choice, candidates, arguments, description, node, diagnostics);
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
        DiagnosticBag diagnostics)
        where T : class, IOverload
    {
        // AN ARGUMENT THAT ALREADY FAILED MAKES THIS FAILURE DERIVED, so it is not reported: no
        // overload could have matched, and saying so adds a second complaint about one mistake.
        if(arguments.Any(PriorFailure.Of))
        {
            return;
        }

        if(choice.NoArityMatch)
        {
            diagnostics.ReportAtNode(
                ProgramModelDiagnostics.NoApplicableOverload, node, null,
                description, arguments.Count, DescribeArities(candidates));

            return;
        }

        if(choice.TypeArgumentMismatch.Count > 0)
        {
            diagnostics.ReportAtNode(
                ProgramModelDiagnostics.TypeArgumentCountMismatch, node, null,
                description, DescribeTypeArities(choice.TypeArgumentMismatch));

            return;
        }

        if(choice.UninferredConflict is { } conflict)
        {
            diagnostics.ReportAtNode(
                ProgramModelDiagnostics.TypeArgumentsConflict, node, null,
                description, conflict.Parameter.Name,
                string.Join(", ", conflict.Bounds.Select(bound => $"'{TypeDisplay.Mist(bound)}'")),
                DescribeTypeParameters(choice.Uninferred));

            return;
        }

        if(choice.Uninferred.Count > 0)
        {
            diagnostics.ReportAtNode(
                ProgramModelDiagnostics.TypeArgumentsNotInferred, node, null,
                description, DescribeTypeParameters(choice.Uninferred));

            return;
        }

        if(choice.Unresolvable.Count > 0)
        {
            diagnostics.ReportAtNode(
                ProgramModelDiagnostics.UnresolvableOverload, node, null,
                description, DescribeSignatures(choice.Unresolvable));

            return;
        }

        diagnostics.ReportAtNode(
            ProgramModelDiagnostics.AmbiguousOverload, node, null,
            description, DescribeSignatures(choice.Ambiguous));
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

    /// <summary>
    /// The parameter counts on offer, ascending and deduplicated, for a diagnostic that would
    /// otherwise say the call was wrong without saying what would be right.
    /// </summary>
    private static string DescribeArities<T>(IReadOnlyList<T> candidates)
        where T : class, IOverload
    {
        var arities = new SortedSet<int>();
        foreach(var candidate in candidates)
        {
            _ = arities.Add(candidate.ParametersList.Count);
        }

        return string.Join(", ", arities);
    }

    private static string DescribeTypeArities<T>(IReadOnlyList<T> candidates)
        where T : class, IOverload
    {
        var arities = new SortedSet<int>();
        foreach(var candidate in candidates)
        {
            _ = arities.Add(candidate.TypeParametersList.Count);
        }

        return string.Join(", ", arities);
    }

    /// <summary>
    /// Every type parameter the first candidate declares, determined or not, because a call states all
    /// of a method's type arguments or none and so all of them are what the user has to write. Which
    /// ones went undetermined is therefore not worth carrying out of selection.
    /// </summary>
    private static string DescribeTypeParameters<T>(IReadOnlyList<T> candidates)
        where T : class, IOverload
    {
        var names = candidates[0].TypeParametersList.Select(parameter => $"'{parameter.Name}'");

        return string.Join(", ", names);
    }
}