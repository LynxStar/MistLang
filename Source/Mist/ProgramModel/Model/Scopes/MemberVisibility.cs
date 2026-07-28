namespace Mist.ProgramModel.Model.Scopes;

/// <summary>
/// Which members a name may resolve to. A property of the PATH a lookup takes, not of the scope
/// being asked: the same <see cref="TypeScope"/> exposes an instance field to a body inside it and
/// hides it from a nested type below it.
/// </summary>
internal enum MemberVisibility
{
    /// <summary>The starting state inside an instance member.</summary>
    InstanceAndStatic,

    /// <summary>
    /// Two ways to arrive: crossing out of a type, where no receiver for it exists, or naming the
    /// type explicitly as in <c>Console.WriteLine</c>.
    /// </summary>
    StaticOnly,

    /// <summary>
    /// Qualifying with a value rather than a type, as in <c>greeting.Text</c>. Only a qualified
    /// access produces this; a bare name inside an instance member stays
    /// <see cref="InstanceAndStatic"/>, since a static member of the enclosing type needs no
    /// receiver and is nameable there.
    /// </summary>
    InstanceOnly,

    /// <summary>No members; only types, namespaces and locals. Used for type-reference positions.</summary>
    None,
}