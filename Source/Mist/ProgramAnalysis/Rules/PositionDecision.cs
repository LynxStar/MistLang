using Mist.Common.Diagnostics;
using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Scopes;

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

    public static string? ReadonlyReference(Expression expression)
    {
        return expression switch
        {
            LocalReferenceExpression { Local: { IsRef: true } local } when local.IsReadonly || local.Parameter is { Modifier.IsReadonly: true } => local.Name,
            InvocationExpression { Signature: { Modifiers.ReturnsReadonlyRef: true } method } => method.Name,
            IndexExpression { Indexer.Modifiers.ReturnsReadonlyRef: true } => "this[]",
            MemberAccessExpression { Member: PropertyResult { Property: { Modifiers.ReturnsReadonlyRef: true } property } } => property.Name,
            PropertyReferenceExpression { Property: { Modifiers.ReturnsReadonlyRef: true } property } => property.Name,
            _ => null,
        };
    }

    public static string? ByReferenceArgument(ParameterModifier parameter)
    {
        return parameter.Direction switch
        {
            ByRefDirection.In => "passed by reference to an in parameter",
            _ => WritableArgument(parameter),
        };
    }

    public static string? WritableArgument(ParameterModifier parameter)
    {
        return parameter.Direction switch
        {
            ByRefDirection.Ref => "passed as a ref argument",
            ByRefDirection.Out => "passed as an out argument",
            _ => null,
        };
    }

    public static DiagnosticDescriptor? Optional(IMistType inner)
    {
        return OptionalUnion.ContainsVoid(inner) ? ProgramAnalysisDiagnostics.OptionalOfOptional : null;
    }
}