using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.Semantics;

/// <summary>
/// A post-processing pass that runs after the semantic model is built but before
/// analyzers. Examines each member's body to determine if it qualifies as
/// implicitly readonly, classifying it as <see cref="ReadonlyStatus.InferredReadonly"/>
/// or <see cref="ReadonlyStatus.Mutable"/>.
/// </summary>
public sealed class ReadonlyInferenceEngine
{
    /// <summary>
    /// Runs readonly inference over all types and their members in the semantic model.
    /// Members already classified as <see cref="ReadonlyStatus.ExplicitReadonly"/> or
    /// <see cref="ReadonlyStatus.ImplicitReadonly"/> are skipped. Unclassified members
    /// are examined and set to either <see cref="ReadonlyStatus.InferredReadonly"/> or
    /// <see cref="ReadonlyStatus.Mutable"/>.
    /// </summary>
    public static void RunInference(ISemanticModel model)
    {
        var typeSymbols = model.AllSymbols
            .Where(s => s.Kind is SymbolKind.Class or SymbolKind.Struct
                                or SymbolKind.Abstract or SymbolKind.Trait);

        foreach (var typeSymbol in typeSymbols)
        {
            var type = typeSymbol.DeclaredType;
            if (type is null)
                continue;

            var memberScope = GetMemberScope(type);
            if (memberScope is null)
                continue;

            // Pass 1: classify members in declaration order.
            // If a called method in the same type hasn't been classified yet,
            // treat it conservatively as mutable.
            ClassifyMembers(memberScope, model);
        }

        // Pass 2: re-evaluate members that may depend on same-type members
        // whose status was resolved in pass 1.
        foreach (var typeSymbol in typeSymbols)
        {
            var type = typeSymbol.DeclaredType;
            if (type is null)
                continue;

            var memberScope = GetMemberScope(type);
            if (memberScope is null)
                continue;

            ClassifyMembers(memberScope, model);
        }
    }

    private static IScope? GetMemberScope(IType type)
    {
        return type switch
        {
            ClassType ct => ct.MemberScope,
            StructType st => st.MemberScope,
            AbstractType at => at.MemberScope,
            TraitType tt => tt.MemberScope,
            _ => null,
        };
    }

    private static void ClassifyMembers(IScope memberScope, ISemanticModel model)
    {
        foreach (var member in memberScope.Members)
        {
            if (member is not Symbol symbol)
                continue;

            if (symbol.Kind is not (SymbolKind.Field or SymbolKind.Property
                or SymbolKind.Indexer or SymbolKind.Method or SymbolKind.Constructor))
                continue;

            // Skip members that are already classified.
            if (symbol.ReadonlyStatus is ReadonlyStatus.ExplicitReadonly
                or ReadonlyStatus.ImplicitReadonly)
                continue;

            var status = ClassifyMember(symbol, model);
            symbol.ReadonlyStatus = status;
        }
    }

    /// <summary>
    /// Classifies a single member by examining its body. Returns
    /// <see cref="ReadonlyStatus.InferredReadonly"/> if the body contains only
    /// readonly-safe operations, or <see cref="ReadonlyStatus.Mutable"/> otherwise.
    /// </summary>
    private static ReadonlyStatus ClassifyMember(Symbol symbol, ISemanticModel model)
    {
        // Fields without initializers are trivially readonly (just data).
        if (symbol.Kind == SymbolKind.Field)
        {
            return ClassifyField(symbol, model);
        }

        // Properties: classify based on accessor bodies.
        if (symbol.Kind is SymbolKind.Property or SymbolKind.Indexer)
        {
            return ClassifyProperty(symbol, model);
        }

        // Methods and constructors: classify based on body.
        return ClassifyMethodOrConstructor(symbol, model);
    }

    /// <summary>
    /// Classifies a field. Fields without initializers are trivially readonly.
    /// Fields with initializers need the initializer examined.
    /// </summary>
    private static ReadonlyStatus ClassifyField(Symbol symbol, ISemanticModel model)
    {
        SyntaxNode declaringNode;
        try { declaringNode = model.GetDeclaringNode(symbol); }
        catch { return ReadonlyStatus.Mutable; }

        if (declaringNode is not FieldDeclarationNode fieldDecl)
            return ReadonlyStatus.Mutable;

        // Fields are not inferred as readonly by the inference engine.
        // A field's readonly status is determined solely by explicit annotation.
        // The inference engine cannot determine if a field is "never assigned outside
        // a constructor" without whole-program analysis, so we conservatively treat
        // all non-explicitly-readonly fields as mutable.
        return ReadonlyStatus.Mutable;
    }

    /// <summary>
    /// Classifies a property or indexer based on its accessor bodies.
    /// Auto-getter properties are trivially readonly.
    /// </summary>
    private static ReadonlyStatus ClassifyProperty(Symbol symbol, ISemanticModel model)
    {
        SyntaxNode declaringNode;
        try { declaringNode = model.GetDeclaringNode(symbol); }
        catch { return ReadonlyStatus.Mutable; }

        switch (declaringNode)
        {
            case BlockPropertyPropertyDeclarationNode blockProp:
            {
                // If the property has a setter or init accessor, it's mutable
                // (setters allow mutation by definition).
                foreach (var accessor in blockProp.Accessors)
                {
                    if (accessor is BlockSetAccessorAccessorDeclarationNode
                        or ExpressionSetAccessorAccessorDeclarationNode
                        or AutoSetAccessorAccessorDeclarationNode
                        or BlockInitAccessorAccessorDeclarationNode
                        or ExpressionInitAccessorAccessorDeclarationNode
                        or AutoInitAccessorAccessorDeclarationNode)
                    {
                        return ReadonlyStatus.Mutable;
                    }
                }

                // Check getter bodies for readonly safety.
                foreach (var accessor in blockProp.Accessors)
                {
                    switch (accessor)
                    {
                        case BlockGetAccessorAccessorDeclarationNode blockGet:
                            if (!IsNodeReadonlySafe(blockGet.Body, model))
                                return ReadonlyStatus.Mutable;
                            break;
                        case ExpressionGetAccessorAccessorDeclarationNode exprGet:
                            if (!IsNodeReadonlySafe(exprGet.Expression, model))
                                return ReadonlyStatus.Mutable;
                            break;
                        // Auto-get is trivially readonly.
                    }
                }

                return ReadonlyStatus.InferredReadonly;
            }

            case ExpressionBodiedPropertyPropertyDeclarationNode exprProp:
            {
                // Expression-bodied properties are get-only, check the body.
                return IsNodeReadonlySafe(exprProp.Body, model)
                    ? ReadonlyStatus.InferredReadonly
                    : ReadonlyStatus.Mutable;
            }

            // Indexer declarations
            case IndexerDeclarationNode indexerDecl
                when indexerDecl.Body is BlockIndexerBodyIndexerBodyNode blockBody:
            {
                // If the indexer has a setter, it's mutable.
                foreach (var accessor in blockBody.Accessors)
                {
                    if (accessor is BlockSetAccessorAccessorDeclarationNode
                        or ExpressionSetAccessorAccessorDeclarationNode
                        or AutoSetAccessorAccessorDeclarationNode
                        or BlockInitAccessorAccessorDeclarationNode
                        or ExpressionInitAccessorAccessorDeclarationNode
                        or AutoInitAccessorAccessorDeclarationNode)
                    {
                        return ReadonlyStatus.Mutable;
                    }
                }

                // Check getter bodies.
                foreach (var accessor in blockBody.Accessors)
                {
                    switch (accessor)
                    {
                        case BlockGetAccessorAccessorDeclarationNode blockGet:
                            if (!IsNodeReadonlySafe(blockGet.Body, model))
                                return ReadonlyStatus.Mutable;
                            break;
                        case ExpressionGetAccessorAccessorDeclarationNode exprGet:
                            if (!IsNodeReadonlySafe(exprGet.Expression, model))
                                return ReadonlyStatus.Mutable;
                            break;
                    }
                }

                return ReadonlyStatus.InferredReadonly;
            }

            default:
                return ReadonlyStatus.Mutable;
        }
    }

    /// <summary>
    /// Classifies a method or constructor by examining its body.
    /// </summary>
    private static ReadonlyStatus ClassifyMethodOrConstructor(Symbol symbol, ISemanticModel model)
    {
        SyntaxNode declaringNode;
        try { declaringNode = model.GetDeclaringNode(symbol); }
        catch { return ReadonlyStatus.Mutable; }

        BlockNode? body = declaringNode switch
        {
            MethodDeclarationNode m => m.Body.GetBlock(),
            ConstructorDeclarationNode c => c.Body.GetBlock(),
            _ => null,
        };

        // No body (intrinsic or abstract) — conservatively treat as mutable.
        if (body is null)
            return ReadonlyStatus.Mutable;

        // For constructors, check if it only assigns to its own fields
        // (constructor inference per design doc).
        if (symbol.Kind == SymbolKind.Constructor)
        {
            return IsConstructorBodyReadonlySafe(body, symbol, model)
                ? ReadonlyStatus.InferredReadonly
                : ReadonlyStatus.Mutable;
        }

        // For methods, check if the body is entirely readonly-safe.
        return IsNodeReadonlySafe(body, model)
            ? ReadonlyStatus.InferredReadonly
            : ReadonlyStatus.Mutable;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Body examination — determines if a node tree is readonly-safe.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Recursively examines an AST node and all its children to determine
    /// if all operations are readonly-safe.
    /// </summary>
    private static bool IsNodeReadonlySafe(SyntaxNode node, ISemanticModel model)
    {
        // Check if this specific node is an unsafe operation.
        if (IsUnsafeNode(node, model))
            return false;

        // Recursively check all children.
        foreach (var child in node.Children())
        {
            if (child is not null && !IsNodeReadonlySafe(child, model))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Determines if a specific node represents an unsafe (mutable) operation.
    /// Does NOT recurse into children — that's handled by the caller.
    /// </summary>
    private static bool IsUnsafeNode(SyntaxNode node, ISemanticModel model)
    {
        switch (node)
        {
            // Assignment: target = value where target resolves to a field or property setter.
            // In the AST, assignments are represented as AssignmentNode with a
            // non-null Value property.
            case AssignmentNode assignment when assignment.Value is not null:
                return IsAssignmentToFieldOrProperty(assignment, model);

            // Method calls and member access: check if the called method is readonly.
            case MethodCallSuffixPostfixSuffixNode methodCall:
                return IsNonReadonlyMethodCall(methodCall, model);

            // Member access that resolves to a property getter call.
            case MemberAccessSuffixPostfixSuffixNode memberAccess:
                return IsNonReadonlyPropertyGetterAccess(memberAccess, model);

            // Object creation: check if the constructor is readonly.
            case ObjectCreationExpressionPrimaryNode objectCreation:
                return IsNonReadonlyConstructorAllocation(objectCreation, model);

            default:
                return false;
        }
    }

    /// <summary>
    /// Checks if an assignment targets a field or property setter (as opposed to a local variable).
    /// Field assignments and property setter calls make a member mutable.
    /// </summary>
    private static bool IsAssignmentToFieldOrProperty(AssignmentNode assignment, ISemanticModel model)
    {
        // Walk the target expression to find what it resolves to.
        // The target is a LogicalOrNode tree. We need to find the leaf
        // identifier or member access that is being assigned to.
        return IsFieldOrPropertyTarget(assignment.Target, model);
    }

    /// <summary>
    /// Determines if an expression tree targets a field or property for assignment purposes.
    /// Assignments to fields or properties (setter calls) make a member mutable.
    /// </summary>
    private static bool IsFieldOrPropertyTarget(SyntaxNode targetNode, ISemanticModel model)
    {
        switch (targetNode)
        {
            // Direct postfix expression: this.field or obj.field or this.Property
            case PostfixNode postfix:
            {
                // If the last suffix is a member access, check if it's a field or property.
                if (postfix.Suffix.Length > 0)
                {
                    var lastSuffix = postfix.Suffix[^1];
                    if (lastSuffix is MemberAccessSuffixPostfixSuffixNode memberAccess)
                    {
                        // Resolve the member to check if it's a field or property.
                        var symbol = ResolveMemberAccessSymbol(postfix, memberAccess, model);
                        if (symbol is not null)
                            return symbol.Kind is SymbolKind.Field or SymbolKind.Property or SymbolKind.Indexer;
                        // If we can't resolve, conservatively treat as mutable (it's an assignment
                        // to something we can't identify — could be a field or property).
                        return true;
                    }

                    // Index suffix — assigning to an indexed element is a setter call (mutable).
                    if (lastSuffix is IndexSuffixPostfixSuffixNode)
                        return true;
                }

                // Bare identifier postfix (no suffixes) — check if it's a field or property.
                if (postfix.Suffix.Length == 0 && postfix.Target is IdentifierExpressionPrimaryNode identifier)
                {
                    var symbol = model.GetDeclaredSymbol(identifier);
                    if (symbol is not null)
                        return symbol.Kind is SymbolKind.Field or SymbolKind.Property or SymbolKind.Indexer;

                    // Unresolved identifier — likely a local variable.
                    return false;
                }

                return false;
            }

            // Walk through expression wrappers to find the actual target.
            default:
            {
                // For other expression node types (LogicalOr, Equality, etc.),
                // walk children to find the actual postfix target.
                foreach (var child in targetNode.Children())
                {
                    if (child is not null && IsFieldOrPropertyTarget(child, model))
                        return true;
                }
                return false;
            }
        }
    }

    /// <summary>
    /// Resolves a member access suffix to its symbol by looking at the receiver type.
    /// </summary>
    private static ISymbol? ResolveMemberAccessSymbol(
        PostfixNode postfix, MemberAccessSuffixPostfixSuffixNode memberAccess, ISemanticModel model)
    {
        // Get the type of the receiver (the expression before the member access).
        // For `this.field`, the receiver is `this` which has the enclosing type.
        // For chained access like `a.b.c`, we need the type of `a.b`.
        IType? receiverType = null;

        if (postfix.Suffix.Length == 1)
        {
            // Single suffix: receiver is the target primary.
            receiverType = model.GetTypeOf(postfix.Target);
        }
        else
        {
            // Multiple suffixes: receiver is the previous suffix's result.
            var prevSuffixIndex = Array.IndexOf(postfix.Suffix.ToArray(), memberAccess) - 1;
            if (prevSuffixIndex >= 0)
            {
                receiverType = model.GetTypeOf(postfix.Suffix[prevSuffixIndex]);
            }
        }

        if (receiverType is null)
            return null;

        var memberName = memberAccess.Name.Lexeme;
        return LookupMemberInType(receiverType, memberName);
    }

    /// <summary>
    /// Checks if a method call suffix targets a non-readonly method.
    /// </summary>
    private static bool IsNonReadonlyMethodCall(
        MethodCallSuffixPostfixSuffixNode methodCall, ISemanticModel model)
    {
        // Try to get the symbol from the model's declared symbol map first.
        var symbol = model.GetDeclaredSymbol(methodCall);
        if (symbol is Symbol sym)
        {
            return !IsReadonly(sym.ReadonlyStatus);
        }

        // GetDeclaredSymbol only maps declaration nodes, not call-site nodes.
        // Fall back to resolving the method by name from AllSymbols.
        // This handles `this.Method()` calls where the method is defined in
        // the same type or a known type.
        var methodName = methodCall.Name.Lexeme;
        var resolved = model.AllSymbols
            .OfType<Symbol>()
            .FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == methodName);

        if (resolved is not null)
        {
            return !IsReadonly(resolved.ReadonlyStatus);
        }

        // If we can't resolve the method, conservatively treat as mutable.
        // This handles cases where the called method hasn't been classified yet
        // (pass 1 behavior).
        return true;
    }

    /// <summary>
    /// Checks if a member access resolves to a non-readonly property getter.
    /// </summary>
    private static bool IsNonReadonlyPropertyGetterAccess(
        MemberAccessSuffixPostfixSuffixNode memberAccess, ISemanticModel model)
    {
        // Member access to a field is always safe (reading fields is permitted).
        // Member access to a property getter needs the getter to be readonly.
        var symbol = model.GetDeclaredSymbol(memberAccess);
        if (symbol is Symbol sym)
        {
            // Fields are always safe to read.
            if (sym.Kind == SymbolKind.Field)
                return false;

            // Properties: check if the property is readonly.
            if (sym.Kind is SymbolKind.Property or SymbolKind.Indexer)
            {
                return !IsReadonly(sym.ReadonlyStatus);
            }
        }

        // If we can't resolve, assume safe (field read or unresolved).
        // This is a pragmatic choice: most member accesses in practice are
        // field reads, and being overly conservative here would prevent
        // inference from working on most real code.
        return false;
    }

    /// <summary>
    /// Checks if an object creation expression targets a non-readonly constructor.
    /// </summary>
    private static bool IsNonReadonlyConstructorAllocation(
        ObjectCreationExpressionPrimaryNode objectCreation, ISemanticModel model)
    {
        // Resolve the type being constructed.
        var createdType = model.GetTypeOf(objectCreation);
        if (createdType is null)
            return true; // Can't resolve — conservatively mutable.

        // If the type itself is readonly, all its constructors are implicitly readonly.
        if (IsTypeReadonly(createdType))
            return false;

        // Look for a constructor in the created type's member scope.
        var memberScope = GetMemberScope(createdType);
        if (memberScope is null)
            return true; // Can't find scope — conservatively mutable.

        // Find constructors and check if any is readonly.
        foreach (var member in memberScope.Members)
        {
            if (member.Kind == SymbolKind.Constructor && member is Symbol ctorSym)
            {
                if (IsReadonly(ctorSym.ReadonlyStatus))
                    return false; // At least one readonly constructor exists.
            }
        }

        // No readonly constructor found — this allocation is unsafe.
        return true;
    }

    /// <summary>
    /// Checks if a constructor body is readonly-safe. A constructor is inferred
    /// readonly when it only assigns to its own fields, does not call non-readonly
    /// methods/getters, does not call setters (other than readonly property/indexer
    /// setters on <c>this</c>), and all <c>new</c> expressions target readonly
    /// constructors.
    /// </summary>
    private static bool IsConstructorBodyReadonlySafe(BlockNode body, Symbol ctorSymbol, ISemanticModel model)
    {
        return IsConstructorNodeSafe(body, ctorSymbol, model);
    }

    /// <summary>
    /// Recursively checks if a node within a constructor body is safe for
    /// readonly constructor inference. Allows field assignments to own fields
    /// and property setter calls on own readonly properties.
    /// </summary>
    private static bool IsConstructorNodeSafe(SyntaxNode node, Symbol ctorSymbol, ISemanticModel model)
    {
        switch (node)
        {
            case AssignmentNode assignment when assignment.Value is not null:
            {
                // In a constructor, assignments to own fields are permitted.
                if (IsAssignmentToOwnField(assignment, ctorSymbol, model))
                {
                    // The value side must still be safe.
                    return IsConstructorNodeSafe(assignment.Value, ctorSymbol, model);
                }
                // In a constructor, assignments to own properties (setter calls) are also permitted.
                if (IsAssignmentToOwnProperty(assignment, ctorSymbol, model))
                {
                    // The value side must still be safe.
                    return IsConstructorNodeSafe(assignment.Value, ctorSymbol, model);
                }
                // Assignment to something other than own field/property — not safe.
                return false;
            }

            case MethodCallSuffixPostfixSuffixNode methodCall:
                return !IsNonReadonlyMethodCall(methodCall, model);

            case MemberAccessSuffixPostfixSuffixNode memberAccess:
                return !IsNonReadonlyPropertyGetterAccess(memberAccess, model);

            case ObjectCreationExpressionPrimaryNode objectCreation:
                return !IsNonReadonlyConstructorAllocation(objectCreation, model);

            default:
            {
                // Recursively check all children.
                foreach (var child in node.Children())
                {
                    if (child is not null && !IsConstructorNodeSafe(child, ctorSymbol, model))
                        return false;
                }
                return true;
            }
        }
    }

    /// <summary>
    /// Checks if an assignment targets a field of the declaring type (own field).
    /// </summary>
    private static bool IsAssignmentToOwnField(AssignmentNode assignment, Symbol ctorSymbol, ISemanticModel model)
    {
        // Check if the target is `this.field` or a bare `field` identifier
        // that resolves to a field of the declaring type.
        return IsOwnFieldTarget(assignment.Target, ctorSymbol, model);
    }

    /// <summary>
    /// Determines if an expression targets a field of the constructor's declaring type.
    /// </summary>
    private static bool IsOwnFieldTarget(SyntaxNode targetNode, Symbol ctorSymbol, ISemanticModel model)
    {
        switch (targetNode)
        {
            case PostfixNode postfix:
            {
                // `this.field` pattern: target is `this`, suffix is member access to a field.
                if (postfix.Suffix.Length == 1
                    && postfix.Target is ThisExpressionPrimaryNode
                    && postfix.Suffix[0] is MemberAccessSuffixPostfixSuffixNode memberAccess)
                {
                    var memberName = memberAccess.Name.Lexeme;
                    // Check if this member is a field in the declaring type.
                    var declaringType = GetDeclaringType(ctorSymbol);
                    if (declaringType is not null)
                    {
                        var scope = GetMemberScope(declaringType);
                        if (scope is not null)
                        {
                            var member = scope.Lookup(memberName);
                            return member is not null && member.Kind == SymbolKind.Field;
                        }
                    }
                    return false;
                }

                // Bare identifier that is a field of the declaring type.
                if (postfix.Suffix.Length == 0 && postfix.Target is IdentifierExpressionPrimaryNode identifier)
                {
                    var memberName = identifier.Name.Lexeme;
                    var declaringType = GetDeclaringType(ctorSymbol);
                    if (declaringType is not null)
                    {
                        var scope = GetMemberScope(declaringType);
                        if (scope is not null)
                        {
                            var member = scope.Lookup(memberName);
                            return member is not null && member.Kind == SymbolKind.Field;
                        }
                    }
                    return false;
                }

                return false;
            }

            default:
            {
                foreach (var child in targetNode.Children())
                {
                    if (child is not null && IsOwnFieldTarget(child, ctorSymbol, model))
                        return true;
                }
                return false;
            }
        }
    }

    /// <summary>
    /// Checks if an assignment targets a readonly property of the declaring type (own readonly property setter call).
    /// In a constructor, assigning to own readonly properties is permitted for readonly inference.
    /// </summary>
    private static bool IsAssignmentToOwnProperty(AssignmentNode assignment, Symbol ctorSymbol, ISemanticModel model)
    {
        return IsOwnReadonlyPropertyTarget(assignment.Target, ctorSymbol, model);
    }

    /// <summary>
    /// Determines if an expression targets a readonly property of the constructor's declaring type.
    /// </summary>
    private static bool IsOwnReadonlyPropertyTarget(SyntaxNode targetNode, Symbol ctorSymbol, ISemanticModel model)
    {
        switch (targetNode)
        {
            case PostfixNode postfix:
            {
                // `this.Property` pattern: target is `this`, suffix is member access to a property.
                if (postfix.Suffix.Length == 1
                    && postfix.Target is ThisExpressionPrimaryNode
                    && postfix.Suffix[0] is MemberAccessSuffixPostfixSuffixNode memberAccess)
                {
                    var memberName = memberAccess.Name.Lexeme;
                    var declaringType = GetDeclaringType(ctorSymbol);
                    if (declaringType is not null)
                    {
                        var scope = GetMemberScope(declaringType);
                        if (scope is not null)
                        {
                            var member = scope.Lookup(memberName);
                            if (member is Symbol sym && sym.Kind is SymbolKind.Property or SymbolKind.Indexer)
                                return IsReadonly(sym.ReadonlyStatus);
                        }
                    }
                    return false;
                }

                // Bare identifier that is a readonly property of the declaring type.
                if (postfix.Suffix.Length == 0 && postfix.Target is IdentifierExpressionPrimaryNode identifier)
                {
                    var memberName = identifier.Name.Lexeme;
                    var declaringType = GetDeclaringType(ctorSymbol);
                    if (declaringType is not null)
                    {
                        var scope = GetMemberScope(declaringType);
                        if (scope is not null)
                        {
                            var member = scope.Lookup(memberName);
                            if (member is Symbol sym && sym.Kind is SymbolKind.Property or SymbolKind.Indexer)
                                return IsReadonly(sym.ReadonlyStatus);
                        }
                    }
                    return false;
                }

                return false;
            }

            default:
            {
                foreach (var child in targetNode.Children())
                {
                    if (child is not null && IsOwnReadonlyPropertyTarget(child, ctorSymbol, model))
                        return true;
                }
                return false;
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helper methods
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns true if the given status represents a readonly classification.
    /// </summary>
    private static bool IsReadonly(ReadonlyStatus status)
    {
        return status is ReadonlyStatus.ExplicitReadonly
            or ReadonlyStatus.InferredReadonly
            or ReadonlyStatus.ImplicitReadonly;
    }

    /// <summary>
    /// Returns true if the given type is marked as readonly.
    /// </summary>
    private static bool IsTypeReadonly(IType type)
    {
        return type switch
        {
            ClassType ct => ct.IsReadonly,
            StructType st => st.IsReadonly,
            AbstractType at => at.IsReadonly,
            TraitType tt => tt.IsReadonly,
            _ => false,
        };
    }

    /// <summary>
    /// Gets the declaring type for a symbol by examining its containing scope.
    /// </summary>
    private static IType? GetDeclaringType(Symbol symbol)
    {
        // Walk up the containing scope to find the type that owns this member.
        var scope = symbol.ContainingScope;
        // The containing scope's parent should be the type scope.
        // Look for a symbol in the parent scope that declares a type.
        if (scope?.Parent is IScope parentScope)
        {
            foreach (var member in parentScope.Members)
            {
                if (member.DeclaredType is not null)
                {
                    var memberScope = GetMemberScope(member.DeclaredType);
                    if (ReferenceEquals(memberScope, scope))
                        return member.DeclaredType;
                }
            }
        }

        // Also check if the scope itself is a type's member scope.
        // The symbol's ContainingScope IS the type's member scope.
        // We need to find which type owns that scope.
        return FindTypeForScope(scope);
    }

    /// <summary>
    /// Finds the type that owns the given scope by checking if any type's
    /// member scope matches.
    /// </summary>
    private static IType? FindTypeForScope(IScope? scope)
    {
        if (scope is null)
            return null;

        // Walk up to find the parent scope that contains type symbols.
        var parentScope = scope.Parent;
        if (parentScope is null)
            return null;

        foreach (var member in parentScope.Members)
        {
            if (member.DeclaredType is not null)
            {
                var memberScope = GetMemberScope(member.DeclaredType);
                if (ReferenceEquals(memberScope, scope))
                    return member.DeclaredType;
            }
        }

        // Try grandparent scope (for namespace-scoped types).
        var grandparent = parentScope.Parent;
        if (grandparent is not null)
        {
            foreach (var member in grandparent.Members)
            {
                if (member.DeclaredType is not null)
                {
                    var memberScope = GetMemberScope(member.DeclaredType);
                    if (ReferenceEquals(memberScope, scope))
                        return member.DeclaredType;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Looks up a member by name in a type's member scope, including
    /// inherited members for class types.
    /// </summary>
    private static ISymbol? LookupMemberInType(IType type, string memberName)
    {
        // For ConstructedType, look up in the generic definition.
        if (type is ConstructedType ct)
            type = ct.GenericDefinition;

        if (type is ClassType classType)
        {
            ClassType? current = classType;
            while (current is not null)
            {
                foreach (var m in current.MemberScope.Members)
                {
                    if (m.Name == memberName)
                        return m;
                }
                current = current.BaseType as ClassType;
            }
            return null;
        }

        var memberScope = GetMemberScope(type);
        return memberScope?.Lookup(memberName);
    }
}
