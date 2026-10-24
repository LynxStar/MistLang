namespace Mist.Driver.Contracts;

/// <summary>Whether a runtime-identifier-specific asset is a native library or a managed assembly.</summary>
public enum RuntimeAssetKind
{
    Native,
    Managed
}