using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model.Types;
using Storm.Runtime;

namespace Mist.Semantics.Model;

/// <summary>
/// Phase C of the <c>unknown-type</c> feature: classifies every field,
/// property, indexer, method, and constructor of every generic type
/// declaration with respect to each of the type's type parameters,
/// producing an <see cref="IReachabilityMap"/> that the Type Checker
/// consults to filter members on <c>Closed_Generic_With_Unknown</c>
/// receivers.
/// </summary>
/// <remarks>
/// <para>
/// <b>Task 17</b> implemented the static (non-body-walk) rules: fields,
/// auto-implemented properties, and auto-implemented indexers. Task 17 also
/// stubbed methods, constructors, and block-bodied properties/indexers at
/// the lattice bottom (<see cref="MemberClassification.DoesNotWriteT"/>),
/// producing a total map pending body-walk refinement.
/// </para>
/// <para>
/// <b>Task 18</b> adds the method-body and constructor-body analysis: it
/// walks each member body, collects <i>direct</i> write-sites into T-typed
/// storage, records each call site (static-internal, static-external, or
/// dynamic-dispatch) into an internal per-generic call graph, and applies
/// the intrinsic classification rules from Requirements 9.5 – 9.9:
/// </para>
/// <para>
/// <b>Task 19</b> (this file, revision) adds two things on top of Task 18:
/// </para>
/// <list type="number">
/// <item><description>
/// <b>Walker-side internal-callee resolution.</b> Calls of the form
/// <c>this.M(...)</c> and unqualified <c>M(...)</c> that lexically appear
/// inside a method/constructor of a generic type <c>G</c> now attempt to
/// resolve <c>M</c> against <c>G</c>'s own member scope. When resolution
/// succeeds the call site is recorded with <c>IsInternal=true</c> and a
/// <c>Callee</c> pointing at the sibling member symbol. Calls that do not
/// match this pattern (external static calls, external/virtual member
/// invocations, bare identifier invocations that resolve outside the
/// generic) remain <c>IsInternal=false, IsDynamic=true</c> per Task 18's
/// conservative rule.
/// </description></item>
/// <item><description>
/// <b>Fixed-point propagation.</b> After the Task-18 intrinsic
/// classifications are seeded, an ascending-lattice fixed-point loop
/// promotes a caller to <see cref="MemberClassification.WritesT"/> whenever
/// any of its resolved internal callees is <see cref="MemberClassification.WritesT"/>.
/// The lattice has height 2 (<c>DoesNotWriteT &lt; WritesT</c>), so the
/// loop terminates in a bounded number of iterations. This is how
/// mutually recursive methods (A → B → A) reach a stable classification.
/// </description></item>
/// </list>
/// <para>
/// The intrinsic classification rules from Requirements 9.5 – 9.9 are
/// unchanged:
/// </para>
/// <list type="bullet">
/// <item><description>
/// Method: <see cref="MemberClassification.WritesT"/> iff the body contains
/// a direct write to T-typed storage (Req 9.5) <b>or</b> calls an external/
/// dynamic-dispatch target with an argument whose type contains <c>T</c> at
/// a storage position (Req 9.8, 9.9).
/// </description></item>
/// <item><description>
/// Constructor: <see cref="MemberClassification.WritesT"/> iff any parameter
/// type contains <c>T</c> at a storage position, <b>or</b> the body performs
/// a direct write to T-typed storage, <b>or</b> any external/dynamic call
/// has a T-containing argument (Req 9.7 + Req 9.5/9.8/9.9).
/// </description></item>
/// </list>
/// <para>
/// <b>Propagation</b> of <see cref="MemberClassification.WritesT"/> <i>through</i>
/// the call graph (if A calls internal B and B is WritesT, then A is WritesT)
/// is <b>Task 19</b>'s fixed-point iteration; Task 18 only produces the
/// <i>intrinsic</i> per-member classification. Static-internal call sites are
/// recorded in the internal call graph but do not themselves promote a
/// caller's classification in Task 18. The lattice is ascending (height 2),
/// so Task 19 can only ever move classifications upward from the Task-18
/// under-approximation.
/// </para>
/// <para>
/// <b>Call graph shape (for Task 19 consumers).</b> For every internal
/// member (method/constructor) of a generic type <c>G</c>, the map records
/// a <see cref="MemberCallInfo"/> containing:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <c>HasDirectWriteFor(T)</c> — per-T bit indicating whether the member's
/// own body writes to T-typed storage directly.
/// </description></item>
/// <item><description>
/// <c>CallSites</c> — list of <see cref="CallSite"/>s, each tagged as
/// internal (points to a member symbol of the same generic <c>G</c>),
/// external (points to a symbol from outside <c>G</c> — e.g., C# BCL or
/// another Mist type), or dynamic (virtual/trait dispatch with unresolved
/// callee). Each call site carries the resolved argument types so Task 19
/// can re-check <c>ContainsTAtStoragePosition</c> for each type parameter.
/// </description></item>
/// <item><description>
/// For constructors, <c>ParameterContainsT(T)</c> — per-T bit indicating
/// whether any parameter type contains <c>T</c> at a storage position.
/// </description></item>
/// </list>
/// </remarks>
public sealed class ReachabilityAnalyzer
{
    /// <summary>
    /// An <see cref="IReachabilityMap"/> that knows about no generic types —
    /// every lookup throws <see cref="KeyNotFoundException"/>. Used as the
    /// default reachability map for <see cref="SemanticModel"/> instances
    /// built without running the analyzer (e.g., in tests that construct a
    /// semantic model directly from a synthetic symbol table rather than
    /// through <see cref="SemanticModelBuilder.Build"/>). Consumers that
    /// would otherwise have received a real map but legitimately want to
    /// disable filtering can use this instance.
    /// </summary>
    public static IReachabilityMap Empty { get; } = new ReachabilityMap(
        new Dictionary<MemberTypeParamKey, MemberClassification>(MemberTypeParamKey.Comparer),
        new Dictionary<MemberTypeParamKey, MemberClassification>(MemberTypeParamKey.Comparer),
        new Dictionary<MemberTypeParamKey, PropertyClassification>(MemberTypeParamKey.Comparer),
        new Dictionary<ISymbol, MemberCallInfo>(ReferenceEqualityComparer.Instance));

    /// <summary>
    /// Classifies every member of every generic type declaration reachable
    /// through <paramref name="symbols"/>.
    /// </summary>
    /// <param name="symbols">
    /// The symbol table populated by Pass 1 / Pass 2 of the semantic model
    /// builder. Method and constructor declaring nodes are looked up through
    /// this table.
    /// </param>
    /// <param name="expressionTypes">
    /// The expression-type map populated by Pass 2. Used by Task 18 to type
    /// assignment targets (LHS storage type) and argument expressions (for
    /// the external/dynamic-dispatch T-containment rule).
    /// </param>
    /// <returns>
    /// An <see cref="IReachabilityMap"/> totalled over every field, property,
    /// indexer, method, and constructor of every generic type declaration in
    /// <paramref name="symbols"/>.
    /// </returns>
    public static IReachabilityMap Analyze(
        SymbolTable symbols,
        IReadOnlyDictionary<SyntaxNode, IType> expressionTypes)
    {
        ArgumentNullException.ThrowIfNull(symbols);
        ArgumentNullException.ThrowIfNull(expressionTypes);

        var methodClassifications = new Dictionary<MemberTypeParamKey, MemberClassification>(MemberTypeParamKey.Comparer);
        var fieldClassifications = new Dictionary<MemberTypeParamKey, MemberClassification>(MemberTypeParamKey.Comparer);
        var propertyClassifications = new Dictionary<MemberTypeParamKey, PropertyClassification>(MemberTypeParamKey.Comparer);
        var callGraph = new Dictionary<ISymbol, MemberCallInfo>(ReferenceEqualityComparer.Instance);

        // Per-generic work queue: each entry carries the methods/constructors
        // discovered for that generic plus the generic's type parameters, so
        // Task 19's fixed-point loop can iterate over the (member, T) pairs
        // scoped to that generic (without cross-generic propagation).
        var perGenericMethods = new List<(List<ISymbol> Members, IReadOnlyList<IType> TypeParameters)>();

        foreach (var typeSymbol in symbols.AllSymbols)
        {
            var (memberScope, typeParameters) = GetGenericTypeInfo(typeSymbol);
            if (memberScope is null || typeParameters is null || typeParameters.Count == 0)
                continue;

            // Enumerate the members of this generic's own scope — the set
            // that forms the "internal" callee pool for Task 18's call-graph
            // distinction. A call to a symbol outside this set (even another
            // member of a different generic type) is classified as "external"
            // for Task 18's conservative rule (Req 9.9).
            var internalMembers = new HashSet<ISymbol>(memberScope.Members, ReferenceEqualityComparer.Instance);

            var genericMethods = new List<ISymbol>();

            foreach (var member in memberScope.Members)
            {
                foreach (var typeParameter in typeParameters)
                {
                    var key = new MemberTypeParamKey(member, typeParameter);

                    switch (member.Kind)
                    {
                        case SymbolKind.Field:
                            fieldClassifications[key] = ClassifyField(member, typeParameter);
                            break;

                        case SymbolKind.Property:
                        case SymbolKind.Indexer:
                            propertyClassifications[key] = ClassifyPropertyOrIndexer(member, typeParameter);
                            break;

                        case SymbolKind.Method:
                        case SymbolKind.Constructor:
                            methodClassifications[key] = ClassifyMethodOrConstructor(
                                member,
                                typeParameter,
                                symbols,
                                expressionTypes,
                                internalMembers,
                                callGraph);
                            break;

                        default:
                            // Parameters, locals, and nested type symbols are
                            // not classifiable members. Skip.
                            break;
                    }
                }

                if (member.Kind is SymbolKind.Method or SymbolKind.Constructor)
                    genericMethods.Add(member);
            }

            if (genericMethods.Count > 0)
                perGenericMethods.Add((genericMethods, typeParameters));
        }

        // Task 19: propagate WritesT classifications through the call graph
        // until no further changes. Runs once across the whole dictionary —
        // every internal callee recorded during Task 18's walk now carries a
        // resolved <see cref="CallSite.Callee"/> symbol, and the loop
        // promotes callers whose internal callees are already WritesT.
        RunFixedPoint(methodClassifications, callGraph, perGenericMethods);

        return new ReachabilityMap(methodClassifications, fieldClassifications, propertyClassifications, callGraph);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Task 19: fixed-point iteration over the call graph.
    //
    // The seed classification is the Task-18 intrinsic rule output
    // (already stored in <paramref name="methodClassifications"/>). The
    // loop promotes a caller to WritesT whenever any of its resolved
    // internal callees is WritesT w.r.t. the same type parameter. Because
    // the lattice has height 2 and we only ever move upward, the total
    // number of (member, T) promotions is bounded by |members| × |Ts|.
    //
    // The iteration is scoped per generic type so that type-parameter
    // instances from two unrelated generics are never confused: the key
    // comparer treats type parameters with the same name+ordinal as
    // equal, so without per-generic scoping a member of G1 could appear
    // to "promote" through a same-name type parameter of G2. Grouping by
    // generic eliminates that risk by construction.
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs the fixed-point propagation loop. For every (member, T) key in
    /// <paramref name="methodClassifications"/>, if any resolved internal
    /// callee of the member is currently classified
    /// <see cref="MemberClassification.WritesT"/> with respect to T, the
    /// member is promoted to <see cref="MemberClassification.WritesT"/>.
    /// Iterates until a full pass produces no change.
    /// </summary>
    /// <remarks>
    /// Lattice height is 2, so termination is bounded by
    /// <c>|(member, T) pairs|</c> iterations in the worst case; in practice
    /// it is typically 1 – 2 passes for real code.
    /// </remarks>
    internal static void RunFixedPoint(
        Dictionary<MemberTypeParamKey, MemberClassification> methodClassifications,
        Dictionary<ISymbol, MemberCallInfo> callGraph,
        IReadOnlyList<(List<ISymbol> Members, IReadOnlyList<IType> TypeParameters)> perGenericMethods)
    {
        bool changed;
        do
        {
            changed = false;

            foreach (var (members, typeParameters) in perGenericMethods)
            {
                foreach (var member in members)
                {
                    if (!callGraph.TryGetValue(member, out var info))
                        continue;

                    foreach (var typeParameter in typeParameters)
                    {
                        var key = new MemberTypeParamKey(member, typeParameter);
                        if (!methodClassifications.TryGetValue(key, out var current))
                            continue;
                        if (current == MemberClassification.WritesT)
                            continue; // Already at the top of the lattice.

                        foreach (var callSite in info.CallSites)
                        {
                            if (!callSite.IsInternal || callSite.Callee is null)
                                continue;

                            var calleeKey = new MemberTypeParamKey(callSite.Callee, typeParameter);
                            if (methodClassifications.TryGetValue(calleeKey, out var calleeClass)
                                && calleeClass == MemberClassification.WritesT)
                            {
                                methodClassifications[key] = MemberClassification.WritesT;
                                changed = true;
                                break;
                            }
                        }
                    }
                }
            }
        }
        while (changed);
    }

    /// <summary>
    /// Runs a single iteration (step) of the fixed-point propagation for
    /// every (member, T) key scoped to the supplied per-generic groupings.
    /// Returns <c>true</c> if any classification was promoted. This entry
    /// point is exposed for Task 19's property-based test, which needs to
    /// observe the fixed-point state at each iteration to assert
    /// monotonicity.
    /// </summary>
    internal static bool RunFixedPointStep(
        Dictionary<MemberTypeParamKey, MemberClassification> methodClassifications,
        Dictionary<ISymbol, MemberCallInfo> callGraph,
        IReadOnlyList<(List<ISymbol> Members, IReadOnlyList<IType> TypeParameters)> perGenericMethods)
    {
        var changed = false;

        foreach (var (members, typeParameters) in perGenericMethods)
        {
            foreach (var member in members)
            {
                if (!callGraph.TryGetValue(member, out var info))
                    continue;

                foreach (var typeParameter in typeParameters)
                {
                    var key = new MemberTypeParamKey(member, typeParameter);
                    if (!methodClassifications.TryGetValue(key, out var current))
                        continue;
                    if (current == MemberClassification.WritesT)
                        continue;

                    foreach (var callSite in info.CallSites)
                    {
                        if (!callSite.IsInternal || callSite.Callee is null)
                            continue;

                        var calleeKey = new MemberTypeParamKey(callSite.Callee, typeParameter);
                        if (methodClassifications.TryGetValue(calleeKey, out var calleeClass)
                            && calleeClass == MemberClassification.WritesT)
                        {
                            methodClassifications[key] = MemberClassification.WritesT;
                            changed = true;
                            break;
                        }
                    }
                }
            }
        }

        return changed;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Field classification (Task 17.2). Unchanged from Task 17.
    // ─────────────────────────────────────────────────────────────────────

    private static MemberClassification ClassifyField(ISymbol field, IType typeParameter)
    {
        var declaredType = field.DeclaredType;
        if (declaredType is null or ErrorType)
            return MemberClassification.DoesNotWriteT;

        return TypeContainment.ContainsTAtStoragePosition(declaredType, typeParameter)
            ? MemberClassification.WritesT
            : MemberClassification.DoesNotWriteT;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Property / indexer classification (Tasks 17.3–17.4). Unchanged from
    // Task 17; Task 18 does not walk accessor bodies in this version
    // because accessor bodies are not yet modeled as MethodDeclarationNode
    // subtrees — block accessors are stored on the PropertyInfo. Refining
    // non-auto accessor classification to use body-walk would require
    // exposing the accessor's block node through the Symbol model, which
    // is out of scope for Task 18. Block-bodied accessors therefore remain
    // conservatively DoesNotWriteT on both read and write.
    // ─────────────────────────────────────────────────────────────────────

    private static PropertyClassification ClassifyPropertyOrIndexer(ISymbol member, IType typeParameter)
    {
        var readClassification = MemberClassification.DoesNotWriteT;

        var declaredType = member.DeclaredType;
        if (declaredType is null or ErrorType)
            return new PropertyClassification(readClassification, MemberClassification.DoesNotWriteT);

        var metadata = (member as Symbol)?.PropertyMetadata;

        var hasAutoWrite =
            metadata is not null &&
            (IsAutoAccessor(metadata.SetAccessor) || IsAutoAccessor(metadata.InitAccessor));

        MemberClassification writeClassification;
        if (hasAutoWrite)
        {
            writeClassification = TypeContainment.ContainsTAtStoragePosition(declaredType, typeParameter)
                ? MemberClassification.WritesT
                : MemberClassification.DoesNotWriteT;
        }
        else
        {
            writeClassification = MemberClassification.DoesNotWriteT;
        }

        return new PropertyClassification(readClassification, writeClassification);
    }

    private static bool IsAutoAccessor(AccessorInfo? accessor)
        => accessor is not null && accessor.BodyKind == AccessorBodyKind.Auto;

    // ─────────────────────────────────────────────────────────────────────
    // Method / constructor classification (Task 18).
    //
    // Algorithm per (member, T):
    //   1. Locate the member's declaring AST node; walk its body if present.
    //   2. Collect write-sites (direct assignments into T-typed storage).
    //   3. Collect call sites (resolved internal, external, dynamic).
    //   4. For a constructor, also check whether any parameter type
    //      contains T at a storage position (Req 9.7 parameter-type rule).
    //   5. Intrinsic classification:
    //        WritesT if (4) or any direct write or any external/dynamic
    //                   call with a T-containing argument.
    //        else DoesNotWriteT (lattice bottom, Task 19 may promote via
    //                           static-internal call propagation).
    //
    // Results are stored in the method classification map AND the call
    // graph (for Task 19 consumption). The call graph for a given member
    // is built once on first visit and reused across all T iterations via
    // the `callGraph` dictionary.
    // ─────────────────────────────────────────────────────────────────────

    private static MemberClassification ClassifyMethodOrConstructor(
        ISymbol member,
        IType typeParameter,
        SymbolTable symbols,
        IReadOnlyDictionary<SyntaxNode, IType> expressionTypes,
        HashSet<ISymbol> internalMembers,
        Dictionary<ISymbol, MemberCallInfo> callGraph)
    {
        // The call graph entry is independent of T — write-sites and call
        // sites depend only on the body text. We build it lazily once per
        // member and reuse it across T iterations.
        if (!callGraph.TryGetValue(member, out var callInfo))
        {
            callInfo = BuildMemberCallInfo(member, symbols, expressionTypes, internalMembers);
            callGraph[member] = callInfo;
        }

        return IntrinsicClassification(callInfo, member, typeParameter, symbols);
    }

    /// <summary>
    /// Constructs the <see cref="MemberCallInfo"/> for a method or
    /// constructor symbol: walks the body (if any), collecting direct
    /// write-site target types and call sites.
    /// </summary>
    private static MemberCallInfo BuildMemberCallInfo(
        ISymbol member,
        SymbolTable symbols,
        IReadOnlyDictionary<SyntaxNode, IType> expressionTypes,
        HashSet<ISymbol> internalMembers)
    {
        var writeSiteTypes = new List<IType>();
        var callSites = new List<CallSite>();

        var declaringNode = symbols.GetDeclaringNode(member);
        if (declaringNode is null)
            return new MemberCallInfo(writeSiteTypes, callSites);

        // Locate the body block and (for constructors) the chain.
        var (body, chain) = GetBodyAndChain(declaringNode);

        if (body is not null)
        {
            WalkNode(body, expressionTypes, internalMembers, writeSiteTypes, callSites);
        }

        if (chain is not null)
        {
            // A `this(...)` or `base(...)` initializer is a call into
            // another constructor. For Task 18 we record it as an internal
            // call when it's `this(...)` (same-type), and as external-with-
            // args otherwise (`base(...)` into a possibly-different-generic
            // ancestor). Task 19 will propagate WritesT through `this(...)`.
            RecordConstructorChain(chain, expressionTypes, internalMembers, callSites);
        }

        return new MemberCallInfo(writeSiteTypes, callSites);
    }

    /// <summary>
    /// Computes the intrinsic (non-propagated) classification for a member
    /// against a specific type parameter, applying the Task 18 rules
    /// spelled out in Requirements 9.5, 9.7, 9.8, 9.9.
    /// </summary>
    private static MemberClassification IntrinsicClassification(
        MemberCallInfo callInfo,
        ISymbol member,
        IType typeParameter,
        SymbolTable symbols)
    {
        // Req 9.7 (constructor parameter rule): if any constructor
        // parameter's type contains T at a storage position, the
        // constructor is WritesT w.r.t. T.
        if (member.Kind == SymbolKind.Constructor)
        {
            var paramTypes = ParameterSignature.Extract(member, symbols);
            foreach (var paramType in paramTypes)
            {
                if (paramType is not null && paramType is not ErrorType
                    && TypeContainment.ContainsTAtStoragePosition(paramType, typeParameter))
                {
                    return MemberClassification.WritesT;
                }
            }
        }

        // Req 9.5 (direct write-site rule): any assignment whose target's
        // declared type contains T at a storage position is a Write_To_T.
        foreach (var writeTargetType in callInfo.WriteSiteTargetTypes)
        {
            if (TypeContainment.ContainsTAtStoragePosition(writeTargetType, typeParameter))
                return MemberClassification.WritesT;
        }

        // Req 9.8 / 9.9 (external / dynamic-dispatch rule): any call
        // whose callee is external or dynamically dispatched is WritesT
        // iff any argument's declared type contains T at a storage
        // position.
        foreach (var callSite in callInfo.CallSites)
        {
            if (callSite.IsInternal)
                continue; // Task 19 propagation, not a Task 18 intrinsic write.

            foreach (var argType in callSite.ArgumentTypes)
            {
                if (argType is not null && argType is not ErrorType
                    && TypeContainment.ContainsTAtStoragePosition(argType, typeParameter))
                {
                    return MemberClassification.WritesT;
                }
            }
        }

        return MemberClassification.DoesNotWriteT;
    }

    // ─────────────────────────────────────────────────────────────────────
    // AST discovery helpers.
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts the method/constructor body block and, for a constructor,
    /// the <c>: this(...)</c> / <c>: base(...)</c> chain. Returns
    /// (null, null) for bodyless (intrinsic / abstract) declarations.
    /// </summary>
    private static (BlockNode? Body, ConstructorChainNode? Chain) GetBodyAndChain(SyntaxNode declaringNode)
    {
        return declaringNode switch
        {
            MethodDeclarationNode m => (m.Body.GetBlock(), null),
            ConstructorDeclarationNode c => (c.Body.GetBlock(), c.Chain),
            _ => (null, null),
        };
    }

    /// <summary>
    /// Depth-first walk of a syntax subtree. Detects assignment nodes and
    /// call suffixes and records their contribution to the write-site set
    /// or the call graph.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The walker is generic over <see cref="SyntaxNode"/> so that it
    /// transparently descends through every statement, expression, and
    /// suffix kind — it does not need a per-kind case for nodes it does not
    /// classify. The specific nodes that contribute to classification are
    /// recognized by type test at visit time.
    /// </para>
    /// <para>
    /// For <see cref="PostfixNode"/> the walker handles the whole node in
    /// one step rather than visiting individual suffixes in isolation —
    /// Task 19's internal-callee resolution needs to inspect a suffix
    /// together with its preceding target/suffix, which is only available
    /// at this level. The walker still descends into the target and the
    /// argument lists to catch nested calls and assignments.
    /// </para>
    /// </remarks>
    private static void WalkNode(
        SyntaxNode node,
        IReadOnlyDictionary<SyntaxNode, IType> expressionTypes,
        HashSet<ISymbol> internalMembers,
        List<IType> writeSiteTargetTypes,
        List<CallSite> callSites)
    {
        switch (node)
        {
            case AssignmentNode assignment when assignment.Value is not null:
            {
                // Simple and compound assignments: LHS is Target (a
                // LogicalOrNode tree whose effective expression type is
                // the storage slot type). If the Target's type contains
                // T at a storage position, this is a Write_To_T.
                if (expressionTypes.TryGetValue(assignment.Target, out var lhsType)
                    && lhsType is not null && lhsType is not ErrorType)
                {
                    writeSiteTargetTypes.Add(lhsType);
                }
                break; // fall through to children walk below
            }

            case PostfixNode postfix:
                WalkPostfix(postfix, expressionTypes, internalMembers, writeSiteTargetTypes, callSites);
                return; // WalkPostfix handles descent into target + arguments itself.

            case ObjectCreationExpressionPrimaryNode objectCreation:
                // `new Foo(...)` is a primary (usable as a Postfix target).
                // Record it as an external call (same rule as before Task
                // 19: we do not resolve constructor callees internally —
                // that would require overload resolution against the
                // class's constructor list, which is out of scope here).
                RecordObjectCreation(objectCreation, expressionTypes, callSites);
                break;
        }

        foreach (var child in node.Children())
        {
            if (child is null)
                continue; // Generator yields unchecked `yield return Value;` for
                          // nodes like AssignmentNode where Value is optional.
            WalkNode(child, expressionTypes, internalMembers, writeSiteTargetTypes, callSites);
        }
    }

    /// <summary>
    /// Walks a <see cref="PostfixNode"/>: a primary target followed by a
    /// chain of suffixes (member access, method call, index, invocation).
    /// The walker inspects the target + first-suffix pair to identify
    /// same-type internal calls (<c>this.M(...)</c> or unqualified
    /// <c>M(...)</c>), then descends into any nested expressions (indexes
    /// and argument lists) to catch writes and calls that appear inside
    /// the suffix chain itself.
    /// </summary>
    private static void WalkPostfix(
        PostfixNode postfix,
        IReadOnlyDictionary<SyntaxNode, IType> expressionTypes,
        HashSet<ISymbol> internalMembers,
        List<IType> writeSiteTargetTypes,
        List<CallSite> callSites)
    {
        // Descend into the target's own subtree. The target may be a
        // ParenthesizedExpression containing further assignments/calls.
        // Do not treat the target itself as a call (we process suffixes
        // below).
        WalkNode(postfix.Target, expressionTypes, internalMembers, writeSiteTargetTypes, callSites);

        // Walk the suffix chain. Only the FIRST suffix can be an internal
        // same-type call because subsequent suffixes operate on the
        // return value of their preceding call and therefore have a
        // receiver whose type is not the enclosing generic's `this` type.
        for (var i = 0; i < postfix.Suffix.Length; i++)
        {
            var suffix = postfix.Suffix[i];

            switch (suffix)
            {
                case MethodCallSuffixPostfixSuffixNode methodCall:
                {
                    // For an internal resolution we need the receiver of
                    // the call. The receiver is the node *preceding* this
                    // suffix: either the Postfix.Target (when i == 0) or
                    // the previous suffix's result (when i > 0). We only
                    // try to resolve internal when the receiver is
                    // literally `this` and this is the first suffix.
                    var isFirstSuffix = i == 0;
                    var receiverIsThis = isFirstSuffix && postfix.Target is ThisExpressionPrimaryNode;

                    RecordMethodCall(
                        methodCall,
                        expressionTypes,
                        internalMembers,
                        callSites,
                        receiverIsThis);
                    break;
                }

                case InvocationSuffixPostfixSuffixNode invocation:
                {
                    // Bare `foo(...)` on an identifier target. We try to
                    // resolve `foo` against the enclosing generic's
                    // members when the postfix has exactly one suffix
                    // (no chain) and the target is a simple identifier
                    // — the C# analogue of an unqualified method call
                    // inside a non-static body, which implicitly binds
                    // to `this.foo`.
                    var isFirstSuffix = i == 0;
                    var identifier = isFirstSuffix ? postfix.Target as IdentifierExpressionPrimaryNode : null;

                    RecordInvocation(
                        invocation,
                        expressionTypes,
                        internalMembers,
                        callSites,
                        identifier);
                    break;
                }

                default:
                    // Member access, index, etc. Descend into the suffix's
                    // children to catch nested expressions (e.g.,
                    // `arr[Foo()].Bar` — the argument to Foo is still
                    // visited via descent into the IndexSuffix).
                    foreach (var child in suffix.Children())
                    {
                        if (child is null)
                            continue;
                        WalkNode(child, expressionTypes, internalMembers, writeSiteTargetTypes, callSites);
                    }
                    continue;
            }

            // For call-shaped suffixes we walk the argument list so that
            // nested assignments/calls inside arguments still contribute
            // to the caller's write-site / call-site set.
            foreach (var child in suffix.Children())
            {
                if (child is null)
                    continue;
                WalkNode(child, expressionTypes, internalMembers, writeSiteTargetTypes, callSites);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Call-site recording helpers.
    //
    // Task 18 distinguishes three call-site kinds:
    //   1. Internal static — resolved to a member of the same generic type.
    //      Recorded but not propagated by Task 18. Task 19 iterates.
    //   2. External static — resolved to a symbol outside the same generic.
    //      Classified WritesT iff any argument's type contains T at a
    //      storage position.
    //   3. Dynamic — unresolved (virtual dispatch, trait satisfaction,
    //      type-argument-dependent dispatch). Same rule as external.
    //
    // The callee resolution strategy follows the same pattern as
    // TypeCheckerAnalyzer.FindMemberSymbol: look up the member by name on
    // the receiver's static type, unwrapping ConstructedType to its generic
    // definition so that `List<int>.Add` resolves to `List<T>.Add`.
    // ─────────────────────────────────────────────────────────────────────

    private static void RecordMethodCall(
        MethodCallSuffixPostfixSuffixNode methodCall,
        IReadOnlyDictionary<SyntaxNode, IType> expressionTypes,
        HashSet<ISymbol> internalMembers,
        List<CallSite> callSites,
        bool receiverIsThis)
    {
        var argumentTypes = CollectArgumentTypes(methodCall.Arguments, expressionTypes);

        // Task 19: when the receiver is literally `this` and this suffix
        // is the first in the chain, look up the called name against the
        // enclosing generic's member scope. Matching members are recorded
        // as internal; anything else falls back to Task 18's conservative
        // external/dynamic-dispatch semantics.
        if (receiverIsThis)
        {
            var calleeName = methodCall.Name.Lexeme;
            var callee = ResolveInternalMember(internalMembers, calleeName);
            if (callee is not null)
            {
                callSites.Add(new CallSite(
                    Callee: callee,
                    IsInternal: true,
                    IsDynamic: false,
                    ArgumentTypes: argumentTypes));
                return;
            }
        }

        // External or unresolved dispatch: Req 9.8 / 9.9 conservative rule.
        callSites.Add(new CallSite(
            Callee: null,
            IsInternal: false,
            IsDynamic: true,
            ArgumentTypes: argumentTypes));
    }

    private static void RecordInvocation(
        InvocationSuffixPostfixSuffixNode invocation,
        IReadOnlyDictionary<SyntaxNode, IType> expressionTypes,
        HashSet<ISymbol> internalMembers,
        List<CallSite> callSites,
        IdentifierExpressionPrimaryNode? identifierTarget)
    {
        var argumentTypes = CollectArgumentTypes(invocation.Arguments, expressionTypes);

        // Task 19: when the invocation's enclosing postfix is a bare
        // `foo(...)` with a simple identifier target, try to resolve
        // `foo` against the enclosing generic's member scope. This
        // models the common Mist pattern of calling a sibling method
        // without the `this.` prefix.
        if (identifierTarget is not null)
        {
            var calleeName = identifierTarget.Name.Lexeme;
            var callee = ResolveInternalMember(internalMembers, calleeName);
            if (callee is not null)
            {
                callSites.Add(new CallSite(
                    Callee: callee,
                    IsInternal: true,
                    IsDynamic: false,
                    ArgumentTypes: argumentTypes));
                return;
            }
        }

        // External or unresolved bare invocation.
        callSites.Add(new CallSite(
            Callee: null,
            IsInternal: false,
            IsDynamic: true,
            ArgumentTypes: argumentTypes));
    }

    /// <summary>
    /// Looks up a callee name in the enclosing generic's member scope.
    /// Returns the first method or constructor member with the matching
    /// name, or <c>null</c> if no matching internal member exists.
    /// Overload resolution is intentionally coarse: Task 19's fixed-point
    /// iteration only needs to know "is there *any* same-type method of
    /// this name that is classified WritesT?", so picking the first
    /// matching overload is sound — if multiple overloads exist and any
    /// one is WritesT the caller becomes WritesT, which matches the
    /// semantic intent of conservative propagation.
    /// </summary>
    private static ISymbol? ResolveInternalMember(HashSet<ISymbol> internalMembers, string name)
    {
        foreach (var member in internalMembers)
        {
            if (member.Kind is not (SymbolKind.Method or SymbolKind.Constructor))
                continue;
            if (member.Name == name)
                return member;
        }
        return null;
    }

    private static void RecordObjectCreation(
        ObjectCreationExpressionPrimaryNode objectCreation,
        IReadOnlyDictionary<SyntaxNode, IType> expressionTypes,
        List<CallSite> callSites)
    {
        // `new Foo(...)` invokes Foo's constructor with the given
        // arguments. For Task 18, treat this as an external call — we do
        // not have the receiver's Symbol available here without repeating
        // the name-resolution pass. This means the constructor's own
        // classification does not propagate in Task 18 (Task 19 would need
        // the callee symbol to propagate); we fall back on the
        // external-args rule, which is sound.
        var argumentTypes = CollectArgumentTypes(objectCreation.Arguments, expressionTypes);
        callSites.Add(new CallSite(
            Callee: null,
            IsInternal: false,
            IsDynamic: true,
            ArgumentTypes: argumentTypes));
    }

    /// <summary>
    /// Records a <c>: this(...)</c> or <c>: base(...)</c> constructor
    /// initializer as a call site. `this(...)` is conservatively recorded
    /// with a null callee — resolving it statically would require overload
    /// resolution against the containing class's constructor list, which is
    /// out of scope for Task 18. Under the external/dynamic rule the
    /// classification still correctly fires when the passed arguments
    /// contain T at a storage position.
    /// </summary>
    private static void RecordConstructorChain(
        ConstructorChainNode chain,
        IReadOnlyDictionary<SyntaxNode, IType> expressionTypes,
        HashSet<ISymbol> internalMembers,
        List<CallSite> callSites)
    {
        ArgumentListNode? arguments = chain switch
        {
            ThisChainConstructorChainNode t => t.Arguments,
            BaseChainConstructorChainNode b => b.Arguments,
            _ => null,
        };

        var argumentTypes = CollectArgumentTypes(arguments, expressionTypes);
        callSites.Add(new CallSite(
            Callee: null,
            IsInternal: false,
            IsDynamic: true,
            ArgumentTypes: argumentTypes));
    }

    /// <summary>
    /// Extracts the expression types of every argument in an
    /// <see cref="ArgumentListNode"/>. Falls back to an empty list for a
    /// null argument list or when no expression-type entries are recorded.
    /// </summary>
    private static IReadOnlyList<IType> CollectArgumentTypes(
        ArgumentListNode? arguments,
        IReadOnlyDictionary<SyntaxNode, IType> expressionTypes)
    {
        if (arguments is null)
            return Array.Empty<IType>();

        var result = new List<IType>();

        // The grammar's ArgumentList rule is `Expression (COMMA Expression)*`
        // which the source generator flattens into a first `Expression`
        // property plus a `.Expressions` ImmutableArray. Walk Children()
        // to collect them in order without relying on the generator's
        // naming convention.
        foreach (var child in arguments.Children())
        {
            if (child is ExpressionNode expr
                && expressionTypes.TryGetValue(expr, out var type)
                && type is not null)
            {
                result.Add(type);
            }
        }

        return result;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Generic-type discovery helpers.
    // ─────────────────────────────────────────────────────────────────────

    private static (IScope? Scope, IReadOnlyList<IType>? TypeParameters) GetGenericTypeInfo(ISymbol typeSymbol)
    {
        return typeSymbol.Kind switch
        {
            SymbolKind.Class when typeSymbol.DeclaredType is ClassType c => (c.MemberScope, c.TypeParameters),
            SymbolKind.Struct when typeSymbol.DeclaredType is StructType s => (s.MemberScope, s.TypeParameters),
            SymbolKind.Interface when typeSymbol.DeclaredType is InterfaceType i => (i.MemberScope, i.TypeParameters),
            _ => (null, null),
        };
    }

    // ─────────────────────────────────────────────────────────────────────
    // Internal data structures — per-member call graph consumed by Task 19.
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A call site observed in a member body. Records the callee (null for
    /// unresolved dynamic/external), whether the callee is a sibling of the
    /// caller within the same generic type (<see cref="IsInternal"/>),
    /// whether the dispatch is dynamic, and the argument types for the
    /// external/dynamic-dispatch rule (Req 9.8/9.9).
    /// </summary>
    /// <remarks>
    /// Prior to Task 19 every resolvable call was recorded as non-internal
    /// because the walker did not thread receiver-type context; Task 19
    /// refines resolution for two common shapes:
    /// <list type="bullet">
    /// <item><description>
    /// <c>this.M(...)</c> — receiver is the <see cref="ThisExpressionPrimaryNode"/>
    /// of the first suffix in the postfix; <c>M</c> is looked up in the
    /// enclosing generic's member scope.
    /// </description></item>
    /// <item><description>
    /// Unqualified <c>M(...)</c> — the postfix has a single invocation
    /// suffix applied to an <see cref="IdentifierExpressionPrimaryNode"/>
    /// whose name is a member of the enclosing generic.
    /// </description></item>
    /// </list>
    /// All other calls (member access on a non-<c>this</c> receiver,
    /// <c>base.M(...)</c>, constructor invocations via <c>new</c>,
    /// constructor chains, calls through non-identifier expressions)
    /// remain conservative dynamic/external per Task 18's rule.
    /// </remarks>
    internal readonly record struct CallSite(
        ISymbol? Callee,
        bool IsInternal,
        bool IsDynamic,
        IReadOnlyList<IType> ArgumentTypes);

    /// <summary>
    /// Per-member call-graph entry. Not exposed through
    /// <see cref="IReachabilityMap"/> — consumed internally by Task 19's
    /// fixed-point iteration.
    /// </summary>
    internal sealed class MemberCallInfo
    {
        public IReadOnlyList<IType> WriteSiteTargetTypes { get; }
        public IReadOnlyList<CallSite> CallSites { get; }

        public MemberCallInfo(IReadOnlyList<IType> writeSiteTargetTypes, IReadOnlyList<CallSite> callSites)
        {
            WriteSiteTargetTypes = writeSiteTargetTypes;
            CallSites = callSites;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Classification-map key structure. Unchanged from Task 17, but exposed
    // as <c>internal</c> so Task 19's property test can construct keys when
    // assembling synthetic classification-dictionary snapshots.
    // ─────────────────────────────────────────────────────────────────────

    internal readonly record struct MemberTypeParamKey(ISymbol Member, IType TypeParameter)
    {
        public static IEqualityComparer<MemberTypeParamKey> Comparer { get; } = new KeyComparer();

        private sealed class KeyComparer : IEqualityComparer<MemberTypeParamKey>
        {
            public bool Equals(MemberTypeParamKey x, MemberTypeParamKey y)
            {
                if (!ReferenceEquals(x.Member, y.Member)) return false;
                var (xName, xOrdinal) = GetNameAndOrdinal(x.TypeParameter);
                var (yName, yOrdinal) = GetNameAndOrdinal(y.TypeParameter);
                return xName == yName && xOrdinal == yOrdinal;
            }

            public int GetHashCode(MemberTypeParamKey obj)
            {
                var (name, ordinal) = GetNameAndOrdinal(obj.TypeParameter);
                return HashCode.Combine(
                    System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj.Member),
                    name,
                    ordinal);
            }

            private static (string Name, int Ordinal) GetNameAndOrdinal(IType tp) => tp switch
            {
                TypeParameterType tpt => (tpt.Name, tpt.Ordinal),
                DependentTypeParameterType dpt => (dpt.Name, dpt.Ordinal),
                _ => (tp.DisplayName, 0),
            };
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Concrete IReachabilityMap backing type. Extends Task 17's version
    // with an internal call-graph dictionary for Task 19 to consume.
    // ─────────────────────────────────────────────────────────────────────

    private sealed class ReachabilityMap : IReachabilityMap
    {
        private readonly IReadOnlyDictionary<MemberTypeParamKey, MemberClassification> _methods;
        private readonly IReadOnlyDictionary<MemberTypeParamKey, MemberClassification> _fields;
        private readonly IReadOnlyDictionary<MemberTypeParamKey, PropertyClassification> _properties;

        public ReachabilityMap(
            IReadOnlyDictionary<MemberTypeParamKey, MemberClassification> methods,
            IReadOnlyDictionary<MemberTypeParamKey, MemberClassification> fields,
            IReadOnlyDictionary<MemberTypeParamKey, PropertyClassification> properties,
            IReadOnlyDictionary<ISymbol, MemberCallInfo> callGraph)
        {
            _methods = methods;
            _fields = fields;
            _properties = properties;
            CallGraph = callGraph;
        }

        /// <summary>
        /// Per-member call-graph (caller → list of <see cref="CallSite"/>s +
        /// its direct write-site target types). Consumed by Task 19's
        /// fixed-point iteration; not part of the public
        /// <see cref="IReachabilityMap"/> contract.
        /// </summary>
        internal IReadOnlyDictionary<ISymbol, MemberCallInfo> CallGraph { get; }

        public MemberClassification GetMethodClassification(ISymbol member, IType typeParameter)
        {
            ArgumentNullException.ThrowIfNull(member);
            ArgumentNullException.ThrowIfNull(typeParameter);
            if (member.Kind is not (SymbolKind.Method or SymbolKind.Constructor))
            {
                throw new ArgumentException(
                    $"GetMethodClassification expected a Method or Constructor symbol, got {member.Kind}.",
                    nameof(member));
            }
            return Lookup(_methods, member, typeParameter);
        }

        public MemberClassification GetFieldClassification(ISymbol member, IType typeParameter)
        {
            ArgumentNullException.ThrowIfNull(member);
            ArgumentNullException.ThrowIfNull(typeParameter);
            if (member.Kind != SymbolKind.Field)
            {
                throw new ArgumentException(
                    $"GetFieldClassification expected a Field symbol, got {member.Kind}.",
                    nameof(member));
            }
            return Lookup(_fields, member, typeParameter);
        }

        public PropertyClassification GetPropertyClassification(ISymbol member, IType typeParameter)
        {
            ArgumentNullException.ThrowIfNull(member);
            ArgumentNullException.ThrowIfNull(typeParameter);
            if (member.Kind is not (SymbolKind.Property or SymbolKind.Indexer))
            {
                throw new ArgumentException(
                    $"GetPropertyClassification expected a Property or Indexer symbol, got {member.Kind}.",
                    nameof(member));
            }
            return Lookup(_properties, member, typeParameter);
        }

        private static TValue Lookup<TValue>(
            IReadOnlyDictionary<MemberTypeParamKey, TValue> dict,
            ISymbol member,
            IType typeParameter)
        {
            var key = new MemberTypeParamKey(member, typeParameter);
            if (dict.TryGetValue(key, out var value))
                return value;
            throw new KeyNotFoundException(
                $"No classification recorded for member '{member.Name}' ({member.Kind}) against type parameter '{typeParameter.DisplayName}'. " +
                "The member may not belong to a generic type declaration, or the wrong type parameter was supplied.");
        }
    }
}
