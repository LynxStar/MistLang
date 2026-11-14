namespace Mist.Common.Source;

/// Line and column are 1-based.
public readonly record struct LinePosition(int Line, int Column);