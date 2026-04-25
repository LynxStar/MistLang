using Mist.Common.Text;

namespace Mist.Semantics.Contracts;

/// <summary>
/// Kinds of symbols in the Mist semantic model.
/// Concrete members deferred to the Mist-language spec.
/// </summary>
public enum SymbolKind
{
    Variable,
    Function,
    Type,
    Parameter,
    Namespace,
}

/// <summary>
/// A named semantic entity in the Mist semantic model.
/// </summary>
public interface ISymbol
{
    string Name { get; }
    SymbolKind Kind { get; }
    IScope ContainingScope { get; }
    IType? DeclaredType { get; }
    SourceSpan? DeclarationSpan { get; }
}
