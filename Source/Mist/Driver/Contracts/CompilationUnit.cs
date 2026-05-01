namespace Mist.Driver.Contracts;

/// <summary>
/// A single Mist source file passed through the compilation pipeline.
/// </summary>
public sealed record CompilationUnit(Common.Text.SourceText Source)
{
    /// <summary>
    /// <c>true</c> if this unit was loaded from the Standard Library (see
    /// <see cref="Mist.Driver.StdlibLoader"/>); <c>false</c> for user sources.
    /// Used for diagnostic presentation and to let analyzers relax user-facing
    /// rules on stdlib sources.
    /// </summary>
    public bool IsStdlib { get; init; }
}
