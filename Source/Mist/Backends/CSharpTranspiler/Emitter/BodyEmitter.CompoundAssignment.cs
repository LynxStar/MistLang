using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

internal static partial class BodyEmitter
{
    [ThreadStatic]
    private static Dictionary<Expression, string>? boundParts;

    /// <summary>
    /// C#'s own <c>op=</c>, <c>++</c> and <c>--</c>, because C# evaluates their target once, which is the order
    /// compound assignment promises; spelling the long form would read a receiver or an index twice. Where C#
    /// has no compound form -- an operator the backend carries as a method, or a member a conversion reaches
    /// through accessors -- the receiver and index are bound once and the target is read and written through
    /// those bindings, in the same order. A value-typed receiver is the storage itself, so it is not copied.
    /// </summary>
    private static string CompoundAssignment(CompoundAssignmentExpression compound, DiagnosticBag diagnostics)
    {
        if(UnsafeReach.Compound(compound, operand => RenderAt(operand, PostfixPrecedence, diagnostics), old => Operation(compound, old, diagnostics)) is { } reached)
        {
            return reached;
        }

        if(CarriedOperator.Applies(compound))
        {
            return Carried(compound, diagnostics);
        }

        var target = Target(compound, diagnostics);
        var symbol = MapBinaryOperator(compound.OperatorKind);

        return compound.IncrementForm switch
        {
            IncrementForm.Prefix => symbol + symbol + target,
            IncrementForm.Postfix => target + symbol + symbol,
            _ => $"{target} {symbol}= {CompoundValue(compound, diagnostics)}",
        };
    }

    private static string Carried(CompoundAssignmentExpression compound, DiagnosticBag diagnostics)
    {
        // A lone part governs the switch itself, so it binds tighter than one; a tuple element only needs to sit
        // in its parentheses.
        var bound = BoundParts(compound.Target).ToList();
        var precedence = bound.Count == 1 ? SwitchPrecedence + 1 : AssignmentPrecedence;
        var parts = bound.Select(part => (Part: part, Rendered: RenderAt(part, precedence, diagnostics), Name: TemporaryName())).ToList();
        var enclosing = boundParts;
        boundParts = new Dictionary<Expression, string>(enclosing ?? [], ReferenceEqualityComparer.Instance);

        string written;
        try
        {
            foreach(var (part, _, name) in parts)
            {
                boundParts[part] = name;
            }

            var target = Target(compound, diagnostics);

            if(compound.IncrementForm == IncrementForm.Postfix)
            {
                var old = TemporaryName();
                written = $"({target} switch {{ var {old} => ({target} = {Operation(compound, old, diagnostics)}, {old}).Item2 }})";
            }
            else
            {
                written = $"{target} = {Operation(compound, target, diagnostics)}";
            }
        }
        finally
        {
            boundParts = enclosing;
        }

        return parts.Count switch
        {
            0 => written,
            1 => $"({parts[0].Rendered} switch {{ var {parts[0].Name} => {written} }})",
            _ => $"(({string.Join(", ", parts.Select(part => part.Rendered))}) switch {{ ({string.Join(", ", parts.Select(part => "var " + part.Name))}) => {written} }})",
        };
    }

    private static IEnumerable<Expression> BoundParts(Expression target)
    {
        var (receiver, index) = target switch
        {
            IndexExpression indexed => (indexed.Receiver, indexed.Index),
            MemberAccessExpression { IsConditional: false } access => (access.Receiver, null),
            _ => (null, null),
        };

        if(receiver is not (null or ThisExpression or TypeReferenceExpression) && !TypeNameEmitter.IsValueKind(receiver.Type) && receiver.Type is not TypeParameter)
        {
            yield return receiver;
        }

        if(index is not (null or LiteralExpression))
        {
            yield return index;
        }
    }

    private static string Target(CompoundAssignmentExpression compound, DiagnosticBag diagnostics)
    {
        return compound.Target is LocalReferenceExpression local
            ? LocalNames.Of(local.Local)
            : RenderAt(compound.Target, PostfixPrecedence, diagnostics);
    }

    private static string Operation(CompoundAssignmentExpression compound, string old, DiagnosticBag diagnostics)
    {
        if(CarriedOperator.Applies(compound))
        {
            return CarriedOperator.Call(compound, old, RenderAt(compound.Value, AssignmentPrecedence, diagnostics));
        }

        return $"{old} {MapBinaryOperator(compound.OperatorKind)} ({CompoundValue(compound, diagnostics)})";
    }

    private static string CompoundValue(CompoundAssignmentExpression compound, DiagnosticBag diagnostics)
    {
        var divisor = compound.GuardsRemainder ? TemporaryName() : null;

        return divisor is not null
            ? $"({RenderAt(compound.Value, SwitchPrecedence + 1, diagnostics)} switch {{ -1 => 1, var {divisor} => {divisor} }})"
            : InvariantCulture.Appends(compound)
                ? Concatenated(compound.Value, AssignmentPrecedence, diagnostics)
                : RenderOperand(compound.Value, compound.Operator, compound.ParameterTypes, 1, AssignmentPrecedence, diagnostics);
    }

    private static string StatementLead(Expression expression)
    {
        return expression is ResultIdentityExpression
            || (expression is CompoundAssignmentExpression compound && CarriedOperator.Applies(compound)
                && (compound.IncrementForm == IncrementForm.Postfix || BoundParts(compound.Target).Any()))
            ? "_ = " : "";
    }

    private static bool BoundPart(Expression expression, CSharpWriter writer)
    {
        if(boundParts is null || !boundParts.TryGetValue(expression, out var name))
        {
            return false;
        }

        writer.Append(name);
        return true;
    }
}