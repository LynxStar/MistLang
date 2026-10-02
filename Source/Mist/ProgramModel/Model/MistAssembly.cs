using System.Collections.Immutable;
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
    string? projectPath = null,
    ImmutableArray<string> globalUsings = default) : IAssembly
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
    /// The source files (compilation units) belonging to this assembly, in processing
    /// order. Populated during Stage 4 as each file is processed.
    /// </summary>
    internal List<CompilationUnit> CompilationUnits { get; } = [];

    IReadOnlyList<CompilationUnit> IAssembly.CompilationUnits => CompilationUnits;

    /// <summary>
    /// Empty for the standard library, which has no mechanism to declare any, and for the ambient
    /// and synthetic assemblies, which have no files.
    /// <para>
    /// Per assembly for the same reason <see cref="EntryPoint"/> is: a referenced project declaring
    /// its own is not an error, and its choice must not reach another project's files. Set at
    /// registration and narrowed once to the names that are namespaces, before any type is resolved.
    /// </para>
    /// </summary>
    public ImmutableArray<string> GlobalUsings { get; internal set; } = globalUsings.IsDefault
        ? ImmutableArray<string>.Empty
        : globalUsings
        ;

    IReadOnlyList<string> IAssembly.GlobalUsings => GlobalUsings;

    /// <summary>
    /// The well-known name of the standard library assembly. It coincides with the
    /// namespace every stdlib declaration lives in, but only coincidentally -- code that
    /// tests a namespace should not reach for this constant.
    /// </summary>
    public const string StandardName = "Standard";

    /// <summary>
    /// The assembly every CLR-projected type belongs to. Ambient types come from many
    /// physical assemblies and are pooled here because nothing currently distinguishes
    /// between them; per-assembly ambient identity is the natural extension if a rule
    /// ever needs it.
    /// <para>
    /// The bracketed name cannot collide with a real project or namespace. Nothing looks
    /// assemblies up by name, so it exists only for diagnostics and <see cref="ToString"/>.
    /// </para>
    /// </summary>
    public static readonly MistAssembly Ambient = new("<ambient>", MistAssemblyKind.Ambient);

    /// <summary>
    /// The assembly every compiler-constructed type belongs to, and the default for any
    /// type that is never registered against a declaration boundary.
    /// </summary>
    public static readonly MistAssembly Synthetic = new("<synthetic>", MistAssemblyKind.Synthetic);

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