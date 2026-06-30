namespace Mist.ProgramModel.Contracts;

/// <summary>
/// A source location for diagnostic reporting. Self-contained — does not
/// reference the old <c>SourceSpan</c> or <c>SourceText</c> types.
/// </summary>
public sealed class SourceOrigin
{
    /// <summary>
    /// The file path where the declaration appeared.
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// The zero-based start offset in the source file.
    /// </summary>
    public required int StartOffset { get; init; }

    /// <summary>
    /// The length of the source span.
    /// </summary>
    public required int Length { get; init; }

    /// <summary>
    /// The one-based start line number.
    /// </summary>
    public required int StartLine { get; init; }

    /// <summary>
    /// The one-based start column number.
    /// </summary>
    public required int StartColumn { get; init; }
}