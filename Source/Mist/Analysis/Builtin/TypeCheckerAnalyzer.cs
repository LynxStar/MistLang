using System.Collections.Immutable;
using Mist.Analysis.Contracts;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.Analysis.Builtin;

/// <summary>
/// Built-in analyzer that verifies Mist type rules against the semantic model.
/// Walks ASTs via the semantic model and checks each expression/statement
/// using expression types computed by the SemanticModelBuilder (Pass 2).
/// </summary>
public sealed class TypeCheckerAnalyzer : IAnalyzer
{
    public string AnalyzerId => "TypeChecker";

    // ── Overloading diagnostic IDs ───────────────────────────────────────
    internal static readonly DiagnosticId NoApplicableOverload = new("MIST_TC_0031");
    internal static readonly DiagnosticId AmbiguousOverload = new("MIST_TC_0032");

    // ── Numeric types for operator checking ──────────────────────────────
    private static readonly HashSet<IType> NumericTypes =
    [
        PrimitiveType.Int, PrimitiveType.Uint,
        PrimitiveType.Long, PrimitiveType.Ulong,
        PrimitiveType.Short, PrimitiveType.Ushort,
        PrimitiveType.Byte, PrimitiveType.Sbyte,
        PrimitiveType.Float, PrimitiveType.Double,
        PrimitiveType.Decimal,
    ];

    // ── Current enclosing type during analysis (set per method/constructor) ──
    private IType? _currentEnclosingType;

    // ── Whether we are currently inside a constructor body (for init-only checks) ──
    private bool _insideConstructor;

    // ── Whether we are currently inside a property accessor body (for field keyword checks) ──
    private bool _insidePropertyAccessor;

    public void Analyze(ISemanticModel model, DiagnosticBag diagnostics)
    {
        // Walk each method, constructor, property, and indexer body to check type rules.
        foreach (var symbol in model.AllSymbols)
        {
            if (symbol.Origin == SymbolOrigin.Backend)
                continue;

            switch (symbol.Kind)
            {
                case SymbolKind.Method:
                    CheckMethodBody(symbol, model, diagnostics);
                    break;
                case SymbolKind.Constructor:
                    CheckConstructorBody(symbol, model, diagnostics);
                    break;
                case SymbolKind.Field:
                    CheckFieldDeclaration(symbol, model, diagnostics);
                    break;
                case SymbolKind.Property:
                    CheckPropertyDeclaration(symbol, model, diagnostics);
                    break;
                case SymbolKind.Indexer:
                    CheckIndexerDeclaration(symbol, model, diagnostics);
                    break;
            }
        }

        // Entry point validation — once per compilation (R10.1, R10.2, R10.3).
        CheckEntryPoint(model, diagnostics);

        // Task 5.9: Check for duplicate indexers across all types (MIST_TC_0029).
        // (Individual indexer checks are done per-symbol above.)

        // Task 5.10: Check for missing interface property implementations (MIST_TC_0030).
        CheckMissingInterfaceProperties(model, diagnostics);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Entry point validation (R10.1, R10.2, R10.3)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Verifies that exactly one class contains a valid entry point:
    /// <c>static void Main()</c> or <c>static void Main(string[] args)</c>.
    /// Emits MIST_TC_0017 if no entry point is found, MIST_TC_0018 if multiple are found.
    /// </summary>
    private static void CheckEntryPoint(ISemanticModel model, DiagnosticBag diagnostics)
    {
        var entryPoints = new List<(ISymbol Symbol, SyntaxNode Node)>();

        foreach (var symbol in model.AllSymbols)
        {
            if (symbol.Origin == SymbolOrigin.Backend)
                continue;

            if (symbol.Kind != SymbolKind.Method || symbol.Name != "Main")
                continue;

            // Get the declaring AST node to inspect modifiers and parameters.
            SyntaxNode declaringNode;
            try { declaringNode = model.GetDeclaringNode(symbol); }
            catch { continue; }

            if (declaringNode is not MethodDeclarationNode methodDecl)
                continue;

            // Must be static.
            var isStatic = false;
            foreach (var mod in methodDecl.Modifiers)
            {
                if (mod is StaticModifierModifierNode)
                {
                    isStatic = true;
                    break;
                }
            }
            if (!isStatic)
                continue;

            // Must return void.
            if (methodDecl.ReturnType is not VoidTypeRefTypeReferenceNode)
                continue;

            // Must have either no parameters or a single string[] parameter.
            if (methodDecl.Parameters is null)
            {
                // static void Main() — valid entry point.
                entryPoints.Add((symbol, declaringNode));
            }
            else
            {
                // Check for exactly one parameter of type string[].
                var parameters = new List<ParameterNode>();
                foreach (var child in methodDecl.Parameters.Children())
                {
                    if (child is ParameterNode param)
                        parameters.Add(param);
                }

                if (parameters.Count == 1)
                {
                    var paramType = parameters[0].Type;
                    // string[] is represented as PrimitiveTypeRef with StringType and an array rank,
                    // or as a NamedTypeRef. Check for the string[] pattern.
                    if (IsStringArrayTypeRef(paramType))
                    {
                        entryPoints.Add((symbol, declaringNode));
                    }
                }
                // Any other parameter count or type is not a valid entry point signature.
            }
        }

        if (entryPoints.Count == 0)
        {
            diagnostics.Add(new Diagnostic(
                new DiagnosticId("MIST_TC_0017"),
                DiagnosticSeverity.Error,
                PipelineStage.Analysis,
                "Program does not contain a valid entry point (static void Main)",
                Location: null,
                CompilationUnitPath: null,
                AnalyzerName: "TypeChecker"));
        }
        else if (entryPoints.Count > 1)
        {
            // Emit a diagnostic listing all entry point locations.
            foreach (var (_, node) in entryPoints)
            {
                diagnostics.Add(MakeDiagnostic(
                    "MIST_TC_0018",
                    "Program contains multiple entry points",
                    node,
                    model));
            }
        }
    }

    /// <summary>
    /// Checks whether a type reference AST node represents <c>string[]</c>.
    /// </summary>
    private static bool IsStringArrayTypeRef(TypeReferenceNode typeRef)
    {
        // string[] as a primitive type ref: PrimitiveTypeRef with StringType and array rank.
        if (typeRef is PrimitiveTypeRefTypeReferenceNode primitiveRef)
        {
            return primitiveRef.PrimitiveType is StringTypePrimitiveTypeNode
                   && primitiveRef.ArrayRank.HasValue;
        }

        // string[] could also appear as a named type ref (unlikely but handle it).
        // In practice, the grammar parses "string" as a PrimitiveType keyword,
        // so this case shouldn't occur for valid Mist code.
        return false;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Enclosing type resolution — determines the type that contains a symbol
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Finds the declaring type (ClassType, StructType, or InterfaceType) that contains the given
    /// method or constructor symbol by walking up the scope chain.
    /// </summary>
    private static IType? FindDeclaringType(ISymbol memberSymbol)
    {
        // The member's ContainingScope is the type scope. Look for a Class/Struct/Interface
        // symbol in the parent scope that owns this type scope.
        var typeScope = memberSymbol.ContainingScope;
        var parentScope = typeScope?.Parent;
        if (parentScope is null)
            return null;

        foreach (var member in parentScope.Members)
        {
            if (member.Kind is SymbolKind.Class or SymbolKind.Struct or SymbolKind.Interface
                && member.DeclaredType is ClassType or StructType or InterfaceType)
            {
                // Check if this type's MemberScope is the same as the member's ContainingScope.
                var memberScope = member.DeclaredType switch
                {
                    ClassType ct => ct.MemberScope,
                    StructType st => st.MemberScope,
                    InterfaceType it => it.MemberScope,
                    _ => null,
                };
                if (ReferenceEquals(memberScope, typeScope))
                    return member.DeclaredType;
            }
        }

        return null;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Top-level check entry points
    // ═══════════════════════════════════════════════════════════════════════

    private void CheckMethodBody(ISymbol methodSymbol, ISemanticModel model, DiagnosticBag diagnostics)
    {
        SyntaxNode declaringNode;
        try { declaringNode = model.GetDeclaringNode(methodSymbol); }
        catch { return; }

        if (declaringNode is not MethodDeclarationNode methodDecl)
            return;

        var returnType = methodSymbol.DeclaredType ?? UnknownType.Instance;
        _currentEnclosingType = FindDeclaringType(methodSymbol);
        _insideConstructor = false;
        _insidePropertyAccessor = false;
        CheckBlock(methodDecl.Body, returnType, model, diagnostics);
    }

    private void CheckConstructorBody(ISymbol ctorSymbol, ISemanticModel model, DiagnosticBag diagnostics)
    {
        SyntaxNode declaringNode;
        try { declaringNode = model.GetDeclaringNode(ctorSymbol); }
        catch { return; }

        if (declaringNode is not ConstructorDeclarationNode ctorDecl)
            return;

        _currentEnclosingType = FindDeclaringType(ctorSymbol);
        _insideConstructor = true;
        _insidePropertyAccessor = false;

        // Check constructor chaining (: this(args))
        if (ctorDecl.Chain is not null)
        {
            CheckConstructorChain(ctorDecl.Chain, ctorSymbol, model, diagnostics);
        }

        CheckBlock(ctorDecl.Body, VoidType.Instance, model, diagnostics);
        _insideConstructor = false;
    }

    private static void CheckFieldDeclaration(ISymbol fieldSymbol, ISemanticModel model, DiagnosticBag diagnostics)
    {
        SyntaxNode declaringNode;
        try { declaringNode = model.GetDeclaringNode(fieldSymbol); }
        catch { return; }

        if (declaringNode is not FieldDeclarationNode fieldDecl)
            return;

        // Field initializers: the grammar stores the initializer as part of the
        // field declaration. The semantic model builder resolves the field type.
        // For field initializers, we check that the expression type is assignable
        // to the field's declared type. However, the current AST stores the
        // initializer as a Token? (the EQUAL token), not as an ExpressionNode.
        // The actual expression is in the Children() of the field declaration.
        // Since the semantic model builder already handles field type resolution,
        // and field initializer expressions are not separately tracked in the
        // expression type map for the current grammar structure, we skip
        // field initializer type checking here. The C# backend will catch
        // any remaining issues.
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property and indexer declaration checking (Tasks 5.1–5.10)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Checks a property declaration for type correctness.
    /// Walks accessor bodies, checks modifier restrictiveness, field keyword usage,
    /// expression body type compatibility, and static access patterns.
    /// </summary>
    private void CheckPropertyDeclaration(ISymbol propertySymbol, ISemanticModel model, DiagnosticBag diagnostics)
    {
        SyntaxNode declaringNode;
        try { declaringNode = model.GetDeclaringNode(propertySymbol); }
        catch { return; }

        _currentEnclosingType = FindDeclaringType(propertySymbol);
        _insideConstructor = false;
        _insidePropertyAccessor = false; // Will be set to true inside CheckAccessorBody

        var propInfo = GetPropertyMetadata(propertySymbol);
        var propType = propertySymbol.DeclaredType ?? UnknownType.Instance;

        switch (declaringNode)
        {
            case BlockPropertyPropertyDeclarationNode blockProp:
            {
                var propModifiers = ExtractPropertyModifiers(blockProp.Modifiers);

                // Task 5.4: Check accessor modifier restrictiveness (MIST_TC_0022).
                CheckAccessorModifierRestrictiveness(blockProp.Accessors, propModifiers, model, diagnostics);

                // Task 5.1: Walk accessor bodies for type correctness.
                foreach (var accessor in blockProp.Accessors)
                {
                    CheckAccessorBody(accessor, propType, propInfo, propModifiers.IsStatic, model, diagnostics);
                }

                // Task 5.5: Check field keyword usage in accessor bodies (MIST_TC_0023, MIST_TC_0024).
                CheckFieldKeywordUsageInAccessors(blockProp.Accessors, propInfo, model, diagnostics);

                // Task 5.7: Check this in static accessor (MIST_TC_0026).
                if (propModifiers.IsStatic)
                {
                    CheckThisInStaticAccessors(blockProp.Accessors, model, diagnostics);
                }
                break;
            }
            case ExpressionBodiedPropertyPropertyDeclarationNode exprProp:
            {
                var propModifiers = ExtractPropertyModifiers(exprProp.Modifiers);

                // Task 5.8: Check expression body type compatibility (MIST_TC_0028).
                CheckExpression(exprProp.Body, model, diagnostics);
                var bodyType = model.GetTypeOf(exprProp.Body);
                if (bodyType is not null && !IsUnknown(bodyType) && !IsUnknown(propType))
                {
                    if (!IsAssignable(bodyType, propType))
                    {
                        diagnostics.Add(MakeDiagnostic(
                            "MIST_TC_0028",
                            $"Expression body type '{bodyType.DisplayName}' is not compatible with property type '{propType.DisplayName}'",
                            exprProp.Body,
                            model));
                    }
                }

                // Task 5.7: Check this in static expression-bodied property (MIST_TC_0026).
                if (propModifiers.IsStatic)
                {
                    CheckThisInExpression(exprProp.Body, model, diagnostics);
                }
                break;
            }
        }
    }

    /// <summary>
    /// Checks an indexer declaration for type correctness.
    /// Also checks for duplicate indexers with the same parameter types (MIST_TC_0029).
    /// </summary>
    private void CheckIndexerDeclaration(ISymbol indexerSymbol, ISemanticModel model, DiagnosticBag diagnostics)
    {
        SyntaxNode declaringNode;
        try { declaringNode = model.GetDeclaringNode(indexerSymbol); }
        catch { return; }

        if (declaringNode is not BlockIndexerIndexerDeclarationNode blockIndexer)
            return;

        _currentEnclosingType = FindDeclaringType(indexerSymbol);
        _insideConstructor = false;
        _insidePropertyAccessor = false; // Will be set to true inside CheckAccessorBody

        var propInfo = GetPropertyMetadata(indexerSymbol);
        var indexerType = indexerSymbol.DeclaredType ?? UnknownType.Instance;
        var indexerModifiers = ExtractPropertyModifiers(blockIndexer.Modifiers);

        // Task 5.4: Check accessor modifier restrictiveness (MIST_TC_0022).
        CheckAccessorModifierRestrictiveness(blockIndexer.Accessors, indexerModifiers, model, diagnostics);

        // Task 5.1: Walk accessor bodies for type correctness.
        foreach (var accessor in blockIndexer.Accessors)
        {
            CheckAccessorBody(accessor, indexerType, propInfo, indexerModifiers.IsStatic, model, diagnostics);
        }

        // Task 5.9: Check for duplicate indexers (MIST_TC_0029).
        CheckDuplicateIndexer(indexerSymbol, blockIndexer, model, diagnostics);
    }

    /// <summary>
    /// Checks a single accessor body for type correctness.
    /// For block bodies, checks statements. For expression bodies, checks expression type.
    /// For set/init accessors, the implicit 'value' parameter is already injected by the semantic model.
    /// </summary>
    private void CheckAccessorBody(
        AccessorDeclarationNode accessor,
        IType propType,
        PropertyInfo? propInfo,
        bool isStatic,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        var previousInsidePropertyAccessor = _insidePropertyAccessor;
        _insidePropertyAccessor = true;

        try
        {
            switch (accessor)
            {
                // Block get accessor — check body statements, return type is the property type.
                case BlockGetAccessorAccessorDeclarationNode blockGet:
                    CheckBlock(blockGet.Body, propType, model, diagnostics);
                    break;

                // Expression get accessor — check expression type matches property type (MIST_TC_0028).
                case ExpressionGetAccessorAccessorDeclarationNode exprGet:
                    CheckExpression(exprGet.Expression, model, diagnostics);
                    CheckExpressionAccessorType(exprGet.Expression, propType, model, diagnostics);
                    break;

                // Block set accessor — check body statements (return type is void).
                case BlockSetAccessorAccessorDeclarationNode blockSet:
                    CheckBlock(blockSet.Body, VoidType.Instance, model, diagnostics);
                    break;

                // Expression set accessor — check expression.
                case ExpressionSetAccessorAccessorDeclarationNode exprSet:
                    CheckExpression(exprSet.Expression, model, diagnostics);
                    break;

                // Block init accessor — check body statements (return type is void).
                case BlockInitAccessorAccessorDeclarationNode blockInit:
                    CheckBlock(blockInit.Body, VoidType.Instance, model, diagnostics);
                    break;

                // Expression init accessor — check expression.
                case ExpressionInitAccessorAccessorDeclarationNode exprInit:
                    CheckExpression(exprInit.Expression, model, diagnostics);
                    break;

                // Auto accessors (get;, set;, init;) — no body to check.
                default:
                    break;
            }
        }
        finally
        {
            _insidePropertyAccessor = previousInsidePropertyAccessor;
        }
    }

    /// <summary>
    /// Checks that an expression accessor's type is compatible with the property type (MIST_TC_0028).
    /// </summary>
    private static void CheckExpressionAccessorType(
        ExpressionNode expression,
        IType propType,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        var exprType = model.GetTypeOf(expression);
        if (exprType is null || IsUnknown(exprType) || IsUnknown(propType))
            return;

        if (!IsAssignable(exprType, propType))
        {
            diagnostics.Add(MakeDiagnostic(
                "MIST_TC_0028",
                $"Expression body type '{exprType.DisplayName}' is not compatible with property type '{propType.DisplayName}'",
                expression,
                model));
        }
    }

    /// <summary>
    /// Checks accessor modifier restrictiveness (MIST_TC_0022).
    /// An accessor modifier must be strictly more restrictive than the property's access level.
    /// </summary>
    private static void CheckAccessorModifierRestrictiveness(
        IReadOnlyList<AccessorDeclarationNode> accessors,
        MemberModifiers propModifiers,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        foreach (var accessor in accessors)
        {
            var accessorModifier = GetAccessorModifier(accessor);
            if (accessorModifier is null)
                continue; // No explicit accessor modifier — OK.

            var propLevel = GetAccessLevel(propModifiers);
            var accessorLevel = GetModifierAccessLevel(accessorModifier);

            // The accessor modifier must be strictly more restrictive than the property.
            if (!IsMoreRestrictive(accessorLevel, propLevel))
            {
                diagnostics.Add(MakeDiagnostic(
                    "MIST_TC_0022",
                    "Accessor modifier must be more restrictive than the property's access level",
                    accessorModifier,
                    model));
            }
        }
    }

    /// <summary>
    /// Checks for field keyword usage outside accessor bodies (MIST_TC_0023)
    /// and in computed properties (MIST_TC_0024).
    /// </summary>
    private static void CheckFieldKeywordUsageInAccessors(
        IReadOnlyList<AccessorDeclarationNode> accessors,
        PropertyInfo? propInfo,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        if (propInfo is null)
            return;

        foreach (var accessor in accessors)
        {
            var fieldNodes = CollectFieldKeywordNodes(accessor);
            foreach (var fieldNode in fieldNodes)
            {
                // MIST_TC_0024: field keyword in computed property (no backing field).
                if (propInfo.IsComputed)
                {
                    diagnostics.Add(MakeDiagnostic(
                        "MIST_TC_0024",
                        "'field' keyword cannot be used in a computed property (no backing field)",
                        fieldNode,
                        model));
                }
            }
        }
    }

    /// <summary>
    /// Checks for 'this' references in static property accessors (MIST_TC_0026).
    /// </summary>
    private static void CheckThisInStaticAccessors(
        IReadOnlyList<AccessorDeclarationNode> accessors,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        foreach (var accessor in accessors)
        {
            var thisNodes = CollectThisKeywordNodes(accessor);
            foreach (var thisNode in thisNodes)
            {
                diagnostics.Add(MakeDiagnostic(
                    "MIST_TC_0026",
                    "'this' cannot be referenced in a static property accessor",
                    thisNode,
                    model));
            }
        }
    }

    /// <summary>
    /// Checks for 'this' references in a static expression-bodied property (MIST_TC_0026).
    /// </summary>
    private static void CheckThisInExpression(
        ExpressionNode expression,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        var thisNodes = new List<SyntaxNode>();
        CollectThisKeywordNodesFromTree(expression, thisNodes);
        foreach (var thisNode in thisNodes)
        {
            diagnostics.Add(MakeDiagnostic(
                "MIST_TC_0026",
                "'this' cannot be referenced in a static property accessor",
                thisNode,
                model));
        }
    }

    /// <summary>
    /// Checks for duplicate indexers with the same parameter types (MIST_TC_0029).
    /// </summary>
    private static void CheckDuplicateIndexer(
        ISymbol indexerSymbol,
        BlockIndexerIndexerDeclarationNode blockIndexer,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        var declaringType = FindDeclaringType(indexerSymbol);
        if (declaringType is null)
            return;

        IScope? memberScope = declaringType switch
        {
            ClassType ct => ct.MemberScope,
            StructType st => st.MemberScope,
            _ => null,
        };

        if (memberScope is null)
            return;

        // Collect parameter types for this indexer.
        var thisParamTypes = CollectIndexerParameterTypes(blockIndexer, model);

        // Check all other indexers in the same type.
        foreach (var member in memberScope.Members)
        {
            if (member.Kind != SymbolKind.Indexer || ReferenceEquals(member, indexerSymbol))
                continue;

            SyntaxNode otherNode;
            try { otherNode = model.GetDeclaringNode(member); }
            catch { continue; }

            if (otherNode is not BlockIndexerIndexerDeclarationNode otherIndexer)
                continue;

            var otherParamTypes = CollectIndexerParameterTypes(otherIndexer, model);

            if (ParameterTypesMatch(thisParamTypes, otherParamTypes))
            {
                diagnostics.Add(MakeDiagnostic(
                    "MIST_TC_0029",
                    "Duplicate indexer declaration with the same parameter types",
                    blockIndexer,
                    model));
                break; // Only report once per indexer.
            }
        }
    }

    /// <summary>
    /// Collects parameter types from an indexer declaration.
    /// </summary>
    private static List<string> CollectIndexerParameterTypes(
        BlockIndexerIndexerDeclarationNode indexer,
        ISemanticModel model)
    {
        var types = new List<string>();
        if (indexer.Parameters is null)
            return types;

        foreach (var child in indexer.Parameters.Children())
        {
            if (child is ParameterNode param)
            {
                var paramSymbol = model.GetDeclaredSymbol(param);
                if (paramSymbol?.DeclaredType is not null)
                    types.Add(paramSymbol.DeclaredType.DisplayName);
                else
                    types.Add("?");
            }
        }

        return types;
    }

    /// <summary>
    /// Checks if two parameter type lists match.
    /// </summary>
    private static bool ParameterTypesMatch(List<string> a, List<string> b)
    {
        if (a.Count != b.Count)
            return false;

        for (var i = 0; i < a.Count; i++)
        {
            if (a[i] != b[i])
                return false;
        }

        return true;
    }

    /// <summary>
    /// Gets the accessor modifier node from an accessor declaration, if present.
    /// </summary>
    private static ModifierNode? GetAccessorModifier(AccessorDeclarationNode accessor)
    {
        return accessor switch
        {
            AutoGetAccessorAccessorDeclarationNode a => a.AccessorModifier,
            BlockGetAccessorAccessorDeclarationNode a => a.AccessorModifier,
            ExpressionGetAccessorAccessorDeclarationNode a => a.AccessorModifier,
            AutoSetAccessorAccessorDeclarationNode a => a.AccessorModifier,
            BlockSetAccessorAccessorDeclarationNode a => a.AccessorModifier,
            ExpressionSetAccessorAccessorDeclarationNode a => a.AccessorModifier,
            AutoInitAccessorAccessorDeclarationNode a => a.AccessorModifier,
            BlockInitAccessorAccessorDeclarationNode a => a.AccessorModifier,
            ExpressionInitAccessorAccessorDeclarationNode a => a.AccessorModifier,
            _ => null,
        };
    }

    /// <summary>
    /// Access level ordering for restrictiveness comparison.
    /// Lower value = more restrictive.
    /// </summary>
    private enum AccessLevel
    {
        Private = 0,
        // Protected = 1, // Not yet supported in Mist
        // Internal = 2,  // Not yet supported in Mist
        Public = 3,
    }

    /// <summary>
    /// Gets the access level from property modifiers.
    /// Default (no modifier) is private.
    /// </summary>
    private static AccessLevel GetAccessLevel(MemberModifiers modifiers)
    {
        if (modifiers.IsPublic) return AccessLevel.Public;
        return AccessLevel.Private; // Default is private.
    }

    /// <summary>
    /// Gets the access level from a modifier AST node.
    /// </summary>
    private static AccessLevel GetModifierAccessLevel(ModifierNode modifier)
    {
        return modifier switch
        {
            PublicModifierModifierNode => AccessLevel.Public,
            PrivateModifierModifierNode => AccessLevel.Private,
            _ => AccessLevel.Private,
        };
    }

    /// <summary>
    /// Returns true if <paramref name="accessor"/> is strictly more restrictive than <paramref name="property"/>.
    /// </summary>
    private static bool IsMoreRestrictive(AccessLevel accessor, AccessLevel property)
    {
        return (int)accessor < (int)property;
    }

    /// <summary>
    /// Extracts modifier flags from a property's modifier list.
    /// </summary>
    private static MemberModifiers ExtractPropertyModifiers(IReadOnlyList<ModifierNode> modifiers)
    {
        var isPublic = false;
        var isPrivate = false;
        var isStatic = false;

        foreach (var mod in modifiers)
        {
            switch (mod)
            {
                case PublicModifierModifierNode:
                    isPublic = true;
                    break;
                case PrivateModifierModifierNode:
                    isPrivate = true;
                    break;
                case StaticModifierModifierNode:
                    isStatic = true;
                    break;
            }
        }

        return new MemberModifiers(isPublic, isPrivate, isStatic);
    }

    /// <summary>
    /// Gets PropertyInfo from a symbol (handles both Symbol and ISymbol).
    /// </summary>
    private static PropertyInfo? GetPropertyMetadata(ISymbol symbol)
    {
        if (symbol is Mist.Semantics.Model.Symbol concreteSymbol)
            return concreteSymbol.PropertyMetadata;
        return null;
    }

    /// <summary>
    /// Collects all FieldKeywordExpressionPrimaryNode nodes from an accessor's body.
    /// </summary>
    private static List<SyntaxNode> CollectFieldKeywordNodes(AccessorDeclarationNode accessor)
    {
        var nodes = new List<SyntaxNode>();
        CollectFieldKeywordNodesFromTree(accessor, nodes);
        return nodes;
    }

    private static void CollectFieldKeywordNodesFromTree(SyntaxNode node, List<SyntaxNode> results)
    {
        if (node is FieldKeywordExpressionPrimaryNode)
        {
            results.Add(node);
            return;
        }

        foreach (var child in node.Children())
        {
            if (child is not null)
                CollectFieldKeywordNodesFromTree(child, results);
        }
    }

    /// <summary>
    /// Collects all ThisExpressionPrimaryNode nodes from an accessor's body.
    /// </summary>
    private static List<SyntaxNode> CollectThisKeywordNodes(AccessorDeclarationNode accessor)
    {
        var nodes = new List<SyntaxNode>();
        CollectThisKeywordNodesFromTree(accessor, nodes);
        return nodes;
    }

    private static void CollectThisKeywordNodesFromTree(SyntaxNode node, List<SyntaxNode> results)
    {
        if (node is ThisExpressionPrimaryNode)
        {
            results.Add(node);
            return;
        }

        foreach (var child in node.Children())
        {
            if (child is not null)
                CollectThisKeywordNodesFromTree(child, results);
        }
    }

    /// <summary>
    /// Checks that all types implementing interfaces provide required property accessors (MIST_TC_0030).
    /// This complements the semantic model builder's Pass1d validation with type-checker-level diagnostics.
    /// </summary>
    private static void CheckMissingInterfaceProperties(ISemanticModel model, DiagnosticBag diagnostics)
    {
        foreach (var symbol in model.AllSymbols)
        {
            if (symbol.Origin == SymbolOrigin.Backend)
                continue;

            if (symbol.Kind is not (SymbolKind.Class or SymbolKind.Struct))
                continue;

            var type = symbol.DeclaredType;
            IReadOnlyList<IType>? interfaces = type switch
            {
                ClassType ct => ct.Interfaces,
                StructType st => st.Interfaces,
                _ => null,
            };

            if (interfaces is null || interfaces.Count == 0)
                continue;

            IScope? memberScope = type switch
            {
                ClassType ct => ct.MemberScope,
                StructType st => st.MemberScope,
                _ => null,
            };

            if (memberScope is null)
                continue;

            // Check each interface's property requirements.
            var visited = new HashSet<InterfaceType>(ReferenceEqualityComparer.Instance);
            CheckInterfacePropertyRequirements(symbol, interfaces, memberScope, type as ClassType, model, diagnostics, visited);
        }
    }

    private static void CheckInterfacePropertyRequirements(
        ISymbol typeSymbol,
        IReadOnlyList<IType> interfaces,
        IScope memberScope,
        ClassType? classType,
        ISemanticModel model,
        DiagnosticBag diagnostics,
        HashSet<InterfaceType> visited)
    {
        foreach (var ifaceType in interfaces)
        {
            if (ifaceType is not InterfaceType iface)
                continue;

            if (!visited.Add(iface))
                continue;

            foreach (var ifaceMember in iface.MemberScope.Members)
            {
                if (ifaceMember.Kind != SymbolKind.Property)
                    continue;

                var requiredPropInfo = GetPropertyMetadata(ifaceMember);
                if (requiredPropInfo is null)
                    continue;

                // Search for matching property in the implementing type.
                var implProperty = FindPropertyInTypeHierarchy(ifaceMember.Name, memberScope, classType);

                if (implProperty is null)
                {
                    // Get the declaring node for location info.
                    SyntaxNode? typeNode;
                    try { typeNode = model.GetDeclaringNode(typeSymbol); }
                    catch { typeNode = null; }

                    if (typeNode is not null)
                    {
                        diagnostics.Add(MakeDiagnostic(
                            "MIST_TC_0030",
                            $"'{typeSymbol.Name}' does not implement interface property '{iface.DisplayName}.{ifaceMember.Name}'",
                            typeNode,
                            model));
                    }
                    continue;
                }

                var implPropInfo = GetPropertyMetadata(implProperty);
                if (implPropInfo is null)
                    continue;

                // Check accessor satisfaction.
                SyntaxNode? implNode;
                try { implNode = model.GetDeclaringNode(implProperty); }
                catch { implNode = null; }

                if (requiredPropInfo.HasGet && !implPropInfo.HasGet && implNode is not null)
                {
                    diagnostics.Add(MakeDiagnostic(
                        "MIST_TC_0030",
                        $"'{typeSymbol.Name}.{ifaceMember.Name}' does not implement 'get' accessor required by interface '{iface.DisplayName}'",
                        implNode,
                        model));
                }

                if (requiredPropInfo.HasSet && !implPropInfo.HasSet && !implPropInfo.HasInit && implNode is not null)
                {
                    diagnostics.Add(MakeDiagnostic(
                        "MIST_TC_0030",
                        $"'{typeSymbol.Name}.{ifaceMember.Name}' does not implement 'set' accessor required by interface '{iface.DisplayName}'",
                        implNode,
                        model));
                }
            }

            // Recurse into parent interfaces.
            if (iface.Interfaces.Count > 0)
            {
                CheckInterfacePropertyRequirements(typeSymbol, iface.Interfaces, memberScope, classType, model, diagnostics, visited);
            }
        }
    }

    /// <summary>
    /// Searches for a property by name in a type's member scope and base type chain.
    /// </summary>
    private static ISymbol? FindPropertyInTypeHierarchy(string propertyName, IScope memberScope, ClassType? classType)
    {
        foreach (var member in memberScope.Members)
        {
            if (member.Kind == SymbolKind.Property && member.Name == propertyName)
                return member;
        }

        if (classType is not null)
        {
            var ancestor = classType.BaseType as ClassType;
            while (ancestor is not null)
            {
                foreach (var member in ancestor.MemberScope.Members)
                {
                    if (member.Kind == SymbolKind.Property && member.Name == propertyName)
                        return member;
                }
                ancestor = ancestor.BaseType as ClassType;
            }
        }

        return null;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Block and statement checking
    // ═══════════════════════════════════════════════════════════════════════

    private void CheckBlock(BlockNode block, IType returnType, ISemanticModel model, DiagnosticBag diagnostics)
    {
        foreach (var stmt in block.Statements)
        {
            CheckStatement(stmt, returnType, model, diagnostics);
        }
    }

    private void CheckStatement(StatementNode stmt, IType returnType, ISemanticModel model, DiagnosticBag diagnostics)
    {
        switch (stmt)
        {
            case VariableDeclarationStatementStatementNode varDeclStmt:
                CheckVariableDeclarationStatement(varDeclStmt.VariableDeclaration, model, diagnostics);
                break;
            case ReturnStatementStatementNode returnStmt:
                CheckReturnStatement(returnStmt.ReturnStatement, returnType, model, diagnostics);
                break;
            case IfStatementStatementNode ifStmt:
                CheckIfStatement(ifStmt.IfStatement, returnType, model, diagnostics);
                break;
            case WhileStatementStatementNode whileStmt:
                CheckWhileStatement(whileStmt.WhileStatement, returnType, model, diagnostics);
                break;
            case ForStatementStatementNode forStmt:
                CheckForStatement(forStmt.ForStatement, returnType, model, diagnostics);
                break;
            case ForeachStatementStatementNode foreachStmt:
                CheckForeachStatement(foreachStmt.ForeachStatement, returnType, model, diagnostics);
                break;
            case ExpressionStatementStatementNode exprStmt:
                CheckExpression(exprStmt.ExpressionStatement.Expression, model, diagnostics);
                break;
            case BreakStatementStatementNode:
                // No type checking needed for break.
                break;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Variable declaration checking
    // ═══════════════════════════════════════════════════════════════════════

    private void CheckVariableDeclarationStatement(
        VariableDeclarationStatementNode varDecl,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        switch (varDecl)
        {
            case TypedVariableDeclarationVariableDeclarationStatementNode typed:
            {
                // Check the initializer expression.
                CheckExpression(typed.Initializer, model, diagnostics);

                // Verify RHS type is assignable to declared type (MIST_TC_0001).
                var declaredType = model.GetTypeOf(typed.Type);
                var initType = model.GetTypeOf(typed.Initializer);

                // When the TypeReference node isn't in the expression type map
                // (source-parsed programs), fall back to the variable symbol's
                // declared type so we can still detect type mismatches
                // (e.g., TypeType assigned to a non-Type variable).
                declaredType ??= model.GetDeclaredSymbol(typed)?.DeclaredType;

                if (declaredType is not null && initType is not null)
                {
                    // Allow numeric-to-numeric assignments in variable declarations
                    // to support constant narrowing (e.g., byte x = 0; where 0 is int).
                    // Non-numeric type mismatches (e.g., Type → int) are still checked.
                    if (!(IsNumeric(initType) && IsNumeric(declaredType)))
                    {
                        CheckAssignability(initType, declaredType, typed.Initializer, diagnostics, model);
                    }
                }
                break;
            }
            case InferredVariableDeclarationVariableDeclarationStatementNode inferred:
            {
                // var with initializer — check the initializer expression.
                CheckExpression(inferred.Initializer, model, diagnostics);

                // MIST_TC_0009: var without initializer.
                // The grammar requires an initializer for var declarations,
                // but if the initializer expression type is null, the var
                // type cannot be inferred.
                var inferredType = model.GetTypeOf(inferred.Initializer);
                if (inferredType is null)
                {
                    diagnostics.Add(MakeDiagnostic(
                        "MIST_TC_0009",
                        "'var' declaration requires an initializer",
                        inferred,
                        model));
                }
                break;
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Return statement checking (MIST_TC_0008)
    // ═══════════════════════════════════════════════════════════════════════

    private void CheckReturnStatement(
        ReturnStatementNode returnStmt,
        IType methodReturnType,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        if (IsUnknown(methodReturnType))
            return;

        if (returnStmt.Value is not null)
        {
            // return expr; — check the expression and verify type.
            CheckExpression(returnStmt.Value, model, diagnostics);
            var valueType = model.GetTypeOf(returnStmt.Value);

            if (methodReturnType is VoidType)
            {
                // Void methods must not return a value.
                if (valueType is not null && !IsUnknown(valueType))
                {
                    diagnostics.Add(MakeDiagnostic(
                        "MIST_TC_0008",
                        $"Cannot return '{valueType.DisplayName}' from method with return type 'void'",
                        returnStmt,
                        model));
                }
            }
            else if (valueType is not null)
            {
                if (!IsAssignable(valueType, methodReturnType))
                {
                    diagnostics.Add(MakeDiagnostic(
                        "MIST_TC_0008",
                        $"Cannot return '{valueType.DisplayName}' from method with return type '{methodReturnType.DisplayName}'",
                        returnStmt,
                        model));
                }
            }
        }
        else
        {
            // return; — only valid in void methods.
            if (methodReturnType is not VoidType)
            {
                diagnostics.Add(MakeDiagnostic(
                    "MIST_TC_0008",
                    $"Cannot return '' from method with return type '{methodReturnType.DisplayName}'",
                    returnStmt,
                    model));
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Control flow condition checking (MIST_TC_0007)
    // ═══════════════════════════════════════════════════════════════════════

    private void CheckIfStatement(IfStatementNode ifStmt, IType returnType, ISemanticModel model, DiagnosticBag diagnostics)
    {
        CheckExpression(ifStmt.Condition, model, diagnostics);
        CheckConditionIsBool(ifStmt.Condition, model, diagnostics);
        CheckBlock(ifStmt.ThenBranch, returnType, model, diagnostics);

        if (ifStmt.ElseBranch is ElseIfElseClauseNode elseIf)
        {
            CheckIfStatement(elseIf.ElseIf, returnType, model, diagnostics);
        }
        else if (ifStmt.ElseBranch is ElseBlockElseClauseNode elseBlock)
        {
            CheckBlock(elseBlock.ElseBlock, returnType, model, diagnostics);
        }
    }

    private void CheckWhileStatement(WhileStatementNode whileStmt, IType returnType, ISemanticModel model, DiagnosticBag diagnostics)
    {
        CheckExpression(whileStmt.Condition, model, diagnostics);
        CheckConditionIsBool(whileStmt.Condition, model, diagnostics);
        CheckBlock(whileStmt.Body, returnType, model, diagnostics);
    }

    private void CheckForStatement(ForStatementNode forStmt, IType returnType, ISemanticModel model, DiagnosticBag diagnostics)
    {
        // Check initializer.
        if (forStmt.Initializer is ForInitializerVariableDeclarationForInitializerNode varInit)
        {
            CheckExpression(varInit.Initializer, model, diagnostics);
            // Check assignability of initializer to declared type.
            var declaredType = model.GetTypeOf(varInit.VariableDeclaration);
            var initType = model.GetTypeOf(varInit.Initializer);
            if (declaredType is not null && initType is not null)
            {
                CheckAssignability(initType, declaredType, varInit.Initializer, diagnostics, model);
            }
        }
        else if (forStmt.Initializer is ForInitializerExpressionForInitializerNode exprInit)
        {
            CheckExpression(exprInit.Expression, model, diagnostics);
        }

        // Check condition.
        if (forStmt.Condition is not null)
        {
            CheckExpression(forStmt.Condition, model, diagnostics);
            CheckConditionIsBool(forStmt.Condition, model, diagnostics);
        }

        // Check iterator.
        if (forStmt.Iterator is not null)
        {
            CheckExpression(forStmt.Iterator, model, diagnostics);
        }

        CheckBlock(forStmt.Body, returnType, model, diagnostics);
    }

    private void CheckForeachStatement(ForeachStatementNode foreachStmt, IType returnType, ISemanticModel model, DiagnosticBag diagnostics)
    {
        CheckExpression(foreachStmt.Collection, model, diagnostics);
        CheckBlock(foreachStmt.Body, returnType, model, diagnostics);
    }

    private static void CheckConditionIsBool(ExpressionNode condition, ISemanticModel model, DiagnosticBag diagnostics)
    {
        var condType = model.GetTypeOf(condition);
        if (condType is null || IsUnknown(condType))
            return;

        if (condType is not PrimitiveType pt || pt != PrimitiveType.Bool)
        {
            diagnostics.Add(MakeDiagnostic(
                "MIST_TC_0007",
                $"Condition must be of type 'bool', but found '{condType.DisplayName}'",
                condition,
                model));
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Expression checking — recursive walk
    // ═══════════════════════════════════════════════════════════════════════

    private void CheckExpression(ExpressionNode expr, ISemanticModel model, DiagnosticBag diagnostics)
    {
        CheckAssignmentNode(expr.Assignment, model, diagnostics);
    }

    private void CheckAssignmentNode(AssignmentNode assignment, ISemanticModel model, DiagnosticBag diagnostics)
    {
        CheckLogicalOrNode(assignment.Target, model, diagnostics);

        if (assignment.Value is not null)
        {
            // This is an assignment: target = value.
            CheckAssignmentNode(assignment.Value, model, diagnostics);

            var lhsType = model.GetTypeOf(assignment.Target);
            var rhsType = model.GetTypeOf(assignment.Value);

            if (lhsType is not null && rhsType is not null)
            {
                CheckAssignability(rhsType, lhsType, assignment, diagnostics, model);
            }

            // Property-specific assignment checks (MIST_TC_0020, MIST_TC_0021, MIST_TC_0025).
            var targetPropertySymbol = TryResolvePropertyFromAssignmentTarget(assignment.Target, model);
            if (targetPropertySymbol is not null)
            {
                var propInfo = GetPropertyMetadata(targetPropertySymbol);
                if (propInfo is not null)
                {
                    // MIST_TC_0020: No accessible set/init accessor.
                    if (!propInfo.HasSet && !propInfo.HasInit)
                    {
                        diagnostics.Add(MakeDiagnostic(
                            "MIST_TC_0020",
                            $"Property '{targetPropertySymbol.Name}' has no accessible 'set' or 'init' accessor",
                            assignment,
                            model));
                    }
                    // MIST_TC_0025: init-only property assigned outside constructor context.
                    else if (propInfo.HasInit && !propInfo.HasSet && !_insideConstructor)
                    {
                        diagnostics.Add(MakeDiagnostic(
                            "MIST_TC_0025",
                            $"Init-only property '{targetPropertySymbol.Name}' can only be assigned in a constructor or object initializer",
                            assignment,
                            model));
                    }

                    // MIST_TC_0021: Property type mismatch.
                    var propType = targetPropertySymbol.DeclaredType;
                    if (propType is not null && rhsType is not null && !IsUnknown(propType) && !IsUnknown(rhsType))
                    {
                        if (!IsAssignable(rhsType, propType))
                        {
                            diagnostics.Add(MakeDiagnostic(
                                "MIST_TC_0021",
                                $"Cannot assign '{rhsType.DisplayName}' to property of type '{propType.DisplayName}'",
                                assignment,
                                model));
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Attempts to resolve the property symbol from the LHS of an assignment.
    /// Returns the property symbol if the target is a member access resolving to a property, null otherwise.
    /// </summary>
    private static ISymbol? TryResolvePropertyFromAssignmentTarget(LogicalOrNode target, ISemanticModel model)
    {
        // Walk down the expression tree to find the innermost postfix expression.
        // An assignment target like `obj.Prop` is: PostfixExpression(target=obj, suffix=[MemberAccessSuffix(Prop)])
        // We need to check if the last suffix resolves to a property symbol.

        // Navigate through the expression tree: LogicalOr -> LogicalAnd -> Equality -> Relational -> Additive -> Multiplicative -> Unary -> Postfix
        var logicalAnd = target.Left;
        if (target.Right is not null) return null;

        var equality = logicalAnd.Left;
        if (logicalAnd.Right is not null) return null;

        var relational = equality.Left;
        if (equality.Right is not null) return null;

        var additive = relational.Left;
        if (relational.Right is not null) return null;

        var multiplicative = additive.Left;
        if (additive.Right is not null) return null;

        var unary = multiplicative.Left;
        if (multiplicative.Right is not null) return null;

        if (unary is not PostfixExpressionUnaryNode postfixExpr)
            return null;

        var postfix = postfixExpr.Primary;

        // Check if the last suffix is a MemberAccessSuffix that resolves to a property.
        if (postfix.Suffix.Length > 0)
        {
            var lastSuffix = postfix.Suffix[^1];
            if (lastSuffix is MemberAccessSuffixPostfixSuffixNode memberAccess)
            {
                var symbol = model.GetDeclaredSymbol(memberAccess);
                if (symbol is not null && symbol.Kind == SymbolKind.Property)
                    return symbol;
            }
        }
        else
        {
            // Simple identifier — check if it resolves to a property.
            var symbol = model.GetDeclaredSymbol(postfix.Target);
            if (symbol is not null && symbol.Kind == SymbolKind.Property)
                return symbol;
        }

        return null;
    }

    private void CheckLogicalOrNode(LogicalOrNode node, ISemanticModel model, DiagnosticBag diagnostics)
    {
        CheckLogicalAndNode(node.Left, model, diagnostics);

        if (node.Right is not null)
        {
            CheckLogicalAndNode(node.Right, model, diagnostics);

            var lhsType = model.GetTypeOf(node.Left);
            var rhsType = model.GetTypeOf(node.Right);

            if (lhsType is not null && rhsType is not null
                && !IsUnknown(lhsType) && !IsUnknown(rhsType))
            {
                if (lhsType != PrimitiveType.Bool || rhsType != PrimitiveType.Bool)
                {
                    diagnostics.Add(MakeDiagnostic(
                        "MIST_TC_0006",
                        "Operator '||' requires operands of type 'bool'",
                        node,
                        model));
                }
            }
        }
    }

    private void CheckLogicalAndNode(LogicalAndNode node, ISemanticModel model, DiagnosticBag diagnostics)
    {
        CheckEqualityNode(node.Left, model, diagnostics);

        if (node.Right is not null)
        {
            CheckEqualityNode(node.Right, model, diagnostics);

            var lhsType = model.GetTypeOf(node.Left);
            var rhsType = model.GetTypeOf(node.Right);

            if (lhsType is not null && rhsType is not null
                && !IsUnknown(lhsType) && !IsUnknown(rhsType))
            {
                if (lhsType != PrimitiveType.Bool || rhsType != PrimitiveType.Bool)
                {
                    diagnostics.Add(MakeDiagnostic(
                        "MIST_TC_0006",
                        "Operator '&&' requires operands of type 'bool'",
                        node,
                        model));
                }
            }
        }
    }

    private void CheckEqualityNode(EqualityNode node, ISemanticModel model, DiagnosticBag diagnostics)
    {
        CheckRelationalNode(node.Left, model, diagnostics);

        if (node.Right is not null)
        {
            CheckRelationalNode(node.Right, model, diagnostics);

            var lhsType = model.GetTypeOf(node.Left);
            var rhsType = model.GetTypeOf(node.Right);
            var op = node.Operator.Lexeme ?? "==";

            if (lhsType is not null && rhsType is not null
                && !IsUnknown(lhsType) && !IsUnknown(rhsType))
            {
                if (!AreEqualityCompatible(lhsType, rhsType))
                {
                    diagnostics.Add(MakeDiagnostic(
                        "MIST_TC_0005",
                        $"Operator '{op}' cannot be applied to operands of type '{lhsType.DisplayName}' and '{rhsType.DisplayName}'",
                        node,
                        model));
                }
            }
        }
    }

    private void CheckRelationalNode(RelationalNode node, ISemanticModel model, DiagnosticBag diagnostics)
    {
        CheckAdditiveNode(node.Left, model, diagnostics);

        if (node.Right is not null)
        {
            CheckAdditiveNode(node.Right, model, diagnostics);

            var lhsType = model.GetTypeOf(node.Left);
            var rhsType = model.GetTypeOf(node.Right);
            var op = node.Operator.Lexeme ?? "<";

            if (lhsType is not null && rhsType is not null
                && !IsUnknown(lhsType) && !IsUnknown(rhsType))
            {
                if (!IsNumeric(lhsType) || !IsNumeric(rhsType))
                {
                    diagnostics.Add(MakeDiagnostic(
                        "MIST_TC_0004",
                        $"Operator '{op}' cannot be applied to operands of type '{lhsType.DisplayName}' and '{rhsType.DisplayName}'",
                        node,
                        model));
                }
            }
        }
    }

    private void CheckAdditiveNode(AdditiveNode node, ISemanticModel model, DiagnosticBag diagnostics)
    {
        CheckMultiplicativeNode(node.Left, model, diagnostics);

        if (node.Right is not null)
        {
            CheckMultiplicativeNode(node.Right, model, diagnostics);

            var lhsType = model.GetTypeOf(node.Left);
            var rhsType = model.GetTypeOf(node.Right);
            var op = node.Operator.Lexeme ?? "+";

            if (lhsType is not null && rhsType is not null
                && !IsUnknown(lhsType) && !IsUnknown(rhsType))
            {
                // + allows string concatenation.
                if (op == "+")
                {
                    var lhsIsString = lhsType is StringType;
                    var rhsIsString = rhsType is StringType;
                    if (lhsIsString || rhsIsString)
                    {
                        // String concatenation is allowed.
                        return;
                    }
                }

                if (!IsNumeric(lhsType) || !IsNumeric(rhsType))
                {
                    diagnostics.Add(MakeDiagnostic(
                        "MIST_TC_0003",
                        $"Operator '{op}' cannot be applied to operands of type '{lhsType.DisplayName}' and '{rhsType.DisplayName}'",
                        node,
                        model));
                }
            }
        }
    }

    private void CheckMultiplicativeNode(MultiplicativeNode node, ISemanticModel model, DiagnosticBag diagnostics)
    {
        CheckUnaryNode(node.Left, model, diagnostics);

        if (node.Right is not null)
        {
            CheckUnaryNode(node.Right, model, diagnostics);

            var lhsType = model.GetTypeOf(node.Left);
            var rhsType = model.GetTypeOf(node.Right);
            var op = node.Operator.Lexeme ?? "*";

            if (lhsType is not null && rhsType is not null
                && !IsUnknown(lhsType) && !IsUnknown(rhsType))
            {
                if (!IsNumeric(lhsType) || !IsNumeric(rhsType))
                {
                    diagnostics.Add(MakeDiagnostic(
                        "MIST_TC_0003",
                        $"Operator '{op}' cannot be applied to operands of type '{lhsType.DisplayName}' and '{rhsType.DisplayName}'",
                        node,
                        model));
                }
            }
        }
    }

    private void CheckUnaryNode(UnaryNode node, ISemanticModel model, DiagnosticBag diagnostics)
    {
        switch (node)
        {
            case LogicalNotUnaryNode logicalNot:
            {
                CheckUnaryNode(logicalNot.Operand, model, diagnostics);
                var operandType = model.GetTypeOf(logicalNot.Operand);
                if (operandType is not null && !IsUnknown(operandType))
                {
                    if (operandType != PrimitiveType.Bool)
                    {
                        diagnostics.Add(MakeDiagnostic(
                            "MIST_TC_0006",
                            "Operator '!' requires operands of type 'bool'",
                            logicalNot,
                            model));
                    }
                }
                break;
            }
            case NegateUnaryNode negate:
            {
                CheckUnaryNode(negate.Operand, model, diagnostics);
                var operandType = model.GetTypeOf(negate.Operand);
                if (operandType is not null && !IsUnknown(operandType))
                {
                    if (!IsNumeric(operandType))
                    {
                        diagnostics.Add(MakeDiagnostic(
                            "MIST_TC_0003",
                            $"Operator '-' cannot be applied to operands of type '{operandType.DisplayName}' and '{operandType.DisplayName}'",
                            negate,
                            model));
                    }
                }
                break;
            }
            case PostfixExpressionUnaryNode postfix:
                CheckPostfixNode(postfix.Primary, model, diagnostics);
                break;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Postfix and primary expression checking
    // ═══════════════════════════════════════════════════════════════════════

    private void CheckPostfixNode(PostfixNode node, ISemanticModel model, DiagnosticBag diagnostics)
    {
        CheckPrimaryNode(node.Target, model, diagnostics);

        // Determine if the target is a type name (for static access checks).
        var targetIsTypeName = IsTargetTypeName(node.Target, model);

        // Track the running type of the target as we process suffixes.
        var currentType = model.GetTypeOf(node.Target);

        foreach (var suffix in node.Suffix)
        {
            switch (suffix)
            {
                case MethodCallSuffixPostfixSuffixNode methodCall:
                    CheckMethodCallArguments(methodCall, currentType, model, diagnostics);
                    CheckMemberAccessModifiers(methodCall.Name.Lexeme, currentType, targetIsTypeName, methodCall, model, diagnostics);
                    currentType = SubstituteMemberType(model.GetTypeOf(suffix), currentType);
                    // Also substitute method-level type arguments in the return type.
                    currentType = SubstituteMethodReturnType(currentType, methodCall, model);
                    // After the first suffix, subsequent accesses are on instances (the result of the method call).
                    targetIsTypeName = false;
                    break;

                case InvocationSuffixPostfixSuffixNode invocation:
                    CheckInvocationArguments(invocation, currentType, model, diagnostics);
                    currentType = model.GetTypeOf(suffix);
                    targetIsTypeName = false;
                    break;

                case IndexSuffixPostfixSuffixNode indexSuffix:
                    CheckIndexSuffix(indexSuffix, currentType, model, diagnostics);
                    currentType = model.GetTypeOf(suffix);
                    targetIsTypeName = false;
                    break;

                case MemberAccessSuffixPostfixSuffixNode memberAccess:
                    CheckMemberAccessModifiers(memberAccess.Name.Lexeme, currentType, targetIsTypeName, memberAccess, model, diagnostics);
                    // MIST_TC_0019: Check property has accessible get accessor when read.
                    CheckPropertyReadAccess(memberAccess, currentType, model, diagnostics);
                    currentType = SubstituteMemberType(model.GetTypeOf(suffix), currentType);
                    // After accessing a field, subsequent accesses are on the field's value (an instance).
                    targetIsTypeName = false;
                    break;
            }
        }
    }

    /// <summary>
    /// Substitutes type parameters in a member's type when the target is a <see cref="ConstructedType"/>.
    /// For example, if the member type is <c>T</c> and the target is <c>Box&lt;int&gt;</c>,
    /// the result is <c>int</c>.
    /// </summary>
    private static IType? SubstituteMemberType(IType? memberType, IType? targetType)
    {
        if (memberType is null || targetType is not ConstructedType ct)
            return memberType;

        var typeParams = GetTypeParameters(ct.GenericDefinition);
        if (typeParams is null)
            return memberType;

        return SubstituteType(memberType, typeParams, ct.TypeArguments);
    }

    /// <summary>
    /// Substitutes method-level type parameters in the return type of a generic method invocation.
    /// For example, if the method is <c>T Identity&lt;T&gt;(T value)</c> and invoked as <c>Identity&lt;int&gt;(42)</c>,
    /// the return type <c>T</c> becomes <c>int</c>.
    /// </summary>
    private static IType? SubstituteMethodReturnType(IType? returnType, MethodCallSuffixPostfixSuffixNode methodCall, ISemanticModel model)
    {
        if (returnType is null || methodCall.TypeArgs is null)
            return returnType;

        var methodSymbol = model.GetDeclaredSymbol(methodCall);
        if (methodSymbol is not Symbol sym || sym.TypeParameters.Count == 0)
            return returnType;

        var methodTypeArgs = ResolveTypeArgumentsFromAst(methodCall.TypeArgs, model);
        if (methodTypeArgs.Count != sym.TypeParameters.Count)
            return returnType;

        return SubstituteType(returnType, sym.TypeParameters, methodTypeArgs);
    }

    /// <summary>
    /// Checks that a property being read has an accessible get accessor (MIST_TC_0019).
    /// </summary>
    private static void CheckPropertyReadAccess(
        MemberAccessSuffixPostfixSuffixNode memberAccess,
        IType? targetType,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        if (targetType is null || IsUnknown(targetType))
            return;

        var memberName = memberAccess.Name.Lexeme;
        var memberSymbol = FindMemberSymbol(targetType, memberName, model);
        if (memberSymbol is null || memberSymbol.Kind != SymbolKind.Property)
            return;

        // Skip backend symbols — they are assumed to have proper accessors.
        if (memberSymbol.Origin == SymbolOrigin.Backend)
            return;

        var propInfo = GetPropertyMetadata(memberSymbol);
        if (propInfo is null)
            return;

        if (!propInfo.HasGet)
        {
            diagnostics.Add(MakeDiagnostic(
                "MIST_TC_0019",
                $"Property '{memberName}' has no accessible 'get' accessor",
                memberAccess,
                model));
        }
    }

    private void CheckPrimaryNode(PrimaryNode node, ISemanticModel model, DiagnosticBag diagnostics)
    {
        switch (node)
        {
            case ArrayCreationExpressionPrimaryNode arrayCreation:
                CheckArrayCreation(arrayCreation, model, diagnostics);
                break;

            case ObjectCreationExpressionPrimaryNode objectCreation:
                CheckObjectCreation(objectCreation, model, diagnostics);
                break;

            case ParenthesizedExpressionPrimaryNode paren:
                CheckExpression(paren.Inner, model, diagnostics);
                break;

            case InterpolatedStringExpressionPrimaryNode interpolated:
                foreach (var part in interpolated.InterpolatedString.Parts)
                {
                    if (part is ExpressionPartInterpolatedPartNode exprPart)
                    {
                        CheckExpression(exprPart.Expression, model, diagnostics);
                    }
                }
                break;

            case TypeLiteralExpressionPrimaryNode:
                // Type literals are always valid primary expressions.
                // Type checking is handled by assignability checks at usage sites.
                break;

            case FieldKeywordExpressionPrimaryNode fieldKeyword:
                // MIST_TC_0023: 'field' keyword used outside property accessor body.
                // If we reach here during method/constructor body checking, it means
                // 'field' is used outside a property accessor context.
                // Note: When checking property accessor bodies, the semantic model
                // resolves 'field' to the backing field symbol, so this check only
                // fires when 'field' appears in a non-property context.
                if (!_insidePropertyAccessor)
                {
                    diagnostics.Add(MakeDiagnostic(
                        "MIST_TC_0023",
                        "'field' keyword can only be used inside a property accessor body",
                        fieldKeyword,
                        model));
                }
                break;

            // Literals and identifiers don't need type checking.
            default:
                break;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Method invocation checking (MIST_TC_0002)
    // ═══════════════════════════════════════════════════════════════════════

    private void CheckMethodCallArguments(
        MethodCallSuffixPostfixSuffixNode methodCall,
        IType? targetType,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Check argument expressions recursively.
        if (methodCall.Arguments is not null)
        {
            CheckArgumentListExpressions(methodCall.Arguments, model, diagnostics);
        }

        // Look up the method symbol to check argument types against parameter types.
        var methodSymbol = model.GetDeclaredSymbol(methodCall);
        if (methodSymbol is null || methodSymbol.Kind != SymbolKind.Method)
            return;

        // Determine type parameter substitution context.
        // 1. If the target is a ConstructedType, substitute the enclosing type's type params.
        // 2. If the method has its own type arguments, substitute those as well.
        IReadOnlyList<TypeParameterType>? enclosingTypeParams = null;
        IReadOnlyList<IType>? enclosingTypeArgs = null;
        if (targetType is ConstructedType targetCt)
        {
            enclosingTypeParams = GetTypeParameters(targetCt.GenericDefinition);
            enclosingTypeArgs = targetCt.TypeArguments;
        }

        // Resolve method-level type arguments if present.
        IReadOnlyList<TypeParameterType>? methodTypeParams = null;
        IReadOnlyList<IType>? methodTypeArgs = null;
        if (methodCall.TypeArgs is not null && methodSymbol is Symbol sym && sym.TypeParameters.Count > 0)
        {
            methodTypeParams = sym.TypeParameters;
            methodTypeArgs = ResolveTypeArgumentsFromAst(methodCall.TypeArgs, model);
        }

        CheckArgumentTypesWithSubstitution(methodCall.Arguments, methodSymbol, model, diagnostics, methodCall,
            enclosingTypeParams, enclosingTypeArgs, methodTypeParams, methodTypeArgs);
            
        var methodName = methodCall.Name.Lexeme;

        // Collect the full overload set from the target type (and inherited methods from base types).
        var candidates = CollectMethodOverloads(targetType, methodName);

        // If there are multiple candidates, use overload resolution.
        if (candidates.Count > 1)
        {
            var argTypes = CollectArgumentTypes(methodCall.Arguments, model);
            var result = OverloadResolver.Resolve(candidates, argTypes, model);

            switch (result.Status)
            {
                case OverloadResolutionStatus.NoApplicableCandidate:
                    var argTypeNames = string.Join(", ", argTypes.Select(t => t.DisplayName));
                    diagnostics.Add(MakeDiagnostic(
                        "MIST_TC_0031",
                        $"No overload for '{methodName}' matches argument types ({argTypeNames})",
                        (SyntaxNode)methodCall,
                        model));
                    return;

                case OverloadResolutionStatus.AmbiguousCall:
                    var sigs = result.AmbiguousCandidates!
                        .Select(c => FormatCandidateSignature(c, methodName, model));
                    diagnostics.Add(MakeDiagnostic(
                        "MIST_TC_0032",
                        $"Call to '{methodName}' is ambiguous between: {string.Join(" and ", sigs)}",
                        (SyntaxNode)methodCall,
                        model));
                    return;

                case OverloadResolutionStatus.Success:
                    // Resolved successfully — proceed with the resolved symbol for further type checking.
                    CheckArgumentTypes(methodCall.Arguments, result.BestCandidate!, model, diagnostics, methodCall);
                    return;
            }
        }
        else
        {
            // Single candidate — preserve existing single-method diagnostics (MIST_TC_0002).
            CheckArgumentTypes(methodCall.Arguments, methodSymbol, model, diagnostics, methodCall);
        }
    }

    private void CheckInvocationArguments(
        InvocationSuffixPostfixSuffixNode invocation,
        IType? targetType,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Check argument expressions recursively.
        if (invocation.Arguments is not null)
        {
            CheckArgumentListExpressions(invocation.Arguments, model, diagnostics);
        }

        // Look up the invoked symbol.
        var invokedSymbol = model.GetDeclaredSymbol(invocation);
        if (invokedSymbol is null)
            return;

        if (invokedSymbol.Kind == SymbolKind.Method)
        {
            var methodName = invokedSymbol.Name;

            // Collect the full overload set from the target type (and inherited methods from base types).
            var candidates = CollectMethodOverloads(targetType, methodName);

            // If there are multiple candidates, use overload resolution.
            if (candidates.Count > 1)
            {
                var argTypes = CollectArgumentTypes(invocation.Arguments, model);
                var result = OverloadResolver.Resolve(candidates, argTypes, model);

                switch (result.Status)
                {
                    case OverloadResolutionStatus.NoApplicableCandidate:
                        var argTypeNames = string.Join(", ", argTypes.Select(t => t.DisplayName));
                        diagnostics.Add(MakeDiagnostic(
                            "MIST_TC_0031",
                            $"No overload for '{methodName}' matches argument types ({argTypeNames})",
                            (SyntaxNode)invocation,
                            model));
                        return;

                    case OverloadResolutionStatus.AmbiguousCall:
                        var sigs = result.AmbiguousCandidates!
                            .Select(c => FormatCandidateSignature(c, methodName, model));
                        diagnostics.Add(MakeDiagnostic(
                            "MIST_TC_0032",
                            $"Call to '{methodName}' is ambiguous between: {string.Join(" and ", sigs)}",
                            (SyntaxNode)invocation,
                            model));
                        return;

                    case OverloadResolutionStatus.Success:
                        // Resolved successfully — proceed with the resolved symbol for further type checking.
                        CheckArgumentTypes(invocation.Arguments, result.BestCandidate!, model, diagnostics, invocation);
                        return;
                }
            }
            else
            {
                // Single candidate — preserve existing single-method diagnostics (MIST_TC_0002).
                CheckArgumentTypes(invocation.Arguments, invokedSymbol, model, diagnostics, invocation);
            }
        }
    }

    private void CheckArgumentListExpressions(ArgumentListNode argList, ISemanticModel model, DiagnosticBag diagnostics)
    {
        // The ArgumentListNode has an Expression property and additional expressions in Children.
        foreach (var child in argList.Children())
        {
            if (child is ExpressionNode expr)
            {
                CheckExpression(expr, model, diagnostics);
            }
        }
    }

    private static void CheckArgumentTypes(
        ArgumentListNode? argList,
        ISymbol methodSymbol,
        ISemanticModel model,
        DiagnosticBag diagnostics,
        SyntaxNode callSite)
    {
        // Get parameter symbols from the method's containing scope.
        // The semantic model builder registers parameters in the method's scope.
        SyntaxNode methodNode;
        try { methodNode = model.GetDeclaringNode(methodSymbol); }
        catch { return; }

        ImmutableArray<ISymbol> paramSymbols;
        if (methodNode is MethodDeclarationNode methodDecl && methodDecl.Parameters is not null)
        {
            var paramList = new List<ISymbol>();
            foreach (var child in methodDecl.Parameters.Children())
            {
                if (child is ParameterNode paramNode)
                {
                    var paramSym = model.GetDeclaredSymbol(paramNode);
                    if (paramSym is not null)
                        paramList.Add(paramSym);
                }
            }
            paramSymbols = [.. paramList];
        }
        else
        {
            return; // No parameters to check.
        }

        // Collect argument expressions.
        var argExprs = new List<ExpressionNode>();
        if (argList is not null)
        {
            foreach (var child in argList.Children())
            {
                if (child is ExpressionNode expr)
                    argExprs.Add(expr);
            }
        }

        // Check each argument against the corresponding parameter.
        var count = Math.Min(argExprs.Count, paramSymbols.Length);
        for (var i = 0; i < count; i++)
        {
            var argType = model.GetTypeOf(argExprs[i]);
            var paramType = paramSymbols[i].DeclaredType;

            if (argType is not null && paramType is not null)
            {
                if (!IsAssignable(argType, paramType))
                {
                    diagnostics.Add(MakeDiagnostic(
                        "MIST_TC_0002",
                        $"Argument type '{argType.DisplayName}' is not assignable to parameter type '{paramType.DisplayName}'",
                        argExprs[i],
                        model));
                }
            }
        }
    }

    /// <summary>
    /// Checks argument types against parameter types with type argument substitution.
    /// Applies enclosing type substitution (for ConstructedType member access) and
    /// method-level substitution (for generic method invocations).
    /// </summary>
    private static void CheckArgumentTypesWithSubstitution(
        ArgumentListNode? argList,
        ISymbol methodSymbol,
        ISemanticModel model,
        DiagnosticBag diagnostics,
        SyntaxNode callSite,
        IReadOnlyList<TypeParameterType>? enclosingTypeParams,
        IReadOnlyList<IType>? enclosingTypeArgs,
        IReadOnlyList<TypeParameterType>? methodTypeParams,
        IReadOnlyList<IType>? methodTypeArgs)
    {
        // Get parameter symbols from the method's containing scope.
        SyntaxNode methodNode;
        try { methodNode = model.GetDeclaringNode(methodSymbol); }
        catch { return; }

        ImmutableArray<ISymbol> paramSymbols;
        if (methodNode is MethodDeclarationNode methodDecl && methodDecl.Parameters is not null)
        {
            var paramList = new List<ISymbol>();
            foreach (var child in methodDecl.Parameters.Children())
            {
                if (child is ParameterNode paramNode)
                {
                    var paramSym = model.GetDeclaredSymbol(paramNode);
                    if (paramSym is not null)
                        paramList.Add(paramSym);
                }
            }
            paramSymbols = [.. paramList];
        }
        else
        {
            return; // No parameters to check.
        }

        // Collect argument expressions.
        var argExprs = new List<ExpressionNode>();
        if (argList is not null)
        {
            foreach (var child in argList.Children())
            {
                if (child is ExpressionNode expr)
                    argExprs.Add(expr);
            }
        }

        // Check each argument against the corresponding parameter, applying substitution.
        var count = Math.Min(argExprs.Count, paramSymbols.Length);
        for (var i = 0; i < count; i++)
        {
            var argType = model.GetTypeOf(argExprs[i]);
            var paramType = paramSymbols[i].DeclaredType;

            if (argType is not null && paramType is not null)
            {
                // Apply enclosing type substitution (e.g., T → int for Box<int>).
                if (enclosingTypeParams is not null && enclosingTypeArgs is not null)
                    paramType = SubstituteType(paramType, enclosingTypeParams, enclosingTypeArgs);

                // Apply method-level substitution (e.g., U → string for Method<string>).
                if (methodTypeParams is not null && methodTypeArgs is not null)
                    paramType = SubstituteType(paramType, methodTypeParams, methodTypeArgs);

                if (!IsAssignable(argType, paramType))
                {
                    diagnostics.Add(MakeDiagnostic(
                        "MIST_TC_0002",
                        $"Argument type '{argType.DisplayName}' is not assignable to parameter type '{paramType.DisplayName}'",
                        argExprs[i],
                        model));
                }
            }
        }
    }

    /// <summary>
    /// Gets the type parameters from a type definition (ClassType, StructType, or InterfaceType).
    /// </summary>
    private static IReadOnlyList<TypeParameterType>? GetTypeParameters(IType type)
    {
        return type switch
        {
            ClassType ct => ct.TypeParameters.Count > 0 ? ct.TypeParameters : null,
            StructType st => st.TypeParameters.Count > 0 ? st.TypeParameters : null,
            InterfaceType it => it.TypeParameters.Count > 0 ? it.TypeParameters : null,
            _ => null,
        };
    }

    /// <summary>
    /// Resolves type arguments from a <see cref="TypeArgumentListNode"/> AST node
    /// by resolving each type reference to its corresponding <see cref="IType"/>.
    /// </summary>
    private static IReadOnlyList<IType> ResolveTypeArgumentsFromAst(TypeArgumentListNode typeArgList, ISemanticModel model)
    {
        var result = new List<IType>();

        // The TypeArgumentListNode has a TypeReference property (first arg) and TypeReferences array (rest).
        var firstType = ResolveTypeReferenceToType(typeArgList.TypeReference, model);
        result.Add(firstType);

        foreach (var typeRef in typeArgList.TypeReferences)
        {
            var argType = ResolveTypeReferenceToType(typeRef, model);
            result.Add(argType);
        }

        return result;
    }

    /// <summary>
    /// Resolves a <see cref="TypeReferenceNode"/> to its corresponding <see cref="IType"/>
    /// by looking up the type name in the semantic model's symbol table.
    /// </summary>
    private static IType ResolveTypeReferenceToType(TypeReferenceNode typeRef, ISemanticModel model)
    {
        // Try the expression type map first (may be populated for some nodes).
        var resolved = model.GetTypeOf(typeRef);
        if (resolved is not null && resolved is not UnknownType)
            return resolved;

        switch (typeRef)
        {
            case PrimitiveTypeRefTypeReferenceNode primitiveRef:
            {
                var primType = ResolvePrimitiveType(primitiveRef.PrimitiveType);
                if (primitiveRef.ArrayRank.HasValue && primType is not null)
                    return new ArrayType(primType);
                return primType ?? UnknownType.Instance;
            }
            case NamedTypeRefTypeReferenceNode namedRef:
            {
                var name = namedRef.Name.Children().FirstOrDefault()?.Span;
                // Try to find the type by looking up the symbol.
                var symbol = model.GetDeclaredSymbol(namedRef);
                if (symbol?.DeclaredType is not null)
                    return symbol.DeclaredType;

                // Fallback: search all symbols for a matching type name.
                var typeName = ExtractTypeName(namedRef);
                foreach (var sym in model.AllSymbols)
                {
                    if (sym.Name == typeName && sym.Kind is SymbolKind.Class or SymbolKind.Struct or SymbolKind.Interface)
                        return sym.DeclaredType ?? UnknownType.Instance;
                }
                return UnknownType.Instance;
            }
            case VoidTypeRefTypeReferenceNode:
                return VoidType.Instance;
            default:
                return UnknownType.Instance;
        }
    }

    /// <summary>
    /// Resolves a primitive type AST node to its corresponding <see cref="IType"/>.
    /// </summary>
    private static IType? ResolvePrimitiveType(PrimitiveTypeNode primitiveType)
    {
        return primitiveType switch
        {
            IntTypePrimitiveTypeNode => PrimitiveType.Int,
            LongTypePrimitiveTypeNode => PrimitiveType.Long,
            ShortTypePrimitiveTypeNode => PrimitiveType.Short,
            ByteTypePrimitiveTypeNode => PrimitiveType.Byte,
            FloatTypePrimitiveTypeNode => PrimitiveType.Float,
            DoubleTypePrimitiveTypeNode => PrimitiveType.Double,
            BoolTypePrimitiveTypeNode => PrimitiveType.Bool,
            CharTypePrimitiveTypeNode => PrimitiveType.Char,
            StringTypePrimitiveTypeNode => StringType.Instance,
            DecimalTypePrimitiveTypeNode => PrimitiveType.Decimal,
            UintTypePrimitiveTypeNode => PrimitiveType.Uint,
            UlongTypePrimitiveTypeNode => PrimitiveType.Ulong,
            UshortTypePrimitiveTypeNode => PrimitiveType.Ushort,
            SbyteTypePrimitiveTypeNode => PrimitiveType.Sbyte,
            _ => null,
        };
    }

    /// <summary>
    /// Extracts the type name from a <see cref="NamedTypeRefTypeReferenceNode"/>.
    /// </summary>
    private static string ExtractTypeName(NamedTypeRefTypeReferenceNode namedRef)
    {
        return namedRef.Name.IDENTIFIER.Lexeme;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Array operation checking (MIST_TC_0010, MIST_TC_0012)
    // ═══════════════════════════════════════════════════════════════════════

    private void CheckArrayCreation(
        ArrayCreationExpressionPrimaryNode arrayCreation,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Check the size expression.
        CheckExpression(arrayCreation.Size, model, diagnostics);

        var sizeType = model.GetTypeOf(arrayCreation.Size);
        if (sizeType is not null && !IsUnknown(sizeType))
        {
            if (sizeType != PrimitiveType.Int)
            {
                diagnostics.Add(MakeDiagnostic(
                    "MIST_TC_0012",
                    $"Array size must be of type 'int', but found '{sizeType.DisplayName}'",
                    arrayCreation.Size,
                    model));
            }
        }
    }

    private void CheckIndexSuffix(
        IndexSuffixPostfixSuffixNode indexSuffix,
        IType? targetType,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Check the index expression.
        CheckExpression(indexSuffix.Index, model, diagnostics);

        var indexType = model.GetTypeOf(indexSuffix.Index);
        if (indexType is not null && !IsUnknown(indexType))
        {
            if (indexType != PrimitiveType.Int)
            {
                diagnostics.Add(MakeDiagnostic(
                    "MIST_TC_0010",
                    $"Array index must be of type 'int', but found '{indexType.DisplayName}'",
                    indexSuffix.Index,
                    model));
            }
        }

        // Verify the target is an array type (the target type should be ArrayType).
        // If targetType is not an array, the semantic model builder would have
        // already flagged it or assigned UnknownType. We don't emit a separate
        // diagnostic here to avoid cascading.
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Object creation checking (MIST_TC_0011)
    // ═══════════════════════════════════════════════════════════════════════

    private void CheckObjectCreation(
        ObjectCreationExpressionPrimaryNode objectCreation,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Check argument expressions.
        if (objectCreation.Arguments is not null)
        {
            CheckArgumentListExpressions(objectCreation.Arguments, model, diagnostics);
        }

        // Get the type being constructed.
        var createdType = model.GetTypeOf(objectCreation);
        if (createdType is null || IsUnknown(createdType))
            return;

        // Find constructors on the type.
        IScope? memberScope = createdType switch
        {
            ClassType ct => ct.MemberScope,
            StructType st => st.MemberScope,
            ConstructedType cst => cst.MemberScope,
            _ => null,
        };
        // Collect all constructors from the target type's member scope.
        var constructors = CollectConstructorOverloads(createdType);

        // Collect argument types.
        var argTypes = CollectArgumentTypes(objectCreation.Arguments, model);

        if (constructors.Count == 0)
        {
            // If no constructors are defined and there are no arguments, it's OK
            // (default constructor). Otherwise, emit diagnostic.
            if (argTypes.Count > 0)
            {
                diagnostics.Add(MakeDiagnostic(
                    "MIST_TC_0011",
                    $"Type '{createdType.DisplayName}' does not contain a constructor matching the given arguments",
                    objectCreation,
                    model));
            }
            return;
        }

        // When multiple constructors exist, use overload resolution.
        if (constructors.Count > 1)
        {
            var result = OverloadResolver.Resolve(constructors, argTypes, model);
            var typeName = createdType.DisplayName;

            switch (result.Status)
            {
                case OverloadResolutionStatus.NoApplicableCandidate:
                    var argTypeNames = string.Join(", ", argTypes.Select(t => t.DisplayName));
                    diagnostics.Add(MakeDiagnostic(
                        "MIST_TC_0031",
                        $"No overload for '{typeName}' constructor matches argument types ({argTypeNames})",
                        objectCreation,
                        model));
                    return;

                case OverloadResolutionStatus.AmbiguousCall:
                    var sigs = result.AmbiguousCandidates!
                        .Select(c => FormatCandidateSignature(c, typeName, model));
                    diagnostics.Add(MakeDiagnostic(
                        "MIST_TC_0032",
                        $"Call to '{typeName}' constructor is ambiguous between: {string.Join(" and ", sigs)}",
                        objectCreation,
                        model));
                    return;

                case OverloadResolutionStatus.Success:
                    // Resolved successfully — no diagnostic needed.
                    return;
            }
        }
        else
        {
            // Single constructor — preserve existing behavior with MIST_TC_0011.
            var hasMatch = ConstructorMatches(constructors[0], argTypes, createdType, model);
            if (!hasMatch)
            {
                diagnostics.Add(MakeDiagnostic(
                    "MIST_TC_0011",
                    $"Type '{createdType.DisplayName}' does not contain a constructor matching the given arguments",
                    objectCreation,
                    model));
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Constructor chain checking (MIST_TC_0013)
    // ═══════════════════════════════════════════════════════════════════════

    private void CheckConstructorChain(
        ConstructorChainNode chain,
        ISymbol ctorSymbol,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Extract arguments from the appropriate chain variant.
        var arguments = chain switch
        {
            ThisChainConstructorChainNode thisChain => thisChain.Arguments,
            BaseChainConstructorChainNode baseChain => baseChain.Arguments,
            _ => null,
        };

        // Check argument expressions.
        if (arguments is not null)
        {
            CheckArgumentListExpressions(arguments, model, diagnostics);
        }

        // Find the enclosing type.
        var enclosingType = FindDeclaringType(ctorSymbol);
        if (enclosingType is null || IsUnknown(enclosingType))
            return;

        // For `: base(...)` chains, resolve the base type and search its constructors.
        // For `: this(...)` chains, search the declaring type's own constructors.
        IType targetType = enclosingType;
        if (chain is BaseChainConstructorChainNode && enclosingType is ClassType ct && ct.BaseType is not null)
        {
            targetType = ct.BaseType;
        }

        IScope? memberScope = targetType switch
        {
            ClassType classType => classType.MemberScope,
            StructType st => st.MemberScope,
            _ => null,
        };

        if (memberScope is null)
            return;

        var argTypes = CollectArgumentTypes(arguments, model);

        // For `: this(...)`, exclude the current constructor from the search.
        // For `: base(...)`, search all constructors on the base type.
        var candidateConstructors = chain is BaseChainConstructorChainNode
            ? memberScope.Members.Where(m => m.Kind == SymbolKind.Constructor).ToList()
            : memberScope.Members.Where(m => m.Kind == SymbolKind.Constructor && !ReferenceEquals(m, ctorSymbol)).ToList();

        if (candidateConstructors.Count == 0)
        {
            // No constructors available — only an error if arguments were provided.
            if (argTypes.Count > 0)
            {
                diagnostics.Add(MakeDiagnostic(
                    "MIST_TC_0013",
                    $"No constructor on '{targetType.DisplayName}' matches the chained arguments",
                    chain,
                    model));
            }
            return;
        }

        // When multiple constructors exist, use overload resolution.
        if (candidateConstructors.Count > 1)
        {
            var result = OverloadResolver.Resolve(candidateConstructors, argTypes, model);
            var typeName = targetType.DisplayName;

            switch (result.Status)
            {
                case OverloadResolutionStatus.NoApplicableCandidate:
                    var argTypeNames = string.Join(", ", argTypes.Select(t => t.DisplayName));
                    diagnostics.Add(MakeDiagnostic(
                        "MIST_TC_0031",
                        $"No overload for '{typeName}' constructor matches argument types ({argTypeNames})",
                        chain,
                        model));
                    return;

                case OverloadResolutionStatus.AmbiguousCall:
                    var sigs = result.AmbiguousCandidates!
                        .Select(c => FormatCandidateSignature(c, typeName, model));
                    diagnostics.Add(MakeDiagnostic(
                        "MIST_TC_0032",
                        $"Call to '{typeName}' constructor is ambiguous between: {string.Join(" and ", sigs)}",
                        chain,
                        model));
                    return;

                case OverloadResolutionStatus.Success:
                    // Resolved successfully — no diagnostic needed.
                    return;
            }
        }
        else
        {
            // Single constructor — preserve existing behavior with MIST_TC_0013.
            var hasMatch = ConstructorMatches(candidateConstructors[0], argTypes, targetType, model);
            if (!hasMatch)
            {
                diagnostics.Add(MakeDiagnostic(
                    "MIST_TC_0013",
                    $"No constructor on '{targetType.DisplayName}' matches the chained arguments",
                    chain,
                    model));
            }
        }
    }

    private static bool ConstructorMatches(ISymbol ctor, List<IType> argTypes, IType? createdType, ISemanticModel model)
    {
        // Get the constructor's parameters.
        SyntaxNode ctorNode;
        try { ctorNode = model.GetDeclaringNode(ctor); }
        catch { return false; }

        if (ctorNode is not ConstructorDeclarationNode ctorDecl)
            return false;

        var paramTypes = new List<IType>();
        if (ctorDecl.Parameters is not null)
        {
            foreach (var child in ctorDecl.Parameters.Children())
            {
                if (child is ParameterNode paramNode)
                {
                    var paramSym = model.GetDeclaredSymbol(paramNode);
                    if (paramSym?.DeclaredType is not null)
                        paramTypes.Add(paramSym.DeclaredType);
                }
            }
        }

        if (argTypes.Count != paramTypes.Count)
            return false;

        // When the created type is a ConstructedType (e.g., GenericBox<int>),
        // substitute type parameters in constructor parameter types with the
        // actual type arguments before comparing.
        IReadOnlyList<TypeParameterType>? typeParams = null;
        IReadOnlyList<IType>? typeArgs = null;
        if (createdType is ConstructedType ct)
        {
            typeParams = GetTypeParameters(ct.GenericDefinition);
            typeArgs = ct.TypeArguments;
        }

        for (var i = 0; i < argTypes.Count; i++)
        {
            var paramType = paramTypes[i];
            if (typeParams is not null && typeArgs is not null)
            {
                paramType = SubstituteType(paramType, typeParams, typeArgs);
            }
            if (!IsAssignable(argTypes[i], paramType))
                return false;
        }

        return true;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Access modifier enforcement (MIST_TC_0014, MIST_TC_0015, MIST_TC_0016)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Determines whether the target of a postfix expression is a type name
    /// (e.g., <c>Console</c> in <c>Console.WriteLine()</c>) rather than an instance.
    /// </summary>
    private static bool IsTargetTypeName(PrimaryNode target, ISemanticModel model)
    {
        if (target is not IdentifierExpressionPrimaryNode identifier)
            return false;

        var name = identifier.Name.Lexeme;

        // Check if any symbol with this name is a variable, parameter, or field.
        // If so, the identifier refers to an instance, not a type name.
        foreach (var symbol in model.AllSymbols)
        {
            if (symbol.Name == name)
            {
                if (symbol.Kind is SymbolKind.LocalVariable or SymbolKind.Parameter or SymbolKind.Field)
                {
                    return false;
                }
                if (symbol.Kind is SymbolKind.Class or SymbolKind.Struct)
                {
                    return true;
                }
            }
        }

        // Fallback: check the expression type for ambient types (e.g. Console)
        // that may not have a symbol in the user-defined symbol table.
        var resolvedType = model.GetTypeOf(target);
        return resolvedType is ClassType or StructType;
    }

    /// <summary>
    /// Checks access modifier rules for a member access or method call.
    /// Emits MIST_TC_0014 (private access), MIST_TC_0015 (static via instance),
    /// or MIST_TC_0016 (instance via type name).
    /// </summary>
    private void CheckMemberAccessModifiers(
        string memberName,
        IType? targetType,
        bool targetIsTypeName,
        SyntaxNode accessNode,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        if (targetType is null || IsUnknown(targetType))
            return;

        // Find the member symbol on the target type.
        var memberSymbol = FindMemberSymbol(targetType, memberName, model);
        if (memberSymbol is null)
            return;

        // Backend symbols are assumed public and valid — skip access modifier checks
        // for them since they have no AST declaring node with modifiers.
        // The type checker treats all symbols uniformly for type checking, but
        // access modifier enforcement only applies to user-defined members.
        if (memberSymbol.Origin == SymbolOrigin.Backend)
            return;

        // Get the declaring AST node to inspect modifiers.
        SyntaxNode? declaringNode;
        try { declaringNode = model.GetDeclaringNode(memberSymbol); }
        catch { return; }

        // Extract modifier information from the declaring node.
        var modifiers = GetMemberModifiers(declaringNode);

        // R9.1, R9.3: Private member access check.
        // Members with no modifier default to private (R9.3).
        if (!modifiers.IsPublic)
        {
            // Private members are only accessible within the declaring type.
            var declaringType = GetDeclaringTypeOfMember(memberSymbol);
            if (declaringType is not null && !IsSameType(declaringType, _currentEnclosingType))
            {
                diagnostics.Add(MakeDiagnostic(
                    "MIST_TC_0014",
                    $"'{memberName}' is inaccessible due to its protection level",
                    accessNode,
                    model));
                return; // Don't emit additional diagnostics for the same access.
            }
        }

        // R9.4: Static members must be accessed via type name, not instance.
        if (modifiers.IsStatic && !targetIsTypeName)
        {
            diagnostics.Add(MakeDiagnostic(
                "MIST_TC_0015",
                $"Static member '{memberName}' cannot be accessed via an instance",
                accessNode,
                model));
            return;
        }

        // R9.5: Instance members must be accessed via instance, not type name.
        if (!modifiers.IsStatic && targetIsTypeName)
        {
            diagnostics.Add(MakeDiagnostic(
                "MIST_TC_0016",
                $"Instance member '{memberName}' cannot be accessed via a type name",
                accessNode,
                model));
        }

        // ── Property-specific access checks ──────────────────────────────

        if (memberSymbol.Kind == SymbolKind.Property)
        {
            var propInfo = GetPropertyMetadata(memberSymbol);
            if (propInfo is not null)
            {
                // MIST_TC_0027: Static property accessed through instance reference.
                if (modifiers.IsStatic && !targetIsTypeName)
                {
                    diagnostics.Add(MakeDiagnostic(
                        "MIST_TC_0027",
                        $"Static property '{memberName}' cannot be accessed through an instance reference",
                        accessNode,
                        model));
                }
            }
        }
    }

    /// <summary>
    /// Finds a member symbol by name on the given type.
    /// For <see cref="ConstructedType"/>, looks up in the generic definition's member scope.
    /// </summary>
    private static ISymbol? FindMemberSymbol(IType targetType, string memberName, ISemanticModel model)
    {
        // Unwrap ConstructedType to look up in the generic definition's member scope.
        if (targetType is ConstructedType ct)
            targetType = ct.GenericDefinition;

        IScope? memberScope = targetType switch
        {
            ClassType ct2 => ct2.MemberScope,
            StructType st => st.MemberScope,
            InterfaceType it => it.MemberScope,
            _ => null,
        };

        if (memberScope is null)
            return null;

        foreach (var member in memberScope.Members)
        {
            if (member.Name == memberName)
                return member;
        }

        return null;
    }

    /// <summary>
    /// Gets the type that declares a given member symbol by finding the type
    /// whose MemberScope matches the member's ContainingScope.
    /// </summary>
    private static IType? GetDeclaringTypeOfMember(ISymbol memberSymbol)
    {
        var typeScope = memberSymbol.ContainingScope;
        var parentScope = typeScope?.Parent;
        if (parentScope is null)
            return null;

        foreach (var member in parentScope.Members)
        {
            if (member.Kind is SymbolKind.Class or SymbolKind.Struct
                && member.DeclaredType is ClassType or StructType)
            {
                var scope = member.DeclaredType switch
                {
                    ClassType ct => ct.MemberScope,
                    StructType st => st.MemberScope,
                    _ => null,
                };
                if (ReferenceEquals(scope, typeScope))
                    return member.DeclaredType;
            }
        }

        return null;
    }

    /// <summary>
    /// Checks if two types refer to the same type (by reference or display name).
    /// </summary>
    private static bool IsSameType(IType? a, IType? b)
    {
        if (a is null || b is null)
            return false;
        if (ReferenceEquals(a, b))
            return true;
        // Fallback: compare by display name for types that may not be reference-equal.
        return a.DisplayName == b.DisplayName;
    }

    /// <summary>
    /// Modifier flags extracted from a member's AST declaring node.
    /// </summary>
    private readonly record struct MemberModifiers(bool IsPublic, bool IsPrivate, bool IsStatic)
    {
        /// <summary>
        /// True if the member has no explicit access modifier (defaults to private per R9.3).
        /// </summary>
        public bool HasNoAccessModifier => !IsPublic && !IsPrivate;
    }

    /// <summary>
    /// Extracts modifier information from a member's AST declaring node.
    /// Returns default (all false) for backend symbols or nodes without modifiers.
    /// </summary>
    private static MemberModifiers GetMemberModifiers(SyntaxNode? declaringNode)
    {
        IReadOnlyList<ModifierNode>? modifiers = declaringNode switch
        {
            FieldDeclarationNode field => field.Modifiers,
            MethodDeclarationNode method => method.Modifiers,
            ConstructorDeclarationNode ctor => ctor.Modifiers,
            BlockPropertyPropertyDeclarationNode blockProp => blockProp.Modifiers,
            ExpressionBodiedPropertyPropertyDeclarationNode exprProp => exprProp.Modifiers,
            BlockIndexerIndexerDeclarationNode blockIndexer => blockIndexer.Modifiers,
            _ => null,
        };

        if (modifiers is null || modifiers.Count == 0)
        {
            // No modifiers → defaults to private (R9.3), not static.
            return new MemberModifiers(IsPublic: false, IsPrivate: false, IsStatic: false);
        }

        var isPublic = false;
        var isPrivate = false;
        var isStatic = false;

        foreach (var mod in modifiers)
        {
            switch (mod)
            {
                case PublicModifierModifierNode:
                    isPublic = true;
                    break;
                case PrivateModifierModifierNode:
                    isPrivate = true;
                    break;
                case StaticModifierModifierNode:
                    isStatic = true;
                    break;
            }
        }

        return new MemberModifiers(isPublic, isPrivate, isStatic);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Assignability and type compatibility helpers
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Checks if <paramref name="source"/> is assignable to <paramref name="target"/>.
    /// Emits MIST_TC_0001 if not.
    /// </summary>
    private static void CheckAssignability(IType source, IType target, SyntaxNode node, DiagnosticBag diagnostics, ISemanticModel model)
    {
        if (!IsAssignable(source, target))
        {
            diagnostics.Add(MakeDiagnostic(
                "MIST_TC_0001",
                $"Cannot assign '{source.DisplayName}' to '{target.DisplayName}'",
                node,
                model));
        }
    }

    /// <summary>
    /// Returns true if <paramref name="source"/> is assignable to <paramref name="target"/>.
    /// Rules:
    /// - Same type (identity)
    /// - Both numeric with implicit widening conversion
    /// - Either is UnknownType (error suppression)
    /// - Subtype assignability: derived class assignable to any ancestor class
    /// </summary>
    internal static bool IsAssignable(IType source, IType target)
    {
        // UnknownType suppresses cascading errors.
        if (IsUnknown(source) || IsUnknown(target))
            return true;

        // Identity.
        if (ReferenceEquals(source, target))
            return true;

        // Same display name (for types that may not be reference-equal).
        if (source.DisplayName == target.DisplayName)
            return true;

        // ConstructedType identity: same generic definition + same type arguments.
        if (source is ConstructedType sourceConstructed && target is ConstructedType targetConstructed)
        {
            if (ReferenceEquals(sourceConstructed.GenericDefinition, targetConstructed.GenericDefinition)
                && sourceConstructed.TypeArguments.Count == targetConstructed.TypeArguments.Count)
            {
                var allMatch = true;
                for (var i = 0; i < sourceConstructed.TypeArguments.Count; i++)
                {
                    if (!TypesEqual(sourceConstructed.TypeArguments[i], targetConstructed.TypeArguments[i]))
                    {
                        allMatch = false;
                        break;
                    }
                }
                if (allMatch) return true;
            }
        }

        // ConstructedType inheritance: walk the base type chain with substitution.
        if (source is ConstructedType sourceConst && target is ConstructedType)
        {
            var baseType = GetSubstitutedBaseType(sourceConst);
            if (baseType is not null && IsAssignable(baseType, target))
                return true;
        }

        // ConstructedType to constructed interface assignability.
        if (target is ConstructedType targetConst && targetConst.GenericDefinition is InterfaceType)
        {
            if (ImplementsConstructedInterface(source, targetConst))
                return true;
        }

        // Numeric widening.
        if (IsNumeric(source) && IsNumeric(target))
            return HasImplicitNumericConversion(source, target);

        // Interface assignability:
        // A class/struct implementing an interface (directly or transitively) is assignable to that interface.
        // An interface is assignable to any interface it inherits from (directly or transitively).
        if (target is InterfaceType targetInterface)
        {
            if (ImplementsInterface(source, targetInterface))
                return true;
        }

        // Subtype assignability — walk the inheritance chain.
        // A derived class is assignable to any of its ancestor classes.
        if (source is ClassType sourceClass && target is ClassType)
        {
            var current = sourceClass.BaseType as ClassType;
            while (current is not null)
            {
                if (ReferenceEquals(current, target) || current.DisplayName == target.DisplayName)
                    return true;
                current = current.BaseType as ClassType;
            }
        }

        return false;
    }

    /// <summary>
    /// Compares two types for structural equality.
    /// For <see cref="ConstructedType"/>, checks that the generic definitions are the same
    /// and all type arguments are recursively equal.
    /// For other types, uses reference equality or display name comparison.
    /// </summary>
    private static bool TypesEqual(IType a, IType b)
    {
        if (ReferenceEquals(a, b))
            return true;

        if (a is ConstructedType aCt && b is ConstructedType bCt)
        {
            if (!ReferenceEquals(aCt.GenericDefinition, bCt.GenericDefinition))
                return false;
            if (aCt.TypeArguments.Count != bCt.TypeArguments.Count)
                return false;
            for (var i = 0; i < aCt.TypeArguments.Count; i++)
            {
                if (!TypesEqual(aCt.TypeArguments[i], bCt.TypeArguments[i]))
                    return false;
            }
            return true;
        }

        if (a is TypeParameterType aTp && b is TypeParameterType bTp)
            return aTp.Name == bTp.Name && aTp.Ordinal == bTp.Ordinal;

        return a.DisplayName == b.DisplayName;
    }

    /// <summary>
    /// Gets the substituted base type of a <see cref="ConstructedType"/>.
    /// If the generic definition is a <see cref="ClassType"/> with a base type,
    /// substitutes the type parameters in the base type with the constructed type's type arguments.
    /// </summary>
    private static IType? GetSubstitutedBaseType(ConstructedType constructedType)
    {
        if (constructedType.GenericDefinition is not ClassType classDef)
            return null;

        var baseType = classDef.BaseType;
        if (baseType is null)
            return null;

        var typeParams = classDef.TypeParameters;
        var typeArgs = constructedType.TypeArguments;

        return SubstituteType(baseType, typeParams, typeArgs);
    }

    /// <summary>
    /// Checks whether <paramref name="source"/> implements the target constructed interface,
    /// including transitive interface inheritance.
    /// </summary>
    private static bool ImplementsConstructedInterface(IType source, ConstructedType targetConstructedInterface)
    {
        // Collect all interfaces from the source type.
        IReadOnlyList<IType>? interfaces = null;
        IReadOnlyList<TypeParameterType>? typeParams = null;
        IReadOnlyList<IType>? typeArgs = null;

        switch (source)
        {
            case ConstructedType sourceCt:
            {
                switch (sourceCt.GenericDefinition)
                {
                    case ClassType classDef:
                        interfaces = classDef.Interfaces;
                        typeParams = classDef.TypeParameters;
                        typeArgs = sourceCt.TypeArguments;
                        // Also check base type chain.
                        var baseType = GetSubstitutedBaseType(sourceCt);
                        if (baseType is not null && ImplementsConstructedInterface(baseType, targetConstructedInterface))
                            return true;
                        break;
                    case StructType structDef:
                        interfaces = structDef.Interfaces;
                        typeParams = structDef.TypeParameters;
                        typeArgs = sourceCt.TypeArguments;
                        break;
                    case InterfaceType ifaceDef:
                        // Check if the source interface itself matches the target.
                        if (IsAssignable(source, targetConstructedInterface))
                            return true;
                        interfaces = ifaceDef.Interfaces;
                        typeParams = ifaceDef.TypeParameters;
                        typeArgs = sourceCt.TypeArguments;
                        break;
                }
                break;
            }
            case ClassType classType:
                interfaces = classType.Interfaces;
                // Also check base type chain.
                var current = classType.BaseType;
                while (current is not null)
                {
                    if (current is ConstructedType baseCt && IsAssignable(baseCt, targetConstructedInterface))
                        return true;
                    if (current is ClassType baseClass)
                    {
                        foreach (var iface in baseClass.Interfaces)
                        {
                            var substituted = iface;
                            if (substituted is ConstructedType ifaceCt && IsAssignable(ifaceCt, targetConstructedInterface))
                                return true;
                        }
                        current = baseClass.BaseType;
                    }
                    else
                        break;
                }
                break;
            case StructType structType:
                interfaces = structType.Interfaces;
                break;
            case InterfaceType ifaceType:
                interfaces = ifaceType.Interfaces;
                break;
        }

        if (interfaces is null)
            return false;

        foreach (var iface in interfaces)
        {
            // Substitute type parameters if we have a mapping.
            var substitutedIface = (typeParams is not null && typeArgs is not null)
                ? SubstituteType(iface, typeParams, typeArgs)
                : iface;

            if (substitutedIface is ConstructedType substitutedCt)
            {
                if (IsAssignable(substitutedCt, targetConstructedInterface))
                    return true;
                // Transitively check the interface's own inherited interfaces.
                if (ImplementsConstructedInterface(substitutedCt, targetConstructedInterface))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Substitutes type parameters with corresponding type arguments in a type.
    /// - If <paramref name="type"/> is a <see cref="TypeParameterType"/> matching one of the <paramref name="typeParams"/>,
    ///   returns the corresponding entry from <paramref name="typeArgs"/>.
    /// - If <paramref name="type"/> is a <see cref="ConstructedType"/>, recursively substitutes its type arguments.
    /// - Otherwise, returns the type unchanged.
    /// </summary>
    private static IType SubstituteType(IType type, IReadOnlyList<TypeParameterType> typeParams, IReadOnlyList<IType> typeArgs)
    {
        if (type is TypeParameterType tpt)
        {
            for (var i = 0; i < typeParams.Count; i++)
            {
                if (tpt.Name == typeParams[i].Name && tpt.Ordinal == typeParams[i].Ordinal)
                    return i < typeArgs.Count ? typeArgs[i] : type;
            }
            return type;
        }

        if (type is ConstructedType ct)
        {
            var substitutedArgs = new IType[ct.TypeArguments.Count];
            var changed = false;
            for (var i = 0; i < ct.TypeArguments.Count; i++)
            {
                substitutedArgs[i] = SubstituteType(ct.TypeArguments[i], typeParams, typeArgs);
                if (!ReferenceEquals(substitutedArgs[i], ct.TypeArguments[i]))
                    changed = true;
            }
            return changed ? new ConstructedType(ct.GenericDefinition, substitutedArgs) : ct;
        }

        return type;
    }

    /// <summary>
    /// Checks whether <paramref name="source"/> implements <paramref name="targetInterface"/>
    /// directly or transitively.
    /// </summary>
    private static bool ImplementsInterface(IType source, InterfaceType targetInterface)
    {
        switch (source)
        {
            case ClassType classType:
                // Check the class's own interfaces (recursively through interface inheritance).
                if (InterfaceListContains(classType.Interfaces, targetInterface))
                    return true;
                // Walk the base type chain and check each ancestor's interfaces.
                var current = classType.BaseType as ClassType;
                while (current is not null)
                {
                    if (InterfaceListContains(current.Interfaces, targetInterface))
                        return true;
                    current = current.BaseType as ClassType;
                }
                return false;

            case StructType structType:
                // Check the struct's interfaces (recursively through interface inheritance).
                return InterfaceListContains(structType.Interfaces, targetInterface);

            case InterfaceType sourceInterface:
                // An interface is assignable to itself or any interface it inherits from.
                if (ReferenceEquals(sourceInterface, targetInterface)
                    || sourceInterface.DisplayName == targetInterface.DisplayName)
                    return true;
                return InterfaceListContains(sourceInterface.Interfaces, targetInterface);

            default:
                return false;
        }
    }

    /// <summary>
    /// Recursively checks whether <paramref name="target"/> is present in the given
    /// interface list or in any of the interfaces' own inherited interface lists.
    /// Uses a visited set to handle diamond inheritance and prevent infinite loops.
    /// </summary>
    private static bool InterfaceListContains(
        IReadOnlyList<IType> interfaces,
        InterfaceType target,
        HashSet<InterfaceType>? visited = null)
    {
        foreach (var ifaceType in interfaces)
        {
            if (ifaceType is not InterfaceType iface)
                continue;

            if (ReferenceEquals(iface, target) || iface.DisplayName == target.DisplayName)
                return true;

            // Recursively check inherited interfaces, guarding against cycles.
            visited ??= new HashSet<InterfaceType>(ReferenceEqualityComparer.Instance);
            if (visited.Add(iface))
            {
                if (InterfaceListContains(iface.Interfaces, target, visited))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if two types are compatible for equality operators (==, !=).
    /// Compatible means: same type, both numeric, both bool, both string,
    /// or both reference types.
    /// </summary>
    private static bool AreEqualityCompatible(IType left, IType right)
    {
        if (IsUnknown(left) || IsUnknown(right))
            return true;

        if (ReferenceEquals(left, right))
            return true;

        if (left.DisplayName == right.DisplayName)
            return true;

        // Both numeric.
        if (IsNumeric(left) && IsNumeric(right))
            return true;

        // Both reference types (class types) can be compared.
        if (left is ClassType or StringType or ArrayType
            && right is ClassType or StringType or ArrayType)
            return true;

        return false;
    }

    private static bool IsNumeric(IType type)
    {
        return NumericTypes.Contains(type);
    }

    private static bool IsUnknown(IType type)
    {
        return type is UnknownType;
    }

    /// <summary>
    /// Checks for implicit numeric widening conversions following C# rules.
    /// </summary>
    private static bool HasImplicitNumericConversion(IType source, IType target)
    {
        // Simplified C# implicit numeric conversion table.
        // byte → short, ushort, int, uint, long, ulong, float, double, decimal
        // sbyte → short, int, long, float, double, decimal
        // short → int, long, float, double, decimal
        // ushort → int, uint, long, ulong, float, double, decimal
        // int → long, float, double, decimal
        // uint → long, ulong, float, double, decimal
        // long → float, double, decimal
        // ulong → float, double, decimal
        // float → double
        // char → ushort, int, uint, long, ulong, float, double, decimal

        if (source == PrimitiveType.Byte)
            return target == PrimitiveType.Short || target == PrimitiveType.Ushort
                || target == PrimitiveType.Int || target == PrimitiveType.Uint
                || target == PrimitiveType.Long || target == PrimitiveType.Ulong
                || target == PrimitiveType.Float || target == PrimitiveType.Double
                || target == PrimitiveType.Decimal;

        if (source == PrimitiveType.Sbyte)
            return target == PrimitiveType.Short
                || target == PrimitiveType.Int
                || target == PrimitiveType.Long
                || target == PrimitiveType.Float || target == PrimitiveType.Double
                || target == PrimitiveType.Decimal;

        if (source == PrimitiveType.Short)
            return target == PrimitiveType.Int
                || target == PrimitiveType.Long
                || target == PrimitiveType.Float || target == PrimitiveType.Double
                || target == PrimitiveType.Decimal;

        if (source == PrimitiveType.Ushort)
            return target == PrimitiveType.Int || target == PrimitiveType.Uint
                || target == PrimitiveType.Long || target == PrimitiveType.Ulong
                || target == PrimitiveType.Float || target == PrimitiveType.Double
                || target == PrimitiveType.Decimal;

        if (source == PrimitiveType.Int)
            return target == PrimitiveType.Long
                || target == PrimitiveType.Float || target == PrimitiveType.Double
                || target == PrimitiveType.Decimal;

        if (source == PrimitiveType.Uint)
            return target == PrimitiveType.Long || target == PrimitiveType.Ulong
                || target == PrimitiveType.Float || target == PrimitiveType.Double
                || target == PrimitiveType.Decimal;

        if (source == PrimitiveType.Long)
            return target == PrimitiveType.Float || target == PrimitiveType.Double
                || target == PrimitiveType.Decimal;

        if (source == PrimitiveType.Ulong)
            return target == PrimitiveType.Float || target == PrimitiveType.Double
                || target == PrimitiveType.Decimal;

        if (source == PrimitiveType.Float)
            return target == PrimitiveType.Double;

        // char → numeric widening
        if (source == PrimitiveType.Char)
            return target == PrimitiveType.Ushort || target == PrimitiveType.Int
                || target == PrimitiveType.Uint || target == PrimitiveType.Long
                || target == PrimitiveType.Ulong || target == PrimitiveType.Float
                || target == PrimitiveType.Double || target == PrimitiveType.Decimal;

        return false;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Utility helpers
    // ═══════════════════════════════════════════════════════════════════════

    private static List<IType> CollectArgumentTypes(ArgumentListNode? argList, ISemanticModel model)
    {
        var types = new List<IType>();
        if (argList is null)
            return types;

        foreach (var child in argList.Children())
        {
            if (child is ExpressionNode expr)
            {
                var type = model.GetTypeOf(expr);
                types.Add(type ?? UnknownType.Instance);
            }
        }

        return types;
    }

    /// <summary>
    /// Collects all method symbols with the given name from the target type's member scope
    /// and inherited methods from base types (walking the base type chain).
    /// </summary>
    private static List<ISymbol> CollectMethodOverloads(IType? targetType, string methodName)
    {
        var results = new List<ISymbol>();
        if (targetType is null)
            return results;

        // Collect from the target type's own member scope.
        var memberScope = targetType switch
        {
            ClassType ct => ct.MemberScope,
            StructType st => st.MemberScope,
            InterfaceType it => it.MemberScope,
            _ => null,
        };

        if (memberScope is not null)
        {
            foreach (var member in memberScope.Members)
            {
                if (member.Kind == SymbolKind.Method && member.Name == methodName)
                    results.Add(member);
            }
        }

        // Walk the base type chain to collect inherited methods.
        if (targetType is ClassType classType)
        {
            var baseType = classType.BaseType as ClassType;
            while (baseType is not null)
            {
                var baseScope = baseType.MemberScope;
                foreach (var member in baseScope.Members)
                {
                    if (member.Kind == SymbolKind.Method && member.Name == methodName)
                        results.Add(member);
                }
                baseType = baseType.BaseType as ClassType;
            }
        }

        return results;
    }

    /// <summary>
    /// Collects all constructor symbols from the target type's member scope.
    /// Unlike methods, constructors are not inherited from base types.
    /// </summary>
    private static List<ISymbol> CollectConstructorOverloads(IType? targetType)
    {
        var results = new List<ISymbol>();
        if (targetType is null)
            return results;

        // For ConstructedType (e.g., GenericBox<int>), unwrap to the generic
        // definition to find constructors in the original type's member scope.
        var lookupType = targetType is ConstructedType cst ? cst.GenericDefinition : targetType;

        var memberScope = lookupType switch
        {
            ClassType ct => ct.MemberScope,
            StructType st => st.MemberScope,
            _ => null,
        };

        if (memberScope is null)
            return results;

        foreach (var member in memberScope.Members)
        {
            if (member.Kind == SymbolKind.Constructor)
                results.Add(member);
        }

        return results;
    }

    /// <summary>
    /// Formats a candidate method symbol's signature for diagnostic messages.
    /// Example: "Print(int)" or "Print(string, int)"
    /// </summary>
    private static string FormatCandidateSignature(ISymbol candidate, string methodName, ISemanticModel model)
    {
        var paramTypes = ParameterSignature.Extract(candidate, model);
        var typeNames = string.Join(", ", paramTypes.Select(t => t.DisplayName));
        return $"{methodName}({typeNames})";
    }

    /// <summary>
    /// Creates a diagnostic for the type checker.
    /// Uses the semantic model to resolve the source file path and convert
    /// offsets to proper 1-based line/column positions.
    /// </summary>
    private static Diagnostic MakeDiagnostic(string id, string message, SyntaxNode node, ISemanticModel model)
    {
        Common.Text.LinePositionSpan? location = null;
        string? compilationUnitPath = null;

        var span = node.Span;
        if (span.Length > 0)
        {
            // Find which AST root contains this node by checking span containment.
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

                // Convert the Storm span to a Mist SourceSpan for proper line/column conversion.
                var sourceSpan = new Common.Text.SourceSpan(span.Start, span.Length);
                location = source.ToLinePositionSpan(sourceSpan);
            }
        }

        return new Diagnostic(
            new DiagnosticId(id),
            DiagnosticSeverity.Error,
            PipelineStage.Analysis,
            message,
            Location: location,
            CompilationUnitPath: compilationUnitPath,
            AnalyzerName: "TypeChecker");
    }
}
