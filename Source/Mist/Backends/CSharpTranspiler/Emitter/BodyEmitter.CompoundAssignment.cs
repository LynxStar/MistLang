using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Expressions;

namespace Mist.Backends.CSharpTranspiler.Emitter;

internal static partial class BodyEmitter
{
    /// <summary>
    /// C#'s own <c>op=</c>, <c>++</c> and <c>--</c>, because C# evaluates their target once, which is the order
    /// compound assignment promises; spelling the long form would read a receiver or an index twice. An operator
    /// the backend carries as a method has no C# compound form, so only that spelling writes the target twice.
    /// </summary>
    private static string CompoundAssignment(CompoundAssignmentExpression compound, DiagnosticBag diagnostics)
    {
        var target = compound.Target is LocalReferenceExpression local
            ? LocalNames.Of(local.Local)
            : RenderAt(compound.Target, PostfixPrecedence, diagnostics);

        if(CarriedOperator.Applies(compound))
        {
            return $"{target} = {CarriedOperator.Call(compound, target, RenderAt(compound.Value, AssignmentPrecedence, diagnostics))}";
        }

        var symbol = MapBinaryOperator(compound.OperatorKind);

        switch(compound.IncrementForm)
        {
            case IncrementForm.Prefix:
                return symbol + symbol + target;

            case IncrementForm.Postfix:
                return target + symbol + symbol;
        }

        var divisor = compound.GuardsRemainder ? TemporaryName() : null;
        var value = divisor is not null
            ? $"({RenderAt(compound.Value, SwitchPrecedence + 1, diagnostics)} switch {{ -1 => 1, var {divisor} => {divisor} }})"
            : InvariantCulture.Appends(compound)
                ? Concatenated(compound.Value, AssignmentPrecedence, diagnostics)
                : RenderOperand(compound.Value, compound.Operator, compound.ParameterTypes, 1, AssignmentPrecedence, diagnostics);

        return $"{target} {symbol}= {value}";
    }
}