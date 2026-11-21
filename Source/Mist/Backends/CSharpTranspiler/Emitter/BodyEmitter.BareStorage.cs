using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;

namespace Mist.Backends.CSharpTranspiler.Emitter;

internal static partial class BodyEmitter
{
    [ThreadStatic]
    private static bool registeringStamps;

    private static void EmitBareStorage(
        string keyword, IMistType elementType, Expression size, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var fill = StorageStamping.Fill(elementType, onStack: keyword == "stackalloc");

        if(fill is { } opened)
        {
            writer.Append($"{opened.Helper}(");
        }

        writer.Append($"{keyword} {TypeNameEmitter.Emit(elementType)}[");
        EmitExpression(size, writer, diagnostics);
        writer.Append(fill switch
        {
            null => "]",
            { Value: { } value } => $"], {value})",
            _ => "])",
        });
    }

    /// <summary>
    /// Registered once, at the outermost block of a body, ahead of every statement nested in it: the
    /// registration only stores a factory, so making it early is harmless and covers lambdas and local
    /// functions that run later.
    /// </summary>
    private static StampRegistration? RegisterStamps(Block block, CSharpWriter writer)
    {
        if(registeringStamps)
        {
            return null;
        }

        registeringStamps = true;

        foreach(var registration in StorageStamping.Registrations(block, enclosingType))
        {
            writer.WriteLine(registration);
        }

        return new StampRegistration();
    }

    private sealed class StampRegistration : IDisposable
    {
        public void Dispose()
        {
            registeringStamps = false;
        }
    }
}