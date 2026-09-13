using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// A conversion between a newtype and its clonable class source: the <c>Clone</c> body it runs, written as a
/// static operator on the newtype whose parameter stands for <c>this</c> (newtypes spec Requirement 10.1).
/// </summary>
internal static class CloneConversionEmitter
{
    public static void Emit(CastOperator conversion, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var clone = (ConcreteType)conversion.DeclaringType!;
        var receiver = conversion.SourceParameter.Type;
        var direction = ReferenceEquals(conversion.TargetType, clone) ? "In" : "Out";

        writer.WriteLine(
            $"public static explicit operator {TypeNameEmitter.Emit(conversion.TargetType)}({TypeNameEmitter.Emit(receiver)} {ReceiverSpelling.Carrier})");

        using var reach = UnsafeReach.Enter(clone, conversion, direction, diagnostics);

        using(writer.Block())
        using(ReceiverSpelling.Enter(receiver, qualifiesEveryMember: true))
        {
            BodyEmitter.EmitBlock(conversion.Body!, [ReceiverSpelling.Carrier], writer, diagnostics);
        }

        foreach(var declaration in reach.Declarations)
        {
            writer.WriteBlankLine();

            foreach(var line in declaration)
            {
                writer.WriteLine(line);
            }
        }
    }
}