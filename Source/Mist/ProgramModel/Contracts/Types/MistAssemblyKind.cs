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
    /// The Mist standard library. A distinguished <see cref="Reference"/>: always present and never
    /// declared by the user, so the compiler recognises it specifically.
    /// </summary>
    Standard,

    /// <summary>
    /// CLR types projected from assembly metadata rather than declared in Mist source.
    /// </summary>
    Ambient,

    /// <summary>
    /// Types the compiler constructs rather than reads from a declaration: constructed generics, unions,
    /// tuples, refinements, lifted literals, type parameters, and the error and unknown sentinels. None is
    /// emitted as a declaration, only as C# syntax at each use site, so there is no declaration boundary
    /// they could honestly belong to.
    /// </summary>
    Synthetic,
}