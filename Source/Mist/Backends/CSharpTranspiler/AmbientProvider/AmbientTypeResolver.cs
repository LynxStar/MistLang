using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Scopes;
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
        // Pointer types (int*, void*, T**) are unsafe and have no Mist representation.
        // Returning ErrorType causes the owning member to be skipped.
        if(clrType.IsPointer)
        {
            return ErrorType.Instance;
        }

        // Function pointers (delegate*) have no name and no Mist representation.
        if(string.IsNullOrEmpty(clrType.Name) && string.IsNullOrEmpty(clrType.Namespace))
        {
            return ErrorType.Instance;
        }

        // Bare by-ref types (T&) that reach the resolver without being unwrapped.
        // Unwrap to the element type and resolve that instead.
        if(clrType.IsByRef)
        {
            var elementType = clrType.GetElementType()!;
            if(elementType.IsPointer)
            {
                return ErrorType.Instance;
            }

            return Resolve(elementType, scope);
        }

        // CLR array type (e.g., int[], string[]): construct Standard.Array<Element, unknown>.
        if(clrType.IsArray)
        {
            return ResolveArrayType(clrType, scope);
        }

        // Closed generic (e.g., IComparable<int>, List<string>): resolve the
        // definition and each argument, then intern a ConstructedType.
        if(clrType.IsGenericType && !clrType.IsGenericTypeDefinition)
        {
            return ResolveClosedGeneric(clrType, scope);
        }

        // Generic type parameter (e.g., T, TKey): resolve by name from the scope chain.
        if(clrType.IsGenericParameter)
        {
            return ResolveTypeParameter(clrType, scope);
        }

        // Named type — top-level or nested. Both are a single FQN lookup; the only
        // difference is how the FQN is spelled, which BuildFQN handles.
        return ResolveByFQN(BuildFQN(clrType));
    }

    /// <summary>
    /// Resolves a generic type parameter (e.g., T, TKey) by name, walking the scope
    /// parent chain. A parameter declared on a declaring type stays visible to its
    /// nested types through scope parentage.
    /// Returns <see cref="ErrorType"/> and emits a diagnostic if not found.
    /// </summary>
    private MistType ResolveTypeParameter(Type clrParam, Scope scope)
    {
        // Members are excluded: a type parameter can only be a type name, and admitting fields
        // or methods would let one shadow the parameter being looked for. No location node --
        // this lookup comes from assembly projection, not from Mist source.
        var request = new LookupRequest(clrParam.Name, MemberVisibility.None, LocationNode: null);
        if(scope.Lookup(request) is TypeResult { Type: MistType resolved })
        {
            return resolved;
        }

        diagnostics.Report(
            BackendDiagnostics.UnresolvedAmbientType,
            default,
            "<ambient>",
            (object?)clrParam.Name);
        return ErrorType.Instance;
    }

    /// <summary>
    /// Resolves a type by its pre-built fully qualified name (CLR-format key).
    /// Checks well-known types first, then projected shells.
    /// Returns <see cref="ErrorType"/> and emits a diagnostic if not found.
    /// </summary>
    private MistType ResolveByFQN(string fqn)
    {
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
            (object?)fqn);
        return ErrorType.Instance;
    }

    /// <summary>
    /// Builds a CLR-format FQN for any named type, matching the key format used in
    /// the shells dictionary. For nested types this walks the DeclaringType chain
    /// ("Namespace.Outer.Inner"); for top-level types the loop is a no-op and it
    /// degenerates to "Namespace.Name". Raw CLR names (with backtick arity) are used.
    /// </summary>
    private static string BuildFQN(Type clrType)
    {
        // Walk any nesting chain from innermost outward: [Inner, Middle, ...].
        var chain = new List<string>();
        var current = clrType;

        while(current is not null && current.IsNested)
        {
            chain.Add(current.Name);
            current = current.DeclaringType;
        }

        // current is now the outermost (non-nested) type — its namespace is the real one.
        var ns = current?.Namespace ?? "";
        chain.Add(current?.Name ?? "");
        chain.Reverse();

        var typePath = string.Join(".", chain);
        return string.IsNullOrEmpty(ns) ? typePath : $"{ns}.{typePath}";
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

    /// <summary>
    /// Resolves a CLR array type (e.g., int[], string[]) by constructing
    /// <c>Standard.Array&lt;ElementType, unknown&gt;</c> — matching how the source-side
    /// TypeReferenceResolver handles array rank suffixes.
    /// </summary>
    private MistType ResolveArrayType(Type clrType, Scope scope)
    {
        var elementType = Resolve(clrType.GetElementType()!, scope);
        if(elementType is ErrorType or NotProjectedType)
        {
            return elementType;
        }

        // Look up Standard.Array<,> from well-known types.
        if(!wellKnownTypes.TryGetValue("System.Array", out var arrayDef))
        {
            return ErrorType.Instance;
        }

        return typeInterner.GetOrCreateConstructedType(
            arrayDef, new IMistType[] { elementType, UnknownType.Instance });
    }
}