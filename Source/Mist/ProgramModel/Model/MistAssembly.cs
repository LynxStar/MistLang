using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Model;

/// <summary>
/// Concrete implementation of <see cref="IAssembly"/>. Created by the builder, one per
/// distinct declaration boundary in the compilation, and handed to every type registered
/// from that boundary.
/// </summary>
public sealed class MistAssembly(
    string name,
    MistAssemblyKind kind,
    string? projectPath = null) : IAssembly
{
    public string Name { get; } = name;

    public MistAssemblyKind Kind { get; } = kind;

    public string? ProjectPath { get; } = projectPath;

    /// <summary>
    /// The type declaring this assembly's entry point, or null if it has none. Recorded
    /// during member resolution as soon as an entry-point method is seen.
    /// <para>
    /// Per assembly rather than per compilation, because that is the granularity the rule
    /// actually has: a referenced project having its own <c>Main</c> is not an error, and
    /// two in one project is one regardless of which project it is. Assigning twice is
    /// therefore the error condition, which makes duplicate detection local instead of a
    /// whole-program count.
    /// </para>
    /// <para>
    /// Only <see cref="MistAssemblyKind.Self"/>, <see cref="MistAssemblyKind.Reference"/>
    /// and <see cref="MistAssemblyKind.Standard"/> can acquire one; member resolution never
    /// visits ambient or synthetic types. The shared ambient and synthetic singletons are
    /// consequently never written, despite being process-wide.
    /// </para>
    /// </summary>
    public IMistType? EntryPoint { get; internal set; }

    /// <summary>
    /// The well-known name of the standard library assembly, which also matches the
    /// namespace every stdlib declaration lives in.
    /// </summary>
    public const string StandardName = "Standard";

    /// <summary>
    /// The name of the bucket holding all CLR-projected types.
    /// </summary>
    public const string AmbientName = "<ambient>";

    /// <summary>
    /// The name of the bucket holding compiler-constructed types.
    /// </summary>
    public const string SyntheticName = "<synthetic>";

    /// <summary>
    /// The assembly every CLR-projected type belongs to. Ambient types come from many
    /// physical assemblies and are pooled here because nothing currently distinguishes
    /// between them; per-assembly ambient identity is the natural extension if a rule
    /// ever needs it.
    /// </summary>
    public static readonly MistAssembly Ambient = new(AmbientName, MistAssemblyKind.Ambient);

    /// <summary>
    /// The assembly every compiler-constructed type belongs to, and the default for any
    /// type that is never registered against a declaration boundary.
    /// </summary>
    public static readonly MistAssembly Synthetic = new(SyntheticName, MistAssemblyKind.Synthetic);

    /// <summary>
    /// Whether this assembly's types are declared in Mist source, and so are the
    /// compiler's to resolve members on and emit. False for CLR-projected and
    /// compiler-constructed types, which have no Mist declaration to work from.
    /// </summary>
    public bool HasMistSource => Kind is
        MistAssemblyKind.Self or
        MistAssemblyKind.Reference or
        MistAssemblyKind.Standard;

    public override string ToString()
    {
        return $"{Name} ({Kind})";
    }
}