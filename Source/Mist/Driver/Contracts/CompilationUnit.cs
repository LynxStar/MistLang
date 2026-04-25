namespace Mist.Driver.Contracts;

/// <summary>
/// A single Mist source file passed through the compilation pipeline.
/// </summary>
public sealed record CompilationUnit(Common.Text.SourceText Source);
