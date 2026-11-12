namespace Mist.Driver.Contracts;

public sealed record CompilationUnit(
    Common.Source.SourceText Source,
    string NamespaceRoot,
    string ProjectFolder)
{
    /// <summary>
    /// Set by <see cref="StdlibLoader"/>.
    /// </summary>
    public bool IsStdlib { get; init; }

    /// <summary>
    /// The canonical path of the referenced project that declared this unit; <c>null</c> for stdlib
    /// units and for the compiled project's own units.
    /// </summary>
    public string? OriginProjectPath { get; init; }

    /// <summary>
    /// Null until parsing (Stage 3) fills it.
    /// </summary>
    public Storm.Runtime.SyntaxNode? Ast { get; init; }
}