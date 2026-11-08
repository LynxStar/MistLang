using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

// Named target methods retain generic operator contracts that CLR operator declarations cannot express.
internal static class CarriedOperator
{
    public static bool Applies(BinaryExpression binary)
    {
        return Applies(binary.Operator);
    }

    public static bool Applies(UnaryExpression unary)
    {
        return Applies(unary.Operator);
    }

    public static bool Applies(CompoundAssignmentExpression compound)
    {
        return Applies(compound.Operator);
    }

    private static bool Applies(Operator? op)
    {
        return op is { Modifiers.IsIntrinsic: false }
            && (op.DeclaringType is ConcreteType { IsIntrinsic: true } || op.TypeParametersList.Count > 0);
    }

    public static string Name(Operator op)
    {
        var prefix = op.DeclaringType is ConcreteType { IsIntrinsic: true } ? op.DeclaringType.Name + "_" : "";
        var instance = UnionSpecialization.CurrentMethod();
        return prefix + (ReferenceEquals(instance?.Method, op) ? UnionSpecialization.Name(op.Name, instance.Arguments) : op.Name);
    }

    public static string Call(BinaryExpression binary, string left, string right)
    {
        return Call(binary.Operator!, binary.OperatorClosing, [left, right]);
    }

    public static string Call(UnaryExpression unary, string operand)
    {
        return Call(unary.Operator!, unary.OperatorClosing, [operand]);
    }

    public static string Call(CompoundAssignmentExpression compound, string target, string value)
    {
        return Call(compound.Operator!, compound.OperatorClosing, [target, value]);
    }

    private static string Call(Operator op, TypeSubstitution closing, IReadOnlyList<string> operands)
    {
        var arguments = op.TypeParametersList.Select(parameter => closing.Apply(parameter, null)).ToArray();
        arguments = arguments.Select(TypeNameEmitter.Closed).ToArray();

        var name = arguments.Length > 0 ? UnionSpecialization.Name(op.Name, arguments) : op.Name;
        var owner = op.DeclaringType is ConcreteType { IsIntrinsic: true }
            ? "MistIntrinsics." + op.DeclaringType.Name + "_" : TypeNameEmitter.Emit(op.DeclaringType!) + ".";
        return $"{owner}{name}({string.Join(", ", operands)})";
    }

    public static void Emit(Operator op, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        writer.WriteLine($"public static {MethodEmitter.EmitSignature(op, diagnostics, Name(op))}");
        using(writer.Block())
        {
            if(op.Body is not null)
            {
                BodyEmitter.EmitMethodBody(op, op.Body, writer, diagnostics);
            }
        }
    }
}