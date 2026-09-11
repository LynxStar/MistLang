using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// Emits one accessor of a property or an indexer. Shared by both for the same reason
/// <see cref="ProgramModel.Construction.Members.AccessorBuilder"/> builds for both: an accessor is
/// the same construct either side of the member it hangs off.
/// </summary>
internal static class AccessorEmitter
{
    /// <param name="isSetter">
    /// Which half of the member this is. Not derivable from the accessor, whose shape is identical
    /// for a getter and a setter -- only the member knows which slot it filled.
    /// </param>
    public static void Emit(
        PropertyAccessor accessor, bool isSetter, bool isReadonly, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var header = Header(accessor, isSetter, isReadonly);

        if(accessor.Body is { } body)
        {
            writer.WriteLine(header);
            using(writer.Block())
            {
                BodyEmitter.EmitBlock(body, isSetter ? ["value"] : [], writer, diagnostics);
            }

            return;
        }

        if(accessor.ExpressionBody is { } expression)
        {
            writer.Write($"{header} => ");
            BodyEmitter.EmitExpression(expression, writer, diagnostics);
            writer.Append(";");
            writer.AppendLine();
            return;
        }

        writer.WriteLine($"{header};");
    }

    /// <summary>
    /// A bodiless accessor as one token, for a caller placing it inline rather than on its own line.
    /// </summary>
    public static string EmitBodiless(PropertyAccessor accessor, bool isSetter)
    {
        return $"{Header(accessor, isSetter, isReadonly: false)};";
    }

    /// <summary>
    /// Whether this accessor emits as a bare <c>get;</c> with nothing to indent under it.
    /// </summary>
    public static bool IsBodiless(PropertyAccessor? accessor)
    {
        return accessor is null or { Body: null, ExpressionBody: null };
    }

    private static string Header(PropertyAccessor accessor, bool isSetter, bool isReadonly)
    {
        var keyword = isSetter
            ? accessor.IsInit ? "init" : "set"
            : "get";

        // Public on an accessor means "whatever the member said", so only a narrowing is spelled.
        var accessibility = accessor.Accessibility == Accessibility.Private ? "private " : "";

        return $"{accessibility}{(isReadonly ? "readonly " : "")}{keyword}";
    }
}