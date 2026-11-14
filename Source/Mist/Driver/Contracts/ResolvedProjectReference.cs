using System.Collections.Immutable;

namespace Mist.Driver.Contracts;

public sealed record ResolvedProjectReference(
    string CanonicalTomlPath,
    string ProjectFolder,
    ProjectConfiguration Configuration,
    ImmutableArray<string> SourcePaths);