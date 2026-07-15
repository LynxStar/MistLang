using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.AmbientProvider;

/// <summary>
/// Resolves CLR types to their Mist ProgramModel equivalents during ambient
/// type projection. Checks the current scope (for type parameters), well-known
/// types (Standard Library), and projected shells — in that order.
/// Handles closed generics by interning <see cref="ConstructedType"/> instances.
/// Emits a diagnostic and returns <see cref="ErrorType"/> for unresolvable types.
/// </summary>
internal sealed class AmbientTypeResolver
{
    private readonly Dictionary<string, MistType> shells;
    private readonly Dictionary<string, IMistType> wellKnownTypes;
    private readonly TypeInterner typeInterner;
    private readonly DiagnosticBag diagnostics;

    public AmbientTypeResolver(
        Dictionary<string, MistType> shells,
        Dictionary<string, IMistType> wellKnownTypes,
        TypeInterner typeInterner,
        DiagnosticBag diagnostics)
    {
        this.shells = shells;
        this.wellKnownTypes = wellKnownTypes;
        this.typeInterner = typeInterner;
        this.diagnostics = diagnostics;
    }

    /// <summary>
    /// Resolves a CLR <see cref="Type"/> reference within the given scope.
    /// Handles closed generics by resolving the definition and type arguments
    /// recursively, then interning a <see cref="ConstructedType"/>.
    /// </summary>
    public MistType Resolve(Type clrType, Scope scope)
    {
        // Closed generic (e.g., IComparable<int>, List<string>).
        if(clrType.IsGenericType && !clrType.IsGenericTypeDefinition)
        {
            return ResolveClosedGeneric(clrType, scope);
        }

        var ns = clrType.Namespace ?? "";
        var clrName = clrType.Name;
        return ResolveByFQN(ns, clrName, scope);
    }

    /// <summary>
    /// Resolves a CLR type by constructing a FQN from namespace and CLR name.
    /// Checks scope locals first (type parameters), then well-known types,
    /// then projected shells. Returns <see cref="ErrorType"/> and emits a
    /// diagnostic if not found.
    /// </summary>
    private MistType ResolveByFQN(string ns, string clrName, Scope scope)
    {
        // Check scope for type parameters (e.g., where T : U resolves U from scope).
        if(scope.LocalTypes.TryGetValue(clrName, out var localType))
        {
            return (MistType)localType;
        }

        var fqn = string.IsNullOrEmpty(ns) ? clrName : $"{ns}.{clrName}";

        if(wellKnownTypes.TryGetValue(fqn, out var wellKnown))
        {
            return (MistType)wellKnown;
        }

        if(shells.TryGetValue(fqn, out var shell))
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

    /// <summary>
    /// Resolves a closed generic CLR type by resolving its definition and each
    /// type argument, then interning a <see cref="ConstructedType"/>.
    /// </summary>
    private MistType ResolveClosedGeneric(Type clrType, Scope scope)
    {
        var definitionClrType = clrType.GetGenericTypeDefinition();
        var definition = Resolve(definitionClrType, scope);

        var clrArgs = clrType.GetGenericArguments();
        var resolvedArgs = new List<IMistType>(clrArgs.Length);

        foreach(var clrArg in clrArgs)
        {
            resolvedArgs.Add(Resolve(clrArg, scope));
        }

        return typeInterner.GetOrCreateConstructedType(definition, resolvedArgs);
    }
}