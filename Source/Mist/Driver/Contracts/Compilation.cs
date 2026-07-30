using System.Collections.Immutable;

namespace Mist.Driver.Contracts;

/// <summary>
/// A logical grouping of one or more compilation units processed together,
/// sharing a single semantic model.
/// </summary>
public sealed record MistCompilation(
    ImmutableArray<CompilationUnit> Units,
    string? EntryPoint = null,
    ProjectOutputType OutputType = ProjectOutputType.Executable)
{
    public string DisplayName { get; init; } = "";

    /// <summary>
    /// The referenced Mist projects that contributed units to this compilation, flattened
    /// from the driver's resolved project graph. Excludes the project being compiled and
    /// the standard library, both of which are identified by flags on the units themselves.
    /// <para>
    /// Empty when nothing is referenced, and empty for compilations assembled directly from
    /// units rather than by the driver.
    /// </para>
    /// </summary>
    public ImmutableArray<MistProject> Projects { get; init; } = ImmutableArray<MistProject>.Empty;

    /// <summary>
    /// Whether the standard library contributed units to this compilation. Recorded by the
    /// driver rather than inferred from <see cref="CompilationUnit.IsStdlib"/> so that the
    /// standard library, like each referenced project, is known before any unit is walked.
    /// <para>
    /// False for compilations assembled directly from units rather than by the driver.
    /// </para>
    /// </summary>
    public bool HasStandardLibrary { get; init; }

    /// <summary>
    /// The implicit usings from the <c>mist.toml</c> of the project being compiled. Held here rather
    /// than in <see cref="Projects"/> because that list deliberately excludes the project being
    /// compiled; the referenced projects carry their own on <see cref="MistProject.GlobalUsings"/>,
    /// and the standard library has none.
    /// </summary>
    public ImmutableArray<string> GlobalUsings { get; init; } = ImmutableArray<string>.Empty;
}