namespace Mist.Backends.Contracts;

/// <summary>Who declared the types an emitted C# file holds, which decides the facades that forward them.</summary>
public sealed record DeclarationOrigin(DeclarationOriginKind Kind, string? ProjectPath)
{
    public static DeclarationOrigin StandardLibrary { get; } = new(DeclarationOriginKind.StandardLibrary, null);

    public static DeclarationOrigin SupportFile { get; } = new(DeclarationOriginKind.SupportFile, null);
}