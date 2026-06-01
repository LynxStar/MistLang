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
        return LookupStandardType(simpleName, typeArgumentCount: -1);
    }

    public IType LookupStandardType(string simpleName, int typeArgumentCount)
    {
        // Walk all symbols to find a type in the "Standard" namespace
        // with the given simple name (and arity, when specified).
        ClassType? classFallback = null;
        StructType? structFallback = null;

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
            {
                if (typeArgumentCount < 0 || ct.TypeParameters.Count == typeArgumentCount)
                    return ct;
                classFallback ??= ct;
            }
            else if (symbol.DeclaredType is StructType st && st.ContainingNamespace == "Standard")
            {
                if (typeArgumentCount < 0 || st.TypeParameters.Count == typeArgumentCount)
                    return st;
                structFallback ??= st;
            }
        }

        if (classFallback is not null)
            return classFallback;
        if (structFallback is not null)
            return structFallback;

        // Fallback to legacy singletons when stdlib is not loaded.
        return FallbackStdlibTypeProvider.Instance.LookupStandardType(simpleName, typeArgumentCount);
    }
}
