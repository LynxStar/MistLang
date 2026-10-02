namespace Mist.Driver.Contracts;

/// <summary>
/// A single Mist source file passed through the compilation pipeline.
/// </summary>
public sealed record CompilationUnit(Common.Source.SourceText Source)
{
    /// <summary>
    /// <c>true</c> if this unit was loaded from the Standard Library (see
    /// <see cref="StdlibLoader"/>); <c>false</c> for user sources.
    /// Used for diagnostic presentation and to let analyzers relax user-facing
    /// rules on stdlib sources.
    /// </summary>
    public bool IsStdlib { get; init; }

    /// <summary>
    /// The root namespace for this compilation unit's inferred namespace
    /// computation. For user code this comes from <c>mist.toml</c> or the
    /// project name; for stdlib units it is <c>"Standard"</c>.
    /// When <c>null</c>, the driver-level <c>namespaceRoot</c> is used.
    /// </summary>
    public string? NamespaceRoot { get; init; }

    /// <summary>
    /// The project folder for this compilation unit's inferred namespace
    /// computation. For user code this is the project directory; for stdlib
    /// units it is the <c>Standard/</c> directory root.
    /// When <c>null</c>, the driver-level <c>projectFolder</c> is used.
    /// </summary>
    public string? ProjectFolder { get; init; }

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