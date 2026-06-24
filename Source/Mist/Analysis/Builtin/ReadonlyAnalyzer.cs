using Mist.Analysis.Contracts;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.Analysis.Builtin;

/// <summary>
/// Built-in analyzer that enforces readonly context rules against the semantic model.
/// Walks type declarations and their members, validating that readonly contexts
/// only perform permitted operations (field reads, readonly method calls, etc.)
/// and prohibit mutations (field assignments, non-readonly method calls, etc.).
/// </summary>
public sealed class ReadonlyAnalyzer : IAnalyzer
{
    public string AnalyzerId => "readonly-context";

    // ── Diagnostic IDs ───────────────────────────────────────────────────
    internal static readonly DiagnosticId FieldAssignmentInReadonlyContext = new("MIST_RO_0001");
    internal static readonly DiagnosticId NonReadonlyMethodCallInReadonlyContext = new("MIST_RO_0002");
    internal static readonly DiagnosticId NonReadonlyGetterCallInReadonlyContext = new("MIST_RO_0003");
    internal static readonly DiagnosticId SetterCallInReadonlyContext = new("MIST_RO_0004");
    internal static readonly DiagnosticId NonReadonlyConstructorAllocation = new("MIST_RO_0005");
    internal static readonly DiagnosticId NonReadonlyMethodOnReadonlyParam = new("MIST_RO_0006");
    internal static readonly DiagnosticId FieldAssignmentOnReadonlyParam = new("MIST_RO_0007");
    internal static readonly DiagnosticId SetterCallOnReadonlyParam = new("MIST_RO_0008");
    internal static readonly DiagnosticId ReadonlyFieldAssignmentOutsideConstructor = new("MIST_RO_0009");
    internal static readonly DiagnosticId FieldAssignmentInReadonlyBlock = new("MIST_RO_0010");
    internal static readonly DiagnosticId ClosedUnknownNonReadonlyAccess = new("MIST_TC_0062");

    /// <summary>
    /// Tracks the current enforcement mode as the analyzer walks into
    /// readonly methods, getters, setters, constructors, and blocks.
    /// </summary>
    private enum ContextKind
    {
        /// <summary>No readonly restrictions.</summary>
        Normal,

        /// <summary>Readonly method body — base rules only.</summary>
        ReadonlyMethod,

        /// <summary>Readonly property/indexer getter — base rules only.</summary>
        ReadonlyGetter,

        /// <summary>Readonly property setter — base rules + backing field exemption.</summary>
        ReadonlySetter,

        /// <summary>Readonly indexer setter — base rules only (no backing field exemption).</summary>
        ReadonlyIndexerSetter,

        /// <summary>Readonly type constructor — base rules + constructor privilege.</summary>
        ReadonlyConstructor,

        /// <summary>Explicit readonly block — base rules only (overrides constructor privilege).</summary>
        ReadonlyBlock,
    }

    /// <summary>
    /// An entry on the context stack, carrying both the enforcement mode and
    /// (for constructor contexts) the declaring type's member scope so that
    /// constructor privilege can be evaluated.
    /// </summary>
    private readonly record struct ContextEntry(ContextKind Kind, IScope? DeclaringTypeScope = null);

    public void Analyze(ISemanticModel model, DiagnosticBag diagnostics)
    {
        var contextStack = new Stack<ContextEntry>();
        contextStack.Push(new ContextEntry(ContextKind.Normal));

        foreach (var symbol in model.AllSymbols)
        {
            if (symbol.Origin != SymbolOrigin.UserCode)
                continue;

            switch (symbol.Kind)
            {
                case SymbolKind.Method:
                case SymbolKind.Property:
                case SymbolKind.Indexer:
                case SymbolKind.Constructor:
                    AnalyzeMember(symbol, model, diagnostics, contextStack);
                    break;
            }
        }

        // Second pass: check for readonly field assignments outside constructors.
        // This applies to ALL methods/properties/indexers regardless of their own readonly status.
        foreach (var symbol in model.AllSymbols)
        {
            if (symbol.Origin != SymbolOrigin.UserCode)
                continue;

            switch (symbol.Kind)
            {
                case SymbolKind.Method:
                case SymbolKind.Property:
                case SymbolKind.Indexer:
                    AnalyzeReadonlyFieldAssignments(symbol, model, diagnostics);
                    break;
            }
        }

        // Third pass: check for operations on readonly parameters.
        // This applies to ALL methods/constructors regardless of their own readonly status.
        foreach (var symbol in model.AllSymbols)
        {
            if (symbol.Origin != SymbolOrigin.UserCode)
                continue;

            switch (symbol.Kind)
            {
                case SymbolKind.Method:
                case SymbolKind.Constructor:
                    AnalyzeReadonlyParameters(symbol, model, diagnostics);
                    break;
            }
        }

        // Fourth pass: check for member access on values whose type is a type parameter
        // with a readonly constraint. When T has `where T : readonly`, all member access
        // on values of type T is treated as if the receiver is in a readonly context.
        foreach (var symbol in model.AllSymbols)
        {
            if (symbol.Origin != SymbolOrigin.UserCode)
                continue;

            switch (symbol.Kind)
            {
                case SymbolKind.Method:
                case SymbolKind.Property:
                case SymbolKind.Indexer:
                case SymbolKind.Constructor:
                    AnalyzeReadonlyConstrainedTypeParamAccess(symbol, model, diagnostics);
                    break;
            }
        }

        // Fifth pass: check for operations on values whose type is a
        // Closed_Generic_With_Unknown. When a variable's resolved type contains
        // unknown at any type argument position (and no open type parameters),
        // all member access is treated as if the receiver is in a readonly context.
        foreach (var symbol in model.AllSymbols)
        {
            if (symbol.Origin != SymbolOrigin.UserCode)
                continue;

            switch (symbol.Kind)
            {
                case SymbolKind.Method:
                case SymbolKind.Property:
                case SymbolKind.Indexer:
                case SymbolKind.Constructor:
                    AnalyzeClosedGenericWithUnknownAccess(symbol, model, diagnostics);
                    break;
            }
        }
    }

    private static void AnalyzeMember(
        ISymbol symbol,
        ISemanticModel model,
        DiagnosticBag diagnostics,
        Stack<ContextEntry> contextStack)
    {
        // Determine the readonly status of this member.
        var readonlyStatus = GetReadonlyStatus(symbol);

        // Only enforce readonly rules on members that are actually readonly.
        if (readonlyStatus == ReadonlyStatus.Mutable || readonlyStatus == ReadonlyStatus.Unknown)
            return;

        // Get the declaring AST node.
        SyntaxNode declaringNode;
        try { declaringNode = model.GetDeclaringNode(symbol); }
        catch { return; }

        // Walk the body based on the member kind.
        switch (symbol.Kind)
        {
            case SymbolKind.Method:
                AnalyzeMethodBody(declaringNode, ContextKind.ReadonlyMethod, null, model, diagnostics, contextStack);
                break;

            case SymbolKind.Constructor:
                // For constructors, resolve the declaring type's member scope for constructor privilege.
                var declaringTypeScope = GetDeclaringTypeScope(symbol);
                AnalyzeConstructorBody(declaringNode, ContextKind.ReadonlyConstructor, declaringTypeScope, model, diagnostics, contextStack);
                break;

            case SymbolKind.Property:
                AnalyzePropertyBody(declaringNode, model, diagnostics, contextStack);
                break;

            case SymbolKind.Indexer:
                AnalyzeIndexerBody(declaringNode, model, diagnostics, contextStack);
                break;
        }
    }

    // ── Readonly field assignment outside constructors (MIST_RO_0009) ────

    /// <summary>
    /// Walks a non-constructor member's body looking for assignments to readonly fields.
    /// Any such assignment emits MIST_RO_0009 because readonly fields can only be assigned
    /// within a constructor of the declaring type.
    /// </summary>
    private static void AnalyzeReadonlyFieldAssignments(
        ISymbol symbol,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Get the declaring type's member scope so we can look up field readonly status.
        var declaringTypeScope = GetDeclaringTypeScope(symbol);

        // Get the declaring AST node.
        SyntaxNode declaringNode;
        try { declaringNode = model.GetDeclaringNode(symbol); }
        catch { return; }

        // Collect all body blocks to walk based on member kind.
        switch (symbol.Kind)
        {
            case SymbolKind.Method:
                if (declaringNode is MethodDeclarationNode methodDecl)
                {
                    var body = methodDecl.Body.GetBlock();
                    if (body is not null)
                        WalkBlockForReadonlyFieldAssignments(body, declaringTypeScope, model, diagnostics);
                }
                break;

            case SymbolKind.Property:
                WalkPropertyForReadonlyFieldAssignments(declaringNode, declaringTypeScope, model, diagnostics);
                break;

            case SymbolKind.Indexer:
                WalkIndexerForReadonlyFieldAssignments(declaringNode, declaringTypeScope, model, diagnostics);
                break;
        }
    }

    private static void WalkPropertyForReadonlyFieldAssignments(
        SyntaxNode declaringNode,
        IScope? declaringTypeScope,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        switch (declaringNode)
        {
            case BlockPropertyPropertyDeclarationNode blockProp:
                foreach (var accessor in blockProp.Accessors)
                {
                    switch (accessor)
                    {
                        case BlockGetAccessorAccessorDeclarationNode blockGet:
                            WalkBlockForReadonlyFieldAssignments(blockGet.Body, declaringTypeScope, model, diagnostics);
                            break;
                        case ExpressionGetAccessorAccessorDeclarationNode exprGet:
                            WalkNodeForReadonlyFieldAssignments(exprGet.Expression, declaringTypeScope, model, diagnostics);
                            break;
                        case BlockSetAccessorAccessorDeclarationNode blockSet:
                            WalkBlockForReadonlyFieldAssignments(blockSet.Body, declaringTypeScope, model, diagnostics);
                            break;
                        case ExpressionSetAccessorAccessorDeclarationNode exprSet:
                            WalkNodeForReadonlyFieldAssignments(exprSet.Expression, declaringTypeScope, model, diagnostics);
                            break;
                        case BlockInitAccessorAccessorDeclarationNode blockInit:
                            WalkBlockForReadonlyFieldAssignments(blockInit.Body, declaringTypeScope, model, diagnostics);
                            break;
                        case ExpressionInitAccessorAccessorDeclarationNode exprInit:
                            WalkNodeForReadonlyFieldAssignments(exprInit.Expression, declaringTypeScope, model, diagnostics);
                            break;
                    }
                }
                break;

            case ExpressionBodiedPropertyPropertyDeclarationNode exprProp:
                WalkNodeForReadonlyFieldAssignments(exprProp.Body, declaringTypeScope, model, diagnostics);
                break;
        }
    }

    private static void WalkIndexerForReadonlyFieldAssignments(
        SyntaxNode declaringNode,
        IScope? declaringTypeScope,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        if (declaringNode is not IndexerDeclarationNode { Body: BlockIndexerBodyIndexerBodyNode blockBody } blockIndexer)
            return;

        foreach (var accessor in blockBody.Accessors)
        {
            switch (accessor)
            {
                case BlockGetAccessorAccessorDeclarationNode blockGet:
                    WalkBlockForReadonlyFieldAssignments(blockGet.Body, declaringTypeScope, model, diagnostics);
                    break;
                case ExpressionGetAccessorAccessorDeclarationNode exprGet:
                    WalkNodeForReadonlyFieldAssignments(exprGet.Expression, declaringTypeScope, model, diagnostics);
                    break;
                case BlockSetAccessorAccessorDeclarationNode blockSet:
                    WalkBlockForReadonlyFieldAssignments(blockSet.Body, declaringTypeScope, model, diagnostics);
                    break;
                case ExpressionSetAccessorAccessorDeclarationNode exprSet:
                    WalkNodeForReadonlyFieldAssignments(exprSet.Expression, declaringTypeScope, model, diagnostics);
                    break;
                case BlockInitAccessorAccessorDeclarationNode blockInit:
                    WalkBlockForReadonlyFieldAssignments(blockInit.Body, declaringTypeScope, model, diagnostics);
                    break;
                case ExpressionInitAccessorAccessorDeclarationNode exprInit:
                    WalkNodeForReadonlyFieldAssignments(exprInit.Expression, declaringTypeScope, model, diagnostics);
                    break;
            }
        }
    }

    private static void WalkBlockForReadonlyFieldAssignments(
        BlockNode block,
        IScope? declaringTypeScope,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        foreach (var stmt in block.Statements)
        {
            WalkStatementForReadonlyFieldAssignments(stmt, declaringTypeScope, model, diagnostics);
        }
    }

    private static void WalkStatementForReadonlyFieldAssignments(
        StatementNode stmt,
        IScope? declaringTypeScope,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        switch (stmt)
        {
            case VariableDeclarationStatementStatementNode varDecl:
                if (varDecl.VariableDeclaration is { } vd)
                    WalkNodeForReadonlyFieldAssignments(vd, declaringTypeScope, model, diagnostics);
                break;

            case ReturnStatementStatementNode returnStmt:
                if (returnStmt.ReturnStatement is { } rs)
                    WalkNodeForReadonlyFieldAssignments(rs, declaringTypeScope, model, diagnostics);
                break;

            case IfStatementStatementNode ifStmt:
                WalkNodeForReadonlyFieldAssignments(ifStmt.IfStatement, declaringTypeScope, model, diagnostics);
                break;

            case WhileStatementStatementNode whileStmt:
                WalkNodeForReadonlyFieldAssignments(whileStmt.WhileStatement, declaringTypeScope, model, diagnostics);
                break;

            case ForStatementStatementNode forStmt:
                WalkNodeForReadonlyFieldAssignments(forStmt.ForStatement, declaringTypeScope, model, diagnostics);
                break;

            case ForeachStatementStatementNode foreachStmt:
                WalkNodeForReadonlyFieldAssignments(foreachStmt.ForeachStatement, declaringTypeScope, model, diagnostics);
                break;

            case ExpressionStatementStatementNode exprStmt:
                WalkNodeForReadonlyFieldAssignments(exprStmt.ExpressionStatement, declaringTypeScope, model, diagnostics);
                break;

            case ReadonlyBlockStatementStatementNode readonlyBlockStmt:
                WalkBlockForReadonlyFieldAssignments(readonlyBlockStmt.ReadonlyBlock.Body, declaringTypeScope, model, diagnostics);
                break;

            case BlockStatementStatementNode blockStmt:
                WalkBlockForReadonlyFieldAssignments(blockStmt.BlockStatement, declaringTypeScope, model, diagnostics);
                break;

            case BreakStatementStatementNode:
                break;

            default:
                WalkChildrenForReadonlyFieldAssignments(stmt, declaringTypeScope, model, diagnostics);
                break;
        }
    }

    private static void WalkNodeForReadonlyFieldAssignments(
        SyntaxNode node,
        IScope? declaringTypeScope,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Check if this node is an assignment to a readonly field.
        if (node is AssignmentNode assignment && assignment.Value is not null)
        {
            CheckReadonlyFieldAssignmentOutsideConstructor(assignment, declaringTypeScope, model, diagnostics);
        }

        // Recursively walk children.
        WalkChildrenForReadonlyFieldAssignments(node, declaringTypeScope, model, diagnostics);
    }

    private static void WalkChildrenForReadonlyFieldAssignments(
        SyntaxNode node,
        IScope? declaringTypeScope,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        foreach (var child in node.Children())
        {
            if (child is not null)
                WalkNodeForReadonlyFieldAssignments(child, declaringTypeScope, model, diagnostics);
        }
    }

    /// <summary>
    /// Checks if an assignment targets a readonly field or a readonly property/indexer setter.
    /// If it targets a readonly field, emits MIST_RO_0009.
    /// If it targets a readonly property/indexer setter, emits MIST_RO_0004.
    /// This is called only for non-constructor contexts.
    /// </summary>
    private static void CheckReadonlyFieldAssignmentOutsideConstructor(
        AssignmentNode assignment,
        IScope? declaringTypeScope,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        var targetInfo = ClassifyAssignmentTarget(assignment.Target, model, declaringTypeScope);

        switch (targetInfo.TargetKind)
        {
            case AssignmentTargetKind.Field:
                CheckReadonlyFieldOutsideConstructor(targetInfo, assignment, declaringTypeScope, model, diagnostics);
                break;

            case AssignmentTargetKind.PropertyOrIndexerSetter:
                CheckReadonlyPropertySetterOutsideConstructor(targetInfo, assignment, declaringTypeScope, model, diagnostics);
                break;
        }
    }

    /// <summary>
    /// Checks if an assignment targets a readonly field outside a constructor. If so, emits MIST_RO_0009.
    /// </summary>
    private static void CheckReadonlyFieldOutsideConstructor(
        AssignmentTargetInfo targetInfo,
        AssignmentNode assignment,
        IScope? declaringTypeScope,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        if (targetInfo.MemberName is null)
            return;

        // Skip the backing field keyword — it's handled by the backing field exemption
        // and the setter can only be called from a constructor (enforced by MIST_RO_0004).
        if (targetInfo.IsBackingFieldKeyword)
            return;

        // Look up the field in the declaring type's scope to check its readonly status.
        if (declaringTypeScope is null)
            return;

        // Only check fields on `this` (the declaring type's own fields).
        if (!targetInfo.IsOnThis)
            return;

        var fieldSymbol = declaringTypeScope.Lookup(targetInfo.MemberName);
        if (fieldSymbol is not Symbol sym)
            return;

        if (sym.Kind != SymbolKind.Field)
            return;

        if (!IsReadonly(sym.ReadonlyStatus))
            return;

        // The field is readonly and we're outside a constructor — emit MIST_RO_0009.
        diagnostics.Add(MakeDiagnostic(
            ReadonlyFieldAssignmentOutsideConstructor,
            $"Cannot assign to readonly field '{targetInfo.MemberName}' outside of a constructor",
            assignment,
            model));
    }

    /// <summary>
    /// Checks if an assignment targets a readonly property/indexer setter outside a constructor
    /// of the declaring type. If so, emits MIST_RO_0004.
    /// </summary>
    private static void CheckReadonlyPropertySetterOutsideConstructor(
        AssignmentTargetInfo targetInfo,
        AssignmentNode assignment,
        IScope? declaringTypeScope,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        if (targetInfo.MemberName is null)
            return;

        // Look up the property/indexer in the declaring type's scope to check its readonly status.
        if (declaringTypeScope is null)
            return;

        // Only check properties/indexers on `this` (the declaring type's own members).
        if (!targetInfo.IsOnThis)
            return;

        var memberSymbol = declaringTypeScope.Lookup(targetInfo.MemberName);
        if (memberSymbol is not Symbol sym)
            return;

        if (sym.Kind is not (SymbolKind.Property or SymbolKind.Indexer))
            return;

        if (!IsReadonly(sym.ReadonlyStatus))
            return;

        // The property/indexer is readonly and we're outside a constructor — emit MIST_RO_0004.
        diagnostics.Add(MakeDiagnostic(
            SetterCallInReadonlyContext,
            $"Cannot call property setter '{targetInfo.MemberName}' within a readonly context",
            assignment,
            model));
    }

    // ── Readonly parameter analysis (MIST_RO_0006, MIST_RO_0007, MIST_RO_0008) ──

    /// <summary>
    /// Walks a method or constructor body looking for operations on readonly parameters.
    /// A readonly parameter is treated as a "readonly receiver" — calling non-readonly methods,
    /// assigning to fields, or calling non-readonly setters on it is prohibited.
    /// </summary>
    private static void AnalyzeReadonlyParameters(
        ISymbol symbol,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Get the declaring AST node.
        SyntaxNode declaringNode;
        try { declaringNode = model.GetDeclaringNode(symbol); }
        catch { return; }

        // Extract readonly parameter flags and names.
        var readonlyFlags = ParameterSignature.ExtractIsReadonly(symbol, model);
        if (readonlyFlags.Count == 0)
            return;

        // Check if there are any readonly parameters at all.
        var hasReadonlyParam = false;
        for (var i = 0; i < readonlyFlags.Count; i++)
        {
            if (readonlyFlags[i])
            {
                hasReadonlyParam = true;
                break;
            }
        }

        if (!hasReadonlyParam)
            return;

        // Extract parameter names from the declaring node's parameter list.
        var readonlyParamNames = ExtractReadonlyParameterNames(declaringNode, readonlyFlags);
        if (readonlyParamNames.Count == 0)
            return;

        // Walk the body looking for operations on readonly parameters.
        switch (declaringNode)
        {
            case MethodDeclarationNode methodDecl:
                var methodBody = methodDecl.Body.GetBlock();
                if (methodBody is not null)
                    WalkBlockForReadonlyParamViolations(methodBody, readonlyParamNames, model, diagnostics);
                break;

            case ConstructorDeclarationNode ctorDecl:
                var ctorBody = ctorDecl.Body.GetBlock();
                if (ctorBody is not null)
                    WalkBlockForReadonlyParamViolations(ctorBody, readonlyParamNames, model, diagnostics);
                break;
        }
    }

    /// <summary>
    /// Extracts the names of readonly parameters from a declaring node's parameter list.
    /// </summary>
    private static HashSet<string> ExtractReadonlyParameterNames(
        SyntaxNode declaringNode,
        IReadOnlyList<bool> readonlyFlags)
    {
        var paramList = declaringNode switch
        {
            MethodDeclarationNode method => method.Parameters,
            ConstructorDeclarationNode ctor => ctor.Parameters,
            _ => null,
        };

        var names = new HashSet<string>(StringComparer.Ordinal);
        if (paramList is null)
            return names;

        var index = 0;

        // First parameter
        if (index < readonlyFlags.Count && readonlyFlags[index])
        {
            var name = paramList.Parameter.Name.Lexeme;
            if (!string.IsNullOrEmpty(name))
                names.Add(name);
        }
        index++;

        // Remaining parameters
        foreach (var param in paramList.Parameters)
        {
            if (index < readonlyFlags.Count && readonlyFlags[index])
            {
                var name = param.Name.Lexeme;
                if (!string.IsNullOrEmpty(name))
                    names.Add(name);
            }
            index++;
        }

        return names;
    }

    private static void WalkBlockForReadonlyParamViolations(
        BlockNode block,
        HashSet<string> readonlyParamNames,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        foreach (var stmt in block.Statements)
        {
            WalkStatementForReadonlyParamViolations(stmt, readonlyParamNames, model, diagnostics);
        }
    }

    private static void WalkStatementForReadonlyParamViolations(
        StatementNode stmt,
        HashSet<string> readonlyParamNames,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        switch (stmt)
        {
            case VariableDeclarationStatementStatementNode varDecl:
                if (varDecl.VariableDeclaration is { } vd)
                    WalkNodeForReadonlyParamViolations(vd, readonlyParamNames, model, diagnostics);
                break;

            case ReturnStatementStatementNode returnStmt:
                if (returnStmt.ReturnStatement is { } rs)
                    WalkNodeForReadonlyParamViolations(rs, readonlyParamNames, model, diagnostics);
                break;

            case IfStatementStatementNode ifStmt:
                WalkNodeForReadonlyParamViolations(ifStmt.IfStatement, readonlyParamNames, model, diagnostics);
                break;

            case WhileStatementStatementNode whileStmt:
                WalkNodeForReadonlyParamViolations(whileStmt.WhileStatement, readonlyParamNames, model, diagnostics);
                break;

            case ForStatementStatementNode forStmt:
                WalkNodeForReadonlyParamViolations(forStmt.ForStatement, readonlyParamNames, model, diagnostics);
                break;

            case ForeachStatementStatementNode foreachStmt:
                WalkNodeForReadonlyParamViolations(foreachStmt.ForeachStatement, readonlyParamNames, model, diagnostics);
                break;

            case ExpressionStatementStatementNode exprStmt:
                WalkNodeForReadonlyParamViolations(exprStmt.ExpressionStatement, readonlyParamNames, model, diagnostics);
                break;

            case ReadonlyBlockStatementStatementNode readonlyBlockStmt:
                WalkBlockForReadonlyParamViolations(readonlyBlockStmt.ReadonlyBlock.Body, readonlyParamNames, model, diagnostics);
                break;

            case BlockStatementStatementNode blockStmt:
                WalkBlockForReadonlyParamViolations(blockStmt.BlockStatement, readonlyParamNames, model, diagnostics);
                break;

            case BreakStatementStatementNode:
                break;

            default:
                WalkChildrenForReadonlyParamViolations(stmt, readonlyParamNames, model, diagnostics);
                break;
        }
    }

    private static void WalkNodeForReadonlyParamViolations(
        SyntaxNode node,
        HashSet<string> readonlyParamNames,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Check this node for readonly parameter violations.
        CheckNodeForReadonlyParamViolations(node, readonlyParamNames, model, diagnostics);

        // Recursively walk children.
        WalkChildrenForReadonlyParamViolations(node, readonlyParamNames, model, diagnostics);
    }

    private static void WalkChildrenForReadonlyParamViolations(
        SyntaxNode node,
        HashSet<string> readonlyParamNames,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        foreach (var child in node.Children())
        {
            if (child is not null)
                WalkNodeForReadonlyParamViolations(child, readonlyParamNames, model, diagnostics);
        }
    }

    /// <summary>
    /// Checks a node for violations of readonly parameter rules:
    /// - Method call on a readonly parameter where the method is not readonly → MIST_RO_0006
    /// - Field assignment on a readonly parameter → MIST_RO_0007
    /// - Setter call on a readonly parameter where the setter is not readonly → MIST_RO_0008
    /// </summary>
    private static void CheckNodeForReadonlyParamViolations(
        SyntaxNode node,
        HashSet<string> readonlyParamNames,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        switch (node)
        {
            // Check assignments: `param.field = value` → MIST_RO_0007
            // Check assignments: `param.Prop = value` → MIST_RO_0008
            case AssignmentNode assignment when assignment.Value is not null:
                CheckReadonlyParamAssignment(assignment, readonlyParamNames, model, diagnostics);
                break;

            // Check method calls and member access on PostfixNode patterns.
            // Pattern: identifier.Method(...) or identifier.Property
            case PostfixNode postfix:
                CheckReadonlyParamPostfix(postfix, readonlyParamNames, model, diagnostics);
                break;
        }
    }

    /// <summary>
    /// Checks if an assignment targets a field or property on a readonly parameter.
    /// </summary>
    private static void CheckReadonlyParamAssignment(
        AssignmentNode assignment,
        HashSet<string> readonlyParamNames,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Look for pattern: param.member = value
        // The target is wrapped in expression nodes (LogicalOrNode → ... → PostfixNode).
        var postfix = FindPostfixNode(assignment.Target);
        if (postfix is null)
            return;

        // The receiver must be a bare identifier (the parameter name).
        var receiverName = GetReceiverParameterName(postfix);
        if (receiverName is null || !readonlyParamNames.Contains(receiverName))
            return;

        // Get the last suffix which is the member being assigned to.
        if (postfix.Suffix.Length == 0)
            return;

        var lastSuffix = postfix.Suffix[^1];
        if (lastSuffix is not MemberAccessSuffixPostfixSuffixNode memberAccess)
            return;

        var memberName = memberAccess.Name.Lexeme;

        // Resolve the member to determine if it's a field or property/indexer.
        var symbol = model.GetDeclaredSymbol(memberAccess);
        if (symbol is Symbol sym)
        {
            if (sym.Kind == SymbolKind.Field)
            {
                // Field assignment on readonly parameter → MIST_RO_0007
                diagnostics.Add(MakeDiagnostic(
                    FieldAssignmentOnReadonlyParam,
                    $"Cannot assign to field '{memberName}' on readonly parameter '{receiverName}'",
                    assignment,
                    model));
                return;
            }

            if (sym.Kind is SymbolKind.Property or SymbolKind.Indexer)
            {
                // Setter call on readonly parameter — check if the setter is readonly.
                if (!IsReadonly(sym.ReadonlyStatus))
                {
                    // Non-readonly setter on readonly parameter → MIST_RO_0008
                    diagnostics.Add(MakeDiagnostic(
                        SetterCallOnReadonlyParam,
                        $"Cannot call property setter '{memberName}' on readonly parameter '{receiverName}'",
                        assignment,
                        model));
                }
                return;
            }
        }

        // If we can't resolve the symbol, conservatively treat it as a field assignment.
        diagnostics.Add(MakeDiagnostic(
            FieldAssignmentOnReadonlyParam,
            $"Cannot assign to field '{memberName}' on readonly parameter '{receiverName}'",
            assignment,
            model));
    }

    /// <summary>
    /// Recursively walks through expression wrapper nodes to find the underlying PostfixNode.
    /// </summary>
    private static PostfixNode? FindPostfixNode(SyntaxNode node)
    {
        if (node is PostfixNode postfix)
            return postfix;

        foreach (var child in node.Children())
        {
            if (child is not null)
            {
                var result = FindPostfixNode(child);
                if (result is not null)
                    return result;
            }
        }

        return null;
    }

    /// <summary>
    /// Checks if a postfix expression calls a non-readonly method on a readonly parameter.
    /// Pattern: param.Method(...)
    /// </summary>
    private static void CheckReadonlyParamPostfix(
        PostfixNode postfix,
        HashSet<string> readonlyParamNames,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // The receiver must be a bare identifier (the parameter name).
        if (postfix.Target is not IdentifierExpressionPrimaryNode identifier)
            return;

        var receiverName = identifier.Name.Lexeme;
        if (!readonlyParamNames.Contains(receiverName))
            return;

        // Check each suffix for method calls on the readonly parameter.
        for (var i = 0; i < postfix.Suffix.Length; i++)
        {
            var suffix = postfix.Suffix[i];

            if (suffix is MethodCallSuffixPostfixSuffixNode methodCall)
            {
                // Only check the first suffix that operates directly on the parameter.
                // Subsequent suffixes operate on the result of the first call.
                if (i == 0)
                {
                    var symbol = model.GetDeclaredSymbol(methodCall);
                    if (symbol is Symbol sym && !IsReadonly(sym.ReadonlyStatus))
                    {
                        diagnostics.Add(MakeDiagnostic(
                            NonReadonlyMethodOnReadonlyParam,
                            $"Cannot call non-readonly method '{sym.Name}' on readonly parameter '{receiverName}'",
                            methodCall,
                            model));
                    }
                }
                // After a method call, the receiver is no longer the parameter — stop checking.
                break;
            }

            if (suffix is MemberAccessSuffixPostfixSuffixNode)
            {
                // Member access on the parameter — this is fine for reads (field reads, property getter reads).
                // But if the NEXT suffix is a method call, the receiver of that call is the member, not the param.
                // So we only check the first level of member access for method calls.
                // If this is the last suffix and it's just a member access (read), that's fine.
                // The assignment case is handled separately in CheckReadonlyParamAssignment.
                continue;
            }

            // For other suffix types (index, invocation), stop — the receiver changes.
            break;
        }
    }

    /// <summary>
    /// Gets the receiver parameter name from a PostfixNode if the pattern is:
    /// identifier.member (where identifier is a bare parameter name with exactly one member access suffix).
    /// Returns null if the pattern doesn't match.
    /// </summary>
    private static string? GetReceiverParameterName(PostfixNode postfix)
    {
        if (postfix.Target is not IdentifierExpressionPrimaryNode identifier)
            return null;

        // Must have at least one suffix (the member being assigned to).
        if (postfix.Suffix.Length == 0)
            return null;

        // The first suffix must be a member access (param.member pattern).
        // For `param.member = value`, Suffix.Length == 1 and it's a MemberAccessSuffix.
        // For `param.sub.member = value`, we only care about direct member access on the param.
        if (postfix.Suffix.Length == 1 && postfix.Suffix[0] is MemberAccessSuffixPostfixSuffixNode)
            return identifier.Name.Lexeme;

        return null;
    }

    // ── Body analysis entry points ───────────────────────────────────────

    private static void AnalyzeMethodBody(
        SyntaxNode declaringNode,
        ContextKind context,
        IScope? declaringTypeScope,
        ISemanticModel model,
        DiagnosticBag diagnostics,
        Stack<ContextEntry> contextStack)
    {
        if (declaringNode is not MethodDeclarationNode methodDecl)
            return;

        var body = methodDecl.Body.GetBlock();
        if (body is null)
            return;

        contextStack.Push(new ContextEntry(context, declaringTypeScope));
        WalkBlock(body, model, diagnostics, contextStack);
        contextStack.Pop();
    }

    private static void AnalyzeConstructorBody(
        SyntaxNode declaringNode,
        ContextKind context,
        IScope? declaringTypeScope,
        ISemanticModel model,
        DiagnosticBag diagnostics,
        Stack<ContextEntry> contextStack)
    {
        if (declaringNode is not ConstructorDeclarationNode ctorDecl)
            return;

        var body = ctorDecl.Body.GetBlock();
        if (body is null)
            return;

        contextStack.Push(new ContextEntry(context, declaringTypeScope));
        WalkBlock(body, model, diagnostics, contextStack);
        contextStack.Pop();
    }

    private static void AnalyzePropertyBody(
        SyntaxNode declaringNode,
        ISemanticModel model,
        DiagnosticBag diagnostics,
        Stack<ContextEntry> contextStack)
    {
        switch (declaringNode)
        {
            case BlockPropertyPropertyDeclarationNode blockProp:
                foreach (var accessor in blockProp.Accessors)
                {
                    switch (accessor)
                    {
                        case BlockGetAccessorAccessorDeclarationNode blockGet:
                            contextStack.Push(new ContextEntry(ContextKind.ReadonlyGetter));
                            WalkBlock(blockGet.Body, model, diagnostics, contextStack);
                            contextStack.Pop();
                            break;
                        case ExpressionGetAccessorAccessorDeclarationNode exprGet:
                            contextStack.Push(new ContextEntry(ContextKind.ReadonlyGetter));
                            WalkNode(exprGet.Expression, model, diagnostics, contextStack);
                            contextStack.Pop();
                            break;
                        case BlockSetAccessorAccessorDeclarationNode blockSet:
                            contextStack.Push(new ContextEntry(ContextKind.ReadonlySetter));
                            WalkBlock(blockSet.Body, model, diagnostics, contextStack);
                            contextStack.Pop();
                            break;
                        case ExpressionSetAccessorAccessorDeclarationNode exprSet:
                            contextStack.Push(new ContextEntry(ContextKind.ReadonlySetter));
                            WalkNode(exprSet.Expression, model, diagnostics, contextStack);
                            contextStack.Pop();
                            break;
                        case BlockInitAccessorAccessorDeclarationNode blockInit:
                            contextStack.Push(new ContextEntry(ContextKind.ReadonlySetter));
                            WalkBlock(blockInit.Body, model, diagnostics, contextStack);
                            contextStack.Pop();
                            break;
                        case ExpressionInitAccessorAccessorDeclarationNode exprInit:
                            contextStack.Push(new ContextEntry(ContextKind.ReadonlySetter));
                            WalkNode(exprInit.Expression, model, diagnostics, contextStack);
                            contextStack.Pop();
                            break;
                    }
                }
                break;

            case ExpressionBodiedPropertyPropertyDeclarationNode exprProp:
                contextStack.Push(new ContextEntry(ContextKind.ReadonlyGetter));
                WalkNode(exprProp.Body, model, diagnostics, contextStack);
                contextStack.Pop();
                break;
        }
    }

    private static void AnalyzeIndexerBody(
        SyntaxNode declaringNode,
        ISemanticModel model,
        DiagnosticBag diagnostics,
        Stack<ContextEntry> contextStack)
    {
        if (declaringNode is not IndexerDeclarationNode { Body: BlockIndexerBodyIndexerBodyNode blockBody } blockIndexer)
            return;

        foreach (var accessor in blockBody.Accessors)
        {
            switch (accessor)
            {
                case BlockGetAccessorAccessorDeclarationNode blockGet:
                    contextStack.Push(new ContextEntry(ContextKind.ReadonlyGetter));
                    WalkBlock(blockGet.Body, model, diagnostics, contextStack);
                    contextStack.Pop();
                    break;
                case ExpressionGetAccessorAccessorDeclarationNode exprGet:
                    contextStack.Push(new ContextEntry(ContextKind.ReadonlyGetter));
                    WalkNode(exprGet.Expression, model, diagnostics, contextStack);
                    contextStack.Pop();
                    break;
                case BlockSetAccessorAccessorDeclarationNode blockSet:
                    contextStack.Push(new ContextEntry(ContextKind.ReadonlyIndexerSetter));
                    WalkBlock(blockSet.Body, model, diagnostics, contextStack);
                    contextStack.Pop();
                    break;
                case ExpressionSetAccessorAccessorDeclarationNode exprSet:
                    contextStack.Push(new ContextEntry(ContextKind.ReadonlyIndexerSetter));
                    WalkNode(exprSet.Expression, model, diagnostics, contextStack);
                    contextStack.Pop();
                    break;
                case BlockInitAccessorAccessorDeclarationNode blockInit:
                    contextStack.Push(new ContextEntry(ContextKind.ReadonlyIndexerSetter));
                    WalkBlock(blockInit.Body, model, diagnostics, contextStack);
                    contextStack.Pop();
                    break;
                case ExpressionInitAccessorAccessorDeclarationNode exprInit:
                    contextStack.Push(new ContextEntry(ContextKind.ReadonlyIndexerSetter));
                    WalkNode(exprInit.Expression, model, diagnostics, contextStack);
                    contextStack.Pop();
                    break;
            }
        }
    }

    // ── Recursive body walking ───────────────────────────────────────────

    private static void WalkBlock(
        BlockNode block,
        ISemanticModel model,
        DiagnosticBag diagnostics,
        Stack<ContextEntry> contextStack)
    {
        foreach (var stmt in block.Statements)
        {
            WalkStatement(stmt, model, diagnostics, contextStack);
        }
    }

    private static void WalkStatement(
        StatementNode stmt,
        ISemanticModel model,
        DiagnosticBag diagnostics,
        Stack<ContextEntry> contextStack)
    {
        switch (stmt)
        {
            case VariableDeclarationStatementStatementNode varDecl:
                // Local variable declarations are always permitted (base rule 4).
                // But we still need to walk the initializer for violations.
                if (varDecl.VariableDeclaration is { } vd)
                    WalkNode(vd, model, diagnostics, contextStack);
                break;

            case ReturnStatementStatementNode returnStmt:
                if (returnStmt.ReturnStatement is { } rs)
                    WalkNode(rs, model, diagnostics, contextStack);
                break;

            case IfStatementStatementNode ifStmt:
                WalkNode(ifStmt.IfStatement, model, diagnostics, contextStack);
                break;

            case WhileStatementStatementNode whileStmt:
                WalkNode(whileStmt.WhileStatement, model, diagnostics, contextStack);
                break;

            case ForStatementStatementNode forStmt:
                WalkNode(forStmt.ForStatement, model, diagnostics, contextStack);
                break;

            case ForeachStatementStatementNode foreachStmt:
                WalkNode(foreachStmt.ForeachStatement, model, diagnostics, contextStack);
                break;

            case ExpressionStatementStatementNode exprStmt:
                WalkNode(exprStmt.ExpressionStatement, model, diagnostics, contextStack);
                break;

            case ReadonlyBlockStatementStatementNode readonlyBlockStmt:
                // Push ReadonlyBlock context — constructor privilege is suspended (exceptions 13–14).
                contextStack.Push(new ContextEntry(ContextKind.ReadonlyBlock));
                WalkBlock(readonlyBlockStmt.ReadonlyBlock.Body, model, diagnostics, contextStack);
                contextStack.Pop();
                break;

            case BlockStatementStatementNode blockStmt:
                WalkBlock(blockStmt.BlockStatement, model, diagnostics, contextStack);
                break;

            case BreakStatementStatementNode:
                // Break is always permitted.
                break;

            default:
                // Walk children for any other statement types.
                WalkChildren(stmt, model, diagnostics, contextStack);
                break;
        }
    }

    private static void WalkNode(
        SyntaxNode node,
        ISemanticModel model,
        DiagnosticBag diagnostics,
        Stack<ContextEntry> contextStack)
    {
        // Special handling for PostfixNode: if the target type is a Closed_Generic_With_Unknown,
        // skip the readonly-context violation checks on suffixes whose receiver is a
        // Closed_Generic_With_Unknown (pass 5 handles them with MIST_TC_0062 instead).
        if (node is PostfixNode postfix)
        {
            var entry = contextStack.Peek();
            if (entry.Kind != ContextKind.Normal)
            {
                var currentType = model.GetTypeOf(postfix.Target);
                if (IsClosedGenericWithUnknown(currentType))
                {
                    // Walk the target normally (it might have its own violations).
                    WalkNode(postfix.Target, model, diagnostics, contextStack);

                    // For each suffix: skip the suffix-level violation check but still
                    // walk its children (e.g., method arguments) for nested violations.
                    foreach (var suffix in postfix.Suffix)
                    {
                        WalkChildren(suffix, model, diagnostics, contextStack);

                        // Update current type to the result of this suffix.
                        // If the result is no longer a Closed_Generic_With_Unknown,
                        // subsequent suffixes should be checked normally by pass 1.
                        currentType = model.GetTypeOf(suffix);
                        if (!IsClosedGenericWithUnknown(currentType))
                        {
                            // Remaining suffixes are not on a Closed_Generic_With_Unknown
                            // receiver — fall through to normal checking for them.
                            // (This handles chained access like xs.GetEnumerator().MoveNext()
                            // where the result of GetEnumerator() is not a Closed_Generic_With_Unknown.)
                            break;
                        }
                    }

                    return;
                }
            }
        }

        // Check this node for violations.
        CheckNodeForViolations(node, model, diagnostics, contextStack);

        // Recursively walk children.
        WalkChildren(node, model, diagnostics, contextStack);
    }

    private static void WalkChildren(
        SyntaxNode node,
        ISemanticModel model,
        DiagnosticBag diagnostics,
        Stack<ContextEntry> contextStack)
    {
        foreach (var child in node.Children())
        {
            if (child is not null)
                WalkNode(child, model, diagnostics, contextStack);
        }
    }

    // ── Violation checking ───────────────────────────────────────────────

    private static void CheckNodeForViolations(
        SyntaxNode node,
        ISemanticModel model,
        DiagnosticBag diagnostics,
        Stack<ContextEntry> contextStack)
    {
        var entry = contextStack.Peek();
        if (entry.Kind == ContextKind.Normal)
            return;

        switch (node)
        {
            case AssignmentNode assignment when assignment.Value is not null:
                CheckAssignmentViolation(assignment, entry, model, diagnostics);
                break;

            case MethodCallSuffixPostfixSuffixNode methodCall:
                CheckMethodCallViolation(methodCall, entry.Kind, model, diagnostics);
                break;

            case MemberAccessSuffixPostfixSuffixNode memberAccess:
                CheckMemberAccessViolation(memberAccess, entry.Kind, model, diagnostics);
                break;

            case ObjectCreationExpressionPrimaryNode objectCreation:
                CheckObjectCreationViolation(objectCreation, entry.Kind, model, diagnostics);
                break;
        }
    }

    // ── Assignment checking (MIST_RO_0001, MIST_RO_0004) ───────────────

    private static void CheckAssignmentViolation(
        AssignmentNode assignment,
        ContextEntry entry,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        var context = entry.Kind;

        // Determine what the assignment target is: a field, a property/indexer (setter call), or a local.
        var targetInfo = ClassifyAssignmentTarget(assignment.Target, model, entry.DeclaringTypeScope);

        switch (targetInfo.TargetKind)
        {
            case AssignmentTargetKind.LocalVariable:
                // Local variable assignment — always permitted (base rule 4).
                return;

            case AssignmentTargetKind.Field:
                // Constructor privilege (exception 11): in ReadonlyConstructor, assignment to
                // own fields (fields of the declaring type via `this`) is permitted.
                // Exception 14: ReadonlyBlock overrides constructor privilege — no exemption.
                if (context == ContextKind.ReadonlyConstructor && targetInfo.IsOnThis)
                {
                    // Check that the field belongs to the declaring type.
                    if (IsOwnMember(targetInfo.MemberName, entry.DeclaringTypeScope, SymbolKind.Field))
                        return; // Permitted — constructor privilege.
                }

                // Backing field exemption (exception 12): in ReadonlySetter, assignment to
                // the backing field keyword (`field = value`) is permitted.
                if (context == ContextKind.ReadonlySetter && targetInfo.IsBackingFieldKeyword)
                    return; // Permitted — backing field exemption.

                // In ReadonlyBlock context, emit the more specific MIST_RO_0010 diagnostic
                // indicating that constructor privilege is suspended.
                if (context == ContextKind.ReadonlyBlock)
                {
                    diagnostics.Add(MakeDiagnostic(
                        FieldAssignmentInReadonlyBlock,
                        $"Cannot assign to field '{targetInfo.MemberName}' within a readonly block (constructor privilege is suspended)",
                        assignment,
                        model));
                    break;
                }

                // In all other readonly contexts, field assignment is prohibited (base rule 6).
                diagnostics.Add(MakeDiagnostic(
                    FieldAssignmentInReadonlyContext,
                    $"Cannot assign to field '{targetInfo.MemberName}' within a readonly context",
                    assignment,
                    model));
                break;

            case AssignmentTargetKind.PropertyOrIndexerSetter:
                // Constructor privilege (exception 11): in ReadonlyConstructor, calling
                // the declaring type's own readonly property/indexer setters on `this` is permitted.
                if (context == ContextKind.ReadonlyConstructor && targetInfo.IsOnThis)
                {
                    if (IsOwnReadonlyPropertyOrIndexer(targetInfo.MemberName, entry.DeclaringTypeScope))
                        return; // Permitted — constructor privilege for readonly setters.
                }

                // In all readonly contexts, setter calls are prohibited (base rule 9).
                diagnostics.Add(MakeDiagnostic(
                    SetterCallInReadonlyContext,
                    $"Cannot call property setter '{targetInfo.MemberName}' within a readonly context",
                    assignment,
                    model));
                break;
        }
    }

    /// <summary>
    /// The kind of target in an assignment expression.
    /// </summary>
    private enum AssignmentTargetKind
    {
        LocalVariable,
        Field,
        PropertyOrIndexerSetter,
    }

    /// <summary>
    /// Information about an assignment target.
    /// </summary>
    private readonly record struct AssignmentTargetInfo(
        AssignmentTargetKind TargetKind,
        string? MemberName,
        bool IsOnThis,
        bool IsBackingFieldKeyword = false);

    /// <summary>
    /// Classifies an assignment target as a local variable, field, or property/indexer setter.
    /// </summary>
    private static AssignmentTargetInfo ClassifyAssignmentTarget(SyntaxNode targetNode, ISemanticModel model, IScope? declaringTypeScope = null)
    {
        switch (targetNode)
        {
            case PostfixNode postfix:
            {
                // `this.member` pattern
                if (postfix.Suffix.Length == 1
                    && postfix.Target is ThisExpressionPrimaryNode
                    && postfix.Suffix[0] is MemberAccessSuffixPostfixSuffixNode memberAccess)
                {
                    var memberName = memberAccess.Name.Lexeme;
                    var symbol = model.GetDeclaredSymbol(memberAccess);

                    if (symbol is not null)
                    {
                        if (symbol.Kind == SymbolKind.Field)
                            return new AssignmentTargetInfo(AssignmentTargetKind.Field, memberName, IsOnThis: true);
                        if (symbol.Kind is SymbolKind.Property or SymbolKind.Indexer)
                            return new AssignmentTargetInfo(AssignmentTargetKind.PropertyOrIndexerSetter, memberName, IsOnThis: true);
                    }

                    // If we can't resolve via GetDeclaredSymbol, try the declaring type scope
                    // to distinguish fields from properties.
                    if (declaringTypeScope is not null)
                    {
                        var scopeMember = declaringTypeScope.Lookup(memberName);
                        if (scopeMember is not null)
                        {
                            if (scopeMember.Kind == SymbolKind.Field)
                                return new AssignmentTargetInfo(AssignmentTargetKind.Field, memberName, IsOnThis: true);
                            if (scopeMember.Kind is SymbolKind.Property or SymbolKind.Indexer)
                                return new AssignmentTargetInfo(AssignmentTargetKind.PropertyOrIndexerSetter, memberName, IsOnThis: true);
                        }
                    }

                    // If we still can't resolve, treat `this.x` as a field
                    // since `this` always refers to the current instance.
                    return new AssignmentTargetInfo(AssignmentTargetKind.Field, memberName, IsOnThis: true);
                }

                // Bare identifier — check if it resolves to a field or property.
                if (postfix.Suffix.Length == 0 && postfix.Target is IdentifierExpressionPrimaryNode identifier)
                {
                    var symbol = model.GetDeclaredSymbol(identifier);
                    if (symbol is not null)
                    {
                        if (symbol.Kind == SymbolKind.Field)
                            return new AssignmentTargetInfo(AssignmentTargetKind.Field, symbol.Name, IsOnThis: true);
                        if (symbol.Kind is SymbolKind.Property or SymbolKind.Indexer)
                            return new AssignmentTargetInfo(AssignmentTargetKind.PropertyOrIndexerSetter, symbol.Name, IsOnThis: true);
                        // Local variable or parameter
                        return new AssignmentTargetInfo(AssignmentTargetKind.LocalVariable, null, IsOnThis: false);
                    }

                    // Unresolved identifiers are conservatively treated as locals.
                    return new AssignmentTargetInfo(AssignmentTargetKind.LocalVariable, null, IsOnThis: false);
                }

                // Bare `field` keyword — the backing field of the enclosing property.
                if (postfix.Suffix.Length == 0 && postfix.Target is FieldKeywordExpressionPrimaryNode)
                {
                    return new AssignmentTargetInfo(AssignmentTargetKind.Field, "field", IsOnThis: true, IsBackingFieldKeyword: true);
                }

                // Member access on something other than `this` — e.g., `obj.field`
                if (postfix.Suffix.Length > 0
                    && postfix.Suffix[^1] is MemberAccessSuffixPostfixSuffixNode lastMemberAccess)
                {
                    var symbol = model.GetDeclaredSymbol(lastMemberAccess);
                    if (symbol is not null)
                    {
                        if (symbol.Kind == SymbolKind.Field)
                            return new AssignmentTargetInfo(AssignmentTargetKind.Field, symbol.Name, IsOnThis: false);
                        if (symbol.Kind is SymbolKind.Property or SymbolKind.Indexer)
                            return new AssignmentTargetInfo(AssignmentTargetKind.PropertyOrIndexerSetter, symbol.Name, IsOnThis: false);
                    }
                }

                return new AssignmentTargetInfo(AssignmentTargetKind.LocalVariable, null, IsOnThis: false);
            }

            default:
            {
                // Walk through expression wrappers to find the actual target.
                foreach (var child in targetNode.Children())
                {
                    if (child is not null)
                    {
                        var result = ClassifyAssignmentTarget(child, model, declaringTypeScope);
                        if (result.TargetKind != AssignmentTargetKind.LocalVariable)
                            return result;
                    }
                }
                return new AssignmentTargetInfo(AssignmentTargetKind.LocalVariable, null, IsOnThis: false);
            }
        }
    }

    /// <summary>
    /// Checks if a member with the given name exists in the declaring type's scope
    /// and matches the expected symbol kind.
    /// </summary>
    private static bool IsOwnMember(string? memberName, IScope? declaringTypeScope, SymbolKind expectedKind)
    {
        if (memberName is null || declaringTypeScope is null)
            return false;

        var member = declaringTypeScope.Lookup(memberName);
        return member is not null && member.Kind == expectedKind;
    }

    /// <summary>
    /// Checks if a member with the given name is a readonly property or indexer
    /// in the declaring type's scope.
    /// </summary>
    private static bool IsOwnReadonlyPropertyOrIndexer(string? memberName, IScope? declaringTypeScope)
    {
        if (memberName is null || declaringTypeScope is null)
            return false;

        var member = declaringTypeScope.Lookup(memberName);
        if (member is not Symbol sym)
            return false;

        if (sym.Kind is not (SymbolKind.Property or SymbolKind.Indexer))
            return false;

        return IsReadonly(sym.ReadonlyStatus);
    }

    // ── Method call checking (MIST_RO_0002) ──────────────────────────────

    private static void CheckMethodCallViolation(
        MethodCallSuffixPostfixSuffixNode methodCall,
        ContextKind context,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Only enforce in readonly contexts.
        if (context == ContextKind.Normal)
            return;

        // Resolve the method being called.
        var symbol = model.GetDeclaredSymbol(methodCall);
        if (symbol is not Symbol sym)
            return; // Can't resolve — skip (avoid false positives).

        // Check if the method is readonly.
        if (IsReadonly(sym.ReadonlyStatus))
            return; // Permitted (base rule 2).

        // Non-readonly method call in a readonly context.
        diagnostics.Add(MakeDiagnostic(
            NonReadonlyMethodCallInReadonlyContext,
            $"Cannot call non-readonly method '{sym.Name}' within a readonly context",
            methodCall,
            model));
    }

    // ── Member access checking (MIST_RO_0003, MIST_RO_0004) ─────────────

    private static void CheckMemberAccessViolation(
        MemberAccessSuffixPostfixSuffixNode memberAccess,
        ContextKind context,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Only enforce in readonly contexts.
        if (context == ContextKind.Normal)
            return;

        var symbol = model.GetDeclaredSymbol(memberAccess);
        if (symbol is not Symbol sym)
            return; // Can't resolve — skip.

        // Field reads are always permitted (base rule 1).
        if (sym.Kind == SymbolKind.Field)
            return;

        // Property/indexer getter access — check if readonly.
        if (sym.Kind is SymbolKind.Property or SymbolKind.Indexer)
        {
            if (!IsReadonly(sym.ReadonlyStatus))
            {
                diagnostics.Add(MakeDiagnostic(
                    NonReadonlyGetterCallInReadonlyContext,
                    $"Cannot call non-readonly property getter '{sym.Name}' within a readonly context",
                    memberAccess,
                    model));
            }
        }
    }

    // ── Object creation checking (MIST_RO_0005) ─────────────────────────

    private static void CheckObjectCreationViolation(
        ObjectCreationExpressionPrimaryNode objectCreation,
        ContextKind context,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Only enforce in readonly contexts.
        if (context == ContextKind.Normal)
            return;

        // Resolve the type being constructed.
        var createdType = model.GetTypeOf(objectCreation);
        if (createdType is null)
            return; // Can't resolve — skip.

        // If the type itself is readonly, all its constructors are implicitly readonly.
        if (IsTypeReadonly(createdType))
            return; // Permitted (base rule 5).

        // Look for a readonly constructor in the created type.
        var memberScope = GetMemberScope(createdType);
        if (memberScope is null)
            return; // Can't find scope — skip.

        // Check if any constructor is readonly.
        foreach (var member in memberScope.Members)
        {
            if (member.Kind == SymbolKind.Constructor && member is Symbol ctorSym)
            {
                if (IsReadonly(ctorSym.ReadonlyStatus))
                    return; // At least one readonly constructor exists — permitted.
            }
        }

        // No readonly constructor found — prohibited (base rule 10).
        var typeName = createdType.DisplayName;
        diagnostics.Add(MakeDiagnostic(
            NonReadonlyConstructorAllocation,
            $"Cannot allocate type '{typeName}' within a readonly context: constructor is not readonly",
            objectCreation,
            model));
    }

    // ── Helper methods ───────────────────────────────────────────────────

    /// <summary>
    /// Extracts the <see cref="ReadonlyStatus"/> from a symbol by checking
    /// the concrete <see cref="Symbol"/> type which carries readonly metadata.
    /// </summary>
    private static ReadonlyStatus GetReadonlyStatus(ISymbol symbol)
    {
        if (symbol is Symbol concreteSymbol)
            return concreteSymbol.ReadonlyStatus;

        return ReadonlyStatus.Unknown;
    }

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
            AbstractType it => it.IsReadonly,
            _ => false,
        };
    }

    /// <summary>
    /// Gets the member scope for a type.
    /// </summary>
    private static IScope? GetMemberScope(IType type)
    {
        // For ConstructedType, look up in the generic definition.
        if (type is ConstructedType constructed)
            type = constructed.GenericDefinition;

        return type switch
        {
            ClassType ct => ct.MemberScope,
            StructType st => st.MemberScope,
            AbstractType it => it.MemberScope,
            _ => null,
        };
    }

    /// <summary>
    /// Looks up a member symbol by name from the receiver type's member scope.
    /// For ConstructedType, resolves through the generic definition's member scope.
    /// Returns null if the member cannot be found.
    /// </summary>
    private static Symbol? LookupMemberByName(IType? receiverType, string memberName)
    {
        var scope = GetMemberScope(receiverType!);
        if (scope is null)
            return null;

        foreach (var member in scope.Members)
        {
            if (member.Name == memberName && member is Symbol sym)
                return sym;
        }

        return null;
    }

    /// <summary>
    /// Gets the declaring type's member scope for a symbol by walking up
    /// the containing scope hierarchy.
    /// </summary>
    private static IScope? GetDeclaringTypeScope(ISymbol symbol)
    {
        // The symbol's ContainingScope is the type's member scope.
        return symbol.ContainingScope;
    }

    // ── Readonly-constrained type parameter member access (Req 12.3–12.6) ──

    /// <summary>
    /// Walks a member's body looking for member access on values whose type is a
    /// type parameter with a readonly constraint. When T has <c>where T : readonly</c>,
    /// all member access on values of type T is treated as if the receiver is in a
    /// readonly context — non-readonly methods/getters are prohibited and setter calls
    /// are prohibited.
    /// </summary>
    private static void AnalyzeReadonlyConstrainedTypeParamAccess(
        ISymbol symbol,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Get the declaring AST node.
        SyntaxNode declaringNode;
        try { declaringNode = model.GetDeclaringNode(symbol); }
        catch { return; }

        switch (symbol.Kind)
        {
            case SymbolKind.Method:
                if (declaringNode is MethodDeclarationNode methodDecl)
                {
                    var body = methodDecl.Body.GetBlock();
                    if (body is not null)
                        WalkBlockForConstrainedTypeParamViolations(body, model, diagnostics);
                }
                break;

            case SymbolKind.Constructor:
                if (declaringNode is ConstructorDeclarationNode ctorDecl)
                {
                    var ctorBody = ctorDecl.Body.GetBlock();
                    if (ctorBody is not null)
                        WalkBlockForConstrainedTypeParamViolations(ctorBody, model, diagnostics);
                }
                break;

            case SymbolKind.Property:
                WalkPropertyForConstrainedTypeParamViolations(declaringNode, model, diagnostics);
                break;

            case SymbolKind.Indexer:
                WalkIndexerForConstrainedTypeParamViolations(declaringNode, model, diagnostics);
                break;
        }
    }

    private static void WalkPropertyForConstrainedTypeParamViolations(
        SyntaxNode declaringNode,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        switch (declaringNode)
        {
            case BlockPropertyPropertyDeclarationNode blockProp:
                foreach (var accessor in blockProp.Accessors)
                {
                    switch (accessor)
                    {
                        case BlockGetAccessorAccessorDeclarationNode blockGet:
                            WalkBlockForConstrainedTypeParamViolations(blockGet.Body, model, diagnostics);
                            break;
                        case ExpressionGetAccessorAccessorDeclarationNode exprGet:
                            WalkNodeForConstrainedTypeParamViolations(exprGet.Expression, model, diagnostics);
                            break;
                        case BlockSetAccessorAccessorDeclarationNode blockSet:
                            WalkBlockForConstrainedTypeParamViolations(blockSet.Body, model, diagnostics);
                            break;
                        case ExpressionSetAccessorAccessorDeclarationNode exprSet:
                            WalkNodeForConstrainedTypeParamViolations(exprSet.Expression, model, diagnostics);
                            break;
                        case BlockInitAccessorAccessorDeclarationNode blockInit:
                            WalkBlockForConstrainedTypeParamViolations(blockInit.Body, model, diagnostics);
                            break;
                        case ExpressionInitAccessorAccessorDeclarationNode exprInit:
                            WalkNodeForConstrainedTypeParamViolations(exprInit.Expression, model, diagnostics);
                            break;
                    }
                }
                break;

            case ExpressionBodiedPropertyPropertyDeclarationNode exprProp:
                WalkNodeForConstrainedTypeParamViolations(exprProp.Body, model, diagnostics);
                break;
        }
    }

    private static void WalkIndexerForConstrainedTypeParamViolations(
        SyntaxNode declaringNode,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        if (declaringNode is not IndexerDeclarationNode { Body: BlockIndexerBodyIndexerBodyNode blockBody } blockIndexer)
            return;

        foreach (var accessor in blockBody.Accessors)
        {
            switch (accessor)
            {
                case BlockGetAccessorAccessorDeclarationNode blockGet:
                    WalkBlockForConstrainedTypeParamViolations(blockGet.Body, model, diagnostics);
                    break;
                case ExpressionGetAccessorAccessorDeclarationNode exprGet:
                    WalkNodeForConstrainedTypeParamViolations(exprGet.Expression, model, diagnostics);
                    break;
                case BlockSetAccessorAccessorDeclarationNode blockSet:
                    WalkBlockForConstrainedTypeParamViolations(blockSet.Body, model, diagnostics);
                    break;
                case ExpressionSetAccessorAccessorDeclarationNode exprSet:
                    WalkNodeForConstrainedTypeParamViolations(exprSet.Expression, model, diagnostics);
                    break;
                case BlockInitAccessorAccessorDeclarationNode blockInit:
                    WalkBlockForConstrainedTypeParamViolations(blockInit.Body, model, diagnostics);
                    break;
                case ExpressionInitAccessorAccessorDeclarationNode exprInit:
                    WalkNodeForConstrainedTypeParamViolations(exprInit.Expression, model, diagnostics);
                    break;
            }
        }
    }

    private static void WalkBlockForConstrainedTypeParamViolations(
        BlockNode block,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        foreach (var stmt in block.Statements)
        {
            WalkStatementForConstrainedTypeParamViolations(stmt, model, diagnostics);
        }
    }

    private static void WalkStatementForConstrainedTypeParamViolations(
        StatementNode stmt,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        switch (stmt)
        {
            case VariableDeclarationStatementStatementNode varDecl:
                if (varDecl.VariableDeclaration is { } vd)
                    WalkNodeForConstrainedTypeParamViolations(vd, model, diagnostics);
                break;

            case ReturnStatementStatementNode returnStmt:
                if (returnStmt.ReturnStatement is { } rs)
                    WalkNodeForConstrainedTypeParamViolations(rs, model, diagnostics);
                break;

            case IfStatementStatementNode ifStmt:
                WalkNodeForConstrainedTypeParamViolations(ifStmt.IfStatement, model, diagnostics);
                break;

            case WhileStatementStatementNode whileStmt:
                WalkNodeForConstrainedTypeParamViolations(whileStmt.WhileStatement, model, diagnostics);
                break;

            case ForStatementStatementNode forStmt:
                WalkNodeForConstrainedTypeParamViolations(forStmt.ForStatement, model, diagnostics);
                break;

            case ForeachStatementStatementNode foreachStmt:
                WalkNodeForConstrainedTypeParamViolations(foreachStmt.ForeachStatement, model, diagnostics);
                break;

            case ExpressionStatementStatementNode exprStmt:
                WalkNodeForConstrainedTypeParamViolations(exprStmt.ExpressionStatement, model, diagnostics);
                break;

            case ReadonlyBlockStatementStatementNode readonlyBlockStmt:
                WalkBlockForConstrainedTypeParamViolations(readonlyBlockStmt.ReadonlyBlock.Body, model, diagnostics);
                break;

            case BlockStatementStatementNode blockStmt:
                WalkBlockForConstrainedTypeParamViolations(blockStmt.BlockStatement, model, diagnostics);
                break;

            case BreakStatementStatementNode:
                break;

            default:
                WalkChildrenForConstrainedTypeParamViolations(stmt, model, diagnostics);
                break;
        }
    }

    private static void WalkNodeForConstrainedTypeParamViolations(
        SyntaxNode node,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Check this node for constrained type parameter violations.
        CheckNodeForConstrainedTypeParamViolations(node, model, diagnostics);

        // Recursively walk children.
        WalkChildrenForConstrainedTypeParamViolations(node, model, diagnostics);
    }

    private static void WalkChildrenForConstrainedTypeParamViolations(
        SyntaxNode node,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        foreach (var child in node.Children())
        {
            if (child is not null)
                WalkNodeForConstrainedTypeParamViolations(child, model, diagnostics);
        }
    }

    /// <summary>
    /// Checks a node for violations of readonly-constrained type parameter rules.
    /// When a PostfixNode's target has a type that is a TypeParameterType with
    /// HasReadonlyConstraint, all member access on it is subject to readonly rules:
    /// - Non-readonly method calls → MIST_RO_0002
    /// - Non-readonly property/indexer getter access → MIST_RO_0003
    /// - Any setter call → MIST_RO_0004
    /// </summary>
    private static void CheckNodeForConstrainedTypeParamViolations(
        SyntaxNode node,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        switch (node)
        {
            case PostfixNode postfix:
                CheckConstrainedTypeParamPostfix(postfix, model, diagnostics);
                break;

            case AssignmentNode assignment when assignment.Value is not null:
                CheckConstrainedTypeParamAssignment(assignment, model, diagnostics);
                break;
        }
    }

    /// <summary>
    /// Checks if a PostfixNode's target type is a readonly-constrained type parameter.
    /// If so, checks all suffixes for non-readonly method calls and non-readonly property
    /// getter access.
    /// </summary>
    private static void CheckConstrainedTypeParamPostfix(
        PostfixNode postfix,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Get the type of the target expression.
        var targetType = model.GetTypeOf(postfix.Target);
        if (!IsReadonlyConstrainedTypeParameter(targetType))
            return;

        // The target is a readonly-constrained type parameter.
        // Check each suffix for violations.
        for (var i = 0; i < postfix.Suffix.Length; i++)
        {
            var suffix = postfix.Suffix[i];

            switch (suffix)
            {
                case MethodCallSuffixPostfixSuffixNode methodCall:
                {
                    // Only check the first suffix that operates directly on the type parameter.
                    if (i == 0)
                    {
                        var symbol = model.GetDeclaredSymbol(methodCall);
                        if (symbol is Symbol sym && !IsReadonly(sym.ReadonlyStatus))
                        {
                            diagnostics.Add(MakeDiagnostic(
                                NonReadonlyMethodCallInReadonlyContext,
                                $"Cannot call non-readonly method '{sym.Name}' within a readonly context",
                                methodCall,
                                model));
                        }
                    }
                    // After a method call, the receiver type changes — stop checking.
                    return;
                }

                case MemberAccessSuffixPostfixSuffixNode memberAccess:
                {
                    // Only check the first suffix that operates directly on the type parameter.
                    if (i == 0)
                    {
                        var symbol = model.GetDeclaredSymbol(memberAccess);
                        if (symbol is Symbol sym)
                        {
                            // Field reads are always permitted.
                            if (sym.Kind == SymbolKind.Field)
                                break;

                            // Property/indexer getter access — check if readonly.
                            if (sym.Kind is SymbolKind.Property or SymbolKind.Indexer)
                            {
                                if (!IsReadonly(sym.ReadonlyStatus))
                                {
                                    diagnostics.Add(MakeDiagnostic(
                                        NonReadonlyGetterCallInReadonlyContext,
                                        $"Cannot call non-readonly property getter '{sym.Name}' within a readonly context",
                                        memberAccess,
                                        model));
                                }
                            }
                        }
                    }
                    // After member access, the receiver type changes for subsequent suffixes.
                    // Continue to check if the next suffix is a method call on the member result.
                    break;
                }

                default:
                    // For other suffix types (index, invocation), the receiver changes — stop.
                    return;
            }
        }
    }

    /// <summary>
    /// Checks if an assignment targets a member on a readonly-constrained type parameter.
    /// Pattern: value.member = expr where value's type is T with readonly constraint.
    /// - Field assignment → MIST_RO_0001 (field assignment in readonly context)
    /// - Property/indexer setter → MIST_RO_0004 (setter call in readonly context)
    /// </summary>
    private static void CheckConstrainedTypeParamAssignment(
        AssignmentNode assignment,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Look for pattern: value.member = expr
        var postfix = FindPostfixNode(assignment.Target);
        if (postfix is null)
            return;

        // The target must be a PostfixNode with at least one member access suffix.
        if (postfix.Suffix.Length == 0)
            return;

        // Check if the receiver's type is a readonly-constrained type parameter.
        var targetType = model.GetTypeOf(postfix.Target);
        if (!IsReadonlyConstrainedTypeParameter(targetType))
            return;

        // Only check direct member access on the type parameter (first suffix).
        var lastSuffix = postfix.Suffix.Length == 1 ? postfix.Suffix[0] : null;
        if (lastSuffix is not MemberAccessSuffixPostfixSuffixNode memberAccess)
            return;

        var memberName = memberAccess.Name.Lexeme;

        // Resolve the member to determine if it's a field or property/indexer.
        var symbol = model.GetDeclaredSymbol(memberAccess);
        if (symbol is Symbol sym)
        {
            if (sym.Kind == SymbolKind.Field)
            {
                // Field assignment on readonly-constrained type parameter.
                diagnostics.Add(MakeDiagnostic(
                    FieldAssignmentInReadonlyContext,
                    $"Cannot assign to field '{memberName}' within a readonly context",
                    assignment,
                    model));
                return;
            }

            if (sym.Kind is SymbolKind.Property or SymbolKind.Indexer)
            {
                // Setter call on readonly-constrained type parameter.
                diagnostics.Add(MakeDiagnostic(
                    SetterCallInReadonlyContext,
                    $"Cannot call property setter '{memberName}' within a readonly context",
                    assignment,
                    model));
                return;
            }
        }

        // If we can't resolve the symbol, conservatively treat it as a field assignment.
        diagnostics.Add(MakeDiagnostic(
            FieldAssignmentInReadonlyContext,
            $"Cannot assign to field '{memberName}' within a readonly context",
            assignment,
            model));
    }

    // ── Fifth pass: Closed Generic With Unknown access ───────────────────

    /// <summary>
    /// Walks a member's body looking for operations on values whose type is a
    /// Closed_Generic_With_Unknown. When a variable's resolved type contains unknown
    /// at any type argument position (and no open type parameters), all member access
    /// is treated as if the receiver is in a readonly context.
    /// </summary>
    private static void AnalyzeClosedGenericWithUnknownAccess(
        ISymbol symbol,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Get the declaring AST node.
        SyntaxNode declaringNode;
        try { declaringNode = model.GetDeclaringNode(symbol); }
        catch { return; }

        switch (symbol.Kind)
        {
            case SymbolKind.Method:
                if (declaringNode is MethodDeclarationNode methodDecl)
                {
                    var body = methodDecl.Body.GetBlock();
                    if (body is not null)
                        WalkBlockForClosedUnknownViolations(body, model, diagnostics);
                }
                break;

            case SymbolKind.Constructor:
                if (declaringNode is ConstructorDeclarationNode ctorDecl)
                {
                    var ctorBody = ctorDecl.Body.GetBlock();
                    if (ctorBody is not null)
                        WalkBlockForClosedUnknownViolations(ctorBody, model, diagnostics);
                }
                break;

            case SymbolKind.Property:
                WalkPropertyForClosedUnknownViolations(declaringNode, model, diagnostics);
                break;

            case SymbolKind.Indexer:
                WalkIndexerForClosedUnknownViolations(declaringNode, model, diagnostics);
                break;
        }
    }

    private static void WalkPropertyForClosedUnknownViolations(
        SyntaxNode declaringNode,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        switch (declaringNode)
        {
            case BlockPropertyPropertyDeclarationNode blockProp:
                foreach (var accessor in blockProp.Accessors)
                {
                    switch (accessor)
                    {
                        case BlockGetAccessorAccessorDeclarationNode blockGet:
                            WalkBlockForClosedUnknownViolations(blockGet.Body, model, diagnostics);
                            break;
                        case ExpressionGetAccessorAccessorDeclarationNode exprGet:
                            WalkNodeForClosedUnknownViolations(exprGet.Expression, model, diagnostics);
                            break;
                        case BlockSetAccessorAccessorDeclarationNode blockSet:
                            WalkBlockForClosedUnknownViolations(blockSet.Body, model, diagnostics);
                            break;
                        case ExpressionSetAccessorAccessorDeclarationNode exprSet:
                            WalkNodeForClosedUnknownViolations(exprSet.Expression, model, diagnostics);
                            break;
                        case BlockInitAccessorAccessorDeclarationNode blockInit:
                            WalkBlockForClosedUnknownViolations(blockInit.Body, model, diagnostics);
                            break;
                        case ExpressionInitAccessorAccessorDeclarationNode exprInit:
                            WalkNodeForClosedUnknownViolations(exprInit.Expression, model, diagnostics);
                            break;
                    }
                }
                break;

            case ExpressionBodiedPropertyPropertyDeclarationNode exprProp:
                WalkNodeForClosedUnknownViolations(exprProp.Body, model, diagnostics);
                break;
        }
    }

    private static void WalkIndexerForClosedUnknownViolations(
        SyntaxNode declaringNode,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        if (declaringNode is not IndexerDeclarationNode { Body: BlockIndexerBodyIndexerBodyNode blockBody } blockIndexer)
            return;

        foreach (var accessor in blockBody.Accessors)
        {
            switch (accessor)
            {
                case BlockGetAccessorAccessorDeclarationNode blockGet:
                    WalkBlockForClosedUnknownViolations(blockGet.Body, model, diagnostics);
                    break;
                case ExpressionGetAccessorAccessorDeclarationNode exprGet:
                    WalkNodeForClosedUnknownViolations(exprGet.Expression, model, diagnostics);
                    break;
                case BlockSetAccessorAccessorDeclarationNode blockSet:
                    WalkBlockForClosedUnknownViolations(blockSet.Body, model, diagnostics);
                    break;
                case ExpressionSetAccessorAccessorDeclarationNode exprSet:
                    WalkNodeForClosedUnknownViolations(exprSet.Expression, model, diagnostics);
                    break;
                case BlockInitAccessorAccessorDeclarationNode blockInit:
                    WalkBlockForClosedUnknownViolations(blockInit.Body, model, diagnostics);
                    break;
                case ExpressionInitAccessorAccessorDeclarationNode exprInit:
                    WalkNodeForClosedUnknownViolations(exprInit.Expression, model, diagnostics);
                    break;
            }
        }
    }

    private static void WalkBlockForClosedUnknownViolations(
        BlockNode block,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        foreach (var stmt in block.Statements)
        {
            WalkStatementForClosedUnknownViolations(stmt, model, diagnostics);
        }
    }

    private static void WalkStatementForClosedUnknownViolations(
        StatementNode stmt,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        switch (stmt)
        {
            case VariableDeclarationStatementStatementNode varDecl:
                if (varDecl.VariableDeclaration is { } vd)
                    WalkNodeForClosedUnknownViolations(vd, model, diagnostics);
                break;

            case ReturnStatementStatementNode returnStmt:
                if (returnStmt.ReturnStatement is { } rs)
                    WalkNodeForClosedUnknownViolations(rs, model, diagnostics);
                break;

            case IfStatementStatementNode ifStmt:
                WalkNodeForClosedUnknownViolations(ifStmt.IfStatement, model, diagnostics);
                break;

            case WhileStatementStatementNode whileStmt:
                WalkNodeForClosedUnknownViolations(whileStmt.WhileStatement, model, diagnostics);
                break;

            case ForStatementStatementNode forStmt:
                WalkNodeForClosedUnknownViolations(forStmt.ForStatement, model, diagnostics);
                break;

            case ForeachStatementStatementNode foreachStmt:
                WalkNodeForClosedUnknownViolations(foreachStmt.ForeachStatement, model, diagnostics);
                break;

            case ExpressionStatementStatementNode exprStmt:
                WalkNodeForClosedUnknownViolations(exprStmt.ExpressionStatement, model, diagnostics);
                break;

            case ReadonlyBlockStatementStatementNode readonlyBlockStmt:
                WalkBlockForClosedUnknownViolations(readonlyBlockStmt.ReadonlyBlock.Body, model, diagnostics);
                break;

            case BlockStatementStatementNode blockStmt:
                WalkBlockForClosedUnknownViolations(blockStmt.BlockStatement, model, diagnostics);
                break;

            case BreakStatementStatementNode:
                break;

            default:
                WalkChildrenForClosedUnknownViolations(stmt, model, diagnostics);
                break;
        }
    }

    private static void WalkNodeForClosedUnknownViolations(
        SyntaxNode node,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Check this node for closed-generic-with-unknown violations.
        CheckNodeForClosedUnknownViolations(node, model, diagnostics);

        // For AssignmentNode with a value, only walk the value side — the target
        // is already handled by CheckClosedUnknownAssignment. Walking the target
        // would cause the postfix check to double-emit for the same member access.
        if (node is AssignmentNode assignment && assignment.Value is not null)
        {
            WalkNodeForClosedUnknownViolations(assignment.Value, model, diagnostics);
            return;
        }

        // Recursively walk children.
        WalkChildrenForClosedUnknownViolations(node, model, diagnostics);
    }

    private static void WalkChildrenForClosedUnknownViolations(
        SyntaxNode node,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        foreach (var child in node.Children())
        {
            if (child is not null)
                WalkNodeForClosedUnknownViolations(child, model, diagnostics);
        }
    }

    /// <summary>
    /// Checks a node for violations of closed-generic-with-unknown readonly rules.
    /// When a PostfixNode's target has a type that is a Closed_Generic_With_Unknown,
    /// all member access on it is subject to readonly rules:
    /// - Non-readonly method calls → MIST_TC_0062
    /// - Non-readonly property/indexer getter access → MIST_TC_0062
    /// - Any setter call → MIST_TC_0062
    /// - Field assignment → MIST_TC_0062
    /// </summary>
    private static void CheckNodeForClosedUnknownViolations(
        SyntaxNode node,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        switch (node)
        {
            case PostfixNode postfix:
                CheckClosedUnknownPostfix(postfix, model, diagnostics);
                break;

            case AssignmentNode assignment when assignment.Value is not null:
                CheckClosedUnknownAssignment(assignment, model, diagnostics);
                break;
        }
    }

    /// <summary>
    /// Checks if a PostfixNode's target type is a Closed_Generic_With_Unknown.
    /// If so, checks each suffix for non-readonly method calls, non-readonly property
    /// getter access, and setter calls. Emits MIST_TC_0062 for violations.
    /// After processing a suffix, updates the current type to the result type of that
    /// suffix — if the result type is also a Closed_Generic_With_Unknown, continues
    /// enforcing on subsequent suffixes.
    /// </summary>
    private static void CheckClosedUnknownPostfix(
        PostfixNode postfix,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Get the type of the target expression.
        var currentType = model.GetTypeOf(postfix.Target);
        if (!IsClosedGenericWithUnknown(currentType))
            return;

        // Walk each suffix, tracking the current receiver type.
        for (var i = 0; i < postfix.Suffix.Length; i++)
        {
            var suffix = postfix.Suffix[i];
            var typeDisplayName = currentType!.DisplayName;

            switch (suffix)
            {
                case MethodCallSuffixPostfixSuffixNode methodCall:
                {
                    var sym = LookupMemberByName(currentType, methodCall.Name.Lexeme);
                    if (sym is not null && !IsReadonly(sym.ReadonlyStatus))
                    {
                        diagnostics.Add(MakeDiagnostic(
                            ClosedUnknownNonReadonlyAccess,
                            $"Cannot call non-readonly method '{sym.Name}' on '{typeDisplayName}': the type is treated as readonly because it contains an 'unknown' type argument.",
                            methodCall,
                            model));
                    }

                    // Update current type to the result of the method call.
                    currentType = model.GetTypeOf(suffix);
                    break;
                }

                case MemberAccessSuffixPostfixSuffixNode memberAccess:
                {
                    var sym = LookupMemberByName(currentType, memberAccess.Name.Lexeme);
                    if (sym is not null)
                    {
                        // Field reads are always permitted.
                        if (sym.Kind == SymbolKind.Field)
                        {
                            // No diagnostic — field reads are fine.
                        }
                        else if (sym.Kind is SymbolKind.Property or SymbolKind.Indexer)
                        {
                            // Property/indexer getter access — check if readonly.
                            if (!IsReadonly(sym.ReadonlyStatus))
                            {
                                diagnostics.Add(MakeDiagnostic(
                                    ClosedUnknownNonReadonlyAccess,
                                    $"Cannot access non-readonly property '{sym.Name}' on '{typeDisplayName}': the type is treated as readonly because it contains an 'unknown' type argument.",
                                    memberAccess,
                                    model));
                            }
                        }
                    }

                    // Update current type to the result of the member access.
                    currentType = model.GetTypeOf(suffix);
                    break;
                }

                case IndexSuffixPostfixSuffixNode indexSuffix:
                {
                    // Index read: check the indexer getter's ReadonlyStatus.
                    // Look up the indexer by the canonical name "this[]".
                    var sym = LookupMemberByName(currentType, "this[]");
                    if (sym is not null && !IsReadonly(sym.ReadonlyStatus))
                    {
                        diagnostics.Add(MakeDiagnostic(
                            ClosedUnknownNonReadonlyAccess,
                            $"Cannot access non-readonly indexer on '{typeDisplayName}': the type is treated as readonly because it contains an 'unknown' type argument.",
                            indexSuffix,
                            model));
                    }

                    // Update current type to the result of the index access.
                    currentType = model.GetTypeOf(suffix);
                    break;
                }

                default:
                    // For other suffix types (invocation, etc.), update the type and continue.
                    currentType = model.GetTypeOf(suffix);
                    break;
            }

            // If the result type is no longer a Closed_Generic_With_Unknown, stop enforcing.
            if (!IsClosedGenericWithUnknown(currentType))
                return;
        }
    }

    /// <summary>
    /// Checks if an assignment targets a member on a Closed_Generic_With_Unknown receiver.
    /// Patterns:
    /// - value.member = expr where value's type is a Closed_Generic_With_Unknown → field/property setter
    /// - value[index] = expr where value's type is a Closed_Generic_With_Unknown → indexer setter
    /// - value.member1.member2 = expr where value.member1's result type is Closed_Generic_With_Unknown → chained
    /// All setter calls and field writes on a Closed_Generic_With_Unknown receiver emit MIST_TC_0062.
    /// </summary>
    private static void CheckClosedUnknownAssignment(
        AssignmentNode assignment,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Look for pattern: value.member = expr or value[index] = expr
        var postfix = FindPostfixNode(assignment.Target);
        if (postfix is null)
            return;

        // The target must be a PostfixNode with at least one suffix.
        if (postfix.Suffix.Length == 0)
            return;

        // Determine the receiver type for the last suffix (the one being assigned to).
        // For a single suffix, the receiver is the target. For multiple suffixes,
        // the receiver is the result type of the second-to-last suffix.
        IType? receiverType;
        if (postfix.Suffix.Length == 1)
        {
            receiverType = model.GetTypeOf(postfix.Target);
        }
        else
        {
            receiverType = model.GetTypeOf(postfix.Suffix[^2]);
        }

        if (!IsClosedGenericWithUnknown(receiverType))
            return;

        var typeDisplayName = receiverType!.DisplayName;
        var lastSuffix = postfix.Suffix[^1];

        switch (lastSuffix)
        {
            case MemberAccessSuffixPostfixSuffixNode memberAccess:
            {
                var memberName = memberAccess.Name.Lexeme;
                var sym = LookupMemberByName(receiverType, memberName);
                if (sym is not null)
                {
                    if (sym.Kind == SymbolKind.Field)
                    {
                        // Field assignment on Closed_Generic_With_Unknown receiver.
                        diagnostics.Add(MakeDiagnostic(
                            ClosedUnknownNonReadonlyAccess,
                            $"Cannot assign to field '{memberName}' on '{typeDisplayName}': the type is treated as readonly because it contains an 'unknown' type argument.",
                            assignment,
                            model));
                        return;
                    }

                    if (sym.Kind is SymbolKind.Property or SymbolKind.Indexer)
                    {
                        // Setter call on Closed_Generic_With_Unknown receiver.
                        diagnostics.Add(MakeDiagnostic(
                            ClosedUnknownNonReadonlyAccess,
                            $"Cannot call property setter '{memberName}' on '{typeDisplayName}': the type is treated as readonly because it contains an 'unknown' type argument.",
                            assignment,
                            model));
                        return;
                    }
                }

                // If we can't resolve the symbol, conservatively treat it as a field assignment.
                diagnostics.Add(MakeDiagnostic(
                    ClosedUnknownNonReadonlyAccess,
                    $"Cannot assign to field '{memberName}' on '{typeDisplayName}': the type is treated as readonly because it contains an 'unknown' type argument.",
                    assignment,
                    model));
                break;
            }

            case IndexSuffixPostfixSuffixNode:
            {
                // Index write (setter) on Closed_Generic_With_Unknown receiver — always prohibited.
                diagnostics.Add(MakeDiagnostic(
                    ClosedUnknownNonReadonlyAccess,
                    $"Cannot call indexer setter on '{typeDisplayName}': the type is treated as readonly because it contains an 'unknown' type argument.",
                    assignment,
                    model));
                break;
            }
        }
    }

    /// <summary>
    /// Returns true if the given type is a TypeParameterType with a readonly constraint.
    /// </summary>
    private static bool IsReadonlyConstrainedTypeParameter(IType? type)
    {
        return type is TypeParameterType typeParam
            && typeParam.Constraints is { HasReadonlyConstraint: true };
    }

    // ── Closed Generic With Unknown detection ────────────────────────────

    /// <summary>
    /// Returns true if the type is a ConstructedType with at least one
    /// type argument that is UnknownType (directly or transitively through
    /// nested type arguments), AND no type argument is an unsubstituted
    /// TypeParameterType (open type parameters mean the type is not yet closed).
    /// </summary>
    internal static bool IsClosedGenericWithUnknown(IType? type)
    {
        if (type is not ConstructedType ct)
            return false;
        return ContainsUnknownTypeArgument(ct) && !ContainsOpenTypeParameter(ct);
    }

    /// <summary>
    /// Recursively checks whether any type argument (at any depth) of the given
    /// ConstructedType is an UnknownType.
    /// </summary>
    internal static bool ContainsUnknownTypeArgument(ConstructedType ct)
    {
        foreach (var arg in ct.TypeArguments)
        {
            if (arg is UnknownType)
                return true;
            if (arg is ConstructedType nested && ContainsUnknownTypeArgument(nested))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Recursively checks whether any type argument (at any depth) of the given
    /// ConstructedType is an unsubstituted TypeParameterType.
    /// </summary>
    internal static bool ContainsOpenTypeParameter(ConstructedType ct)
    {
        foreach (var arg in ct.TypeArguments)
        {
            if (arg is TypeParameterType)
                return true;
            if (arg is ConstructedType nested && ContainsOpenTypeParameter(nested))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Creates a diagnostic with location information derived from the given AST node.
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
            AnalyzerName: "ReadonlyContext");
    }
}
