using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// A newtype over an intrinsic Standard type is that type's contract renamed, and the backend owes it an
/// implementation exactly as it owes one of the source (newtypes spec Requirement 5.2). It already has one to
/// reuse: the clone holds one backing value in <c>Value</c>, and every intrinsic member forwards to the
/// source's own, taking the backing value of an argument typed as the newtype and wrapping a result typed as
/// it. A Mist-bodied member of the source was bound again for the clone and emits as any member does. Boolean's
/// short-circuiting operators emit as the bitwise ones beside the true and false operators, the only way C#
/// lets a type of its own take part in them.
/// </summary>
internal static class IntrinsicCloneEmitter
{
    public static bool Forwards(MemberModifiers modifiers, MemberType? declaringType)
    {
        return modifiers.IsIntrinsic && Wraps(declaringType);
    }

    public static bool Wraps(MemberType? type)
    {
        return type is ConcreteType { CloneSource.IsIntrinsic: true };
    }

    public static void EmitWrapper(ConcreteType clone, CSharpWriter writer)
    {
        writer.WriteLine($"public {TypeEmitter.CleanName(clone)}({TypeNameEmitter.Emit(clone.CloneSource!)} value)");
        using(writer.Block())
        {
            writer.WriteLine("Value = value;");
        }

        writer.WriteBlankLine();
        writer.WriteLine($"public override string ToString() => {InvariantCulture.ObjectText("Value", clone.CloneSource!)};");
    }

    public static void EmitMethod(Method method, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var clone = (ConcreteType)method.DeclaringType!;
        var arguments = method.ParametersList.Select(parameter => Unwrap(parameter, clone)).ToList();
        var counterpart = Counterpart(clone.CloneSource!.EnumerateDelegates(), method.DeclarationNode);
        var call = counterpart is not null && IntrinsicCalls.Respells(counterpart)
            ? IntrinsicCalls.Rewrite(counterpart, "Value", clone.CloneSource, arguments)
            : $"{Receiver(method.Modifiers, clone)}.{method.Name}({string.Join(", ", arguments)})";

        writer.WriteLine($"{MemberModifierEmitter.Emit(method.Modifiers, emitReadonly: false)}{MethodEmitter.EmitSignature(method, diagnostics)}");
        writer.WriteLine($"    => {Wrap(method.ReturnType, clone, call)};");
    }

    public static void EmitOperator(Operator op, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var clone = (ConcreteType)op.DeclaringType!;
        var operands = op.ParametersList.Select(parameter => Unwrap(parameter, clone)).ToList();
        var expression = operands.Count == 1 ? $"{op.Symbol}{operands[0]}" : $"{operands[0]} {op.Symbol} {operands[1]}";
        var parameters = ParameterEmitter.Emit(op.ParametersList, diagnostics);
        var modifiers = MemberModifierEmitter.Emit(op.Modifiers, emitReadonly: false);
        var logical = op.Kind is OperatorKind.LogicalAnd or OperatorKind.LogicalOr;
        var result = IsItself(op.ReturnType, clone) ? clone.CloneSource! : op.ReturnType;
        var symbol = op.Kind switch
        {
            OperatorKind.LogicalAnd => "&",
            OperatorKind.LogicalOr => "|",
            _ => op.Symbol,
        };

        writer.WriteLine($"{modifiers}{MethodEmitter.EmitReturnType(op)} operator {symbol}({parameters})");
        writer.WriteLine($"    => {Wrap(op.ReturnType, clone, NarrowArithmetic.Spell(result, $"({expression})"))};");

        if(logical && clone.OperatorsList.First(other => other.Kind is OperatorKind.LogicalAnd or OperatorKind.LogicalOr) == op)
        {
            var self = TypeNameEmitter.Emit(op.ParametersList[0].Type);
            writer.WriteLine($"public static bool operator true({self} value) => value.Value;");
            writer.WriteLine($"public static bool operator false({self} value) => !value.Value;");
        }
    }

    public static void EmitProperty(Property property, CSharpWriter writer)
    {
        var clone = (ConcreteType)property.DeclaringType!;
        var read = IntrinsicCalls.RespellsProperty(property.Name, clone.CloneSource)
            ? IntrinsicCalls.RewriteProperty("Value")
            : $"{Receiver(property.Modifiers, clone)}.{property.Name}";

        writer.WriteLine($"{MemberModifierEmitter.Emit(property.Modifiers, emitReadonly: false)}{TypeNameEmitter.Emit(property.Type)} {property.Name} => {Wrap(property.Type, clone, read)};");
    }

    public static void EmitIndexer(Indexer indexer, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var clone = (ConcreteType)indexer.DeclaringType!;
        var arguments = indexer.ParametersList.Select(parameter => Unwrap(parameter, clone));
        var parameters = ParameterEmitter.Emit(indexer.ParametersList, diagnostics);

        writer.WriteLine($"{MemberModifierEmitter.Emit(indexer.Modifiers, emitReadonly: false)}{TypeNameEmitter.Emit(indexer.Type)} this[{parameters}]");
        writer.WriteLine($"    => {Wrap(indexer.Type, clone, $"Value[{string.Join(", ", arguments)}]")};");
    }

    public static void EmitConstructor(Constructor constructor, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var clone = (ConcreteType)constructor.DeclaringType!;
        var arguments = constructor.ParametersList.Select(parameter => Unwrap(parameter, clone)).ToList();
        var backing = RegexLowering.Is(clone.CloneSource) && arguments.Count == 1
            ? RegexLowering.Construct(arguments[0])
            : $"new {TypeNameEmitter.Emit(clone.CloneSource!)}({string.Join(", ", arguments)})";

        writer.WriteLine($"public {TypeEmitter.CleanName(clone)}({ParameterEmitter.Emit(constructor.ParametersList, diagnostics)})");
        writer.WriteLine($"    : this({backing}) {{ }}");
    }

    public static void EmitCastOperator(CastOperator castOperator, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var clone = (ConcreteType)castOperator.DeclaringType!;
        var operand = Unwrap(castOperator.SourceParameter, clone);
        var from = Backing(castOperator.SourceParameter.Type, clone);
        var to = Backing(castOperator.TargetType, clone);
        var converted = ReferenceEquals(from, to) ? operand : Convert(operand, from, to);
        var kind = castOperator.IsImplicit ? "implicit" : "explicit";
        var parameters = ParameterEmitter.Emit(castOperator.ParametersList, diagnostics);

        writer.WriteLine($"{MemberModifierEmitter.Emit(castOperator.Modifiers, emitReadonly: false)}{kind} operator {TypeNameEmitter.Emit(castOperator.TargetType)}({parameters})");
        var result = Wrap(castOperator.TargetType, clone, converted);
        var absentStaysAbsent = clone is ClassType && castOperator is { CopiesClone: true, IsImplicit: true };

        writer.WriteLine(absentStaysAbsent
            ? $"    => {castOperator.SourceParameter.Name} is null ? null! : {result};"
            : $"    => {result};");
    }

    private static string Convert(string operand, IMistType from, IMistType to)
    {
        if(RegexLowering.Is(to))
        {
            return RegexLowering.Construct(operand);
        }

        return RegexLowering.Is(from)
            ? RegexLowering.PatternOf(operand)
            : $"({TypeNameEmitter.Emit(to)})({operand})";
    }

    private static IMistType Backing(IMistType type, ConcreteType clone)
    {
        return IsItself(type, clone) ? clone.CloneSource! : type;
    }

    private static string Receiver(MemberModifiers modifiers, ConcreteType clone)
    {
        return modifiers.IsStatic ? TypeNameEmitter.Emit(clone.CloneSource!) : "Value";
    }

    private static string Unwrap(Parameter parameter, ConcreteType clone)
    {
        return IsItself(parameter.Type, clone) ? $"{parameter.Name}.Value" : parameter.Name;
    }

    private static string Wrap(IMistType type, ConcreteType clone, string expression)
    {
        return IsItself(type, clone)
            ? $"new {TypeNameEmitter.Emit(type)}({expression})"
            : expression;
    }

    private static bool IsItself(IMistType type, ConcreteType clone)
    {
        return ConversionRelation.IsIdentity(type, clone) || (type is ConstructedType { Definition: var definition } && ReferenceEquals(definition, clone));
    }

    private static Method? Counterpart(IEnumerable<Method> members, Storm.Runtime.SyntaxNode? declarationNode)
    {
        return declarationNode is null ? null : members.FirstOrDefault(member => ReferenceEquals(member.DeclarationNode, declarationNode));
    }
}