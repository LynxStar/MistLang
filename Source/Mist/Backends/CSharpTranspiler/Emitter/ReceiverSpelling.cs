using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// How a body spells its own receiver: <c>this</c> inside a type, and a receiver parameter where the body
/// is written outside the type it runs on. Inside the extension block that carries an intrinsic type's
/// Mist-bodied members an implicit reference to a sibling member has to name it too, C# resolving nothing
/// through an extension block's receiver by bare name; inside a conversion running a <c>Clone</c> body every
/// implicit member does, since a bare name would resolve against the newtype the conversion is written in.
/// </summary>
internal static class ReceiverSpelling
{
    public const string Carrier = "@this";

    [ThreadStatic]
    private static IMistType? carried;

    [ThreadStatic]
    private static bool qualifiesEveryMember;

    public static string This => carried is null ? "this" : Carrier;

    public static bool Carries => carried is not null;

    public static string Qualifier(MemberType? declaringType, bool isStatic)
    {
        if(carried is null || declaringType is null || (!qualifiesEveryMember && !ReferenceEquals(declaringType, carried)))
        {
            return "";
        }

        return (isStatic ? TypeNameEmitter.Emit(UnsafeReach.Closed(declaringType)) : Carrier) + ".";
    }

    public static IDisposable Enter(IMistType type, bool qualifiesEveryMember = false)
    {
        carried = type;
        ReceiverSpelling.qualifiesEveryMember = qualifiesEveryMember;
        return new Scope();
    }

    private sealed class Scope : IDisposable
    {
        public void Dispose()
        {
            carried = null;
            qualifiesEveryMember = false;
        }
    }
}