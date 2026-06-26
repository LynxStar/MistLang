using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Backends.CSharpTranspiler;

/// <summary>
/// Computes which Mist methods require the C# <c>async</c> keyword in emitted code.
///
/// In C#, <c>await</c> can only appear inside an <c>async</c> method. But in Mist,
/// a sync method can call async methods (with auto-unwrap) or use explicit <c>await</c>.
/// This pass determines which methods need the C# <c>async</c> annotation by:
///
/// <list type="number">
///   <item>Seeding: methods declared <c>async</c> in Mist.</item>
///   <item>Seeding: sync methods that directly invoke async methods (triggering auto-unwrap).</item>
///   <item>Seeding: sync methods that use explicit <c>await</c>.</item>
///   <item>Seeding: sync methods that contain async functor expressions (<c>async SyncMethod()</c>),
///         which produce <c>Task&lt;T&gt;</c>-typed locals requiring scope-exit await.</item>
///   <item>Propagating transitively: if method A needs C# async, then any sync method
///         that calls A also needs C# async (because A's C# signature returns Task&lt;T&gt;).</item>
/// </list>
///
/// The propagation is computed as a fixed-point over the call graph.
/// Results are stored on each method symbol's <see cref="Symbol.NeedsCSharpAsync"/> property.
/// </summary>
internal static class NeedsAsyncPropagationPass
{
    /// <summary>
    /// Runs the needs-async propagation pass over the semantic model.
    /// After this method returns, every method symbol that requires the C#
    /// <c>async</c> keyword will have <see cref="Symbol.NeedsCSharpAsync"/> set to <c>true</c>.
    /// </summary>
    public static void Run(ISemanticModel model)
    {
        // Step 1: Collect all method symbols and their declaring AST nodes.
        var methodSymbols = new List<Symbol>();
        var symbolToNode = new Dictionary<Symbol, Storm.Runtime.SyntaxNode>();

        foreach(var symbol in model.AllSymbols)
        {
            if(symbol is not Symbol sym)
                continue;
            if(sym.Kind != SymbolKind.Method)
                continue;

            Storm.Runtime.SyntaxNode? declaringNode;
            try { declaringNode = model.GetDeclaringNode(sym); }
            catch { continue; }

            if(declaringNode is null)
                continue;

            methodSymbols.Add(sym);
            symbolToNode[sym] = declaringNode;
        }

        // Step 2: Seed — mark methods that directly need C# async.
        // Also build a call graph: for each method, record which other method symbols it calls.
        var callerToCallees = new Dictionary<Symbol, HashSet<Symbol>>();

        foreach(var method in methodSymbols)
        {
            // Seed rule 1: methods declared async in Mist always need C# async.
            if(method.IsAsync)
            {
                method.NeedsCSharpAsync = true;
            }

            var declaringNode = symbolToNode[method];
            var block = GetMethodBodyBlock(declaringNode);
            if(block is null)
                continue;

            // Walk the method body to find await expressions, async method calls,
            // and all method invocations (for the call graph).
            var callees = new HashSet<Symbol>();
            var needsAsync = method.NeedsCSharpAsync;

            WalkMethodBody(block, model, method, ref needsAsync, callees);

            method.NeedsCSharpAsync = needsAsync;
            if(callees.Count > 0)
            {
                callerToCallees[method] = callees;
            }
        }

        // Seed rule 5: Drop() methods on types with task-like droppable fields.
        // The emitter's EmitRemainingFieldDropsIfManualDrop now skips task-like fields
        // (Task<T>, ValueTask<T>) — their "drop" (await) is intentionally omitted when
        // the user's manual Drop() doesn't explicitly handle them. Therefore we no longer
        // need to mark Drop() as async based solely on having task-like fields.
        // This rule is now disabled since the emitter skips task-like field auto-drops.

        // Step 3: Build reverse call graph (callee → callers).
        var calleeToCallers = new Dictionary<Symbol, List<Symbol>>();
        foreach(var (caller, callees) in callerToCallees)
        {
            foreach(var callee in callees)
            {
                if(!calleeToCallers.TryGetValue(callee, out var callers))
                {
                    callers = new List<Symbol>();
                    calleeToCallers[callee] = callers;
                }
                callers.Add(caller);
            }
        }

        // Step 4: Fixed-point propagation.
        // If a callee needs C# async, then any caller that invokes it (and is not
        // already marked) also needs C# async — because the callee's C# return type
        // is now Task<T>, requiring await at the call site in the emitted C#.
        var worklist = new Queue<Symbol>();
        foreach(var method in methodSymbols)
        {
            if(method.NeedsCSharpAsync)
            {
                worklist.Enqueue(method);
            }
        }

        while(worklist.Count > 0)
        {
            var callee = worklist.Dequeue();

            if(!calleeToCallers.TryGetValue(callee, out var callers))
                continue;

            foreach(var caller in callers)
            {
                if(caller.NeedsCSharpAsync)
                    continue;

                // The caller invokes a method that needs C# async.
                // The caller will need to emit `await` for that call, so it
                // also needs C# async.
                caller.NeedsCSharpAsync = true;
                worklist.Enqueue(caller);
            }
        }
    }

    /// <summary>
    /// Extracts the body block from a method declaration AST node.
    /// Returns null for bodyless methods (intrinsic, abstract type signatures).
    /// </summary>
    private static BlockNode? GetMethodBodyBlock(Storm.Runtime.SyntaxNode declaringNode)
    {
        return declaringNode switch
        {
            MethodDeclarationNode methodDecl => methodDecl.Body.GetBlock(),
            ConstructorDeclarationNode ctorDecl => ctorDecl.Body.GetBlock(),
            _ => null,
        };
    }

    /// <summary>
    /// Walks a method body AST looking for:
    /// <list type="bullet">
    ///   <item><c>AwaitExpressionUnaryNode</c> — the method uses explicit await (seed rule 3).</item>
    ///   <item><c>AsyncExpressionUnaryNode</c> — the method uses an async functor expression (seed rule 4).</item>
    ///   <item>Direct invocations of async methods in sync context (seed rule 2).</item>
    ///   <item>All method invocations — recorded in <paramref name="callees"/> for the call graph.</item>
    /// </list>
    /// </summary>
    private static void WalkMethodBody(
        Storm.Runtime.SyntaxNode node,
        ISemanticModel model,
        Symbol enclosingMethod,
        ref bool needsAsync,
        HashSet<Symbol> callees)
    {
        switch(node)
        {
            case AwaitExpressionUnaryNode awaitExpr:
                // Seed rule 3: explicit await requires C# async on the enclosing method.
                needsAsync = true;
                // Walk the operand to collect any nested callees.
                WalkMethodBody(awaitExpr.Operand, model, enclosingMethod, ref needsAsync, callees);
                return;

            case AsyncExpressionUnaryNode asyncExpr:
                // Seed rule 4: async functor expressions (async SyncMethod()) produce
                // Task<T>-typed locals that require scope-exit await in try/finally.
                // The containing method must be emitted as C# async for that await to be valid.
                needsAsync = true;
                // Walk the operand to collect any nested callees.
                WalkMethodBody(asyncExpr.Operand, model, enclosingMethod, ref needsAsync, callees);
                return;

            case PostfixExpressionUnaryNode postfixExpr:
                // Delegate to the postfix node handler.
                WalkPostfixNode(postfixExpr.Primary, model, enclosingMethod, ref needsAsync, callees);
                return;

            case PostfixNode postfix:
                WalkPostfixNode(postfix, model, enclosingMethod, ref needsAsync, callees);
                return;
        }

        // Default: walk all children.
        foreach(var child in node.Children())
        {
            if(child is null)
                continue;
            WalkMethodBody(child, model, enclosingMethod, ref needsAsync, callees);
        }
    }

    /// <summary>
    /// Handles a <see cref="PostfixNode"/> which represents expressions like
    /// <c>foo()</c> or <c>obj.Method()</c>. Resolves the target symbol for
    /// direct invocations and member method calls.
    /// </summary>
    private static void WalkPostfixNode(
        PostfixNode postfix,
        ISemanticModel model,
        Symbol enclosingMethod,
        ref bool needsAsync,
        HashSet<Symbol> callees)
    {
        // Check for direct invocation: identifier(args)
        // The target identifier has a registered symbol via RegisterUsageSite.
        if(postfix.Suffix.Length > 0 && postfix.Suffix[^1] is InvocationSuffixPostfixSuffixNode)
        {
            var targetSymbol = model.GetDeclaredSymbol(postfix.Target);
            if(targetSymbol is Symbol { Kind: SymbolKind.Method } calleeSym)
            {
                callees.Add(calleeSym);

                // Seed rule 2: sync method directly invoking an async method
                // triggers auto-unwrap, which requires await in emitted C#.
                if(!enclosingMethod.IsAsync && calleeSym.IsAsync)
                {
                    needsAsync = true;
                }
            }
        }

        // Check for member method calls in the suffix chain: obj.Method(args)
        foreach(var suffix in postfix.Suffix)
        {
            if(suffix is MethodCallSuffixPostfixSuffixNode methodCall)
            {
                // Try to resolve the callee symbol from the suffix node.
                // The semantic model may have registered it via GetDeclaredSymbol
                // if the builder stored it, or we can look it up by name from
                // the receiver type.
                var calleeSym = TryResolveMethodCallSymbol(methodCall, postfix, model);

                // Fallback: if standard resolution fails (e.g., for namespace-qualified
                // calls like Namespace.Type.Method()), try to find the method symbol
                // by reconstructing the qualified type name from the suffix chain.
                if(calleeSym is null)
                {
                    calleeSym = TryResolveQualifiedMethodCall(methodCall, postfix, model);
                }

                if(calleeSym is not null)
                {
                    callees.Add(calleeSym);

                    // Seed rule 2: sync method calling an async method.
                    if(!enclosingMethod.IsAsync && calleeSym.IsAsync)
                    {
                        needsAsync = true;
                    }
                }

                // Walk arguments for nested invocations.
                if(methodCall.Arguments is not null)
                {
                    WalkMethodBody(methodCall.Arguments, model, enclosingMethod, ref needsAsync, callees);
                }
            }
            else if(suffix is InvocationSuffixPostfixSuffixNode invocation)
            {
                // Walk arguments for nested invocations.
                if(invocation.Arguments is not null)
                {
                    WalkMethodBody(invocation.Arguments, model, enclosingMethod, ref needsAsync, callees);
                }
            }
            else
            {
                // Walk other suffix types for nested expressions.
                WalkMethodBody(suffix, model, enclosingMethod, ref needsAsync, callees);
            }
        }

        // Walk the target primary expression for nested invocations.
        WalkMethodBody(postfix.Target, model, enclosingMethod, ref needsAsync, callees);
    }

    /// <summary>
    /// Attempts to resolve the method symbol for a member method call suffix.
    /// Uses the semantic model's type information to find the method on the receiver type.
    /// </summary>
    private static Symbol? TryResolveMethodCallSymbol(
        MethodCallSuffixPostfixSuffixNode methodCall,
        PostfixNode postfix,
        ISemanticModel model)
    {
        // Strategy: look up the receiver type from the expression type of the
        // preceding suffix or target, then find the method by name in that type's scope.
        var memberName = methodCall.Name.Lexeme;

        // Determine the receiver type. The receiver is the type of the expression
        // before this suffix in the chain.
        IType? receiverType = null;

        // Find the index of this method call in the suffix array.
        var suffixIndex = -1;
        for(var i = 0; i < postfix.Suffix.Length; i++)
        {
            if(ReferenceEquals(postfix.Suffix[i], methodCall))
            {
                suffixIndex = i;
                break;
            }
        }

        if(suffixIndex == 0)
        {
            // The receiver is the target primary expression.
            receiverType = model.GetTypeOf(postfix.Target);
        }
        else if(suffixIndex > 0)
        {
            // The receiver is the result of the previous suffix.
            receiverType = model.GetTypeOf(postfix.Suffix[suffixIndex - 1]);
        }

        if(receiverType is null)
            return null;

        // Look up the method by name in the receiver type's member scope.
        return FindMethodInType(receiverType, memberName);
    }

    /// <summary>
    /// Finds a method symbol by name in a type's member scope.
    /// </summary>
    private static Symbol? FindMethodInType(IType type, string methodName)
    {
        var scope = GetMemberScope(type);
        if(scope is null)
            return null;

        var found = scope.Lookup(methodName);
        if(found is Symbol sym && sym.Kind == SymbolKind.Method)
        {
            return sym;
        }

        // Check base type for classes.
        if(type is ClassType classType && classType.BaseType is not null)
        {
            return FindMethodInType(classType.BaseType, methodName);
        }

        return null;
    }

    /// <summary>
    /// Gets the member scope for a type, handling constructed types by
    /// looking at their generic definition.
    /// </summary>
    private static IScope? GetMemberScope(IType type)
    {
        return type switch
        {
            ClassType ct => ct.MemberScope,
            StructType st => st.MemberScope,
            AbstractType it => it.MemberScope,
            ConstructedType constructed => GetMemberScope(constructed.GenericDefinition),
            _ => null,
        };
    }

    /// <summary>
    /// Fallback resolution for namespace-qualified method calls like
    /// <c>Namespace.Type.Method()</c>. When the standard resolution via
    /// <see cref="TryResolveMethodCallSymbol"/> fails (because namespace
    /// identifiers resolve to ErrorType in expressions), this method
    /// reconstructs the qualified type name from the suffix chain and
    /// searches the model's symbols for a matching method.
    /// </summary>
    private static Symbol? TryResolveQualifiedMethodCall(
        MethodCallSuffixPostfixSuffixNode methodCall,
        PostfixNode postfix,
        ISemanticModel model)
    {
        var methodName = methodCall.Name.Lexeme;

        // Find the index of this method call in the suffix array.
        var suffixIndex = -1;
        for(var i = 0; i < postfix.Suffix.Length; i++)
        {
            if(ReferenceEquals(postfix.Suffix[i], methodCall))
            {
                suffixIndex = i;
                break;
            }
        }

        // Need at least one preceding MemberAccessSuffix for the type name.
        if(suffixIndex < 1)
            return null;

        // Build the qualified name from the target identifier + member access suffixes.
        // For "AsyncDropChaining.Program.RunDemo()", we want "AsyncDropChaining.Program".
        if(postfix.Target is not IdentifierExpressionPrimaryNode targetId)
            return null;

        var parts = new List<string> { targetId.Name.Lexeme };
        for(var i = 0; i < suffixIndex; i++)
        {
            if(postfix.Suffix[i] is MemberAccessSuffixPostfixSuffixNode memberAccess)
            {
                parts.Add(memberAccess.Name.Lexeme);
            }
            else
            {
                // Non-member-access suffix in the chain — can't reconstruct as qualified name.
                return null;
            }
        }

        // The last part is the type name, everything before is the namespace.
        // E.g., ["AsyncDropChaining", "Program"] → namespace="AsyncDropChaining", type="Program"
        if(parts.Count < 2)
            return null;

        var typeName = parts[^1];
        var namespaceName = string.Join(".", parts.Take(parts.Count - 1));

        // Search the model's symbols for a class/struct in the matching namespace
        // that has the method we're looking for.
        foreach(var symbol in model.AllSymbols)
        {
            if(symbol is not Symbol sym)
                continue;

            if(sym.Kind is not (SymbolKind.Class or SymbolKind.Struct))
                continue;

            if(sym.Name != typeName)
                continue;

            // Check if the type is in the right namespace.
            var containingNs = (sym.DeclaredType as ClassType)?.ContainingNamespace
                             ?? (sym.DeclaredType as StructType)?.ContainingNamespace;

            if(containingNs != namespaceName)
                continue;

            // Found the type — look for the method.
            var typeForLookup = sym.DeclaredType;
            if(typeForLookup is null)
                continue;

            return FindMethodInType(typeForLookup, methodName);
        }

        return null;
    }

    /// <summary>
    /// Finds the enclosing type (class or struct) for a method symbol by checking
    /// the method's declaring scope chain.
    /// </summary>
    private static IType? FindEnclosingType(Symbol method, ISemanticModel model)
    {
        // Search all type symbols to find one whose member scope contains this method.
        foreach(var symbol in model.AllSymbols)
        {
            if(symbol is not Symbol sym)
                continue;

            var memberScope = sym.DeclaredType switch
            {
                ClassType ct => ct.MemberScope,
                StructType st => st.MemberScope,
                _ => null,
            };

            if(memberScope is null)
                continue;

            foreach(var member in memberScope.Members)
            {
                if(ReferenceEquals(member, method))
                    return sym.DeclaredType;
            }
        }

        return null;
    }
}