using System.Collections.Immutable;

namespace Mist.Driver.Generators;

public sealed record ProvenanceRecord(
    string ToolchainIdentity,
    string GeneratorIdentity,
    string ConsumerName,
    string ConsumerManifestPath,
    ImmutableArray<ContentHash> Inputs,
    ImmutableArray<string> Arguments,
    string RunKey,
    ImmutableArray<ContentHash> Outputs,
    bool SeedTool)
{
    public int Version { get; init; } = 1;

    public ImmutableArray<ContentHash> Files { get; init; } = [];

    public string? ArtifactPath { get; init; }
}