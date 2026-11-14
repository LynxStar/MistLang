using System.Collections.Immutable;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;

namespace Mist.ProgramModel.Model;

/// <summary>
/// One per declaration boundary in the compilation, handed to every type registered from it.
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
    /// Every method with the shape of an entry point, recorded during member resolution. Choosing
    /// among them takes <see cref="EntryPointSetting"/> and the files holding top-level statements,
    /// and is left to whoever judges the finished model.
    /// <para>
    /// Per assembly because a referenced project having its own <c>Main</c> is not an error. Member
    /// resolution never visits ambient or synthetic types, so the process-wide <see cref="Ambient"/>
    /// and <see cref="Synthetic"/> singletons are never written.
    /// </para>
    /// </summary>
    internal List<Method> EntryPoints { get; } = [];

    IReadOnlyList<IMethod> IAssembly.EntryPoints => EntryPoints;

    public string? EntryPointSetting { get; init; }

    public bool IsLibrary { get; init; }

    /// <summary>
    /// In processing order.
    /// </summary>
    internal List<CompilationUnit> CompilationUnits { get; } = [];

    IReadOnlyList<CompilationUnit> IAssembly.CompilationUnits => CompilationUnits;

    /// <summary>
    /// Empty for the standard library, which has no mechanism to declare any, and for the ambient
    /// and synthetic assemblies, which have no files.
    /// <para>
    /// Per assembly for the same reason <see cref="EntryPoints"/> is: a referenced project declaring
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
    /// Coincides with the namespace every stdlib declaration lives in, but code that tests a
    /// namespace should not reach for this constant.
    /// </summary>
    public const string StandardName = "Standard";

    /// <summary>
    /// Every CLR-projected type belongs here: ambient types from many physical assemblies are pooled
    /// because no rule distinguishes them. The bracketed name cannot collide with a real project or
    /// namespace.
    /// </summary>
    public static readonly MistAssembly Ambient = new("<ambient>", MistAssemblyKind.Ambient);

    /// <summary>
    /// The assembly every compiler-constructed type belongs to, and the default for any
    /// type that is never registered against a declaration boundary.
    /// </summary>
    public static readonly MistAssembly Synthetic = new("<synthetic>", MistAssemblyKind.Synthetic);

    /// <summary>
    /// Whether this assembly's types have Mist declarations, and so are the compiler's to resolve
    /// members on and emit.
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