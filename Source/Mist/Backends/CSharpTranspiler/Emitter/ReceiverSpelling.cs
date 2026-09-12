using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// How a body spells its own receiver: <c>this</c> inside a type, and the block's receiver parameter
/// inside the extension block that carries an intrinsic type's Mist-bodied members -- where an
/// implicit reference to a sibling member has to name it too, C# resolving nothing through an
/// extension block's receiver by bare name.
/// </summary>
internal static class ReceiverSpelling
{
    public const string Carrier = "@this";

    [ThreadStatic]
    private static ConcreteType? carried;

    public static string This => carried is null ? "this" : Carrier;

    public static string Qualifier(MemberType? declaringType, bool isStatic)
    {
        if(carried is null || !ReferenceEquals(declaringType, carried))
        {
            return "";
        }

        return (isStatic ? TypeNameEmitter.Emit(carried) : Carrier) + ".";
    }

    public static IDisposable Enter(ConcreteType type)
    {
        carried = type;
        return new Scope();
    }

    private sealed class Scope : IDisposable
    {
        public void Dispose()
        {
            carried = null;
        }
    }
}