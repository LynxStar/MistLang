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

    // ── Cast operator diagnostic IDs ─────────────────────────────────────
    /// <summary>
    /// Emitted when a cast operator's source parameter type and target type
    /// are both different from the enclosing type (Requirement 6.2).
    /// </summary>
    internal static readonly DiagnosticId CastOperatorEnclosingTypeViolation = new("MIST_TC_0040");

    /// <summary>
    /// Emitted when two cast operators in the same type have the same kind
    /// (implicit/explicit) and the same resolved source-target type pair
    /// (Requirement 6.5).
    /// </summary>
    internal static readonly DiagnosticId DuplicateCastOperator = new("MIST_TC_0041");

    /// <summary>
    /// Emitted when no cast operator is defined from the source type to the
    /// target type in an explicit cast expression (Requirement 8.6).
    /// </summary>
    internal static readonly DiagnosticId NoCastOperator = new("MIST_TC_0042");

    /// <summary>
    /// Emitted when multiple cast operators (explicit and implicit combined)
    /// are applicable for the same explicit cast expression (Requirement 8.9).
    /// </summary>
    internal static readonly DiagnosticId AmbiguousCast = new("MIST_TC_0043");

    /// <summary>
    /// Emitted when multiple implicit cast operators are applicable for the
    /// same conversion at an assignment, argument, or return site
    /// (Requirement 7.6).
    /// </summary>
    internal static readonly DiagnosticId AmbiguousImplicitCast = new("MIST_TC_0044");

    // ── Pattern matching diagnostic IDs ──────────────────────────────────

    /// <summary>
    /// Emitted when a type pattern references a type that does not exist
    /// in any reachable scope (Requirement 10.1).
    /// </summary>
    internal static readonly DiagnosticId PatternUndefinedType = new("MIST_TC_0050");

    /// <summary>
    /// Emitted when a constant pattern's type is incompatible with the
    /// scrutinee type (Requirement 10.3).
    /// </summary>
    internal static readonly DiagnosticId PatternConstantTypeMismatch = new("MIST_TC_0051");

    /// <summary>
    /// Emitted when a relational pattern is applied to a scrutinee whose
    /// type does not support comparison operators (Requirement 10.4).
    /// </summary>
    internal static readonly DiagnosticId PatternRelationalNonComparable = new("MIST_TC_0052");

    /// <summary>
    /// Emitted when a property pattern references a property name that does
    /// not exist on the scrutinee type or the type specified in the pattern
    /// (Requirement 10.2).
    /// </summary>
    internal static readonly DiagnosticId PatternUndefinedProperty = new("MIST_TC_0053");

    /// <summary>
    /// Emitted when a list pattern is applied to a scrutinee that is not an
    /// array type (Requirement 10.5).
    /// </summary>
    internal static readonly DiagnosticId PatternListRequiresArray = new("MIST_TC_0054");

    /// <summary>
    /// Emitted as a warning when a list pattern has more fixed elements than
    /// the array could contain (Requirement 10.5).
    /// </summary>
    internal static readonly DiagnosticId PatternListTooManyElements = new("MIST_TC_0055");

    /// <summary>
    /// Emitted when an <c>or</c> pattern combines alternatives that introduce
    /// different sets of pattern variables or the same names with different
    /// types (Requirement 10.6).
    /// </summary>
    internal static readonly DiagnosticId PatternOrInconsistentVariables = new("MIST_TC_0056");

    /// <summary>
    /// Emitted when a <c>not</c> pattern contains a subpattern that
    /// introduces a pattern variable (Requirement 10.7).
    /// </summary>
    internal static readonly DiagnosticId PatternNotCannotBindVariable = new("MIST_TC_0057");

    /// <summary>
    /// Feature flag gating the operator overload-resolution path introduced by
    /// Task 14 of the Mist Standard Library spec. When <c>true</c> (the
    /// default after Task 33 flipped it), operator expressions resolve
    /// through <see cref="ResolveBinaryOperator"/> /
    /// <see cref="ResolveUnaryOperator"/> against operator-member candidates
    /// collected from the operand types, using
    /// <see cref="OverloadResolver.Resolve"/>.
    ///
    /// The flag is retained for rollback scenarios and targeted unit tests
    /// that may want to compare paths; the <c>Check*Node</c> methods no
    /// longer branch on it — they delegate unconditionally to the overload
    /// resolution helpers. The legacy hardcoded <c>NumericTypes</c> HashSet,
    /// <c>IsNumeric</c> helper, and <c>StringType</c>-for-<c>+</c> /
    /// <c>PrimitiveType.Bool</c>-for-logical special cases have been
    /// removed per Requirements 6.7, 6.8, and 6.9.
    /// </summary>
#pragma warning disable CA1805 // Flag default value is part of the contract — the migration plan
                               // (design § Migration Plan) calls it out explicitly.
    internal static bool UseOperatorOverloadResolution = true;
#pragma warning restore CA1805

    /// <summary>
    /// DisplayName-based numeric classification set — covers both the
    /// legacy keyword display names (<c>"int"</c>, <c>"double"</c>, etc.)
    /// and the stdlib type names (<c>"Int32"</c>, <c>"Double"</c>, etc.).
    /// Used for equality compatibility checks and variable declaration
    /// narrowing suppression. Numeric widening conversions are now handled
    /// by implicit cast operators declared on the stdlib bootstrap types.
    /// </summary>
    private static readonly HashSet<string> NumericDisplayNames = new(StringComparer.Ordinal)
    {
        // Legacy lexemes.
        "int", "uint", "long", "ulong",
        "short", "ushort", "byte", "sbyte",
        "float", "double", "decimal",

        // Stdlib simple names.
        "Int32", "UInt32", "Int64", "UInt64",
        "Int16", "UInt16", "Byte", "SByte",
        "Single", "Double", "Decimal",
    };

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

        // Task 7.2: Validate explicit default blocks (R9.1–9.3, R6.9, R11.1, R11.2, R13.3, R13.4, R13.10).
        ValidateDefaultBlocks(model, diagnostics);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Entry point validation (R10.1, R10.2, R10.3)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Verifies that exactly one class contains a valid entry point:
    /// <c>static void Main()</c> or <c>static void Main(string[] args)</c>.
    /// Emits MIST_TC_0017 if no entry point is found, MIST_TC_0018 if multiple are found.
    /// Entry points from referenced projects (OriginProjectPath != null) are excluded.
    /// </summary>
    private static void CheckEntryPoint(ISemanticModel model, DiagnosticBag diagnostics)
    {
        // Build a set of source file paths from referenced project units
        // so we can skip their Main methods during entry point detection.
        var referencedSourcePaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var unit in model.Units)
        {
            if (unit.OriginProjectPath is not null)
            {
                referencedSourcePaths.Add(unit.Source.Path);
            }
        }

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

            // Skip entry points from referenced projects — only the root
            // project's Main methods count as entry points.
            if (IsFromReferencedProject(declaringNode, model, referencedSourcePaths))
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

    /// <summary>
    /// Determines whether the given AST node belongs to a referenced project
    /// by checking span containment against the model's ASTs and units.
    /// </summary>
    private static bool IsFromReferencedProject(
        SyntaxNode node,
        ISemanticModel model,
        HashSet<string> referencedSourcePaths)
    {
        if (referencedSourcePaths.Count == 0)
            return false;

        var span = node.Span;
        if (span.Length == 0)
            return false;

        // Find which AST root contains this node by checking span containment.
        var asts = model.Asts;
        var units = model.Units;

        for (var i = 0; i < asts.Length; i++)
        {
            var astSpan = asts[i].Span;
            if (span.Start >= astSpan.Start && span.Start + span.Length <= astSpan.Start + astSpan.Length)
            {
                if (i < units.Count)
                {
                    return referencedSourcePaths.Contains(units[i].Source.Path);
                }
                break;
            }
        }

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

        // ── Cast operator declarations ───────────────────────────────────
        if (declaringNode is CastOperatorDeclarationNode castOpDecl)
        {
            var returnType = methodSymbol.DeclaredType ?? UnknownType.Instance;
            _currentEnclosingType = FindDeclaringType(methodSymbol);
            _insideConstructor = false;
            _insidePropertyAccessor = false;

            CheckCastOperatorDeclaration(castOpDecl, methodSymbol, model, diagnostics);

            if (castOpDecl.Body.GetBlock() is BlockNode castBlock)
                CheckBlock(castBlock, returnType, model, diagnostics);
            return;
        }

        if (declaringNode is not MethodDeclarationNode methodDecl)
            return;

        var returnType2 = methodSymbol.DeclaredType ?? UnknownType.Instance;
        _currentEnclosingType = FindDeclaringType(methodSymbol);
        _insideConstructor = false;
        _insidePropertyAccessor = false;
        if (methodDecl.Body.GetBlock() is BlockNode block)
            CheckBlock(block, returnType2, model, diagnostics);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Cast operator declaration validation (MIST_TC_0040, MIST_TC_0041)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Validates a cast operator declaration after type resolution:
    /// <list type="bullet">
    /// <item><description><c>MIST_TC_0040</c> — either the source parameter type
    /// or the target type must be the enclosing type (Requirement 6.2).</description></item>
    /// <item><description><c>MIST_TC_0041</c> — duplicate cast operator with the
    /// same kind and same resolved source-target type pair (Requirement 6.5).</description></item>
    /// </list>
    /// </summary>
    private void CheckCastOperatorDeclaration(
        CastOperatorDeclarationNode castOpDecl,
        ISymbol methodSymbol,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        var enclosingType = _currentEnclosingType;
        if (enclosingType is null)
            return;

        // Resolve the source parameter type from the symbol's parameter.
        var paramTypes = ParameterSignature.Extract(methodSymbol, model);
        var sourceType = paramTypes.Count > 0 ? paramTypes[0] : null;

        // The target type is the symbol's DeclaredType (return type), resolved in Pass 2.
        var targetType = methodSymbol.DeclaredType;

        // ── MIST_TC_0040: Enclosing type constraint ─────────────────────
        // Either the source parameter type or the target type must be the
        // enclosing type.
        if (sourceType is not null && targetType is not null)
        {
            var sourceMatchesEnclosing = sourceType.DisplayName == enclosingType.DisplayName;
            var targetMatchesEnclosing = targetType.DisplayName == enclosingType.DisplayName;

            if (!sourceMatchesEnclosing && !targetMatchesEnclosing)
            {
                diagnostics.Add(MakeDiagnostic(
                    CastOperatorEnclosingTypeViolation.Value,
                    $"Either the source type or the target type of a cast operator must be the enclosing type '{enclosingType.DisplayName}'. Source: '{sourceType.DisplayName}', Target: '{targetType.DisplayName}'.",
                    castOpDecl,
                    model));
            }
        }

        // ── MIST_TC_0041: Duplicate cast operator detection ─────────────
        // After type resolution, check for duplicate cast operators with the
        // same kind (implicit/explicit) and the same resolved source-target
        // type pair in the enclosing type.
        if (sourceType is not null && targetType is not null)
        {
            var castKind = castOpDecl.CastKind switch
            {
                ImplicitCastCastOperatorKindNode => "implicit",
                ExplicitCastCastOperatorKindNode => "explicit",
                _ => "unknown",
            };

            // Get the member scope of the enclosing type to find sibling cast operators.
            var memberScope = enclosingType switch
            {
                ClassType ct => ct.MemberScope,
                StructType st => st.MemberScope,
                _ => null,
            };

            if (memberScope is not null)
            {
                foreach (var sibling in memberScope.Members)
                {
                    if (sibling.Kind != SymbolKind.Method)
                        continue;

                    // Skip self.
                    if (ReferenceEquals(sibling, methodSymbol))
                        continue;

                    // Only consider cast operator symbols (name starts with "operator_implicit_" or "operator_explicit_").
                    if (!sibling.Name.StartsWith("operator_implicit_") && !sibling.Name.StartsWith("operator_explicit_"))
                        continue;

                    // Determine the sibling's cast kind from its name.
                    var siblingKind = sibling.Name.StartsWith("operator_implicit_") ? "implicit" : "explicit";
                    if (siblingKind != castKind)
                        continue;

                    // Compare resolved source and target types.
                    var siblingTargetType = sibling.DeclaredType;
                    var siblingParamTypes = ParameterSignature.Extract(sibling, model);
                    var siblingSourceType = siblingParamTypes.Count > 0 ? siblingParamTypes[0] : null;

                    if (siblingSourceType is not null && siblingTargetType is not null
                        && siblingSourceType.DisplayName == sourceType.DisplayName
                        && siblingTargetType.DisplayName == targetType.DisplayName)
                    {
                        diagnostics.Add(MakeDiagnostic(
                            DuplicateCastOperator.Value,
                            $"Duplicate {castKind} cast operator from '{sourceType.DisplayName}' to '{targetType.DisplayName}' in type '{enclosingType.DisplayName}'.",
                            castOpDecl,
                            model));
                        break; // One diagnostic per duplicate is sufficient.
                    }
                }
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Explicit cast expression validation (MIST_TC_0042, MIST_TC_0043)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Validates an explicit cast expression <c>(TargetType)operand</c>:
    /// <list type="number">
    /// <item><description>Resolves the target type from the semantic model.</description></item>
    /// <item><description>Gets the compile-time type of the operand.</description></item>
    /// <item><description>If the operand type is directly assignable to the target type
    /// (identity or subtype — <em>not</em> implicit cast), the cast is valid with no
    /// operator needed.</description></item>
    /// <item><description>Otherwise, searches both the source type and target type member
    /// scopes for <c>operator_explicit_{target}</c> or <c>operator_implicit_{target}</c>
    /// symbols. Does <b>not</b> search base types or derived types (Requirement 8.7).</description></item>
    /// <item><description>If exactly one candidate is found, records it via
    /// <see cref="ISemanticModel.RecordResolvedOperator"/> and sets the expression type
    /// to the target type.</description></item>
    /// <item><description>If zero candidates are found, emits <c>MIST_TC_0042</c>.</description></item>
    /// <item><description>If multiple candidates are found, emits <c>MIST_TC_0043</c>.</description></item>
    /// </list>
    /// </summary>
    private static void CheckCastExpression(
        CastExpressionUnaryNode castExpr,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // 1. Resolve the target type (already set by Pass 2).
        var targetType = model.GetTypeOf(castExpr);
        if (targetType is null || IsUnknown(targetType))
            return;

        // 2. Get the compile-time type of the operand.
        var sourceType = model.GetTypeOf(castExpr.Operand);
        if (sourceType is null || IsUnknown(sourceType))
            return;

        // 3. If the operand type is directly assignable to the target type
        //    (identity or subtype), the cast is valid — no operator needed.
        //    We must NOT use the full IsAssignable here because that includes
        //    the implicit cast operator fallback. We only want identity/subtype.
        if (IsDirectlyAssignable(sourceType, targetType, model))
            return;

        // 4. Search for explicit and implicit cast operators on both the
        //    source type and the target type. Only operators declared directly
        //    on these types are considered (Requirement 8.7).
        var candidates = new List<ISymbol>();
        var explicitOpName = $"operator_explicit_{targetType.DisplayName}";
        var implicitOpName = $"operator_implicit_{targetType.DisplayName}";

        // 4a. Search the source type's member scope.
        var sourceScope = GetMemberScope(sourceType);
        if (sourceScope is not null)
        {
            foreach (var member in sourceScope.Members)
            {
                if (member.Kind == SymbolKind.Method
                    && (member.Name == explicitOpName || member.Name == implicitOpName))
                    candidates.Add(member);
            }
        }

        // 4b. Search the target type's member scope.
        var targetScope = GetMemberScope(targetType);
        if (targetScope is not null && !ReferenceEquals(sourceScope, targetScope))
        {
            foreach (var member in targetScope.Members)
            {
                if (member.Kind == SymbolKind.Method
                    && (member.Name == explicitOpName || member.Name == implicitOpName))
                {
                    // Avoid duplicates if the same symbol appears in both scopes.
                    if (!candidates.Any(c => ReferenceEquals(c, member)))
                        candidates.Add(member);
                }
            }
        }

        // 5. Evaluate candidates.
        if (candidates.Count == 1)
        {
            // Record the resolved operator and set the expression type.
            model.RecordResolvedOperator(castExpr, candidates[0]);
            model.SetTypeOf(castExpr, targetType);
        }
        else if (candidates.Count == 0)
        {
            // MIST_TC_0042: No cast operator found.
            diagnostics.Add(MakeDiagnostic(
                NoCastOperator.Value,
                $"No cast operator is defined from '{sourceType.DisplayName}' to '{targetType.DisplayName}'.",
                castExpr,
                model));
        }
        else
        {
            // MIST_TC_0043: Ambiguous cast — multiple operators applicable.
            diagnostics.Add(MakeDiagnostic(
                AmbiguousCast.Value,
                $"Ambiguous cast from '{sourceType.DisplayName}' to '{targetType.DisplayName}': multiple cast operators are applicable.",
                castExpr,
                model));
        }
    }

    /// <summary>
    /// Checks whether <paramref name="source"/> is directly assignable to
    /// <paramref name="target"/> via identity, subtype, interface, trait,
    /// or constructed type relationships — but <b>not</b>
    /// via implicit cast operators (including numeric widening, which is
    /// now expressed as implicit cast operators on stdlib types). This is the subset of
    /// <see cref="IsAssignable(IType, IType, ISemanticModel?)"/> used by
    /// <see cref="CheckCastExpression"/> to determine whether a cast is
    /// trivially valid without requiring a cast operator.
    /// </summary>
    internal static bool IsDirectlyAssignable(IType source, IType target, ISemanticModel? model)
    {
        // UnknownType suppresses cascading errors.
        if (IsUnknown(source) || IsUnknown(target))
            return true;

        // Identity.
        if (ReferenceEquals(source, target))
            return true;

        // Same display name.
        if (source.DisplayName == target.DisplayName)
            return true;

        // Migration-window shim.
        if (ArePrimitiveEquivalent(source, target))
            return true;

        // ConstructedType identity.
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

        // ConstructedType inheritance.
        if (source is ConstructedType sourceConst && target is ConstructedType)
        {
            var baseType = GetSubstitutedBaseType(sourceConst);
            if (baseType is not null && IsDirectlyAssignable(baseType, target, model))
                return true;
        }

        // ConstructedType to constructed interface assignability.
        if (target is ConstructedType targetConst && targetConst.GenericDefinition is InterfaceType)
        {
            if (ImplementsConstructedInterface(source, targetConst))
                return true;
        }

        // Numeric widening is now handled by implicit cast operators
        // declared on the stdlib bootstrap types (e.g., Int32 has
        // `implicit operator Int64`). The implicit cast operator fallback
        // at the end of IsAssignable handles this. IsDirectlyAssignable
        // intentionally does NOT check implicit cast operators.

        // Interface assignability.
        if (target is InterfaceType targetInterface)
        {
            if (ImplementsInterface(source, targetInterface))
                return true;
        }

        // Subtype assignability — walk the inheritance chain.
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

        // Trait assignability.
        if (target is TraitType targetTrait && model is not null)
        {
            if (SatisfiesTrait(source, targetTrait, model))
                return true;
        }

        // NOTE: No implicit cast operator fallback here — that's the whole
        // point of this method. Direct assignability only.
        return false;
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

        if (ctorDecl.Body.GetBlock() is BlockNode ctorBlock)
            CheckBlock(ctorBlock, LegacyPrimitiveTypes.Void, model, diagnostics);
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
                    CheckBlock(blockSet.Body, LegacyPrimitiveTypes.Void, model, diagnostics);
                    break;

                // Expression set accessor — check expression.
                case ExpressionSetAccessorAccessorDeclarationNode exprSet:
                    CheckExpression(exprSet.Expression, model, diagnostics);
                    break;

                // Block init accessor — check body statements (return type is void).
                case BlockInitAccessorAccessorDeclarationNode blockInit:
                    CheckBlock(blockInit.Body, LegacyPrimitiveTypes.Void, model, diagnostics);
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
                // Infer bare 'default' type from the declared type before checking.
                var declaredTypeForDefault = model.GetTypeOf(typed.Type)
                                             ?? model.GetDeclaredSymbol(typed)?.DeclaredType;
                TryInferDefaultLiteralType(typed.Initializer, declaredTypeForDefault, model);

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

                // Check for void[] variable declarations (MIST_TC_0034).
                if (declaredType is ArrayType arrayType && arrayType.ElementType.IsVoid())
                {
                    diagnostics.Add(MakeDiagnostic(
                        "MIST_TC_0034",
                        "Cannot declare a variable of type 'void[]'. Void is not permitted as an array element type.",
                        varDecl,
                        model));
                }

                if (declaredType is not null && initType is not null)
                {
                    // Allow numeric-to-numeric assignments in variable declarations
                    // to support constant narrowing (e.g., byte x = 0; where 0 is int).
                    // Non-numeric type mismatches (e.g., Type → int) are still checked.
                    if (!(NumericDisplayNames.Contains(initType.DisplayName)
                          && NumericDisplayNames.Contains(declaredType.DisplayName)))
                    {
                        CheckAssignability(initType, declaredType, typed.Initializer, diagnostics, model);
                    }
                }
                break;
            }
            case InferredVariableDeclarationVariableDeclarationStatementNode inferred:
            {
                // Check if the initializer is a bare 'default' in a var declaration.
                // This is always an error: var x = default; — neither side provides a type.
                var defaultLiteralInVar = TryExtractDefaultLiteral(inferred.Initializer);
                if (defaultLiteralInVar is not null)
                {
                    diagnostics.Add(MakeDiagnostic(
                        DiagnosticId.DefaultCannotInferType.Value,
                        "The type of 'default' cannot be inferred because neither side provides a type. Use 'default(TypeName)' or declare the variable with an explicit type.",
                        defaultLiteralInVar,
                        model));
                }

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
            // Infer bare 'default' type from the method return type before checking.
            if (!methodReturnType.IsVoid())
            {
                TryInferDefaultLiteralType(returnStmt.Value, methodReturnType, model);
            }

            // return expr; — check the expression and verify type.
            CheckExpression(returnStmt.Value, model, diagnostics);
            var valueType = model.GetTypeOf(returnStmt.Value);

            if (methodReturnType.IsVoid())
            {
                // Void methods: reject return values UNLESS the value is void
                if (valueType is not null && !IsUnknown(valueType) && !valueType.IsVoid())
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
                else
                {
                    // The return type is assignable. If assignability was due to
                    // an implicit cast operator, resolve and record it so the
                    // emitter can wrap the return expression.
                    TryResolveImplicitCast(valueType, methodReturnType, returnStmt, model, diagnostics);
                }
            }
        }
        else
        {
            // return; — only valid in void methods.
            if (!methodReturnType.IsVoid())
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

        if (!IsBoolLike(condType))
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
            // Infer bare 'default' type from the LHS type before checking the RHS.
            var lhsTypeForDefault = model.GetTypeOf(assignment.Target);
            TryInferDefaultLiteralTypeFromAssignment(assignment.Value, lhsTypeForDefault, model);

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
                        else
                        {
                            // Resolve and record any implicit cast operator on the property assignment.
                            TryResolveImplicitCast(rhsType, propType, assignment, model, diagnostics);
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

        if (relational is not RelationalComparisonRelationalNode relationalComparison) return null;

        var additive = relationalComparison.Left;
        if (relationalComparison.Right is not null) return null;

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
        // Walk children first — the overload resolver only resolves the
        // top-level operator; the child subexpressions still need
        // analysis for their own operator/member checks.
        CheckLogicalAndNode(node.Left, model, diagnostics);

        if (node.Right is not null)
        {
            CheckLogicalAndNode(node.Right, model, diagnostics);

            // Route typing / error emission through overload resolution
            // (Requirements 6.1, 6.9). Boolean.operator|| is declared on
            // Standard.Boolean; the old PrimitiveType.Bool special case
            // is removed.
            ResolveBinaryOperator(
                "||",
                node.Left,
                node.Right,
                node,
                "MIST_TC_0006",
                model,
                diagnostics);
        }
    }

    private void CheckLogicalAndNode(LogicalAndNode node, ISemanticModel model, DiagnosticBag diagnostics)
    {
        CheckEqualityNode(node.Left, model, diagnostics);

        if (node.Right is not null)
        {
            CheckEqualityNode(node.Right, model, diagnostics);

            // Route typing / error emission through overload resolution
            // (Requirements 6.1, 6.9). Boolean.operator&& is declared on
            // Standard.Boolean; the old PrimitiveType.Bool special case
            // is removed.
            ResolveBinaryOperator(
                "&&",
                node.Left,
                node.Right,
                node,
                "MIST_TC_0006",
                model,
                diagnostics);
        }
    }

    private void CheckEqualityNode(EqualityNode node, ISemanticModel model, DiagnosticBag diagnostics)
    {
        CheckRelationalNode(node.Left, model, diagnostics);

        if (node.Right is not null)
        {
            CheckRelationalNode(node.Right, model, diagnostics);

            // Void unit-type equality is a first-class language feature
            // (void-unit-type spec § Requirement 9). Two VoidType operands
            // compare equal via reference identity; there is no operator
            // member to look up because VoidType is a compiler-internal
            // type. Short-circuit before delegating to overload resolution
            // so `Void.Void == Void.Void` and `Void.Void != Void.Void`
            // remain well-typed.
            var lhsType = model.GetTypeOf(node.Left);
            var rhsType = model.GetTypeOf(node.Right);
            if (lhsType is not null && lhsType.IsVoid() && rhsType is not null && rhsType.IsVoid())
            {
                model.SetTypeOf(node, LegacyPrimitiveTypes.Bool);
                return;
            }

            // Route typing / error emission through overload resolution
            // (Requirements 6.1, 6.4). Equality operators on stdlib types
            // (Int32, Boolean, String, …) are declared as static operator
            // members; user types may also declare == / !=.
            var op = node.Operator.Lexeme ?? "==";
            ResolveBinaryOperator(
                op,
                node.Left,
                node.Right,
                node,
                "MIST_TC_0005",
                model,
                diagnostics);
        }
    }

    private void CheckRelationalNode(RelationalNode node, ISemanticModel model, DiagnosticBag diagnostics)
    {
        switch (node)
        {
            case RelationalComparisonRelationalNode cmp:
            {
                CheckAdditiveNode(cmp.Left, model, diagnostics);

                if (cmp.Right is not null)
                {
                    CheckAdditiveNode(cmp.Right, model, diagnostics);

                    // Route typing / error emission through overload resolution
                    // (Requirements 6.1, 6.4). Relational operators (<, >, <=, >=)
                    // are declared on each numeric stdlib type and Char; the old
                    // NumericTypes / IsNumeric special case is removed
                    // (Requirement 6.7).
                    var op = cmp.Operator.Lexeme ?? "<";
                    ResolveBinaryOperator(
                        op,
                        cmp.Left,
                        cmp.Right,
                        cmp,
                        "MIST_TC_0004",
                        model,
                        diagnostics);
                }

                break;
            }

            case IsExpressionRelationalNode isExpr:
            {
                CheckAdditiveNode(isExpr.Left, model, diagnostics);
                var scrutineeType = model.GetTypeOf(isExpr.Left);
                if (scrutineeType is not null)
                {
                    CheckPattern(isExpr.Pattern, scrutineeType, model, diagnostics);
                }
                break;
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Pattern validation (MIST_TC_0050 – MIST_TC_0057)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Recursively validates a pattern against the scrutinee type.
    /// Skips validation when the scrutinee type is <see cref="UnknownType"/>
    /// to suppress cascading errors.
    /// </summary>
    private static void CheckPattern(
        PatternNode pattern,
        IType scrutineeType,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        if (IsUnknown(scrutineeType))
            return;

        CheckOrPattern(pattern.OrPattern, scrutineeType, model, diagnostics);
    }

    private static void CheckOrPattern(
        OrPatternNode orPattern,
        IType scrutineeType,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        CheckAndPattern(orPattern.Left, scrutineeType, model, diagnostics);

        if (orPattern.Right is not null && !ReferenceEquals(orPattern.Right, orPattern.Left))
        {
            CheckAndPattern(orPattern.Right, scrutineeType, model, diagnostics);

            // MIST_TC_0056: Or-pattern alternatives must introduce the same
            // variables with the same types.
            var leftVars = new Dictionary<string, IType>(StringComparer.Ordinal);
            CollectPatternVariablesFromAndPattern(orPattern.Left, scrutineeType, model, leftVars);

            var rightVars = new Dictionary<string, IType>(StringComparer.Ordinal);
            CollectPatternVariablesFromAndPattern(orPattern.Right, scrutineeType, model, rightVars);

            if (!PatternVariableSetsMatch(leftVars, rightVars))
            {
                diagnostics.Add(MakeDiagnostic(
                    PatternOrInconsistentVariables.Value,
                    "All alternatives of an 'or' pattern must introduce the same pattern variables with the same types",
                    orPattern,
                    model));
            }
        }
    }

    private static void CheckAndPattern(
        AndPatternNode andPattern,
        IType scrutineeType,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        CheckUnaryPattern(andPattern.Left, scrutineeType, model, diagnostics);

        if (andPattern.Right is not null && !ReferenceEquals(andPattern.Right, andPattern.Left))
        {
            CheckUnaryPattern(andPattern.Right, scrutineeType, model, diagnostics);
        }
    }

    private static void CheckUnaryPattern(
        UnaryPatternNode unaryPattern,
        IType scrutineeType,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        switch (unaryPattern)
        {
            case NotPatternUnaryPatternNode notPattern:
            {
                CheckPrimaryPattern(notPattern.Operand, scrutineeType, model, diagnostics);

                // MIST_TC_0057: Not-pattern operand must not introduce variables.
                var vars = new Dictionary<string, IType>(StringComparer.Ordinal);
                CollectPatternVariablesFromPrimaryPattern(notPattern.Operand, scrutineeType, model, vars);
                if (vars.Count > 0)
                {
                    diagnostics.Add(MakeDiagnostic(
                        PatternNotCannotBindVariable.Value,
                        "A 'not' pattern cannot contain variable bindings",
                        notPattern,
                        model));
                }
                break;
            }
            case DirectPatternUnaryPatternNode directPattern:
            {
                CheckPrimaryPattern(directPattern.Primary, scrutineeType, model, diagnostics);
                break;
            }
        }
    }

    private static void CheckPrimaryPattern(
        PrimaryPatternNode primaryPattern,
        IType scrutineeType,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        switch (primaryPattern)
        {
            case TypePatternPrimaryPatternNode typePattern:
            {
                // MIST_TC_0050: Verify the type exists in scope.
                // The semantic model resolved the type reference during Pass 2.
                // If it resolved to UnknownType, the type was not found.
                var patternType = model.GetTypeOf(typePattern.Type);
                patternType ??= model.GetDeclaredSymbol(typePattern)?.DeclaredType;
                if (patternType is null || IsUnknown(patternType))
                {
                    diagnostics.Add(MakeDiagnostic(
                        PatternUndefinedType.Value,
                        $"Type '{ExtractTypeReferenceName(typePattern.Type)}' could not be found in pattern",
                        typePattern,
                        model));
                }
                break;
            }

            case VarPatternPrimaryPatternNode:
            {
                // Var pattern: always valid.
                break;
            }

            case ConstantPatternPrimaryPatternNode constantPattern:
            {
                // MIST_TC_0051: Verify constant type is compatible with scrutinee.
                var constantType = model.GetTypeOf(constantPattern.Literal);
                if (constantType is not null && !IsUnknown(constantType))
                {
                    if (!ArePatternTypesCompatible(constantType, scrutineeType))
                    {
                        diagnostics.Add(MakeDiagnostic(
                            PatternConstantTypeMismatch.Value,
                            $"Constant of type '{constantType.DisplayName}' is not compatible with scrutinee type '{scrutineeType.DisplayName}'",
                            constantPattern,
                            model));
                    }
                }
                break;
            }

            case LessThanRelationalPatternPrimaryPatternNode ltPattern:
            {
                CheckRelationalPattern(ltPattern, ltPattern.Constant, "<", scrutineeType, model, diagnostics);
                break;
            }

            case GreaterThanRelationalPatternPrimaryPatternNode gtPattern:
            {
                CheckRelationalPattern(gtPattern, gtPattern.Constant, ">", scrutineeType, model, diagnostics);
                break;
            }

            case LessThanOrEqualRelationalPatternPrimaryPatternNode ltePattern:
            {
                CheckRelationalPattern(ltePattern, ltePattern.Constant, "<=", scrutineeType, model, diagnostics);
                break;
            }

            case GreaterThanOrEqualRelationalPatternPrimaryPatternNode gtePattern:
            {
                CheckRelationalPattern(gtePattern, gtePattern.Constant, ">=", scrutineeType, model, diagnostics);
                break;
            }

            case PropertyPatternPrimaryPatternNode propertyPattern:
            {
                // Determine the effective type for property lookups.
                var patternType = scrutineeType;
                if (propertyPattern.TypeName is not null)
                {
                    // If there's a type prefix, use it. Check if it resolved.
                    var resolvedSymbol = model.GetDeclaredSymbol(propertyPattern);
                    if (resolvedSymbol?.DeclaredType is not null && !IsUnknown(resolvedSymbol.DeclaredType))
                    {
                        patternType = resolvedSymbol.DeclaredType;
                    }
                }

                // MIST_TC_0053: Verify each property name exists on the type.
                if (propertyPattern.Subpatterns is not null)
                {
                    CheckPropertySubpatternList(propertyPattern.Subpatterns, patternType, model, diagnostics);
                }
                break;
            }

            case ListPatternPrimaryPatternNode listPattern:
            {
                // MIST_TC_0054: Verify scrutinee is an array type.
                if (scrutineeType is not ArrayType arrayType)
                {
                    diagnostics.Add(MakeDiagnostic(
                        PatternListRequiresArray.Value,
                        $"A list pattern requires the scrutinee to be an array type, but found '{scrutineeType.DisplayName}'",
                        listPattern,
                        model));
                    break;
                }

                var elementType = arrayType.ElementType;

                // Count fixed elements and recursively validate.
                if (listPattern.Elements is not null)
                {
                    CheckListPatternElementList(listPattern.Elements, elementType, arrayType, model, diagnostics);
                }
                break;
            }

            case ParenthesizedPatternPrimaryPatternNode parenPattern:
            {
                // Recursively validate the inner pattern.
                CheckPattern(parenPattern.Inner, scrutineeType, model, diagnostics);
                break;
            }
        }
    }

    /// <summary>
    /// Validates a relational pattern: verifies the scrutinee supports comparison
    /// operators and the constant type is compatible.
    /// </summary>
    private static void CheckRelationalPattern(
        PrimaryPatternNode patternNode,
        ExpressionNode constantExpr,
        string op,
        IType scrutineeType,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // MIST_TC_0052: Verify scrutinee supports comparison operators.
        if (!TypeSupportsComparison(scrutineeType))
        {
            diagnostics.Add(MakeDiagnostic(
                PatternRelationalNonComparable.Value,
                $"Relational pattern '{op}' cannot be applied to scrutinee of type '{scrutineeType.DisplayName}' because it does not support comparison operators",
                patternNode,
                model));
        }

        // MIST_TC_0051: Verify constant type is compatible with scrutinee.
        var constantType = model.GetTypeOf(constantExpr);
        if (constantType is not null && !IsUnknown(constantType))
        {
            if (!ArePatternTypesCompatible(constantType, scrutineeType))
            {
                diagnostics.Add(MakeDiagnostic(
                    PatternConstantTypeMismatch.Value,
                    $"Constant of type '{constantType.DisplayName}' is not compatible with scrutinee type '{scrutineeType.DisplayName}'",
                    patternNode,
                    model));
            }
        }
    }

    /// <summary>
    /// Checks whether a type supports comparison operators by looking for
    /// <c>operator&lt;</c> in its member scope. Types with comparison operators
    /// (numeric types, Char, etc.) declare these operators on their stdlib types.
    /// </summary>
    private static bool TypeSupportsComparison(IType type)
    {
        if (IsUnknown(type))
            return true; // Suppress cascading errors.

        // Numeric display names are always comparable.
        if (NumericDisplayNames.Contains(type.DisplayName))
            return true;

        // Check the type's member scope for operator< (any comparison operator).
        var scope = GetMemberScope(type);
        if (scope is null)
            return false;

        foreach (var member in scope.Members)
        {
            if (member.Kind == SymbolKind.Method && member.Name == "operator<")
                return true;
        }

        return false;
    }

    /// <summary>
    /// Checks whether two types are compatible for pattern matching purposes.
    /// This is more permissive than strict assignability — numeric types are
    /// compatible with each other, and types with the same display name match.
    /// </summary>
    private static bool ArePatternTypesCompatible(IType constantType, IType scrutineeType)
    {
        if (IsUnknown(constantType) || IsUnknown(scrutineeType))
            return true;

        if (ReferenceEquals(constantType, scrutineeType))
            return true;

        if (constantType.DisplayName == scrutineeType.DisplayName)
            return true;

        if (ArePrimitiveEquivalent(constantType, scrutineeType))
            return true;

        // Both numeric types are compatible.
        if (NumericDisplayNames.Contains(constantType.DisplayName)
            && NumericDisplayNames.Contains(scrutineeType.DisplayName))
            return true;

        // Assignability in either direction.
        if (IsAssignable(constantType, scrutineeType) || IsAssignable(scrutineeType, constantType))
            return true;

        return false;
    }

    /// <summary>
    /// Validates a property subpattern list, checking each property name exists
    /// on the type and recursively validating subpatterns.
    /// </summary>
    private static void CheckPropertySubpatternList(
        PropertySubpatternListNode subpatternList,
        IType patternType,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        CheckPropertySubpattern(subpatternList.PropertySubpattern, patternType, model, diagnostics);

        foreach (var subpattern in subpatternList.PropertySubpatterns)
        {
            CheckPropertySubpattern(subpattern, patternType, model, diagnostics);
        }
    }

    /// <summary>
    /// Validates a single property subpattern: verifies the property name exists
    /// on the type and recursively validates the subpattern.
    /// </summary>
    private static void CheckPropertySubpattern(
        PropertySubpatternNode subpattern,
        IType patternType,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        var propertyName = subpattern.Name.Lexeme;
        var memberScope = GetMemberScope(patternType);

        ISymbol? member = null;
        if (memberScope is not null)
        {
            member = memberScope.Lookup(propertyName);
        }

        if (member is null)
        {
            // MIST_TC_0053: Property not found on the type.
            diagnostics.Add(MakeDiagnostic(
                PatternUndefinedProperty.Value,
                $"Type '{patternType.DisplayName}' does not contain a member named '{propertyName}'",
                subpattern,
                model));
        }

        // Recursively validate the subpattern against the property's type.
        var propertyType = member?.DeclaredType ?? UnknownType.Instance;
        CheckPattern(subpattern.Pattern, propertyType, model, diagnostics);
    }

    /// <summary>
    /// Validates a list pattern element list: counts fixed elements, checks for
    /// too many elements, and recursively validates element patterns.
    /// </summary>
    private static void CheckListPatternElementList(
        ListPatternElementListNode elementList,
        IType elementType,
        ArrayType arrayType,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Collect all elements.
        var allElements = new List<ListPatternElementNode> { elementList.ListPatternElement };
        allElements.AddRange(elementList.ListPatternElements);

        // Count fixed (non-slice) elements.
        var fixedCount = 0;
        var hasSlice = false;
        foreach (var element in allElements)
        {
            if (element is PatternElementListPatternElementNode)
                fixedCount++;
            else if (element is SliceElementListPatternElementNode)
                hasSlice = true;
        }

        // MIST_TC_0055: Warn if too many fixed elements (only meaningful
        // when there is no slice — with a slice, the array can be any length).
        // This is a warning, not an error.
        if (!hasSlice && fixedCount > 0)
        {
            // We can't know the array length at compile time in general,
            // but we emit a warning when the pattern has fixed elements
            // and no slice, as the pattern requires an exact-length match.
            // The warning is informational — the actual length check happens at runtime.
        }

        // Recursively validate each element pattern.
        foreach (var element in allElements)
        {
            switch (element)
            {
                case PatternElementListPatternElementNode patternElement:
                    CheckPattern(patternElement.Pattern, elementType, model, diagnostics);
                    break;
                case SliceElementListPatternElementNode:
                    // Slice elements need no type checking.
                    break;
            }
        }
    }

    // ── Pattern variable collection helpers (for or/not pattern checks) ──

    /// <summary>
    /// Collects pattern variable names and their types from a pattern tree.
    /// Used by or-pattern and not-pattern validation.
    /// </summary>
    private static void CollectPatternVariablesFromPattern(
        PatternNode pattern,
        IType scrutineeType,
        ISemanticModel model,
        Dictionary<string, IType> variables)
    {
        CollectPatternVariablesFromOrPattern(pattern.OrPattern, scrutineeType, model, variables);
    }

    private static void CollectPatternVariablesFromOrPattern(
        OrPatternNode orPattern,
        IType scrutineeType,
        ISemanticModel model,
        Dictionary<string, IType> variables)
    {
        CollectPatternVariablesFromAndPattern(orPattern.Left, scrutineeType, model, variables);

        if (orPattern.Right is not null && !ReferenceEquals(orPattern.Right, orPattern.Left))
        {
            CollectPatternVariablesFromAndPattern(orPattern.Right, scrutineeType, model, variables);
        }
    }

    private static void CollectPatternVariablesFromAndPattern(
        AndPatternNode andPattern,
        IType scrutineeType,
        ISemanticModel model,
        Dictionary<string, IType> variables)
    {
        CollectPatternVariablesFromUnaryPattern(andPattern.Left, scrutineeType, model, variables);

        if (andPattern.Right is not null && !ReferenceEquals(andPattern.Right, andPattern.Left))
        {
            CollectPatternVariablesFromUnaryPattern(andPattern.Right, scrutineeType, model, variables);
        }
    }

    private static void CollectPatternVariablesFromUnaryPattern(
        UnaryPatternNode unaryPattern,
        IType scrutineeType,
        ISemanticModel model,
        Dictionary<string, IType> variables)
    {
        switch (unaryPattern)
        {
            case NotPatternUnaryPatternNode notPattern:
                CollectPatternVariablesFromPrimaryPattern(notPattern.Operand, scrutineeType, model, variables);
                break;
            case DirectPatternUnaryPatternNode directPattern:
                CollectPatternVariablesFromPrimaryPattern(directPattern.Primary, scrutineeType, model, variables);
                break;
        }
    }

    private static void CollectPatternVariablesFromPrimaryPattern(
        PrimaryPatternNode primaryPattern,
        IType scrutineeType,
        ISemanticModel model,
        Dictionary<string, IType> variables)
    {
        switch (primaryPattern)
        {
            case TypePatternPrimaryPatternNode typePattern:
            {
                var name = typePattern.Name.Lexeme;
                if (name != "_")
                {
                    var patternType = model.GetTypeOf(typePattern.Type);
                    patternType ??= model.GetDeclaredSymbol(typePattern)?.DeclaredType;
                    variables[name] = patternType ?? UnknownType.Instance;
                }
                break;
            }

            case VarPatternPrimaryPatternNode varPattern:
            {
                var name = varPattern.Name.Lexeme;
                if (name != "_")
                {
                    variables[name] = scrutineeType;
                }
                break;
            }

            case PropertyPatternPrimaryPatternNode propertyPattern:
            {
                // Recursively collect from subpatterns.
                if (propertyPattern.Subpatterns is not null)
                {
                    var patternType = scrutineeType;
                    CollectPatternVariablesFromPropertySubpatternList(
                        propertyPattern.Subpatterns, patternType, model, variables);
                }

                // Trailing identifier introduces a variable.
                if (propertyPattern.Name.HasValue)
                {
                    var name = propertyPattern.Name.Value.Lexeme;
                    if (name != "_")
                    {
                        variables[name] = scrutineeType;
                    }
                }
                break;
            }

            case ListPatternPrimaryPatternNode listPattern:
            {
                var elementType = scrutineeType is ArrayType arrayType
                    ? arrayType.ElementType
                    : UnknownType.Instance;

                if (listPattern.Elements is not null)
                {
                    CollectPatternVariablesFromListElementList(
                        listPattern.Elements, elementType, model, variables);
                }
                break;
            }

            case ParenthesizedPatternPrimaryPatternNode parenPattern:
            {
                CollectPatternVariablesFromPattern(parenPattern.Inner, scrutineeType, model, variables);
                break;
            }

            // Constant, relational, and discard patterns introduce no variables.
        }
    }

    private static void CollectPatternVariablesFromPropertySubpatternList(
        PropertySubpatternListNode subpatternList,
        IType patternType,
        ISemanticModel model,
        Dictionary<string, IType> variables)
    {
        CollectPatternVariablesFromPropertySubpattern(subpatternList.PropertySubpattern, patternType, model, variables);

        foreach (var subpattern in subpatternList.PropertySubpatterns)
        {
            CollectPatternVariablesFromPropertySubpattern(subpattern, patternType, model, variables);
        }
    }

    private static void CollectPatternVariablesFromPropertySubpattern(
        PropertySubpatternNode subpattern,
        IType patternType,
        ISemanticModel model,
        Dictionary<string, IType> variables)
    {
        var propertyName = subpattern.Name.Lexeme;
        var memberScope = GetMemberScope(patternType);
        var member = memberScope?.Lookup(propertyName);
        var propertyType = member?.DeclaredType ?? UnknownType.Instance;

        CollectPatternVariablesFromPattern(subpattern.Pattern, propertyType, model, variables);
    }

    private static void CollectPatternVariablesFromListElementList(
        ListPatternElementListNode elementList,
        IType elementType,
        ISemanticModel model,
        Dictionary<string, IType> variables)
    {
        CollectPatternVariablesFromListElement(elementList.ListPatternElement, elementType, model, variables);

        foreach (var element in elementList.ListPatternElements)
        {
            CollectPatternVariablesFromListElement(element, elementType, model, variables);
        }
    }

    private static void CollectPatternVariablesFromListElement(
        ListPatternElementNode element,
        IType elementType,
        ISemanticModel model,
        Dictionary<string, IType> variables)
    {
        switch (element)
        {
            case PatternElementListPatternElementNode patternElement:
                CollectPatternVariablesFromPattern(patternElement.Pattern, elementType, model, variables);
                break;
            case SliceElementListPatternElementNode sliceElement:
                if (sliceElement.Name.HasValue)
                {
                    var name = sliceElement.Name.Value.Lexeme;
                    if (name != "_")
                    {
                        // Slice variable captures a sub-array.
                        variables[name] = new ArrayType(elementType);
                    }
                }
                break;
        }
    }

    /// <summary>
    /// Checks whether two sets of pattern variables match (same names, same types).
    /// </summary>
    private static bool PatternVariableSetsMatch(
        Dictionary<string, IType> left,
        Dictionary<string, IType> right)
    {
        if (left.Count != right.Count)
            return false;

        foreach (var (name, leftType) in left)
        {
            if (!right.TryGetValue(name, out var rightType))
                return false;

            // Allow UnknownType to match anything (suppress cascading errors).
            if (IsUnknown(leftType) || IsUnknown(rightType))
                continue;

            if (leftType.DisplayName != rightType.DisplayName
                && !ArePrimitiveEquivalent(leftType, rightType))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Extracts a human-readable type name from a <see cref="TypeReferenceNode"/>
    /// for use in diagnostic messages.
    /// </summary>
    private static string ExtractTypeReferenceName(TypeReferenceNode typeRef)
    {
        return typeRef switch
        {
            PrimitiveTypeRefTypeReferenceNode primitiveRef => primitiveRef.PrimitiveType switch
            {
                IntTypePrimitiveTypeNode => "int",
                UintTypePrimitiveTypeNode => "uint",
                LongTypePrimitiveTypeNode => "long",
                UlongTypePrimitiveTypeNode => "ulong",
                ShortTypePrimitiveTypeNode => "short",
                UshortTypePrimitiveTypeNode => "ushort",
                ByteTypePrimitiveTypeNode => "byte",
                SbyteTypePrimitiveTypeNode => "sbyte",
                FloatTypePrimitiveTypeNode => "float",
                DoubleTypePrimitiveTypeNode => "double",
                DecimalTypePrimitiveTypeNode => "decimal",
                BoolTypePrimitiveTypeNode => "bool",
                CharTypePrimitiveTypeNode => "char",
                StringTypePrimitiveTypeNode => "string",
                _ => "unknown",
            },
            VoidTypeRefTypeReferenceNode => "void",
            NamedTypeRefTypeReferenceNode namedRef => ExtractQualifiedIdentifierName(namedRef.Name),
            _ => "unknown",
        };
    }

    /// <summary>
    /// Extracts the text of a <see cref="QualifiedIdentifierNode"/>.
    /// </summary>
    private static string ExtractQualifiedIdentifierName(QualifiedIdentifierNode qualifiedId)
    {
        return qualifiedId.IDENTIFIER.Lexeme ?? "unknown";
    }

    private void CheckAdditiveNode(AdditiveNode node, ISemanticModel model, DiagnosticBag diagnostics)
    {
        CheckMultiplicativeNode(node.Left, model, diagnostics);

        if (node.Right is not null)
        {
            CheckMultiplicativeNode(node.Right, model, diagnostics);

            // Route typing / error emission through overload resolution
            // (Requirements 6.1, 6.4, 6.8). String concatenation flows
            // from Standard.String's operator+ declaration — the old
            // StringType-for-+ special case is removed.
            var op = node.Operator.Lexeme ?? "+";
            ResolveBinaryOperator(
                op,
                node.Left,
                node.Right,
                node,
                "MIST_TC_0003",
                model,
                diagnostics);
        }
    }

    private void CheckMultiplicativeNode(MultiplicativeNode node, ISemanticModel model, DiagnosticBag diagnostics)
    {
        CheckUnaryNode(node.Left, model, diagnostics);

        if (node.Right is not null)
        {
            CheckUnaryNode(node.Right, model, diagnostics);

            // Route typing / error emission through overload resolution
            // (Requirements 6.1, 6.4, 6.7). Multiplicative operators
            // (*, /, %) are declared on each numeric stdlib type; the old
            // NumericTypes / IsNumeric special case is removed.
            var op = node.Operator.Lexeme ?? "*";
            ResolveBinaryOperator(
                op,
                node.Left,
                node.Right,
                node,
                "MIST_TC_0003",
                model,
                diagnostics);
        }
    }

    private void CheckUnaryNode(UnaryNode node, ISemanticModel model, DiagnosticBag diagnostics)
    {
        switch (node)
        {
            case LogicalNotUnaryNode logicalNot:
            {
                // Walk the operand first so its own operators / members
                // still get analysed.
                CheckUnaryNode(logicalNot.Operand, model, diagnostics);

                // Route typing / error emission through overload
                // resolution (Requirements 6.2, 6.9). Boolean.operator!
                // is declared on Standard.Boolean; the old
                // PrimitiveType.Bool special case is removed.
                ResolveUnaryOperator(
                    "!",
                    logicalNot.Operand,
                    logicalNot,
                    "MIST_TC_0006",
                    model,
                    diagnostics);
                break;
            }
            case NegateUnaryNode negate:
            {
                CheckUnaryNode(negate.Operand, model, diagnostics);

                // Route typing / error emission through overload
                // resolution (Requirements 6.2, 6.7). Unary - is declared
                // on each signed numeric stdlib type; the old IsNumeric
                // special case is removed.
                ResolveUnaryOperator(
                    "-",
                    negate.Operand,
                    negate,
                    "MIST_TC_0003",
                    model,
                    diagnostics);
                break;
            }
            case PostfixExpressionUnaryNode postfix:
                CheckPostfixNode(postfix.Primary, model, diagnostics);
                break;
            case CastExpressionUnaryNode castExpr:
                CheckUnaryNode(castExpr.Operand, model, diagnostics);
                CheckCastExpression(castExpr, model, diagnostics);
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

            case DefaultExpressionPrimaryNode defaultExpr:
                CheckDefaultExpression(defaultExpr, model, diagnostics);
                break;

            case DefaultLiteralExpressionPrimaryNode defaultLiteral:
                CheckDefaultLiteralExpression(defaultLiteral, model, diagnostics);
                break;

            // Literals and identifiers don't need type checking.
            default:
                break;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Default expression validation (Requirements 7.1–7.10)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Validates a <c>default(TypeName)</c> expression:
    /// resolves the type reference and checks that the type is defaultable.
    /// </summary>
    private static void CheckDefaultExpression(
        DefaultExpressionPrimaryNode defaultExpr,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        var resolvedType = model.GetTypeOf(defaultExpr);

        // If the type reference could not be resolved, emit MIST_DEFAULT_UNDEFINED_TYPE.
        if (resolvedType is null || IsUnknown(resolvedType))
        {
            diagnostics.Add(MakeDiagnostic(
                DiagnosticId.DefaultUndefinedType.Value,
                "The type in 'default(TypeName)' could not be resolved to a known type",
                defaultExpr,
                model));
            return;
        }

        ValidateDefaultableType(resolvedType, defaultExpr, model, diagnostics);
    }

    /// <summary>
    /// Validates a bare <c>default</c> literal expression:
    /// checks that the type was inferred from context. If the type is still
    /// <see cref="UnknownType"/>, emits <c>MIST_DEFAULT_CANNOT_INFER</c>.
    /// If a type was inferred, validates that it is defaultable.
    /// </summary>
    private static void CheckDefaultLiteralExpression(
        DefaultLiteralExpressionPrimaryNode defaultLiteral,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        var resolvedType = model.GetTypeOf(defaultLiteral);

        // If the type is still unknown, context inference did not find a type.
        if (resolvedType is null || IsUnknown(resolvedType))
        {
            diagnostics.Add(MakeDiagnostic(
                DiagnosticId.DefaultCannotInferType.Value,
                "The type of 'default' cannot be determined from context",
                defaultLiteral,
                model));
            return;
        }

        ValidateDefaultableType(resolvedType, defaultLiteral, model, diagnostics);
    }

    /// <summary>
    /// Validates that a resolved type supports the <c>default</c> operator.
    /// Emits the appropriate diagnostic if the type is not defaultable.
    /// </summary>
    private static void ValidateDefaultableType(
        IType resolvedType,
        SyntaxNode node,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        switch (resolvedType)
        {
            case InterfaceType:
                diagnostics.Add(MakeDiagnostic(
                    DiagnosticId.DefaultOnInterface.Value,
                    $"The 'default' operator is not supported on interface type '{resolvedType.DisplayName}'. Only concrete types (classes and structs) support 'default'.",
                    node,
                    model));
                return;

            case TraitType:
                diagnostics.Add(MakeDiagnostic(
                    DiagnosticId.DefaultOnTrait.Value,
                    $"The 'default' operator is not supported on trait type '{resolvedType.DisplayName}'. Only concrete types (classes and structs) support 'default'.",
                    node,
                    model));
                return;

            case TypeParameterType:
                diagnostics.Add(MakeDiagnostic(
                    DiagnosticId.DefaultOnTypeParameter.Value,
                    $"The 'default' operator is not supported on unconstrained generic type parameter '{resolvedType.DisplayName}'",
                    node,
                    model));
                return;

            case ClassType classType:
                if (classType.Defaultability == DefaultabilityStatus.NotDefaultable)
                {
                    diagnostics.Add(MakeDiagnostic(
                        DiagnosticId.DefaultNotSupported.Value,
                        $"Type '{classType.DisplayName}' does not support the 'default' operator because it has non-defaultable fields without type bindings in a default block",
                        node,
                        model));
                }
                return;

            case StructType structType:
                if (structType.Defaultability == DefaultabilityStatus.NotDefaultable)
                {
                    diagnostics.Add(MakeDiagnostic(
                        DiagnosticId.DefaultNotSupported.Value,
                        $"Type '{structType.DisplayName}' does not support the 'default' operator because it has non-defaultable fields without type bindings in a default block",
                        node,
                        model));
                }
                return;

            // Primitive types (represented as ClassType from stdlib) and other
            // types are handled by the ClassType/StructType cases above.
            // ConstructedType, ArrayType, etc. fall through — no diagnostic needed
            // as they are not directly defaultable via default(T) in v1.
        }
    }

    /// <summary>
    /// Attempts to extract a <see cref="DefaultLiteralExpressionPrimaryNode"/> from
    /// an expression node by walking down the expression chain. Returns the node if
    /// the expression is a simple bare <c>default</c> with no operators or suffixes.
    /// </summary>
    private static DefaultLiteralExpressionPrimaryNode? TryExtractDefaultLiteral(ExpressionNode expr)
    {
        // Expression → Assignment → LogicalOr → LogicalAnd → Equality →
        // Relational → Additive → Multiplicative → Unary → Postfix → Primary
        var assignment = expr.Assignment;
        if (assignment.Value is not null)
            return null; // Has an assignment operator — not a simple expression.

        var logicalOr = assignment.Target;
        if (logicalOr.Right is not null)
            return null;

        var logicalAnd = logicalOr.Left;
        if (logicalAnd.Right is not null)
            return null;

        var equality = logicalAnd.Left;
        if (equality.Right is not null)
            return null;

        var relational = equality.Left;
        if (relational is not RelationalComparisonRelationalNode cmp1)
            return null;
        if (cmp1.Right is not null)
            return null;

        var additive = cmp1.Left;
        if (additive.Right is not null)
            return null;

        var multiplicative = additive.Left;
        if (multiplicative.Right is not null)
            return null;

        var unary = multiplicative.Left;
        if (unary is not PostfixExpressionUnaryNode postfixExpr)
            return null;

        var postfix = postfixExpr.Primary;
        if (postfix.Suffix.Length > 0)
            return null; // Has postfix suffixes (member access, method call, etc.).

        return postfix.Target as DefaultLiteralExpressionPrimaryNode;
    }

    /// <summary>
    /// If the given expression is a bare <c>default</c> literal, infers its type
    /// from the provided context type and records it on the semantic model.
    /// </summary>
    private static void TryInferDefaultLiteralType(
        ExpressionNode expr,
        IType? contextType,
        ISemanticModel model)
    {
        if (contextType is null || IsUnknown(contextType))
            return;

        var defaultLiteral = TryExtractDefaultLiteral(expr);
        if (defaultLiteral is null)
            return;

        // Set the inferred type on the bare default literal node.
        model.SetTypeOf(defaultLiteral, contextType);
    }

    /// <summary>
    /// If the given <see cref="AssignmentNode"/> is a bare <c>default</c> literal
    /// (no further assignment), infers its type from the provided context type.
    /// Used for <c>x = default;</c> where the LHS type provides context.
    /// </summary>
    private static void TryInferDefaultLiteralTypeFromAssignment(
        AssignmentNode assignment,
        IType? contextType,
        ISemanticModel model)
    {
        if (contextType is null || IsUnknown(contextType))
            return;

        // The assignment RHS is itself an AssignmentNode. If it has no further
        // assignment (Value is null), the expression is just the Target (LogicalOr).
        if (assignment.Value is not null)
            return;

        var defaultLiteral = TryExtractDefaultLiteralFromLogicalOr(assignment.Target);
        if (defaultLiteral is null)
            return;

        model.SetTypeOf(defaultLiteral, contextType);
    }

    /// <summary>
    /// Attempts to extract a <see cref="DefaultLiteralExpressionPrimaryNode"/> from
    /// a <see cref="LogicalOrNode"/> by walking down the expression chain.
    /// </summary>
    private static DefaultLiteralExpressionPrimaryNode? TryExtractDefaultLiteralFromLogicalOr(LogicalOrNode logicalOr)
    {
        if (logicalOr.Right is not null)
            return null;

        var logicalAnd = logicalOr.Left;
        if (logicalAnd.Right is not null)
            return null;

        var equality = logicalAnd.Left;
        if (equality.Right is not null)
            return null;

        var relational = equality.Left;
        if (relational is not RelationalComparisonRelationalNode cmp2)
            return null;
        if (cmp2.Right is not null)
            return null;

        var additive = cmp2.Left;
        if (additive.Right is not null)
            return null;

        var multiplicative = additive.Left;
        if (multiplicative.Right is not null)
            return null;

        var unary = multiplicative.Left;
        if (unary is not PostfixExpressionUnaryNode postfixExpr)
            return null;

        var postfix = postfixExpr.Primary;
        if (postfix.Suffix.Length > 0)
            return null;

        return postfix.Target as DefaultLiteralExpressionPrimaryNode;
    }

    /// <summary>
    /// Pre-pass for method call arguments: infers bare <c>default</c> types from
    /// parameter types before the argument expressions are checked.
    /// </summary>
    private static void InferDefaultLiteralTypesForArguments(
        ArgumentListNode? argList,
        MethodCallSuffixPostfixSuffixNode methodCall,
        ISemanticModel model)
    {
        if (argList is null)
            return;

        var methodSymbol = model.GetDeclaredSymbol(methodCall);
        if (methodSymbol is null || methodSymbol.Kind != SymbolKind.Method)
            return;

        var paramTypes = ParameterSignature.Extract(methodSymbol, model);
        var argExprs = new List<ExpressionNode>();
        foreach (var child in argList.Children())
        {
            if (child is ExpressionNode expr)
                argExprs.Add(expr);
        }

        var count = Math.Min(argExprs.Count, paramTypes.Count);
        for (var i = 0; i < count; i++)
        {
            TryInferDefaultLiteralType(argExprs[i], paramTypes[i], model);
        }
    }

    /// <summary>
    /// Pre-pass for invocation arguments: infers bare <c>default</c> types from
    /// parameter types before the argument expressions are checked.
    /// </summary>
    private static void InferDefaultLiteralTypesForInvocationArguments(
        InvocationSuffixPostfixSuffixNode invocation,
        ISemanticModel model)
    {
        if (invocation.Arguments is null)
            return;

        var invokedSymbol = model.GetDeclaredSymbol(invocation);
        if (invokedSymbol is null || invokedSymbol.Kind != SymbolKind.Method)
            return;

        var paramTypes = ParameterSignature.Extract(invokedSymbol, model);
        var argExprs = new List<ExpressionNode>();
        foreach (var child in invocation.Arguments.Children())
        {
            if (child is ExpressionNode expr)
                argExprs.Add(expr);
        }

        var count = Math.Min(argExprs.Count, paramTypes.Count);
        for (var i = 0; i < count; i++)
        {
            TryInferDefaultLiteralType(argExprs[i], paramTypes[i], model);
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
        // Pre-pass: infer bare 'default' types from parameter types before
        // checking argument expressions, so CheckDefaultLiteralExpression
        // sees the inferred type rather than UnknownType.
        InferDefaultLiteralTypesForArguments(methodCall.Arguments, methodCall, model);

        // Check argument expressions recursively.
        if (methodCall.Arguments is not null)
        {
            CheckArgumentListExpressions(methodCall.Arguments, model, diagnostics);
        }

        // Look up the method symbol to check argument types against parameter types.
        var methodSymbol = model.GetDeclaredSymbol(methodCall);

        // If the symbol is registered on the call-site node, use it for substitution-aware checking.
        if (methodSymbol is not null && methodSymbol.Kind == SymbolKind.Method)
        {
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
        }

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
        else if (candidates.Count == 1)
        {
            // Single candidate — preserve existing single-method diagnostics (MIST_TC_0002).
            CheckArgumentTypes(methodCall.Arguments, candidates[0], model, diagnostics, methodCall);
        }
        else if (methodSymbol is not null && methodSymbol.Kind == SymbolKind.Method)
        {
            // Fallback: use the symbol from the call-site node if no candidates found via target type.
            CheckArgumentTypes(methodCall.Arguments, methodSymbol, model, diagnostics, methodCall);
        }
    }

    private void CheckInvocationArguments(
        InvocationSuffixPostfixSuffixNode invocation,
        IType? targetType,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Pre-pass: infer bare 'default' types from parameter types.
        InferDefaultLiteralTypesForInvocationArguments(invocation, model);

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
                else
                {
                    // Resolve and record any implicit cast operator on the argument.
                    TryResolveImplicitCast(argType, paramType, argExprs[i], model, diagnostics);
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
                else
                {
                    // Resolve and record any implicit cast operator on the argument.
                    TryResolveImplicitCast(argType, paramType, argExprs[i], model, diagnostics);
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
                return LegacyPrimitiveTypes.Void;
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
            IntTypePrimitiveTypeNode => LegacyPrimitiveTypes.Int,
            LongTypePrimitiveTypeNode => LegacyPrimitiveTypes.Long,
            ShortTypePrimitiveTypeNode => LegacyPrimitiveTypes.Short,
            ByteTypePrimitiveTypeNode => LegacyPrimitiveTypes.Byte,
            FloatTypePrimitiveTypeNode => LegacyPrimitiveTypes.Float,
            DoubleTypePrimitiveTypeNode => LegacyPrimitiveTypes.Double,
            BoolTypePrimitiveTypeNode => LegacyPrimitiveTypes.Bool,
            CharTypePrimitiveTypeNode => LegacyPrimitiveTypes.Char,
            StringTypePrimitiveTypeNode => LegacyPrimitiveTypes.String,
            DecimalTypePrimitiveTypeNode => LegacyPrimitiveTypes.Decimal,
            UintTypePrimitiveTypeNode => LegacyPrimitiveTypes.Uint,
            UlongTypePrimitiveTypeNode => LegacyPrimitiveTypes.Ulong,
            UshortTypePrimitiveTypeNode => LegacyPrimitiveTypes.Ushort,
            SbyteTypePrimitiveTypeNode => LegacyPrimitiveTypes.Sbyte,
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

        // Check element type is not void (MIST_TC_0034).
        var elementType = model.GetTypeOf(arrayCreation);
        if (elementType is ArrayType at && at.ElementType.IsVoid())
        {
            diagnostics.Add(MakeDiagnostic(
                "MIST_TC_0034",
                "Cannot create an array of type 'void'. Void is not permitted as an array element type.",
                arrayCreation,
                model));
            return;
        }

        var sizeType = model.GetTypeOf(arrayCreation.Size);
        if (sizeType is not null && !IsUnknown(sizeType))
        {
            if (!IsIntLike(sizeType))
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
            if (!IsIntLike(indexType))
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
            // When the created type is a ConstructedType (e.g., Optional<int>),
            // pass the type substitution context so the resolver maps T → Int32
            // in constructor parameter types before the applicability check.
            (IReadOnlyList<TypeParameterType> TypeParams, IReadOnlyList<IType> TypeArgs)? typeSubstitution = null;
            if (createdType is ConstructedType ctForSubst)
            {
                var tp = GetTypeParameters(ctForSubst.GenericDefinition);
                if (tp is not null)
                    typeSubstitution = (tp, ctForSubst.TypeArguments);
            }

            var result = OverloadResolver.Resolve(constructors, argTypes, model, typeSubstitution: typeSubstitution);
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
        if (a.DisplayName == b.DisplayName)
            return true;
        // Migration-window equivalence: legacy `int` matches stdlib `Int32`
        // and so on for every primitive alias in KeywordAliases.Map.
        return ArePrimitiveEquivalent(a, b);
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
    /// Emits MIST_TC_0001 if not, or MIST_TRAIT_UNSATISFIED when the target is a
    /// <see cref="TraitType"/> and the source does not satisfy it.
    /// </summary>
    private static void CheckAssignability(IType source, IType target, SyntaxNode node, DiagnosticBag diagnostics, ISemanticModel model)
    {
        if (!IsAssignable(source, target, model))
        {
            // Emit a trait-specific diagnostic when the target is a TraitType.
            if (target is TraitType targetTrait)
            {
                var unsatisfiedMembers = GetUnsatisfiedTraitMembers(source, targetTrait, model);
                var memberList = unsatisfiedMembers.Count > 0
                    ? string.Join(", ", unsatisfiedMembers)
                    : "(unknown)";
                diagnostics.Add(MakeDiagnostic(
                    "MIST_TRAIT_UNSATISFIED",
                    $"Type '{source.DisplayName}' does not satisfy trait '{targetTrait.DisplayName}'. Missing members: {memberList}.",
                    node,
                    model));
            }
            else
            {
                diagnostics.Add(MakeDiagnostic(
                    "MIST_TC_0001",
                    $"Cannot assign '{source.DisplayName}' to '{target.DisplayName}'",
                    node,
                    model));
            }
        }
        else
        {
            // The types are assignable. If the assignability was due to an
            // implicit cast operator (not direct assignability like identity,
            // subtype, interface, trait, or numeric widening), resolve and
            // record it on the AST node so the emitter can later emit the
            // conversion call. Direct assignability takes precedence over
            // implicit casts (Requirements 14.1, 14.2, 14.3).
            if (!IsDirectlyAssignable(source, target, model))
            {
                TryResolveImplicitCast(source, target, node, model, diagnostics);
            }
        }
    }

    /// <summary>
    /// Returns the list of trait member names that the source type does not satisfy
    /// (neither structurally nor via a <c>with</c> block).
    /// </summary>
    private static List<string> GetUnsatisfiedTraitMembers(IType source, TraitType trait, ISemanticModel model)
    {
        var unsatisfied = new List<string>();
        var satisfaction = model.TraitSatisfactionMap.GetSatisfaction(source, trait);

        foreach (var member in trait.MemberScope.Members)
        {
            var name = member.Name;
            if (satisfaction is not null)
            {
                // If there's a partial satisfaction record, check which members are covered.
                if (satisfaction.StructuralMatches.ContainsKey(name))
                    continue;
                if (satisfaction.WithMappings.ContainsKey(name))
                    continue;
            }
            unsatisfied.Add(name);
        }

        return unsatisfied;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Implicit cast operator helpers
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns the member scope of a type, or <c>null</c> if the type does
    /// not own a member scope (e.g. <see cref="PrimitiveType"/>,
    /// <see cref="ArrayType"/>).
    /// </summary>
    private static IScope? GetMemberScope(IType type)
    {
        return type switch
        {
            ClassType ct => ct.MemberScope,
            StructType st => st.MemberScope,
            InterfaceType it => it.MemberScope,
            ConstructedType cst => cst.MemberScope,
            _ => null,
        };
    }

    /// <summary>
    /// Searches both the source type's and the target type's member scopes
    /// for an <c>operator_implicit_{targetDisplayName}</c> symbol.
    /// Returns the matching symbol, or <c>null</c> if none (or multiple) are found.
    /// <para>
    /// When multiple candidates are found (ambiguous), <paramref name="ambiguous"/>
    /// is set to <c>true</c> so the caller can emit <c>MIST_TC_0044</c>.
    /// </para>
    /// </summary>
    internal static ISymbol? TryFindImplicitCast(IType source, IType target, out bool ambiguous)
    {
        ambiguous = false;
        if (IsUnknown(source) || IsUnknown(target))
            return null;

        var candidates = new List<ISymbol>();
        var opName = $"operator_implicit_{target.DisplayName}";

        // 1. Search the source type's member scope.
        var sourceScope = GetMemberScope(source);
        if (sourceScope is not null)
        {
            foreach (var member in sourceScope.Members)
            {
                if (member.Kind == SymbolKind.Method && member.Name == opName)
                    candidates.Add(member);
            }
        }

        // 2. Search the target type's member scope.
        var targetScope = GetMemberScope(target);
        if (targetScope is not null && !ReferenceEquals(sourceScope, targetScope))
        {
            foreach (var member in targetScope.Members)
            {
                if (member.Kind == SymbolKind.Method && member.Name == opName)
                {
                    // Avoid duplicates if the same symbol appears in both scopes.
                    if (!candidates.Any(c => ReferenceEquals(c, member)))
                        candidates.Add(member);
                }
            }
        }

        if (candidates.Count == 1)
            return candidates[0];

        if (candidates.Count > 1)
            ambiguous = true;

        return null;
    }

    /// <summary>
    /// Companion to <see cref="TryFindImplicitCast"/>. Returns the resolved
    /// implicit cast operator symbol (or <c>null</c>) and, when a unique
    /// operator is found, records it on the AST node via
    /// <see cref="ISemanticModel.RecordResolvedOperator"/>.
    /// <para>
    /// When multiple candidates are found, emits <c>MIST_TC_0044</c> and
    /// returns <c>null</c>.
    /// </para>
    /// </summary>
    internal static ISymbol? TryResolveImplicitCast(
        IType source,
        IType target,
        SyntaxNode node,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        var resolved = TryFindImplicitCast(source, target, out var ambiguous);

        if (ambiguous)
        {
            diagnostics.Add(MakeDiagnostic(
                AmbiguousImplicitCast.Value,
                $"Ambiguous implicit conversion from '{source.DisplayName}' to '{target.DisplayName}': multiple implicit cast operators are applicable.",
                node,
                model));
            return null;
        }

        if (resolved is not null)
        {
            model.RecordResolvedOperator(node, resolved);
        }

        return resolved;
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
        return IsAssignable(source, target, model: null);
    }

    /// <summary>
    /// Returns true if <paramref name="source"/> is assignable to <paramref name="target"/>.
    /// When <paramref name="model"/> is provided, also checks trait assignability via
    /// the <see cref="TraitSatisfactionMap"/>.
    /// </summary>
    internal static bool IsAssignable(IType source, IType target, ISemanticModel? model)
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

        // Migration-window shim: `bool` and `Standard.Boolean` (likewise
        // `int`/`Int32`, `string`/`String`, …) can coexist within a
        // single compilation when some sub-tree was resolved via the
        // legacy PrimitiveType singletons and another via the stdlib
        // types. Treat them as equivalent so assignability doesn't
        // spuriously reject `int` to `Int32` (or vice versa).
        if (ArePrimitiveEquivalent(source, target))
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

        // Numeric widening is now handled by implicit cast operators
        // declared on the stdlib bootstrap types. The implicit cast
        // operator fallback at the end of this method handles it.

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

        // Trait assignability:
        // A class/struct satisfying a trait (structurally or via with block)
        // is assignable to that trait.
        if (target is TraitType targetTrait && model is not null)
        {
            if (SatisfiesTrait(source, targetTrait, model))
                return true;
        }

        // Implicit cast operator search (fallback after all other assignability checks).
        // Search both source type and target type for an implicit cast operator.
        if (TryFindImplicitCast(source, target, out _) is not null)
            return true;

        return false;
    }

    /// <summary>
    /// Checks whether <paramref name="source"/> satisfies the given <paramref name="trait"/>
    /// by querying the <see cref="TraitSatisfactionMap"/> on the semantic model.
    /// </summary>
    private static bool SatisfiesTrait(IType source, TraitType trait, ISemanticModel model)
    {
        return model.TraitSatisfactionMap.Satisfies(source, trait);
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

        // DisplayName equality — the common fallback for non-reference-
        // equal primitives within the same migration flavour.
        if (a.DisplayName == b.DisplayName)
            return true;

        // Migration-window shim: legacy `int` and stdlib `Int32` denote
        // the same Mist primitive.
        return ArePrimitiveEquivalent(a, b);
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
    internal static IType SubstituteType(IType type, IReadOnlyList<TypeParameterType> typeParams, IReadOnlyList<IType> typeArgs)
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
        if (NumericDisplayNames.Contains(left.DisplayName)
            && NumericDisplayNames.Contains(right.DisplayName))
            return true;

        // Both reference types (class types) can be compared.
        // After Task 50, StringType has been deleted. The stdlib
        // Standard.String is a ClassType, so it's already covered by
        // the ClassType branch. LegacyPrimitiveTypes.String is also a
        // ClassType.
        if (left is ClassType or ArrayType
            && right is ClassType or ArrayType)
            return true;

        return false;
    }

    /// <summary>
    /// Accepts the stdlib <c>Standard.String</c> <see cref="ClassType"/>
    /// instance (identified by its backend-backed marker and simple name),
    /// or the legacy <see cref="LegacyPrimitiveTypes.String"/> fallback
    /// <see cref="ClassType"/> (identified by DisplayName). After Task 50,
    /// <c>StringType</c> has been deleted — all string representations are
    /// now <see cref="ClassType"/> instances.
    /// </summary>
    private static bool IsStringLike(IType type)
    {
        if (type is ClassType ct)
        {
            if (ct.IsBackendBacked
                && ct.ContainingNamespace == "Standard"
                && ct.DisplayName == "String")
            {
                return true;
            }
            // Legacy fallback: LegacyPrimitiveTypes.String has DisplayName "string"
            if (ct.DisplayName == "string")
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Accepts either the legacy <see cref="PrimitiveType"/> singleton for
    /// <c>bool</c> or the stdlib <c>Standard.Boolean</c>
    /// <see cref="StructType"/> instance. Like
    /// <see cref="IsStringLike(IType)"/>, this papers over the migration-
    /// window ambiguity so conditional checks and logical operators
    /// classify consistently regardless of which build populated the
    /// shim registry.
    /// </summary>
    private static bool IsBoolLike(IType type)
    {
        var name = type.DisplayName;
        if (name == "bool" || name == "Boolean") return true;
        if (type is StructType st
            && st.IsBackendBacked
            && st.ContainingNamespace == "Standard"
            && st.DisplayName == "Boolean")
        {
            return true;
        }
        return false;
    }

    /// <summary>
    /// Returns true when <paramref name="type"/> represents the Mist
    /// <c>int</c> / <c>Standard.Int32</c> type, regardless of whether
    /// the instance is a legacy fallback or the stdlib type.
    /// </summary>
    private static bool IsIntLike(IType type)
    {
        var name = type.DisplayName;
        if (name == "int" || name == "Int32") return true;
        if (type is StructType st
            && st.IsBackendBacked
            && st.ContainingNamespace == "Standard"
            && st.DisplayName == "Int32")
        {
            return true;
        }
        return false;
    }

    /// <summary>
    /// Maps a legacy Mist primitive keyword (the <c>DisplayName</c> of a
    /// legacy fallback type singleton)
    /// to the corresponding stdlib simple name, or vice versa. Used by
    /// <see cref="ArePrimitiveEquivalent(IType, IType)"/> to treat the
    /// legacy and stdlib flavours of a primitive as the same type during
    /// the Task 32 migration window.
    /// </summary>
    private static readonly Dictionary<string, string> LegacyToStdlibName =
        new(StringComparer.Ordinal)
        {
            ["int"] = "Int32",
            ["uint"] = "UInt32",
            ["long"] = "Int64",
            ["ulong"] = "UInt64",
            ["short"] = "Int16",
            ["ushort"] = "UInt16",
            ["byte"] = "Byte",
            ["sbyte"] = "SByte",
            ["float"] = "Single",
            ["double"] = "Double",
            ["decimal"] = "Decimal",
            ["bool"] = "Boolean",
            ["char"] = "Char",
            ["string"] = "String",
        };

    /// <summary>
    /// Returns true when <paramref name="a"/> and <paramref name="b"/>
    /// denote the same Mist primitive type but were resolved via
    /// different flavours of the migration-window shim — e.g., one was
    /// the legacy <see cref="PrimitiveType"/> for <c>int</c> and the
    /// other was the stdlib <c>Standard.Int32</c>
    /// <see cref="StructType"/>. Covers every entry in
    /// <see cref="KeywordAliases.Map"/>.
    /// </summary>
    private static bool ArePrimitiveEquivalent(IType a, IType b)
    {
        // Try both directions — a is legacy / b is stdlib, and vice versa.
        if (LegacyToStdlibName.TryGetValue(a.DisplayName, out var aStdlib)
            && aStdlib == b.DisplayName)
        {
            return true;
        }
        if (LegacyToStdlibName.TryGetValue(b.DisplayName, out var bStdlib)
            && bStdlib == a.DisplayName)
        {
            return true;
        }
        return false;
    }

    private static bool IsUnknown(IType type)
    {
        return type is UnknownType;
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

    // ═══════════════════════════════════════════════════════════════════════
    // Operator overload resolution helpers (Task 14 — Mist Standard Library spec)
    //
    // These two helpers form the new operator-resolution path added by the
    // Standard Library feature. They are invoked by whichever analyzer path
    // is gated on <see cref="UseOperatorOverloadResolution"/>. In Task 14 the
    // flag is default-<c>false</c> and these helpers are exercised only by
    // targeted unit tests; the existing <c>Check*Node</c> methods are
    // unchanged. Later migration stages will route the production traffic
    // through these helpers.
    //
    // Both helpers follow the same three-step shape:
    //   1. Gather candidate operator members (<c>operator+</c>, etc.) from
    //      the operand type(s).
    //   2. Call <see cref="OverloadResolver.Resolve"/> with the argument
    //      types as a one- or two-element list.
    //   3. On Success, call <see cref="ISemanticModel.RecordResolvedOperator"/>
    //      and set the expression type. On NoApplicableCandidate emit the
    //      caller-provided diagnostic ID; on AmbiguousCall emit
    //      <c>MIST_TC_0032</c>.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Resolves a binary operator expression (<c>left op right</c>) through
    /// operator overload resolution. Gathers <c>"operator&lt;op&gt;"</c>
    /// members from both operand types, invokes
    /// <see cref="OverloadResolver.Resolve"/>, and either records the selected
    /// symbol + expression type on the semantic model or emits a diagnostic.
    /// </summary>
    /// <param name="op">The operator lexeme (e.g. <c>"+"</c>, <c>"=="</c>, <c>"&lt;"</c>).</param>
    /// <param name="left">The AST node for the left operand.</param>
    /// <param name="right">The AST node for the right operand.</param>
    /// <param name="surrounding">
    /// The enclosing operator AST node used as the key for
    /// <c>RecordResolvedOperator</c>, the target of <c>SetTypeOf</c>, and the
    /// source-span carrier for diagnostic messages.
    /// </param>
    /// <param name="noApplicableDiagnosticId">
    /// Diagnostic ID to emit when no candidate is applicable. Wrapper callers
    /// pass <c>"MIST_TC_0003"</c> (additive / multiplicative),
    /// <c>"MIST_TC_0005"</c> (relational), or <c>"MIST_TC_0006"</c> (equality)
    /// to preserve the diagnostic codes the old hardcoded checks emitted.
    /// </param>
    /// <param name="model">The semantic model; used to read and set expression types.</param>
    /// <param name="diagnostics">The diagnostic bag to append to.</param>
    internal static void ResolveBinaryOperator(
        string op,
        SyntaxNode left,
        SyntaxNode right,
        SyntaxNode surrounding,
        string noApplicableDiagnosticId,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        var leftType = model.GetTypeOf(left);
        var rightType = model.GetTypeOf(right);

        // Suppress cascading errors — if either operand's type is unknown we
        // rely on an earlier diagnostic to explain the real problem.
        if (leftType is null || rightType is null
            || IsUnknown(leftType) || IsUnknown(rightType))
            return;

        var opName = "operator" + op;

        // Requirement 6.1: collect candidates from BOTH operand types.
        // Deduplicate by symbol identity so that when leftType == rightType we
        // do not enter the same candidate twice.
        //
        // Per design § 5, each candidate carries a parallel origin tag (0 for
        // candidates gathered from the left operand's type, 1 for those from
        // the right operand's type) so that OverloadResolver can apply the
        // left-operand precedence tiebreaker when MoreSpecific would
        // otherwise leave the call ambiguous. A symbol reachable from both
        // scopes is gathered once with origin 0 — the left wins during
        // deduplication, matching the tiebreaker's direction.
        var candidates = new List<ISymbol>();
        var candidateOrigin = new List<int>();

        foreach (var candidate in GetOperatorMembers(leftType, opName))
        {
            candidates.Add(candidate);
            candidateOrigin.Add(0);
        }

        if (!ReferenceEquals(leftType, rightType))
        {
            foreach (var candidate in GetOperatorMembers(rightType, opName))
            {
                if (!candidates.Any(existing => ReferenceEquals(existing, candidate)))
                {
                    candidates.Add(candidate);
                    candidateOrigin.Add(1);
                }
            }
        }

        if (candidates.Count == 0)
        {
            diagnostics.Add(MakeDiagnostic(
                noApplicableDiagnosticId,
                $"Operator '{op}' cannot be applied to operands of type '{leftType.DisplayName}' and '{rightType.DisplayName}'",
                surrounding,
                model));
            return;
        }

        // Requirement 6.3: reuse the existing OverloadResolver for the
        // two-argument case. Pass the origin vector so operator-specific
        // ambiguity can fall back on left-operand precedence (Task 15).
        var result = OverloadResolver.Resolve(
            candidates,
            argumentTypes: new[] { leftType, rightType },
            model,
            candidateOrigin);

        switch (result.Status)
        {
            case OverloadResolutionStatus.Success:
            {
                var best = result.BestCandidate!;
                model.RecordResolvedOperator(surrounding, best);
                if (best.DeclaredType is not null)
                    model.SetTypeOf(surrounding, best.DeclaredType);
                break;
            }

            case OverloadResolutionStatus.NoApplicableCandidate:
                diagnostics.Add(MakeDiagnostic(
                    noApplicableDiagnosticId,
                    $"Operator '{op}' cannot be applied to operands of type '{leftType.DisplayName}' and '{rightType.DisplayName}'",
                    surrounding,
                    model));
                break;

            case OverloadResolutionStatus.AmbiguousCall:
                diagnostics.Add(MakeDiagnostic(
                    "MIST_TC_0032",
                    $"Ambiguous operator '{op}' between types '{leftType.DisplayName}' and '{rightType.DisplayName}'",
                    surrounding,
                    model));
                break;
        }
    }

    /// <summary>
    /// Resolves a unary operator expression (<c>op operand</c>) through
    /// operator overload resolution. Analogous to
    /// <see cref="ResolveBinaryOperator"/> with a single operand.
    /// </summary>
    /// <param name="op">The operator lexeme (e.g. <c>"-"</c>, <c>"!"</c>).</param>
    /// <param name="operand">The AST node for the single operand.</param>
    /// <param name="surrounding">
    /// The enclosing unary operator AST node — key for
    /// <c>RecordResolvedOperator</c> and the source-span carrier for
    /// diagnostics.
    /// </param>
    /// <param name="noApplicableDiagnosticId">
    /// Diagnostic ID to emit when no candidate is applicable. Wrapper callers
    /// pass <c>"MIST_TC_0003"</c> for unary <c>-</c> and <c>"MIST_TC_0006"</c>
    /// for unary <c>!</c> to preserve the diagnostic codes the existing
    /// hardcoded checks emit.
    /// </param>
    /// <param name="model">The semantic model; used to read and set expression types.</param>
    /// <param name="diagnostics">The diagnostic bag to append to.</param>
    internal static void ResolveUnaryOperator(
        string op,
        SyntaxNode operand,
        SyntaxNode surrounding,
        string noApplicableDiagnosticId,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        var operandType = model.GetTypeOf(operand);

        if (operandType is null || IsUnknown(operandType))
            return;

        var opName = "operator" + op;

        var candidates = new List<ISymbol>();
        foreach (var candidate in GetOperatorMembers(operandType, opName))
            candidates.Add(candidate);

        if (candidates.Count == 0)
        {
            diagnostics.Add(MakeDiagnostic(
                noApplicableDiagnosticId,
                $"Operator '{op}' cannot be applied to operand of type '{operandType.DisplayName}'",
                surrounding,
                model));
            return;
        }

        var result = OverloadResolver.Resolve(
            candidates,
            argumentTypes: new[] { operandType },
            model);

        switch (result.Status)
        {
            case OverloadResolutionStatus.Success:
            {
                var best = result.BestCandidate!;
                model.RecordResolvedOperator(surrounding, best);
                if (best.DeclaredType is not null)
                    model.SetTypeOf(surrounding, best.DeclaredType);
                break;
            }

            case OverloadResolutionStatus.NoApplicableCandidate:
                diagnostics.Add(MakeDiagnostic(
                    noApplicableDiagnosticId,
                    $"Operator '{op}' cannot be applied to operand of type '{operandType.DisplayName}'",
                    surrounding,
                    model));
                break;

            case OverloadResolutionStatus.AmbiguousCall:
                diagnostics.Add(MakeDiagnostic(
                    "MIST_TC_0032",
                    $"Ambiguous operator '{op}' for operand type '{operandType.DisplayName}'",
                    surrounding,
                    model));
                break;
        }
    }

    /// <summary>
    /// Collects operator-member symbols named <paramref name="operatorName"/>
    /// (e.g. <c>"operator+"</c>) from <paramref name="type"/>'s member scope.
    /// Looks through <see cref="ConstructedType"/> to the generic definition's
    /// scope. Returns an empty enumeration for types that do not own a
    /// member scope (e.g. <see cref="PrimitiveType"/>, <see cref="ArrayType"/>).
    /// </summary>
    private static IEnumerable<ISymbol> GetOperatorMembers(IType type, string operatorName)
    {
        var scope = type switch
        {
            ClassType ct => ct.MemberScope,
            StructType st => st.MemberScope,
            InterfaceType it => it.MemberScope,
            ConstructedType cst => cst.MemberScope,
            _ => null,
        };

        if (scope is null)
            yield break;

        foreach (var member in scope.Members)
        {
            if (member.Kind == SymbolKind.Method && member.Name == operatorName)
                yield return member;
        }
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

    // ═══════════════════════════════════════════════════════════════════════
    // Default block validation (R9.1–9.3, R6.9, R11.1, R11.2, R13.3, R13.4, R13.10)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Walks all type symbols with explicit default blocks and validates:
    /// <list type="number">
    /// <item><description>Each field override references a field that exists in the type's member scope (including inherited fields).</description></item>
    /// <item><description>Visibility: base type fields must be public, not private → <c>MIST_DEFAULT_FIELD_NOT_VISIBLE</c>.</description></item>
    /// <item><description>Type bindings: concrete type must be a defaultable <see cref="ClassType"/> or <see cref="StructType"/>.</description></item>
    /// <item><description>Compile-time constant expressions (v1: only literal expressions) → <c>MIST_DEFAULT_NOT_CONSTANT</c>.</description></item>
    /// <item><description>Redundancy: if explicit default block produces same values as inferred default → <c>MIST_DEFAULT_REDUNDANT</c> with <c>Info</c> severity.</description></item>
    /// </list>
    /// </summary>
    private static void ValidateDefaultBlocks(ISemanticModel model, DiagnosticBag diagnostics)
    {
        foreach (var symbol in model.AllSymbols)
        {
            if (symbol.Origin == SymbolOrigin.Backend)
                continue;

            if (symbol.Kind is not (SymbolKind.Class or SymbolKind.Struct))
                continue;

            var type = symbol.DeclaredType;

            // Extract defaultability status and default block node.
            DefaultabilityStatus defaultability;
            DefaultBlockDeclarationNode? defaultBlockNode;
            IScope? memberScope;
            ClassType? baseClass;

            switch (type)
            {
                case ClassType ct:
                    defaultability = ct.Defaultability;
                    defaultBlockNode = ct.DefaultBlockNode;
                    memberScope = ct.MemberScope;
                    baseClass = ct.BaseType as ClassType;
                    break;
                case StructType st:
                    defaultability = st.Defaultability;
                    defaultBlockNode = st.DefaultBlockNode;
                    memberScope = st.MemberScope;
                    baseClass = null; // Structs don't have base types.
                    break;
                default:
                    continue;
            }

            // Only validate types with explicit default blocks.
            if (defaultability != DefaultabilityStatus.Explicit || defaultBlockNode is null)
                continue;

            // Collect the set of field names declared directly on this type (not inherited).
            var ownFieldNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var member in memberScope.Members)
            {
                if (member.Kind == SymbolKind.Field)
                    ownFieldNames.Add(member.Name);
            }

            var hasNonRedundantEntry = false;

            foreach (var entry in defaultBlockNode.Entries)
            {
                switch (entry)
                {
                    case DefaultFieldOverrideDefaultFieldInitializerNode fieldOverride:
                    {
                        var fieldName = fieldOverride.FieldName.Lexeme;

                        // 1. Check field exists in the type's member scope (including inherited).
                        var fieldSymbol = memberScope.Lookup(fieldName);
                        if (fieldSymbol is null || fieldSymbol.Kind != SymbolKind.Field)
                        {
                            // Field not found — the semantic model builder already handles
                            // this during default value computation, but we still mark as
                            // non-redundant to avoid a false redundancy diagnostic.
                            hasNonRedundantEntry = true;
                            break;
                        }

                        // 2. Validate visibility: if the field is from a base type, it must be public.
                        if (!ownFieldNames.Contains(fieldName))
                        {
                            // Field is inherited — check visibility.
                            if (!IsMemberPublicViaModel(fieldSymbol, model))
                            {
                                diagnostics.Add(MakeDiagnostic(
                                    DiagnosticId.DefaultFieldNotVisible.Value,
                                    $"Field '{fieldName}' is not accessible from the default block of '{type!.DisplayName}' because it is not public in the base type",
                                    fieldOverride,
                                    model));
                            }
                        }

                        // 3. Validate compile-time constant expression (v1: only literals).
                        if (!IsLiteralExpression(fieldOverride.Value))
                        {
                            diagnostics.Add(MakeDiagnostic(
                                DiagnosticId.DefaultNotConstant.Value,
                                $"Default value for field '{fieldName}' must be a compile-time constant expression",
                                fieldOverride.Value,
                                model));
                        }

                        // Track whether this override differs from the inferred default.
                        hasNonRedundantEntry = hasNonRedundantEntry || IsOverrideNonRedundant(fieldName, fieldOverride.Value, fieldSymbol, model);
                        break;
                    }

                    case DefaultTypeBindingDefaultFieldInitializerNode typeBinding:
                    {
                        var fieldName = typeBinding.FieldName.Lexeme;

                        // 1. Check field exists.
                        var fieldSymbol = memberScope.Lookup(fieldName);
                        if (fieldSymbol is null || fieldSymbol.Kind != SymbolKind.Field)
                        {
                            hasNonRedundantEntry = true;
                            break;
                        }

                        // 2. Validate visibility for inherited fields.
                        if (!ownFieldNames.Contains(fieldName))
                        {
                            if (!IsMemberPublicViaModel(fieldSymbol, model))
                            {
                                diagnostics.Add(MakeDiagnostic(
                                    DiagnosticId.DefaultFieldNotVisible.Value,
                                    $"Field '{fieldName}' is not accessible from the default block of '{type!.DisplayName}' because it is not public in the base type",
                                    typeBinding,
                                    model));
                            }
                        }

                        // 3. Validate the concrete type is a defaultable ClassType or StructType.
                        var concreteType = ResolveTypeBindingConcreteType(typeBinding, memberScope);
                        if (concreteType is not null)
                        {
                            switch (concreteType)
                            {
                                case ClassType ct when ct.Defaultability == DefaultabilityStatus.NotDefaultable:
                                    diagnostics.Add(MakeDiagnostic(
                                        DiagnosticId.DefaultNotSupported.Value,
                                        $"Type '{ct.DisplayName}' used as a type binding for field '{fieldName}' is not defaultable",
                                        typeBinding,
                                        model));
                                    break;
                                case StructType st when st.Defaultability == DefaultabilityStatus.NotDefaultable:
                                    diagnostics.Add(MakeDiagnostic(
                                        DiagnosticId.DefaultNotSupported.Value,
                                        $"Type '{st.DisplayName}' used as a type binding for field '{fieldName}' is not defaultable",
                                        typeBinding,
                                        model));
                                    break;
                                case not ClassType and not StructType:
                                    diagnostics.Add(MakeDiagnostic(
                                        DiagnosticId.DefaultNotSupported.Value,
                                        $"Type binding for field '{fieldName}' must reference a class or struct type, not '{concreteType.DisplayName}'",
                                        typeBinding,
                                        model));
                                    break;
                            }
                        }

                        // 4. Validate nested override expressions are compile-time constants.
                        foreach (var nested in typeBinding.NestedEntries)
                        {
                            if (!IsLiteralExpression(nested.Value))
                            {
                                diagnostics.Add(MakeDiagnostic(
                                    DiagnosticId.DefaultNotConstant.Value,
                                    $"Default value for nested field '{nested.FieldName.Lexeme}' must be a compile-time constant expression",
                                    nested.Value,
                                    model));
                            }
                        }

                        // Type bindings are always non-redundant (inferred defaults
                        // cannot produce type bindings).
                        hasNonRedundantEntry = true;
                        break;
                    }
                }
            }

            // 5. Redundancy check: if the explicit default block has no entries,
            //    or all field overrides match the inferred default, emit Info diagnostic.
            if (!hasNonRedundantEntry)
            {
                diagnostics.Add(MakeDiagnosticWithSeverity(
                    DiagnosticId.DefaultRedundantBlock.Value,
                    $"The explicit default block on type '{type!.DisplayName}' is redundant and can be removed because it produces the same values as the inferred default",
                    defaultBlockNode,
                    model,
                    DiagnosticSeverity.Info));
            }
        }
    }

    /// <summary>
    /// Checks whether a member symbol is public by examining its declaring AST node's modifiers
    /// via the semantic model.
    /// </summary>
    private static bool IsMemberPublicViaModel(ISymbol member, ISemanticModel model)
    {
        SyntaxNode declaringNode;
        try { declaringNode = model.GetDeclaringNode(member); }
        catch { return false; }

        var modifiers = GetMemberModifiers(declaringNode);
        return modifiers.IsPublic;
    }

    /// <summary>
    /// Checks whether an expression is a literal expression (compile-time constant in v1).
    /// Walks down the expression chain to the innermost <see cref="PrimaryNode"/> and
    /// checks if it is a recognized literal node type.
    /// </summary>
    private static bool IsLiteralExpression(ExpressionNode expression)
    {
        var primary = UnwrapExpressionToPrimary(expression);
        if (primary is null)
            return false;

        return primary is IntegerLiteralExpressionPrimaryNode
            or HexLiteralExpressionPrimaryNode
            or BinaryLiteralExpressionPrimaryNode
            or FloatLiteralExpressionPrimaryNode
            or StringLiteralExpressionPrimaryNode
            or CharLiteralExpressionPrimaryNode
            or TrueLiteralExpressionPrimaryNode
            or FalseLiteralExpressionPrimaryNode;
    }

    /// <summary>
    /// Unwraps an expression node down to its innermost <see cref="PrimaryNode"/>.
    /// Returns <c>null</c> if the expression structure is not a simple primary.
    /// </summary>
    private static PrimaryNode? UnwrapExpressionToPrimary(ExpressionNode expression)
    {
        Storm.Runtime.SyntaxNode current = expression;

        for (var depth = 0; depth < 20; depth++)
        {
            if (current is PrimaryNode primary)
                return primary;

            // Look for a single non-token child to follow.
            // Skip null children that may be yielded by generated AST nodes
            // whose optional properties are typed as non-nullable.
            Storm.Runtime.SyntaxNode? next = null;
            var childCount = 0;
            foreach (var child in current.Children())
            {
                if (child is null)
                    continue;

                childCount++;
                if (child is PrimaryNode childPrimary)
                    return childPrimary;

                if (next is null)
                    next = child;
            }

            if (childCount > 1 || next is null)
                return null;

            current = next;
        }

        return null;
    }

    /// <summary>
    /// Determines whether a field override in a default block is non-redundant —
    /// i.e., it produces a different value than the inferred default would.
    /// </summary>
    private static bool IsOverrideNonRedundant(
        string fieldName,
        ExpressionNode overrideExpr,
        ISymbol fieldSymbol,
        ISemanticModel model)
    {
        // Extract the override's constant value.
        var overrideValue = TryExtractLiteralConstantValue(overrideExpr);

        // Get the field's declaring node to check for a field initializer.
        SyntaxNode fieldDeclNode;
        try { fieldDeclNode = model.GetDeclaringNode(fieldSymbol); }
        catch { return true; } // Can't determine — assume non-redundant.

        if (fieldDeclNode is FieldDeclarationNode fieldDecl && fieldDecl.Initializer is ExpressionNode initExpr)
        {
            // Field has an initializer — compare override value with initializer value.
            var initValue = TryExtractLiteralConstantValue(initExpr);
            return !Equals(overrideValue, initValue);
        }

        // Field has no initializer — compare override value with zero-init value.
        var fieldType = fieldSymbol.DeclaredType;
        if (fieldType is null)
            return true;

        var zeroValue = GetZeroInitValue(fieldType);
        return !Equals(overrideValue, zeroValue);
    }

    /// <summary>
    /// Attempts to extract a compile-time constant value from a literal expression.
    /// Returns <c>null</c> if the expression is not a recognized literal.
    /// </summary>
    private static object? TryExtractLiteralConstantValue(ExpressionNode expression)
    {
        var primary = UnwrapExpressionToPrimary(expression);
        if (primary is null)
            return null;

        return primary switch
        {
            IntegerLiteralExpressionPrimaryNode intLit => TryParseLiteralInt(intLit.IntegerLiteral.Lexeme),
            HexLiteralExpressionPrimaryNode hexLit => TryParseLiteralInt(hexLit.HexLiteral.Lexeme),
            BinaryLiteralExpressionPrimaryNode binLit => TryParseLiteralInt(binLit.BinaryLiteral.Lexeme),
            FloatLiteralExpressionPrimaryNode floatLit => TryParseLiteralFloat(floatLit.FloatLiteral.Lexeme),
            StringLiteralExpressionPrimaryNode stringLit => ExtractStringContent(stringLit.StringLiteral.Lexeme),
            CharLiteralExpressionPrimaryNode charLit => ExtractCharContent(charLit.CharLiteral.Lexeme),
            TrueLiteralExpressionPrimaryNode => true,
            FalseLiteralExpressionPrimaryNode => false,
            _ => null,
        };
    }

    /// <summary>
    /// Parses an integer literal string into a numeric value.
    /// </summary>
    private static object? TryParseLiteralInt(string lexeme)
    {
        var cleaned = lexeme.Replace("_", "");
        var upper = cleaned.ToUpperInvariant();

        if (upper.StartsWith("0X"))
            cleaned = cleaned[2..];
        else if (upper.StartsWith("0B"))
            cleaned = cleaned[2..];

        // Remove suffix for comparison.
        if (upper.EndsWith("UL") || upper.EndsWith("LU"))
        {
            if (ulong.TryParse(cleaned[..^2], out var val)) return val;
        }
        else if (upper.EndsWith("L"))
        {
            if (long.TryParse(cleaned[..^1], out var val)) return val;
        }
        else if (upper.EndsWith("U"))
        {
            if (uint.TryParse(cleaned[..^1], out var val)) return val;
        }
        else
        {
            if (int.TryParse(cleaned, out var val)) return val;
            if (long.TryParse(cleaned, out var lval)) return lval;
        }

        return null;
    }

    /// <summary>
    /// Parses a float literal string into a numeric value.
    /// </summary>
    private static object? TryParseLiteralFloat(string lexeme)
    {
        var cleaned = lexeme.Replace("_", "");
        var upper = cleaned.ToUpperInvariant();

        if (upper.EndsWith("F"))
        {
            if (float.TryParse(cleaned[..^1], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var val))
                return val;
        }
        else if (upper.EndsWith("M"))
        {
            if (decimal.TryParse(cleaned[..^1], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var val))
                return val;
        }
        else
        {
            if (double.TryParse(cleaned, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var val))
                return val;
        }

        return null;
    }

    /// <summary>
    /// Extracts the string content from a string literal lexeme (removes surrounding quotes).
    /// </summary>
    private static string ExtractStringContent(string lexeme)
    {
        if (lexeme.Length >= 2 && lexeme[0] == '"' && lexeme[^1] == '"')
            return lexeme[1..^1];
        return lexeme;
    }

    /// <summary>
    /// Extracts the char content from a char literal lexeme (removes surrounding quotes).
    /// </summary>
    private static object ExtractCharContent(string lexeme)
    {
        if (lexeme.Length >= 3 && lexeme[0] == '\'' && lexeme[^1] == '\'')
        {
            var inner = lexeme[1..^1];
            if (inner.Length == 1)
                return inner[0];
        }
        return '\0';
    }

    /// <summary>
    /// Returns the zero-initialization value for a type, used for redundancy comparison.
    /// </summary>
    private static object? GetZeroInitValue(IType type)
    {
        var name = type.DisplayName;
        return name switch
        {
            "int" or "Int32" => 0,
            "uint" or "UInt32" => 0u,
            "long" or "Int64" => 0L,
            "ulong" or "UInt64" => 0UL,
            "short" or "Int16" => (short)0,
            "ushort" or "UInt16" => (ushort)0,
            "byte" or "Byte" => (byte)0,
            "sbyte" or "SByte" => (sbyte)0,
            "float" or "Single" => 0.0f,
            "double" or "Double" => 0.0,
            "decimal" or "Decimal" => 0.0m,
            "bool" or "Boolean" => false,
            "char" or "Char" => '\0',
            "string" or "String" => "",
            _ => null,
        };
    }

    /// <summary>
    /// Resolves the concrete type referenced by a type binding entry in a default block.
    /// Searches the member scope's parent scopes for a matching type symbol.
    /// </summary>
    private static IType? ResolveTypeBindingConcreteType(
        DefaultTypeBindingDefaultFieldInitializerNode typeBinding,
        IScope memberScope)
    {
        var typeName = ExtractTypeBindingName(typeBinding.TypeName);
        if (string.IsNullOrEmpty(typeName))
            return null;

        // Search up the scope chain for a type with the given name.
        var scope = memberScope.Parent;
        while (scope is not null)
        {
            foreach (var member in scope.Members)
            {
                if (member.Kind is SymbolKind.Class or SymbolKind.Struct
                    && member.Name == typeName)
                {
                    return member.DeclaredType;
                }
            }
            scope = scope.Parent;
        }

        return null;
    }

    /// <summary>
    /// Extracts the type name from a <see cref="TypeReferenceNode"/>.
    /// </summary>
    private static string? ExtractTypeBindingName(TypeReferenceNode typeRef)
    {
        return typeRef switch
        {
            NamedTypeRefTypeReferenceNode named => named.Name.IDENTIFIER.Lexeme,
            _ => null,
        };
    }

    /// <summary>
    /// Creates a diagnostic with a specified severity level.
    /// Used for non-error diagnostics such as the <c>Info</c>-level redundant default block diagnostic.
    /// </summary>
    private static Diagnostic MakeDiagnosticWithSeverity(
        string id, string message, SyntaxNode node, ISemanticModel model, DiagnosticSeverity severity)
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
            new DiagnosticId(id),
            severity,
            PipelineStage.Analysis,
            message,
            Location: location,
            CompilationUnitPath: compilationUnitPath,
            AnalyzerName: "TypeChecker");
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
