using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.AmbientProvider;

/// <summary>
/// Resolves CLR types to their Mist ProgramModel equivalents during ambient
/// type projection. Checks the current scope (for type parameters), well-known
/// types (Standard Library), and projected shells — in that order.
/// Emits a diagnostic and returns <see cref="ErrorType"/> for unresolvable types.
/// </summary>
internal sealed class AmbientTypeResolver
{
    private readonly Dictionary<(string Namespace, string ClrName), MistType> shells;
    private readonly Dictionary<(string Namespace, string ClrName), IMistType> wellKnownTypes;
    private readonly DiagnosticBag diagnostics;

    public AmbientTypeResolver(
        Dictionary<(string Namespace, string ClrName), MistType> shells,
        Dictionary<(string Namespace, string ClrName), IMistType> wellKnownTypes,
        DiagnosticBag diagnostics)
    {
        this.shells = shells;
        this.wellKnownTypes = wellKnownTypes;
        this.diagnostics = diagnostics;
    }

    /// <summary>
    /// Resolves a CLR type by namespace and CLR name within the given scope.
    /// Checks scope locals first (type parameters), then well-known types,
    /// then projected shells. Returns <see cref="ErrorType"/> and emits a
    /// diagnostic if not found.
    /// </summary>
    public IMistType Resolve(string ns, string clrName, Scope scope)
    {
        // Check scope for type parameters (e.g., where T : U resolves U from scope).
        if(scope.LocalTypes.TryGetValue(clrName, out var localType))
        {
            return localType;
        }

        var key = (ns, clrName);

        if(wellKnownTypes.TryGetValue(key, out var wellKnown))
        {
            return wellKnown;
        }

        if(shells.TryGetValue(key, out var shell))
        {
            return shell;
        }

        diagnostics.Report(
            BackendDiagnostics.UnresolvedAmbientType,
            default,
            "<ambient>",
            (object?)ns,
            clrName);
        return ErrorType.Instance;
    }
}
