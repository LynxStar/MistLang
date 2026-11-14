namespace Mist.Driver.Contracts;

/// <summary>Selected for the target runtime identifier and deployed flat beside the output.</summary>
public sealed record RuntimeAsset(string Path, string Rid, RuntimeAssetKind Kind);