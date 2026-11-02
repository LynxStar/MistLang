using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

internal static class ResultLowering
{
    public static void EmitConstruction(ResultConstructionExpression construction, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        writer.Append($"{TypeNameEmitter.Emit(construction.Type)}.{(construction.Case == ResultCase.Success ? "Success" : "Failure")}(");
        var result = ResultTypes.Of(construction.Type)!;
        var payloadType = construction.Case == ResultCase.Success ? result.Success : result.Error;
        if(OptionalUnion.IsVoid(construction.Payload.Type) && OptionalUnion.ContainsVoid(payloadType))
        {
            writer.Append("null");
        }
        else
        {
            BodyEmitter.EmitEntering(construction.Payload, payloadType, writer, diagnostics);
        }

        writer.Append(")");
    }

    public static string Emit(ResultPropagationExpression propagation, UnitHoisting.Hoisting hoisting, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var snapshot = BodyEmitter.TemporaryName();
        var union = (UnionType)TypeNameEmitter.Closed(propagation.Inspection);
        var failure = UnionCarrierNames.ConstituentName(TypeNameEmitter.Closed(propagation.FailureCase), union);
        var success = UnionCarrierNames.ConstituentName(TypeNameEmitter.Closed(propagation.SuccessCase), union);
        writer.WriteLine($"var {snapshot} = ({BodyEmitter.Render(propagation.Operand, diagnostics)}).Inspect();");
        writer.WriteLine($"if ({snapshot}.Is{failure})");
        using(writer.Block())
        {
            hoisting.Bind(new UnitHoisting.Step(propagation.ErrorValue, UnitHoisting.StepKind.Spill), $"{snapshot}.As{failure}.Item2");
            BodyEmitter.EmitStatement(propagation.FailureReturn, writer, diagnostics);
        }

        return $"{snapshot}.As{success}.Item2";
    }
}