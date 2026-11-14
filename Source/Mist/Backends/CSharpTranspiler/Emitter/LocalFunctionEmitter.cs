using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// Only <c>static</c> and <c>async</c> survive of its modifiers: an accessibility modifier is admitted
/// by Mist and means nothing, since the function is visible exactly where a C# local function is. A static one
/// that reads a hidden slot value drops <c>static</c>, as a static lambda does, because the value is a C# local.
/// </summary>
internal static class LocalFunctionEmitter
{
    public static void Emit(LocalFunctionStatement statement, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var function = statement.Function;

        string spell(Parameter parameter)
        {
            return LocalNames.Of(statement.Parameters[function.ParametersList.IndexOf(parameter)]);
        }

        var modifiers = (function.Modifiers.IsStatic && !BodyEmitter.ReadsHiddenValues(statement) ? "static " : "")
            + (AsyncPromotion.EmitsAsync(function) ? "async " : "");

        writer.WriteLine(
            $"{modifiers}{MethodEmitter.EmitReturnType(function)} {LocalNames.Of(function)}"
            + $"({ParameterEmitter.Emit(function.ParametersList, diagnostics, spell)})");

        using(writer.Block())
        {
            BodyEmitter.EmitLocalFunctionBody(statement, spell, writer, diagnostics);
        }
    }
}