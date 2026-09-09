using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Members;
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
        foreach(var (field, value, binding) in DefaultSynthesis.Assignments(type))
        {
            if(binding is not null)
            {
                EmitBinding(field, binding, writer, diagnostics);
                continue;
            }

            writer.Write($"this.{field.Name} = ");

            if(value is not null)
            {
                BodyEmitter.EmitExpression(value, writer, diagnostics);
            }
            else
            {
                writer.Append(DefaultSynthesis.Expression(field.Type)!);
            }

            writer.Append(";");
            writer.AppendLine();
        }
    }

    /// <summary>
    /// A field bound to a concrete type: that type's own default, then the nested overrides, then the
    /// assignment. Built through a LOCAL rather than assigned and then overridden through the field,
    /// because the field's declared type is the abstract one the binding exists to widen from, and its
    /// members would not be reachable through it.
    /// </summary>
    private static void EmitBinding(
        Field field, DefaultBindingEntry binding, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var local = $"__default_{field.Name}";

        writer.WriteLine(
            $"var {local} = {DefaultSynthesis.Expression(binding.BoundType!)};");

        foreach(var nested in binding.NestedEntries.OfType<DefaultValueEntry>())
        {
            writer.Write($"{local}.{nested.FieldName} = ");
            BodyEmitter.EmitExpression(nested.Value, writer, diagnostics);
            writer.Append(";");
            writer.AppendLine();
        }

        writer.WriteLine($"this.{field.Name} = {local};");
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