using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// Emits the members <c>default(T)</c> resolves through: an initializer holding each field's default,
/// and a factory returning an instance built from it. See <see cref="DefaultSynthesis"/> for which
/// types get them and why the sentinel exists.
/// </summary>
internal static class DefaultEmitter
{
    public static void Emit(ConcreteType type, CSharpWriter writer, DiagnosticBag diagnostics, ref bool anyEmitted)
    {
        var self = TypeEmitter.EmitTypeName(type);
        var sentinel = DefaultSynthesis.NeedsSentinel(type);

        SeparateMember(writer, ref anyEmitted);

        if(sentinel)
        {
            writer.WriteLine($"private {TypeEmitter.CleanName(type)}({DefaultSynthesis.MarkerType} marker)");
        }
        else
        {
            writer.WriteLine($"public {TypeEmitter.CleanName(type)}()");
        }

        using(writer.Block())
        {
            EmitAssignments(type, writer, diagnostics);
        }

        SeparateMember(writer, ref anyEmitted);
        writer.WriteLine($"public static {self} {DefaultSynthesis.FactoryName}()");
        using(writer.Block())
        {
            // The marker argument is TYPED rather than a bare `default`, which converts to every
            // one-parameter constructor and so leaves the call ambiguous on any type declaring one.
            var argument = sentinel ? $"default({DefaultSynthesis.MarkerType})" : "";
            writer.WriteLine($"return new {self}({argument});");
        }
    }

    private static void EmitAssignments(ConcreteType type, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        foreach(var (field, value) in DefaultSynthesis.Assignments(type))
        {
            writer.Write($"this.{field.Name} = ");

            if(value is not null)
            {
                BodyEmitter.EmitExpression(value, writer, diagnostics);
            }
            else
            {
                writer.Append(BodyEmitter.EmitTypeDefault(field.Type)!);
            }

            writer.Append(";");
            writer.AppendLine();
        }
    }

    private static void SeparateMember(CSharpWriter writer, ref bool anyEmitted)
    {
        if(anyEmitted)
        {
            writer.WriteBlankLine();
        }

        anyEmitted = true;
    }
}