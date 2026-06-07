using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Semantics;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.Analysis.Builtin;

/// <summary>
/// Verifies the cooperation contract for types declared <c>owned</c> (Requirement 5,
/// Ownership v2 design Component 3). For every assignment to a field of <c>this</c> inside
/// the type's methods and constructors, the source must resolve as a non-error owned move
/// via <see cref="MoveBoundaryResolver"/>. When it does not, a
/// <c>MIST_OWN_CONTRACT_VIOLATION</c> is emitted naming the method, the field, and the
/// non-owned source (Req 5 AC 3–4, Req 13 AC 4–5).
///
/// <para>
/// This is the "not blind trust" guarantee behind Req 3 AC 3: mutable methods are permitted
/// on owned types <em>because</em> this verifier proves every internal field assignment keeps
/// the owned graph free of non-owned aliases.
/// </para>
///
/// <para>
/// Static/global reads are rejected as sources (design LD-2): a field assignment whose source
/// is a static field/property read does not yield an owned value and is not a fresh graph, and
/// treating it as clonable would silently duplicate global state. Such sources are reported as
/// contract violations directly, closing the alias-leak path into the owned graph.
/// </para>
/// </summary>
public sealed class CooperationContractVerifier
{
    /// <summary>
    /// Verifies the cooperation contract for a single <c>owned</c> type. Walks every
    /// method and constructor body of the type, finds assignments to fields of <c>this</c>,
    /// and routes each source through <paramref name="resolver"/>. A source that resolves to
    /// <see cref="MoveOutcome.Error"/> — or is a static/global read (LD-2) — produces a
    /// <c>MIST_OWN_CONTRACT_VIOLATION</c> diagnostic.
    /// </summary>
    /// <param name="ownedType">The type declared <c>owned</c> whose contract is verified.</param>
    /// <param name="resolver">The single move-boundary authority used to classify sources.</param>
    /// <param name="model">The semantic model.</param>
    /// <param name="diagnostics">The diagnostic bag contract violations are emitted into.</param>
    public static void Verify(
        IType ownedType,
        MoveBoundaryResolver resolver,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        ArgumentNullException.ThrowIfNull(ownedType);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(diagnostics);

        if (!IsOwnedType(ownedType))
            return;

        var memberScope = MemberScopeOf(ownedType);
        if (memberScope is null)
            return;

        foreach (var member in memberScope.Members)
        {
            if (member.Origin != SymbolOrigin.UserCode)
                continue;

            if (member.Kind is not (SymbolKind.Method or SymbolKind.Constructor))
                continue;

            VerifyMethodOrConstructor(member, resolver, model, diagnostics);
        }
    }

    /// <summary>
    /// Verifies a single method/constructor body: seeds owned parameters as Live, finds
    /// every <c>this.Field = &lt;source&gt;</c> assignment, and validates the source.
    /// </summary>
    private static void VerifyMethodOrConstructor(
        ISymbol methodSymbol,
        MoveBoundaryResolver resolver,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        SyntaxNode declaringNode;
        try { declaringNode = model.GetDeclaringNode(methodSymbol); }
        catch { return; }

        BlockNode? body = declaringNode switch
        {
            MethodDeclarationNode methodDecl => methodDecl.Body.GetBlock(),
            ConstructorDeclarationNode ctorDecl => ctorDecl.Body.GetBlock(),
            _ => null,
        };

        if (body is null)
            return;

        // A fresh flow state per body. Owned parameters start Live so that
        // `this.Field = ownedParam` resolves as a valid move (Req 6 AC 3).
        var flowState = new MethodFlowState();
        RegisterOwnedParameters(declaringNode, model, flowState);

        foreach (var assignment in FindFieldAssignments(body, model))
        {
            VerifyFieldAssignment(methodSymbol, assignment, resolver, flowState, model, diagnostics);
        }
    }

    /// <summary>
    /// Validates a single <c>this.Field = &lt;source&gt;</c> assignment. The source is
    /// rejected when it is a static/global read (LD-2) or when the resolver classifies it as
    /// <see cref="MoveOutcome.Error"/>. The resolver's own diagnostics are suppressed (a
    /// throwaway bag) so the contract-specific <c>MIST_OWN_CONTRACT_VIOLATION</c> is the only
    /// message emitted.
    /// </summary>
    private static void VerifyFieldAssignment(
        ISymbol methodSymbol,
        FieldAssignment assignment,
        MoveBoundaryResolver resolver,
        MethodFlowState flowState,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        var source = assignment.Source;
        var fieldType = assignment.Field.DeclaredType;

        // LD-2: a static/global read never yields an owned value and is not a fresh graph;
        // cloning it would duplicate global state. Reject it directly as a contract violation.
        if (IsStaticOrGlobalRead(source, model))
        {
            ReportContractViolation(methodSymbol, assignment, model, diagnostics);
            return;
        }

        // Route the source through the single move boundary. A throwaway bag suppresses the
        // resolver's own MIST_OWN_REQUIRES_OWNED / MIST_OWN_NOT_CLONABLE messages; the
        // contract verifier owns the user-facing diagnostic for owned-type bodies.
        var throwaway = new DiagnosticBag();
        var resolution = resolver.Resolve(
            source,
            fieldType,
            OwnedMoveSite.OwnedFieldAssignment,
            flowState,
            model,
            throwaway);

        if (resolution.Outcome == MoveOutcome.Error)
        {
            ReportContractViolation(methodSymbol, assignment, model, diagnostics);
        }
    }

    /// <summary>
    /// Emits a <c>MIST_OWN_CONTRACT_VIOLATION</c> naming the method/constructor, the field,
    /// and the non-owned source expression (Req 13 AC 4–5).
    /// </summary>
    private static void ReportContractViolation(
        ISymbol methodSymbol,
        FieldAssignment assignment,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        var sourceText = GetNodeText(assignment.Source);
        diagnostics.Add(MakeDiagnostic(
            OwnershipAnalyzer.ContractViolation,
            OwnershipAnalyzer.ContractViolationMessage(methodSymbol.Name, assignment.Field.Name, sourceText),
            assignment.Node,
            model));
    }

    // ── Field-assignment discovery ────────────────────────────────────────

    /// <summary>
    /// A discovered assignment to a field of <c>this</c>: the field symbol, the source
    /// expression being assigned, and the assignment node (for diagnostic location).
    /// </summary>
    private readonly record struct FieldAssignment(ISymbol Field, SyntaxNode Source, SyntaxNode Node);

    /// <summary>
    /// Walks a body block recursively and yields every assignment whose target is an instance
    /// field of <c>this</c> (either <c>this.Field = …</c> or a bare <c>Field = …</c>).
    /// </summary>
    private static List<FieldAssignment> FindFieldAssignments(BlockNode body, ISemanticModel model)
    {
        var results = new List<FieldAssignment>();
        CollectFieldAssignments(body, model, results);
        return results;
    }

    /// <summary>
    /// Recursively visits all descendants of <paramref name="node"/>, collecting assignments
    /// to fields of <c>this</c>.
    /// </summary>
    private static void CollectFieldAssignments(SyntaxNode node, ISemanticModel model, List<FieldAssignment> results)
    {
        if (node is AssignmentNode assignment && assignment.Value is not null)
        {
            var fieldSymbol = TryResolveThisFieldTarget(assignment.Target, model);
            if (fieldSymbol is not null)
            {
                results.Add(new FieldAssignment(fieldSymbol, assignment.Value, assignment));
            }
        }

        foreach (var child in node.Children())
        {
            if (child is not null)
                CollectFieldAssignments(child, model, results);
        }
    }

    /// <summary>
    /// Resolves the target of an assignment to an instance field symbol when the target is
    /// <c>this.Field</c> or a bare <c>Field</c> reference; returns null otherwise (locals,
    /// parameters, properties, static fields, and field accesses on other receivers are not
    /// instance-field-of-this targets).
    /// </summary>
    private static ISymbol? TryResolveThisFieldTarget(SyntaxNode target, ISemanticModel model)
    {
        if (target is ExpressionNode expr)
            return expr.AsAssignmentOrNull() is { } exprAssignment
                ? TryResolveThisFieldTarget(exprAssignment, model)
                : null;

        if (target is AssignmentNode assignment)
        {
            if (assignment.Value is not null)
                return null;
            return TryResolveThisFieldTarget(assignment.Target, model);
        }

        if (target is PostfixNode postfix)
        {
            // `this.Field` — exactly one member-access suffix on a `this` receiver.
            // GetDeclaredSymbol only maps declaration nodes (not usage sites), so the
            // member is resolved by name in the receiver type's member scope instead.
            if (postfix.Suffix.Length == 1
                && postfix.Target is ThisExpressionPrimaryNode
                && postfix.Suffix[0] is MemberAccessSuffixPostfixSuffixNode memberAccess)
            {
                var receiverType = model.GetTypeOf(postfix.Target);
                if (receiverType is null)
                    return null;
                return AsInstanceField(LookupMember(receiverType, memberAccess.Name.Lexeme));
            }

            // Bare `Field` reference (no suffixes) resolving to a field of the type.
            if (postfix.Suffix.Length == 0)
                return TryResolveThisFieldTarget(postfix.Target, model);

            return null;
        }

        if (target is IdentifierExpressionPrimaryNode identifier)
        {
            return AsInstanceField(model.GetDeclaredSymbol(identifier));
        }

        // Walk single-child wrapper chains.
        SyntaxNode? singleChild = null;
        var childCount = 0;
        foreach (var child in target.Children())
        {
            if (child is null)
                continue;
            childCount++;
            singleChild = child;
            if (childCount > 1)
                break;
        }

        return childCount == 1 && singleChild is not null
            ? TryResolveThisFieldTarget(singleChild, model)
            : null;
    }

    /// <summary>
    /// Returns the symbol if it denotes an instance (non-static) field; null otherwise.
    /// </summary>
    private static ISymbol? AsInstanceField(ISymbol? symbol)
    {
        if (symbol is not Symbol sym || sym.Kind != SymbolKind.Field)
            return null;
        return sym.IsStatic ? null : sym;
    }

    // ── Static / global read detection (LD-2) ─────────────────────────────

    /// <summary>
    /// Returns <c>true</c> if the source expression is a read of a static field or static
    /// property (e.g. <c>SomeType.SharedInstance</c> or a bare static reference). Such reads
    /// are rejected as field-assignment sources in owned types (design LD-2) regardless of
    /// whether the static's type happens to be clonable.
    /// </summary>
    private static bool IsStaticOrGlobalRead(SyntaxNode source, ISemanticModel model)
    {
        if (source is ExpressionNode expr)
            return expr.AsAssignmentOrNull() is { } exprAssignment && IsStaticOrGlobalRead(exprAssignment, model);

        if (source is AssignmentNode assignment)
            return assignment.Value is null && IsStaticOrGlobalRead(assignment.Target, model);

        if (source is PostfixNode postfix)
        {
            // Member access whose final suffix is a member read: `Receiver.Member`.
            if (postfix.Suffix.Length == 1
                && postfix.Suffix[0] is MemberAccessSuffixPostfixSuffixNode memberAccess)
            {
                // A type-qualified read (`SomeType.Member`) is a static/global read. The
                // receiver names a type rather than a value: it has no local/parameter
                // binding and resolves to a declared class/struct/etc. This is the reliable
                // signal — Symbol.IsStatic is not consistently set for properties whose
                // accessors are lowered to a backing field.
                if (ReceiverNamesType(postfix.Target, model))
                    return true;

                // Secondary signal: the resolved member is itself static. Resolve it by name
                // in the receiver type's scope (GetDeclaredSymbol only maps declarations).
                var receiverType = model.GetTypeOf(postfix.Target);
                if (receiverType is not null && IsStaticMember(LookupMember(receiverType, memberAccess.Name.Lexeme)))
                    return true;

                return false;
            }

            // Bare identifier resolving to a static field/property.
            if (postfix.Suffix.Length == 0)
                return IsStaticOrGlobalRead(postfix.Target, model);

            return false;
        }

        if (source is IdentifierExpressionPrimaryNode identifier)
            return IsStaticMember(model.GetDeclaredSymbol(identifier));

        // Walk single-child wrapper chains.
        SyntaxNode? singleChild = null;
        var childCount = 0;
        foreach (var child in source.Children())
        {
            if (child is null)
                continue;
            childCount++;
            singleChild = child;
            if (childCount > 1)
                break;
        }

        return childCount == 1 && singleChild is not null && IsStaticOrGlobalRead(singleChild, model);
    }

    /// <summary>
    /// Returns <c>true</c> when the receiver expression of a member access names a type
    /// (e.g. the <c>Registry</c> in <c>Registry.Shared</c>) rather than a value. A
    /// type-named receiver has no local-variable/parameter binding and matches a declared
    /// class/struct/interface/trait/enum symbol in the model. This is the LD-2 marker for a
    /// static/global read.
    /// </summary>
    private static bool ReceiverNamesType(SyntaxNode receiver, ISemanticModel model)
    {
        // Unwrap a no-suffix postfix wrapper around the primary identifier.
        if (receiver is PostfixNode { Suffix.Length: 0 } postfix)
            receiver = postfix.Target;

        if (receiver is not IdentifierExpressionPrimaryNode identifier)
            return false;

        // A value reference (local/parameter) is NOT a type name.
        var declared = model.GetDeclaredSymbol(identifier);
        if (declared is Symbol sym && sym.Kind is SymbolKind.LocalVariable or SymbolKind.Parameter)
            return false;

        var name = identifier.Name.Lexeme;
        foreach (var symbol in model.AllSymbols)
        {
            if (symbol.Name != name)
                continue;
            if (symbol.Kind is SymbolKind.Class or SymbolKind.Struct
                or SymbolKind.Interface or SymbolKind.Trait or SymbolKind.Enum)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Looks up a member by name in a type's member scope, resolving constructed generic
    /// types to their generic definition. Mirrors the receiver-type member lookup used by
    /// the readonly inference engine — necessary because <c>GetDeclaredSymbol</c> maps only
    /// declaration nodes, not the usage-site member-access nodes this verifier inspects.
    /// </summary>
    private static ISymbol? LookupMember(IType type, string memberName)
    {
        var scope = MemberScopeOf(type);
        return scope?.Lookup(memberName);
    }

    /// <summary>
    /// Returns <c>true</c> if the symbol is a static field or static property — the global
    /// state whose aliasing into an owned graph LD-2 forbids.
    /// </summary>
    private static bool IsStaticMember(ISymbol? symbol)
    {
        return symbol is Symbol sym
            && sym.IsStatic
            && sym.Kind is SymbolKind.Field or SymbolKind.Property;
    }

    // ── Parameter seeding (mirrors OwnershipAnalyzer.RegisterOwnedParameters) ──

    /// <summary>
    /// Registers all owned parameters of a method/constructor as Live in the flow state so a
    /// move from an owned parameter into a field resolves correctly (Req 6 AC 3).
    /// </summary>
    private static void RegisterOwnedParameters(SyntaxNode declaringNode, ISemanticModel model, MethodFlowState state)
    {
        ParameterListNode? paramList = declaringNode switch
        {
            MethodDeclarationNode m => m.Parameters,
            ConstructorDeclarationNode c => c.Parameters,
            _ => null,
        };

        if (paramList is null)
            return;

        foreach (var child in paramList.Children())
        {
            if (child is ParameterNode paramNode)
            {
                var paramSymbol = model.GetDeclaredSymbol(paramNode);
                if (paramSymbol is Symbol sym && sym.OwnershipTier == OwnershipTier.Owned)
                {
                    state.VariableStates[paramSymbol] = VariableOwnershipState.Live;
                    state.BindingKinds[paramSymbol] = BindingKind.Owned;
                }
            }
        }
    }

    // ── Type helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Returns <c>true</c> if the given type is declared as an owned type (owned class or
    /// struct). For constructed generic types the generic definition is consulted.
    /// </summary>
    private static bool IsOwnedType(IType type)
    {
        var resolved = type is ConstructedType constructed ? constructed.GenericDefinition : type;
        return resolved switch
        {
            ClassType ct => ct.IsOwnedType,
            StructType st => st.IsOwnedType,
            _ => false,
        };
    }

    /// <summary>
    /// Returns the member scope for a class or struct type, or null for other type kinds.
    /// </summary>
    private static IScope? MemberScopeOf(IType type)
    {
        var resolved = type is ConstructedType constructed ? constructed.GenericDefinition : type;
        return resolved switch
        {
            ClassType ct => ct.MemberScope,
            StructType st => st.MemberScope,
            _ => null,
        };
    }

    // ── Source / diagnostic plumbing (mirrors MoveBoundaryResolver) ─────────

    /// <summary>
    /// Extracts the source text covered by a node's span for embedding in the diagnostic
    /// message. Returns a placeholder when the span cannot be resolved.
    /// </summary>
    /// <summary>
    /// Renders a readable name for a source expression directly from the syntax tree, for
    /// embedding in the diagnostic message. Source spans are local to each compilation unit
    /// and a node does not carry its owning unit, so slicing source text by span is
    /// unreliable across the concatenated stdlib + user ASTs; rendering from identifiers and
    /// member-access suffixes avoids that entirely. Handles the common contract-source
    /// shapes — a bare identifier (<c>input</c>), a member access (<c>Registry.Shared</c>),
    /// and <c>this.Field</c> — and falls back to a placeholder otherwise.
    /// </summary>
    private static string GetNodeText(SyntaxNode node)
    {
        var rendered = RenderExpression(node);
        return string.IsNullOrEmpty(rendered) ? "<expression>" : rendered;
    }

    /// <summary>
    /// Builds a dotted path string from an expression's primary target and its member-access
    /// suffixes (e.g. <c>Registry.Shared</c>, <c>this.Setting</c>, or a bare <c>input</c>).
    /// Returns the empty string when the expression is not a simple identifier/member-access
    /// chain.
    /// </summary>
    private static string RenderExpression(SyntaxNode node)
    {
        switch (node)
        {
            case ExpressionNode expr:
                return expr.AsAssignmentOrNull() is { } exprAssignment ? RenderExpression(exprAssignment) : "";

            case AssignmentNode assignment:
                return assignment.Value is null ? RenderExpression(assignment.Target) : "";

            case IdentifierExpressionPrimaryNode identifier:
                return identifier.Name.Lexeme;

            case ThisExpressionPrimaryNode:
                return "this";

            case PostfixNode postfix:
            {
                var baseText = RenderExpression(postfix.Target);
                if (baseText.Length == 0)
                    return "";

                foreach (var suffix in postfix.Suffix)
                {
                    if (suffix is MemberAccessSuffixPostfixSuffixNode memberAccess)
                        baseText += "." + memberAccess.Name.Lexeme;
                    else
                        return ""; // calls/indexers/etc. — not a simple source path
                }

                return baseText;
            }
        }

        // Walk single-child wrapper chains (the operator-precedence node tower).
        SyntaxNode? singleChild = null;
        var childCount = 0;
        foreach (var child in node.Children())
        {
            if (child is null)
                continue;
            childCount++;
            singleChild = child;
            if (childCount > 1)
                break;
        }

        return childCount == 1 && singleChild is not null ? RenderExpression(singleChild) : "";
    }

    /// <summary>
    /// Creates an analysis diagnostic with source location resolved from the AST node span.
    /// Mirrors <c>OwnershipAnalyzer.MakeDiagnostic</c> / <c>MoveBoundaryResolver.MakeDiagnostic</c>
    /// so the verifier's diagnostics carry the same location and analyzer attribution.
    /// </summary>
    private static Diagnostic MakeDiagnostic(
        DiagnosticId id,
        string message,
        SyntaxNode node,
        ISemanticModel model)
    {
        Common.Text.LinePositionSpan? location = null;
        string? compilationUnitPath = null;

        var span = node.Span;
        if (span.Length > 0)
        {
            var asts = model.Asts;
            var units = model.Units;
            var astIndex = -1;

            for (var i = 0; i < asts.Length; i++)
            {
                var astSpan = asts[i].Span;
                if (span.Start >= astSpan.Start && span.Start + span.Length <= astSpan.Start + astSpan.Length)
                {
                    astIndex = i;
                    break;
                }
            }

            if (astIndex >= 0 && astIndex < units.Count)
            {
                var source = units[astIndex].Source;
                compilationUnitPath = source.Path;

                var sourceSpan = new Common.Text.SourceSpan(span.Start, span.Length);
                location = source.ToLinePositionSpan(sourceSpan);
            }
        }

        return new Diagnostic(
            id,
            DiagnosticSeverity.Error,
            PipelineStage.Analysis,
            message,
            Location: location ?? default,
            CompilationUnitPath: compilationUnitPath ?? "",
            AnalyzerName: "Ownership");
    }
}
