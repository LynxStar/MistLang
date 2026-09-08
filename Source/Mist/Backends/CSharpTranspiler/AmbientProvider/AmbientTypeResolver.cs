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
    private readonly Dictionary<string, DeclaredType> shells;
    private readonly Dictionary<string, IMistType> wellKnownTypes;
    private readonly TypeInterner typeInterner;
    private readonly DiagnosticBag diagnostics;

    public AmbientTypeResolver(
        Dictionary<string, DeclaredType> shells,
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

        // A DELEGATE IS A FUNCTION TYPE, whatever it is called. Comparison, Predicate and
        // RequestDelegate are ordinary CLR delegates that Mist has no name for, and a nominal
        // projection of one gives a lambda no parameter types to read and a method group nothing to
        // convert to. Its Invoke signature is what it means.
        if(FunctionShape(clrType, scope) is { } function)
        {
            return function;
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
    /// The function type a CLR delegate stands for, or null where it is not one -- or where it is
    /// one Mist already names. Func and Action map onto Standard's own declarations, which collapse
    /// to a function type by their own route; taking them here instead would bypass the pack that
    /// makes a single declaration cover seventeen CLR arities.
    /// </summary>
    private FunctionType? FunctionShape(Type clrType, Scope scope)
    {
        if(clrType.IsGenericParameter || !IsDelegate(clrType) || IsWellKnown(clrType))
        {
            return null;
        }

        var invoke = clrType.GetMethod("Invoke");

        if(invoke is null)
        {
            return null;
        }

        var parameters = invoke.GetParameters()
            .Select(parameter => (IMistType)Resolve(parameter.ParameterType, scope))
            .ToList();

        var returnType = invoke.ReturnType == typeof(void)
            || invoke.ReturnType.FullName == "System.Void"
                ? wellKnownTypes["System.Void"]
                : ResolveInbound(
                    invoke.ReturnType, scope, ClrNullability.ResultIsNonNull(invoke.ReturnParameter, invoke));

        return typeInterner.GetOrCreateFunctionType(parameters, returnType);
    }

    private static bool IsDelegate(Type clrType)
    {
        for(var current = clrType.BaseType; current is not null; current = current.BaseType)
        {
            if(current.FullName == "System.MulticastDelegate")
            {
                return true;
            }
        }

        return false;
    }

    private bool IsWellKnown(Type clrType)
    {
        var definition = clrType.IsGenericType && !clrType.IsGenericTypeDefinition
            ? clrType.GetGenericTypeDefinition()
            : clrType;

        return wellKnownTypes.ContainsKey(BuildFQN(definition));
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

        // ERROR WITHOUT A DIAGNOSTIC, because the caller already knows what to do with it: every
        // member projector skips a member whose signature will not resolve, and a member mentioning
        // a type from an assembly nobody loaded is unusable whether or not we say so. Classic never
        // reaches these -- it resolves on demand, so a signature it never asks about costs nothing.
        // v2 projects every member of every type eagerly and so walks thousands of them, which is
        // how samples/Testing came to be refused over Xunit.Sdk.ITheoryData, a name its author
        // never wrote. Calling such a member still fails, at the call site, in the user's own file.
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
    /// <c>Standard.Array&lt;ElementType, Length&gt;</c>, where the length is an
    /// <see cref="UnknownValueType"/> over <c>int</c> because reflection carries no length.
    /// <para>
    /// The length has to be built the same way <c>ScopeResolver.ConstructArray</c> builds it, or a
    /// CLR <c>string[]</c> and a Mist <c>string[]</c> intern as two different types. It is
    /// specifically NOT <c>UnknownType</c>: that is the user-facing top type, and putting it at a
    /// dependent-value position says the array's LENGTH is a value of unknown type rather than an
    /// int nobody has named -- which loses the domain that lets <c>arr.Length</c> emit as an
    /// <c>int</c>, and reads as an array carrying the top type to anything asking where
    /// <c>unknown</c> appears.
    /// </para>
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

        if(!wellKnownTypes.TryGetValue("System.Int32", out var int32Def))
        {
            return ErrorType.Instance;
        }

        var length = typeInterner.GetOrCreateUnknownValue(int32Def);

        return typeInterner.GetOrCreateConstructedType(
            arrayDef, new IMistType[] { elementType, length });
    }

    /// <summary>
    /// Resolves a CLR type in an INBOUND position -- a method return, property, field or indexer
    /// result -- as <c>T | void</c> for reference types and <c>Nullable&lt;T&gt;</c>.
    /// <para>
    /// PARAMETERS DELIBERATELY DO NOT COME THROUGH HERE. Mist has no null to send, so an outbound
    /// reference is non-null by construction; a union there buys nothing and gives overload
    /// resolution extra candidates to be ambiguous between. Inbound is the only direction a null
    /// can arrive from, and NRT annotations are advisory -- a lying one is still a naked null --
    /// so they are ignored and every reference result is wrapped.
    /// </para>
    /// <para>
    /// A generic parameter is left alone. Wrapping T before substitution would make the element of
    /// a <c>List&lt;int&gt;</c> optional, which is unsound in the other direction; the reference
    /// case belongs at substitution time instead.
    /// </para>
    /// </summary>
    /// <param name="declaredNonNull">
    /// What the assembly's nullable metadata says about this result. A reference the CLR PROMISES is
    /// never null needs no wrapping, and wrapping it anyway makes a caller narrow something that
    /// cannot be absent -- which is what stopped `xs.Skip(5).First()` resolving. Nullable-of-T is
    /// unaffected: its absence is in the type, not in an annotation.
    /// </param>
    public IMistType ResolveInbound(Type clrType, Scope scope, bool declaredNonNull = false)
    {
        var effective = clrType.IsByRef ? clrType.GetElementType()! : clrType;
        var underlying = Nullable.GetUnderlyingType(effective);

        if(underlying is not null)
        {
            return WrapWithVoid(Resolve(underlying, scope));
        }

        var resolved = Resolve(clrType, scope);

        return IsReferenceType(effective) && !declaredNonNull ? WrapWithVoid(resolved) : resolved;
    }

    private IMistType WrapWithVoid(MistType inner)
    {
        if(inner is ErrorType or NotProjectedType)
        {
            return inner;
        }

        return typeInterner.GetOrCreateUnionType([inner, ResolveByFQN("System.Void")]);
    }

    private static bool IsReferenceType(Type clrType)
    {
        if(clrType.IsGenericParameter)
        {
            return false;
        }

        if(clrType.IsArray)
        {
            return true;
        }

        return clrType.IsClass || clrType.IsInterface;
    }
}