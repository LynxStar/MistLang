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
    /// The request with the enclosing type's receiver taken away. Instance members fall out of
    /// consideration because reading one needs an instance; static members stay because they do not.
    /// <para>
    /// Applied wherever a lookup passes a point at which no receiver of the enclosing type is to be
    /// had. That is not always the type's lexical edge: travelling OUT of a type reaches code with no
    /// instance of it (<see cref="TypeScope"/>), but so does reaching the enclosing type from INSIDE
    /// a member that holds no instance either -- <see cref="MethodScope"/> when static, and
    /// <see cref="InitializerScope"/> always, since an initializer runs before any constructor body.
    /// </para>
    /// <para>
    /// Only ever NARROWS. A type-position request (<see cref="MemberVisibility.None"/>) travels
    /// through unchanged: it admits no member at all, so it has no receiver to lose, and moving it to
    /// <see cref="MemberVisibility.StaticOnly"/> would widen it into admitting static ones -- which
    /// would let a static member sharing a type's name mask that type from a type position that has
    /// to reach past the enclosing type to find it.
    /// </para>
    /// </summary>
    public LookupRequest WithoutReceiver()
    {
        if(Visibility == MemberVisibility.None)
        {
            return this;
        }

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