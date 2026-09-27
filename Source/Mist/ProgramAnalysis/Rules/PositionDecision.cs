using Mist.Common.Diagnostics;
using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;

namespace Mist.ProgramAnalysis.Rules;

internal static class PositionDecision
{
    public static DiagnosticDescriptor? Coalescing(bool statementPosition)
    {
        return statementPosition ? null : ProgramAnalysisDiagnostics.CoalescingAssignmentNotAStatement;
    }

    public static DiagnosticDescriptor? Reassignment(LocalVariable target)
    {
        return target.IsReadonly ? ProgramAnalysisDiagnostics.ReadonlyLocalReassigned : null;
    }

    public static DiagnosticDescriptor? Optional(IMistType inner)
    {
        return OptionalUnion.ContainsVoid(inner) ? ProgramAnalysisDiagnostics.OptionalOfOptional : null;
    }
}