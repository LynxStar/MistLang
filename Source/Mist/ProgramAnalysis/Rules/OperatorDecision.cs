using Mist.Common.Diagnostics;
using Mist.ProgramModel;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
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
        return OperatorAdmissibility.UnaryResultType(kind, operand) is null ? ProgramAnalysisDiagnostics.OperatorNotApplicable : null;
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