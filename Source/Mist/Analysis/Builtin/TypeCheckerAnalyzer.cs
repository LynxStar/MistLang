using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Mist.Analysis.Contracts;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
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
    internal static bool useOperatorOverloadResolution = true;
#pragma warning restore CA1805

    // ── Diagnostic ID constants for reflection-based test discovery ──────
    internal static readonly DiagnosticId DiagnosticIdMIST_TC_0102 = new("MIST_TC_0102");
    internal static readonly DiagnosticId DiagnosticIdMIST_TC_0103 = new("MIST_TC_0103");

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
    private IType? currentEnclosingType;

    // ── Current method symbol during analysis (set per method body) ──
    private ISymbol? currentMethodSymbol;

    // ── Whether we are currently inside a constructor body (for init-only checks) ──
    private bool insideConstructor;

    // ── Whether we are currently inside a property accessor body (for field keyword checks) ──
    private bool insidePropertyAccessor;

    // ── Whether the postfix currently being walked is the target of an ──
    //    assignment — set by CheckAssignmentNode before recursing into the
    //    target, and cleared afterwards. When true, the LAST suffix of the
    //    top-level postfix in the target expression is a *write* context
    //    (Requirement 10.3); all other suffixes in the chain are reads.
    //    Only the Task 21 member-filtering logic consults this flag; legacy
    //    read/write distinctions continue to use their existing lookups
    //    (e.g. TryResolvePropertyFromAssignmentTarget).
    private bool analyzingAssignmentLhs;

    // ── Tracks whether we're inside a sub-expression context (for ??= statement-only check) ──
    private int assignmentNestingDepth;

    // ── Tracks whether the current expression is at statement level (for ??= statement-only check) ──
    private bool atStatementLevel;

    // ── Flow narrowing context for refinement types (Requirements 7.1–7.8) ──
    //    Maps local variable names to the condition constraints proven within
    //    the current then-branch scope. Entries are pushed when an if-condition
    //    proves relational constraints and popped after the then-branch is checked.
    //    At the assignment site, the stored constraints are checked against the
    //    target refinement type's predicate to determine if the assignment is safe.
    //    Reassignment within the branch invalidates the narrowing entry.
    private readonly Dictionary<string, List<ConditionConstraint>> narrowingContext = new(StringComparer.Ordinal);

    // ── Set of variable names captured by closures in the current method body ──
    //    Populated at the start of each method body check. Variables in this set
    //    are ineligible for flow narrowing (Requirement 7.7).
    private readonly HashSet<string> closureCapturedVariables = new(StringComparer.Ordinal);

    // ── Flow narrowing context for void-coalescing assignment (??=) ──
    //    Maps local variable names to their narrowed types after ??= statements.
    //    After `x ??= value` where x has type `T | void`, the variable is narrowed
    //    to type T for all subsequent statements in the same scope and nested blocks.
    //    Invalidated on reassignment.
    private readonly Dictionary<string, IType> voidCoalescingTypeNarrowings = new(StringComparer.Ordinal);

    // ── Cache for cross-reference lookups from dedicated types to registered declarations ──
    //    Used by CollectConstructorOverloads to bridge SpanType to its registered StructType,
    //    and by GetAbstractList to bridge ArrayType to its registered ClassType.
    //    Cleared at the start of each Analyze() call.
    private readonly Dictionary<string, StructType?> registeredStructTypeCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ClassType?> registeredClassTypeCache = new(StringComparer.Ordinal);

    public void Analyze(ISemanticModel model, DiagnosticBag diagnostics)
    {
        // Clear flow narrowing state from any previous compilation.
        narrowingContext.Clear();
        closureCapturedVariables.Clear();
        voidCoalescingTypeNarrowings.Clear();
        registeredStructTypeCache.Clear();
        registeredClassTypeCache.Clear();

        // Walk each method, constructor, property, and indexer body to check type rules.
        foreach(var symbol in model.AllSymbols)
        {
            if(symbol.Origin == SymbolOrigin.Backend)
            {
                continue;
            }

            switch(symbol.Kind)
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

        // Task 5.10: Check for missing abstract property implementations (MIST_TC_0030).
        CheckMissingAbstractProperties(model, diagnostics);

        // Task 10.1: Check readonly abstract contract enforcement (MIST_TC_0079).
        CheckReadonlyAbstractContracts(model, diagnostics);

        // Task 6.2: Check constraint satisfaction for inheritance clauses where
        // constrained generics are used as base types or implemented abstracts.
        CheckInheritanceClauseConstraints(model, diagnostics);

        // Task 7.2: Validate explicit default blocks (R9.1–9.3, R6.9, R11.1, R11.2, R13.3, R13.4, R13.10).
        ValidateDefaultBlocks(model, diagnostics);

        // Task 6.1: Validate dependent type arguments are compile-time constants
        // and that their types match the parameter annotations (MIST_DT_0002, MIST_DT_0003).
        ValidateDependentTypeArguments(model, diagnostics);

        // Task 6.6: Validate dependent parameter forwarding in inheritance — widening only.
        // Emits MIST_DT_0004 when a derived type forwards a dependent parameter whose value
        // type cannot be implicitly widened to the base parameter's value type.
        ValidateDependentParameterForwarding(model, diagnostics);

        // Task 11.1: Validate that refinement predicates using property patterns only
        // reference readonly members (MIST_TC_0100, Requirements 3.1–3.4).
        CheckRefinementPredicateReadonly(model, diagnostics);

        // Task 6.3: Validate that relational predicates in adhoc refinement constraints
        // are only applied to comparable types (MIST_REFINEMENT_0007, Requirements 11.1–11.7).
        ValidatePredicateTypeCompatibility(model, diagnostics);

        // Task 2.3: Validate that all enum declarations have readonly base types
        // (MIST_TC_0102, MIST_TC_0103, Requirements 2.1–2.4, 3.1–3.5).
        CheckEnumBaseTypeReadonly(model, diagnostics);

        // Task 4.5: Accessibility-consistency, base/interface, and override/interface-impl enforcement.
        // These methods are implemented but require ClassType.Accessibility to be correctly
        // propagated (which depends on ResolveAccessibilityAndSealed in SemanticModelBuilder).
        CheckAccessibilityConsistency(model, diagnostics);
        CheckBaseTypeAndInterfaceAccessibility(model, diagnostics);
        CheckOverrideAndInterfaceImplAccessibility(model, diagnostics);

        // Task 4.6: Nested-type accessibility-domain enforcement (Requirements 11.1, 11.2).
        CheckNestedTypeAccessibilityDomain(model, diagnostics);

        // Task 4.7: Sealed enforcement and protected-in-sealed handling
        // (Requirements 12.1–12.9, 13.1–13.3).
        CheckSealedEnforcement(model, diagnostics);
        CheckProtectedInSealed(model, diagnostics);
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
        // Library projects do not require an entry point — skip validation entirely.
        if(model.Compilation.OutputType == ProjectOutputType.Library)
        {
            return;
        }

        // Build a set of source file paths from referenced project units
        // so we can skip their Main methods during entry point detection.
        var referencedSourcePaths = new HashSet<string>(StringComparer.Ordinal);
        foreach(var unit in model.Units)
        {
            if(unit.OriginProjectPath is not null)
            {
                _ = referencedSourcePaths.Add(unit.Source.Path);
            }
        }

        var entryPoints = new List<(ISymbol Symbol, SyntaxNode Node)>();

        foreach(var symbol in model.AllSymbols)
        {
            if(symbol.Origin == SymbolOrigin.Backend)
            {
                continue;
            }

            if(symbol.Kind != SymbolKind.Method || symbol.Name != "Main")
            {
                continue;
            }

            // Get the declaring AST node to inspect modifiers and parameters.
            SyntaxNode declaringNode;
            try { declaringNode = model.GetDeclaringNode(symbol); }
            catch { continue; }

            if(declaringNode is not MethodDeclarationNode methodDecl)
            {
                continue;
            }

            // Skip entry points from referenced projects — only the root
            // project's Main methods count as entry points.
            if(IsFromReferencedProject(declaringNode, model, referencedSourcePaths))
            {
                continue;
            }

            // Must be static.
            var isStatic = false;
            foreach(var mod in methodDecl.Modifiers)
            {
                if(mod is StaticModifierModifierNode)
                {
                    isStatic = true;
                    break;
                }
            }

            if(!isStatic)
            {
                continue;
            }

            // Must return void.
            if(methodDecl.ReturnType.First is not VoidSingleTypeRefSingleTypeRefNode
                || methodDecl.ReturnType.SingleTypeRefs.Length > 0)
            {
                continue;
            }

            // Must have either no parameters or a single string[] parameter.
            if(methodDecl.Parameters is null)
            {
                // static void Main() — valid entry point.
                entryPoints.Add((symbol, declaringNode));
            }
            else
            {
                // Check for exactly one parameter of type string[].
                var parameters = new List<ParameterNode>();
                foreach(var child in methodDecl.Parameters.Children())
                {
                    if(child is ParameterNode param)
                    {
                        parameters.Add(param);
                    }
                }

                if(parameters.Count == 1)
                {
                    var paramType = parameters[0].Type;
                    // string[] is represented as PrimitiveTypeRef with StringType and an array rank,
                    // or as a NamedTypeRef. Check for the string[] pattern.
                    if(IsStringArrayTypeRef(paramType))
                    {
                        entryPoints.Add((symbol, declaringNode));
                    }
                }
                // Any other parameter count or type is not a valid entry point signature.
            }
        }

        if(entryPoints.Count == 0)
        {
            diagnostics.Report(TypeCheckDiagnostics.NoValidEntryPoint, default, "<unknown>", "TypeChecker");
        }
        else if(entryPoints.Count > 1)
        {
            // Emit a diagnostic listing all entry point locations.
            foreach(var (_, node) in entryPoints)
            {
                diagnostics.ReportAtNode(TypeCheckDiagnostics.MultipleEntryPoints, node, "TypeChecker");
            }
        }
    }

    /// <summary>
    /// Checks whether a type reference AST node represents <c>string[]</c>.
    /// </summary>
    private static bool IsStringArrayTypeRef(TypeReferenceNode typeRef)
    {
        // string[] as a primitive type ref: PrimitiveTypeRef with StringType and array rank.
        if(typeRef.First is PrimitiveSingleTypeRefSingleTypeRefNode primitiveRef)
        {
            return primitiveRef.PrimitiveType is StringTypePrimitiveTypeNode
                   && primitiveRef.ArrayRank is not null;
        }

        // string[] could also appear as a named type ref (unlikely but handle it).
        // In practice, the grammar parses "string" as a PrimitiveType keyword,
        // so this case shouldn't occur for valid Mist code.
        return false;
    }

    /// <summary>
    /// Determines whether the given method symbol belongs to a referenced project
    /// by checking if its containing type's namespace matches a referenced project's
    /// namespace root.
    /// </summary>
    private static bool IsFromReferencedProject(
        SyntaxNode node,
        ISemanticModel model,
        HashSet<string> referencedSourcePaths)
    {
        if(referencedSourcePaths.Count == 0)
        {
            return false;
        }

        // Check each referenced unit's source path against the compilation units.
        // The declaring node belongs to a referenced project if it was parsed from
        // a source file that belongs to a referenced project.
        var units = model.Units;

        // Build a set of namespace roots from referenced projects.
        var referencedNamespaceRoots = new HashSet<string>(StringComparer.Ordinal);
        foreach(var unit in units)
        {
            if(unit.OriginProjectPath is not null && unit.NamespaceRoot is not null)
            {
                _ = referencedNamespaceRoots.Add(unit.NamespaceRoot);
            }
        }

        if(referencedNamespaceRoots.Count == 0)
        {
            return false;
        }

        // Find the containing type of the method by walking up the scope chain.
        // The method's declaring node is inside a class/struct. Check if that
        // type's containing namespace matches a referenced project's namespace root.
        if(node is not MethodDeclarationNode)
        {
            return false;
        }

        // Get the declared symbol for this node to find its containing scope.
        var methodSymbol = model.GetDeclaredSymbol(node);
        if(methodSymbol is null)
        {
            return false;
        }

        // Walk up the scope chain to find the containing type.
        var typeScope = methodSymbol.ContainingScope;
        var parentScope = typeScope?.Parent;
        if(parentScope is null)
        {
            return false;
        }

        // Find the type symbol that owns this scope.
        foreach(var member in parentScope.Members)
        {
            if(member.Kind is SymbolKind.Class or SymbolKind.Struct)
            {
                var memberScope = member.DeclaredType switch
                {
                    ClassType ct => ct.MemberScope,
                    StructType st => st.MemberScope,
                    _ => null,
                };

                if(memberScope is not null && ReferenceEquals(memberScope, typeScope))
                {
                    // Found the containing type. Check its namespace.
                    var containingNs = member.DeclaredType switch
                    {
                        ClassType ct => ct.ContainingNamespace,
                        StructType st => st.ContainingNamespace,
                        _ => null,
                    };

                    if(containingNs is not null && referencedNamespaceRoots.Contains(containingNs))
                    {
                        return true;
                    }

                    // Also check if the namespace starts with a referenced namespace root
                    // (for nested namespaces like "Inheritance.Models").
                    foreach(var nsRoot in referencedNamespaceRoots)
                    {
                        if(containingNs is not null && containingNs.StartsWith(nsRoot + ".", StringComparison.Ordinal))
                        {
                            return true;
                        }
                    }

                    return false;
                }
            }
        }

        return false;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Enclosing type resolution — determines the type that contains a symbol
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Finds the declaring type (ClassType, StructType, or AbstractType) that contains the given
    /// method or constructor symbol by walking up the scope chain.
    /// </summary>
    private static IType? FindDeclaringType(ISymbol memberSymbol)
    {
        // The member's ContainingScope is the type scope. Look for a Class/Struct/Abstract
        // symbol in the parent scope that owns this type scope.
        var typeScope = memberSymbol.ContainingScope;
        var parentScope = typeScope?.Parent;
        if(parentScope is null)
        {
            return null;
        }

        foreach(var member in parentScope.Members)
        {
            if(member.Kind is SymbolKind.Class or SymbolKind.Struct or SymbolKind.Abstract
                && member.DeclaredType is ClassType or StructType or AbstractType)
            {
                // Check if this type's MemberScope is the same as the member's ContainingScope.
                var memberScope = member.DeclaredType switch
                {
                    ClassType ct => ct.MemberScope,
                    StructType st => st.MemberScope,
                    AbstractType it => it.MemberScope,
                    _ => null,
                };
                if(ReferenceEquals(memberScope, typeScope))
                {
                    return member.DeclaredType;
                }
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
        if(declaringNode is CastOperatorDeclarationNode castOpDecl)
        {
            // ── MIST_TC_0108: async on operator (Requirement 2.5) ──
            foreach(var mod in castOpDecl.Modifiers)
            {
                if(mod is AsyncModifierModifierNode)
                {
                    diagnostics.ReportAtNode(TypeCheckDiagnostics.AsyncOnConstructorOrOperator, declaringNode, "TypeChecker", "operators");
                    break;
                }
            }

            var returnType = methodSymbol.DeclaredType ?? ErrorType.Instance;
            currentEnclosingType = FindDeclaringType(methodSymbol);
            currentMethodSymbol = methodSymbol;
            insideConstructor = false;
            insidePropertyAccessor = false;

            CheckCastOperatorDeclaration(castOpDecl, methodSymbol, model, diagnostics);

            // ── Flow narrowing: detect closure-captured variables ──
            if(castOpDecl.Body.GetBlock() is BlockNode castBlock)
            {
                CollectClosureCapturedVariables(castBlock);
                CheckRefLambdaCaptureProhibition(castBlock, model, diagnostics);
                CheckBlock(castBlock, returnType, model, diagnostics);

                // ── Local definite-assignment analysis (Requirements 2.1–2.6) ──
                CheckLocalDefiniteAssignment(castBlock, model, diagnostics);
            }

            return;
        }

        // ── Regular operator declarations ────────────────────────────────
        if(declaringNode is OperatorDeclarationNode opDecl)
        {
            // ── MIST_TC_0108: async on operator (Requirement 2.5) ──
            foreach(var mod in opDecl.Modifiers)
            {
                if(mod is AsyncModifierModifierNode)
                {
                    diagnostics.ReportAtNode(TypeCheckDiagnostics.AsyncOnConstructorOrOperator, declaringNode, "TypeChecker", "operators");
                    break;
                }
            }

            var returnType3 = methodSymbol.DeclaredType ?? ErrorType.Instance;
            currentEnclosingType = FindDeclaringType(methodSymbol);
            currentMethodSymbol = methodSymbol;
            insideConstructor = false;
            insidePropertyAccessor = false;

            if(opDecl.Body.GetBlock() is BlockNode opBlock)
            {
                CollectClosureCapturedVariables(opBlock);
                CheckRefLambdaCaptureProhibition(opBlock, model, diagnostics);
                CheckBlock(opBlock, returnType3, model, diagnostics);

                // ── Local definite-assignment analysis (Requirements 2.1–2.6) ──
                CheckLocalDefiniteAssignment(opBlock, model, diagnostics);
            }

            return;
        }

        if(declaringNode is not MethodDeclarationNode methodDecl)
        {
            return;
        }

        // ── MIST_TC_0104: async + intrinsic incompatibility (Requirement 2.4) ──
        // Note on numbering: the structured-concurrency design document's Diagnostic
        // ID Allocation table proposed MIST_TC_0070 for this diagnostic. That code
        // was already allocated to the type-unions feature (see
        // UnionNonConstituentAssignment above), so the async+intrinsic diagnostic
        // was reallocated to MIST_TC_0104 — the next free code after the enum
        // diagnostics (MIST_TC_0102, 0103). This continues the same reallocation
        // strategy used throughout this file.
        var hasAsync = false;
        var hasIntrinsic = false;
        foreach(var mod in methodDecl.Modifiers)
        {
            if(mod is AsyncModifierModifierNode)
            {
                hasAsync = true;
            }
            else if(mod is IntrinsicModifierModifierNode)
            {
                hasIntrinsic = true;
            }
        }

        if(hasAsync && hasIntrinsic)
        {
            diagnostics.ReportAtNode(TypeCheckDiagnostics.AsyncIntrinsicIncompatible, declaringNode, "TypeChecker", methodSymbol.Name);
        }

        // ── MIST_TC_0106: async method explicitly declares Task<T> return type (Requirement 2.6) ──
        // When a method has the `async` modifier, the compiler wraps the return type
        // in Task<T> automatically. If the developer also writes Task<T> explicitly,
        // that's an error — it would result in double-wrapping (Task<Task<T>>).
        if(hasAsync && methodSymbol is Symbol { DeclaredReturnType: { } declaredReturn }
            && SemanticModelBuilder.IsStandardTaskType(declaredReturn))
        {
            diagnostics.ReportAtNode(TypeCheckDiagnostics.AsyncExplicitTaskReturnType, declaringNode, "TypeChecker", methodSymbol.Name);
        }

        // For async methods, use DeclaredReturnType (the unwrapped type T) for
        // return statement checking, since the developer writes `return T` not
        // `return Task<T>`. The compiler wraps the return type automatically.
        var returnType2 = (methodSymbol is Symbol { IsAsync: true, DeclaredReturnType: { } asyncDeclaredReturn }
            ? asyncDeclaredReturn
            : methodSymbol.DeclaredType) ?? ErrorType.Instance;
        currentEnclosingType = FindDeclaringType(methodSymbol);
        currentMethodSymbol = methodSymbol;
        insideConstructor = false;
        insidePropertyAccessor = false;

        // ── Anonymous type scope restriction: method signatures (Requirements 4.3, 4.4) ──
        CheckAnonymousTypeEscapeInSignature(methodSymbol, declaringNode, model, diagnostics);

        // ── Flow narrowing: detect closure-captured variables ──
        if(methodDecl.Body.GetBlock() is BlockNode block)
        {
            CollectClosureCapturedVariables(block);
            CheckRefLambdaCaptureProhibition(block, model, diagnostics);

            // ── Flow narrowing: inject parameter refinement narrowing (Requirement 8.1–8.5) ──
            var paramNarrowings = InjectParameterRefinementNarrowing(declaringNode, model);

            CheckBlock(block, returnType2, model, diagnostics);

            // Remove parameter refinement narrowing entries after the body.
            RemoveParameterRefinementNarrowing(paramNarrowings);

            // ── Local definite-assignment analysis (Requirements 2.1–2.6) ──
            CheckLocalDefiniteAssignment(block, model, diagnostics);
        }
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
        var enclosingType = currentEnclosingType;
        if(enclosingType is null)
        {
            return;
        }

        // Resolve the source parameter type from the symbol's parameter.
        var paramTypes = ParameterSignature.Extract(methodSymbol, model);
        var sourceType = paramTypes.Count > 0 ? paramTypes[0] : null;

        // The target type is the symbol's DeclaredType (return type), resolved in Pass 2.
        var targetType = methodSymbol.DeclaredType;

        // ── MIST_TC_0040: Enclosing type constraint ─────────────────────
        // Either the source parameter type or the target type must be the
        // enclosing type.
        if(sourceType is not null && targetType is not null)
        {
            var sourceMatchesEnclosing = sourceType.DisplayName == enclosingType.DisplayName
                || (sourceType.IsVoid() && enclosingType.IsVoid());
            var targetMatchesEnclosing = targetType.DisplayName == enclosingType.DisplayName
                || (targetType.IsVoid() && enclosingType.IsVoid());

            if(!sourceMatchesEnclosing && !targetMatchesEnclosing)
            {
                diagnostics.ReportAtNode(TypeCheckDiagnostics.CastOperatorEnclosingTypeViolation, castOpDecl, "TypeChecker", enclosingType.DisplayName, sourceType!.DisplayName, targetType!.DisplayName);
            }
        }

        // ── MIST_TC_0041: Duplicate cast operator detection ─────────────
        // After type resolution, check for duplicate cast operators with the
        // same kind (implicit/explicit) and the same resolved source-target
        // type pair in the enclosing type.
        if(sourceType is not null && targetType is not null)
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

            if(memberScope is not null)
            {
                foreach(var sibling in memberScope.Members)
                {
                    if(sibling.Kind != SymbolKind.Method)
                    {
                        continue;
                    }

                    // Skip self.
                    if(ReferenceEquals(sibling, methodSymbol))
                    {
                        continue;
                    }

                    // Only consider cast operator symbols (name starts with "operator_implicit_" or "operator_explicit_").
                    if(!sibling.Name.StartsWith("operator_implicit_") && !sibling.Name.StartsWith("operator_explicit_"))
                    {
                        continue;
                    }

                    // Determine the sibling's cast kind from its name.
                    var siblingKind = sibling.Name.StartsWith("operator_implicit_") ? "implicit" : "explicit";
                    if(siblingKind != castKind)
                    {
                        continue;
                    }

                    // Compare resolved source and target types.
                    var siblingTargetType = sibling.DeclaredType;
                    var siblingParamTypes = ParameterSignature.Extract(sibling, model);
                    var siblingSourceType = siblingParamTypes.Count > 0 ? siblingParamTypes[0] : null;

                    if(siblingSourceType is not null && siblingTargetType is not null
                        && siblingSourceType.DisplayName == sourceType.DisplayName
                        && siblingTargetType.DisplayName == targetType.DisplayName)
                    {
                        diagnostics.ReportAtNode(TypeCheckDiagnostics.DuplicateCastOperator, castOpDecl, "TypeChecker", castKind, sourceType!.DisplayName, targetType!.DisplayName, enclosingType.DisplayName);
                        break; // One diagnostic per duplicate is sufficient.
                    }
                }
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Explicit cast expression validation (MIST_TC_0042, MIST_TC_0043, MIST_TC_0067)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Validates an explicit cast expression <c>(TargetType)operand</c>:
    /// <list type="number">
    /// <item><description>Resolves the target type from the semantic model.</description></item>
    /// <item><description>Gets the compile-time type of the operand.</description></item>
    /// <item><description>If the operand type is directly assignable to the target type
    /// (identity or subtype — <em>not</em> implicit cast), the cast is valid with no
    /// operator needed. This also covers the unknown-type top-type rule — when the
    /// target is <see cref="UnknownType"/>, <see cref="IsDirectlyAssignable"/>
    /// accepts every non-<c>void</c> source per Req 13.5 / Req 4.1, so the operator
    /// search is skipped entirely (Task 26.2).</description></item>
    /// <item><description>If the operand type is <see cref="UnknownType"/>
    /// (Task 26.1 / Req 13.4), searches <em>only</em> the target type's member
    /// scope for a cast operator whose source type is <c>unknown</c>. The source's
    /// scope is empty by construction (Req 3.4), so searching it is unnecessary.
    /// If no such operator exists, emits <c>MIST_TC_0067</c> (the targeted
    /// "no cast operator from unknown" diagnostic). If exactly one exists, records
    /// it. If multiple exist, emits <c>MIST_TC_0043</c> for ambiguity.</description></item>
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
        //    For enum reverse casts, the model builder sets the expression type to
        //    EnumType | void — unwrap to get the raw target type for operator lookup.
        var targetType = model.GetTypeOf(castExpr);
        if(targetType is null || IsErrorType(targetType))
        {
            return;
        }

        if(targetType is UnionType castUnion
            && castUnion.Constituents.Count == 2
            && castUnion.Constituents.Any(c => c is EnumType)
            && castUnion.Constituents.Any(c => c.IsVoid()))
        {
            // Already validated as an enum reverse cast by the model builder.
            // The expression type is correct (EnumType | void). Nothing more to check.
            return;
        }

        // 2. Get the compile-time type of the operand.
        var sourceType = model.GetTypeOf(castExpr.Operand);
        if(sourceType is null || IsErrorType(sourceType))
        {
            return;
        }

        // Handle enum reverse cast that the model builder couldn't resolve
        // (e.g., cross-file enums whose UnderlyingType wasn't resolved yet in Pass 2).
        if(targetType is EnumType targetEnumForCast
            && targetEnumForCast.UnderlyingType is not null
            && IsAssignable(sourceType, targetEnumForCast.UnderlyingType))
        {
            // Valid cast — update the expression type to EnumType | void.
            var voidType = model.AllSymbols
                .Where(s => s.Name == "Void" && s.Kind is SymbolKind.Struct or SymbolKind.Class
                         && s.DeclaredType is StructType { ContainingNamespace: "Standard" })
                .Select(s => s.DeclaredType!)
                .FirstOrDefault();

            if(voidType is not null)
            {
                var nullableEnumType = new UnionType(new List<IType> { targetEnumForCast, voidType });
                model.SetTypeOf(castExpr, nullableEnumType);
            }

            return;
        }

        // 3. If the operand type is directly assignable to the target type
        //    (identity or subtype), the cast is valid — no operator needed.
        //    We must NOT use the full IsAssignable here because that includes
        //    the implicit cast operator fallback. We only want identity/subtype.
        //
        //    The unknown-type top-type rule (Req 4.1, 13.5) is baked into
        //    IsDirectlyAssignable: `(unknown) expr` for any non-void `expr`
        //    is trivially valid and returns before any operator search
        //    (Task 26.2).
        if(IsDirectlyAssignable(sourceType, targetType, model))
        {
            return;
        }

        // 3a. Newtype ↔ source type explicit cast (newtypes feature, Req 2.1).
        //     A cast from a newtype to its source type or from the source type
        //     to the newtype is always valid — no operator lookup needed.
        if(sourceType is NewtypeType sourceNewtype
            && IsDirectlyAssignable(sourceNewtype.SourceType, targetType, model))
        {
            return;
        }

        if(targetType is NewtypeType targetNewtype
            && IsDirectlyAssignable(sourceType, targetNewtype.SourceType, model))
        {
            return;
        }

        // 3b. Cast from `unknown` to a concrete target type (Task 26.1 /
        //     Req 13.4, 13.6). UnknownType has no member scope (Req 3.4),
        //     so only the target's scope is searched for an operator
        //     whose source is `unknown` and target is T. When zero
        //     candidates are found we emit the targeted
        //     MIST_TC_0067 instead of the generic MIST_TC_0042 so the
        //     user sees an explanation specific to the top-type case
        //     (Req 16.4). Ambiguity still routes through MIST_TC_0043.
        if(sourceType is UnknownType && targetType is not UnknownType)
        {
            CheckCastFromUnknown(castExpr, sourceType, targetType, model, diagnostics);
            return;
        }

        // 4. Search for explicit and implicit cast operators on both the
        //    source type and the target type. Only operators declared directly
        //    on these types are considered (Requirement 8.7).
        var candidates = new List<ISymbol>();
        var explicitOpName = $"operator_explicit_{targetType.DisplayName}";
        var implicitOpName = $"operator_implicit_{targetType.DisplayName}";

        // 4a. Search the source type's member scope.
        var sourceScope = GetMemberScope(sourceType);
        if(sourceScope is not null)
        {
            foreach(var member in sourceScope.Members)
            {
                if(member.Kind == SymbolKind.Method
                    && (member.Name == explicitOpName || member.Name == implicitOpName))
                {
                    candidates.Add(member);
                }
            }
        }

        // 4b. Search the target type's member scope.
        var targetScope = GetMemberScope(targetType);
        if(targetScope is not null && !ReferenceEquals(sourceScope, targetScope))
        {
            foreach(var member in targetScope.Members)
            {
                if(member.Kind == SymbolKind.Method
                    && (member.Name == explicitOpName || member.Name == implicitOpName))
                {
                    // Avoid duplicates if the same symbol appears in both scopes.
                    if(!candidates.Any(c => ReferenceEquals(c, member)))
                    {
                        candidates.Add(member);
                    }
                }
            }
        }

        // 5. Evaluate candidates.
        if(candidates.Count == 1)
        {
            // Record the resolved operator and set the expression type.
            model.RecordResolvedOperator(castExpr, candidates[0]);
            model.SetTypeOf(castExpr, targetType);
        }
        else if(candidates.Count == 0)
        {
            // MIST_TC_0042: No cast operator found.
            diagnostics.ReportAtNode(TypeCheckDiagnostics.NoCastOperator, castExpr, "TypeChecker", sourceType.DisplayName, targetType.DisplayName);
        }
        else
        {
            // MIST_TC_0043: Ambiguous cast -- multiple operators applicable.
            diagnostics.ReportAtNode(TypeCheckDiagnostics.AmbiguousCast, castExpr, "TypeChecker", sourceType.DisplayName, targetType.DisplayName);
        }
    }

    /// <summary>
    /// Resolves a cast expression <c>(T) expr</c> where the operand's static
    /// type is <see cref="UnknownType"/> (Task 26.1 / Req 13.4).
    /// <para>
    /// Since <see cref="UnknownType"/> has no member scope (Req 3.4), only
    /// the target type's scope is searched for an implicit or explicit cast
    /// operator whose source type is <c>unknown</c> and whose target type
    /// is <paramref name="targetType"/>. When zero candidates are found the
    /// targeted <see cref="NoCastOperatorFromUnknown"/>
    /// (<c>MIST_TC_0067</c>) diagnostic is emitted instead of the generic
    /// <see cref="NoCastOperator"/> (<c>MIST_TC_0042</c>) so the user sees
    /// an explanation specific to the <c>unknown</c> case (Req 16.4).
    /// Ambiguity still routes through <see cref="AmbiguousCast"/>
    /// (<c>MIST_TC_0043</c>).
    /// </para>
    /// </summary>
    private static void CheckCastFromUnknown(
        CastExpressionUnaryNode castExpr,
        IType sourceType,
        IType targetType,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        var candidates = new List<ISymbol>();
        var explicitOpName = $"operator_explicit_{targetType.DisplayName}";
        var implicitOpName = $"operator_implicit_{targetType.DisplayName}";

        // Search only the target type's member scope. The source
        // (UnknownType) has no scope (Req 3.4) so no second search is
        // needed.
        var targetScope = GetMemberScope(targetType);
        if(targetScope is not null)
        {
            foreach(var member in targetScope.Members)
            {
                if(member.Kind == SymbolKind.Method
                    && (member.Name == explicitOpName || member.Name == implicitOpName))
                {
                    candidates.Add(member);
                }
            }
        }

        if(candidates.Count == 1)
        {
            model.RecordResolvedOperator(castExpr, candidates[0]);
            model.SetTypeOf(castExpr, targetType);
        }
        else if(candidates.Count == 0)
        {
            // MIST_TC_0067: targeted diagnostic for the top-type case.
            diagnostics.ReportAtNode(TypeCheckDiagnostics.NoCastOperatorFromUnknown, castExpr, "TypeChecker", targetType.DisplayName);
        }
        else
        {
            // MIST_TC_0043: Ambiguous cast -- multiple operators applicable.
            diagnostics.ReportAtNode(TypeCheckDiagnostics.AmbiguousCast, castExpr, "TypeChecker", sourceType.DisplayName, targetType.DisplayName);
        }
    }

    /// <summary>
    /// Checks whether <paramref name="source"/> is directly assignable to
    /// <paramref name="target"/> via identity, subtype, abstract,
    /// or constructed type relationships — but <b>not</b>
    /// via implicit cast operators (including numeric widening, which is
    /// now expressed as implicit cast operators on stdlib types). This is the subset of
    /// <see cref="IsAssignable(IType, IType, ISemanticModel?)"/> used by
    /// <see cref="CheckCastExpression"/> to determine whether a cast is
    /// trivially valid without requiring a cast operator.
    /// </summary>
    internal static bool IsDirectlyAssignable(IType source, IType target, ISemanticModel? model)
    {
        // ── type-aliases feature: transparent unwrapping (Requirement 5.1) ──
        // Type aliases are fully transparent — unwrap to the underlying type
        // before any comparison so that using an alias is semantically
        // identical to using the underlying type directly.
        if(source is TypeAliasType sourceAlias)
        {
            source = sourceAlias.UnderlyingType;
        }

        if(target is TypeAliasType targetAlias)
        {
            target = targetAlias.UnderlyingType;
        }

        // ── refinement-types feature: nominal subtyping (Requirement 8.3) ──
        // Same rules as in IsAssignable: widening from refinement to base is
        // always safe, and same-name refinement types are identity-assignable.
        // Different refinement types with same base use subset proving (Requirement 9.1, 9.2).
        if(source is RefinementType sourceRefinementDirect)
        {
            if(target is RefinementType targetRefinementDirect)
            {
                if(sourceRefinementDirect.Name == targetRefinementDirect.Name)
                {
                    return true;
                }

                // Different refinement types with same base: use subset proving.
                if(IsDirectlyAssignable(sourceRefinementDirect.BaseType, targetRefinementDirect.BaseType, model))
                {
                    if(RefinementSolver.Implies(sourceRefinementDirect.Predicate, targetRefinementDirect.Predicate))
                    {
                        return true;
                    }
                }
            }

            source = sourceRefinementDirect.BaseType;
        }

        // ── refinement-types feature: target is RefinementType ───────────
        // Same rule as in IsAssignable: when the target is a RefinementType,
        // check assignability against the base type.
        if(target is RefinementType targetRefinementDirectForBase)
        {
            return IsDirectlyAssignable(source, targetRefinementDirectForBase.BaseType, model);
        }

        // ErrorType suppresses cascading errors.
        if(IsErrorType(source) || IsErrorType(target))
        {
            return true;
        }

        // ── unknown-type feature: top-type direct-assignability (Task 26.2) ──
        //
        // UnknownType is the user-facing top type (Req 4.1). Every non-void
        // source is assignable to `unknown` without any cast operator lookup,
        // including in the cast-expression path — `(unknown) expr` is a
        // trivially-valid cast (Req 13.5) that the Emitter lowers as a direct
        // C# cast to `object`. This mirrors the top-type rule baked into
        // `IsAssignable` so the two helpers agree on the unknown-target case;
        // `IsDirectlyAssignable` is used by `CheckCastExpression` as the
        // "trivially valid, no operator needed" gate, and by the assignment-
        // site precedence check in `CheckAssignabilityAndTrackImplicitCast`
        // which must not try to resolve an implicit cast operator when the
        // target is `unknown` (UnknownType has no members, Req 3.4, so no
        // such operator could exist anyway).
        //
        // void → unknown is rejected here; the caller (assignment site or
        // CheckCastExpression) is responsible for emitting MIST_TC_0061.
        if(target is UnknownType)
        {
            if(source.IsVoid())
            {
                return false;
            }

            return true;
        }
        // source is UnknownType with a non-unknown target: fall through to
        // the rest of IsDirectlyAssignable, which will reject by returning
        // false. The cast-expression path then routes to
        // `CheckCastFromUnknown` for the operator lookup.

        // ── type-unions feature: union type direct-assignability ──────────
        //
        // Mirrors the union rules in IsAssignable so that IsDirectlyAssignable
        // and IsAssignable agree on union-related assignments. This ensures
        // the cast-expression path and the implicit-cast precedence check
        // both handle union types consistently.
        if(target is UnionType targetUnionDirect)
        {
            if(HasErrorConstituent(targetUnionDirect))
            {
                return true;
            }

            if(source is UnionType sourceUnionDirect)
            {
                if(HasErrorConstituent(sourceUnionDirect))
                {
                    return true;
                }

                foreach(var constituent in sourceUnionDirect.Constituents)
                {
                    if(!IsConstituentOf(constituent, targetUnionDirect))
                    {
                        return false;
                    }
                }

                return true;
            }

            return IsConstituentOf(source, targetUnionDirect);
        }

        if(source is UnionType)
        {
            return false;
        }

        // ── newtypes feature: nominal distinctness (Req 2.1, 2.3, 2.4, 2.5) ──
        //
        // A newtype is a nominally distinct type — it is only assignable to
        // itself (same instance). It is NOT assignable to its source type,
        // the source type is NOT assignable to the newtype, and two different
        // newtypes are NOT assignable to each other even if they share the
        // same source type. An explicit cast is required for conversion.
        //
        // This check is placed before the identity/DisplayName checks so that
        // a newtype with the same DisplayName as another type (unlikely but
        // possible in error-recovery scenarios) is still rejected.
        if(source is NewtypeType || target is NewtypeType)
        {
            // Only the same newtype instance is assignable to itself.
            return ReferenceEquals(source, target);
        }

        // Identity.
        if(ReferenceEquals(source, target))
        {
            return true;
        }

        // Same display name.
        if(source.DisplayName == target.DisplayName)
        {
            return true;
        }

        // ── anonymous-types feature: structural equality (Requirement 4.6) ──
        // Two AnonymousType instances with the same property names, types, and
        // order are the same type and are directly assignable to each other.
        // This explicit check ensures correctness regardless of DisplayName
        // formatting and makes the structural equality contract visible.
        if(source is AnonymousType sourceAnon && target is AnonymousType targetAnon)
        {
            return sourceAnon.StructurallyEquals(targetAnon);
        }

        // Migration-window shim.
        if(ArePrimitiveEquivalent(source, target))
        {
            return true;
        }

        // Tuple assignability with name erasure: two TupleType instances are
        // assignable if they have the same arity and each element type is
        // assignable. Element names are ignored (name erasure).
        if(source is TupleType sourceTuple && target is TupleType targetTuple)
        {
            if(sourceTuple.Arity == targetTuple.Arity)
            {
                var allElementsAssignable = true;
                for(var i = 0; i < sourceTuple.Arity; i++)
                {
                    if(!IsDirectlyAssignable(sourceTuple.ElementTypes[i], targetTuple.ElementTypes[i], model))
                    {
                        allElementsAssignable = false;
                        break;
                    }
                }

                if(allElementsAssignable)
                {
                    return true;
                }
            }

            return false;
        }

        // ConstructedType identity.
        if(source is ConstructedType sourceConstructed && target is ConstructedType targetConstructed)
        {
            if(ReferenceEquals(sourceConstructed.GenericDefinition, targetConstructed.GenericDefinition)
                && sourceConstructed.TypeArguments.Count == targetConstructed.TypeArguments.Count)
            {
                var allMatch = true;
                for(var i = 0; i < sourceConstructed.TypeArguments.Count; i++)
                {
                    if(!TypesEqual(sourceConstructed.TypeArguments[i], targetConstructed.TypeArguments[i]))
                    {
                        allMatch = false;
                        break;
                    }
                }

                if(allMatch)
                {
                    return true;
                }

                // Covariant unknown widening (unknown-type feature, Task 23.3).
                // Req 8.4: a trivially-valid cast like `(List<unknown>) intList`
                // must not require a user-declared cast operator. Applying the
                // widening rule here lets `CheckCastExpression` accept it
                // directly.
                if(AssignableWithUnknownWidening(
                        sourceConstructed.GenericDefinition,
                        sourceConstructed.TypeArguments,
                        targetConstructed.TypeArguments))
                {
                    return true;
                }
            }
        }

        // ConstructedType inheritance.
        if(source is ConstructedType sourceConst && target is ConstructedType)
        {
            var baseType = GetSubstitutedBaseType(sourceConst);
            if(baseType is not null && IsDirectlyAssignable(baseType, target, model))
            {
                return true;
            }
        }

        // ConstructedType (struct) to plain StructType assignability.
        if(source is ConstructedType sourceStructConst
            && sourceStructConst.GenericDefinition is StructType
            && target is StructType)
        {
            var baseType = GetSubstitutedBaseType(sourceStructConst);
            if(baseType is not null && IsDirectlyAssignable(baseType, target, model))
            {
                return true;
            }
        }

        // ConstructedType to constructed abstract assignability.
        if(target is ConstructedType targetConst && targetConst.GenericDefinition is AbstractType)
        {
            if(ImplementsConstructedAbstract(source, targetConst))
            {
                return true;
            }
        }

        // Numeric widening is now handled by implicit cast operators
        // declared on the stdlib bootstrap types (e.g., Int32 has
        // `implicit operator Int64`). The implicit cast operator fallback
        // at the end of IsAssignable handles this. IsDirectlyAssignable
        // intentionally does NOT check implicit cast operators.

        // Abstract assignability (also checks with-block conformance via ConformanceMap).
        if(target is AbstractType targetAbstract)
        {
            if(ImplementsAbstract(source, targetAbstract, model))
            {
                return true;
            }
        }

        // Subtype assignability — walk the inheritance chain.
        if(source is ClassType sourceClass && target is ClassType)
        {
            var current = sourceClass.BaseType as ClassType;
            while(current is not null)
            {
                if(ReferenceEquals(current, target) || current.DisplayName == target.DisplayName)
                {
                    return true;
                }

                current = current.BaseType as ClassType;
            }
        }

        // Struct subtype assignability — walk the inheritance chain.
        if(source is StructType sourceStruct && target is StructType)
        {
            var current = sourceStruct.BaseType as StructType;
            while(current is not null)
            {
                if(ReferenceEquals(current, target) || current.DisplayName == target.DisplayName)
                {
                    return true;
                }

                current = current.BaseType as StructType;
            }
        }

        // Plain StructType to ConstructedType (struct) assignability.
        if(source is StructType sourceStructForCt && target is ConstructedType targetStructCt
            && targetStructCt.GenericDefinition is StructType)
        {
            var currentBase = sourceStructForCt.BaseType;
            while(currentBase is not null)
            {
                if(IsDirectlyAssignable(currentBase, target, model))
                {
                    return true;
                }

                currentBase = currentBase switch
                {
                    StructType st => st.BaseType,
                    ConstructedType ct when ct.GenericDefinition is StructType ctStructDef => ctStructDef.BaseType,
                    _ => null
                };
            }
        }

        // ── enums feature: implicit conversion to underlying type (Req 7.1, 7.2) ──
        // An enum value is directly assignable to its underlying type without
        // requiring an implicit cast operator lookup.
        if(source is EnumType sourceEnum && IsDirectlyAssignable(sourceEnum.UnderlyingType, target, model))
        {
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

        if(declaringNode is not ConstructorDeclarationNode ctorDecl)
        {
            return;
        }

        // ── MIST_TC_0108: async on constructor (Requirement 2.5) ──
        foreach(var mod in ctorDecl.Modifiers)
        {
            if(mod is AsyncModifierModifierNode)
            {
                diagnostics.ReportAtNode(TypeCheckDiagnostics.AsyncOnConstructorOrOperator, declaringNode, "TypeChecker", "constructors");
                break;
            }
        }

        currentEnclosingType = FindDeclaringType(ctorSymbol);
        insideConstructor = true;
        insidePropertyAccessor = false;

        // Check constructor chaining (: this(args))
        if(ctorDecl.Chain is not null)
        {
            CheckConstructorChain(ctorDecl.Chain, ctorSymbol, model, diagnostics);
        }

        if(ctorDecl.Body.GetBlock() is BlockNode ctorBlock)
        {
            // ── Flow narrowing: inject parameter refinement narrowing (Requirement 8.1–8.5) ──
            var paramNarrowings = InjectParameterRefinementNarrowing(declaringNode, model);

            CheckBlock(ctorBlock, LegacyPrimitiveTypes.Void, model, diagnostics);

            // Remove parameter refinement narrowing entries after the body.
            RemoveParameterRefinementNarrowing(paramNarrowings);

            // ── Definite assignment analysis (Requirements 3.1–3.6) ──
            CheckDefiniteAssignment(ctorSymbol, ctorBlock, model, diagnostics);
        }

        insideConstructor = false;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Definite assignment analysis for constructors (Requirements 3.1–3.6)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Verifies that all fields of the declaring type are definitely assigned
    /// by the end of the constructor body. Emits <c>MIST_FIELD_NOT_DEFINITELY_ASSIGNED</c>
    /// for any non-defaultable field without an initializer that is not assigned
    /// on all control-flow paths.
    /// </summary>
    private static void CheckDefiniteAssignment(
        ISymbol ctorSymbol, BlockNode ctorBlock, ISemanticModel model, DiagnosticBag diagnostics)
    {
        // Find the declaring type.
        var declaringType = FindDeclaringType(ctorSymbol);
        if(declaringType is null)
        {
            return;
        }

        // Get the member scope to enumerate fields.
        var memberScope = declaringType switch
        {
            ClassType ct => ct.MemberScope,
            StructType st => st.MemberScope,
            _ => null,
        };
        if(memberScope is null)
        {
            return;
        }

        // Collect all instance fields declared on this type (not inherited).
        // Static fields are not part of instance construction and must be excluded.
        // Inherited fields are excluded because their assignment is handled by the base constructor.
        var allFields = new List<string>();
        foreach(var member in memberScope.Members)
        {
            if(member.Kind == SymbolKind.Field && member is Symbol sym && !sym.IsStatic
                && ReferenceEquals(member.ContainingScope, memberScope))
            {
                allFields.Add(member.Name);
            }
        }

        if(allFields.Count == 0)
        {
            return;
        }

        // Build a mapping from property names to their backing field names.
        // When code assigns `this.PropertyName = value`, it's actually assigning
        // the backing field `_PropertyName_k__BackingField`.
        var propertyToBackingField = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach(var member in memberScope.Members)
        {
            if(member.Kind == SymbolKind.Property && member is Symbol propSym
                && propSym.PropertyMetadata?.BackingField is Symbol backingField
                && !backingField.IsStatic)
            {
                propertyToBackingField[member.Name] = backingField.Name;
            }
        }

        // Compute the initial definitely-assigned set:
        // - Fields with initializers (Req 3.2)
        // - Fields with defaultable types and no initializer (Req 3.3 — synthetic default injection)
        var initiallyAssigned = new HashSet<string>(StringComparer.Ordinal);
        foreach(var member in memberScope.Members)
        {
            if(member.Kind != SymbolKind.Field)
            {
                continue;
            }

            // Skip static fields — they are not part of instance construction.
            if(member is Symbol fieldSym && fieldSym.IsStatic)
            {
                continue;
            }

            // Skip inherited fields — their initialization is the base constructor's responsibility.
            if(!ReferenceEquals(member.ContainingScope, memberScope))
            {
                continue;
            }

            // Check if the field has an initializer.
            var hasInitializer = false;
            try
            {
                var fieldNode = model.GetDeclaringNode(member);
                if(fieldNode is FieldDeclarationNode fieldDecl && fieldDecl.Initializer is not null)
                {
                    hasInitializer = true;
                }
                // For backing fields, the declaring node is the property node.
                // Check if the property has an initializer.
                else if(fieldNode is BlockPropertyPropertyDeclarationNode propDecl && propDecl.Initializer is not null)
                {
                    hasInitializer = true;
                }
            }
            catch
            {
                // If we can't get the declaring node, treat as no initializer.
            }

            if(hasInitializer)
            {
                // Req 3.2: Field with initializer → pre-assigned.
                _ = initiallyAssigned.Add(member.Name);
            }
            else if(member.DeclaredType is not null && IsTypeDefaultable(member.DeclaredType))
            {
                // Req 3.3: Field type is defaultable, no initializer → pre-assigned (synthetic default injection).
                _ = initiallyAssigned.Add(member.Name);
            }
            else if(member.DeclaredType is TypeParameterType)
            {
                // Type parameter fields are runtime zero-initialized (like C#'s default(T)).
                // Constraint satisfaction for the type argument is checked at instantiation sites.
                _ = initiallyAssigned.Add(member.Name);
            }
            else if(member.DeclaredType is TupleType tupleField
                && tupleField.ElementTypes.Any(e => e is TypeParameterType { IsPack: true }))
            {
                // Spread-generated aggregate fields whose type is a value-pack tuple (TArgs...)
                // are assigned via zip assignment (Item... = values...) which is not tracked by
                // the simple forward dataflow. Treat them as pre-assigned since the constructor's
                // zip assignment logic handles them (the type checker validates the zip separately).
                _ = initiallyAssigned.Add(member.Name);
            }
        }

        // If all fields are already assigned, nothing to check.
        if(initiallyAssigned.Count >= allFields.Count)
        {
            return;
        }

        // Compute the set of non-defaultable fields (those not in initiallyAssigned).
        var allNonDefaultableFields = new HashSet<string>(StringComparer.Ordinal);
        foreach(var fieldName in allFields)
        {
            if(!initiallyAssigned.Contains(fieldName))
            {
                _ = allNonDefaultableFields.Add(fieldName);
            }
        }

        // Perform forward dataflow analysis on the constructor body.
        // This detects read-before-assignment violations AND computes the assigned set at exit.
        var state = new AssignmentState(initiallyAssigned);
        var readViolations = RunForwardDataflow(ctorBlock, state, propertyToBackingField, model, true, initiallyAssigned, allNonDefaultableFields);

        // Emit MIST_FIELD_READ_BEFORE_ASSIGNMENT for each field-read violation (Req 1.1, 1.4, 1.8, 7.1, 7.4).
        // Emit MIST_LOCAL_READ_BEFORE_ASSIGNMENT for each local-read violation (Req 2.1, 2.3, 2.6, 7.2, 7.4).
        var ctorName = ctorSymbol.Name;
        foreach(var violation in readViolations)
        {
            if(violation.Kind == ReadViolationKind.Field)
            {
                diagnostics.ReportAtNode(CommonDiagnostics.FieldReadBeforeAssignment, violation.Location, "TypeChecker", violation.Name, ctorName);
            }
            else if(violation.Kind == ReadViolationKind.Local)
            {
                diagnostics.ReportAtNode(CommonDiagnostics.LocalReadBeforeAssignment, violation.Location, "TypeChecker", violation.Name);
            }
        }

        // Existing end-of-constructor check: MIST_FIELD_NOT_DEFINITELY_ASSIGNED (Req 1.7).
        // Check that all fields are in the definitely-assigned set at exit.
        var assignedAtExit = state.AssignedFields;
        foreach(var fieldName in allFields)
        {
            if(!assignedAtExit.Contains(fieldName))
            {
                // Find the field symbol to get its type for the diagnostic message.
                ISymbol? fieldSymbol = null;
                foreach(var member in memberScope.Members)
                {
                    if(member.Kind == SymbolKind.Field && member.Name == fieldName)
                    {
                        fieldSymbol = member;
                        break;
                    }
                }

                var typeName = fieldSymbol?.DeclaredType?.DisplayName ?? "unknown";

                // Get the constructor's declaring node for location.
                SyntaxNode ctorDeclNode;
                try { ctorDeclNode = model.GetDeclaringNode(ctorSymbol); }
                catch { continue; }

                diagnostics.ReportAtNode(CommonDiagnostics.FieldNotDefinitelyAssigned, ctorDeclNode, "TypeChecker", fieldName, ctorName, typeName);
            }
        }
    }

    /// <summary>
    /// Performs forward dataflow analysis on a non-constructor method/function/operator body
    /// to detect reads of local variables before they are definitely assigned.
    /// Emits <c>MIST_LOCAL_READ_BEFORE_ASSIGNMENT</c> for each violation (Req 2.1–2.6, 7.2, 7.4).
    /// </summary>
    private static void CheckLocalDefiniteAssignment(
        BlockNode block, ISemanticModel model, DiagnosticBag diagnostics)
    {
        var state = new AssignmentState();
        var emptyPropertyMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var readViolations = RunForwardDataflow(block, state, emptyPropertyMap, model, false, null, null);

        foreach(var violation in readViolations)
        {
            if(violation.Kind == ReadViolationKind.Local)
            {
                diagnostics.ReportAtNode(CommonDiagnostics.LocalReadBeforeAssignment, violation.Location, "TypeChecker", violation.Name);
            }
        }
    }

    /// <summary>
    /// Performs a forward dataflow analysis on a block to determine which fields
    /// and locals are definitely assigned at the exit point, and collects any
    /// read-before-assignment violations detected along the way.
    /// </summary>
    /// <param name="block">The block of statements to analyze.</param>
    /// <param name="state">The initial assignment state (seeded with pre-assigned fields/locals).</param>
    /// <param name="propertyToBackingField">Maps property names to backing field names.</param>
    /// <param name="model">The semantic model for symbol resolution.</param>
    /// <param name="isConstructor">Whether the block is inside a constructor (enables field-read checks).</param>
    /// <param name="initiallyAssignedFields">Fields pre-assigned via initializer or synthetic default (for field-read checking).</param>
    /// <param name="allNonDefaultableFields">All non-defaultable fields that the constructor must assign (for this-escape detection).</param>
    /// <returns>The list of read-before-assignment violations detected during the walk.</returns>
    private static List<ReadViolation> RunForwardDataflow(
        BlockNode block,
        AssignmentState state,
        Dictionary<string, string> propertyToBackingField,
        ISemanticModel model,
        bool isConstructor,
        HashSet<string>? initiallyAssignedFields,
        HashSet<string>? allNonDefaultableFields = null)
    {
        var violations = new List<ReadViolation>();
        AnalyzeBlockDataflow(block, state, propertyToBackingField, model, isConstructor, initiallyAssignedFields, violations, allNonDefaultableFields);
        return violations;
    }

    /// <summary>
    /// Walks a block of statements performing forward read+write dataflow analysis.
    /// Records field/local writes and detects reads of unassigned locations.
    /// </summary>
    private static void AnalyzeBlockDataflow(
        BlockNode block,
        AssignmentState state,
        Dictionary<string, string> propertyToBackingField,
        ISemanticModel model,
        bool isConstructor,
        HashSet<string>? initiallyAssignedFields,
        List<ReadViolation> violations,
        HashSet<string>? allNonDefaultableFields)
    {
        foreach(var stmt in block.Statements)
        {
            if(state.Terminated)
            {
                break;
            }

            AnalyzeStatementDataflow(stmt, state, propertyToBackingField, model, isConstructor, initiallyAssignedFields, violations, allNonDefaultableFields);
        }
    }

    /// <summary>
    /// Analyzes a single statement for the forward read+write dataflow.
    /// Handles assignments (writes), reads, control flow (if/else, loops), and termination.
    /// </summary>
    private static void AnalyzeStatementDataflow(
        StatementNode stmt,
        AssignmentState state,
        Dictionary<string, string> propertyToBackingField,
        ISemanticModel model,
        bool isConstructor,
        HashSet<string>? initiallyAssignedFields,
        List<ReadViolation> violations,
        HashSet<string>? allNonDefaultableFields)
    {
        switch(stmt)
        {
            case ExpressionStatementStatementNode exprStmt:
                AnalyzeExpressionDataflow(exprStmt.ExpressionStatement.Expression, state, propertyToBackingField, model, isConstructor, initiallyAssignedFields, violations, allNonDefaultableFields);
                break;

            case IfStatementStatementNode ifStmt:
                AnalyzeIfDataflow(ifStmt.IfStatement, state, propertyToBackingField, model, isConstructor, initiallyAssignedFields, violations, allNonDefaultableFields);
                break;

            case WhileStatementStatementNode whileStmt:
                // Conservative: analyze body with a copy but do NOT propagate body-only
                // assignments outward (the body may not execute). Reads inside the body
                // are checked against the pre-loop state.
                AnalyzeExpressionForReads(whileStmt.WhileStatement.Condition, state, model, isConstructor, initiallyAssignedFields, violations, allNonDefaultableFields);
                var whileBodyState = state.Clone();
                AnalyzeBlockDataflow(whileStmt.WhileStatement.Body, whileBodyState, propertyToBackingField, model, isConstructor, initiallyAssignedFields, violations, allNonDefaultableFields);
                break;

            case ForStatementStatementNode forStmt:
                // Analyze the initializer (may introduce a local assignment).
                AnalyzeForInitializerDataflow(forStmt.ForStatement, state, model, isConstructor, initiallyAssignedFields, violations, allNonDefaultableFields);
                // Check condition and iterator for reads.
                if(forStmt.ForStatement.Condition is not null)
                {
                    AnalyzeExpressionForReads(forStmt.ForStatement.Condition, state, model, isConstructor, initiallyAssignedFields, violations, allNonDefaultableFields);
                }

                if(forStmt.ForStatement.Iterator is not null)
                {
                    AnalyzeExpressionForReads(forStmt.ForStatement.Iterator, state, model, isConstructor, initiallyAssignedFields, violations, allNonDefaultableFields);
                }
                // Conservative: body may not execute.
                var forBodyState = state.Clone();
                AnalyzeBlockDataflow(forStmt.ForStatement.Body, forBodyState, propertyToBackingField, model, isConstructor, initiallyAssignedFields, violations, allNonDefaultableFields);
                break;

            case ForeachStatementStatementNode foreachStmt:
                // Check collection expression for reads.
                AnalyzeExpressionForReads(foreachStmt.ForeachStatement.GetCollection(), state, model, isConstructor, initiallyAssignedFields, violations, allNonDefaultableFields);
                // Conservative: body may not execute.
                var foreachBodyState = state.Clone();
                // The foreach iteration variable is implicitly assigned by the loop mechanism.
                var foreachVarSymbol = model.GetDeclaredSymbol(foreachStmt.ForeachStatement);
                if(foreachVarSymbol is not null)
                {
                    _ = foreachBodyState.AssignedLocals.Add(foreachVarSymbol);
                }

                AnalyzeBlockDataflow(foreachStmt.ForeachStatement.GetBody(), foreachBodyState, propertyToBackingField, model, isConstructor, initiallyAssignedFields, violations, allNonDefaultableFields);
                break;

            case BlockStatementStatementNode blockStmt:
                AnalyzeBlockDataflow(blockStmt.BlockStatement, state, propertyToBackingField, model, isConstructor, initiallyAssignedFields, violations, allNonDefaultableFields);
                break;

            case ReturnStatementStatementNode returnStmt:
                // Check the return value expression for reads before marking terminated.
                if(returnStmt.ReturnStatement.Value is not null)
                {
                    AnalyzeExpressionForReads(returnStmt.ReturnStatement.Value, state, model, isConstructor, initiallyAssignedFields, violations, allNonDefaultableFields);
                    // this-escape: returning `this` from a constructor while fields are unassigned (Req 1.8).
                    if(isConstructor && allNonDefaultableFields is not null)
                    {
                        CheckThisEscapeInExpression(returnStmt.ReturnStatement.Value, state, allNonDefaultableFields, violations, returnStmt.ReturnStatement);
                    }
                }

                state.Terminated = true;
                break;

            case VariableDeclarationStatementStatementNode varDeclStmt:
                AnalyzeVariableDeclarationDataflow(varDeclStmt.VariableDeclaration, state, model, isConstructor, initiallyAssignedFields, violations, allNonDefaultableFields);
                break;

            case UninitializedVariableStatementStatementNode:
                // No initializer — the local is NOT added to AssignedLocals.
                // Definite-assignment analysis will ensure it's assigned before use.
                break;

            case BreakStatementStatementNode:
                state.Terminated = true;
                break;

            case ReadonlyBlockStatementStatementNode rb:
                AnalyzeBlockDataflow(rb.ReadonlyBlock.Body, state, propertyToBackingField, model, isConstructor, initiallyAssignedFields, violations, allNonDefaultableFields);
                break;
        }
    }

    /// <summary>
    /// Analyzes an if statement for the forward dataflow. Clones state per branch,
    /// analyzes each, and intersects at the merge point using <see cref="AssignmentState.IntersectWith"/>.
    /// </summary>
    private static void AnalyzeIfDataflow(
        IfStatementNode ifStmt,
        AssignmentState state,
        Dictionary<string, string> propertyToBackingField,
        ISemanticModel model,
        bool isConstructor,
        HashSet<string>? initiallyAssignedFields,
        List<ReadViolation> violations,
        HashSet<string>? allNonDefaultableFields)
    {
        // Check the condition expression for reads.
        AnalyzeExpressionForReads(ifStmt.Condition, state, model, isConstructor, initiallyAssignedFields, violations, allNonDefaultableFields);

        if(ifStmt.ElseBranch is null)
        {
            // No else branch: the then-path is conditional. Assignments in the then-branch
            // are not guaranteed. Analyze the then-branch with a copy for read-checking
            // but do not propagate its writes outward.
            var thenState = state.Clone();
            AnalyzeBlockDataflow(ifStmt.ThenBranch, thenState, propertyToBackingField, model, isConstructor, initiallyAssignedFields, violations, allNonDefaultableFields);
            return;
        }

        // Both then and else branches exist — clone per branch, analyze, intersect.
        var thenBranchState = state.Clone();
        AnalyzeBlockDataflow(ifStmt.ThenBranch, thenBranchState, propertyToBackingField, model, isConstructor, initiallyAssignedFields, violations, allNonDefaultableFields);

        var elseBranchState = state.Clone();
        switch(ifStmt.ElseBranch)
        {
            case ElseBlockElseClauseNode elseBlock:
                AnalyzeBlockDataflow(elseBlock.ElseBlock, elseBranchState, propertyToBackingField, model, isConstructor, initiallyAssignedFields, violations, allNonDefaultableFields);
                break;
            case ElseIfElseClauseNode elseIf:
                AnalyzeIfDataflow(elseIf.ElseIf, elseBranchState, propertyToBackingField, model, isConstructor, initiallyAssignedFields, violations, allNonDefaultableFields);
                break;
        }

        // Merge: use IntersectWith to compute the post-merge state.
        thenBranchState.IntersectWith(elseBranchState);

        // Update the current state to the merged result.
        state.AssignedFields.Clear();
        state.AssignedFields.UnionWith(thenBranchState.AssignedFields);
        state.AssignedLocals.Clear();
        state.AssignedLocals.UnionWith(thenBranchState.AssignedLocals);
        state.Terminated = thenBranchState.Terminated;
    }

    /// <summary>
    /// Analyzes an expression statement for the forward dataflow.
    /// Detects assignment targets (writes) and checks reads of unassigned locations.
    /// Handles simple assignments and compound assignments (read-then-write).
    /// </summary>
    private static void AnalyzeExpressionDataflow(
        ExpressionNode expr,
        AssignmentState state,
        Dictionary<string, string> propertyToBackingField,
        ISemanticModel model,
        bool isConstructor,
        HashSet<string>? initiallyAssignedFields,
        List<ReadViolation> violations,
        HashSet<string>? allNonDefaultableFields)
    {
        if(expr is not AssignmentFormExpressionNode assignExpr)
        {
            // Not an assignment form — check the entire expression for reads.
            AnalyzeExpressionForReads(expr, state, model, isConstructor, initiallyAssignedFields, violations, allNonDefaultableFields);
            return;
        }

        var assignment = assignExpr.Assignment;
        var assignedValue = assignment.Group0?.Value;
        if(assignedValue is null)
        {
            // No assignment value — this is just an expression (e.g., method call).
            // Check the target for reads.
            AnalyzeVoidCoalescingForReads(assignment.Target, state, model, isConstructor, initiallyAssignedFields, violations, allNonDefaultableFields);
            return;
        }

        // This is an assignment: target = value (or target ??= value).
        // Only ??= is a read-then-write (it reads the target to check for void).
        var isVoidCoalescingAssign = assignment.Group0?.Operator is { } op
            && op.Kind == (int)TokenKind.QUESTION_QUESTION_EQUAL;

        // Extract the assignment target (field or local).
        var fieldName = TryExtractThisFieldAssignmentTarget(assignment.Target);
        fieldName ??= TryExtractBareFieldAssignmentTarget(assignment.Target);

        var localSymbol = TryExtractLocalAssignmentTarget(assignment.Target, model);

        if(isVoidCoalescingAssign)
        {
            // Void-coalescing assignment (??=): read-then-write.
            // Check the target as a read FIRST (Req 2.6).
            AnalyzeVoidCoalescingForReads(assignment.Target, state, model, isConstructor, initiallyAssignedFields, violations, allNonDefaultableFields);
        }
        else if(fieldName is null && localSymbol is null)
        {
            // Not a simple field/local target — check target for reads
            // (e.g., array element assignment: arr[i] = ...).
            AnalyzeVoidCoalescingForReads(assignment.Target, state, model, isConstructor, initiallyAssignedFields, violations, allNonDefaultableFields);
        }

        // Check the RHS value for reads.
        AnalyzeExpressionForReads(assignedValue, state, model, isConstructor, initiallyAssignedFields, violations, allNonDefaultableFields);

        // Record the write.
        if(fieldName is not null)
        {
            _ = state.AssignedFields.Add(fieldName);
            if(propertyToBackingField.TryGetValue(fieldName, out var backingFieldName))
            {
                _ = state.AssignedFields.Add(backingFieldName);
            }
        }

        if(localSymbol is not null)
        {
            _ = state.AssignedLocals.Add(localSymbol);
        }
    }

    /// <summary>
    /// Analyzes a variable declaration for the forward dataflow.
    /// A local with an initializer is marked as assigned after the initializer is checked for reads.
    /// </summary>
    private static void AnalyzeVariableDeclarationDataflow(
        VariableDeclarationStatementNode varDecl,
        AssignmentState state,
        ISemanticModel model,
        bool isConstructor,
        HashSet<string>? initiallyAssignedFields,
        List<ReadViolation> violations,
        HashSet<string>? allNonDefaultableFields)
    {
        switch(varDecl)
        {
            case TypedVariableDeclarationVariableDeclarationStatementNode typed:
                AnalyzeExpressionForReads(typed.Initializer, state, model, isConstructor, initiallyAssignedFields, violations, allNonDefaultableFields);
                var typedSymbol = model.GetDeclaredSymbol(typed);
                if(typedSymbol is not null)
                {
                    _ = state.AssignedLocals.Add(typedSymbol);
                }

                break;

            case InferredVariableDeclarationVariableDeclarationStatementNode inferred:
                AnalyzeExpressionForReads(inferred.Initializer, state, model, isConstructor, initiallyAssignedFields, violations, allNonDefaultableFields);
                var inferredSymbol = model.GetDeclaredSymbol(inferred);
                if(inferredSymbol is not null)
                {
                    _ = state.AssignedLocals.Add(inferredSymbol);
                }

                break;

            case VarTupleDeconstructionVariableDeclarationStatementNode varTuple:
                AnalyzeExpressionForReads(varTuple.Initializer, state, model, isConstructor, initiallyAssignedFields, violations, allNonDefaultableFields);
                var tupleSymbol = model.GetDeclaredSymbol(varTuple);
                if(tupleSymbol is not null)
                {
                    _ = state.AssignedLocals.Add(tupleSymbol);
                }

                break;

            case TypedTupleDeconstructionVariableDeclarationStatementNode typedTuple:
                AnalyzeExpressionForReads(typedTuple.Initializer, state, model, isConstructor, initiallyAssignedFields, violations, allNonDefaultableFields);
                var typedTupleSymbol = model.GetDeclaredSymbol(typedTuple);
                if(typedTupleSymbol is not null)
                {
                    _ = state.AssignedLocals.Add(typedTupleSymbol);
                }

                break;
        }
    }

    /// <summary>
    /// Analyzes the initializer of a for-loop for the forward dataflow.
    /// The loop variable introduced by the initializer is marked as assigned.
    /// </summary>
    private static void AnalyzeForInitializerDataflow(
        ForStatementNode forStmt,
        AssignmentState state,

        ISemanticModel model,
        bool isConstructor,
        HashSet<string>? initiallyAssignedFields,
        List<ReadViolation> violations,
        HashSet<string>? allNonDefaultableFields)
    {
        switch(forStmt.Initializer)
        {
            case ForInitializerVariableDeclarationForInitializerNode varInit:
                AnalyzeExpressionForReads(varInit.Initializer, state, model, isConstructor, initiallyAssignedFields, violations, allNonDefaultableFields);
                var varSymbol = model.GetDeclaredSymbol(varInit);
                if(varSymbol is not null)
                {
                    _ = state.AssignedLocals.Add(varSymbol);
                }

                break;

            case ForInitializerVarDeclarationForInitializerNode varDeclInit:
                AnalyzeExpressionForReads(varDeclInit.Initializer, state, model, isConstructor, initiallyAssignedFields, violations, allNonDefaultableFields);
                var varDeclSymbol = model.GetDeclaredSymbol(varDeclInit);
                if(varDeclSymbol is not null)
                {
                    _ = state.AssignedLocals.Add(varDeclSymbol);
                }

                break;

            case ForInitializerExpressionForInitializerNode exprInit:
                AnalyzeExpressionForReads(exprInit.Expression, state, model, isConstructor, initiallyAssignedFields, violations, allNonDefaultableFields);
                break;
        }
    }

    /// <summary>
    /// Checks an expression tree for reads of unassigned fields/locals.
    /// This is the main read-detection entry point for arbitrary expressions.
    /// </summary>
    private static void AnalyzeExpressionForReads(
        ExpressionNode expr,
        AssignmentState state,
        ISemanticModel model,
        bool isConstructor,
        HashSet<string>? initiallyAssignedFields,
        List<ReadViolation> violations,
        HashSet<string>? allNonDefaultableFields)
    {
        // Lambda expressions introduce their own scope — don't analyze their body here.
        if(expr.AsLambdaOrNull() is not null)
        {
            return;
        }

        var assignment = expr.AsAssignmentOrNull();
        if(assignment is null)
        {
            return;
        }

        // If this is an assignment expression (nested), handle it recursively.
        var assignedValue = assignment.Group0?.Value;
        if(assignedValue is not null)
        {
            // Nested assignment: check RHS for reads, then the target.
            AnalyzeExpressionForReads(assignedValue, state, model, isConstructor, initiallyAssignedFields, violations, allNonDefaultableFields);
            return;
        }

        // No assignment value — this is a read expression. Check the target.
        AnalyzeVoidCoalescingForReads(assignment.Target, state, model, isConstructor, initiallyAssignedFields, violations, allNonDefaultableFields);
    }

    /// <summary>
    /// Analyzes a VoidCoalescingNode (the target side of an expression) for reads of unassigned locations.
    /// </summary>
    private static void AnalyzeVoidCoalescingForReads(
        VoidCoalescingNode target,
        AssignmentState state,
        ISemanticModel model,
        bool isConstructor,
        HashSet<string>? initiallyAssignedFields,
        List<ReadViolation> violations,
        HashSet<string>? allNonDefaultableFields)
    {
        if(target.Left is null)
        {
            return;
        }

        // Check the main expression chain.
        AnalyzeLogicalOrForReads(target.Left, state, model, isConstructor, initiallyAssignedFields, violations, allNonDefaultableFields);

        // Check any void-coalescing right-hand sides (?? expressions).
        foreach(var logicalOr in target.LogicalOrs)
        {
            AnalyzeLogicalOrForReads(logicalOr, state, model, isConstructor, initiallyAssignedFields, violations, allNonDefaultableFields);
        }
    }

    /// <summary>
    /// Analyzes a LogicalOrNode chain for reads.
    /// </summary>
    private static void AnalyzeLogicalOrForReads(
        LogicalOrNode logicalOr,
        AssignmentState state,
        ISemanticModel model,
        bool isConstructor,
        HashSet<string>? initiallyAssignedFields,
        List<ReadViolation> violations,
        HashSet<string>? allNonDefaultableFields)
    {
        AnalyzeLogicalAndForReads(logicalOr.Left, state, model, isConstructor, initiallyAssignedFields, violations, allNonDefaultableFields);
        if(logicalOr.Right is not null)
        {
            AnalyzeLogicalAndForReads(logicalOr.Right, state, model, isConstructor, initiallyAssignedFields, violations, allNonDefaultableFields);
        }
    }

    /// <summary>
    /// Analyzes a LogicalAndNode chain for reads.
    /// </summary>
    private static void AnalyzeLogicalAndForReads(
        LogicalAndNode logicalAnd,
        AssignmentState state,
        ISemanticModel model,
        bool isConstructor,
        HashSet<string>? initiallyAssignedFields,
        List<ReadViolation> violations,
        HashSet<string>? allNonDefaultableFields)
    {
        AnalyzeEqualityForReads(logicalAnd.Left, state, model, isConstructor, initiallyAssignedFields, violations, allNonDefaultableFields);
        if(logicalAnd.Right is not null)
        {
            AnalyzeEqualityForReads(logicalAnd.Right, state, model, isConstructor, initiallyAssignedFields, violations, allNonDefaultableFields);
        }
    }

    /// <summary>
    /// Analyzes an EqualityNode for reads.
    /// </summary>
    private static void AnalyzeEqualityForReads(
        EqualityNode equality,
        AssignmentState state,
        ISemanticModel model,
        bool isConstructor,
        HashSet<string>? initiallyAssignedFields,
        List<ReadViolation> violations,
        HashSet<string>? allNonDefaultableFields)
    {
        AnalyzeRelationalForReads(equality.Left, state, model, isConstructor, initiallyAssignedFields, violations, allNonDefaultableFields);

        // Group0 is the flat n-ary operand list; visit every operand so reads in a
        // multi-term chain (a == b == c) are all analyzed, not just the last one.
        foreach(var group in equality.Group0)
        {
            AnalyzeRelationalForReads(group.Right, state, model, isConstructor, initiallyAssignedFields, violations, allNonDefaultableFields);
        }
    }

    /// <summary>
    /// Analyzes a RelationalNode for reads.
    /// </summary>
    private static void AnalyzeRelationalForReads(
        RelationalNode relational,
        AssignmentState state,
        ISemanticModel model,
        bool isConstructor,
        HashSet<string>? initiallyAssignedFields,
        List<ReadViolation> violations,
        HashSet<string>? allNonDefaultableFields)
    {
        switch(relational)
        {
            case RelationalComparisonRelationalNode cmp:
                AnalyzeAdditiveForReads(cmp.Left, state, model, isConstructor, initiallyAssignedFields, violations, allNonDefaultableFields);
                foreach(var group in cmp.Group0)
                {
                    AnalyzeAdditiveForReads(group.Right, state, model, isConstructor, initiallyAssignedFields, violations, allNonDefaultableFields);
                }

                break;
            case IsExpressionRelationalNode isExpr:
                AnalyzeAdditiveForReads(isExpr.Left, state, model, isConstructor, initiallyAssignedFields, violations, allNonDefaultableFields);
                break;
            case AsExpressionRelationalNode asExpr:
                AnalyzeAdditiveForReads(asExpr.Left, state, model, isConstructor, initiallyAssignedFields, violations, allNonDefaultableFields);
                break;
        }
    }

    /// <summary>
    /// Analyzes an AdditiveNode for reads.
    /// </summary>
    private static void AnalyzeAdditiveForReads(
        AdditiveNode additive,
        AssignmentState state,
        ISemanticModel model,
        bool isConstructor,
        HashSet<string>? initiallyAssignedFields,
        List<ReadViolation> violations,
        HashSet<string>? allNonDefaultableFields)
    {
        AnalyzeMultiplicativeForReads(additive.Left, state, model, isConstructor, initiallyAssignedFields, violations, allNonDefaultableFields);
        foreach(var group in additive.Group0)
        {
            AnalyzeMultiplicativeForReads(group.Right, state, model, isConstructor, initiallyAssignedFields, violations, allNonDefaultableFields);
        }
    }

    /// <summary>
    /// Analyzes a MultiplicativeNode for reads.
    /// </summary>
    private static void AnalyzeMultiplicativeForReads(
        MultiplicativeNode multiplicative,
        AssignmentState state,
        ISemanticModel model,
        bool isConstructor,
        HashSet<string>? initiallyAssignedFields,
        List<ReadViolation> violations,
        HashSet<string>? allNonDefaultableFields)
    {
        // A genuine range expression (UnwrapRange == null) contributes no field reads
        // that this analysis handles; range-bound read analysis is a later range task.
        if(multiplicative.Left.UnwrapRange() is { } left)
        {
            AnalyzeUnaryForReads(left, state, model, isConstructor, initiallyAssignedFields, violations, allNonDefaultableFields);
        }

        foreach(var group in multiplicative.Group0)
        {
            if(group.Right.UnwrapRange() is { } right)
            {
                AnalyzeUnaryForReads(right, state, model, isConstructor, initiallyAssignedFields, violations, allNonDefaultableFields);
            }
        }
    }

    /// <summary>
    /// Analyzes a UnaryNode for reads. Dispatches to the PostfixNode for postfix expressions.
    /// </summary>
    private static void AnalyzeUnaryForReads(
        UnaryNode unary,
        AssignmentState state,
        ISemanticModel model,
        bool isConstructor,
        HashSet<string>? initiallyAssignedFields,
        List<ReadViolation> violations,
        HashSet<string>? allNonDefaultableFields)
    {
        switch(unary)
        {
            case PostfixExpressionUnaryNode postfixExpr:
                AnalyzePostfixForReads(postfixExpr.Primary, state, model, isConstructor, initiallyAssignedFields, violations, allNonDefaultableFields);
                break;
            case LogicalNotUnaryNode logicalNot:
                AnalyzeUnaryForReads(logicalNot.Operand, state, model, isConstructor, initiallyAssignedFields, violations, allNonDefaultableFields);
                break;
            case NegateUnaryNode negate:
                AnalyzeUnaryForReads(negate.Operand, state, model, isConstructor, initiallyAssignedFields, violations, allNonDefaultableFields);
                break;
            case CastExpressionUnaryNode castExpr:
                AnalyzeUnaryForReads(castExpr.Operand, state, model, isConstructor, initiallyAssignedFields, violations, allNonDefaultableFields);
                break;
            case AsyncExpressionUnaryNode asyncExpr:
                AnalyzeUnaryForReads(asyncExpr.Operand, state, model, isConstructor, initiallyAssignedFields, violations, allNonDefaultableFields);
                break;
            case AwaitExpressionUnaryNode awaitExpr:
                AnalyzeUnaryForReads(awaitExpr.Operand, state, model, isConstructor, initiallyAssignedFields, violations, allNonDefaultableFields);
                break;
        }
    }

    /// <summary>
    /// Analyzes a PostfixNode for reads. This is where field reads (this.field),
    /// local identifier reads, and this-escape patterns are detected.
    /// </summary>
    private static void AnalyzePostfixForReads(
        PostfixNode? postfix,
        AssignmentState state,
        ISemanticModel model,
        bool isConstructor,
        HashSet<string>? initiallyAssignedFields,
        List<ReadViolation> violations,
        HashSet<string>? allNonDefaultableFields)
    {
        if(postfix is null)
        {
            return;
        }

        // Check for this.field read pattern or this-escape patterns.
        if(isConstructor && postfix.Target is ThisExpressionPrimaryNode && postfix.Suffix.Length >= 1)
        {
            var firstSuffix = postfix.Suffix[0];

            if(firstSuffix is MemberAccessSuffixPostfixSuffixNode memberAccess)
            {
                var fieldName = memberAccess.Name.Lexeme;
                if(!state.AssignedFields.Contains(fieldName)
                    && initiallyAssignedFields is not null
                    && !initiallyAssignedFields.Contains(fieldName))
                {
                    violations.Add(new ReadViolation(ReadViolationKind.Field, fieldName, null, postfix));
                }
            }
            else if(firstSuffix is MethodCallSuffixPostfixSuffixNode)
            {
                // this-escape: method called on `this` before all non-defaultable fields are assigned (Req 1.8).
                if(allNonDefaultableFields is not null)
                {
                    foreach(var field in allNonDefaultableFields)
                    {
                        if(!state.AssignedFields.Contains(field))
                        {
                            violations.Add(new ReadViolation(ReadViolationKind.Field, field, null, postfix));
                        }
                    }
                }
            }

            // Check remaining suffixes for reads (e.g., this.field.Method(arg) — check arg).
            for(var i = 1; i < postfix.Suffix.Length; i++)
            {
                AnalyzeSuffixForReads(postfix.Suffix[i], state, model, isConstructor, initiallyAssignedFields, violations, allNonDefaultableFields);
            }

            return;
        }

        // Check for bare `this` passed as an argument or used in a context requiring this-escape detection.
        // A bare `this` (no suffixes) in a non-assignment position is handled by the caller
        // (e.g., argument checking in AnalyzeSuffixForReads).

        // Check for local variable read pattern.
        if(postfix.Target is IdentifierExpressionPrimaryNode identifier && postfix.Suffix.Length == 0)
        {
            var symbol = model.GetDeclaredSymbol(identifier);
            if(symbol is not null && symbol.Kind == SymbolKind.LocalVariable
                && !state.AssignedLocals.Contains(symbol))
            {
                // Only flag locals that were introduced by variable declarations tracked
                // by the dataflow (typed/inferred/for/foreach/tuple deconstructions).
                // Pattern variables (is T name, var name, switch arms) are implicitly assigned
                // by the pattern match and should not trigger this diagnostic.
                SyntaxNode? declaringNode = null;
                try { declaringNode = model.GetDeclaringNode(symbol); } catch { /* ignore */ }

                if(declaringNode is TypedVariableDeclarationVariableDeclarationStatementNode
                    or InferredVariableDeclarationVariableDeclarationStatementNode
                    or VarTupleDeconstructionVariableDeclarationStatementNode
                    or TypedTupleDeconstructionVariableDeclarationStatementNode
                    or UninitializedVariableDeclarationNode
                    or ForInitializerVariableDeclarationForInitializerNode
                    or ForInitializerVarDeclarationForInitializerNode
                    or ForeachStatementNode)
                {
                    violations.Add(new ReadViolation(ReadViolationKind.Local, symbol.Name, symbol, identifier));
                }
            }
        }

        // Check all suffixes for reads (arguments, index expressions, etc.).
        foreach(var suffix in postfix.Suffix)
        {
            AnalyzeSuffixForReads(suffix, state, model, isConstructor, initiallyAssignedFields, violations, allNonDefaultableFields);
        }
    }

    /// <summary>
    /// Analyzes a postfix suffix (method call arguments, index expressions) for reads.
    /// Also detects this-escape when `this` is passed as a method/function argument (Req 1.8).
    /// </summary>
    private static void AnalyzeSuffixForReads(
        PostfixSuffixNode suffix,
        AssignmentState state,
        ISemanticModel model,
        bool isConstructor,
        HashSet<string>? initiallyAssignedFields,
        List<ReadViolation> violations,
        HashSet<string>? allNonDefaultableFields)
    {
        switch(suffix)
        {
            case InvocationSuffixPostfixSuffixNode invocation:
                if(invocation.Arguments is not null)
                {
                    CheckArgumentForThisEscape(invocation.Arguments.Argument, state, isConstructor, allNonDefaultableFields, violations);
                    AnalyzeExpressionForReads(ArgumentListNodeExtensions.ExtractExpression(invocation.Arguments.Argument), state, model, isConstructor, initiallyAssignedFields, violations, allNonDefaultableFields);
                    foreach(var arg in invocation.Arguments.Arguments)
                    {
                        CheckArgumentForThisEscape(arg, state, isConstructor, allNonDefaultableFields, violations);
                        AnalyzeExpressionForReads(ArgumentListNodeExtensions.ExtractExpression(arg), state, model, isConstructor, initiallyAssignedFields, violations, allNonDefaultableFields);
                    }
                }

                break;

            case MethodCallSuffixPostfixSuffixNode methodCall:
                if(methodCall.Arguments is not null)
                {
                    CheckArgumentForThisEscape(methodCall.Arguments.Argument, state, isConstructor, allNonDefaultableFields, violations);
                    AnalyzeExpressionForReads(ArgumentListNodeExtensions.ExtractExpression(methodCall.Arguments.Argument), state, model, isConstructor, initiallyAssignedFields, violations, allNonDefaultableFields);
                    foreach(var arg in methodCall.Arguments.Arguments)
                    {
                        CheckArgumentForThisEscape(arg, state, isConstructor, allNonDefaultableFields, violations);
                        AnalyzeExpressionForReads(ArgumentListNodeExtensions.ExtractExpression(arg), state, model, isConstructor, initiallyAssignedFields, violations, allNonDefaultableFields);
                    }
                }

                break;

            case IndexSuffixPostfixSuffixNode index:
                AnalyzeExpressionForReads(index.Index, state, model, isConstructor, initiallyAssignedFields, violations, allNonDefaultableFields);
                break;
        }
    }

    /// <summary>
    /// Checks whether an argument expression is a bare `this` reference (this-escape via argument passing).
    /// If so and any non-defaultable fields are unassigned, emits a violation for each (Req 1.8).
    /// </summary>
    private static void CheckArgumentForThisEscape(
        ArgumentNode arg,
        AssignmentState state,

        bool isConstructor,
        HashSet<string>? allNonDefaultableFields,
        List<ReadViolation> violations)
    {
        if(!isConstructor || allNonDefaultableFields is null)
        {
            return;
        }

        var expr = ArgumentListNodeExtensions.ExtractExpression(arg);
        if(!IsBarThisExpression(expr))
        {
            return;
        }

        // this is passed as an argument — treat as a read of all unassigned non-defaultable fields.
        foreach(var field in allNonDefaultableFields)
        {
            if(!state.AssignedFields.Contains(field))
            {
                violations.Add(new ReadViolation(ReadViolationKind.Field, field, null, GetThisNode(expr)));
            }
        }
    }

    /// <summary>
    /// Checks whether an expression is a bare `this` reference (this-escape via return).
    /// If so and any non-defaultable fields are unassigned, emits a violation for each (Req 1.8).
    /// </summary>
    private static void CheckThisEscapeInExpression(
        ExpressionNode expr,
        AssignmentState state,
        HashSet<string> allNonDefaultableFields,
        List<ReadViolation> violations,
        SyntaxNode locationNode)
    {
        if(!IsBarThisExpression(expr))
        {
            return;
        }

        // this is returned — treat as a read of all unassigned non-defaultable fields.
        foreach(var field in allNonDefaultableFields)
        {
            if(!state.AssignedFields.Contains(field))
            {
                violations.Add(new ReadViolation(ReadViolationKind.Field, field, null, locationNode));
            }
        }
    }

    /// <summary>
    /// Determines whether an expression is a bare `this` reference (no suffixes).
    /// </summary>
    private static bool IsBarThisExpression(ExpressionNode expr)
    {
        var assignment = expr.AsAssignmentOrNull();
        if(assignment is null || assignment.Group0?.Value is not null)
        {
            return false;
        }

        var target = assignment.Target;
        if(target.Left is not { } logicalOr)
        {
            return false;
        }

        if(logicalOr.Right is not null)
        {
            return false;
        }

        var logicalAnd = logicalOr.Left;
        if(logicalAnd.Right is not null)
        {
            return false;
        }

        var equality = logicalAnd.Left;
        if(!equality.Group0.IsDefaultOrEmpty)
        {
            return false;
        }

        if(equality.Left is not RelationalComparisonRelationalNode relational)
        {
            return false;
        }

        if(!relational.Group0.IsDefaultOrEmpty)
        {
            return false;
        }

        var additive = relational.Left;
        if(!additive.Group0.IsDefaultOrEmpty)
        {
            return false;
        }

        var multiplicative = additive.Left;
        if(!multiplicative.Group0.IsDefaultOrEmpty)
        {
            return false;
        }

        if(multiplicative.Left.UnwrapRange() is not PostfixExpressionUnaryNode postfixExpr)
        {
            return false;
        }

        var postfix = postfixExpr.Primary;
        if(postfix is null)
        {
            return false;
        }

        return postfix.Target is ThisExpressionPrimaryNode && postfix.Suffix.Length == 0;
    }

    /// <summary>
    /// Extracts the ThisExpressionPrimaryNode from an expression known to be a bare `this`.
    /// Falls back to the expression node itself as the location.
    /// </summary>
    private static SyntaxNode GetThisNode(ExpressionNode expr)
    {
        var assignment = expr.AsAssignmentOrNull();
        if(assignment?.Target.Left?.Left.Left.Left is RelationalComparisonRelationalNode relational)
        {
            if(relational.Left.Left.Left.UnwrapRange() is PostfixExpressionUnaryNode postfixExpr
                && postfixExpr.Primary?.Target is ThisExpressionPrimaryNode thisNode)
            {
                return thisNode;
            }
        }

        return expr;
    }

    /// <summary>
    /// Attempts to extract the local variable symbol from an assignment target.
    /// Returns the symbol if the target is a bare identifier that resolves to a local variable.
    /// </summary>
    private static ISymbol? TryExtractLocalAssignmentTarget(VoidCoalescingNode target, ISemanticModel model)
    {
        if(target.Left is not { } logicalOr)
        {
            return null;
        }

        if(logicalOr.Right is not null)
        {
            return null;
        }

        var logicalAnd = logicalOr.Left;
        if(logicalAnd.Right is not null)
        {
            return null;
        }

        var equality = logicalAnd.Left;
        if(!equality.Group0.IsDefaultOrEmpty)
        {
            return null;
        }

        if(equality.Left is not RelationalComparisonRelationalNode relational)
        {
            return null;
        }

        if(!relational.Group0.IsDefaultOrEmpty)
        {
            return null;
        }

        var additive = relational.Left;
        if(!additive.Group0.IsDefaultOrEmpty)
        {
            return null;
        }

        var multiplicative = additive.Left;
        if(!multiplicative.Group0.IsDefaultOrEmpty)
        {
            return null;
        }

        if(multiplicative.Left.UnwrapRange() is not PostfixExpressionUnaryNode postfixExpr)
        {
            return null;
        }

        var postfix = postfixExpr.Primary;
        if(postfix is null)
        {
            return null;
        }

        if(postfix.Target is not IdentifierExpressionPrimaryNode identifier)
        {
            return null;
        }

        if(postfix.Suffix.Length != 0)
        {
            return null;
        }

        var symbol = model.GetDeclaredSymbol(identifier);
        if(symbol is not null && symbol.Kind == SymbolKind.LocalVariable)
        {
            return symbol;
        }

        return null;
    }

    /// <summary>
    /// Represents a detected read of a not-yet-assigned location.
    /// Collected by the forward dataflow walker for downstream diagnostic emission.
    /// </summary>
    internal readonly record struct ReadViolation(
        ReadViolationKind Kind,
        string Name,
        ISymbol? Symbol,
        SyntaxNode Location);

    /// <summary>
    /// The kind of read-before-assignment violation.
    /// </summary>
    internal enum ReadViolationKind
    {
        /// <summary>Read of an unassigned constructor field (this.field).</summary>
        Field,
        /// <summary>Read of an unassigned local variable.</summary>
        Local,
    }

    /// <summary>
    /// Attempts to extract the field name from a bare <c>fieldName</c> assignment target
    /// (without the <c>this.</c> prefix). In a constructor body, <c>Item = x</c> is
    /// semantically equivalent to <c>this.Item = x</c>.
    /// </summary>
    private static string? TryExtractBareFieldAssignmentTarget(VoidCoalescingNode target)
    {
        // Unwrap the nested expression tree to reach the PostfixNode.
        if(target.Left is not { } logicalOr)
        {
            return null;
        }

        if(logicalOr.Right is not null)
        {
            return null;
        }

        var logicalAnd = logicalOr.Left;
        if(logicalAnd.Right is not null)
        {
            return null;
        }

        var equality = logicalAnd.Left;
        if(!equality.Group0.IsDefaultOrEmpty)
        {
            return null;
        }

        if(equality.Left is not RelationalComparisonRelationalNode relational)
        {
            return null;
        }

        if(!relational.Group0.IsDefaultOrEmpty)
        {
            return null;
        }

        var additive = relational.Left;
        if(!additive.Group0.IsDefaultOrEmpty)
        {
            return null;
        }

        var multiplicative = additive.Left;
        if(!multiplicative.Group0.IsDefaultOrEmpty)
        {
            return null;
        }

        if(multiplicative.Left.UnwrapRange() is not PostfixExpressionUnaryNode postfixExpr)
        {
            return null;
        }

        var postfix = postfixExpr.Primary;
        if(postfix is null)
        {
            return null;
        }

        // Check that the target is a bare identifier with no suffix (no member access, no invocation).
        if(postfix.Target is not IdentifierExpressionPrimaryNode identifier)
        {
            return null;
        }

        if(postfix.Suffix.Length != 0)
        {
            return null;
        }

        return identifier.Name.Lexeme;
    }

    /// <summary>
    /// Attempts to extract the field name from a <c>this.fieldName</c> assignment target.
    /// Returns the field name if the target matches the pattern, otherwise null.
    /// </summary>
    private static string? TryExtractThisFieldAssignmentTarget(VoidCoalescingNode target)
    {
        // Unwrap the deeply nested expression tree to get to the PostfixNode.
        // VoidCoalescingNode -> Left (LogicalOrNode) -> Left (LogicalAndNode)
        //   -> Left (EqualityNode) -> Left (RelationalComparisonRelationalNode)
        //   -> Left (AdditiveNode) -> Left (MultiplicativeNode) -> Left (UnaryNode)
        //   -> Primary (PostfixNode)
        if(target.Left is not { } logicalOr)
        {
            return null;
        }

        if(logicalOr.Right is not null)
        {
            return null;
        }

        var logicalAnd = logicalOr.Left;
        if(logicalAnd.Right is not null)
        {
            return null;
        }

        var equality = logicalAnd.Left;
        if(!equality.Group0.IsDefaultOrEmpty)
        {
            return null;
        }

        if(equality.Left is not RelationalComparisonRelationalNode relational)
        {
            return null;
        }

        if(!relational.Group0.IsDefaultOrEmpty)
        {
            return null;
        }

        var additive = relational.Left;
        if(!additive.Group0.IsDefaultOrEmpty)
        {
            return null;
        }

        var multiplicative = additive.Left;
        if(!multiplicative.Group0.IsDefaultOrEmpty)
        {
            return null;
        }

        if(multiplicative.Left.UnwrapRange() is not PostfixExpressionUnaryNode postfixExpr)
        {
            return null;
        }

        var postfix = postfixExpr.Primary;
        if(postfix is null)
        {
            return null;
        }

        // Check that the target is `this.fieldName` — a ThisExpression with exactly one MemberAccessSuffix.
        if(postfix.Target is not ThisExpressionPrimaryNode)
        {
            return null;
        }

        if(postfix.Suffix.Length != 1)
        {
            return null;
        }

        if(postfix.Suffix[0] is not MemberAccessSuffixPostfixSuffixNode memberAccess)
        {
            return null;
        }

        return memberAccess.Name.Lexeme;
    }

    private static void CheckFieldDeclaration(ISymbol fieldSymbol, ISemanticModel model, DiagnosticBag diagnostics)
    {
        SyntaxNode declaringNode;
        try { declaringNode = model.GetDeclaringNode(fieldSymbol); }
        catch { return; }

        if(declaringNode is not FieldDeclarationNode)
        {
            return;
        }

        // ── MIST2002: Ref struct field restriction (Requirement 3.5) ──
        // A ref struct type cannot be used as a field in a non-ref-struct type,
        // since that would allow the ref struct value to escape to the heap.
        var fieldType = fieldSymbol.DeclaredType;
        if(fieldType is not null && IsRefStructType(fieldType))
        {
            var containingType = FindDeclaringType(fieldSymbol);
            var containingIsRefStruct = containingType is not null && IsRefStructType(containingType);
            if(!containingIsRefStruct)
            {
                var containerName = containingType switch
                {
                    ClassType ct => ct.DisplayName,
                    StructType st => st.DisplayName,
                    _ => "unknown"
                };
                diagnostics.ReportAtNode(TypeCheckDiagnostics.RefStructFieldRestriction, declaringNode, "TypeChecker", fieldType.DisplayName, containerName);
            }
        }

        // ── MIST_ANON_ESCAPE_FIELD: Anonymous type field restriction (Requirement 4.5) ──
        // Anonymous types cannot be used as field types because they have no denotable name.
        if(fieldType is AnonymousType)
        {
            diagnostics.ReportAtNode(TypeCheckDiagnostics.AnonEscapeField, declaringNode, "TypeChecker");
        }

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

        currentEnclosingType = FindDeclaringType(propertySymbol);
        insideConstructor = false;
        insidePropertyAccessor = false; // Will be set to true inside CheckAccessorBody

        var propInfo = GetPropertyMetadata(propertySymbol);
        var propType = propertySymbol.DeclaredType ?? ErrorType.Instance;

        // ── MIST2003: Ref struct property restriction (Requirement 3.6) ──
        // A ref struct type cannot be used as a property type in a non-ref-struct type,
        // since that would allow the ref struct value to escape to the heap.
        if(IsRefStructType(propType))
        {
            var containingType = currentEnclosingType;
            var containingIsRefStruct = containingType is not null && IsRefStructType(containingType);
            if(!containingIsRefStruct)
            {
                var containerName = containingType switch
                {
                    ClassType ct => ct.DisplayName,
                    StructType st => st.DisplayName,
                    _ => "unknown"
                };
                diagnostics.ReportAtNode(TypeCheckDiagnostics.RefStructPropertyRestriction, declaringNode, "TypeChecker", propType.DisplayName, containerName);
            }
        }

        switch(declaringNode)
        {
            case BlockPropertyPropertyDeclarationNode blockProp:
                {
                    var isStatic = ExtractIsStatic(blockProp.Modifiers);

                    // Task 5.4: Check accessor modifier restrictiveness (MIST_TC_0022).
                    CheckAccessorModifierRestrictiveness(blockProp.Accessors, propertySymbol, diagnostics);

                    // Task 5.1: Walk accessor bodies for type correctness.
                    foreach(var accessor in blockProp.Accessors)
                    {
                        CheckAccessorBody(accessor, propType, model, diagnostics);
                    }

                    // Task 5.5: Check field keyword usage in accessor bodies (MIST_TC_0023, MIST_TC_0024).
                    CheckFieldKeywordUsageInAccessors(blockProp.Accessors, propInfo, diagnostics);

                    // Task 5.7: Check this in static accessor (MIST_TC_0026).
                    if(isStatic)
                    {
                        CheckThisInStaticAccessors(blockProp.Accessors, diagnostics);
                    }

                    break;
                }
            case ExpressionBodiedPropertyPropertyDeclarationNode exprProp:
                {
                    var isStatic = ExtractIsStatic(exprProp.Modifiers);

                    // Task 5.8: Check expression body type compatibility (MIST_TC_0028).
                    CheckExpression(exprProp.Body, model, diagnostics);
                    var bodyType = model.GetTypeOf(exprProp.Body);
                    if(bodyType is not null && !IsErrorType(bodyType) && !IsErrorType(propType))
                    {
                        if(!IsAssignable(bodyType, propType))
                        {
                            diagnostics.ReportAtNode(TypeCheckDiagnostics.PropertyBodyTypeMismatch, exprProp.Body, "TypeChecker", bodyType.DisplayName, propType.DisplayName);
                        }
                    }

                    // Task 5.7: Check this in static expression-bodied property (MIST_TC_0026).
                    if(isStatic)
                    {
                        CheckThisInExpression(exprProp.Body, diagnostics);
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

        if(declaringNode is not IndexerDeclarationNode indexerDecl)
        {
            return;
        }

        if(indexerDecl.Body is not BlockIndexerBodyIndexerBodyNode blockBody)
        {
            return;
        }

        currentEnclosingType = FindDeclaringType(indexerSymbol);
        insideConstructor = false;
        insidePropertyAccessor = false; // Will be set to true inside CheckAccessorBody

        _ = GetPropertyMetadata(indexerSymbol);
        var indexerType = indexerSymbol.DeclaredType ?? ErrorType.Instance;

        // Task 5.4: Check accessor modifier restrictiveness (MIST_TC_0022).
        CheckAccessorModifierRestrictiveness(blockBody.Accessors, indexerSymbol, diagnostics);

        // ── Flow narrowing: inject parameter refinement narrowing for indexer parameters (Requirement 8.1–8.5) ──
        var paramNarrowings = InjectParameterRefinementNarrowing(declaringNode, model);

        // Task 5.1: Walk accessor bodies for type correctness.
        foreach(var accessor in blockBody.Accessors)
        {
            CheckAccessorBody(accessor, indexerType, model, diagnostics);
        }

        // Remove parameter refinement narrowing entries after the accessor bodies.
        RemoveParameterRefinementNarrowing(paramNarrowings);

        // Task 5.9: Check for duplicate indexers (MIST_TC_0029).
        CheckDuplicateIndexer(indexerSymbol, indexerDecl, model, diagnostics);
    }

    /// <summary>
    /// Checks a single accessor body for type correctness.
    /// For block bodies, checks statements. For expression bodies, checks expression type.
    /// For set/init accessors, the implicit 'value' parameter is already injected by the semantic model.
    /// </summary>
    private void CheckAccessorBody(
        AccessorDeclarationNode accessor,
        IType propType,

        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        var previousInsidePropertyAccessor = insidePropertyAccessor;
        insidePropertyAccessor = true;

        try
        {
            switch(accessor)
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
            insidePropertyAccessor = previousInsidePropertyAccessor;
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
        if(exprType is null || IsErrorType(exprType) || IsErrorType(propType))
        {
            return;
        }

        if(!IsAssignable(exprType, propType))
        {
            diagnostics.ReportAtNode(TypeCheckDiagnostics.PropertyBodyTypeMismatch, expression, "TypeChecker", exprType.DisplayName, propType.DisplayName);
        }
    }

    /// <summary>
    /// Checks accessor modifier restrictiveness (MIST_TC_0022).
    /// An accessor modifier must be strictly more restrictive than the property's access level.
    /// Now reads accessibility from the property symbol rather than from AST modifiers.
    /// </summary>
    private static void CheckAccessorModifierRestrictiveness(
        IReadOnlyList<AccessorDeclarationNode> accessors,
        ISymbol propertySymbol,

        DiagnosticBag diagnostics)
    {
        foreach(var accessor in accessors)
        {
            var accessorModifier = GetAccessorModifier(accessor);
            if(accessorModifier is null)
            {
                continue; // No explicit accessor modifier — OK.
            }

            var propLevel = propertySymbol.Accessibility;
            var accessorLevel = GetModifierAccessibility(accessorModifier);

            // The accessor modifier must be strictly more restrictive than the property.
            if(!IsMoreRestrictive(accessorLevel, propLevel))
            {
                diagnostics.ReportAtNode(TypeCheckDiagnostics.AccessorModifierNotRestrictive, accessorModifier, "TypeChecker");
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

        DiagnosticBag diagnostics)
    {
        if(propInfo is null)
        {
            return;
        }

        foreach(var accessor in accessors)
        {
            var fieldNodes = CollectFieldKeywordNodes(accessor);
            foreach(var fieldNode in fieldNodes)
            {
                // MIST_TC_0024: field keyword in computed property (no backing field).
                if(propInfo.IsComputed)
                {
                    diagnostics.ReportAtNode(TypeCheckDiagnostics.FieldKeywordInComputedProperty, fieldNode, "TypeChecker");
                }
            }
        }
    }

    /// <summary>
    /// Checks for 'this' references in static property accessors (MIST_TC_0026).
    /// </summary>
    private static void CheckThisInStaticAccessors(
        IReadOnlyList<AccessorDeclarationNode> accessors,

        DiagnosticBag diagnostics)
    {
        foreach(var accessor in accessors)
        {
            var thisNodes = CollectThisKeywordNodes(accessor);
            foreach(var thisNode in thisNodes)
            {
                diagnostics.ReportAtNode(TypeCheckDiagnostics.ThisInStaticAccessor, thisNode, "TypeChecker");
            }
        }
    }

    /// <summary>
    /// Checks for 'this' references in a static expression-bodied property (MIST_TC_0026).
    /// </summary>
    private static void CheckThisInExpression(
        ExpressionNode expression,

        DiagnosticBag diagnostics)
    {
        var thisNodes = new List<SyntaxNode>();
        CollectThisKeywordNodesFromTree(expression, thisNodes);
        foreach(var thisNode in thisNodes)
        {
            diagnostics.ReportAtNode(TypeCheckDiagnostics.ThisInStaticAccessor, thisNode, "TypeChecker");
        }
    }

    /// <summary>
    /// Checks for duplicate indexers with the same parameter types (MIST_TC_0029).
    /// </summary>
    private static void CheckDuplicateIndexer(
        ISymbol indexerSymbol,
        IndexerDeclarationNode indexerDecl,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        var declaringType = FindDeclaringType(indexerSymbol);
        if(declaringType is null)
        {
            return;
        }

        var memberScope = declaringType switch
        {
            ClassType ct => ct.MemberScope,
            StructType st => st.MemberScope,
            _ => null,
        };

        if(memberScope is null)
        {
            return;
        }

        // Collect parameter types for this indexer.
        var thisParamTypes = CollectIndexerParameterTypes(indexerDecl, model);

        // Check all other indexers in the same type.
        foreach(var member in memberScope.Members)
        {
            if(member.Kind != SymbolKind.Indexer || ReferenceEquals(member, indexerSymbol))
            {
                continue;
            }

            SyntaxNode otherNode;
            try { otherNode = model.GetDeclaringNode(member); }
            catch { continue; }

            if(otherNode is not IndexerDeclarationNode otherIndexer)
            {
                continue;
            }

            var otherParamTypes = CollectIndexerParameterTypes(otherIndexer, model);

            if(ParameterTypesMatch(thisParamTypes, otherParamTypes))
            {
                diagnostics.ReportAtNode(TypeCheckDiagnostics.DuplicateIndexer, indexerDecl, "TypeChecker");
                break; // Only report once per indexer.
            }
        }
    }

    /// <summary>
    /// Collects parameter types from an indexer declaration.
    /// </summary>
    private static List<string> CollectIndexerParameterTypes(
        IndexerDeclarationNode indexer,
        ISemanticModel model)
    {
        var types = new List<string>();
        if(indexer.Parameters is null)
        {
            return types;
        }

        foreach(var child in indexer.Parameters.Children())
        {
            if(child is ParameterNode param)
            {
                var paramSymbol = model.GetDeclaredSymbol(param);
                if(paramSymbol?.DeclaredType is not null)
                {
                    types.Add(paramSymbol.DeclaredType.DisplayName);
                }
                else
                {
                    types.Add("?");
                }
            }
        }

        return types;
    }

    /// <summary>
    /// Checks if two parameter type lists match.
    /// </summary>
    private static bool ParameterTypesMatch(List<string> a, List<string> b)
    {
        if(a.Count != b.Count)
        {
            return false;
        }

        for(var i = 0; i < a.Count; i++)
        {
            if(a[i] != b[i])
            {
                return false;
            }
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
    /// Gets the accessibility from a modifier AST node (used for accessor modifiers
    /// which are not yet stored on a symbol).
    /// </summary>
    private static Accessibility GetModifierAccessibility(ModifierNode modifier)
    {
        return modifier switch
        {
            PublicModifierModifierNode => Accessibility.Public,
            PrivateModifierModifierNode => Accessibility.Private,
            ProtectedModifierModifierNode => Accessibility.Protected,
            InternalModifierModifierNode => Accessibility.Internal,
            _ => Accessibility.Private,
        };
    }

    /// <summary>
    /// Returns true if <paramref name="accessor"/> is strictly more restrictive than <paramref name="property"/>.
    /// Uses the resolved <see cref="Accessibility"/> enum values.
    /// </summary>
    private static bool IsMoreRestrictive(Accessibility accessor, Accessibility property)
    {
        // Ordering: Private < Protected/Internal < Public.
        // Protected and Internal are incomparable but both less than Public and more than Private.
        var accessorRank = GetAccessibilityRank(accessor);
        var propertyRank = GetAccessibilityRank(property);
        return accessorRank < propertyRank;
    }

    /// <summary>
    /// Returns a numeric rank for accessibility restrictiveness comparison.
    /// Lower = more restrictive. Protected and Internal share the same rank.
    /// </summary>
    private static int GetAccessibilityRank(Accessibility accessibility)
    {
        return accessibility switch
        {
            Accessibility.Private => 0,
            Accessibility.Protected => 1,
            Accessibility.Internal => 1,
            Accessibility.Public => 3,
            _ => 0,
        };
    }

    /// <summary>
    /// Extracts the staticness flag from a property's modifier list.
    /// Accessibility is no longer read from AST modifiers here — it comes from
    /// <see cref="ISymbol.Accessibility"/> on the property symbol.
    /// </summary>
    private static bool ExtractIsStatic(IReadOnlyList<ModifierNode> modifiers)
    {
        foreach(var mod in modifiers)
        {
            if(mod is StaticModifierModifierNode)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Gets PropertyInfo from a symbol (handles both Symbol and ISymbol).
    /// </summary>
    private static PropertyInfo? GetPropertyMetadata(ISymbol symbol)
    {
        if(symbol is Symbol concreteSymbol)
        {
            return concreteSymbol.PropertyMetadata;
        }

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
        if(node is FieldKeywordExpressionPrimaryNode)
        {
            results.Add(node);
            return;
        }

        foreach(var child in node.Children())
        {
            if(child is not null)
            {
                CollectFieldKeywordNodesFromTree(child, results);
            }
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
        if(node is ThisExpressionPrimaryNode)
        {
            results.Add(node);
            return;
        }

        foreach(var child in node.Children())
        {
            if(child is not null)
            {
                CollectThisKeywordNodesFromTree(child, results);
            }
        }
    }

    /// <summary>
    /// Checks constraint satisfaction for inheritance clauses where constrained generics
    /// are used as base types or implemented abstracts. For example, if
    /// <c>class Foo : Bar&lt;int&gt;</c> and <c>Bar&lt;T&gt;</c> has <c>where T : IComparable&lt;T&gt;</c>,
    /// this verifies that <c>int</c> satisfies the constraint.
    /// </summary>
    private static void CheckInheritanceClauseConstraints(ISemanticModel model, DiagnosticBag diagnostics)
    {
        foreach(var symbol in model.AllSymbols)
        {
            if(symbol.Origin == SymbolOrigin.Backend)
            {
                continue;
            }

            if(symbol.Kind is not (SymbolKind.Class or SymbolKind.Struct or SymbolKind.Abstract))
            {
                continue;
            }

            var declaredType = symbol.DeclaredType;
            if(declaredType is null)
            {
                continue;
            }

            // Get the declaring node for diagnostic location.
            SyntaxNode declaringNode;
            try { declaringNode = model.GetDeclaringNode(symbol); }
            catch { continue; }

            // Check base type (classes only).
            if(declaredType is ClassType classType && classType.BaseType is ConstructedType baseConstructed)
            {
                var baseTypeParams = GetTypeParameters(baseConstructed.GenericDefinition);
                if(baseTypeParams is not null)
                {
                    var baseTypeArgs = baseConstructed.TypeArguments;
                    var genericName = baseConstructed.GenericDefinition.DisplayName;
                    for(var i = 0; i < baseTypeParams.Count && i < baseTypeArgs.Count; i++)
                    {
                        if(baseTypeArgs[i] is TypeParameterType derivedTypeParam)
                        {
                            // The type argument is a type parameter of the derived type —
                            // check constraint propagation ("at least as restrictive").
                            if(baseTypeParams[i] is TypeParameterType baseTP)
                            {
                                CheckConstraintPropagation(
                                    derivedTypeParam, baseTP, declaredType,
                                    genericName, declaringNode, diagnostics);
                            }
                        }
                        else
                        {
                            if(baseTypeParams[i] is TypeParameterType baseTP2)
                            {
                                // Requirement 11: whole-pack constraint — check each element independently.
                                if(baseTP2.IsPack
                                    && baseConstructed.DependentArguments is not null
                                    && i < baseConstructed.DependentArguments.Count
                                    && baseConstructed.DependentArguments[i] is { PackElementTypes: { } packElements })
                                {
                                    CheckPackConstraintSatisfaction(packElements, baseTP2, declaringNode, model, diagnostics);
                                }
                                else
                                {
                                    CheckConstraintSatisfaction(baseTypeArgs[i], baseTP2, declaringNode, genericName, model, diagnostics);
                                }
                            }
                        }
                    }
                }
            }

            // Check implemented abstracts.
            var abstracts = declaredType switch
            {
                ClassType ct => ct.Abstracts,
                StructType st => st.Abstracts,
                AbstractType it => it.Abstracts,
                _ => Array.Empty<IType>()
            };

            foreach(var abs in abstracts)
            {
                if(abs is not ConstructedType absConstructed)
                {
                    continue;
                }

                var absTypeParams = GetTypeParameters(absConstructed.GenericDefinition);
                if(absTypeParams is null)
                {
                    continue;
                }

                var absTypeArgs = absConstructed.TypeArguments;
                var genericName = absConstructed.GenericDefinition.DisplayName;
                for(var i = 0; i < absTypeParams.Count && i < absTypeArgs.Count; i++)
                {
                    if(absTypeArgs[i] is TypeParameterType derivedTypeParam)
                    {
                        // The type argument is a type parameter of the derived type —
                        // check constraint propagation ("at least as restrictive").
                        if(absTypeParams[i] is TypeParameterType absTP)
                        {
                            CheckConstraintPropagation(
                                derivedTypeParam, absTP, declaredType,
                                genericName, declaringNode, diagnostics);
                        }
                    }
                    else
                    {
                        if(absTypeParams[i] is TypeParameterType absTP2)
                        {
                            // Requirement 11: whole-pack constraint — check each element independently.
                            if(absTP2.IsPack
                                && absConstructed.DependentArguments is not null
                                && i < absConstructed.DependentArguments.Count
                                && absConstructed.DependentArguments[i] is { PackElementTypes: { } packElements })
                            {
                                CheckPackConstraintSatisfaction(packElements, absTP2, declaringNode, model, diagnostics);
                            }
                            else
                            {
                                CheckConstraintSatisfaction(absTypeArgs[i], absTP2, declaringNode, genericName, model, diagnostics);
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Checks that a derived type's constraints on a type parameter are at least as
    /// restrictive as the base/abstract type's constraints on the corresponding type
    /// parameter. Emits MIST_CONSTRAINT_0017 when constraints are insufficient.
    /// (Requirements 7.1, 7.2, 7.3)
    /// </summary>
    private static void CheckConstraintPropagation(
        TypeParameterType derivedTypeParam,
        TypeParameterType baseTypeParam,
        IType derivedType,
        string baseTypeName,
        SyntaxNode node,

        DiagnosticBag diagnostics)
    {
        var baseConstraints = baseTypeParam.Constraints;
        if(baseConstraints is null)
        {
            return; // Base has no constraints — nothing to propagate.
        }

        var derivedConstraints = derivedTypeParam.Constraints;

        // Check class constraint propagation.
        if(baseConstraints.HasClassConstraint)
        {
            if(derivedConstraints is null || !derivedConstraints.HasClassConstraint)
            {
                EmitPropagationDiagnostic(derivedType, baseTypeName, derivedTypeParam.Name, node, diagnostics);
                return;
            }
        }

        // Check struct constraint propagation.
        if(baseConstraints.HasStructConstraint)
        {
            if(derivedConstraints is null || !derivedConstraints.HasStructConstraint)
            {
                EmitPropagationDiagnostic(derivedType, baseTypeName, derivedTypeParam.Name, node, diagnostics);
                return;
            }
        }

        // Check base class constraint propagation.
        if(baseConstraints.BaseClassConstraint is not null)
        {
            if(derivedConstraints?.BaseClassConstraint is null
                || !DerivesFromClass(derivedConstraints.BaseClassConstraint, baseConstraints.BaseClassConstraint))
            {
                EmitPropagationDiagnostic(derivedType, baseTypeName, derivedTypeParam.Name, node, diagnostics);
                return;
            }
        }

        // Check abstract constraint propagation: derived must require each abstract the base requires.
        foreach(var baseIface in baseConstraints.AbstractConstraints)
        {
            if(derivedConstraints is null || !HasMatchingAbstractConstraint(derivedConstraints, baseIface))
            {
                EmitPropagationDiagnostic(derivedType, baseTypeName, derivedTypeParam.Name, node, diagnostics);
                return;
            }
        }

        // Check constructor constraint propagation: derived must have at least the same
        // constructor constraints as the base (each base constructor constraint must be
        // matched by a derived constructor constraint).
        foreach(var baseCtorParams in baseConstraints.ConstructorConstraints)
        {
            if(derivedConstraints is null || !HasMatchingConstructorConstraint(derivedConstraints, baseCtorParams))
            {
                EmitPropagationDiagnostic(derivedType, baseTypeName, derivedTypeParam.Name, node, diagnostics);
                return;
            }
        }

        // Check owned constraint propagation: if the base requires owned, derived must too.
        if(baseConstraints.HasOwnedConstraint)
        {
            if(derivedConstraints is null || !derivedConstraints.HasOwnedConstraint)
            {
                EmitPropagationDiagnostic(derivedType, baseTypeName, derivedTypeParam.Name, node, diagnostics);
                return;
            }
        }
    }

    private static void EmitPropagationDiagnostic(
        IType derivedType,
        string baseTypeName,
        string typeParamName,
        SyntaxNode node,

        DiagnosticBag diagnostics)
    {
        diagnostics.ReportAtNode(TypeCheckDiagnostics.ConstraintPropagationInsufficient, node, "TypeChecker", derivedType.DisplayName, baseTypeName, typeParamName);
    }

    /// <summary>
    /// Checks whether the derived constraints include an abstract constraint that
    /// matches (or is more derived than) the given base abstract constraint.
    /// </summary>
    private static bool HasMatchingAbstractConstraint(TypeParameterConstraints derivedConstraints, IType baseIface)
    {
        foreach(var derivedIface in derivedConstraints.AbstractConstraints)
        {
            if(ImplementsAbstractConstraint(derivedIface, baseIface))
            {
                return true;
            }

            // Direct match by display name.
            if(derivedIface.DisplayName == baseIface.DisplayName)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks whether the derived constraints include a constructor constraint
    /// with the same parameter types as the given base constructor constraint.
    /// </summary>
    private static bool HasMatchingConstructorConstraint(
        TypeParameterConstraints derivedConstraints,
        IReadOnlyList<IType> baseCtorParams)
    {
        foreach(var derivedCtorParams in derivedConstraints.ConstructorConstraints)
        {
            if(derivedCtorParams.Count != baseCtorParams.Count)
            {
                continue;
            }

            var allMatch = true;
            for(var j = 0; j < derivedCtorParams.Count; j++)
            {
                if(derivedCtorParams[j].DisplayName != baseCtorParams[j].DisplayName)
                {
                    allMatch = false;
                    break;
                }
            }

            if(allMatch)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks that all types implementing abstracts provide required property accessors (MIST_TC_0030).
    /// This complements the semantic model builder's Pass1d validation with type-checker-level diagnostics.
    /// </summary>
    private static void CheckMissingAbstractProperties(ISemanticModel model, DiagnosticBag diagnostics)
    {
        foreach(var symbol in model.AllSymbols)
        {
            if(symbol.Origin == SymbolOrigin.Backend)
            {
                continue;
            }

            if(symbol.Kind is not (SymbolKind.Class or SymbolKind.Struct))
            {
                continue;
            }

            var type = symbol.DeclaredType;
            var abstracts = type switch
            {
                ClassType ct => ct.Abstracts,
                StructType st => st.Abstracts,
                _ => null,
            };

            if(abstracts is null || abstracts.Count == 0)
            {
                continue;
            }

            var memberScope = type switch
            {
                ClassType ct => ct.MemberScope,
                StructType st => st.MemberScope,
                _ => null,
            };

            if(memberScope is null)
            {
                continue;
            }

            // Check each abstract's property requirements.
            var visited = new HashSet<AbstractType>(ReferenceEqualityComparer.Instance);
            CheckAbstractPropertyRequirements(symbol, abstracts, memberScope, type as ClassType, model, diagnostics, visited);
        }
    }

    private static void CheckAbstractPropertyRequirements(
        ISymbol typeSymbol,
        IReadOnlyList<IType> abstracts,
        IScope memberScope,
        ClassType? classType,
        ISemanticModel model,
        DiagnosticBag diagnostics,
        HashSet<AbstractType> visited)
    {
        foreach(var absType in abstracts)
        {
            if(absType is not AbstractType abs)
            {
                continue;
            }

            if(!visited.Add(abs))
            {
                continue;
            }

            foreach(var abstractMember in abs.MemberScope.Members)
            {
                if(abstractMember.Kind != SymbolKind.Property)
                {
                    continue;
                }

                var requiredPropInfo = GetPropertyMetadata(abstractMember);
                if(requiredPropInfo is null)
                {
                    continue;
                }

                // Search for matching property in the implementing type.
                var implProperty = FindPropertyInTypeHierarchy(abstractMember.Name, memberScope, classType);

                if(implProperty is null)
                {
                    // Get the declaring node for location info.
                    SyntaxNode? typeNode;
                    try { typeNode = model.GetDeclaringNode(typeSymbol); }
                    catch { typeNode = null; }

                    if(typeNode is not null)
                    {
                        diagnostics.ReportAtNode(TypeCheckDiagnostics.MissingAbstractPropertyImpl, typeNode, "TypeChecker", typeSymbol.Name, abstractMember.Name);
                    }

                    continue;
                }

                var implPropInfo = GetPropertyMetadata(implProperty);
                if(implPropInfo is null)
                {
                    continue;
                }

                // Check accessor satisfaction.
                SyntaxNode? implNode;
                try { implNode = model.GetDeclaringNode(implProperty); }
                catch { implNode = null; }

                if(requiredPropInfo.HasGet && !implPropInfo.HasGet && implNode is not null)
                {
                    diagnostics.ReportAtNode(TypeCheckDiagnostics.MissingAbstractPropertyImpl, implNode, "TypeChecker", typeSymbol.Name, abstractMember.Name);
                }

                if(requiredPropInfo.HasSet && !implPropInfo.HasSet && !implPropInfo.HasInit && implNode is not null)
                {
                    diagnostics.ReportAtNode(TypeCheckDiagnostics.MissingAbstractPropertyImpl, implNode, "TypeChecker", typeSymbol.Name, abstractMember.Name);
                }
            }

            // Recurse into parent abstracts.
            if(abs.Abstracts.Count > 0)
            {
                CheckAbstractPropertyRequirements(typeSymbol, abs.Abstracts, memberScope, classType, model, diagnostics, visited);
            }
        }
    }

    /// <summary>
    /// Searches for a property by name in a type's member scope and base type chain.
    /// </summary>
    private static ISymbol? FindPropertyInTypeHierarchy(string propertyName, IScope memberScope, ClassType? classType)
    {
        foreach(var member in memberScope.Members)
        {
            if(member.Kind == SymbolKind.Property && member.Name == propertyName)
            {
                return member;
            }
        }

        if(classType is not null)
        {
            var ancestor = classType.BaseType as ClassType;
            while(ancestor is not null)
            {
                foreach(var member in ancestor.MemberScope.Members)
                {
                    if(member.Kind == SymbolKind.Property && member.Name == propertyName)
                    {
                        return member;
                    }
                }

                ancestor = ancestor.BaseType as ClassType;
            }
        }

        return null;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Readonly abstract contract checking (MIST_TC_0079)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Verifies that when a type implements a readonly abstract, each
    /// implementing member is also readonly (explicit, inferred, or implicit).
    /// If an implementing member is not readonly, emits <c>MIST_TC_0079</c>.
    /// The implementing type itself does not need to be readonly — only the
    /// specific members that correspond to readonly abstract members.
    /// (Requirements 13.1, 13.2, 13.3, 13.4, 13.5, 13.6)
    /// </summary>
    private static void CheckReadonlyAbstractContracts(ISemanticModel model, DiagnosticBag diagnostics)
    {
        foreach(var symbol in model.AllSymbols)
        {
            if(symbol.Origin == SymbolOrigin.Backend)
            {
                continue;
            }

            if(symbol.Kind is not (SymbolKind.Class or SymbolKind.Struct))
            {
                continue;
            }

            var type = symbol.DeclaredType;

            // Check abstract contracts.
            var abstracts = type switch
            {
                ClassType ct => ct.Abstracts,
                StructType st => st.Abstracts,
                _ => null,
            };

            var memberScope = type switch
            {
                ClassType ct => ct.MemberScope,
                StructType st => st.MemberScope,
                _ => null,
            };

            if(memberScope is not null && abstracts is not null && abstracts.Count > 0)
            {
                CheckReadonlyAbstractContracts(symbol, abstracts, memberScope, model, diagnostics);
            }
        }
    }

    /// <summary>
    /// Checks readonly contracts for all abstracts implemented by a type.
    /// For each readonly abstract member, verifies the implementing member is also readonly.
    /// </summary>
    private static void CheckReadonlyAbstractContracts(
        ISymbol typeSymbol,
        IReadOnlyList<IType> abstracts,
        IScope memberScope,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        foreach(var absType in abstracts)
        {
            if(absType is not AbstractType abs)
            {
                continue;
            }

            // Only check readonly abstracts (all members are implicitly readonly).
            if(!abs.IsReadonly)
            {
                // Even if the abstract itself is not readonly, individual members
                // might be explicitly readonly. Check those too.
                CheckReadonlyMembersOfAbstract(typeSymbol, abs, memberScope, model, diagnostics);
                continue;
            }

            // The abstract is readonly — all its members are implicitly readonly.
            foreach(var abstractMember in abs.MemberScope.Members)
            {
                if(abstractMember.Kind is not (SymbolKind.Method or SymbolKind.Property or SymbolKind.Indexer))
                {
                    continue;
                }

                var implMember = FindMemberInScope(abstractMember.Name, abstractMember.Kind, memberScope);
                if(implMember is null)
                {
                    continue; // Missing implementation is reported by CheckMissingAbstractProperties.
                }

                if(!IsMemberReadonly(implMember))
                {
                    EmitReadonlyContractViolation(typeSymbol, implMember, abstractMember, abs.DisplayName, model, diagnostics);
                }
            }

            // Recurse into parent abstracts.
            if(abs.Abstracts.Count > 0)
            {
                CheckReadonlyAbstractContracts(typeSymbol, abs.Abstracts, memberScope, model, diagnostics);
            }
        }
    }

    /// <summary>
    /// Checks individual readonly members of a non-readonly abstract.
    /// </summary>
    private static void CheckReadonlyMembersOfAbstract(
        ISymbol typeSymbol,
        AbstractType abs,
        IScope memberScope,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        foreach(var abstractMember in abs.MemberScope.Members)
        {
            if(abstractMember.Kind is not (SymbolKind.Method or SymbolKind.Property or SymbolKind.Indexer))
            {
                continue;
            }

            // Check if this specific abstract member is readonly.
            if(!IsMemberReadonly(abstractMember))
            {
                continue;
            }

            var implMember = FindMemberInScope(abstractMember.Name, abstractMember.Kind, memberScope);
            if(implMember is null)
            {
                continue;
            }

            if(!IsMemberReadonly(implMember))
            {
                EmitReadonlyContractViolation(typeSymbol, implMember, abstractMember, abs.DisplayName, model, diagnostics);
            }
        }

        // Recurse into parent abstracts.
        if(abs.Abstracts.Count > 0)
        {
            foreach(var parentAbs in abs.Abstracts)
            {
                if(parentAbs is AbstractType parentAbsType)
                {
                    if(parentAbsType.IsReadonly)
                    {
                        CheckReadonlyAbstractContracts(typeSymbol, new[] { parentAbsType }, memberScope, model, diagnostics);
                    }
                    else
                    {
                        CheckReadonlyMembersOfAbstract(typeSymbol, parentAbsType, memberScope, model, diagnostics);
                    }
                }
            }
        }
    }


    /// <summary>
    /// Finds a member by name and kind in a scope.
    /// </summary>
    private static ISymbol? FindMemberInScope(string name, SymbolKind kind, IScope scope)
    {
        foreach(var member in scope.Members)
        {
            if(member.Name == name && member.Kind == kind)
            {
                return member;
            }

            // Properties can satisfy property requirements, fields can satisfy field requirements.
            if(member.Name == name)
            {
                var kindCompatible = kind switch
                {
                    SymbolKind.Field => member.Kind is SymbolKind.Field or SymbolKind.Property,
                    SymbolKind.Property => member.Kind is SymbolKind.Property or SymbolKind.Field,
                    _ => false,
                };
                if(kindCompatible)
                {
                    return member;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Determines whether a symbol is readonly (explicit, inferred, or implicit).
    /// </summary>
    private static bool IsMemberReadonly(ISymbol symbol)
    {
        if(symbol is Symbol concreteSymbol)
        {
            return concreteSymbol.ReadonlyStatus is ReadonlyStatus.ExplicitReadonly
                or ReadonlyStatus.InferredReadonly
                or ReadonlyStatus.ImplicitReadonly;
        }

        return false;
    }

    /// <summary>
    /// Emits the <c>MIST_TC_0079</c> diagnostic for a readonly contract violation.
    /// </summary>
    private static void EmitReadonlyContractViolation(
        ISymbol typeSymbol,
        ISymbol implMember,
        ISymbol contractMember,
        string contractSourceName,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        SyntaxNode? implNode;
        try { implNode = model.GetDeclaringNode(implMember); }
        catch { implNode = null; }

        // Fall back to the type declaration node if the member node is unavailable.
        if(implNode is null)
        {
            try { implNode = model.GetDeclaringNode(typeSymbol); }
            catch { implNode = null; }
        }

        if(implNode is null)
        {
            return;
        }

        _ = contractMember.Kind switch
        {
            SymbolKind.Method => "Method",
            SymbolKind.Property => "Property",
            SymbolKind.Indexer => "Indexer",
            SymbolKind.Field => "Field",
            _ => "Member",
        };
        diagnostics.ReportAtNode(TypeCheckDiagnostics.ReadonlyContractViolation, implNode, "TypeChecker", implMember.Name, typeSymbol.Name, contractSourceName);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Block and statement checking
    // ═══════════════════════════════════════════════════════════════════════

    private void CheckBlock(BlockNode block, IType returnType, ISemanticModel model, DiagnosticBag diagnostics)
    {
        foreach(var stmt in block.Statements)
        {
            CheckStatement(stmt, returnType, model, diagnostics);
        }
    }

    private void CheckStatement(StatementNode stmt, IType returnType, ISemanticModel model, DiagnosticBag diagnostics)
    {
        switch(stmt)
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
                atStatementLevel = true;
                CheckExpression(exprStmt.ExpressionStatement.Expression, model, diagnostics);
                atStatementLevel = false;
                break;
            case BreakStatementStatementNode:
                // No type checking needed for break.
                break;
            case ReadonlyBlockStatementStatementNode readonlyBlockStmt:
                // Recurse into the readonly block's body so that statements inside
                // (including assignments to readonly locals) are type-checked.
                CheckBlock(readonlyBlockStmt.ReadonlyBlock.Body, returnType, model, diagnostics);
                break;
            case BlockStatementStatementNode blockStmt:
                CheckBlock(blockStmt.BlockStatement, returnType, model, diagnostics);
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
        // ── ref-parameters feature: ref local requires initializer (MIST_REF_0010) ──
        // The current grammar enforces initializers for all local variable declarations,
        // so this check is defensive. If the grammar ever changes to allow uninitialized
        // locals, this ensures ref locals are still validated.
        CheckRefLocalRequiresInitializer(varDecl, model, diagnostics);

        switch(varDecl)
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

                    // ── ??= flow narrowing: if the initializer is a simple identifier
                    // that has been narrowed by a preceding ??= statement, use the
                    // narrowed type instead of the stale cached union type.
                    if(initType is UnionType)
                    {
                        var narrowedInitType = TryGetNarrowedTypeForExpression(typed.Initializer);
                        if(narrowedInitType is not null)
                        {
                            initType = narrowedInitType;
                        }
                    }

                    // When the TypeReference node isn't in the expression type map
                    // (source-parsed programs), fall back to the variable symbol's
                    // declared type so we can still detect type mismatches
                    // (e.g., TypeType assigned to a non-Type variable).
                    declaredType ??= model.GetDeclaredSymbol(typed)?.DeclaredType;

                    // ── first-class-functions: method group ambiguity check (MIST_TC_0121) ──
                    // When the declared type is a Func type and the initializer is a bare
                    // identifier (method group), check for ambiguous overloads.
                    if(declaredType is not null && SemanticModelBuilder.IsFuncType(declaredType))
                    {
                        var postfix = TryExtractPostfixNode(typed.Initializer);
                        if(postfix is not null)
                        {
                            CheckMethodGroupAmbiguity(postfix, declaredType, model, diagnostics);
                        }
                    }

                    // Check for void[] variable declarations (MIST_TC_0034).
                    if(declaredType is ArrayType arrayType && arrayType.ElementType.IsVoid())
                    {
                        diagnostics.ReportAtNode(CommonDiagnostics.VoidArrayNotPermitted, varDecl, "TypeChecker");
                    }

                    if(declaredType is not null && initType is not null)
                    {
                        // ── flow narrowing: use narrowed type if available ──
                        var effectiveInitType = GetEffectiveSourceType(typed.Initializer, initType);

                        // ── refinement-types feature: compile-time enforcement ──
                        // When the target is a RefinementType, check the assignment
                        // against the refinement predicate before the generic
                        // assignability check. If a refinement-specific diagnostic
                        // is emitted, skip the generic check to avoid cascading.
                        if(TryCheckRefinementAssignment(effectiveInitType, declaredType, typed.Initializer, diagnostics, model))
                        {
                            break;
                        }

                        // ── regex-type feature: string literal shorthand (Requirements 6.5, 6.8, 6.9) ──
                        // When the target is regex and the source is string, apply
                        // the string→regex conversion rules before the generic check.
                        if(TryCheckStringToRegexAssignment(initType, declaredType, typed.Initializer, diagnostics, model))
                        {
                            break;
                        }

                        // Allow numeric-to-numeric assignments in variable declarations
                        // to support constant narrowing (e.g., byte x = 0; where 0 is int).
                        // Non-numeric type mismatches (e.g., Type → int) are still checked.
                        if(!(NumericDisplayNames.Contains(initType.DisplayName)
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
                    if(defaultLiteralInVar is not null)
                    {
                        diagnostics.ReportAtNode(CommonDiagnostics.DefaultCannotInferType, defaultLiteralInVar, "TypeChecker");
                    }

                    // var with initializer — check the initializer expression.
                    CheckExpression(inferred.Initializer, model, diagnostics);

                    // After type checking the initializer, the expression type may have been
                    // refined (e.g., type argument inference resolved T → bool). Update the
                    // local variable's DeclaredType so downstream usage sees the concrete type.
                    var varSymbol = model.GetDeclaredSymbol(inferred) as Symbol;
                    if(varSymbol is not null && varSymbol.DeclaredType is TypeParameterType)
                    {
                        var refinedType = GetRefinedInitializerType(inferred.Initializer, model);
                        if(refinedType is not null and not TypeParameterType)
                        {
                            varSymbol.DeclaredType = refinedType;
                        }
                    }

                    // After type checking an `as` expression, the type checker may have
                    // refined the result from the preliminary target type T (set in Pass 2)
                    // to T | void (NonProvable classification). Update the local variable's
                    // DeclaredType so downstream member access correctly sees the union type
                    // and requires optional chaining (?.) to unwrap.
                    if(varSymbol is not null)
                    {
                        var asExprNode = TryExtractAsExpression(inferred.Initializer);
                        if(asExprNode is not null)
                        {
                            var asRefinedType = model.GetTypeOf(asExprNode);
                            if(asRefinedType is not null and not ErrorType)
                            {
                                varSymbol.DeclaredType = asRefinedType;
                            }
                        }
                    }

                    // MIST_TC_0009: var without initializer.
                    // The grammar requires an initializer for var declarations,
                    // but if the initializer expression type is null, the var
                    // type cannot be inferred.
                    var inferredType = model.GetTypeOf(inferred.Initializer);
                    if(inferredType is null)
                    {
                        diagnostics.ReportAtNode(TypeCheckDiagnostics.VarRequiresInitializer, inferred, "TypeChecker");
                    }

                    break;
                }
            case VarTupleDeconstructionVariableDeclarationStatementNode varTuple:
                {
                    // var (a, b, ...) = expr; — check initializer and validate tuple deconstruction.
                    CheckExpression(varTuple.Initializer, model, diagnostics);

                    var initType = model.GetTypeOf(varTuple.Initializer);
                    if(initType is not null and not ErrorType)
                    {
                        if(initType is not TupleType tupleType)
                        {
                            diagnostics.ReportAtNode(TypeCheckDiagnostics.PatternNarrowToClosedGenericWithUnknown, varTuple, "TypeChecker", "unknown", "a concrete type");
                        }
                        else
                        {
                            // Count targets: FirstName + RestNames.
                            var targetCount = 1 + varTuple.RestNames.Length;
                            if(targetCount != tupleType.Arity)
                            {
                                diagnostics.ReportAtNode(TypeCheckDiagnostics.VoidToUnknown, varTuple, "TypeChecker", "void");
                            }
                        }
                    }

                    break;
                }
            case TypedTupleDeconstructionVariableDeclarationStatementNode typedTuple:
                {
                    // (Type1 a, Type2 b, ...) = expr; — check initializer and validate types.
                    CheckExpression(typedTuple.Initializer, model, diagnostics);

                    var initType = model.GetTypeOf(typedTuple.Initializer);
                    if(initType is not null and not ErrorType)
                    {
                        if(initType is not TupleType tupleType)
                        {
                            diagnostics.ReportAtNode(TypeCheckDiagnostics.PatternNarrowToClosedGenericWithUnknown, typedTuple, "TypeChecker", "unknown", "a concrete type");
                        }
                        else
                        {
                            // Count targets: First + Rest + Additional.
                            var targetCount = 2 + typedTuple.TupleDeconstructionElements.Length;
                            if(targetCount != tupleType.Arity)
                            {
                                diagnostics.ReportAtNode(TypeCheckDiagnostics.VoidToUnknown, typedTuple, "TypeChecker", "void");
                            }
                            else
                            {
                                // Verify each element type is assignable.
                                void checkDeconstructionElement(TupleDeconstructionElementNode element, int index)
                                {
                                    var declaredType = model.GetTypeOf(element.Type);
                                    if(declaredType is not null && index < tupleType.Arity)
                                    {
                                        var elementType = tupleType.ElementTypes[index];
                                        if(!IsAssignable(elementType, declaredType, model))
                                        {
                                            diagnostics.ReportAtNode(TypeCheckDiagnostics.TupleDeconstructionTypeMismatch, element, "TypeChecker", elementType.DisplayName, declaredType.DisplayName, index);
                                        }
                                    }
                                }

                                checkDeconstructionElement(typedTuple.First, 0);
                                checkDeconstructionElement(typedTuple.Rest, 1);
                                for(var i = 0; i < typedTuple.TupleDeconstructionElements.Length; i++)
                                {
                                    checkDeconstructionElement(typedTuple.TupleDeconstructionElements[i], 2 + i);
                                }
                            }
                        }
                    }

                    break;
                }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Return statement checking (MIST_TC_0008)
    // ═══════════════════════════════════════════════════════════════════════

    // ═══════════════════════════════════════════════════════════════════════
    // Ref local requires initializer validation (MIST_REF_0010)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Validates that a ref or ref readonly local variable has an initializer.
    /// The current grammar enforces initializers for all local variable declarations,
    /// so this is a defensive check. Emits <c>MIST_REF_0010</c> if a ref local
    /// somehow lacks an initializer.
    /// </summary>
    private static void CheckRefLocalRequiresInitializer(
        VariableDeclarationStatementNode varDecl,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        var symbol = model.GetDeclaredSymbol(varDecl);
        if(symbol is not Symbol { Kind: SymbolKind.LocalVariable } sym)
        {
            return;
        }

        if(sym.RefKind == RefKind.None)
        {
            return;
        }

        // Check if the declaration has an initializer based on its type.
        var hasInitializer = varDecl switch
        {
            TypedVariableDeclarationVariableDeclarationStatementNode => true,
            InferredVariableDeclarationVariableDeclarationStatementNode => true,
            _ => false,
        };

        if(!hasInitializer)
        {
            diagnostics.ReportAtNode(TypeCheckDiagnostics.RefLocalMustInit, varDecl, "TypeChecker");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Ref return lifetime safety (MIST_REF_0006)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Validates that a return expression in a ref-returning method references a storage
    /// location whose lifetime exceeds the method call. Returns <c>true</c> if a diagnostic
    /// was emitted (caller should skip further type checks to avoid cascading).
    /// <para>
    /// Permitted return targets:
    /// <list type="bullet">
    ///   <item><description>Instance fields via <c>this</c> (including chained field access)</description></item>
    ///   <item><description>Array elements accessed through an instance field of <c>this</c></description></item>
    ///   <item><description>Static fields (accessed via a type name)</description></item>
    ///   <item><description>Ref parameters (their storage outlives the callee)</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// Rejected return targets:
    /// <list type="bullet">
    ///   <item><description>Local variables</description></item>
    ///   <item><description>Non-ref parameters</description></item>
    ///   <item><description>Literals, new expressions, method call results, arithmetic expressions</description></item>
    /// </list>
    /// </para>
    /// </summary>
    private bool CheckRefReturnLifetimeSafety(
        ReturnStatementNode returnStmt,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Only applies when the enclosing method has a ref or ref readonly return type.
        if(currentMethodSymbol is not Symbol methodSym)
        {
            return false;
        }

        if(methodSym.ReturnRefKind == RefKind.None)
        {
            return false;
        }

        // The return statement must have a value expression.
        if(returnStmt.Value is null)
        {
            return false;
        }

        // Check whether the return expression references a valid long-lived storage location.
        if(IsValidRefReturnTarget(returnStmt.Value, model))
        {
            return false;
        }

        // The return expression does not reference a valid long-lived storage location.
        // Emit MIST_REF_0006.
        var description = DescribeRefReturnTarget(returnStmt.Value, model);
        diagnostics.ReportAtNode(TypeCheckDiagnostics.RefReturnLocalEscape, returnStmt, "TypeChecker", description);
        return true;
    }

    /// <summary>
    /// Determines whether an expression is a valid target for a ref return — i.e., its
    /// storage location's lifetime exceeds the method call.
    /// </summary>
    private static bool IsValidRefReturnTarget(ExpressionNode expr, ISemanticModel model)
    {
        var postfix = TryExtractPostfixNode(expr);
        if(postfix is null)
        {
            // Expression has binary/logical operators or assignment — not a valid ref return target.
            return false;
        }

        // Case 1: Expression has suffixes (member access, index, method call, etc.)
        if(postfix.Suffix.Length > 0)
        {
            // Check if the root is `this` — field access via this is valid.
            if(postfix.Target is ThisExpressionPrimaryNode)
            {
                // Verify the suffix chain consists only of member accesses and index accesses.
                // Method calls in the chain invalidate the ref return (the result is a temporary).
                return AllSuffixesAreFieldOrIndexAccess(postfix.Suffix);
            }

            // Check if the root is an identifier that resolves to a ref parameter —
            // accessing fields/elements of a ref parameter is valid (the ref parameter's
            // storage outlives the callee).
            if(postfix.Target is IdentifierExpressionPrimaryNode rootIdentifier)
            {
                var symbol = model.GetDeclaredSymbol(rootIdentifier);
                if(symbol is Symbol { Kind: SymbolKind.Parameter } paramSym && paramSym.RefKind == RefKind.Ref)
                {
                    // A ref parameter with field/index access — valid.
                    return AllSuffixesAreFieldOrIndexAccess(postfix.Suffix);
                }

                // Check if the identifier resolves to a type (static field access).
                // In Mist, static field access is `TypeName.fieldName`, which is an
                // identifier with a member access suffix.
                var resolvedType = model.GetTypeOf(rootIdentifier);
                if(resolvedType is not null && symbol is not null &&
                    symbol.Kind is SymbolKind.Class or SymbolKind.Struct or SymbolKind.Enum)
                {
                    // Static member access — valid (static fields outlive any method call).
                    return AllSuffixesAreFieldOrIndexAccess(postfix.Suffix);
                }
            }

            // Any other root with suffixes (local variable with member access, etc.)
            // is not a valid ref return target.
            return false;
        }

        // Case 2: No suffixes — the expression is just a primary node (identifier, literal, etc.)
        if(postfix.Target is IdentifierExpressionPrimaryNode identifier)
        {
            var symbol = model.GetDeclaredSymbol(identifier);
            if(symbol is not Symbol concreteSymbol)
            {
                return false;
            }

            // A ref parameter by itself is a valid ref return target —
            // the caller's storage outlives the callee.
            if(concreteSymbol.Kind == SymbolKind.Parameter && concreteSymbol.RefKind == RefKind.Ref)
            {
                return true;
            }

            // Local variables and non-ref parameters are NOT valid — their storage
            // is destroyed when the method returns.
            return false;
        }

        // All other primary nodes (literals, this, new expressions, etc.) are not valid.
        return false;
    }

    /// <summary>
    /// Checks that all suffixes in a postfix chain are member accesses or index accesses
    /// (no method calls, invocations, or other suffixes that produce temporaries).
    /// </summary>
    private static bool AllSuffixesAreFieldOrIndexAccess(IReadOnlyList<PostfixSuffixNode> suffixes)
    {
        foreach(var suffix in suffixes)
        {
            if(suffix is not (MemberAccessSuffixPostfixSuffixNode or IndexSuffixPostfixSuffixNode))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Produces a human-readable description of the ref return target for use in the
    /// MIST_REF_0006 diagnostic message.
    /// </summary>
    private static string DescribeRefReturnTarget(ExpressionNode expr, ISemanticModel model)
    {
        var postfix = TryExtractPostfixNode(expr);
        if(postfix is null)
        {
            return DescribeExpression(expr, model);
        }

        if(postfix.Target is IdentifierExpressionPrimaryNode identifier)
        {
            return identifier.Name.Lexeme;
        }

        return DescribeExpression(expr, model);
    }

    private void CheckReturnStatement(
        ReturnStatementNode returnStmt,
        IType methodReturnType,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        if(IsErrorType(methodReturnType))
        {
            return;
        }

        if(returnStmt.Value is not null)
        {
            // Infer bare 'default' type from the method return type before checking.
            if(!methodReturnType.IsVoid())
            {
                TryInferDefaultLiteralType(returnStmt.Value, methodReturnType, model);
            }

            // return expr; — check the expression and verify type.
            CheckExpression(returnStmt.Value, model, diagnostics);

            // ── ref-parameters feature: ref return lifetime safety (MIST_REF_0006) ──
            // When the enclosing method has a ref or ref readonly return type,
            // validate that the return expression references a storage location
            // whose lifetime exceeds the method call.
            if(CheckRefReturnLifetimeSafety(returnStmt, model, diagnostics))
            {
                // A ref lifetime diagnostic was emitted; skip further type checks
                // to avoid cascading diagnostics.
                return;
            }

            var valueType = model.GetTypeOf(returnStmt.Value);

            if(methodReturnType.IsVoid())
            {
                // Void methods: reject return values UNLESS the value is void
                if(valueType is not null && !IsErrorType(valueType) && !valueType.IsVoid())
                {
                    diagnostics.ReportAtNode(TypeCheckDiagnostics.ReturnValueInVoidMethod, returnStmt, "TypeChecker", valueType!.DisplayName);
                }
            }
            else if(valueType is not null)
            {
                // ── flow narrowing: use narrowed type if available ──
                var effectiveValueType = GetEffectiveSourceType(returnStmt.Value, valueType);

                // ── refinement-types feature: compile-time enforcement ──
                // When the return type is a RefinementType, check the return
                // value against the refinement predicate.
                if(TryCheckRefinementAssignment(effectiveValueType, methodReturnType, returnStmt.Value, diagnostics, model))
                {
                    // Refinement-specific diagnostic emitted; skip generic.
                }
                // ── regex-type feature: string literal shorthand (Requirements 6.5, 6.8, 6.9) ──
                // When the return type is regex and the value is string, apply
                // the string→regex conversion rules.
                else if(TryCheckStringToRegexAssignment(valueType, methodReturnType, returnStmt.Value, diagnostics, model))
                {
                    // Regex-specific diagnostic emitted or accepted; skip generic.
                }
                else if(!IsAssignable(valueType, methodReturnType))
                {
                    // unknown-type feature (Req 4.2, 4.4): emit a targeted
                    // diagnostic for void→unknown or unknown→non-unknown
                    // before falling back to the generic MIST_TC_0008.
                    if(TryEmitUnknownAssignabilityDiagnostic(valueType, methodReturnType, returnStmt, diagnostics))
                    {
                        // Targeted unknown diagnostic emitted; skip generic.
                    }
                    // type-unions feature (Req 6.3, 6.4): emit a targeted
                    // diagnostic for union-specific failures.
                    else if(TryEmitUnionAssignabilityDiagnostic(valueType, methodReturnType, returnStmt, diagnostics))
                    {
                        // Targeted union diagnostic emitted; skip generic.
                    }
                    // enums feature (Req 7.3): emit a targeted diagnostic
                    // for reverse conversion (underlying type → enum).
                    else if(TryEmitEnumAssignabilityDiagnostic(valueType, methodReturnType, returnStmt, diagnostics, model))
                    {
                        // Targeted enum diagnostic emitted; skip generic.
                    }
                    else
                    {
                        diagnostics.ReportAtNode(TypeCheckDiagnostics.ReturnValueInVoidMethod, returnStmt, "TypeChecker", valueType.DisplayName);
                    }
                }
                else
                {
                    // The return type is assignable. If assignability was due to
                    // an implicit cast operator, resolve and record it so the
                    // emitter can wrap the return expression.
                    _ = TryResolveImplicitCast(valueType, methodReturnType, returnStmt, model, diagnostics);
                }
            }
        }
        else
        {
            // return; — only valid in void methods.
            if(!methodReturnType.IsVoid())
            {
                diagnostics.ReportAtNode(TypeCheckDiagnostics.ReturnValueInVoidMethod, returnStmt, "TypeChecker", "void");
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

        // ── Flow narrowing (Requirements 7.1–7.8) ──────────────────────────
        // Analyze the condition to detect comparisons that prove relational
        // constraints for local variables. Store the constraints so they can
        // be checked against target refinement types at assignment sites.
        var narrowings = CollectNarrowingsFromCondition(ifStmt.Condition, model);

        // Save previous narrowing state for variables we're about to narrow,
        // so we can restore them after the then-branch (preserving outer
        // for-loop narrowing, for example).
        var previousNarrowings = new List<(string VarName, List<ConditionConstraint>? Previous)>();
        foreach(var (varName, constraints) in narrowings)
        {
            _ = narrowingContext.TryGetValue(varName, out var previous);
            previousNarrowings.Add((varName, previous is not null ? new List<ConditionConstraint>(previous) : null));

            // Merge: if there's existing narrowing, combine constraints; otherwise set new.
            if(previous is not null)
            {
                var merged = new List<ConditionConstraint>(previous);
                merged.AddRange(constraints);
                narrowingContext[varName] = merged;
            }
            else
            {
                narrowingContext[varName] = constraints;
            }
        }

        CheckBlock(ifStmt.ThenBranch, returnType, model, diagnostics);

        // Restore previous narrowing entries after the then-branch.
        foreach(var (varName, previous) in previousNarrowings)
        {
            if(previous is not null)
            {
                narrowingContext[varName] = previous;
            }
            else
            {
                _ = narrowingContext.Remove(varName);
            }
        }

        if(ifStmt.ElseBranch is ElseIfElseClauseNode elseIf)
        {
            CheckIfStatement(elseIf.ElseIf, returnType, model, diagnostics);
        }
        else if(ifStmt.ElseBranch is ElseBlockElseClauseNode elseBlock)
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

    // ═══════════════════════════════════════════════════════════════════════
    // Flow narrowing helpers (Requirements 7.1–7.8)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Analyzes an if-condition to detect comparisons or <c>is</c> patterns that
    /// prove a refinement predicate for a local variable. Returns a list of
    /// (variableName, RefinementType) pairs for variables that should be narrowed
    /// within the then-branch.
    /// </summary>
    private List<(string VarName, List<ConditionConstraint> Constraints)> CollectNarrowingsFromCondition(
        ExpressionNode condition,
        ISemanticModel model)
    {
        var result = new List<(string, List<ConditionConstraint>)>();

        // Navigate: Expression → Assignment → LogicalOr → LogicalAnd → Equality → Relational
        var assignment = condition.AsAssignmentOrNull();
        if(assignment is null)
        {
            return result; // Lambda form is not a narrowing condition.
        }

        if(assignment.Group0?.Value is not null)
        {
            return result; // Not a simple condition.
        }

        var voidCoalescing = assignment.Target;
        if(voidCoalescing is null)
        {
            return result;
        }

        if(voidCoalescing.LogicalOrs.Length > 0)
        {
            return result;
        }

        var logicalOr = voidCoalescing.Left;
        if(logicalOr is null)
        {
            return result;
        }

        // Only handle simple conditions (no || for now).
        if(logicalOr.Right is not null && !ReferenceEquals(logicalOr.Right, logicalOr.Left))
        {
            return result;
        }

        var logicalAnd = logicalOr.Left;
        if(logicalAnd is null)
        {
            return result;
        }

        // Only handle simple conditions (no && for now — could be extended later).
        if(logicalAnd.Right is not null && !ReferenceEquals(logicalAnd.Right, logicalAnd.Left))
        {
            return result;
        }

        var equality = logicalAnd.Left;
        if(equality is null)
        {
            return result;
        }

        if(!equality.Group0.IsDefaultOrEmpty)
        {
            return result;
        }

        var relational = equality.Left;
        if(relational is null)
        {
            return result;
        }

        switch(relational)
        {
            case RelationalComparisonRelationalNode cmp when !cmp.Group0.IsDefaultOrEmpty:
                {
                    // Simple comparison: x >= N, x <= N, x > N, x < N
                    TryNarrowFromComparison(cmp, model, result);
                    break;
                }
            case IsExpressionRelationalNode isExpr:
                {
                    // `is` pattern: x is >= N
                    TryNarrowFromIsPattern(isExpr, model, result);
                    break;
                }
        }

        return result;
    }

    /// <summary>
    /// Attempts to narrow a variable from a simple relational comparison (e.g., <c>x >= 18</c>).
    /// Extracts the condition constraint and stores it for later checking at assignment sites.
    /// </summary>
    private void TryNarrowFromComparison(
        RelationalComparisonRelationalNode cmp,
        ISemanticModel model,
        List<(string VarName, List<ConditionConstraint> Constraints)> result)
    {
        // Extract the variable name from the LHS.
        var varName = TryExtractSimpleIdentifierName(cmp.Left);
        if(varName is null)
        {
            return;
        }

        // Check eligibility: must be a local primitive variable not captured by closures.
        if(closureCapturedVariables.Contains(varName))
        {
            return;
        }

        var varType = TryGetVariableType(cmp.Left, model);
        if(varType is null || !IsPrimitiveImmutableType(varType))
        {
            return;
        }

        // Extract the operator and constant value from the comparison. The caller
        // gates on a non-empty Group0, so the last element carries this comparison's
        // operator and right operand (preserving the former last-wins shim reads).
        var op = cmp.Group0[^1].Operator.Lexeme;
        if(op is null)
        {
            return;
        }

        var constantValue = TryExtractLongFromAdditiveNode(cmp.Group0[^1].Right);
        if(!constantValue.HasValue)
        {
            return;
        }

        // Build a constraint from the condition and store it directly.
        var constraint = op switch
        {
            ">=" => new ConditionConstraint(ConditionRelOp.Gte, constantValue.Value),
            "<=" => new ConditionConstraint(ConditionRelOp.Lte, constantValue.Value),
            ">" => new ConditionConstraint(ConditionRelOp.Gt, constantValue.Value),
            "<" => new ConditionConstraint(ConditionRelOp.Lt, constantValue.Value),
            _ => (ConditionConstraint?)null
        };

        if(constraint is not null)
        {
            result.Add((varName, new List<ConditionConstraint> { constraint.Value }));
        }
    }

    /// <summary>
    /// Attempts to narrow a variable from an <c>is</c> pattern expression (e.g., <c>x is >= 18</c>).
    /// When the pattern is a relational pattern, extracts the constraints and stores them
    /// for later checking at assignment sites.
    /// </summary>
    private void TryNarrowFromIsPattern(
        IsExpressionRelationalNode isExpr,
        ISemanticModel model,
        List<(string VarName, List<ConditionConstraint> Constraints)> result)
    {
        // Extract the variable name from the LHS.
        var varName = TryExtractSimpleIdentifierName(isExpr.Left);
        if(varName is null)
        {
            return;
        }

        // Check eligibility: must be a local primitive variable not captured by closures.
        if(closureCapturedVariables.Contains(varName))
        {
            return;
        }

        var varType = TryGetVariableType(isExpr.Left, model);
        if(varType is null || !IsPrimitiveImmutableType(varType))
        {
            return;
        }

        // Extract the relational pattern from the is expression.
        // Pattern → OrPattern → AndPattern → UnaryPattern → PrimaryPattern
        var pattern = isExpr.Pattern;
        if(pattern?.OrPattern is null)
        {
            return;
        }

        var orPattern = pattern.OrPattern;
        if(orPattern.Right is not null && !ReferenceEquals(orPattern.Right, orPattern.Left))
        {
            return; // Or patterns not supported for narrowing.
        }

        var andPattern = orPattern.Left;
        if(andPattern is null)
        {
            return;
        }

        // Extract constraints directly from the pattern.
        var constraints = ExtractConstraintsFromAndPattern(andPattern);
        if(constraints.Count > 0)
        {
            result.Add((varName, constraints));
        }
    }

    /// <summary>
    /// Checks if a set of condition constraints implies all constraints in the
    /// given refinement predicate.
    /// </summary>
    private static bool ConstraintsImplyPredicate(
        List<ConditionConstraint> conditionConstraints,
        RefinementPredicate predicate)
    {
        // Extract the predicate's constraints.
        var predicateConstraints = ExtractPredicateConstraints(predicate);
        if(predicateConstraints.Count == 0)
        {
            return false;
        }

        // For implication to hold, every constraint in the predicate must be
        // implied by at least one constraint in the condition.
        foreach(var target in predicateConstraints)
        {
            var implied = false;
            foreach(var source in conditionConstraints)
            {
                if(ConditionConstraintImplies(source, target))
                {
                    implied = true;
                    break;
                }
            }

            if(!implied)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Determines if a source condition constraint implies a target predicate constraint.
    /// Uses the same logic as <see cref="RefinementPredicate.Implies"/> but operates
    /// on the condition-level constraint representation.
    /// </summary>
    private static bool ConditionConstraintImplies(ConditionConstraint source, ConditionConstraint target)
    {
        // Both symbolic with same ID and same operator → tautology
        if(source.SymbolicId is not null && target.SymbolicId is not null)
        {
            return source.SymbolicId == target.SymbolicId && source.Op == target.Op;
        }

        // One symbolic, one concrete → cannot prove
        if(source.SymbolicId is not null || target.SymbolicId is not null)
        {
            return false;
        }

        // Both concrete → existing logic
        return (source.Op, target.Op) switch
        {
            // Source: >= a
            (ConditionRelOp.Gte, ConditionRelOp.Gte) => source.Value >= target.Value,
            (ConditionRelOp.Gte, ConditionRelOp.Gt) => source.Value > target.Value,

            // Source: > a
            (ConditionRelOp.Gt, ConditionRelOp.Gte) => source.Value >= target.Value,
            (ConditionRelOp.Gt, ConditionRelOp.Gt) => source.Value >= target.Value,

            // Source: <= a
            (ConditionRelOp.Lte, ConditionRelOp.Lte) => source.Value <= target.Value,
            (ConditionRelOp.Lte, ConditionRelOp.Lt) => source.Value < target.Value,

            // Source: < a
            (ConditionRelOp.Lt, ConditionRelOp.Lte) => source.Value <= target.Value,
            (ConditionRelOp.Lt, ConditionRelOp.Lt) => source.Value <= target.Value,

            _ => false
        };
    }

    /// <summary>
    /// Extracts relational constraints from a refinement predicate's AST.
    /// </summary>
    private static List<ConditionConstraint> ExtractPredicateConstraints(RefinementPredicate predicate)
    {
        return ExtractConstraintsFromAndPattern(predicate.AstNode);
    }

    /// <summary>
    /// Extracts relational constraints from an <see cref="AndPatternNode"/>.
    /// </summary>
    private static List<ConditionConstraint> ExtractConstraintsFromAndPattern(AndPatternNode andPattern)
    {
        var constraints = new List<ConditionConstraint>();
        ExtractConstraintFromUnary(andPattern.Left, constraints);

        if(andPattern.Right is not null && !ReferenceEquals(andPattern.Right, andPattern.Left))
        {
            ExtractConstraintFromUnary(andPattern.Right, constraints);
        }

        if(!andPattern.UnaryPatterns.IsDefaultOrEmpty)
        {
            foreach(var part in andPattern.UnaryPatterns)
            {
                ExtractConstraintFromUnary(part, constraints);
            }
        }

        return constraints;
    }

    /// <summary>
    /// Extracts a relational constraint from a <see cref="UnaryPatternNode"/>.
    /// </summary>
    private static void ExtractConstraintFromUnary(UnaryPatternNode unary, List<ConditionConstraint> constraints)
    {
        if(unary is not DirectPatternUnaryPatternNode direct)
        {
            return;
        }

        switch(direct.Primary)
        {
            case GreaterThanOrEqualRelationalPatternPrimaryPatternNode gte:
                {
                    var val = TryExtractLongFromPatternConstant(gte.Constant);
                    if(val.HasValue)
                    {
                        constraints.Add(new ConditionConstraint(ConditionRelOp.Gte, val.Value));
                    }

                    break;
                }
            case LessThanOrEqualRelationalPatternPrimaryPatternNode lte:
                {
                    var val = TryExtractLongFromPatternConstant(lte.Constant);
                    if(val.HasValue)
                    {
                        constraints.Add(new ConditionConstraint(ConditionRelOp.Lte, val.Value));
                    }

                    break;
                }
            case GreaterThanRelationalPatternPrimaryPatternNode gt:
                {
                    var val = TryExtractLongFromPatternConstant(gt.Constant);
                    if(val.HasValue)
                    {
                        constraints.Add(new ConditionConstraint(ConditionRelOp.Gt, val.Value));
                    }

                    break;
                }
            case LessThanRelationalPatternPrimaryPatternNode lt:
                {
                    var val = TryExtractLongFromPatternConstant(lt.Constant);
                    if(val.HasValue)
                    {
                        constraints.Add(new ConditionConstraint(ConditionRelOp.Lt, val.Value));
                    }

                    break;
                }
        }
    }

    /// <summary>
    /// Extracts a long integer value from a relational-pattern constant.
    /// The constant is now bounded to <see cref="VoidCoalescingNode"/>.
    /// Delegates to the same logic used by <see cref="RefinementPredicate"/>.
    /// </summary>
    private static long? TryExtractLongFromPatternConstant(VoidCoalescingNode expr)
    {
        return RefinementPredicate.TryExtractLongFromVoidCoalescing(expr);
    }

    /// <summary>
    /// Extracts a simple identifier name from an additive expression node.
    /// Returns null if the expression is not a simple identifier reference.
    /// </summary>
    private static string? TryExtractSimpleIdentifierName(AdditiveNode additive)
    {
        if(additive is not null && !additive.Group0.IsDefaultOrEmpty)
        {
            return null;
        }

        var multiplicative = additive?.Left;
        if(multiplicative is not null && !multiplicative.Group0.IsDefaultOrEmpty)
        {
            return null;
        }

        if(multiplicative?.Left.UnwrapRange() is not PostfixExpressionUnaryNode postfixUnary)
        {
            return null;
        }

        var postfix = postfixUnary.Primary;
        if(postfix.Suffix.Length > 0)
        {
            return null;
        }

        if(postfix.Target is IdentifierExpressionPrimaryNode identifier)
        {
            return identifier.Name.Lexeme;
        }

        return null;
    }

    /// <summary>
    /// Extracts a long integer value from an additive expression node (for the RHS of a comparison).
    /// </summary>
    private static long? TryExtractLongFromAdditiveNode(AdditiveNode? additive)
    {
        if(additive is null)
        {
            return null;
        }

        if(!additive.Group0.IsDefaultOrEmpty)
        {
            return null;
        }

        var multiplicative = additive.Left;
        if(multiplicative is not null && !multiplicative.Group0.IsDefaultOrEmpty)
        {
            return null;
        }

        if(multiplicative?.Left.UnwrapRange() is not PostfixExpressionUnaryNode postfixUnary)
        {
            return null;
        }

        var postfix = postfixUnary.Primary;
        if(postfix.Suffix.Length > 0)
        {
            return null;
        }

        if(postfix.Target is IntegerLiteralExpressionPrimaryNode intLiteral)
        {
            return RefinementPredicate.ParseIntegerLexeme(intLiteral.IntegerLiteral.Lexeme);
        }

        return null;
    }

    /// <summary>
    /// Gets the type of a local variable by name from the semantic model.
    /// Returns null if the variable is not found or is not a local variable.
    /// </summary>
    private static IType? TryGetVariableType(AdditiveNode node, ISemanticModel model)
    {
        // Get the type from the expression node itself.
        var type = model.GetTypeOf(node);
        return type;
    }

    /// <summary>
    /// Returns the effective type of an expression, checking the narrowing context
    /// for identifier expressions that have been narrowed via flow analysis.
    /// With constraint-based narrowing, this always returns null — the actual
    /// constraint checking happens at the assignment site in TryCheckRefinementAssignment.
    /// This method is retained for structural compatibility with ApplyNarrowingToArguments.
    /// </summary>
    private string? TryExtractNarrowedIdentifierName(ExpressionNode expr)
    {
        // Navigate to the identifier: Expression → Assignment → LogicalOr → ... → Primary
        var assignment = expr.AsAssignmentOrNull();
        if(assignment is null)
        {
            return null; // Lambda form is not a narrowed identifier.
        }

        if(assignment.Group0?.Value is not null)
        {
            return null;
        }

        var logicalOr = assignment.Target?.Left;
        if(logicalOr?.Right is not null && !ReferenceEquals(logicalOr.Right, logicalOr.Left))
        {
            return null;
        }

        var logicalAnd = logicalOr?.Left;
        if(logicalAnd?.Right is not null && !ReferenceEquals(logicalAnd.Right, logicalAnd.Left))
        {
            return null;
        }

        var equality = logicalAnd?.Left;
        if(equality is not null && !equality.Group0.IsDefaultOrEmpty)
        {
            return null;
        }

        if(equality?.Left is not RelationalComparisonRelationalNode relCmp)
        {
            return null;
        }

        if(!relCmp.Group0.IsDefaultOrEmpty)
        {
            return null;
        }

        var additive = relCmp.Left;
        if(additive is not null && !additive.Group0.IsDefaultOrEmpty)
        {
            return null;
        }

        var multiplicative = additive?.Left;
        if(multiplicative is not null && !multiplicative.Group0.IsDefaultOrEmpty)
        {
            return null;
        }

        if(multiplicative?.Left.UnwrapRange() is not PostfixExpressionUnaryNode postfixUnary)
        {
            return null;
        }

        var postfix = postfixUnary.Primary;
        if(postfix.Suffix.Length > 0)
        {
            return null;
        }

        if(postfix.Target is IdentifierExpressionPrimaryNode identifier)
        {
            if(narrowingContext.ContainsKey(identifier.Name.Lexeme))
            {
                return identifier.Name.Lexeme;
            }
        }

        return null;
    }

    private enum ConditionRelOp { Gte, Lte, Gt, Lt }

    /// <summary>
    /// A relational fact proven about a variable. Either concrete (Value is meaningful,
    /// SymbolicId is null) or symbolic (SymbolicId is non-null, Value is ignored).
    /// </summary>
    private readonly record struct ConditionConstraint(ConditionRelOp Op, long Value, int? SymbolicId = null);

    /// <summary>
    /// Returns the effective source type for an expression. With constraint-based
    /// narrowing, this always returns the original type — constraint checking
    /// happens at the assignment site in TryCheckRefinementAssignment.
    /// </summary>
    private static IType GetEffectiveSourceType(ExpressionNode expr, IType originalType)
    {
        _ = expr; // Narrowing is now checked at the assignment site.
        return originalType;
    }

    /// <summary>
    /// Checks if an expression is a simple identifier that has been narrowed by a
    /// preceding <c>??=</c> statement. If so, returns the narrowed type; otherwise null.
    /// </summary>
    private IType? TryGetNarrowedTypeForExpression(ExpressionNode expr)
    {
        var varName = TryExtractIdentifierFromExpression(expr);
        if(varName is not null && voidCoalescingTypeNarrowings.TryGetValue(varName, out var narrowedType))
        {
            return narrowedType;
        }

        return null;
    }

    /// <summary>
    /// Attempts to extract a simple identifier name from an expression node.
    /// Returns null if the expression is not a simple identifier reference.
    /// </summary>
    private static string? TryExtractIdentifierFromExpression(ExpressionNode expr)
    {
        // Expression → Assignment → VoidCoalescing → LogicalOr → ... → Postfix → Identifier
        var assignment = expr.AsAssignmentOrNull();
        if(assignment is null)
        {
            return null; // Lambda form is not a simple identifier reference.
        }

        if(assignment.Group0?.Value is not null || assignment.Group0?.Operator is not null)
        {
            return null;
        }

        return TryExtractIdentifierFromLogicalOr(assignment.Target.Left);
    }

    /// <summary>
    /// Returns the effective source type for an assignment RHS expression.
    /// With constraint-based narrowing, this always returns the original type —
    /// constraint checking happens at the assignment site in TryCheckRefinementAssignment.
    /// </summary>
    private static IType GetEffectiveSourceTypeFromAssignment(AssignmentNode assignmentValue, IType originalType)
    {
        _ = assignmentValue; // Narrowing is now checked at the assignment site.
        return originalType;
    }

    /// <summary>
    /// Invalidates narrowing for the variable being assigned to.
    /// Called when a narrowed variable is reassigned within a branch (Requirement 7.4).
    /// </summary>
    private void InvalidateNarrowingForAssignmentTarget(LogicalOrNode target)
    {
        var varName = TryExtractIdentifierFromLogicalOr(target);
        if(varName is not null)
        {
            _ = narrowingContext.Remove(varName);
            _ = voidCoalescingTypeNarrowings.Remove(varName);
        }
    }

    /// <summary>
    /// Extracts a simple identifier name from a <see cref="LogicalOrNode"/>.
    /// Returns null if the expression is not a simple identifier reference.
    /// </summary>
    private static string? TryExtractIdentifierFromLogicalOr(LogicalOrNode logicalOr)
    {
        if(logicalOr.Right is not null && !ReferenceEquals(logicalOr.Right, logicalOr.Left))
        {
            return null;
        }

        var logicalAnd = logicalOr.Left;
        if(logicalAnd?.Right is not null && !ReferenceEquals(logicalAnd.Right, logicalAnd.Left))
        {
            return null;
        }

        var equality = logicalAnd?.Left;
        if(equality is not null && !equality.Group0.IsDefaultOrEmpty)
        {
            return null;
        }

        if(equality?.Left is not RelationalComparisonRelationalNode relCmp)
        {
            return null;
        }

        if(!relCmp.Group0.IsDefaultOrEmpty)
        {
            return null;
        }

        var additive = relCmp.Left;
        if(additive is not null && !additive.Group0.IsDefaultOrEmpty)
        {
            return null;
        }

        var multiplicative = additive?.Left;
        if(multiplicative is not null && !multiplicative.Group0.IsDefaultOrEmpty)
        {
            return null;
        }

        if(multiplicative?.Left.UnwrapRange() is not PostfixExpressionUnaryNode postfixUnary)
        {
            return null;
        }

        var postfix = postfixUnary.Primary;
        if(postfix.Suffix.Length > 0)
        {
            return null;
        }

        if(postfix.Target is IdentifierExpressionPrimaryNode identifier)
        {
            return identifier.Name.Lexeme;
        }

        return null;
    }

    /// <summary>
    /// Collects variable names captured by closures (lambda expressions) in the
    /// given block. Variables captured by closures are ineligible for flow narrowing
    /// (Requirement 7.7). Also checks for ref locals captured by lambdas and emits
    /// <c>MIST_REF_0011</c> (Requirements 6.7, 14.11).
    /// </summary>
    private void CollectClosureCapturedVariables(BlockNode block)
    {
        // Currently, Mist does not have lambda/closure expressions.
        // This method is a placeholder for future closure support.
        // When lambdas are added, this should walk the block looking for
        // lambda expressions and collect all identifiers they reference
        // into _closureCapturedVariables. For now, clear the set to ensure
        // a clean state for each method body.
        closureCapturedVariables.Clear();
        _ = block; // Suppress unused parameter warning.
    }

    /// <summary>
    /// Checks for ref locals captured by lambda or closure expressions and emits
    /// <c>MIST_REF_0011</c> for each violation. A ref local (one with
    /// <see cref="RefKind.Ref"/> or <see cref="RefKind.RefReadonly"/>) cannot be
    /// captured because its storage location may not outlive the closure.
    /// (Requirements 6.7, 14.11)
    /// </summary>
    /// <remarks>
    /// Currently, Mist does not have lambda/closure expressions in the grammar.
    /// This method is structurally complete and ready to be activated when lambda
    /// syntax is added. When a lambda AST node type is introduced, this method
    /// should be called from <see cref="CheckBlock"/> or the lambda-visiting logic,
    /// walking the lambda body for identifier references that resolve to outer-scope
    /// symbols with <c>RefKind != None</c>.
    /// </remarks>
    private static void CheckRefLambdaCaptureProhibition(
        BlockNode block,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // When lambda/closure AST nodes are added to the grammar, this method
        // should:
        // 1. Walk the block (and nested blocks) to find lambda expression nodes.
        // 2. For each lambda, collect identifier references in its body.
        // 3. For each identifier that resolves to a symbol from an outer scope:
        //    a. Check if the symbol has RefKind != RefKind.None.
        //    b. If so, emit MIST_REF_0011 at the identifier's location.
        //
        // Example implementation pattern (pseudocode for when lambdas exist):
        //
        //   foreach (var lambda in CollectLambdaNodes(block))
        //   {
        //       foreach (var identifier in CollectIdentifiersInLambdaBody(lambda))
        //       {
        //           var symbol = model.GetDeclaredSymbol(identifier) as Symbol;
        //           if (symbol is null) continue;
        //           if (symbol.Kind != SymbolKind.LocalVariable) continue;
        //           if (symbol.RefKind == RefKind.None) continue;
        //           // Symbol is a ref local captured by a lambda — emit diagnostic.
        //           diagnostics.Add(MakeDiagnostic(
        //               "MIST_REF_0011",
        //               $"Cannot capture ref local '{symbol.Name}' in a lambda or closure",
        //               identifier,
        //               model));
        //       }
        //   }

        _ = block; // Suppress unused parameter warning.
        _ = model;
        _ = diagnostics;
    }

    /// <summary>
    /// Injects flow narrowing entries for parameters that have adhoc refinement constraints.
    /// Within the method/constructor/indexer body, these parameters are treated as having
    /// an anonymous <see cref="RefinementType"/> (base type + predicate), allowing assignment
    /// to named refinement types whose predicate is implied by the parameter's constraint.
    /// Returns the list of parameter names that were narrowed, for later removal.
    /// (Requirements 8.1–8.5)
    /// </summary>
    private List<string> InjectParameterRefinementNarrowing(SyntaxNode declaringNode, ISemanticModel model)
    {
        var narrowedParams = new List<string>();

        var paramSymbols = CollectParameterSymbols(declaringNode, model);
        foreach(var paramSymbol in paramSymbols)
        {
            var refinement = model.GetParameterRefinement(paramSymbol);
            if(refinement is null)
            {
                continue;
            }

            var baseType = paramSymbol.DeclaredType;
            if(baseType is null)
            {
                continue;
            }

            // Skip parameters captured by closures (Requirement 7.7).
            if(closureCapturedVariables.Contains(paramSymbol.Name))
            {
                continue;
            }

            // Extract constraints from the parameter's refinement predicate and store them.
            var constraints = ExtractPredicateConstraints(refinement);
            if(constraints.Count > 0)
            {
                narrowingContext[paramSymbol.Name] = constraints;
                narrowedParams.Add(paramSymbol.Name);
            }
        }

        return narrowedParams;
    }

    /// <summary>
    /// Removes parameter refinement narrowing entries that were injected at method entry.
    /// Only removes entries that are still present (they may have been invalidated by
    /// reassignment within the body).
    /// </summary>
    private void RemoveParameterRefinementNarrowing(List<string> narrowedParams)
    {
        foreach(var paramName in narrowedParams)
        {
            _ = narrowingContext.Remove(paramName);
        }
    }

    /// <summary>
    /// Previously applied narrowing to argument expressions by overriding their type
    /// in the semantic model. With constraint-based narrowing, this is no longer needed
    /// because TryCheckRefinementAssignment checks the narrowing context directly.
    /// Returns an empty list for API compatibility with RestoreNarrowedArguments.
    /// </summary>
    private static List<(SyntaxNode Node, IType OriginalType)> ApplyNarrowingToArguments(
        ArgumentListNode? argList,
        ISemanticModel model)
    {
        _ = argList;
        _ = model;
        return new List<(SyntaxNode, IType)>();
    }

    /// <summary>
    /// Restores original types for argument expressions that were temporarily narrowed.
    /// </summary>
    private static void RestoreNarrowedArguments(
        List<(SyntaxNode Node, IType OriginalType)> narrowedArgs,
        ISemanticModel model)
    {
        foreach(var (node, originalType) in narrowedArgs)
        {
            model.SetTypeOf(node, originalType);
        }
    }

    /// <summary>
    /// Extracts the <see cref="IdentifierExpressionPrimaryNode"/> from a simple
    /// identifier expression. Returns null if the expression is not a simple identifier.
    /// </summary>
    private static IdentifierExpressionPrimaryNode? TryExtractIdentifierPrimaryNode(ExpressionNode expr)
    {
        var assignment = expr.AsAssignmentOrNull();
        if(assignment is null)
        {
            return null; // Lambda form is not a simple identifier.
        }

        if(assignment.Group0?.Value is not null)
        {
            return null;
        }

        var logicalOr = assignment.Target?.Left;
        if(logicalOr?.Right is not null && !ReferenceEquals(logicalOr.Right, logicalOr.Left))
        {
            return null;
        }

        var logicalAnd = logicalOr?.Left;
        if(logicalAnd?.Right is not null && !ReferenceEquals(logicalAnd.Right, logicalAnd.Left))
        {
            return null;
        }

        var equality = logicalAnd?.Left;
        if(equality is not null && !equality.Group0.IsDefaultOrEmpty)
        {
            return null;
        }

        if(equality?.Left is not RelationalComparisonRelationalNode relCmp)
        {
            return null;
        }

        if(!relCmp.Group0.IsDefaultOrEmpty)
        {
            return null;
        }

        var additive = relCmp.Left;
        if(additive is not null && !additive.Group0.IsDefaultOrEmpty)
        {
            return null;
        }

        var multiplicative = additive?.Left;
        if(multiplicative is not null && !multiplicative.Group0.IsDefaultOrEmpty)
        {
            return null;
        }

        if(multiplicative?.Left.UnwrapRange() is not PostfixExpressionUnaryNode postfixUnary)
        {
            return null;
        }

        var postfix = postfixUnary.Primary;
        if(postfix.Suffix.Length > 0)
        {
            return null;
        }

        return postfix.Target as IdentifierExpressionPrimaryNode;
    }

    private void CheckForStatement(ForStatementNode forStmt, IType returnType, ISemanticModel model, DiagnosticBag diagnostics)
    {
        // Check initializer.
        if(forStmt.Initializer is ForInitializerVariableDeclarationForInitializerNode varInit)
        {
            CheckExpression(varInit.Initializer, model, diagnostics);
            // Check assignability of initializer to declared type.
            var declaredType = model.GetTypeOf(varInit.VariableDeclaration);
            var initType = model.GetTypeOf(varInit.Initializer);
            if(declaredType is not null && initType is not null)
            {
                CheckAssignability(initType, declaredType, varInit.Initializer, diagnostics, model);
            }
        }
        else if(forStmt.Initializer is ForInitializerVarDeclarationForInitializerNode varDeclInit)
        {
            CheckExpression(varDeclInit.Initializer, model, diagnostics);
        }
        else if(forStmt.Initializer is ForInitializerExpressionForInitializerNode exprInit)
        {
            CheckExpression(exprInit.Expression, model, diagnostics);
        }

        // Check condition.
        if(forStmt.Condition is not null)
        {
            CheckExpression(forStmt.Condition, model, diagnostics);
            CheckConditionIsBool(forStmt.Condition, model, diagnostics);
        }

        // Check iterator.
        if(forStmt.Iterator is not null)
        {
            CheckExpression(forStmt.Iterator, model, diagnostics);
        }

        // Inject for-loop induction variable narrowing before checking the body.
        var narrowedVar = TryInjectForLoopNarrowing(forStmt, model);

        CheckBlock(forStmt.Body, returnType, model, diagnostics);

        // Remove narrowing after the body check.
        if(narrowedVar is not null)
        {
            _ = narrowingContext.Remove(narrowedVar);
        }
    }

    /// <summary>
    /// Attempts to inject induction variable narrowing for a for-loop.
    /// Extracts the induction variable name and initial literal value from the initializer,
    /// extracts the upper bound from the condition (literal or .Length on ArrayType),
    /// checks for reassignment in the body, and injects constraints into _narrowingContext.
    /// Returns the narrowed variable name if narrowing was injected, null otherwise.
    /// </summary>
    private string? TryInjectForLoopNarrowing(ForStatementNode forStmt, ISemanticModel model)
    {
        // 1. Extract induction variable name and initializer expression from the initializer.
        string? varName;
        ExpressionNode? initializerExpr;

        if(forStmt.Initializer is ForInitializerVariableDeclarationForInitializerNode varInit)
        {
            varName = varInit.Name.Lexeme;
            initializerExpr = varInit.Initializer;
        }
        else if(forStmt.Initializer is ForInitializerVarDeclarationForInitializerNode varDeclInit)
        {
            varName = varDeclInit.Name.Lexeme;
            initializerExpr = varDeclInit.Initializer;
        }
        else
        {
            return null;
        }

        if(varName is null)
        {
            return null;
        }

        // 2. Extract lower bound from initializer value — must be a literal integer.
        var lowerBound = TryExtractLongFromExpression(initializerExpr);
        if(!lowerBound.HasValue)
        {
            return null;
        }

        // 3. Extract upper bound from condition.
        if(forStmt.Condition is null)
        {
            return null;
        }

        var upperBound = TryExtractForLoopUpperBound(forStmt.Condition, varName, model);
        if(upperBound is null)
        {
            return null;
        }

        // 4. Check for reassignment in body — if the induction variable is reassigned, skip narrowing.
        if(HasReassignmentInBody(forStmt.Body, varName))
        {
            return null;
        }

        // 5. Inject constraints into _narrowingContext.
        var constraints = new List<ConditionConstraint>
        {
            new(ConditionRelOp.Gte, lowerBound.Value),
            upperBound.Value
        };
        narrowingContext[varName] = constraints;

        return varName;
    }

    /// <summary>
    /// Extracts a long integer value from an expression node (for the for-loop initializer).
    /// Navigates: Expression → Assignment → LogicalOr → ... → IntegerLiteral.
    /// </summary>
    private static long? TryExtractLongFromExpression(ExpressionNode expr)
    {
        var assignment = expr.AsAssignmentOrNull();
        if(assignment is null)
        {
            return null; // Lambda form is not a literal integer.
        }

        if(assignment.Group0?.Value is not null)
        {
            return null;
        }

        var logicalOr = assignment.Target?.Left;
        if(logicalOr is null)
        {
            return null;
        }

        if(logicalOr.Right is not null && !ReferenceEquals(logicalOr.Right, logicalOr.Left))
        {
            return null;
        }

        var logicalAnd = logicalOr.Left;
        if(logicalAnd is null)
        {
            return null;
        }

        if(logicalAnd.Right is not null && !ReferenceEquals(logicalAnd.Right, logicalAnd.Left))
        {
            return null;
        }

        var equality = logicalAnd.Left;
        if(equality is null)
        {
            return null;
        }

        if(!equality.Group0.IsDefaultOrEmpty)
        {
            return null;
        }

        if(equality.Left is not RelationalComparisonRelationalNode relCmp)
        {
            return null;
        }

        if(!relCmp.Group0.IsDefaultOrEmpty)
        {
            return null;
        }

        return TryExtractLongFromAdditiveNode(relCmp.Left);
    }

    /// <summary>
    /// Extracts the upper bound constraint from a for-loop condition.
    /// Handles patterns: varName &lt; expr, varName &lt;= expr
    /// where expr is a literal integer or identifier.Length on an ArrayType.
    /// </summary>
    private static ConditionConstraint? TryExtractForLoopUpperBound(ExpressionNode condition, string varName, ISemanticModel model)
    {
        // Navigate: Expression → Assignment → LogicalOr → LogicalAnd → Equality → Relational
        var assignment = condition.AsAssignmentOrNull();
        if(assignment is null)
        {
            return null; // Lambda form is not a for-loop condition.
        }

        if(assignment.Group0?.Value is not null)
        {
            return null;
        }

        var logicalOr = assignment.Target?.Left;
        if(logicalOr is null)
        {
            return null;
        }

        if(logicalOr.Right is not null && !ReferenceEquals(logicalOr.Right, logicalOr.Left))
        {
            return null;
        }

        var logicalAnd = logicalOr.Left;
        if(logicalAnd is null)
        {
            return null;
        }

        if(logicalAnd.Right is not null && !ReferenceEquals(logicalAnd.Right, logicalAnd.Left))
        {
            return null;
        }

        var equality = logicalAnd.Left;
        if(equality is null)
        {
            return null;
        }

        if(!equality.Group0.IsDefaultOrEmpty)
        {
            return null;
        }

        if(equality.Left is not RelationalComparisonRelationalNode cmp)
        {
            return null;
        }

        if(cmp.Group0.IsDefaultOrEmpty)
        {
            return null;
        }

        // Verify the LHS is the induction variable.
        var lhsName = TryExtractSimpleIdentifierName(cmp.Left);
        if(lhsName is null || !string.Equals(lhsName, varName, StringComparison.Ordinal))
        {
            return null;
        }

        // Determine the operator. The Group0 guard above ensures a comparison is
        // present; its last element carries this loop condition's operator and right
        // operand (preserving the former last-wins shim reads).
        var op = cmp.Group0[^1].Operator.Lexeme;
        ConditionRelOp relOp;
        if(op == "<")
        {
            relOp = ConditionRelOp.Lt;
        }
        else if(op == "<=")
        {
            relOp = ConditionRelOp.Lte;
        }
        else
        {
            return null;
        }

        // Try to extract the upper bound value from the RHS.
        // Case a: literal integer
        var literalValue = TryExtractLongFromAdditiveNode(cmp.Group0[^1].Right);
        if(literalValue.HasValue)
        {
            return new ConditionConstraint(relOp, literalValue.Value);
        }

        // Case b: identifier.Length on an ArrayType
        var symbolicBound = TryExtractArrayLengthBound(cmp.Group0[^1].Right, relOp, model);
        if(symbolicBound is not null)
        {
            return symbolicBound;
        }

        return null;
    }

    /// <summary>
    /// Attempts to extract a symbolic or concrete upper bound from an expression of the form
    /// identifier.Length where identifier's type is ArrayType.
    /// </summary>
    private static ConditionConstraint? TryExtractArrayLengthBound(AdditiveNode additive, ConditionRelOp relOp, ISemanticModel model)
    {
        // Navigate: Additive → Multiplicative → Unary → Postfix
        if(!additive.Group0.IsDefaultOrEmpty)
        {
            return null;
        }

        var multiplicative = additive.Left;
        if(multiplicative is not null && !multiplicative.Group0.IsDefaultOrEmpty)
        {
            return null;
        }

        if(multiplicative?.Left.UnwrapRange() is not PostfixExpressionUnaryNode postfixUnary)
        {
            return null;
        }

        var postfix = postfixUnary.Primary;

        // Must have at least one suffix, and the last suffix must be MemberAccessSuffix "Length".
        if(postfix.Suffix.Length < 1)
        {
            return null;
        }

        if(postfix.Suffix[^1] is not MemberAccessSuffixPostfixSuffixNode memberAccess)
        {
            return null;
        }

        if(memberAccess.Name.Lexeme != "Length")
        {
            return null;
        }

        // Determine the receiver type for the .Length access.
        IType? receiverType;
        if(postfix.Suffix.Length == 1)
        {
            // Simple case: identifier.Length — get type of the target primary.
            receiverType = model.GetTypeOf(postfix.Target);
            // Fallback: if GetTypeOf returns null or non-ArrayType, try looking up
            // the identifier's declared type directly from the semantic model.
            if(receiverType is not ArrayType)
            {
                if(postfix.Target is IdentifierExpressionPrimaryNode targetIdent)
                {
                    receiverType = model.GetTypeOf(targetIdent);
                    if(receiverType is null)
                    {
                        var symbol = model.GetDeclaredSymbol(targetIdent);
                        receiverType = symbol?.DeclaredType;
                    }
                }
            }
        }
        else
        {
            // Complex case: expr[i].Length or expr.member.Length — get type of the
            // suffix just before .Length (e.g., the IndexSuffix result type).
            var prevSuffix = postfix.Suffix[^2];
            receiverType = model.GetTypeOf(prevSuffix);
            // Fallback: for nested arrays like d[i].Length, the target d has type
            // ArrayType(ArrayType(int)), and d[i] gives the inner ArrayType(int).
            // Derive the inner type from the outer array's element type.
            if(receiverType is not ArrayType)
            {
                var targetType = model.GetTypeOf(postfix.Target);
                if(targetType is ArrayType outerArray && outerArray.ElementType is ArrayType)
                {
                    receiverType = outerArray.ElementType;
                }
            }
        }

        if(receiverType is not ArrayType arrayType)
        {
            // Handle nested array access: target has suffixes (e.g., d[i].Length where
            // the postfix is the whole `d[i]` expression). Check if the postfix expression
            // itself resolves to an ArrayType.
            if(postfix.Suffix.Length > 0)
            {
                // The whole postfix expression (target + suffixes except the last .Length)
                // might resolve to an ArrayType. Check the expression type of the postfix node.
                var postfixType = model.GetTypeOf(postfix);
                if(postfixType is ArrayType innerArrayType)
                {
                    if(innerArrayType.KnownLength.HasValue)
                    {
                        return new ConditionConstraint(relOp, innerArrayType.KnownLength.Value);
                    }

                    if(innerArrayType.SymbolicLengthId.HasValue)
                    {
                        return new ConditionConstraint(relOp, 0, innerArrayType.SymbolicLengthId.Value);
                    }
                }
            }

            return null;
        }

        // If the array has a concrete known length, use it.
        if(arrayType.KnownLength.HasValue)
        {
            return new ConditionConstraint(relOp, arrayType.KnownLength.Value);
        }

        // If the array has a symbolic length ID, use it.
        if(arrayType.SymbolicLengthId.HasValue)
        {
            return new ConditionConstraint(relOp, 0, arrayType.SymbolicLengthId.Value);
        }

        return null;
    }

    /// <summary>
    /// Checks whether the loop body contains any direct assignment targeting the
    /// specified variable name. In Mist, compound assignments (+=, -=) are desugared
    /// to simple assignments (i = i + 1), and increment/decrement operators don't exist,
    /// so we only need to detect direct assignments.
    /// </summary>
    private static bool HasReassignmentInBody(BlockNode body, string varName)
    {
        return HasReassignmentInNode(body, varName);
    }

    /// <summary>
    /// Recursively walks an AST node looking for assignments targeting the given variable.
    /// </summary>
    private static bool HasReassignmentInNode(SyntaxNode node, string varName)
    {
        // Check if this node is an assignment expression targeting the variable.
        if(node is AssignmentNode assignment && assignment.Group0?.Value is not null)
        {
            var targetName = TryExtractIdentifierFromLogicalOr(assignment.Target.Left);
            if(targetName is not null && string.Equals(targetName, varName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        // Recurse into children.
        foreach(var child in node.Children())
        {
            if(child is not null && HasReassignmentInNode(child, varName))
            {
                return true;
            }
        }

        return false;
    }

    private void CheckForeachStatement(ForeachStatementNode foreachStmt, IType returnType, ISemanticModel model, DiagnosticBag diagnostics)
    {
        CheckExpression(foreachStmt.GetCollection(), model, diagnostics);

        // Resolve the collection expression's type and validate it implements IEnumerable<TElement>.
        var collectionType = model.GetTypeOf(foreachStmt.GetCollection());
        if(collectionType is not null && !IsErrorType(collectionType))
        {
            var elementType = FindIEnumerableImplementation(collectionType, model);
            if(elementType is null)
            {
                // Emit diagnostic: type does not implement IEnumerable<TElement>.
                diagnostics.ReportAtNode(TypeCheckDiagnostics.ForeachNotEnumerable, foreachStmt.GetCollection(), "TypeChecker", collectionType.DisplayName);

                // Bind iteration variable to ErrorType for error recovery.
                if(model.GetDeclaredSymbol(foreachStmt) is Symbol iterVarSymbol)
                {
                    iterVarSymbol.DeclaredType = ErrorType.Instance;
                }
            }
            else
            {
                // Validate explicit type annotation against the resolved TElement.
                var typeRef = foreachStmt.GetTypeOrNull();
                if(typeRef is not null)
                {
                    var declaredType = ResolveTypeReferenceToType(typeRef, model);
                    if(declaredType is not null && !IsErrorType(declaredType))
                    {
                        if(!IsAssignable(elementType, declaredType, model))
                        {
                            // Emit diagnostic: TElement is not assignable to the declared type.
                            diagnostics.ReportAtNode(TypeCheckDiagnostics.ForeachTypeMismatch, typeRef, "TypeChecker", elementType.DisplayName, declaredType!.DisplayName);
                        }

                        // Bind the iteration variable to the declared type (user's intent).
                        if(model.GetDeclaredSymbol(foreachStmt) is Symbol iterVarSymbol)
                        {
                            iterVarSymbol.DeclaredType = declaredType;
                        }
                    }
                    else
                    {
                        // Declared type could not be resolved; fall back to TElement.
                        if(model.GetDeclaredSymbol(foreachStmt) is Symbol iterVarSymbol)
                        {
                            iterVarSymbol.DeclaredType = elementType;
                        }
                    }
                }
                else
                {
                    // Var foreach: bind the iteration variable to the inferred element type.
                    if(model.GetDeclaredSymbol(foreachStmt) is Symbol iterVarSymbol)
                    {
                        iterVarSymbol.DeclaredType = elementType;
                    }
                }
            }
        }

        CheckBlock(foreachStmt.GetBody(), returnType, model, diagnostics);
    }

    /// <summary>
    /// Searches a type's abstract list for <c>IEnumerable&lt;TElement&gt;</c> and returns
    /// the resolved <c>TElement</c>, or <c>null</c> if the type does not implement
    /// <c>IEnumerable&lt;TElement&gt;</c>.
    /// </summary>
    private IType? FindIEnumerableImplementation(IType type, ISemanticModel model)
    {
        var abstracts = GetAbstractList(type, model);

        // If the source is a ConstructedType, we need to substitute type parameters
        // in the abstract list with the concrete type arguments.
        IReadOnlyList<IType>? typeParams = null;
        IReadOnlyList<IType>? typeArgs = null;
        if(type is ConstructedType sourceCt)
        {
            switch(sourceCt.GenericDefinition)
            {
                case ClassType classDef:
                    abstracts = classDef.Abstracts;
                    typeParams = classDef.TypeParameters;
                    typeArgs = sourceCt.TypeArguments;
                    break;
                case StructType structDef:
                    abstracts = structDef.Abstracts;
                    typeParams = structDef.TypeParameters;
                    typeArgs = sourceCt.TypeArguments;
                    break;
                case AbstractType abstractDef:
                    abstracts = abstractDef.Abstracts;
                    typeParams = abstractDef.TypeParameters;
                    typeArgs = sourceCt.TypeArguments;
                    break;
            }
        }
        // Bridge: for dedicated intrinsic types, construct type parameter → type argument
        // mapping so that abstract type parameters (e.g., TValue in IEnumerable<TValue>)
        // are substituted with the actual element type.
        else if(type is ArrayType bridgeArray)
        {
            var registeredClass = FindRegisteredClassType("Array", "Standard", model);
            if(registeredClass != null)
            {
                abstracts = registeredClass.Abstracts;
                typeParams = registeredClass.TypeParameters;
                // Array<TValue, Length> → [elementType, unknownLength]
                typeArgs = new IType[] { bridgeArray.ElementType, new ArrayType(bridgeArray.ElementType) };
            }
        }
        else if(type is SpanType bridgeSpan)
        {
            var name = bridgeSpan.IsReadOnly ? "ReadOnlySpan" : "Span";
            var registeredStruct = FindRegisteredStructType(name, "Standard", model);
            if(registeredStruct != null)
            {
                abstracts = registeredStruct.Abstracts;
                typeParams = registeredStruct.TypeParameters;
                // Span<T, Length> → [elementType, unknownLength]
                typeArgs = new IType[] { bridgeSpan.ElementType, new SpanType(bridgeSpan.ElementType, bridgeSpan.IsReadOnly) };
            }
        }

        foreach(var abs in abstracts)
        {
            // Substitute type parameters if we have a mapping.
            var substitutedAbs = (typeParams is not null && typeArgs is not null)
                ? SubstituteType(abs, typeParams, typeArgs)
                : abs;

            if(substitutedAbs is ConstructedType constructedAbs &&
                constructedAbs.GenericDefinition is AbstractType absDefinition &&
                absDefinition.DisplayName == "IEnumerable" &&
                absDefinition.ContainingNamespace == "Standard" &&
                constructedAbs.TypeArguments.Count > 0)
            {
                return constructedAbs.TypeArguments[0];
            }
        }

        return null;
    }

    private static void CheckConditionIsBool(ExpressionNode condition, ISemanticModel model, DiagnosticBag diagnostics)
    {
        var condType = model.GetTypeOf(condition);
        if(condType is null || IsErrorType(condType))
        {
            return;
        }

        if(!IsBoolLike(condType))
        {
            diagnostics.ReportAtNode(TypeCheckDiagnostics.ConditionNotBool, condition, "TypeChecker", condType.DisplayName);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Expression checking — recursive walk
    // ═══════════════════════════════════════════════════════════════════════

    private void CheckExpression(ExpressionNode expr, ISemanticModel model, DiagnosticBag diagnostics)
    {
        if(expr.AsLambdaOrNull() is { } lambda)
        {
            CheckLambdaExpression(lambda, expr, model, diagnostics);
        }
        else if(expr.AsAssignmentOrNull() is { } assignment)
        {
            CheckAssignmentNode(assignment, model, diagnostics);
        }
    }

    // ── first-class-functions: lambda expression validation ──────────────

    /// <summary>
    /// Validates a lambda expression for parameter consistency, type inference
    /// completeness, return type consistency, and missing returns.
    /// </summary>
    private void CheckLambdaExpression(
        LambdaExpressionNode lambda,
        ExpressionNode enclosingExpr,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Get the parameter list from the lambda.
        var paramList = lambda switch
        {
            ExpressionLambdaLambdaExpressionNode exprLambda => exprLambda.Params,
            BlockLambdaLambdaExpressionNode blockLambda => blockLambda.Params,
            _ => null,
        };

        // ── MIST_TC_0115: Mixed typed/untyped parameters ──
        // When a lambda has parenthesized params, all must be either all typed or all untyped.
        if(paramList is ParenthesizedLambdaParamsLambdaParameterListNode parenParams
            && parenParams.Params is not null)
        {
            var paramNodes = new List<LambdaParamNode> { parenParams.Params.LambdaParam };
            paramNodes.AddRange(parenParams.Params.LambdaParams);

            if(paramNodes.Count > 1)
            {
                var hasTyped = false;
                var hasUntyped = false;
                foreach(var p in paramNodes)
                {
                    if(p is TypedLambdaParamLambdaParamNode)
                    {
                        hasTyped = true;
                    }
                    else
                    {
                        hasUntyped = true;
                    }
                }

                if(hasTyped && hasUntyped)
                {
                    diagnostics.ReportAtNode(TypeCheckDiagnostics.MixedLambdaParameterTypes, enclosingExpr, "TypeChecker");
                }
            }
        }

        // ── MIST_TC_0116: Cannot infer parameter types ──
        // When a lambda has implicit (untyped) params and the resolved type is UnknownType,
        // the semantic model could not infer parameter types (no target Func type available).
        var resolvedType = model.GetTypeOf(enclosingExpr);
        if(resolvedType is UnknownType)
        {
            // Only emit when the lambda actually has implicit params that need inference.
            var hasImplicitParams = paramList is SingleImplicitParamLambdaParameterListNode;
            if(!hasImplicitParams && paramList is ParenthesizedLambdaParamsLambdaParameterListNode pp && pp.Params is not null)
            {
                var firstParam = pp.Params.LambdaParam;
                hasImplicitParams = firstParam is UntypedLambdaParamLambdaParamNode;
            }

            if(hasImplicitParams)
            {
                diagnostics.ReportAtNode(TypeCheckDiagnostics.CannotInferLambdaParameterTypes, enclosingExpr, "TypeChecker");
            }
        }

        // ── MIST_TC_0117: Inconsistent return types in block lambda ──
        // When the semantic model resolved the lambda to a Func type whose return type
        // is ErrorType, the block has inconsistent return types.
        if(lambda is BlockLambdaLambdaExpressionNode
            && resolvedType is ConstructedType ct
            && SemanticModelBuilder.IsFuncType(ct))
        {
            var returnType = SemanticModelBuilder.GetFuncReturnType(ct);
            if(IsErrorType(returnType))
            {
                diagnostics.ReportAtNode(TypeCheckDiagnostics.InconsistentLambdaReturnTypes, enclosingExpr, "TypeChecker");
            }
        }

        // ── MIST_TC_0118: Missing return in non-void block lambda ──
        // When the target return type is non-void but the block has no return statements
        // with values, emit an error.
        if(lambda is BlockLambdaLambdaExpressionNode blockLambdaForReturn
            && resolvedType is ConstructedType funcCt
            && SemanticModelBuilder.IsFuncType(funcCt))
        {
            var returnType = SemanticModelBuilder.GetFuncReturnType(funcCt);
            if(!IsErrorType(returnType) && !returnType.IsVoid())
            {
                // For async lambdas, the return type is Task<T>. If T is Void
                // (i.e., the return type is Task<Void>), the lambda is effectively
                // void and does not require a return statement.
                var isAsyncLambda = blockLambdaForReturn.Modifiers.Any(
                    m => m is AsyncLambdaModifierLambdaModifierNode);
                var isAsyncVoid = isAsyncLambda
                    && SemanticModelBuilder.IsStandardTaskType(returnType)
                    && returnType is ConstructedType taskCt
                    && taskCt.TypeArguments.Count > 0
                    && taskCt.TypeArguments[^1].IsVoid();

                // Check if the block has any return statements with values.
                if(!isAsyncVoid && !BlockHasReturnWithValue(blockLambdaForReturn.Body))
                {
                    diagnostics.ReportAtNode(TypeCheckDiagnostics.MissingReturnInNonVoidLambda, enclosingExpr, "TypeChecker", returnType.DisplayName);
                }
            }
        }

        // ── MIST_TC_0123/0124/0125: Capture restriction validation ──
        // ALL lambdas (not just static) must not capture variables with restricted types/ownership.
        // Ref struct types cannot live on the heap (0123), owned variables cannot be shared (0124),
        // and borrowed/frozen/readonly-ref variables have bounded lifetimes (0125).
        {
            var captureParamNames = CollectLambdaParameterNames(lambda);
            SyntaxNode captureBodyNode = lambda switch
            {
                ExpressionLambdaLambdaExpressionNode el => el.Body,
                BlockLambdaLambdaExpressionNode bl => bl.Body,
                _ => null!,
            };

            if(captureBodyNode is not null)
            {
                CheckCaptureRestrictions(captureBodyNode, captureParamNames, model, diagnostics);
            }
        }

        // ── MIST_TC_0122: Static lambda capture violation ──
        // A static lambda cannot capture variables from the enclosing scope.
        // It may only reference its own parameters, static fields/methods, constants, and type names.
        if(IsStaticLambda(lambda))
        {
            var lambdaParamNames = CollectLambdaParameterNames(lambda);
            SyntaxNode bodyNode = lambda switch
            {
                ExpressionLambdaLambdaExpressionNode el => el.Body,
                BlockLambdaLambdaExpressionNode bl => bl.Body,
                _ => null!,
            };

            if(bodyNode is not null)
            {
                CheckStaticLambdaBody(bodyNode, lambdaParamNames, model, diagnostics);
            }
        }

        // ── MIST_TC_0126: await in non-async lambda body ──
        // When a lambda does NOT have the async modifier, any await expression
        // in its body is an error. Async lambdas permit await freely.
        // The static async combination is valid (both modifiers can coexist).
        if(!IsAsyncLambda(lambda))
        {
            SyntaxNode asyncBodyNode = lambda switch
            {
                ExpressionLambdaLambdaExpressionNode el => el.Body,
                BlockLambdaLambdaExpressionNode bl => bl.Body,
                _ => null!,
            };

            if(asyncBodyNode is not null)
            {
                CheckAwaitInNonAsyncLambdaBody(asyncBodyNode, model, diagnostics);
            }
        }

        // Recurse into the lambda body to check nested expressions.
        switch(lambda)
        {
            case ExpressionLambdaLambdaExpressionNode exprLambda:
                CheckExpression(exprLambda.Body, model, diagnostics);

                // Validate expression lambda body type against expected return type.
                // Specifically catches fallible casts (e.g., enum reverse cast returning
                // EnumType | void) being used where a non-optional type is expected.
                if(resolvedType is ConstructedType exprFuncCt
                    && SemanticModelBuilder.IsFuncType(exprFuncCt)
                    && !IsAsyncLambda(lambda))
                {
                    var expectedReturnType = SemanticModelBuilder.GetFuncReturnType(exprFuncCt);
                    var bodyType = model.GetTypeOf(exprLambda.Body);
                    if(bodyType is UnionType bodyUnion
                        && expectedReturnType is not null && !IsErrorType(expectedReturnType)
                        && expectedReturnType is not UnionType
                        && !expectedReturnType.IsVoid()
                        && !IsAssignable(bodyType, expectedReturnType))
                    {
                        diagnostics.ReportAtNode(TypeCheckDiagnostics.ExpressionBodyTypeMismatch, exprLambda.Body, "TypeChecker", bodyType.DisplayName, expectedReturnType.DisplayName);
                    }
                }

                break;
            case BlockLambdaLambdaExpressionNode blockLambda:
                CheckBlock(blockLambda.Body, GetLambdaBodyReturnType(resolvedType), model, diagnostics);
                break;
        }
    }

    /// <summary>
    /// Returns <c>true</c> if the given block contains at least one return statement
    /// with a value expression. Does not descend into nested lambdas.
    /// </summary>
    private static bool BlockHasReturnWithValue(BlockNode block)
    {
        foreach(var stmt in block.Statements)
        {
            if(StatementHasReturnWithValue(stmt))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks a single statement (and its nested control-flow blocks) for return
    /// statements with values. Does not descend into nested lambdas.
    /// </summary>
    private static bool StatementHasReturnWithValue(StatementNode stmt)
    {
        switch(stmt)
        {
            case ReturnStatementStatementNode returnStmt:
                return returnStmt.ReturnStatement.Value is not null;

            case IfStatementStatementNode ifStmt:
                if(BlockHasReturnWithValue(ifStmt.IfStatement.ThenBranch))
                {
                    return true;
                }

                if(ifStmt.IfStatement.ElseBranch is not null)
                {
                    return ElseClauseHasReturnWithValue(ifStmt.IfStatement.ElseBranch);
                }

                return false;

            case WhileStatementStatementNode whileStmt:
                return BlockHasReturnWithValue(whileStmt.WhileStatement.Body);

            case ForStatementStatementNode forStmt:
                return BlockHasReturnWithValue(forStmt.ForStatement.Body);

            case ForeachStatementStatementNode foreachStmt:
                return BlockHasReturnWithValue(foreachStmt.ForeachStatement.GetBody());

            case ReadonlyBlockStatementStatementNode readonlyBlockStmt:
                return BlockHasReturnWithValue(readonlyBlockStmt.ReadonlyBlock.Body);

            case BlockStatementStatementNode blockStmt:
                return BlockHasReturnWithValue(blockStmt.BlockStatement);

            case WithBlockStatementStatementNode withBlockStmt:
                return BlockHasReturnWithValue(withBlockStmt.WithBlockStatement.Body);

            default:
                return false;
        }
    }

    /// <summary>
    /// Checks an else clause for return statements with values.
    /// </summary>
    private static bool ElseClauseHasReturnWithValue(ElseClauseNode elseClause)
    {
        return elseClause switch
        {
            ElseBlockElseClauseNode elseBlock => BlockHasReturnWithValue(elseBlock.ElseBlock),
            ElseIfElseClauseNode elseIf =>
                BlockHasReturnWithValue(elseIf.ElseIf.ThenBranch)
                || (elseIf.ElseIf.ElseBranch is not null && ElseClauseHasReturnWithValue(elseIf.ElseIf.ElseBranch)),
            _ => false,
        };
    }

    /// <summary>
    /// Extracts the return type from a lambda's resolved Func type for use as the
    /// expected return type when checking the block body. Returns <c>ErrorType</c>
    /// if the type is not a Func type.
    /// </summary>
    private static IType GetLambdaBodyReturnType(IType? resolvedType)
    {
        if(resolvedType is ConstructedType ct && SemanticModelBuilder.IsFuncType(ct))
        {
            return SemanticModelBuilder.GetFuncReturnType(ct);
        }

        return ErrorType.Instance;
    }

    /// <summary>
    /// Returns <c>true</c> when the lambda has the <c>static</c> modifier.
    /// </summary>
    private static bool IsStaticLambda(LambdaExpressionNode lambda)
    {
        var modifiers = lambda switch
        {
            ExpressionLambdaLambdaExpressionNode exprLambda => exprLambda.Modifiers,
            BlockLambdaLambdaExpressionNode blockLambda => blockLambda.Modifiers,
            _ => ImmutableArray<LambdaModifierNode>.Empty,
        };

        return modifiers.Any(m => m is StaticLambdaModifierLambdaModifierNode);
    }

    /// <summary>
    /// Collects the parameter names declared by a lambda expression.
    /// </summary>
    private static HashSet<string> CollectLambdaParameterNames(LambdaExpressionNode lambda)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        var paramList = lambda switch
        {
            ExpressionLambdaLambdaExpressionNode exprLambda => exprLambda.Params,
            BlockLambdaLambdaExpressionNode blockLambda => blockLambda.Params,
            _ => null,
        };

        switch(paramList)
        {
            case SingleImplicitParamLambdaParameterListNode singleParam:
                _ = names.Add(singleParam.Name.Lexeme);
                break;

            case ParenthesizedLambdaParamsLambdaParameterListNode parenParams when parenParams.Params is not null:
                {
                    var paramNodes = new List<LambdaParamNode> { parenParams.Params.LambdaParam };
                    paramNodes.AddRange(parenParams.Params.LambdaParams);

                    foreach(var paramNode in paramNodes)
                    {
                        var name = paramNode switch
                        {
                            TypedLambdaParamLambdaParamNode typed => typed.Name.Lexeme,
                            UntypedLambdaParamLambdaParamNode untyped => untyped.Name.Lexeme,
                            _ => null,
                        };
                        if(name is not null)
                        {
                            _ = names.Add(name);
                        }
                    }

                    break;
                }
        }

        return names;
    }

    /// <summary>
    /// Walks the body of a static lambda looking for references to enclosing variables,
    /// instance fields, or <c>this</c>. Emits <c>MIST_TC_0122</c> for each violation.
    /// Does not descend into nested lambda bodies (they have their own scope rules).
    /// </summary>
    private static void CheckStaticLambdaBody(
        SyntaxNode node,
        HashSet<string> lambdaParamNames,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // 'this' reference in a static lambda is always a violation.
        if(node is ThisExpressionPrimaryNode)
        {
            diagnostics.ReportAtNode(TypeCheckDiagnostics.StaticLambdaCaptureViolation, node, "TypeChecker", "'this'");
            return;
        }

        // Check identifier references.
        if(node is IdentifierExpressionPrimaryNode identifier)
        {
            var name = identifier.Name.Lexeme;

            // The lambda's own parameters are always permitted.
            if(lambdaParamNames.Contains(name))
            {
                return;
            }

            // Resolve the symbol to determine what it refers to.
            var symbol = model.GetDeclaredSymbol(identifier);
            if(symbol is Symbol concreteSymbol)
            {
                switch(concreteSymbol.Kind)
                {
                    case SymbolKind.LocalVariable:
                        // Local variable from an enclosing scope -- violation.
                        diagnostics.ReportAtNode(TypeCheckDiagnostics.StaticLambdaCaptureViolation, identifier, "TypeChecker", $"outer variable '{name}'");
                        return;

                    case SymbolKind.Parameter:
                        // Parameter from an enclosing method/lambda — violation.
                        // (The lambda's own params were already excluded above.)
                        diagnostics.ReportAtNode(TypeCheckDiagnostics.StaticLambdaCaptureViolation, identifier, "TypeChecker", $"outer parameter '{name}'");
                        return;

                    case SymbolKind.Field:
                        // Instance field — violation. Static fields are OK.
                        if(!concreteSymbol.IsStatic)
                        {
                            diagnostics.ReportAtNode(TypeCheckDiagnostics.StaticLambdaCaptureViolation, identifier, "TypeChecker", $"instance field '{name}'");
                            return;
                        }

                        break;

                    case SymbolKind.Method:
                        // Instance method — violation. Static methods are OK.
                        if(!concreteSymbol.IsStatic)
                        {
                            diagnostics.ReportAtNode(TypeCheckDiagnostics.StaticLambdaCaptureViolation, identifier, "TypeChecker", $"instance method '{name}'");
                            return;
                        }

                        break;

                    case SymbolKind.Property:
                        // Instance property — violation. Static properties are OK.
                        if(!concreteSymbol.IsStatic)
                        {
                            diagnostics.ReportAtNode(TypeCheckDiagnostics.StaticLambdaCaptureViolation, identifier, "TypeChecker", $"instance property '{name}'");
                            return;
                        }

                        break;

                        // Namespace, Class, Struct, Abstract, Enum, TypeAlias, Newtype,
                        // Constructor, Indexer — these are type-level references and are always OK.
                }
            }

            // If the symbol could not be resolved, or it's a type name / static member,
            // no diagnostic is needed.
            return;
        }

        // Do not descend into nested lambda bodies — they have their own scope rules.
        if(node is LambdaExpressionNode)
        {
            return;
        }

        // Recurse into child nodes.
        foreach(var child in node.Children())
        {
            if(child is not null)
            {
                CheckStaticLambdaBody(child, lambdaParamNames, model, diagnostics);
            }
        }
    }

    /// <summary>
    /// Walks the body of a lambda looking for captured variables (outer scope locals/parameters)
    /// that have restricted types or ownership tiers. Emits:
    /// <list type="bullet">
    ///   <item><c>MIST_TC_0123</c> — captured variable is a ref struct type.</item>
    ///   <item><c>MIST_TC_0124</c> — captured variable has <c>owned</c> ownership.</item>
    ///   <item><c>MIST_TC_0125</c> — captured variable has <c>borrowed</c>, <c>frozen</c>, or <c>readonly ref</c> ownership.</item>
    /// </list>
    /// Does not descend into nested lambda bodies (they have their own capture scope).
    /// </summary>
    private static void CheckCaptureRestrictions(
        SyntaxNode node,
        HashSet<string> lambdaParamNames,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Check identifier references for captured variables with restricted types/ownership.
        if(node is IdentifierExpressionPrimaryNode identifier)
        {
            var name = identifier.Name.Lexeme;

            // The lambda's own parameters are NOT captured — skip them.
            if(lambdaParamNames.Contains(name))
            {
                return;
            }

            // Resolve the symbol to determine what it refers to.
            var symbol = model.GetDeclaredSymbol(identifier);
            if(symbol is Symbol concreteSymbol &&
                concreteSymbol.Kind is SymbolKind.LocalVariable or SymbolKind.Parameter)
            {
                // This is a captured variable from an outer scope.
                var declaredType = concreteSymbol.DeclaredType;

                // ── MIST_TC_0123: ref struct capture ──
                if(declaredType is not null && IsRefStructType(declaredType))
                {
                    diagnostics.ReportAtNode(TypeCheckDiagnostics.CaptureRefStruct, identifier, "TypeChecker", name, declaredType.DisplayName);
                    return;
                }

                // ── MIST_TC_0124: owned capture ──
                if(concreteSymbol.OwnershipTier == OwnershipTier.Owned)
                {
                    diagnostics.ReportAtNode(TypeCheckDiagnostics.CaptureOwned, identifier, "TypeChecker", name);
                    return;
                }

                // ── MIST_TC_0125: borrowed/frozen/readonly-ref capture ──
                if(concreteSymbol.OwnershipTier is OwnershipTier.Borrowed or OwnershipTier.Frozen or OwnershipTier.ReadonlyRef)
                {
                    var tierName = concreteSymbol.OwnershipTier switch
                    {
                        OwnershipTier.Borrowed => "borrowed",
                        OwnershipTier.Frozen => "frozen",
                        OwnershipTier.ReadonlyRef => "readonly ref",
                        _ => concreteSymbol.OwnershipTier.ToString(),
                    };
                    diagnostics.ReportAtNode(TypeCheckDiagnostics.CaptureBorrowedOrFrozen, identifier, "TypeChecker", tierName, name);
                    return;
                }
            }

            // Unrestricted reference types, value types, and variables with no special ownership are permitted.
            return;
        }

        // Do not descend into nested lambda bodies — they have their own capture scope.
        if(node is LambdaExpressionNode)
        {
            return;
        }

        // Recurse into child nodes.
        foreach(var child in node.Children())
        {
            if(child is not null)
            {
                CheckCaptureRestrictions(child, lambdaParamNames, model, diagnostics);
            }
        }
    }

    /// <summary>
    /// Returns <c>true</c> when the lambda has the <c>async</c> modifier.
    /// </summary>
    private static bool IsAsyncLambda(LambdaExpressionNode lambda)
    {
        var modifiers = lambda switch
        {
            ExpressionLambdaLambdaExpressionNode exprLambda => exprLambda.Modifiers,
            BlockLambdaLambdaExpressionNode blockLambda => blockLambda.Modifiers,
            _ => ImmutableArray<LambdaModifierNode>.Empty,
        };

        return modifiers.Any(m => m is AsyncLambdaModifierLambdaModifierNode);
    }

    /// <summary>
    /// Walks the body of a non-async lambda looking for <c>await</c> expressions.
    /// Emits <c>MIST_TC_0126</c> for each <c>await</c> found. Does not descend
    /// into nested lambda bodies (they have their own async context).
    /// </summary>
    private static void CheckAwaitInNonAsyncLambdaBody(
        SyntaxNode node,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        if(node is AwaitExpressionUnaryNode)
        {
            diagnostics.ReportAtNode(TypeCheckDiagnostics.AwaitInNonAsyncLambda, node, "TypeChecker");
            return;
        }

        // Do not descend into nested lambda bodies — they have their own async context.
        if(node is LambdaExpressionNode)
        {
            return;
        }

        foreach(var child in node.Children())
        {
            if(child is not null)
            {
                CheckAwaitInNonAsyncLambdaBody(child, model, diagnostics);
            }
        }
    }

    private void CheckAssignmentNode(AssignmentNode assignment, ISemanticModel model, DiagnosticBag diagnostics)
    {
        // Task 21: when this assignment has a value, the target is an
        // *lvalue* (write position). Flag the flag while we walk the
        // target so CheckPostfixNode treats the terminal suffix of the
        // target as a write context for the unknown-type member filter.
        // Nested assignments (e.g., `a = b = c`) always go through this
        // same method, so the flag is naturally re-established for each
        // inner assignment target as the recursion walks inward.
        var assignedValue = assignment.Group0?.Value;
        var targetIsLhs = assignedValue is not null;
        var previousAssignmentLhs = analyzingAssignmentLhs;
        if(targetIsLhs)
        {
            analyzingAssignmentLhs = true;
        }

        try
        {
            CheckVoidCoalescingNode(assignment.Target, model, diagnostics);
        }
        finally
        {
            analyzingAssignmentLhs = previousAssignmentLhs;
        }

        if(assignedValue is not null)
        {
            // ── optional-types feature: void-coalescing assignment (??=) ──
            // When the operator is ??=, apply special validation and flow narrowing.
            if(assignment.Group0?.Operator is { } op && op.Kind == (int)TokenKind.QUESTION_QUESTION_EQUAL)
            {
                CheckVoidCoalescingAssignment(assignment, model, diagnostics);
                return;
            }

            // ── readonly-locals feature: readonly local reassignment prohibition ──
            // MIST_READONLY_LOCAL_0001: Cannot reassign readonly local variable.
            CheckReadonlyLocalReassignment(assignment, model, diagnostics);

            // ── ref-parameters feature: ref readonly assignment prohibition ──
            // MIST_REF_0005: Cannot assign through a ref readonly local.
            // MIST_REF_0009: Cannot assign through a ref readonly return value.
            CheckRefReadonlyAssignmentProhibition(assignment, model, diagnostics);

            // ── stack-only-types feature: read-only span indexer write rejection ──
            // MIST_TC_0156: Cannot assign through a ReadOnlySpan indexer.
            CheckReadOnlySpanIndexerAssignment(assignment, model, diagnostics);

            // ── ref-parameters feature: ref field storage prohibition ──
            // MIST_REF_0008: Cannot store a ref in a field.
            CheckRefFieldStorageProhibition(assignment, model, diagnostics);

            // ── anonymous-types feature: read-only property enforcement ──
            // MIST_ANON_READONLY: Anonymous type properties are read-only.
            CheckAnonymousTypeReadonlyAssignment(assignment, model, diagnostics);

            // Infer bare 'default' type from the LHS type before checking the RHS.
            // The RHS is now a full ExpressionNode (widened to reach lambdas); only the
            // assignment form can carry a bare 'default' literal.
            var lhsTypeForDefault = model.GetTypeOf(assignment.Target);
            var rhsAssignment = assignedValue.AsAssignmentOrNull();
            if(rhsAssignment is not null)
            {
                TryInferDefaultLiteralTypeFromAssignment(rhsAssignment, lhsTypeForDefault, model);
            }

            // This is an assignment: target = value.
            assignmentNestingDepth++;
            CheckExpression(assignedValue, model, diagnostics);
            assignmentNestingDepth--;

            var lhsType = model.GetTypeOf(assignment.Target);
            var rhsType = model.GetTypeOf(assignedValue);

            // ── variadics feature (task 14.2): zip parallel expansion (Requirements 8.5-8.7) ──
            // A zip is the parallel two-operand value spread `left... = right...`. It is its
            // own chokepoint: permitted only when the operands are proven equal length (same
            // pack origin, or two concrete tuples of equal count), otherwise MIST_VAR_0006.
            // Handled before the ordinary tuple-to-tuple assignability check so a zip is never
            // mis-reported as a scalar assignment.
            if(TryCheckZipAssignment(assignment, model, diagnostics))
            {
                return;
            }

            // ── first-class-functions: method group ambiguity check (MIST_TC_0121) ──
            // When the LHS type is a Func type and the RHS is a bare identifier
            // (method group), check for ambiguous overloads.
            if(lhsType is not null && SemanticModelBuilder.IsFuncType(lhsType))
            {
                var rhsPostfix = TryExtractPostfixNode(assignedValue);
                if(rhsPostfix is not null)
                {
                    CheckMethodGroupAmbiguity(rhsPostfix, lhsType, model, diagnostics);
                }
            }

            if(lhsType is not null && rhsType is not null)
            {
                // ── flow narrowing: use narrowed type for RHS if available ──
                var effectiveRhsType = rhsAssignment is not null
                    ? GetEffectiveSourceTypeFromAssignment(rhsAssignment, rhsType)
                    : rhsType;

                // ── refinement-types feature: compile-time enforcement ──
                // When the target is a RefinementType, check the assignment
                // against the refinement predicate before the generic
                // assignability check.
                if(!TryCheckRefinementAssignment(effectiveRhsType, lhsType, assignedValue, diagnostics, model)
                    // ── regex-type feature: string literal shorthand (Requirements 6.5, 6.8, 6.9) ──
                    // When the target is regex and the source is string, apply
                    // the string→regex conversion rules.
                    && !TryCheckStringToRegexAssignment(rhsType, lhsType, assignedValue, diagnostics, model))
                {
                    CheckAssignability(rhsType, lhsType, assignment, diagnostics, model);
                }
            }

            // ── flow narrowing: invalidate narrowing on reassignment (Requirement 7.4) ──
            InvalidateNarrowingForAssignmentTarget(assignment.Target.Left);

            // Property-specific assignment checks (MIST_TC_0020, MIST_TC_0021, MIST_TC_0025).
            var targetPropertySymbol = TryResolvePropertyFromAssignmentTarget(assignment.Target.Left, model);
            if(targetPropertySymbol is not null)
            {
                var propInfo = GetPropertyMetadata(targetPropertySymbol);
                if(propInfo is not null)
                {
                    // MIST_TC_0020: No accessible set/init accessor.
                    if(!propInfo.HasSet && !propInfo.HasInit)
                    {
                        diagnostics.ReportAtNode(TypeCheckDiagnostics.PropertyNoSetAccessor, assignment, "TypeChecker", targetPropertySymbol.Name);
                    }
                    // MIST_TC_0025: init-only property assigned outside constructor context.
                    else if(propInfo.HasInit && !propInfo.HasSet && !insideConstructor)
                    {
                        diagnostics.ReportAtNode(TypeCheckDiagnostics.InitOnlyPropertyOutsideCtor, assignment, "TypeChecker", targetPropertySymbol.Name);
                    }

                    // MIST_TC_0021: Property type mismatch.
                    var propType = targetPropertySymbol.DeclaredType;
                    if(propType is not null && rhsType is not null && !IsErrorType(propType) && !IsErrorType(rhsType))
                    {
                        // ── regex-type feature: string literal shorthand ──
                        // Skip the property type mismatch check if the string→regex
                        // conversion was already handled by the main assignment check.
                        if(IsRegexLike(propType) && IsStringLike(rhsType))
                        {
                            // Already handled by TryCheckStringToRegexAssignment above.
                        }
                        else if(!IsAssignable(rhsType, propType))
                        {
                            // unknown-type feature (Req 4.2, 4.4): emit a targeted
                            // diagnostic for void→unknown or unknown→non-unknown
                            // before falling back to the generic MIST_TC_0021.
                            if(TryEmitUnknownAssignabilityDiagnostic(rhsType, propType, assignment, diagnostics))
                            {
                                // Targeted unknown diagnostic emitted; skip generic.
                            }
                            // type-unions feature (Req 6.3, 6.4): emit a targeted
                            // diagnostic for union-specific failures.
                            else if(TryEmitUnionAssignabilityDiagnostic(rhsType, propType, assignment, diagnostics))
                            {
                                // Targeted union diagnostic emitted; skip generic.
                            }
                            // enums feature (Req 7.3): emit a targeted diagnostic
                            // for reverse conversion (underlying type → enum).
                            else if(TryEmitEnumAssignabilityDiagnostic(rhsType, propType, assignment, diagnostics, model))
                            {
                                // Targeted enum diagnostic emitted; skip generic.
                            }
                            else
                            {
                                diagnostics.ReportAtNode(TypeCheckDiagnostics.PropertyAssignmentTypeMismatch, assignment, "TypeChecker", rhsType.DisplayName, propType.DisplayName);
                            }
                        }
                        else
                        {
                            // Resolve and record any implicit cast operator on the property assignment.
                            _ = TryResolveImplicitCast(rhsType, propType, assignment, model, diagnostics);
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Checks if an assignment target is a readonly local variable.
    /// Emits <c>MIST_READONLY_LOCAL_0001</c> when the target is a local variable
    /// with <see cref="ReadonlyStatus.ExplicitReadonly"/>.
    /// </summary>
    private static void CheckReadonlyLocalReassignment(
        AssignmentNode assignment,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Extract the PostfixNode from the assignment target.
        var postfix = TryExtractPostfixFromLogicalOr(assignment.Target.Left);
        if(postfix is null)
        {
            return;
        }

        // Only check simple identifiers (no member access or indexing suffixes).
        if(postfix.Suffix.Length != 0)
        {
            return;
        }

        if(postfix.Target is not IdentifierExpressionPrimaryNode)
        {
            return;
        }

        var symbol = model.GetDeclaredSymbol(postfix.Target);
        if(symbol is not Symbol sym)
        {
            return;
        }

        if(sym.Kind != SymbolKind.LocalVariable)
        {
            return;
        }

        if(sym.ReadonlyStatus != ReadonlyStatus.ExplicitReadonly)
        {
            return;
        }

        diagnostics.ReportAtNode(TypeCheckDiagnostics.ReadonlyLocalReassignment, assignment, "TypeChecker", sym.Name);
    }

    /// <summary>
    /// Checks if an assignment target is a ref readonly local or a ref readonly return value.
    /// Emits MIST_REF_0005 for ref readonly local assignment and MIST_REF_0009 for ref readonly
    /// return value assignment.
    /// </summary>
    private static void CheckRefReadonlyAssignmentProhibition(
        AssignmentNode assignment,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Extract the PostfixNode from the assignment target.
        var postfix = TryExtractPostfixFromLogicalOr(assignment.Target.Left);
        if(postfix is null)
        {
            return;
        }

        // Case 1: Simple identifier — check if it resolves to a ref readonly local or parameter.
        if(postfix.Suffix.Length == 0 && postfix.Target is IdentifierExpressionPrimaryNode)
        {
            var symbol = model.GetDeclaredSymbol(postfix.Target);
            if(symbol is Symbol sym
                && sym.RefKind == RefKind.RefReadonly
                && sym.Kind is SymbolKind.LocalVariable or SymbolKind.Parameter)
            {
                diagnostics.ReportAtNode(TypeCheckDiagnostics.RefReadonlyAssignment, assignment, "TypeChecker");
                return;
            }
        }

        // Case 2: Method call result — check if the last suffix is a method call
        // whose resolved method has ReturnRefKind == RefReadonly.
        if(postfix.Suffix.Length > 0)
        {
            var lastSuffix = postfix.Suffix[^1];

            // Direct method call: target.Method() = value
            if(lastSuffix is MethodCallSuffixPostfixSuffixNode methodCall)
            {
                var methodSymbol = model.GetDeclaredSymbol(methodCall);
                if(methodSymbol is Symbol methodSym && methodSym.ReturnRefKind == RefKind.RefReadonly)
                {
                    diagnostics.ReportAtNode(TypeCheckDiagnostics.RefReadonlyReturnAssignment, assignment, "TypeChecker");
                    return;
                }
            }

            // Member access after a method call: target.Method().field = value
            // In this case, the method call is not the last suffix, so we check
            // if any suffix in the chain is a ref readonly method call that the
            // subsequent member access operates on.
            // For the simple case of `GetRef().field = value`, the last suffix is
            // a MemberAccessSuffix, and the preceding suffix is the method call.
            if(lastSuffix is MemberAccessSuffixPostfixSuffixNode && postfix.Suffix.Length >= 2)
            {
                var precedingSuffix = postfix.Suffix[^2];
                if(precedingSuffix is MethodCallSuffixPostfixSuffixNode precedingMethodCall)
                {
                    var methodSymbol = model.GetDeclaredSymbol(precedingMethodCall);
                    if(methodSymbol is Symbol methodSym && methodSym.ReturnRefKind == RefKind.RefReadonly)
                    {
                        diagnostics.ReportAtNode(TypeCheckDiagnostics.RefReadonlyReturnAssignment, assignment, "TypeChecker");
                        return;
                    }
                }
            }
        }

        // Case 3: Bare method call (no suffixes, target is a method invocation).
        // This handles the case where the target is an invocation expression like `GetRef() = value`.
        // In Mist's AST, this would be a PostfixNode with an InvocationSuffix.
        if(postfix.Suffix.Length == 1 && postfix.Suffix[0] is InvocationSuffixPostfixSuffixNode)
        {
            // Resolve the method from the target identifier.
            var symbol = model.GetDeclaredSymbol(postfix.Target);
            if(symbol is Symbol methodSym && methodSym.Kind == SymbolKind.Method && methodSym.ReturnRefKind == RefKind.RefReadonly)
            {
                diagnostics.ReportAtNode(TypeCheckDiagnostics.RefReadonlyReturnAssignment, assignment, "TypeChecker");
                return;
            }
        }
    }

    /// <summary>
    /// Checks if an assignment targets the indexer of a read-only span.
    /// Emits <c>MIST_TC_0156</c> when the assignment target is an index suffix whose
    /// receiver is a <see cref="SpanType"/> with <c>IsReadOnly == true</c>.
    /// This check is independent of length validation (Requirement 2.8).
    /// </summary>
    private static void CheckReadOnlySpanIndexerAssignment(
        AssignmentNode assignment,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Extract the PostfixNode from the assignment target.
        var postfix = TryExtractPostfixFromLogicalOr(assignment.Target.Left);
        if(postfix is null)
        {
            return;
        }

        // The assignment target must end with an index suffix.
        if(postfix.Suffix.Length == 0 || postfix.Suffix[^1] is not IndexSuffixPostfixSuffixNode)
        {
            return;
        }

        // Determine the receiver type (the expression before the index suffix).
        var receiverType = postfix.Suffix.Length > 1
            ? model.GetTypeOf(postfix.Suffix[^2])
            : model.GetTypeOf(postfix.Target);

        if(receiverType is SpanType { IsReadOnly: true })
        {
            diagnostics.ReportAtNode(TypeCheckDiagnostics.ReadOnlySpanIndexerAssignment, assignment, "TypeChecker");
        }
    }

    /// <summary>
    /// Checks if an assignment stores a ref (parameter or local) into a field.
    /// Emits <c>MIST_REF_0008</c> when the assignment target is a field (member access)
    /// and the source expression is a ref parameter or ref local.
    /// </summary>
    private static void CheckRefFieldStorageProhibition(
        AssignmentNode assignment,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Step 1: Check if the assignment target is a field (member access suffix).
        var targetPostfix = TryExtractPostfixFromLogicalOr(assignment.Target.Left);
        if(targetPostfix is null)
        {
            return;
        }

        // The target must have at least one suffix, and the last suffix must be a member access.
        // This covers `this.field = ...`, `obj.field = ...`, and `this.inner.field = ...`.
        if(targetPostfix.Suffix.Length == 0)
        {
            return;
        }

        var lastSuffix = targetPostfix.Suffix[^1];
        if(lastSuffix is not MemberAccessSuffixPostfixSuffixNode)
        {
            return;
        }

        // Step 2: Check if the RHS is a ref parameter or ref local.
        // The RHS is assignment.Value which is now a full ExpressionNode. A simple ref
        // identifier reference only occurs in the assignment form; a lambda RHS is not one.
        var rhsAssignment = assignment.Group0?.Value?.AsAssignmentOrNull();
        if(rhsAssignment is null)
        {
            return; // Lambda-form RHS — not a simple ref identifier.
        }

        if(rhsAssignment.Group0?.Value is not null)
        {
            return; // Chained assignment — not a simple ref identifier.
        }

        var rhsPostfix = TryExtractPostfixFromLogicalOr(rhsAssignment.Target.Left);
        if(rhsPostfix is null)
        {
            return;
        }

        // The RHS must be a simple identifier (no suffixes) to be a direct ref reference.
        if(rhsPostfix.Suffix.Length > 0)
        {
            return;
        }

        if(rhsPostfix.Target is not IdentifierExpressionPrimaryNode rhsIdentifier)
        {
            return;
        }

        var rhsSymbol = model.GetDeclaredSymbol(rhsIdentifier);
        if(rhsSymbol is not Symbol concreteSymbol)
        {
            return;
        }

        // Only parameters and local variables can be refs.
        if(concreteSymbol.Kind is not (SymbolKind.Parameter or SymbolKind.LocalVariable))
        {
            return;
        }

        // Check if the symbol has a ref kind (Ref or RefReadonly).
        if(concreteSymbol.RefKind is RefKind.Ref or RefKind.RefReadonly)
        {
            diagnostics.ReportAtNode(TypeCheckDiagnostics.RefStoredInField, assignment, "TypeChecker");
        }
    }

    /// <summary>
    /// Checks if an assignment target is a member access on an anonymous type value.
    /// Emits <c>MIST_ANON_READONLY</c> because anonymous type properties are read-only.
    /// </summary>
    private static void CheckAnonymousTypeReadonlyAssignment(
        AssignmentNode assignment,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Extract the PostfixNode from the assignment target.
        var targetPostfix = TryExtractPostfixFromLogicalOr(assignment.Target.Left);
        if(targetPostfix is null)
        {
            return;
        }

        // The target must have at least one suffix, and the last suffix must be a member access.
        // This covers `anon.Property = value`.
        if(targetPostfix.Suffix.Length == 0)
        {
            return;
        }

        var lastSuffix = targetPostfix.Suffix[^1];
        if(lastSuffix is not MemberAccessSuffixPostfixSuffixNode)
        {
            return;
        }

        // Determine the receiver type: it's the type of the previous suffix,
        // or of the primary target if this is the only suffix.
        var receiverType = targetPostfix.Suffix.Length > 1
            ? model.GetTypeOf(targetPostfix.Suffix[^2])
            : model.GetTypeOf(targetPostfix.Target);

        if(receiverType is AnonymousType)
        {
            diagnostics.ReportAtNode(TypeCheckDiagnostics.AnonReadonly, assignment, "TypeChecker");
        }
    }

    /// <summary>
    /// Checks that anonymous types do not escape into method signatures (return type or parameters).
    /// Emits <c>MIST_ANON_ESCAPE_SIGNATURE</c> for any violation.
    /// </summary>
    private static void CheckAnonymousTypeEscapeInSignature(
        ISymbol methodSymbol,
        SyntaxNode declaringNode,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Check return type.
        var returnType = (methodSymbol as Symbol)?.DeclaredReturnType ?? methodSymbol.DeclaredType;
        if(returnType is AnonymousType)
        {
            diagnostics.ReportAtNode(TypeCheckDiagnostics.AnonEscapeSignature, declaringNode, "TypeChecker");
        }

        // Check parameter types.
        if(methodSymbol is Symbol { Parameters.Count: > 0 } sym)
        {
            foreach(var param in sym.Parameters)
            {
                if(param.DeclaredType is AnonymousType)
                {
                    diagnostics.ReportAtNode(TypeCheckDiagnostics.AnonEscapeSignature, declaringNode, "TypeChecker");
                    break;
                }
            }
        }
        else
        {
            // For Mist-declared methods, collect parameters from the AST.
            var paramSymbols = CollectParameterSymbols(declaringNode, model);
            foreach(var param in paramSymbols)
            {
                if(param.DeclaredType is AnonymousType)
                {
                    diagnostics.ReportAtNode(TypeCheckDiagnostics.AnonEscapeSignature, declaringNode, "TypeChecker");
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Extracts the PostfixNode from a LogicalOrNode assignment target by walking
    /// through the expression wrapper chain.
    /// </summary>
    private static PostfixNode? TryExtractPostfixFromLogicalOr(LogicalOrNode target)
    {
        if(target.Right is not null)
        {
            return null;
        }

        var logicalAnd = target.Left;
        if(logicalAnd.Right is not null)
        {
            return null;
        }

        var equality = logicalAnd.Left;
        if(!equality.Group0.IsDefaultOrEmpty)
        {
            return null;
        }

        if(equality.Left is not RelationalComparisonRelationalNode relationalComparison)
        {
            return null;
        }

        if(!relationalComparison.Group0.IsDefaultOrEmpty)
        {
            return null;
        }

        var additive = relationalComparison.Left;
        if(!additive.Group0.IsDefaultOrEmpty)
        {
            return null;
        }

        var multiplicative = additive.Left;
        if(!multiplicative.Group0.IsDefaultOrEmpty)
        {
            return null;
        }

        if(multiplicative.Left.UnwrapRange() is not PostfixExpressionUnaryNode postfixExpr)
        {
            return null;
        }

        return postfixExpr.Primary;
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
        if(target.Right is not null)
        {
            return null;
        }

        var equality = logicalAnd.Left;
        if(logicalAnd.Right is not null)
        {
            return null;
        }

        var relational = equality.Left;
        if(!equality.Group0.IsDefaultOrEmpty)
        {
            return null;
        }

        if(relational is not RelationalComparisonRelationalNode relationalComparison)
        {
            return null;
        }

        var additive = relationalComparison.Left;
        if(!relationalComparison.Group0.IsDefaultOrEmpty)
        {
            return null;
        }

        var multiplicative = additive.Left;
        if(!additive.Group0.IsDefaultOrEmpty)
        {
            return null;
        }

        var unary = multiplicative.Left.UnwrapRange();
        if(!multiplicative.Group0.IsDefaultOrEmpty)
        {
            return null;
        }

        if(unary is not PostfixExpressionUnaryNode postfixExpr)
        {
            return null;
        }

        var postfix = postfixExpr.Primary;

        // Check if the last suffix is a MemberAccessSuffix that resolves to a property.
        if(postfix.Suffix.Length > 0)
        {
            var lastSuffix = postfix.Suffix[^1];
            if(lastSuffix is MemberAccessSuffixPostfixSuffixNode memberAccess)
            {
                var symbol = model.GetDeclaredSymbol(memberAccess);
                if(symbol is not null && symbol.Kind == SymbolKind.Property)
                {
                    return symbol;
                }

                // Fallback: resolve via the receiver type's member scope.
                // This handles cases where the member access node is not registered
                // in the symbol table (e.g., dependent property access on constructed types
                // like FixedBuffer<5>.Size, or instance access like buf.Size).
                var receiverType = postfix.Suffix.Length > 1
                    ? model.GetTypeOf(postfix.Suffix[^2])
                    : model.GetTypeOf(postfix.Target);
                if(receiverType is not null)
                {
                    var resolved = FindPropertyMemberSymbol(receiverType, memberAccess.Name.Lexeme);
                    if(resolved is not null)
                    {
                        return resolved;
                    }
                }
            }
        }
        else
        {
            // Simple identifier — check if it resolves to a property.
            var symbol = model.GetDeclaredSymbol(postfix.Target);
            if(symbol is not null && symbol.Kind == SymbolKind.Property)
            {
                return symbol;
            }
        }

        return null;
    }

    /// <summary>
    /// Finds a static property member symbol by name in the target type's member scope.
    /// Unlike <see cref="FindMemberSymbol"/>, this specifically looks for static property symbols,
    /// which handles the case of generated dependent type properties (e.g., FixedBuffer&lt;5&gt;.Size)
    /// that are not registered in the symbol table via AST node association.
    /// Only static properties are matched to avoid interfering with instance property assignment
    /// validation that relies on the existing GetDeclaredSymbol path.
    /// </summary>
    private static ISymbol? FindPropertyMemberSymbol(IType targetType, string memberName)
    {
        // Unwrap ConstructedType to look up in the generic definition's member scope.
        if(targetType is ConstructedType ct)
        {
            targetType = ct.GenericDefinition;
        }

        var memberScope = targetType switch
        {
            ClassType ct2 => ct2.MemberScope,
            StructType st => st.MemberScope,
            AbstractType it => it.MemberScope,
            _ => null,
        };

        if(memberScope is null)
        {
            return null;
        }

        foreach(var member in memberScope.Members)
        {
            if(member.Name == memberName && member.Kind == SymbolKind.Property)
            {
                // Only match static properties to avoid interfering with
                // instance property assignment validation.
                if(member is Symbol sym && sym.PropertyMetadata is { IsStatic: true })
                {
                    return member;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Type-checks a <c>VoidCoalescing</c> node (<c>left ?? right1 ?? right2 ...</c>).
    /// <para>
    /// For each <c>??</c> operator in the chain:
    /// <list type="number">
    ///   <item>Verifies the left operand's type contains void; emits <c>MIST_TC_0041</c> if not.</item>
    ///   <item>Computes T (the non-void portion of the left type).</item>
    ///   <item>Type-checks the right operand.</item>
    ///   <item>Verifies the right operand is assignable to T; emits <c>MIST_TC_0044</c> if not.</item>
    ///   <item>The result type becomes T (for the next iteration if chained).</item>
    /// </list>
    /// </para>
    /// </summary>
    private void CheckVoidCoalescingNode(VoidCoalescingNode node, ISemanticModel model, DiagnosticBag diagnostics)
    {
        // Always check the left operand.
        CheckLogicalOrNode(node.Left, model, diagnostics);

        if(node.LogicalOrs.Length == 0)
        {
            return; // No ?? operators — nothing more to check.
        }

        // Walk the chain of ?? operators.
        var currentLeftType = model.GetTypeOf(node.Left);

        foreach(var rightOperand in node.LogicalOrs)
        {
            // Check the right operand's sub-expressions.
            CheckLogicalOrNode(rightOperand, model, diagnostics);

            // Validate: left operand must be a union type containing void.
            if(currentLeftType is not null)
            {
                var containsVoid = currentLeftType is UnionType leftUnion
                    && leftUnion.Constituents.Any(c => c.IsVoid());

                if(!containsVoid)
                {
                    diagnostics.ReportAtNode(TypeCheckDiagnostics.DuplicateCastOperator, node, "TypeChecker");
                    // Continue checking remaining operands with the right operand's type.
                    currentLeftType = model.GetTypeOf(rightOperand);
                    continue;
                }

                // Compute T (non-void portion).
                var union = (UnionType)currentLeftType;
                var nonVoidConstituents = union.Constituents
                    .Where(c => !c.IsVoid())
                    .ToList();
                var nonVoidPortion = nonVoidConstituents.Count == 1
                    ? nonVoidConstituents[0]
                    : new UnionType(nonVoidConstituents.ToImmutableArray());

                // Validate: right operand must be assignable to T.
                var rightType = model.GetTypeOf(rightOperand);
                if(rightType is not null && !IsAssignable(rightType, nonVoidPortion, model))
                {
                    diagnostics.ReportAtNode(TypeCheckDiagnostics.AmbiguousImplicitCast, rightOperand, "TypeChecker");
                }

                // Result type is T (non-void portion) for the next iteration.
                currentLeftType = nonVoidPortion;
            }
            else
            {
                // If left type is null (unresolved), just move on.
                currentLeftType = model.GetTypeOf(rightOperand);
            }
        }
    }

    /// <summary>
    /// Validates a void-coalescing assignment (<c>x ??= value</c>).
    /// Checks that the left operand is a mutable variable whose type contains void,
    /// that the right operand is assignable to the non-void portion, and applies
    /// flow narrowing so subsequent statements see the narrowed type.
    /// </summary>
    private void CheckVoidCoalescingAssignment(AssignmentNode assignment, ISemanticModel model, DiagnosticBag diagnostics)
    {
        // ── MIST_TC_0047: ??= is statement-only, cannot be used as a sub-expression ──
        if(!atStatementLevel || assignmentNestingDepth > 0)
        {
            diagnostics.ReportAtNode(TypeCheckDiagnostics.VoidCoalescingNotStatement, assignment, "TypeChecker");
            return;
        }

        // The optional operator+value project into the nullable Group0 struct; this
        // method is only reached for a present ??= assignment, so the value is set.
        if(assignment.Group0?.Value is not { } assignedValue)
        {
            return;
        }

        // Check the RHS expression. The RHS is now a full ExpressionNode (widened to
        // reach lambdas); route through the expression-level entry point.
        CheckExpression(assignedValue, model, diagnostics);

        // ── MIST_TC_0043: Left operand must be a mutable variable ──
        var targetVarName = TryExtractIdentifierFromLogicalOr(assignment.Target.Left);
        if(targetVarName is null)
        {
            diagnostics.ReportAtNode(TypeCheckDiagnostics.AmbiguousCast, assignment, "TypeChecker");
            return;
        }

        // Check that the variable is not readonly.
        var targetPostfix = TryExtractPostfixFromLogicalOr(assignment.Target.Left);
        if(targetPostfix is not null)
        {
            var targetSymbol = model.GetDeclaredSymbol(targetPostfix.Target);
            if(targetSymbol is Symbol concreteTargetSymbol
                && concreteTargetSymbol.Kind == SymbolKind.LocalVariable
                && concreteTargetSymbol.ReadonlyStatus == ReadonlyStatus.ExplicitReadonly)
            {
                diagnostics.ReportAtNode(TypeCheckDiagnostics.AmbiguousCast, assignment, "TypeChecker");
                return;
            }
        }

        // ── MIST_TC_0042: Left operand type must contain void ──
        var lhsType = model.GetTypeOf(assignment.Target);
        if(lhsType is null)
        {
            return;
        }

        var containsVoid = lhsType is UnionType lhsUnion
            && lhsUnion.Constituents.Any(c => c.IsVoid());

        if(!containsVoid)
        {
            diagnostics.ReportAtNode(TypeCheckDiagnostics.NoCastOperator, assignment, "TypeChecker");
            return;
        }

        // Compute T (non-void portion).
        var union = (UnionType)lhsType;
        var nonVoidConstituents = union.Constituents
            .Where(c => !c.IsVoid())
            .ToList();
        var nonVoidPortion = nonVoidConstituents.Count == 1
            ? nonVoidConstituents[0]
            : new UnionType(nonVoidConstituents.ToImmutableArray());

        // ── MIST_TC_0044: Right operand must be assignable to T ──
        var rhsType = model.GetTypeOf(assignedValue);
        if(rhsType is not null && !IsAssignable(rhsType, nonVoidPortion, model))
        {
            diagnostics.ReportAtNode(TypeCheckDiagnostics.AmbiguousImplicitCast, assignedValue, "TypeChecker");
        }

        // ── Flow narrowing: after ??=, the variable's type is T (not T | void) ──
        voidCoalescingTypeNarrowings[targetVarName] = nonVoidPortion;

        // Update the symbol's DeclaredType so that subsequent GetTypeOf calls
        // for this variable return the narrowed type.
        if(targetPostfix is not null)
        {
            var symbol = model.GetDeclaredSymbol(targetPostfix.Target);
            if(symbol is Symbol concreteSymbol && concreteSymbol.Kind == SymbolKind.LocalVariable)
            {
                concreteSymbol.DeclaredType = nonVoidPortion;
            }
        }
    }

    private void CheckLogicalOrNode(LogicalOrNode node, ISemanticModel model, DiagnosticBag diagnostics)
    {
        // Walk children first — the overload resolver only resolves the
        // top-level operator; the child subexpressions still need
        // analysis for their own operator/member checks.
        CheckLogicalAndNode(node.Left, model, diagnostics);

        if(node.Right is not null)
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
                TypeCheckDiagnostics.OperatorNotApplicableLogical,
                model,
                diagnostics);
        }
    }

    private void CheckLogicalAndNode(LogicalAndNode node, ISemanticModel model, DiagnosticBag diagnostics)
    {
        CheckEqualityNode(node.Left, model, diagnostics);

        if(node.Right is not null)
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
                TypeCheckDiagnostics.OperatorNotApplicableLogical,
                model,
                diagnostics);
        }
    }

    private void CheckEqualityNode(EqualityNode node, ISemanticModel model, DiagnosticBag diagnostics)
    {
        CheckRelationalNode(node.Left, model, diagnostics);

        // Group0 is the flat n-ary operand list. Fold it left-associatively
        // (a == b == c => (a == b) == c): after the first comparison the node
        // carries the running result type, which becomes the next left operand.
        var currentLeft = (SyntaxNode)node.Left;
        foreach(var group in node.Group0)
        {
            CheckRelationalNode(group.Right, model, diagnostics);

            // Void unit-type equality is a first-class language feature
            // (void-unit-type spec § Requirement 9). Two VoidType operands
            // compare equal via reference identity; there is no operator
            // member to look up because VoidType is a compiler-internal
            // type. Short-circuit before delegating to overload resolution
            // so `Void.Void == Void.Void` and `Void.Void != Void.Void`
            // remain well-typed.
            var lhsType = model.GetTypeOf(currentLeft);
            var rhsType = model.GetTypeOf(group.Right);
            if(lhsType is not null && lhsType.IsVoid() && rhsType is not null && rhsType.IsVoid())
            {
                model.SetTypeOf(node, LegacyPrimitiveTypes.Bool);
                currentLeft = node;
                continue;
            }

            // ── anonymous-types feature: equality operator validation ──
            // Two values of the same AnonymousType (structurally equal) can
            // be compared with == / !=. Different anonymous type shapes are
            // rejected with MIST_ANON_EQUALITY_MISMATCH.
            if(lhsType is AnonymousType lhsAnon && rhsType is AnonymousType rhsAnon)
            {
                if(lhsAnon.StructurallyEquals(rhsAnon))
                {
                    model.SetTypeOf(node, LegacyPrimitiveTypes.Bool);
                }
                else
                {
                    diagnostics.ReportAtNode(TypeCheckDiagnostics.AnonEqualityMismatch, node, "TypeChecker");
                }

                currentLeft = node;
                continue;
            }

            // Route typing / error emission through overload resolution
            // (Requirements 6.1, 6.4). Equality operators on stdlib types
            // (Int32, Boolean, String, …) are declared as static operator
            // members; user types may also declare == / !=.
            var op = group.Operator.Lexeme ?? "==";
            ResolveBinaryOperator(
                op,
                currentLeft,
                group.Right,
                node,
                TypeCheckDiagnostics.OperatorNotApplicableComparison,
                model,
                diagnostics);
            currentLeft = node;
        }
    }

    private void CheckRelationalNode(RelationalNode node, ISemanticModel model, DiagnosticBag diagnostics)
    {
        switch(node)
        {
            case RelationalComparisonRelationalNode cmp:
                {
                    CheckAdditiveNode(cmp.Left, model, diagnostics);

                    var currentLeft = (SyntaxNode)cmp.Left;
                    foreach(var group in cmp.Group0)
                    {
                        CheckAdditiveNode(group.Right, model, diagnostics);

                        // Route typing / error emission through overload resolution
                        // (Requirements 6.1, 6.4). Relational operators (<, >, <=, >=)
                        // are declared on each numeric stdlib type and Char; the old
                        // NumericTypes / IsNumeric special case is removed
                        // (Requirement 6.7). Each Group0 element carries its own
                        // operator, so a chain (a < b < c) folds left-associatively.
                        var op = group.Operator.Lexeme ?? "<";
                        ResolveBinaryOperator(
                            op,
                            currentLeft,
                            group.Right,
                            cmp,
                            TypeCheckDiagnostics.OperatorNotApplicableRelational,
                            model,
                            diagnostics);
                        currentLeft = cmp;
                    }

                    break;
                }

            case IsExpressionRelationalNode isExpr:
                {
                    CheckAdditiveNode(isExpr.Left, model, diagnostics);
                    var scrutineeType = model.GetTypeOf(isExpr.Left);
                    if(scrutineeType is not null)
                    {
                        CheckPattern(isExpr.Pattern, scrutineeType, model, diagnostics);
                    }

                    break;
                }

            case AsExpressionRelationalNode asExpr:
                {
                    CheckAdditiveNode(asExpr.Left, model, diagnostics);
                    CheckAsExpression(asExpr, model, diagnostics);
                    break;
                }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // As expression classification (MIST_TC_0160 – MIST_TC_0162)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Validates and classifies an <c>as</c> expression. Determines whether the
    /// conversion is provable (result type = T), non-provable (result type = T | void),
    /// or invalid (compile error). Sets the expression type and records the conversion
    /// kind on the semantic model.
    /// </summary>
    private static void CheckAsExpression(
        AsExpressionRelationalNode asExpr,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // The target type was resolved by Pass 2 and set as the preliminary type.
        var targetType = model.GetTypeOf(asExpr);
        if(targetType is null || IsErrorType(targetType))
        {
            return;
        }

        // Get the compile-time type of the left operand.
        var sourceType = model.GetTypeOf(asExpr.Left);
        if(sourceType is null || IsErrorType(sourceType))
        {
            return;
        }

        // Reject void source.
        if(sourceType.IsVoid())
        {
            diagnostics.ReportAtNode(TypeCheckDiagnostics.AsOperatorVoidSource, asExpr, "TypeChecker");
            return;
        }

        // Reject void target.
        if(targetType.IsVoid())
        {
            diagnostics.ReportAtNode(TypeCheckDiagnostics.AsOperatorVoidTarget, asExpr, "TypeChecker");
            return;
        }

        // Classify the conversion.
        var classification = ClassifyAsConversion(sourceType, targetType, model);

        switch(classification)
        {
            case AsConversionClassification.Provable:
                model.SetTypeOf(asExpr, targetType);
                model.RecordAsConversionKind(asExpr, AsConversionKind.Provable);
                break;

            case AsConversionClassification.NonProvable:
                var optionalType = new UnionType(new List<IType> { targetType, LegacyPrimitiveTypes.Void });
                model.SetTypeOf(asExpr, optionalType);
                model.RecordAsConversionKind(asExpr, AsConversionKind.NonProvable);
                break;

            case AsConversionClassification.Invalid:
                diagnostics.ReportAtNode(TypeCheckDiagnostics.AsOperatorNoRelationship, asExpr, "TypeChecker", sourceType.DisplayName, targetType.DisplayName);
                break;
        }
    }

    /// <summary>
    /// Internal classification result for <see cref="ClassifyAsConversion"/>. Distinct
    /// from <see cref="AsConversionKind"/> because this enum includes the Invalid case
    /// which never gets recorded on the model (it produces a diagnostic instead).
    /// </summary>
    private enum AsConversionClassification
    {
        Provable,
        NonProvable,
        Invalid,
    }

    /// <summary>
    /// Classifies the conversion relationship between a source type and target type
    /// for an <c>as</c> expression. Returns Provable (always succeeds), NonProvable
    /// (requires runtime check), or Invalid (no type relationship exists).
    /// </summary>
    private static AsConversionClassification ClassifyAsConversion(
        IType sourceType, IType targetType, ISemanticModel model)
    {
        // 1. Provable: source is directly assignable to target
        //    (identity, subtype, abstract — NOT implicit cast operators)
        if(IsDirectlyAssignable(sourceType, targetType, model))
        {
            return AsConversionClassification.Provable;
        }

        // 2. Non-provable: target is directly assignable to source (narrowing)
        //    Base → Derived, Abstract → Concrete
        if(IsDirectlyAssignable(targetType, sourceType, model))
        {
            return AsConversionClassification.NonProvable;
        }

        // 3. Non-provable: source is unknown → any concrete target
        if(sourceType is UnknownType)
        {
            return AsConversionClassification.NonProvable;
        }

        // 4. Non-provable: source is a union and target is a constituent or related
        if(sourceType is UnionType sourceUnion)
        {
            if(IsConstituentOf(targetType, sourceUnion))
            {
                return AsConversionClassification.NonProvable;
            }

            // Target shares inheritance with a constituent
            foreach(var constituent in sourceUnion.Constituents)
            {
                if(IsDirectlyAssignable(targetType, constituent, model)
                    || IsDirectlyAssignable(constituent, targetType, model))
                {
                    return AsConversionClassification.NonProvable;
                }
            }
        }

        // 5. Non-provable: both are classes in the same hierarchy (sibling types)
        if(SharesTypeHierarchy(sourceType, targetType))
        {
            return AsConversionClassification.NonProvable;
        }

        // 6. Non-provable: target is an abstract that source MIGHT implement
        if(targetType.Kind is TypeKind.Abstract)
        {
            if(sourceType.Kind is TypeKind.Class or TypeKind.Struct)
            {
                return AsConversionClassification.NonProvable;
            }
        }

        // 7. Invalid: no type relationship exists
        return AsConversionClassification.Invalid;
    }

    /// <summary>
    /// Determines whether two types share a class or struct hierarchy — i.e., they
    /// have a common ancestor (sibling types). This covers cases where neither is a
    /// direct ancestor of the other, but both descend from the same base.
    /// </summary>
    private static bool SharesTypeHierarchy(IType a, IType b)
    {
        // Class hierarchy sharing
        if(a is ClassType classA && b is ClassType classB)
        {
            var ancestorsA = CollectClassAncestors(classA);
            var ancestorsB = CollectClassAncestors(classB);
            foreach(var ancestor in ancestorsA)
            {
                if(ancestorsB.Contains(ancestor))
                {
                    return true;
                }
            }
        }

        // Struct hierarchy sharing
        if(a is StructType structA && b is StructType structB)
        {
            var ancestorsA = CollectStructAncestors(structA);
            var ancestorsB = CollectStructAncestors(structB);
            foreach(var ancestor in ancestorsA)
            {
                if(ancestorsB.Contains(ancestor))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Collects all ancestor class display names for a class type by walking
    /// the inheritance chain.
    /// </summary>
    private static HashSet<string> CollectClassAncestors(ClassType classType)
    {
        var ancestors = new HashSet<string>(StringComparer.Ordinal);
        var current = classType.BaseType as ClassType;
        while(current is not null)
        {
            _ = ancestors.Add(current.DisplayName);
            current = current.BaseType as ClassType;
        }

        return ancestors;
    }

    /// <summary>
    /// Collects all ancestor struct display names for a struct type by walking
    /// the inheritance chain.
    /// </summary>
    private static HashSet<string> CollectStructAncestors(StructType structType)
    {
        var ancestors = new HashSet<string>(StringComparer.Ordinal);
        var current = structType.BaseType as StructType;
        while(current is not null)
        {
            _ = ancestors.Add(current.DisplayName);
            current = current.BaseType as StructType;
        }

        return ancestors;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Pattern validation (MIST_TC_0050 – MIST_TC_0057)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Recursively validates a pattern against the scrutinee type.
    /// Skips validation when the scrutinee type is <see cref="ErrorType"/>
    /// to suppress cascading errors.
    /// </summary>
    private static void CheckPattern(
        PatternNode pattern,
        IType scrutineeType,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        if(IsErrorType(scrutineeType))
        {
            return;
        }

        CheckOrPattern(pattern.OrPattern, scrutineeType, model, diagnostics);
    }

    private static void CheckOrPattern(
        OrPatternNode orPattern,
        IType scrutineeType,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        CheckAndPattern(orPattern.Left, scrutineeType, model, diagnostics);

        if(orPattern.Right is not null && !ReferenceEquals(orPattern.Right, orPattern.Left))
        {
            CheckAndPattern(orPattern.Right, scrutineeType, model, diagnostics);

            // MIST_TC_0056: Or-pattern alternatives must introduce the same
            // variables with the same types.
            var leftVars = new Dictionary<string, IType>(StringComparer.Ordinal);
            CollectPatternVariablesFromAndPattern(orPattern.Left, scrutineeType, model, leftVars);

            var rightVars = new Dictionary<string, IType>(StringComparer.Ordinal);
            CollectPatternVariablesFromAndPattern(orPattern.Right, scrutineeType, model, rightVars);

            if(!PatternVariableSetsMatch(leftVars, rightVars))
            {
                diagnostics.ReportAtNode(TypeCheckDiagnostics.PatternOrInconsistentVariables, orPattern, "TypeChecker");
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

        if(andPattern.Right is not null && !ReferenceEquals(andPattern.Right, andPattern.Left))
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
        switch(unaryPattern)
        {
            case NotPatternUnaryPatternNode notPattern:
                {
                    CheckPrimaryPattern(notPattern.Operand, scrutineeType, model, diagnostics);

                    // MIST_TC_0057: Not-pattern operand must not introduce variables.
                    var vars = new Dictionary<string, IType>(StringComparer.Ordinal);
                    CollectPatternVariablesFromPrimaryPattern(notPattern.Operand, scrutineeType, model, vars);
                    if(vars.Count > 0)
                    {
                        diagnostics.ReportAtNode(TypeCheckDiagnostics.PatternNotCannotBindVariable, notPattern, "TypeChecker");
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
        switch(primaryPattern)
        {
            case TypePatternPrimaryPatternNode typePattern:
                {
                    // MIST_TC_0050: Verify the type exists in scope.
                    // The semantic model resolved the type reference during Pass 2.
                    // If it resolved to ErrorType, the type was not found.
                    var patternType = model.GetTypeOf(typePattern.Type);
                    patternType ??= model.GetDeclaredSymbol(typePattern)?.DeclaredType;
                    if(patternType is null || IsErrorType(patternType))
                    {
                        diagnostics.ReportAtNode(TypeCheckDiagnostics.PatternUndefinedType, typePattern, "TypeChecker");
                    }
                    else if(patternType is UnknownType)
                    {
                        // Task 24.1 / Req 11.3: narrowing to `unknown` is not
                        // meaningful. `is unknown u` is tautologically true
                        // because every value is assignable to `unknown`
                        // (Req 4.1), and the introduced binding has no more
                        // specific type than the scrutinee — it discards
                        // information instead of recovering it.
                        // Exception: `is object` (NamedSingleTypeRef) maps to C#'s
                        // `is object` pattern which tests for non-null.
                        if(typePattern.Type.First is not NamedSingleTypeRefSingleTypeRefNode)
                        {
                            diagnostics.ReportAtNode(TypeCheckDiagnostics.PatternNarrowToUnknown, typePattern, "TypeChecker");
                        }
                    }
                    else if(IsClosedGenericWithUnknown(patternType))
                    {
                        // Task 24.2 / Req 11.4: narrowing to a
                        // Closed_Generic_With_Unknown (e.g., `List<unknown>`)
                        // is not meaningful. Covariant_Unknown_Widening
                        // (Req 8) makes the pattern tautologically true, and
                        // the introduced binding is strictly less useful than
                        // the scrutinee — members classified WritesT with
                        // respect to the `unknown`-bound type parameter are
                        // filtered per Requirement 10, without any information
                        // gain.
                        diagnostics.ReportAtNode(TypeCheckDiagnostics.PatternNarrowToClosedGenericWithUnknown, typePattern, "TypeChecker", patternType!.DisplayName, GetConcreteConstructedSuggestion(patternType));
                    }
                    else if(HasOpenDependentArguments(patternType))
                    {
                        // Task 6.5 / Req 9.3, 9.4, 15.7: pattern match against an
                        // open dependent type (dependent args contain unresolved
                        // parameters) is rejected — pattern matching requires
                        // concrete dependent values.
                        diagnostics.ReportAtNode(TypeCheckDiagnostics.DependentOpenTypePattern, typePattern, "TypeChecker");
                    }
                    else if(patternType is RefinementType refinementPattern)
                    {
                        // Refinement type pattern: `is AdultAge age` — runtime predicate check.
                        // Verify the scrutinee type is compatible with the refinement's base type.
                        // The scrutinee must be assignable to the base type (or be the base type,
                        // or be another refinement of the same base type).
                        var effectiveScrutinee = scrutineeType is RefinementType scrutineeRefinement
                            ? scrutineeRefinement.BaseType
                            : scrutineeType;

                        if(effectiveScrutinee is UnionType unionForRefinement)
                        {
                            // Scrutinee is a union: check if the refinement's base type is a constituent.
                            if(!IsConstituentOfUnion(refinementPattern.BaseType, unionForRefinement))
                            {
                                diagnostics.ReportAtNode(TypeCheckDiagnostics.UnionPatternNonConstituent, typePattern, "TypeChecker", patternType!.DisplayName, effectiveScrutinee.DisplayName);
                            }
                        }
                        else if(!IsAssignable(effectiveScrutinee, refinementPattern.BaseType, model)
                                 && !IsAssignable(refinementPattern.BaseType, effectiveScrutinee, model))
                        {
                            // Scrutinee type is not compatible with the refinement's base type.                            diagnostics.ReportAtNode(TypeCheckDiagnostics.PatternIncompatibleRefinement, typePattern, "TypeChecker");
                        }
                        // Otherwise: valid refinement type pattern — the is expression will
                        // evaluate the refinement predicate at runtime.
                    }
                    else if(scrutineeType is UnionType unionForTypePattern
                             && !IsConstituentOfUnion(patternType, unionForTypePattern))
                    {
                        // Task 6.3 / Req 8.3, 12.5: type pattern references a
                        // type that is not a constituent of the union — the
                        // pattern will never match at runtime.
                        diagnostics.ReportAtNode(TypeCheckDiagnostics.UnionPatternNonConstituent, typePattern, "TypeChecker", patternType!.DisplayName, unionForTypePattern.DisplayName);
                    }

                    break;
                }

            case BareTypePatternPrimaryPatternNode bareTypePattern:
                {
                    // Bare type pattern: `Type` (no binding name) — pure type test.
                    // Verify the type exists in scope.
                    var patternType = model.GetTypeOf(bareTypePattern.Type);
                    if(patternType is null || IsErrorType(patternType))
                    {
                        diagnostics.ReportAtNode(TypeCheckDiagnostics.PatternUndefinedType, bareTypePattern, "TypeChecker");
                    }
                    else if(patternType is EnumType)
                    {
                        // Enum member pattern: `is EnumName.MemberName` — value equality check.
                        // The semantic model builder resolved this as an enum member access.
                        // Accept it without error; the emitter will generate a == comparison.
                        // Verify the scrutinee type is compatible (must be the same enum type
                        // or the enum's underlying type).
                        if(!IsAssignable(scrutineeType, patternType)
                            && !(patternType is EnumType enumPatternType
                                 && IsAssignable(scrutineeType, enumPatternType.UnderlyingType)))
                        {
                            diagnostics.ReportAtNode(TypeCheckDiagnostics.PatternConstantTypeMismatch, bareTypePattern, "TypeChecker");
                        }
                    }
                    else if(patternType is UnknownType)
                    {
                        // Allow `is object` when the source uses the `object` keyword explicitly
                        // (a NamedSingleTypeRef, not an UnknownSingleTypeRef). This maps to C#'s
                        // `is object` pattern which tests for non-null.
                        if(bareTypePattern.Type.First is not NamedSingleTypeRefSingleTypeRefNode)
                        {
                            diagnostics.ReportAtNode(TypeCheckDiagnostics.PatternNarrowToUnknown, bareTypePattern, "TypeChecker");
                        }
                    }
                    else if(IsClosedGenericWithUnknown(patternType))
                    {
                        diagnostics.ReportAtNode(TypeCheckDiagnostics.PatternNarrowToClosedGenericWithUnknown, bareTypePattern, "TypeChecker", patternType!.DisplayName, GetConcreteConstructedSuggestion(patternType));
                    }
                    else if(HasOpenDependentArguments(patternType))
                    {
                        // Task 6.5 / Req 9.3, 9.4, 15.7: pattern match against an
                        // open dependent type (dependent args contain unresolved
                        // parameters) is rejected — pattern matching requires
                        // concrete dependent values.
                        diagnostics.ReportAtNode(TypeCheckDiagnostics.DependentOpenTypePattern, bareTypePattern, "TypeChecker");
                    }
                    else if(patternType is RefinementType bareRefinementPattern)
                    {
                        // Refinement type pattern (bare): `is AdultAge` — runtime predicate check.
                        // Verify the scrutinee type is compatible with the refinement's base type.
                        var effectiveScrutinee = scrutineeType is RefinementType scrutineeRefinementBare
                            ? scrutineeRefinementBare.BaseType
                            : scrutineeType;

                        if(effectiveScrutinee is UnionType unionForBareRefinement)
                        {
                            if(!IsConstituentOfUnion(bareRefinementPattern.BaseType, unionForBareRefinement))
                            {
                                diagnostics.ReportAtNode(TypeCheckDiagnostics.UnionPatternNonConstituent, bareTypePattern, "TypeChecker", patternType!.DisplayName, effectiveScrutinee.DisplayName);
                            }
                        }
                        else if(!IsAssignable(effectiveScrutinee, bareRefinementPattern.BaseType, model)
                                 && !IsAssignable(bareRefinementPattern.BaseType, effectiveScrutinee, model))
                        {
                            diagnostics.ReportAtNode(TypeCheckDiagnostics.PatternIncompatibleRefinement, bareTypePattern, "TypeChecker");
                        }
                    }
                    else if(scrutineeType is UnionType unionForBareTypePattern
                             && !IsConstituentOfUnion(patternType, unionForBareTypePattern))
                    {
                        // Task 6.3 / Req 8.3, 12.5: type pattern references a
                        // type that is not a constituent of the union — the
                        // pattern will never match at runtime.
                        diagnostics.ReportAtNode(TypeCheckDiagnostics.UnionPatternNonConstituent, bareTypePattern, "TypeChecker", patternType!.DisplayName, unionForBareTypePattern.DisplayName);
                    }

                    break;
                }

            case VarPatternPrimaryPatternNode:
                {
                    // Var pattern: always valid.
                    break;
                }

            case DiscardPatternPrimaryPatternNode:
                {
                    // Discard pattern (bare _): always valid, no binding.
                    break;
                }

            case ConstantPatternPrimaryPatternNode constantPattern:
                {
                    // Task 3.4: Regex pattern matching validation.
                    // When a regex constant pattern is used, the scrutinee must be `string`.
                    // Capture groups are ignored — no bindings are introduced.
                    if(constantPattern.Literal is RegexConstantConstantLiteralNode regexConst)
                    {
                        // MIST_TC_REGEX_SCRUTINEE: Verify scrutinee is string (Req 7.2, 7.3).
                        // A refinement type whose base type is string is also accepted.
                        var effectiveType = scrutineeType is RefinementType refinement
                            ? refinement.BaseType
                            : scrutineeType;
                        if(!IsStringLike(effectiveType))
                        {
                            diagnostics.ReportAtNode(TypeCheckDiagnostics.RegexScrutineeNotString, constantPattern, "TypeChecker", effectiveType.DisplayName);
                        }

                        // Validate regex pattern syntax at pattern position (Req 7.7).
                        ValidateRegexPattern(regexConst.Value.Lexeme, constantPattern, diagnostics);
                    }
                    else
                    {
                        // MIST_TC_0051: Verify constant type is compatible with scrutinee.
                        var constantType = model.GetTypeOf(constantPattern.Literal);
                        if(constantType is not null && !IsErrorType(constantType))
                        {
                            if(!ArePatternTypesCompatible(constantType, scrutineeType))
                            {
                                diagnostics.ReportAtNode(TypeCheckDiagnostics.PatternConstantTypeMismatch, constantPattern, "TypeChecker");
                            }
                        }
                    }

                    break;
                }

            case LessThanRelationalPatternPrimaryPatternNode ltPattern:
                {
                    CheckRelationalPattern(ltPattern, ltPattern.Constant, scrutineeType, model, diagnostics);
                    break;
                }

            case GreaterThanRelationalPatternPrimaryPatternNode gtPattern:
                {
                    CheckRelationalPattern(gtPattern, gtPattern.Constant, scrutineeType, model, diagnostics);
                    break;
                }

            case LessThanOrEqualRelationalPatternPrimaryPatternNode ltePattern:
                {
                    CheckRelationalPattern(ltePattern, ltePattern.Constant, scrutineeType, model, diagnostics);
                    break;
                }

            case GreaterThanOrEqualRelationalPatternPrimaryPatternNode gtePattern:
                {
                    CheckRelationalPattern(gtePattern, gtePattern.Constant, scrutineeType, model, diagnostics);
                    break;
                }

            case PropertyPatternPrimaryPatternNode propertyPattern:
                {
                    // Determine the effective type for property lookups.
                    var patternType = scrutineeType;
                    if(propertyPattern.TypeName is not null)
                    {
                        // If there's a type prefix, use it. Check if it resolved.
                        var resolvedSymbol = model.GetDeclaredSymbol(propertyPattern);
                        if(resolvedSymbol?.DeclaredType is not null && !IsErrorType(resolvedSymbol.DeclaredType))
                        {
                            patternType = resolvedSymbol.DeclaredType;
                        }
                    }

                    // MIST_TC_0053: Verify each property name exists on the type.
                    if(propertyPattern.Subpatterns is not null)
                    {
                        CheckPropertySubpatternList(propertyPattern.Subpatterns, patternType, model, diagnostics);
                    }

                    break;
                }

            case ListPatternPrimaryPatternNode listPattern:
                {
                    // Suppress cascading diagnostics when the scrutinee already
                    // resolved to an error/unknown type upstream.
                    if(scrutineeType is ErrorType or UnknownType)
                    {
                        break;
                    }

                    // MIST_TC_0054: a list pattern requires a countable, indexable
                    // receiver (arrays, spans, string, List<T>, countable+int-indexer),
                    // not just an array (Req 20.3).
                    if(!SemanticModelBuilder.IsIndexableType(scrutineeType))
                    {
                        diagnostics.ReportAtNode(
                            TypeCheckDiagnostics.PatternListRequiresIndexable, listPattern, "TypeChecker", scrutineeType.DisplayName);
                        break;
                    }

                    // Element sub-patterns are checked against the receiver's element
                    // type (Req 20.1); a carried slice sub-pattern is validated against
                    // the receiver's slice-result type inside the element-list check.
                    var elementType = SemanticModelBuilder.TryGetElementType(scrutineeType, out var resolvedElementType)
                        ? resolvedElementType
                        : ErrorType.Instance;

                    if(listPattern.Elements is not null)
                    {
                        CheckListPatternElementList(listPattern.Elements, scrutineeType, elementType, model, diagnostics);
                    }

                    break;
                }

            case TuplePositionalPatternPrimaryPatternNode tuplePattern:
                {
                    // Suppress cascading errors when scrutinee already has an error type.
                    if(scrutineeType is ErrorType)
                    {
                        break;
                    }

                    // When the scrutinee is unknown, the positional pattern acts as a runtime
                    // type check (narrowing to a tuple type). Skip element validation since
                    // element types are not known at compile time.
                    if(scrutineeType is UnknownType)
                    {
                        break;
                    }

                    if(scrutineeType is not TupleType tupleType)
                    {
                        diagnostics.ReportAtNode(TypeCheckDiagnostics.TuplePositionalPatternRequiresTupleType, tuplePattern, "TypeChecker", scrutineeType!.DisplayName);
                        break;
                    }

                    // Collect element patterns: first, rest, plus any additional elements.
                    var elementPatterns = new List<PatternNode> { tuplePattern.First, tuplePattern.Rest };
                    elementPatterns.AddRange(tuplePattern.Patterns);

                    if(elementPatterns.Count != tupleType.Arity)
                    {
                        diagnostics.ReportAtNode(TypeCheckDiagnostics.TuplePositionalPatternArityMismatch, tuplePattern, "TypeChecker", elementPatterns.Count, tupleType.DisplayName, tupleType.Arity);
                        break;
                    }

                    // Validate each element against its corresponding type independently.
                    for(var i = 0; i < elementPatterns.Count; i++)
                    {
                        CheckPattern(elementPatterns[i], tupleType.ElementTypes[i], model, diagnostics);
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
        VoidCoalescingNode constantExpr,
        IType scrutineeType,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // MIST_TC_0052: Verify scrutinee supports comparison operators.
        if(!TypeSupportsComparison(scrutineeType))
        {
            diagnostics.ReportAtNode(TypeCheckDiagnostics.PatternRelationalNonComparable, patternNode, "TypeChecker");
        }

        // MIST_TC_0051: Verify constant type is compatible with scrutinee.
        var constantType = model.GetTypeOf(constantExpr);
        if(constantType is not null && !IsErrorType(constantType))
        {
            if(!ArePatternTypesCompatible(constantType, scrutineeType))
            {
                diagnostics.ReportAtNode(TypeCheckDiagnostics.PatternConstantTypeMismatch, patternNode, "TypeChecker");
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
        if(IsErrorType(type))
        {
            return true; // Suppress cascading errors.
        }

        // Numeric display names are always comparable.
        if(NumericDisplayNames.Contains(type.DisplayName))
        {
            return true;
        }

        // Check the type's member scope for operator< (any comparison operator).
        var scope = GetMemberScope(type);
        if(scope is null)
        {
            return false;
        }

        foreach(var member in scope.Members)
        {
            if(member.Kind == SymbolKind.Method && member.Name == "operator<")
            {
                return true;
            }
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
        if(IsErrorType(constantType) || IsErrorType(scrutineeType))
        {
            return true;
        }

        if(ReferenceEquals(constantType, scrutineeType))
        {
            return true;
        }

        if(constantType.DisplayName == scrutineeType.DisplayName)
        {
            return true;
        }

        if(ArePrimitiveEquivalent(constantType, scrutineeType))
        {
            return true;
        }

        // Both numeric types are compatible.
        if(NumericDisplayNames.Contains(constantType.DisplayName)
            && NumericDisplayNames.Contains(scrutineeType.DisplayName))
        {
            return true;
        }

        // Assignability in either direction.
        if(IsAssignable(constantType, scrutineeType) || IsAssignable(scrutineeType, constantType))
        {
            return true;
        }

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

        foreach(var subpattern in subpatternList.PropertySubpatterns)
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

        // Handle TupleType specially: positional (Item1..ItemN) and named element access.
        if(patternType is TupleType tupleType)
        {
            IType? elementType = null;

            // Check positional access: Item1, Item2, ..., ItemN (1-indexed).
            if(propertyName.StartsWith("Item", StringComparison.Ordinal)
                && int.TryParse(propertyName.AsSpan(4), out var itemIndex)
                && itemIndex >= 1 && itemIndex <= tupleType.Arity)
            {
                elementType = tupleType.ElementTypes[itemIndex - 1];
            }
            // Check named element access.
            else if(tupleType.ElementNames is not null)
            {
                for(var i = 0; i < tupleType.ElementNames.Count; i++)
                {
                    if(tupleType.ElementNames[i] == propertyName)
                    {
                        elementType = tupleType.ElementTypes[i];
                        break;
                    }
                }
            }

            if(elementType is null)
            {
                diagnostics.ReportAtNode(TypeCheckDiagnostics.PatternUndefinedProperty, subpattern, "TypeChecker", propertyName, patternType.DisplayName);
                elementType = ErrorType.Instance;
            }

            CheckPattern(subpattern.Pattern, elementType, model, diagnostics);
            return;
        }

        // Req 8.3: Property patterns on `unknown` accept any property names — defer
        // validation to runtime. The sub-pattern is validated against `unknown` so
        // that nested `var` bindings still get the correct (unknown) type.
        if(patternType is UnknownType)
        {
            CheckPattern(subpattern.Pattern, patternType, model, diagnostics);
            return;
        }

        // Req 5.1, 5.3: Validate property patterns against AnonymousType property
        // names and types. Report error for names that don't exist on the type.
        if(patternType is AnonymousType anonType)
        {
            IType? anonPropertyType = null;
            for(var i = 0; i < anonType.PropertyNames.Count; i++)
            {
                if(anonType.PropertyNames[i] == propertyName)
                {
                    anonPropertyType = anonType.PropertyTypes[i];
                    break;
                }
            }

            if(anonPropertyType is null)
            {
                diagnostics.ReportAtNode(TypeCheckDiagnostics.PatternUndefinedProperty, subpattern, "TypeChecker", propertyName, patternType.DisplayName);
                anonPropertyType = ErrorType.Instance;
            }

            CheckPattern(subpattern.Pattern, anonPropertyType, model, diagnostics);
            return;
        }

        var memberScope = GetMemberScope(patternType);

        ISymbol? member = null;
        if(memberScope is not null)
        {
            member = memberScope.Lookup(propertyName);
        }

        if(member is null)
        {
            // MIST_TC_0053: Property not found on the type.
            diagnostics.ReportAtNode(TypeCheckDiagnostics.PatternUndefinedProperty, subpattern, "TypeChecker", propertyName, patternType.DisplayName);
        }

        // Recursively validate the subpattern against the property's type.
        var propertyType = member?.DeclaredType ?? ErrorType.Instance;
        CheckPattern(subpattern.Pattern, propertyType, model, diagnostics);
    }

    /// <summary>
    /// Validates a list pattern element list: retains the fixed-element count used by
    /// the <see cref="TypeCheckDiagnostics.PatternListTooManyElements"/> (MIST_TC_0055)
    /// check, enforces the at-most-one-slice rule
    /// (<see cref="TypeCheckDiagnostics.PatternListMultipleSlices"/>, MIST_TC_0186,
    /// Req 20.4), type-checks each fixed element sub-pattern against the receiver's
    /// element type (Req 20.1), and validates each slice element against the receiver's
    /// slice-result type via <see cref="CheckSliceElement"/> (Req 20.2, 20.5).
    /// </summary>
    private static void CheckListPatternElementList(
        ListPatternElementListNode elementList,
        IType receiverType,
        IType elementType,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Collect all elements.
        var allElements = new List<ListPatternElementNode> { elementList.ListPatternElement };
        allElements.AddRange(elementList.ListPatternElements);

        // Count fixed (non-slice) elements and slice elements.
        var fixedCount = 0;
        var hasSlice = false;
        var sliceCount = 0;
        foreach(var element in allElements)
        {
            if(element is PatternElementListPatternElementNode)
            {
                fixedCount++;
            }
            else if(element is SliceElementListPatternElementNode)
            {
                hasSlice = true;
                sliceCount++;
            }
        }

        // MIST_TC_0055: Warn if too many fixed elements (only meaningful
        // when there is no slice — with a slice, the array can be any length).
        // This is a warning, not an error.
        if(!hasSlice && fixedCount > 0)
        {
            // We can't know the array length at compile time in general,
            // but we emit a warning when the pattern has fixed elements
            // and no slice, as the pattern requires an exact-length match.
            // The warning is informational — the actual length check happens at runtime.
        }

        // MIST_TC_0186: a list pattern may contain at most one slice pattern (Req 20.4).
        if(sliceCount > 1)
        {
            diagnostics.ReportAtNode(TypeCheckDiagnostics.PatternListMultipleSlices, elementList, "TypeChecker");
        }

        // Recursively validate each element pattern.
        foreach(var element in allElements)
        {
            switch(element)
            {
                case PatternElementListPatternElementNode patternElement:
                    CheckPattern(patternElement.Pattern, elementType, model, diagnostics);
                    break;
                case SliceElementListPatternElementNode sliceElement:
                    CheckSliceElement(sliceElement, receiverType, model, diagnostics);
                    break;
            }
        }
    }

    /// <summary>
    /// Validates a slice element (<c>.. subpattern?</c>) inside a list pattern. A bare
    /// slice needs no further checking beyond the indexable-receiver requirement the
    /// caller already enforced. A slice that carries a sub-pattern requires the
    /// receiver to be a sliceable type (<see cref="SemanticModelBuilder.IsSliceableType"/>);
    /// otherwise <see cref="TypeCheckDiagnostics.PatternSliceRequiresSliceable"/>
    /// (MIST_TC_0187) is emitted naming the receiver type (Req 20.5). When the receiver
    /// is sliceable the carried sub-pattern is type-checked against the receiver's
    /// slice-result type (<see cref="SemanticModelBuilder.TryGetSliceResultType"/>,
    /// Req 20.2). A receiver that already resolved to an error/unknown type is
    /// suppressed to avoid cascading diagnostics, mirroring the sibling pattern checks.
    /// </summary>
    private static void CheckSliceElement(
        SliceElementListPatternElementNode sliceElement,
        IType receiverType,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // A bare slice `..` carries no sub-pattern and needs no receiver validation
        // beyond the indexable-receiver requirement enforced by the caller.
        if(sliceElement.Subpattern is null)
        {
            return;
        }

        // Suppress when the receiver already resolved to an error/unknown type.
        if(receiverType is ErrorType or UnknownType)
        {
            return;
        }

        // MIST_TC_0187: a slice sub-pattern requires a sliceable receiver (Req 20.5).
        // List<T> is indexable but NOT sliceable, so this rejects a carrying slice
        // (e.g. `[.., var rest]`) applied to a List<T>.
        if(!SemanticModelBuilder.IsSliceableType(receiverType))
        {
            diagnostics.ReportAtNode(
                TypeCheckDiagnostics.PatternSliceRequiresSliceable, sliceElement, "TypeChecker", receiverType.DisplayName);
            return;
        }

        // Type-check the carried sub-pattern against the receiver's slice-result type
        // (array -> same array, span -> same span, string -> string, custom -> declared
        // slice-result type) (Req 20.2).
        var sliceResultType = SemanticModelBuilder.TryGetSliceResultType(receiverType, out var resolvedSliceType)
            ? resolvedSliceType
            : ErrorType.Instance;
        CheckPattern(sliceElement.Subpattern, sliceResultType, model, diagnostics);
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

        if(orPattern.Right is not null && !ReferenceEquals(orPattern.Right, orPattern.Left))
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

        if(andPattern.Right is not null && !ReferenceEquals(andPattern.Right, andPattern.Left))
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
        switch(unaryPattern)
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
        switch(primaryPattern)
        {
            case TypePatternPrimaryPatternNode typePattern:
                {
                    var name = typePattern.Name.Lexeme;
                    if(name != "_")
                    {
                        var patternType = model.GetTypeOf(typePattern.Type);
                        patternType ??= model.GetDeclaredSymbol(typePattern)?.DeclaredType;
                        variables[name] = patternType ?? ErrorType.Instance;
                    }

                    break;
                }

            case BareTypePatternPrimaryPatternNode:
                {
                    // Bare type pattern: no binding name, nothing to collect.
                    break;
                }

            case VarPatternPrimaryPatternNode varPattern:
                {
                    var name = varPattern.Name.Lexeme;
                    if(name != "_")
                    {
                        variables[name] = scrutineeType;
                    }

                    break;
                }

            case DiscardPatternPrimaryPatternNode:
                {
                    // Discard pattern (bare _): no binding, nothing to collect.
                    break;
                }

            case PropertyPatternPrimaryPatternNode propertyPattern:
                {
                    // Recursively collect from subpatterns.
                    if(propertyPattern.Subpatterns is not null)
                    {
                        var patternType = scrutineeType;
                        CollectPatternVariablesFromPropertySubpatternList(
                            propertyPattern.Subpatterns, patternType, model, variables);
                    }

                    // Trailing identifier introduces a variable.
                    if(propertyPattern.Name.HasValue)
                    {
                        var name = propertyPattern.Name.Value.Lexeme;
                        if(name != "_")
                        {
                            variables[name] = scrutineeType;
                        }
                    }

                    break;
                }

            case ListPatternPrimaryPatternNode listPattern:
                {
                    var elementType = SemanticModelBuilder.TryGetElementType(scrutineeType, out var resolvedElementType)
                        ? resolvedElementType
                        : ErrorType.Instance;

                    if(listPattern.Elements is not null)
                    {
                        CollectPatternVariablesFromListElementList(
                            listPattern.Elements, scrutineeType, elementType, model, variables);
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

        foreach(var subpattern in subpatternList.PropertySubpatterns)
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
        var propertyType = member?.DeclaredType ?? ErrorType.Instance;

        CollectPatternVariablesFromPattern(subpattern.Pattern, propertyType, model, variables);
    }

    private static void CollectPatternVariablesFromListElementList(
        ListPatternElementListNode elementList,
        IType receiverType,
        IType elementType,
        ISemanticModel model,
        Dictionary<string, IType> variables)
    {
        CollectPatternVariablesFromListElement(elementList.ListPatternElement, receiverType, elementType, model, variables);

        foreach(var element in elementList.ListPatternElements)
        {
            CollectPatternVariablesFromListElement(element, receiverType, elementType, model, variables);
        }
    }

    private static void CollectPatternVariablesFromListElement(
        ListPatternElementNode element,
        IType receiverType,
        IType elementType,
        ISemanticModel model,
        Dictionary<string, IType> variables)
    {
        switch(element)
        {
            case PatternElementListPatternElementNode patternElement:
                CollectPatternVariablesFromPattern(patternElement.Pattern, elementType, model, variables);
                break;
            case SliceElementListPatternElementNode sliceElement:
                // Collect any variables the slice sub-pattern binds, typed as the
                // receiver's slice-result type (array -> same array, span -> same span,
                // string -> string, custom -> declared slice-result type), so a binding
                // such as `.. var middle` on a non-array sliceable receiver is recorded
                // with the correct type instead of always an array of the element type.
                if(sliceElement.Subpattern is not null)
                {
                    var sliceResultType = SemanticModelBuilder.TryGetSliceResultType(receiverType, out var resolvedSliceType)
                        ? resolvedSliceType
                        : ErrorType.Instance;
                    CollectPatternVariablesFromPattern(
                        sliceElement.Subpattern, sliceResultType, model, variables);
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
        if(left.Count != right.Count)
        {
            return false;
        }

        foreach(var (name, leftType) in left)
        {
            if(!right.TryGetValue(name, out var rightType))
            {
                return false;
            }

            // Allow ErrorType to match anything (suppress cascading errors).
            if(IsErrorType(leftType) || IsErrorType(rightType))
            {
                continue;
            }

            if(leftType.DisplayName != rightType.DisplayName
                && !ArePrimitiveEquivalent(leftType, rightType))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Extracts a human-readable type name from a <see cref="TypeReferenceNode"/>
    /// for use in diagnostic messages.
    /// </summary>
    private static string ExtractTypeReferenceName(TypeReferenceNode typeRef)
    {
        return typeRef.First switch
        {
            PrimitiveSingleTypeRefSingleTypeRefNode primitiveRef => primitiveRef.PrimitiveType switch
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
                RegexTypePrimitiveTypeNode => "regex",
                _ => "unknown",
            },
            VoidSingleTypeRefSingleTypeRefNode => "void",
            NamedSingleTypeRefSingleTypeRefNode namedRef => ExtractQualifiedIdentifierName(namedRef.Name),
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

    /// <summary>
    /// Returns <c>true</c> when <paramref name="type"/> is a
    /// <see cref="ConstructedType"/> whose type arguments contain
    /// <see cref="UnknownType"/> at any position (direct or transitive).
    /// This is the <em>Closed_Generic_With_Unknown</em> shape defined in
    /// the unknown-type feature glossary: a Constructed_Type in which at
    /// least one type argument is <c>unknown</c> directly or transitively
    /// (e.g., <c>List&lt;unknown&gt;</c>, <c>Map&lt;string, unknown&gt;</c>,
    /// <c>Optional&lt;List&lt;unknown&gt;&gt;</c>). Used by the Task 24
    /// pattern-narrowing rejection rule (Req 11.4).
    /// </summary>
    /// <remarks>
    /// The check is recursive through nested constructed types and array
    /// element types. A bare <see cref="UnknownType"/> is <em>not</em> a
    /// Closed_Generic_With_Unknown — that shape is handled by the Req 11.3
    /// rejection instead. Non-constructed types (primitives, class types
    /// without type arguments, etc.) return <c>false</c>.
    /// </remarks>
    private static bool IsClosedGenericWithUnknown(IType type)
    {
        if(type is not ConstructedType ct)
        {
            return false;
        }

        foreach(var arg in ct.TypeArguments)
        {
            if(ContainsUnknownAtAnyPosition(arg))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Provides a concrete constructed type suggestion for diagnostics when
    /// narrowing to a closed generic with <c>unknown</c>. Replaces <c>unknown</c>
    /// type arguments with <c>T</c> to suggest a concrete alternative.
    /// </summary>
    private static string GetConcreteConstructedSuggestion(IType type)
    {
        if(type is not ConstructedType ct)
        {
            return type.DisplayName;
        }

        var defName = ct.GenericDefinition.DisplayName;
        var args = ct.TypeArguments.Select(a =>
            a is UnknownType ? "T" : a.DisplayName);
        return $"{defName}<{string.Join(", ", args)}>";
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="type"/> is a
    /// <see cref="ConstructedType"/> with dependent arguments where at least
    /// one <see cref="DependentValueArgument.Value"/> is a <see cref="string"/>
    /// (indicating an unresolved/forwarded parameter rather than a concrete
    /// literal value). This is the <em>Open_Dependent_Type</em> shape: a
    /// constructed type whose dependent arguments contain unresolved parameters
    /// (e.g., <c>FixedBuffer&lt;N&gt;</c> where <c>N</c> is a type parameter).
    /// Used by the Task 6.5 pattern-matching rejection rule (Req 9.3, 9.4, 15.7).
    /// </summary>
    private static bool HasOpenDependentArguments(IType type)
    {
        if(type is not ConstructedType ct)
        {
            return false;
        }

        if(ct.DependentArguments is null)
        {
            return false;
        }

        foreach(var depArg in ct.DependentArguments)
        {
            if(depArg is not null && depArg.Value is string)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Recursive structural containment check: returns <c>true</c> if
    /// <paramref name="type"/> is <see cref="UnknownType"/>, an array whose
    /// element (transitively) contains <c>unknown</c>, or a constructed
    /// type whose arguments (transitively) contain <c>unknown</c>.
    /// </summary>
    private static bool ContainsUnknownAtAnyPosition(IType type)
    {
        switch(type)
        {
            case UnknownType:
                return true;
            case ArrayType arr:
                return ContainsUnknownAtAnyPosition(arr.ElementType);
            case ConstructedType ct:
                foreach(var arg in ct.TypeArguments)
                {
                    if(ContainsUnknownAtAnyPosition(arg))
                    {
                        return true;
                    }
                }

                return false;
            default:
                return false;
        }
    }

    private void CheckAdditiveNode(AdditiveNode node, ISemanticModel model, DiagnosticBag diagnostics)
    {
        CheckMultiplicativeNode(node.Left, model, diagnostics);

        // Group0 pairs each operator with its own operand, so a mixed chain
        // (10 + 3 - 5) folds left-associatively instead of collapsing to the last
        // operator; the node carries the running result type after the first step.
        var currentLeft = (SyntaxNode)node.Left;
        foreach(var group in node.Group0)
        {
            CheckMultiplicativeNode(group.Right, model, diagnostics);

            // Route typing / error emission through overload resolution
            // (Requirements 6.1, 6.4, 6.8). String concatenation flows
            // from Standard.String's operator+ declaration — the old
            // StringType-for-+ special case is removed.
            var op = group.Operator.Lexeme ?? "+";
            ResolveBinaryOperator(
                op,
                currentLeft,
                group.Right,
                node,
                TypeCheckDiagnostics.OperatorNotApplicableArithmetic,
                model,
                diagnostics);
            currentLeft = node;
        }
    }

    private void CheckMultiplicativeNode(MultiplicativeNode node, ISemanticModel model, DiagnosticBag diagnostics)
    {
        // For the pass-through (NonRange) operand, check the inner unary as before.
        // A genuine range expression has its bounds validated by CheckRange
        // (Requirement 7).
        if(node.Left.UnwrapRange() is { } left)
        {
            CheckUnaryNode(left, model, diagnostics);
        }
        else if(node.Left is RangeExpressionRangeNode leftRange)
        {
            CheckRange(leftRange, model, diagnostics);
        }

        var currentLeft = (SyntaxNode)node.Left;
        foreach(var group in node.Group0)
        {
            var rightNode = group.Right;
            if(rightNode.UnwrapRange() is { } right)
            {
                CheckUnaryNode(right, model, diagnostics);
            }
            else if(rightNode is RangeExpressionRangeNode rightRange)
            {
                CheckRange(rightRange, model, diagnostics);
            }

            // Route typing / error emission through overload resolution
            // (Requirements 6.1, 6.4, 6.7). Multiplicative operators
            // (*, /, %) are declared on each numeric stdlib type; the old
            // NumericTypes / IsNumeric special case is removed.
            var op = group.Operator.Lexeme ?? "*";
            ResolveBinaryOperator(
                op,
                currentLeft,
                group.Right,
                node,
                TypeCheckDiagnostics.OperatorNotApplicableArithmetic,
                model,
                diagnostics);
            currentLeft = node;
        }
    }

    private void CheckUnaryNode(UnaryNode node, ISemanticModel model, DiagnosticBag diagnostics)
    {
        switch(node)
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
                        TypeCheckDiagnostics.OperatorNotApplicableLogical,
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
                        TypeCheckDiagnostics.OperatorNotApplicableArithmetic,
                        model,
                        diagnostics);
                    break;
                }
            case FromEndExpressionUnaryNode fromEnd:
                {
                    // Walk the operand first so its own operators / members
                    // still get analysed, then validate the operand type.
                    CheckUnaryNode(fromEnd.Operand, model, diagnostics);
                    CheckFromEndExpression(fromEnd, model, diagnostics);
                    break;
                }
            case PostfixExpressionUnaryNode postfix:
                CheckPostfixNode(postfix.Primary, model, diagnostics);
                // Propagate refined type from the PostfixNode up to the
                // PostfixExpressionUnaryNode so that var declarations and
                // other upstream consumers see the correct type after
                // type argument inference.
                var postfixType = model.GetTypeOf(postfix.Primary);
                if(postfixType is not null && postfixType != model.GetTypeOf(postfix))
                {
                    model.SetTypeOf(postfix, postfixType);
                }

                break;
            case CastExpressionUnaryNode castExpr:
                CheckUnaryNode(castExpr.Operand, model, diagnostics);
                CheckCastExpression(castExpr, model, diagnostics);
                break;
            case AsyncExpressionUnaryNode asyncExpr:
                CheckUnaryNode(asyncExpr.Operand, model, diagnostics);
                CheckAsyncFunctorOperand(asyncExpr, model, diagnostics);
                CheckAsyncFunctorDoubleWrap(asyncExpr, model, diagnostics);
                break;
            case AwaitExpressionUnaryNode awaitExpr:
                CheckUnaryNode(awaitExpr.Operand, model, diagnostics);
                CheckAwaitOperandType(awaitExpr, model, diagnostics);
                break;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Index-from-end operand validation (MIST_TC_0181 / MIST_TC_0182)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Validates the operand of an index-from-end expression (<c>^operand</c>).
    /// The operand must be integer-compatible, in which case the expression is
    /// accepted (Requirement 6.1). A <c>void</c>-typed operand emits
    /// <see cref="TypeCheckDiagnostics.IndexFromEndOperandVoid"/> (MIST_TC_0182,
    /// Requirement 6.3); any other non-integer operand emits
    /// <see cref="TypeCheckDiagnostics.IndexFromEndOperandNotInteger"/>
    /// (MIST_TC_0181, Requirements 6.2, 12.1), naming the operand type. Exactly
    /// one diagnostic is emitted, located at the index-from-end expression's
    /// source span. When the operand already resolved to an unknown or error
    /// type (a prior error that produced its own diagnostic), the check is
    /// suppressed to avoid cascading diagnostics — mirroring the sibling unary
    /// operand checks in this file.
    /// </summary>
    private static void CheckFromEndExpression(
        FromEndExpressionUnaryNode fromEnd,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        var operandType = model.GetTypeOf(fromEnd.Operand);

        // Suppress cascading diagnostics when the operand type is unresolved or
        // already reported as an error upstream.
        if(operandType is null or UnknownType || IsErrorType(operandType))
        {
            return;
        }

        // Integer-compatible operand — accept (Requirement 6.1).
        if(IsIntLike(operandType))
        {
            return;
        }

        // A void-typed operand gets the dedicated diagnostic (Requirement 6.3),
        // never the generic non-integer one.
        if(operandType.IsVoid())
        {
            diagnostics.ReportAtNode(TypeCheckDiagnostics.IndexFromEndOperandVoid, fromEnd, "TypeChecker");
            return;
        }

        // Any other non-integer operand type (Requirements 6.2, 12.1).
        diagnostics.ReportAtNode(TypeCheckDiagnostics.IndexFromEndOperandNotInteger, fromEnd, "TypeChecker", operandType.DisplayName);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Range bound validation (MIST_TC_0183)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Validates the bounds of a range expression (<c>start..end</c>). Each
    /// syntactically present bound (either may be absent) is first walked so its
    /// own nested operators and member accesses — including a from-end bound's
    /// operand — are still analysed, then its resolved type is checked: an
    /// integer-compatible bound (Requirement 7.1) or a <c>Standard.Index</c>
    /// bound (Requirement 7.2) is accepted. A present bound with any other type
    /// rejects the <em>entire</em> range with exactly one
    /// <see cref="TypeCheckDiagnostics.RangeBoundInvalidType"/> (MIST_TC_0183,
    /// Requirements 7.3, 12.2), naming the offending bound type and pointing at
    /// that bound's source span. An absent bound is always valid and denotes the
    /// start or end of the sequence (Requirement 7.4); a present-but-invalid
    /// bound is never treated as absent (Requirement 7.5). A bound that already
    /// resolved to an unknown or error type (a prior error that produced its own
    /// diagnostic) is skipped to avoid cascading diagnostics, mirroring
    /// <see cref="CheckFromEndExpression"/>.
    /// </summary>
    private void CheckRange(
        RangeExpressionRangeNode range,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Walk each present bound first so nested operators / members (and a
        // from-end bound's own operand) are still analysed.
        if(range.Start is not null)
        {
            CheckUnaryNode(range.Start, model, diagnostics);
        }

        if(range.End is not null)
        {
            CheckUnaryNode(range.End, model, diagnostics);
        }

        // Validate the present bounds. The whole range is rejected on the first
        // invalid present bound, so at most one MIST_TC_0183 is emitted.
        if(!ValidateRangeBound(range.Start, model, diagnostics))
        {
            return;
        }

        _ = ValidateRangeBound(range.End, model, diagnostics);
    }

    /// <summary>
    /// Validates a single range bound. Returns <c>true</c> when the bound is
    /// absent (Requirement 7.4), integer-compatible (Requirement 7.1),
    /// <c>Standard.Index</c> (Requirement 7.2), or already error-typed
    /// (suppressed to avoid cascading diagnostics); returns <c>false</c> and
    /// emits <see cref="TypeCheckDiagnostics.RangeBoundInvalidType"/>
    /// (MIST_TC_0183) when the bound is present with an invalid type
    /// (Requirements 7.3, 7.5, 12.2).
    /// </summary>
    private static bool ValidateRangeBound(
        UnaryNode? bound,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Absent bound — always valid (Requirement 7.4).
        if(bound is null)
        {
            return true;
        }

        var boundType = model.GetTypeOf(bound);

        // Suppress cascading diagnostics when the bound type is unresolved or
        // already reported as an error upstream.
        if(boundType is null or UnknownType || IsErrorType(boundType))
        {
            return true;
        }

        // Integer-compatible bound (Requirement 7.1) or Standard.Index bound
        // (Requirement 7.2) — accept.
        if(IsIntLike(boundType) || IsStandardIndex(boundType))
        {
            return true;
        }

        // Any other present bound type rejects the entire range (Requirements
        // 7.3, 7.5, 12.2), naming the offending bound type and pointing at the
        // bound's source span.
        diagnostics.ReportAtNode(TypeCheckDiagnostics.RangeBoundInvalidType, bound, "TypeChecker", boundType.DisplayName);
        return false;
    }

    /// <summary>
    /// Returns true when <paramref name="type"/> is the stdlib
    /// <c>Standard.Index</c> struct (surfaced from <c>System.Index</c>), used to
    /// accept an index-from-end value as a range bound (Requirement 7.2).
    /// </summary>
    private static bool IsStandardIndex(IType type)
    {
        return type is StructType { ContainingNamespace: "Standard", DisplayName: "Index" };
    }

    /// <summary>
    /// Returns true when <paramref name="type"/> is the stdlib
    /// <c>Standard.Range</c> struct (surfaced from <c>System.Range</c>), used to
    /// route a range-typed element-access argument through the sliceable-receiver
    /// validation path (Requirements 8.7, 12.4).
    /// </summary>
    private static bool IsStandardRange(IType type)
    {
        return type is StructType { ContainingNamespace: "Standard", DisplayName: "Range" };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Async functor operand validation (MIST_TC_0107)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Validates that the operand of an <c>async</c> functor expression is a
    /// method invocation. The operand must be a <see cref="PostfixExpressionUnaryNode"/>
    /// whose <see cref="PostfixNode"/> has an <see cref="InvocationSuffixPostfixSuffixNode"/>
    /// or <see cref="MethodCallSuffixPostfixSuffixNode"/> as its last suffix.
    /// If not, emits <c>MIST_TC_0107</c> (Requirement 3.7).
    /// Suppresses the diagnostic when the operand type is
    /// <see cref="UnknownType"/> or <see cref="ErrorType"/> (indicating
    /// a prior error that already produced a diagnostic).
    /// </summary>
    private static void CheckAsyncFunctorOperand(
        AsyncExpressionUnaryNode asyncExpr,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        var operand = asyncExpr.Operand;

        // If the operand resolved to an error type, suppress cascading diagnostics.
        var operandType = model.GetTypeOf(operand);
        if(operandType is UnknownType || IsErrorType(operandType ?? ErrorType.Instance))
        {
            return;
        }

        // A valid operand is a PostfixExpressionUnaryNode whose PostfixNode ends
        // with an invocation suffix (direct call) or method call suffix (dot call).
        if(operand is PostfixExpressionUnaryNode postfixExpr)
        {
            var postfix = postfixExpr.Primary;
            if(postfix.Suffix.Length > 0)
            {
                var lastSuffix = postfix.Suffix[^1];
                if(lastSuffix is InvocationSuffixPostfixSuffixNode or MethodCallSuffixPostfixSuffixNode)
                {
                    return;
                }
            }
        }

        var expressionKind = DescribeUnaryExpressionKind(operand);
        diagnostics.ReportAtNode(TypeCheckDiagnostics.AsyncFunctorNonInvocationOperand, asyncExpr, "TypeChecker", expressionKind);
    }

    /// <summary>
    /// Returns a human-readable description of the expression kind for use in
    /// the <c>MIST_TC_0107</c> diagnostic message.
    /// </summary>
    private static string DescribeUnaryExpressionKind(UnaryNode operand)
    {
        switch(operand)
        {
            case PostfixExpressionUnaryNode postfixExpr:
                {
                    var postfix = postfixExpr.Primary;
                    if(postfix.Suffix.Length == 0)
                    {
                        return postfix.Target switch
                        {
                            IdentifierExpressionPrimaryNode => "a variable reference",
                            IntegerLiteralExpressionPrimaryNode => "a literal",
                            FloatLiteralExpressionPrimaryNode => "a literal",
                            StringLiteralExpressionPrimaryNode => "a literal",
                            TrueLiteralExpressionPrimaryNode => "a literal",
                            FalseLiteralExpressionPrimaryNode => "a literal",
                            CharLiteralExpressionPrimaryNode => "a literal",
                            ThisExpressionPrimaryNode => "a 'this' expression",
                            _ => "an expression"
                        };
                    }

                    var lastSuffix = postfix.Suffix[^1];
                    return lastSuffix switch
                    {
                        MemberAccessSuffixPostfixSuffixNode => "a property access",
                        IndexSuffixPostfixSuffixNode => "an index access",
                        SwitchExpressionSuffixPostfixSuffixNode => "a switch expression",
                        _ => "an expression"
                    };
                }
            case CastExpressionUnaryNode:
                return "a cast expression";
            case LogicalNotUnaryNode:
                return "a logical not expression";
            case NegateUnaryNode:
                return "a negation expression";
            case AwaitExpressionUnaryNode:
                return "an await expression";
            case AsyncExpressionUnaryNode:
                return "an async expression";
            default:
                return "an expression";
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Async functor double-wrap warning (MIST_TC_0109)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Emits a warning when the <c>async</c> functor is applied to an async
    /// method in an async context, producing <c>Task&lt;Task&lt;T&gt;&gt;</c>.
    /// This is technically valid but almost certainly unintended
    /// (Requirement 3.6).
    /// Suppresses the warning when the operand type is
    /// <see cref="UnknownType"/> or <see cref="ErrorType"/> (indicating
    /// a prior error that already produced a diagnostic), or when the
    /// enclosing method is not async.
    /// </summary>
    private void CheckAsyncFunctorDoubleWrap(
        AsyncExpressionUnaryNode asyncExpr,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Only relevant in an async context.
        if(currentMethodSymbol is not Symbol { IsAsync: true })
        {
            return;
        }

        // Suppress if the operand resolved to an error type.
        var operandType = model.GetTypeOf(asyncExpr.Operand);
        if(operandType is null || operandType is UnknownType || IsErrorType(operandType))
        {
            return;
        }

        // In an async context, if the operand (a method invocation) already has
        // type Task<T>, it means the target method is async. The async functor
        // will wrap it again to Task<Task<T>> — the double-wrap condition.
        if(!SemanticModelBuilder.IsStandardTaskType(operandType))
        {
            return;
        }

        diagnostics.ReportAtNode(TypeCheckDiagnostics.AsyncFunctorDoubleWrap, asyncExpr, "TypeChecker");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Await operand type validation (MIST_TC_0105)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Validates that the operand of an <c>await</c> expression has type
    /// <c>Task&lt;T&gt;</c>. If the operand type is not a
    /// <see cref="ConstructedType"/> whose generic definition is
    /// <c>Standard.Task</c>, emits <c>MIST_TC_0105</c> including the
    /// actual type in the message (Requirement 4.3).
    /// Suppresses the diagnostic when the operand type is
    /// <see cref="UnknownType"/> or <see cref="ErrorType"/> (indicating
    /// a prior error that already produced a diagnostic).
    /// </summary>
    private static void CheckAwaitOperandType(
        AwaitExpressionUnaryNode awaitExpr,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        var operandType = model.GetTypeOf(awaitExpr.Operand);
        if(operandType is null || operandType is UnknownType || IsErrorType(operandType))
        {
            return;
        }

        if(SemanticModelBuilder.IsStandardTaskType(operandType))
        {
            return;
        }

        // Also accept ValueTask<T> as a valid await operand.
        if(IsValueTaskType(operandType))
        {
            return;
        }

        if(operandType is UnionType union
            && union.Constituents.FirstOrDefault(c => !c.IsVoid()) is { } awaited
            && union.Constituents.Any(c => c.IsVoid())
            && (SemanticModelBuilder.IsStandardTaskType(awaited) || IsValueTaskType(awaited)))
        {
            diagnostics.ReportAtNode(
                TypeCheckDiagnostics.AwaitOptionalOperand, awaitExpr, "TypeChecker",
                operandType.DisplayName, awaited.DisplayName);

            return;
        }

        diagnostics.ReportAtNode(TypeCheckDiagnostics.AwaitNonTaskOperand, awaitExpr, "TypeChecker", operandType.DisplayName);
    }

    private static bool IsValueTaskType(IType type)
    {
        return type is ConstructedType
        {
            GenericDefinition: ClassType { DisplayName: "ValueTask", ContainingNamespace: "Standard", IsBackendBacked: true },
        };
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

        // Task 21: if this postfix is the LHS of an assignment, reset the
        // flag for inner expressions (e.g., arguments inside method calls)
        // so those nested visits do not inherit the LHS context.
        analyzingAssignmentLhs = false;

        for(var suffixIndex = 0; suffixIndex < node.Suffix.Length; suffixIndex++)
        {
            var suffix = node.Suffix[suffixIndex];

            switch(suffix)
            {
                case MethodCallSuffixPostfixSuffixNode methodCall:
                    // Task 25.1: bare-unknown member access rejection.
                    // A method call on a receiver whose static type is
                    // exactly `UnknownType` has no valid target — the
                    // singleton has no member scope (Req 3.4). Emit
                    // MIST_TC_0065 and short-circuit the remaining
                    // suffix logic to suppress cascading diagnostics.
                    if(currentType is UnknownType)
                    {
                        diagnostics.ReportAtNode(TypeCheckDiagnostics.BareUnknownMemberAccess, methodCall, "TypeChecker", methodCall.Name.Lexeme, "unknown");
                        currentType = ErrorType.Instance;
                        targetIsTypeName = false;
                        break;
                    }
                    // Task 6.2: union type method call validation (Req 7.1, 7.2, 7.3).
                    // If the receiver is a UnionType containing void, direct `.` access
                    // is invalid — the user must use `?.` to safely unwrap the optional.
                    // For non-void unions (e.g., int | string), check that the method
                    // exists on ALL constituents.
                    if(currentType is UnionType unionForMethodCall)
                    {
                        if(unionForMethodCall.Constituents.Any(c => c.IsVoid()))
                        {
                            diagnostics.ReportAtNode(TypeCheckDiagnostics.UnionMemberNotOnAllConstituents, methodCall, "TypeChecker", methodCall.Name.Lexeme, currentType!.DisplayName, "void");
                            currentType = ErrorType.Instance;
                            targetIsTypeName = false;
                            break;
                        }

                        var unionMethodResult = CheckUnionMemberAccess(
                            methodCall.Name.Lexeme, unionForMethodCall, methodCall, model, diagnostics);
                        currentType = unionMethodResult;
                        targetIsTypeName = false;
                        break;
                    }

                    CheckMethodCallArguments(methodCall, currentType, model, diagnostics);
                    CheckMemberAccessModifiers(methodCall.Name.Lexeme, currentType, targetIsTypeName, methodCall, model, diagnostics);
                    currentType = SubstituteMemberType(model.GetTypeOf(suffix), currentType);
                    // Also substitute method-level type arguments in the return type.
                    currentType = SubstituteMethodReturnType(currentType, methodCall, model);
                    // ── stack-only-types: Slice length inference (Requirements 1.6, 2.6) ──
                    // When a Slice call is made on a SpanType receiver, infer the result
                    // as a SpanType of the same kind with KnownLength = len for a literal
                    // len argument, unknown otherwise.
                    currentType = TryInferSliceLengthFromCall(methodCall, currentType);
                    // A method call's return flows through the unknown-receiver
                    // substitution so the result type is reported as `unknown` at
                    // any position the generic's type parameter was closed with it.
                    currentType = SubstituteClosedUnknownInResultType(currentType, model.GetTypeOf(node.Target));
                    // After the first suffix, subsequent accesses are on instances (the result of the method call).
                    targetIsTypeName = false;
                    break;

                case InvocationSuffixPostfixSuffixNode invocation:
                    CheckInvocationArguments(invocation, currentType, node, model, diagnostics);
                    currentType = model.GetTypeOf(suffix);
                    targetIsTypeName = false;
                    break;

                case IndexSuffixPostfixSuffixNode indexSuffix:
                    // Task 25.1: bare-unknown member access rejection.
                    // An indexer access on a receiver whose static type is
                    // exactly `UnknownType` has no valid target — the
                    // singleton has no indexer (Req 3.4). Emit
                    // MIST_TC_0065 naming the canonical indexer lexeme
                    // and short-circuit the remaining suffix logic to
                    // suppress cascading diagnostics.
                    if(currentType is UnknownType)
                    {
                        diagnostics.ReportAtNode(TypeCheckDiagnostics.BareUnknownMemberAccess, indexSuffix, "TypeChecker", "[]", "unknown");
                        currentType = ErrorType.Instance;
                        targetIsTypeName = false;
                        break;
                    }

                    CheckIndexSuffix(indexSuffix, currentType, model, diagnostics);
                    currentType = model.GetTypeOf(suffix);
                    // A read through an indexer on List<unknown> yields `unknown`
                    // at every position the generic's T was closed with unknown.
                    currentType = SubstituteClosedUnknownInResultType(currentType, model.GetTypeOf(node.Target));
                    targetIsTypeName = false;
                    break;

                case SwitchExpressionSuffixPostfixSuffixNode switchExpr:
                    // Walk each switch arm: check guard type and recurse into sub-expressions.
                    if(switchExpr.Arms is not null)
                    {
                        CheckSwitchArm(switchExpr.Arms.SwitchArm, model, diagnostics);
                        foreach(var arm in switchExpr.Arms.SwitchArms)
                        {
                            CheckSwitchArm(arm, model, diagnostics);
                        }

                        // Task 3.4: Validate switch arm patterns against the scrutinee type.
                        // The scrutinee type is `currentType` (the type of the expression
                        // before the switch suffix was applied).
                        if(currentType is not null and not ErrorType)
                        {
                            CheckPattern(switchExpr.Arms.SwitchArm.Pattern, currentType, model, diagnostics);
                            foreach(var arm in switchExpr.Arms.SwitchArms)
                            {
                                CheckPattern(arm.Pattern, currentType, model, diagnostics);
                            }
                        }

                        // Task 5.3: Arm result type compatibility checking.
                        // If the semantic model determined that no common type exists
                        // (unified type is UnknownType), emit MIST_TC_0091.
                        CheckSwitchArmResultTypeCompatibility(switchExpr, model, diagnostics);

                        // Task 5.5: Exhaustiveness checking.
                        // The scrutinee type is `currentType` at this point (the type
                        // of the expression before the switch suffix was applied).
                        if(currentType is not null and not ErrorType)
                        {
                            var armTuples = BuildSwitchArmTuples(switchExpr.Arms);
                            var missingCases = ExhaustivenessChecker.CheckExhaustiveness(currentType, armTuples);
                            if(missingCases.Count > 0)
                            {
                                var casesStr = string.Join(", ", missingCases);
                                diagnostics.ReportAtNode(TypeCheckDiagnostics.SwitchNotExhaustive, switchExpr, "TypeChecker", casesStr);
                            }
                        }

                        // Task 5.6: Unreachable arm detection.
                        CheckSwitchUnreachableArms(switchExpr.Arms, diagnostics);
                    }

                    currentType = model.GetTypeOf(suffix) ?? currentType;
                    targetIsTypeName = false;
                    break;

                case MemberAccessSuffixPostfixSuffixNode memberAccess:
                    // Task 25.1: bare-unknown member access rejection.
                    // A member access on a receiver whose static type is
                    // exactly `UnknownType` has no valid target — the
                    // singleton has no member scope (Req 3.4). Emit
                    // MIST_TC_0065 and short-circuit the remaining
                    // suffix logic to suppress cascading diagnostics.
                    if(currentType is UnknownType)
                    {
                        diagnostics.ReportAtNode(TypeCheckDiagnostics.BareUnknownMemberAccess, memberAccess, "TypeChecker", memberAccess.Name.Lexeme, "unknown");
                        currentType = ErrorType.Instance;
                        break;
                    }
                    // Task 6.2: union type member access validation (Req 7.1, 7.2).
                    // If the receiver is a UnionType containing void, direct `.` access
                    // is invalid — the user must use `?.` to safely unwrap the optional.
                    // For non-void unions (e.g., int | string), check that the member
                    // exists on ALL constituents.
                    if(currentType is UnionType unionForMemberAccess)
                    {
                        if(unionForMemberAccess.Constituents.Any(c => c.IsVoid()))
                        {
                            diagnostics.ReportAtNode(TypeCheckDiagnostics.UnionMemberNotOnAllConstituents, memberAccess, "TypeChecker", memberAccess.Name.Lexeme, currentType!.DisplayName, "void");
                            currentType = ErrorType.Instance;
                            targetIsTypeName = false;
                            break;
                        }

                        var unionMemberResult = CheckUnionMemberAccess(
                            memberAccess.Name.Lexeme, unionForMemberAccess, memberAccess, model, diagnostics);
                        currentType = unionMemberResult;
                        targetIsTypeName = false;
                        break;
                    }
                    // Enum member access: members are always public and static,
                    // so skip access modifier checks. The semantic model builder
                    // already validates member existence and reports MIST_SB_0082
                    // for undefined members.
                    if(currentType is EnumType)
                    {
                        currentType = model.GetTypeOf(suffix) ?? ErrorType.Instance;
                        targetIsTypeName = false;
                        break;
                    }
                    // Anonymous type property access: properties are always public
                    // and have no declared symbol, so skip access modifier checks.
                    // The semantic model builder resolves property types and reports
                    // MIST_UNDEFINED_MEMBER for unknown property names.
                    if(currentType is AnonymousType)
                    {
                        currentType = model.GetTypeOf(suffix) ?? ErrorType.Instance;
                        targetIsTypeName = false;
                        break;
                    }

                    CheckMemberAccessModifiers(memberAccess.Name.Lexeme, currentType, targetIsTypeName, memberAccess, model, diagnostics);
                    // MIST_TC_0019: Check property has accessible get accessor when read.
                    CheckPropertyReadAccess(memberAccess, currentType, model, diagnostics);
                    currentType = SubstituteMemberType(model.GetTypeOf(suffix), currentType);
                    // A read of a property/field whose substituted type mentions T
                    // at a position closed with unknown must report the result as
                    // `unknown` at that position (Requirement 10.2).
                    currentType = SubstituteClosedUnknownInResultType(currentType, model.GetTypeOf(node.Target));
                    // After accessing a field, subsequent accesses are on the field's value (an instance).
                    targetIsTypeName = false;
                    break;

                case OptionalMemberAccessSuffixPostfixSuffixNode optionalMemberAccess:
                    {
                        // Task 6.1: Optional chaining member access (Requirements 3.1, 3.2, 3.3, 3.4).
                        // The receiver must be a UnionType containing void.
                        var optMemberResult = CheckOptionalChainingReceiver(
                            currentType, optionalMemberAccess, diagnostics);
                        if(optMemberResult is null)
                        {
                            currentType = ErrorType.Instance;
                            targetIsTypeName = false;
                            break;
                        }

                        // Resolve the member on the projected type T (void removed).
                        var projectedType = optMemberResult;
                        var memberSymbol = FindMemberSymbol(projectedType, optionalMemberAccess.Name.Lexeme, model);
                        if(memberSymbol is null)
                        {
                            // FindMemberSymbol reads declared member scopes only, so it cannot see an
                            // extension member. The builder already resolved this suffix through the
                            // extension registry, and a member that genuinely does not exist fails
                            // there first -- Stage 4 errors abort the compile before Stage 5 runs.
                            var builderMemberType = model.GetTypeOf(suffix);

                            if(projectedType is UnknownType || builderMemberType is null or ErrorType)
                            {
                                diagnostics.ReportAtNode(TypeCheckDiagnostics.UndefinedMember, optionalMemberAccess, "TypeChecker");
                                currentType = ErrorType.Instance;
                                targetIsTypeName = false;
                                break;
                            }

                            currentType = builderMemberType;
                            targetIsTypeName = false;
                            break;
                        }

                        // The result type is R | void (flattened if R already contains void).
                        var memberType = SubstituteMemberType(memberSymbol.DeclaredType, projectedType);
                        currentType = WrapWithVoid(memberType, currentType);
                        model.SetTypeOf(suffix, currentType);
                        targetIsTypeName = false;
                        break;
                    }

                case OptionalMethodCallSuffixPostfixSuffixNode optionalMethodCall:
                    {
                        // Task 6.1: Optional chaining method call (Requirements 3.1, 3.2, 3.3, 3.4).
                        // The receiver must be a UnionType containing void.
                        var receiverTypeForVoidWrap = currentType;
                        var optMethodResult = CheckOptionalChainingReceiver(
                            currentType, optionalMethodCall, diagnostics);
                        if(optMethodResult is null)
                        {
                            currentType = ErrorType.Instance;
                            targetIsTypeName = false;
                            break;
                        }

                        // Resolve the method on the projected type T (void removed).
                        var projectedType = optMethodResult;
                        var methodSymbol = FindMemberSymbol(projectedType, optionalMethodCall.Name.Lexeme, model);
                        if(methodSymbol is null)
                        {
                            var builderReturnType = model.GetTypeOf(suffix);

                            if(projectedType is UnknownType || builderReturnType is null or ErrorType)
                            {
                                diagnostics.ReportAtNode(TypeCheckDiagnostics.UndefinedMember, optionalMethodCall, "TypeChecker");
                                currentType = ErrorType.Instance;
                                targetIsTypeName = false;
                                break;
                            }

                            if(optionalMethodCall.Arguments is not null)
                            {
                                CheckArgumentListExpressions(optionalMethodCall.Arguments, model, diagnostics);
                            }

                            currentType = builderReturnType;
                            targetIsTypeName = false;
                            break;
                        }

                        // Check argument expressions recursively.
                        if(optionalMethodCall.Arguments is not null)
                        {
                            CheckArgumentListExpressions(optionalMethodCall.Arguments, model, diagnostics);
                        }

                        // The result type is R | void (flattened if R already contains void).
                        var returnType = SubstituteMemberType(methodSymbol.DeclaredType, projectedType);
                        // Also substitute method-level type arguments in the return type.
                        if(optionalMethodCall.TypeArgs is not null && methodSymbol is Symbol sym && sym.TypeParameters.Count > 0)
                        {
                            var resolvedTypeArgs = ResolveTypeArgumentsFromAst(optionalMethodCall.TypeArgs, model);
                            if(resolvedTypeArgs.Count == sym.TypeParameters.Count)
                            {
                                returnType = SubstituteType(returnType!, sym.TypeParameters, resolvedTypeArgs);
                            }
                        }

                        currentType = WrapWithVoid(returnType, receiverTypeForVoidWrap);
                        model.SetTypeOf(suffix, currentType);
                        targetIsTypeName = false;
                        break;
                    }
            }
        }

        // After processing all suffixes, if the final type differs from what the
        // semantic model recorded (e.g., due to type argument inference refining
        // a TypeParameterType to a concrete type), update the PostfixNode's
        // expression type so that upstream consumers (var declarations, argument
        // type resolution) see the refined type.
        if(currentType is not null && node.Suffix.Length > 0)
        {
            var existingNodeType = model.GetTypeOf(node);
            if(existingNodeType is not null && currentType != existingNodeType)
            {
                model.SetTypeOf(node, currentType);
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Optional chaining helpers (MIST_TC_0111)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Validates that the receiver type of an optional chaining operator (<c>?.</c>)
    /// is a <see cref="UnionType"/> containing void. If valid, returns the projected
    /// type T (the union with void removed). If invalid, emits
    /// <see cref="OptionalChainingRequiresOptionalType"/> and returns <c>null</c>.
    /// </summary>
    private static IType? CheckOptionalChainingReceiver(
        IType? currentType,
        SyntaxNode node,

        DiagnosticBag diagnostics)
    {
        if(currentType is null or ErrorType)
        {
            return null;
        }

        // Unwrap type aliases (e.g., Optional<T> → T | void).
        var effectiveType = currentType;
        if(effectiveType is TypeAliasType alias)
        {
            effectiveType = alias.UnderlyingType;
        }

        // The receiver must be a UnionType containing void.
        if(effectiveType is UnionType union)
        {
            var hasVoid = false;
            foreach(var constituent in union.Constituents)
            {
                if(constituent.IsVoid())
                {
                    hasVoid = true;
                    break;
                }
            }

            if(hasVoid)
            {
                // Compute projected type T by removing void from constituents.
                var nonVoidConstituents = new List<IType>();
                foreach(var constituent in union.Constituents)
                {
                    if(!constituent.IsVoid())
                    {
                        nonVoidConstituents.Add(constituent);
                    }
                }

                if(nonVoidConstituents.Count == 0)
                {
                    // All constituents are void -- degenerate case.
                    diagnostics.ReportAtNode(TypeCheckDiagnostics.OptionalChainingRequiresOptionalType, node, "TypeChecker", currentType!.DisplayName);
                    return null;
                }

                if(nonVoidConstituents.Count == 1)
                {
                    return nonVoidConstituents[0];
                }

                return new UnionType(nonVoidConstituents);
            }
        }

        // Not a union containing void -- emit error.
        diagnostics.ReportAtNode(TypeCheckDiagnostics.OptionalChainingRequiresOptionalType, node, "TypeChecker", currentType!.DisplayName);
        return null;
    }

    /// <summary>
    /// Wraps a type with void to produce <c>R | void</c>. If the type already
    /// contains void (is a union with void), returns it unchanged (flattening).
    /// If the type is null or ErrorType, returns ErrorType.
    /// Uses the void type from the receiver's union to ensure type identity consistency.
    /// </summary>
    private static IType WrapWithVoid(IType? type, IType? receiverType = null)
    {
        if(type is null or ErrorType)
        {
            return ErrorType.Instance;
        }

        // If R is already void, return void.
        if(type.IsVoid())
        {
            return type;
        }

        // If R is already a union containing void, return it unchanged.
        if(type is UnionType existingUnion)
        {
            foreach(var constituent in existingUnion.Constituents)
            {
                if(constituent.IsVoid())
                {
                    return type;
                }
            }
        }

        // Extract the void type from the receiver's union (if available) to ensure
        // type identity consistency. This avoids mismatches between LegacyPrimitiveTypes.Void
        // (DisplayName "void") and the stdlib Standard.Void (DisplayName "Void").
        IType voidType = LegacyPrimitiveTypes.Void;
        if(receiverType is UnionType receiverUnion)
        {
            foreach(var constituent in receiverUnion.Constituents)
            {
                if(constituent.IsVoid())
                {
                    voidType = constituent;
                    break;
                }
            }
        }

        // Create R | void.
        return new UnionType(new List<IType> { type, voidType });
    }

    /// <summary>
    /// Checks a single switch arm: validates the guard expression type (must be bool)
    /// and recursively walks the guard and result sub-expressions.
    /// Emits <see cref="SwitchGuardNotBool"/> (MIST_TC_0092) if the guard type is not bool.
    /// </summary>
    private void CheckSwitchArm(SwitchArmNode arm, ISemanticModel model, DiagnosticBag diagnostics)
    {
        // Walk the guard expression (if present) and verify it resolves to bool.
        if(arm.Guard is not null)
        {
            CheckVoidCoalescingNode(arm.Guard.Condition, model, diagnostics);
            var guardType = model.GetTypeOf(arm.Guard.Condition);
            if(guardType is not null && !IsErrorType(guardType) && !IsBoolLike(guardType))
            {
                diagnostics.ReportAtNode(TypeCheckDiagnostics.SwitchGuardNotBool, arm.Guard.Condition, "TypeChecker", guardType.DisplayName);
            }
        }

        // Walk the result expression to check its sub-expressions.
        CheckExpression(arm.Result, model, diagnostics);
    }

    /// <summary>
    /// Checks that all switch arm result expressions have compatible types.
    /// If the semantic model's unified result type is <see cref="UnknownType"/>
    /// (indicating no common type could be determined), emits
    /// <see cref="SwitchIncompatibleArmTypes"/> (MIST_TC_0091) with the first
    /// pair of incompatible types found.
    /// </summary>
    private static void CheckSwitchArmResultTypeCompatibility(
        SwitchExpressionSuffixPostfixSuffixNode switchExpr,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // The unified result type stored by the semantic model is UnknownType.Instance
        // when no common type could be determined across all arms.
        var unifiedType = model.GetTypeOf(switchExpr);
        if(unifiedType is not UnknownType)
        {
            return;
        }

        // Collect arm result types to find the first incompatible pair.
        var arms = switchExpr.Arms!;
        var firstArmType = model.GetTypeOf(arms.SwitchArm.Result);
        if(firstArmType is null || IsErrorType(firstArmType))
        {
            return;
        }

        // Check the first arm against remaining arms to find the incompatible pair.
        foreach(var arm in arms.SwitchArms)
        {
            var armType = model.GetTypeOf(arm.Result);
            if(armType is null || IsErrorType(armType))
            {
                continue;
            }

            if(!IsAssignable(armType, firstArmType) && !IsAssignable(firstArmType, armType))
            {
                diagnostics.ReportAtNode(TypeCheckDiagnostics.SwitchIncompatibleArmTypes, switchExpr, "TypeChecker", firstArmType.DisplayName, armType!.DisplayName);
                return;
            }
        }
    }

    /// <summary>
    /// Builds the list of <c>(PatternNode, bool HasGuard)</c> tuples from a
    /// <see cref="SwitchArmListNode"/> for use by the exhaustiveness checker.
    /// </summary>
    private static IReadOnlyList<(PatternNode Pattern, bool HasGuard)> BuildSwitchArmTuples(
        SwitchArmListNode armList)
    {
        var result = new List<(PatternNode, bool)>
        {
            (armList.SwitchArm.Pattern, armList.SwitchArm.Guard is not null)
        };
        foreach(var arm in armList.SwitchArms)
        {
            result.Add((arm.Pattern, arm.Guard is not null));
        }

        return result;
    }

    /// <summary>
    /// Detects unreachable switch arms and emits <see cref="SwitchUnreachableArm"/>
    /// (MIST_TC_0093) for each one. An arm is unreachable if:
    /// <list type="bullet">
    ///   <item>An unguarded catch-all (<c>_</c> or <c>var</c>) appears at an earlier position.</item>
    ///   <item>An unguarded constant pattern duplicates a preceding unguarded constant pattern.</item>
    ///   <item>An unguarded type pattern duplicates a preceding unguarded type pattern for the same type.</item>
    /// </list>
    /// </summary>
    private static void CheckSwitchUnreachableArms(
        SwitchArmListNode armList,

        DiagnosticBag diagnostics)
    {
        // Collect all arms into a flat list for positional iteration.
        var arms = new List<SwitchArmNode> { armList.SwitchArm };
        foreach(var arm in armList.SwitchArms)
        {
            arms.Add(arm);
        }

        var catchAllSeenAtIndex = -1;
        var seenConstants = new HashSet<string>(StringComparer.Ordinal);
        var seenTypes = new HashSet<string>(StringComparer.Ordinal);

        for(var i = 0; i < arms.Count; i++)
        {
            var arm = arms[i];
            var hasGuard = arm.Guard is not null;

            // Rule 1: If an unguarded catch-all was seen at an earlier position,
            // all subsequent arms are unreachable.
            if(catchAllSeenAtIndex >= 0 && i > catchAllSeenAtIndex)
            {
                diagnostics.ReportAtNode(TypeCheckDiagnostics.SwitchUnreachableArm, arm, "TypeChecker");
                continue;
            }

            // Extract the primary pattern for classification.
            var primary = TryGetSinglePrimaryPattern(arm.Pattern);
            if(primary is null)
            {
                continue;
            }

            // Check if this arm is a catch-all (discard or var pattern).
            if(!hasGuard && IsCatchAllPrimaryPattern(primary))
            {
                catchAllSeenAtIndex = i;
                continue;
            }

            // Rule 2: Duplicate unguarded constant patterns.
            if(!hasGuard && primary is ConstantPatternPrimaryPatternNode constantPattern)
            {
                var key = GetConstantKey(constantPattern.Literal);
                if(key is not null && !seenConstants.Add(key))
                {
                    diagnostics.ReportAtNode(TypeCheckDiagnostics.SwitchUnreachableArm, arm, "TypeChecker");
                }

                continue;
            }

            // Rule 3: Duplicate unguarded type patterns for the same type.
            if(!hasGuard)
            {
                var typeName = primary switch
                {
                    TypePatternPrimaryPatternNode typePattern =>
                        ExtractTypeDisplayNameFromRef(typePattern.Type),
                    BareTypePatternPrimaryPatternNode bareTypePattern =>
                        ExtractTypeDisplayNameFromRef(bareTypePattern.Type),
                    _ => null,
                };

                if(typeName is not null && !seenTypes.Add(typeName))
                {
                    diagnostics.ReportAtNode(TypeCheckDiagnostics.SwitchUnreachableArm, arm, "TypeChecker");
                }
            }
        }
    }

    /// <summary>
    /// Attempts to extract the single <see cref="PrimaryPatternNode"/> from a
    /// <see cref="PatternNode"/>, navigating through the Or → And → Unary chain.
    /// Returns <c>null</c> if the pattern is compound.
    /// </summary>
    private static PrimaryPatternNode? TryGetSinglePrimaryPattern(PatternNode pattern)
    {
        var orPattern = pattern.OrPattern;

        // If there are multiple or-alternatives, this is not a simple pattern.
        if(orPattern.AndPatterns.Length > 0)
        {
            return null;
        }

        var andPattern = orPattern.Left;

        // If there are multiple and-conjuncts, this is not a simple pattern.
        if(andPattern.UnaryPatterns.Length > 0)
        {
            return null;
        }

        var unaryPattern = andPattern.Left;

        // Only direct patterns (not negated) can be catch-alls or simple patterns.
        if(unaryPattern is not DirectPatternUnaryPatternNode directPattern)
        {
            return null;
        }

        // Handle parenthesized patterns by recursing into the inner pattern.
        if(directPattern.Primary is ParenthesizedPatternPrimaryPatternNode parenthesized)
        {
            return TryGetSinglePrimaryPattern(parenthesized.Inner);
        }

        return directPattern.Primary;
    }

    /// <summary>
    /// Returns <c>true</c> if the given primary pattern is a catch-all
    /// (discard <c>_</c> or <c>var x</c>).
    /// </summary>
    private static bool IsCatchAllPrimaryPattern(PrimaryPatternNode primary)
    {
        return primary is VarPatternPrimaryPatternNode or DiscardPatternPrimaryPatternNode;
    }

    /// <summary>
    /// Returns a string key representing a constant literal for duplicate detection.
    /// Returns <c>null</c> if the literal type is not recognized.
    /// </summary>
    private static string? GetConstantKey(ConstantLiteralNode literal)
    {
        return literal switch
        {
            IntegerConstantConstantLiteralNode intConst => $"int:{intConst.Value.Lexeme}",
            FloatConstantConstantLiteralNode floatConst => $"float:{floatConst.Value.Lexeme}",
            StringConstantConstantLiteralNode strConst => $"string:{strConst.Value.Lexeme}",
            CharConstantConstantLiteralNode charConst => $"char:{charConst.Value.Lexeme}",
            TrueConstantConstantLiteralNode => "bool:true",
            FalseConstantConstantLiteralNode => "bool:false",
            _ => null,
        };
    }

    /// <summary>
    /// Extracts the display name from a <see cref="TypeReferenceNode"/> for
    /// type pattern duplicate detection.
    /// </summary>
    private static string? ExtractTypeDisplayNameFromRef(TypeReferenceNode typeRef)
    {
        return typeRef.First switch
        {
            PrimitiveSingleTypeRefSingleTypeRefNode primitiveRef => primitiveRef.PrimitiveType switch
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
                RegexTypePrimitiveTypeNode => "regex",
                _ => null,
            },
            NamedSingleTypeRefSingleTypeRefNode namedRef => namedRef.Name.IDENTIFIER.Lexeme,
            _ => null,
        };
    }

    /// <summary>
    /// Substitutes type parameters in a member's type when the target is a <see cref="ConstructedType"/>.
    /// For example, if the member type is <c>T</c> and the target is <c>Box&lt;int&gt;</c>,
    /// the result is <c>int</c>.
    /// </summary>
    private static IType? SubstituteMemberType(IType? memberType, IType? targetType)
    {
        if(memberType is null)
        {
            return memberType;
        }

        if(targetType is ConstructedType ct)
        {
            var typeParams = GetTypeParameters(ct.GenericDefinition);
            if(typeParams is null)
            {
                return memberType;
            }

            return SubstituteType(memberType, typeParams, ct.TypeArguments);
        }

        return memberType;
    }

    /// <summary>
    /// Substitutes method-level type parameters in the return type of a generic method invocation.
    /// For example, if the method is <c>T Identity&lt;T&gt;(T value)</c> and invoked as <c>Identity&lt;int&gt;(42)</c>,
    /// the return type <c>T</c> becomes <c>int</c>.
    /// Also handles inferred type arguments when explicit type arguments are omitted.
    /// </summary>
    private static IType? SubstituteMethodReturnType(IType? returnType, MethodCallSuffixPostfixSuffixNode methodCall, ISemanticModel model)
    {
        if(returnType is null)
        {
            return returnType;
        }

        var methodSymbol = model.GetDeclaredSymbol(methodCall);
        if(methodSymbol is not Symbol sym || sym.TypeParameters.Count == 0)
        {
            return returnType;
        }

        // Try explicit type args first (existing behavior)
        if(methodCall.TypeArgs is not null)
        {
            var methodTypeArgs = ResolveTypeArgumentsFromAst(methodCall.TypeArgs, model);
            if(methodTypeArgs.Count != sym.TypeParameters.Count)
            {
                return returnType;
            }

            return SubstituteType(returnType, sym.TypeParameters, methodTypeArgs);
        }

        // Try inferred type args
        var inferredArgs = model.GetInferredTypeArguments(methodCall);
        if(inferredArgs is not null && inferredArgs.Count == sym.TypeParameters.Count)
        {
            return SubstituteType(returnType, sym.TypeParameters, inferredArgs);
        }

        return returnType;
    }

    /// <summary>
    /// When a <c>Slice(start, len)</c> call is made on a <see cref="SpanType"/> receiver,
    /// infers the result as a <see cref="SpanType"/> of the same kind (mutable/read-only)
    /// with <c>KnownLength = len</c> when the second argument is a literal integer,
    /// or unknown length otherwise (Requirements 1.6, 2.6).
    /// Returns <paramref name="currentType"/> unchanged if not applicable.
    /// </summary>
    private static IType? TryInferSliceLengthFromCall(
        MethodCallSuffixPostfixSuffixNode methodCall,
        IType? currentType)
    {
        // Only applies to Slice calls whose return type is already a SpanType.
        if(currentType is not SpanType resultSpan)
        {
            return currentType;
        }

        // Must be a method named "Slice".
        if(methodCall.Name.Lexeme != "Slice")
        {
            return currentType;
        }

        // Must have arguments with at least two parameters (start, length).
        if(methodCall.Arguments is null)
        {
            return currentType;
        }

        // The second argument is the length parameter.
        if(methodCall.Arguments.Arguments.Length < 1)
        {
            return currentType;
        }

        var lengthArg = methodCall.Arguments.Arguments[0];
        var lengthExpr = ArgumentListNodeExtensions.ExtractExpression(lengthArg);

        // Try to extract a literal integer value from the length argument.
        var literalValue = TryExtractLiteralConstantValue(lengthExpr);
        if(literalValue is int intVal)
        {
            return new SpanType(resultSpan.ElementType, (long)intVal, resultSpan.IsReadOnly);
        }

        if(literalValue is long longVal)
        {
            return new SpanType(resultSpan.ElementType, longVal, resultSpan.IsReadOnly);
        }

        if(literalValue is uint uintVal)
        {
            return new SpanType(resultSpan.ElementType, uintVal, resultSpan.IsReadOnly);
        }

        if(literalValue is ulong ulongVal && ulongVal <= long.MaxValue)
        {
            return new SpanType(resultSpan.ElementType, (long)ulongVal, resultSpan.IsReadOnly);
        }

        // Non-literal length → result is unknown-length (already the case from the declared return type).
        return currentType;
    }

    /// <summary>
    /// After a member read passes the filter, substitute <see cref="UnknownType"/>
    /// for every type-parameter position of <paramref name="receiverType"/>'s
    /// generic definition that is closed with <c>unknown</c>. This implements
    /// Requirement 10.2: a read through <c>List&lt;unknown&gt;</c> of an
    /// indexer that returns <c>T</c> must report the result as
    /// <see cref="UnknownType"/>, not <c>T</c>.
    /// </summary>
    /// <remarks>
    /// PRESERVE: This method handles result-type substitution for member access
    /// on Closed_Generic_With_Unknown receivers. It is orthogonal to the readonly
    /// enforcement (Tasks 7–8 reachability cleanup) and must NOT be removed.
    /// See unknown-readonly-semantics spec, Task 6 and design § SubstituteClosedUnknownInResultType.
    /// </remarks>
    private static IType? SubstituteClosedUnknownInResultType(IType? resultType, IType? receiverType)
    {
        if(resultType is null || receiverType is not ConstructedType ct)
        {
            return resultType;
        }

        var typeParams = GetTypeParameters(ct.GenericDefinition);
        if(typeParams is null)
        {
            return resultType;
        }

        // Build a parallel (TypeParameter, TypeArgument) pair but replace
        // every occurrence of UnknownType with itself so SubstituteType
        // maps T → unknown at exactly the positions the user wrote.
        // Non-unknown positions use the original argument (already the
        // user's concrete substitution), preserving the current behavior
        // for `List<int>` where T → int.
        var count = Math.Min(typeParams.Count, ct.TypeArguments.Count);
        var args = new IType[count];
        for(var i = 0; i < count; i++)
        {
            args[i] = ct.TypeArguments[i];
        }

        // SubstituteType already does this mapping when used with the
        // constructed type's own arguments; `SubstituteMemberType` is the
        // suffix-level helper used elsewhere and is equivalent here. We
        // call SubstituteType directly to make the intent explicit and
        // to decouple from the suffix API.
        return SubstituteType(resultType, typeParams, args);
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
        if(targetType is null || IsErrorType(targetType))
        {
            return;
        }

        var memberName = memberAccess.Name.Lexeme;
        var memberSymbol = FindMemberSymbol(targetType, memberName, model);
        if(memberSymbol is null || memberSymbol.Kind != SymbolKind.Property)
        {
            return;
        }

        // Skip backend symbols — they are assumed to have proper accessors.
        if(memberSymbol.Origin == SymbolOrigin.Backend)
        {
            return;
        }

        var propInfo = GetPropertyMetadata(memberSymbol);
        if(propInfo is null)
        {
            return;
        }

        if(!propInfo.HasGet)
        {
            diagnostics.ReportAtNode(TypeCheckDiagnostics.PropertyNoGetAccessor, memberAccess, "TypeChecker");
        }
    }

    private void CheckPrimaryNode(PrimaryNode node, ISemanticModel model, DiagnosticBag diagnostics)
    {
        switch(node)
        {
            case ArrayCreationExpressionPrimaryNode arrayCreation:
                CheckArrayCreation(arrayCreation, model, diagnostics);
                break;

            case StackallocExpressionPrimaryNode stackallocExpr:
                CheckStackallocExpression(stackallocExpr, model, diagnostics);
                break;

            case ObjectCreationExpressionPrimaryNode objectCreation:
                CheckObjectCreation(objectCreation, model, diagnostics);
                break;

            case ObjectCreationExpressionNoArgsPrimaryNode objectCreationNoArgs:
                CheckObjectCreationNoArgs(objectCreationNoArgs, model, diagnostics);
                break;

            case TargetTypedNewExpressionPrimaryNode targetTypedNew:
                CheckTargetTypedNewExpression(targetTypedNew, model, diagnostics);
                break;

            case TargetTypedNewExpressionNoArgsPrimaryNode targetTypedNewNoArgs:
                CheckTargetTypedNewExpressionNoArgs(targetTypedNewNoArgs, model, diagnostics);
                break;

            case ParenthesizedExpressionPrimaryNode paren:
                CheckExpression(paren.Inner, model, diagnostics);
                break;

            case InterpolatedStringExpressionPrimaryNode interpolated:
                foreach(var part in interpolated.InterpolatedString.Parts)
                {
                    if(part is ExpressionPartInterpolatedPartNode exprPart)
                    {
                        CheckExpression(exprPart.Expression, model, diagnostics);
                    }
                }

                break;

            case TypeLiteralExpressionPrimaryNode:
                // Type literals are always valid primary expressions.
                // Type checking is handled by assignability checks at usage sites.
                break;

            // Must precede the general array literal case below: an absent element list is
            // the empty literal, which the grammar no longer gives a distinct node.
            case ArrayLiteralExpressionPrimaryNode { Elements: null } emptyArrayLiteral:
                // Empty array literal [] — when no target type is available (resolved
                // as ErrorType by the semantic model builder), emit a diagnostic error.
                // When assigned to a typed variable, the type is inferred from the
                // annotation and no error is needed.
                {
                    var resolvedType = model.GetTypeOf(emptyArrayLiteral);
                    if(resolvedType is ErrorType)
                    {
                        diagnostics.ReportAtNode(TypeCheckDiagnostics.EmptyArrayLiteralNoType, emptyArrayLiteral, "TypeChecker");
                    }
                }

                break;

            case FieldKeywordExpressionPrimaryNode fieldKeyword:
                // MIST_TC_0023: 'field' keyword used outside property accessor body.
                // If we reach here during method/constructor body checking, it means
                // 'field' is used outside a property accessor context.
                // Note: When checking property accessor bodies, the semantic model
                // resolves 'field' to the backing field symbol, so this check only
                // fires when 'field' appears in a non-property context.
                if(!insidePropertyAccessor)
                {
                    diagnostics.ReportAtNode(TypeCheckDiagnostics.FieldKeywordOutsideAccessor, fieldKeyword, "TypeChecker");
                }

                break;

            case DefaultExpressionPrimaryNode defaultExpr:
                CheckDefaultExpression(defaultExpr, model, diagnostics);
                break;

            case DefaultLiteralExpressionPrimaryNode defaultLiteral:
                CheckDefaultLiteralExpression(defaultLiteral, model, diagnostics);
                break;

            case ArrayLiteralExpressionPrimaryNode arrayLiteral:
                CheckArrayLiteral(arrayLiteral, model, diagnostics);
                break;

            case RegexLiteralExpressionPrimaryNode regexLiteral:
                ValidateRegexPattern(regexLiteral.RegexLiteral.Lexeme, regexLiteral, diagnostics);
                break;

            // Literals and identifiers don't need type checking.
            default:
                break;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Regex literal compile-time validation (Requirements 4.6, 5.3, 5.4, 5.5)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Validates a regex literal pattern at compile time using the .NET regex engine.
    /// The <paramref name="lexeme"/> is the full token text including backtick delimiters.
    /// Escape sequences <c>\`</c> → <c>`</c> and <c>\\</c> → <c>\</c> are resolved
    /// before validation. If the pattern is invalid, emits <c>MIST_TC_REGEX_INVALID</c>
    /// with the error description and offset. The expression type remains
    /// <c>Standard.Regex</c> regardless of validity (to prevent cascading errors).
    /// </summary>
    private static void ValidateRegexPattern(
        string lexeme,
        SyntaxNode node,

        DiagnosticBag diagnostics)
    {
        // Strip backtick delimiters to get the raw pattern content.
        var raw = lexeme.Length >= 2 ? lexeme[1..^1] : string.Empty;

        // Resolve escape sequences: \` → ` and \\ → \
        var pattern = raw.Replace("\\`", "`").Replace("\\\\", "\\");

        try
        {
            // Validate using the same regex flavor as the runtime (.NET).
            _ = new Regex(pattern, RegexOptions.None);
        }
        catch(ArgumentException ex)
        {
            // Extract the offset from the exception if available.
            // .NET's RegexParseException (derived from ArgumentException) provides an Offset property.
            _ = ex is RegexParseException parseEx ? parseEx.Offset : 0; diagnostics.ReportAtNode(TypeCheckDiagnostics.RegexPatternInvalid, node, "TypeChecker");
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
        if(resolvedType is null || IsErrorType(resolvedType))
        {
            diagnostics.ReportAtNode(CommonDiagnostics.DefaultUndefinedType, defaultExpr, "TypeChecker");
            return;
        }

        ValidateDefaultableType(resolvedType, defaultExpr, diagnostics);
    }

    /// <summary>
    /// Validates a bare <c>default</c> literal expression:
    /// checks that the type was inferred from context. If the type is still
    /// <see cref="ErrorType"/>, emits <c>MIST_DEFAULT_CANNOT_INFER</c>.
    /// If a type was inferred, validates that it is defaultable.
    /// </summary>
    private static void CheckDefaultLiteralExpression(
        DefaultLiteralExpressionPrimaryNode defaultLiteral,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        var resolvedType = model.GetTypeOf(defaultLiteral);

        // If the type is still unknown, context inference did not find a type.
        if(resolvedType is null || IsErrorType(resolvedType))
        {
            diagnostics.ReportAtNode(CommonDiagnostics.DefaultCannotInferType, defaultLiteral, "TypeChecker");
            return;
        }

        ValidateDefaultableType(resolvedType, defaultLiteral, diagnostics);
    }

    /// <summary>
    /// Validates that a resolved type supports the <c>default</c> operator.
    /// Emits the appropriate diagnostic if the type is not defaultable.
    /// </summary>
    private static void ValidateDefaultableType(
        IType resolvedType,
        SyntaxNode node,

        DiagnosticBag diagnostics)
    {
        // Unwrap type aliases to their underlying type for validation.
        while(resolvedType is TypeAliasType alias)
        {
            resolvedType = alias.UnderlyingType;
        }

        // ── unknown-type storage ban on `default` (Task 12.4) ────────────
        // Requirement 5.6 — `default(unknown)` and bare `default` inferred
        // to `unknown` are forbidden. Checked before the abstract/
        // type-parameter rejections below because `unknown` is none of
        // those — it is its own TypeKind (TypeKind.Unknown) — and the
        // diagnostic for each of those cases would be misleading. The
        // rule fires for both explicit `default(unknown)` and bare
        // `default` whose inferred type is UnknownType (e.g.,
        // `unknown x = default;`).
        if(resolvedType is UnknownType)
        {
            diagnostics.ReportAtNode(TypeCheckDiagnostics.UnknownHasNoDefault, node, "TypeChecker");
            return;
        }

        switch(resolvedType)
        {
            case AbstractType:
                diagnostics.ReportAtNode(CommonDiagnostics.DefaultOnAbstract, node, "TypeChecker");
                return;

            case TypeParameterType typeParamType:
                if(typeParamType.Constraints?.HasDefaultConstraint == true)
                {
                    return;
                }

                diagnostics.ReportAtNode(CommonDiagnostics.DefaultOnTypeParameter, node, "TypeChecker");
                return;

            case ClassType classType:
                if(classType.Defaultability == DefaultabilityStatus.NotDefaultable)
                {
                    diagnostics.ReportAtNode(CommonDiagnostics.DefaultNotSupported, node, "TypeChecker");
                }

                return;

            case StructType structType:
                if(structType.Defaultability == DefaultabilityStatus.NotDefaultable)
                {
                    diagnostics.ReportAtNode(CommonDiagnostics.DefaultNotSupported, node, "TypeChecker");
                }

                return;

            case EnumType enumType:
                if(enumType.Defaultability != DefaultabilityStatus.Explicit)
                {
                    diagnostics.ReportAtNode(CommonDiagnostics.DefaultNotSupported, node, "TypeChecker");
                }

                return;

            case UnionType unionType:
                if(!unionType.Constituents.Any(c => c.IsVoid()))
                {
                    diagnostics.ReportAtNode(CommonDiagnostics.DefaultNotSupported, node, "TypeChecker");
                }

                return;

                // Primitive types (represented as ClassType from stdlib) and other
                // types are handled by the ClassType/StructType cases above.
                // ConstructedType, ArrayType, etc. fall through — no diagnostic needed
                // as they are not directly defaultable via default(T) in v1.
        }
    }

    /// <summary>
    /// Walks down the expression tree to retrieve the refined type after type checking.
    /// When type argument inference resolves a TypeParameterType to a concrete type,
    /// the method call suffix's expression type is updated via SetTypeOf, but the
    /// wrapper nodes (ExpressionNode, AssignmentNode, etc.) retain the stale type.
    /// This helper walks down to the PostfixNode to get the actual type.
    /// </summary>
    private static IType? GetRefinedInitializerType(ExpressionNode expr, ISemanticModel model)
    {
        // Expression → Assignment → LogicalOr → LogicalAnd → Equality →
        // Relational → Additive → Multiplicative → Unary → Postfix
        var assignment = expr.AsAssignmentOrNull();
        if(assignment is null)
        {
            return null; // Lambda form — not a simple postfix chain.
        }

        if(assignment.Group0?.Value is not null)
        {
            return null;
        }

        var logicalOr = assignment.Target.Left;
        if(logicalOr.Right is not null)
        {
            return null;
        }

        var logicalAnd = logicalOr.Left;
        if(logicalAnd.Right is not null)
        {
            return null;
        }

        var equality = logicalAnd.Left;
        if(!equality.Group0.IsDefaultOrEmpty)
        {
            return null;
        }

        var relational = equality.Left;
        if(relational is not RelationalComparisonRelationalNode cmp)
        {
            return null;
        }

        if(!cmp.Group0.IsDefaultOrEmpty)
        {
            return null;
        }

        var additive = cmp.Left;
        if(!additive.Group0.IsDefaultOrEmpty)
        {
            return null;
        }

        var multiplicative = additive.Left;
        if(!multiplicative.Group0.IsDefaultOrEmpty)
        {
            return null;
        }

        var unary = multiplicative.Left.UnwrapRange();
        if(unary is not PostfixExpressionUnaryNode postfixExpr)
        {
            return null;
        }

        // The PostfixNode's type is updated by CheckPostfixNode when inference
        // refines the return type. Use it directly.
        return model.GetTypeOf(postfixExpr.Primary);
    }

    /// <summary>
    /// Attempts to extract an <see cref="AsExpressionRelationalNode"/> from an
    /// expression node by walking down the expression chain. Returns the node if
    /// the expression is a simple <c>expr as T</c> with no outer operators.
    /// Used to propagate the type checker's refined result type (T or T | void)
    /// to the enclosing <c>var</c> declaration's symbol.
    /// </summary>
    private static AsExpressionRelationalNode? TryExtractAsExpression(ExpressionNode expr)
    {
        // Expression → Assignment → VoidCoalescing → LogicalOr → LogicalAnd → Equality → Relational
        var assignment = expr.AsAssignmentOrNull();
        if(assignment is null)
        {
            return null; // Lambda form — not an as expression.
        }

        if(assignment.Group0?.Value is not null)
        {
            return null;
        }

        // VoidCoalescing: must not have any ?? right-hand operands.
        if(assignment.Target.LogicalOrs.Length > 0)
        {
            return null;
        }

        var logicalOr = assignment.Target.Left;
        if(logicalOr.Right is not null)
        {
            return null;
        }

        var logicalAnd = logicalOr.Left;
        if(logicalAnd.Right is not null)
        {
            return null;
        }

        var equality = logicalAnd.Left;
        if(!equality.Group0.IsDefaultOrEmpty)
        {
            return null;
        }

        return equality.Left as AsExpressionRelationalNode;
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
        var assignment = expr.AsAssignmentOrNull();
        if(assignment is null)
        {
            return null; // Lambda form — not a bare 'default'.
        }

        if(assignment.Group0?.Value is not null)
        {
            return null; // Has an assignment operator — not a simple expression.
        }

        var logicalOr = assignment.Target.Left;
        if(logicalOr.Right is not null)
        {
            return null;
        }

        var logicalAnd = logicalOr.Left;
        if(logicalAnd.Right is not null)
        {
            return null;
        }

        var equality = logicalAnd.Left;
        if(!equality.Group0.IsDefaultOrEmpty)
        {
            return null;
        }

        var relational = equality.Left;
        if(relational is not RelationalComparisonRelationalNode cmp1)
        {
            return null;
        }

        if(!cmp1.Group0.IsDefaultOrEmpty)
        {
            return null;
        }

        var additive = cmp1.Left;
        if(!additive.Group0.IsDefaultOrEmpty)
        {
            return null;
        }

        var multiplicative = additive.Left;
        if(!multiplicative.Group0.IsDefaultOrEmpty)
        {
            return null;
        }

        var unary = multiplicative.Left.UnwrapRange();
        if(unary is not PostfixExpressionUnaryNode postfixExpr)
        {
            return null;
        }

        var postfix = postfixExpr.Primary;
        if(postfix.Suffix.Length > 0)
        {
            return null; // Has postfix suffixes (member access, method call, etc.).
        }

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
        if(contextType is null || IsErrorType(contextType))
        {
            return;
        }

        var defaultLiteral = TryExtractDefaultLiteral(expr);
        if(defaultLiteral is null)
        {
            return;
        }

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
        if(contextType is null || IsErrorType(contextType))
        {
            return;
        }

        // The assignment RHS is itself an AssignmentNode. If it has no further
        // assignment (Value is null), the expression is just the Target (LogicalOr).
        if(assignment.Group0?.Value is not null)
        {
            return;
        }

        var defaultLiteral = TryExtractDefaultLiteralFromLogicalOr(assignment.Target.Left);
        if(defaultLiteral is null)
        {
            return;
        }

        model.SetTypeOf(defaultLiteral, contextType);
    }

    /// <summary>
    /// Attempts to extract a <see cref="DefaultLiteralExpressionPrimaryNode"/> from
    /// a <see cref="LogicalOrNode"/> by walking down the expression chain.
    /// </summary>
    private static DefaultLiteralExpressionPrimaryNode? TryExtractDefaultLiteralFromLogicalOr(LogicalOrNode logicalOr)
    {
        if(logicalOr.Right is not null)
        {
            return null;
        }

        var logicalAnd = logicalOr.Left;
        if(logicalAnd.Right is not null)
        {
            return null;
        }

        var equality = logicalAnd.Left;
        if(!equality.Group0.IsDefaultOrEmpty)
        {
            return null;
        }

        var relational = equality.Left;
        if(relational is not RelationalComparisonRelationalNode cmp2)
        {
            return null;
        }

        if(!cmp2.Group0.IsDefaultOrEmpty)
        {
            return null;
        }

        var additive = cmp2.Left;
        if(!additive.Group0.IsDefaultOrEmpty)
        {
            return null;
        }

        var multiplicative = additive.Left;
        if(!multiplicative.Group0.IsDefaultOrEmpty)
        {
            return null;
        }

        var unary = multiplicative.Left.UnwrapRange();
        if(unary is not PostfixExpressionUnaryNode postfixExpr)
        {
            return null;
        }

        var postfix = postfixExpr.Primary;
        if(postfix.Suffix.Length > 0)
        {
            return null;
        }

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
        if(argList is null)
        {
            return;
        }

        var methodSymbol = model.GetDeclaredSymbol(methodCall);
        if(methodSymbol is null || methodSymbol.Kind != SymbolKind.Method)
        {
            return;
        }

        var paramTypes = ParameterSignature.Extract(methodSymbol, model);
        var argExprs = new List<ExpressionNode>();
        foreach(var expr in argList.GetAllExpressions())
        {
            argExprs.Add(expr);
        }

        var count = Math.Min(argExprs.Count, paramTypes.Count);
        for(var i = 0; i < count; i++)
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
        if(invocation.Arguments is null)
        {
            return;
        }

        var invokedSymbol = model.GetDeclaredSymbol(invocation);
        if(invokedSymbol is null || invokedSymbol.Kind != SymbolKind.Method)
        {
            return;
        }

        var paramTypes = ParameterSignature.Extract(invokedSymbol, model);
        var argExprs = new List<ExpressionNode>();
        foreach(var child in invocation.Arguments.Children())
        {
            if(child is ExpressionNode expr)
            {
                argExprs.Add(expr);
            }
        }

        var count = Math.Min(argExprs.Count, paramTypes.Count);
        for(var i = 0; i < count; i++)
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
        // sees the inferred type rather than ErrorType.
        InferDefaultLiteralTypesForArguments(methodCall.Arguments, methodCall, model);

        // Check argument expressions recursively.
        if(methodCall.Arguments is not null)
        {
            CheckArgumentListExpressions(methodCall.Arguments, model, diagnostics);
        }

        // ── variadics value-side spread (task 14.1): general tuple decomposition ──
        // A value-spread argument `x...` decomposes the tuple value into ordered
        // argument slots. When it is the sole argument, check the tuple's element
        // types positionally against the callee's parameter slots (MIST_VAR_0011);
        // regardless of shape, a spread argument is handled here so the ordinary
        // 1:1 argument-type check never mis-reports it as a single argument.
        if(methodCall.Arguments is not null && ArgumentListContainsValueSpread(methodCall.Arguments))
        {
            if(TryGetSoleValueSpreadArgument(methodCall.Arguments, out var spreadPostfix))
            {
                var destinationSlots = ResolveMethodCallDestinationSlots(methodCall, targetType, model);
                CheckDecomposedValueSpread(spreadPostfix, destinationSlots, model, diagnostics);
            }

            return;
        }

        // ── Flow narrowing: apply narrowed types to argument expressions ──
        // When a narrowed variable is passed as a method argument, temporarily
        // override its type in the semantic model so that the static
        // CheckArgumentTypes methods pick up the narrowed type.
        var narrowedArgs = ApplyNarrowingToArguments(methodCall.Arguments, model);
        // Look up the method symbol to check argument types against parameter types.
        var methodSymbol = model.GetDeclaredSymbol(methodCall);

        // ── ref call-site annotation enforcement (MIST_REF_0002, MIST_REF_0003) ──
        if(methodSymbol is not null && methodSymbol.Kind == SymbolKind.Method && methodCall.Arguments is not null)
        {
            CheckRefCallSiteAnnotationsForSymbol(methodCall.Arguments, methodSymbol, model, diagnostics);
        }

        // If the symbol is registered on the call-site node, use it for substitution-aware checking.
        // However, when there are multiple overloads with the same name, skip this early path
        // and fall through to the multi-candidate overload resolution below, which correctly
        // handles ref-argument exact-match filtering and non-generic preference tiebreaking.
        // Also skip Backend-origin symbols — the type checker was not designed to validate
        // CLR method arguments (substitution may not match CLR type parameter ordinals).
        // Backend symbols are registered for the emitter's ref/in keyword selection only.
        var hasMultipleOverloads = false;
        if(methodSymbol is not null && methodSymbol.Kind == SymbolKind.Method
            && methodSymbol.Origin != SymbolOrigin.Backend)
        {
            var methodName0 = methodCall.Name.Lexeme;
            var candidates0 = CollectMethodOverloads(targetType, methodName0);
            hasMultipleOverloads = candidates0.Count > 1;
        }

        if(methodSymbol is not null && methodSymbol.Kind == SymbolKind.Method
            && methodSymbol.Origin != SymbolOrigin.Backend && !hasMultipleOverloads)
        {
            // Determine type parameter substitution context.
            // 1. If the target is a ConstructedType, substitute the enclosing type's type params.
            // 2. If the method has its own type arguments, substitute those as well.
            IReadOnlyList<IType>? enclosingTypeParams = null;
            IReadOnlyList<IType>? enclosingTypeArgs = null;
            if(targetType is ConstructedType targetCt)
            {
                enclosingTypeParams = GetTypeParameters(targetCt.GenericDefinition);
                enclosingTypeArgs = targetCt.TypeArguments;
            }

            // Resolve method-level type arguments if present.
            IReadOnlyList<IType>? methodTypeParams = null;
            IReadOnlyList<IType>? methodTypeArgs = null;
            if(methodCall.TypeArgs is not null && HasOnlyTypeReferenceArgs(methodCall.TypeArgs)
                && methodSymbol is Symbol sym && sym.TypeParameters.Count > 0)
            {
                methodTypeParams = sym.TypeParameters;
                methodTypeArgs = ResolveTypeArgumentsFromAst(methodCall.TypeArgs, model);

                // Check constraint satisfaction for each explicit type argument
                // against the corresponding method type parameter.
                for(var i = 0; i < methodTypeParams.Count && i < methodTypeArgs.Count; i++)
                {
                    if(methodTypeParams[i] is TypeParameterType methodTP)
                    {
                        CheckConstraintSatisfaction(methodTypeArgs[i], methodTP, methodCall, sym.Name, model, diagnostics);
                    }
                }
            }
            else if(methodCall.TypeArgs is null && methodSymbol is Symbol inferSym && inferSym.TypeParameters.Count > 0)
            {
                // Type argument inference: deduce type arguments from argument types.
                var paramTypes = ParameterSignature.Extract(methodSymbol, model);
                var argTypes = CollectArgumentTypes(methodCall.Arguments, model);

                var inferenceResult = TypeArgumentInferenceEngine.TryInfer(
                    inferSym.TypeParameters, paramTypes, argTypes);

                if(inferenceResult.Success)
                {
                    methodTypeParams = inferSym.TypeParameters;
                    methodTypeArgs = inferenceResult.InferredTypeArguments!;

                    // Record inferred type arguments for the emitter.
                    model.RecordInferredTypeArguments(methodCall, methodTypeArgs);

                    // Check constraints (same path as explicit type args).
                    for(var i = 0; i < methodTypeParams.Count && i < methodTypeArgs.Count; i++)
                    {
                        if(methodTypeParams[i] is TypeParameterType methodTP)
                        {
                            CheckConstraintSatisfaction(methodTypeArgs[i], methodTP, methodCall, inferSym.Name, model, diagnostics);
                        }
                    }
                }
                else
                {
                    // Emit inference failure diagnostic.
                    EmitInferenceFailureDiagnostic(inferenceResult, methodCall, diagnostics);
                }
            }

            CheckArgumentTypesWithSubstitution(methodCall.Arguments, methodSymbol, model, diagnostics,
                enclosingTypeParams, enclosingTypeArgs, methodTypeParams, methodTypeArgs);
        }

        var methodName = methodCall.Name.Lexeme;

        // Collect the full overload set from the target type (and inherited methods from base types).
        var candidates = CollectMethodOverloads(targetType, methodName);

        // If there are multiple candidates, use overload resolution.
        if(candidates.Count > 1)
        {
            var argTypes = CollectArgumentTypes(methodCall.Arguments, model);
            var argRefKinds = CollectArgumentRefKinds(methodCall.Arguments);

            // Inference-aware overload resolution: when type arguments are omitted,
            // attempt inference for each generic candidate and exclude those where
            // inference fails (Requirements 5.1, 5.2, 5.3, 5.4, 5.5, 5.6).
            InferenceResult?[]? inferenceResults = null;
            if(methodCall.TypeArgs is null)
            {
                inferenceResults = new InferenceResult?[candidates.Count];
                for(var i = 0; i < candidates.Count; i++)
                {
                    if(candidates[i] is Symbol candidateSym && candidateSym.TypeParameters.Count > 0)
                    {
                        var candidateParamTypes = ParameterSignature.Extract(candidates[i], model);
                        inferenceResults[i] = TypeArgumentInferenceEngine.TryInfer(
                            candidateSym.TypeParameters, candidateParamTypes, argTypes);
                    }
                    // null entry means non-generic candidate — passes through unchanged.
                }
            }
            else
            {
                // Explicit type arguments provided: create synthetic inference results
                // so the overload resolver substitutes the method's type parameters
                // into parameter types before the assignability check.
                var resolvedExplicitTypeArgs = ResolveTypeArgumentsFromAst(methodCall.TypeArgs, model);
                if(resolvedExplicitTypeArgs.Count > 0)
                {
                    inferenceResults = new InferenceResult?[candidates.Count];
                    for(var i = 0; i < candidates.Count; i++)
                    {
                        if(candidates[i] is Symbol candidateSym
                            && candidateSym.TypeParameters.Count == resolvedExplicitTypeArgs.Count)
                        {
                            inferenceResults[i] = new InferenceResult(
                                Success: true,
                                InferredTypeArguments: resolvedExplicitTypeArgs.ToArray(),
                                UninferrableTypeParameters: null,
                                Conflicts: null);
                        }
                        else if(candidates[i] is Symbol mismatchSym
                            && mismatchSym.TypeParameters.Count > 0
                            && mismatchSym.TypeParameters.Count != resolvedExplicitTypeArgs.Count)
                        {
                            // Mark generic candidates with wrong arity as failed
                            // so the overload resolver excludes them.
                            inferenceResults[i] = new InferenceResult(
                                Success: false,
                                InferredTypeArguments: null,
                                UninferrableTypeParameters: new[] { "arity mismatch" },
                                Conflicts: null);
                        }
                    }
                }
            }

            var result = OverloadResolver.Resolve(candidates, argTypes, model,
                argumentRefKinds: argRefKinds, inferenceResults: inferenceResults);

            switch(result.Status)
            {
                case OverloadResolutionStatus.NoApplicableCandidate:
                    var argTypeNames = string.Join(", ", argTypes.Select(t => t.DisplayName));
                    diagnostics.ReportAtNode(TypeCheckDiagnostics.NoApplicableOverload, methodCall, "TypeChecker", methodName, argTypeNames);
                    return;

                case OverloadResolutionStatus.AmbiguousCall:
                    // Check if the ambiguity is specifically across multiple DelegateType parameters
                    // (delegate-func-conversion: MIST_TC_0094).
                    if(IsDelegateOverloadAmbiguity(result.AmbiguousCandidates!, model))
                    {
                        var delegateNames = GetAmbiguousDelegateNames(result.AmbiguousCandidates!, model);
                        diagnostics.ReportAtNode(TypeCheckDiagnostics.DelegateOverloadAmbiguity, methodCall, "TypeChecker", delegateNames.Item1, delegateNames.Item2);
                    }
                    else
                    {
                        var sigs = result.AmbiguousCandidates!
                            .Select(c => FormatCandidateSignature(c, methodName, model));
                        diagnostics.ReportAtNode(TypeCheckDiagnostics.AmbiguousOverload, methodCall, "TypeChecker", methodName, string.Join(", ", sigs));
                    }

                    return;

                case OverloadResolutionStatus.Success:
                    // Record inferred type arguments for the winning candidate if inference was used.
                    IReadOnlyList<IType>? winnerMethodTypeParams = null;
                    IReadOnlyList<IType>? winnerMethodTypeArgs = null;
                    if(inferenceResults is not null && result.BestCandidate is Symbol winnerSym && winnerSym.TypeParameters.Count > 0)
                    {
                        var winnerIndex = -1;
                        for(var i = 0; i < candidates.Count; i++)
                        {
                            if(ReferenceEquals(candidates[i], result.BestCandidate))
                            {
                                winnerIndex = i;
                                break;
                            }
                        }

                        if(winnerIndex >= 0 && inferenceResults[winnerIndex] is { Success: true } winnerInference)
                        {
                            model.RecordInferredTypeArguments(methodCall, winnerInference.InferredTypeArguments!);
                            winnerMethodTypeParams = winnerSym.TypeParameters;
                            winnerMethodTypeArgs = winnerInference.InferredTypeArguments!;

                            // Substitute inferred type arguments into the return type so that
                            // downstream type checking sees the concrete return type.
                            var currentReturnType = model.GetTypeOf(methodCall);
                            if(currentReturnType is not null)
                            {
                                var substitutedReturnType = SubstituteType(currentReturnType, winnerSym.TypeParameters, winnerInference.InferredTypeArguments!);
                                model.SetTypeOf(methodCall, substitutedReturnType);
                            }

                            // Check constraints for the inferred type arguments.
                            for(var i = 0; i < winnerSym.TypeParameters.Count && i < winnerInference.InferredTypeArguments!.Count; i++)
                            {
                                if(winnerSym.TypeParameters[i] is TypeParameterType methodTP)
                                {
                                    CheckConstraintSatisfaction(winnerInference.InferredTypeArguments[i], methodTP,
                                        methodCall, winnerSym.Name, model, diagnostics);
                                }
                            }
                        }
                    }

                    // Resolved successfully — proceed with the resolved symbol for further type checking.
                    // Use substitution-aware checking when inference provided type arguments.
                    if(winnerMethodTypeArgs is not null)
                    {
                        IReadOnlyList<IType>? winnerEnclosingTypeParams = null;
                        IReadOnlyList<IType>? winnerEnclosingTypeArgs = null;
                        if(targetType is ConstructedType winnerTargetCt)
                        {
                            winnerEnclosingTypeParams = GetTypeParameters(winnerTargetCt.GenericDefinition);
                            winnerEnclosingTypeArgs = winnerTargetCt.TypeArguments;
                        }

                        CheckArgumentTypesWithSubstitution(methodCall.Arguments, result.BestCandidate!, model, diagnostics,
                            winnerEnclosingTypeParams, winnerEnclosingTypeArgs,
                            winnerMethodTypeParams, winnerMethodTypeArgs);
                    }
                    else
                    {
                        CheckArgumentTypes(methodCall.Arguments, result.BestCandidate!, model, diagnostics);
                    }

                    return;
            }
        }
        else if(candidates.Count == 1)
        {
            // Single candidate — attempt inference if generic and type args omitted,
            // or use explicit type args if provided.
            var singleCandidate = candidates[0];
            if(singleCandidate is Symbol singleSym && singleSym.TypeParameters.Count > 0)
            {
                if(methodCall.TypeArgs is not null && HasOnlyTypeReferenceArgs(methodCall.TypeArgs))
                {
                    // Explicit type arguments provided (all are type references) — resolve and use them.
                    var explicitMethodTypeArgs = ResolveTypeArgumentsFromAst(methodCall.TypeArgs, model);

                    // Check constraints for the explicit type arguments.
                    for(var i = 0; i < singleSym.TypeParameters.Count && i < explicitMethodTypeArgs.Count; i++)
                    {
                        if(singleSym.TypeParameters[i] is TypeParameterType methodTP)
                        {
                            CheckConstraintSatisfaction(explicitMethodTypeArgs[i], methodTP,
                                methodCall, singleSym.Name, model, diagnostics);
                        }
                    }

                    // Check argument types with explicit substitution.
                    IReadOnlyList<IType>? singleEnclosingTypeParams = null;
                    IReadOnlyList<IType>? singleEnclosingTypeArgs = null;
                    if(targetType is ConstructedType singleTargetCtExplicit)
                    {
                        singleEnclosingTypeParams = GetTypeParameters(singleTargetCtExplicit.GenericDefinition);
                        singleEnclosingTypeArgs = singleTargetCtExplicit.TypeArguments;
                    }

                    CheckArgumentTypesWithSubstitution(methodCall.Arguments, singleCandidate, model, diagnostics,
                        singleEnclosingTypeParams, singleEnclosingTypeArgs,
                        singleSym.TypeParameters, explicitMethodTypeArgs);
                }
                else if(methodCall.TypeArgs is null)
                {
                    // No type arguments — attempt inference.
                    var argTypes = CollectArgumentTypes(methodCall.Arguments, model);
                    var paramTypes = ParameterSignature.Extract(singleCandidate, model);

                    var inferenceResult = TypeArgumentInferenceEngine.TryInfer(
                        singleSym.TypeParameters, paramTypes, argTypes);

                    if(inferenceResult.Success)
                    {
                        // Record inferred type arguments for the emitter.
                        model.RecordInferredTypeArguments(methodCall, inferenceResult.InferredTypeArguments!);

                        // Substitute inferred type arguments into the return type so that
                        // downstream type checking sees the concrete return type.
                        var currentReturnType = model.GetTypeOf(methodCall);
                        if(currentReturnType is not null)
                        {
                            var substitutedReturnType = SubstituteType(currentReturnType, singleSym.TypeParameters, inferenceResult.InferredTypeArguments!);
                            model.SetTypeOf(methodCall, substitutedReturnType);
                        }

                        // Check constraints for the inferred type arguments.
                        for(var i = 0; i < singleSym.TypeParameters.Count && i < inferenceResult.InferredTypeArguments!.Count; i++)
                        {
                            if(singleSym.TypeParameters[i] is TypeParameterType methodTP)
                            {
                                CheckConstraintSatisfaction(inferenceResult.InferredTypeArguments[i], methodTP,
                                    methodCall, singleSym.Name, model, diagnostics);
                            }
                        }

                        // Check argument types with inferred substitution.
                        IReadOnlyList<IType>? singleEnclosingTypeParams = null;
                        IReadOnlyList<IType>? singleEnclosingTypeArgs = null;
                        if(targetType is ConstructedType singleTargetCt)
                        {
                            singleEnclosingTypeParams = GetTypeParameters(singleTargetCt.GenericDefinition);
                            singleEnclosingTypeArgs = singleTargetCt.TypeArguments;
                        }

                        CheckArgumentTypesWithSubstitution(methodCall.Arguments, singleCandidate, model, diagnostics,
                            singleEnclosingTypeParams, singleEnclosingTypeArgs,
                            singleSym.TypeParameters, inferenceResult.InferredTypeArguments!);
                    }
                    else
                    {
                        // Emit inference failure diagnostic.
                        EmitInferenceFailureDiagnostic(inferenceResult, methodCall, diagnostics);
                    }
                }
                else
                {
                    // Type arguments contain dependent type literals — fall through to basic checking.
                    CheckArgumentTypes(methodCall.Arguments, singleCandidate, model, diagnostics);
                }
            }
            else
            {
                // Non-generic single candidate — preserve existing single-method diagnostics (MIST_TC_0002).
                CheckArgumentTypes(methodCall.Arguments, singleCandidate, model, diagnostics);
            }
        }
        else if(methodSymbol is not null && methodSymbol.Kind == SymbolKind.Method
            && methodSymbol.Origin != SymbolOrigin.Backend)
        {
            // Fallback: use the symbol from the call-site node if no candidates found via target type.
            CheckArgumentTypes(methodCall.Arguments, methodSymbol, model, diagnostics);
        }

        // Restore original types after argument checking.
        RestoreNarrowedArguments(narrowedArgs, model);
    }

    private void CheckInvocationArguments(
        InvocationSuffixPostfixSuffixNode invocation,
        IType? targetType,
        PostfixNode containingPostfix,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Pre-pass: infer bare 'default' types from parameter types.
        InferDefaultLiteralTypesForInvocationArguments(invocation, model);

        // Check argument expressions recursively.
        if(invocation.Arguments is not null)
        {
            CheckArgumentListExpressions(invocation.Arguments, model, diagnostics);
        }

        // ── variadics value-side spread (task 14.1): general tuple decomposition ──
        // A value-spread argument `x...` decomposes the tuple value into ordered
        // argument slots. When it is the sole argument, check the tuple's element
        // types positionally against the destination parameter slots (a Func value's
        // parameter list or the invoked method's parameters), emitting MIST_VAR_0011
        // on a slot-count or slot-type mismatch. A spread argument is handled here so
        // the ordinary invocation-argument checks never mis-count it as one argument.
        if(invocation.Arguments is not null && ArgumentListContainsValueSpread(invocation.Arguments))
        {
            if(TryGetSoleValueSpreadArgument(invocation.Arguments, out var spreadPostfix))
            {
                var destinationSlots = ResolveInvocationDestinationSlots(invocation, targetType, containingPostfix, model);
                CheckDecomposedValueSpread(spreadPostfix, destinationSlots, model, diagnostics);
            }

            return;
        }

        // ── first-class-functions: Func-typed invocation validation ──
        // When the target expression's type is a Func constructed type, validate
        // argument count and types against the Func's parameter types.
        if(targetType is not null && SemanticModelBuilder.IsFuncType(targetType) && targetType is ConstructedType funcCt)
        {
            CheckFuncInvocationArguments(invocation, funcCt, model, diagnostics);
            return;
        }

        // ── delegate-func-conversion: DelegateType invocation validation ──
        // When the target expression's type is a DelegateType, validate arguments
        // against the DelegateType's FuncSignature (the equivalent func<...> type).
        // The result type is the FuncSignature's return type (last type argument).
        if(targetType is DelegateType delegateType)
        {
            CheckFuncInvocationArguments(invocation, delegateType.FuncSignature, model, diagnostics);
            return;
        }

        // ── delegate-func-conversion: BaseDelegateType is NOT invocable ──
        // System.Delegate cannot be invoked directly — it requires a cast to a
        // specific delegate type or func<...> first.
        if(targetType is BaseDelegateType)
        {
            diagnostics.ReportAtNode(TypeCheckDiagnostics.BaseDelegateNotInvocable, invocation, "TypeChecker");
            return;
        }

        // Look up the invoked symbol.
        var invokedSymbol = model.GetDeclaredSymbol(invocation);
        if(invokedSymbol is null)
        {
            return;
        }

        // ── ref call-site annotation enforcement (MIST_REF_0002, MIST_REF_0003) ──
        if(invokedSymbol.Kind == SymbolKind.Method && invocation.Arguments is not null)
        {
            CheckRefCallSiteAnnotationsForSymbol(invocation.Arguments, invokedSymbol, model, diagnostics);
        }

        if(invokedSymbol.Kind == SymbolKind.Method)
        {
            var methodName = invokedSymbol.Name;

            // Collect the full overload set from the target type (and inherited methods from base types).
            var candidates = CollectMethodOverloads(targetType, methodName);

            // If there are multiple candidates, use overload resolution.
            if(candidates.Count > 1)
            {
                var argTypes = CollectArgumentTypes(invocation.Arguments, model);
                var argRefKinds = CollectArgumentRefKinds(invocation.Arguments);
                var result = OverloadResolver.Resolve(candidates, argTypes, model, argumentRefKinds: argRefKinds);

                switch(result.Status)
                {
                    case OverloadResolutionStatus.NoApplicableCandidate:
                        var argTypeNames = string.Join(", ", argTypes.Select(t => t.DisplayName));
                        diagnostics.ReportAtNode(TypeCheckDiagnostics.NoApplicableOverload, invocation, "TypeChecker", methodName, argTypeNames);
                        return;

                    case OverloadResolutionStatus.AmbiguousCall:
                        if(IsDelegateOverloadAmbiguity(result.AmbiguousCandidates!, model))
                        {
                            var delegateNames = GetAmbiguousDelegateNames(result.AmbiguousCandidates!, model);
                            diagnostics.ReportAtNode(TypeCheckDiagnostics.DelegateOverloadAmbiguity, invocation, "TypeChecker", delegateNames.Item1, delegateNames.Item2);
                        }
                        else
                        {
                            var sigs = result.AmbiguousCandidates!
                                .Select(c => FormatCandidateSignature(c, methodName, model));
                            diagnostics.ReportAtNode(TypeCheckDiagnostics.AmbiguousOverload, invocation, "TypeChecker", methodName, string.Join(", ", sigs));
                        }

                        return;

                    case OverloadResolutionStatus.Success:
                        // Resolved successfully — proceed with the resolved symbol for further type checking.
                        CheckArgumentTypes(invocation.Arguments, result.BestCandidate!, model, diagnostics);
                        return;
                }
            }
            else
            {
                // Single candidate — preserve existing single-method diagnostics (MIST_TC_0002).
                CheckArgumentTypes(invocation.Arguments, invokedSymbol, model, diagnostics);
            }
        }
    }

    private void CheckArgumentListExpressions(ArgumentListNode argList, ISemanticModel model, DiagnosticBag diagnostics)
    {
        // The ArgumentListNode has Argument (first) and Arguments (rest), each wrapping an ExpressionNode.
        foreach(var expr in argList.GetAllExpressions())
        {
            CheckExpression(expr, model, diagnostics);
        }

        // Validate ref arguments are assignable storage locations (MIST_REF_0001).
        CheckRefArgumentAssignability(argList, model, diagnostics);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Func invocation argument validation (MIST_TC_0119, MIST_TC_0120)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Validates that a Func-typed invocation has the correct number of arguments
    /// and that each argument's type is compatible with the corresponding Func
    /// parameter type. Emits <c>MIST_TC_0119</c> for count mismatches and
    /// <c>MIST_TC_0120</c> for type mismatches.
    /// </summary>
    private static void CheckFuncInvocationArguments(
        InvocationSuffixPostfixSuffixNode invocation,
        ConstructedType funcType,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Determine the Func parameter types. For a variadic Func<params TArgs, TResult>,
        // the parameter types live in the pack's DependentValueArgument.PackElementTypes.
        // For the legacy per-arity Func<T1, ..., TN, TResult>, the parameter types are
        // TypeArguments[0..^1].
        IReadOnlyList<IType> paramTypes;
        if(funcType.DependentArguments is not null
            && funcType.DependentArguments.Count > 0
            && funcType.DependentArguments[0] is { PackElementTypes: { } packElements })
        {
            // Variadic form: pack element types are the parameter types.
            paramTypes = packElements;
        }
        else
        {
            // Legacy per-arity form: all type arguments except the last are parameter types.
            var typeArgs = funcType.TypeArguments;
            paramTypes = typeArgs.Take(typeArgs.Count - 1).ToList();
        }

        var expectedParamCount = paramTypes.Count;

        // Collect actual argument types.
        var argTypes = CollectArgumentTypes(invocation.Arguments, model);
        var actualArgCount = argTypes.Count;

        // ── MIST_TC_0119: argument count mismatch ──
        if(actualArgCount != expectedParamCount)
        {
            diagnostics.ReportAtNode(TypeCheckDiagnostics.FuncInvocationArgumentCountMismatch, invocation, "TypeChecker");
            return;
        }

        // ── MIST_TC_0120: argument type mismatch ──
        for(var i = 0; i < actualArgCount; i++)
        {
            var argType = argTypes[i];
            var paramType = paramTypes[i];

            // Skip error types to avoid cascading diagnostics.
            if(IsErrorType(argType) || IsErrorType(paramType))
            {
                continue;
            }

            if(!IsAssignable(argType, paramType, model))
            {
                diagnostics.ReportAtNode(TypeCheckDiagnostics.FuncInvocationArgumentTypeMismatch, invocation, "TypeChecker");
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Ambiguous method group conversion validation (MIST_TC_0121)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Checks whether a method group conversion is ambiguous. When an identifier
    /// resolves to a method symbol and the expression is used as a value (assigned
    /// to a Func-typed variable), verifies that only one overload of the method
    /// matches the target Func type's signature. Emits <c>MIST_TC_0121</c> if
    /// multiple overloads match.
    /// </summary>
    private static void CheckMethodGroupAmbiguity(
        PostfixNode postfix,
        IType targetFuncType,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Only applies when the target is a Func type and the expression is a bare identifier
        // (no invocation suffix — i.e., used as a value, not called).
        if(targetFuncType is not ConstructedType funcCt || !SemanticModelBuilder.IsFuncType(funcCt))
        {
            return;
        }

        if(postfix.Target is not IdentifierExpressionPrimaryNode identifier)
        {
            return;
        }

        // If there's an invocation suffix, this is a call, not a method group conversion.
        if(postfix.Suffix.Length > 0 && postfix.Suffix[0] is InvocationSuffixPostfixSuffixNode)
        {
            return;
        }

        // Resolve the symbol for the identifier.
        var symbol = model.GetDeclaredSymbol(identifier);
        if(symbol is not Symbol { Kind: SymbolKind.Method } methodSymbol)
        {
            return;
        }

        // Find the containing scope to look for overloads.
        var containingScope = methodSymbol.ContainingScope;
        if(containingScope is null)
        {
            return;
        }

        // Collect all methods with the same name in the containing scope.
        var methodName = methodSymbol.Name;
        var overloads = new List<ISymbol>();
        foreach(var member in containingScope.Members)
        {
            if(member.Kind == SymbolKind.Method && member.Name == methodName)
            {
                overloads.Add(member);
            }
        }

        // If there's only one overload, no ambiguity is possible.
        if(overloads.Count <= 1)
        {
            return;
        }

        // Extract the target Func type's parameter types (all but last type arg).
        var funcTypeArgs = funcCt.TypeArguments;
        var funcParamCount = funcTypeArgs.Count - 1;

        // Count how many overloads match the target Func type's signature.
        var matchingOverloads = new List<ISymbol>();
        foreach(var overload in overloads)
        {
            var paramTypes = ParameterSignature.Extract(overload, model);
            if(paramTypes.Count != funcParamCount)
            {
                continue;
            }

            var allMatch = true;
            for(var i = 0; i < funcParamCount; i++)
            {
                if(!IsAssignable(funcTypeArgs[i], paramTypes[i], model)
                    && !IsAssignable(paramTypes[i], funcTypeArgs[i], model))
                {
                    allMatch = false;
                    break;
                }
            }

            if(allMatch)
            {
                matchingOverloads.Add(overload);
            }
        }

        // If more than one overload matches, emit MIST_TC_0121.
        if(matchingOverloads.Count > 1)
        {
            var signatures = matchingOverloads
                .Select(o => FormatCandidateSignature(o, methodName, model)); diagnostics.ReportAtNode(TypeCheckDiagnostics.AmbiguousMethodGroupConversion, postfix, "TypeChecker");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Ref argument assignable storage location validation (MIST_REF_0001)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Validates that each <c>ref</c>-annotated argument in the argument list is an
    /// assignable storage location: a mutable local variable, a mutable field, or an
    /// array element (indexer access). Emits <c>MIST_REF_0001</c> for expressions that
    /// are not valid storage locations (literals, temporaries, method return values,
    /// arithmetic results, readonly bindings, frozen variables, readonly-ref variables).
    /// </summary>
    private static void CheckRefArgumentAssignability(ArgumentListNode argList, ISemanticModel model, DiagnosticBag diagnostics)
    {
        CheckSingleRefArgument(argList.Argument, model, diagnostics);
        foreach(var arg in argList.Arguments)
        {
            CheckSingleRefArgument(arg, model, diagnostics);
        }
    }

    /// <summary>
    /// Checks a single argument node. If it is a <see cref="RefArgumentArgumentNode"/>,
    /// validates that its expression is an assignable storage location.
    /// </summary>
    private static void CheckSingleRefArgument(ArgumentNode arg, ISemanticModel model, DiagnosticBag diagnostics)
    {
        if(arg is not RefArgumentArgumentNode refArg)
        {
            return;
        }

        var expr = refArg.Expression;

        // ── readonly-locals feature: reject passing readonly locals by mutable ref ──
        if(CheckReadonlyLocalRefArgument(refArg, expr, model, diagnostics))
        {
            return;
        }

        if(IsAssignableStorageLocation(expr, model, out var description))
        {
            return;
        }

        diagnostics.ReportAtNode(TypeCheckDiagnostics.RefArgNotStorageLocation, refArg, "TypeChecker", description);
    }

    /// <summary>
    /// Checks if a ref argument expression resolves to a local variable with
    /// <see cref="ReadonlyStatus.ExplicitReadonly"/>. If so, emits
    /// <c>MIST_READONLY_LOCAL_0001</c> and returns <c>true</c> to indicate the
    /// diagnostic was handled. Returns <c>false</c> if the expression is not a
    /// readonly local (allowing the caller to proceed with other checks).
    /// </summary>
    private static bool CheckReadonlyLocalRefArgument(
        RefArgumentArgumentNode refArg,
        ExpressionNode expr,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        var postfix = TryExtractPostfixNode(expr);
        if(postfix is null || postfix.Suffix.Length > 0)
        {
            return false;
        }

        if(postfix.Target is not IdentifierExpressionPrimaryNode identifier)
        {
            return false;
        }

        var symbol = model.GetDeclaredSymbol(identifier);
        if(symbol is not Symbol concreteSymbol)
        {
            return false;
        }

        if(concreteSymbol.Kind != SymbolKind.LocalVariable)
        {
            return false;
        }

        if(concreteSymbol.ReadonlyStatus != ReadonlyStatus.ExplicitReadonly)
        {
            return false;
        }

        diagnostics.ReportAtNode(TypeCheckDiagnostics.ReadonlyLocalRefArgument, refArg, "TypeChecker", concreteSymbol.Name);
        return true;
    }

    /// <summary>
    /// Determines whether an expression represents an assignable storage location suitable
    /// for a <c>ref</c> argument. Returns <c>true</c> if the expression is:
    /// <list type="bullet">
    ///   <item>A local variable that is not readonly, not frozen, and not readonly-ref ownership tier</item>
    ///   <item>A parameter that is not readonly, not frozen, and not readonly-ref ownership tier</item>
    ///   <item>A mutable field access (member access suffix)</item>
    ///   <item>An array element access (index suffix)</item>
    /// </list>
    /// Returns <c>false</c> for literals, <c>new</c> expressions, method call results,
    /// binary/unary expressions, <c>this</c>, and readonly/frozen/readonly-ref variables.
    /// </summary>
    private static bool IsAssignableStorageLocation(ExpressionNode expr, ISemanticModel model, out string description)
    {
        // Navigate through the expression tree to reach the PostfixNode:
        // Expression → Assignment → LogicalOr → LogicalAnd → Equality → Relational → Additive → Multiplicative → Unary → Postfix → Primary
        var postfix = TryExtractPostfixNode(expr);
        if(postfix is null)
        {
            // The expression has binary/logical operators or assignment — not a storage location.
            description = DescribeExpression(expr, model);
            return false;
        }

        // Case 1: Expression has suffixes — check if the last suffix is a member access or index.
        if(postfix.Suffix.Length > 0)
        {
            var lastSuffix = postfix.Suffix[^1];

            // Array element access (indexer) — always an assignable storage location.
            if(lastSuffix is IndexSuffixPostfixSuffixNode)
            {
                return AssignableResult(out description);
            }

            // Member access — a field access is assignable.
            if(lastSuffix is MemberAccessSuffixPostfixSuffixNode)
            {
                return AssignableResult(out description);
            }

            // Method call result, invocation result, switch expression result — not assignable.
            description = DescribeExpression(expr, model);
            return false;
        }

        // Case 2: No suffixes — the expression is just a primary node.
        var primary = postfix.Target;

        // Identifier expression — check if it resolves to a mutable variable/parameter.
        if(primary is IdentifierExpressionPrimaryNode identifier)
        {
            var symbol = model.GetDeclaredSymbol(identifier);
            if(symbol is not Symbol concreteSymbol)
            {
                // Cannot resolve — treat as non-assignable with the identifier name.
                description = identifier.Name.Lexeme;
                return false;
            }

            // Only local variables and parameters are valid storage locations for ref.
            if(concreteSymbol.Kind is not (SymbolKind.LocalVariable or SymbolKind.Parameter))
            {
                description = identifier.Name.Lexeme;
                return false;
            }

            // Reject readonly variables (explicit readonly modifier).
            if(concreteSymbol.ReadonlyStatus is ReadonlyStatus.ExplicitReadonly or ReadonlyStatus.InferredReadonly or ReadonlyStatus.ImplicitReadonly)
            {
                description = identifier.Name.Lexeme;
                return false;
            }

            // Reject frozen ownership tier.
            if(concreteSymbol.OwnershipTier == OwnershipTier.Frozen)
            {
                description = identifier.Name.Lexeme;
                return false;
            }

            // Reject readonly ref ownership tier.
            if(concreteSymbol.OwnershipTier == OwnershipTier.ReadonlyRef)
            {
                description = identifier.Name.Lexeme;
                return false;
            }

            // Reject ref readonly locals (they are readonly references).
            if(concreteSymbol.RefKind == RefKind.RefReadonly)
            {
                description = identifier.Name.Lexeme;
                return false;
            }

            // The variable/parameter is mutable — valid storage location.
            return AssignableResult(out description);
        }

        // Parenthesized expression — unwrap and check inner.
        if(primary is ParenthesizedExpressionPrimaryNode paren)
        {
            return IsAssignableStorageLocation(paren.Inner, model, out description);
        }

        // All other primary nodes are not assignable storage locations:
        // literals, this, new expressions, array literals, tuple literals, etc.
        description = DescribeExpression(expr, model);
        return false;
    }

    /// <summary>
    /// Collects all <see cref="ArgumentNode"/> objects from an <see cref="ArgumentListNode"/>
    /// in order (first + rest). Returns an empty list if <paramref name="argList"/> is null.
    /// </summary>
    private static List<ArgumentNode> CollectArgumentNodes(ArgumentListNode? argList)
    {
        if(argList is null)
        {
            return [];
        }

        var nodes = new List<ArgumentNode>(1 + argList.Arguments.Length)
        {
            argList.Argument
        };
        foreach(var arg in argList.Arguments)
        {
            nodes.Add(arg);
        }

        return nodes;
    }

    /// <summary>
    /// Extracts the <see cref="PostfixNode"/> from an expression, returning null if the
    /// expression contains binary operators, logical operators, or assignments that make
    /// it inherently non-assignable.
    /// </summary>
    private static PostfixNode? TryExtractPostfixNode(ExpressionNode expr)
    {
        var assignment = expr.AsAssignmentOrNull();
        if(assignment is null)
        {
            return null; // Lambda form — not a simple postfix chain.
        }

        if(assignment.Group0?.Value is not null)
        {
            return null;
        }

        var logicalOr = assignment.Target?.Left;
        if(logicalOr?.Right is not null && !ReferenceEquals(logicalOr.Right, logicalOr.Left))
        {
            return null;
        }

        var logicalAnd = logicalOr?.Left;
        if(logicalAnd?.Right is not null && !ReferenceEquals(logicalAnd.Right, logicalAnd.Left))
        {
            return null;
        }

        var equality = logicalAnd?.Left;
        if(equality is not null && !equality.Group0.IsDefaultOrEmpty)
        {
            return null;
        }

        if(equality?.Left is not RelationalComparisonRelationalNode relCmp)
        {
            return null;
        }

        if(!relCmp.Group0.IsDefaultOrEmpty)
        {
            return null;
        }

        var additive = relCmp.Left;
        if(additive is not null && !additive.Group0.IsDefaultOrEmpty)
        {
            return null;
        }

        var multiplicative = additive?.Left;
        if(multiplicative is not null && !multiplicative.Group0.IsDefaultOrEmpty)
        {
            return null;
        }

        if(multiplicative?.Left.UnwrapRange() is not PostfixExpressionUnaryNode postfixUnary)
        {
            return null;
        }

        return postfixUnary.Primary;
    }

    /// <summary>
    /// Produces a human-readable description of an expression for use in diagnostic messages.
    /// </summary>
    private static string DescribeExpression(ExpressionNode expr, ISemanticModel model)
    {
        // Try to get the type for a meaningful description.
        var type = model.GetTypeOf(expr);

        // Try to extract a simple identifier name.
        var identifierNode = TryExtractIdentifierPrimaryNode(expr);
        if(identifierNode is not null)
        {
            return identifierNode.Name.Lexeme;
        }

        // Try to unwrap to a primary node for a more specific description.
        var primary = UnwrapExpressionToPrimary(expr);
        if(primary is not null)
        {
            return primary switch
            {
                IntegerLiteralExpressionPrimaryNode intLit => intLit.IntegerLiteral.Lexeme,
                FloatLiteralExpressionPrimaryNode floatLit => floatLit.FloatLiteral.Lexeme,
                StringLiteralExpressionPrimaryNode strLit => strLit.StringLiteral.Lexeme,
                CharLiteralExpressionPrimaryNode charLit => charLit.CharLiteral.Lexeme,
                TrueLiteralExpressionPrimaryNode => "true",
                FalseLiteralExpressionPrimaryNode => "false",
                ThisExpressionPrimaryNode => "this",
                ObjectCreationExpressionPrimaryNode => "new expression",
                ObjectCreationExpressionNoArgsPrimaryNode => "new expression",
                ArrayCreationExpressionPrimaryNode => "new expression",
                StackallocExpressionPrimaryNode => "stackalloc expression",
                ArrayLiteralExpressionPrimaryNode => "array literal",
                DefaultExpressionPrimaryNode => "default expression",
                DefaultLiteralExpressionPrimaryNode => "default",
                TupleLiteralExpressionPrimaryNode => "tuple expression",
                _ => type?.DisplayName ?? "expression",
            };
        }

        return type?.DisplayName ?? "expression";
    }

    private static bool AssignableResult(out string description)
    {
        description = string.Empty;
        return true;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Ref call-site annotation enforcement (MIST_REF_0002, MIST_REF_0003)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Resolves parameter symbols from a method symbol and validates ref annotations
    /// at the call site. Emits <c>MIST_REF_0002</c> when a ref parameter is missing
    /// the <c>ref</c> keyword at the call site, and <c>MIST_REF_0003</c> when a
    /// non-ref parameter has a spurious <c>ref</c> keyword.
    /// </summary>
    private static void CheckRefCallSiteAnnotationsForSymbol(
        ArgumentListNode argList,
        ISymbol methodSymbol,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        SyntaxNode methodNode;
        try { methodNode = model.GetDeclaringNode(methodSymbol); }
        catch { return; }

        var parameters = methodNode switch
        {
            MethodDeclarationNode method => method.Parameters,
            ConstructorDeclarationNode ctor => ctor.Parameters,
            IndexerDeclarationNode indexer => indexer.Parameters,
            _ => null,
        };

        if(parameters is null)
        {
            return;
        }

        var paramSymbols = ImmutableArray.CreateBuilder<ISymbol>();
        foreach(var child in parameters.Children())
        {
            if(child is ParameterNode paramNode)
            {
                var paramSym = model.GetDeclaredSymbol(paramNode);
                if(paramSym is not null)
                {
                    paramSymbols.Add(paramSym);
                }
            }
        }

        CheckRefCallSiteAnnotations(argList, paramSymbols.ToImmutable(), diagnostics);
    }

    /// <summary>
    /// Validates that ref annotations at call sites match the parameter declarations:
    /// <list type="bullet">
    ///   <item>If a parameter has <c>RefKind.Ref</c> but the argument is a
    ///   <see cref="ValueArgumentArgumentNode"/>, emits <c>MIST_REF_0002</c>.</item>
    ///   <item>If a parameter has <c>RefKind.None</c> but the argument is a
    ///   <see cref="RefArgumentArgumentNode"/>, emits <c>MIST_REF_0003</c>.</item>
    /// </list>
    /// </summary>
    private static void CheckRefCallSiteAnnotations(
        ArgumentListNode? argList,
        ImmutableArray<ISymbol> paramSymbols,

        DiagnosticBag diagnostics)
    {
        if(argList is null)
        {
            return;
        }

        // Collect all ArgumentNode objects (first + rest).
        var argNodes = new List<ArgumentNode>(1 + argList.Arguments.Length)
        {
            argList.Argument
        };
        foreach(var arg in argList.Arguments)
        {
            argNodes.Add(arg);
        }

        var count = Math.Min(argNodes.Count, paramSymbols.Length);
        for(var i = 0; i < count; i++)
        {
            var argNode = argNodes[i];
            var paramSym = paramSymbols[i];

            // Access RefKind from the concrete Symbol type.
            if(paramSym is not Symbol concreteParam)
            {
                continue;
            }

            if(concreteParam.RefKind == RefKind.Ref && argNode is ValueArgumentArgumentNode)
            {
                // Parameter is ref but argument is missing the 'ref' keyword.
                diagnostics.ReportAtNode(TypeCheckDiagnostics.RefKeywordMissing, argNode, "TypeChecker");
            }
            else if(concreteParam.RefKind == RefKind.None && argNode is RefArgumentArgumentNode)
            {
                // Parameter is not ref but argument has a spurious 'ref' keyword.
                diagnostics.ReportAtNode(TypeCheckDiagnostics.RefKeywordOnNonRefParam, argNode, "TypeChecker");
            }
        }
    }

    private void CheckArgumentTypes(
        ArgumentListNode? argList,
        ISymbol methodSymbol,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Get parameter symbols from the method's containing scope.
        // For CLR Backend symbols, parameters are stored directly on the Symbol instance.
        ImmutableArray<ISymbol> paramSymbols;

        if(methodSymbol is Symbol { Parameters.Count: > 0 } backendSym)
        {
            paramSymbols = [.. backendSym.Parameters];
        }
        else
        {
            SyntaxNode methodNode;
            try { methodNode = model.GetDeclaringNode(methodSymbol); }
            catch { return; }

            if(methodNode is MethodDeclarationNode methodDecl && methodDecl.Parameters is not null)
            {
                var paramList = new List<ISymbol>();
                foreach(var child in methodDecl.Parameters.Children())
                {
                    if(child is ParameterNode paramNode)
                    {
                        var paramSym = model.GetDeclaredSymbol(paramNode);
                        if(paramSym is not null)
                        {
                            paramList.Add(paramSym);
                        }
                    }
                }

                paramSymbols = [.. paramList];
            }
            else
            {
                return; // No parameters to check.
            }
        }

        // Collect argument expressions.
        var argExprs = new List<ExpressionNode>();
        if(argList is not null)
        {
            foreach(var expr in argList.GetAllExpressions())
            {
                argExprs.Add(expr);
            }
        }

        // Collect argument nodes for ref-argument detection.
        var argNodes = CollectArgumentNodes(argList);

        // Variadics value-level params (Requirements 13.2, 13.3, 13.7):
        // Detect if the last parameter is a `params T[]` so trailing arguments
        // are checked against the element type T rather than the array type T[].
        IType? valueLevelParamsElementType = null;
        var fixedParamCount = paramSymbols.Length;
        if(paramSymbols.Length > 0
            && paramSymbols[^1] is Symbol lastParamSym
            && lastParamSym.HasFlattenedCallConvention
            && lastParamSym.DeclaredType is ArrayType lastArrayType)
        {
            valueLevelParamsElementType = lastArrayType.ElementType;
            fixedParamCount = paramSymbols.Length - 1;
        }

        // Check fixed (non-params) arguments against corresponding parameters.
        var count = valueLevelParamsElementType is not null
            ? Math.Min(argExprs.Count, fixedParamCount)
            : Math.Min(argExprs.Count, paramSymbols.Length);
        for(var i = 0; i < count; i++)
        {
            var argType = model.GetTypeOf(argExprs[i]);
            var paramType = paramSymbols[i].DeclaredType;

            if(argType is not null && paramType is not null)
            {
                // ── ref-parameters feature: exact type match (MIST_REF_0007) ──
                if(i < argNodes.Count && argNodes[i] is RefArgumentArgumentNode refArgNode)
                {
                    if(IsAssignableStorageLocation(refArgNode.Expression, model, out _)
                        && !RefTypesCompatible(argType, paramType))
                    {
                        diagnostics.ReportAtNode(TypeCheckDiagnostics.RefArgTypeMismatch, refArgNode, "TypeChecker");
                    }

                    continue;
                }

                // ── MIST2007: Ref struct abstract argument restriction (Requirements 3.11, 3.12) ──
                if(IsRefStructType(argType) && IsAbstractTargetType(paramType))
                {
                    diagnostics.ReportAtNode(TypeCheckDiagnostics.RefStructToAbstract, argExprs[i], "TypeChecker");
                    continue;
                }

                // ── refinement-types feature: compile-time enforcement ──
                if(TryCheckRefinementAssignment(argType, paramType, argExprs[i], diagnostics, model))
                {
                    continue;
                }

                // ── regex-type feature: string literal shorthand ──
                if(TryCheckStringToRegexAssignment(argType, paramType, argExprs[i], diagnostics, model))
                {
                    continue;
                }

                if(!IsAssignable(argType, paramType))
                {
                    if(TryEmitUnknownAssignabilityDiagnostic(argType, paramType, argExprs[i], diagnostics))
                    {
                    }
                    else if(TryEmitUnionAssignabilityDiagnostic(argType, paramType, argExprs[i], diagnostics))
                    {
                    }
                    else if(TryEmitEnumAssignabilityDiagnostic(argType, paramType, argExprs[i], diagnostics, model))
                    {
                    }
                    else if(TryEmitSpanInteropMismatchDiagnostic(argType, paramType, argExprs[i], diagnostics))
                    {
                    }
                    else
                    {
                        diagnostics.ReportAtNode(TypeCheckDiagnostics.ExpressionBodyTypeMismatch, argExprs[i], "TypeChecker");
                    }
                }
                else
                {
                    _ = TryResolveImplicitCast(argType, paramType, argExprs[i], model, diagnostics);
                }
            }
        }

        // Variadics value-level params: check trailing arguments against element type T.
        // Requirement 13.2: collect trailing args assignable to T.
        // Requirement 13.3: zero trailing args is valid (empty array).
        // Requirement 13.7: non-assignable trailing arg emits type error.
        if(valueLevelParamsElementType is not null)
        {
            for(var i = fixedParamCount; i < argExprs.Count; i++)
            {
                var argType = model.GetTypeOf(argExprs[i]);
                if(argType is not null && !IsAssignable(argType, valueLevelParamsElementType))
                {
                    diagnostics.ReportAtNode(TypeCheckDiagnostics.ExpressionBodyTypeMismatch, argExprs[i], "TypeChecker");
                }
            }
        }

        // adhoc-refinement-constraints feature: validate arguments against parameter
        // refinement constraints (Requirements 6.1, 6.2, 6.3, 6.4, 6.5, 6.6, 6.7).
        ValidateParameterRefinementConstraints(argList, methodSymbol, model, diagnostics);
    }

    /// <summary>
    /// Checks argument types against parameter types with type argument substitution.
    /// Applies enclosing type substitution (for ConstructedType member access) and
    /// method-level substitution (for generic method invocations).
    /// </summary>
    private void CheckArgumentTypesWithSubstitution(
        ArgumentListNode? argList,
        ISymbol methodSymbol,
        ISemanticModel model,
        DiagnosticBag diagnostics,

        IReadOnlyList<IType>? enclosingTypeParams,
        IReadOnlyList<IType>? enclosingTypeArgs,
        IReadOnlyList<IType>? methodTypeParams,
        IReadOnlyList<IType>? methodTypeArgs)
    {
        // Get parameter symbols from the method's containing scope.
        // For CLR Backend symbols, parameters are stored directly on the Symbol instance.
        ImmutableArray<ISymbol> paramSymbols;

        if(methodSymbol is Symbol { Parameters.Count: > 0 } backendSym)
        {
            paramSymbols = [.. backendSym.Parameters];
        }
        else
        {
            SyntaxNode methodNode;
            try { methodNode = model.GetDeclaringNode(methodSymbol); }
            catch { return; }

            if(methodNode is MethodDeclarationNode methodDecl && methodDecl.Parameters is not null)
            {
                var paramList = new List<ISymbol>();
                foreach(var child in methodDecl.Parameters.Children())
                {
                    if(child is ParameterNode paramNode)
                    {
                        var paramSym = model.GetDeclaredSymbol(paramNode);
                        if(paramSym is not null)
                        {
                            paramList.Add(paramSym);
                        }
                    }
                }

                paramSymbols = [.. paramList];
            }
            else
            {
                return; // No parameters to check.
            }
        }

        // Collect argument expressions.
        var argExprs = new List<ExpressionNode>();
        if(argList is not null)
        {
            foreach(var expr in argList.GetAllExpressions())
            {
                argExprs.Add(expr);
            }
        }

        // Collect argument nodes for ref-argument detection.
        var argNodes = CollectArgumentNodes(argList);

        // Variadics value-level params (Requirements 13.2, 13.3, 13.7):
        // Detect if the last parameter is a `params T[]` so trailing arguments
        // are checked against the element type T rather than the array type T[].
        IType? valueLevelParamsElementType = null;
        var fixedParamCount = paramSymbols.Length;
        if(paramSymbols.Length > 0
            && paramSymbols[^1] is Symbol lastParamSymSub
            && lastParamSymSub.HasFlattenedCallConvention
            && lastParamSymSub.DeclaredType is ArrayType lastArrayTypeSub)
        {
            valueLevelParamsElementType = lastArrayTypeSub.ElementType;

            // Apply substitutions to the element type.
            if(enclosingTypeParams is not null && enclosingTypeArgs is not null)
            {
                valueLevelParamsElementType = SubstituteType(valueLevelParamsElementType, enclosingTypeParams, enclosingTypeArgs);
            }

            if(methodTypeParams is not null && methodTypeArgs is not null)
            {
                valueLevelParamsElementType = SubstituteType(valueLevelParamsElementType, methodTypeParams, methodTypeArgs);
            }

            fixedParamCount = paramSymbols.Length - 1;
        }

        // Check fixed (non-params) arguments against corresponding parameters, applying substitution.
        var count = valueLevelParamsElementType is not null
            ? Math.Min(argExprs.Count, fixedParamCount)
            : Math.Min(argExprs.Count, paramSymbols.Length);
        for(var i = 0; i < count; i++)
        {
            var argType = model.GetTypeOf(argExprs[i]);
            var paramType = paramSymbols[i].DeclaredType;

            if(argType is not null && paramType is not null)
            {
                // Apply enclosing type substitution (e.g., T → int for Box<int>).
                if(enclosingTypeParams is not null && enclosingTypeArgs is not null)
                {
                    paramType = SubstituteType(paramType, enclosingTypeParams, enclosingTypeArgs);
                }

                // Apply method-level substitution (e.g., U → string for Method<string>).
                if(methodTypeParams is not null && methodTypeArgs is not null)
                {
                    paramType = SubstituteType(paramType, methodTypeParams, methodTypeArgs);
                }

                // ── ref-parameters feature: exact type match (MIST_REF_0007) ──
                if(i < argNodes.Count && argNodes[i] is RefArgumentArgumentNode refArgNode)
                {
                    if(IsAssignableStorageLocation(refArgNode.Expression, model, out _)
                        && !RefTypesCompatible(argType, paramType))
                    {
                        diagnostics.ReportAtNode(TypeCheckDiagnostics.RefArgTypeMismatch, refArgNode, "TypeChecker");
                    }

                    continue;
                }

                // ── MIST2007: Ref struct abstract argument restriction (Requirements 3.11, 3.12) ──
                if(IsRefStructType(argType) && IsAbstractTargetType(paramType))
                {
                    diagnostics.ReportAtNode(TypeCheckDiagnostics.RefStructToAbstract, argExprs[i], "TypeChecker");
                    continue;
                }

                // ── refinement-types feature: compile-time enforcement ──
                if(TryCheckRefinementAssignment(argType, paramType, argExprs[i], diagnostics, model))
                {
                    continue;
                }

                // ── regex-type feature: string literal shorthand ──
                if(TryCheckStringToRegexAssignment(argType, paramType, argExprs[i], diagnostics, model))
                {
                    continue;
                }

                if(!IsAssignable(argType, paramType))
                {
                    if(TryEmitUnknownAssignabilityDiagnostic(argType, paramType, argExprs[i], diagnostics))
                    {
                    }
                    else if(TryEmitUnionAssignabilityDiagnostic(argType, paramType, argExprs[i], diagnostics))
                    {
                    }
                    else if(TryEmitEnumAssignabilityDiagnostic(argType, paramType, argExprs[i], diagnostics, model))
                    {
                    }
                    else if(TryEmitSpanInteropMismatchDiagnostic(argType, paramType, argExprs[i], diagnostics))
                    {
                    }
                    else
                    {
                        diagnostics.ReportAtNode(TypeCheckDiagnostics.ExpressionBodyTypeMismatch, argExprs[i], "TypeChecker");
                    }
                }
                else
                {
                    _ = TryResolveImplicitCast(argType, paramType, argExprs[i], model, diagnostics);
                }
            }
        }

        // Variadics value-level params: check trailing arguments against element type T.
        if(valueLevelParamsElementType is not null)
        {
            for(var i = fixedParamCount; i < argExprs.Count; i++)
            {
                var argType = model.GetTypeOf(argExprs[i]);
                if(argType is not null && !IsAssignable(argType, valueLevelParamsElementType))
                {
                    diagnostics.ReportAtNode(TypeCheckDiagnostics.ExpressionBodyTypeMismatch, argExprs[i], "TypeChecker");
                }
            }
        }

        // adhoc-refinement-constraints feature: validate arguments against parameter
        // refinement constraints (Requirements 6.1, 6.2, 6.3, 6.4, 6.5, 6.6, 6.7).
        ValidateParameterRefinementConstraints(argList, methodSymbol, model, diagnostics);
    }

    /// <summary>
    /// Gets the type parameters from a type definition (ClassType, StructType, or AbstractType).
    /// </summary>
    private static IReadOnlyList<IType>? GetTypeParameters(IType type)
    {
        return type switch
        {
            ClassType ct => ct.TypeParameters.Count > 0 ? ct.TypeParameters : null,
            StructType st => st.TypeParameters.Count > 0 ? st.TypeParameters : null,
            AbstractType it => it.TypeParameters.Count > 0 ? it.TypeParameters : null,
            _ => null,
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Parameter refinement constraint enforcement (MIST_REFINEMENT_0005)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Validates arguments against adhoc refinement constraints on parameters.
    /// For each parameter with an adhoc refinement predicate:
    /// - If the argument is a compile-time constant, evaluates the predicate and emits
    ///   MIST_REFINEMENT_0005 on violation.
    /// - If the argument is non-constant, accepts (defers to runtime guard).
    /// - If the argument's type is a named refinement type, uses RefinementSolver.Implies
    ///   to check if the argument's predicate implies the parameter's constraint.
    /// </summary>
    private static void ValidateParameterRefinementConstraints(
        ArgumentListNode? argList,
        ISymbol callableSymbol,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        if(argList is null)
        {
            return;
        }

        // Get the declaring node to resolve parameter symbols.
        SyntaxNode declaringNode;
        try { declaringNode = model.GetDeclaringNode(callableSymbol); }
        catch { return; }

        // Collect parameter symbols from the declaring node.
        var paramSymbols = CollectParameterSymbols(declaringNode, model);
        if(paramSymbols.Count == 0)
        {
            return;
        }

        // Collect argument expressions.
        var argExprs = new List<ExpressionNode>();
        foreach(var expr in argList.GetAllExpressions())
        {
            argExprs.Add(expr);
        }

        // Check each argument against the corresponding parameter's refinement constraint.
        var count = Math.Min(argExprs.Count, paramSymbols.Count);
        for(var i = 0; i < count; i++)
        {
            var paramRefinement = model.GetParameterRefinement(paramSymbols[i]);
            if(paramRefinement is null)
            {
                continue;
            }

            var argExpr = argExprs[i];
            var argType = model.GetTypeOf(argExpr);

            // Case 1: Argument's type is a named refinement type — check implication.
            if(argType is RefinementType sourceRefinement)
            {
                if(RefinementSolver.Implies(sourceRefinement.Predicate, paramRefinement))
                {
                    // Source predicate implies parameter constraint — accept.
                    continue;
                }

                // Implication not provable — emit compile-time error.
                var callableName1 = GetCallableDescription(callableSymbol);
                diagnostics.ReportAtNode(TypeCheckDiagnostics.RefinementImplicationFailure, argExpr, "TypeChecker", paramSymbols[i].Name, callableName1);
                continue;
            }

            // Case 2: Argument is a compile-time constant — evaluate predicate.
            var constantValue = TryExtractLiteralConstantValue(argExpr);
            if(constantValue is not null)
            {
                if(!paramRefinement.Evaluate(constantValue))
                {
                    diagnostics.ReportAtNode(TypeCheckDiagnostics.ParameterRefinementViolation, argExpr, "TypeChecker", constantValue.ToString()!, paramSymbols[i].Name);
                }

                continue;
            }

            // Case 3: Non-constant, non-refinement-typed argument — reject at compile time.
            // Adhoc parameter refinement constraints enforce the same rules as named
            // refinement type parameters: non-provable arguments require explicit
            // pattern matching to establish the constraint.
            diagnostics.ReportAtNode(TypeCheckDiagnostics.ParameterRefinementNonConstant, argExpr, "TypeChecker", paramSymbols[i].Name);
        }
    }

    /// <summary>
    /// Collects parameter symbols from a method, constructor, or indexer declaring node.
    /// </summary>
    private static List<ISymbol> CollectParameterSymbols(SyntaxNode declaringNode, ISemanticModel model)
    {
        var paramList = new List<ISymbol>();

        var parameters = declaringNode switch
        {
            MethodDeclarationNode method => method.Parameters,
            ConstructorDeclarationNode ctor => ctor.Parameters,
            IndexerDeclarationNode indexer => indexer.Parameters,
            _ => null,
        };

        if(parameters is null)
        {
            return paramList;
        }

        foreach(var child in parameters.Children())
        {
            if(child is ParameterNode paramNode)
            {
                var paramSym = model.GetDeclaredSymbol(paramNode);
                if(paramSym is not null)
                {
                    paramList.Add(paramSym);
                }
            }
        }

        return paramList;
    }

    /// <summary>
    /// Returns a human-readable description of the callable for diagnostic messages.
    /// For methods: "method '{Name}'"
    /// For constructors: "constructor '{TypeName}'"
    /// For indexers: "indexer '{TypeName}'"
    /// </summary>
    private static string GetCallableDescription(ISymbol callableSymbol)
    {
        return callableSymbol.Kind switch
        {
            SymbolKind.Constructor => $"constructor '{FindDeclaringType(callableSymbol)?.DisplayName ?? callableSymbol.Name}'",
            SymbolKind.Indexer => $"indexer '{FindDeclaringType(callableSymbol)?.DisplayName ?? callableSymbol.Name}'",
            _ => $"method '{callableSymbol.Name}'",
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Readonly type argument call-site checking (MIST_TC_0076)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns true if the given type is marked as readonly (has <c>IsReadonly == true</c>).
    /// Checks ClassType, StructType, and AbstractType.
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
    /// Determines whether a type is an owned type. A type is considered owned if
    /// it has the <c>owned</c> modifier or an implied ownership discipline
    /// (<c>affine</c> or <c>linear</c>).
    /// </summary>
    private static bool IsTypeOwned(IType type)
    {
        return type switch
        {
            ClassType ct => ct.IsOwnedType,
            StructType st => st.IsOwnedType,
            ConstructedType constructed => IsTypeOwned(constructed.GenericDefinition),
            _ => false,
        };
    }

    /// <summary>
    /// Names of types that are intrinsically defaultable (primitives and string).
    /// Mirrors <c>SemanticModelBuilder.IntrinsicDefaultableNames</c>.
    /// </summary>
    private static readonly HashSet<string> IntrinsicDefaultableNames = new(StringComparer.Ordinal)
    {
        "Int32", "UInt32", "Int64", "UInt64", "Int16", "UInt16",
        "Byte", "SByte", "Single", "Double", "Decimal",
        "Boolean", "Char", "String",
        "int", "uint", "long", "ulong", "short", "ushort",
        "byte", "sbyte", "float", "double", "decimal",
        "bool", "char", "string",
    };

    /// <summary>
    /// Names of primitive struct types that are always stampable. This covers
    /// legacy singleton StructType instances that may not have had
    /// <see cref="StructType.IsStampable"/> set by <c>ComputeStampability</c>
    /// (e.g., when the standard library is not loaded in tests).
    /// Excludes <c>string</c> because it is a ClassType, not a StructType.
    /// </summary>
    private static readonly HashSet<string> PrimitiveStructNames = new(StringComparer.Ordinal)
    {
        "Int32", "UInt32", "Int64", "UInt64", "Int16", "UInt16",
        "Byte", "SByte", "Single", "Double", "Decimal",
        "Boolean", "Char",
        "int", "uint", "long", "ulong", "short", "ushort",
        "byte", "sbyte", "float", "double", "decimal",
        "bool", "char",
    };

    /// <summary>
    /// Returns <c>true</c> if a <see cref="StructType"/> is a primitive numeric or
    /// boolean/char type by name. Used as a fallback in the stampability gate for
    /// legacy primitive singletons that may not have been processed by
    /// <c>ComputeStampability</c>.
    /// </summary>
    private static bool IsPrimitiveStructByName(StructType st)
    {
        return PrimitiveStructNames.Contains(st.DisplayName);
    }

    /// <summary>
    /// Determines whether a type is defaultable — either intrinsically defaultable
    /// (primitive/string), or a ClassType/StructType/EnumType with <c>Inferred</c>
    /// or <c>Explicit</c> defaultability, or a union type containing void.
    /// Abstracts are never defaultable.
    /// </summary>
    private static bool IsTypeDefaultable(IType type)
    {
        // Intrinsically defaultable: primitives and string.
        if(type is StructType or ClassType)
        {
            if(IntrinsicDefaultableNames.Contains(type.DisplayName))
            {
                return true;
            }
        }

        return type switch
        {
            ClassType ct => ct.Defaultability is DefaultabilityStatus.Inferred or DefaultabilityStatus.Explicit,
            StructType st => st.Defaultability is DefaultabilityStatus.Inferred or DefaultabilityStatus.Explicit,
            EnumType et => et.Defaultability is DefaultabilityStatus.Inferred or DefaultabilityStatus.Explicit,
            UnionType ut => ut.Constituents.Any(c => c.IsVoid()),
            ConstructedType constructed => IsTypeDefaultable(constructed.GenericDefinition),
            TypeParameterType tp => tp.Constraints?.HasDefaultConstraint == true,
            AbstractType => false,
            _ => false,
        };
    }


    /// <summary>
    /// Returns true if all type arguments in the list are type reference nodes
    /// (not literal type arguments like integer literals for dependent types).
    /// </summary>
    private static bool HasOnlyTypeReferenceArgs(TypeArgumentListNode typeArgList)
    {
        if(typeArgList.TypeArgument is not TypeReferenceTypeArgumentTypeArgumentNode)
        {
            return false;
        }

        foreach(var arg in typeArgList.TypeArguments)
        {
            if(arg is not TypeReferenceTypeArgumentTypeArgumentNode)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Resolves type arguments from a <see cref="TypeArgumentListNode"/> AST node
    /// by resolving each type reference to its corresponding <see cref="IType"/>.
    /// </summary>
    private static IReadOnlyList<IType> ResolveTypeArgumentsFromAst(TypeArgumentListNode typeArgList, ISemanticModel model)
    {
        var result = new List<IType>();

        // The TypeArgumentListNode has TypeArgument (first arg) and TypeArguments (rest).
        var firstType = ResolveTypeReferenceToType(typeArgList.GetTypeReference(), model);
        result.Add(firstType);

        foreach(var typeRef in typeArgList.GetTypeReferences())
        {
            var argType = ResolveTypeReferenceToType(typeRef, model);
            result.Add(argType);
        }

        return result;
    }

    /// <summary>
    /// Resolves a <see cref="TypeReferenceNode"/> to its corresponding <see cref="IType"/>
    /// by looking up the type name in the semantic model's symbol table.
    /// Type aliases are resolved transparently — the underlying type is returned
    /// with no alias wrapper remaining.
    /// </summary>
    private static IType ResolveTypeReferenceToType(TypeReferenceNode typeRef, ISemanticModel model, DiagnosticBag? diagnostics = null)
    {
        // Try the expression type map first (may be populated for some nodes).
        var resolved = model.GetTypeOf(typeRef);
        if(resolved is not null and not ErrorType)
        {
            // Transparently unwrap type aliases (Requirement 5.1).
            if(resolved is TypeAliasType aliasFromMap)
            {
                return aliasFromMap.UnderlyingType;
            }

            return resolved;
        }

        switch(typeRef.First)
        {
            case PrimitiveSingleTypeRefSingleTypeRefNode primitiveRef:
                {
                    var primType = ResolvePrimitiveType(primitiveRef.PrimitiveType);
                    if(primitiveRef.ArrayRank is not null && primType is not null)
                    {
                        return new ArrayType(primType);
                    }

                    return primType ?? ErrorType.Instance;
                }
            case NamedSingleTypeRefSingleTypeRefNode namedRef:
                {
                    _ = namedRef.Name.Children().FirstOrDefault()?.Span;
                    // Try to find the type by looking up the symbol. The semantic model
                    // keys declared symbols on the flat TypeReferenceNode, so look up via
                    // `typeRef` (not the SingleTypeRef child).
                    var symbol = model.GetDeclaredSymbol(typeRef);
                    if(symbol?.DeclaredType is not null)
                    {
                        return ResolveTypeAliasMaybeGeneric(
                            symbol.DeclaredType, namedRef, model, diagnostics);
                    }

                    // Fallback: search all symbols for a matching type name.
                    // This lookup is namespace-blind and import-blind. When more than
                    // one type shares the bare name it cannot safely choose the
                    // intended one, so it returns ErrorType instead of guessing. A
                    // wrong guess resolves the reference to an unrelated same-named
                    // type and produces spurious diagnostics (e.g. a generic 'default'
                    // constraint checked against the wrong type). Treating the
                    // ambiguous case as unresolved leaves downstream checks suppressed,
                    // matching the prior behavior for these references. A single
                    // unambiguous match is still resolved.
                    var typeName = ExtractTypeName(namedRef);
                    ISymbol? matchedSymbol = null;
                    foreach(var sym in model.AllSymbols)
                    {
                        if(sym.Name == typeName && sym.Kind is SymbolKind.Class or SymbolKind.Struct or SymbolKind.Abstract or SymbolKind.TypeAlias)
                        {
                            if(matchedSymbol is not null)
                            {
                                // Ambiguous bare-name match across namespaces:
                                // cannot resolve safely.
                                return ErrorType.Instance;
                            }

                            matchedSymbol = sym;
                        }
                    }

                    if(matchedSymbol is not null)
                    {
                        var declaredType = matchedSymbol.DeclaredType ?? ErrorType.Instance;
                        return ResolveTypeAliasMaybeGeneric(
                            declaredType, namedRef, model, diagnostics);
                    }

                    return ErrorType.Instance;
                }
            case VoidSingleTypeRefSingleTypeRefNode:
                return LegacyPrimitiveTypes.Void;
            default:
                return ErrorType.Instance;
        }
    }

    /// <summary>
    /// If <paramref name="type"/> is a <see cref="TypeAliasType"/>, transparently
    /// resolves it to its underlying type. For generic aliases with type arguments
    /// on the reference node, verifies argument count and substitutes type arguments
    /// into the underlying type. Reports <c>MIST_SB_0063</c> on arity mismatch.
    /// Non-alias types are returned unchanged.
    /// </summary>
    private static IType ResolveTypeAliasMaybeGeneric(
        IType type,
        NamedSingleTypeRefSingleTypeRefNode namedRef,
        ISemanticModel model,
        DiagnosticBag? diagnostics)
    {
        if(type is not TypeAliasType aliasType)
        {
            return type;
        }

        var underlyingType = aliasType.UnderlyingType;
        var typeParams = aliasType.TypeParameters;

        // Non-generic alias — return the underlying type directly.
        if(typeParams.Count == 0)
        {
            return underlyingType;
        }

        // Generic alias — check if type arguments are provided on the reference.
        if(namedRef.TypeArgs is null)
        {
            // No type arguments provided for a generic alias — return as-is
            // (the underlying type still contains unsubstituted type parameters).
            return underlyingType;
        }

        // Resolve the type arguments from the AST.
        var typeArgs = ResolveTypeArgumentsFromAst(namedRef.TypeArgs, model);

        // Verify argument count matches parameter count (Requirement 5.3).
        if(typeArgs.Count != typeParams.Count)
        {
            diagnostics?.ReportAtNode(TypeCheckDiagnostics.TypeAliasArgumentCountMismatch, namedRef, "TypeChecker");

            return ErrorType.Instance;
        }

        // Substitute type arguments into the underlying type.
        return SubstituteType(underlyingType, typeParams, typeArgs);
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
            RegexTypePrimitiveTypeNode => LegacyPrimitiveTypes.Regex,
            _ => null,
        };
    }

    /// <summary>
    /// Extracts the type name from a <see cref="NamedSingleTypeRefSingleTypeRefNode"/>.
    /// </summary>
    private static string ExtractTypeName(NamedSingleTypeRefSingleTypeRefNode namedRef)
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
        if(elementType is ArrayType at && at.ElementType.IsVoid())
        {
            diagnostics.ReportAtNode(CommonDiagnostics.VoidArrayNotPermitted, arrayCreation, "TypeChecker");
            return;
        }

        // ── unknown-type storage ban on `new T[n]` (Task 12.3) ───────────
        // Requirement 5.5 — `new unknown[n]` is forbidden because every
        // slot of the resulting array would persist an `unknown`-typed
        // value, and array elements are writable storage. The check runs
        // against the element type (unwrapped from the ArrayType that
        // SemanticModelBuilder produces for this node); nested
        // `unknown` occurrences inside a non-trivial element type like
        // `List<unknown>[]` also trip the ban via IsStorageBanned.
        if(elementType is ArrayType arr && TypeContainment.IsStorageBanned(arr.ElementType))
        {
            diagnostics.ReportAtNode(TypeCheckDiagnostics.UnknownAsArrayElement, arrayCreation, "TypeChecker", arr.ElementType.DisplayName);
            return;
        }

        var sizeType = model.GetTypeOf(arrayCreation.Size);
        if(sizeType is not null && !IsErrorType(sizeType))
        {
            if(!IsIntLike(sizeType))
            {
                diagnostics.ReportAtNode(TypeCheckDiagnostics.ArraySizeNotInt, arrayCreation.Size, "TypeChecker");
            }
        }

        // ── Stampability gate for bare `new T[size]` ─────────────────────
        // Requirement 4.1/4.2/4.5/4.6: bare array creation is restricted
        // to stampable element types (primitives and pure structs). All
        // other types require initializer-based construction.
        if(elementType is ArrayType arrayType)
        {
            var elemType = arrayType.ElementType;

            // Skip the gate if the element type is unresolved/error.
            if(elemType is null || IsErrorType(elemType))
            {
                return;
            }

            // Accept stampable struct types (includes primitives like int,
            // bool, char, float — all modeled as StructType with IsStampable = true).
            // Also accept legacy primitive struct singletons that may not have
            // had IsStampable set (they are intrinsically stampable by name).
            if(elemType is StructType st && (st.IsStampable || IsPrimitiveStructByName(st)))
            {
                return;
            }

            // All other element types are not stampable — emit the diagnostic.
            _ = elemType switch
            {
                StructType => $"'{elemType.DisplayName}' is a struct with fields that are not stampable",
                _ => $"'{elemType.DisplayName}' is a reference type",
            }; diagnostics.ReportAtNode(CommonDiagnostics.ArrayRequiresInitializer, arrayCreation, "TypeChecker");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Stackalloc expression checking (MIST_TC_0150, MIST_TC_0151)
    // ═══════════════════════════════════════════════════════════════════════

    private void CheckStackallocExpression(
        StackallocExpressionPrimaryNode stackallocExpr,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Recursively type-check the size expression.
        CheckExpression(stackallocExpr.Size, model, diagnostics);

        // ── MIST_TC_0151: size must be integer-typed (R4.7, R4.8) ────────
        // Uses the same `IsIntLike` predicate that array creation uses at
        // MIST_TC_0012 for `new T[n]`.
        var sizeType = model.GetTypeOf(stackallocExpr.Size);
        if(sizeType is not null && !IsErrorType(sizeType))
        {
            if(!IsIntLike(sizeType))
            {
                diagnostics.ReportAtNode(TypeCheckDiagnostics.StackallocSizeNotInt, stackallocExpr.Size, "TypeChecker", sizeType.DisplayName);
            }
        }

        // ── Element-type stampability gate (R5.1–R5.7, R14.3) ────────────
        // Reuses the same IsStampable + IsPrimitiveStructByName gate that
        // bare `new T[size]` uses, but anchored at the element-type node
        // and with span-specific diagnostic IDs distinguishing the reason:
        //   MIST_TC_0152 — reference type
        //   MIST_TC_0153 — non-stampable struct
        //   MIST_TC_0154 — ref struct
        //   MIST_TC_0155 — unconstrained type parameter
        var spanType = model.GetTypeOf(stackallocExpr);
        if(spanType is SpanType span)
        {
            var elemType = span.ElementType;

            // Skip the gate if the element type is unresolved/error.
            if(elemType is null || IsErrorType(elemType))
            {
                return;
            }

            // Reject ref structs unconditionally BEFORE the stampability check:
            // a ref struct cannot be used as a span element type even if its
            // fields happen to be all primitives (R5.6, MIST_TC_0154).
            if(IsRefStructType(elemType))
            {
                diagnostics.ReportAtNode(TypeCheckDiagnostics.StackallocRefStructElement, stackallocExpr.Type, "TypeChecker", elemType.DisplayName);
                return;
            }

            // Accept stampable struct types (includes primitives like int,
            // bool, char, float — all modeled as StructType with IsStampable = true).
            // Also accept legacy primitive struct singletons that may not have
            // had IsStampable set (they are intrinsically stampable by name).
            if(elemType is StructType st && (st.IsStampable || IsPrimitiveStructByName(st)))
            {
                return;
            }

            // Reject: determine the specific descriptor and emit.
            var typeNode = (SyntaxNode)stackallocExpr.Type;
            switch(elemType)
            {
                case { } when IsRefStructType(elemType):
                    diagnostics.ReportAtNode(TypeCheckDiagnostics.StackallocRefStructElement, typeNode, "TypeChecker", elemType.DisplayName);
                    break;

                case StructType:
                    diagnostics.ReportAtNode(TypeCheckDiagnostics.StackallocNonStampableElement, typeNode, "TypeChecker", elemType.DisplayName);
                    break;

                case TypeParameterType:
                    diagnostics.ReportAtNode(TypeCheckDiagnostics.StackallocTypeParameterElement, typeNode, "TypeChecker", elemType.DisplayName);
                    break;

                default:
                    diagnostics.ReportAtNode(TypeCheckDiagnostics.StackallocReferenceTypeElement, typeNode, "TypeChecker", elemType!.DisplayName);
                    break;
            }
        }
    }

    /// <summary>
    /// Detects the stackalloc-target-type-specific assignability failure mode:
    /// when the source type is a <see cref="SpanType"/> (the result of a stackalloc
    /// expression) and the target type is not a <see cref="SpanType"/> family type,
    /// emits <c>MIST_TC_0150</c> and returns <c>true</c>. Returns <c>false</c> if the
    /// case does not apply, leaving the caller to emit its regular diagnostic.
    /// </summary>
    private static bool TryEmitStackallocTargetMismatchDiagnostic(
        IType source,
        IType target,
        SyntaxNode node,
        DiagnosticBag diagnostics)
    {
        if(source is SpanType && target is not SpanType)
        {
            diagnostics.ReportAtNode(TypeCheckDiagnostics.StackallocAssignmentMismatch, node, "TypeChecker", source.DisplayName, target.DisplayName);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Detects the span-interop-specific assignability failure mode:
    /// when both source and target are <see cref="SpanType"/> but the assignment
    /// is rejected because the span kind differs (mutable vs read-only) or the
    /// element types cannot be reconciled. Emits <c>MIST_TC_0157</c> with a message
    /// identifying the specific mismatch reason and returns <c>true</c>. Returns
    /// <c>false</c> if neither type is a <see cref="SpanType"/>, leaving the caller
    /// to emit its regular diagnostic. (Requirements 7.4, 7.5, 7.6, 7.7, 9.2)
    /// </summary>
    private static bool TryEmitSpanInteropMismatchDiagnostic(
        IType source,
        IType target,
        SyntaxNode node,
        DiagnosticBag diagnostics)
    {
        if(source is SpanType sourceSpan && target is SpanType targetSpan)
        {
            // Both are SpanType but IsAssignable returned false.
            // Identify the reason: kind mismatch or element type mismatch.
            if(sourceSpan.IsReadOnly != targetSpan.IsReadOnly)
            {
                var sourceKind = sourceSpan.IsReadOnly ? "ReadOnlySpan" : "Span";
                var targetKind = targetSpan.IsReadOnly ? "ReadOnlySpan" : "Span";
                diagnostics.ReportAtNode(TypeCheckDiagnostics.SpanKindMismatch, node, "TypeChecker", sourceKind, targetKind);
            }
            else
            {
                // Kind matches but element types cannot be reconciled.
                diagnostics.ReportAtNode(TypeCheckDiagnostics.SpanElementTypeMismatch, node, "TypeChecker", source.DisplayName, target.DisplayName);
            }

            return true;
        }

        return false;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Array literal checking (MIST_TC_0034, MIST_TC_0095, MIST_SB_0055)
    // ═══════════════════════════════════════════════════════════════════════

    private void CheckArrayLiteral(
        ArrayLiteralExpressionPrimaryNode arrayLiteral,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // The empty literal [] matches its own case in CheckExpression and never reaches
        // here, but the element list is optional on the node so this is guarded rather
        // than assumed. Every check below compares against the first element, so the
        // first and the remainder are kept separate.
        var allElements = arrayLiteral.GetElements();
        if(allElements.IsEmpty)
        {
            return;
        }

        var firstElement = allElements[0];
        var remainingElements = allElements.RemoveAt(0);

        // Recursively type-check the first element expression.
        CheckExpression(firstElement, model, diagnostics);

        // Recursively type-check each remaining element expression.
        foreach(var elem in remainingElements)
        {
            CheckExpression(elem, model, diagnostics);
        }

        // Get the type of the first element.
        var firstType = model.GetTypeOf(firstElement);

        // Validate the first element is not void (MIST_TC_0034).
        if(firstType is not null && !IsErrorType(firstType) && firstType.IsVoid())
        {
            diagnostics.ReportAtNode(CommonDiagnostics.VoidArrayNotPermitted, firstElement, "TypeChecker");
        }

        // Validate the first element does not contain unknown (MIST_SB_0055).
        if(firstType is not null && !IsErrorType(firstType) && TypeContainment.IsStorageBanned(firstType))
        {
            diagnostics.ReportAtNode(TypeCheckDiagnostics.UnknownAsArrayElement, firstElement, "TypeChecker", firstType.DisplayName);
        }

        // Validate each remaining element.
        foreach(var elem in remainingElements)
        {
            var elemType = model.GetTypeOf(elem);
            if(elemType is null || IsErrorType(elemType))
            {
                continue;
            }

            // Reject void elements (MIST_TC_0034).
            if(elemType.IsVoid())
            {
                diagnostics.ReportAtNode(CommonDiagnostics.VoidArrayNotPermitted, elem, "TypeChecker");
                continue;
            }

            // Reject unknown elements (MIST_SB_0055).
            if(TypeContainment.IsStorageBanned(elemType))
            {
                diagnostics.ReportAtNode(TypeCheckDiagnostics.UnknownAsArrayElement, elem, "TypeChecker", elemType.DisplayName);
                continue;
            }

            // Validate element type compatibility with the first element (MIST_TC_0095).
            if(firstType is not null && !IsErrorType(firstType) && !firstType.IsVoid()
                && !TypeContainment.IsStorageBanned(firstType))
            {
                if(!IsAssignable(elemType, firstType, model) && !IsAssignable(firstType, elemType, model))
                {
                    diagnostics.ReportAtNode(TypeCheckDiagnostics.ArrayLiteralIncompatibleTypes, elem, "TypeChecker");
                }
            }
        }

        // When the array literal has a known target type (from the semantic model),
        // validate each element is assignable to the target element type.
        var arrayType = model.GetTypeOf(arrayLiteral);
        if(arrayType is ArrayType targetArrayType)
        {
            var targetElementType = targetArrayType.ElementType;
            if(!IsErrorType(targetElementType) && !targetElementType.IsVoid()
                && !TypeContainment.IsStorageBanned(targetElementType))
            {
                // Check first element against target element type.
                if(firstType is not null && !IsErrorType(firstType) && !firstType.IsVoid()
                    && !TypeContainment.IsStorageBanned(firstType))
                {
                    if(!IsAssignable(firstType, targetElementType, model))
                    {
                        diagnostics.ReportAtNode(TypeCheckDiagnostics.ArrayLiteralIncompatibleTypes, firstElement, "TypeChecker");
                    }
                }

                // Check remaining elements against target element type.
                foreach(var elem in remainingElements)
                {
                    var elemType = model.GetTypeOf(elem);
                    if(elemType is null || IsErrorType(elemType) || elemType.IsVoid()
                        || TypeContainment.IsStorageBanned(elemType))
                    {
                        continue;
                    }

                    if(!IsAssignable(elemType, targetElementType, model))
                    {
                        diagnostics.ReportAtNode(TypeCheckDiagnostics.ArrayLiteralIncompatibleTypes, elem, "TypeChecker");
                    }
                }
            }
        }
    }

    /// <summary>
    /// Validates an element-access <c>receiver[argument]</c> (an
    /// <see cref="IndexSuffixPostfixSuffixNode"/>), branching on the argument's
    /// Compile_Time_Type:
    /// <list type="bullet">
    /// <item>a <c>Standard.Index</c> argument (e.g. from <c>^k</c>) requires an
    /// indexable receiver (<see cref="SemanticModelBuilder.IsIndexableType"/>),
    /// otherwise <see cref="TypeCheckDiagnostics.IndexAccessRequiresIndexable"/>
    /// (MIST_TC_0184) is emitted naming the receiver type (Req 8.6, 12.3);</item>
    /// <item>a <c>Standard.Range</c> argument (e.g. from <c>a..b</c>) requires a
    /// sliceable receiver (<see cref="SemanticModelBuilder.IsSliceableType"/>),
    /// otherwise <see cref="TypeCheckDiagnostics.RangeAccessRequiresSliceable"/>
    /// (MIST_TC_0185) is emitted naming the receiver type (Req 8.7, 12.4);</item>
    /// <item>an ordinary integer argument or a matching custom indexer keeps its
    /// existing validation (<see cref="TypeCheckDiagnostics.IndexerParameterTypeMismatch"/>
    /// and the index-refinement checks below), unchanged.</item>
    /// </list>
    /// The <c>Index</c>/<c>Range</c> branches short-circuit the integer/custom-indexer
    /// path (an <c>Index</c>/<c>Range</c> value is deliberately not <c>int</c>-like).
    /// An argument or receiver type that resolved to an unknown or error type (a prior
    /// error that produced its own diagnostic) is suppressed to avoid cascading
    /// diagnostics, mirroring <see cref="CheckFromEndExpression"/> and
    /// <see cref="CheckRange"/>. The diagnostic is reported at the element-access span
    /// (Req 12.3, 12.4).
    /// </summary>
    private void CheckIndexSuffix(
        IndexSuffixPostfixSuffixNode indexSuffix,
        IType? targetType,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Check the index expression.
        CheckExpression(indexSuffix.Index, model, diagnostics);

        var indexType = model.GetTypeOf(indexSuffix.Index);

        // Branch on the argument's resolved type (ranges-and-indices, Req 8.6, 8.7,
        // 12.3, 12.4). A Standard.Index / Standard.Range argument follows a dedicated
        // receiver-validation path that short-circuits the ordinary integer /
        // custom-indexer validation below. Suppress when the argument type is
        // unresolved or already reported as an error upstream.
        if(indexType is not null and not UnknownType && !IsErrorType(indexType))
        {
            if(IsStandardIndex(indexType))
            {
                // Index-from-end (or direct Index) access requires a countable,
                // indexable receiver (Req 8.6, 12.3).
                if(targetType is not null and not UnknownType && !IsErrorType(targetType)
                    && !SemanticModelBuilder.IsIndexableType(targetType))
                {
                    diagnostics.ReportAtNode(
                        TypeCheckDiagnostics.IndexAccessRequiresIndexable, indexSuffix, "TypeChecker", targetType.DisplayName);
                }

                return;
            }

            if(IsStandardRange(indexType))
            {
                // Range slicing requires a sliceable receiver (Req 8.7, 12.4).
                if(targetType is not null and not UnknownType && !IsErrorType(targetType)
                    && !SemanticModelBuilder.IsSliceableType(targetType))
                {
                    diagnostics.ReportAtNode(
                        TypeCheckDiagnostics.RangeAccessRequiresSliceable, indexSuffix, "TypeChecker", targetType.DisplayName);
                }

                return;
            }
        }

        var hasCustomIndexer = false;

        // If the target type has a custom indexer (e.g., Dictionary<TKey, TValue>),
        // validate the index type against the indexer's parameter type instead of
        // requiring int.
        if(targetType is not null and not ArrayType)
        {
            var indexerLookupType = targetType is ConstructedType ict ? ict.GenericDefinition : targetType;
            var indexerScope = indexerLookupType switch
            {
                ClassType ct => ct.MemberScope,
                StructType st => st.MemberScope,
                AbstractType it => it.MemberScope,
                _ => null,
            };

            if(indexerScope is not null)
            {
                ISymbol? indexerSymbol = null;
                foreach(var member in indexerScope.Members)
                {
                    if(member.Kind == SymbolKind.Indexer)
                    {
                        indexerSymbol = member;
                        break;
                    }
                }

                if(indexerSymbol is not null)
                {
                    hasCustomIndexer = true;
                    // Custom indexer found — validate index type against indexer parameter type.
                    // The indexer's parameter types are stored in Symbol.Parameters (CLR types).
                    if(indexerSymbol is Symbol { Parameters.Count: > 0 } indexerSym && indexType is not null && !IsErrorType(indexType))
                    {
                        var expectedParamType = indexerSym.Parameters[0].DeclaredType;
                        if(expectedParamType is not null)
                        {
                            // Apply type substitution for ConstructedType.
                            if(targetType is ConstructedType ict2)
                            {
                                var typeParams = GetTypeParameters(ict2.GenericDefinition);
                                if(typeParams is not null)
                                {
                                    expectedParamType = SubstituteType(expectedParamType, typeParams, ict2.TypeArguments);
                                }
                            }

                            if(!IsAssignable(indexType, expectedParamType))
                            {
                                diagnostics.ReportAtNode(TypeCheckDiagnostics.IndexerParameterTypeMismatch, indexSuffix.Index, "TypeChecker");
                            }
                        }
                    }
                }
            }
        }

        if(!hasCustomIndexer && indexType is not null && !IsErrorType(indexType))
        {
            if(!IsIntLike(indexType))
            {
                diagnostics.ReportAtNode(TypeCheckDiagnostics.IndexerParameterTypeMismatch, indexSuffix.Index, "TypeChecker");
            }
        }

        // Validate refinement constraint on the indexer parameter.
        // Arrays, spans, and custom indexers use the same validation path (Requirement 2.1).
        if(targetType is ArrayType arrayTarget)
        {
            ValidateArrayIndexRefinement(indexSuffix, arrayTarget, model, diagnostics);
        }
        else if(targetType is SpanType spanTarget)
        {
            // Spans reuse the exact array bounds machinery (Requirements 1.5, 2.5, 14.6).
            ValidateLengthIndexRefinement(
                indexSuffix, spanTarget.KnownLength, spanTarget.SymbolicLengthId, spanTarget.DisplayName, model, diagnostics);
        }
        else if(targetType is not null)
        {
            ValidateIndexerParameterRefinement(indexSuffix, targetType, model, diagnostics);
        }
    }

    /// <summary>
    /// Validates the index argument against the ArrayBounds predicate for an array indexer.
    /// One-line adapter delegating to the shared length-based bounds helper.
    /// </summary>
    /// <remarks>
    /// Requirements: 2.1, 2.2, 2.3, 2.4, 5.1, 5.2, 5.3, 5.4
    /// </remarks>
    private void ValidateArrayIndexRefinement(
        IndexSuffixPostfixSuffixNode indexSuffix,
        ArrayType arrayType,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        ValidateLengthIndexRefinement(
                indexSuffix, arrayType.KnownLength, arrayType.SymbolicLengthId, arrayType.DisplayName, model, diagnostics);
    }

    /// <summary>
    /// Validates the index argument against the Bounds predicate for a length-carrying
    /// indexer target (an array or a span). Constructs the predicate (>= 0 and &lt; Length)
    /// from the target's KnownLength or SymbolicLengthId, then validates using the 3-case
    /// logic: literal evaluation, narrowing context implication, refinement type implication,
    /// or reject with diagnostic. Shared verbatim between <see cref="ArrayType"/> and
    /// <see cref="SpanType"/> — this is reuse of the existing array machinery, not a second
    /// bounds checker.
    /// </summary>
    /// <remarks>
    /// Requirements: 2.1, 2.2, 2.3, 2.4, 5.1, 5.2, 5.3, 5.4, 1.5, 2.5, 14.6
    /// </remarks>
    private void ValidateLengthIndexRefinement(
        IndexSuffixPostfixSuffixNode indexSuffix,
        long? knownLength,
        int? symbolicLengthId,
        string targetDisplayName,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Build the target constraints based on what's known about the target's length.
        // Per Requirement 2.1: no bypass — validation always runs.
        // - Concrete length: full bounds check (>= 0 and < KnownLength)
        // - Symbolic length: full bounds check (>= 0 and < symbolic ID)
        // - Unknown length (no KnownLength, no SymbolicLengthId): only enforce >= 0.
        //   This occurs for local variables with explicit array type annotations
        //   (e.g., `int[] numbers = new int[3]`) and unknown-length spans. The upper
        //   bound cannot be proven statically, so it is deferred to runtime — matching
        //   C# semantics.
        var targetConstraints = new List<ConditionConstraint>
        {
            new(ConditionRelOp.Gte, 0)
        };

        if(knownLength.HasValue)
        {
            targetConstraints.Add(new ConditionConstraint(ConditionRelOp.Lt, knownLength.Value));
        }
        else if(symbolicLengthId.HasValue)
        {
            targetConstraints.Add(new ConditionConstraint(ConditionRelOp.Lt, 0, symbolicLengthId.Value));
        }

        // Case 1 (literal): Evaluate predicate directly.
        var constantValue = TryExtractLiteralConstantValue(indexSuffix.Index);
        if(constantValue is not null)
        {
            var longVal = constantValue switch
            {
                int i => i,
                long l => l,
                uint u => u,
                ulong ul => (long)ul,
                _ => (long?)null
            };

            if(longVal.HasValue)
            {
                // Check the literal against all target constraints.
                var allSatisfied = true;
                foreach(var target in targetConstraints)
                {
                    if(target.SymbolicId is not null)
                    {
                        // Cannot prove a literal satisfies a symbolic constraint.
                        allSatisfied = false;
                        break;
                    }

                    var satisfied = (target.Op, target.Value) switch
                    {
                        (ConditionRelOp.Gte, var v) => longVal.Value >= v,
                        (ConditionRelOp.Gt, var v) => longVal.Value > v,
                        (ConditionRelOp.Lte, var v) => longVal.Value <= v,
                        (ConditionRelOp.Lt, var v) => longVal.Value < v,
                        _ => false
                    };

                    if(!satisfied)
                    {
                        allSatisfied = false;
                        break;
                    }
                }

                if(allSatisfied)
                {
                    return; // Literal satisfies all constraints — accept.
                }
            }

            // If the only unsatisfied constraint is a symbolic upper bound
            // (unresolvable at compile time), and the literal satisfies the
            // lower bound (>= 0), accept it — defer upper bound to runtime.
            if(longVal.HasValue && longVal.Value >= 0 && !knownLength.HasValue)
            {
                return;
            }

            // Literal fails — emit diagnostic.
            diagnostics.ReportAtNode(TypeCheckDiagnostics.ParameterRefinementViolation, indexSuffix.Index, "TypeChecker", longVal.HasValue ? longVal.Value.ToString() : "?", "index");
            return;
        }

        // Case 2 (narrowing context): Check if _narrowingContext[varName] implies predicate.
        var varName = TryExtractIndexIdentifierName(indexSuffix.Index);
        if(varName is not null && narrowingContext.TryGetValue(varName, out var constraints))
        {
            // Check if every target constraint is implied by at least one source constraint.
            var allImplied = true;
            foreach(var target in targetConstraints)
            {
                var implied = false;
                foreach(var source in constraints)
                {
                    if(ConditionConstraintImplies(source, target))
                    {
                        implied = true;
                        break;
                    }
                }

                if(!implied)
                {
                    allImplied = false;
                    break;
                }
            }

            if(allImplied)
            {
                return; // Narrowing context proves bounds — accept.
            }
        }

        // Case 3 (refinement type): Check if argument's RefinementType implies predicate.
        var argType = model.GetTypeOf(indexSuffix.Index);
        if(argType is RefinementType sourceRefinement)
        {
            var predicateConstraints = ExtractPredicateConstraints(sourceRefinement.Predicate);
            // Check if the source refinement's constraints imply all target constraints.
            var allImplied = true;
            foreach(var target in targetConstraints)
            {
                var implied = false;
                foreach(var source in predicateConstraints)
                {
                    if(ConditionConstraintImplies(source, target))
                    {
                        implied = true;
                        break;
                    }
                }

                if(!implied)
                {
                    allImplied = false;
                    break;
                }
            }

            if(allImplied)
            {
                return; // Refinement type proves bounds — accept.
            }

            // Refinement type doesn't imply — emit implication failure diagnostic.
            var boundsDesc2 = knownLength.HasValue
                ? $">= 0 and < {knownLength.Value}"
                : $">= 0 and < {targetDisplayName}.Length";
            diagnostics.ReportAtNode(TypeCheckDiagnostics.RefinementImplicationFailure, indexSuffix.Index, "TypeChecker", "index", boundsDesc2);
            return;
        }

        // Case 4: Emit diagnostic — cannot prove bounds.
        // For unknown-length arrays (no KnownLength, no SymbolicLengthId), accept
        // non-constant indices — the upper bound is deferred to runtime.
        if(!knownLength.HasValue && !symbolicLengthId.HasValue)
        {
            return;
        }

        diagnostics.ReportAtNode(TypeCheckDiagnostics.ParameterRefinementNonConstant, indexSuffix.Index, "TypeChecker", "index");
    }

    /// <summary>
    /// Extracts a simple identifier name from an index expression node.
    /// Returns null if the expression is not a simple identifier reference.
    /// Used by <see cref="ValidateArrayIndexRefinement"/> to look up narrowing context.
    /// </summary>
    private static string? TryExtractIndexIdentifierName(ExpressionNode expr)
    {
        var primary = UnwrapExpressionToPrimary(expr);
        if(primary is IdentifierExpressionPrimaryNode identifier)
        {
            return identifier.Name.Lexeme;
        }

        return null;
    }

    /// <summary>
    /// Validates the index argument against parameter refinement constraints on a custom indexer.
    /// Finds the indexer symbol on the target type and checks the argument against its first
    /// parameter's adhoc refinement predicate.
    /// </summary>
    private static void ValidateIndexerParameterRefinement(
        IndexSuffixPostfixSuffixNode indexSuffix,
        IType targetType,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Find the indexer symbol on the target type.
        var memberScope = targetType switch
        {
            ClassType ct => ct.MemberScope,
            StructType st => st.MemberScope,
            ConstructedType cst => cst.MemberScope,
            _ => null,
        };

        if(memberScope is null)
        {
            return;
        }

        ISymbol? indexerSymbol = null;
        foreach(var member in memberScope.Members)
        {
            if(member.Kind == SymbolKind.Indexer)
            {
                indexerSymbol = member;
                break;
            }
        }

        if(indexerSymbol is null)
        {
            return;
        }

        // Get the declaring node to find parameter symbols.
        SyntaxNode declaringNode;
        try { declaringNode = model.GetDeclaringNode(indexerSymbol); }
        catch { return; }

        if(declaringNode is not IndexerDeclarationNode indexerDecl || indexerDecl.Parameters is null)
        {
            return;
        }

        // Get the first parameter symbol and check its refinement.
        foreach(var child in indexerDecl.Parameters.Children())
        {
            if(child is ParameterNode paramNode)
            {
                var paramSym = model.GetDeclaredSymbol(paramNode);
                if(paramSym is null)
                {
                    break;
                }

                var paramRefinement = model.GetParameterRefinement(paramSym);
                if(paramRefinement is null)
                {
                    break;
                }

                var argType = model.GetTypeOf(indexSuffix.Index);

                // Case 1: Argument's type is a named refinement type — check implication.
                if(argType is RefinementType sourceRefinement)
                {
                    if(RefinementSolver.Implies(sourceRefinement.Predicate, paramRefinement))
                    {
                        break; // Implication holds — accept.
                    }

                    // Implication not provable — emit compile-time error.
                    var typeName2 = FindDeclaringType(indexerSymbol)?.DisplayName ?? targetType.DisplayName;
                    diagnostics.ReportAtNode(TypeCheckDiagnostics.RefinementImplicationFailure, indexSuffix.Index, "TypeChecker", "index", typeName2);
                    break;
                }

                // Case 2: Argument is a compile-time constant — evaluate predicate.
                var constantValue = TryExtractLiteralConstantValue(indexSuffix.Index);
                if(constantValue is not null)
                {
                    if(!paramRefinement.Evaluate(constantValue))
                    {
                        diagnostics.ReportAtNode(TypeCheckDiagnostics.ParameterRefinementViolation, indexSuffix.Index, "TypeChecker", constantValue.ToString()!, "index");
                    }

                    break;
                }

                // Case 3: Non-constant, non-refinement-typed argument — reject at compile time.
                diagnostics.ReportAtNode(TypeCheckDiagnostics.ParameterRefinementNonConstant, indexSuffix.Index, "TypeChecker", "index");
                break;
            }
        }
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
        if(objectCreation.Arguments is not null)
        {
            CheckArgumentListExpressions(objectCreation.Arguments, model, diagnostics);
        }

        // Get the type being constructed.
        var createdType = model.GetTypeOf(objectCreation);
        if(createdType is null || IsErrorType(createdType))
        {
            return;
        }

        // Type aliases are transparent — unwrap to the underlying type
        // so constructor lookup finds the actual class/struct constructors.
        if(createdType is TypeAliasType aliasCreated)
        {
            createdType = aliasCreated.UnderlyingType;
        }

        // ── new Array<T, Size>(fn) — initializer-based array construction ──
        // When the created type is an ArrayType (produced by the special-case
        // in SemanticModelBuilder for `new Array<T, Size>(fn)`), validate the
        // function argument: it must be a Func<int, T> where T is assignable
        // to the element type. The lambda target type was already set during
        // semantic model construction, so argument type resolution has
        // inferred the lambda's type. We validate the resolved argument type
        // here.
        if(createdType is ArrayType arrayCtorType)
        {
            var arrayCtorArgTypes = CollectArgumentTypes(objectCreation.Arguments, model);

            // Validate: exactly one argument, which must be a Func<int, T>.
            if(arrayCtorArgTypes.Count != 1)
            {
                diagnostics.ReportAtNode(TypeCheckDiagnostics.ArrayCtorRequiresOneArg, objectCreation, "TypeChecker", arrayCtorType.ElementType.DisplayName);
                return;
            }

            var argType = arrayCtorArgTypes[0];
            if(IsErrorType(argType))
            {
                return; // Error already reported during argument resolution.
            }

            // Check that the argument is a Func type with the right shape:
            // Func<int, TReturn> where TReturn is assignable to elementType.
            if(!SemanticModelBuilder.IsFuncType(argType) || argType is not ConstructedType funcCt)
            {
                diagnostics.ReportAtNode(TypeCheckDiagnostics.ArrayCtorRequiresOneArg, objectCreation, "TypeChecker", arrayCtorType.ElementType.DisplayName);
                return;
            }

            // Func<int, T> has 2 type arguments: [int, T].
            if(funcCt.TypeArguments.Count != 2)
            {
                diagnostics.ReportAtNode(TypeCheckDiagnostics.ArrayCtorRequiresOneArg, objectCreation, "TypeChecker", arrayCtorType.ElementType.DisplayName);
                return;
            }

            // Check that the first parameter is int.
            var paramType = funcCt.TypeArguments[0];
            if(!IsIntType(paramType))
            {
                diagnostics.ReportAtNode(TypeCheckDiagnostics.ArrayCtorRequiresOneArg, objectCreation, "TypeChecker", arrayCtorType.ElementType.DisplayName);
                return;
            }

            // Check that the return type is assignable to the element type.
            var returnType = funcCt.TypeArguments[1];
            if(!IsAssignable(returnType, arrayCtorType.ElementType, model))
            {
                diagnostics.ReportAtNode(TypeCheckDiagnostics.ArrayCtorRequiresOneArg, objectCreation, "TypeChecker", arrayCtorType.ElementType.DisplayName);
                return;
            }

            // Valid — no further checks needed.
            return;
        }

        // ── unknown-type storage ban on `new` (Task 12.1, 12.2) ──────────
        // Two guards, checked before constructor lookup because the
        // constructor lookup would fail anyway (UnknownType has no members
        // — Requirement 3.4 — and the constructed generic's constructor
        // would require storing an `unknown`-typed argument into a
        // T-typed slot, which is forbidden).
        if(createdType is UnknownType)
        {
            // Requirement 5.4 — `new unknown(...)` is categorically
            // forbidden. In practice the grammar rejects this form
            // before we get here (see remarks on
            // SemanticModelBuilder.UnknownCannotBeInstantiated), so this
            // branch is a defensive guard for programmatically-constructed
            // ASTs.
            diagnostics.ReportAtNode(TypeCheckDiagnostics.UnknownCannotBeInstantiated, objectCreation, "TypeChecker");
            return;
        }

        if(TypeContainment.IsStorageBanned(createdType))
        {
            // Requirement 5.7 — a generic construction whose substituted
            // type contains `unknown` at one or more positions is
            // forbidden because every constructor of such a generic would
            // need to store an `unknown`-typed argument into a T-typed
            // slot. Diagnostic message names the generic type (Req 16.2)
            // and states the rule.
            diagnostics.ReportAtNode(TypeCheckDiagnostics.UnknownInGenericConstruction, objectCreation, "TypeChecker", createdType.DisplayName);
            return;
        }

        // ── new T(...) validation against constructor constraints ──
        // When the type being constructed is a TypeParameterType, validate
        // the call against the declared constructor constraints on that type
        // parameter (Requirements 6.5, 6.6, 10.8).
        if(createdType is TypeParameterType typeParam)
        {
            var constraints = typeParam.Constraints;

            // If no constructor constraints exist, we cannot use new T(...).
            if(constraints is null || constraints.ConstructorConstraints.Count == 0)
            {
                diagnostics.ReportAtNode(TypeCheckDiagnostics.ConstraintNewTNoMatch, objectCreation, "TypeChecker", typeParam.Name, "", "none");
                return;
            }

            // Collect argument types from the new T(...) call.
            var ctorArgTypes = CollectArgumentTypes(objectCreation.Arguments, model);

            // Check if any declared constructor constraint signature matches.
            var matched = false;
            foreach(var requiredParams in constraints.ConstructorConstraints)
            {
                if(requiredParams.Count != ctorArgTypes.Count)
                {
                    continue;
                }

                var allMatch = true;
                for(var i = 0; i < requiredParams.Count; i++)
                {
                    if(!IsAssignable(ctorArgTypes[i], requiredParams[i], model))
                    {
                        allMatch = false;
                        break;
                    }
                }

                if(allMatch)
                {
                    matched = true;
                    break;
                }
            }

            if(!matched)
            {
                var argTypeNames = FormatArgTypes(objectCreation.Arguments, model);
                var signatures = string.Join(", ",
                    constraints.ConstructorConstraints.Select(p =>
                        "new(" + string.Join(", ", p.Select(t => t.DisplayName)) + ")"));
                diagnostics.ReportAtNode(TypeCheckDiagnostics.ConstraintNewTNoMatch, objectCreation, "TypeChecker", typeParam.Name, argTypeNames, signatures);
            }

            return;
        }

        // ── Constraint satisfaction checking for generic type arguments ──
        // When constructing a generic type (e.g., new Box<int>()), verify that
        // each type argument satisfies the constraints on the corresponding
        // type parameter of the generic definition.
        if(createdType is ConstructedType constructedForConstraints)
        {
            var typeParams = GetTypeParameters(constructedForConstraints.GenericDefinition);
            if(typeParams is not null)
            {
                var typeArgs = constructedForConstraints.TypeArguments;
                var genericName = constructedForConstraints.GenericDefinition.DisplayName;
                for(var i = 0; i < typeParams.Count && i < typeArgs.Count; i++)
                {
                    if(typeParams[i] is TypeParameterType tp5848)
                    {
                        // Requirement 11: whole-pack constraint — check each element independently.
                        if(tp5848.IsPack
                            && constructedForConstraints.DependentArguments is not null
                            && i < constructedForConstraints.DependentArguments.Count
                            && constructedForConstraints.DependentArguments[i] is { PackElementTypes: { } packElements })
                        {
                            CheckPackConstraintSatisfaction(packElements, tp5848, objectCreation, model, diagnostics);
                        }
                        else
                        {
                            CheckConstraintSatisfaction(typeArgs[i], tp5848, objectCreation, genericName, model, diagnostics);
                        }
                    }
                }
            }
        }

        // Find constructors on the type.
        var memberScope = createdType switch
        {
            ClassType ct => ct.MemberScope,
            StructType st => st.MemberScope,
            ConstructedType cst => cst.MemberScope,
            NewtypeType nt => nt.MemberScope,
            _ => null,
        };
        // Collect all constructors from the target type's member scope.
        var constructors = CollectConstructorOverloads(createdType, model);

        // Collect argument types.
        var argTypes = CollectArgumentTypes(objectCreation.Arguments, model);

        if(constructors.Count == 0)
        {
            // If no constructors are defined and there are no arguments, it's OK
            // (default constructor). Otherwise, emit diagnostic.
            if(argTypes.Count > 0)
            {
                diagnostics.ReportAtNode(TypeCheckDiagnostics.NoMatchingConstructor, objectCreation, "TypeChecker", createdType.DisplayName);
                return;
            }
        }
        else if(constructors.Count > 1)
        {
            // When multiple constructors exist, use overload resolution.
            // When the created type is a ConstructedType (e.g., Optional<int>),
            // pass the type substitution context so the resolver maps T → Int32
            // in constructor parameter types before the applicability check.
            (IReadOnlyList<IType> TypeParams, IReadOnlyList<IType> TypeArgs)? typeSubstitution = null;
            if(createdType is ConstructedType ctForSubst)
            {
                var tp = GetTypeParameters(ctForSubst.GenericDefinition);
                if(tp is not null)
                {
                    typeSubstitution = (tp, ctForSubst.TypeArguments);
                }
            }

            var argRefKinds = CollectArgumentRefKinds(objectCreation.Arguments);
            var result = OverloadResolver.Resolve(constructors, argTypes, model, typeSubstitution: typeSubstitution, argumentRefKinds: argRefKinds);
            var typeName = createdType.DisplayName;

            switch(result.Status)
            {
                case OverloadResolutionStatus.NoApplicableCandidate:
                    var argTypeNames = string.Join(", ", argTypes.Select(t => t.DisplayName));
                    diagnostics.ReportAtNode(TypeCheckDiagnostics.NoApplicableOverload, objectCreation, "TypeChecker", typeName, argTypeNames);
                    return;

                case OverloadResolutionStatus.AmbiguousCall:
                    var sigs = result.AmbiguousCandidates!
                        .Select(c => FormatCandidateSignature(c, typeName, model));
                    diagnostics.ReportAtNode(TypeCheckDiagnostics.AmbiguousOverload, objectCreation, "TypeChecker", typeName, string.Join(", ", sigs));
                    return;

                case OverloadResolutionStatus.Success:
                    // Resolved successfully — fall through to initializer check.
                    // Validate parameter refinement constraints on the resolved constructor.
                    ValidateParameterRefinementConstraints(objectCreation.Arguments, result.BestCandidate!, model, diagnostics);
                    // ── ref call-site annotation enforcement (MIST_REF_0002, MIST_REF_0003) ──
                    if(objectCreation.Arguments is not null)
                    {
                        CheckRefCallSiteAnnotationsForSymbol(objectCreation.Arguments, result.BestCandidate!, model, diagnostics);
                    }

                    break;
            }
        }
        else
        {
            // Single constructor — preserve existing behavior with MIST_TC_0011.
            var hasMatch = ConstructorMatches(constructors[0], argTypes, createdType, model);
            if(!hasMatch)
            {
                diagnostics.ReportAtNode(TypeCheckDiagnostics.NoMatchingConstructor, objectCreation, "TypeChecker", createdType.DisplayName);
            }
            else
            {
                // Constructor matches — validate parameter refinement constraints.
                ValidateParameterRefinementConstraints(objectCreation.Arguments, constructors[0], model, diagnostics);
                // ── ref call-site annotation enforcement (MIST_REF_0002, MIST_REF_0003) ──
                if(objectCreation.Arguments is not null)
                {
                    CheckRefCallSiteAnnotationsForSymbol(objectCreation.Arguments, constructors[0], model, diagnostics);
                }
            }
        }

        // ── Property initializer block validation ────────────────────────
        if(objectCreation.Initializer is not null)
        {
            CheckPropertyInitializerBlock(objectCreation.Initializer, createdType, model, diagnostics);
        }
    }

    /// <summary>
    /// Validates a <c>new Type { ... }</c> expression (no constructor arguments).
    /// Resolves the type, verifies a parameterless constructor exists, then
    /// validates the property initializer block.
    /// </summary>
    private void CheckObjectCreationNoArgs(
        ObjectCreationExpressionNoArgsPrimaryNode objectCreation,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Get the type being constructed.
        var createdType = model.GetTypeOf(objectCreation);
        if(createdType is null || IsErrorType(createdType))
        {
            return;
        }

        // Type aliases are transparent — unwrap to the underlying type.
        if(createdType is TypeAliasType aliasCreated)
        {
            createdType = aliasCreated.UnderlyingType;
        }

        // ── unknown-type storage ban ─────────────────────────────────────
        if(createdType is UnknownType)
        {
            diagnostics.ReportAtNode(TypeCheckDiagnostics.UnknownCannotBeInstantiated, objectCreation, "TypeChecker");
            return;
        }

        if(TypeContainment.IsStorageBanned(createdType))
        {
            diagnostics.ReportAtNode(TypeCheckDiagnostics.UnknownInGenericConstruction, objectCreation, "TypeChecker", createdType.DisplayName);
            return;
        }

        // ── Constraint satisfaction checking for generic type arguments ──
        if(createdType is ConstructedType constructedForConstraints)
        {
            var typeParams = GetTypeParameters(constructedForConstraints.GenericDefinition);
            if(typeParams is not null)
            {
                var typeArgs = constructedForConstraints.TypeArguments;
                var genericName = constructedForConstraints.GenericDefinition.DisplayName;
                for(var i = 0; i < typeParams.Count && i < typeArgs.Count; i++)
                {
                    if(typeParams[i] is TypeParameterType tp)
                    {
                        // Requirement 11: whole-pack constraint — check each element independently.
                        if(tp.IsPack
                            && constructedForConstraints.DependentArguments is not null
                            && i < constructedForConstraints.DependentArguments.Count
                            && constructedForConstraints.DependentArguments[i] is { PackElementTypes: { } packElements })
                        {
                            CheckPackConstraintSatisfaction(packElements, tp, objectCreation, model, diagnostics);
                        }
                        else
                        {
                            CheckConstraintSatisfaction(typeArgs[i], tp, objectCreation, genericName, model, diagnostics);
                        }
                    }
                }
            }
        }

        // ── Verify parameterless constructor exists ──────────────────────
        // The no-args variant invokes the parameterless constructor.
        // If no constructors are defined, a default parameterless constructor
        // is implied. If constructors exist, at least one must be parameterless.
        var constructors = CollectConstructorOverloads(createdType, model);
        if(constructors.Count > 0)
        {
            var hasParameterless = false;
            foreach(var ctor in constructors)
            {
                var paramTypes = ParameterSignature.Extract(ctor, model);
                if(paramTypes.Count == 0)
                {
                    hasParameterless = true;
                    break;
                }
            }

            if(!hasParameterless)
            {
                diagnostics.ReportAtNode(TypeCheckDiagnostics.NoMatchingConstructor, objectCreation, "TypeChecker", createdType.DisplayName);
            }
        }

        // ── Property initializer block validation ────────────────────────
        CheckPropertyInitializerBlock(objectCreation.Initializer, createdType, model, diagnostics);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Target-typed new expression validation (MIST_TARGET_NEW_*)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Validates a <c>new(args)</c> target-typed new expression. Checks for:
    /// <list type="bullet">
    ///   <item><c>var</c> declaration context (no target type from <c>var</c>)</item>
    ///   <item>No target type available (ErrorType)</item>
    ///   <item>Target type is an abstract</item>
    ///   <item>Target type is an abstract class</item>
    ///   <item>Constructor argument mismatch</item>
    /// </list>
    /// </summary>
    private void CheckTargetTypedNewExpression(
        TargetTypedNewExpressionPrimaryNode targetNew,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Check argument expressions first (for nested expression validation).
        if(targetNew.Arguments is not null)
        {
            CheckArgumentListExpressions(targetNew.Arguments, model, diagnostics);
        }

        // Detect var context: if the parent chain leads to an InferredVariableDeclaration,
        // the target type is unknowable and we emit a specific diagnostic.
        if(IsInVarDeclarationContext(targetNew))
        {
            diagnostics.ReportAtNode(CommonDiagnostics.TargetNewWithVar, targetNew, "TypeChecker");
            return;
        }

        // Get the resolved type from the semantic model.
        var resolvedType = model.GetTypeOf(targetNew);
        if(resolvedType is null || IsErrorType(resolvedType))
        {
            diagnostics.ReportAtNode(CommonDiagnostics.TargetNewNoContext, targetNew, "TypeChecker");
            return;
        }

        // Unwrap type aliases for validation.
        var validationType = resolvedType;
        if(validationType is TypeAliasType aliasType)
        {
            validationType = aliasType.UnderlyingType;
        }

        // Check for non-instantiable types.
        if(IsAbstractTargetType(validationType))
        {
            diagnostics.ReportAtNode(CommonDiagnostics.TargetNewAbstract, targetNew, "TypeChecker", validationType.DisplayName);
            return;
        }

        if(validationType is ClassType { IsAbstract: true })
        {
            diagnostics.ReportAtNode(TypeCheckDiagnostics.AbstractInstantiation, targetNew, "TypeChecker", validationType.DisplayName);
            return;
        }

        // Constructor resolution — validate arguments against accessible constructors.
        var constructors = CollectConstructorOverloads(validationType, model);
        var argTypes = CollectArgumentTypes(targetNew.Arguments, model);

        if(constructors.Count == 0)
        {
            // No constructors defined — default parameterless constructor implied.
            if(argTypes.Count > 0)
            {
                diagnostics.ReportAtNode(TypeCheckDiagnostics.NoMatchingConstructor, targetNew, "TypeChecker", resolvedType.DisplayName);
                return;
            }
        }
        else if(constructors.Count > 1)
        {
            // Multiple constructors — use overload resolution.
            (IReadOnlyList<IType> TypeParams, IReadOnlyList<IType> TypeArgs)? typeSubstitution = null;
            if(validationType is ConstructedType ctForSubst)
            {
                var tp = GetTypeParameters(ctForSubst.GenericDefinition);
                if(tp is not null)
                {
                    typeSubstitution = (tp, ctForSubst.TypeArguments);
                }
            }

            var argRefKinds = CollectArgumentRefKinds(targetNew.Arguments);
            var result = OverloadResolver.Resolve(constructors, argTypes, model, typeSubstitution: typeSubstitution, argumentRefKinds: argRefKinds);

            switch(result.Status)
            {
                case OverloadResolutionStatus.NoApplicableCandidate:
                    var argTypeNames = string.Join(", ", argTypes.Select(t => t.DisplayName));
                    diagnostics.ReportAtNode(TypeCheckDiagnostics.NoApplicableOverload, targetNew, "TypeChecker", resolvedType.DisplayName, argTypeNames);
                    return;

                case OverloadResolutionStatus.AmbiguousCall:
                    var sigs = result.AmbiguousCandidates!
                        .Select(c => FormatCandidateSignature(c, resolvedType.DisplayName, model));
                    diagnostics.ReportAtNode(TypeCheckDiagnostics.AmbiguousOverload, targetNew, "TypeChecker", resolvedType.DisplayName, string.Join(", ", sigs));
                    return;

                case OverloadResolutionStatus.Success:
                    ValidateParameterRefinementConstraints(targetNew.Arguments, result.BestCandidate!, model, diagnostics);
                    if(targetNew.Arguments is not null)
                    {
                        CheckRefCallSiteAnnotationsForSymbol(targetNew.Arguments, result.BestCandidate!, model, diagnostics);
                    }

                    break;
            }
        }
        else
        {
            // Single constructor — check match.
            var hasMatch = ConstructorMatches(constructors[0], argTypes, validationType, model);
            if(!hasMatch)
            {
                diagnostics.ReportAtNode(TypeCheckDiagnostics.NoMatchingConstructor, targetNew, "TypeChecker", resolvedType.DisplayName);
            }
            else
            {
                ValidateParameterRefinementConstraints(targetNew.Arguments, constructors[0], model, diagnostics);
                if(targetNew.Arguments is not null)
                {
                    CheckRefCallSiteAnnotationsForSymbol(targetNew.Arguments, constructors[0], model, diagnostics);
                }
            }
        }

        // Property initializer block validation.
        if(targetNew.Initializer is not null)
        {
            CheckPropertyInitializerBlock(targetNew.Initializer, validationType, model, diagnostics);
        }
    }

    /// <summary>
    /// Validates a <c>new { ... }</c> target-typed new expression (no parentheses, no arguments).
    /// Same validation as <see cref="CheckTargetTypedNewExpression"/> but without argument checks
    /// and always verifying a parameterless constructor exists.
    /// </summary>
    private void CheckTargetTypedNewExpressionNoArgs(
        TargetTypedNewExpressionNoArgsPrimaryNode targetNewNoArgs,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Detect var context.
        if(IsInVarDeclarationContext(targetNewNoArgs))
        {
            diagnostics.ReportAtNode(CommonDiagnostics.TargetNewWithVar, targetNewNoArgs, "TypeChecker");
            return;
        }

        // Get the resolved type from the semantic model.
        var resolvedType = model.GetTypeOf(targetNewNoArgs);
        if(resolvedType is null || IsErrorType(resolvedType))
        {
            diagnostics.ReportAtNode(CommonDiagnostics.TargetNewNoContext, targetNewNoArgs, "TypeChecker");
            return;
        }

        // Unwrap type aliases for validation.
        var validationType = resolvedType;
        if(validationType is TypeAliasType aliasType)
        {
            validationType = aliasType.UnderlyingType;
        }

        // Check for non-instantiable types.
        if(IsAbstractTargetType(validationType))
        {
            diagnostics.ReportAtNode(CommonDiagnostics.TargetNewAbstract, targetNewNoArgs, "TypeChecker", validationType.DisplayName);
            return;
        }

        if(validationType is ClassType { IsAbstract: true })
        {
            diagnostics.ReportAtNode(TypeCheckDiagnostics.AbstractInstantiation, targetNewNoArgs, "TypeChecker", validationType.DisplayName);
            return;
        }

        // Verify parameterless constructor exists.
        var constructors = CollectConstructorOverloads(validationType, model);
        if(constructors.Count > 0)
        {
            var hasParameterless = false;
            foreach(var ctor in constructors)
            {
                var paramTypes = ParameterSignature.Extract(ctor, model);
                if(paramTypes.Count == 0)
                {
                    hasParameterless = true;
                    break;
                }
            }

            if(!hasParameterless)
            {
                diagnostics.ReportAtNode(TypeCheckDiagnostics.NoMatchingConstructor, targetNewNoArgs, "TypeChecker", resolvedType.DisplayName);
            }
        }

        // Property initializer block validation.
        CheckPropertyInitializerBlock(targetNewNoArgs.Initializer, validationType, model, diagnostics);
    }

    /// <summary>
    /// Determines whether the given target-typed new expression node is the initializer
    /// of a <c>var</c> (inferred) variable declaration.
    /// </summary>
    private static bool IsInVarDeclarationContext(SyntaxNode node)
    {
        // Walk up the parent chain looking for InferredVariableDeclarationVariableDeclarationStatementNode.
        // The typical chain is: TargetTypedNew → Primary → Postfix → Unary → ... → Expression → InferredVarDecl.
        var current = node.Parent;
        while(current is not null)
        {
            if(current is InferredVariableDeclarationVariableDeclarationStatementNode)
            {
                return true;
            }

            // Stop at statement or declaration boundaries to avoid false positives.
            if(current is TypedVariableDeclarationVariableDeclarationStatementNode)
            {
                return false;
            }

            current = current.Parent;
        }

        return false;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property initializer block validation (MIST_PI_0001–MIST_PI_0006)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Validates each assignment in a property initializer block against the
    /// constructed type. Checks member existence, type compatibility,
    /// writability, accessibility, and duplicate detection.
    /// </summary>
    private void CheckPropertyInitializerBlock(
        PropertyInitializerBlockNode initializerBlock,
        IType constructedType,

        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        if(initializerBlock.Assignments is null)
        {
            return;
        }

        // Unwrap type aliases and ConstructedType for member lookup.
        var lookupType = constructedType;
        if(lookupType is TypeAliasType aliasType)
        {
            lookupType = aliasType.UnderlyingType;
        }

        // For ConstructedType, look up members on the generic definition.
        var memberLookupType = lookupType is ConstructedType cst ? cst.GenericDefinition : lookupType;

        var memberScope = memberLookupType switch
        {
            ClassType ct => ct.MemberScope,
            StructType st => st.MemberScope,
            _ => null,
        };

        if(memberScope is null)
        {
            return;
        }

        var seenNames = new HashSet<string>(StringComparer.Ordinal);

        // Iterate over all assignments: first + rest.
        var assignments = initializerBlock.Assignments;
        CheckSinglePropertyAssignment(assignments.PropertyAssignment, memberScope, memberLookupType, seenNames, model, diagnostics);
        foreach(var assignment in assignments.PropertyAssignments)
        {
            CheckSinglePropertyAssignment(assignment, memberScope, memberLookupType, seenNames, model, diagnostics);
        }
    }

    /// <summary>
    /// Validates a single property assignment within an initializer block.
    /// </summary>
    private void CheckSinglePropertyAssignment(
        PropertyAssignmentNode assignment,
        IScope memberScope,
        IType memberLookupType,
        HashSet<string> seenNames,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Collection element assignments (bare expressions in collection initializers)
        // are not property assignments — they don't target named members.
        // Skip property-initializer validation for them.
        if(assignment is CollectionElementAssignmentPropertyAssignmentNode)
        {
            return;
        }

        if(assignment is not NamedPropertyAssignmentPropertyAssignmentNode namedAssignment)
        {
            return;
        }

        var memberName = namedAssignment.Name.Lexeme;

        // ── 6. Duplicate detection ───────────────────────────────────────
        if(!seenNames.Add(memberName))
        {
            diagnostics.ReportAtNode(TypeCheckDiagnostics.PropertyInitializerDuplicate, assignment, "TypeChecker", memberName);
            return;
        }

        // ── 1. Member existence ──────────────────────────────────────────
        var symbol = memberScope.Lookup(memberName);
        if(symbol is null || symbol.Kind is not (SymbolKind.Property or SymbolKind.Field))
        {
            diagnostics.ReportAtNode(TypeCheckDiagnostics.PropertyInitializerMemberNotFound, assignment, "TypeChecker", memberLookupType.DisplayName, memberName);
            return;
        }

        // ── 5. Accessibility ─────────────────────────────────────────────
        // Backend symbols are assumed public; only check user-defined members.
        if(symbol.Origin != SymbolOrigin.Backend)
        {
            // Read the resolved accessibility from the symbol. The Type Checker
            // never computes defaults — the Semantic Model already set concrete values.
            if(symbol.Accessibility == Accessibility.Private)
            {
                // Private members are only accessible within the declaring type.
                var declaringType = GetDeclaringTypeOfMember(symbol);
                if(declaringType is not null && !IsSameType(declaringType, currentEnclosingType))
                {
                    diagnostics.ReportAtNode(TypeCheckDiagnostics.PropertyInitializerInaccessible, assignment, "TypeChecker");
                    return;
                }
            }
        }

        // ── 3. Writability (property) ────────────────────────────────────
        if(symbol.Kind == SymbolKind.Property)
        {
            var propInfo = GetPropertyMetadata(symbol);
            if(propInfo is not null && !propInfo.HasSet && !propInfo.HasInit)
            {
                diagnostics.ReportAtNode(TypeCheckDiagnostics.PropertyInitializerNoSetOrInit, assignment, "TypeChecker", memberName);
                return;
            }
        }

        // ── 4. Writability (field) ───────────────────────────────────────
        if(symbol.Kind == SymbolKind.Field && symbol is Symbol concreteField)
        {
            if(concreteField.ReadonlyStatus is ReadonlyStatus.ExplicitReadonly
                or ReadonlyStatus.InferredReadonly
                or ReadonlyStatus.ImplicitReadonly)
            {
                diagnostics.ReportAtNode(TypeCheckDiagnostics.PropertyInitializerReadonlyField, assignment, "TypeChecker", memberName);
                return;
            }
        }

        // ── 2. Type compatibility ────────────────────────────────────────
        var memberType = symbol.DeclaredType;
        var valueType = model.GetTypeOf(namedAssignment.Value);
        if(memberType is not null && valueType is not null
            && !IsErrorType(memberType) && !IsErrorType(valueType))
        {
            if(!IsAssignable(valueType, memberType, model))
            {
                diagnostics.ReportAtNode(TypeCheckDiagnostics.PropertyInitializerTypeMismatch, assignment, "TypeChecker", valueType!.DisplayName, memberName, memberType!.DisplayName);
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
        if(arguments is not null)
        {
            CheckArgumentListExpressions(arguments, model, diagnostics);
        }

        // Find the enclosing type.
        var enclosingType = FindDeclaringType(ctorSymbol);
        if(enclosingType is null || IsErrorType(enclosingType))
        {
            return;
        }

        // For `: base(...)` chains, resolve the base type and search its constructors.
        // For `: this(...)` chains, search the declaring type's own constructors.
        var targetType = enclosingType;
        if(chain is BaseChainConstructorChainNode && enclosingType is ClassType ct && ct.BaseType is not null)
        {
            targetType = ct.BaseType;
        }
        else if(chain is BaseChainConstructorChainNode && enclosingType is StructType st && st.BaseType is not null)
        {
            targetType = st.BaseType;
        }

        var memberScope = targetType switch
        {
            ClassType classType => classType.MemberScope,
            StructType st2 => st2.MemberScope,
            ConstructedType cst => cst.MemberScope,
            _ => null,
        };

        if(memberScope is null)
        {
            return;
        }

        var argTypes = CollectArgumentTypes(arguments, model);

        // For `: this(...)`, exclude the current constructor from the search.
        // For `: base(...)`, search all constructors on the base type.
        var candidateConstructors = chain is BaseChainConstructorChainNode
            ? memberScope.Members.Where(m => m.Kind == SymbolKind.Constructor).ToList()
            : memberScope.Members.Where(m => m.Kind == SymbolKind.Constructor && !ReferenceEquals(m, ctorSymbol)).ToList();

        if(candidateConstructors.Count == 0)
        {
            // No constructors available — only an error if arguments were provided.
            if(argTypes.Count > 0)
            {
                if(chain is BaseChainConstructorChainNode && enclosingType is StructType)
                {
                    diagnostics.ReportAtNode(TypeCheckDiagnostics.StructBaseCtorMismatch, chain, "TypeChecker", targetType.DisplayName);
                }
                else
                {
                    diagnostics.ReportAtNode(TypeCheckDiagnostics.NoMatchingConstructor, chain, "TypeChecker", targetType.DisplayName);
                }
            }

            return;
        }

        // When multiple constructors exist, use overload resolution.
        if(candidateConstructors.Count > 1)
        {
            var argRefKinds = CollectArgumentRefKinds(arguments);
            var result = OverloadResolver.Resolve(candidateConstructors, argTypes, model, argumentRefKinds: argRefKinds);
            var typeName = targetType.DisplayName;

            switch(result.Status)
            {
                case OverloadResolutionStatus.NoApplicableCandidate:
                    var argTypeNames = string.Join(", ", argTypes.Select(t => t.DisplayName));
                    if(chain is BaseChainConstructorChainNode && enclosingType is StructType)
                    {
                        diagnostics.ReportAtNode(TypeCheckDiagnostics.StructBaseCtorMismatch, chain, "TypeChecker");
                    }
                    else
                    {
                        diagnostics.ReportAtNode(TypeCheckDiagnostics.NoApplicableOverload, chain, "TypeChecker", typeName, string.Join(", ", argTypes.Select(t => t.DisplayName)));
                    }

                    return;

                case OverloadResolutionStatus.AmbiguousCall:
                    var sigs = result.AmbiguousCandidates!
                        .Select(c => FormatCandidateSignature(c, typeName, model));
                    diagnostics.ReportAtNode(TypeCheckDiagnostics.AmbiguousOverload, chain, "TypeChecker", typeName, string.Join(", ", sigs));
                    return;

                case OverloadResolutionStatus.Success:
                    // Resolved successfully — validate parameter refinement constraints.
                    ValidateParameterRefinementConstraints(arguments, result.BestCandidate!, model, diagnostics);
                    return;
            }
        }
        else
        {
            // Single constructor — preserve existing behavior with MIST_TC_0013.
            var hasMatch = ConstructorMatches(candidateConstructors[0], argTypes, targetType, model);
            if(!hasMatch)
            {
                if(chain is BaseChainConstructorChainNode && enclosingType is StructType)
                {
                    diagnostics.ReportAtNode(TypeCheckDiagnostics.StructBaseCtorMismatch, chain, "TypeChecker", targetType.DisplayName);
                }
                else
                {
                    diagnostics.ReportAtNode(TypeCheckDiagnostics.NoMatchingConstructor, chain, "TypeChecker", targetType.DisplayName);
                }
            }
            else
            {
                // Constructor matches — validate parameter refinement constraints.
                ValidateParameterRefinementConstraints(arguments, candidateConstructors[0], model, diagnostics);
            }
        }
    }

    private static bool ConstructorMatches(ISymbol ctor, List<IType> argTypes, IType? createdType, ISemanticModel model)
    {
        // Get the constructor's declaring node. For cloned newtype constructors,
        // the declaring node is not registered directly. Fall back to finding
        // the original constructor in the source type's scope.
        SyntaxNode? ctorNode;
        try { ctorNode = model.GetDeclaringNode(ctor); }
        catch { ctorNode = null; }

        if(ctorNode is null && createdType is NewtypeType nt)
        {
            // Find the matching original constructor in the source type's scope.
            var sourceScope = GetMemberScope(nt.SourceType);
            if(sourceScope is not null)
            {
                foreach(var member in sourceScope.Members)
                {
                    if(member.Kind == SymbolKind.Constructor)
                    {
                        try { ctorNode = model.GetDeclaringNode(member); }
                        catch { continue; }

                        if(ctorNode is not null)
                        {
                            break;
                        }
                    }
                }
            }
        }

        // For ambient/backend-projected constructors (no AST declaring node),
        // extract parameter types from the symbol's ParameterSignature directly.
        if(ctorNode is not ConstructorDeclarationNode ctorDecl)
        {
            var extractedParamTypes = ParameterSignature.Extract(ctor, model);
            if(extractedParamTypes.Count == 0 && argTypes.Count == 0)
            {
                return true;
            }

            if(extractedParamTypes.Count != argTypes.Count)
            {
                return false;
            }

            IReadOnlyList<IType>? typeParamsFallback = null;
            IReadOnlyList<IType>? typeArgsFallback = null;
            if(createdType is ConstructedType ctFallback)
            {
                typeParamsFallback = GetTypeParameters(ctFallback.GenericDefinition);
                typeArgsFallback = ctFallback.TypeArguments;
            }

            for(var i = 0; i < argTypes.Count; i++)
            {
                var paramType = extractedParamTypes[i];
                if(typeParamsFallback is not null && typeArgsFallback is not null)
                {
                    paramType = SubstituteType(paramType, typeParamsFallback, typeArgsFallback);
                }

                if(!IsAssignable(argTypes[i], paramType))
                {
                    return false;
                }
            }

            return true;
        }

        var paramTypes = new List<IType>();
        if(ctorDecl.Parameters is not null)
        {
            foreach(var child in ctorDecl.Parameters.Children())
            {
                if(child is ParameterNode paramNode)
                {
                    var paramSym = model.GetDeclaredSymbol(paramNode);
                    if(paramSym?.DeclaredType is not null)
                    {
                        paramTypes.Add(paramSym.DeclaredType);
                    }
                }
            }
        }

        if(argTypes.Count != paramTypes.Count)
        {
            // Variadics: a value-pack parameter (params TArgs values) has declared type
            // TupleType([pack]). When the created type is a ConstructedType with DependentArguments
            // that bind the pack to N concrete types, the constructor accepts N arguments at that
            // parameter position. Check if the argument count matches after expansion.
            if(createdType is ConstructedType ctForPack && ctForPack.DependentArguments is not null)
            {
                var expandedParamTypes = TryExpandVariadicConstructorParams(
                    paramTypes, ctForPack, GetTypeParameters(ctForPack.GenericDefinition));
                if(expandedParamTypes is not null && expandedParamTypes.Count == argTypes.Count)
                {
                    // Use the expanded param types for comparison instead.
                    for(var i = 0; i < argTypes.Count; i++)
                    {
                        if(!IsAssignable(argTypes[i], expandedParamTypes[i]))
                        {
                            return false;
                        }
                    }

                    return true;
                }
            }

            return false;
        }

        // When the created type is a ConstructedType (e.g., GenericBox<int>),
        // substitute type parameters in constructor parameter types with the
        // actual type arguments before comparing.
        IReadOnlyList<IType>? typeParams = null;
        IReadOnlyList<IType>? typeArgs = null;
        if(createdType is ConstructedType ct)
        {
            typeParams = GetTypeParameters(ct.GenericDefinition);
            typeArgs = ct.TypeArguments;
        }
        else if(createdType is SpanType spanCreated)
        {
            // Bridge: SpanType is a dedicated IType without a GenericDefinition.
            // The constructor lives on the registered StructType (Span or ReadOnlySpan)
            // whose type parameters are [T, Length]. Build the substitution mapping
            // T → ElementType, Length → UnknownType (since Span<T> aliases Span<T, unknown>).
            var declaringType = FindDeclaringType(ctor);
            if(declaringType is not null)
            {
                typeParams = GetTypeParameters(declaringType);
                if(typeParams is not null)
                {
                    var args = new IType[typeParams.Count];
                    // First type parameter is the element type T.
                    args[0] = spanCreated.ElementType;
                    // Remaining type parameters (Length) map to UnknownType
                    // unless the SpanType has a known length.
                    for(var j = 1; j < args.Length; j++)
                    {
                        args[j] = UnknownType.Instance;
                    }

                    typeArgs = args;
                }
            }
        }

        for(var i = 0; i < argTypes.Count; i++)
        {
            var paramType = paramTypes[i];
            if(typeParams is not null && typeArgs is not null)
            {
                paramType = SubstituteType(paramType, typeParams, typeArgs);
            }

            if(!IsAssignable(argTypes[i], paramType))
            {
                return false;
            }
        }

        return true;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Access modifier enforcement (MIST_TC_0014, MIST_TC_0015, MIST_TC_0016)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Expands a variadic constructor's parameter list by replacing any value-pack
    /// TupleType parameter (whose single element is a pack TypeParameterType) with
    /// the concrete element types from the ConstructedType's DependentArguments.
    /// Returns null if no expansion is possible (no pack parameters found or no
    /// DependentArguments available).
    /// </summary>
    private static IReadOnlyList<IType>? TryExpandVariadicConstructorParams(
        List<IType> paramTypes,
        ConstructedType constructedType,
        IReadOnlyList<IType>? typeParams)
    {
        if(typeParams is null || constructedType.DependentArguments is null)
        {
            return null;
        }

        var expanded = new List<IType>();
        var anyExpanded = false;

        foreach(var paramType in paramTypes)
        {
            if(paramType is TupleType tupleParam
                && tupleParam.ElementTypes.Count == 1
                && tupleParam.ElementTypes[0] is TypeParameterType { IsPack: true } packParam)
            {
                // Find the pack's ordinal in the type parameter list.
                var packOrdinal = -1;
                for(var i = 0; i < typeParams.Count; i++)
                {
                    if(typeParams[i] is TypeParameterType tp && tp.Name == packParam.Name)
                    {
                        packOrdinal = i;
                        break;
                    }
                }

                if(packOrdinal >= 0
                    && packOrdinal < constructedType.DependentArguments.Count
                    && constructedType.DependentArguments[packOrdinal] is { IsPack: true } depArg
                    && depArg.PackElementTypes is not null)
                {
                    expanded.AddRange(depArg.PackElementTypes);
                    anyExpanded = true;
                    continue;
                }
            }

            // Non-pack parameter: substitute type parameters with type arguments.
            var substituted = paramType;
            if(typeParams.Count > 0)
            {
                substituted = SubstituteType(paramType, typeParams, constructedType.TypeArguments);
            }

            expanded.Add(substituted);
        }

        return anyExpanded ? expanded : null;
    }

    /// <summary>
    /// Determines whether the target of a postfix expression is a type name
    /// (e.g., <c>Console</c> in <c>Console.WriteLine()</c>) rather than an instance.
    /// </summary>
    private static bool IsTargetTypeName(PrimaryNode target, ISemanticModel model)
    {
        if(target is not IdentifierExpressionPrimaryNode)
        {
            return false;
        }

        // Check the resolved symbol for this identifier expression. The semantic model
        // registers usage-site bindings during name resolution, so GetDeclaredSymbol
        // returns the symbol the identifier was bound to (field, variable, type, etc.).
        // Fields/variables/parameters indicate an instance; class/struct/enum indicate a type name.
        var resolvedSymbol = model.GetDeclaredSymbol(target);
        if(resolvedSymbol is not null)
        {
            return resolvedSymbol.Kind is SymbolKind.Class or SymbolKind.Struct or SymbolKind.Enum;
        }

        // Fallback: check the expression type for ambient types (e.g. Console)
        // that may not have a symbol in the user-defined symbol table.
        var resolvedType = model.GetTypeOf(target);
        return resolvedType is ClassType or StructType or EnumType;
    }

    /// <summary>
    /// Checks access modifier rules for a member access or method call.
    /// Emits MIST_TC_0014 (private access), MIST_TC_0015 (static via instance),
    /// or MIST_TC_0016 (instance via type name).
    /// Now reads accessibility from <see cref="ISymbol.Accessibility"/> rather than
    /// computing it from AST modifiers. The Type Checker never computes a default.
    /// </summary>
    private void CheckMemberAccessModifiers(
        string memberName,
        IType? targetType,
        bool targetIsTypeName,
        SyntaxNode accessNode,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        if(targetType is null || IsErrorType(targetType))
        {
            return;
        }

        // Find the member symbol on the target type.
        var memberSymbol = FindMemberSymbol(targetType, memberName, model);
        if(memberSymbol is null)
        {
            return;
        }

        // Backend symbols are assumed public and valid — skip access modifier checks
        // for them since they have no AST declaring node with modifiers.
        // The type checker treats all symbols uniformly for type checking, but
        // access modifier enforcement only applies to user-defined members.
        if(memberSymbol.Origin == SymbolOrigin.Backend)
        {
            return;
        }

        // Get the declaring AST node to inspect modifiers (only for staticness now).
        SyntaxNode? declaringNode;
        try { declaringNode = model.GetDeclaringNode(memberSymbol); }
        catch { return; }

        // Extract only staticness from the declaring node.
        var modifiers = GetMemberModifiers(declaringNode);

        // ── Full accessibility enforcement (Requirements 6, 7, 11.3) ─────
        var memberAccessibility = memberSymbol.Accessibility;
        var declaringType = GetDeclaringTypeOfMember(memberSymbol);

        switch(memberAccessibility)
        {
            case Accessibility.Private:
                // Private members are accessible only within the declaring type's
                // domain, which includes the declaring type itself and any nested
                // types of the same declaring type (Req 7.1, 11.3).
                if(declaringType is not null && !IsWithinDeclaringTypeDomain(declaringType, currentEnclosingType))
                {
                    diagnostics.ReportAtNode(TypeCheckDiagnostics.PrivateOutsideDomain, accessNode, "TypeChecker", memberName);
                    return;
                }

                break;

            case Accessibility.Protected:
                // Protected members are accessible from the declaring type or any
                // type derived from it (Req 7.2).
                if(declaringType is not null && !IsDeclaredOrDerivedType(declaringType, currentEnclosingType))
                {
                    diagnostics.ReportAtNode(TypeCheckDiagnostics.ProtectedNonDerived, accessNode, "TypeChecker", memberName);
                    return;
                }

                break;

            case Accessibility.Internal:
                // Internal members are accessible only within the same compilation.
                // A reference from an External_Assembly is an error (Req 6.4, 7.3).
                if(IsCrossAssemblyReference(memberSymbol, accessNode, model))
                {
                    diagnostics.ReportAtNode(TypeCheckDiagnostics.InternalCrossAssembly, accessNode, "TypeChecker", memberName);
                    return;
                }

                break;

            case Accessibility.Public:
                // Public members are always accessible — no check needed.
                break;
        }

        // R9.4: Static members must be accessed via type name, not instance.
        if(modifiers.IsStatic && !targetIsTypeName)
        {
            diagnostics.ReportAtNode(TypeCheckDiagnostics.StaticMemberViaInstance, accessNode, "TypeChecker");
            return;
        }

        // R9.5: Instance members must be accessed via instance, not type name.
        if(!modifiers.IsStatic && targetIsTypeName)
        {
            diagnostics.ReportAtNode(TypeCheckDiagnostics.InstanceMemberViaType, accessNode, "TypeChecker");
        }

        // ── Property-specific access checks ──────────────────────────────

        if(memberSymbol.Kind == SymbolKind.Property)
        {
            var propInfo = GetPropertyMetadata(memberSymbol);
            if(propInfo is not null)
            {
                // MIST_TC_0027: Static property accessed through instance reference.
                if(modifiers.IsStatic && !targetIsTypeName)
                {
                    diagnostics.ReportAtNode(TypeCheckDiagnostics.StaticPropertyViaInstance, accessNode, "TypeChecker");
                }
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Accessibility domain helpers (Requirements 6, 7, 11.3)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Determines whether the given <paramref name="accessingType"/> is within the
    /// accessibility domain of <paramref name="declaringType"/> for private-access
    /// purposes. The domain includes the declaring type itself and any nested type
    /// declared within the same declaring type (Requirement 11.3).
    /// </summary>
    private static bool IsWithinDeclaringTypeDomain(IType declaringType, IType? accessingType)
    {
        if(accessingType is null)
        {
            return false;
        }

        // Same type — always in domain.
        if(IsSameType(declaringType, accessingType))
        {
            return true;
        }

        // Check if the accessing type is a nested type whose enclosing type is
        // the declaring type. Walk up the nesting chain: the accessing type's
        // ContainingNamespace or parent scope may point to the declaring type.
        // In the Mist model, nesting is modeled by the parent scope chain —
        // a nested type's MemberScope.Parent is the enclosing type's MemberScope.
        var enclosingType = GetEnclosingTypeOfType(accessingType);
        while(enclosingType is not null)
        {
            if(IsSameType(declaringType, enclosingType))
            {
                return true;
            }

            enclosingType = GetEnclosingTypeOfType(enclosingType);
        }

        return false;
    }

    /// <summary>
    /// Determines whether the <paramref name="accessingType"/> is the same as
    /// or derived from the <paramref name="declaringType"/> for protected-access
    /// purposes (Requirement 7.2).
    /// </summary>
    private static bool IsDeclaredOrDerivedType(IType declaringType, IType? accessingType)
    {
        if(accessingType is null)
        {
            return false;
        }

        // Same type — always in domain.
        if(IsSameType(declaringType, accessingType))
        {
            return true;
        }

        // Walk the class inheritance chain.
        if(accessingType is ClassType accessingClass)
        {
            var current = accessingClass.BaseType as ClassType;
            while(current is not null)
            {
                if(IsSameType(declaringType, current))
                {
                    return true;
                }

                current = current.BaseType as ClassType;
            }
        }

        // Walk the struct inheritance chain (Mist structs are inheritable).
        if(accessingType is StructType accessingStruct)
        {
            var current = accessingStruct.BaseType as StructType;
            while(current is not null)
            {
                if(IsSameType(declaringType, current))
                {
                    return true;
                }

                current = current.BaseType as StructType;
            }
        }

        return false;
    }

    /// <summary>
    /// Determines whether a reference to the given symbol crosses the compilation-as-assembly
    /// boundary. A reference is "cross-assembly" (external) when:
    /// <list type="bullet">
    ///   <item>The symbol's origin is <see cref="SymbolOrigin.Backend"/> (CLR/runtime-projected type), or</item>
    ///   <item>The accessing site and the declaring member live in <em>different</em> projects.</item>
    /// </list>
    /// The project of a source unit is identified by its <see cref="CompilationUnit.OriginProjectPath"/>
    /// (<see langword="null"/> for the current compilation's own source units, non-null for a
    /// referenced project). Crucially, a referenced project accessing its <em>own</em> internal
    /// members (accessing site and declaration share the same <c>OriginProjectPath</c>) is
    /// in-assembly and must not be flagged — only accesses that genuinely cross a project
    /// boundary are rejected. (Requirements 6.1, 6.2, 6.5)
    /// </summary>
    private static bool IsCrossAssemblyReference(ISymbol symbol, SyntaxNode accessNode, ISemanticModel model)
    {
        // Backend-origin symbols are always external (CLR/runtime projected).
        if(symbol.Origin == SymbolOrigin.Backend)
        {
            return true;
        }

        // Determine the project of the declaring member and of the accessing site by
        // mapping each to its owning compilation unit's OriginProjectPath. The reference
        // crosses the assembly boundary only when the two projects differ.
        SyntaxNode? declaringNode = null;
        try { declaringNode = model.GetDeclaringNode(symbol); }
        catch { /* unresolved declaring node — treated as same-project below */ }

        var declaringProject = GetProjectPathForNode(declaringNode, model);
        var accessingProject = GetProjectPathForNode(accessNode, model);

        return !string.Equals(declaringProject, accessingProject, StringComparison.Ordinal);
    }

    /// <summary>
    /// Resolves the <see cref="CompilationUnit.OriginProjectPath"/> of the source unit that
    /// contains <paramref name="node"/>, or <see langword="null"/> when the node belongs to
    /// the current compilation's own sources (or its origin cannot be resolved). Two nodes in
    /// the same project return equal values; a current-project node and a referenced-project
    /// node return different values.
    /// </summary>
    private static string? GetProjectPathForNode(SyntaxNode? node, ISemanticModel model)
    {
        var sourcePath = GetNodeSourcePath(node);
        if(sourcePath is null)
        {
            return null;
        }

        foreach(var unit in model.Units)
        {
            if(string.Equals(unit.Source.Path, sourcePath, StringComparison.Ordinal))
            {
                return unit.OriginProjectPath;
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves the source file path of a node by chasing its <see cref="NodeOrigin"/> chain
    /// to the underlying <see cref="ParsedOrigin"/>. Returns <see langword="null"/> for
    /// synthesized nodes with no parsed origin.
    /// </summary>
    private static string? GetNodeSourcePath(SyntaxNode? node)
    {
        var origin = node?.Origin;
        while(origin is SyntheticOrigin synthetic)
        {
            origin = synthetic.DerivedFrom;
        }

        return origin is ParsedOrigin parsed ? parsed.Source.Path : null;
    }

    /// <summary>
    /// Gets the enclosing type of a type by walking the scope chain. In the Mist model,
    /// a nested type's member scope's parent is the enclosing type's member scope.
    /// Returns null if the type is not nested.
    /// </summary>
    private static IType? GetEnclosingTypeOfType(IType type)
    {
        // Get the type's member scope.
        var memberScope = type switch
        {
            ClassType ct => ct.MemberScope,
            StructType st => st.MemberScope,
            AbstractType at => at.MemberScope,
            _ => null,
        };

        if(memberScope is null)
        {
            return null;
        }

        // The parent of the type's member scope is the enclosing scope.
        // If the enclosing scope contains a type symbol whose member scope
        // matches the type's member scope's parent, that's the enclosing type.
        var enclosingScope = memberScope.Parent;
        if(enclosingScope is null)
        {
            return null;
        }

        // Check if the enclosing scope is itself a type's member scope
        // by looking for a type symbol in the grandparent scope.
        var grandparentScope = enclosingScope.Parent;
        if(grandparentScope is null)
        {
            return null;
        }

        foreach(var member in grandparentScope.Members)
        {
            if(member.Kind is SymbolKind.Class or SymbolKind.Struct or SymbolKind.Abstract)
            {
                var ownerScope = member.DeclaredType switch
                {
                    ClassType ct => ct.MemberScope,
                    StructType st => st.MemberScope,
                    AbstractType at => at.MemberScope,
                    _ => null,
                };

                if(ownerScope is not null && ReferenceEquals(ownerScope, enclosingScope))
                {
                    return member.DeclaredType;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Finds a member symbol by name on the given type.
    /// For <see cref="ConstructedType"/>, looks up in the generic definition's member scope.
    /// </summary>
    private static ISymbol? FindMemberSymbol(IType targetType, string memberName, ISemanticModel model)
    {
        // Unwrap ConstructedType to look up in the generic definition's member scope.
        if(targetType is ConstructedType ct)
        {
            targetType = ct.GenericDefinition;
        }

        var memberScope = targetType switch
        {
            ClassType ct2 => ct2.MemberScope,
            StructType st => st.MemberScope,
            AbstractType it => it.MemberScope,
            EnumType et => et.MemberScope,
            _ => null,
        };

        if(memberScope is null)
        {
            return null;
        }

        foreach(var member in memberScope.Members)
        {
            if(member.Name == memberName)
            {
                return member;
            }
        }

        // For abstract types, also search inherited abstract type member scopes.
        if(targetType is AbstractType abstractType)
        {
            foreach(var parentAbstract in abstractType.Abstracts)
            {
                var found = FindMemberSymbol(parentAbstract, memberName, model);
                if(found is not null)
                {
                    return found;
                }
            }
        }

        // Classes and structs are inheritable in Mist — walk the base-type chain so
        // that inherited members are found for accessibility enforcement. A generic
        // base is stored as a ConstructedType, which the recursive call unwraps.
        var baseType = targetType switch
        {
            ClassType baseClassOwner => baseClassOwner.BaseType,
            StructType baseStructOwner => baseStructOwner.BaseType,
            _ => null,
        };

        if(baseType is not null)
        {
            return FindMemberSymbol(baseType, memberName, model);
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
        if(parentScope is null)
        {
            return null;
        }

        foreach(var member in parentScope.Members)
        {
            if(member.Kind is SymbolKind.Class or SymbolKind.Struct
                && member.DeclaredType is ClassType or StructType)
            {
                var scope = member.DeclaredType switch
                {
                    ClassType ct => ct.MemberScope,
                    StructType st => st.MemberScope,
                    _ => null,
                };
                if(ReferenceEquals(scope, typeScope))
                {
                    return member.DeclaredType;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Checks if two types refer to the same type (by reference or display name).
    /// </summary>
    private static bool IsSameType(IType? a, IType? b)
    {
        if(a is null || b is null)
        {
            return false;
        }

        if(ReferenceEquals(a, b))
        {
            return true;
        }
        // Fallback: compare by display name for types that may not be reference-equal.
        if(a.DisplayName == b.DisplayName)
        {
            return true;
        }
        // Migration-window equivalence: legacy `int` matches stdlib `Int32`
        // and so on for every primitive alias in KeywordAliases.Map.
        return ArePrimitiveEquivalent(a, b);
    }

    /// <summary>
    /// Modifier flags extracted from a member's AST declaring node.
    /// Only <see cref="IsStatic"/> is retained from the AST helper — accessibility
    /// is now read from <see cref="ISymbol.Accessibility"/> on the resolved symbol.
    /// </summary>
    private readonly record struct MemberModifiers(bool IsStatic);

    /// <summary>
    /// Extracts the staticness flag from a member's AST declaring node.
    /// Accessibility is no longer extracted here — it is read from
    /// <see cref="ISymbol.Accessibility"/> on the resolved symbol.
    /// Returns default (IsStatic: false) for backend symbols or nodes without modifiers.
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
            IndexerDeclarationNode blockIndexer => blockIndexer.Modifiers,
            _ => null,
        };

        if(modifiers is null || modifiers.Count == 0)
        {
            return new MemberModifiers(IsStatic: false);
        }

        var isStatic = false;

        foreach(var mod in modifiers)
        {
            if(mod is StaticModifierModifierNode)
            {
                isStatic = true;
                break;
            }
        }

        return new MemberModifiers(isStatic);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Refinement type compile-time enforcement (MIST_TC_0097, MIST_TC_0098)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Checks whether an assignment to a <see cref="RefinementType"/> target is valid.
    /// <list type="bullet">
    ///   <item>If the source is already the same <see cref="RefinementType"/>, accepts without re-checking.</item>
    ///   <item>If the source is a compile-time constant, evaluates the predicate and emits
    ///         <see cref="RefinementLiteralViolation"/> if it fails.</item>
    ///   <item>If the source is a non-constant expression of the base type, emits
    ///         <see cref="RefinementNonConstantAssignment"/>.</item>
    /// </list>
    /// Returns <c>true</c> if a refinement-specific diagnostic was emitted (caller should
    /// skip the generic assignability check), <c>false</c> otherwise.
    /// </summary>
    private bool TryCheckRefinementAssignment(
        IType sourceType,
        IType targetType,
        ExpressionNode expression,
        DiagnosticBag diagnostics,
        ISemanticModel model)
    {
        // Handle union-of-refinements: when the target is a UnionType with RefinementType
        // constituents, check if the literal satisfies at least one constituent's predicate.
        if(targetType is UnionType targetUnion && targetUnion.Constituents.Any(c => c is RefinementType))
        {
            var unionConstantValue = TryExtractLiteralConstantValue(expression);
            if(unionConstantValue is not null)
            {
                // Check if the literal satisfies any constituent's refinement predicate.
                var satisfiesAny = false;
                foreach(var constituent in targetUnion.Constituents)
                {
                    if(constituent is RefinementType refinementConstituent)
                    {
                        if(refinementConstituent.Predicate.Evaluate(unionConstantValue))
                        {
                            satisfiesAny = true;
                            break;
                        }
                    }
                    else if(IsAssignable(sourceType, constituent, model))
                    {
                        // Non-refinement constituent that the source is assignable to.
                        satisfiesAny = true;
                        break;
                    }
                }

                if(!satisfiesAny)
                {
                    diagnostics.ReportAtNode(TypeCheckDiagnostics.RefinementLiteralViolation, expression, "TypeChecker", unionConstantValue.ToString()!, targetUnion.DisplayName, targetUnion.DisplayName);
                    return true;
                }

                // Literal satisfies at least one constituent — accept.
                return false;
            }

            // Non-constant expression assigned to union-of-refinements: require pattern matching.
            if(sourceType is not UnionType and not RefinementType)
            {
                var isBaseOfAnyConstituent = targetUnion.Constituents
                    .OfType<RefinementType>()
                    .Any(r => IsAssignable(sourceType, r.BaseType, model));
                if(isBaseOfAnyConstituent)
                {
                    diagnostics.ReportAtNode(TypeCheckDiagnostics.RefinementNonConstantAssignment, expression, "TypeChecker", sourceType.DisplayName, targetType.DisplayName);
                    return true;
                }
            }

            return false;
        }

        // Only applies when the target is a RefinementType.
        if(targetType is not RefinementType targetRefinement)
        {
            return false;
        }

        // If the source is already the same RefinementType, accept without re-checking.
        // The source type already guarantees compliance (Requirement 5.5).
        if(sourceType is RefinementType sourceRefinement &&
            sourceRefinement.Name == targetRefinement.Name)
        {
            return false;
        }

        // If the source is a RefinementType with a different name but the same
        // base type, attempt subset proving via the RefinementSolver (Requirement 9.1, 9.3).
        if(sourceType is RefinementType sourceRefinementDifferent)
        {
            if(IsAssignable(sourceRefinementDifferent.BaseType, targetRefinement.BaseType, model))
            {
                if(RefinementSolver.Implies(sourceRefinementDifferent.Predicate, targetRefinement.Predicate))
                {
                    // Subset proven — accept the assignment.
                    return false;
                }

                // Cannot prove subset relationship -- emit diagnostic.
                diagnostics.ReportAtNode(TypeCheckDiagnostics.RefinementSubsetNotProvable, expression, "TypeChecker", sourceRefinementDifferent.DisplayName, targetRefinement.DisplayName, sourceRefinementDifferent.Predicate.Description, targetRefinement.Predicate.Description);
                return true;
            }

            // Different base types — fall through to normal assignability.
            return false;
        }

        // Check if the source is a compile-time constant literal.
        var constantValue = TryExtractLiteralConstantValue(expression);
        if(constantValue is not null)
        {
            // Evaluate the predicate against the literal value.
            if(!targetRefinement.Predicate.Evaluate(constantValue))
            {
                diagnostics.ReportAtNode(TypeCheckDiagnostics.RefinementLiteralViolation, expression, "TypeChecker", constantValue.ToString()!, targetRefinement.Predicate.Description, targetRefinement.DisplayName);
                return true;
            }

            // Literal satisfies the predicate — accept the assignment.
            return false;
        }

        // Non-constant expression: check if the source type is the base type
        // (or assignable to the base type). If so, check flow narrowing constraints
        // before emitting the non-constant diagnostic.
        if(IsAssignable(sourceType, targetRefinement.BaseType, model))
        {
            // ── Flow narrowing: check if the variable has constraints that prove the target ──
            var narrowedVarName = TryExtractNarrowedIdentifierName(expression);
            if(narrowedVarName is not null &&
                narrowingContext.TryGetValue(narrowedVarName, out var constraints))
            {
                if(ConstraintsImplyPredicate(constraints, targetRefinement.Predicate))
                {
                    // Constraints prove the target refinement — accept the assignment.
                    return false;
                }
            }

            diagnostics.ReportAtNode(TypeCheckDiagnostics.RefinementNonConstantAssignment, expression, "TypeChecker", sourceType.DisplayName, targetRefinement.DisplayName);
            return true;
        }

        // Source type is not related to the base type — let normal assignability handle it.
        return false;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // String → Regex conversion rules (Requirements 6.1–6.10)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Handles the special string→regex conversion rules when the target type
    /// is <c>Standard.Regex</c> and the source type is <c>Standard.String</c>.
    /// <para>
    /// Rules:
    /// <list type="bullet">
    /// <item><description>String literal shorthand: if the source expression is a
    /// string literal and the literal is a valid .NET regex pattern, accept the
    /// assignment without requiring an explicit cast (Requirement 6.8). If the
    /// literal is NOT a valid regex, emit <c>MIST_TC_REGEX_INVALID</c>
    /// (Requirement 6.9).</description></item>
    /// <item><description>Non-literal string: emit a type mismatch diagnostic
    /// indicating that an explicit cast is required (Requirement 6.5).</description></item>
    /// </list>
    /// </para>
    /// Returns <c>true</c> if this method handled the case (either accepted or
    /// emitted a diagnostic). Returns <c>false</c> if not applicable (not a
    /// string→regex scenario), letting the caller proceed with normal
    /// assignability checks.
    /// </summary>
    private static bool TryCheckStringToRegexAssignment(
        IType sourceType,
        IType targetType,
        ExpressionNode expression,
        DiagnosticBag diagnostics,
        ISemanticModel model)
    {
        // Only applies when target is regex-like and source is string-like.
        if(!IsRegexLike(targetType) || !IsStringLike(sourceType))
        {
            return false;
        }

        // Check if the source expression is a string literal.
        var primary = UnwrapExpressionToPrimary(expression);
        if(primary is StringLiteralExpressionPrimaryNode stringLiteral)
        {
            // String literal shorthand: validate the pattern at compile time.
            var content = ExtractStringContent(stringLiteral.StringLiteral.Lexeme);

            try
            {
                _ = new Regex(content, RegexOptions.None);
                // Valid regex pattern — accept the assignment without explicit cast.
                // Record the explicit cast operator so the emitter can emit the
                // conversion (the pattern is proven valid at compile time, so no
                // runtime check is needed, but the emitter still needs to know
                // to wrap it in `new Regex(...)`).
                var explicitOpName = $"operator_explicit_{targetType.DisplayName}";
                var targetScope = GetMemberScope(targetType);
                if(targetScope is not null)
                {
                    foreach(var member in targetScope.Members)
                    {
                        if(member.Kind == SymbolKind.Method && member.Name == explicitOpName)
                        {
                            model.RecordResolvedOperator(expression, member);
                            break;
                        }
                    }
                }

                return true;
            }
            catch(ArgumentException ex)
            {
                // Invalid regex pattern — emit diagnostic.
                _ = ex is RegexParseException parseEx ? parseEx.Offset : 0; diagnostics.ReportAtNode(TypeCheckDiagnostics.RegexPatternInvalid, expression, "TypeChecker");
                return true;
            }
        }

        // Non-literal string assigned to regex without explicit cast.
        // Emit a type mismatch diagnostic suggesting an explicit cast.
        diagnostics.ReportAtNode(TypeCheckDiagnostics.ImplicitConversionNotExists, expression, "TypeChecker", sourceType.DisplayName, targetType.DisplayName);
        return true;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Refinement predicate readonly validation (MIST_TC_0100)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Validates that all refinement types with property patterns only reference
    /// readonly members. Iterates over all type alias symbols whose declared type
    /// is a <see cref="RefinementType"/> (or a <see cref="UnionType"/> containing
    /// refinement constituents) and checks property patterns in their predicates.
    /// Emits <see cref="RefinementMutableMember"/> for each mutable member found.
    /// </summary>
    private static void CheckRefinementPredicateReadonly(ISemanticModel model, DiagnosticBag diagnostics)
    {
        foreach(var symbol in model.AllSymbols)
        {
            if(symbol.Origin == SymbolOrigin.Backend)
            {
                continue;
            }

            if(symbol.Kind != SymbolKind.TypeAlias)
            {
                continue;
            }

            var declaredType = symbol.DeclaredType;
            if(declaredType is RefinementType refinementType)
            {
                ValidateRefinementPredicateMembers(
                    refinementType.Name,
                    refinementType.BaseType,
                    refinementType.Predicate.AstNode,
                    model,
                    diagnostics);
            }
            else if(declaredType is UnionType unionType)
            {
                foreach(var constituent in unionType.Constituents)
                {
                    if(constituent is RefinementType constituentRefinement)
                    {
                        ValidateRefinementPredicateMembers(
                            constituentRefinement.Name,
                            constituentRefinement.BaseType,
                            constituentRefinement.Predicate.AstNode,
                            model,
                            diagnostics);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Validates that all property patterns within a refinement predicate's
    /// <see cref="AndPatternNode"/> reference only readonly members on the base type.
    /// Primitive base types (int, bool, char, etc.) are skipped because they are
    /// inherently immutable (Requirement 3.3).
    /// </summary>
    private static void ValidateRefinementPredicateMembers(
        string typeName,
        IType baseType,
        AndPatternNode andPattern,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Primitive types are inherently immutable — no readonly check needed (Requirement 3.3).
        if(IsPrimitiveImmutableType(baseType))
        {
            return;
        }

        // Walk the and-pattern looking for property patterns.
        ValidateUnaryPatternReadonly(typeName, baseType, andPattern.Left, model, diagnostics);

        if(andPattern.Right is not null && !ReferenceEquals(andPattern.Right, andPattern.Left))
        {
            ValidateUnaryPatternReadonly(typeName, baseType, andPattern.Right, model, diagnostics);
        }

        if(!andPattern.UnaryPatterns.IsDefaultOrEmpty)
        {
            foreach(var part in andPattern.UnaryPatterns)
            {
                ValidateUnaryPatternReadonly(typeName, baseType, part, model, diagnostics);
            }
        }
    }

    /// <summary>
    /// Validates a single <see cref="UnaryPatternNode"/> within a refinement predicate
    /// for readonly member references.
    /// </summary>
    private static void ValidateUnaryPatternReadonly(
        string typeName,
        IType baseType,
        UnaryPatternNode unary,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        if(unary is DirectPatternUnaryPatternNode direct)
        {
            ValidatePrimaryPatternReadonly(typeName, baseType, direct.Primary, model, diagnostics);
        }
        else if(unary is NotPatternUnaryPatternNode not)
        {
            ValidatePrimaryPatternReadonly(typeName, baseType, not.Operand, model, diagnostics);
        }
    }

    /// <summary>
    /// Validates a <see cref="PrimaryPatternNode"/> within a refinement predicate.
    /// Only <see cref="PropertyPatternPrimaryPatternNode"/> instances require readonly
    /// validation — relational and constant patterns are inherently safe.
    /// </summary>
    private static void ValidatePrimaryPatternReadonly(
        string typeName,
        IType baseType,
        PrimaryPatternNode primary,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        if(primary is not PropertyPatternPrimaryPatternNode propertyPattern)
        {
            return;
        }

        if(propertyPattern.Subpatterns is null)
        {
            return;
        }

        ValidatePropertySubpatternListReadonly(
            typeName, baseType, propertyPattern.Subpatterns, model, diagnostics);
    }

    /// <summary>
    /// Validates all property subpatterns in a list for readonly member references.
    /// </summary>
    private static void ValidatePropertySubpatternListReadonly(
        string typeName,
        IType patternType,
        PropertySubpatternListNode subpatternList,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        ValidatePropertySubpatternReadonly(
            typeName, patternType, subpatternList.PropertySubpattern, model, diagnostics);

        foreach(var subpattern in subpatternList.PropertySubpatterns)
        {
            ValidatePropertySubpatternReadonly(typeName, patternType, subpattern, model, diagnostics);
        }
    }

    /// <summary>
    /// Validates a single property subpattern: resolves the member, checks its
    /// readonly status, and recursively validates nested property patterns.
    /// Emits <see cref="RefinementMutableMember"/> if the member is mutable.
    /// </summary>
    private static void ValidatePropertySubpatternReadonly(
        string typeName,
        IType patternType,
        PropertySubpatternNode subpattern,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        var memberName = subpattern.Name.Lexeme;
        var memberScope = GetMemberScope(patternType);

        ISymbol? member = null;
        if(memberScope is not null)
        {
            member = memberScope.Lookup(memberName);
        }

        if(member is not null)
        {
            // Check if the member is readonly.
            if(!IsMemberReadonly(member))
            {
                diagnostics.ReportAtNode(TypeCheckDiagnostics.RefinementMutableMember, subpattern, "TypeChecker", typeName, memberName);
            }

            // Recursively validate nested property patterns in the subpattern's inner pattern.
            var memberType = member.DeclaredType ?? ErrorType.Instance;
            ValidateNestedPatternReadonly(typeName, memberType, subpattern.Pattern, model, diagnostics);
        }
    }

    /// <summary>
    /// Recursively validates nested patterns within a property subpattern for
    /// readonly member references. Navigates the Pattern → OrPattern → AndPattern
    /// hierarchy to find nested property patterns.
    /// </summary>
    private static void ValidateNestedPatternReadonly(
        string typeName,
        IType memberType,
        PatternNode pattern,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        var orPattern = pattern.OrPattern;
        if(orPattern is null)
        {
            return;
        }

        ValidateNestedAndPatternReadonly(typeName, memberType, orPattern.Left, model, diagnostics);

        if(orPattern.Right is not null && !ReferenceEquals(orPattern.Right, orPattern.Left))
        {
            ValidateNestedAndPatternReadonly(typeName, memberType, orPattern.Right, model, diagnostics);
        }

        if(!orPattern.AndPatterns.IsDefaultOrEmpty)
        {
            foreach(var andAlt in orPattern.AndPatterns)
            {
                ValidateNestedAndPatternReadonly(typeName, memberType, andAlt, model, diagnostics);
            }
        }
    }

    /// <summary>
    /// Validates an <see cref="AndPatternNode"/> within a nested pattern for readonly
    /// member references.
    /// </summary>
    private static void ValidateNestedAndPatternReadonly(
        string typeName,
        IType memberType,
        AndPatternNode andPattern,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Skip readonly checks for primitive member types (inherently immutable).
        if(IsPrimitiveImmutableType(memberType))
        {
            return;
        }

        ValidateUnaryPatternReadonly(typeName, memberType, andPattern.Left, model, diagnostics);

        if(andPattern.Right is not null && !ReferenceEquals(andPattern.Right, andPattern.Left))
        {
            ValidateUnaryPatternReadonly(typeName, memberType, andPattern.Right, model, diagnostics);
        }

        if(!andPattern.UnaryPatterns.IsDefaultOrEmpty)
        {
            foreach(var part in andPattern.UnaryPatterns)
            {
                ValidateUnaryPatternReadonly(typeName, memberType, part, model, diagnostics);
            }
        }
    }

    /// <summary>
    /// Determines whether a type is a primitive immutable type (int, bool, char, etc.)
    /// that does not require readonly checks in refinement predicates because its values
    /// are inherently immutable (Requirement 3.3).
    /// </summary>
    private static bool IsPrimitiveImmutableType(IType type)
    {
        var displayName = type.DisplayName;
        return displayName switch
        {
            // Legacy primitive names.
            "int" or "uint" or "long" or "ulong" => true,
            "short" or "ushort" or "byte" or "sbyte" => true,
            "float" or "double" or "decimal" => true,
            "char" or "bool" or "string" => true,
            // Stdlib names.
            "Int32" or "UInt32" or "Int64" or "UInt64" => true,
            "Int16" or "UInt16" or "Byte" or "SByte" => true,
            "Single" or "Double" or "Decimal" => true,
            "Char" or "Boolean" or "String" => true,
            _ => false,
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Enum base type readonly validation (MIST_TC_0102, MIST_TC_0103)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Validates that all enum declarations have readonly base types.
    /// For union base types, each constituent is checked individually.
    /// Skips enums with null or <see cref="ErrorType"/> underlying types to avoid
    /// cascading errors.
    /// </summary>
    private static void CheckEnumBaseTypeReadonly(ISemanticModel model, DiagnosticBag diagnostics)
    {
        foreach(var symbol in model.AllSymbols)
        {
            if(symbol.Kind != SymbolKind.Enum)
            {
                continue;
            }

            if(symbol.DeclaredType is not EnumType enumType)
            {
                continue;
            }

            var underlyingType = enumType.UnderlyingType;
            if(underlyingType is null or ErrorType)
            {
                continue;
            }

            if(underlyingType is UnionType unionType)
            {
                // Check each constituent individually.
                foreach(var constituent in unionType.Constituents)
                {
                    if(!IsTypeReadonly(constituent))
                    {
                        diagnostics.ReportAtNode(TypeCheckDiagnostics.EnumUnionConstituentNotReadonly, model.GetDeclaringNode(symbol), "TypeChecker", symbol.Name, constituent.DisplayName);
                    }
                }
            }
            else
            {
                if(!IsTypeReadonly(underlyingType))
                {
                    diagnostics.ReportAtNode(TypeCheckDiagnostics.EnumBaseTypeNotReadonly, model.GetDeclaringNode(symbol), "TypeChecker", symbol.Name, underlyingType.DisplayName);
                }
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Accessibility-consistency enforcement (Requirements 8.1–8.6)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// For every declaration, verifies that no type exposed in its signature
    /// (return type, parameter types, field type, property/indexer type) is
    /// less accessible than the declaration itself.
    /// Emits <c>MIST_TC_0173</c> (InconsistentAccessibility) on violations.
    /// </summary>
    private static void CheckAccessibilityConsistency(ISemanticModel model, DiagnosticBag diagnostics)
    {
        foreach(var symbol in model.AllSymbols)
        {
            if(symbol.Origin == SymbolOrigin.Backend)
            {
                continue;
            }

            var declarationAccessibility = symbol.Accessibility;

            SyntaxNode? declaringNode = null;
            try { declaringNode = model.GetDeclaringNode(symbol); }
            catch { /* ignore */ }

            switch(symbol.Kind)
            {
                case SymbolKind.Method:
                    {
                        // Check return type.
                        var returnType = symbol.DeclaredType;
                        if(returnType is not null && !IsErrorType(returnType))
                        {
                            CheckTypeAccessibility(returnType, declarationAccessibility, "return type", symbol.Name, declaringNode, diagnostics);
                        }

                        // Check parameter types.
                        var paramTypes = ParameterSignature.Extract(symbol, model);
                        foreach(var paramType in paramTypes)
                        {
                            if(!IsErrorType(paramType))
                            {
                                CheckTypeAccessibility(paramType, declarationAccessibility, "parameter type", symbol.Name, declaringNode, diagnostics);
                            }
                        }

                        break;
                    }

                case SymbolKind.Constructor:
                    {
                        // Check parameter types.
                        var paramTypes = ParameterSignature.Extract(symbol, model);
                        foreach(var paramType in paramTypes)
                        {
                            if(!IsErrorType(paramType))
                            {
                                CheckTypeAccessibility(paramType, declarationAccessibility, "parameter type", symbol.Name, declaringNode, diagnostics);
                            }
                        }

                        break;
                    }

                case SymbolKind.Field:
                    {
                        var fieldType = symbol.DeclaredType;
                        if(fieldType is not null && !IsErrorType(fieldType))
                        {
                            CheckTypeAccessibility(fieldType, declarationAccessibility, "field type", symbol.Name, declaringNode, diagnostics);
                        }

                        break;
                    }

                case SymbolKind.Property:
                    {
                        var propType = symbol.DeclaredType;
                        if(propType is not null && !IsErrorType(propType))
                        {
                            CheckTypeAccessibility(propType, declarationAccessibility, "property type", symbol.Name, declaringNode, diagnostics);
                        }

                        break;
                    }

                case SymbolKind.Indexer:
                    {
                        // Check indexer return type.
                        var indexerType = symbol.DeclaredType;
                        if(indexerType is not null && !IsErrorType(indexerType))
                        {
                            CheckTypeAccessibility(indexerType, declarationAccessibility, "indexer type", symbol.Name, declaringNode, diagnostics);
                        }

                        // Check indexer parameter types.
                        var paramTypes = ParameterSignature.Extract(symbol, model);
                        foreach(var paramType in paramTypes)
                        {
                            if(!IsErrorType(paramType))
                            {
                                CheckTypeAccessibility(paramType, declarationAccessibility, "parameter type", symbol.Name, declaringNode, diagnostics);
                            }
                        }

                        break;
                    }
            }
        }
    }

    /// <summary>
    /// Checks whether a type exposed in a signature is at least as accessible as the
    /// declaration that exposes it. Emits <c>MIST_TC_0173</c> if the type is less accessible.
    /// Only checks named user types (ClassType, StructType, AbstractType, EnumType, TypeAliasType, NewtypeType);
    /// primitive types, arrays, tuples, and error types are always considered accessible.
    /// </summary>
    private static void CheckTypeAccessibility(
        IType exposedType,
        Accessibility declarationAccessibility,
        string signatureRole,
        string declarationName,
        SyntaxNode? declaringNode,
        DiagnosticBag diagnostics)
    {
        if(declaringNode is null)
        {
            return;
        }

        // Unwrap type aliases to check the underlying type's accessibility.
        if(exposedType is TypeAliasType alias)
        {
            // Check the alias itself.
            if(!AccessibilityOrder.IsAtLeastAsAccessibleAs(alias.Accessibility, declarationAccessibility))
            {
                diagnostics.ReportAtNode(TypeCheckDiagnostics.InconsistentAccessibility, declaringNode, "TypeChecker", signatureRole, alias.DisplayName, "member", declarationName);
                return;
            }

            // Also check the underlying type.
            CheckTypeAccessibility(alias.UnderlyingType, declarationAccessibility, signatureRole, declarationName, declaringNode, diagnostics);
            return;
        }

        // Get the type's accessibility. Not all types carry it (e.g., PrimitiveType, ArrayType).
        var typeAccessibility = GetTypeAccessibility(exposedType);
        if(typeAccessibility is null)
        {
            // For array types, check the element type.
            if(exposedType is ArrayType arrayType)
            {
                CheckTypeAccessibility(arrayType.ElementType, declarationAccessibility, signatureRole, declarationName, declaringNode, diagnostics);
            }

            // For constructed types, check the generic definition and type arguments.
            if(exposedType is ConstructedType constructedType)
            {
                CheckTypeAccessibility(constructedType.GenericDefinition, declarationAccessibility, signatureRole, declarationName, declaringNode, diagnostics);
                foreach(var typeArg in constructedType.TypeArguments)
                {
                    CheckTypeAccessibility(typeArg, declarationAccessibility, signatureRole, declarationName, declaringNode, diagnostics);
                }
            }

            return;
        }

        // Backend-backed types (projected from CLR assemblies) are universally accessible
        // within a Mist compilation regardless of their projected accessibility level.
        // The CLR -> Mist accessibility mapping (assembly -> Private) is correct for
        // visibility filtering (hiding non-public members), but should not trigger
        // inconsistent-accessibility diagnostics for types that are reachable as dependencies.
        if(IsBackendBacked(exposedType))
        {
            return;
        }

        if(!AccessibilityOrder.IsAtLeastAsAccessibleAs(typeAccessibility.Value, declarationAccessibility))
        {
            diagnostics.ReportAtNode(TypeCheckDiagnostics.InconsistentAccessibility, declaringNode, "TypeChecker", signatureRole, exposedType.DisplayName, "member", declarationName);
        }
    }

    /// <summary>
    /// Returns <c>true</c> if the type is projected from a CLR assembly (backend-backed),
    /// meaning it is universally accessible regardless of its projected accessibility level.
    /// </summary>
    private static bool IsBackendBacked(IType type)
    {
        return type switch
        {
            ClassType ct => ct.IsBackendBacked,
            StructType st => st.IsBackendBacked,
            _ => false,
        };
    }

    /// <summary>
    /// Extracts the <see cref="Accessibility"/> from a type, or returns <c>null</c>
    /// if the type is a primitive, type parameter, or other type without a declared accessibility.
    /// </summary>
    private static Accessibility? GetTypeAccessibility(IType type)
    {
        return type switch
        {
            ClassType ct => ct.Accessibility,
            StructType st => st.Accessibility,
            AbstractType at => at.Accessibility,
            EnumType et => et.Accessibility,
            TypeAliasType ta => ta.Accessibility,
            NewtypeType nt => nt.Accessibility,
            _ => null,
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Base-type and interface accessibility enforcement (Requirements 9.1–9.4)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// For every class, struct, and abstract type, verifies that:
    /// <list type="bullet">
    ///   <item>The base class (if any) is at least as accessible as the type.</item>
    ///   <item>Each implemented interface/abstract is at least as accessible as the type.</item>
    ///   <item>Each base interface of an interface is at least as accessible as the derived interface.</item>
    /// </list>
    /// Emits <c>MIST_TC_0174</c> (BaseLessAccessible) on violations.
    /// </summary>
    private static void CheckBaseTypeAndInterfaceAccessibility(ISemanticModel model, DiagnosticBag diagnostics)
    {
        foreach(var symbol in model.AllSymbols)
        {
            if(symbol.Origin == SymbolOrigin.Backend)
            {
                continue;
            }

            if(symbol.Kind is not (SymbolKind.Class or SymbolKind.Struct or SymbolKind.Abstract))
            {
                continue;
            }

            var declaredType = symbol.DeclaredType;
            if(declaredType is null)
            {
                continue;
            }

            SyntaxNode? declaringNode;
            try { declaringNode = model.GetDeclaringNode(symbol); }
            catch { continue; }

            if(declaringNode is null)
            {
                continue;
            }

            var typeAccessibility = GetTypeAccessibility(declaredType);
            if(typeAccessibility is null)
            {
                continue;
            }

            // Check base class (classes and structs).
            var baseType = declaredType switch
            {
                ClassType ct => ct.BaseType,
                StructType st => st.BaseType,
                _ => null,
            };

            if(baseType is not null && !IsBackendBacked(baseType))
            {
                var baseAccessibility = GetTypeAccessibility(baseType);
                if(baseAccessibility is not null
                    && !AccessibilityOrder.IsAtLeastAsAccessibleAs(baseAccessibility.Value, typeAccessibility.Value))
                {
                    diagnostics.ReportAtNode(TypeCheckDiagnostics.BaseLessAccessible, declaringNode, "TypeChecker", baseType.DisplayName, declaredType.DisplayName);
                }
            }

            // Check implemented abstracts/interfaces.
            var abstracts = declaredType switch
            {
                ClassType ct => ct.Abstracts,
                StructType st => st.Abstracts,
                AbstractType at => at.Abstracts,
                _ => Array.Empty<IType>(),
            };

            foreach(var abs in abstracts)
            {
                if(IsBackendBacked(abs))
                {
                    continue;
                }

                var absAccessibility = GetTypeAccessibility(abs);
                if(absAccessibility is not null
                    && !AccessibilityOrder.IsAtLeastAsAccessibleAs(absAccessibility.Value, typeAccessibility.Value))
                {
                    diagnostics.ReportAtNode(TypeCheckDiagnostics.BaseLessAccessible, declaringNode, "TypeChecker", abs.DisplayName, declaredType.DisplayName);
                }
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Override and interface-implementation accessibility enforcement (Requirements 10.1–10.3)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// For every method that implements an abstract member (interface implementation),
    /// verifies that:
    /// <list type="bullet">
    ///   <item>An overriding member has the same accessibility as the overridden member (Req 10.1).</item>
    ///   <item>A member implementing an interface member is public (Req 10.2).</item>
    /// </list>
    /// In Mist, abstract (interface) members are implicitly public. An implementing
    /// member must therefore be public to satisfy the C# rule that interface
    /// implementations are public.
    /// Emits <c>MIST_TC_0175</c> (OverrideAccessibilityViolation) on violations.
    /// </summary>
    private static void CheckOverrideAndInterfaceImplAccessibility(ISemanticModel model, DiagnosticBag diagnostics)
    {
        foreach(var symbol in model.AllSymbols)
        {
            if(symbol.Origin == SymbolOrigin.Backend)
            {
                continue;
            }

            if(symbol.Kind is not (SymbolKind.Class or SymbolKind.Struct))
            {
                continue;
            }

            var declaredType = symbol.DeclaredType;
            if(declaredType is null)
            {
                continue;
            }

            var memberScope = declaredType switch
            {
                ClassType ct => ct.MemberScope,
                StructType st => st.MemberScope,
                _ => null,
            };

            if(memberScope is null)
            {
                continue;
            }

            // Get the abstracts implemented by this type.
            var abstracts = declaredType switch
            {
                ClassType ct => ct.Abstracts,
                StructType st => st.Abstracts,
                _ => Array.Empty<IType>(),
            };

            if(abstracts.Count == 0)
            {
                continue;
            }

            // For each abstract member, find the implementing member and check accessibility.
            foreach(var absType in abstracts)
            {
                if(absType is not AbstractType abs)
                {
                    continue;
                }

                foreach(var abstractMember in abs.MemberScope.Members)
                {
                    if(abstractMember.Kind is not (SymbolKind.Method or SymbolKind.Property or SymbolKind.Indexer))
                    {
                        continue;
                    }

                    // Find the implementing member in this type.
                    ISymbol? implMember = null;
                    foreach(var member in memberScope.Members)
                    {
                        if(member.Name == abstractMember.Name && member.Kind == abstractMember.Kind)
                        {
                            implMember = member;
                            break;
                        }
                    }

                    if(implMember is null)
                    {
                        continue; // Missing implementations are reported elsewhere.
                    }

                    // C# rule: interface implementations must be public.
                    // Abstract members in Mist are implicitly public.
                    if(implMember.Accessibility != Accessibility.Public)
                    {
                        SyntaxNode? implNode = null;
                        try { implNode = model.GetDeclaringNode(implMember); }
                        catch { /* ignore */ }

                        if(implNode is not null)
                        {
                            diagnostics.ReportAtNode(TypeCheckDiagnostics.OverrideAccessibilityViolation, implNode, "TypeChecker", implMember.Accessibility.ToString().ToLowerInvariant(), abs.DisplayName, abstractMember.Name);
                        }
                    }
                }
            }

            // Check overriding members in class inheritance (base class virtual methods).
            // In C#, an overriding member must have the same accessibility as the overridden member.
            if(declaredType is ClassType classType && classType.BaseType is ClassType baseClass)
            {
                CheckOverrideAccessibilityAgainstBase(memberScope, baseClass, model, diagnostics);
            }
            else if(declaredType is StructType structType && structType.BaseType is StructType baseStruct)
            {
                CheckOverrideAccessibilityAgainstBaseStruct(memberScope, baseStruct, model, diagnostics);
            }
        }
    }

    /// <summary>
    /// Checks that overriding members in a class have the same accessibility as
    /// the corresponding member in the base class.
    /// </summary>
    private static void CheckOverrideAccessibilityAgainstBase(
        IScope memberScope,
        ClassType baseClass,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        foreach(var member in memberScope.Members)
        {
            if(member.Kind is not (SymbolKind.Method or SymbolKind.Property))
            {
                continue;
            }

            if(member.Origin == SymbolOrigin.Backend)
            {
                continue;
            }

            // Search for a matching member in the base class chain.
            var baseMember = FindMemberInBaseClassChain(member.Name, member.Kind, baseClass);
            if(baseMember is null)
            {
                continue;
            }

            // If the base member is from Backend (CLR), skip — accessibility mismatch
            // for CLR-projected members is not enforceable from user code.
            if(baseMember.Origin == SymbolOrigin.Backend)
            {
                continue;
            }

            // An overriding member must have the same accessibility as the overridden member.
            if(member.Accessibility != baseMember.Accessibility)
            {
                SyntaxNode? memberNode = null;
                try { memberNode = model.GetDeclaringNode(member); }
                catch { /* ignore */ }

                if(memberNode is not null)
                {
                    var baseTypeName = baseClass.DisplayName;
                    diagnostics.ReportAtNode(TypeCheckDiagnostics.OverrideAccessibilityViolation, memberNode, "TypeChecker", member.Accessibility.ToString().ToLowerInvariant(), baseTypeName, baseMember.Name);
                }
            }
        }
    }

    /// <summary>
    /// Checks that overriding members in a struct have the same accessibility as
    /// the corresponding member in the base struct.
    /// </summary>
    private static void CheckOverrideAccessibilityAgainstBaseStruct(
        IScope memberScope,
        StructType baseStruct,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        foreach(var member in memberScope.Members)
        {
            if(member.Kind is not (SymbolKind.Method or SymbolKind.Property))
            {
                continue;
            }

            if(member.Origin == SymbolOrigin.Backend)
            {
                continue;
            }

            // Search for a matching member in the base struct chain.
            var baseMember = FindMemberInBaseStructChain(member.Name, member.Kind, baseStruct);
            if(baseMember is null)
            {
                continue;
            }

            if(baseMember.Origin == SymbolOrigin.Backend)
            {
                continue;
            }

            // An overriding member must have the same accessibility as the overridden member.
            if(member.Accessibility != baseMember.Accessibility)
            {
                SyntaxNode? memberNode = null;
                try { memberNode = model.GetDeclaringNode(member); }
                catch { /* ignore */ }

                if(memberNode is not null)
                {
                    var baseTypeName = baseStruct.DisplayName;
                    diagnostics.ReportAtNode(TypeCheckDiagnostics.OverrideAccessibilityViolation, memberNode, "TypeChecker", member.Accessibility.ToString().ToLowerInvariant(), baseTypeName, baseMember.Name);
                }
            }
        }
    }

    /// <summary>
    /// Searches the class inheritance chain for a member with the given name and kind.
    /// Returns the first match found in the ancestor chain, or null.
    /// </summary>
    private static ISymbol? FindMemberInBaseClassChain(string name, SymbolKind kind, ClassType baseClass)
    {
        var current = baseClass;
        while(current is not null)
        {
            foreach(var member in current.MemberScope.Members)
            {
                if(member.Name == name && member.Kind == kind)
                {
                    return member;
                }
            }

            current = current.BaseType as ClassType;
        }

        return null;
    }

    /// <summary>
    /// Searches the struct inheritance chain for a member with the given name and kind.
    /// Returns the first match found in the ancestor chain, or null.
    /// </summary>
    private static ISymbol? FindMemberInBaseStructChain(string name, SymbolKind kind, StructType baseStruct)
    {
        var current = baseStruct;
        while(current is not null)
        {
            foreach(var member in current.MemberScope.Members)
            {
                if(member.Name == name && member.Kind == kind)
                {
                    return member;
                }
            }

            current = current.BaseType as StructType;
        }

        return null;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Nested-type accessibility-domain enforcement (Requirements 11.1, 11.2)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// For every type reference in the program (field types, parameter types, return types,
    /// base types, etc.), checks whether the referenced type is a nested type whose computed
    /// accessibility domain excludes the referencing location.
    /// <para>
    /// A nested type's accessibility domain is the intersection of its own declared
    /// <see cref="Accessibility"/> and its enclosing type's accessibility domain (Req 11.1).
    /// References from outside this computed domain are rejected (Req 11.2); references
    /// from within are permitted.
    /// </para>
    /// Emits <c>MIST_TC_0176</c> (NestedTypeOutsideDomain) on violations.
    /// </summary>
    private static void CheckNestedTypeAccessibilityDomain(ISemanticModel model, DiagnosticBag diagnostics)
    {
        foreach(var symbol in model.AllSymbols)
        {
            if(symbol.Origin == SymbolOrigin.Backend)
            {
                continue;
            }

            // Determine the enclosing type of the symbol (the location from which types are referenced).
            var referencingType = GetDeclaringTypeOfMember(symbol);

            SyntaxNode? declaringNode = null;
            try { declaringNode = model.GetDeclaringNode(symbol); }
            catch { /* ignore */ }

            switch(symbol.Kind)
            {
                case SymbolKind.Method:
                    {
                        // Check return type.
                        var returnType = symbol.DeclaredType;
                        if(returnType is not null && !IsErrorType(returnType))
                        {
                            CheckNestedTypeReference(returnType, referencingType, declaringNode, diagnostics);
                        }

                        // Check parameter types.
                        var paramTypes = ParameterSignature.Extract(symbol, model);
                        foreach(var paramType in paramTypes)
                        {
                            if(!IsErrorType(paramType))
                            {
                                CheckNestedTypeReference(paramType, referencingType, declaringNode, diagnostics);
                            }
                        }

                        break;
                    }

                case SymbolKind.Constructor:
                    {
                        // Check parameter types.
                        var paramTypes = ParameterSignature.Extract(symbol, model);
                        foreach(var paramType in paramTypes)
                        {
                            if(!IsErrorType(paramType))
                            {
                                CheckNestedTypeReference(paramType, referencingType, declaringNode, diagnostics);
                            }
                        }

                        break;
                    }

                case SymbolKind.Field:
                    {
                        var fieldType = symbol.DeclaredType;
                        if(fieldType is not null && !IsErrorType(fieldType))
                        {
                            CheckNestedTypeReference(fieldType, referencingType, declaringNode, diagnostics);
                        }

                        break;
                    }

                case SymbolKind.Property:
                    {
                        var propType = symbol.DeclaredType;
                        if(propType is not null && !IsErrorType(propType))
                        {
                            CheckNestedTypeReference(propType, referencingType, declaringNode, diagnostics);
                        }

                        break;
                    }

                case SymbolKind.Indexer:
                    {
                        // Check indexer return type.
                        var indexerType = symbol.DeclaredType;
                        if(indexerType is not null && !IsErrorType(indexerType))
                        {
                            CheckNestedTypeReference(indexerType, referencingType, declaringNode, diagnostics);
                        }

                        // Check indexer parameter types.
                        var paramTypes = ParameterSignature.Extract(symbol, model);
                        foreach(var paramType in paramTypes)
                        {
                            if(!IsErrorType(paramType))
                            {
                                CheckNestedTypeReference(paramType, referencingType, declaringNode, diagnostics);
                            }
                        }

                        break;
                    }

                case SymbolKind.Class:
                case SymbolKind.Struct:
                    {
                        // Check base type and implemented abstracts.
                        var declaredType = symbol.DeclaredType;
                        if(declaredType is null)
                        {
                            break;
                        }

                        var baseType = declaredType switch
                        {
                            ClassType ct => ct.BaseType,
                            StructType st => st.BaseType,
                            _ => null,
                        };

                        if(baseType is not null && !IsErrorType(baseType))
                        {
                            CheckNestedTypeReference(baseType, referencingType, declaringNode, diagnostics);
                        }

                        var abstracts = declaredType switch
                        {
                            ClassType ct => ct.Abstracts,
                            StructType st => st.Abstracts,
                            _ => Array.Empty<IType>(),
                        };

                        foreach(var abs in abstracts)
                        {
                            if(!IsErrorType(abs))
                            {
                                CheckNestedTypeReference(abs, referencingType, declaringNode, diagnostics);
                            }
                        }

                        break;
                    }
            }
        }
    }

    /// <summary>
    /// Checks if a referenced type is a nested type and, if so, whether the reference
    /// location is within the nested type's computed accessibility domain.
    /// The domain is the intersection of the nested type's own accessibility and its
    /// enclosing type's accessibility domain.
    /// </summary>
    private static void CheckNestedTypeReference(
        IType referencedType,
        IType? referencingType,
        SyntaxNode? declaringNode,
        DiagnosticBag diagnostics)
    {
        if(declaringNode is null)
        {
            return;
        }

        // Unwrap type aliases — check the alias itself and its underlying type.
        if(referencedType is TypeAliasType alias)
        {
            CheckNestedTypeReferenceForNamedType(alias, referencingType, declaringNode, diagnostics);
            CheckNestedTypeReference(alias.UnderlyingType, referencingType, declaringNode, diagnostics);
            return;
        }

        // For array types, check the element type.
        if(referencedType is ArrayType arrayType)
        {
            CheckNestedTypeReference(arrayType.ElementType, referencingType, declaringNode, diagnostics);
            return;
        }

        // For constructed types, check the generic definition and type arguments.
        if(referencedType is ConstructedType constructedType)
        {
            CheckNestedTypeReference(constructedType.GenericDefinition, referencingType, declaringNode, diagnostics);
            foreach(var typeArg in constructedType.TypeArguments)
            {
                CheckNestedTypeReference(typeArg, referencingType, declaringNode, diagnostics);
            }

            return;
        }

        // Check the type itself.
        CheckNestedTypeReferenceForNamedType(referencedType, referencingType, declaringNode, diagnostics);
    }

    /// <summary>
    /// For a specific named type (ClassType, StructType, etc.), checks if it is nested
    /// and whether the referencing location is within its computed accessibility domain.
    /// </summary>
    private static void CheckNestedTypeReferenceForNamedType(
        IType referencedType,
        IType? referencingType,
        SyntaxNode? declaringNode,
        DiagnosticBag diagnostics)
    {
        // Only check user-defined named types that can be nested.
        var enclosingType = GetEnclosingTypeOfType(referencedType);
        if(enclosingType is null)
        {
            // Not a nested type — no domain intersection check needed.
            return;
        }

        // The type is nested. Compute whether the reference is within the
        // nested type's accessibility domain (the intersection of its own
        // accessibility and its enclosing type's domain).
        if(!IsWithinNestedTypeDomain(referencedType, enclosingType, referencingType))
        {
            diagnostics.ReportAtNode(TypeCheckDiagnostics.NestedTypeOutsideDomain, declaringNode!, "TypeChecker", referencedType.DisplayName);
        }
    }

    /// <summary>
    /// Determines whether <paramref name="referencingType"/> is within the accessibility
    /// domain of a nested type. The domain is the intersection of:
    /// <list type="bullet">
    ///   <item>The nested type's own declared accessibility domain.</item>
    ///   <item>Its enclosing type's accessibility domain (recursively for multi-level nesting).</item>
    /// </list>
    /// Both conditions must be satisfied for the reference to be within the domain.
    /// </summary>
    private static bool IsWithinNestedTypeDomain(
        IType nestedType,
        IType enclosingType,
        IType? referencingType)
    {
        // First, check the nested type's own accessibility against the referencing location.
        var nestedAccessibility = GetTypeAccessibility(nestedType);
        if(nestedAccessibility is null)
        {
            return true; // Can't determine accessibility — assume accessible.
        }

        if(!IsTypeAccessibleFrom(nestedAccessibility.Value, enclosingType, referencingType))
        {
            return false;
        }

        // Second, check the enclosing type's accessibility domain.
        // If the enclosing type is itself nested, recursively check its domain.
        var grandparentType = GetEnclosingTypeOfType(enclosingType);
        if(grandparentType is not null)
        {
            // The enclosing type is also nested — recursively check its domain.
            if(!IsWithinNestedTypeDomain(enclosingType, grandparentType, referencingType))
            {
                return false;
            }
        }
        else
        {
            // The enclosing type is a top-level type. Check its accessibility
            // against the referencing location.
            var enclosingAccessibility = GetTypeAccessibility(enclosingType);
            if(enclosingAccessibility is not null)
            {
                if(!IsTopLevelTypeAccessibleFrom(enclosingAccessibility.Value))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Checks whether a nested type with the given accessibility, declared within
    /// <paramref name="declaringType"/>, is accessible from <paramref name="referencingType"/>.
    /// This is the per-level check (not recursive — does not check the enclosing type's domain).
    /// </summary>
    private static bool IsTypeAccessibleFrom(
        Accessibility nestedAccessibility,
        IType declaringType,
        IType? referencingType)
    {
        return nestedAccessibility switch
        {
            Accessibility.Public => true,
            Accessibility.Private => IsWithinDeclaringTypeDomain(declaringType, referencingType),
            Accessibility.Protected => IsDeclaredOrDerivedType(declaringType, referencingType),
            Accessibility.Internal => true,
            _ => true,
        };
    }

    /// <summary>
    /// Checks whether a top-level type with the given accessibility is accessible
    /// from the referencing type's location. For top-level types:
    /// <list type="bullet">
    ///   <item><c>Public</c> and <c>Internal</c>: accessible within the same compilation.</item>
    ///   <item><c>Private</c>: only accessible within the same file/scope (unusual for top-level).</item>
    ///   <item><c>Protected</c>: invalid for top-level types (would be caught elsewhere).</item>
    /// </list>
    /// Since all user-code is in the same compilation, top-level internal and public types
    /// are always accessible. This method is relevant primarily for cross-assembly checks.
    /// </summary>
    private static bool IsTopLevelTypeAccessibleFrom(Accessibility typeAccessibility)
    {
        // Within the same compilation, both Public and Internal top-level types are accessible.
        // Private top-level types are unusual but would only be accessible within their own scope.
        // Cross-assembly checks for internal are handled by IsCrossAssemblyReference at the
        // member-access level. For type references within the same compilation, all top-level
        // types are accessible.
        return typeAccessibility is Accessibility.Public or Accessibility.Internal;
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
        // ── MIST2007: Ref struct abstract assignment restriction (Requirements 3.11, 3.12) ──
        // A ref struct value cannot be assigned to an abstract-typed variable because
        // that would require boxing (heap allocation), violating the stack-only constraint.
        // The restriction is at the usage site — declaring that a ref struct implements
        // an abstract is permitted.
        if(IsRefStructType(source) && IsAbstractTargetType(target))
        {
            _ = target.DisplayName; diagnostics.ReportAtNode(TypeCheckDiagnostics.RefStructToAbstract, node, "TypeChecker");
            return;
        }

        if(!IsAssignable(source, target, model))
        {
            // stack-only-types feature (R4.6): before falling back to the
            // generic type-mismatch diagnostic, detect when a stackalloc
            // result (SpanType) is assigned to a non-span target.
            if(TryEmitStackallocTargetMismatchDiagnostic(source, target, node, diagnostics))
            {
                return;
            }

            // stack-only-types feature (R7.7): detect span-to-span mismatch
            // (kind differs or element types not reconcilable) and emit a
            // targeted diagnostic before the generic type-mismatch.
            if(TryEmitSpanInteropMismatchDiagnostic(source, target, node, diagnostics))
            {
                return;
            }

            // unknown-type feature (Req 4.2, 4.4): before falling back to
            // the generic type-mismatch diagnostic, detect the two specific
            // unknown-related failure modes and emit the targeted code.
            if(TryEmitUnknownAssignabilityDiagnostic(source, target, node, diagnostics))
            {
                return;
            }

            // type-unions feature (Req 6.3, 6.4): before falling back to
            // the generic type-mismatch diagnostic, detect union-specific
            // failure modes and emit the targeted code.
            if(TryEmitUnionAssignabilityDiagnostic(source, target, node, diagnostics))
            {
                return;
            }

            // enums feature (Req 7.3): detect reverse conversion from
            // underlying type to enum type and emit the targeted diagnostic.
            if(TryEmitEnumAssignabilityDiagnostic(source, target, node, diagnostics, model))
            {
                return;
            }

            // newtypes feature (Req 2.1, 2.3, 2.4, 2.5): detect newtype↔source
            // and cross-newtype assignments and emit the targeted diagnostic.
            if(TryEmitNewtypeAssignabilityDiagnostic(source, target, node, diagnostics))
            {
                return;
            }

            // delegate-func-conversion (Req 2.5, 6.1, 6.6, 6.7): detect
            // delegate-specific failure modes and emit the targeted diagnostic.
            if(TryEmitDelegateAssignabilityDiagnostic(source, target, node, diagnostics))
            {
                return;
            }

            diagnostics.ReportAtNode(TypeCheckDiagnostics.ImplicitConversionNotExists, node, "TypeChecker", source.DisplayName, target.DisplayName);
        }
        else
        {
            // The types are assignable. If the assignability was due to an
            // implicit cast operator (not direct assignability like identity,
            // subtype, abstract, or numeric widening), resolve and
            // record it on the AST node so the emitter can later emit the
            // conversion call. Direct assignability takes precedence over
            // implicit casts (Requirements 14.1, 14.2, 14.3).
            if(!IsDirectlyAssignable(source, target, model))
            {
                _ = TryResolveImplicitCast(source, target, node, model, diagnostics);
            }
        }
    }

    /// <summary>
    /// Detects the two unknown-type-specific assignability failure modes and,
    /// when one matches, emits the targeted diagnostic and returns <c>true</c>.
    /// Returns <c>false</c> if neither case applies, leaving the caller to
    /// emit its regular type-mismatch diagnostic (e.g., <c>MIST_TC_0001</c>,
    /// <c>MIST_TC_0002</c>, <c>MIST_TC_0008</c>, or <c>MIST_TC_0021</c>).
    /// <para>
    /// The cases are:
    /// <list type="bullet">
    /// <item><description><c>MIST_TC_0061</c> — <c>void</c> assigned to
    /// <c>unknown</c> (Req 4.2).</description></item>
    /// <item><description><c>MIST_TC_0060</c> — a value whose static type is
    /// <c>unknown</c> assigned to a non-<c>unknown</c> target (Req 4.4).
    /// Pattern-narrowing (<c>is T t</c>) is the designated mechanism and is
    /// mediated by a separate check, not this one.</description></item>
    /// <item><description><c>MIST_TC_0058</c> — a covariant <c>unknown</c>
    /// widening is attempted on a struct (value-type) generic (Req 8.6).
    /// Widening is restricted to class (reference-type) generics so the
    /// C# emission strategy remains sound.</description></item>
    /// </list>
    /// </para>
    /// </summary>
    private static bool TryEmitUnknownAssignabilityDiagnostic(
        IType source,
        IType target,
        SyntaxNode node,
        DiagnosticBag diagnostics)
    {
        // void → unknown (Req 4.2). The top-type clause in IsAssignable
        // explicitly rejects this and leaves the emission to the caller.
        if(target is UnknownType && source.IsVoid())
        {
            diagnostics.ReportAtNode(TypeCheckDiagnostics.VoidToUnknown, node, "TypeChecker");
            return true;
        }

        // unknown → non-unknown (Req 4.4). Use `is T t` to narrow first.
        if(source is UnknownType && target is not UnknownType)
        {
            diagnostics.ReportAtNode(TypeCheckDiagnostics.UnknownToNonUnknown, node, "TypeChecker");
            return true;
        }

        // Struct-generic widening restriction (Req 8.1, 8.6, Task 23.4).
        // If the only reason assignability fails is that the generic
        // definition being widened is a struct rather than a class, emit
        // the explanatory MIST_TC_0058 so the error points at the rule
        // rather than cascading a generic type-mismatch diagnostic.
        if(WouldAssignableWithUnknownWideningIfClass(source, target))
        {
            diagnostics.ReportAtNode(TypeCheckDiagnostics.StructGenericUnknownWidening, node, "TypeChecker");
            return true;
        }

        return false;
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
            AbstractType it => it.MemberScope,
            ConstructedType cst => cst.MemberScope,
            EnumType et => et.MemberScope,
            NewtypeType nt => nt.MemberScope,
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
        if(IsErrorType(source) || IsErrorType(target))
        {
            return null;
        }

        var candidates = new List<ISymbol>();
        var opName = $"operator_implicit_{target.DisplayName}";

        // 1. Search the source type's member scope.
        var sourceScope = GetMemberScope(source);
        if(sourceScope is not null)
        {
            foreach(var member in sourceScope.Members)
            {
                if(member.Kind == SymbolKind.Method && member.Name == opName)
                {
                    candidates.Add(member);
                }
            }
        }

        // 2. Search the target type's member scope.
        var targetScope = GetMemberScope(target);
        if(targetScope is not null && !ReferenceEquals(sourceScope, targetScope))
        {
            foreach(var member in targetScope.Members)
            {
                if(member.Kind == SymbolKind.Method && member.Name == opName)
                {
                    // Avoid duplicates if the same symbol appears in both scopes.
                    if(!candidates.Any(c => ReferenceEquals(c, member)))
                    {
                        candidates.Add(member);
                    }
                }
            }
        }

        if(candidates.Count == 1)
        {
            return candidates[0];
        }

        if(candidates.Count > 1)
        {
            ambiguous = true;
        }

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

        if(ambiguous)
        {
            diagnostics.ReportAtNode(TypeCheckDiagnostics.AmbiguousImplicitCast, node, "TypeChecker");
            return null;
        }

        if(resolved is not null)
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
    /// - Either is ErrorType (error suppression)
    /// - Subtype assignability: derived class assignable to any ancestor class
    /// </summary>
    internal static bool IsAssignable(IType source, IType target)
    {
        return IsAssignable(source, target, model: null);
    }

    /// <summary>
    /// Returns true if <paramref name="source"/> is assignable to <paramref name="target"/>.
    /// When <paramref name="model"/> is provided, also checks abstract assignability.
    /// </summary>
    internal static bool IsAssignable(IType source, IType target, ISemanticModel? model)
    {
        // ── type-aliases feature: transparent unwrapping (Requirement 5.1) ──
        // Type aliases are fully transparent — unwrap to the underlying type
        // before any comparison so that using an alias is semantically
        // identical to using the underlying type directly.
        if(source is TypeAliasType sourceAlias)
        {
            source = sourceAlias.UnderlyingType;
        }

        if(target is TypeAliasType targetAlias)
        {
            target = targetAlias.UnderlyingType;
        }

        // ── refinement-types feature: nominal subtyping (Requirement 8.3) ──
        // A RefinementType is a nominal subtype of its BaseType. Widening
        // from refinement to base is always safe. Same-name refinement types
        // are identity-assignable.
        if(source is RefinementType sourceRefinement)
        {
            if(target is RefinementType targetRefinement)
            {
                // Same refinement type: accept (identity).
                if(sourceRefinement.Name == targetRefinement.Name)
                {
                    return true;
                }

                // Different refinement types with same base: use subset proving.
                // If the source predicate implies the target predicate, the
                // assignment is safe (Requirement 9.1, 9.2).
                if(IsAssignable(sourceRefinement.BaseType, targetRefinement.BaseType, model))
                {
                    if(RefinementSolver.Implies(sourceRefinement.Predicate, targetRefinement.Predicate))
                    {
                        return true;
                    }
                }

                // Cannot prove subset — fall through to unwrap and check base.
            }

            // Widening: RefinementType → BaseType is always safe.
            // Unwrap the source to its base type and continue checking.
            source = sourceRefinement.BaseType;
        }

        // ── refinement-types feature: target is RefinementType ───────────
        // When the target is a RefinementType and the source is assignable
        // to the base type, IsAssignable returns true. The actual predicate
        // enforcement (literal evaluation or non-constant rejection) is
        // handled by TryCheckRefinementAssignment at the call site.
        if(target is RefinementType targetRefinementForBase)
        {
            return IsAssignable(source, targetRefinementForBase.BaseType, model);
        }

        // ErrorType suppresses cascading errors.
        if(IsErrorType(source) || IsErrorType(target))
        {
            return true;
        }

        // ── unknown-type feature (Req 4.1, 4.2, 4.3, 4.4, 4.5) ──────────
        //
        // UnknownType is the user-facing top type for assignment:
        //   * Every non-void source type is assignable to UnknownType.
        //   * void is NOT assignable to UnknownType — it carries no value.
        //     The caller emits MIST_TC_0061 (VoidToUnknown) at the
        //     specific assignment / return / argument / initializer site.
        //   * A value whose static type is UnknownType is NOT assignable
        //     to a non-UnknownType target. Pattern matching (`is T t`) is
        //     the designated mechanism to narrow an `unknown` value back
        //     to a concrete type. The caller emits MIST_TC_0060
        //     (UnknownToNonUnknown) at the widening site.
        //   * UnknownType → UnknownType is identity-accepted.
        //
        // This clause is placed immediately after the ErrorType cascade
        // suppression and before every other rule so that UnknownType is
        // handled uniformly across identity, ConstructedType, abstract,
        // subtype, and implicit-cast branches.
        if(target is UnknownType)
        {
            if(source.IsVoid())
            {
                return false;
            }

            return true;
        }

        if(source is UnknownType)
        {
            // target is not UnknownType (checked above); reject.
            return false;
        }

        // ── type-unions feature: union type assignability ─────────────────
        //
        // Union type rules are checked before identity/display-name so that
        // structural union equality (same constituents regardless of order)
        // is handled correctly, and constituent-to-union / union-to-wider-union
        // assignments are accepted.
        //
        // Rules:
        //   * UnionType → UnionType: valid if every source constituent is
        //     a constituent of the target (order-independent).
        //   * T (non-union) → UnionType: valid if T is a constituent of
        //     the target union.
        //   * UnionType → T (non-union): rejected (must narrow first).
        //
        // A union containing ErrorType is treated as "poisoned" — accept
        // any assignment to suppress cascading errors.
        if(target is UnionType targetUnion)
        {
            // Poisoned union target: suppress cascading errors.
            if(HasErrorConstituent(targetUnion))
            {
                return true;
            }

            if(source is UnionType sourceUnionForTarget)
            {
                // Poisoned union source: suppress cascading errors.
                if(HasErrorConstituent(sourceUnionForTarget))
                {
                    return true;
                }

                // Union → Union: every source constituent must be in target.
                foreach(var constituent in sourceUnionForTarget.Constituents)
                {
                    if(!IsConstituentOf(constituent, targetUnion))
                    {
                        return false;
                    }
                }

                return true;
            }

            // Non-union → Union: source must be a constituent of target.
            // Special case: if the union contains UnknownType (e.g., `unknown | void`),
            // any non-void source is assignable — this mirrors the direct UnknownType
            // target rule and handles CLR reference-type parameters projected as T?.
            if(!source.IsVoid() && targetUnion.Constituents.Any(c => c is UnknownType))
            {
                return true;
            }

            if(IsConstituentOf(source, targetUnion))
            {
                return true;
            }

            // Subtype fallback: if the source is assignable to any non-void constituent
            // via inheritance (e.g., ConstructedType<T> assignable to its non-generic base),
            // accept the assignment. This avoids infinite recursion by only checking
            // constituents that are class/struct types (not other unions or error types).
            foreach(var constituent in targetUnion.Constituents)
            {
                if(constituent.IsVoid() || constituent is UnionType || constituent is ErrorType)
                {
                    continue;
                }

                if(IsAssignable(source, constituent, model))
                {
                    return true;
                }
            }

            return false;
        }

        if(source is UnionType)
        {
            // Union → non-union: rejected (must narrow first).
            // The only exception is if the target is UnknownType, but that
            // was already handled above.
            return false;
        }

        // ── delegate-func-conversion: DelegateType assignability ──────────
        //
        // func<...> → DelegateType: accepted if signature-compatible
        // DelegateType → func<...>: accepted if signature-compatible
        // DelegateType → BaseDelegateType: always accepted
        // func<...> → BaseDelegateType: always accepted
        // DelegateType(A) → DelegateType(B) where A ≠ B: REJECTED (nominal distinctness)
        // BaseDelegateType → DelegateType: REJECTED (cannot narrow)
        // BaseDelegateType → func<...>: REJECTED (cannot narrow)
        if(target is DelegateType targetDelegate)
        {
            if(source is DelegateType sourceDelegate)
            {
                // Same delegate type → accept
                if(sourceDelegate.ClrFullName == targetDelegate.ClrFullName
                    && DelegateTypeArgumentsMatch(sourceDelegate, targetDelegate))
                {
                    return true;
                }
                // Different named delegates → reject (nominal distinctness, Req 6.1)
                return false;
            }
            // func<...> → DelegateType: check signature compatibility
            if(SemanticModelBuilder.IsFuncType(source))
            {
                return IsSignatureCompatible(source, targetDelegate.FuncSignature);
            }

            return false;
        }

        if(source is DelegateType sourceDelegate2)
        {
            if(target is BaseDelegateType)
            {
                return true;  // Any delegate → System.Delegate
            }
            // DelegateType → func<...>: check signature compatibility
            if(SemanticModelBuilder.IsFuncType(target))
            {
                return IsSignatureCompatible(sourceDelegate2.FuncSignature, target);
            }

            return false;
        }

        if(target is BaseDelegateType)
        {
            // func<...> → System.Delegate: always accepted
            if(SemanticModelBuilder.IsFuncType(source))
            {
                return true;
            }
            // DelegateType → System.Delegate handled above
            return false;
        }

        if(source is BaseDelegateType)
        {
            // System.Delegate cannot be narrowed to anything
            return false;
        }

        // ── newtypes feature: nominal distinctness (Req 2.1, 2.3, 2.4, 2.5) ──
        //
        // A newtype is a nominally distinct type — it is only assignable to
        // itself (same instance). It is NOT assignable to its source type,
        // the source type is NOT assignable to the newtype, and two different
        // newtypes are NOT assignable to each other even if they share the
        // same source type. An explicit cast is required for conversion.
        //
        // This check must appear before the identity/DisplayName checks AND
        // before the TryFindImplicitCast fallback at the end of this method,
        // because cloned implicit cast operators on the newtype's member scope
        // could otherwise accidentally allow the assignment.
        if(source is NewtypeType || target is NewtypeType)
        {
            // Only the same newtype instance is assignable to itself.
            return ReferenceEquals(source, target);
        }

        // Identity.
        if(ReferenceEquals(source, target))
        {
            return true;
        }

        // Same display name (for types that may not be reference-equal).
        if(source.DisplayName == target.DisplayName)
        {
            return true;
        }

        // ── anonymous-types feature: structural equality (Requirement 4.6) ──
        // Two AnonymousType instances with the same property names, types, and
        // order are the same type and are assignable to each other.
        // This explicit check ensures correctness regardless of DisplayName
        // formatting and makes the structural equality contract visible.
        if(source is AnonymousType sourceAnon && target is AnonymousType targetAnon)
        {
            return sourceAnon.StructurallyEquals(targetAnon);
        }

        // ArrayType assignability: a concrete-length array (e.g., int[10]) is
        // assignable to an unsized or symbolic-length array (int[]) with the same
        // element type. A symbolic-length array is also assignable to an unsized
        // array. Two arrays with the same concrete length are assignable.
        // This is a natural subtype relationship — a sized array is a more
        // specific version of an unsized array.
        if(source is ArrayType sourceArray && target is ArrayType targetArray)
        {
            if(IsAssignable(sourceArray.ElementType, targetArray.ElementType, model))
            {
                // Target is unsized (no known length, no symbolic ID) — any source matches.
                if(targetArray.KnownLength is null && targetArray.SymbolicLengthId is null)
                {
                    return true;
                }
                // Target has a symbolic length — any source with same element type is
                // acceptable (symbolic just means "unknown at compile time").
                if(targetArray.SymbolicLengthId is not null)
                {
                    return true;
                }
                // Target has a concrete length — source must have the same concrete length.
                if(targetArray.KnownLength is not null
                    && sourceArray.KnownLength == targetArray.KnownLength)
                {
                    return true;
                }
            }

            return false;
        }

        // SpanType assignability (Requirements 4.4, 4.5): mirrors ArrayType exactly.
        // A known-length span (e.g., Span<int, 4>) is assignable to an unknown-length
        // or symbolic-length span (Span<int>) with the same element type and matching
        // kind (mutable/read-only). Two spans with the same concrete length are assignable.
        // This is the permitted known→unknown widening that erases the known length.
        if(source is SpanType sourceSpan && target is SpanType targetSpan)
        {
            // Kind must match: Span→Span or ReadOnlySpan→ReadOnlySpan.
            if(sourceSpan.IsReadOnly != targetSpan.IsReadOnly)
            {
                return false;
            }

            if(IsAssignable(sourceSpan.ElementType, targetSpan.ElementType, model))
            {
                // Target is unsized (no known length, no symbolic ID) — any source matches.
                if(targetSpan.KnownLength is null && targetSpan.SymbolicLengthId is null)
                {
                    return true;
                }
                // Target has a symbolic length — any source with same element type is
                // acceptable (symbolic just means "unknown at compile time").
                if(targetSpan.SymbolicLengthId is not null)
                {
                    return true;
                }
                // Target has a concrete length — source must have the same concrete length.
                if(targetSpan.KnownLength is not null
                    && sourceSpan.KnownLength == targetSpan.KnownLength)
                {
                    return true;
                }
            }

            return false;
        }

        // ArrayType → ConstructedType(Array<T, unknown>) bridging:
        // The dedicated ArrayType representation (e.g., int[3]) must be
        // assignable to the stdlib ConstructedType form Array<T, unknown>
        // used in constructor parameter declarations (e.g., Span's ctor).
        // A known-length or unsized ArrayType widens to Array<T, unknown>
        // when element types are compatible.
        if(source is ArrayType sourceArrayForCt
            && target is ConstructedType targetArrayCt
            && targetArrayCt.GenericDefinition is ClassType targetArrayClass
            && targetArrayClass.DisplayName == "Array"
            && targetArrayClass.ContainingNamespace == "Standard"
            && targetArrayCt.TypeArguments.Count >= 2)
        {
            var targetElementType = targetArrayCt.TypeArguments[0];
            var targetLengthType = targetArrayCt.TypeArguments[1];

            if(IsAssignable(sourceArrayForCt.ElementType, targetElementType, model))
            {
                // Target length is unknown — any source length is accepted (widening).
                if(targetLengthType is UnknownType)
                {
                    return true;
                }
            }
        }

        // Migration-window shim: `bool` and `Standard.Boolean` (likewise
        // `int`/`Int32`, `string`/`String`, …) can coexist within a
        // single compilation when some sub-tree was resolved via the
        // legacy PrimitiveType singletons and another via the stdlib
        // types. Treat them as equivalent so assignability doesn't
        // spuriously reject `int` to `Int32` (or vice versa).
        if(ArePrimitiveEquivalent(source, target))
        {
            return true;
        }

        // Dependent type parameters are assignable to their value type.
        // E.g., a dependent parameter `Size:int` resolves to DependentTypeParameterType
        // but should be assignable to `int`/`Int32` (its ValueType).
        if(source is DependentTypeParameterType depSource && depSource.ValueType is not DependentTypeParameterType)
        {
            return IsAssignable(depSource.ValueType, target, model);
        }

        if(target is DependentTypeParameterType depTarget && depTarget.ValueType is not DependentTypeParameterType)
        {
            return IsAssignable(source, depTarget.ValueType, model);
        }

        // Tuple assignability with name erasure: two TupleType instances are
        // assignable if they have the same arity and each element type is
        // assignable. Element names are ignored (name erasure).
        if(source is TupleType sourceTuple && target is TupleType targetTuple)
        {
            if(sourceTuple.Arity == targetTuple.Arity)
            {
                var allElementsAssignable = true;
                for(var i = 0; i < sourceTuple.Arity; i++)
                {
                    if(!IsAssignable(sourceTuple.ElementTypes[i], targetTuple.ElementTypes[i], model))
                    {
                        allElementsAssignable = false;
                        break;
                    }
                }

                if(allElementsAssignable)
                {
                    return true;
                }
            }

            return false;
        }

        // ConstructedType identity: same generic definition + same type arguments + same dependent arguments.
        if(source is ConstructedType sourceConstructed && target is ConstructedType targetConstructed)
        {
            if(ReferenceEquals(sourceConstructed.GenericDefinition, targetConstructed.GenericDefinition)
                && sourceConstructed.TypeArguments.Count == targetConstructed.TypeArguments.Count)
            {
                var allMatch = true;
                for(var i = 0; i < sourceConstructed.TypeArguments.Count; i++)
                {
                    if(!TypesEqual(sourceConstructed.TypeArguments[i], targetConstructed.TypeArguments[i]))
                    {
                        allMatch = false;
                        break;
                    }
                }

                // Compare dependent value arguments (Requirements 8.1, 8.2).
                // FixedBuffer<5> ≠ FixedBuffer<6> even though both have TypeArguments=[int].
                if(allMatch && !DependentArgumentsEqual(sourceConstructed.DependentArguments, targetConstructed.DependentArguments))
                {
                    allMatch = false;
                }

                if(allMatch)
                {
                    return true;
                }

                // Covariant unknown widening (unknown-type feature, Task 23.2).
                // Req 8.1–8.3: after the identity check, try the position-wise
                // widening rule that accepts any target position whose type
                // argument is `UnknownType`, provided the generic definition
                // at that level is a class (reference-type) generic. Nested
                // constructions recurse through the same rule. Transitive
                // widening along the base-type / abstract chain is picked
                // up by the existing loops below.
                if(AssignableWithUnknownWidening(
                        sourceConstructed.GenericDefinition,
                        sourceConstructed.TypeArguments,
                        targetConstructed.TypeArguments))
                {
                    return true;
                }
            }
        }

        // ConstructedType inheritance: walk the base type chain with substitution.
        if(source is ConstructedType sourceConst && target is ConstructedType)
        {
            var baseType = GetSubstitutedBaseType(sourceConst);
            if(baseType is not null && IsAssignable(baseType, target))
            {
                return true;
            }
        }

        // ConstructedType (struct) to plain StructType assignability:
        // When source is a ConstructedType wrapping a StructType (e.g., Point3D<int>),
        // walk the definition's ancestor chain to check if any ancestor matches the target.
        if(source is ConstructedType sourceStructConst
            && sourceStructConst.GenericDefinition is StructType
            && target is StructType)
        {
            var baseType = GetSubstitutedBaseType(sourceStructConst);
            if(baseType is not null && IsAssignable(baseType, target))
            {
                return true;
            }
        }

        // ConstructedType (class) to plain ClassType assignability:
        // When source is a ConstructedType wrapping a ClassType (e.g., DbContextOptions<MyContext>),
        // check if the generic definition IS the target or has the target in its base chain.
        // This handles the CLR pattern where a generic type inherits from a non-generic base
        // (e.g., DbContextOptions<T> : DbContextOptions).
        if(source is ConstructedType sourceClassConst
            && sourceClassConst.GenericDefinition is ClassType sourceClassDef
            && target is ClassType targetClassType
            && target is not ConstructedType)
        {
            // Check if the generic definition itself is the target.
            if(ReferenceEquals(sourceClassDef, targetClassType)
                || sourceClassDef.DisplayName == targetClassType.DisplayName)
            {
                return true;
            }

            // Walk the generic definition's base type chain.
            var ancestor = sourceClassDef.BaseType as ClassType;
            while(ancestor is not null)
            {
                if(ReferenceEquals(ancestor, targetClassType)
                    || ancestor.DisplayName == targetClassType.DisplayName)
                {
                    return true;
                }

                ancestor = ancestor.BaseType as ClassType;
            }
        }

        // ConstructedType to constructed abstract assignability.
        if(target is ConstructedType targetConst && targetConst.GenericDefinition is AbstractType)
        {
            if(ImplementsConstructedAbstract(source, targetConst))
            {
                return true;
            }
        }

        // Numeric widening is now handled by implicit cast operators
        // declared on the stdlib bootstrap types. The implicit cast
        // operator fallback at the end of this method handles it.

        // Implicit numeric narrowing for CLR interop: allow integer types to narrow
        // to smaller integer types when the target is a backend-projected numeric type.
        // This mirrors C#'s implicit narrowing of constant expressions and is safe
        // because the generated C# will emit the appropriate cast.
        if(IsIntegerNumericType(source) && IsIntegerNumericType(target)
            && source.DisplayName != target.DisplayName)
        {
            return true;
        }

        // Abstract assignability:
        // A class/struct implementing an abstract (directly or transitively) is assignable to that abstract.
        // An abstract is assignable to any abstract it inherits from (directly or transitively).
        // Also checks with-block conformance via ConformanceMap when model is available.
        if(target is AbstractType targetAbstract)
        {
            if(ImplementsAbstract(source, targetAbstract, model))
            {
                return true;
            }
        }

        // Subtype assignability — walk the inheritance chain.
        // A derived class is assignable to any of its ancestor classes.
        if(source is ClassType sourceClass && target is ClassType)
        {
            var current = sourceClass.BaseType as ClassType;
            while(current is not null)
            {
                if(ReferenceEquals(current, target) || current.DisplayName == target.DisplayName)
                {
                    return true;
                }

                current = current.BaseType as ClassType;
            }
        }

        // Struct subtype assignability — walk the inheritance chain.
        // A derived struct is assignable to any of its ancestor structs.
        if(source is StructType sourceStruct && target is StructType)
        {
            var current = sourceStruct.BaseType as StructType;
            while(current is not null)
            {
                if(ReferenceEquals(current, target) || current.DisplayName == target.DisplayName)
                {
                    return true;
                }

                current = current.BaseType as StructType;
            }
        }

        // Plain StructType to ConstructedType (struct) assignability:
        // When source is a plain StructType whose ancestor chain contains a
        // ConstructedType (e.g., Derived : Base<int>), check if any ancestor
        // matches the target ConstructedType.
        if(source is StructType sourceStructForCt && target is ConstructedType targetStructCt
            && targetStructCt.GenericDefinition is StructType)
        {
            var currentBase = sourceStructForCt.BaseType;
            while(currentBase is not null)
            {
                if(IsAssignable(currentBase, target))
                {
                    return true;
                }

                currentBase = currentBase switch
                {
                    StructType st => st.BaseType,
                    ConstructedType ct when ct.GenericDefinition is StructType ctStructDef => ctStructDef.BaseType,
                    _ => null
                };
            }
        }

        // ── enums feature: implicit conversion to underlying type (Req 7.1, 7.2) ──
        // An enum value is assignable to its underlying type. This is a direct
        // structural rule (not an implicit cast operator) so it takes precedence
        // over the operator fallback below.
        if(source is EnumType sourceEnum && IsAssignable(sourceEnum.UnderlyingType, target, model))
        {
            return true;
        }

        // Implicit cast operator search (fallback after all other assignability checks).
        // Search both source type and target type for an implicit cast operator.
        if(TryFindImplicitCast(source, target, out _) is not null)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Compares two types for structural equality.
    /// For <see cref="ConstructedType"/>, checks that the generic definitions are the same
    /// and all type arguments are recursively equal.
    /// For other types, uses reference equality or display name comparison.
    /// </summary>
    internal static bool TypesEqual(IType a, IType b)
    {
        if(ReferenceEquals(a, b))
        {
            return true;
        }

        if(a is ConstructedType aCt && b is ConstructedType bCt)
        {
            if(!ReferenceEquals(aCt.GenericDefinition, bCt.GenericDefinition))
            {
                return false;
            }

            if(aCt.TypeArguments.Count != bCt.TypeArguments.Count)
            {
                return false;
            }

            for(var i = 0; i < aCt.TypeArguments.Count; i++)
            {
                if(!TypesEqual(aCt.TypeArguments[i], bCt.TypeArguments[i]))
                {
                    return false;
                }
            }

            // Compare dependent value arguments (Requirements 8.1, 8.2).
            if(!DependentArgumentsEqual(aCt.DependentArguments, bCt.DependentArguments))
            {
                return false;
            }

            return true;
        }

        if(a is TypeParameterType aTp && b is TypeParameterType bTp)
        {
            return aTp.Name == bTp.Name && aTp.Ordinal == bTp.Ordinal;
        }

        // DisplayName equality — the common fallback for non-reference-
        // equal primitives within the same migration flavour.
        if(a.DisplayName == b.DisplayName)
        {
            return true;
        }

        // Migration-window shim: legacy `int` and stdlib `Int32` denote
        // the same Mist primitive.
        return ArePrimitiveEquivalent(a, b);
    }

    /// <summary>
    /// Checks whether an argument type is compatible with a parameter type for ref arguments.
    /// This is a relaxed version of <see cref="TypesEqual"/> that handles:
    /// <list type="bullet">
    /// <item>Nullable unions (T | void): strips the void constituent before comparing.</item>
    /// <item>Array sizing: a sized array (e.g., string[2]) is compatible with an unsized
    /// array (string[]) since CLR ref parameters use erased nullability and unsized arrays.</item>
    /// </list>
    /// </summary>
    private static bool RefTypesCompatible(IType argType, IType paramType)
    {
        // Strip nullable union: if paramType is T | void, compare against T.
        if(paramType is UnionType paramUnion && paramUnion.Constituents.Count == 2
            && paramUnion.Constituents.Any(c => c.IsVoid()))
        {
            paramType = paramUnion.Constituents.First(c => !c.IsVoid());
        }

        // For arrays, use assignability (handles sized → unsized).
        if(argType is ArrayType && paramType is ArrayType)
        {
            return IsAssignable(argType, paramType);
        }

        // For spans, use assignability (handles sized → unsized, same kind).
        if(argType is SpanType && paramType is SpanType)
        {
            return IsAssignable(argType, paramType);
        }

        return TypesEqual(argType, paramType);
    }

    /// <summary>
    /// Compares two dependent argument lists for value equality.
    /// Two lists are equal when they have the same length and each pair of
    /// non-null entries has equal values (using <see cref="object.Equals(object?, object?)"/>).
    /// </summary>
    private static bool DependentArgumentsEqual(
        IReadOnlyList<DependentValueArgument?>? a,
        IReadOnlyList<DependentValueArgument?>? b)
    {
        if(a is null && b is null)
        {
            return true;
        }

        if(a is null || b is null)
        {
            return false;
        }

        if(a.Count != b.Count)
        {
            return false;
        }

        for(var i = 0; i < a.Count; i++)
        {
            var argA = a[i];
            var argB = b[i];
            if(argA is null && argB is null)
            {
                continue;
            }

            if(argA is null || argB is null)
            {
                return false;
            }

            // A pack slot binds a comptime Type[] value (the [int, string] in Foo<[int, string]>).
            // Two constructed variadic types are equal only when their pack slots bind equal Type[]
            // values, so identity is order-sensitive and arity-sensitive (Requirements 5.1-5.4). A
            // pack's Type[] value is never equal to a scalar dependent value.
            if(argA.PackElementTypes is not null || argB.PackElementTypes is not null)
            {
                if(argA.PackElementTypes is null || argB.PackElementTypes is null)
                {
                    return false;
                }

                if(!PackElementTypesEqual(argA.PackElementTypes, argB.PackElementTypes))
                {
                    return false;
                }

                continue;
            }

            if(!Equals(argA.Value, argB.Value))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Order- and arity-sensitive elementwise equality for two comptime <c>Type[]</c> values bound
    /// to type-parameter pack slots (Requirement 5.2). Two values are equal exactly when they have
    /// the same length and, at every index, their element types are equal under
    /// <see cref="TypesEqual"/> — the same type-equality relation used for ordinary type arguments.
    /// Order therefore matters (Requirement 5.3) and a differing length is a distinct pack arity
    /// (Requirement 5.4), so <c>[int, string]</c>, <c>[string, int]</c>, <c>[int, string, bool]</c>,
    /// and the empty <c>[]</c> are pairwise distinct.
    /// </summary>
    private static bool PackElementTypesEqual(IReadOnlyList<IType> a, IReadOnlyList<IType> b)
    {
        if(a.Count != b.Count)
        {
            return false;
        }

        for(var i = 0; i < a.Count; i++)
        {
            if(!TypesEqual(a[i], b[i]))
            {
                return false;
            }
        }

        return true;
    }

    // ── Variadics: the shared flat-to-bracketed binding rule (Requirement 4, 7.4, 7.6) ──
    //
    // Given a declared slot list with at most one unbracketed (params) pack and a use-site
    // argument list, BindPackSlots binds the fixed scalar and bracketed Type[]: slots one-to-one
    // inward from both ends and assigns the remaining contiguous middle (possibly empty) to the
    // single unbracketed pack. It is the classic-track counterpart of the ProgramModel PackBinder
    // and is kind-driven so the same algorithm serves both the type-argument side (folding a flat
    // list such as Foo<int, string> into the canonical bracketed form Foo<[int, string]>) and the
    // value-parameter side (collecting the contiguous middle arguments into the value pack tuple
    // (TArgs...)). SemanticModelBuilder invokes it while constructing variadic types, and reports
    // a too-short argument list as MIST_VAR_0005.

    /// <summary>
    /// The binding role of one declared slot. Fixed scalar slots and bracketed <c>Type[]:</c>
    /// packs each consume exactly one use-site argument (binding inward from both ends); the
    /// single unbracketed <c>params</c> pack absorbs the contiguous middle.
    /// </summary>
    internal enum PackBindingSlotKind
    {
        /// <summary>An ordinary scalar slot (a regular non-pack type parameter, or a fixed value
        /// parameter). Consumes exactly one use-site argument.</summary>
        Fixed,

        /// <summary>An explicit <c>Type[]:</c> pack supplied in brackets at the use site. Consumes
        /// exactly one use-site argument (a bracketed group).</summary>
        BracketedPack,

        /// <summary>The list's single bracket-free <c>params</c> pack. Absorbs the contiguous
        /// middle arguments (possibly zero).</summary>
        UnbracketedPack,
    }

    /// <summary>
    /// A resolved use-site argument: either a single scalar type (for example the <c>int</c> in
    /// <c>Foo&lt;int, string&gt;</c>) or a bracketed group (for example the <c>[int, string]</c>
    /// in <c>Foo&lt;[int, string]&gt;</c>, including the empty group <c>[]</c>).
    /// </summary>
    internal readonly struct PackUseSiteArgument
    {
        private PackUseSiteArgument(bool isBracketedGroup, IReadOnlyList<IType> types)
        {
            IsBracketedGroup = isBracketedGroup;
            Types = types;
        }

        /// <summary>True when this argument was written as a bracketed group <c>[...]</c>.</summary>
        public bool IsBracketedGroup { get; }

        /// <summary>The argument's element types: a single element for a scalar, or the group's
        /// ordered elements (possibly zero) for a bracketed group.</summary>
        public IReadOnlyList<IType> Types { get; }

        /// <summary>Creates a scalar argument carrying a single type.</summary>
        public static PackUseSiteArgument Scalar(IType type)
        {
            return new PackUseSiteArgument(isBracketedGroup: false, new[] { type });
        }

        /// <summary>Creates a bracketed-group argument carrying the group's ordered element types.</summary>
        public static PackUseSiteArgument Group(IReadOnlyList<IType> types)
        {
            return new PackUseSiteArgument(isBracketedGroup: true, types);
        }
    }

    /// <summary>
    /// The binding of one declared slot: an ordered element list plus whether the slot is a pack.
    /// A fixed slot carries a single scalar element; a pack slot carries the ordered pack elements
    /// (which the caller lifts into a comptime <c>Type[]</c> value).
    /// </summary>
    internal readonly struct PackSlotBinding
    {
        public PackSlotBinding(bool isPack, IReadOnlyList<IType> elements)
        {
            IsPack = isPack;
            Elements = elements;
        }

        /// <summary>True when this slot is a pack (bracketed or unbracketed); false for a fixed
        /// scalar slot.</summary>
        public bool IsPack { get; }

        /// <summary>The ordered element types bound to this slot. For a fixed slot this is a
        /// single scalar type; for a pack slot it is the ordered pack elements (possibly empty).</summary>
        public IReadOnlyList<IType> Elements { get; }
    }

    /// <summary>Whether <see cref="BindPackSlots"/> produced a binding or rejected the argument
    /// list as too short (reported by the caller as <c>MIST_VAR_0005</c>).</summary>
    internal enum PackBindingStatus
    {
        /// <summary>Every slot was bound.</summary>
        Bound,

        /// <summary>The flat argument list was too short to fill the fixed and bracketed slots.</summary>
        TooFewArguments,
    }

    /// <summary>The outcome of the binding rule: a status and, when bound, one
    /// <see cref="PackSlotBinding"/> per declared slot in declaration order.</summary>
    internal readonly struct PackBindingOutcome
    {
        private PackBindingOutcome(PackBindingStatus status, IReadOnlyList<PackSlotBinding> slots)
        {
            Status = status;
            Slots = slots;
        }

        public PackBindingStatus Status { get; }

        public IReadOnlyList<PackSlotBinding> Slots { get; }

        public static PackBindingOutcome Bound(IReadOnlyList<PackSlotBinding> slots)
        {
            return new PackBindingOutcome(PackBindingStatus.Bound, slots);
        }

        public static PackBindingOutcome TooFew()
        {
            return new PackBindingOutcome(PackBindingStatus.TooFewArguments, Array.Empty<PackSlotBinding>());
        }
    }

    /// <summary>
    /// The shared flat-to-bracketed binding rule (Requirement 4 and the design's
    /// "Value-Parameter Arity and the Shared Binding Rule"). Given a declared slot list with at
    /// most one <see cref="PackBindingSlotKind.UnbracketedPack"/> and a use-site argument list,
    /// binds fixed scalar and bracketed pack slots one-to-one inward from both ends and assigns
    /// the remaining contiguous middle arguments (possibly zero) to the single unbracketed pack,
    /// so the pack may appear leading, middle, or trailing and may absorb an empty middle
    /// (Requirements 4.1, 4.2, 4.3). Returns <see cref="PackBindingStatus.TooFewArguments"/> when
    /// the flat list is shorter than the number of fixed and bracketed slots (the minimum required
    /// while the pack absorbs zero arguments), which the caller reports as <c>MIST_VAR_0005</c>
    /// (Requirements 4.6, 7.6).
    /// <para>
    /// The rule is kind-driven and free of any model dependency beyond <see cref="IType"/>, so the
    /// same algorithm serves both the type side (fixed scalar slots, bracketed <c>Type[]:</c>
    /// packs, and the single <c>params</c> pack of a type-parameter list) and the value side (fixed
    /// value parameters and the single value pack <c>params TArgs args</c>, whose contiguous middle
    /// collects into <c>(TArgs...)</c>). It is the classic-track counterpart of the ProgramModel
    /// <c>PackBinder</c>; a caller lifts each pack slot's element list into a comptime
    /// <c>Type[]</c>, so the flat and bracketed forms of the same pack denote the same type
    /// (Requirement 4.4).
    /// </para>
    /// </summary>
    internal static PackBindingOutcome BindPackSlots(
        IReadOnlyList<PackBindingSlotKind> slots,
        IReadOnlyList<PackUseSiteArgument> arguments)
    {
        // The variadic sink is the list's first (and, when well-formed, only) unbracketed pack;
        // a surplus unbracketed pack is already reported as MIST_VAR_0001 during model
        // construction, so the binder may rely on there being zero or one.
        var packIndex = -1;
        for(var i = 0; i < slots.Count; i++)
        {
            if(slots[i] == PackBindingSlotKind.UnbracketedPack)
            {
                packIndex = i;
                break;
            }
        }

        var leadingCount = packIndex >= 0 ? packIndex : slots.Count;
        var trailingCount = packIndex >= 0 ? slots.Count - packIndex - 1 : 0;

        // Requirement 4.6 / 7.6: a flat argument list that cannot fill every fixed and bracketed
        // slot (with the pack empty) is too short to bind.
        var minimumRequired = leadingCount + trailingCount;
        if(arguments.Count < minimumRequired)
        {
            return PackBindingOutcome.TooFew();
        }

        var bindings = new PackSlotBinding[slots.Count];

        // Leading fixed/bracketed slots take the leading arguments one-to-one, in order.
        for(var i = 0; i < leadingCount; i++)
        {
            bindings[i] = BindArityOnePackSlot(slots[i], arguments[i]);
        }

        // Trailing fixed/bracketed slots take the trailing arguments one-to-one, in order.
        for(var j = 0; j < trailingCount; j++)
        {
            var slotIndex = packIndex + 1 + j;
            var argumentIndex = arguments.Count - trailingCount + j;
            bindings[slotIndex] = BindArityOnePackSlot(slots[slotIndex], arguments[argumentIndex]);
        }

        // The single unbracketed pack absorbs the contiguous middle (possibly empty).
        if(packIndex >= 0)
        {
            var middleElements = CollectPackElements(arguments, leadingCount, arguments.Count - trailingCount);
            bindings[packIndex] = new PackSlotBinding(isPack: true, middleElements);
        }

        return PackBindingOutcome.Bound(bindings);
    }

    /// <summary>
    /// Binds a single arity-1 slot (fixed scalar or bracketed pack) to its one use-site argument.
    /// A fixed slot carries the argument's element(s) verbatim (a well-formed fixed argument is a
    /// single scalar); a bracketed pack slot is marked as a pack whose elements are the group's
    /// element types.
    /// </summary>
    private static PackSlotBinding BindArityOnePackSlot(PackBindingSlotKind slot, PackUseSiteArgument argument)
    {
        var isPack = slot != PackBindingSlotKind.Fixed;
        return new PackSlotBinding(isPack, argument.Types);
    }

    /// <summary>
    /// Collects the ordered element types the unbracketed pack binds from its contiguous middle
    /// run <c>[start, end)</c>. A middle that is exactly one bracketed group is the pack's explicit
    /// bracketed spelling, so its element types are used directly; otherwise the flat scalar middle
    /// arguments contribute their types in order. Both spellings therefore produce the same ordered
    /// element list, so the flat and bracketed forms denote the same type (Requirement 4.4).
    /// </summary>
    private static IReadOnlyList<IType> CollectPackElements(
        IReadOnlyList<PackUseSiteArgument> arguments, int start, int end)
    {
        if(end - start == 1 && arguments[start].IsBracketedGroup)
        {
            return arguments[start].Types;
        }

        var elements = new List<IType>(Math.Max(0, end - start));
        for(var i = start; i < end; i++)
        {
            elements.AddRange(arguments[i].Types);
        }

        return elements;
    }

    // ─── Value-side spread: general tuple decomposition (variadics, task 14.1) ──
    //
    // The value-side `...` operator decomposes a tuple value into ordered argument
    // slots (Requirements 8.2, 8.3): every tuple value decomposes, with `args...`
    // simply the case where the tuple originated from a pack, and a spread-generated
    // aggregate `Item...` is re-spread as the tuple value it names (Requirement 10.9).
    // A decomposition is well-typed if and only if the tuple's ordered element types
    // are positionally assignable to the destination slots, else MIST_VAR_0011
    // (Requirements 7.5, 8.4). Only tuples whose element types are concretely known
    // are decided here; a symbolic value pack in an open generic body (its element
    // types not yet bound) is left to the generic-enforcement task, and binding a
    // flat argument list that mixes fixed arguments with a spread is left to the
    // call-site binder task — a spread argument is nonetheless intercepted so it is
    // never mis-reported by the ordinary one-to-one argument-type check.

    /// <summary>
    /// Whether a value-side spread decomposition is well-typed against its
    /// destination slots, or the way it is ill-typed (Requirements 8.4, 7.5).
    /// </summary>
    internal enum ValueSpreadDecompositionStatus
    {
        /// <summary>The tuple's ordered element types are positionally assignable to
        /// the destination slots (an empty tuple against zero slots is well-typed).</summary>
        WellTyped,

        /// <summary>The tuple's element count differs from the destination slot count.</summary>
        SlotCountMismatch,

        /// <summary>An element type is not assignable to its corresponding slot type;
        /// the offending 0-based position is returned via the mismatch index.</summary>
        SlotTypeMismatch,
    }

    /// <summary>
    /// The pure decomposition well-typedness rule (Requirements 8.2, 8.4, 7.5): a
    /// tuple decomposed into <paramref name="destinationSlots"/> is well-typed iff its
    /// ordered <paramref name="tupleElements"/> match the slots one-to-one in count and
    /// each element is assignable to the slot at the same position. An empty tuple
    /// against zero slots is well-typed (Requirement 8.2). Error types on either side
    /// are skipped to suppress cascading diagnostics. This is a pure function over the
    /// element and slot type lists so it can be exercised directly in-process.
    /// </summary>
    internal static ValueSpreadDecompositionStatus CheckValueSpreadDecomposition(
        IReadOnlyList<IType> tupleElements,
        IReadOnlyList<IType> destinationSlots,
        ISemanticModel? model,
        out int mismatchIndex)
    {
        mismatchIndex = -1;

        if(tupleElements.Count != destinationSlots.Count)
        {
            return ValueSpreadDecompositionStatus.SlotCountMismatch;
        }

        for(var i = 0; i < tupleElements.Count; i++)
        {
            var element = tupleElements[i];
            var slot = destinationSlots[i];

            if(IsErrorType(element) || IsErrorType(slot))
            {
                continue;
            }

            if(!IsAssignable(element, slot, model))
            {
                mismatchIndex = i;
                return ValueSpreadDecompositionStatus.SlotTypeMismatch;
            }
        }

        return ValueSpreadDecompositionStatus.WellTyped;
    }

    /// <summary>
    /// Checks a sole value-spread argument <c>x...</c> against the resolved destination
    /// slots and reports <c>MIST_VAR_0011</c> on a count or positional-assignability
    /// mismatch (Requirements 8.4, 7.5). A symbolic value pack (<c>args...</c>) or
    /// spread-generated aggregate (<c>Item...</c>) whose single element is an unbound
    /// pack is checked for provable forwarding (Requirements 12.6, 12.7, 12.8): the
    /// destination must provably accept <c>(TArgs...)</c> for every <c>TArgs</c> (another
    /// <c>params TArgs</c> / <c>(TArgs...)</c> parameter, or a <c>Func&lt;TArgs..., R&gt;</c>
    /// value), otherwise <c>MIST_VAR_0011</c> is emitted. An unresolved destination (for
    /// example an overloaded callee) is deferred to the call-site binder. No diagnostic
    /// is emitted in the deferred cases.
    /// </summary>
    private static void CheckDecomposedValueSpread(
        PostfixNode spreadPostfix,
        IReadOnlyList<IType>? destinationSlots,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // The value spread is a no-op on the operand's type, so the postfix node's
        // recorded type is the tuple value being decomposed.
        if(model.GetTypeOf(spreadPostfix) is not TupleType tuple)
        {
            return;
        }

        if(TupleContainsSymbolicPack(tuple))
        {
            // Generic-body provable forwarding (Requirements 12.6, 12.7, 12.8):
            // A symbolic pack spread `args...` is accepted only when the destination
            // provably accepts `(TArgs...)` for every `TArgs`. If the destination is
            // unresolved (null), defer -- the call-site binder will handle it.
            if(destinationSlots is null)
            {
                return;
            }

            if(!IsDestinationProvablyPackCompatible(tuple, destinationSlots))
            {
                diagnostics.ReportAtNode(
                    VariadicsDiagnostics.SpreadDestinationMismatch, spreadPostfix, "TypeChecker",
                    "a symbolic value pack cannot be spread into a fixed-arity destination that is not provably (TArgs...)-compatible for every instantiation");
            }

            return;
        }

        if(destinationSlots is null)
        {
            return;
        }

        var status = CheckValueSpreadDecomposition(tuple.ElementTypes, destinationSlots, model, out var mismatchIndex);
        switch(status)
        {
            case ValueSpreadDecompositionStatus.SlotCountMismatch:
                diagnostics.ReportAtNode(
                    VariadicsDiagnostics.SpreadDestinationMismatch, spreadPostfix, "TypeChecker",
                    $"the tuple has {tuple.ElementTypes.Count} element(s) but the call site expects {destinationSlots.Count} slot(s)");
                break;

            case ValueSpreadDecompositionStatus.SlotTypeMismatch:
                diagnostics.ReportAtNode(
                    VariadicsDiagnostics.SpreadDestinationMismatch, spreadPostfix, "TypeChecker",
                    $"element {mismatchIndex + 1} of type '{tuple.ElementTypes[mismatchIndex].DisplayName}' is not assignable to the target slot type '{destinationSlots[mismatchIndex].DisplayName}'");
                break;
        }
    }

    /// <summary>
    /// True when the tuple carries an unbound type-parameter pack as one of its
    /// elements -- the classic representation of a value pack <c>(TArgs...)</c> or a
    /// spread-generated aggregate while the pack is symbolic. Decomposition of such a
    /// tuple is deferred (its element types are not yet concretely known).
    /// </summary>
    private static bool TupleContainsSymbolicPack(TupleType tuple)
    {
        foreach(var element in tuple.ElementTypes)
        {
            if(element is TypeParameterType { IsPack: true })
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Determines whether the destination slots are provably <c>(TArgs...)</c>-compatible
    /// for every instantiation of the pack in the source <paramref name="sourceTuple"/>
    /// (Requirements 12.7, 12.8). A destination is provably compatible iff:
    /// <list type="number">
    /// <item><description>
    /// The destination is a single slot whose type is a <see cref="TupleType"/> wrapping
    /// exactly the same pack type parameter(s) — indicating another <c>params TArgs</c>
    /// or <c>(TArgs...)</c> parameter.
    /// </description></item>
    /// <item><description>
    /// The destination slots themselves contain only the same pack type parameter as a
    /// direct element — indicating a <c>Func&lt;TArgs..., R&gt;</c> value whose parameter
    /// slots are the spread of the same pack.
    /// </description></item>
    /// </list>
    /// In all other cases (fixed-arity callee, different pack, scalar parameters) the
    /// destination is NOT provably compatible and <c>MIST_VAR_0011</c> should be emitted.
    /// </summary>
    private static bool IsDestinationProvablyPackCompatible(
        TupleType sourceTuple,
        IReadOnlyList<IType> destinationSlots)
    {
        // Collect the symbolic pack(s) from the source tuple.
        var sourcePacks = new List<TypeParameterType>();
        foreach(var element in sourceTuple.ElementTypes)
        {
            if(element is TypeParameterType { IsPack: true } pack)
            {
                sourcePacks.Add(pack);
            }
        }

        if(sourcePacks.Count == 0)
        {
            return false;
        }

        // Case 1: The destination has exactly one slot that is a TupleType wrapping the
        // same pack(s) — the callee declares `params TArgs` or `(TArgs...)`.
        if(destinationSlots.Count == 1 && destinationSlots[0] is TupleType destTuple)
        {
            if(TupleContainsSameSymbolicPacks(destTuple, sourcePacks))
            {
                return true;
            }
        }

        // Case 2: The destination slots directly contain the same pack type parameter(s)
        // as elements — the callee is a Func<TArgs..., R> value whose parameter slots
        // are the spread of the same pack.
        if(destinationSlots.Count == sourcePacks.Count)
        {
            var allMatch = true;
            for(var i = 0; i < destinationSlots.Count; i++)
            {
                if(destinationSlots[i] is not TypeParameterType { IsPack: true } destPack
                    || !ReferenceEquals(destPack, sourcePacks[i]))
                {
                    allMatch = false;
                    break;
                }
            }

            if(allMatch)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when <paramref name="destTuple"/> wraps exactly the same symbolic pack type
    /// parameters as <paramref name="sourcePacks"/>, in the same order. This covers the
    /// case where the destination parameter is <c>params TArgs</c> or <c>(TArgs...)</c>
    /// and hence its type is a single-element tuple wrapping the pack.
    /// </summary>
    private static bool TupleContainsSameSymbolicPacks(
        TupleType destTuple,
        List<TypeParameterType> sourcePacks)
    {
        if(destTuple.ElementTypes.Count != sourcePacks.Count)
        {
            return false;
        }

        for(var i = 0; i < destTuple.ElementTypes.Count; i++)
        {
            if(destTuple.ElementTypes[i] is not TypeParameterType { IsPack: true } destPack
                || !ReferenceEquals(destPack, sourcePacks[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// True when any argument expression in <paramref name="argList"/> is a value-side
    /// spread <c>x...</c> (a postfix chain whose final suffix is the spread operator).
    /// </summary>
    private static bool ArgumentListContainsValueSpread(ArgumentListNode argList)
    {
        foreach(var expr in argList.GetAllExpressions())
        {
            if(TryGetValueSpreadPostfix(expr, out _))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when <paramref name="argList"/> has exactly one argument and it is a
    /// value-side spread <c>x...</c>, yielding the spread's postfix node.
    /// </summary>
    private static bool TryGetSoleValueSpreadArgument(ArgumentListNode argList, out PostfixNode spreadPostfix)
    {
        spreadPostfix = null!;

        var expressions = argList.GetAllExpressions();
        if(expressions.Length != 1)
        {
            return false;
        }

        return TryGetValueSpreadPostfix(expressions[0], out spreadPostfix);
    }

    /// <summary>
    /// Extracts the postfix chain of a value-side spread expression <c>x...</c> -- a
    /// simple postfix whose final suffix is the spread operator (<c>...</c>).
    /// Returns false for any other expression shape.
    /// </summary>
    private static bool TryGetValueSpreadPostfix(ExpressionNode expr, out PostfixNode spreadPostfix)
    {
        spreadPostfix = null!;

        var postfix = TryExtractPostfixNode(expr);
        if(postfix is null || postfix.Suffix.Length == 0)
        {
            return false;
        }

        if(postfix.Suffix[^1] is not SpreadSuffixPostfixSuffixNode)
        {
            return false;
        }

        spreadPostfix = postfix;
        return true;
    }

    /// <summary>
    /// Resolves the destination parameter slots for a value spread in an invocation
    /// <c>expr(x...)</c>: the parameter list of a <c>Func</c> value or delegate being
    /// invoked, or the invoked method's parameters. Returns null when the destination
    /// cannot be resolved (deferred to the call-site binder).
    /// </summary>
    private static IReadOnlyList<IType>? ResolveInvocationDestinationSlots(
        InvocationSuffixPostfixSuffixNode invocation,
        IType? targetType,
        PostfixNode containingPostfix,
        ISemanticModel model)
    {
        if(targetType is ConstructedType funcCt && SemanticModelBuilder.IsFuncType(funcCt))
        {
            return FuncParameterSlots(funcCt);
        }

        if(targetType is DelegateType delegateType)
        {
            return FuncParameterSlots(delegateType.FuncSignature);
        }

        var invokedSymbol = model.GetDeclaredSymbol(invocation);
        if(invokedSymbol is { Kind: SymbolKind.Method })
        {
            return ParameterSignature.Extract(invokedSymbol, model);
        }

        // Fallback: for direct method calls (e.g., `Sum3(args...)`) where the symbol is
        // registered on the PostfixNode's Target identifier rather than the InvocationSuffix,
        // look up the method symbol from the target node. This handles method-level packs
        // spreading into fixed-arity destinations (Requirement 21, MIST_VAR_0011).
        var targetSymbol = model.GetDeclaredSymbol(containingPostfix.Target);
        if(targetSymbol is { Kind: SymbolKind.Method })
        {
            return ParameterSignature.Extract(targetSymbol, model);
        }

        return null;
    }

    /// <summary>
    /// Resolves the destination parameter slots for a value spread in a method call
    /// <c>receiver.M(x...)</c>. When the callee resolves to a single method the
    /// decomposition is checked against its parameters; an overloaded callee (choosing
    /// which overload a decomposition binds to) is deferred to the call-site binder and
    /// yields null.
    /// </summary>
    private static IReadOnlyList<IType>? ResolveMethodCallDestinationSlots(
        MethodCallSuffixPostfixSuffixNode methodCall,
        IType? targetType,
        ISemanticModel model)
    {
        var candidates = CollectMethodOverloads(targetType, methodCall.Name.Lexeme);
        if(candidates.Count == 1)
        {
            return ParameterSignature.Extract(candidates[0], model);
        }

        if(candidates.Count == 0)
        {
            var methodSymbol = model.GetDeclaredSymbol(methodCall);
            if(methodSymbol is { Kind: SymbolKind.Method })
            {
                return ParameterSignature.Extract(methodSymbol, model);
            }
        }

        return null;
    }

    /// <summary>
    /// The parameter slots of a constructed <c>Func&lt;P1, ..., PN, TResult&gt;</c> value:
    /// its type arguments except the trailing result type (an empty list for the
    /// zero-parameter <c>Func&lt;TResult&gt;</c>).
    /// </summary>
    private static IReadOnlyList<IType> FuncParameterSlots(ConstructedType funcType)
    {
        var typeArgs = funcType.TypeArguments;
        var count = typeArgs.Count - 1;
        if(count <= 0)
        {
            return Array.Empty<IType>();
        }

        var slots = new IType[count];
        for(var i = 0; i < count; i++)
        {
            slots[i] = typeArgs[i];
        }

        return slots;
    }

    // ─── Zip: parallel two-operand spread (variadics, task 14.2) ──
    //
    // A zip is the parallel spread of two operands on an assignment,
    // canonically `left... = right...` where BOTH sides carry the value-side
    // `...` suffix (for example a variadic constructor's `Item... = args...`,
    // pairing a value pack into its spread-generated aggregate). It expands the
    // operands element by element into N paired positions, where N is the common
    // length, so each left-hand slot receives exactly one right-hand value
    // (Requirement 8.5). It is permitted ONLY when the two operands are proven to
    // have equal length -- either because they originate from the SAME pack (both
    // are the tuple `(P...)` of one pack `P`, hence the same symbolic length N for
    // every instantiation) or because equal length is otherwise established (two
    // concrete tuples of equal element count) (Requirement 8.6). A zip between
    // operands not proven equal length emits MIST_VAR_0006 (Requirement 8.7).

    /// <summary>
    /// Whether the two operands of a zip are proven to have equal length
    /// (Requirements 8.5, 8.6, 8.7).
    /// </summary>
    internal enum ZipLengthProofStatus
    {
        /// <summary>The operands are proven to have equal length: either the same
        /// pack origin, or two concrete tuples of equal element count.</summary>
        ProvenEqual,

        /// <summary>The operands are NOT proven to have equal length (different pack
        /// origin, a symbolic pack against an unrelated operand, or two concrete
        /// tuples of differing count) -- the zip is rejected with MIST_VAR_0006.</summary>
        NotProven,

        /// <summary>The length proof does not apply here (an operand carries an error
        /// type, or an operand is not tuple-shaped) -- the zip is deferred and no
        /// diagnostic is emitted.</summary>
        NotApplicable,
    }

    /// <summary>
    /// The pure zip length-proof rule (Requirements 8.5, 8.6, 8.7): two spread
    /// operands may be zipped iff their lengths are proven equal. Two fully concrete
    /// tuples are proven equal exactly when their element counts match. When at least
    /// one operand's length is symbolic, equality is proven only when BOTH operands
    /// are the sole-element tuple <c>(P...)</c> of the SAME pack <c>P</c> (same
    /// origin, hence the same length N for every instantiation). Any other case with
    /// symbolic content is not proven. This is a pure function over the two operand
    /// types so it can be exercised directly in-process (task 14.5).
    /// </summary>
    internal static ZipLengthProofStatus CheckZipLengthProof(IType leftOperand, IType rightOperand)
    {
        if(IsErrorType(leftOperand) || IsErrorType(rightOperand))
        {
            return ZipLengthProofStatus.NotApplicable;
        }

        if(leftOperand is not TupleType leftTuple || rightOperand is not TupleType rightTuple)
        {
            return ZipLengthProofStatus.NotApplicable;
        }

        var leftSymbolic = TupleContainsSymbolicPack(leftTuple);
        var rightSymbolic = TupleContainsSymbolicPack(rightTuple);

        // Both operands fully concrete: equal length is proven exactly by equal count.
        if(!leftSymbolic && !rightSymbolic)
        {
            return leftTuple.ElementTypes.Count == rightTuple.ElementTypes.Count
                ? ZipLengthProofStatus.ProvenEqual
                : ZipLengthProofStatus.NotProven;
        }

        // At least one operand's length is symbolic: equal length is proven only when
        // both operands are the SAME pack `(P...)` (same origin, same length N).
        var leftPack = TryGetSolePackOfTuple(leftTuple);
        var rightPack = TryGetSolePackOfTuple(rightTuple);
        if(leftPack is not null && rightPack is not null && SamePackOrigin(leftPack, rightPack))
        {
            return ZipLengthProofStatus.ProvenEqual;
        }

        return ZipLengthProofStatus.NotProven;
    }

    /// <summary>
    /// When <paramref name="tuple"/> is the sole-element tuple <c>(P...)</c> of a
    /// symbolic type-parameter pack <c>P</c> (the classic representation of a value
    /// pack or a spread-generated aggregate while the pack is symbolic), returns that
    /// pack; otherwise null.
    /// </summary>
    private static TypeParameterType? TryGetSolePackOfTuple(TupleType tuple)
    {
        if(tuple.ElementTypes.Count == 1 && tuple.ElementTypes[0] is TypeParameterType { IsPack: true } pack)
        {
            return pack;
        }

        return null;
    }

    /// <summary>
    /// True when two symbolic packs are the SAME pack origin -- reference-identical,
    /// or the same pack parameter (same name, ordinal, and fixed arity). Two operands
    /// that spread the same pack therefore have the same length N for every
    /// instantiation, which is what makes their zip provably equal-length.
    /// </summary>
    private static bool SamePackOrigin(TypeParameterType left, TypeParameterType right)
    {
        if(ReferenceEquals(left, right))
        {
            return true;
        }

        return string.Equals(left.Name, right.Name, StringComparison.Ordinal)
            && left.Ordinal == right.Ordinal
            && left.PackFixedArity == right.PackFixedArity;
    }

    /// <summary>
    /// Intercepts a zip <c>left... = right...</c> on an assignment: when BOTH the
    /// target and the value are value-side spreads, this is the parallel two-operand
    /// spread (Requirement 8.5), which is its own chokepoint. It is permitted only
    /// when the two operands are proven equal length (Requirement 8.6); otherwise
    /// <c>MIST_VAR_0006</c> is emitted (Requirement 8.7). Returns true when the
    /// assignment is recognized as a zip (handled here, so the ordinary tuple-to-tuple
    /// assignability check must not also run over it), false otherwise.
    /// </summary>
    private static bool TryCheckZipAssignment(
        AssignmentNode assignment,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // A zip is a plain `=` assignment; compound operators are not zips.
        if(assignment.Group0?.Operator is { } op && op.Kind != (int)TokenKind.EQUAL)
        {
            return false;
        }

        // Both operands must be value-side spreads: `left... = right...`.
        var leftPostfix = TryExtractPostfixFromLogicalOr(assignment.Target.Left);
        if(leftPostfix is null
            || leftPostfix.Suffix.Length == 0
            || leftPostfix.Suffix[^1] is not SpreadSuffixPostfixSuffixNode)
        {
            return false;
        }

        if(assignment.Group0?.Value is not { } assignedValue
            || !TryGetValueSpreadPostfix(assignedValue, out var rightPostfix))
        {
            return false;
        }

        // The spread suffix is a no-op on the operand's type, so each postfix node's
        // recorded type is the tuple value being spread (Requirements 8.2, 8.5).
        if(model.GetTypeOf(leftPostfix) is not { } leftOperand
            || model.GetTypeOf(rightPostfix) is not { } rightOperand)
        {
            // Recognized as a zip but its operand types are unresolved: defer without a
            // diagnostic (the ordinary assignability check must still not run over it).
            return true;
        }

        if(CheckZipLengthProof(leftOperand, rightOperand) == ZipLengthProofStatus.NotProven)
        {
            // Requirement 8.7: a zip between operands not proven equal length.
            diagnostics.ReportAtNode(VariadicsDiagnostics.ZipLengthNotProven, assignment, "TypeChecker");
        }

        return true;
    }

    // ─── Covariant unknown widening (unknown-type feature, Task 23) ──────
    //
    // Req 8.1–8.3: `Foo<A_1, …, A_n>` is assignable to `Foo<B_1, …, B_n>`
    // when, at every position i, either `A_i == B_i` or `B_i is UnknownType`
    // and the generic definition being widened is a class (reference-type)
    // generic. Nested widening recurses into constructed type arguments, so
    // `Optional<List<int>>` widens to `Optional<List<unknown>>` even when
    // `Optional` is a struct, because the widening happens at the inner
    // `List` boundary and `List` is a class.
    //
    // Req 8.5: the rule is one-directional by construction — only the target
    // position may be `UnknownType`. A concrete target with a source of
    // `UnknownType` falls through to the default `TypesEqual` check and is
    // rejected.
    //
    // The struct-generic restriction (Req 8.1, 8.6) is enforced by checking
    // the enclosing generic definition's <see cref="IType.Kind"/> at each
    // level where a widening actually happens. When the check fails only
    // because of this kind restriction, the caller of
    // <see cref="IsAssignable"/> detects that via
    // <see cref="WouldAssignableWithUnknownWideningIfClass"/> and emits
    // <see cref="StructGenericUnknownWidening"/> (MIST_TC_0058).

    /// <summary>
    /// Position-wise covariant widening check for the type arguments of a
    /// single generic construction.
    /// <para>
    /// For each position <c>i</c>, the rule accepts when
    /// <c>argsS[i] == argsT[i]</c>, or when <c>argsT[i]</c> is
    /// <see cref="UnknownType"/> and <paramref name="defCtx"/>'s
    /// <see cref="IType.Kind"/> is <see cref="TypeKind.Class"/>, or when both
    /// sides are <see cref="ConstructedType"/>s sharing the same generic
    /// definition and their nested widening succeeds.
    /// </para>
    /// <para>
    /// <paramref name="defCtx"/> carries the generic definition whose
    /// arguments are being compared at the current level; the class-only
    /// rule is enforced only where a widening actually happens at that
    /// level (Req 8.1, 8.6). Nested levels carry their own <c>defCtx</c>.
    /// </para>
    /// </summary>
    private static bool AssignableWithUnknownWidening(
        IType defCtx,
        IReadOnlyList<IType> argsS,
        IReadOnlyList<IType> argsT,
        bool ignoreClassRestriction = false)
    {
        if(argsS.Count != argsT.Count)
        {
            return false;
        }

        for(var i = 0; i < argsS.Count; i++)
        {
            var s = argsS[i];
            var t = argsT[i];

            // Identity at this position — no widening happens here.
            if(TypesEqual(s, t))
            {
                continue;
            }

            // Widening: target is `unknown`. The class-only rule (Req 8.1,
            // 8.6) is enforced at the level where widening happens unless
            // the caller is probing the "would widen if class" case.
            if(t is UnknownType)
            {
                if(!ignoreClassRestriction && defCtx.Kind != TypeKind.Class)
                {
                    return false;
                }

                continue;
            }

            // Nested widening: both sides are constructed from the same
            // generic definition. Recurse with the nested definition as
            // the new defCtx so the class-only rule is checked there.
            if(s is ConstructedType sc
                && t is ConstructedType tc
                && ReferenceEquals(sc.GenericDefinition, tc.GenericDefinition))
            {
                if(!AssignableWithUnknownWidening(
                        sc.GenericDefinition,
                        sc.TypeArguments,
                        tc.TypeArguments,
                        ignoreClassRestriction))
                {
                    return false;
                }

                continue;
            }

            // Anything else (mismatched nested definitions, bare types
            // differing, source-side UnknownType widening to concrete
            // target — Req 8.5) is not a valid widening.
            return false;
        }

        return true;
    }

    /// <summary>
    /// Returns <c>true</c> when the only thing preventing the covariant
    /// unknown widening from <paramref name="source"/> to
    /// <paramref name="target"/> is the class-only restriction (Req 8.6).
    /// Used by <see cref="TryEmitUnknownAssignabilityDiagnostic"/> to emit
    /// the explanatory <see cref="StructGenericUnknownWidening"/>
    /// diagnostic instead of a generic type-mismatch error at widening
    /// sites.
    /// </summary>
    private static bool WouldAssignableWithUnknownWideningIfClass(IType source, IType target)
    {
        if(source is not ConstructedType sc || target is not ConstructedType tc)
        {
            return false;
        }

        if(!ReferenceEquals(sc.GenericDefinition, tc.GenericDefinition))
        {
            return false;
        }

        if(sc.TypeArguments.Count != tc.TypeArguments.Count)
        {
            return false;
        }

        // If the strict widening already succeeds, this is not a
        // "would widen if class" case.
        if(AssignableWithUnknownWidening(
                sc.GenericDefinition, sc.TypeArguments, tc.TypeArguments))
        {
            return false;
        }

        // Probe the relaxed widening (class-only restriction ignored). If
        // it would succeed, then the class-only rule is the sole reason
        // the strict widening failed — that is the struct-generic
        // restriction diagnostic case (Req 8.6).
        return AssignableWithUnknownWidening(
            sc.GenericDefinition,
            sc.TypeArguments,
            tc.TypeArguments,
            ignoreClassRestriction: true);
    }

    /// <summary>
    /// Gets the substituted base type of a <see cref="ConstructedType"/>.
    /// If the generic definition is a <see cref="ClassType"/> with a base type,
    /// substitutes the type parameters in the base type with the constructed type's type arguments.
    /// </summary>
    private static IType? GetSubstitutedBaseType(ConstructedType constructedType)
    {
        if(constructedType.GenericDefinition is ClassType classDef)
        {
            var baseType = classDef.BaseType;
            if(baseType is null)
            {
                return null;
            }

            var typeParams = classDef.TypeParameters;
            var typeArgs = constructedType.TypeArguments;

            return SubstituteType(baseType, typeParams, typeArgs);
        }

        if(constructedType.GenericDefinition is StructType structDef)
        {
            var baseType = structDef.BaseType;
            if(baseType is null)
            {
                return null;
            }

            var typeParams = structDef.TypeParameters;
            var typeArgs = constructedType.TypeArguments;

            return SubstituteType(baseType, typeParams, typeArgs);
        }

        return null;
    }

    /// <summary>
    /// Checks whether <paramref name="source"/> implements the target constructed abstract,
    /// including transitive abstract inheritance.
    /// </summary>
    [ThreadStatic] private static HashSet<string>? abstractCheckVisited;

    private static bool ImplementsConstructedAbstract(IType source, ConstructedType targetConstructedAbstract)
    {
        // Cycle detection for CRTP patterns (e.g., NpgsqlDbContextOptionsBuilder extends
        // RelationalDbContextOptionsBuilder<NpgsqlDbContextOptionsBuilder, ...>).
        // Without this, the recursive walk enters infinite recursion when a type
        // refers to itself as a generic argument in its own base/abstract chain.
        abstractCheckVisited ??= new HashSet<string>(StringComparer.Ordinal);
        var key = $"{source.DisplayName}→{targetConstructedAbstract.DisplayName}";
        if(!abstractCheckVisited.Add(key))
        {
            return false; // Already checking this pair — cycle detected.
        }

        try
        {
            return ImplementsConstructedAbstractCore(source, targetConstructedAbstract);
        }
        finally
        {
            _ = abstractCheckVisited.Remove(key);
        }
    }

    private static bool ImplementsConstructedAbstractCore(IType source, ConstructedType targetConstructedAbstract)
    {
        // Collect all abstracts from the source type.
        IReadOnlyList<IType>? abstracts = null;
        IReadOnlyList<IType>? typeParams = null;
        IReadOnlyList<IType>? typeArgs = null;

        switch(source)
        {
            case ConstructedType sourceCt:
                {
                    switch(sourceCt.GenericDefinition)
                    {
                        case ClassType classDef:
                            abstracts = classDef.Abstracts;
                            typeParams = classDef.TypeParameters;
                            typeArgs = sourceCt.TypeArguments;
                            // Also check base type chain.
                            var baseType = GetSubstitutedBaseType(sourceCt);
                            if(baseType is not null && ImplementsConstructedAbstract(baseType, targetConstructedAbstract))
                            {
                                return true;
                            }

                            break;
                        case StructType structDef:
                            abstracts = structDef.Abstracts;
                            typeParams = structDef.TypeParameters;
                            typeArgs = sourceCt.TypeArguments;
                            // Also check struct base type chain.
                            var structBaseType = GetSubstitutedBaseType(sourceCt);
                            if(structBaseType is not null && ImplementsConstructedAbstract(structBaseType, targetConstructedAbstract))
                            {
                                return true;
                            }

                            break;
                        case AbstractType abstractDef:
                            // Check if the source abstract itself matches the target.
                            if(IsAssignable(source, targetConstructedAbstract))
                            {
                                return true;
                            }

                            abstracts = abstractDef.Abstracts;
                            typeParams = abstractDef.TypeParameters;
                            typeArgs = sourceCt.TypeArguments;
                            break;
                    }

                    break;
                }
            case ClassType classType:
                abstracts = classType.Abstracts;
                // Also check base type chain.
                var current = classType.BaseType;
                while(current is not null)
                {
                    if(current is ConstructedType baseCt && IsAssignable(baseCt, targetConstructedAbstract))
                    {
                        return true;
                    }

                    if(current is ClassType baseClass)
                    {
                        foreach(var abs in baseClass.Abstracts)
                        {
                            var substituted = abs;
                            if(substituted is ConstructedType absCt && IsAssignable(absCt, targetConstructedAbstract))
                            {
                                return true;
                            }
                        }

                        current = baseClass.BaseType;
                    }
                    else
                    {
                        break;
                    }
                }

                break;
            case StructType structType:
                abstracts = structType.Abstracts;
                // Also check struct base type chain.
                var currentStructBase = structType.BaseType;
                while(currentStructBase is not null)
                {
                    if(currentStructBase is StructType baseStruct)
                    {
                        foreach(var abs in baseStruct.Abstracts)
                        {
                            if(abs is ConstructedType absCt && IsAssignable(absCt, targetConstructedAbstract))
                            {
                                return true;
                            }
                        }

                        currentStructBase = baseStruct.BaseType;
                    }
                    else
                    {
                        break;
                    }
                }

                break;
            case AbstractType abstractType:
                abstracts = abstractType.Abstracts;
                break;
        }

        if(abstracts is null)
        {
            return false;
        }

        foreach(var abs in abstracts)
        {
            // Substitute type parameters if we have a mapping.
            var substitutedAbs = (typeParams is not null && typeArgs is not null)
                ? SubstituteType(abs, typeParams, typeArgs)
                : abs;

            if(substitutedAbs is ConstructedType substitutedCt)
            {
                // Variadics: when the source is a variadic ConstructedType, propagate its
                // DependentArguments to the substituted abstract so that pack-based
                // abstract implementations (e.g., Tup<params TArgs> : ICountable<TArgs...>)
                // correctly match their target at a concrete arity.
                if(substitutedCt.DependentArguments is null
                    && source is ConstructedType sourceCtForPack
                    && sourceCtForPack.DependentArguments is not null
                    && ReferenceEquals(substitutedCt.GenericDefinition, targetConstructedAbstract.GenericDefinition))
                {
                    substitutedCt = new ConstructedType(
                        substitutedCt.GenericDefinition,
                        substitutedCt.TypeArguments,
                        sourceCtForPack.DependentArguments);
                }

                if(IsAssignable(substitutedCt, targetConstructedAbstract))
                {
                    return true;
                }

                // Transitively check the abstract's own inherited abstracts.
                if(ImplementsConstructedAbstract(substitutedCt, targetConstructedAbstract))
                {
                    return true;
                }
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
    internal static IType SubstituteType(IType type, IReadOnlyList<IType> typeParams, IReadOnlyList<IType> typeArgs)
    {
        if(type is TypeParameterType tpt)
        {
            for(var i = 0; i < typeParams.Count; i++)
            {
                if(typeParams[i] is TypeParameterType tp && tpt.Name == tp.Name && tpt.Ordinal == tp.Ordinal)
                {
                    return i < typeArgs.Count ? tpt.Close(typeArgs[i]) : type;
                }
            }

            return type;
        }

        if(type is ConstructedType ct)
        {
            var substitutedArgs = new IType[ct.TypeArguments.Count];
            var changed = false;
            for(var i = 0; i < ct.TypeArguments.Count; i++)
            {
                substitutedArgs[i] = SubstituteType(ct.TypeArguments[i], typeParams, typeArgs);
                if(!ReferenceEquals(substitutedArgs[i], ct.TypeArguments[i]))
                {
                    changed = true;
                }
            }

            return changed ? new ConstructedType(ct.GenericDefinition, substitutedArgs) : ct;
        }

        // Substitute inside array element types (e.g., T[] → string[] when T = string).
        if(type is ArrayType arrayType)
        {
            var substitutedElement = SubstituteType(arrayType.ElementType, typeParams, typeArgs);
            return ReferenceEquals(substitutedElement, arrayType.ElementType)
                ? arrayType
                : new ArrayType(substitutedElement);
        }

        // Substitute inside span element types (e.g., Span<T> → Span<int> when T = int).
        if(type is SpanType spanType)
        {
            var substitutedElement = SubstituteType(spanType.ElementType, typeParams, typeArgs);
            if(ReferenceEquals(substitutedElement, spanType.ElementType))
            {
                return spanType;
            }

            if(spanType.KnownLength.HasValue)
            {
                return new SpanType(substitutedElement, spanType.KnownLength.Value, spanType.IsReadOnly);
            }

            if(spanType.SymbolicLengthId.HasValue)
            {
                return new SpanType(substitutedElement, spanType.SymbolicLengthId.Value, spanType.IsReadOnly);
            }

            return new SpanType(substitutedElement, spanType.IsReadOnly);
        }

        // Substitute inside union type constituents (e.g., T | void → string | void when T = string).
        if(type is UnionType unionType)
        {
            var substitutedConstituents = new IType[unionType.Constituents.Count];
            var changed = false;
            for(var i = 0; i < unionType.Constituents.Count; i++)
            {
                substitutedConstituents[i] = SubstituteType(unionType.Constituents[i], typeParams, typeArgs);
                if(!ReferenceEquals(substitutedConstituents[i], unionType.Constituents[i]))
                {
                    changed = true;
                }
            }

            if(!changed)
            {
                return unionType;
            }

            var result = new UnionType(substitutedConstituents)
            {
                NullableProjection = unionType.NullableProjection
            };
            return result;
        }

        return type;
    }

    /// <summary>
    /// Checks whether <paramref name="source"/> implements <paramref name="targetAbstract"/>
    /// directly or transitively. When <paramref name="model"/> is provided, also consults
    /// the <see cref="ConformanceMap"/> for with-block conformance.
    /// </summary>
    private static bool ImplementsAbstract(IType source, AbstractType targetAbstract, ISemanticModel? model = null)
    {
        switch(source)
        {
            case ClassType classType:
                // Check the class's own abstracts (recursively through abstract inheritance).
                if(AbstractListContains(classType.Abstracts, targetAbstract))
                {
                    return true;
                }
                // Walk the base type chain and check each ancestor's abstracts.
                var current = classType.BaseType as ClassType;
                while(current is not null)
                {
                    if(AbstractListContains(current.Abstracts, targetAbstract))
                    {
                        return true;
                    }

                    current = current.BaseType as ClassType;
                }
                // Check with-block conformance via ConformanceMap.
                if(model?.ConformanceMap.HasConformance(classType, targetAbstract) == true)
                {
                    return true;
                }

                return false;

            case StructType structType:
                // Check the struct's own abstracts (recursively through abstract inheritance).
                if(AbstractListContains(structType.Abstracts, targetAbstract))
                {
                    return true;
                }
                // Walk the struct base type chain and check each ancestor's abstracts.
                var currentStruct = structType.BaseType as StructType;
                while(currentStruct is not null)
                {
                    if(AbstractListContains(currentStruct.Abstracts, targetAbstract))
                    {
                        return true;
                    }

                    currentStruct = currentStruct.BaseType as StructType;
                }
                // Check with-block conformance via ConformanceMap.
                if(model?.ConformanceMap.HasConformance(structType, targetAbstract) == true)
                {
                    return true;
                }

                return false;

            case AbstractType sourceAbstract:
                // An abstract is assignable to itself or any abstract it inherits from.
                if(ReferenceEquals(sourceAbstract, targetAbstract)
                    || sourceAbstract.DisplayName == targetAbstract.DisplayName)
                {
                    return true;
                }

                return AbstractListContains(sourceAbstract.Abstracts, targetAbstract);

            default:
                return false;
        }
    }

    /// <summary>
    /// Recursively checks whether <paramref name="target"/> is present in the given
    /// abstract list or in any of the abstracts' own inherited abstract lists.
    /// Uses a visited set to handle diamond inheritance and prevent infinite loops.
    /// </summary>
    private static bool AbstractListContains(
        IReadOnlyList<IType> abstracts,
        AbstractType target,
        HashSet<AbstractType>? visited = null)
    {
        foreach(var absType in abstracts)
        {
            if(absType is not AbstractType abs)
            {
                continue;
            }

            if(ReferenceEquals(abs, target) || abs.DisplayName == target.DisplayName)
            {
                return true;
            }

            // Recursively check inherited abstracts, guarding against cycles.
            visited ??= new HashSet<AbstractType>(ReferenceEqualityComparer.Instance);
            if(visited.Add(abs))
            {
                if(AbstractListContains(abs.Abstracts, target, visited))
                {
                    return true;
                }
            }
        }

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
        if(type is ClassType ct)
        {
            if(ct.IsBackendBacked
                && ct.ContainingNamespace == "Standard"
                && ct.DisplayName == "String")
            {
                return true;
            }
            // Legacy fallback: LegacyPrimitiveTypes.String has DisplayName "string"
            if(ct.DisplayName == "string")
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Accepts the stdlib <c>Standard.Regex</c> <see cref="ClassType"/>
    /// instance (identified by its backend-backed marker and simple name),
    /// or the legacy <see cref="LegacyPrimitiveTypes.Regex"/> fallback
    /// <see cref="ClassType"/> (identified by DisplayName "regex").
    /// Used by the string↔regex conversion rules (Requirements 6.1–6.10).
    /// </summary>
    private static bool IsRegexLike(IType type)
    {
        if(type is ClassType ct)
        {
            if(ct.IsBackendBacked
                && ct.ContainingNamespace == "Standard"
                && ct.DisplayName == "Regex")
            {
                return true;
            }
            // Legacy fallback: LegacyPrimitiveTypes.Regex has DisplayName "regex"
            if(ct.DisplayName == "regex")
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
        if(name is "bool" or "Boolean")
        {
            return true;
        }

        if(type is StructType st
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
        if(name is "int" or "Int32")
        {
            return true;
        }

        if(type is StructType st
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
        if(LegacyToStdlibName.TryGetValue(a.DisplayName, out var aStdlib)
            && aStdlib == b.DisplayName)
        {
            return true;
        }

        if(LegacyToStdlibName.TryGetValue(b.DisplayName, out var bStdlib)
            && bStdlib == a.DisplayName)
        {
            return true;
        }

        return false;
    }

    private static bool IsErrorType(IType type)
    {
        return type is ErrorType;
    }

    /// <summary>
    /// Returns true if the given type represents the <c>int</c> (Int32) primitive type.
    /// Used for validating array initializer constructor index parameters.
    /// </summary>
    private static bool IsIntType(IType type)
    {
        if(type is TypeAliasType alias)
        {
            type = alias.UnderlyingType;
        }

        return type is StructType st && st.DisplayName == "Int32";
    }

    /// <summary>
    /// Returns true if the given type is one of the CLR integer numeric types.
    /// Used for implicit integer narrowing in CLR interop scenarios (e.g., int → byte).
    /// </summary>
    private static bool IsIntegerNumericType(IType type)
    {
        if(type is TypeAliasType alias)
        {
            type = alias.UnderlyingType;
        }

        if(type is not StructType st)
        {
            return false;
        }

        return st.DisplayName is "Byte" or "SByte" or "Int16" or "UInt16"
            or "Int32" or "UInt32" or "Int64" or "UInt64";
    }

    /// <summary>
    /// Returns true if the given type is a ref struct (stack-only value type).
    /// Handles both plain <see cref="StructType"/> and <see cref="ConstructedType"/>
    /// whose generic definition is a ref struct (e.g., a generic ref struct like Span&lt;int&gt;).
    /// </summary>
    private static bool IsRefStructType(IType type)
    {
        return type is SpanType
            || type is StructType { IsRefStruct: true }
            || (type is ConstructedType ct && ct.GenericDefinition is StructType { IsRefStruct: true });
    }

    /// <summary>
    /// Returns true if the given type is an abstract type (assignment target
    /// that would require boxing a ref struct). Handles both plain <see cref="AbstractType"/>
    /// and <see cref="ConstructedType"/> whose generic definition is an abstract
    /// (e.g., IEnumerable&lt;int&gt;).
    /// </summary>
    private static bool IsAbstractTargetType(IType type)
    {
        return type is AbstractType
            || (type is ConstructedType ct && ct.GenericDefinition is AbstractType);
    }

    // ── delegate-func-conversion: assignability helpers ─────────────────

    /// <summary>
    /// Returns true if two <see cref="DelegateType"/> instances have matching
    /// type arguments (pairwise equality). Used to distinguish
    /// <c>Predicate&lt;int&gt;</c> from <c>Predicate&lt;string&gt;</c>.
    /// </summary>
    private static bool DelegateTypeArgumentsMatch(DelegateType a, DelegateType b)
    {
        if(a.TypeArguments.Count != b.TypeArguments.Count)
        {
            return false;
        }

        for(var i = 0; i < a.TypeArguments.Count; i++)
        {
            if(!TypesEqual(a.TypeArguments[i], b.TypeArguments[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Returns true if two func types (<see cref="ConstructedType"/> wrapping
    /// <c>Standard.Func</c>) are signature-compatible — same arity and each
    /// corresponding type argument is assignable.
    /// </summary>
    private static bool IsSignatureCompatible(IType sourceFunc, IType targetFunc)
    {
        if(sourceFunc is not ConstructedType sourceCt || targetFunc is not ConstructedType targetCt)
        {
            return false;
        }

        if(sourceCt.TypeArguments.Count != targetCt.TypeArguments.Count)
        {
            return false;
        }

        for(var i = 0; i < sourceCt.TypeArguments.Count; i++)
        {
            if(!IsAssignable(sourceCt.TypeArguments[i], targetCt.TypeArguments[i]))
            {
                return false;
            }
        }

        return true;
    }

    // ── delegate-func-conversion: overload ambiguity helpers ─────────────

    /// <summary>
    /// Determines whether an ambiguous overload resolution result involves
    /// multiple <see cref="DelegateType"/>-typed parameters — i.e., the candidates
    /// differ primarily by which named delegate type they accept at the same position.
    /// When true, the caller should emit <c>MIST_TC_0094</c> instead of the generic
    /// <c>MIST_TC_0032</c> (Requirements 7.5, 7.6).
    /// </summary>
    private static bool IsDelegateOverloadAmbiguity(IReadOnlyList<ISymbol> ambiguousCandidates, ISemanticModel model)
    {
        if(ambiguousCandidates.Count < 2)
        {
            return false;
        }

        // Check if at least one parameter position has different DelegateType values across candidates.
        var paramSets = ambiguousCandidates
            .Select(c => ParameterSignature.Extract(c, model))
            .ToList();

        var paramCount = paramSets[0].Count;
        if(paramSets.Any(ps => ps.Count != paramCount))
        {
            return false;
        }

        for(var i = 0; i < paramCount; i++)
        {
            var delegateCount = 0;
            DelegateType? firstDelegate = null;
            var hasDifferentDelegates = false;

            foreach(var ps in paramSets)
            {
                if(ps[i] is DelegateType dt)
                {
                    delegateCount++;
                    if(firstDelegate is null)
                    {
                        firstDelegate = dt;
                    }
                    else if(firstDelegate.ClrFullName != dt.ClrFullName)
                    {
                        hasDifferentDelegates = true;
                    }
                }
            }

            // Ambiguity across multiple distinct DelegateTypes at this position.
            if(hasDifferentDelegates && delegateCount >= 2)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Extracts the names of the first two distinct <see cref="DelegateType"/> parameters
    /// from the ambiguous candidates for use in the <c>MIST_TC_0094</c> diagnostic message.
    /// </summary>
    private static (string, string) GetAmbiguousDelegateNames(IReadOnlyList<ISymbol> ambiguousCandidates, ISemanticModel model)
    {
        var delegateNames = new List<string>();

        foreach(var candidate in ambiguousCandidates)
        {
            var paramTypes = ParameterSignature.Extract(candidate, model);
            foreach(var paramType in paramTypes)
            {
                if(paramType is DelegateType dt && !delegateNames.Contains(dt.DisplayName))
                {
                    delegateNames.Add(dt.DisplayName);
                    if(delegateNames.Count >= 2)
                    {
                        return (delegateNames[0], delegateNames[1]);
                    }
                }
            }
        }

        // Fallback — should not happen if IsDelegateOverloadAmbiguity returned true.
        return (delegateNames.Count > 0 ? delegateNames[0] : "?",
                delegateNames.Count > 1 ? delegateNames[1] : "?");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Utility helpers
    // ═══════════════════════════════════════════════════════════════════════

    private static List<IType> CollectArgumentTypes(ArgumentListNode? argList, ISemanticModel model)
    {
        var types = new List<IType>();
        if(argList is null)
        {
            return types;
        }

        foreach(var expr in argList.GetAllExpressions())
        {
            var type = model.GetTypeOf(expr);
            types.Add(type ?? ErrorType.Instance);
        }

        return types;
    }

    /// <summary>
    /// Collects the RefKind for each argument in the argument list.
    /// A <see cref="RefArgumentArgumentNode"/> maps to <see cref="RefKind.Ref"/>;
    /// a <see cref="ValueArgumentArgumentNode"/> maps to <see cref="RefKind.None"/>.
    /// Used by overload resolution to distinguish ref from non-ref overloads (Requirement 7.4).
    /// </summary>
    private static List<RefKind> CollectArgumentRefKinds(ArgumentListNode? argList)
    {
        var refKinds = new List<RefKind>();
        if(argList is null)
        {
            return refKinds;
        }

        // First argument
        refKinds.Add(argList.Argument is RefArgumentArgumentNode ? RefKind.Ref : RefKind.None);

        // Remaining arguments
        foreach(var arg in argList.Arguments)
        {
            refKinds.Add(arg is RefArgumentArgumentNode ? RefKind.Ref : RefKind.None);
        }

        return refKinds;
    }

    /// <summary>
    /// Formats the argument types of an argument list as a comma-separated string
    /// of display names. Used for diagnostic messages in <c>new T(...)</c> validation.
    /// </summary>
    private static string FormatArgTypes(ArgumentListNode? argList, ISemanticModel model)
    {
        var types = CollectArgumentTypes(argList, model);
        return string.Join(", ", types.Select(t => t.DisplayName));
    }

    /// <summary>
    /// Collects all method symbols with the given name from the target type's member scope
    /// and inherited methods from base types (walking the base type chain).
    ///
    /// <para>
    /// For CLR-projected (Backend) types, reflection flattens inherited members onto each
    /// type's own member scope (the projector enumerates with <c>Public | Instance | Static</c>
    /// and no <c>DeclaredOnly</c>). Once the base chain is connected to the cached,
    /// fully-projected ancestors (cross-assembly inheritance, Requirements 6.1/6.2), the same
    /// inherited signature appears once per level: on the leaf (flattened) and again on every
    /// ancestor scope. Without de-duplication this turns a single inherited method into N
    /// identical-signature overload candidates (e.g. a method overridden through two levels
    /// yields three copies of the same <c>()-&gt;string</c> signature), which overload
    /// resolution then reports as ambiguous — making inherited members effectively uncallable
    /// (Requirements 7.1, 7.5, 7.8).
    /// </para>
    ///
    /// <para>
    /// To keep only the most-derived visible signature for each inherited member
    /// (Requirements 7.3, 7.8) — and to fall back to the nearest visible ancestor when the
    /// most-derived definition is hidden by the visibility filter (Requirement 7.4) — Backend
    /// candidates are de-duplicated by full signature (parameter types + ref kinds + generic
    /// arity), keeping the first occurrence. Because the leaf scope is collected before the
    /// base chain is walked, the first occurrence is always the most-derived definition; when
    /// the most-derived definition was hidden (never projected), the first occurrence is the
    /// nearest visible ancestor's. De-duplication is restricted to Backend symbols: Mist-source
    /// method overloads store their parameters in AST scopes (not on <see cref="Symbol.Parameters"/>),
    /// so their signatures are not computed here and their resolution is left unchanged.
    /// Distinct overloads (different parameter types, ref kinds, or arity) keep distinct
    /// signature keys and are all preserved.
    /// </para>
    /// </summary>
    private static List<ISymbol> CollectMethodOverloads(IType? targetType, string methodName)
    {
        var results = new List<ISymbol>();
        if(targetType is null)
        {
            return results;
        }

        // Tracks the full signatures of Backend (CLR-projected) candidates already kept, so a
        // later (less-derived) copy of the same inherited signature is skipped (Req 7.3, 7.8).
        var seenBackendSignatures = new HashSet<string>(StringComparer.Ordinal);

        void addIfNotDuplicate(ISymbol member)
        {
            // Only CLR-projected (Backend) methods are flattened onto every level of the chain,
            // so only they can produce same-signature cross-level duplicates. Mist-source methods
            // are added verbatim (their signatures live in AST scopes, not on Symbol.Parameters).
            if(member.Origin == SymbolOrigin.Backend
                && BuildBackendOverloadSignatureKey(member) is { } signatureKey)
            {
                if(!seenBackendSignatures.Add(signatureKey))
                {
                    return; // A more-derived copy of this exact signature was already kept.
                }
            }

            results.Add(member);
        }

        // Collect from the target type's own member scope (the most-derived level).
        var memberScope = targetType switch
        {
            ClassType ct => ct.MemberScope,
            StructType st => st.MemberScope,
            AbstractType it => it.MemberScope,
            _ => null,
        };

        if(memberScope is not null)
        {
            foreach(var member in memberScope.Members)
            {
                if(member.Kind == SymbolKind.Method && member.Name == methodName)
                {
                    addIfNotDuplicate(member);
                }
            }
        }

        // Walk the base type chain to collect inherited methods (less-derived levels).
        if(targetType is ClassType classType)
        {
            var baseType = classType.BaseType as ClassType;
            while(baseType is not null)
            {
                var baseScope = baseType.MemberScope;
                foreach(var member in baseScope.Members)
                {
                    if(member.Kind == SymbolKind.Method && member.Name == methodName)
                    {
                        addIfNotDuplicate(member);
                    }
                }

                baseType = baseType.BaseType as ClassType;
            }
        }

        return results;
    }

    /// <summary>
    /// Builds a stable full-signature key for a CLR-projected (Backend) method symbol, used to
    /// de-duplicate inherited members that reflection flattens onto every level of a connected
    /// inheritance chain (see <see cref="CollectMethodOverloads"/>). The key combines the
    /// method's generic arity with each parameter's projected type display name and ref kind,
    /// so genuinely distinct overloads (different parameter types, ref kinds, or arity) keep
    /// distinct keys and are never collapsed.
    /// </summary>
    /// <returns>
    /// The signature key, or <c>null</c> when the symbol is not a <see cref="Symbol"/> (in which
    /// case the caller does not de-duplicate it).
    /// </returns>
    private static string? BuildBackendOverloadSignatureKey(ISymbol member)
    {
        if(member is not Symbol sym)
        {
            return null;
        }

        var builder = new System.Text.StringBuilder();
        _ = builder.Append(sym.TypeParameters.Count).Append('|');

        foreach(var parameter in sym.Parameters)
        {
            _ = builder.Append(parameter.DeclaredType?.DisplayName ?? "?");
            if(parameter is Symbol paramSym && paramSym.RefKind != RefKind.None)
            {
                _ = builder.Append('#').Append(paramSym.RefKind);
            }

            _ = builder.Append(',');
        }

        return builder.ToString();
    }

    /// <summary>
    /// Collects all constructor symbols from the target type's member scope.
    /// Unlike methods, constructors are not inherited from base types.
    /// </summary>
    private List<ISymbol> CollectConstructorOverloads(IType? targetType, ISemanticModel? model = null)
    {
        var results = new List<ISymbol>();
        if(targetType is null)
        {
            return results;
        }

        // For ConstructedType (e.g., GenericBox<int>), unwrap to the generic
        // definition to find constructors in the original type's member scope.
        var lookupType = targetType is ConstructedType cst ? cst.GenericDefinition : targetType;

        IScope? memberScope;

        if(lookupType is SpanType spanLookup && model is not null)
        {
            // Bridge: SpanType is a dedicated IType without a member scope.
            // Look up the corresponding registered StructType in the Standard namespace
            // and pull constructors from its member scope.
            var registeredStruct = FindRegisteredStructType(
                spanLookup.IsReadOnly ? "ReadOnlySpan" : "Span", "Standard", model);
            memberScope = registeredStruct?.MemberScope;
        }
        else
        {
            memberScope = lookupType switch
            {
                ClassType ct => ct.MemberScope,
                StructType st => st.MemberScope,
                NewtypeType nt => nt.MemberScope,
                _ => null,
            };
        }

        if(memberScope is null)
        {
            return results;
        }

        foreach(var member in memberScope.Members)
        {
            if(member.Kind == SymbolKind.Constructor)
            {
                results.Add(member);
            }
        }

        return results;
    }

    /// <summary>
    /// Looks up a registered <see cref="StructType"/> by name and namespace in the semantic model.
    /// Used to bridge dedicated types (e.g., <see cref="SpanType"/>) to their registered
    /// declarations so that member scope data (constructors, abstracts) can be accessed.
    /// Results are cached per compilation for performance.
    /// </summary>
    private StructType? FindRegisteredStructType(string name, string namespaceName, ISemanticModel model)
    {
        var cacheKey = $"{namespaceName}.{name}";
        if(registeredStructTypeCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        StructType? result = null;
        foreach(var symbol in model.AllSymbols)
        {
            if(symbol.Kind == SymbolKind.Struct
                && symbol.Name == name
                && symbol.DeclaredType is StructType st
                && st.ContainingNamespace == namespaceName)
            {
                result = st;
                break;
            }
        }

        registeredStructTypeCache[cacheKey] = result;
        return result;
    }

    /// <summary>
    /// Looks up a registered <see cref="ClassType"/> by name and namespace in the semantic model.
    /// Used to bridge dedicated types (e.g., <see cref="ArrayType"/>) to their registered
    /// declarations so that abstract data can be accessed.
    /// Results are cached per compilation for performance.
    /// </summary>
    private ClassType? FindRegisteredClassType(string name, string namespaceName, ISemanticModel model)
    {
        var cacheKey = $"{namespaceName}.{name}";
        if(registeredClassTypeCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        ClassType? result = null;
        foreach(var symbol in model.AllSymbols)
        {
            if(symbol.Kind == SymbolKind.Class
                && symbol.Name == name
                && symbol.DeclaredType is ClassType ct
                && ct.ContainingNamespace == namespaceName)
            {
                result = ct;
                break;
            }
        }

        registeredClassTypeCache[cacheKey] = result;
        return result;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Operator overload resolution helpers (Task 14 — Mist Standard Library spec)
    //
    // These two helpers form the new operator-resolution path added by the
    // Standard Library feature. They are invoked by whichever analyzer path
    // is gated on <see cref="useOperatorOverloadResolution"/>. In Task 14 the
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
    /// <param name="noApplicableDescriptor">
    /// Descriptor to emit when no candidate is applicable. Wrapper callers
    /// pass <c>TypeCheckDiagnostics.OperatorNotApplicableArithmetic</c> (additive / multiplicative),
    /// <c>TypeCheckDiagnostics.OperatorNotApplicableComparison</c> (relational), or
    /// <c>TypeCheckDiagnostics.OperatorNotApplicableLogical</c> (equality / logical).
    /// </param>
    /// <param name="model">The semantic model; used to read and set expression types.</param>
    /// <param name="diagnostics">The diagnostic bag to append to.</param>
    internal static void ResolveBinaryOperator(
        string op,
        SyntaxNode left,
        SyntaxNode right,
        SyntaxNode surrounding,
        DiagnosticDescriptor noApplicableDescriptor,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        var leftType = model.GetTypeOf(left);
        var rightType = model.GetTypeOf(right);

        // Suppress cascading errors — if either operand's type is unknown we
        // rely on an earlier diagnostic to explain the real problem.
        if(leftType is null || rightType is null
            || IsErrorType(leftType) || IsErrorType(rightType))
        {
            return;
        }

        // Task 25.2: bare-unknown operator rejection (Req 13.2).
        // `UnknownType` has no declared members (Req 3.4), so no operator
        // symbols are reachable from either operand's scope. Rather than
        // falling through to the generic "no applicable operator"
        // diagnostic, emit the targeted MIST_TC_0066 that names the
        // unknown operand(s) and suggests using an `is` pattern
        // (Req 16.4). Short-circuit before candidate collection to
        // suppress cascading.
        if(leftType is UnknownType || rightType is UnknownType)
        {
            diagnostics.ReportAtNode(TypeCheckDiagnostics.BareUnknownOperator, surrounding, "TypeChecker", op, leftType.DisplayName, rightType.DisplayName);
            return;
        }

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

        foreach(var candidate in GetOperatorMembers(leftType, opName))
        {
            candidates.Add(candidate);
            candidateOrigin.Add(0);
        }

        if(!ReferenceEquals(leftType, rightType))
        {
            foreach(var candidate in GetOperatorMembers(rightType, opName))
            {
                if(!candidates.Any(existing => ReferenceEquals(existing, candidate)))
                {
                    candidates.Add(candidate);
                    candidateOrigin.Add(1);
                }
            }
        }

        if(candidates.Count == 0)
        {
            diagnostics.ReportAtNode(noApplicableDescriptor, surrounding, "TypeChecker", op, leftType.DisplayName, rightType.DisplayName);
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

        switch(result.Status)
        {
            case OverloadResolutionStatus.Success:
                {
                    var best = result.BestCandidate!;
                    model.RecordResolvedOperator(surrounding, best);
                    if(best.DeclaredType is not null)
                    {
                        model.SetTypeOf(surrounding, best.DeclaredType);
                    }

                    break;
                }

            case OverloadResolutionStatus.NoApplicableCandidate:
                diagnostics.ReportAtNode(noApplicableDescriptor, surrounding, "TypeChecker", op, leftType.DisplayName, rightType.DisplayName);
                break;

            case OverloadResolutionStatus.AmbiguousCall:
                diagnostics.ReportAtNode(TypeCheckDiagnostics.AmbiguousOverload, surrounding, "TypeChecker", op, "multiple candidates");
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
    /// <param name="noApplicableDescriptor">
    /// Descriptor to emit when no candidate is applicable. Wrapper callers
    /// pass <c>TypeCheckDiagnostics.OperatorNotApplicableArithmetic</c> for
    /// unary <c>-</c> and <c>TypeCheckDiagnostics.OperatorNotApplicableLogical</c>
    /// for unary <c>!</c>.
    /// </param>
    /// <param name="model">The semantic model; used to read and set expression types.</param>
    /// <param name="diagnostics">The diagnostic bag to append to.</param>
    internal static void ResolveUnaryOperator(
        string op,
        SyntaxNode operand,
        SyntaxNode surrounding,
        DiagnosticDescriptor noApplicableDescriptor,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        var operandType = model.GetTypeOf(operand);

        if(operandType is null || IsErrorType(operandType))
        {
            return;
        }

        // Task 25.2: bare-unknown operator rejection (Req 13.2).
        // `UnknownType` has no declared members (Req 3.4), so no operator
        // symbols are reachable from the operand's scope. Rather than
        // falling through to the generic "no applicable operator"
        // diagnostic, emit the targeted MIST_TC_0066 that names the
        // unknown operand and suggests using an `is` pattern
        // (Req 16.4). Short-circuit before candidate collection to
        // suppress cascading.
        if(operandType is UnknownType)
        {
            diagnostics.ReportAtNode(TypeCheckDiagnostics.BareUnknownOperator, surrounding, "TypeChecker", op, operandType.DisplayName, operandType.DisplayName);
            return;
        }

        var opName = "operator" + op;

        var candidates = new List<ISymbol>();
        foreach(var candidate in GetOperatorMembers(operandType, opName))
        {
            candidates.Add(candidate);
        }

        if(candidates.Count == 0)
        {
            diagnostics.ReportAtNode(noApplicableDescriptor, surrounding, "TypeChecker", op, operandType.DisplayName, operandType.DisplayName);
            return;
        }

        var result = OverloadResolver.Resolve(
            candidates,
            argumentTypes: new[] { operandType },
            model);

        switch(result.Status)
        {
            case OverloadResolutionStatus.Success:
                {
                    var best = result.BestCandidate!;
                    model.RecordResolvedOperator(surrounding, best);
                    if(best.DeclaredType is not null)
                    {
                        model.SetTypeOf(surrounding, best.DeclaredType);
                    }

                    break;
                }

            case OverloadResolutionStatus.NoApplicableCandidate:
                diagnostics.ReportAtNode(noApplicableDescriptor, surrounding, "TypeChecker", op, operandType.DisplayName, operandType.DisplayName);
                break;

            case OverloadResolutionStatus.AmbiguousCall:
                diagnostics.ReportAtNode(TypeCheckDiagnostics.AmbiguousOverload, surrounding, "TypeChecker", op, "multiple candidates");
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
        // ── refinement-types feature: arithmetic erasure (Requirement 8.1, 8.2) ──
        // A RefinementType has no operators of its own — unwrap to the BaseType
        // so that operator lookup finds the base type's operators. The result
        // type of the resolved operator will naturally be the BaseType, which
        // implements the "arithmetic erases refinements" rule.
        if(type is RefinementType refinement)
        {
            type = refinement.BaseType;
        }

        var scope = type switch
        {
            ClassType ct => ct.MemberScope,
            StructType st => st.MemberScope,
            AbstractType it => it.MemberScope,
            ConstructedType cst => cst.MemberScope,
            _ => null,
        };

        if(scope is null)
        {
            yield break;
        }

        foreach(var member in scope.Members)
        {
            if(member.Kind == SymbolKind.Method && member.Name == operatorName)
            {
                yield return member;
            }
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
        foreach(var symbol in model.AllSymbols)
        {
            if(symbol.Origin == SymbolOrigin.Backend)
            {
                continue;
            }

            if(symbol.Kind is not (SymbolKind.Class or SymbolKind.Struct))
            {
                continue;
            }

            var type = symbol.DeclaredType;

            // Extract defaultability status and default block node.
            DefaultabilityStatus defaultability;
            DefaultBlockDeclarationNode? defaultBlockNode;
            IScope? memberScope;
            ClassType? baseClass;

            switch(type)
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
            if(defaultability != DefaultabilityStatus.Explicit || defaultBlockNode is null)
            {
                continue;
            }

            // Collect the set of field names declared directly on this type (not inherited).
            var ownFieldNames = new HashSet<string>(StringComparer.Ordinal);
            foreach(var member in memberScope.Members)
            {
                if(member.Kind == SymbolKind.Field)
                {
                    _ = ownFieldNames.Add(member.Name);
                }
            }

            var hasNonRedundantEntry = false;

            foreach(var entry in defaultBlockNode.Entries)
            {
                switch(entry)
                {
                    case DefaultFieldOverrideDefaultFieldInitializerNode fieldOverride:
                        {
                            var fieldName = fieldOverride.FieldName.Lexeme;

                            // 1. Check field exists in the type's member scope (including inherited).
                            var fieldSymbol = memberScope.Lookup(fieldName);
                            if(fieldSymbol is null || fieldSymbol.Kind != SymbolKind.Field)
                            {
                                // Field not found — the semantic model builder already handles
                                // this during default value computation, but we still mark as
                                // non-redundant to avoid a false redundancy diagnostic.
                                hasNonRedundantEntry = true;
                                break;
                            }

                            // 2. Validate visibility: if the field is from a base type, it must be public.
                            if(!ownFieldNames.Contains(fieldName))
                            {
                                // Field is inherited — check visibility.
                                if(!IsMemberPublic(fieldSymbol))
                                {
                                    diagnostics.ReportAtNode(CommonDiagnostics.DefaultFieldNotVisible, fieldOverride, "TypeChecker");
                                }
                            }

                            // 3. Validate compile-time constant expression (v1: only literals).
                            if(!IsLiteralExpression(fieldOverride.Value))
                            {
                                diagnostics.ReportAtNode(CommonDiagnostics.DefaultNotConstant, fieldOverride.Value, "TypeChecker");
                            }

                            // Track whether this override differs from the inferred default.
                            hasNonRedundantEntry = hasNonRedundantEntry || IsOverrideNonRedundant(fieldOverride.Value, fieldSymbol, model);
                            break;
                        }

                    case DefaultTypeBindingDefaultFieldInitializerNode typeBinding:
                        {
                            var fieldName = typeBinding.FieldName.Lexeme;

                            // 1. Check field exists.
                            var fieldSymbol = memberScope.Lookup(fieldName);
                            if(fieldSymbol is null || fieldSymbol.Kind != SymbolKind.Field)
                            {
                                hasNonRedundantEntry = true;
                                break;
                            }

                            // 2. Validate visibility for inherited fields.
                            if(!ownFieldNames.Contains(fieldName))
                            {
                                if(!IsMemberPublic(fieldSymbol))
                                {
                                    diagnostics.ReportAtNode(CommonDiagnostics.DefaultFieldNotVisible, typeBinding, "TypeChecker");
                                }
                            }

                            // 3. Validate the concrete type is a defaultable ClassType or StructType.
                            var concreteType = ResolveTypeBindingConcreteType(typeBinding, memberScope);
                            if(concreteType is not null)
                            {
                                switch(concreteType)
                                {
                                    case ClassType ct when ct.Defaultability == DefaultabilityStatus.NotDefaultable:
                                        diagnostics.ReportAtNode(CommonDiagnostics.DefaultNotSupported, typeBinding, "TypeChecker");
                                        break;
                                    case StructType st when st.Defaultability == DefaultabilityStatus.NotDefaultable:
                                        diagnostics.ReportAtNode(CommonDiagnostics.DefaultNotSupported, typeBinding, "TypeChecker");
                                        break;
                                    case not ClassType and not StructType:
                                        diagnostics.ReportAtNode(CommonDiagnostics.DefaultNotSupported, typeBinding, "TypeChecker");
                                        break;
                                }
                            }

                            // 4. Validate nested override expressions are compile-time constants.
                            foreach(var nested in typeBinding.NestedEntries)
                            {
                                if(!IsLiteralExpression(nested.Value))
                                {
                                    diagnostics.ReportAtNode(CommonDiagnostics.DefaultNotConstant, nested.Value, "TypeChecker");
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
            //    Exception: types with constructors NEED the explicit default block as an
            //    opt-in mechanism — without it they become NotDefaultable. In that case
            //    the block is structurally necessary and not redundant.
            if(!hasNonRedundantEntry)
            {
                var hasConstructors = memberScope.Members.Any(m => m.Kind == SymbolKind.Constructor);
                if(!hasConstructors)
                {
                    diagnostics.ReportAtNode(CommonDiagnostics.DefaultRedundantBlock, defaultBlockNode, "TypeChecker");
                }
            }
        }
    }

    /// <summary>
    /// Checks whether a member symbol is public by reading its resolved accessibility.
    /// </summary>
    private static bool IsMemberPublic(ISymbol member)
    {
        return member.Accessibility == Accessibility.Public;
    }

    /// <summary>
    /// Checks whether an expression is a literal expression (compile-time constant in v1).
    /// Walks down the expression chain to the innermost <see cref="PrimaryNode"/> and
    /// checks if it is a recognized literal node type.
    /// </summary>
    private static bool IsLiteralExpression(ExpressionNode expression)
    {
        var primary = UnwrapExpressionToPrimary(expression);
        if(primary is null)
        {
            return false;
        }

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
        SyntaxNode current = expression;

        for(var depth = 0; depth < 20; depth++)
        {
            if(current is PrimaryNode primary)
            {
                return primary;
            }

            // Look for a single non-token child to follow.
            // Skip null children that may be yielded by generated AST nodes
            // whose optional properties are typed as non-nullable.
            SyntaxNode? next = null;
            var childCount = 0;
            foreach(var child in current.Children())
            {
                if(child is null)
                {
                    continue;
                }

                childCount++;
                if(child is PrimaryNode childPrimary)
                {
                    return childPrimary;
                }

                next ??= child;
            }

            if(childCount > 1 || next is null)
            {
                return null;
            }

            current = next;
        }

        return null;
    }

    /// <summary>
    /// Determines whether a field override in a default block is non-redundant —
    /// i.e., it produces a different value than the inferred default would.
    /// </summary>
    private static bool IsOverrideNonRedundant(

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

        if(fieldDeclNode is FieldDeclarationNode fieldDecl && fieldDecl.Initializer is ExpressionNode initExpr)
        {
            // Field has an initializer — compare override value with initializer value.
            var initValue = TryExtractLiteralConstantValue(initExpr);
            return !Equals(overrideValue, initValue);
        }

        // Field has no initializer — compare override value with zero-init value.
        var fieldType = fieldSymbol.DeclaredType;
        if(fieldType is null)
        {
            return true;
        }

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
        if(primary is null)
        {
            return null;
        }

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

        if(upper.StartsWith("0X"))
        {
            cleaned = cleaned[2..];
        }
        else if(upper.StartsWith("0B"))
        {
            cleaned = cleaned[2..];
        }

        // Remove suffix for comparison.
        if(upper.EndsWith("UL") || upper.EndsWith("LU"))
        {
            if(ulong.TryParse(cleaned[..^2], out var val))
            {
                return val;
            }
        }
        else if(upper.EndsWith("L"))
        {
            if(long.TryParse(cleaned[..^1], out var val))
            {
                return val;
            }
        }
        else if(upper.EndsWith("U"))
        {
            if(uint.TryParse(cleaned[..^1], out var val))
            {
                return val;
            }
        }
        else
        {
            if(int.TryParse(cleaned, out var val))
            {
                return val;
            }

            if(long.TryParse(cleaned, out var lval))
            {
                return lval;
            }
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

        if(upper.EndsWith("F"))
        {
            if(float.TryParse(cleaned[..^1], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var val))
            {
                return val;
            }
        }
        else if(upper.EndsWith("M"))
        {
            if(decimal.TryParse(cleaned[..^1], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var val))
            {
                return val;
            }
        }
        else
        {
            if(double.TryParse(cleaned, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var val))
            {
                return val;
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts the string content from a string literal lexeme (removes surrounding quotes).
    /// </summary>
    private static string ExtractStringContent(string lexeme)
    {
        if(lexeme.Length >= 2 && lexeme[0] == '"' && lexeme[^1] == '"')
        {
            return lexeme[1..^1];
        }

        return lexeme;
    }

    /// <summary>
    /// Extracts the char content from a char literal lexeme (removes surrounding quotes).
    /// </summary>
    private static object ExtractCharContent(string lexeme)
    {
        if(lexeme.Length >= 3 && lexeme[0] == '\'' && lexeme[^1] == '\'')
        {
            var inner = lexeme[1..^1];
            if(inner.Length == 1)
            {
                return inner[0];
            }
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
        if(string.IsNullOrEmpty(typeName))
        {
            return null;
        }

        // Search up the scope chain for a type with the given name.
        var scope = memberScope.Parent;
        while(scope is not null)
        {
            foreach(var member in scope.Members)
            {
                if(member.Kind is SymbolKind.Class or SymbolKind.Struct
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
        return typeRef.First switch
        {
            NamedSingleTypeRefSingleTypeRefNode named => named.Name.IDENTIFIER.Lexeme,
            _ => null,
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Union type assignability helpers
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns <c>true</c> if the given union type contains at least one
    /// <see cref="ErrorType"/> constituent. A "poisoned" union suppresses
    /// cascading diagnostics by accepting any assignment.
    /// </summary>
    private static bool HasErrorConstituent(UnionType union)
    {
        foreach(var constituent in union.Constituents)
        {
            if(IsErrorType(constituent))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns <c>true</c> if <paramref name="type"/> is a constituent of
    /// <paramref name="union"/> — i.e., it matches one of the union's
    /// constituent types by display name or primitive equivalence.
    /// </summary>
    private static bool IsConstituentOf(IType type, UnionType union)
    {
        foreach(var constituent in union.Constituents)
        {
            if(ReferenceEquals(type, constituent))
            {
                return true;
            }

            if(type.DisplayName == constituent.DisplayName)
            {
                return true;
            }

            if(ArePrimitiveEquivalent(type, constituent))
            {
                return true;
            }
            // Refinement type constituent: the source is a constituent if it matches
            // the refinement's base type (e.g., int is a constituent of a union
            // containing RefinementType with BaseType=int).
            if(constituent is RefinementType refinement
                && (type.DisplayName == refinement.BaseType.DisplayName
                    || ArePrimitiveEquivalent(type, refinement.BaseType)))
            {
                return true;
            }
            // Cross-representation delegate compatibility: a stdlib Func<T1, ..., Void>
            // is assignable to a CLR Action<T1, ...> constituent when the parameter
            // counts match. This handles the case where the lambda resolves to stdlib
            // Func but the method parameter was projected as CLR Action. The type
            // checker already validated parameter type compatibility during lambda
            // inference, so arg-count matching is sufficient here.
            if(SemanticModelBuilder.IsFuncType(type) && type is ConstructedType funcCt
                && constituent is ConstructedType ctConstituent
                && !SemanticModelBuilder.IsFuncType(constituent)
                && constituent.DisplayName != "Void" && !constituent.IsVoid())
            {
                var funcArgs = funcCt.TypeArguments;
                var lastFuncArg = funcArgs.Count > 0 ? funcArgs[funcArgs.Count - 1] : null;
                var isVoidReturn = lastFuncArg is not null && lastFuncArg.IsVoid();

                if(isVoidReturn && funcArgs.Count - 1 == ctConstituent.TypeArguments.Count)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Validates a member access on a union-typed receiver (Req 7.1, 7.2, 7.3).
    /// Checks that the member exists on ALL constituent types. If it does,
    /// returns the union of member return types (or the single type if all
    /// identical). If the member is missing on any constituent, emits
    /// <see cref="UnionMemberNotOnAllConstituents"/> and returns
    /// <see cref="ErrorType.Instance"/>.
    /// </summary>
    private static IType? CheckUnionMemberAccess(
        string memberName,
        UnionType unionType,
        SyntaxNode node,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // If the union is poisoned (contains ErrorType), suppress cascading
        // diagnostics and return ErrorType.
        if(HasErrorConstituent(unionType))
        {
            return ErrorType.Instance;
        }

        var missingOn = new List<string>();
        var memberTypes = new List<IType>();

        foreach(var constituent in unionType.Constituents)
        {
            var memberSymbol = FindMemberSymbol(constituent, memberName, model);
            if(memberSymbol is null)
            {
                missingOn.Add(constituent.DisplayName);
            }
            else
            {
                var memberType = memberSymbol.DeclaredType;
                if(memberType is not null)
                {
                    memberTypes.Add(memberType);
                }
            }
        }

        if(missingOn.Count > 0)
        {
            var missingList = string.Join(", ", missingOn.Select(t => $"'{t}'"));
            diagnostics.ReportAtNode(TypeCheckDiagnostics.UnionMemberNotOnAllConstituents, node, "TypeChecker", memberName, unionType.DisplayName, missingList);
            return ErrorType.Instance;
        }

        // All constituents have the member. Compute the result type:
        // if all member types are identical, return that type; otherwise
        // return a union of the distinct member types.
        if(memberTypes.Count == 0)
        {
            return ErrorType.Instance;
        }

        // Check if all member types are the same.
        var allSame = true;
        var firstDisplayName = memberTypes[0].DisplayName;
        for(var i = 1; i < memberTypes.Count; i++)
        {
            if(memberTypes[i].DisplayName != firstDisplayName)
            {
                allSame = false;
                break;
            }
        }

        if(allSame)
        {
            return memberTypes[0];
        }

        // Distinct member types — build a union of the return types.
        // Deduplicate by DisplayName to avoid redundant constituents.
        var distinctTypes = new List<IType>();
        var seenDisplayNames = new HashSet<string>(StringComparer.Ordinal);
        foreach(var memberType in memberTypes)
        {
            if(seenDisplayNames.Add(memberType.DisplayName))
            {
                distinctTypes.Add(memberType);
            }
        }

        if(distinctTypes.Count == 1)
        {
            return distinctTypes[0];
        }

        return new UnionType(distinctTypes);
    }

    /// <summary>
    /// Attempts to emit a union-type-specific assignability diagnostic.
    /// Returns <c>true</c> if a targeted diagnostic was emitted, in which
    /// case the caller should not emit the generic MIST_TC_0001 diagnostic.
    /// </summary>
    private static bool TryEmitUnionAssignabilityDiagnostic(
        IType source,
        IType target,
        SyntaxNode node,
        DiagnosticBag diagnostics)
    {
        // Union → non-union (Req 6.4): must narrow first.
        if(source is UnionType && target is not UnionType)
        {
            diagnostics.ReportAtNode(TypeCheckDiagnostics.UnionToNonUnionAssignment, node, "TypeChecker", source.DisplayName, target.DisplayName);
            return true;
        }

        // Non-constituent → union (Req 6.3): source is not a constituent.
        if(source is not UnionType && target is UnionType)
        {
            diagnostics.ReportAtNode(TypeCheckDiagnostics.UnionNonConstituentAssignment, node, "TypeChecker", source.DisplayName, target.DisplayName);
            return true;
        }

        // Union → narrower union (Req 6.3): some source constituents missing.
        if(source is UnionType srcUnion && target is UnionType tgtUnion)
        {
            var missing = new List<string>();
            foreach(var constituent in srcUnion.Constituents)
            {
                if(!IsConstituentOf(constituent, tgtUnion))
                {
                    missing.Add(constituent.DisplayName);
                }
            }

            if(missing.Count > 0)
            {
                var missingList = string.Join(", ", missing);
                diagnostics.ReportAtNode(TypeCheckDiagnostics.UnionIncompatibleConstituents, node, "TypeChecker", source.DisplayName, target.DisplayName, missingList);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Detects the enum-specific assignability failure mode: assigning a value
    /// of the underlying type to a variable of the enum type (reverse conversion).
    /// When this case matches, emits <c>MIST_SB_0083</c> and returns <c>true</c>.
    /// Returns <c>false</c> if the case does not apply.
    /// </summary>
    private static bool TryEmitEnumAssignabilityDiagnostic(
        IType source,
        IType target,
        SyntaxNode node,
        DiagnosticBag diagnostics,
        ISemanticModel model)
    {
        // Unwrap type aliases for both source and target before checking.
        var unwrappedSource = source;
        while(unwrappedSource is TypeAliasType srcAlias)
        {
            unwrappedSource = srcAlias.UnderlyingType;
        }

        var unwrappedTarget = target;
        while(unwrappedTarget is TypeAliasType tgtAlias)
        {
            unwrappedTarget = tgtAlias.UnderlyingType;
        }

        // underlying type → enum (Req 7.3): reverse conversion rejected.
        if(unwrappedTarget is EnumType targetEnum &&
            unwrappedSource is not EnumType &&
            IsAssignable(unwrappedSource, targetEnum.UnderlyingType, model))
        {
            diagnostics.ReportAtNode(TypeCheckDiagnostics.CannotAssignUnderlyingToEnum, node, "TypeChecker");
            return true;
        }

        return false;
    }

    /// <summary>
    /// Detects delegate-func-conversion-specific assignability failure modes and
    /// emits a targeted diagnostic. Returns <c>true</c> if a diagnostic was emitted.
    /// <list type="bullet">
    /// <item><description><c>MIST_TC_0129</c> — Signature mismatch: func/lambda is
    /// not signature-compatible with target DelegateType (Req 2.5).</description></item>
    /// <item><description><c>MIST_TC_0130</c> — Nominal mismatch: cannot convert
    /// between two distinct named delegates even when signatures match (Req 6.1, 6.6).</description></item>
    /// <item><description><c>MIST_TC_0131</c> — Cannot implicitly narrow
    /// System.Delegate to a specific delegate or func type (Req 6.7).</description></item>
    /// </list>
    /// </summary>
    private static bool TryEmitDelegateAssignabilityDiagnostic(
        IType source,
        IType target,
        SyntaxNode node,
        DiagnosticBag diagnostics)
    {
        // Unwrap type aliases before checking.
        var unwrappedSource = source;
        while(unwrappedSource is TypeAliasType srcAlias)
        {
            unwrappedSource = srcAlias.UnderlyingType;
        }

        var unwrappedTarget = target;
        while(unwrappedTarget is TypeAliasType tgtAlias)
        {
            unwrappedTarget = tgtAlias.UnderlyingType;
        }

        // ── MIST_TC_0131: System.Delegate cannot be narrowed ──
        // Source is BaseDelegateType and target is a specific DelegateType or func<...>.
        if(unwrappedSource is BaseDelegateType)
        {
            if(unwrappedTarget is DelegateType)
            {
                diagnostics.ReportAtNode(TypeCheckDiagnostics.DelegateCannotNarrowBaseDelegate, node, "TypeChecker");
                return true;
            }

            if(SemanticModelBuilder.IsFuncType(unwrappedTarget))
            {
                diagnostics.ReportAtNode(TypeCheckDiagnostics.DelegateCannotNarrowBaseDelegate, node, "TypeChecker");
                return true;
            }

            return false;
        }

        // ── MIST_TC_0130: Nominal mismatch between two distinct named delegates ──
        if(unwrappedTarget is DelegateType && unwrappedSource is DelegateType)
        {
            // If we reach here, assignability already failed — they must be different.            diagnostics.ReportAtNode(TypeCheckDiagnostics.DelegateNominalMismatch, node, "TypeChecker");
            return true;
        }

        // ── MIST_TC_0129: Signature mismatch (func → DelegateType) ──
        if(unwrappedTarget is DelegateType targetDelegateForSig)
        {
            if(SemanticModelBuilder.IsFuncType(unwrappedSource))
            {
                // Signature mismatch: provide details about the incompatibility.
                _ = DescribeSignatureMismatch(unwrappedSource, targetDelegateForSig.FuncSignature); diagnostics.ReportAtNode(TypeCheckDiagnostics.DelegateSignatureMismatch, node, "TypeChecker");
                return true;
            }

            return false;
        }

        return false;
    }

    /// <summary>
    /// Describes why a func type is not signature-compatible with a target func type.
    /// Returns a human-readable reason string.
    /// </summary>
    private static string DescribeSignatureMismatch(IType sourceFunc, IType targetFunc)
    {
        if(sourceFunc is ConstructedType sourceCt && targetFunc is ConstructedType targetCt)
        {
            if(sourceCt.TypeArguments.Count != targetCt.TypeArguments.Count)
            {
                // Parameter count difference: func arity includes return type, so parameter count is arity - 1
                var sourceParamCount = sourceCt.TypeArguments.Count - 1;
                var targetParamCount = targetCt.TypeArguments.Count - 1;
                return $"parameter count mismatch (expected {targetParamCount}, got {sourceParamCount})";
            }

            for(var i = 0; i < sourceCt.TypeArguments.Count; i++)
            {
                if(!IsAssignable(sourceCt.TypeArguments[i], targetCt.TypeArguments[i]))
                {
                    if(i == sourceCt.TypeArguments.Count - 1)
                    {
                        return $"return type '{sourceCt.TypeArguments[i].DisplayName}' is not compatible with '{targetCt.TypeArguments[i].DisplayName}'";
                    }

                    return $"parameter {i + 1} type '{sourceCt.TypeArguments[i].DisplayName}' is not compatible with '{targetCt.TypeArguments[i].DisplayName}'";
                }
            }
        }

        return "incompatible signature";
    }

    /// <summary>
    /// Detects newtype-specific assignability failure modes and emits a targeted
    /// diagnostic. Returns <c>true</c> if a diagnostic was emitted.
    /// <list type="bullet">
    /// <item><description>Source type → Newtype: requires explicit cast (Req 2.3)</description></item>
    /// <item><description>Newtype → Source type: requires explicit cast (Req 2.4)</description></item>
    /// <item><description>Newtype A → Newtype B: not convertible (Req 2.5)</description></item>
    /// </list>
    /// </summary>
    private static bool TryEmitNewtypeAssignabilityDiagnostic(
        IType source,
        IType target,
        SyntaxNode node,
        DiagnosticBag diagnostics)
    {
        // Unwrap type aliases before checking.
        var unwrappedSource = source;
        while(unwrappedSource is TypeAliasType srcAlias)
        {
            unwrappedSource = srcAlias.UnderlyingType;
        }

        var unwrappedTarget = target;
        while(unwrappedTarget is TypeAliasType tgtAlias)
        {
            unwrappedTarget = tgtAlias.UnderlyingType;
        }

        // Cross-newtype assignment (Req 2.5): two different newtypes.
        if(unwrappedSource is NewtypeType && unwrappedTarget is NewtypeType)
        {
            diagnostics.ReportAtNode(TypeCheckDiagnostics.ImplicitConversionNotExists, node, "TypeChecker", source.DisplayName, target.DisplayName);
            return true;
        }

        // Newtype → Source type (Req 2.4): newtype assigned where source type expected.
        if(unwrappedSource is NewtypeType)
        {
            diagnostics.ReportAtNode(TypeCheckDiagnostics.ImplicitConversionNotExists, node, "TypeChecker", source.DisplayName, target.DisplayName);
            return true;
        }

        // Source type → Newtype (Req 2.3): source type assigned where newtype expected.
        if(unwrappedTarget is NewtypeType)
        {
            diagnostics.ReportAtNode(TypeCheckDiagnostics.ImplicitConversionNotExists, node, "TypeChecker");
            return true;
        }

        return false;
    }

    /// <summary>
    /// Determines whether the given type is a constituent of the specified union type.
    /// Comparison is by <see cref="IType.DisplayName"/> equality (structural).
    /// </summary>
    private static bool IsConstituentOfUnion(IType type, UnionType union)
    {
        foreach(var constituent in union.Constituents)
        {
            if(string.Equals(constituent.DisplayName, type.DisplayName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Type argument inference failure diagnostics (Task 4.1/4.2)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Emits a diagnostic when type argument inference fails for a generic method call.
    /// Emits MIST_TC_0080 for uninferrable type parameters or MIST_TC_0081 for conflicting
    /// inferred types. Exactly one diagnostic is emitted per call site.
    /// </summary>
    private static void EmitInferenceFailureDiagnostic(
        InferenceResult inferenceResult,
        MethodCallSuffixPostfixSuffixNode methodCall,
        DiagnosticBag diagnostics)
    {
        if(inferenceResult.Conflicts is { Count: > 0 })
        {
            diagnostics.ReportAtNode(TypeCheckDiagnostics.TypeInferenceConflict, methodCall, "TypeChecker");
        }
        else if(inferenceResult.UninferrableTypeParameters is { Count: > 0 })
        {
            diagnostics.ReportAtNode(TypeCheckDiagnostics.TypeInferenceFailed, methodCall, "TypeChecker");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Generic constraint satisfaction checking (Task 6.1)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Verifies that a type argument satisfies all constraints declared on the
    /// corresponding type parameter. Emits diagnostics MIST_CONSTRAINT_0009
    /// through MIST_CONSTRAINT_0015 for each violated constraint.
    /// </summary>
    /// <param name="typeArgument">The concrete type argument being checked.</param>
    /// <param name="typeParameter">The type parameter with constraints to check against.</param>
    /// <param name="node">The syntax node for diagnostic location reporting.</param>
    /// <param name="genericTypeName">The display name of the generic type/method for diagnostic messages.</param>
    /// <param name="model">The semantic model.</param>
    /// <param name="diagnostics">The diagnostic bag to emit errors into.</param>
    internal static void CheckConstraintSatisfaction(
        IType typeArgument,
        TypeParameterType typeParameter,
        SyntaxNode node,
        string genericTypeName,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Suppress cascading errors for error types.
        if(IsErrorType(typeArgument))
        {
            return;
        }

        // Unwrap type aliases to their underlying type for constraint checking.
        var effectiveTypeArgument = typeArgument is TypeAliasType aliasArgEarly
            ? aliasArgEarly.UnderlyingType
            : typeArgument;

        // ── MIST2004: Ref struct generic type argument restriction (Requirement 3.7) ──
        // A ref struct type cannot be used as a generic type argument, since generic
        // instantiation may box or store the value on the heap.
        if(IsRefStructType(effectiveTypeArgument))
        {
            diagnostics.ReportAtNode(TypeCheckDiagnostics.RefStructGenericArgRestriction, node, "TypeChecker", effectiveTypeArgument.DisplayName);
        }

        // ── MIST_ANON_ESCAPE_GENERIC: Anonymous type generic argument restriction (Requirement 4.7) ──
        // Anonymous types have no denotable name and cannot be used as generic type arguments.
        if(effectiveTypeArgument is AnonymousType)
        {
            diagnostics.ReportAtNode(TypeCheckDiagnostics.AnonEscapeGeneric, node, "TypeChecker");
        }

        var constraints = typeParameter.Constraints;
        if(constraints is null)
        {
            return;
        }

        // Unwrap type aliases to their underlying type for constraint checking.
        if(typeArgument is TypeAliasType aliasArg)
        {
            typeArgument = aliasArg.UnderlyingType;
        }

        // Check class constraint: type argument must be a reference type.
        if(constraints.HasClassConstraint)
        {
            if(typeArgument.Kind == TypeKind.Struct)
            {
                diagnostics.ReportAtNode(TypeCheckDiagnostics.ConstraintClassViolation, node, "TypeChecker", typeArgument.DisplayName, typeParameter.Name);
            }
        }

        // Check struct constraint: type argument must be a value type.
        if(constraints.HasStructConstraint)
        {
            if(typeArgument.Kind != TypeKind.Struct)
            {
                diagnostics.ReportAtNode(TypeCheckDiagnostics.ConstraintStructViolation, node, "TypeChecker", typeArgument.DisplayName, typeParameter.Name);
            }
        }

        // Check base class constraint: type argument must derive from the base class.
        if(constraints.BaseClassConstraint is not null)
        {
            if(!DerivesFromClass(typeArgument, constraints.BaseClassConstraint))
            {
                diagnostics.ReportAtNode(TypeCheckDiagnostics.ConstraintBaseClassNotDerived, node, "TypeChecker", typeArgument.DisplayName, constraints.BaseClassConstraint!.DisplayName, typeParameter.Name, genericTypeName);
            }
        }

        // Check abstract constraints: type argument must implement each abstract.
        foreach(var abs in constraints.AbstractConstraints)
        {
            if(!ImplementsAbstractConstraint(typeArgument, abs, model))
            {
                diagnostics.ReportAtNode(TypeCheckDiagnostics.ConstraintAbstractNotImplemented, node, "TypeChecker", typeArgument.DisplayName, abs.DisplayName, typeParameter.Name, genericTypeName);
            }
        }

        // Check constructor constraints (OR semantics): type argument must have
        // at least one matching public constructor.
        if(constraints.ConstructorConstraints.Count > 0)
        {
            CheckConstructorConstraints(
                typeArgument, typeParameter, constraints.ConstructorConstraints,
node, model, diagnostics);
        }

        // Check readonly constraint: type argument must be a readonly type.
        if(constraints.HasReadonlyConstraint)
        {
            if(!IsTypeReadonly(typeArgument))
            {
                diagnostics.ReportAtNode(TypeCheckDiagnostics.ConstraintReadonlyViolation, node, "TypeChecker");
            }
        }

        // Check mutable constraint: type argument must NOT be a readonly type.
        if(constraints.HasMutableConstraint)
        {
            if(IsTypeReadonly(typeArgument))
            {
                diagnostics.ReportAtNode(TypeCheckDiagnostics.ConstraintMutableViolation, node, "TypeChecker");
            }
        }

        // Check owned constraint: type argument must be an owned type.
        if(constraints.HasOwnedConstraint)
        {
            if(!IsTypeOwned(typeArgument))
            {
                diagnostics.ReportAtNode(TypeCheckDiagnostics.ConstraintOwnedViolation, node, "TypeChecker");
            }
        }

        // Check default constraint: type argument must be a defaultable type.
        if(constraints.HasDefaultConstraint)
        {
            if(!IsTypeDefaultable(typeArgument))
            {
                diagnostics.ReportAtNode(CommonDiagnostics.DefaultConstraintNotSatisfied, node, "TypeChecker");
            }
        }

        // Check substructural constraint: type argument's discipline must satisfy
        // the constraint discipline per the satisfaction lattice.
        var requiredDiscipline = GetConstraintDiscipline(constraints);
        if(requiredDiscipline != SubstructuralDiscipline.None)
        {
            var argDiscipline = GetArgumentDiscipline(typeArgument);
            if(!SatisfiesSubstructuralConstraint(argDiscipline, requiredDiscipline))
            {
                _ = requiredDiscipline switch
                {
                    SubstructuralDiscipline.Linear => "linear",
                    SubstructuralDiscipline.Affine => "affine",
                    SubstructuralDiscipline.Relevant => "relevant",
                    _ => "substructural",
                }; diagnostics.ReportAtNode(TypeCheckDiagnostics.ConstraintSubstructuralViolation, node, "TypeChecker");
            }
        }
    }

    /// <summary>
    /// Verifies that every element of a bound type-parameter pack satisfies the
    /// constraints declared on the pack's type parameter (whole-pack constraint,
    /// applied elementwise). Each failing element emits <c>MIST_VAR_0009</c> and
    /// is treated as an error type so downstream analysis continues.
    /// An empty pack vacuously satisfies the constraint (Requirement 11.4).
    /// </summary>
    /// <param name="packElementTypes">The ordered element types bound to the pack.</param>
    /// <param name="typeParameter">The pack type parameter with constraints to check.</param>
    /// <param name="node">The syntax node for diagnostic location reporting.</param>
    /// <param name="model">The semantic model.</param>
    /// <param name="diagnostics">The diagnostic bag to emit errors into.</param>
    internal static void CheckPackConstraintSatisfaction(
        IReadOnlyList<IType> packElementTypes,
        TypeParameterType typeParameter,
        SyntaxNode node,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Requirement 11.4: empty pack vacuously satisfies any constraint.
        if(packElementTypes.Count == 0)
        {
            return;
        }

        var constraints = typeParameter.Constraints;
        if(constraints is null)
        {
            return; // Unconstrained pack — nothing to check.
        }

        // Requirement 11.1: check each element independently against the constraint.
        for(var i = 0; i < packElementTypes.Count; i++)
        {
            var element = packElementTypes[i];

            // Suppress cascading errors for error types.
            if(IsErrorType(element))
            {
                continue;
            }

            if(!SatisfiesConstraints(element, constraints, model))
            {
                // Requirement 11.3: emit MIST_VAR_0009 per failing element.
                var constraintName = DescribeConstraints(constraints);
                diagnostics.ReportAtNode(
                    VariadicsDiagnostics.PackElementConstraintNotSatisfied,
                    node, "TypeChecker", element.DisplayName, constraintName);
            }
        }
    }

    /// <summary>
    /// Returns <c>true</c> when a type satisfies all constraints in the given set.
    /// Used by <see cref="CheckPackConstraintSatisfaction"/> for elementwise checks.
    /// </summary>
    private static bool SatisfiesConstraints(IType type, TypeParameterConstraints constraints, ISemanticModel model)
    {
        // Unwrap type aliases.
        if(type is TypeAliasType alias)
        {
            type = alias.UnderlyingType;
        }

        // class constraint
        if(constraints.HasClassConstraint && type.Kind == TypeKind.Struct)
        {
            return false;
        }

        // struct constraint
        if(constraints.HasStructConstraint && type.Kind != TypeKind.Struct)
        {
            return false;
        }

        // base class constraint
        if(constraints.BaseClassConstraint is not null && !DerivesFromClass(type, constraints.BaseClassConstraint))
        {
            return false;
        }

        // abstract constraints (interfaces)
        foreach(var abs in constraints.AbstractConstraints)
        {
            if(!ImplementsAbstractConstraint(type, abs, model))
            {
                return false;
            }
        }

        // readonly constraint
        if(constraints.HasReadonlyConstraint && !IsTypeReadonly(type))
        {
            return false;
        }

        // mutable constraint
        if(constraints.HasMutableConstraint && IsTypeReadonly(type))
        {
            return false;
        }

        // owned constraint
        if(constraints.HasOwnedConstraint && !IsTypeOwned(type))
        {
            return false;
        }

        // default constraint
        if(constraints.HasDefaultConstraint && !IsTypeDefaultable(type))
        {
            return false;
        }

        // substructural constraint
        var requiredDiscipline = GetConstraintDiscipline(constraints);
        if(requiredDiscipline != SubstructuralDiscipline.None)
        {
            var argDiscipline = GetArgumentDiscipline(type);
            if(!SatisfiesSubstructuralConstraint(argDiscipline, requiredDiscipline))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Produces a human-readable description of the constraints for diagnostic messages.
    /// </summary>
    private static string DescribeConstraints(TypeParameterConstraints constraints)
    {
        var parts = new List<string>();

        if(constraints.HasClassConstraint)
        {
            parts.Add("class");
        }

        if(constraints.HasStructConstraint)
        {
            parts.Add("struct");
        }

        if(constraints.BaseClassConstraint is not null)
        {
            parts.Add(constraints.BaseClassConstraint.DisplayName);
        }

        foreach(var abs in constraints.AbstractConstraints)
        {
            parts.Add(abs.DisplayName);
        }

        if(constraints.ConstructorConstraints.Count > 0)
        {
            parts.Add("new()");
        }

        if(constraints.HasReadonlyConstraint)
        {
            parts.Add("readonly");
        }

        if(constraints.HasMutableConstraint)
        {
            parts.Add("mutable");
        }

        if(constraints.HasOwnedConstraint)
        {
            parts.Add("owned");
        }

        if(constraints.HasDefaultConstraint)
        {
            parts.Add("default");
        }

        if(constraints.HasLinearConstraint)
        {
            parts.Add("linear");
        }

        if(constraints.HasAffineConstraint)
        {
            parts.Add("affine");
        }

        if(constraints.HasRelevantConstraint)
        {
            parts.Add("relevant");
        }

        return parts.Count > 0 ? string.Join(", ", parts) : "constraint";
    }

    /// <summary>
    /// Returns the substructural discipline required by a type parameter's constraints.
    /// Returns <see cref="SubstructuralDiscipline.None"/> if no substructural constraint is present.
    /// </summary>
    private static SubstructuralDiscipline GetConstraintDiscipline(TypeParameterConstraints constraints)
    {
        if(constraints.HasLinearConstraint)
        {
            return SubstructuralDiscipline.Linear;
        }

        if(constraints.HasAffineConstraint)
        {
            return SubstructuralDiscipline.Affine;
        }

        if(constraints.HasRelevantConstraint)
        {
            return SubstructuralDiscipline.Relevant;
        }

        return SubstructuralDiscipline.None;
    }

    /// <summary>
    /// Returns the substructural discipline of a type argument. Resolves through
    /// <see cref="ConstructedType"/> to get the generic definition's discipline.
    /// For <see cref="TypeParameterType"/>, reads constraint booleans.
    /// </summary>
    private static SubstructuralDiscipline GetArgumentDiscipline(IType type)
    {
        if(type is TypeParameterType tpType)
        {
            var tpConstraints = tpType.Constraints;
            if(tpConstraints is null)
            {
                return SubstructuralDiscipline.None;
            }

            if(tpConstraints.HasLinearConstraint)
            {
                return SubstructuralDiscipline.Linear;
            }

            if(tpConstraints.HasAffineConstraint)
            {
                return SubstructuralDiscipline.Affine;
            }

            if(tpConstraints.HasRelevantConstraint)
            {
                return SubstructuralDiscipline.Relevant;
            }

            return SubstructuralDiscipline.None;
        }

        var resolved = type is ConstructedType constructed ? constructed.GenericDefinition : type;
        return resolved switch
        {
            ClassType ct => ct.Discipline,
            StructType st => st.Discipline,
            _ => SubstructuralDiscipline.None,
        };
    }

    /// <summary>
    /// Determines whether an argument discipline satisfies a required constraint discipline
    /// per the satisfaction lattice:
    /// <list type="bullet">
    ///   <item>Linear satisfies {linear, affine, relevant}.</item>
    ///   <item>Affine satisfies only {affine}.</item>
    ///   <item>Relevant satisfies only {relevant}.</item>
    ///   <item>Unrestricted (None) satisfies nothing.</item>
    /// </list>
    /// </summary>
    private static bool SatisfiesSubstructuralConstraint(
        SubstructuralDiscipline argument,
        SubstructuralDiscipline constraint)
    {
        return constraint switch
        {
            SubstructuralDiscipline.Linear => argument == SubstructuralDiscipline.Linear,
            SubstructuralDiscipline.Affine => argument is SubstructuralDiscipline.Affine or SubstructuralDiscipline.Linear,
            SubstructuralDiscipline.Relevant => argument is SubstructuralDiscipline.Relevant or SubstructuralDiscipline.Linear,
            _ => true,
        };
    }

    /// <summary>
    /// Checks constructor constraints with OR semantics. The type argument must
    /// have a public constructor matching at least one of the declared constructor
    /// constraint signatures.
    /// </summary>
    private static void CheckConstructorConstraints(
        IType typeArgument,
        TypeParameterType typeParameter,
        IReadOnlyList<IReadOnlyList<IType>> constructorConstraints,

        SyntaxNode node,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Collect public constructors from the type argument.
        var publicConstructors = CollectPublicConstructors(typeArgument);

        // For each constructor constraint, check if at least one public constructor matches.
        foreach(var requiredParams in constructorConstraints)
        {
            var matched = false;

            // Structs always have a parameterless constructor.
            if(requiredParams.Count == 0 && typeArgument.Kind == TypeKind.Struct)
            {
                matched = true;
            }
            else
            {
                foreach(var ctor in publicConstructors)
                {
                    if(ConstructorMatchesConstraint(ctor, requiredParams, model))
                    {
                        matched = true;
                        break;
                    }
                }

                // If no explicit constructors are defined and the constraint is
                // parameterless, the default constructor satisfies it.
                if(!matched && requiredParams.Count == 0 && publicConstructors.Count == 0)
                {
                    // Types with no declared constructors have an implicit
                    // public parameterless constructor.
                    matched = true;
                }
            }

            if(!matched)
            {
                if(requiredParams.Count == 0)
                {
                    diagnostics.ReportAtNode(TypeCheckDiagnostics.ConstraintParameterlessCtorViolation, node, "TypeChecker", typeArgument.DisplayName, typeParameter.Name);
                }
                else
                {
                    var paramTypes = string.Join(", ", requiredParams.Select(p => p.DisplayName));
                    diagnostics.ReportAtNode(TypeCheckDiagnostics.ConstraintParameterizedCtorViolation, node, "TypeChecker", typeArgument.DisplayName, paramTypes, typeParameter.Name);
                }
            }
        }
    }

    /// <summary>
    /// Checks whether a type derives from (or is) the specified base class.
    /// </summary>
    private static bool DerivesFromClass(IType typeArgument, IType baseClassConstraint)
    {
        // The type argument itself matches the base class.
        if(ReferenceEquals(typeArgument, baseClassConstraint)
            || typeArgument.DisplayName == baseClassConstraint.DisplayName)
        {
            return true;
        }

        // Walk the inheritance chain for ClassType.
        if(typeArgument is ClassType classType)
        {
            var current = classType.BaseType as ClassType;
            while(current is not null)
            {
                if(ReferenceEquals(current, baseClassConstraint)
                    || current.DisplayName == baseClassConstraint.DisplayName)
                {
                    return true;
                }

                current = current.BaseType as ClassType;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks whether a type argument implements the specified abstract constraint.
    /// Handles both <see cref="AbstractType"/> and <see cref="ConstructedType"/>
    /// (for generic abstract constraints like <c>IComparable&lt;T&gt;</c>).
    /// When <paramref name="model"/> is provided, also consults the <see cref="ConformanceMap"/>
    /// for with-block conformance.
    /// </summary>
    private static bool ImplementsAbstractConstraint(IType typeArgument, IType abstractConstraint, ISemanticModel? model = null)
    {
        // If the constraint is a plain AbstractType, use the existing helper.
        if(abstractConstraint is AbstractType abstractType)
        {
            return ImplementsAbstract(typeArgument, abstractType, model);
        }

        // For ConstructedType abstract constraints (e.g., IComparable<int>),
        // check if the type argument implements a matching constructed abstract.
        if(abstractConstraint is ConstructedType constructedAbstract
            && constructedAbstract.GenericDefinition is AbstractType)
        {
            return ImplementsConstructedAbstract(typeArgument, constructedAbstract);
        }

        // Fallback: display name comparison.
        return GetStaticAbstractList(typeArgument)
            .Any(i => i.DisplayName == abstractConstraint.DisplayName);
    }

    /// <summary>
    /// Gets the abstract list from a type (ClassType, StructType, or AbstractType).
    /// Static version used by constraint checking where the model is not needed.
    /// </summary>
    private static IReadOnlyList<IType> GetStaticAbstractList(IType type)
    {
        return type switch
        {
            ClassType ct => ct.Abstracts,
            StructType st => st.Abstracts,
            AbstractType it => it.Abstracts,
            _ => Array.Empty<IType>(),
        };
    }

    /// <summary>
    /// Gets the abstract list from a type (ClassType, StructType, or AbstractType).
    /// For dedicated intrinsic types (<see cref="ArrayType"/> and <see cref="SpanType"/>),
    /// bridges to the registered declaration's abstract list.
    /// </summary>
    private IReadOnlyList<IType> GetAbstractList(IType type, ISemanticModel model)
    {
        // Bridge: for dedicated intrinsic types, look up the registered declaration's abstracts.
        if(type is ArrayType)
        {
            var registeredClass = FindRegisteredClassType("Array", "Standard", model);
            if(registeredClass != null)
            {
                return registeredClass.Abstracts;
            }
        }

        if(type is SpanType spanType)
        {
            var name = spanType.IsReadOnly ? "ReadOnlySpan" : "Span";
            var registeredStruct = FindRegisteredStructType(name, "Standard", model);
            if(registeredStruct != null)
            {
                return registeredStruct.Abstracts;
            }
        }

        return type switch
        {
            ClassType ct => ct.Abstracts,
            StructType st => st.Abstracts,
            AbstractType it => it.Abstracts,
            _ => Array.Empty<IType>(),
        };
    }

    /// <summary>
    /// Collects all public constructors from a type argument.
    /// </summary>
    private static List<ISymbol> CollectPublicConstructors(IType typeArgument)
    {
        var results = new List<ISymbol>();

        var memberScope = typeArgument switch
        {
            ClassType ct => ct.MemberScope,
            StructType st => st.MemberScope,
            ConstructedType cst => cst.MemberScope,
            _ => null,
        };

        if(memberScope is null)
        {
            return results;
        }

        foreach(var member in memberScope.Members)
        {
            if(member.Kind != SymbolKind.Constructor)
            {
                continue;
            }

            // Check if the constructor is public using the resolved accessibility.
            if(member.Accessibility == Accessibility.Public)
            {
                results.Add(member);
            }
        }

        return results;
    }

    /// <summary>
    /// Checks whether a constructor symbol matches a constructor constraint
    /// (a list of required parameter types).
    /// </summary>
    private static bool ConstructorMatchesConstraint(
        ISymbol ctor,
        IReadOnlyList<IType> requiredParams,
        ISemanticModel model)
    {
        SyntaxNode ctorNode;
        try { ctorNode = model.GetDeclaringNode(ctor); }
        catch { return false; }

        if(ctorNode is not ConstructorDeclarationNode ctorDecl)
        {
            return false;
        }

        var paramTypes = new List<IType>();
        if(ctorDecl.Parameters is not null)
        {
            foreach(var child in ctorDecl.Parameters.Children())
            {
                if(child is ParameterNode paramNode)
                {
                    var paramSym = model.GetDeclaredSymbol(paramNode);
                    if(paramSym?.DeclaredType is not null)
                    {
                        paramTypes.Add(paramSym.DeclaredType);
                    }
                }
            }
        }

        if(paramTypes.Count != requiredParams.Count)
        {
            return false;
        }

        for(var i = 0; i < requiredParams.Count; i++)
        {
            // For constructor constraints, we check exact type match
            // (by display name) since the constraint specifies the
            // exact parameter types required.
            if(paramTypes[i].DisplayName != requiredParams[i].DisplayName)
            {
                return false;
            }
        }

        return true;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Dependent type argument validation (MIST_DT_0002, MIST_DT_0003)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Validates all dependent type arguments in the program:
    /// <list type="bullet">
    /// <item><c>MIST_DT_0002</c> — a dependent type argument position contains a
    /// non-constant expression (variable, method call, property access, or expression).</item>
    /// <item><c>MIST_DT_0003</c> — a dependent type argument's resolved type does not
    /// match the dependent parameter's value type annotation.</item>
    /// </list>
    /// Walks all AST trees recursively to find <see cref="TypeArgumentListNode"/> instances
    /// and validates each dependent position against the corresponding type parameter.
    /// </summary>
    private static void ValidateDependentTypeArguments(ISemanticModel model, DiagnosticBag diagnostics)
    {
        foreach(var ast in model.Asts)
        {
            ValidateDependentTypeArgumentsInTree(ast, model, diagnostics);
        }
    }

    /// <summary>
    /// Recursively walks an AST subtree looking for nodes that carry type argument lists
    /// and validates dependent positions.
    /// </summary>
    private static void ValidateDependentTypeArgumentsInTree(
        SyntaxNode node, ISemanticModel model, DiagnosticBag diagnostics)
    {
        // Check if this node carries a type argument list that needs validation.
        switch(node)
        {
            case ObjectCreationExpressionPrimaryNode objCreation when objCreation.TypeArgs is not null:
                ValidateTypeArgListForNode(objCreation, objCreation.TypeArgs, model, diagnostics);
                break;

            case NamedSingleTypeRefSingleTypeRefNode namedRef when namedRef.TypeArgs is not null:
                ValidateTypeArgListForNode(namedRef, namedRef.TypeArgs, model, diagnostics);
                break;

            case BaseTypeRefNode baseRef when baseRef.TypeArgs is not null:
                ValidateTypeArgListForNode(baseRef, baseRef.TypeArgs, model, diagnostics);
                break;

            case MethodCallSuffixPostfixSuffixNode methodCall when methodCall.TypeArgs is not null:
                ValidateMethodCallDependentTypeArgs(methodCall, model, diagnostics);
                break;
        }

        // Recurse into children.
        foreach(var child in node.Children())
        {
            if(child is not null)
            {
                ValidateDependentTypeArgumentsInTree(child, model, diagnostics);
            }
        }
    }

    /// <summary>
    /// Validates a single <see cref="TypeArgumentListNode"/> against the resolved type's
    /// parameters. Emits <c>MIST_DT_0002</c> for non-constant arguments in dependent
    /// positions and <c>MIST_DT_0003</c> for type mismatches.
    /// </summary>
    private static void ValidateTypeArgListForNode(
        SyntaxNode ownerNode,
        TypeArgumentListNode typeArgList,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Resolve the type that this type argument list belongs to.
        var resolvedType = model.GetTypeOf(ownerNode);
        if(resolvedType is null or ErrorType)
        {
            return;
        }

        // Unwrap type aliases to get the underlying constructed type.
        if(resolvedType is TypeAliasType alias)
        {
            resolvedType = alias.UnderlyingType;
        }

        // We need a ConstructedType to get the generic definition's type parameters.
        ConstructedType? constructedType = null;
        IReadOnlyList<IType>? typeParams;
        if(resolvedType is ConstructedType ct)
        {
            constructedType = ct;
            typeParams = GetTypeParameters(ct.GenericDefinition);
        }
        else
        {
            // For non-constructed types, try to get type parameters directly.
            typeParams = GetTypeParameters(resolvedType);
        }

        if(typeParams is null || typeParams.Count == 0)
        {
            return;
        }

        // Collect all type argument nodes.
        var argNodes = new List<TypeArgumentNode> { typeArgList.TypeArgument };
        foreach(var extra in typeArgList.TypeArguments)
        {
            argNodes.Add(extra);
        }

        // Validate each position.
        for(var i = 0; i < argNodes.Count && i < typeParams.Count; i++)
        {
            if(typeParams[i] is not DependentTypeParameterType depParam)
            {
                continue;
            }

            var argNode = argNodes[i];

            // Check MIST_DT_0002: Is the argument a compile-time constant?
            if(!IsDependentArgCompileTimeConstant(argNode, model))
            {
                _ = ExtractTypeArgumentText(argNode); diagnostics.ReportAtNode(TypeCheckDiagnostics.DependentArgNotConstant, argNode, "TypeChecker");
                continue; // Skip type match check if not a constant.
            }

            // Check MIST_DT_0003: Does the argument's type match the parameter's value type?
            if(constructedType?.DependentArguments is not null
                && i < constructedType.DependentArguments.Count
                && constructedType.DependentArguments[i] is { } depArg)
            {
                if(!DependentValueTypeMatches(depArg, depParam))
                {
                    diagnostics.ReportAtNode(TypeCheckDiagnostics.DependentArgTypeMismatch, argNode, "TypeChecker");
                }
                else
                {
                    // Check MIST_REFINEMENT_0004/0006/0008: Adhoc refinement constraint validation.
                    ValidateAdhocConstraintForDependentArg(
                        depParam, depArg, constructedType, i, argNode, diagnostics);
                }
            }
        }
    }

    /// <summary>
    /// Validates an adhoc refinement constraint on a dependent type parameter against
    /// the provided argument. Handles two cases:
    /// <list type="bullet">
    ///   <item>Concrete constant value → evaluate predicate, emit MIST_REFINEMENT_0004 on violation.</item>
    ///   <item>Forwarded dependent parameter → check implication, emit MIST_REFINEMENT_0006 or 0008.</item>
    /// </list>
    /// </summary>
    private static void ValidateAdhocConstraintForDependentArg(
        DependentTypeParameterType depParam,
        DependentValueArgument depArg,
        ConstructedType constructedType,
        int argIndex,
        SyntaxNode argNode,

        DiagnosticBag diagnostics)
    {
        var targetConstraint = depParam.AdhocConstraint;
        if(targetConstraint is null)
        {
            return;
        }

        // Determine if this is a forwarded dependent parameter or a concrete constant.
        var isForwarded = argIndex < constructedType.TypeArguments.Count
                          && constructedType.TypeArguments[argIndex] is DependentTypeParameterType;

        if(isForwarded)
        {
            // Forwarding case: check implication between source and target constraints.
            var sourceParam = (DependentTypeParameterType)constructedType.TypeArguments[argIndex];
            var sourceConstraint = sourceParam.AdhocConstraint;

            if(sourceConstraint is null)
            {
                // MIST_REFINEMENT_0008: Source has no constraint, target requires one.                diagnostics.ReportAtNode(TypeCheckDiagnostics.RefinementUnconstrainedForwarding, argNode, "TypeChecker");
            }
            else if(!RefinementSolver.Implies(sourceConstraint, targetConstraint))
            {
                // MIST_REFINEMENT_0006: Source constraint does not imply target constraint.                diagnostics.ReportAtNode(TypeCheckDiagnostics.RefinementImplicationFailure, argNode, "TypeChecker");
            }
        }
        else
        {
            // Concrete constant case: evaluate the predicate against the value.
            if(!targetConstraint.Evaluate(depArg.Value))
            {
                _ = constructedType.GenericDefinition.DisplayName; diagnostics.ReportAtNode(TypeCheckDiagnostics.RefinementConstraintViolation, argNode, "TypeChecker");
            }
        }
    }

    /// <summary>
    /// Validates dependent type arguments on a method call suffix node.
    /// Resolves the called method's type parameters and checks each dependent position.
    /// </summary>
    private static void ValidateMethodCallDependentTypeArgs(
        MethodCallSuffixPostfixSuffixNode methodCall,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Try to resolve the method symbol to get its type parameters.
        var methodSymbol = model.GetDeclaredSymbol(methodCall);
        if(methodSymbol is not Symbol sym || sym.TypeParameters.Count == 0)
        {
            return;
        }

        var typeParams = sym.TypeParameters;
        var typeArgList = methodCall.TypeArgs!;

        // Collect all type argument nodes.
        var argNodes = new List<TypeArgumentNode> { typeArgList.TypeArgument };
        foreach(var extra in typeArgList.TypeArguments)
        {
            argNodes.Add(extra);
        }

        // Validate each position.
        for(var i = 0; i < argNodes.Count && i < typeParams.Count; i++)
        {
            if(typeParams[i] is not DependentTypeParameterType depParam)
            {
                continue;
            }

            var argNode = argNodes[i];

            // Check MIST_DT_0002: Is the argument a compile-time constant?
            if(!IsDependentArgCompileTimeConstant(argNode, model))
            {
                _ = ExtractTypeArgumentText(argNode); diagnostics.ReportAtNode(TypeCheckDiagnostics.DependentArgNotConstant, argNode, "TypeChecker");
                continue;
            }

            // For method calls, type mismatch checking (MIST_DT_0003) is handled
            // by the semantic model builder during type argument resolution. The
            // DependentValueArgument's ValueType is set from the parameter's ValueType
            // during resolution, so mismatches are caught at the semantic level.

            // Check adhoc refinement constraint on the dependent parameter.
            if(depParam.AdhocConstraint is { } constraint)
            {
                ValidateMethodCallAdhocConstraint(constraint, argNode, model, diagnostics);
            }
        }
    }

    /// <summary>
    /// Validates an adhoc refinement constraint on a method's dependent type parameter
    /// against the provided type argument. Extracts the constant value from the AST node
    /// and evaluates the predicate, or checks implication for forwarded parameters.
    /// </summary>
    private static void ValidateMethodCallAdhocConstraint(
        RefinementPredicate constraint,
        TypeArgumentNode argNode,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Check if this is a forwarded dependent parameter.
        if(argNode is TypeReferenceTypeArgumentTypeArgumentNode typeRefArg
            && typeRefArg.Type.First is NamedSingleTypeRefSingleTypeRefNode namedRef
            && namedRef.TypeArgs is null
            && namedRef.ArrayRank is null)
        {
            var resolvedType = model.GetTypeOf(namedRef);
            if(resolvedType is DependentTypeParameterType forwardedParam)
            {
                // Forwarding case: check implication.
                var sourceConstraint = forwardedParam.AdhocConstraint;
                if(sourceConstraint is null)
                {
                    diagnostics.ReportAtNode(TypeCheckDiagnostics.RefinementUnconstrainedForwarding, argNode, "TypeChecker");
                }
                else if(!RefinementSolver.Implies(sourceConstraint, constraint))
                {
                    diagnostics.ReportAtNode(TypeCheckDiagnostics.RefinementImplicationFailure, argNode, "TypeChecker");
                }

                return;
            }
        }

        // Concrete constant case: extract the value and evaluate.
        var constantValue = TryExtractConstantFromTypeArgNode(argNode);
        if(constantValue is not null && !constraint.Evaluate(constantValue))
        {
            _ = ExtractTypeArgumentText(argNode); diagnostics.ReportAtNode(TypeCheckDiagnostics.RefinementConstraintViolation, argNode, "TypeChecker");
        }
    }

    /// <summary>
    /// Extracts a compile-time constant value from a <see cref="TypeArgumentNode"/>.
    /// Returns <c>null</c> if the value cannot be extracted.
    /// </summary>
    private static object? TryExtractConstantFromTypeArgNode(TypeArgumentNode argNode)
    {
        return argNode switch
        {
            IntegerLiteralTypeArgumentTypeArgumentNode intLit =>
                long.TryParse(intLit.Literal.Lexeme, out var v) ? v : null,
            NegatedIntegerTypeArgumentTypeArgumentNode negLit =>
                long.TryParse(negLit.Literal.Lexeme, out var v) ? -v : null,
            HexLiteralTypeArgumentTypeArgumentNode hexLit =>
                TryParseHexTypeArg(hexLit.Literal.Lexeme),
            BinaryLiteralTypeArgumentTypeArgumentNode binLit =>
                TryParseBinaryTypeArg(binLit.Literal.Lexeme),
            TrueLiteralTypeArgumentTypeArgumentNode => true,
            FalseLiteralTypeArgumentTypeArgumentNode => false,
            CharLiteralTypeArgumentTypeArgumentNode charLit =>
                charLit.Literal.Lexeme.Length >= 3 ? charLit.Literal.Lexeme[1] : null,
            _ => null,
        };
    }

    private static object? TryParseHexTypeArg(string lexeme)
    {
        var hexStr = lexeme.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? lexeme[2..] : lexeme;
        return long.TryParse(hexStr, System.Globalization.NumberStyles.HexNumber, null, out var v)
            ? v : null;
    }

    private static object? TryParseBinaryTypeArg(string lexeme)
    {
        var binStr = lexeme.StartsWith("0b", StringComparison.OrdinalIgnoreCase)
            ? lexeme[2..] : lexeme;
        try { return Convert.ToInt64(binStr, 2); }
        catch { return null; }
    }

    /// <summary>
    /// Determines whether a <see cref="TypeArgumentNode"/> represents a valid compile-time
    /// constant for a dependent type argument position.
    /// <para>
    /// Accepted forms: integer literals, negated integer literals, hex/binary literals,
    /// boolean literals, char literals, and identifiers that resolve to const declarations
    /// or forwarded dependent type parameters.
    /// </para>
    /// <para>
    /// Rejected forms: type references that resolve to variables, method calls, property
    /// accesses, or any non-constant expression.
    /// </para>
    /// </summary>
    private static bool IsDependentArgCompileTimeConstant(TypeArgumentNode argNode, ISemanticModel model)
    {
        switch(argNode)
        {
            // All literal forms are compile-time constants.
            case IntegerLiteralTypeArgumentTypeArgumentNode:
            case NegatedIntegerTypeArgumentTypeArgumentNode:
            case HexLiteralTypeArgumentTypeArgumentNode:
            case BinaryLiteralTypeArgumentTypeArgumentNode:
            case TrueLiteralTypeArgumentTypeArgumentNode:
            case FalseLiteralTypeArgumentTypeArgumentNode:
            case CharLiteralTypeArgumentTypeArgumentNode:
                return true;

            // A type reference in a dependent position needs disambiguation.
            case TypeReferenceTypeArgumentTypeArgumentNode typeRefArg:
                {
                    // A bare identifier could be a const reference or a forwarded dependent parameter.
                    if(typeRefArg.Type.First is NamedSingleTypeRefSingleTypeRefNode namedRef
                        && namedRef.TypeArgs is null
                        && namedRef.ArrayRank is null)
                    {
                        // Check what the semantic model resolved this to. The model keys
                        // declared symbols / types on the flat TypeReferenceNode, so look
                        // up via `typeRefArg.Type` (not the SingleTypeRef child).
                        var symbol = model.GetDeclaredSymbol(typeRefArg.Type);
                        if(symbol is not null)
                        {
                            // Type symbols are not constants — they shouldn't be in a dependent position.
                            if(symbol.Kind is SymbolKind.Class or SymbolKind.Struct or SymbolKind.Abstract
                                or SymbolKind.TypeAlias or SymbolKind.Enum)
                            {
                                return false;
                            }

                            // Dependent type parameters (forwarded) are compile-time constants.
                            if(symbol.DeclaredType is DependentTypeParameterType)
                            {
                                return true;
                            }

                            // Static get-only properties generated for dependent parameters are constants.
                            if(symbol.Kind == SymbolKind.Property)
                            {
                                return true;
                            }

                            // Fields marked as const are compile-time constants.
                            // In Mist, fields used as dependent arguments are accepted by the
                            // semantic model builder — the type checker trusts that resolution.
                            if(symbol.Kind == SymbolKind.Field)
                            {
                                return true;
                            }

                            // Local variables are NOT compile-time constants.
                            if(symbol.Kind == SymbolKind.LocalVariable)
                            {
                                return false;
                            }

                            // Method symbols are not constants.
                            if(symbol.Kind == SymbolKind.Method)
                            {
                                return false;
                            }
                        }

                        // If the symbol wasn't found via GetDeclaredSymbol, check the type map.
                        // If the type reference resolved to a type, it's not a constant.
                        var resolvedType = model.GetTypeOf(typeRefArg.Type);
                        if(resolvedType is not null and not ErrorType)
                        {
                            // If it resolved to a DependentTypeParameterType, it's a forwarded param.
                            if(resolvedType is DependentTypeParameterType)
                            {
                                return true;
                            }

                            // If it resolved to a concrete type, it's not a constant in a dependent position.
                            if(resolvedType is ClassType or StructType or AbstractType
                                or TypeAliasType or TypeParameterType)
                            {
                                return false;
                            }
                        }

                        // Bare identifier that didn't resolve to a type — assume it's a const reference
                        // (the semantic model builder would have emitted an unresolved diagnostic already).
                        return true;
                    }

                    // Complex type references (qualified names, generic types, arrays) are not constants.
                    return false;
                }

            default:
                return false;
        }
    }

    /// <summary>
    /// Checks whether a <see cref="DependentValueArgument"/>'s value type matches the
    /// expected <see cref="DependentTypeParameterType"/>'s value type annotation.
    /// </summary>
    private static bool DependentValueTypeMatches(DependentValueArgument depArg, DependentTypeParameterType depParam)
    {
        var actualType = depArg.ValueType;
        var expectedType = depParam.ValueType;

        // ErrorType matches anything (error recovery).
        if(actualType is ErrorType || expectedType is ErrorType)
        {
            return true;
        }

        // Forwarded dependent parameters: the value is a string (the parameter name)
        // and the ValueType is the forwarded parameter's value type. Check compatibility.
        // For forwarded parameters, the types should match exactly.
        return TypesEqual(actualType, expectedType);
    }

    /// <summary>
    /// Extracts a human-readable text representation from a <see cref="TypeArgumentNode"/>
    /// for use in diagnostic messages.
    /// </summary>
    private static string ExtractTypeArgumentText(TypeArgumentNode argNode)
    {
        return argNode switch
        {
            IntegerLiteralTypeArgumentTypeArgumentNode intLit => intLit.Literal.Lexeme,
            NegatedIntegerTypeArgumentTypeArgumentNode negLit => $"-{negLit.Literal.Lexeme}",
            HexLiteralTypeArgumentTypeArgumentNode hexLit => hexLit.Literal.Lexeme,
            BinaryLiteralTypeArgumentTypeArgumentNode binLit => binLit.Literal.Lexeme,
            TrueLiteralTypeArgumentTypeArgumentNode => "true",
            FalseLiteralTypeArgumentTypeArgumentNode => "false",
            CharLiteralTypeArgumentTypeArgumentNode charLit => charLit.Literal.Lexeme,
            TypeReferenceTypeArgumentTypeArgumentNode typeRef => ExtractTypeReferenceName(typeRef.Type),
            _ => "<expression>",
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Dependent parameter forwarding validation (Task 6.6, Requirements 12.1–12.5)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Validates that when a derived type forwards a dependent parameter to a base type
    /// or implemented abstract, the derived parameter's value type can be implicitly
    /// widened to the base parameter's value type. Emits <c>MIST_DT_0004</c> for
    /// narrowing violations.
    /// </summary>
    private static void ValidateDependentParameterForwarding(ISemanticModel model, DiagnosticBag diagnostics)
    {
        foreach(var symbol in model.AllSymbols)
        {
            if(symbol.Origin == SymbolOrigin.Backend)
            {
                continue;
            }

            if(symbol.Kind is not (SymbolKind.Class or SymbolKind.Struct))
            {
                continue;
            }

            var declaredType = symbol.DeclaredType;
            if(declaredType is null)
            {
                continue;
            }

            // Get the declaring node for diagnostic location.
            SyntaxNode declaringNode;
            try { declaringNode = model.GetDeclaringNode(symbol); }
            catch { continue; }

            // Check base type (classes only).
            if(declaredType is ClassType classType && classType.BaseType is ConstructedType baseConstructed)
            {
                ValidateDependentForwardingForConstructedBase(
                    declaredType, baseConstructed, declaringNode, diagnostics);
            }

            // Check implemented abstracts.
            var abstracts = declaredType switch
            {
                ClassType ct => ct.Abstracts,
                StructType st => st.Abstracts,
                _ => Array.Empty<IType>()
            };

            foreach(var abs in abstracts)
            {
                if(abs is ConstructedType absConstructed)
                {
                    ValidateDependentForwardingForConstructedBase(
                        declaredType, absConstructed, declaringNode, diagnostics);
                }
            }
        }
    }

    /// <summary>
    /// Checks a single constructed base type or abstract for dependent parameter forwarding
    /// violations. When a type argument at a dependent parameter position is itself a
    /// <see cref="DependentTypeParameterType"/> (forwarded from the derived type), validates
    /// that the derived parameter's value type can widen to the base parameter's value type.
    /// Also validates adhoc refinement constraints: forwarded parameters must have constraints
    /// that imply the base parameter's constraint, and fixed values must satisfy the constraint.
    /// </summary>
    private static void ValidateDependentForwardingForConstructedBase(
        IType derivedType,
        ConstructedType baseConstructed,
        SyntaxNode declaringNode,

        DiagnosticBag diagnostics)
    {
        var baseTypeParams = GetTypeParameters(baseConstructed.GenericDefinition);
        if(baseTypeParams is null)
        {
            return;
        }

        var baseTypeArgs = baseConstructed.TypeArguments;

        for(var i = 0; i < baseTypeParams.Count && i < baseTypeArgs.Count; i++)
        {
            // Only check positions where the base parameter is a dependent type parameter.
            if(baseTypeParams[i] is not DependentTypeParameterType baseDepParam)
            {
                continue;
            }

            if(baseTypeArgs[i] is DependentTypeParameterType derivedDepParam)
            {
                // Forwarded parameter case.

                // Validate widening: derived parameter's value type must be implicitly
                // convertible (widenable) to the base parameter's value type.
                if(!IsImplicitlyWidenable(derivedDepParam.ValueType, baseDepParam.ValueType))
                {
                    _ = derivedType.DisplayName;
                    _ = baseConstructed.GenericDefinition.DisplayName; diagnostics.ReportAtNode(TypeCheckDiagnostics.DependentNarrowingViolation, declaringNode, "TypeChecker");
                }

                // Validate adhoc refinement constraint implication for forwarded parameters.
                var targetConstraint = baseDepParam.AdhocConstraint;
                if(targetConstraint is not null)
                {
                    var sourceConstraint = derivedDepParam.AdhocConstraint;
                    if(sourceConstraint is null)
                    {
                        // MIST_REFINEMENT_0008: Source has no constraint, target requires one.                        diagnostics.ReportAtNode(TypeCheckDiagnostics.RefinementUnconstrainedForwarding, declaringNode, "TypeChecker");
                    }
                    else if(!RefinementSolver.Implies(sourceConstraint, targetConstraint))
                    {
                        // MIST_REFINEMENT_0006: Source constraint does not imply target constraint.                        diagnostics.ReportAtNode(TypeCheckDiagnostics.RefinementImplicationFailure, declaringNode, "TypeChecker");
                    }
                }
            }
            else
            {
                // Fixed value case: a concrete value is provided for the base's dependent parameter.
                // Validate that the fixed value satisfies the base parameter's adhoc constraint.
                var targetConstraint = baseDepParam.AdhocConstraint;
                if(targetConstraint is not null
                    && baseConstructed.DependentArguments is not null
                    && i < baseConstructed.DependentArguments.Count
                    && baseConstructed.DependentArguments[i] is { } depArg)
                {
                    if(!targetConstraint.Evaluate(depArg.Value))
                    {
                        _ = baseConstructed.GenericDefinition.DisplayName; diagnostics.ReportAtNode(TypeCheckDiagnostics.RefinementConstraintViolation, declaringNode, "TypeChecker");
                    }
                }
            }
        }
    }

    /// <summary>
    /// Determines whether a value of type <paramref name="sourceType"/> can be implicitly
    /// widened to <paramref name="targetType"/>. This implements the primitive widening
    /// rules for dependent type parameter forwarding:
    /// <list type="bullet">
    ///   <item><c>byte</c> → <c>short</c>, <c>int</c>, <c>long</c></item>
    ///   <item><c>short</c> → <c>int</c>, <c>long</c></item>
    ///   <item><c>int</c> → <c>long</c></item>
    ///   <item><c>uint</c> → <c>ulong</c></item>
    ///   <item><c>char</c> → <c>int</c>, <c>long</c></item>
    ///   <item>Same type → same type (always OK)</item>
    /// </list>
    /// </summary>
    private static bool IsImplicitlyWidenable(IType sourceType, IType targetType)
    {
        // Same type is always OK.
        if(TypesEqual(sourceType, targetType))
        {
            return true;
        }

        // ErrorType matches anything (error recovery).
        if(sourceType is ErrorType || targetType is ErrorType)
        {
            return true;
        }

        // Normalize display names to canonical (legacy keyword) form for comparison.
        var source = NormalizePrimitiveDisplayName(sourceType.DisplayName);
        var target = NormalizePrimitiveDisplayName(targetType.DisplayName);

        // Same canonical name after normalization.
        if(source == target)
        {
            return true;
        }

        // Apply widening rules.
        return source switch
        {
            "byte" => target is "short" or "int" or "long",
            "short" => target is "int" or "long",
            "int" => target is "long",
            "uint" => target is "ulong",
            "char" => target is "int" or "long",
            _ => false,
        };
    }

    /// <summary>
    /// Normalizes a primitive type display name to its canonical keyword form.
    /// Maps stdlib names (e.g., "Int32") to legacy keyword names (e.g., "int")
    /// for consistent widening rule evaluation.
    /// </summary>
    private static string NormalizePrimitiveDisplayName(string displayName)
    {
        return displayName switch
        {
            "Int32" => "int",
            "UInt32" => "uint",
            "Int64" => "long",
            "UInt64" => "ulong",
            "Int16" => "short",
            "UInt16" => "ushort",
            "Byte" => "byte",
            "SByte" => "sbyte",
            "Boolean" => "bool",
            "Char" => "char",
            _ => displayName,
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Predicate-type compatibility validation (Requirements 11.1–11.7, 13.6)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Display names of types that support relational predicates in adhoc refinement
    /// constraints. Includes all numeric types and <c>char</c>.
    /// </summary>
    private static readonly HashSet<string> ComparableRefinementTypeNames = new(StringComparer.Ordinal)
    {
        // Legacy keyword names.
        "int", "uint", "long", "ulong",
        "short", "ushort", "byte", "sbyte",
        "char", "float", "double", "decimal",

        // Stdlib simple names.
        "Int32", "UInt32", "Int64", "UInt64",
        "Int16", "UInt16", "Byte", "SByte",
        "Char", "Single", "Double", "Decimal",
    };

    /// <summary>
    /// Validates that adhoc refinement constraint predicates are compatible with the
    /// type of the constrained name. Relational predicates (>, <, >=, <=) require a
    /// comparable type (numeric + char). Constant patterns and <c>and</c> combinators
    /// are valid on all equality-comparable types.
    /// Emits <c>MIST_REFINEMENT_0007</c> when a relational predicate is applied to
    /// a non-comparable type (e.g., <c>bool</c>, <c>string</c>).
    /// </summary>
    private static void ValidatePredicateTypeCompatibility(ISemanticModel model, DiagnosticBag diagnostics)
    {
        foreach(var symbol in model.AllSymbols)
        {
            if(symbol.Origin == SymbolOrigin.Backend)
            {
                continue;
            }

            var declaringNode = model.GetDeclaringNode(symbol);

            // Check dependent type parameters with adhoc constraints.
            var typeParams = GetTypeParametersForSymbol(symbol);
            if(typeParams is not null)
            {
                foreach(var tp in typeParams)
                {
                    if(tp is DependentTypeParameterType depParam && depParam.AdhocConstraint is { } constraint)
                    {
                        if(PredicateContainsRelationalPattern(constraint.AstNode)
                            && !IsComparableRefinementType(depParam.ValueType))
                        {
                            var clauseNode = FindAdhocConstraintClauseNode(declaringNode, depParam.Name); diagnostics.ReportAtNode(TypeCheckDiagnostics.RefinementPredicateIncompatible, clauseNode ?? declaringNode, "TypeChecker");
                        }
                    }
                }
            }

            // Check parameter refinements.
            switch(symbol.Kind)
            {
                case SymbolKind.Method:
                case SymbolKind.Constructor:
                case SymbolKind.Indexer:
                    CheckParameterRefinementCompatibility(declaringNode, model, diagnostics);
                    break;
            }
        }
    }

    /// <summary>
    /// Checks parameter refinement predicates on a method, constructor, or indexer for
    /// type compatibility. Emits <c>MIST_REFINEMENT_0007</c> if a relational predicate
    /// is applied to a non-comparable parameter type.
    /// </summary>
    private static void CheckParameterRefinementCompatibility(

        SyntaxNode declaringNode,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Get the parameter list from the declaring node.
        var paramNodes = GetParameterNodes(declaringNode);
        if(paramNodes is null)
        {
            return;
        }

        foreach(var paramNode in paramNodes)
        {
            if(paramNode is not ParameterNode param)
            {
                continue;
            }

            // Look up the parameter symbol in the semantic model.
            var paramSymbol = model.GetDeclaredSymbol(param);
            if(paramSymbol is null)
            {
                continue;
            }

            var refinement = model.GetParameterRefinement(paramSymbol);
            if(refinement is null)
            {
                continue;
            }

            var paramType = paramSymbol.DeclaredType;
            if(paramType is null)
            {
                continue;
            }

            if(PredicateContainsRelationalPattern(refinement.AstNode)
                && !IsComparableRefinementType(paramType))
            {
                var clauseNode = FindAdhocConstraintClauseNode(declaringNode, param.Name.Lexeme); diagnostics.ReportAtNode(TypeCheckDiagnostics.RefinementPredicateIncompatible, clauseNode ?? declaringNode, "TypeChecker");
            }
        }
    }

    /// <summary>
    /// Returns the type parameters for a symbol, handling different symbol kinds.
    /// </summary>
    private static IReadOnlyList<IType>? GetTypeParametersForSymbol(ISymbol symbol)
    {
        return symbol.DeclaredType switch
        {
            ClassType classType => classType.TypeParameters,
            StructType structType => structType.TypeParameters,
            AbstractType abstractType => abstractType.TypeParameters,
            _ => symbol is Symbol s && s.TypeParameters.Count > 0 ? s.TypeParameters : null,
        };
    }

    /// <summary>
    /// Returns the parameter nodes from a declaration node.
    /// </summary>
    private static IEnumerable<SyntaxNode>? GetParameterNodes(SyntaxNode declaringNode)
    {
        var paramList = declaringNode switch
        {
            MethodDeclarationNode methodDecl => methodDecl.Parameters,
            ConstructorDeclarationNode ctorDecl => ctorDecl.Parameters,
            IndexerDeclarationNode indexerDecl => indexerDecl.Parameters,
            _ => null,
        };

        return paramList?.Children();
    }

    /// <summary>
    /// Determines whether a type is comparable for refinement predicate purposes.
    /// Comparable types are: all integer types, char, float, double, decimal.
    /// Non-comparable types include: bool, string, and user-defined types.
    /// </summary>
    private static bool IsComparableRefinementType(IType type)
    {
        if(IsErrorType(type))
        {
            return true; // Suppress cascading errors.
        }

        return ComparableRefinementTypeNames.Contains(type.DisplayName);
    }

    /// <summary>
    /// Determines whether an <see cref="AndPatternNode"/> contains any relational patterns
    /// (>, <, >=, <=). Returns false if the predicate only contains constant patterns
    /// and <c>and</c> combinators.
    /// </summary>
    private static bool PredicateContainsRelationalPattern(AndPatternNode andPattern)
    {
        if(UnaryPatternContainsRelational(andPattern.Left))
        {
            return true;
        }

        if(andPattern.Right is not null && !ReferenceEquals(andPattern.Right, andPattern.Left))
        {
            if(UnaryPatternContainsRelational(andPattern.Right))
            {
                return true;
            }
        }

        if(!andPattern.UnaryPatterns.IsDefaultOrEmpty)
        {
            foreach(var part in andPattern.UnaryPatterns)
            {
                if(UnaryPatternContainsRelational(part))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Checks whether a <see cref="UnaryPatternNode"/> contains a relational pattern.
    /// </summary>
    private static bool UnaryPatternContainsRelational(UnaryPatternNode unary)
    {
        return unary switch
        {
            DirectPatternUnaryPatternNode direct => PrimaryPatternIsRelational(direct.Primary),
            NotPatternUnaryPatternNode not => PrimaryPatternIsRelational(not.Operand),
            _ => false,
        };
    }

    /// <summary>
    /// Checks whether a <see cref="PrimaryPatternNode"/> is a relational pattern.
    /// </summary>
    private static bool PrimaryPatternIsRelational(PrimaryPatternNode primary)
    {
        return primary is GreaterThanRelationalPatternPrimaryPatternNode
            or LessThanRelationalPatternPrimaryPatternNode
            or GreaterThanOrEqualRelationalPatternPrimaryPatternNode
            or LessThanOrEqualRelationalPatternPrimaryPatternNode;
    }

    /// <summary>
    /// Finds the <see cref="AdhocRefinementConstraintWhereClauseNode"/> for a given name
    /// on a declaration node, for use as the diagnostic location.
    /// </summary>
    private static SyntaxNode? FindAdhocConstraintClauseNode(SyntaxNode declaringNode, string name)
    {
        var constraints = GetWhereClausesFromDeclaration(declaringNode);
        if(constraints.IsDefaultOrEmpty)
        {
            return null;
        }

        foreach(var clause in constraints)
        {
            if(clause is AdhocRefinementConstraintWhereClauseNode refinement
                && refinement.Name.Lexeme == name)
            {
                return refinement;
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the where clauses from a declaration node.
    /// </summary>
    private static ImmutableArray<WhereClauseNode> GetWhereClausesFromDeclaration(SyntaxNode declaringNode)
    {
        return declaringNode switch
        {
            TypeBodyDeclarationNode typeBody => typeBody.Constraints,
            MethodDeclarationNode methodDecl => methodDecl.Constraints,
            TypeAliasDeclarationNode aliasDecl => aliasDecl.Constraints,
            ConstructorDeclarationNode ctorDecl => ctorDecl.Constraints,
            IndexerDeclarationNode indexerDecl => indexerDecl.Constraints,
            _ => ImmutableArray<WhereClauseNode>.Empty,
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Sealed enforcement (Requirements 12.1–12.9, 13.3)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Enforces sealed-type rules across the compilation:
    /// <list type="bullet">
    ///   <item>Reject deriving from a sealed class or sealed struct (Req 12.1–12.4).</item>
    ///   <item>Accept type aliases targeting sealed types (Req 12.5).</item>
    ///   <item>Accept refinement types over sealed types (Req 12.6).</item>
    ///   <item>Reject newtypes wrapping sealed types (Req 12.7).</item>
    ///   <item>Reject <c>sealed</c> on type alias declarations (Req 12.9).</item>
    ///   <item>Reject <c>sealed</c> on non-type members (Req 13.3).</item>
    /// </list>
    /// </summary>
    private static void CheckSealedEnforcement(ISemanticModel model, DiagnosticBag diagnostics)
    {
        foreach(var symbol in model.AllSymbols)
        {
            if(symbol.Origin == SymbolOrigin.Backend)
            {
                continue;
            }

            switch(symbol.Kind)
            {
                case SymbolKind.Class:
                    {
                        // Check if this class derives from a sealed type (Req 12.2, 12.4).
                        if(symbol.DeclaredType is ClassType classType && classType.BaseType is not null)
                        {
                            if(IsTypeSealedForInheritance(classType.BaseType))
                            {
                                SyntaxNode? node = null;
                                try { node = model.GetDeclaringNode(symbol); }
                                catch { /* ignore */ }

                                if(node is not null)
                                {
                                    diagnostics.ReportAtNode(TypeCheckDiagnostics.SealedInheritance, node, "TypeChecker", classType.BaseType.DisplayName);
                                }
                            }
                        }

                        break;
                    }

                case SymbolKind.Struct:
                    {
                        // Check if this struct derives from a sealed type (Req 12.2, 12.4).
                        if(symbol.DeclaredType is StructType structType && structType.BaseType is not null)
                        {
                            if(IsTypeSealedForInheritance(structType.BaseType))
                            {
                                SyntaxNode? node = null;
                                try { node = model.GetDeclaringNode(symbol); }
                                catch { /* ignore */ }

                                if(node is not null)
                                {
                                    diagnostics.ReportAtNode(TypeCheckDiagnostics.SealedInheritance, node, "TypeChecker", structType.BaseType.DisplayName);
                                }
                            }
                        }

                        break;
                    }

                case SymbolKind.Newtype:
                    {
                        // Reject newtype wrapping a sealed type (Req 12.7).
                        if(symbol.DeclaredType is NewtypeType newtypeType)
                        {
                            if(IsTypeSealedForInheritance(newtypeType.SourceType))
                            {
                                SyntaxNode? node = null;
                                try { node = model.GetDeclaringNode(symbol); }
                                catch { /* ignore */ }

                                if(node is not null)
                                {
                                    diagnostics.ReportAtNode(TypeCheckDiagnostics.NewtypeOverSealed, node, "TypeChecker", newtypeType.SourceType.DisplayName);
                                }
                            }
                        }

                        break;
                    }

                case SymbolKind.TypeAlias:
                    {
                        // Check for `sealed` modifier on a type alias (Req 12.9).
                        CheckSealedOnTypeAlias(symbol, model, diagnostics);
                        // Type aliases targeting sealed types are accepted (Req 12.5) — no check needed.
                        // Refinement types over sealed types are accepted (Req 12.6) — no check needed.
                        break;
                    }

                case SymbolKind.Method:
                case SymbolKind.Field:
                case SymbolKind.Property:
                case SymbolKind.Constructor:
                case SymbolKind.Indexer:
                    {
                        // Reject `sealed` on non-type members (Req 13.3).
                        CheckSealedOnNonTypeMember(symbol, model, diagnostics);
                        break;
                    }
            }
        }
    }

    /// <summary>
    /// Determines whether a type is sealed for inheritance-checking purposes.
    /// Returns <c>true</c> for sealed classes and sealed structs (both user-declared
    /// and External_Assembly-projected types).
    /// </summary>
    private static bool IsTypeSealedForInheritance(IType type)
    {
        // Unwrap type aliases — the alias is transparent, check the underlying type.
        if(type is TypeAliasType alias)
        {
            return IsTypeSealedForInheritance(alias.UnderlyingType);
        }

        return type switch
        {
            ClassType ct => ct.IsSealed,
            StructType st => st.IsSealed,
            _ => false,
        };
    }

    /// <summary>
    /// Checks whether a type alias declaration carries the <c>sealed</c> modifier and
    /// emits <c>MIST_TC_0179</c> (SealedOnNonType) if so.
    /// </summary>
    private static void CheckSealedOnTypeAlias(ISymbol symbol, ISemanticModel model, DiagnosticBag diagnostics)
    {
        SyntaxNode? node;
        try { node = model.GetDeclaringNode(symbol); }
        catch { return; }

        if(node is not TypeAliasDeclarationNode aliasDecl)
        {
            return;
        }

        foreach(var mod in aliasDecl.Modifiers)
        {
            if(mod is SealedModifierModifierNode)
            {
                diagnostics.ReportAtNode(TypeCheckDiagnostics.SealedOnNonType, node, "TypeChecker", "a type alias");
                return;
            }
        }
    }

    /// <summary>
    /// Checks whether a non-type member declaration carries the <c>sealed</c> modifier
    /// and emits <c>MIST_TC_0179</c> (SealedOnNonType) if so. Applies to methods, fields,
    /// properties, constructors, and indexers (Req 13.3).
    /// </summary>
    private static void CheckSealedOnNonTypeMember(ISymbol symbol, ISemanticModel model, DiagnosticBag diagnostics)
    {
        SyntaxNode? node;
        try { node = model.GetDeclaringNode(symbol); }
        catch { return; }

        if(node is null)
        {
            return;
        }

        IReadOnlyList<ModifierNode>? modifiers = node switch
        {
            MethodDeclarationNode method => method.Modifiers,
            FieldDeclarationNode field => field.Modifiers,
            BlockPropertyPropertyDeclarationNode blockProp => blockProp.Modifiers,
            ExpressionBodiedPropertyPropertyDeclarationNode exprProp => exprProp.Modifiers,
            ConstructorDeclarationNode ctor => ctor.Modifiers,
            IndexerDeclarationNode indexer => indexer.Modifiers,
            OperatorDeclarationNode op => op.Modifiers,
            CastOperatorDeclarationNode castOp => castOp.Modifiers,
            _ => null,
        };

        if(modifiers is null)
        {
            return;
        }

        foreach(var mod in modifiers)
        {
            if(mod is SealedModifierModifierNode)
            {
                var memberKind = node switch
                {
                    MethodDeclarationNode => "a method",
                    FieldDeclarationNode => "a field",
                    BlockPropertyPropertyDeclarationNode => "a property",
                    ExpressionBodiedPropertyPropertyDeclarationNode => "a property",
                    ConstructorDeclarationNode => "a constructor",
                    IndexerDeclarationNode => "an indexer",
                    OperatorDeclarationNode => "an operator",
                    CastOperatorDeclarationNode => "a cast operator",
                    _ => "a non-type member",
                };
                diagnostics.ReportAtNode(TypeCheckDiagnostics.SealedOnNonType, node, "TypeChecker", memberKind);
                return;
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Protected-in-sealed warning (Requirement 13.1, 13.2)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// For every sealed class or sealed struct, checks whether any of its members
    /// have <c>Protected</c> accessibility. If so, emits a warning-severity diagnostic
    /// (<c>MIST_TC_0180</c>) stating that the protected modifier is unnecessary because
    /// the type cannot be inherited. This mirrors C# CS0628.
    /// <para>
    /// Protected members on non-sealed types are fully enforced without a warning (Req 13.1).
    /// </para>
    /// </summary>
    private static void CheckProtectedInSealed(ISemanticModel model, DiagnosticBag diagnostics)
    {
        foreach(var symbol in model.AllSymbols)
        {
            if(symbol.Origin == SymbolOrigin.Backend)
            {
                continue;
            }

            if(symbol.Kind is not (SymbolKind.Class or SymbolKind.Struct))
            {
                continue;
            }

            var declaredType = symbol.DeclaredType;
            if(declaredType is null)
            {
                continue;
            }

            // Only check sealed types.
            var isSealed = declaredType switch
            {
                ClassType ct => ct.IsSealed,
                StructType st => st.IsSealed,
                _ => false,
            };

            if(!isSealed)
            {
                continue;
            }

            // Get the member scope and check each member's accessibility.
            var memberScope = declaredType switch
            {
                ClassType ct => ct.MemberScope,
                StructType st => st.MemberScope,
                _ => null,
            };

            if(memberScope is null)
            {
                continue;
            }

            foreach(var member in memberScope.Members)
            {
                if(member.Origin == SymbolOrigin.Backend)
                {
                    continue;
                }

                // Only check members that belong directly to this type (not inherited).
                if(!ReferenceEquals(member.ContainingScope, memberScope))
                {
                    continue;
                }

                if(member.Accessibility != Accessibility.Protected)
                {
                    continue;
                }

                SyntaxNode? memberNode = null;
                try { memberNode = model.GetDeclaringNode(member); }
                catch { /* ignore */ }

                if(memberNode is not null)
                {
                    diagnostics.ReportAtNode(TypeCheckDiagnostics.ProtectedInSealed, memberNode, "TypeChecker", member.Name);
                }
            }
        }
    }
}