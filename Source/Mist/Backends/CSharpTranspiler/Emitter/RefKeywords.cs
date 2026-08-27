namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// The <c>ref</c> that precedes a type, in the one spelling C# accepts everywhere it is legal.
/// <para>
/// Shared because a ref alias is declared in four places -- a parameter, a return type, a local, and
/// the initializer that local aliases -- and C# fixes the order as <c>ref readonly</c>, the reverse
/// of how Mist writes it. Spelling it per site is how the two drift apart.
/// </para>
/// </summary>
internal static class RefKeywords
{
    public static string For(bool isRef, bool isReadonly)
    {
        if(!isRef)
        {
            return "";
        }

        return isReadonly ? "ref readonly " : "ref ";
    }
}