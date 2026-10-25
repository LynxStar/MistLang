using System.Diagnostics.CodeAnalysis;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.AmbientProvider;

/// <summary>
/// Resolves CLR types to their Mist ProgramModel equivalents during ambient
/// type projection. Checks the current scope (for type parameters), well-known
/// types (Standard Library), and projected shells — in that order.
/// Handles closed generics by interning <see cref="ConstructedType"/> instances.
/// Returns <see cref="ErrorType"/> silently for unresolvable metadata types.
/// </summary>
internal sealed partial class AmbientTypeResolver
{
    private readonly Dictionary<string, DeclaredType> shells;
    private readonly Dictionary<string, IMistType> wellKnownTypes;
    private readonly TypeInterner typeInterner;
    private readonly Func<string, MistType?>? declaredTypes;
    private readonly HashSet<string> unavailable = [];

    public TupleNameRows TupleNames { get; } = new();

    public AmbientTypeResolver(
        Dictionary<string, DeclaredType> shells,
        Dictionary<string, IMistType> wellKnownTypes,
        TypeInterner typeInterner,
        DiagnosticBag diagnostics,
        Func<string, MistType?>? declaredTypes = null)
    {
        this.shells = shells;
        this.wellKnownTypes = wellKnownTypes;
        this.typeInterner = typeInterner;
        this.declaredTypes = declaredTypes;
        _ = diagnostics;
    }

    /// <summary>
    /// Resolves a CLR <see cref="Type"/> reference within the given scope.
    /// Handles closed generics by resolving the definition and type arguments
    /// recursively, then interning a <see cref="ConstructedType"/>.
    /// </summary>
    public MistType Resolve(Type clrType, Scope scope, TupleElementNames? names = null)
    {
        return Resolve(clrType, scope, names, null);
    }

    private MistType Resolve(
        Type clrType, Scope scope, TupleElementNames? names, IReadOnlyDictionary<Type, MistType>? substitutions)
    {
        if(!clrType.IsGenericParameter && !clrType.HasElementType
            && unavailable.Contains(BuildFQN(clrType.IsGenericType ? clrType.GetGenericTypeDefinition() : clrType)))
        {
            return ErrorType.Instance;
        }

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

            return Resolve(elementType, scope, names, substitutions);
        }

        // CLR array type (e.g., int[], string[]): construct Standard.Array<Element, unknown>.
        if(clrType.IsArray)
        {
            return ResolveArrayType(clrType, scope, names, substitutions);
        }

        if(NullableUnderlying(clrType) is { } underlying)
        {
            return (MistType)WrapWithVoid(Resolve(underlying, scope, names, substitutions));
        }

        if(ClrValueTuple.Cardinality(clrType) >= 2)
        {
            return ResolveTuple(clrType, scope, names, substitutions);
        }

        // A DELEGATE IS A FUNCTION TYPE, whatever it is called. Comparison, Predicate and
        // RequestDelegate are ordinary CLR delegates that Mist has no name for, and a nominal
        // projection of one gives a lambda no parameter types to read and a method group nothing to
        // convert to. Its Invoke signature is what it means.
        if(FunctionShape(clrType, scope, names, substitutions) is { } function)
        {
            return function;
        }

        // Closed generic (e.g., IComparable<int>, List<string>): resolve the
        // definition and each argument, then intern a ConstructedType.
        if(clrType.IsGenericType && !clrType.IsGenericTypeDefinition)
        {
            return ResolveClosedGeneric(clrType, scope, names, substitutions);
        }

        // Generic type parameter (e.g., T, TKey): resolve by name from the scope chain.
        if(clrType.IsGenericParameter)
        {
            if(substitutions is not null && substitutions.TryGetValue(clrType, out var argument))
            {
                return argument;
            }

            return ResolveTypeParameter(clrType, scope);
        }

        // Named type — top-level or nested. Both are a single FQN lookup; the only
        // difference is how the FQN is spelled, which BuildFQN handles.
        return ResolveByFQN(BuildFQN(clrType));
    }

    /// <summary>
    /// CLR callable arguments occupy ordinary metadata slots even when their Standard declaration
    /// uses a pack, so the Invoke signature supplies the structural shape before generic construction.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = MetadataOnlyReflection.Justification)]
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = MetadataOnlyReflection.Justification)]
    private MistType? FunctionShape(
        Type clrType, Scope scope, TupleElementNames? names, IReadOnlyDictionary<Type, MistType>? substitutions)
    {
        if(clrType.IsGenericParameter || !IsDelegate(clrType))
        {
            return null;
        }

        var definition = clrType.IsGenericType && !clrType.IsGenericTypeDefinition
            ? clrType.GetGenericTypeDefinition()
            : clrType;
        var invoke = definition.GetMethod("Invoke");

        if(invoke is null)
        {
            return null;
        }

        var arguments = new Dictionary<Type, MistType>();
        if(clrType != definition)
        {
            var parameters = definition.GetGenericArguments();
            var closedArguments = clrType.GetGenericArguments();
            for(var i = 0; i < parameters.Length; i++)
            {
                arguments.Add(parameters[i], Resolve(closedArguments[i], scope, names, substitutions));
            }
        }

        var parameterTypes = invoke.GetParameters()
            .Select(parameter => (IMistType)Resolve(
                parameter.ParameterType, scope,
                TupleElementNames.Of(parameter, parameter.ParameterType), arguments))
            .ToList();

        var returnType = invoke.ReturnType == typeof(void)
            || invoke.ReturnType.FullName == "System.Void"
                ? wellKnownTypes["System.Void"]
                : Resolve(invoke.ReturnType, scope,
                    TupleElementNames.Of(invoke.ReturnParameter, invoke.ReturnType), arguments);

        var closedInvoke = clrType == definition ? invoke : clrType.GetMethod("Invoke")!;
        var effectiveReturn = closedInvoke.ReturnType.IsByRef
            ? closedInvoke.ReturnType.GetElementType()!
            : closedInvoke.ReturnType;
        if(IsReferenceType(effectiveReturn)
            && !ClrNullability.ResultIsNonNull(closedInvoke.ReturnParameter, closedInvoke))
        {
            returnType = WrapWithVoid((MistType)returnType);
        }

        if(parameterTypes.Any(parameter => parameter is ErrorType or NotProjectedType)
            || returnType is ErrorType or NotProjectedType)
        {
            return ErrorType.Instance;
        }

        return typeInterner.GetOrCreateFunctionType(parameterTypes, returnType);
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

    /// <summary>
    /// Not <see cref="Nullable.GetUnderlyingType"/>, which compares by reference against the runtime's own
    /// Nullable definition and so never matches a type loaded through MetadataLoadContext -- the only way
    /// this resolver is ever handed one.
    /// </summary>
    private static Type? NullableUnderlying(Type clrType)
    {
        return clrType.IsGenericType
            && !clrType.IsGenericTypeDefinition
            && clrType.GetGenericTypeDefinition().FullName == "System.Nullable`1"
                ? clrType.GetGenericArguments()[0]
                : null;
    }

    /// <summary>
    /// Resolves a generic type parameter (e.g., T, TKey) by name, walking the scope
    /// parent chain. A parameter declared on a declaring type stays visible to its
    /// nested types through scope parentage.
    /// </summary>
    private static MistType ResolveTypeParameter(Type clrParam, Scope scope)
    {
        // Members are excluded: a type parameter can only be a type name, and admitting fields
        // or methods would let one shadow the parameter being looked for. No location node --
        // this lookup comes from assembly projection, not from Mist source.
        var request = new LookupRequest(clrParam.Name, MemberVisibility.None, LocationNode: null);
        if(scope.Lookup(request) is TypeResult { Type: MistType resolved })
        {
            return resolved;
        }

        return ErrorType.Instance;
    }

    /// <summary>
    /// Resolves a type by its pre-built fully qualified name (CLR-format key).
    /// Checks well-known types first, then projected shells.
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

        // A C# assembly compiled against a composed Mist project names that project's types; the stale
        // standalone copy that defines them is never registered, so the declared type is what they mean.
        if(declaredTypes?.Invoke(fqn) is { } declared)
        {
            return declared;
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
    internal static string BuildFQN(Type clrType)
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
    /// <para>
    /// A NESTED generic repeats its declaring type's parameters in CLR metadata, so the leading
    /// arguments of <c>List`1+Enumerator[Int32]</c> belong to the enclosing instantiation and not to
    /// the nested definition, which <see cref="TypeParameterProjector"/> projected without them. They
    /// are split the same way here: the leading ones build the enclosing <c>List&lt;int&gt;</c>, and only
    /// the remainder are the nested type's own.
    /// </para>
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2055", Justification = MetadataOnlyReflection.Justification)]
    private MistType ResolveClosedGeneric(
        Type clrType, Scope scope, TupleElementNames? names, IReadOnlyDictionary<Type, MistType>? substitutions)
    {
        names?.Skip(ClrValueTuple.Cardinality(clrType) ?? 0);

        var definitionClrType = clrType.GetGenericTypeDefinition();
        var definition = Resolve(definitionClrType, scope);

        if(definition is ErrorType or NotProjectedType)
        {
            return definition;
        }

        var clrArgs = clrType.GetGenericArguments();
        var inherited = definitionClrType.DeclaringType?.GetGenericArguments().Length ?? 0;
        var enclosing = inherited == 0
            ? null
            : Resolve(definitionClrType.DeclaringType!.MakeGenericType(clrArgs[..inherited]), scope, names, substitutions) as ConstructedType;
        if(inherited > 0 && enclosing is null)
        {
            return ErrorType.Instance;
        }

        var resolvedArgs = new List<IMistType>(clrArgs.Length - inherited);

        foreach(var clrArg in clrArgs[inherited..])
        {
            var argument = Resolve(clrArg, scope, names, substitutions);
            if(argument is ErrorType or NotProjectedType)
            {
                return argument;
            }

            resolvedArgs.Add(argument);
        }

        // A span's Mist declaration carries a length the CLR one has no slot for (WellKnownTypeMap says
        // why), so the CLR arguments fill the leading slots and the length arrives unknown, as an
        // array's does.
        if(definition is IGenericDeclaration { TypeParameters.Count: var declared } && declared > resolvedArgs.Count
            && wellKnownTypes.TryGetValue("System.Int32", out var int32))
        {
            while(resolvedArgs.Count < declared)
            {
                resolvedArgs.Add(typeInterner.GetOrCreateUnknownValue(int32));
            }
        }

        return typeInterner.GetOrCreateConstructedType(definition, resolvedArgs, enclosing);
    }

    /// <summary>
    /// A tuple of the ELEMENTS, each Rest spread into the one flat list C# reads it as. Names are taken
    /// in the order <see cref="ClrValueTuple.NameSlots"/> counts them, which every branch of
    /// <see cref="Resolve"/> keeps to: a Rest skips its own names as it is met, the flat tuple having
    /// already taken the ones that mean anything, and a delegate consumes its generic arguments.
    /// </summary>
    private MistType ResolveTuple(
        Type clrType, Scope scope, TupleElementNames? names, IReadOnlyDictionary<Type, MistType>? substitutions)
    {
        var elementNames = names?.Take(ClrValueTuple.Cardinality(clrType)!.Value);
        var elements = new List<IMistType>();
        AppendTupleElements(clrType, scope, names, substitutions, elements);

        if(elements.Find(element => element is ErrorType or NotProjectedType) is MistType unresolved)
        {
            return unresolved;
        }

        var tuple = typeInterner.GetOrCreateTupleType(elements);

        return elementNames is not null && Array.Exists(elementNames, name => name is not null)
            ? new NamedTupleType(tuple, elementNames)
            : tuple;
    }

    private void AppendTupleElements(
        Type clrTuple, Scope scope, TupleElementNames? names,
        IReadOnlyDictionary<Type, MistType>? substitutions, List<IMistType> elements)
    {
        var arguments = clrTuple.GetGenericArguments();

        foreach(var argument in arguments.Take(ClrValueTuple.RestPosition))
        {
            elements.Add(Resolve(argument, scope, names, substitutions));
        }

        if(arguments.Length > ClrValueTuple.RestPosition)
        {
            var rest = arguments[ClrValueTuple.RestPosition];
            names?.Skip(ClrValueTuple.Cardinality(rest)!.Value);
            AppendTupleElements(rest, scope, names, substitutions, elements);
        }
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
    private MistType ResolveArrayType(
        Type clrType, Scope scope, TupleElementNames? names, IReadOnlyDictionary<Type, MistType>? substitutions)
    {
        var elementType = Resolve(clrType.GetElementType()!, scope, names, substitutions);
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
    public IMistType ResolveInbound(
        Type clrType, Scope scope, bool declaredNonNull = false, TupleElementNames? names = null)
    {
        var effective = clrType.IsByRef ? clrType.GetElementType()! : clrType;
        var resolved = Resolve(clrType, scope, names);

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

    internal void MarkUnavailable(IEnumerable<string> names)
    {
        unavailable.UnionWith(names);
    }

    internal MistType? Declared(Type clrType)
    {
        var declared = ResolveByFQN(BuildFQN(clrType.IsGenericType ? clrType.GetGenericTypeDefinition() : clrType));
        return declared is ErrorType ? null : declared;
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