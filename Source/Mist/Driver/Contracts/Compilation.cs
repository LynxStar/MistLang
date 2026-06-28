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
}