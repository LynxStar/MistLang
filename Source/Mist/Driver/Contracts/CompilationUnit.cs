namespace Mist.Driver.Contracts;

/// <summary>
/// A single Mist source file passed through the compilation pipeline.
/// </summary>
public sealed record CompilationUnit(
    Common.Source.SourceText Source,
    string NamespaceRoot,
    string ProjectFolder)
{
    /// <summary>
    /// <c>true</c> if this unit was loaded from the Standard Library (see
    /// <see cref="StdlibLoader"/>); <c>false</c> for user sources.
    /// Used for diagnostic presentation and to let analyzers relax user-facing
    /// rules on stdlib sources.
    /// </summary>
    public bool IsStdlib { get; init; }

    /// <summary>
    /// The canonical path of the project that directly declared this unit as a
    /// reference. <c>null</c> for stdlib units and for the top-level user
    /// project's own units. Groups a unit with the <see cref="MistProject"/> it came from.
    /// </summary>
    public string? OriginProjectPath { get; init; }

    /// <summary>
    /// The parsed AST root for this compilation unit. Assigned after Stage 3
    /// (parsing) when the unit is prepared for program model construction.
    /// </summary>
    public Storm.Runtime.SyntaxNode? Ast { get; init; }
}