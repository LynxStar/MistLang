using System.Collections.Immutable;

namespace Mist.Driver.Contracts;

/// <summary>
/// Units processed together into one program model.
/// </summary>
public sealed record MistCompilation(
    ImmutableArray<CompilationUnit> Units,
    string? EntryPoint = null,
    ProjectOutputType OutputType = ProjectOutputType.Executable)
{
    public string DisplayName { get; init; } = "";

    /// <summary>
    /// The referenced Mist projects that contributed units. Excludes the project being compiled and
    /// the standard library, both of which are identified by flags on the units themselves. Empty for
    /// compilations assembled directly from units rather than by the driver.
    /// </summary>
    public ImmutableArray<MistProject> Projects { get; init; } = ImmutableArray<MistProject>.Empty;

    /// <summary>
    /// Recorded by the driver rather than inferred from <see cref="CompilationUnit.IsStdlib"/> so that the
    /// standard library, like each referenced project, is known before any unit is walked. False for
    /// compilations assembled directly from units.
    /// </summary>
    public bool HasStandardLibrary { get; init; }

    /// <summary>
    /// The compiled project's own. <see cref="Projects"/> excludes that project, the referenced projects
    /// carry theirs on <see cref="MistProject.GlobalUsings"/>, and the standard library has none.
    /// </summary>
    public ImmutableArray<string> GlobalUsings { get; init; } = ImmutableArray<string>.Empty;
}