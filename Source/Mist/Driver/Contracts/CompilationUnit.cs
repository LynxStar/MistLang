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
    /// A Standard unit under <c>Standard/Experimental/</c>: it declares what only the experimental
    /// pipeline can model, such as an intrinsic generic class over a CLR type, and the classic
    /// pipeline compiles without it.
    /// </summary>
    public bool IsExperimentalOnly { get; init; }

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
    /// project's own units. Used by the semantic model to enforce transitive
    /// visibility rules: a unit is directly visible to project P only if P's
    /// canonical path matches this value, or if this value is <c>null</c>
    /// (stdlib / user's own units).
    /// </summary>
    public string? OriginProjectPath { get; init; }

    /// <summary>
    /// True if this compilation unit's AST contains at least one
    /// StatementItemTopLevelItemNode (a top-level statement).
    /// Set after parsing, before semantic model construction.
    /// </summary>
    public bool HasTopLevelStatements { get; init; }

    /// <summary>
    /// The parsed AST root for this compilation unit. Assigned after Stage 3
    /// (parsing) when the unit is prepared for semantic model construction.
    /// </summary>
    public Storm.Runtime.SyntaxNode? Ast { get; init; }
}