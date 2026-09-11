using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// Emits a local function as a C# local function where it was declared. Only <c>static</c> and
/// <c>async</c> survive of its modifiers: an accessibility modifier is admitted by Mist and means
/// nothing, since the function is visible exactly where a C# local function is.
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

        var modifiers = (function.Modifiers.IsStatic ? "static " : "")
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