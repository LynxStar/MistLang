using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

// Cloning a fallible numeric contract maps its success through the wrapper without duplicating the checked operation.
internal static class IntrinsicCloneArithmetic
{
    public static bool Emit(Operator op, Operator source, ConcreteType clone, IReadOnlyList<string> operands,
        CSharpWriter writer, DiagnosticBag diagnostics)
    {
        if(ResultTypes.Of(op.ReturnType) is not { } target || ResultTypes.Of(source.ReturnType) is not { } original
            || operands.Count != 2
            || !ReferenceEquals(target.Success is ConstructedType closed ? closed.Definition : target.Success, clone))
        {
            return false;
        }

        var verb = op.Kind switch
        {
            OperatorKind.Plus => "AddChecked",
            OperatorKind.Minus => "SubtractChecked",
            OperatorKind.Star => "MultiplyChecked",
            OperatorKind.Slash => "DivideChecked",
            _ => "RemainderChecked",
        };
        var suffix = original.Success.Name == "Int32" ? "" : original.Success.Name;
        var inspection = original.Inspection(null);
        var success = UnionCarrierNames.ConstituentName(inspection.Constituents.Single(part =>
            ReferenceEquals(((ITupleType)part).ElementTypes[1], original.Success)), inspection);
        var failure = UnionCarrierNames.ConstituentName(inspection.Constituents.Single(part =>
            !ReferenceEquals(((ITupleType)part).ElementTypes[1], original.Success)), inspection);
        var result = TypeNameEmitter.Emit(target.Type);
        writer.WriteLine($"public static {result} operator {op.Symbol}({ParameterEmitter.Emit(op.ParametersList, diagnostics)})");
        using(writer.Block())
        {
            writer.WriteLine($"var inspected = Standard.ResultArithmetic.{verb}{suffix}({string.Join(", ", operands)}).Inspect();");
            writer.WriteLine($"if (inspected.Is{failure})");
            using(writer.Block())
            {
                writer.WriteLine($"return {result}.Failure(inspected.As{failure}.Item2);");
            }

            writer.WriteLine($"return {result}.Success(new {TypeNameEmitter.Emit(target.Success)}(inspected.As{success}.Item2));");
        }

        return true;
    }
}