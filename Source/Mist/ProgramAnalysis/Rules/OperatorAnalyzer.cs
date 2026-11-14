using Mist.Common.Diagnostics;
using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// The binder gives these nodes error types; their complaint is reported here about the operands,
/// whose types remain sound.
/// </summary>
internal sealed class OperatorAnalyzer : IProgramAnalyzer
{
    public string Id => "operator-and-positional-checks";

    public void Analyze(AnalysisContext context)
    {
        var judged = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach(var node in context.Nodes)
        {
            visit(node);
        }

        void visit(object node)
        {
            if(!judged.Add(node))
            {
                return;
            }

            foreach(var child in BodyWalker.ChildrenOf(node))
            {
                visit(child);
            }

            switch(node)
            {
                case BinaryExpression binary when binary.Type is ErrorType:
                    JudgeBinary(context, binary);
                    break;

                case CompoundAssignmentExpression { ResultType: ErrorType } compound:
                    JudgeCompound(context, compound);
                    break;

                case UnaryExpression { Type: ErrorType } unary when OperatorDecision.Tie(unary.AmbiguousOperators) is { } tie:
                    context.Refuse(
                        unary, tie, [unary.Operand],
                        OperatorDecision.Callee(unary.AmbiguousOperators, $"operator '{Spelling(unary.OperatorKind)}'"), OperatorDecision.Signatures(unary.AmbiguousOperators));
                    break;

                case UnaryExpression unary when unary.Type is ErrorType
                    && !context.HasFailure(unary.Operand)
                    && OperatorDecision.Unary(unary.OperatorKind, unary.Operand.Type) is { } refused:
                    context.Refuse(
                        unary, refused, [],
                        new NameValue(Spelling(unary.OperatorKind)), DiagnosticSnapshot.Type(unary.Operand.Type), DiagnosticSnapshot.Type(unary.Operand.Type));
                    break;

                case MemberAccessExpression { IsConditional: true } access
                    when OperatorDecision.Conditional(access.Receiver.Type) is { } conditional:
                    context.Refuse(
                        access, conditional, [access.Receiver],
                        DiagnosticSnapshot.Type(access.Receiver.Type));
                    break;

                case MemberAccessExpression { Receiver.Type: UnknownType, Member: null or NotFoundResult, Type: ErrorType } access:
                    context.Refuse(access, OperatorDecision.Member(access.Receiver.Type)!, [access.Receiver], new NameValue(access.MemberName));
                    break;
            }
        }
    }

    private static void JudgeBinary(AnalysisContext context, BinaryExpression binary)
    {
        IMistType wellKnown(WellKnownType type)
        {
            return context.Model.TypesByFQN[type.FullyQualifiedName];
        }

        if(OperatorDecision.Tie(binary.AmbiguousOperators) is { } tie)
        {
            context.Refuse(
                binary, tie, [binary.Left, binary.Right],
                OperatorDecision.Callee(binary.AmbiguousOperators, $"operator '{Spelling(binary.OperatorKind)}'"), OperatorDecision.Signatures(binary.AmbiguousOperators));
            return;
        }

        if(OperatorDecision.Binary(binary.OperatorKind, binary.Left.Type, binary.Right.Type, wellKnown) is not { } descriptor)
        {
            return;
        }

        context.Refuse(
            binary, descriptor, [binary.Left, binary.Right],
            new NameValue(Spelling(binary.OperatorKind)), DiagnosticSnapshot.Type(binary.Left.Type), DiagnosticSnapshot.Type(binary.Right.Type));
    }

    private static void JudgeCompound(AnalysisContext context, CompoundAssignmentExpression compound)
    {
        IMistType wellKnown(WellKnownType type)
        {
            return context.Model.TypesByFQN[type.FullyQualifiedName];
        }

        var spelling = Spelling(compound.OperatorKind, compound.IncrementForm);

        if(OperatorDecision.Tie(compound.AmbiguousOperators) is { } tie)
        {
            context.Refuse(
                compound, tie, [compound.Target, compound.Value],
                OperatorDecision.Callee(compound.AmbiguousOperators, $"operator '{spelling}'"), OperatorDecision.Signatures(compound.AmbiguousOperators));
            return;
        }

        var descriptor = compound.IncrementForm == IncrementForm.None
            ? OperatorDecision.Binary(compound.OperatorKind, compound.Target.Type, compound.Value.Type, wellKnown)
            : OperatorDecision.Increment(compound.Target.Type, compound.OperatorKind, wellKnown);

        if(descriptor is null || context.HasFailure(compound.Target))
        {
            return;
        }

        context.Refuse(
            compound, descriptor, [],
            new NameValue(spelling), DiagnosticSnapshot.Type(compound.Target.Type), DiagnosticSnapshot.Type(compound.Value.Type));
    }

    private static string Spelling(BinaryOperatorKind kind, IncrementForm form)
    {
        return form == IncrementForm.None
            ? Spelling(kind) + "="
            : kind == BinaryOperatorKind.Add ? "++" : "--";
    }

    private static string Spelling(BinaryOperatorKind kind)
    {
        return kind switch
        {
            BinaryOperatorKind.Add or BinaryOperatorKind.Concatenate => "+",
            BinaryOperatorKind.Subtract => "-",
            BinaryOperatorKind.Multiply => "*",
            BinaryOperatorKind.Divide => "/",
            BinaryOperatorKind.Modulo => "%",
            BinaryOperatorKind.Equal => "==",
            BinaryOperatorKind.NotEqual => "!=",
            BinaryOperatorKind.LessThan => "<",
            BinaryOperatorKind.GreaterThan => ">",
            BinaryOperatorKind.LessThanOrEqual => "<=",
            BinaryOperatorKind.GreaterThanOrEqual => ">=",
            BinaryOperatorKind.LogicalAnd => "&&",
            BinaryOperatorKind.LogicalOr => "||",
            _ => kind.ToString(),
        };
    }

    private static string Spelling(UnaryOperatorKind kind)
    {
        return kind switch
        {
            UnaryOperatorKind.Negate => "-",
            UnaryOperatorKind.LogicalNot => "!",
            _ => kind.ToString(),
        };
    }
}