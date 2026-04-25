namespace Mist.Common.Text;

/// <summary>
/// A 1-based line and column position within a source file.
/// </summary>
public readonly record struct LinePosition(int Line, int Column);
