using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// The members a conformer owes the CLR enumerable interfaces but never wrote. Mist's
/// <c>IEnumerable</c> and <c>IEnumerator</c> declare only what iteration needs -- an enumerator, a
/// move and a current -- while the CLR pair they project onto inherit from the non-generic
/// interfaces and <c>IDisposable</c>, so conforming to one obliges four more members.
/// <para>
/// Synthesized rather than declared in <c>Source/Standard</c>, because each one's body is a
/// forward to a member the CONFORMER declares, which the abstract cannot name. <c>Reset</c> is the
/// exception and throws: Mist models no restart, and the CLR documents refusing it as the allowed
/// answer for an enumerator that cannot.
/// </para>
/// </summary>
internal static class IteratorBoilerplateEmitter
{
    public static void Emit(ConcreteType type, CSharpWriter writer, ref bool anyEmitted)
    {
        var emitted = new HashSet<string>(StringComparer.Ordinal);

        foreach(var reached in SlotLift.Reached(type))
        {
            var definition = SlotLift.Definition(reached);
            var element = reached is ConstructedType { TypeArguments.Count: > 0 } constructed
                ? TypeNameEmitter.Emit(constructed.TypeArguments[0])
                : "object";

            if(Is(definition, WellKnownTypes.IEnumerator) && emitted.Add("enumerator"))
            {
                EmitEnumerator(writer, ref anyEmitted);
            }
            else if(Is(definition, WellKnownTypes.IEnumerable) && emitted.Add("enumerable"))
            {
                EmitEnumerable(element, writer, ref anyEmitted);
            }
        }
    }

    private static void EmitEnumerator(CSharpWriter writer, ref bool anyEmitted)
    {
        TypeEmitter.SeparateMember(writer, ref anyEmitted);
        writer.WriteLine("object System.Collections.IEnumerator.Current => Current;");

        TypeEmitter.SeparateMember(writer, ref anyEmitted);
        writer.WriteLine("public void Reset() { throw new System.NotSupportedException(); }");

        TypeEmitter.SeparateMember(writer, ref anyEmitted);
        writer.WriteLine("public void Dispose() { }");
    }

    private static void EmitEnumerable(string element, CSharpWriter writer, ref bool anyEmitted)
    {
        TypeEmitter.SeparateMember(writer, ref anyEmitted);
        writer.WriteLine(
            $"System.Collections.Generic.IEnumerator<{element}>"
            + $" System.Collections.Generic.IEnumerable<{element}>.GetEnumerator() => GetEnumerator();");

        TypeEmitter.SeparateMember(writer, ref anyEmitted);
        writer.WriteLine(
            "System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()"
            + " => GetEnumerator();");
    }

    private static bool Is(IMistType definition, WellKnownType wellKnown)
    {
        return definition.FullyQualifiedName == wellKnown.FullyQualifiedName;
    }
}