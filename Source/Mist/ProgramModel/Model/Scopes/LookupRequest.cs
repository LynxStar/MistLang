using Storm.Runtime;

namespace Mist.ProgramModel.Model.Scopes;

/// <summary>
/// A name being resolved, plus the state scopes narrow as it travels outward. A value type, so a
/// scope narrows by handing its parent a modified copy and cannot affect a sibling's view.
/// </summary>
/// <param name="LocationNode">
/// Where to place the one diagnostic resolution itself reports, import ambiguity. Null for lookups
/// with no syntax behind them, such as resolving a CLR type parameter while projecting an assembly.
/// </param>
internal readonly record struct LookupRequest(
    string Name,
    MemberVisibility Visibility,
    SyntaxNode? LocationNode)
{
    /// <summary>
    /// <c>this</c> is a keyword, so nothing can be declared under this name and no ordinary lookup
    /// can collide with it.
    /// </summary>
    internal const string ReceiverName = "this";

    /// <summary>
    /// A request for the enclosing instance. Starts wide: whether a receiver exists here is for the
    /// scopes to answer, not the caller to assume.
    /// </summary>
    public static LookupRequest Receiver(SyntaxNode? locationNode)
    {
        return new LookupRequest(ReceiverName, MemberVisibility.InstanceAndStatic, locationNode);
    }

    /// <summary>
    /// The request as seen one level further out, having left a type behind. Instance members fall
    /// away because no receiver for that type exists out here; static members need none.
    /// </summary>
    public LookupRequest CrossTypeBoundary()
    {
        return this with { Visibility = MemberVisibility.StaticOnly };
    }

    /// <summary>
    /// A request for the members of one specific type, as reached by qualification. Travels no chain,
    /// so there is no narrowing to do -- the caller already knows which type to ask.
    /// </summary>
    public static LookupRequest Member(string name, MemberVisibility visibility, SyntaxNode? locationNode)
    {
        return new LookupRequest(name, visibility, locationNode);
    }

    public bool Permits(bool isStatic)
    {
        return Visibility switch
        {
            MemberVisibility.InstanceAndStatic => true,
            MemberVisibility.StaticOnly => isStatic,
            MemberVisibility.InstanceOnly => !isStatic,
            _ => false,
        };
    }
}