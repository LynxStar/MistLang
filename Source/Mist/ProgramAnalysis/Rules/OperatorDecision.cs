using Mist.Common.Diagnostics;
using Mist.ProgramModel;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

internal static class OperatorDecision
{
    public static DiagnosticDescriptor? Binary(
        BinaryOperatorKind kind, IMistType left, IMistType right, Func<WellKnownType, IMistType> wellKnown)
    {
        if(OperatorAdmissibility.ResultType(kind, left, right, wellKnown) is not null)
        {
            return null;
        }

        return left is UnknownType || right is UnknownType
            ? ProgramAnalysisDiagnostics.UnknownHasNoOperators : ProgramAnalysisDiagnostics.OperatorNotApplicable;
    }

    public static DiagnosticDescriptor? Unary(UnaryOperatorKind kind, IMistType operand)
    {
        if(OperatorAdmissibility.UnaryResultType(kind, operand) is not null)
        {
            return null;
        }

        return operand is UnknownType
            ? ProgramAnalysisDiagnostics.UnknownHasNoOperators : ProgramAnalysisDiagnostics.OperatorNotApplicable;
    }

    /// <summary>
    /// A tie the binder recorded among declared operators is refused under the code a tied method call carries
    /// (standard-library Requirement 14.2), naming each candidate with its owner: two owners may declare one
    /// parameter list, so the parameters alone would describe a tie between two identical signatures.
    /// </summary>
    public static DiagnosticDescriptor? Tie(IReadOnlyList<ClosedOperator> tied)
    {
        return tied.Count > 1 ? ProgramModelDiagnostics.AmbiguousOverload : null;
    }

    public static string Signatures(IReadOnlyList<ClosedOperator> tied)
    {
        return string.Join(", ", tied.Select(OperatorSelection.Describe));
    }

    public static DiagnosticDescriptor? Conditional(IMistType receiver)
    {
        return OptionalUnion.AdmitsConditionalAccess(receiver) ? null : ProgramAnalysisDiagnostics.ConditionalAccessOnNonOptional;
    }

    public static DiagnosticDescriptor? Member(IMistType receiver)
    {
        return receiver is UnknownType ? ProgramAnalysisDiagnostics.UnknownHasNoMembers : null;
    }
}