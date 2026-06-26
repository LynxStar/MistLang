using Mist.Common.Source;
using Mist.Semantics.Contracts;

namespace Mist.Semantics.Model;

/// <summary>
/// A concrete implementation of <see cref="ISymbol"/>.
/// </summary>
public sealed class Symbol : ISymbol
{
    public Symbol(
        string name,
        SymbolKind kind,
        SymbolOrigin origin,
        IScope containingScope,
        IType? declaredType,
        SourceSpan? declarationSpan,
        bool isIntrinsic = false,
        bool hasBody = true)
    {
        Name = name;
        Kind = kind;
        Origin = origin;
        ContainingScope = containingScope;
        DeclaredType = declaredType;

        // Backend symbols have no source location in user code (R7.4).
        DeclarationSpan = origin == SymbolOrigin.Backend ? null : declarationSpan;

        IsIntrinsic = isIntrinsic;
        HasBody = hasBody;
    }

    public string Name { get; }
    public SymbolKind Kind { get; }
    public SymbolOrigin Origin { get; }
    public IScope ContainingScope { get; }
    public IType? DeclaredType { get; internal set; }
    public SourceSpan? DeclarationSpan { get; }

    /// <summary>
    /// Indicates the member's body is provided by the backend via the
    /// <c>IntrinsicEmissionTable</c> rather than by a Mist-bodied method
    /// (i.e., a member-level <c>intrinsic</c> modifier was present on its
    /// declaration). The flag is set by the semantic model builder and read
    /// by the emitter; it is immutable after construction.
    /// </summary>
    public bool IsIntrinsic { get; }

    /// <summary>
    /// Indicates whether the declaration provides a body. <c>true</c> for
    /// ordinary block-bodied methods, constructors, and operators;
    /// <c>false</c> for declarations that terminate with <c>;</c> (the
    /// <c>MethodNoBody</c> / <c>OperatorNoBody</c> grammar variants).
    /// Intrinsic members omit their bodies (Requirement 5.2); non-intrinsic
    /// members must provide one (Requirement 5.3, 5.4). The flag is set by
    /// the semantic model builder and read by downstream validation
    /// (<c>Pass1e_ValidateIntrinsicMarkers</c>, Task 9). Defaults to
    /// <c>true</c> so existing non-member callers (namespaces, fields,
    /// locals, parameters) remain correct without modification.
    /// </summary>
    public bool HasBody { get; }

    /// <summary>
    /// Property-specific metadata. Non-null only for Property and Indexer symbols.
    /// </summary>
    public PropertyInfo? PropertyMetadata { get; internal set; }

    /// <summary>
    /// Type parameters for generic method symbols. Empty for non-generic methods and non-method symbols.
    /// </summary>
    public IReadOnlyList<IType> TypeParameters { get; internal set; } = Array.Empty<IType>();

    /// <summary>
    /// Parameter symbols for method and constructor symbols projected from CLR assemblies.
    /// Each entry is a <see cref="SymbolKind.Parameter"/> symbol whose <see cref="DeclaredType"/>
    /// carries the parameter type. Empty for non-method symbols and Mist-declared methods
    /// (which store parameters in scopes via AST nodes).
    /// </summary>
    public IReadOnlyList<ISymbol> Parameters { get; internal set; } = Array.Empty<ISymbol>();

    /// <summary>
    /// Context with-clauses declared on this method symbol. Each entry records the
    /// resolved context type, the bound identifier, and the source span. Empty for
    /// non-method symbols and methods without with-clauses.
    /// </summary>
    public IReadOnlyList<ContextClauseInfo> WithClauses { get; internal set; } = Array.Empty<ContextClauseInfo>();

    /// <summary>
    /// Tracks the readonly classification of this symbol. Applies to fields, properties,
    /// indexers, methods, constructors, and parameters. Defaults to <see cref="ReadonlyStatus.Unknown"/>
    /// until the semantic model builder or inference engine classifies it.
    /// </summary>
    public ReadonlyStatus ReadonlyStatus { get; internal set; } = ReadonlyStatus.Unknown;

    /// <summary>
    /// The ownership tier declared on this symbol (variable, parameter, field).
    /// Defaults to <see cref="OwnershipTier.Unrestricted"/> when no ownership
    /// specifier is present.
    /// </summary>
    public OwnershipTier OwnershipTier { get; internal set; } = OwnershipTier.Unrestricted;

    /// <summary>
    /// The ref kind of this symbol. Applies to parameters and local variables.
    /// A value of <see cref="RefKind.Ref"/> means the symbol is a mutable
    /// reference to a storage location; <see cref="RefKind.RefReadonly"/>
    /// means a readonly reference. Defaults to <see cref="RefKind.None"/>
    /// (standard pass-by-value).
    /// </summary>
    public RefKind RefKind { get; internal set; } = RefKind.None;

    /// <summary>
    /// The original CLR calling convention for this parameter, when it was projected
    /// from a CLR assembly. Only meaningful for parameters with <see cref="RefKind.Ref"/>
    /// that originated from the backend. Null for Mist-native parameters.
    /// </summary>
    public ClrCallingConvention? ClrCallingConvention { get; internal set; }

    /// <summary>
    /// The ref kind of this method's return type. Applies only to method symbols.
    /// A value of <see cref="RefKind.Ref"/> means the method returns a mutable
    /// reference; <see cref="RefKind.RefReadonly"/> means it returns a readonly
    /// reference. Defaults to <see cref="RefKind.None"/> (standard return-by-value).
    /// </summary>
    public RefKind ReturnRefKind { get; internal set; } = RefKind.None;

    /// <summary>
    /// Indicates whether the method was declared with the <c>async</c> modifier.
    /// When true, the method's <see cref="DeclaredReturnType"/> records what the
    /// developer wrote (T) and <see cref="EffectiveReturnType"/> records the
    /// compiler-constructed <c>Task&lt;T&gt;</c>. Defaults to <c>false</c>.
    /// </summary>
    public bool IsAsync { get; internal set; }

    /// <summary>
    /// Indicates whether this symbol was declared as <c>static</c>. For method symbols
    /// projected from CLR assemblies, this distinguishes static methods (reachable only
    /// via the type name) from instance methods (reachable via an instance). Defaults
    /// to <c>false</c>.
    /// </summary>
    public bool IsStatic { get; internal set; }

    /// <summary>
    /// Indicates whether this async method should emit <c>ValueTask&lt;T&gt;</c>
    /// instead of <c>Task&lt;T&gt;</c> in the generated C#. This is computed by
    /// <c>ValueTaskUsageAnalysisPass</c> in the C# transpiler backend.
    /// <para>
    /// A method qualifies for <c>ValueTask&lt;T&gt;</c> when its returned task is
    /// awaited at most once and is never assigned to a multi-read variable,
    /// passed as a method argument, or returned from the enclosing method.
    /// </para>
    /// Defaults to <c>false</c> (conservative: emit <c>Task&lt;T&gt;</c>).
    /// </summary>
    public bool UseValueTask { get; internal set; }

    /// <summary>
    /// Indicates whether this method requires the C# <c>async</c> keyword in
    /// emitted code. This is computed by the needs-async propagation pass and
    /// is true when:
    /// <list type="bullet">
    ///   <item>The method is declared <c>async</c> in Mist.</item>
    ///   <item>The method (sync in Mist) directly invokes an async method,
    ///         triggering auto-unwrap which requires <c>await</c> in C#.</item>
    ///   <item>The method uses an explicit <c>await</c> expression.</item>
    ///   <item>The method calls another method that needs C# <c>async</c>
    ///         (transitive propagation over the call graph).</item>
    /// </list>
    /// Defaults to <c>false</c>. Set by
    /// <c>NeedsAsyncPropagationPass</c> in the C# transpiler backend.
    /// </summary>
    public bool NeedsCSharpAsync { get; internal set; }

    /// <summary>
    /// The return type as written by the developer (T, not Task&lt;T&gt;).
    /// For async methods this differs from <see cref="DeclaredType"/> which
    /// stores the effective return type (Task&lt;T&gt;). For non-async methods
    /// this is the same as <see cref="DeclaredType"/>. Null for non-method symbols.
    /// </summary>
    public IType? DeclaredReturnType { get; internal set; }

    /// <summary>
    /// The effective return type visible to callers. For async methods this is
    /// <c>Task&lt;T&gt;</c> (a <see cref="Types.ConstructedType"/> whose generic
    /// definition is the stdlib <c>Task</c> class). For non-async methods this
    /// is the same as <see cref="DeclaredType"/>. Null for non-method symbols.
    /// </summary>
    public IType? EffectiveReturnType { get; internal set; }

    /// <summary>
    /// Whether this field is declared with the <c>owned</c> ownership specifier (Req 1 AC 1)
    /// or implicitly owned because the containing type is an <c>owned type</c> (Req 3 AC 1).
    /// Only meaningful for field symbols.
    /// </summary>
    public bool IsOwnedField { get; internal set; }

    /// <summary>
    /// The explicitly declared this-tier from a <c>with {tier}</c> annotation on a method.
    /// Null when no explicit annotation is present (tier will be inferred).
    /// Only meaningful for method symbols.
    /// </summary>
    public ThisTier? DeclaredThisTier { get; internal set; }

    /// <summary>
    /// The inferred (or declared) this-tier for a method, set by <c>ThisTierInferencePass</c>.
    /// Represents the minimum ownership tier required on <c>this</c> for the method to
    /// execute safely. Defaults to <see cref="ThisTier.None"/>.
    /// Only meaningful for method symbols.
    /// </summary>
    public ThisTier InferredThisTier { get; internal set; } = ThisTier.None;

    /// <summary>
    /// Indicates that this method symbol is a CLR virtual or abstract method that can be
    /// overridden by derived types. Set during type projection for methods that are
    /// virtual/abstract on the CLR type. Used by the C# emitter to detect implicit
    /// override matches (Mist methods that fill a CLR base class slot).
    /// </summary>
    public bool IsVirtualOrAbstract { get; internal set; }

    /// <summary>
    /// The CLR access level of this method, when it differs from Mist's visibility model.
    /// Stores the C# access modifier string (e.g., "protected", "protected internal")
    /// for methods projected from CLR assemblies. Null for Mist-native symbols or when
    /// the access level is public (already expressible in Mist).
    /// Used by the emitter during implicit override emission.
    /// </summary>
    public string? ClrAccessModifier { get; internal set; }
}

/// <summary>
/// A concrete implementation of <see cref="IType"/>.
/// </summary>
public sealed class MistType : IType
{
    public MistType(string displayName, TypeKind kind)
    {
        DisplayName = displayName;
        Kind = kind;
    }

    public string DisplayName { get; }
    public TypeKind Kind { get; }
}

/// <summary>
/// Manages symbol registration and lookup during semantic model construction.
/// </summary>
public sealed class SymbolTable
{
    private List<ISymbol> allSymbols = [];
    private Dictionary<Storm.Runtime.SyntaxNode, ISymbol> nodeToSymbol = [];
    private Dictionary<ISymbol, Storm.Runtime.SyntaxNode> symbolToNode = [];

    public void Register(ISymbol symbol, Storm.Runtime.SyntaxNode declaringNode)
    {
        allSymbols.Add(symbol);
        nodeToSymbol[declaringNode] = symbol;
        symbolToNode[symbol] = declaringNode;
    }

    public IReadOnlyList<ISymbol> AllSymbols => allSymbols;

    public ISymbol? GetDeclaredSymbol(Storm.Runtime.SyntaxNode node)
    {
        return nodeToSymbol.GetValueOrDefault(node);
    }

    public Storm.Runtime.SyntaxNode? GetDeclaringNode(ISymbol symbol)
    {
        return symbolToNode.GetValueOrDefault(symbol);
    }

    /// <summary>
    /// Registers a usage-site AST node (e.g., an <c>IdentifierExpressionPrimaryNode</c>
    /// at a call site or assignment RHS) as referencing the given symbol. This allows
    /// downstream analyzers to resolve identifier references via <see cref="GetDeclaredSymbol"/>.
    /// Unlike <see cref="Register"/>, this does NOT add the symbol to <see cref="AllSymbols"/>
    /// (it's already registered from its declaration site).
    /// </summary>
    public void RegisterUsageSite(Storm.Runtime.SyntaxNode usageNode, ISymbol referencedSymbol)
    {
        _ = nodeToSymbol.TryAdd(usageNode, referencedSymbol);
    }
}