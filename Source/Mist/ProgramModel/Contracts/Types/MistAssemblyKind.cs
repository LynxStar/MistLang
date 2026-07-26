namespace Mist.ProgramModel.Contracts.Types;

/// <summary>
/// Where an assembly came from, relative to the compilation being performed.
/// </summary>
public enum MistAssemblyKind
{
    /// <summary>
    /// The project currently being compiled. Exactly one assembly has this kind, and
    /// it is the only one whose declarations the compilation owns.
    /// </summary>
    Self,

    /// <summary>
    /// Another Mist project pulled in through <c>[references]</c> in <c>mist.toml</c>.
    /// </summary>
    Reference,

    /// <summary>
    /// The Mist standard library. A distinguished <see cref="Reference"/>: it arrives
    /// the same way, but it is always present and never declared by the user, so the
    /// compiler needs to recognise it specifically.
    /// </summary>
    Standard,

    /// <summary>
    /// CLR types projected from assembly metadata rather than declared in Mist source.
    /// </summary>
    Ambient,

    /// <summary>
    /// Types the compiler constructs rather than reads from a declaration: constructed
    /// generics, unions, tuples, refinements, lifted literals, type parameters, and the
    /// error and unknown sentinels.
    /// <para>
    /// These exist only to give the program model something to reason about. None is
    /// emitted as a declaration -- a constructed type surfaces as C# syntax at each use
    /// site (<c>int[]</c>, <c>List&lt;int&gt;</c>) rather than as a type of its own -- so
    /// there is no declaration boundary they could honestly belong to.
    /// </para>
    /// </summary>
    Synthetic,
}