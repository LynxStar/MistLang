using Mist.Semantics.Contracts;
using Mist.Semantics.Model.Types;

namespace Mist.Semantics.Model;

/// <summary>
/// An <see cref="IStdlibTypeProvider"/> that resolves Standard Library types
/// directly from the global scope and symbol table built during Pass 1 of
/// <see cref="SemanticModelBuilder.Build"/>. This provider is used to
/// upgrade the <c>CSharpAmbientTypeResolver</c>'s stdlib provider after
/// Pass 1 completes, so that ambient-type member signatures reference the
/// real stdlib <see cref="IType"/> instances (with operator members in
/// their scopes) instead of the <see cref="FallbackStdlibTypeProvider"/>
/// singletons (which have empty scopes).
/// </summary>
internal sealed class ScopeBasedStdlibTypeProvider : IStdlibTypeProvider
{
    private readonly Scope _globalScope;
    private readonly SymbolTable _symbolTable;

    public ScopeBasedStdlibTypeProvider(Scope globalScope, SymbolTable symbolTable)
    {
        _globalScope = globalScope;
        _symbolTable = symbolTable;
    }

    public IType LookupStandardType(string simpleName)
    {
        return LookupStandardType(simpleName, typeArgumentCount: -1);
    }

    public IType LookupStandardType(string simpleName, int typeArgumentCount)
    {
        // Map simple name to fully-qualified stdlib name.
        var qualifiedName = "Standard." + simpleName;

        try
        {
            return SemanticModelBuilder.LookupQualifiedStdlibType(
                _globalScope, _symbolTable, qualifiedName, typeArgumentCount);
        }
        catch(StdlibMissingException)
        {
            // Stdlib not loaded — fall back to legacy singletons.
            return FallbackStdlibTypeProvider.Instance.LookupStandardType(simpleName, typeArgumentCount);
        }
    }
}