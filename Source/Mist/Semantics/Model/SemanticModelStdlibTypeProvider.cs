using Mist.Semantics.Contracts;
using Mist.Semantics.Model.Types;

namespace Mist.Semantics.Model;

/// <summary>
/// An <see cref="IStdlibTypeProvider"/> backed by a built
/// <see cref="ISemanticModel"/>. Walks the model's symbol table to find
/// Standard Library types by simple name.
///
/// <para>
/// If the <c>Standard</c> namespace is not present (e.g., stdlib was not
/// loaded), falls back to <see cref="FallbackStdlibTypeProvider"/> so
/// that tests and configurations without stdlib continue to work.
/// </para>
/// </summary>
internal sealed class SemanticModelStdlibTypeProvider : IStdlibTypeProvider
{
    private readonly ISemanticModel _model;

    public SemanticModelStdlibTypeProvider(ISemanticModel model)
    {
        _model = model;
    }

    public IType LookupStandardType(string simpleName)
    {
        // Walk all symbols to find a type in the "Standard" namespace
        // with the given simple name.
        foreach (var symbol in _model.AllSymbols)
        {
            if (symbol.Kind != SymbolKind.Class
                && symbol.Kind != SymbolKind.Struct
                && symbol.Kind != SymbolKind.Interface)
            {
                continue;
            }

            if (symbol.Name != simpleName)
                continue;

            if (symbol.DeclaredType is ClassType ct && ct.ContainingNamespace == "Standard")
                return ct;

            if (symbol.DeclaredType is StructType st && st.ContainingNamespace == "Standard")
                return st;
        }

        // Fallback to legacy singletons when stdlib is not loaded.
        return FallbackStdlibTypeProvider.Instance.LookupStandardType(simpleName);
    }
}
