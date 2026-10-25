namespace Mist.Driver.Contracts;

/// <summary>One runtime-identifier-specific file a package supplies, selected for the target identifier and deployed flat beside the output.</summary>
public sealed record RuntimeAsset(string Path, string Rid, RuntimeAssetKind Kind);