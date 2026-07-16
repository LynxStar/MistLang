using Mist.Common.Source;
using Mist.Semantics.Model;

namespace Mist.Semantics.Contracts;

/// <summary>
/// Tracks the provenance of a symbol — where it was defined.
/// All symbols in the semantic model use the same ISymbol/IType representations
/// regardless of origin; this enum is the only distinction.
/// </summary>
public enum SymbolOrigin
{
    /// <summary>Defined in Mist source files written by the user.</summary>
    UserCode,

    /// <summary>Provided by the backend runtime (e.g., .NET BCL types for the C# backend).</summary>
    Backend,

    // Future: Library — provided by an external Mist package/library.
    // Not a v1 goal, but the enum is designed to accommodate it.
}

/// <summary>
/// Kinds of symbols in the Mist semantic model.
/// </summary>
public enum SymbolKind
{
    Namespace,
    Class,
    Struct,
    Abstract,
    Enum,
    TypeAlias,
    Newtype,
    Field,
    Constructor,
    Method,
    Parameter,
    LocalVariable,
    Property,
    Indexer,
}

/// <summary>
/// A named semantic entity in the Mist semantic model.
/// </summary>
public interface ISymbol
{
    string Name { get; }
    SymbolKind Kind { get; }
    SymbolOrigin Origin { get; }
    IScope ContainingScope { get; }
    IType? DeclaredType { get; }
    SourceSpan? DeclarationSpan { get; }

    /// <summary>
    /// The resolved accessibility of this symbol. Assigned during Semantic Model
    /// construction and never null/pending afterward.
    /// </summary>
    Accessibility Accessibility { get; }
}