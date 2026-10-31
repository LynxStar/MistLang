namespace Mist.Common.Source;

/// <summary>
/// One range of a translated text: copied verbatim from <see cref="Original"/> at
/// <see cref="OriginalStart"/>, or written by the translator for the construct spanning
/// <see cref="OriginalStart"/> and <see cref="OriginalLength"/> there.
/// </summary>
public sealed record OriginSegment(int GeneratedStart, int Length, SourceText Original, int OriginalStart, int OriginalLength, bool Verbatim)
{
    public int GeneratedEnd => GeneratedStart + Length;
}