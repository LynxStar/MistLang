using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// The intrinsic members whose C# is not the call the emitter would otherwise write. An `intrinsic`
/// declaration is the stdlib's promise that the backend supplies the member, and most are kept with no
/// entry here: an operator emits as an operator, and a member of a type the backing map projects emits
/// as a call on that type, which is why Console.WriteLine and Math.Abs need nothing. Two are spelled
/// differently from the CLR and need rewriting -- Type.Of is `GetType` on the value, and Clone on a
/// backend-backed type IS the value, a struct copying on assignment and String being immutable.
/// <para>
/// Nothing here can tell that a promise is UNKEPT: an unrewritten call is emitted as-is and fails
/// against generated C#, which is how Type.Of and Clone were found. The standard-library spec asks for
/// that to be a compiler error naming the member (Req 5.9), and samples/StandardSurface is what measures
/// the promise across the stdlib until it is.
/// </para>
/// </summary>
internal static class IntrinsicCalls
{
    public static string? Rewrite(Method? callee, string? receiver, IReadOnlyList<string> arguments)
    {
        if(callee is not { Modifiers.IsIntrinsic: true })
        {
            return null;
        }

        if(callee is { Name: "Of", DeclaringType.FullyQualifiedName: "Standard.Type" } && arguments.Count == 1)
        {
            return $"{arguments[0]}.GetType()";
        }

        return callee.Name == "Clone" && arguments.Count == 0 && receiver is not null
            && ClonesAsItself(callee.DeclaringType)
                ? receiver
                : null;
    }

    public static bool ClonesAsItself(IMistType? type)
    {
        return type is not null && SlotLift.Definition(type) is ConcreteType { IsIntrinsic: true };
    }
}