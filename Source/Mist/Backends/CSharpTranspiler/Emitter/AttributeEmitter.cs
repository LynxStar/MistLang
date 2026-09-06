using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// Emits the attributes written on a declaration, one bracketed group per attribute.
/// <para>
/// TWO SHAPES BECAUSE C# HAS TWO POSITIONS. Every target but a parameter takes its attributes on
/// lines of their own above the declaration; a parameter takes them inline ahead of its type, where
/// there is no line to own. <see cref="ParameterEmitter"/> builds its text as a string rather than
/// into the writer, which is why <see cref="Inline"/> renders through a writer of its own instead of
/// sharing the caller's.
/// </para>
/// </summary>
internal static class AttributeEmitter
{
    public static void EmitLines(
        IReadOnlyList<DeclaredAttribute> attributes, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        foreach(var attribute in attributes)
        {
            writer.Write("[");
            Render(attribute, writer, diagnostics);
            writer.Append("]");
            writer.AppendLine();
        }
    }

    public static string Inline(IReadOnlyList<DeclaredAttribute> attributes, DiagnosticBag diagnostics)
    {
        if(attributes.Count == 0)
        {
            return "";
        }

        var scratch = new CSharpWriter();

        foreach(var attribute in attributes)
        {
            scratch.Append("[");
            Render(attribute, scratch, diagnostics);
            scratch.Append("] ");
        }

        return scratch.ToString();
    }

    private static void Render(DeclaredAttribute attribute, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        writer.Append(TypeNameEmitter.Emit(attribute.AttributeType));

        if(attribute.PositionalArguments.Count == 0 && attribute.NamedArguments.Count == 0)
        {
            return;
        }

        writer.Append("(");
        var lead = "";

        foreach(var argument in attribute.PositionalArguments)
        {
            writer.Append(lead);
            BodyEmitter.EmitExpression(argument, writer, diagnostics);
            lead = ", ";
        }

        foreach(var (name, value) in attribute.NamedArguments)
        {
            writer.Append($"{lead}{name} = ");
            BodyEmitter.EmitExpression(value, writer, diagnostics);
            lead = ", ";
        }

        writer.Append(")");
    }
}