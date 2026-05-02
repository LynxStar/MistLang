using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model.Types;

namespace Mist.Semantics.Model;

/// <summary>
/// Builds an immutable <see cref="SemanticModel"/> from a set of ASTs.
/// Walks ASTs to build symbols, scopes, and types, then freezes all mutable
/// builders before returning the immutable model.
/// </summary>
public sealed class SemanticModelBuilder : ISemanticModelBuilder
{
    // ── Overloading diagnostic IDs ───────────────────────────────────────
    internal static readonly DiagnosticId DuplicateMethodSignature = new("MIST_SM_0020");
    internal static readonly DiagnosticId DuplicateConstructorSignature = new("MIST_SM_0021");
    internal static readonly DiagnosticId MissingInterfaceOverload = new("MIST_SM_0022");

    // ── Intrinsic-marker diagnostic IDs (Task 9, Requirements 5.3, 5.6, 5.13, 5.15) ──
    /// <summary>
    /// An intrinsic member was declared with a body. Per Requirement 5.3,
    /// intrinsic members must terminate with <c>;</c> because their body is
    /// supplied by the backend's <c>IntrinsicEmissionTable</c>.
    /// </summary>
    internal static readonly DiagnosticId IntrinsicMemberHasBody = new("MIST_SB_0030");

    /// <summary>
    /// The <c>intrinsic</c> modifier (at either type or member level) was
    /// used in a non-stdlib compilation unit. Per Requirements 5.6 and 5.13,
    /// the modifier is reserved for the Standard Library.
    /// </summary>
    internal static readonly DiagnosticId IntrinsicReservedForStdlib = new("MIST_SB_0031");

    /// <summary>
    /// A member-level <c>intrinsic</c> appeared inside a type that does not
    /// itself carry the type-level <c>intrinsic</c> marker. Per Requirement
    /// 5.15, intrinsic members are always hosted on Backend_Backed_Types.
    /// </summary>
    internal static readonly DiagnosticId MemberIntrinsicRequiresTypeIntrinsic = new("MIST_SB_0032");

    // ── Operator-shape diagnostic IDs (Task 12, Requirements 4.2, 4.7, 4.8, 4.9) ──
    /// <summary>
    /// An <c>operator</c> declaration appeared outside a class or struct body
    /// (Requirement 4.7). NOTE: the grammar already forbids operator
    /// declarations outside a class/struct body (see <c>MemberDeclaration</c>
    /// rule in <c>Mist.storm</c>), so this diagnostic is a defensive
    /// safety net that currently has no triggerable path from parsed source.
    /// It is registered here so the diagnostic ID is allocated and the
    /// shape-validation logic would cover a future grammar change that
    /// admitted top-level operator declarations.
    /// </summary>
    internal static readonly DiagnosticId OperatorOutsideTypeBody = new("MIST_SB_0033");

    /// <summary>
    /// An <c>operator</c> declaration's parameter count does not match the
    /// arity required by its operator symbol (Requirements 4.8 &amp; 4.9).
    /// Binary operators require exactly 2 parameters; unary operators require
    /// exactly 1. The <c>-</c> symbol is overloaded binary/unary; its arity
    /// is disambiguated by declared parameter count (2 = binary, 1 = unary;
    /// anything else selects the closest-fitting error message).
    /// </summary>
    internal static readonly DiagnosticId OperatorArityMismatch = new("MIST_SB_0034");

    /// <summary>
    /// An <c>operator</c> declaration is missing the required
    /// <c>public static</c> modifier combination (Requirement 4.2).
    /// Every operator must be declared <c>public static</c> in v1; any
    /// deviation (missing either modifier, or adding <c>private</c>) fires
    /// this diagnostic.
    /// </summary>
    internal static readonly DiagnosticId OperatorMustBePublicStatic = new("MIST_SB_0035");

    // ── Cast-operator-shape diagnostic IDs (Task 3.2, Requirements 6.1, 6.3, 6.4, 6.5) ──

    /// <summary>
    /// A cast operator declaration has a parameter count other than 1
    /// (Requirement 6.3, 6.4). Cast operators must have exactly one parameter.
    /// </summary>
    internal static readonly DiagnosticId CastOperatorArityMismatch = new("MIST_SB_0040");

    /// <summary>
    /// A cast operator declaration is missing the required
    /// <c>public static</c> modifier combination (Requirement 6.1).
    /// </summary>
    internal static readonly DiagnosticId CastOperatorMustBePublicStatic = new("MIST_SB_0041");

    /// <summary>
    /// A cast operator declaration appeared outside a class or struct body
    /// (Requirement 6.1). Like <see cref="OperatorOutsideTypeBody"/>, this
    /// is currently unreachable from parsed source because the grammar
    /// restricts cast operator declarations to <c>MemberDeclaration</c>.
    /// Registered defensively.
    /// </summary>
    internal static readonly DiagnosticId CastOperatorOutsideTypeBody = new("MIST_SB_0042");

    /// <summary>
    /// Two cast operators in the same type have the same kind (both implicit
    /// or both explicit) and the same target type name (Requirement 6.5).
    /// This is a name-based duplicate check; full type-based duplicate
    /// detection is deferred to the type checker.
    /// </summary>
    internal static readonly DiagnosticId DuplicateCastOperator = new("MIST_SB_0043");

    // ── Trait with-block diagnostic IDs (Task 4.1, Requirements 6.1–6.9) ──

    /// <summary>
    /// The type name in a <c>with</c> block could not be resolved to a
    /// declared class or struct (Requirement 6.2).
    /// </summary>
    internal static readonly DiagnosticId TraitWithUndefinedType = new("MIST_TRAIT_WITH_UNDEFINED_TYPE");

    /// <summary>
    /// The trait name in a <c>with</c> block could not be resolved to a
    /// declared trait (Requirement 6.3).
    /// </summary>
    internal static readonly DiagnosticId TraitWithUndefinedTrait = new("MIST_TRAIT_WITH_UNDEFINED_TRAIT");

    /// <summary>
    /// A duplicate <c>with</c> block was found for the same type-trait pair
    /// (Requirement 6.9).
    /// </summary>
    internal static readonly DiagnosticId TraitDuplicateWith = new("MIST_TRAIT_DUPLICATE_WITH");

    /// <summary>
    /// A member mapping in a <c>with</c> block references a source member
    /// that does not exist as a public member on the type (Requirement 6.4).
    /// </summary>
    internal static readonly DiagnosticId TraitWithUndefinedSource = new("MIST_TRAIT_WITH_UNDEFINED_SOURCE");

    /// <summary>
    /// A member mapping in a <c>with</c> block references a target member
    /// that does not exist in the trait (Requirement 6.5).
    /// </summary>
    internal static readonly DiagnosticId TraitWithUndefinedTarget = new("MIST_TRAIT_WITH_UNDEFINED_TARGET");

    /// <summary>
    /// A member mapping in a <c>with</c> block maps a source member to a
    /// target trait member with an incompatible type signature (Requirement 6.6).
    /// </summary>
    internal static readonly DiagnosticId TraitWithTypeMismatch = new("MIST_TRAIT_WITH_TYPE_MISMATCH");

    // ── Trait satisfaction diagnostic IDs (Task 5.1, Requirements 5.1–5.7, 6.7–6.8) ──

    /// <summary>
    /// A private member on a type matches a trait member by name, but trait
    /// matching requires public visibility (Requirement 5.7).
    /// </summary>
    internal static readonly DiagnosticId TraitMemberVisibility = new("MIST_TRAIT_MEMBER_VISIBILITY");

    /// <summary>
    /// A <c>with</c> block exists for a type-trait pair but does not cover all
    /// trait members that are not structurally matched (Requirement 6.8).
    /// </summary>
    internal static readonly DiagnosticId TraitWithUnsatisfied = new("MIST_TRAIT_WITH_UNSATISFIED");

    public ISemanticModel Build(
        MistCompilation compilation,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        IAmbientTypeResolver? ambientTypeResolver,
        string namespaceRoot,
        string projectFolder,
        DiagnosticBag diagnostics,
        ImmutableHashSet<string>? directReferencePaths = null)
    {
        var globalScope = new Scope(parent: null);
        var transitiveScope = new Scope(parent: null);
        var symbolTable = new SymbolTable();

        // (Task 49: PrimitiveTypeRegistry and PrimitiveType have been
        // deleted. No migration-window registry reset is needed.)

        // Pass 1: Register user-defined types.
        // Walk each AST to discover namespaces, types, and members.
        // Units from transitive-only references are registered in a separate
        // scope that is not searched during name resolution for the user
        // project, enforcing transitive visibility rules (Requirements 6.1,
        // 6.2, 6.4).
        var withDeclarations = new List<WithDeclarationNode>();
        for (var i = 0; i < asts.Length; i++)
        {
            var ast = asts[i];
            var sourceText = i < compilation.Units.Length ? compilation.Units[i].Source : null;
            var unit = i < compilation.Units.Length ? compilation.Units[i] : null;

            // Each unit can carry its own namespace root and project folder
            // (e.g., stdlib units use "Standard" and the Standard/ directory).
            // Fall back to the driver-level values for user units.
            var effectiveNamespaceRoot = unit?.NamespaceRoot ?? namespaceRoot;
            var effectiveProjectFolder = unit?.ProjectFolder ?? projectFolder;

            // Determine whether this unit is directly visible to the user
            // project. A unit is directly visible if:
            //   - OriginProjectPath is null (stdlib or user's own code), OR
            //   - OriginProjectPath is in the set of direct reference paths.
            // Units from transitive-only references are registered in a
            // separate scope so their symbols are not found during name
            // resolution for the user project.
            var isTransitiveOnly = directReferencePaths is not null
                && unit?.OriginProjectPath is not null
                && !directReferencePaths.Contains(unit.OriginProjectPath);

            var targetScope = isTransitiveOnly ? transitiveScope : globalScope;

            try
            {
                Pass1_RegisterTypes(ast, targetScope, symbolTable, sourceText, effectiveNamespaceRoot, effectiveProjectFolder, diagnostics, withDeclarations);
            }
            catch (Exception ex)
            {
                diagnostics.Add(new Diagnostic(
                    new DiagnosticId("MIST_SEMANTIC_ERROR"),
                    DiagnosticSeverity.Error,
                    PipelineStage.SemanticModelConstruction,
                    $"Error during semantic model construction: {ex.Message}",
                    Location: null,
                    CompilationUnitPath: null,
                    AnalyzerName: null));
            }
        }

        // Pass 1e: Validate intrinsic markers (Task 9).
        // Walks every registered type and member to emit diagnostics for:
        //   * MIST_SB_0030 — intrinsic member with a body (Requirement 5.3).
        //   * MIST_SB_0031 — 'intrinsic' modifier used in a non-stdlib unit
        //     (Requirements 5.6, 5.13). Until Task 26 lands
        //     CompilationUnit.IsStdlib, every unit is treated as non-stdlib,
        //     so any 'intrinsic' in the semantic model currently fires this.
        //   * MIST_SB_0032 — member-level 'intrinsic' inside a non-intrinsic
        //     containing type (Requirement 5.15).
        Pass1e_ValidateIntrinsicMarkers(asts, symbolTable, compilation, diagnostics);

        // Pass 1e (cont.): Validate operator-declaration shape (Task 12).
        // Walks every operator symbol registered on class/struct member scopes
        // to emit diagnostics for:
        //   * MIST_SB_0033 — operator declared outside a class/struct body
        //     (Requirement 4.7). Unreachable from parsed source because the
        //     grammar already restricts OperatorDeclaration to MemberDeclaration;
        //     registered defensively.
        //   * MIST_SB_0034 — operator parameter count mismatches its required
        //     arity (Requirements 4.8, 4.9).
        //   * MIST_SB_0035 — operator not declared `public static`
        //     (Requirement 4.2).
        Pass1e_ValidateOperatorShape(asts, symbolTable, compilation, diagnostics);

        // Pass 1e (cont.): Validate cast-operator-declaration shape.
        // Walks every cast operator symbol registered on class/struct member
        // scopes to emit diagnostics for:
        //   * MIST_SB_0040 — cast operator parameter count is not 1
        //     (Requirements 6.3, 6.4).
        //   * MIST_SB_0041 — cast operator not declared `public static`
        //     (Requirement 6.1).
        //   * MIST_SB_0042 — cast operator declared outside a class/struct
        //     body (Requirement 6.1). Defensive; unreachable from parsed source.
        //   * MIST_SB_0043 — duplicate cast operator (same kind + same target
        //     type name) in the same type (Requirement 6.5).
        Pass1e_ValidateCastOperatorShape(asts, symbolTable, compilation, diagnostics);

        // (Task 49: PrimitiveTypeRegistry has been deleted. Stdlib types
        // are resolved directly via ResolvePrimitiveKeyword in Pass 2.)

        // Pass 1b: Resolve base types for class inheritance.
        // This runs after all types are registered so that forward references
        // to base classes are supported.
        Pass1_ResolveBaseTypes(asts, globalScope, symbolTable, compilation, diagnostics);

        // Pass 1b (cont.): Resolve interface lists on classes, structs, and interfaces.
        // After base types are resolved, resolve interface names on all types.
        // For classes, the first name in the base clause may be a base class
        // (already resolved above) or an interface; this pass handles the
        // interface case and any additional interface names.
        Pass1_ResolveInterfaceLists(symbolTable, asts, compilation, diagnostics);

        // Pass 1g: Resolve with-block declarations (Task 4.1).
        // After all types are registered and interface lists are resolved,
        // validate with-block type/trait references and member mappings.
        // Stores validated mappings in a WithBlockRegistry for use by the
        // trait satisfaction pass.
        var withBlockRegistry = new WithBlockRegistry();
        Pass1_ResolveWithBlocks(withDeclarations, globalScope, symbolTable, withBlockRegistry, diagnostics);

        // Pass 1g (cont.): Resolve trait satisfaction (Task 5.1).
        // After with blocks are resolved, compute structural matches and
        // combine with with-block mappings to determine which types satisfy
        // which traits. Results are stored in a TraitSatisfactionMap.
        var traitSatisfactionMap = new TraitSatisfactionMap();
        Pass1_ResolveTraitSatisfaction(symbolTable, withBlockRegistry, traitSatisfactionMap, diagnostics);

        // Pass 1c: Detect circular interface inheritance chains.
        // After interface lists are resolved, walk each interface's Interfaces
        // chain to detect cycles (including self-inheritance). Cycles are
        // broken by removing the back-edge from the Interfaces list.
        Pass1_DetectInterfaceCycles(symbolTable, asts, compilation, diagnostics);

        // Pass 1c (cont.): Detect circular class inheritance chains.
        // After all base types are resolved, walk each class's BaseType chain
        // to detect cycles (including self-inheritance). Cycles are broken by
        // setting BaseType = null on the class that closes the cycle.
        Pass1_DetectCycles(symbolTable, asts, compilation, diagnostics);

        // Pass 1d: Validate interface satisfaction.
        // After cycles are broken, check that each class and struct
        // implementing interfaces provides all required methods.
        Pass1_ValidateInterfaceSatisfaction(symbolTable, asts, compilation, diagnostics);

        // Pass 1f: Check that base class constructor requirements are satisfied.
        // If a base class has explicit constructors that require parameters,
        // the derived class must provide a constructor with `: base(args)`.
        Pass1_CheckBaseConstructorSatisfied(symbolTable, asts, compilation, diagnostics);

        // Upgrade the ambient type resolver's stdlib provider so that
        // lazy type factories (e.g., Math.Abs returning Int32) reference
        // the real stdlib IType instances registered in Pass 1 instead of
        // the FallbackStdlibTypeProvider singletons (which have empty
        // member scopes and therefore no operator members).
        if (ambientTypeResolver is Backends.CSharpTranspiler.CSharpAmbientTypeResolver csharpResolver)
        {
            csharpResolver.UpdateStdlibProvider(
                new ScopeBasedStdlibTypeProvider(globalScope, symbolTable));
        }

        // Pass 2: Resolve types and build expression types.
        var expressionTypes = new Dictionary<Storm.Runtime.SyntaxNode, IType>();
        for (var i = 0; i < asts.Length; i++)
        {
            var ast = asts[i];
            var sourceText = i < compilation.Units.Length ? compilation.Units[i].Source : null;

            try
            {
                Pass2_ResolveTypes(ast, globalScope, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
            }
            catch (Exception ex)
            {
                diagnostics.Add(new Diagnostic(
                    new DiagnosticId("MIST_SEMANTIC_ERROR"),
                    DiagnosticSeverity.Error,
                    PipelineStage.SemanticModelConstruction,
                    $"Error during type resolution: {ex.Message}",
                    Location: null,
                    CompilationUnitPath: null,
                    AnalyzerName: null));
            }
        }

        // Post-Pass 2: Check for member redeclarations in derived classes.
        // This runs after type resolution so that parameter types are resolved
        // and method signatures can be compared accurately for overloading.
        Pass1_CheckMemberRedeclarations(symbolTable, asts, compilation, diagnostics);

        // Post-Pass 2: Check for duplicate method/constructor signatures.
        // This runs after type resolution so that parameter types are resolved
        // and signatures can be compared accurately.
        Pass1_CheckDuplicateSignatures(symbolTable, asts, compilation, diagnostics);

        return new SemanticModel(
            compilation,
            compilation.Units.Select(u => u).ToList(),
            asts,
            globalScope,
            symbolTable,
            expressionTypes,
            traitSatisfactionMap);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Inferred Namespace Computation
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Computes the inferred namespace for a source file based on its location
    /// relative to the project folder.
    /// </summary>
    /// <param name="namespaceRoot">The root namespace segment (from mist.toml or project name).</param>
    /// <param name="projectFolder">The absolute path to the project folder.</param>
    /// <param name="filePath">The absolute path to the source file.</param>
    /// <returns>The inferred namespace string.</returns>
    internal static string ComputeInferredNamespace(
        string namespaceRoot, string projectFolder, string filePath)
    {
        var fileDir = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(fileDir))
            return namespaceRoot;

        var relativePath = Path.GetRelativePath(projectFolder, fileDir);
        if (relativePath == "." || string.IsNullOrEmpty(relativePath))
            return namespaceRoot;

        var segments = relativePath
            .Replace(Path.DirectorySeparatorChar, '.')
            .Replace(Path.AltDirectorySeparatorChar, '.');
        return $"{namespaceRoot}.{segments}";
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Pass 1 — Register user-defined types
    // ═══════════════════════════════════════════════════════════════════════

    private static void Pass1_RegisterTypes(
        Storm.Runtime.SyntaxNode ast,
        Scope globalScope,
        SymbolTable symbolTable,
        SourceText? sourceText,
        string namespaceRoot,
        string projectFolder,
        DiagnosticBag diagnostics,
        List<WithDeclarationNode>? withDeclarations = null)
    {
        if (ast is not CompilationUnitNode compilationUnit)
        {
            return;
        }

        // Compute the inferred namespace for this file based on its path.
        var filePath = sourceText?.Path ?? "";
        var inferredNamespace = string.IsNullOrEmpty(filePath)
            ? namespaceRoot
            : ComputeInferredNamespace(namespaceRoot, projectFolder, filePath);

        // Track the "current namespace" — initially the inferred namespace.
        var currentNamespace = inferredNamespace;
        var currentScope = GetOrCreateNamespaceScope(globalScope, symbolTable, currentNamespace, ast);

        // Iterate over TopLevelItem nodes.
        foreach (var item in compilationUnit.Items)
        {
            switch (item)
            {
                case NamespaceItemTopLevelItemNode nsItem:
                {
                    var nsDecl = nsItem.NamespaceDeclaration;
                    var declaredName = ExtractNamespaceName(nsDecl, sourceText);

                    if (nsDecl is FileScopedNamespaceNamespaceDeclarationNode)
                    {
                        // File-scoped: update current namespace for subsequent types.
                        currentNamespace = declaredName;
                        currentScope = GetOrCreateNamespaceScope(globalScope, symbolTable, currentNamespace, nsDecl);

                        // Register types in the file-scoped namespace body.
                        var bodyTypes = GetNamespaceBodyTypes(nsDecl);
                        foreach (var typeDecl in bodyTypes)
                        {
                            RegisterTypeDeclaration(typeDecl, currentScope, symbolTable, currentNamespace, sourceText, diagnostics);
                        }
                    }
                    else if (nsDecl is BlockNamespaceNamespaceDeclarationNode)
                    {
                        // Block: register only enclosed types; current namespace unchanged after block.
                        var blockScope = GetOrCreateNamespaceScope(globalScope, symbolTable, declaredName, nsDecl);
                        var bodyTypes = GetNamespaceBodyTypes(nsDecl);
                        foreach (var typeDecl in bodyTypes)
                        {
                            RegisterTypeDeclaration(typeDecl, blockScope, symbolTable, declaredName, sourceText, diagnostics);
                        }
                    }
                    break;
                }
                case TypeItemTopLevelItemNode typeItem:
                {
                    // Register type under the current namespace.
                    RegisterTypeDeclaration(typeItem.TypeDeclaration, currentScope, symbolTable, currentNamespace, sourceText, diagnostics);
                    break;
                }
                case WithItemTopLevelItemNode withItem:
                {
                    // Store the WithDeclaration AST node for later resolution.
                    // No symbols are registered at this point — with blocks don't declare types.
                    withDeclarations?.Add(withItem.WithDeclaration);
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Gets or creates a namespace scope for the given namespace name.
    /// Reuses an existing scope if the namespace was already registered by a previous file.
    /// </summary>
    private static Scope GetOrCreateNamespaceScope(
        Scope globalScope,
        SymbolTable symbolTable,
        string namespaceName,
        Storm.Runtime.SyntaxNode declaringNode)
    {
        // Try to find an existing namespace scope.
        Scope? namespaceScope = FindExistingNamespaceScope(globalScope, symbolTable, namespaceName);

        if (namespaceScope is not null)
            return namespaceScope;

        // Also check if there's already a namespace symbol registered (even if no types yet).
        foreach (var member in globalScope.Members)
        {
            if (member.Kind == SymbolKind.Namespace && member.Name == namespaceName)
            {
                // The namespace symbol exists but FindExistingNamespaceScope didn't find a scope
                // (no types registered yet). We need to find or create the scope.
                // Look for the scope associated with this namespace symbol.
                // Since we can't directly get the scope from the symbol, create a new one
                // and it will be found next time via types registered in it.
                namespaceScope = new Scope(parent: globalScope);
                return namespaceScope;
            }
        }

        // Create a new namespace scope and register the namespace symbol.
        namespaceScope = new Scope(parent: globalScope);

        var namespaceSpan = ToMistSourceSpan(declaringNode.Span);
        var namespaceSymbol = new Symbol(
            namespaceName,
            SymbolKind.Namespace,
            SymbolOrigin.UserCode,
            globalScope,
            declaredType: null,
            namespaceSpan);

        globalScope.AddMember(namespaceSymbol);
        symbolTable.Register(namespaceSymbol, declaringNode);

        return namespaceScope;
    }

    private static void RegisterTypeDeclaration(
        TypeDeclarationNode typeDecl,
        Scope parentScope,
        SymbolTable symbolTable,
        string? namespaceName,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        switch (typeDecl)
        {
            case ClassTypeTypeDeclarationNode classTypeNode:
                RegisterClassDeclaration(classTypeNode.ClassDeclaration, parentScope, symbolTable, namespaceName, sourceText, diagnostics);
                break;
            case StructTypeTypeDeclarationNode structTypeNode:
                RegisterStructDeclaration(structTypeNode.StructDeclaration, parentScope, symbolTable, namespaceName, sourceText, diagnostics);
                break;
            case InterfaceTypeTypeDeclarationNode interfaceTypeNode:
                RegisterInterfaceDeclaration(
                    interfaceTypeNode.InterfaceDeclaration,
                    parentScope, symbolTable, namespaceName, sourceText, diagnostics);
                break;
            case TraitTypeTypeDeclarationNode traitTypeNode:
                RegisterTraitDeclaration(
                    traitTypeNode.TraitDeclaration,
                    parentScope, symbolTable, namespaceName, sourceText, diagnostics);
                break;
        }
    }

    private static void RegisterClassDeclaration(
        ClassDeclarationNode classDecl,
        Scope parentScope,
        SymbolTable symbolTable,
        string? namespaceName,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        var className = classDecl.Name.Lexeme;
        var typeScope = new Scope(parent: parentScope);
        var isBackendBacked = HasIntrinsicModifier(classDecl.Modifiers);
        var classType = new ClassType(className, typeScope, namespaceName, isBackendBacked);

        var classSpan = ToMistSourceSpan(classDecl.Name.Span);
        var classSymbol = new Symbol(className, SymbolKind.Class, SymbolOrigin.UserCode, parentScope, classType, classSpan);

        parentScope.AddMember(classSymbol);
        symbolTable.Register(classSymbol, classDecl);

        // Register type parameters if present.
        RegisterTypeParameters(classDecl.TypeParams, className, typeScope, classType, sourceText, diagnostics);

        foreach (var memberDecl in classDecl.Members)
        {
            RegisterMemberDeclaration(memberDecl, typeScope, symbolTable, sourceText, diagnostics);
        }
    }

    private static void RegisterStructDeclaration(
        StructDeclarationNode structDecl,
        Scope parentScope,
        SymbolTable symbolTable,
        string? namespaceName,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        var structName = structDecl.Name.Lexeme;
        var typeScope = new Scope(parent: parentScope);
        var isBackendBacked = HasIntrinsicModifier(structDecl.Modifiers);
        var structType = new StructType(structName, typeScope, namespaceName, isBackendBacked);

        var structSpan = ToMistSourceSpan(structDecl.Name.Span);
        var structSymbol = new Symbol(structName, SymbolKind.Struct, SymbolOrigin.UserCode, parentScope, structType, structSpan);

        parentScope.AddMember(structSymbol);
        symbolTable.Register(structSymbol, structDecl);

        // Register type parameters if present.
        RegisterTypeParameters(structDecl.TypeParams, structName, typeScope, structType, sourceText, diagnostics);

        foreach (var memberDecl in structDecl.Members)
        {
            RegisterMemberDeclaration(memberDecl, typeScope, symbolTable, sourceText, diagnostics);
        }
    }

    private static void RegisterInterfaceDeclaration(
        InterfaceDeclarationNode interfaceDecl,
        Scope parentScope,
        SymbolTable symbolTable,
        string? namespaceName,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        var interfaceName = interfaceDecl.Name.Lexeme;
        var typeScope = new Scope(parent: parentScope);
        var interfaceType = new InterfaceType(interfaceName, typeScope, namespaceName);

        var interfaceSpan = ToMistSourceSpan(interfaceDecl.Name.Span);
        var interfaceSymbol = new Symbol(interfaceName, SymbolKind.Interface, SymbolOrigin.UserCode, parentScope, interfaceType, interfaceSpan);

        parentScope.AddMember(interfaceSymbol);
        symbolTable.Register(interfaceSymbol, interfaceDecl);

        // Register type parameters if present.
        RegisterTypeParameters(interfaceDecl.TypeParams, interfaceName, typeScope, interfaceType, sourceText, diagnostics);

        foreach (var member in interfaceDecl.Members)
        {
            switch (member)
            {
                case InterfaceMethodMemberInterfaceMemberDeclarationNode methodMember:
                    RegisterInterfaceMethodDeclaration(methodMember.Method, typeScope, symbolTable, sourceText, diagnostics);
                    break;
                case InterfacePropertyMemberInterfaceMemberDeclarationNode propertyMember:
                    RegisterInterfacePropertyDeclaration(propertyMember.Property, typeScope, symbolTable);
                    break;
            }
        }
    }

    private static void RegisterTraitDeclaration(
        TraitDeclarationNode traitDecl,
        Scope parentScope,
        SymbolTable symbolTable,
        string? namespaceName,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        var traitName = traitDecl.Name.Lexeme;
        var typeScope = new Scope(parent: parentScope);
        var traitType = new TraitType(traitName, typeScope, namespaceName);

        var traitSpan = ToMistSourceSpan(traitDecl.Name.Span);
        var traitSymbol = new Symbol(traitName, SymbolKind.Trait, SymbolOrigin.UserCode, parentScope, traitType, traitSpan);

        parentScope.AddMember(traitSymbol);
        symbolTable.Register(traitSymbol, traitDecl);

        foreach (var member in traitDecl.Members)
        {
            switch (member)
            {
                case TraitFieldMemberTraitMemberDeclarationNode fieldMember:
                {
                    var fieldDecl = fieldMember.Field;
                    var fieldName = fieldDecl.Name.Lexeme;
                    var fieldSpan = ToMistSourceSpan(fieldDecl.Name.Span);
                    var fieldSymbol = new Symbol(fieldName, SymbolKind.Field, SymbolOrigin.UserCode, typeScope, declaredType: null, fieldSpan);
                    typeScope.AddMember(fieldSymbol);
                    symbolTable.Register(fieldSymbol, fieldDecl);
                    break;
                }
                case TraitMethodMemberTraitMemberDeclarationNode methodMember:
                {
                    var methodDecl = methodMember.Method;
                    var methodName = methodDecl.Name.Lexeme;
                    var methodSpan = ToMistSourceSpan(methodDecl.Name.Span);
                    var methodSymbol = new Symbol(methodName, SymbolKind.Method, SymbolOrigin.UserCode, typeScope, declaredType: null, methodSpan);
                    typeScope.AddMember(methodSymbol);
                    symbolTable.Register(methodSymbol, methodDecl);
                    break;
                }
                case TraitPropertyMemberTraitMemberDeclarationNode propertyMember:
                {
                    var propertyDecl = propertyMember.Property;
                    var propName = propertyDecl.Name.Lexeme;
                    var propSpan = ToMistSourceSpan(propertyDecl.Name.Span);
                    var propSymbol = new Symbol(propName, SymbolKind.Property, SymbolOrigin.UserCode, typeScope, declaredType: null, propSpan);
                    typeScope.AddMember(propSymbol);
                    symbolTable.Register(propSymbol, propertyDecl);
                    break;
                }
            }
        }
    }

    private static void RegisterInterfaceMethodDeclaration(
        InterfaceMethodDeclarationNode methodDecl,
        Scope typeScope,
        SymbolTable symbolTable,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        var methodName = methodDecl.Name.Lexeme;
        var methodSpan = ToMistSourceSpan(methodDecl.Name.Span);
        var methodSymbol = new Symbol(methodName, SymbolKind.Method, SymbolOrigin.UserCode, typeScope, declaredType: null, methodSpan);
        typeScope.AddMember(methodSymbol);
        symbolTable.Register(methodSymbol, methodDecl);

        // Register method type parameters if present.
        RegisterMethodTypeParameters(methodDecl.TypeParams, methodSymbol, typeScope, sourceText, diagnostics);
    }

    /// <summary>
    /// Registers an interface property declaration as a <see cref="SymbolKind.Property"/> symbol
    /// on the interface type scope.
    /// </summary>
    private static void RegisterInterfacePropertyDeclaration(
        InterfacePropertyDeclarationNode propertyDecl,
        Scope typeScope,
        SymbolTable symbolTable)
    {
        var propName = propertyDecl.Name.Lexeme;
        var propSpan = ToMistSourceSpan(propertyDecl.Name.Span);

        var hasGet = false;
        var hasSet = false;
        AccessorInfo? getAccessor = null;
        AccessorInfo? setAccessor = null;

        foreach (var accessor in propertyDecl.Accessors)
        {
            switch (accessor)
            {
                case InterfaceGetAccessorInterfaceAccessorDeclarationNode:
                    hasGet = true;
                    getAccessor = new AccessorInfo { Kind = AccessorKind.Get, BodyKind = AccessorBodyKind.Auto, Modifier = null };
                    break;
                case InterfaceSetAccessorInterfaceAccessorDeclarationNode:
                    hasSet = true;
                    setAccessor = new AccessorInfo { Kind = AccessorKind.Set, BodyKind = AccessorBodyKind.Auto, Modifier = null };
                    break;
            }
        }

        var propertyInfo = new PropertyInfo
        {
            HasGet = hasGet,
            HasSet = hasSet,
            HasInit = false,
            IsComputed = false,
            IsStatic = false,
            IsExpressionBodied = false,
            GetAccessor = getAccessor,
            SetAccessor = setAccessor,
            InitAccessor = null,
            BackingField = null,
        };

        var propSymbol = new Symbol(propName, SymbolKind.Property, SymbolOrigin.UserCode, typeScope, declaredType: null, propSpan);
        propSymbol.PropertyMetadata = propertyInfo;
        typeScope.AddMember(propSymbol);
        symbolTable.Register(propSymbol, propertyDecl);
    }

    private static void RegisterMemberDeclaration(
        MemberDeclarationNode memberDecl,
        Scope typeScope,
        SymbolTable symbolTable,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        switch (memberDecl)
        {
            case FieldMemberMemberDeclarationNode fieldMember:
                RegisterFieldDeclaration(fieldMember.Field, typeScope, symbolTable);
                break;
            case ConstructorMemberMemberDeclarationNode ctorMember:
                RegisterConstructorDeclaration(ctorMember.Constructor, typeScope, symbolTable);
                break;
            case MethodMemberMemberDeclarationNode methodMember:
                RegisterMethodDeclaration(methodMember.Method, typeScope, symbolTable, sourceText, diagnostics);
                break;
            case OperatorMemberMemberDeclarationNode operatorMember:
                RegisterOperatorDeclaration(operatorMember.Operator, typeScope, symbolTable);
                break;
            case PropertyMemberMemberDeclarationNode propertyMember:
                RegisterPropertyDeclaration(propertyMember.Property, typeScope, symbolTable);
                break;
            case IndexerMemberMemberDeclarationNode indexerMember:
                RegisterIndexerDeclaration(indexerMember.Indexer, typeScope, symbolTable);
                break;
            case CastOperatorMemberMemberDeclarationNode castOpMember:
                RegisterCastOperatorDeclaration(castOpMember.CastOperator, typeScope, symbolTable, sourceText);
                break;
        }
    }

    private static void RegisterFieldDeclaration(FieldDeclarationNode fieldDecl, Scope typeScope, SymbolTable symbolTable)
    {
        var fieldName = fieldDecl.Name.Lexeme;
        var fieldSpan = ToMistSourceSpan(fieldDecl.Name.Span);
        var fieldSymbol = new Symbol(fieldName, SymbolKind.Field, SymbolOrigin.UserCode, typeScope, declaredType: null, fieldSpan);
        typeScope.AddMember(fieldSymbol);
        symbolTable.Register(fieldSymbol, fieldDecl);
    }

    private static void RegisterConstructorDeclaration(ConstructorDeclarationNode ctorDecl, Scope typeScope, SymbolTable symbolTable)
    {
        var ctorName = ctorDecl.Name.Lexeme;
        var ctorSpan = ToMistSourceSpan(ctorDecl.Name.Span);
        var isIntrinsic = HasIntrinsicModifier(ctorDecl.Modifiers);
        var hasBody = ctorDecl.Body is not MethodNoBodyMethodBodyNode;
        var ctorSymbol = new Symbol(
            ctorName, SymbolKind.Constructor, SymbolOrigin.UserCode, typeScope,
            declaredType: null, ctorSpan,
            isIntrinsic: isIntrinsic, hasBody: hasBody);
        typeScope.AddMember(ctorSymbol);
        symbolTable.Register(ctorSymbol, ctorDecl);
    }

    private static void RegisterMethodDeclaration(MethodDeclarationNode methodDecl, Scope typeScope, SymbolTable symbolTable, SourceText? sourceText, DiagnosticBag diagnostics)
    {
        var methodName = methodDecl.Name.Lexeme;
        var methodSpan = ToMistSourceSpan(methodDecl.Name.Span);
        var isIntrinsic = HasIntrinsicModifier(methodDecl.Modifiers);
        var hasBody = methodDecl.Body is not MethodNoBodyMethodBodyNode;
        var methodSymbol = new Symbol(
            methodName, SymbolKind.Method, SymbolOrigin.UserCode, typeScope,
            declaredType: null, methodSpan,
            isIntrinsic: isIntrinsic, hasBody: hasBody);
        typeScope.AddMember(methodSymbol);
        symbolTable.Register(methodSymbol, methodDecl);

        // Register method type parameters if present.
        RegisterMethodTypeParameters(methodDecl.TypeParams, methodSymbol, typeScope, sourceText, diagnostics);
    }

    /// <summary>
    /// Registers an operator declaration as a static method symbol on the
    /// containing type's scope. The operator's name is mangled as
    /// <c>operator&lt;symbol&gt;</c> (e.g., <c>operator+</c>) so that overload
    /// resolution in Task 14 can look it up by constructing the same key from
    /// an operator expression.
    ///
    /// Pass 1 establishes only the symbol's identity (name, kind, modifiers,
    /// body-presence). Parameter signature extraction and the operator's
    /// return type are wired up in Pass 2's <see cref="ResolveOperatorDeclaration"/>,
    /// which mirrors <see cref="ResolveMethodDeclaration"/> — operators are
    /// treated as static methods for semantic-model purposes (Requirement 4.6),
    /// and <see cref="ParameterSignature.Extract"/> reads the parameter list
    /// from the <see cref="OperatorDeclarationNode"/> just like for methods.
    /// </summary>
    private static void RegisterOperatorDeclaration(OperatorDeclarationNode opDecl, Scope typeScope, SymbolTable symbolTable)
    {
        var opLexeme = GetOperatorSymbolLexeme(opDecl.Symbol);
        var opName = "operator" + opLexeme;
        var opSpan = ToMistSourceSpan(opDecl.Symbol.Span);
        var isIntrinsic = HasIntrinsicModifier(opDecl.Modifiers);
        var hasBody = opDecl.Body is not OperatorNoBodyOperatorBodyNode;
        var opSymbol = new Symbol(
            opName, SymbolKind.Method, SymbolOrigin.UserCode, typeScope,
            declaredType: null, opSpan,
            isIntrinsic: isIntrinsic, hasBody: hasBody);
        typeScope.AddMember(opSymbol);
        symbolTable.Register(opSymbol, opDecl);
    }

    /// <summary>
    /// Registers a cast operator declaration as a static method symbol on the
    /// containing type's scope. The operator's name is encoded as
    /// <c>operator_{kind}_{targetType}</c> (e.g., <c>operator_implicit_Weight</c>,
    /// <c>operator_explicit_string</c>) so that the type checker can look it up
    /// by constructing the same key from a cast expression or implicit conversion
    /// site.
    ///
    /// Pass 1 establishes only the symbol's identity (name, kind, modifiers,
    /// body-presence). The target type and parameter are resolved in Pass 2's
    /// <see cref="ResolveCastOperatorDeclaration"/>.
    /// </summary>
    private static void RegisterCastOperatorDeclaration(
        CastOperatorDeclarationNode castOpDecl, Scope typeScope, SymbolTable symbolTable, SourceText? sourceText = null)
    {
        // Extract the cast kind prefix.
        var kindPrefix = castOpDecl.CastKind switch
        {
            ImplicitCastCastOperatorKindNode => "implicit",
            ExplicitCastCastOperatorKindNode => "explicit",
            _ => "unknown",
        };

        // Extract the target type name from the type reference for the encoded name.
        // The full type resolution happens in Pass 2; here we use the raw lexeme.
        var targetTypeName = ExtractTypeReferenceName(castOpDecl.TargetType, sourceText);
        var opName = $"operator_{kindPrefix}_{targetTypeName}";

        var opSpan = ToMistSourceSpan(castOpDecl.CastKind.Span);
        var isIntrinsic = HasIntrinsicModifier(castOpDecl.Modifiers);
        var hasBody = castOpDecl.Body is not OperatorNoBodyOperatorBodyNode;

        var opSymbol = new Symbol(
            opName, SymbolKind.Method, SymbolOrigin.UserCode, typeScope,
            declaredType: null, opSpan,
            isIntrinsic: isIntrinsic, hasBody: hasBody);
        typeScope.AddMember(opSymbol);
        symbolTable.Register(opSymbol, castOpDecl);
    }

    /// <summary>
    /// Extracts the raw type name from a <see cref="TypeReferenceNode"/> for use
    /// in encoded symbol names (e.g., cast operator names). For named types, this
    /// returns the IDENTIFIER lexeme; for primitive types, the keyword string
    /// (e.g., <c>"int"</c>, <c>"string"</c>). Array ranks are not included.
    /// <para>
    /// The generated parser may not populate <c>IDENTIFIER.Lexeme</c> on
    /// <see cref="QualifiedIdentifierNode"/>. When the lexeme is empty, this
    /// method falls back to extracting the name from the node's source span
    /// via the <paramref name="sourceText"/> parameter (if available).
    /// </para>
    /// </summary>
    private static string ExtractTypeReferenceName(TypeReferenceNode typeRef, SourceText? sourceText = null)
    {
        switch (typeRef)
        {
            case NamedTypeRefTypeReferenceNode namedRef:
                var lexeme = namedRef.Name.IDENTIFIER.Lexeme;
                if (!string.IsNullOrEmpty(lexeme))
                    return lexeme;
                // Fallback: extract from the QualifiedIdentifier's source span.
                if (sourceText is not null)
                {
                    var span = namedRef.Name.Span;
                    if (span.Length > 0 && span.Start + span.Length <= sourceText.Contents.Length)
                        return sourceText.Contents.Substring(span.Start, span.Length);
                }
                // Last resort: extract from the TypeReference's own span (covers
                // cases where the QualifiedIdentifier span is zero but the
                // TypeReference span is valid).
                {
                    var span = namedRef.Span;
                    if (sourceText is not null && span.Length > 0 && span.Start + span.Length <= sourceText.Contents.Length)
                    {
                        var raw = sourceText.Contents.Substring(span.Start, span.Length);
                        // Strip array rank suffix if present.
                        var bracketIdx = raw.IndexOf('[');
                        return bracketIdx >= 0 ? raw.Substring(0, bracketIdx).Trim() : raw.Trim();
                    }
                }
                return "";
            case PrimitiveTypeRefTypeReferenceNode primitiveRef:
                return FormatPrimitiveType(primitiveRef.PrimitiveType);
            case VoidTypeRefTypeReferenceNode:
                return "void";
            default:
                return "unknown";
        }
    }

    /// <summary>
    /// Returns <c>true</c> if the modifier list contains an
    /// <see cref="IntrinsicModifierModifierNode"/>. Used at type level to set
    /// <c>IsBackendBacked</c> on <see cref="ClassType"/>/<see cref="StructType"/>
    /// and at member level to set <c>IsIntrinsic</c> on <see cref="Symbol"/>.
    /// </summary>
    private static bool HasIntrinsicModifier(ImmutableArray<ModifierNode> modifiers)
    {
        foreach (var modifier in modifiers)
        {
            if (modifier is IntrinsicModifierModifierNode)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Extracts the textual lexeme of an operator symbol node
    /// (e.g., <c>"+"</c> for <see cref="PlusOpOperatorSymbolNode"/>). The
    /// lexeme becomes the suffix of the operator's symbol name
    /// (<c>"operator+"</c>) so the type checker can construct the same key
    /// from an operator expression.
    /// </summary>
    private static string GetOperatorSymbolLexeme(OperatorSymbolNode symbol) => symbol switch
    {
        PlusOpOperatorSymbolNode => "+",
        MinusOpOperatorSymbolNode => "-",
        StarOpOperatorSymbolNode => "*",
        SlashOpOperatorSymbolNode => "/",
        ModuloOpOperatorSymbolNode => "%",
        EqualEqualOpOperatorSymbolNode => "==",
        NotEqualOpOperatorSymbolNode => "!=",
        LessThanOpOperatorSymbolNode => "<",
        GreaterThanOpOperatorSymbolNode => ">",
        LessThanOrEqualOpOperatorSymbolNode => "<=",
        GreaterThanOrEqualOpOperatorSymbolNode => ">=",
        LogicalAndOpOperatorSymbolNode => "&&",
        LogicalOrOpOperatorSymbolNode => "||",
        ExclamationOpOperatorSymbolNode => "!",
        _ => "",
    };

    /// <summary>
    /// Extracts type parameter names from a <see cref="TypeParameterListNode"/> and returns
    /// them as a list of <see cref="TypeParameterType"/> instances with correct ordinals.
    /// Also checks for duplicate names and shadowing of the enclosing type name.
    /// </summary>
    private static IReadOnlyList<TypeParameterType> ExtractTypeParameters(
        TypeParameterListNode? typeParamList,
        string? enclosingTypeName,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        if (typeParamList is null)
            return Array.Empty<TypeParameterType>();

        var typeParams = new List<TypeParameterType>();
        var seenNames = new HashSet<string>();
        var ordinal = 0;

        // Process the first type parameter.
        var firstName = typeParamList.TypeParameter.Name.Lexeme;
        ProcessTypeParameter(firstName, ordinal++, typeParamList.TypeParameter, enclosingTypeName, seenNames, typeParams, sourceText, diagnostics);

        // Process the rest of the type parameters.
        foreach (var tp in typeParamList.TypeParameters)
        {
            var name = tp.Name.Lexeme;
            ProcessTypeParameter(name, ordinal++, tp, enclosingTypeName, seenNames, typeParams, sourceText, diagnostics);
        }

        return typeParams;
    }

    /// <summary>
    /// Processes a single type parameter: checks for duplicates and shadowing,
    /// then creates and adds a <see cref="TypeParameterType"/> to the list.
    /// </summary>
    private static void ProcessTypeParameter(
        string name,
        int ordinal,
        TypeParameterNode node,
        string? enclosingTypeName,
        HashSet<string> seenNames,
        List<TypeParameterType> typeParams,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        // Check for duplicate type parameter names.
        if (!seenNames.Add(name))
        {
            diagnostics.Add(MakeDiagnosticWithLocation(
                DiagnosticId.DuplicateTypeParameter.Value,
                DiagnosticSeverity.Error,
                $"Duplicate type parameter name '{name}'.",
                node,
                sourceText));
        }

        // Check if the type parameter shadows the enclosing type name.
        if (enclosingTypeName is not null && name == enclosingTypeName)
        {
            diagnostics.Add(MakeDiagnosticWithLocation(
                DiagnosticId.TypeParameterShadowsType.Value,
                DiagnosticSeverity.Warning,
                $"Type parameter '{name}' shadows the enclosing type name.",
                node,
                sourceText));
        }

        typeParams.Add(new TypeParameterType(name, ordinal));
    }

    /// <summary>
    /// Registers type parameters for a class, struct, or interface declaration.
    /// Creates <see cref="TypeParameterType"/> instances, stores them on the declaring type,
    /// and registers each as a symbol in the type's member scope.
    /// </summary>
    private static void RegisterTypeParameters(
        TypeParameterListNode? typeParamList,
        string typeName,
        Scope typeScope,
        IType declaringType,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        var typeParams = ExtractTypeParameters(typeParamList, typeName, sourceText, diagnostics);
        if (typeParams.Count == 0)
            return;

        // Store type parameters on the declaring type.
        switch (declaringType)
        {
            case ClassType classType:
                classType.TypeParameters = typeParams;
                break;
            case StructType structType:
                structType.TypeParameters = typeParams;
                break;
            case InterfaceType interfaceType:
                interfaceType.TypeParameters = typeParams;
                break;
        }

        // Register each type parameter as a symbol in the type's member scope
        // so it can be resolved as a type within the type body.
        foreach (var tp in typeParams)
        {
            var tpSpan = GetTypeParameterSpan(typeParamList!, tp.Ordinal);
            var tpSymbol = new Symbol(tp.Name, SymbolKind.Class, SymbolOrigin.UserCode, typeScope, tp, tpSpan);
            typeScope.AddMember(tpSymbol);
        }
    }

    /// <summary>
    /// Registers type parameters for a method declaration (both regular and interface methods).
    /// Creates <see cref="TypeParameterType"/> instances, stores them on the method symbol,
    /// and registers each in the method's enclosing type scope so they can be resolved.
    /// </summary>
    private static void RegisterMethodTypeParameters(
        TypeParameterListNode? typeParamList,
        Symbol methodSymbol,
        Scope typeScope,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        if (typeParamList is null)
            return;

        // For method type parameters, we don't check shadowing against the enclosing type name
        // (that's only for type-level type parameters). We only check for duplicates.
        var typeParams = ExtractTypeParameters(typeParamList, enclosingTypeName: null, sourceText, diagnostics);
        if (typeParams.Count == 0)
            return;

        // Store type parameters on the method symbol.
        methodSymbol.TypeParameters = typeParams;

        // Register each type parameter as a symbol in the type scope so they
        // take precedence over enclosing type parameters during resolution.
        foreach (var tp in typeParams)
        {
            var tpSpan = GetTypeParameterSpan(typeParamList, tp.Ordinal);
            var tpSymbol = new Symbol(tp.Name, SymbolKind.Class, SymbolOrigin.UserCode, typeScope, tp, tpSpan);
            typeScope.AddMember(tpSymbol);
        }
    }

    /// <summary>
    /// Gets the source span for a type parameter at the given ordinal in a <see cref="TypeParameterListNode"/>.
    /// </summary>
    private static Common.Text.SourceSpan? GetTypeParameterSpan(TypeParameterListNode typeParamList, int ordinal)
    {
        if (ordinal == 0)
            return ToMistSourceSpan(typeParamList.TypeParameter.Name.Span);

        var index = ordinal - 1;
        if (index < typeParamList.TypeParameters.Length)
            return ToMistSourceSpan(typeParamList.TypeParameters[index].Name.Span);

        return null;
    }

    /// <summary>
    /// Registers a property declaration as a <see cref="SymbolKind.Property"/> symbol.
    /// Walks the accessor list to build <see cref="PropertyInfo"/>, classifies
    /// computed properties, and generates a backing field when needed.
    /// </summary>
    private static void RegisterPropertyDeclaration(PropertyDeclarationNode propertyDecl, Scope typeScope, SymbolTable symbolTable)
    {
        switch (propertyDecl)
        {
            case BlockPropertyPropertyDeclarationNode blockProp:
                RegisterBlockProperty(blockProp, typeScope, symbolTable);
                break;
            case ExpressionBodiedPropertyPropertyDeclarationNode exprProp:
                RegisterExpressionBodiedProperty(exprProp, typeScope, symbolTable);
                break;
        }
    }

    private static void RegisterBlockProperty(BlockPropertyPropertyDeclarationNode blockProp, Scope typeScope, SymbolTable symbolTable)
    {
        var propName = blockProp.Name.Lexeme;
        var propSpan = ToMistSourceSpan(blockProp.Name.Span);
        var isStatic = blockProp.Modifiers.Any(m => m is StaticModifierModifierNode);

        // Walk accessor list to build PropertyInfo.
        AccessorInfo? getAccessor = null;
        AccessorInfo? setAccessor = null;
        AccessorInfo? initAccessor = null;
        var hasAutoAccessor = false;
        var usesFieldKeyword = false;

        foreach (var accessor in blockProp.Accessors)
        {
            switch (accessor)
            {
                case AutoGetAccessorAccessorDeclarationNode autoGet:
                    getAccessor = new AccessorInfo { Kind = AccessorKind.Get, BodyKind = AccessorBodyKind.Auto, Modifier = autoGet.AccessorModifier };
                    hasAutoAccessor = true;
                    break;
                case BlockGetAccessorAccessorDeclarationNode blockGet:
                    getAccessor = new AccessorInfo { Kind = AccessorKind.Get, BodyKind = AccessorBodyKind.Block, Modifier = blockGet.AccessorModifier };
                    if (ContainsFieldKeyword(blockGet.Body)) usesFieldKeyword = true;
                    break;
                case ExpressionGetAccessorAccessorDeclarationNode exprGet:
                    getAccessor = new AccessorInfo { Kind = AccessorKind.Get, BodyKind = AccessorBodyKind.Expression, Modifier = exprGet.AccessorModifier };
                    if (ContainsFieldKeywordInExpression(exprGet.Expression)) usesFieldKeyword = true;
                    break;
                case AutoSetAccessorAccessorDeclarationNode autoSet:
                    setAccessor = new AccessorInfo { Kind = AccessorKind.Set, BodyKind = AccessorBodyKind.Auto, Modifier = autoSet.AccessorModifier };
                    hasAutoAccessor = true;
                    break;
                case BlockSetAccessorAccessorDeclarationNode blockSet:
                    setAccessor = new AccessorInfo { Kind = AccessorKind.Set, BodyKind = AccessorBodyKind.Block, Modifier = blockSet.AccessorModifier };
                    if (ContainsFieldKeyword(blockSet.Body)) usesFieldKeyword = true;
                    break;
                case ExpressionSetAccessorAccessorDeclarationNode exprSet:
                    setAccessor = new AccessorInfo { Kind = AccessorKind.Set, BodyKind = AccessorBodyKind.Expression, Modifier = exprSet.AccessorModifier };
                    if (ContainsFieldKeywordInExpression(exprSet.Expression)) usesFieldKeyword = true;
                    break;
                case AutoInitAccessorAccessorDeclarationNode autoInit:
                    initAccessor = new AccessorInfo { Kind = AccessorKind.Init, BodyKind = AccessorBodyKind.Auto, Modifier = autoInit.AccessorModifier };
                    hasAutoAccessor = true;
                    break;
                case BlockInitAccessorAccessorDeclarationNode blockInit:
                    initAccessor = new AccessorInfo { Kind = AccessorKind.Init, BodyKind = AccessorBodyKind.Block, Modifier = blockInit.AccessorModifier };
                    if (ContainsFieldKeyword(blockInit.Body)) usesFieldKeyword = true;
                    break;
                case ExpressionInitAccessorAccessorDeclarationNode exprInit:
                    initAccessor = new AccessorInfo { Kind = AccessorKind.Init, BodyKind = AccessorBodyKind.Expression, Modifier = exprInit.AccessorModifier };
                    if (ContainsFieldKeywordInExpression(exprInit.Expression)) usesFieldKeyword = true;
                    break;
            }
        }

        var hasGet = getAccessor is not null;
        var hasSet = setAccessor is not null;
        var hasInit = initAccessor is not null;

        // Computed property classification:
        // IsComputed = HasGet && !HasSet && !HasInit && GetAccessor.BodyKind != Auto
        var isComputed = hasGet && !hasSet && !hasInit && getAccessor!.BodyKind != AccessorBodyKind.Auto;

        // Backing field generation:
        // NeedsBackingField = !IsComputed && (HasAutoAccessor || UsesFieldKeyword)
        Symbol? backingFieldSymbol = null;
        if (!isComputed && (hasAutoAccessor || usesFieldKeyword))
        {
            var backingFieldName = $"_{propName}_k__BackingField";
            var backingFieldSpan = propSpan; // Use property span for the backing field.
            backingFieldSymbol = new Symbol(backingFieldName, SymbolKind.Field, SymbolOrigin.UserCode, typeScope, declaredType: null, backingFieldSpan);
            typeScope.AddMember(backingFieldSymbol);
            symbolTable.Register(backingFieldSymbol, blockProp);
        }

        var propertyInfo = new PropertyInfo
        {
            HasGet = hasGet,
            HasSet = hasSet,
            HasInit = hasInit,
            IsComputed = isComputed,
            IsStatic = isStatic,
            IsExpressionBodied = false,
            GetAccessor = getAccessor,
            SetAccessor = setAccessor,
            InitAccessor = initAccessor,
            BackingField = backingFieldSymbol,
        };

        var propSymbol = new Symbol(propName, SymbolKind.Property, SymbolOrigin.UserCode, typeScope, declaredType: null, propSpan);
        propSymbol.PropertyMetadata = propertyInfo;
        typeScope.AddMember(propSymbol);
        symbolTable.Register(propSymbol, blockProp);
    }

    private static void RegisterExpressionBodiedProperty(ExpressionBodiedPropertyPropertyDeclarationNode exprProp, Scope typeScope, SymbolTable symbolTable)
    {
        var propName = exprProp.Name.Lexeme;
        var propSpan = ToMistSourceSpan(exprProp.Name.Span);
        var isStatic = exprProp.Modifiers.Any(m => m is StaticModifierModifierNode);

        // Expression-bodied properties are always computed (get-only with expression body).
        var getAccessor = new AccessorInfo { Kind = AccessorKind.Get, BodyKind = AccessorBodyKind.Expression, Modifier = null };

        var propertyInfo = new PropertyInfo
        {
            HasGet = true,
            HasSet = false,
            HasInit = false,
            IsComputed = true,
            IsStatic = isStatic,
            IsExpressionBodied = true,
            GetAccessor = getAccessor,
            SetAccessor = null,
            InitAccessor = null,
            BackingField = null,
        };

        var propSymbol = new Symbol(propName, SymbolKind.Property, SymbolOrigin.UserCode, typeScope, declaredType: null, propSpan);
        propSymbol.PropertyMetadata = propertyInfo;
        typeScope.AddMember(propSymbol);
        symbolTable.Register(propSymbol, exprProp);
    }

    /// <summary>
    /// Registers an indexer declaration as a <see cref="SymbolKind.Indexer"/> symbol.
    /// Stores parameter types and accessor info via <see cref="PropertyInfo"/>.
    /// </summary>
    private static void RegisterIndexerDeclaration(IndexerDeclarationNode indexerDecl, Scope typeScope, SymbolTable symbolTable)
    {
        if (indexerDecl is not BlockIndexerIndexerDeclarationNode blockIndexer)
            return;

        var indexerName = "this[]";
        var indexerSpan = ToMistSourceSpan(blockIndexer.THIS.Span);
        var isStatic = blockIndexer.Modifiers.Any(m => m is StaticModifierModifierNode);

        // Walk accessor list to build PropertyInfo (same as block property).
        AccessorInfo? getAccessor = null;
        AccessorInfo? setAccessor = null;
        AccessorInfo? initAccessor = null;

        foreach (var accessor in blockIndexer.Accessors)
        {
            switch (accessor)
            {
                case AutoGetAccessorAccessorDeclarationNode autoGet:
                    getAccessor = new AccessorInfo { Kind = AccessorKind.Get, BodyKind = AccessorBodyKind.Auto, Modifier = autoGet.AccessorModifier };
                    break;
                case BlockGetAccessorAccessorDeclarationNode blockGet:
                    getAccessor = new AccessorInfo { Kind = AccessorKind.Get, BodyKind = AccessorBodyKind.Block, Modifier = blockGet.AccessorModifier };
                    break;
                case ExpressionGetAccessorAccessorDeclarationNode exprGet:
                    getAccessor = new AccessorInfo { Kind = AccessorKind.Get, BodyKind = AccessorBodyKind.Expression, Modifier = exprGet.AccessorModifier };
                    break;
                case AutoSetAccessorAccessorDeclarationNode autoSet:
                    setAccessor = new AccessorInfo { Kind = AccessorKind.Set, BodyKind = AccessorBodyKind.Auto, Modifier = autoSet.AccessorModifier };
                    break;
                case BlockSetAccessorAccessorDeclarationNode blockSet:
                    setAccessor = new AccessorInfo { Kind = AccessorKind.Set, BodyKind = AccessorBodyKind.Block, Modifier = blockSet.AccessorModifier };
                    break;
                case ExpressionSetAccessorAccessorDeclarationNode exprSet:
                    setAccessor = new AccessorInfo { Kind = AccessorKind.Set, BodyKind = AccessorBodyKind.Expression, Modifier = exprSet.AccessorModifier };
                    break;
                case AutoInitAccessorAccessorDeclarationNode autoInit:
                    initAccessor = new AccessorInfo { Kind = AccessorKind.Init, BodyKind = AccessorBodyKind.Auto, Modifier = autoInit.AccessorModifier };
                    break;
                case BlockInitAccessorAccessorDeclarationNode blockInit:
                    initAccessor = new AccessorInfo { Kind = AccessorKind.Init, BodyKind = AccessorBodyKind.Block, Modifier = blockInit.AccessorModifier };
                    break;
                case ExpressionInitAccessorAccessorDeclarationNode exprInit:
                    initAccessor = new AccessorInfo { Kind = AccessorKind.Init, BodyKind = AccessorBodyKind.Expression, Modifier = exprInit.AccessorModifier };
                    break;
            }
        }

        var propertyInfo = new PropertyInfo
        {
            HasGet = getAccessor is not null,
            HasSet = setAccessor is not null,
            HasInit = initAccessor is not null,
            IsComputed = false,
            IsStatic = isStatic,
            IsExpressionBodied = false,
            GetAccessor = getAccessor,
            SetAccessor = setAccessor,
            InitAccessor = initAccessor,
            BackingField = null,
        };

        var indexerSymbol = new Symbol(indexerName, SymbolKind.Indexer, SymbolOrigin.UserCode, typeScope, declaredType: null, indexerSpan);
        indexerSymbol.PropertyMetadata = propertyInfo;
        typeScope.AddMember(indexerSymbol);
        symbolTable.Register(indexerSymbol, blockIndexer);
    }

    /// <summary>
    /// Checks whether a block body contains a <c>field</c> keyword expression.
    /// </summary>
    private static bool ContainsFieldKeyword(BlockNode block)
    {
        foreach (var child in block.Children())
        {
            if (ContainsFieldKeywordInNode(child))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Checks whether an expression contains a <c>field</c> keyword expression.
    /// </summary>
    private static bool ContainsFieldKeywordInExpression(ExpressionNode expr)
    {
        return ContainsFieldKeywordInNode(expr);
    }

    /// <summary>
    /// Recursively checks whether a syntax node tree contains a <c>field</c> keyword expression.
    /// </summary>
    private static bool ContainsFieldKeywordInNode(Storm.Runtime.SyntaxNode node)
    {
        if (node is FieldKeywordExpressionPrimaryNode)
            return true;

        foreach (var child in node.Children())
        {
            if (child is not null && ContainsFieldKeywordInNode(child))
                return true;
        }
        return false;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Pass 1e — Validate intrinsic markers (Task 9)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Validates the <c>intrinsic</c> markers propagated by Pass 1 onto types
    /// and members. Emits three diagnostics:
    /// <list type="bullet">
    /// <item>
    /// <description><c>MIST_SB_0030</c> — an intrinsic member has a body
    /// (Requirement 5.3). Intrinsic members must terminate with <c>;</c>
    /// because their bodies are supplied by the backend.</description>
    /// </item>
    /// <item>
    /// <description><c>MIST_SB_0031</c> — the <c>intrinsic</c> modifier
    /// appears in a non-stdlib compilation unit (Requirements 5.6 &amp; 5.13).
    /// Fires on both type-level and member-level misuse. Until Task 26
    /// introduces <c>CompilationUnit.IsStdlib</c>, every unit is treated as
    /// non-stdlib, so any occurrence of <c>intrinsic</c> currently triggers
    /// this diagnostic.</description>
    /// </item>
    /// <item>
    /// <description><c>MIST_SB_0032</c> — a member-level <c>intrinsic</c> sits
    /// inside a type that is not itself declared <c>intrinsic</c>
    /// (Requirement 5.15). Intrinsic members are always hosted on
    /// Backend_Backed_Types.</description>
    /// </item>
    /// </list>
    /// </summary>
    private static void Pass1e_ValidateIntrinsicMarkers(
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        SymbolTable symbolTable,
        MistCompilation compilation,
        DiagnosticBag diagnostics)
    {
        // Iterate class and struct type symbols. For each, check the
        // type-level intrinsic marker (MIST_SB_0031), then walk its member
        // scope to check each member-level intrinsic marker.
        foreach (var typeSymbol in symbolTable.AllSymbols)
        {
            if (typeSymbol.Kind != SymbolKind.Class && typeSymbol.Kind != SymbolKind.Struct)
                continue;

            var containingType = typeSymbol.DeclaredType;
            if (containingType is null)
                continue;

            var isBackendBacked = containingType switch
            {
                ClassType ct => ct.IsBackendBacked,
                StructType st => st.IsBackendBacked,
                _ => false,
            };

            // MIST_SB_0031 (type-level): the 'intrinsic' modifier is reserved
            // for the Standard Library. Units loaded via StdlibLoader carry
            // IsStdlib=true; user-code units do not, so this fires only on
            // user code.
            if (isBackendBacked && !IsStdlibSymbol(typeSymbol, symbolTable, asts, compilation))
            {
                var declaringNode = symbolTable.GetDeclaringNode(typeSymbol);
                if (declaringNode is not null)
                {
                    var sourceText = FindSourceTextForNode(declaringNode, asts, compilation);
                    diagnostics.Add(MakeDiagnosticWithLocation(
                        IntrinsicReservedForStdlib.Value,
                        DiagnosticSeverity.Error,
                        "The 'intrinsic' modifier is reserved for the Standard Library and cannot be used in user code.",
                        declaringNode,
                        sourceText));
                }
            }

            // Walk the type's members to check member-level intrinsic markers.
            var memberScope = containingType switch
            {
                ClassType ct => ct.MemberScope,
                StructType st => st.MemberScope,
                _ => null,
            };
            if (memberScope is null)
                continue;

            foreach (var member in memberScope.Members)
            {
                if (member is not Symbol memberSymbol || !memberSymbol.IsIntrinsic)
                    continue;

                // Only validate members that can carry 'intrinsic': methods,
                // constructors, and operators (operators are registered as
                // SymbolKind.Method with a mangled "operator<sym>" name).
                if (memberSymbol.Kind != SymbolKind.Method
                    && memberSymbol.Kind != SymbolKind.Constructor)
                {
                    continue;
                }

                var memberDeclaringNode = symbolTable.GetDeclaringNode(memberSymbol);
                var memberSourceText = memberDeclaringNode is null
                    ? null
                    : FindSourceTextForNode(memberDeclaringNode, asts, compilation);

                // MIST_SB_0030 — intrinsic member with a body.
                if (memberSymbol.HasBody && memberDeclaringNode is not null)
                {
                    diagnostics.Add(MakeDiagnosticWithLocation(
                        IntrinsicMemberHasBody.Value,
                        DiagnosticSeverity.Error,
                        $"Intrinsic member '{typeSymbol.Name}.{memberSymbol.Name}' must not have a body; use ';' to mark it as backend-provided.",
                        memberDeclaringNode,
                        memberSourceText));
                }

                // MIST_SB_0031 — member-level 'intrinsic' in user code.
                if (!IsStdlibSymbol(typeSymbol, symbolTable, asts, compilation) && memberDeclaringNode is not null)
                {
                    diagnostics.Add(MakeDiagnosticWithLocation(
                        IntrinsicReservedForStdlib.Value,
                        DiagnosticSeverity.Error,
                        "The 'intrinsic' modifier is reserved for the Standard Library and cannot be used in user code.",
                        memberDeclaringNode,
                        memberSourceText));
                }

                // MIST_SB_0032 — member-level 'intrinsic' inside a
                // non-intrinsic containing type (Requirement 5.15).
                if (!isBackendBacked && memberDeclaringNode is not null)
                {
                    diagnostics.Add(MakeDiagnosticWithLocation(
                        MemberIntrinsicRequiresTypeIntrinsic.Value,
                        DiagnosticSeverity.Error,
                        $"Member-level 'intrinsic' is only permitted inside a type that is itself declared 'intrinsic'. Type '{typeSymbol.Name}' is not intrinsic.",
                        memberDeclaringNode,
                        memberSourceText));
                }
            }
        }
    }

    /// <summary>
    /// Returns <c>true</c> if the type declared by <paramref name="typeSymbol"/>
    /// resides in a Standard Library compilation unit — i.e., the
    /// <see cref="CompilationUnit"/> whose AST contains the symbol's declaring
    /// node has <see cref="CompilationUnit.IsStdlib"/> set to <c>true</c>.
    /// Units loaded via <see cref="StdlibLoader"/> carry this flag; user-code
    /// units do not. Gates <c>MIST_SB_0031</c> (Requirements 5.6, 5.13) so the
    /// diagnostic fires only on user code.
    /// </summary>
    private static bool IsStdlibSymbol(
        ISymbol typeSymbol,
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation)
    {
        // Locate the declaring AST node for this type symbol, then walk the
        // ASTs using reference-equality containment (the same mechanism that
        // powers FindCompilationUnitPathForSymbol) to find the matching
        // CompilationUnit and read its IsStdlib flag. Reference equality is
        // reliable across files; span-based matching is not (every file's
        // span starts at 0).
        var declaringNode = symbolTable.GetDeclaringNode(typeSymbol);
        if (declaringNode is null)
            return false;

        for (var i = 0; i < asts.Length; i++)
        {
            if (asts[i] is CompilationUnitNode cu && ContainsTypeDeclarationNode(cu, declaringNode))
            {
                return i < compilation.Units.Length && compilation.Units[i].IsStdlib;
            }
        }

        return false;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Pass 1e — Validate operator-declaration shape (Task 12)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Binary operator symbols that require exactly 2 parameters
    /// (Requirement 4.3, 4.5). The <c>-</c> symbol is omitted here because it
    /// is overloaded binary/unary and disambiguated by parameter count below.
    /// </summary>
    private static readonly HashSet<string> BinaryOperatorSymbols = new()
    {
        "+", "*", "/", "%", "==", "!=", "<", ">", "<=", ">=", "&&", "||",
    };

    /// <summary>
    /// Unary operator symbols that require exactly 1 parameter
    /// (Requirement 4.4). The <c>-</c> symbol is omitted here because it is
    /// overloaded binary/unary.
    /// </summary>
    private static readonly HashSet<string> UnaryOperatorSymbols = new()
    {
        "!",
    };

    /// <summary>
    /// Validates the shape of every registered operator declaration and
    /// emits diagnostics for:
    /// <list type="bullet">
    /// <item>
    /// <description><c>MIST_SB_0033</c> — operator declared outside a class
    /// or struct body (Requirement 4.7). This is currently unreachable from
    /// parsed source because the grammar already restricts
    /// <c>OperatorDeclaration</c> to appear only within
    /// <c>MemberDeclaration</c>. The diagnostic ID is registered defensively
    /// so a future grammar change that admits top-level operators inherits
    /// the validation automatically.</description>
    /// </item>
    /// <item>
    /// <description><c>MIST_SB_0034</c> — binary operator without exactly 2
    /// parameters, or unary operator without exactly 1 parameter
    /// (Requirements 4.8, 4.9). Arity rules: <c>+ * / % == != &lt; &gt; &lt;= &gt;= &amp;&amp; ||</c>
    /// are binary; <c>!</c> is unary; <c>-</c> is overloaded (2 params → binary,
    /// 1 param → unary, anything else treated as binary when count &gt; 1 or
    /// unary when count ≤ 1).</description>
    /// </item>
    /// <item>
    /// <description><c>MIST_SB_0035</c> — operator not declared
    /// <c>public static</c> (Requirement 4.2). Both modifiers must be present
    /// in the declaration's modifier list.</description>
    /// </item>
    /// </list>
    /// </summary>
    private static void Pass1e_ValidateOperatorShape(
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        SymbolTable symbolTable,
        MistCompilation compilation,
        DiagnosticBag diagnostics)
    {
        // Walk every registered symbol whose declaring node is an
        // OperatorDeclarationNode. Operator symbols are registered with
        // SymbolKind.Method and a mangled "operator<sym>" name, so filter by
        // declaring-node shape rather than by name to avoid collisions with
        // user methods that happen to start with "operator".
        foreach (var symbol in symbolTable.AllSymbols)
        {
            if (symbolTable.GetDeclaringNode(symbol) is not OperatorDeclarationNode opDecl)
                continue;

            var sourceText = FindSourceTextForNode(opDecl, asts, compilation);
            var opLexeme = GetOperatorSymbolLexeme(opDecl.Symbol);

            // Parameter count: the ParameterList rule is
            // `Parameter (COMMA Parameter)*`, so count = 1 + Parameters.Length
            // when non-null (one for the `Parameter` head, plus each COMMA-
            // separated `Parameter` in the tail), or 0 when the list is absent.
            var paramCount = opDecl.Parameters is null
                ? 0
                : 1 + opDecl.Parameters.Parameters.Length;

            // Determine the operator's expected arity. For `-` (overloaded
            // binary/unary), disambiguate by the declared parameter count:
            // a count > 1 is diagnosed as a binary-arity mismatch (because
            // the author was likely declaring subtraction), and a count ≤ 1
            // is diagnosed as a unary-arity mismatch.
            bool isBinary;
            if (opLexeme == "-")
            {
                isBinary = paramCount > 1;
            }
            else if (BinaryOperatorSymbols.Contains(opLexeme))
            {
                isBinary = true;
            }
            else if (UnaryOperatorSymbols.Contains(opLexeme))
            {
                isBinary = false;
            }
            else
            {
                // Unknown operator symbol (grammar forbids this); skip shape
                // validation so we don't produce a misleading diagnostic.
                continue;
            }

            var expectedArity = isBinary ? 2 : 1;

            // MIST_SB_0034 — arity mismatch (Requirements 4.8 & 4.9).
            if (paramCount != expectedArity)
            {
                var message = isBinary
                    ? $"Binary operator '{opLexeme}' must declare exactly 2 parameters; found {paramCount}."
                    : $"Unary operator '{opLexeme}' must declare exactly 1 parameter; found {paramCount}.";

                diagnostics.Add(MakeDiagnosticWithLocation(
                    OperatorArityMismatch.Value,
                    DiagnosticSeverity.Error,
                    message,
                    opDecl,
                    sourceText));
            }

            // MIST_SB_0035 — operator must be declared `public static`
            // (Requirement 4.2). Both modifiers must be present.
            var hasPublic = false;
            var hasStatic = false;
            foreach (var modifier in opDecl.Modifiers)
            {
                if (modifier is PublicModifierModifierNode) hasPublic = true;
                if (modifier is StaticModifierModifierNode) hasStatic = true;
            }

            if (!hasPublic || !hasStatic)
            {
                diagnostics.Add(MakeDiagnosticWithLocation(
                    OperatorMustBePublicStatic.Value,
                    DiagnosticSeverity.Error,
                    $"Operator '{opLexeme}' must be declared 'public static'.",
                    opDecl,
                    sourceText));
            }

            // MIST_SB_0033 — operator declared outside a class/struct body
            // (Requirement 4.7). The grammar already restricts
            // OperatorDeclaration to MemberDeclaration (see Mist.storm), so
            // every operator reaches this pass inside a type's member scope
            // and this check is unreachable from parsed source today. It is
            // wired defensively so a future grammar change admitting top-
            // level operators would inherit validation automatically. We
            // detect a top-level operator by checking whether the symbol's
            // containing scope is the member scope of a registered class or
            // struct type. If no registered type claims this scope, the
            // operator is not inside any type body.
            if (!IsScopeInsideRegisteredType(symbol.ContainingScope, symbolTable))
            {
                diagnostics.Add(MakeDiagnosticWithLocation(
                    OperatorOutsideTypeBody.Value,
                    DiagnosticSeverity.Error,
                    "Operators must be declared as 'public static' members of a class or struct.",
                    opDecl,
                    sourceText));
            }
        }
    }

    /// <summary>
    /// Returns <c>true</c> if <paramref name="scope"/> (or any of its
    /// ancestor scopes) is the member scope of a class or struct type
    /// registered in <paramref name="symbolTable"/>. Used by
    /// <see cref="Pass1e_ValidateOperatorShape"/> as the trigger for
    /// <c>MIST_SB_0033</c>. The grammar forbids operator declarations
    /// outside a type body today, so every reachable operator symbol
    /// satisfies this condition; the helper exists to guarantee the
    /// diagnostic would still fire on a future grammar relaxation.
    /// </summary>
    private static bool IsScopeInsideRegisteredType(
        IScope? scope,
        SymbolTable symbolTable)
    {
        for (var s = scope; s is not null; s = s.Parent)
        {
            foreach (var typeSymbol in symbolTable.AllSymbols)
            {
                if (typeSymbol.Kind != SymbolKind.Class && typeSymbol.Kind != SymbolKind.Struct)
                    continue;

                var memberScope = typeSymbol.DeclaredType switch
                {
                    ClassType ct => ct.MemberScope,
                    StructType st => st.MemberScope,
                    _ => null,
                };

                if (memberScope is not null && ReferenceEquals(memberScope, s))
                    return true;
            }
        }

        return false;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Pass 1e — Validate cast-operator-declaration shape (Task 3.2)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Validates the shape of every registered cast operator declaration and
    /// emits diagnostics for:
    /// <list type="bullet">
    /// <item>
    /// <description><c>MIST_SB_0040</c> — cast operator parameter count is
    /// not exactly 1 (Requirements 6.3, 6.4).</description>
    /// </item>
    /// <item>
    /// <description><c>MIST_SB_0041</c> — cast operator not declared
    /// <c>public static</c> (Requirement 6.1).</description>
    /// </item>
    /// <item>
    /// <description><c>MIST_SB_0042</c> — cast operator declared outside a
    /// class or struct body (Requirement 6.1). Unreachable from parsed source
    /// because the grammar restricts <c>CastOperatorDeclaration</c> to
    /// <c>MemberDeclaration</c>; registered defensively.</description>
    /// </item>
    /// <item>
    /// <description><c>MIST_SB_0043</c> — duplicate cast operator (same kind
    /// and same target type name) in the same type (Requirement 6.5).
    /// Name-based only; full type-based duplicate detection is deferred to
    /// the type checker.</description>
    /// </item>
    /// </list>
    /// </summary>
    private static void Pass1e_ValidateCastOperatorShape(
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        SymbolTable symbolTable,
        MistCompilation compilation,
        DiagnosticBag diagnostics)
    {
        // Track seen cast operator encoded names per containing scope for
        // duplicate detection. Key = containing scope reference, Value = set
        // of encoded names already seen (e.g., "operator_implicit_Weight").
        var seenPerScope = new Dictionary<IScope, HashSet<string>>(ReferenceEqualityComparer.Instance);

        foreach (var symbol in symbolTable.AllSymbols)
        {
            if (symbolTable.GetDeclaringNode(symbol) is not CastOperatorDeclarationNode castOpDecl)
                continue;

            var sourceText = FindSourceTextForNode(castOpDecl, asts, compilation);

            // ── MIST_SB_0040: Parameter count must be exactly 1 ──────────
            // The ParameterList rule is `Parameter (COMMA Parameter)*`, so
            // count = 1 + Parameters.Length when non-null, or 0 when absent.
            var paramCount = castOpDecl.Parameters is null
                ? 0
                : 1 + castOpDecl.Parameters.Parameters.Length;

            if (paramCount != 1)
            {
                diagnostics.Add(MakeDiagnosticWithLocation(
                    CastOperatorArityMismatch.Value,
                    DiagnosticSeverity.Error,
                    $"Cast operator must have exactly one parameter; found {paramCount}.",
                    castOpDecl,
                    sourceText));
            }

            // ── MIST_SB_0041: Must be declared `public static` ──────────
            var hasPublic = false;
            var hasStatic = false;
            foreach (var modifier in castOpDecl.Modifiers)
            {
                if (modifier is PublicModifierModifierNode) hasPublic = true;
                if (modifier is StaticModifierModifierNode) hasStatic = true;
            }

            if (!hasPublic || !hasStatic)
            {
                diagnostics.Add(MakeDiagnosticWithLocation(
                    CastOperatorMustBePublicStatic.Value,
                    DiagnosticSeverity.Error,
                    "Cast operator must be declared 'public static'.",
                    castOpDecl,
                    sourceText));
            }

            // ── MIST_SB_0042: Must be inside a class or struct ──────────
            if (!IsScopeInsideRegisteredType(symbol.ContainingScope, symbolTable))
            {
                diagnostics.Add(MakeDiagnosticWithLocation(
                    CastOperatorOutsideTypeBody.Value,
                    DiagnosticSeverity.Error,
                    "Cast operators must be declared inside a class or struct.",
                    castOpDecl,
                    sourceText));
            }

            // ── MIST_SB_0043: Duplicate cast operator detection ─────────
            // Two cast operators in the same type with the same encoded name
            // (same kind + same target type name) are duplicates.
            if (symbol.ContainingScope is not null)
            {
                if (!seenPerScope.TryGetValue(symbol.ContainingScope, out var seenNames))
                {
                    seenNames = new HashSet<string>();
                    seenPerScope[symbol.ContainingScope] = seenNames;
                }

                if (!seenNames.Add(symbol.Name))
                {
                    // Extract kind and type names for the diagnostic message.
                    var kindPrefix = castOpDecl.CastKind switch
                    {
                        ImplicitCastCastOperatorKindNode => "implicit",
                        ExplicitCastCastOperatorKindNode => "explicit",
                        _ => "unknown",
                    };
                    var targetTypeName = ExtractTypeReferenceName(castOpDecl.TargetType, sourceText);

                    // Extract source type name from the parameter (if present).
                    var sourceTypeName = castOpDecl.Parameters is not null
                        ? ExtractTypeReferenceName(castOpDecl.Parameters.Parameter.Type, sourceText)
                        : "?";

                    diagnostics.Add(MakeDiagnosticWithLocation(
                        DuplicateCastOperator.Value,
                        DiagnosticSeverity.Error,
                        $"Duplicate {kindPrefix} cast operator from '{sourceTypeName}' to '{targetTypeName}'.",
                        castOpDecl,
                        sourceText));
                }
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Pass 1b — Resolve base types for class inheritance
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// After all types are registered in Pass 1, resolve base type references
    /// for classes with inheritance clauses. This runs as a separate step so
    /// that forward references to base classes are supported.
    /// </summary>
    private static void Pass1_ResolveBaseTypes(
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        Scope globalScope,
        SymbolTable symbolTable,
        MistCompilation compilation,
        DiagnosticBag diagnostics)
    {
        // Walk all registered symbols to find class types and their AST nodes.
        foreach (var symbol in symbolTable.AllSymbols)
        {
            if (symbol.Kind != SymbolKind.Class || symbol.DeclaredType is not ClassType classType)
            {
                continue;
            }

            var declaringNode = symbolTable.GetDeclaringNode(symbol);
            if (declaringNode is not ClassDeclarationNode classDecl)
            {
                continue;
            }

            // Check if this class has an inheritance clause.
            if (classDecl.BaseList is null)
            {
                continue;
            }

            var baseClassName = classDecl.BaseList.First.Name.Lexeme;
            var parentScope = symbol.ContainingScope;

            // Look up the base class name in the parent scope.
            var resolvedSymbol = parentScope.Lookup(baseClassName);

            if (resolvedSymbol is null)
            {
                // Determine the source text for the diagnostic.
                var sourceText = FindSourceTextForClassDecl(classDecl, asts, compilation);

                diagnostics.Add(MakeDiagnosticWithLocation(
                    "MIST_UNDEFINED_BASE_TYPE",
                    DiagnosticSeverity.Error,
                    $"The base type '{baseClassName}' could not be found.",
                    classDecl,
                    sourceText));
            }
            else if (resolvedSymbol.DeclaredType is StructType)
            {
                var sourceText = FindSourceTextForClassDecl(classDecl, asts, compilation);

                diagnostics.Add(MakeDiagnosticWithLocation(
                    "MIST_CANNOT_INHERIT_STRUCT",
                    DiagnosticSeverity.Error,
                    $"Class '{classType.DisplayName}' cannot inherit from struct '{baseClassName}'.",
                    classDecl,
                    sourceText));
            }
            else if (resolvedSymbol.DeclaredType is ClassType resolvedClassType)
            {
                var typeArgs = classDecl.BaseList.First.TypeArgs;
                classType.BaseType = ResolveGenericTypeReference(
                    resolvedClassType, typeArgs, baseClassName,
                    classType, classDecl, asts, compilation, diagnostics);
            }
        }
    }

    /// <summary>
    /// Resolves a type reference in an inheritance context (base clause or interface list),
    /// handling type arguments to produce a <see cref="ConstructedType"/> when appropriate.
    /// Emits diagnostics for arity mismatches, non-generic type arguments, and missing type arguments.
    /// </summary>
    private static IType ResolveGenericTypeReference(
        IType resolvedType,
        TypeArgumentListNode? typeArgs,
        string typeName,
        IType declaringType,
        Storm.Runtime.SyntaxNode declaringNode,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation,
        DiagnosticBag diagnostics)
    {
        // Get the type parameters from the resolved type.
        var typeParams = GetTypeParameters(resolvedType);
        var hasTypeArgs = typeArgs is not null;
        var typeParamCount = typeParams.Count;

        if (hasTypeArgs)
        {
            // Type arguments were provided.
            if (typeParamCount == 0)
            {
                // The referenced type is not generic — emit MIST_NOT_GENERIC.
                var sourceText = FindSourceTextForNode(declaringNode, asts, compilation);
                diagnostics.Add(MakeDiagnosticWithLocation(
                    DiagnosticId.NotGeneric.Value,
                    DiagnosticSeverity.Error,
                    $"Type '{typeName}' is not generic and does not accept type arguments.",
                    declaringNode,
                    sourceText));
                return resolvedType;
            }

            // Resolve the type arguments.
            var resolvedTypeArgs = ResolveTypeArgumentList(typeArgs!, declaringType, declaringNode, asts, compilation, diagnostics);
            var typeArgCount = resolvedTypeArgs.Count;

            if (typeArgCount != typeParamCount)
            {
                // Arity mismatch — emit MIST_ARITY_MISMATCH.
                var sourceText = FindSourceTextForNode(declaringNode, asts, compilation);
                diagnostics.Add(MakeDiagnosticWithLocation(
                    DiagnosticId.ArityMismatch.Value,
                    DiagnosticSeverity.Error,
                    $"Type '{typeName}' expects {typeParamCount} type argument(s) but received {typeArgCount}.",
                    declaringNode,
                    sourceText));
                return resolvedType;
            }

            // Construct the ConstructedType.
            return new ConstructedType(resolvedType, resolvedTypeArgs);
        }
        else
        {
            // No type arguments provided.
            if (typeParamCount > 0)
            {
                // The referenced type is generic but no type arguments were provided.
                // Check if the declaring type has matching type parameters that could
                // be forwarded (e.g., class Foo<T> : Bar<T> is handled when type args
                // are explicitly provided). If the declaring type is non-generic,
                // emit MIST_MISSING_TYPE_ARGUMENTS.
                var declaringTypeParams = GetTypeParameters(declaringType);
                if (declaringTypeParams.Count == 0)
                {
                    var sourceText = FindSourceTextForNode(declaringNode, asts, compilation);
                    diagnostics.Add(MakeDiagnosticWithLocation(
                        DiagnosticId.MissingTypeArguments.Value,
                        DiagnosticSeverity.Error,
                        $"Generic type '{typeName}' requires type arguments.",
                        declaringNode,
                        sourceText));
                }
            }

            return resolvedType;
        }
    }

    /// <summary>
    /// Resolves a <see cref="TypeArgumentListNode"/> to a list of <see cref="IType"/> instances.
    /// Each type argument name is looked up in the declaring type's member scope (for type parameters)
    /// and in the parent scope (for concrete types).
    /// </summary>
    private static IReadOnlyList<IType> ResolveTypeArgumentList(
        TypeArgumentListNode typeArgList,
        IType declaringType,
        Storm.Runtime.SyntaxNode declaringNode,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation,
        DiagnosticBag diagnostics)
    {
        var resolvedArgs = new List<IType>();

        // Resolve the first type argument.
        var firstType = ResolveTypeArgFromTypeReference(typeArgList.TypeReference, declaringType, declaringNode, asts, compilation, diagnostics);
        resolvedArgs.Add(firstType);

        // Resolve the rest of the type arguments.
        foreach (var typeRef in typeArgList.TypeReferences)
        {
            var argType = ResolveTypeArgFromTypeReference(typeRef, declaringType, declaringNode, asts, compilation, diagnostics);
            resolvedArgs.Add(argType);
        }

        return resolvedArgs;
    }

    /// <summary>
    /// Resolves a single type argument from a <see cref="TypeReferenceNode"/> in an inheritance context.
    /// Handles primitive types, type parameter references, and named types.
    /// </summary>
    private static IType ResolveTypeArgFromTypeReference(
        TypeReferenceNode typeRef,
        IType declaringType,
        Storm.Runtime.SyntaxNode declaringNode,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation,
        DiagnosticBag diagnostics)
    {
        switch (typeRef)
        {
            case PrimitiveTypeRefTypeReferenceNode primitiveRef:
                var baseType = ResolvePrimitiveType(primitiveRef.PrimitiveType);
                if (primitiveRef.ArrayRank.HasValue)
                    return new ArrayType(baseType);
                return baseType;

            case VoidTypeRefTypeReferenceNode:
                return LegacyPrimitiveTypes.Void;

            case NamedTypeRefTypeReferenceNode namedRef:
            {
                // The generated parser may not populate IDENTIFIER.Lexeme on
                // QualifiedIdentifierNode. Fall back to extracting the name
                // from the source text span when the lexeme is empty.
                var argName = namedRef.Name.IDENTIFIER.Lexeme;
                if (string.IsNullOrEmpty(argName))
                {
                    var span = namedRef.Name.Span;
                    if (span.Length > 0)
                    {
                        // Find the correct source text by matching the declaring
                        // node to its compilation unit via containment check
                        // (span-based matching is unreliable across files).
                        var srcTextForArg = FindSourceTextByContainment(declaringNode, asts, compilation);
                        if (srcTextForArg is not null && span.Start + span.Length <= srcTextForArg.Contents.Length)
                        {
                            argName = srcTextForArg.Contents.Substring(span.Start, span.Length);
                        }
                    }
                }

                // First, look up in the declaring type's member scope (for type parameters).
                var memberScope = GetMemberScope(declaringType);
                ISymbol? resolvedSymbol = memberScope?.Lookup(argName);

                if (resolvedSymbol is not null && resolvedSymbol.DeclaredType is TypeParameterType tpt)
                {
                    return tpt;
                }

                // Look up in the parent scope chain (for concrete types).
                if (resolvedSymbol is null && memberScope?.Parent is not null)
                {
                    resolvedSymbol = memberScope.Parent.Lookup(argName);
                }

                if (resolvedSymbol is not null && resolvedSymbol.DeclaredType is not null)
                {
                    var resolvedType = resolvedSymbol.DeclaredType;

                    // If this type argument itself has type arguments (nested generics),
                    // resolve them recursively.
                    if (namedRef.TypeArgs is not null)
                    {
                        var nestedTypeParams = GetTypeParameters(resolvedType);
                        if (nestedTypeParams.Count == 0)
                        {
                            var sourceText = FindSourceTextForNode(declaringNode, asts, compilation);
                            diagnostics.Add(MakeDiagnosticWithLocation(
                                DiagnosticId.NotGeneric.Value,
                                DiagnosticSeverity.Error,
                                $"Type '{argName}' is not generic and does not accept type arguments.",
                                declaringNode,
                                sourceText));
                            return resolvedType;
                        }

                        var nestedArgs = ResolveTypeArgumentList(namedRef.TypeArgs, declaringType, declaringNode, asts, compilation, diagnostics);
                        if (nestedArgs.Count != nestedTypeParams.Count)
                        {
                            var sourceText = FindSourceTextForNode(declaringNode, asts, compilation);
                            diagnostics.Add(MakeDiagnosticWithLocation(
                                DiagnosticId.ArityMismatch.Value,
                                DiagnosticSeverity.Error,
                                $"Type '{argName}' expects {nestedTypeParams.Count} type argument(s) but received {nestedArgs.Count}.",
                                declaringNode,
                                sourceText));
                            return resolvedType;
                        }

                        return new ConstructedType(resolvedType, nestedArgs);
                    }

                    if (namedRef.ArrayRank.HasValue)
                        return new ArrayType(resolvedType);

                    return resolvedType;
                }

                // Type argument could not be resolved.
                var srcText = FindSourceTextForNode(declaringNode, asts, compilation);
                diagnostics.Add(MakeDiagnosticWithLocation(
                    DiagnosticId.UndefinedTypeArgument.Value,
                    DiagnosticSeverity.Error,
                    $"Type argument '{argName}' could not be resolved.",
                    declaringNode,
                    srcText));
                return UnknownType.Instance;
            }

            default:
                return UnknownType.Instance;
        }
    }

    /// <summary>
    /// Gets the type parameters from a type (ClassType, StructType, or InterfaceType).
    /// Returns an empty list for types that don't have type parameters.
    /// </summary>
    private static IReadOnlyList<TypeParameterType> GetTypeParameters(IType type)
    {
        return type switch
        {
            ClassType ct => ct.TypeParameters,
            StructType st => st.TypeParameters,
            InterfaceType it => it.TypeParameters,
            _ => Array.Empty<TypeParameterType>(),
        };
    }

    /// <summary>
    /// Gets the member scope from a type (ClassType, StructType, or InterfaceType).
    /// Returns null for types that don't have a member scope.
    /// </summary>
    private static IScope? GetMemberScope(IType type)
    {
        return type switch
        {
            ClassType ct => ct.MemberScope,
            StructType st => st.MemberScope,
            InterfaceType it => it.MemberScope,
            _ => null,
        };
    }

    /// <summary>
    /// Finds the <see cref="SourceText"/> for any syntax node by walking the ASTs.
    /// </summary>
    private static SourceText? FindSourceTextForNode(
        Storm.Runtime.SyntaxNode node,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation)
    {
        // Use the node's span to find which compilation unit it belongs to.
        for (var i = 0; i < asts.Length; i++)
        {
            if (asts[i] is CompilationUnitNode cu)
            {
                // Check if the node's span falls within this compilation unit's span.
                if (node.Span.Start >= cu.Span.Start && node.Span.Start < cu.Span.Start + cu.Span.Length)
                {
                    return i < compilation.Units.Length ? compilation.Units[i].Source : null;
                }
            }
        }

        // Fallback: try to find by checking if the node is contained in any type declaration.
        for (var i = 0; i < asts.Length; i++)
        {
            if (i < compilation.Units.Length)
            {
                return compilation.Units[i].Source;
            }
        }

        return null;
    }

    /// <summary>
    /// Finds the <see cref="SourceText"/> for a type declaration node by checking
    /// which compilation unit contains it via reference-equality containment check.
    /// This is more reliable than span-based matching when multiple files have
    /// overlapping spans.
    /// </summary>
    private static SourceText? FindSourceTextByContainment(
        Storm.Runtime.SyntaxNode declaringNode,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation)
    {
        for (var i = 0; i < asts.Length; i++)
        {
            if (asts[i] is CompilationUnitNode cu && ContainsTypeDeclarationNode(cu, declaringNode))
            {
                return i < compilation.Units.Length ? compilation.Units[i].Source : null;
            }
        }

        // Fallback to span-based matching.
        return FindSourceTextForNode(declaringNode, asts, compilation);
    }

    /// <summary>
    /// Finds the compilation unit path for a class declaration node by walking
    /// the ASTs to find which compilation unit contains the node.
    /// </summary>
    private static string? FindCompilationUnitPath(
        ClassDeclarationNode classDecl,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation)
    {
        for (var i = 0; i < asts.Length; i++)
        {
            if (asts[i] is CompilationUnitNode cu && ContainsClassDeclaration(cu, classDecl))
            {
                return i < compilation.Units.Length ? compilation.Units[i].Source?.Path : null;
            }
        }
        return null;
    }

    /// <summary>
    /// Finds the <see cref="SourceText"/> for a class declaration node by walking
    /// the ASTs to find which compilation unit contains the node.
    /// </summary>
    private static SourceText? FindSourceTextForClassDecl(
        ClassDeclarationNode classDecl,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation)
    {
        for (var i = 0; i < asts.Length; i++)
        {
            if (asts[i] is CompilationUnitNode cu && ContainsClassDeclaration(cu, classDecl))
            {
                return i < compilation.Units.Length ? compilation.Units[i].Source : null;
            }
        }
        return null;
    }

    /// <summary>
    /// Checks whether a compilation unit contains the given class declaration.
    /// </summary>
    private static bool ContainsClassDeclaration(CompilationUnitNode cu, ClassDeclarationNode classDecl)
    {
        foreach (var item in cu.Items)
        {
            switch (item)
            {
                case TypeItemTopLevelItemNode typeItem:
                    if (typeItem.TypeDeclaration is ClassTypeTypeDeclarationNode ct && ReferenceEquals(ct.ClassDeclaration, classDecl))
                        return true;
                    break;
                case NamespaceItemTopLevelItemNode nsItem:
                    var namespaceTypes = GetNamespaceBodyTypes(nsItem.NamespaceDeclaration);
                    foreach (var typeDecl in namespaceTypes)
                    {
                        if (typeDecl is ClassTypeTypeDeclarationNode nsCt && ReferenceEquals(nsCt.ClassDeclaration, classDecl))
                            return true;
                    }
                    break;
            }
        }

        return false;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Pass 1b (cont.) — Resolve interface lists on all types
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// After all types are registered and base types are resolved, resolve
    /// interface lists on classes, structs, and interfaces.
    ///
    /// For classes: the first name in the base clause (<c>BaseClassName</c>)
    /// may be a base class (already resolved by <see cref="Pass1_ResolveBaseTypes"/>)
    /// or an interface. If it resolves to an <see cref="InterfaceType"/>, it is
    /// treated as an interface (no base class). The class's <c>Interfaces</c>
    /// property is set accordingly.
    ///
    /// For structs: the first name in the interface list (<c>InterfaceName</c>)
    /// is resolved to an <see cref="InterfaceType"/> and stored on
    /// <c>StructType.Interfaces</c>.
    ///
    /// For interfaces: the first name in the interface list (<c>InterfaceName</c>)
    /// is resolved to an <see cref="InterfaceType"/> and stored on
    /// <c>InterfaceType.Interfaces</c>.
    ///
    /// Emits <c>MIST_UNDEFINED_INTERFACE</c> when an interface name is not found,
    /// and <c>MIST_NOT_AN_INTERFACE</c> when a name resolves to a class or struct.
    /// </summary>
    private static void Pass1_ResolveInterfaceLists(
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation,
        DiagnosticBag diagnostics)
    {
        foreach (var symbol in symbolTable.AllSymbols)
        {
            switch (symbol.Kind)
            {
                case SymbolKind.Class when symbol.DeclaredType is ClassType classType:
                    ResolveClassInterfaceList(symbol, classType, symbolTable, asts, compilation, diagnostics);
                    break;

                case SymbolKind.Struct when symbol.DeclaredType is StructType structType:
                    ResolveStructInterfaceList(symbol, structType, symbolTable, asts, compilation, diagnostics);
                    break;

                case SymbolKind.Interface when symbol.DeclaredType is InterfaceType interfaceType:
                    ResolveInterfaceInterfaceList(symbol, interfaceType, symbolTable, asts, compilation, diagnostics);
                    break;
            }
        }
    }

    /// <summary>
    /// Resolves the interface list for a class declaration.
    /// The first name in the base clause (<c>BaseClassName</c>) may be a base class
    /// or an interface. If Pass1_ResolveBaseTypes already set <c>BaseType</c>, the
    /// first name is a base class and is skipped here. If <c>BaseType</c> is null
    /// and the base clause is present, the first name is checked as an interface.
    /// </summary>
    private static void ResolveClassInterfaceList(
        ISymbol classSymbol,
        ClassType classType,
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation,
        DiagnosticBag diagnostics)
    {
        var declaringNode = symbolTable.GetDeclaringNode(classSymbol);
        if (declaringNode is not ClassDeclarationNode classDecl)
            return;

        // No base clause means no base class and no interfaces.
        if (classDecl.BaseList is null)
            return;

        // Collect all BaseTypeRefNode entries from the base list.
        var allEntries = new List<BaseTypeRefNode>();
        allEntries.Add(classDecl.BaseList.First);
        foreach (var rest in classDecl.BaseList.BaseTypeRefs)
        {
            allEntries.Add(rest);
        }

        // If Pass1_ResolveBaseTypes already resolved a base class, the first
        // entry was consumed as the base class. Start from the second entry.
        var startIndex = classType.BaseType is not null ? 1 : 0;

        if (startIndex == 0)
        {
            // The first name was NOT resolved as a base class. Check if it's an interface.
            var firstName = allEntries[0].Name.Lexeme;
            if (string.IsNullOrEmpty(firstName))
                return;

            var parentScope = classSymbol.ContainingScope;
            var resolvedSymbol = parentScope.Lookup(firstName);

            if (resolvedSymbol is null)
            {
                // Already reported as MIST_UNDEFINED_BASE_TYPE by Pass1_ResolveBaseTypes. Skip.
                startIndex = 1;
            }
            else if (resolvedSymbol.DeclaredType is InterfaceType resolvedInterface)
            {
                var typeArgs = allEntries[0].TypeArgs;
                var resolvedType = ResolveGenericTypeReference(
                    resolvedInterface, typeArgs, firstName,
                    classType, classDecl, asts, compilation, diagnostics);
                classType.Interfaces = new List<IType> { resolvedType };
                startIndex = 1;
            }
            else if (resolvedSymbol.DeclaredType is StructType)
            {
                // Already reported as MIST_CANNOT_INHERIT_STRUCT by Pass1_ResolveBaseTypes.
                startIndex = 1;
            }
            else
            {
                // Resolved to a ClassType but BaseType is null (shouldn't happen normally).
                startIndex = 1;
            }
        }

        // Resolve remaining entries as interfaces.
        if (startIndex < allEntries.Count)
        {
            var parentScope = classSymbol.ContainingScope;
            var additionalInterfaces = ResolveInterfaceEntries(
                allEntries.Skip(startIndex),
                parentScope,
                classType,
                classDecl,
                symbolTable, asts, compilation, diagnostics,
                findPathForSymbol: classSymbol);

            if (additionalInterfaces.Count > 0)
            {
                var existing = classType.Interfaces.ToList();
                existing.AddRange(additionalInterfaces);
                classType.Interfaces = existing;
            }
        }
    }

    /// <summary>
    /// Resolves the interface list for a struct declaration.
    /// </summary>
    private static void ResolveStructInterfaceList(
        ISymbol structSymbol,
        StructType structType,
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation,
        DiagnosticBag diagnostics)
    {
        var declaringNode = symbolTable.GetDeclaringNode(structSymbol);
        if (declaringNode is not StructDeclarationNode structDecl)
            return;

        if (structDecl.Interfaces is null)
            return;

        var entries = ExtractBaseListEntries(structDecl.Interfaces);
        if (entries.Count == 0)
            return;

        var parentScope = structSymbol.ContainingScope;
        var resolvedInterfaces = ResolveInterfaceEntries(
            entries,
            parentScope,
            structType,
            structDecl,
            symbolTable, asts, compilation, diagnostics,
            findPathForSymbol: structSymbol);

        if (resolvedInterfaces.Count > 0)
        {
            structType.Interfaces = resolvedInterfaces;
        }
    }

    /// <summary>
    /// Resolves the interface list for an interface declaration.
    /// </summary>
    private static void ResolveInterfaceInterfaceList(
        ISymbol interfaceSymbol,
        InterfaceType interfaceType,
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation,
        DiagnosticBag diagnostics)
    {
        var declaringNode = symbolTable.GetDeclaringNode(interfaceSymbol);
        if (declaringNode is not InterfaceDeclarationNode interfaceDecl)
            return;

        if (interfaceDecl.Interfaces is null)
            return;

        var entries = ExtractBaseListEntries(interfaceDecl.Interfaces);
        if (entries.Count == 0)
            return;

        var parentScope = interfaceSymbol.ContainingScope;
        var resolvedInterfaces = ResolveInterfaceEntries(
            entries,
            parentScope,
            interfaceType,
            interfaceDecl,
            symbolTable, asts, compilation, diagnostics,
            findPathForSymbol: interfaceSymbol);

        if (resolvedInterfaces.Count > 0)
        {
            interfaceType.Interfaces = resolvedInterfaces;
        }
    }

    /// <summary>
    /// Resolves a set of interface names to <see cref="InterfaceType"/> instances.
    /// Emits diagnostics for undefined names and non-interface types.
    /// </summary>
    private static List<InterfaceType> ResolveInterfaceNames(
        IEnumerable<string> interfaceNames,
        IScope parentScope,
        string declaringTypeName,
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation,
        DiagnosticBag diagnostics,
        ISymbol findPathForSymbol)
    {
        var resolved = new List<InterfaceType>();

        foreach (var name in interfaceNames)
        {
            var resolvedSymbol = parentScope.Lookup(name);

            if (resolvedSymbol is null)
            {
                var sourceText = FindSourceTextForSymbol(
                    findPathForSymbol, symbolTable, asts, compilation);
                var declaringNode = symbolTable.GetDeclaringNode(findPathForSymbol);

                if (declaringNode is not null)
                {
                    diagnostics.Add(MakeDiagnosticWithLocation(
                        "MIST_UNDEFINED_INTERFACE",
                        DiagnosticSeverity.Error,
                        $"The interface type '{name}' could not be found.",
                        declaringNode,
                        sourceText));
                }
                else
                {
                    string? compilationUnitPath = FindCompilationUnitPathForSymbol(
                        findPathForSymbol, symbolTable, asts, compilation);

                    diagnostics.Add(new Diagnostic(
                        new DiagnosticId("MIST_UNDEFINED_INTERFACE"),
                        DiagnosticSeverity.Error,
                        PipelineStage.SemanticModelConstruction,
                        $"The interface type '{name}' could not be found.",
                        Location: null,
                        CompilationUnitPath: compilationUnitPath,
                        AnalyzerName: null));
                }
                continue;
            }

            if (resolvedSymbol.DeclaredType is InterfaceType resolvedInterface)
            {
                resolved.Add(resolvedInterface);
            }
            else
            {
                var sourceText = FindSourceTextForSymbol(
                    findPathForSymbol, symbolTable, asts, compilation);
                var declaringNode = symbolTable.GetDeclaringNode(findPathForSymbol);

                if (declaringNode is not null)
                {
                    diagnostics.Add(MakeDiagnosticWithLocation(
                        "MIST_NOT_AN_INTERFACE",
                        DiagnosticSeverity.Error,
                        $"'{name}' is not an interface; only interfaces can appear in an interface list.",
                        declaringNode,
                        sourceText));
                }
                else
                {
                    string? compilationUnitPath = FindCompilationUnitPathForSymbol(
                        findPathForSymbol, symbolTable, asts, compilation);

                    diagnostics.Add(new Diagnostic(
                        new DiagnosticId("MIST_NOT_AN_INTERFACE"),
                        DiagnosticSeverity.Error,
                        PipelineStage.SemanticModelConstruction,
                        $"'{name}' is not an interface; only interfaces can appear in an interface list.",
                        Location: null,
                        CompilationUnitPath: compilationUnitPath,
                        AnalyzerName: null));
                }
            }
        }

        return resolved;
    }

    /// <summary>
    /// Resolves a set of <see cref="BaseTypeRefNode"/> entries to interface types,
    /// handling type arguments to produce <see cref="ConstructedType"/> when appropriate.
    /// Emits diagnostics for undefined names and non-interface types.
    /// </summary>
    private static List<IType> ResolveInterfaceEntries(
        IEnumerable<BaseTypeRefNode> entries,
        IScope parentScope,
        IType declaringType,
        Storm.Runtime.SyntaxNode declaringNode,
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation,
        DiagnosticBag diagnostics,
        ISymbol findPathForSymbol)
    {
        var resolved = new List<IType>();

        foreach (var entry in entries)
        {
            var name = entry.Name.Lexeme;
            if (string.IsNullOrEmpty(name))
                continue;

            var resolvedSymbol = parentScope.Lookup(name);

            if (resolvedSymbol is null)
            {
                var sourceText = FindSourceTextForSymbol(
                    findPathForSymbol, symbolTable, asts, compilation);
                var declNode = symbolTable.GetDeclaringNode(findPathForSymbol);

                if (declNode is not null)
                {
                    diagnostics.Add(MakeDiagnosticWithLocation(
                        "MIST_UNDEFINED_INTERFACE",
                        DiagnosticSeverity.Error,
                        $"The interface type '{name}' could not be found.",
                        declNode,
                        sourceText));
                }
                else
                {
                    string? compilationUnitPath = FindCompilationUnitPathForSymbol(
                        findPathForSymbol, symbolTable, asts, compilation);

                    diagnostics.Add(new Diagnostic(
                        new DiagnosticId("MIST_UNDEFINED_INTERFACE"),
                        DiagnosticSeverity.Error,
                        PipelineStage.SemanticModelConstruction,
                        $"The interface type '{name}' could not be found.",
                        Location: null,
                        CompilationUnitPath: compilationUnitPath,
                        AnalyzerName: null));
                }
                continue;
            }

            if (resolvedSymbol.DeclaredType is InterfaceType resolvedInterface)
            {
                var resolvedType = ResolveGenericTypeReference(
                    resolvedInterface, entry.TypeArgs, name,
                    declaringType, declaringNode, asts, compilation, diagnostics);
                resolved.Add(resolvedType);
            }
            else
            {
                var sourceText = FindSourceTextForSymbol(
                    findPathForSymbol, symbolTable, asts, compilation);
                var declNode = symbolTable.GetDeclaringNode(findPathForSymbol);

                if (declNode is not null)
                {
                    diagnostics.Add(MakeDiagnosticWithLocation(
                        "MIST_NOT_AN_INTERFACE",
                        DiagnosticSeverity.Error,
                        $"'{name}' is not an interface; only interfaces can appear in an interface list.",
                        declNode,
                        sourceText));
                }
                else
                {
                    string? compilationUnitPath = FindCompilationUnitPathForSymbol(
                        findPathForSymbol, symbolTable, asts, compilation);

                    diagnostics.Add(new Diagnostic(
                        new DiagnosticId("MIST_NOT_AN_INTERFACE"),
                        DiagnosticSeverity.Error,
                        PipelineStage.SemanticModelConstruction,
                        $"'{name}' is not an interface; only interfaces can appear in an interface list.",
                        Location: null,
                        CompilationUnitPath: compilationUnitPath,
                        AnalyzerName: null));
                }
            }
        }

        return resolved;
    }

    /// <summary>
    /// Extracts all <see cref="BaseTypeRefNode"/> entries from a <see cref="BaseListNode"/>.
    /// </summary>
    private static List<BaseTypeRefNode> ExtractBaseListEntries(BaseListNode baseList)
    {
        var entries = new List<BaseTypeRefNode>();
        entries.Add(baseList.First);
        foreach (var rest in baseList.BaseTypeRefs)
        {
            entries.Add(rest);
        }
        return entries;
    }

    /// <summary>
    /// Finds the compilation unit path for any symbol by looking up its declaring
    /// node in the symbol table and walking the ASTs.
    /// </summary>
    private static string? FindCompilationUnitPathForSymbol(
        ISymbol symbol,
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation)
    {
        var declaringNode = symbolTable.GetDeclaringNode(symbol);
        if (declaringNode is null)
            return null;

        for (var i = 0; i < asts.Length; i++)
        {
            if (asts[i] is CompilationUnitNode cu && ContainsTypeDeclarationNode(cu, declaringNode))
            {
                return i < compilation.Units.Length ? compilation.Units[i].Source?.Path : null;
            }
        }

        return null;
    }

    /// <summary>
    /// Finds the <see cref="SourceText"/> for any symbol by looking up its declaring
    /// node in the symbol table and walking the ASTs.
    /// </summary>
    private static SourceText? FindSourceTextForSymbol(
        ISymbol symbol,
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation)
    {
        var declaringNode = symbolTable.GetDeclaringNode(symbol);
        if (declaringNode is null)
            return null;

        for (var i = 0; i < asts.Length; i++)
        {
            if (asts[i] is CompilationUnitNode cu && ContainsTypeDeclarationNode(cu, declaringNode))
            {
                return i < compilation.Units.Length ? compilation.Units[i].Source : null;
            }
        }

        return null;
    }

    /// <summary>
    /// Checks whether a compilation unit contains the given declaring node
    /// (class, struct, or interface declaration).
    /// </summary>
    private static bool ContainsTypeDeclarationNode(CompilationUnitNode cu, Storm.Runtime.SyntaxNode declaringNode)
    {
        foreach (var item in cu.Items)
        {
            switch (item)
            {
                case TypeItemTopLevelItemNode typeItem:
                    if (MatchesTypeDeclaration(typeItem.TypeDeclaration, declaringNode))
                        return true;
                    break;
                case NamespaceItemTopLevelItemNode nsItem:
                    var namespaceTypes = GetNamespaceBodyTypes(nsItem.NamespaceDeclaration);
                    foreach (var typeDecl in namespaceTypes)
                    {
                        if (MatchesTypeDeclaration(typeDecl, declaringNode))
                            return true;
                    }
                    break;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks whether a type declaration node wraps the given declaring node.
    /// </summary>
    private static bool MatchesTypeDeclaration(TypeDeclarationNode typeDecl, Storm.Runtime.SyntaxNode declaringNode)
    {
        return typeDecl switch
        {
            ClassTypeTypeDeclarationNode ct => ReferenceEquals(ct.ClassDeclaration, declaringNode),
            StructTypeTypeDeclarationNode st => ReferenceEquals(st.StructDeclaration, declaringNode),
            InterfaceTypeTypeDeclarationNode it => ReferenceEquals(it.InterfaceDeclaration, declaringNode),
            _ => false,
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Pass 1g — Resolve with-block declarations
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Resolves <c>with</c> block declarations collected during Pass 1.
    /// For each <see cref="WithDeclarationNode"/>:
    /// <list type="number">
    ///   <item>Resolve the type name to a <see cref="ClassType"/> or <see cref="StructType"/>.</item>
    ///   <item>Resolve the trait name to a <see cref="TraitType"/>.</item>
    ///   <item>Check for duplicate <c>with</c> blocks for the same type-trait pair.</item>
    ///   <item>Validate each member mapping (source exists and is public, target exists in trait, types compatible).</item>
    ///   <item>Store validated mappings in the <see cref="WithBlockRegistry"/>.</item>
    /// </list>
    /// </summary>
    private static void Pass1_ResolveWithBlocks(
        List<WithDeclarationNode> withDeclarations,
        Scope globalScope,
        SymbolTable symbolTable,
        WithBlockRegistry registry,
        DiagnosticBag diagnostics)
    {
        // Track seen type-trait pairs for duplicate detection.
        var seenPairs = new HashSet<(IType, TraitType)>();

        foreach (var withDecl in withDeclarations)
        {
            var typeName = withDecl.TypeName.Lexeme;
            var traitName = withDecl.TraitName.Lexeme;

            // 1. Resolve type name to a ClassType or StructType.
            var resolvedType = ResolveWithBlockTypeName(typeName, globalScope, symbolTable);
            if (resolvedType is null)
            {
                diagnostics.Add(new Diagnostic(
                    TraitWithUndefinedType,
                    DiagnosticSeverity.Error,
                    PipelineStage.SemanticModelConstruction,
                    $"The type '{typeName}' in the `with` block could not be found.",
                    Location: null,
                    CompilationUnitPath: null,
                    AnalyzerName: null));
                continue; // Skip this with block.
            }

            // 2. Resolve trait name to a TraitType.
            var resolvedTrait = ResolveWithBlockTraitName(traitName, globalScope, symbolTable);
            if (resolvedTrait is null)
            {
                diagnostics.Add(new Diagnostic(
                    TraitWithUndefinedTrait,
                    DiagnosticSeverity.Error,
                    PipelineStage.SemanticModelConstruction,
                    $"The trait '{traitName}' in the `with` block could not be found.",
                    Location: null,
                    CompilationUnitPath: null,
                    AnalyzerName: null));
                continue; // Skip this with block.
            }

            // 3. Check for duplicate with blocks for the same type-trait pair.
            if (!seenPairs.Add((resolvedType, resolvedTrait)))
            {
                diagnostics.Add(new Diagnostic(
                    TraitDuplicateWith,
                    DiagnosticSeverity.Error,
                    PipelineStage.SemanticModelConstruction,
                    $"Duplicate `with` block for type '{typeName}' and trait '{traitName}'.",
                    Location: null,
                    CompilationUnitPath: null,
                    AnalyzerName: null));
                continue; // Skip duplicate.
            }

            // 4. Validate each member mapping.
            foreach (var mapping in withDecl.Mappings)
            {
                var sourceName = mapping.SourceName.Lexeme;
                var targetName = mapping.TargetName.Lexeme;

                // 4a. Resolve source member on the type (must be public).
                var sourceMember = FindPublicMember(resolvedType, sourceName, symbolTable);
                if (sourceMember is null)
                {
                    diagnostics.Add(new Diagnostic(
                        TraitWithUndefinedSource,
                        DiagnosticSeverity.Error,
                        PipelineStage.SemanticModelConstruction,
                        $"The member '{sourceName}' does not exist as a public member on type '{typeName}'.",
                        Location: null,
                        CompilationUnitPath: null,
                        AnalyzerName: null));
                    continue; // Skip this mapping.
                }

                // 4b. Resolve target member in the trait.
                var targetMember = FindTraitMember(resolvedTrait, targetName);
                if (targetMember is null)
                {
                    diagnostics.Add(new Diagnostic(
                        TraitWithUndefinedTarget,
                        DiagnosticSeverity.Error,
                        PipelineStage.SemanticModelConstruction,
                        $"The member '{targetName}' does not exist in trait '{traitName}'.",
                        Location: null,
                        CompilationUnitPath: null,
                        AnalyzerName: null));
                    continue; // Skip this mapping.
                }

                // 4c. Check type compatibility between source and target.
                // At this stage (Pass 1), member DeclaredType may not yet be resolved.
                // Compare the type references from the AST nodes structurally.
                var sourceTypeRef = GetMemberTypeReference(sourceMember, symbolTable);
                var targetTypeRef = GetTraitMemberTypeReference(targetMember, symbolTable);

                if (sourceTypeRef is not null && targetTypeRef is not null
                    && !TypeReferencesMatch(sourceTypeRef, targetTypeRef))
                {
                    var expectedTypeStr = FormatTypeReference(targetTypeRef);
                    var actualTypeStr = FormatTypeReference(sourceTypeRef);
                    diagnostics.Add(new Diagnostic(
                        TraitWithTypeMismatch,
                        DiagnosticSeverity.Error,
                        PipelineStage.SemanticModelConstruction,
                        $"Cannot map '{typeName}.{sourceName}' to '{traitName}.{targetName}': expected type '{expectedTypeStr}', found '{actualTypeStr}'.",
                        Location: null,
                        CompilationUnitPath: null,
                        AnalyzerName: null));
                    continue; // Skip this mapping.
                }

                // 5. Store validated mapping.
                registry.AddMapping(resolvedType, resolvedTrait, new ValidatedMemberMapping
                {
                    SourceMember = sourceMember,
                    TargetMember = targetMember,
                    SourceName = sourceName,
                    TargetName = targetName,
                });
            }
        }
    }

    /// <summary>
    /// Resolves a type name from a <c>with</c> block to a <see cref="ClassType"/>
    /// or <see cref="StructType"/> by searching all namespace scopes.
    /// </summary>
    private static IType? ResolveWithBlockTypeName(
        string typeName, Scope globalScope, SymbolTable symbolTable)
    {
        // Search all namespace scopes for the type.
        foreach (var nsMember in globalScope.Members)
        {
            if (nsMember.Kind == SymbolKind.Namespace)
            {
                var nsScope = FindNamespaceScopeForSymbol(nsMember, symbolTable);
                if (nsScope is not null)
                {
                    var found = nsScope.Lookup(typeName);
                    if (found?.DeclaredType is ClassType or StructType)
                        return found.DeclaredType;
                }
            }
        }

        // Also search the global scope directly (types without a namespace).
        var globalFound = globalScope.Lookup(typeName);
        if (globalFound?.DeclaredType is ClassType or StructType)
            return globalFound.DeclaredType;

        return null;
    }

    /// <summary>
    /// Resolves a trait name from a <c>with</c> block to a <see cref="TraitType"/>
    /// by searching all namespace scopes.
    /// </summary>
    private static TraitType? ResolveWithBlockTraitName(
        string traitName, Scope globalScope, SymbolTable symbolTable)
    {
        // Search all namespace scopes for the trait.
        foreach (var nsMember in globalScope.Members)
        {
            if (nsMember.Kind == SymbolKind.Namespace)
            {
                var nsScope = FindNamespaceScopeForSymbol(nsMember, symbolTable);
                if (nsScope is not null)
                {
                    var found = nsScope.Lookup(traitName);
                    if (found?.DeclaredType is TraitType traitType)
                        return traitType;
                }
            }
        }

        // Also search the global scope directly.
        var globalFound = globalScope.Lookup(traitName);
        if (globalFound?.DeclaredType is TraitType globalTrait)
            return globalTrait;

        return null;
    }

    /// <summary>
    /// Finds the scope associated with a namespace symbol by looking at
    /// the types registered in that namespace.
    /// </summary>
    private static IScope? FindNamespaceScopeForSymbol(
        ISymbol namespaceSymbol, SymbolTable symbolTable)
    {
        // Find any type symbol whose ContainingScope is the namespace scope.
        foreach (var sym in symbolTable.AllSymbols)
        {
            if (sym.ContainingScope != namespaceSymbol.ContainingScope
                && sym.Kind is SymbolKind.Class or SymbolKind.Struct or SymbolKind.Interface or SymbolKind.Trait)
            {
                // Check if this symbol's containing scope is a child of the global scope
                // and the namespace name matches.
                var declaringNode = symbolTable.GetDeclaringNode(sym);
                if (declaringNode is not null)
                {
                    // The symbol's containing scope is the namespace scope if
                    // the namespace symbol's name matches the type's containing namespace.
                    var typeNamespace = GetTypeContainingNamespace(sym.DeclaredType);
                    if (typeNamespace == namespaceSymbol.Name)
                        return sym.ContainingScope;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Gets the containing namespace from a type.
    /// </summary>
    private static string? GetTypeContainingNamespace(IType? type)
    {
        return type switch
        {
            ClassType ct => ct.ContainingNamespace,
            StructType st => st.ContainingNamespace,
            InterfaceType it => it.ContainingNamespace,
            TraitType tt => tt.ContainingNamespace,
            _ => null,
        };
    }

    /// <summary>
    /// Finds a public member on a type by name. Returns <c>null</c> if the member
    /// does not exist or is not public.
    /// </summary>
    private static ISymbol? FindPublicMember(
        IType type, string memberName, SymbolTable symbolTable)
    {
        var memberScope = type switch
        {
            ClassType ct => ct.MemberScope,
            StructType st => st.MemberScope,
            _ => null,
        };

        if (memberScope is null)
            return null;

        // Search for a member with the given name.
        foreach (var member in memberScope.Members)
        {
            if (member.Name == memberName
                && member.Kind is SymbolKind.Field or SymbolKind.Method or SymbolKind.Property)
            {
                // Check if the member is public by examining its AST node modifiers.
                if (IsMemberPublic(member, symbolTable))
                    return member;
            }
        }

        return null;
    }

    /// <summary>
    /// Determines whether a member symbol is public by checking its declaring
    /// AST node's modifiers. A member is considered public if it has an explicit
    /// <c>public</c> modifier.
    /// </summary>
    private static bool IsMemberPublic(ISymbol member, SymbolTable symbolTable)
    {
        var declaringNode = symbolTable.GetDeclaringNode(member);
        if (declaringNode is null)
            return false;

        ImmutableArray<ModifierNode>? modifiers = declaringNode switch
        {
            FieldDeclarationNode f => f.Modifiers,
            MethodDeclarationNode m => m.Modifiers,
            BlockPropertyPropertyDeclarationNode bp => bp.Modifiers,
            ExpressionBodiedPropertyPropertyDeclarationNode ep => ep.Modifiers,
            ConstructorDeclarationNode c => c.Modifiers,
            OperatorDeclarationNode o => o.Modifiers,
            _ => null,
        };

        if (modifiers is null)
            return false;

        return modifiers.Value.Any(m => m is PublicModifierModifierNode);
    }

    /// <summary>
    /// Finds a member in a trait's member scope by name.
    /// </summary>
    private static ISymbol? FindTraitMember(TraitType trait, string memberName)
    {
        foreach (var member in trait.MemberScope.Members)
        {
            if (member.Name == memberName)
                return member;
        }
        return null;
    }

    /// <summary>
    /// Extracts the type reference AST node from a type member's declaring node.
    /// Used for type compatibility checking in <c>with</c> block validation.
    /// </summary>
    private static TypeReferenceNode? GetMemberTypeReference(
        ISymbol member, SymbolTable symbolTable)
    {
        var declaringNode = symbolTable.GetDeclaringNode(member);
        return declaringNode switch
        {
            FieldDeclarationNode f => f.Type,
            MethodDeclarationNode m => m.ReturnType,
            BlockPropertyPropertyDeclarationNode bp => bp.Type,
            ExpressionBodiedPropertyPropertyDeclarationNode ep => ep.Type,
            _ => null,
        };
    }

    /// <summary>
    /// Extracts the type reference AST node from a trait member's declaring node.
    /// Used for type compatibility checking in <c>with</c> block validation.
    /// </summary>
    private static TypeReferenceNode? GetTraitMemberTypeReference(
        ISymbol traitMember, SymbolTable symbolTable)
    {
        var declaringNode = symbolTable.GetDeclaringNode(traitMember);
        return declaringNode switch
        {
            TraitFieldDeclarationNode f => f.Type,
            TraitMethodDeclarationNode m => m.ReturnType,
            TraitPropertyDeclarationNode p => p.Type,
            _ => null,
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Pass 1g (cont.) — Resolve trait satisfaction
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// For each trait in the program and each class/struct, computes structural
    /// matches and combines them with <c>with</c> block mappings to determine
    /// which types satisfy which traits. Results are stored in the
    /// <see cref="TraitSatisfactionMap"/>.
    /// <list type="bullet">
    ///   <item>Emits <c>MIST_TRAIT_MEMBER_VISIBILITY</c> when a private member
    ///         matches a trait member name.</item>
    ///   <item>Emits <c>MIST_TRAIT_WITH_UNSATISFIED</c> when a <c>with</c> block
    ///         exists but doesn't cover all unmapped members.</item>
    /// </list>
    /// </summary>
    private static void Pass1_ResolveTraitSatisfaction(
        SymbolTable symbolTable,
        WithBlockRegistry withBlockRegistry,
        TraitSatisfactionMap satisfactionMap,
        DiagnosticBag diagnostics)
    {
        // Collect all traits.
        var traits = new List<(ISymbol Symbol, TraitType Trait)>();
        foreach (var sym in symbolTable.AllSymbols)
        {
            if (sym.Kind == SymbolKind.Trait && sym.DeclaredType is TraitType tt)
                traits.Add((sym, tt));
        }

        // Collect all classes and structs.
        var types = new List<(ISymbol Symbol, IType Type)>();
        foreach (var sym in symbolTable.AllSymbols)
        {
            if (sym.Kind == SymbolKind.Class && sym.DeclaredType is ClassType ct)
                types.Add((sym, ct));
            else if (sym.Kind == SymbolKind.Struct && sym.DeclaredType is StructType st)
                types.Add((sym, st));
        }

        // For each trait × type pair, attempt satisfaction.
        foreach (var (_, trait) in traits)
        {
            foreach (var (typeSym, type) in types)
            {
                var typeName = typeSym.Name;
                var memberScope = type switch
                {
                    ClassType ct => ct.MemberScope,
                    StructType st => st.MemberScope,
                    _ => null,
                };

                if (memberScope is null)
                    continue;

                // 1. Structural matching: for each trait member, search the
                //    type's public members for a match.
                var structuralMatches = new Dictionary<string, ISymbol>();

                foreach (var traitMember in trait.MemberScope.Members)
                {
                    var matched = TryStructuralMatch(
                        traitMember, memberScope, typeName, symbolTable, diagnostics);

                    if (matched is not null)
                    {
                        structuralMatches[traitMember.Name] = matched;
                    }
                }

                // 2. With-block augmentation: apply with-block mappings to
                //    cover any trait members not structurally matched.
                var withMappings = new Dictionary<string, ISymbol>();
                var hasWithBlock = withBlockRegistry.HasMappings(type, trait);

                if (hasWithBlock)
                {
                    foreach (var mapping in withBlockRegistry.GetMappings(type, trait))
                    {
                        // Only apply the mapping if the trait member is not
                        // already structurally matched.
                        if (!structuralMatches.ContainsKey(mapping.TargetName))
                        {
                            withMappings[mapping.TargetName] = mapping.SourceMember;
                        }
                    }
                }

                // 3. Satisfaction check: are all trait members covered?
                var unsatisfiedMembers = new List<string>();
                foreach (var traitMember in trait.MemberScope.Members)
                {
                    if (!structuralMatches.ContainsKey(traitMember.Name)
                        && !withMappings.ContainsKey(traitMember.Name))
                    {
                        unsatisfiedMembers.Add(traitMember.Name);
                    }
                }

                if (unsatisfiedMembers.Count == 0)
                {
                    // All trait members are covered — register satisfaction.
                    satisfactionMap.Register(type, trait, new TraitSatisfaction
                    {
                        StructuralMatches = structuralMatches,
                        WithMappings = withMappings,
                    });
                }
                else if (hasWithBlock)
                {
                    // 5. Emit MIST_TRAIT_WITH_UNSATISFIED: a with block exists
                    //    but doesn't cover all unmapped members.
                    var memberList = string.Join(", ", unsatisfiedMembers);
                    diagnostics.Add(new Diagnostic(
                        TraitWithUnsatisfied,
                        DiagnosticSeverity.Error,
                        PipelineStage.SemanticModelConstruction,
                        $"`with` block for '{typeName} : {trait.DisplayName}' does not cover all trait members. Unsatisfied: {memberList}.",
                        Location: null,
                        CompilationUnitPath: null,
                        AnalyzerName: null));
                }
                // 4. No diagnostic for types without with blocks — trait
                //    satisfaction is lazy (checked at assignment time by the
                //    type checker).
            }
        }
    }

    /// <summary>
    /// Attempts to structurally match a single trait member against the type's
    /// public members. Returns the matching type member symbol, or <c>null</c>
    /// if no structural match was found.
    /// <para>
    /// Also emits <c>MIST_TRAIT_MEMBER_VISIBILITY</c> when a private member
    /// matches the trait member by name.
    /// </para>
    /// </summary>
    private static ISymbol? TryStructuralMatch(
        ISymbol traitMember,
        IScope typeMemberScope,
        string typeName,
        SymbolTable symbolTable,
        DiagnosticBag diagnostics)
    {
        var traitMemberName = traitMember.Name;

        switch (traitMember.Kind)
        {
            case SymbolKind.Field:
                return TryMatchTraitField(traitMember, typeMemberScope, typeName, symbolTable, diagnostics);

            case SymbolKind.Method:
                return TryMatchTraitMethod(traitMember, typeMemberScope, typeName, symbolTable, diagnostics);

            case SymbolKind.Property:
                return TryMatchTraitProperty(traitMember, typeMemberScope, typeName, symbolTable, diagnostics);

            default:
                return null;
        }
    }

    /// <summary>
    /// Matches a trait field member against the type's public fields or public
    /// properties with compatible type.
    /// </summary>
    private static ISymbol? TryMatchTraitField(
        ISymbol traitField,
        IScope typeMemberScope,
        string typeName,
        SymbolTable symbolTable,
        DiagnosticBag diagnostics)
    {
        var traitFieldName = traitField.Name;
        var traitTypeRef = GetTraitMemberTypeReference(traitField, symbolTable);
        var foundPrivateMatch = false;

        foreach (var member in typeMemberScope.Members)
        {
            if (member.Name != traitFieldName)
                continue;

            // Accept fields or properties.
            if (member.Kind is not (SymbolKind.Field or SymbolKind.Property))
                continue;

            // Check visibility.
            if (!IsMemberPublic(member, symbolTable))
            {
                foundPrivateMatch = true;
                continue;
            }

            // Check type compatibility.
            if (traitTypeRef is not null)
            {
                var memberTypeRef = GetMemberTypeReference(member, symbolTable);
                if (memberTypeRef is not null && !TypeReferencesMatch(traitTypeRef, memberTypeRef))
                    continue; // Type mismatch — not a match.
            }

            return member;
        }

        if (foundPrivateMatch)
        {
            diagnostics.Add(new Diagnostic(
                TraitMemberVisibility,
                DiagnosticSeverity.Error,
                PipelineStage.SemanticModelConstruction,
                $"Member '{typeName}.{traitFieldName}' exists but is not public; trait matching requires public visibility.",
                Location: null,
                CompilationUnitPath: null,
                AnalyzerName: null));
        }

        return null;
    }

    /// <summary>
    /// Matches a trait method member against the type's public methods with
    /// the same name, return type, and parameter types.
    /// </summary>
    private static ISymbol? TryMatchTraitMethod(
        ISymbol traitMethod,
        IScope typeMemberScope,
        string typeName,
        SymbolTable symbolTable,
        DiagnosticBag diagnostics)
    {
        var traitMethodName = traitMethod.Name;
        var traitNode = symbolTable.GetDeclaringNode(traitMethod);
        if (traitNode is not TraitMethodDeclarationNode traitMethodDecl)
            return null;

        var traitReturnType = traitMethodDecl.ReturnType;
        var traitParams = ExtractParameterTypes(traitMethodDecl.Parameters);
        var foundPrivateMatch = false;

        foreach (var member in typeMemberScope.Members)
        {
            if (member.Name != traitMethodName || member.Kind != SymbolKind.Method)
                continue;

            // Check visibility.
            if (!IsMemberPublic(member, symbolTable))
            {
                foundPrivateMatch = true;
                continue;
            }

            // Check signature: return type + parameter types.
            var memberNode = symbolTable.GetDeclaringNode(member);
            if (memberNode is not MethodDeclarationNode implMethodDecl)
                continue;

            // Compare return types.
            if (!TypeReferencesMatch(traitReturnType, implMethodDecl.ReturnType))
                continue;

            // Compare parameter types.
            var implParams = ExtractParameterTypes(implMethodDecl.Parameters);
            if (traitParams.Count != implParams.Count)
                continue;

            var paramsMatch = true;
            for (var i = 0; i < traitParams.Count; i++)
            {
                if (!TypeReferencesMatch(traitParams[i], implParams[i]))
                {
                    paramsMatch = false;
                    break;
                }
            }

            if (!paramsMatch)
                continue;

            return member;
        }

        if (foundPrivateMatch)
        {
            diagnostics.Add(new Diagnostic(
                TraitMemberVisibility,
                DiagnosticSeverity.Error,
                PipelineStage.SemanticModelConstruction,
                $"Member '{typeName}.{traitMethodName}' exists but is not public; trait matching requires public visibility.",
                Location: null,
                CompilationUnitPath: null,
                AnalyzerName: null));
        }

        return null;
    }

    /// <summary>
    /// Matches a trait property member against the type's public properties
    /// with the same name, compatible type, and at least the trait's accessors.
    /// </summary>
    private static ISymbol? TryMatchTraitProperty(
        ISymbol traitProperty,
        IScope typeMemberScope,
        string typeName,
        SymbolTable symbolTable,
        DiagnosticBag diagnostics)
    {
        var traitPropName = traitProperty.Name;
        var traitTypeRef = GetTraitMemberTypeReference(traitProperty, symbolTable);
        var traitNode = symbolTable.GetDeclaringNode(traitProperty);
        var traitAccessors = GetTraitPropertyAccessors(traitNode);
        var foundPrivateMatch = false;

        foreach (var member in typeMemberScope.Members)
        {
            if (member.Name != traitPropName || member.Kind != SymbolKind.Property)
                continue;

            // Check visibility.
            if (!IsMemberPublic(member, symbolTable))
            {
                foundPrivateMatch = true;
                continue;
            }

            // Check type compatibility.
            if (traitTypeRef is not null)
            {
                var memberTypeRef = GetMemberTypeReference(member, symbolTable);
                if (memberTypeRef is not null && !TypeReferencesMatch(traitTypeRef, memberTypeRef))
                    continue; // Type mismatch.
            }

            // Check that the type's property has at least the accessors
            // declared in the trait.
            var implPropInfo = (member as Symbol)?.PropertyMetadata;
            if (implPropInfo is not null && traitAccessors is not null)
            {
                if (traitAccessors.Value.HasGet && !implPropInfo.HasGet)
                    continue;
                if (traitAccessors.Value.HasSet && !implPropInfo.HasSet)
                    continue;
            }

            return member;
        }

        if (foundPrivateMatch)
        {
            diagnostics.Add(new Diagnostic(
                TraitMemberVisibility,
                DiagnosticSeverity.Error,
                PipelineStage.SemanticModelConstruction,
                $"Member '{typeName}.{traitPropName}' exists but is not public; trait matching requires public visibility.",
                Location: null,
                CompilationUnitPath: null,
                AnalyzerName: null));
        }

        return null;
    }

    /// <summary>
    /// Extracts the accessor flags (get/set) from a trait property declaration node.
    /// </summary>
    private static (bool HasGet, bool HasSet)? GetTraitPropertyAccessors(
        Storm.Runtime.SyntaxNode? declaringNode)
    {
        if (declaringNode is not TraitPropertyDeclarationNode traitPropDecl)
            return null;

        var hasGet = false;
        var hasSet = false;

        foreach (var accessor in traitPropDecl.Accessors)
        {
            switch (accessor)
            {
                case InterfaceGetAccessorInterfaceAccessorDeclarationNode:
                    hasGet = true;
                    break;
                case InterfaceSetAccessorInterfaceAccessorDeclarationNode:
                    hasSet = true;
                    break;
            }
        }

        return (hasGet, hasSet);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Pass 1c — Detect circular interface inheritance chains
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// After interface lists are resolved, walk each interface's Interfaces
    /// chain to detect cycles (including self-inheritance). When a cycle is
    /// found, emit a <c>MIST_CIRCULAR_INTERFACE_INHERITANCE</c> diagnostic
    /// and break the cycle by removing the back-edge from the Interfaces list.
    /// </summary>
    private static void Pass1_DetectInterfaceCycles(
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation,
        DiagnosticBag diagnostics)
    {
        // Track interfaces that have already been verified as cycle-free so
        // we don't re-walk them.
        var verified = new HashSet<InterfaceType>();

        foreach (var symbol in symbolTable.AllSymbols)
        {
            if (symbol.Kind != SymbolKind.Interface || symbol.DeclaredType is not InterfaceType interfaceType)
            {
                continue;
            }

            if (interfaceType.Interfaces.Count == 0 || verified.Contains(interfaceType))
            {
                continue;
            }

            // Walk the Interfaces chain from this interface, tracking visited
            // interfaces using a stack-based DFS.
            DetectInterfaceCyclesDfs(
                interfaceType, verified, symbolTable, asts, compilation, diagnostics);
        }
    }

    /// <summary>
    /// Performs a DFS walk of the interface inheritance graph starting from
    /// <paramref name="root"/>. If a cycle is detected, emits a diagnostic
    /// and removes the back-edge from the parent's Interfaces list.
    /// </summary>
    private static void DetectInterfaceCyclesDfs(
        InterfaceType root,
        HashSet<InterfaceType> verified,
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation,
        DiagnosticBag diagnostics)
    {
        // path tracks the current DFS path for cycle reporting.
        var path = new List<InterfaceType>();
        // pathSet provides O(1) lookup for cycle detection.
        var pathSet = new HashSet<InterfaceType>();

        DetectInterfaceCyclesDfsVisit(
            root, parent: null, path, pathSet, verified,
            symbolTable, asts, compilation, diagnostics);
    }

    /// <summary>
    /// Recursive DFS visitor for interface cycle detection.
    /// </summary>
    private static void DetectInterfaceCyclesDfsVisit(
        InterfaceType current,
        InterfaceType? parent,
        List<InterfaceType> path,
        HashSet<InterfaceType> pathSet,
        HashSet<InterfaceType> verified,
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation,
        DiagnosticBag diagnostics)
    {
        if (verified.Contains(current))
        {
            return;
        }

        if (!pathSet.Add(current))
        {
            // Cycle detected — current was already on the path.
            // Build the chain string for the diagnostic message.
            var cycleStart = path.IndexOf(current);
            var cycleMembers = path.Skip(cycleStart).Select(i => i.DisplayName).ToList();
            cycleMembers.Add(current.DisplayName);
            var chainStr = string.Join(" -> ", cycleMembers);

            // Find the interface that has the back-edge (the parent that
            // references current in its Interfaces list).
            var closingInterface = path[^1];

            // Find the symbol and source text for the closing interface.
            var sourceText = FindSourceTextForInterfaceType(
                closingInterface, symbolTable, asts, compilation);
            Storm.Runtime.SyntaxNode? closingNode = null;
            foreach (var sym in symbolTable.AllSymbols)
            {
                if (sym.DeclaredType == closingInterface)
                {
                    closingNode = symbolTable.GetDeclaringNode(sym);
                    break;
                }
            }

            if (closingNode is not null)
            {
                diagnostics.Add(MakeDiagnosticWithLocation(
                    "MIST_CIRCULAR_INTERFACE_INHERITANCE",
                    DiagnosticSeverity.Error,
                    $"Circular interface inheritance detected: '{chainStr}'.",
                    closingNode,
                    sourceText));
            }
            else
            {
                string? compilationUnitPath = FindCompilationUnitPathForInterfaceType(
                    closingInterface, symbolTable, asts, compilation);

                diagnostics.Add(new Diagnostic(
                    new DiagnosticId("MIST_CIRCULAR_INTERFACE_INHERITANCE"),
                    DiagnosticSeverity.Error,
                    PipelineStage.SemanticModelConstruction,
                    $"Circular interface inheritance detected: '{chainStr}'.",
                    Location: null,
                    CompilationUnitPath: compilationUnitPath,
                    AnalyzerName: null));
            }

            // Break the cycle by removing the back-edge from the closing
            // interface's Interfaces list.
            var updatedInterfaces = closingInterface.Interfaces
                .Where(i => !ReferenceEquals(i, current))
                .ToList();
            closingInterface.Interfaces = updatedInterfaces;

            return;
        }

        path.Add(current);

        // Visit each interface in the current interface's Interfaces list.
        // Take a snapshot of the list since it may be modified during cycle breaking.
        var interfaces = current.Interfaces.OfType<InterfaceType>().ToList();
        foreach (var child in interfaces)
        {
            DetectInterfaceCyclesDfsVisit(
                child, current, path, pathSet, verified,
                symbolTable, asts, compilation, diagnostics);
        }

        path.RemoveAt(path.Count - 1);
        pathSet.Remove(current);
        verified.Add(current);
    }

    /// <summary>
    /// Finds the compilation unit path for an interface type by looking up its
    /// declaring node in the symbol table.
    /// </summary>
    private static string? FindCompilationUnitPathForInterfaceType(
        InterfaceType interfaceType,
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation)
    {
        foreach (var symbol in symbolTable.AllSymbols)
        {
            if (symbol.DeclaredType == interfaceType)
            {
                return FindCompilationUnitPathForSymbol(symbol, symbolTable, asts, compilation);
            }
        }
        return null;
    }

    /// <summary>
    /// Finds the <see cref="SourceText"/> for an interface type by looking up its
    /// declaring node in the symbol table.
    /// </summary>
    private static SourceText? FindSourceTextForInterfaceType(
        InterfaceType interfaceType,
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation)
    {
        foreach (var symbol in symbolTable.AllSymbols)
        {
            if (symbol.DeclaredType == interfaceType)
            {
                return FindSourceTextForSymbol(symbol, symbolTable, asts, compilation);
            }
        }
        return null;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Pass 1c (cont.) — Detect circular class inheritance chains
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// After all base types are resolved, walk each class's BaseType chain to
    /// detect cycles (including self-inheritance). When a cycle is found, emit
    /// a <c>MIST_CIRCULAR_INHERITANCE</c> diagnostic and set <c>BaseType = null</c>
    /// on the class that closes the cycle to break it.
    /// </summary>
    private static void Pass1_DetectCycles(
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation,
        DiagnosticBag diagnostics)
    {
        // Track classes that have already been verified as cycle-free so we
        // don't re-walk them.
        var verified = new HashSet<ClassType>();

        foreach (var symbol in symbolTable.AllSymbols)
        {
            if (symbol.Kind != SymbolKind.Class || symbol.DeclaredType is not ClassType classType)
            {
                continue;
            }

            if (classType.BaseType is null || verified.Contains(classType))
            {
                continue;
            }

            // Walk the BaseType chain from this class, tracking visited classes.
            var visited = new HashSet<ClassType>();
            var chain = new List<ClassType>();
            var current = classType;

            while (current is not null)
            {
                if (!visited.Add(current))
                {
                    // Cycle detected — current was already visited.
                    // Build the chain string for the diagnostic message.
                    var cycleStart = chain.IndexOf(current);
                    var cycleMembers = chain.Skip(cycleStart).Select(c => c.DisplayName).ToList();
                    cycleMembers.Add(current.DisplayName);
                    var chainStr = string.Join(" -> ", cycleMembers);

                    // Find the class declaration node that closes the cycle
                    // (the last class in the chain before we revisited current).
                    var closingClass = chain[^1];
                    var closingSourceText = FindSourceTextForClassType(closingClass, symbolTable, asts, compilation);
                    Storm.Runtime.SyntaxNode? closingNode = null;
                    foreach (var sym in symbolTable.AllSymbols)
                    {
                        if (sym.DeclaredType == closingClass)
                        {
                            closingNode = symbolTable.GetDeclaringNode(sym);
                            break;
                        }
                    }

                    if (closingNode is not null)
                    {
                        diagnostics.Add(MakeDiagnosticWithLocation(
                            "MIST_CIRCULAR_INHERITANCE",
                            DiagnosticSeverity.Error,
                            $"Circular inheritance detected: '{chainStr}'.",
                            closingNode,
                            closingSourceText));
                    }
                    else
                    {
                        string? compilationUnitPath = FindCompilationUnitPathForClass(closingClass, symbolTable, asts, compilation);

                        diagnostics.Add(new Diagnostic(
                            new DiagnosticId("MIST_CIRCULAR_INHERITANCE"),
                            DiagnosticSeverity.Error,
                            PipelineStage.SemanticModelConstruction,
                            $"Circular inheritance detected: '{chainStr}'.",
                            Location: null,
                            CompilationUnitPath: compilationUnitPath,
                            AnalyzerName: null));
                    }

                    // Break the cycle by nulling the BaseType on the closing class.
                    closingClass.BaseType = null;
                    break;
                }

                chain.Add(current);
                current = current.BaseType as ClassType;
            }

            // All classes in this chain that didn't form a cycle are verified.
            foreach (var c in chain)
            {
                if (c.BaseType is not null || c == classType)
                {
                    verified.Add(c);
                }
            }
        }
    }

    /// <summary>
    /// Finds the compilation unit path for a class type by looking up its
    /// declaring node in the symbol table.
    /// </summary>
    private static string? FindCompilationUnitPathForClass(
        ClassType classType,
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation)
    {
        foreach (var symbol in symbolTable.AllSymbols)
        {
            if (symbol.DeclaredType == classType)
            {
                var declaringNode = symbolTable.GetDeclaringNode(symbol);
                if (declaringNode is ClassDeclarationNode classDecl)
                {
                    return FindCompilationUnitPath(classDecl, asts, compilation);
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Finds the <see cref="SourceText"/> for a class type by looking up its
    /// declaring node in the symbol table.
    /// </summary>
    private static SourceText? FindSourceTextForClassType(
        ClassType classType,
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation)
    {
        foreach (var symbol in symbolTable.AllSymbols)
        {
            if (symbol.DeclaredType == classType)
            {
                var declaringNode = symbolTable.GetDeclaringNode(symbol);
                if (declaringNode is ClassDeclarationNode classDecl)
                {
                    return FindSourceTextForClassDecl(classDecl, asts, compilation);
                }
            }
        }
        return null;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Pass 1d — Validate interface satisfaction
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// After cycles are broken, check that each class and struct implementing
    /// interfaces provides all required methods. Walks the interface hierarchy
    /// transitively (using a visited set for diamond inheritance) to collect
    /// required methods, then searches the implementing type's member scope
    /// (and base type chain for classes) for matching methods.
    /// </summary>
    private static void Pass1_ValidateInterfaceSatisfaction(
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation,
        DiagnosticBag diagnostics)
    {
        foreach (var symbol in symbolTable.AllSymbols)
        {
            switch (symbol.Kind)
            {
                case SymbolKind.Class when symbol.DeclaredType is ClassType classType:
                    if (classType.Interfaces.Count > 0)
                    {
                        ValidateInterfaceSatisfaction(
                            classType.DisplayName, classType.Interfaces, classType.MemberScope,
                            classType, symbolTable, asts, compilation, diagnostics);
                    }
                    break;

                case SymbolKind.Struct when symbol.DeclaredType is StructType structType:
                    if (structType.Interfaces.Count > 0)
                    {
                        ValidateInterfaceSatisfaction(
                            structType.DisplayName, structType.Interfaces, structType.MemberScope,
                            null, symbolTable, asts, compilation, diagnostics);
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// Validates that the implementing type provides all methods required by
    /// its interfaces (including transitively inherited interfaces).
    /// </summary>
    /// <param name="typeName">Display name of the implementing type.</param>
    /// <param name="interfaces">Direct interfaces of the implementing type.</param>
    /// <param name="memberScope">Member scope of the implementing type.</param>
    /// <param name="classType">If the implementing type is a class, its ClassType (for base chain walking). Null for structs.</param>
    /// <param name="symbolTable">The symbol table.</param>
    /// <param name="asts">The ASTs.</param>
    /// <param name="compilation">The compilation.</param>
    /// <param name="diagnostics">Diagnostic bag.</param>
    private static void ValidateInterfaceSatisfaction(
        string typeName,
        IReadOnlyList<IType> interfaces,
        IScope memberScope,
        ClassType? classType,
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation,
        DiagnosticBag diagnostics)
    {
        // Collect all required methods transitively from the interface hierarchy.
        // Each entry is (interfaceType, methodSymbol) so we can report which
        // interface requires the method.
        var requiredMethods = new List<(InterfaceType Interface, ISymbol Method)>();
        var requiredProperties = new List<(InterfaceType Interface, ISymbol Property)>();
        var visitedInterfaces = new HashSet<InterfaceType>();
        CollectRequiredMembers(interfaces, requiredMethods, requiredProperties, visitedInterfaces);

        // For each required method, search for a matching implementation.
        foreach (var (requiredInterface, requiredMethod) in requiredMethods)
        {
            var requiredNode = symbolTable.GetDeclaringNode(requiredMethod);
            if (requiredNode is not InterfaceMethodDeclarationNode requiredMethodDecl)
                continue;

            // Search the implementing type's member scope for a method with the same name.
            var matchResult = FindMatchingMethod(
                requiredMethod.Name, requiredMethodDecl,
                memberScope, classType, symbolTable);

            switch (matchResult)
            {
                case MethodMatchResult.MatchedResult:
                    // Method found with matching signature — satisfied.
                    break;

                case MethodMatchResult.MissingResult:
                {
                    // No method with this name found anywhere in the type or its base chain.
                    // Emit MIST_SM_0022 with the specific interface method signature.
                    var paramSig = FormatParameterTypes(requiredMethodDecl.Parameters);
                    var missingSourceText = FindSourceTextForType(
                        typeName, symbolTable, asts, compilation);
                    var missingNode = FindDeclaringNodeForType(typeName, symbolTable);

                    var message = $"'{typeName}' does not implement interface method '{requiredInterface.DisplayName}.{requiredMethod.Name}({paramSig})'.";

                    if (missingNode is not null)
                    {
                        diagnostics.Add(MakeDiagnosticWithLocation(
                            MissingInterfaceOverload.Value,
                            DiagnosticSeverity.Error,
                            message,
                            missingNode,
                            missingSourceText));
                    }
                    else
                    {
                        string? missingPath = FindCompilationUnitPathForType(
                            typeName, symbolTable, asts, compilation);

                        diagnostics.Add(new Diagnostic(
                            MissingInterfaceOverload,
                            DiagnosticSeverity.Error,
                            PipelineStage.SemanticModelConstruction,
                            message,
                            Location: null,
                            CompilationUnitPath: missingPath,
                            AnalyzerName: null));
                    }
                    break;
                }

                case MethodMatchResult.SignatureMismatch:
                {
                    // Method name matches but no overload has a matching parameter signature.
                    // Emit MIST_SM_0022 with the specific interface method signature.
                    var paramSig = FormatParameterTypes(requiredMethodDecl.Parameters);
                    var mismatchSourceText = FindSourceTextForType(
                        typeName, symbolTable, asts, compilation);
                    var mismatchNode = FindDeclaringNodeForType(typeName, symbolTable);

                    var message = $"'{typeName}' does not implement interface method '{requiredInterface.DisplayName}.{requiredMethod.Name}({paramSig})'.";

                    if (mismatchNode is not null)
                    {
                        diagnostics.Add(MakeDiagnosticWithLocation(
                            MissingInterfaceOverload.Value,
                            DiagnosticSeverity.Error,
                            message,
                            mismatchNode,
                            mismatchSourceText));
                    }
                    else
                    {
                        string? mismatchPath = FindCompilationUnitPathForType(
                            typeName, symbolTable, asts, compilation);

                        diagnostics.Add(new Diagnostic(
                            MissingInterfaceOverload,
                            DiagnosticSeverity.Error,
                            PipelineStage.SemanticModelConstruction,
                            message,
                            Location: null,
                            CompilationUnitPath: mismatchPath,
                            AnalyzerName: null));
                    }
                    break;
                }
            }
        }

        // For each required property, search for a matching implementation.
        foreach (var (requiredInterface, requiredProperty) in requiredProperties)
        {
            var requiredPropInfo = (requiredProperty as Symbol)?.PropertyMetadata;
            if (requiredPropInfo is null)
                continue;

            // Search the implementing type's member scope (and base chain) for a property with the same name.
            var implProperty = FindMatchingProperty(requiredProperty.Name, memberScope, classType);

            if (implProperty is null)
            {
                string? missingPath = FindCompilationUnitPathForType(
                    typeName, symbolTable, asts, compilation);

                diagnostics.Add(new Diagnostic(
                    new DiagnosticId("MIST_MISSING_INTERFACE_PROPERTY"),
                    DiagnosticSeverity.Error,
                    PipelineStage.SemanticModelConstruction,
                    $"'{typeName}' does not implement interface property '{requiredInterface.DisplayName}.{requiredProperty.Name}'.",
                    Location: null,
                    CompilationUnitPath: missingPath,
                    AnalyzerName: null));
                continue;
            }

            var implPropInfo = (implProperty as Symbol)?.PropertyMetadata;
            if (implPropInfo is null)
                continue;

            // Check that the implementing property has the required accessors.
            if (requiredPropInfo.HasGet && !implPropInfo.HasGet)
            {
                string? path = FindCompilationUnitPathForType(typeName, symbolTable, asts, compilation);
                diagnostics.Add(new Diagnostic(
                    new DiagnosticId("MIST_MISSING_INTERFACE_PROPERTY_ACCESSOR"),
                    DiagnosticSeverity.Error,
                    PipelineStage.SemanticModelConstruction,
                    $"'{typeName}.{requiredProperty.Name}' does not implement 'get' accessor required by interface '{requiredInterface.DisplayName}'.",
                    Location: null,
                    CompilationUnitPath: path,
                    AnalyzerName: null));
            }

            if (requiredPropInfo.HasSet && !implPropInfo.HasSet && !implPropInfo.HasInit)
            {
                string? path = FindCompilationUnitPathForType(typeName, symbolTable, asts, compilation);
                diagnostics.Add(new Diagnostic(
                    new DiagnosticId("MIST_MISSING_INTERFACE_PROPERTY_ACCESSOR"),
                    DiagnosticSeverity.Error,
                    PipelineStage.SemanticModelConstruction,
                    $"'{typeName}.{requiredProperty.Name}' does not implement 'set' accessor required by interface '{requiredInterface.DisplayName}'.",
                    Location: null,
                    CompilationUnitPath: path,
                    AnalyzerName: null));
            }
        }
    }

    /// <summary>
    /// Recursively collects all required methods and properties from the interface hierarchy,
    /// using a visited set to handle diamond inheritance.
    /// </summary>
    private static void CollectRequiredMembers(
        IReadOnlyList<IType> interfaces,
        List<(InterfaceType Interface, ISymbol Method)> requiredMethods,
        List<(InterfaceType Interface, ISymbol Property)> requiredProperties,
        HashSet<InterfaceType> visited)
    {
        foreach (var ifaceType in interfaces)
        {
            if (ifaceType is not InterfaceType iface)
                continue;

            if (!visited.Add(iface))
                continue;

            // Add all method and property members from this interface.
            foreach (var member in iface.MemberScope.Members)
            {
                if (member.Kind == SymbolKind.Method)
                {
                    requiredMethods.Add((iface, member));
                }
                else if (member.Kind == SymbolKind.Property)
                {
                    requiredProperties.Add((iface, member));
                }
            }

            // Recurse into parent interfaces.
            if (iface.Interfaces.Count > 0)
            {
                CollectRequiredMembers(iface.Interfaces, requiredMethods, requiredProperties, visited);
            }
        }
    }

    /// <summary>
    /// Searches the implementing type's member scope (and base type chain for
    /// classes) for a property with the given name.
    /// </summary>
    private static ISymbol? FindMatchingProperty(
        string propertyName,
        IScope memberScope,
        ClassType? classType)
    {
        // Search the direct member scope first.
        foreach (var member in memberScope.Members)
        {
            if (member.Kind == SymbolKind.Property && member.Name == propertyName)
                return member;
        }

        // For classes, walk the base type chain.
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

    /// <summary>
    /// Result of searching for a matching method implementation.
    /// </summary>
    private abstract record MethodMatchResult
    {
        public static readonly MethodMatchResult Matched = new MatchedResult();
        public static readonly MethodMatchResult Missing = new MissingResult();

        public static MethodMatchResult Mismatch(string expectedSig, string actualSig)
            => new SignatureMismatch(expectedSig, actualSig);

        public sealed record MatchedResult : MethodMatchResult;
        public sealed record MissingResult : MethodMatchResult;
        public sealed record SignatureMismatch(string ExpectedSig, string ActualSig) : MethodMatchResult;
    }

    /// <summary>
    /// Searches the implementing type's member scope (and base type chain for
    /// classes) for a method matching the required interface method.
    /// With overloading, we must check all scopes in the chain — a matching
    /// overload might be in a base class even if the derived class has methods
    /// with the same name but different signatures.
    /// </summary>
    private static MethodMatchResult FindMatchingMethod(
        string methodName,
        InterfaceMethodDeclarationNode requiredMethodDecl,
        IScope memberScope,
        ClassType? classType,
        SymbolTable symbolTable)
    {
        // Track whether we found any method with the same name across all scopes.
        var foundAnyWithName = false;
        string? lastMismatchExpected = null;
        string? lastMismatchActual = null;

        // Search the direct member scope first.
        var result = SearchScopeForMethod(methodName, requiredMethodDecl, memberScope, symbolTable);
        if (result is MethodMatchResult.MatchedResult)
            return result;
        if (result is MethodMatchResult.SignatureMismatch directMismatch)
        {
            foundAnyWithName = true;
            lastMismatchExpected = directMismatch.ExpectedSig;
            lastMismatchActual = directMismatch.ActualSig;
        }

        // For classes, walk the base type chain.
        if (classType is not null)
        {
            var ancestor = classType.BaseType as ClassType;
            while (ancestor is not null)
            {
                result = SearchScopeForMethod(methodName, requiredMethodDecl, ancestor.MemberScope, symbolTable);
                if (result is MethodMatchResult.MatchedResult)
                    return result;
                if (result is MethodMatchResult.SignatureMismatch ancestorMismatch)
                {
                    foundAnyWithName = true;
                    lastMismatchExpected = ancestorMismatch.ExpectedSig;
                    lastMismatchActual = ancestorMismatch.ActualSig;
                }

                ancestor = ancestor.BaseType as ClassType;
            }
        }

        if (foundAnyWithName)
            return MethodMatchResult.Mismatch(lastMismatchExpected!, lastMismatchActual!);

        return MethodMatchResult.Missing;
    }

    /// <summary>
    /// Searches a single scope for a method with the given name and checks
    /// whether any overload's signature matches the required interface method.
    /// With overloading, multiple methods may share the same name — we must
    /// check all of them before concluding there is no match.
    /// </summary>
    private static MethodMatchResult SearchScopeForMethod(
        string methodName,
        InterfaceMethodDeclarationNode requiredMethodDecl,
        IScope scope,
        SymbolTable symbolTable)
    {
        var foundAnyWithName = false;
        string? lastActualSig = null;

        foreach (var member in scope.Members)
        {
            if (member.Kind != SymbolKind.Method || member.Name != methodName)
                continue;

            foundAnyWithName = true;

            // Found a method with the same name. Check signature.
            var memberNode = symbolTable.GetDeclaringNode(member);
            if (memberNode is not MethodDeclarationNode implMethodDecl)
                continue;

            if (SignaturesMatch(requiredMethodDecl, implMethodDecl))
                return MethodMatchResult.Matched;

            // Track the last seen actual signature for diagnostic purposes.
            lastActualSig = FormatMethodSignature(implMethodDecl);
        }

        if (foundAnyWithName)
        {
            // Name matches but no overload has a matching signature.
            var expectedSig = FormatInterfaceMethodSignature(requiredMethodDecl);
            return MethodMatchResult.Mismatch(expectedSig, lastActualSig ?? "()");
        }

        return MethodMatchResult.Missing;
    }

    /// <summary>
    /// Checks whether an implementing method's signature matches the required
    /// interface method's signature (return type and parameter types).
    /// </summary>
    private static bool SignaturesMatch(
        InterfaceMethodDeclarationNode required,
        MethodDeclarationNode actual)
    {
        // Compare return types.
        if (!TypeReferencesMatch(required.ReturnType, actual.ReturnType))
            return false;

        // Compare parameter types.
        var requiredParams = ExtractParameterTypes(required.Parameters);
        var actualParams = ExtractParameterTypes(actual.Parameters);

        if (requiredParams.Count != actualParams.Count)
            return false;

        for (var i = 0; i < requiredParams.Count; i++)
        {
            if (!TypeReferencesMatch(requiredParams[i], actualParams[i]))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Extracts the list of parameter type references from a parameter list node.
    /// </summary>
    private static List<TypeReferenceNode> ExtractParameterTypes(ParameterListNode? paramList)
    {
        var types = new List<TypeReferenceNode>();
        if (paramList is null)
            return types;

        foreach (var child in paramList.Children())
        {
            if (child is ParameterNode param)
            {
                types.Add(param.Type);
            }
        }

        return types;
    }

    /// <summary>
    /// Compares two TypeReferenceNode instances for structural equality.
    /// </summary>
    private static bool TypeReferencesMatch(TypeReferenceNode a, TypeReferenceNode b)
    {
        return (a, b) switch
        {
            (VoidTypeRefTypeReferenceNode, VoidTypeRefTypeReferenceNode) => true,

            (PrimitiveTypeRefTypeReferenceNode pa, PrimitiveTypeRefTypeReferenceNode pb) =>
                PrimitiveTypesMatch(pa.PrimitiveType, pb.PrimitiveType)
                && pa.ArrayRank.HasValue == pb.ArrayRank.HasValue,

            (NamedTypeRefTypeReferenceNode na, NamedTypeRefTypeReferenceNode nb) =>
                na.Name.IDENTIFIER.Lexeme == nb.Name.IDENTIFIER.Lexeme
                && na.ArrayRank.HasValue == nb.ArrayRank.HasValue,

            // A named type ref could match a primitive type ref if someone
            // uses the type name form (e.g., "string" as named vs primitive).
            // In practice the parser produces consistent node types, so
            // cross-kind comparisons are mismatches.
            _ => false,
        };
    }

    /// <summary>
    /// Compares two PrimitiveTypeNode instances for equality by variant type.
    /// </summary>
    private static bool PrimitiveTypesMatch(PrimitiveTypeNode a, PrimitiveTypeNode b)
    {
        return (a, b) switch
        {
            (IntTypePrimitiveTypeNode, IntTypePrimitiveTypeNode) => true,
            (UintTypePrimitiveTypeNode, UintTypePrimitiveTypeNode) => true,
            (LongTypePrimitiveTypeNode, LongTypePrimitiveTypeNode) => true,
            (UlongTypePrimitiveTypeNode, UlongTypePrimitiveTypeNode) => true,
            (ShortTypePrimitiveTypeNode, ShortTypePrimitiveTypeNode) => true,
            (UshortTypePrimitiveTypeNode, UshortTypePrimitiveTypeNode) => true,
            (ByteTypePrimitiveTypeNode, ByteTypePrimitiveTypeNode) => true,
            (SbyteTypePrimitiveTypeNode, SbyteTypePrimitiveTypeNode) => true,
            (FloatTypePrimitiveTypeNode, FloatTypePrimitiveTypeNode) => true,
            (DoubleTypePrimitiveTypeNode, DoubleTypePrimitiveTypeNode) => true,
            (DecimalTypePrimitiveTypeNode, DecimalTypePrimitiveTypeNode) => true,
            (BoolTypePrimitiveTypeNode, BoolTypePrimitiveTypeNode) => true,
            (CharTypePrimitiveTypeNode, CharTypePrimitiveTypeNode) => true,
            (StringTypePrimitiveTypeNode, StringTypePrimitiveTypeNode) => true,
            _ => false,
        };
    }

    /// <summary>
    /// Formats an interface method signature as a string for diagnostic messages.
    /// Format: "returnType(paramType1, paramType2, ...)"
    /// </summary>
    private static string FormatInterfaceMethodSignature(InterfaceMethodDeclarationNode methodDecl)
    {
        var returnType = FormatTypeReference(methodDecl.ReturnType);
        var paramTypes = FormatParameterTypes(methodDecl.Parameters);
        return $"{returnType}({paramTypes})";
    }

    /// <summary>
    /// Formats a method signature as a string for diagnostic messages.
    /// Format: "returnType(paramType1, paramType2, ...)"
    /// </summary>
    private static string FormatMethodSignature(MethodDeclarationNode methodDecl)
    {
        var returnType = FormatTypeReference(methodDecl.ReturnType);
        var paramTypes = FormatParameterTypes(methodDecl.Parameters);
        return $"{returnType}({paramTypes})";
    }

    /// <summary>
    /// Formats a parameter list as a comma-separated string of type names.
    /// </summary>
    private static string FormatParameterTypes(ParameterListNode? paramList)
    {
        if (paramList is null)
            return "";

        var types = new List<string>();
        foreach (var child in paramList.Children())
        {
            if (child is ParameterNode param)
            {
                types.Add(FormatTypeReference(param.Type));
            }
        }

        return string.Join(", ", types);
    }

    /// <summary>
    /// Formats a TypeReferenceNode as a human-readable string.
    /// </summary>
    private static string FormatTypeReference(TypeReferenceNode typeRef)
    {
        return typeRef switch
        {
            VoidTypeRefTypeReferenceNode => "void",
            PrimitiveTypeRefTypeReferenceNode p =>
                FormatPrimitiveType(p.PrimitiveType) + (p.ArrayRank.HasValue ? "[]" : ""),
            NamedTypeRefTypeReferenceNode n =>
                n.Name.IDENTIFIER.Lexeme + (n.ArrayRank.HasValue ? "[]" : ""),
            _ => "unknown",
        };
    }

    /// <summary>
    /// Formats a PrimitiveTypeNode as a keyword string.
    /// </summary>
    private static string FormatPrimitiveType(PrimitiveTypeNode node)
    {
        return node switch
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
        };
    }

    /// <summary>
    /// Finds the compilation unit path for a type by its display name.
    /// </summary>
    private static string? FindCompilationUnitPathForType(
        string typeName,
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation)
    {
        foreach (var symbol in symbolTable.AllSymbols)
        {
            if ((symbol.Kind == SymbolKind.Class || symbol.Kind == SymbolKind.Struct || symbol.Kind == SymbolKind.Interface || symbol.Kind == SymbolKind.Trait)
                && symbol.Name == typeName)
            {
                return FindCompilationUnitPathForSymbol(symbol, symbolTable, asts, compilation);
            }
        }
        return null;
    }

    /// <summary>
    /// Finds the <see cref="SourceText"/> for a type by its display name.
    /// </summary>
    private static SourceText? FindSourceTextForType(
        string typeName,
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation)
    {
        foreach (var symbol in symbolTable.AllSymbols)
        {
            if ((symbol.Kind == SymbolKind.Class || symbol.Kind == SymbolKind.Struct || symbol.Kind == SymbolKind.Interface || symbol.Kind == SymbolKind.Trait)
                && symbol.Name == typeName)
            {
                return FindSourceTextForSymbol(symbol, symbolTable, asts, compilation);
            }
        }
        return null;
    }

    /// <summary>
    /// Finds the declaring <see cref="Storm.Runtime.SyntaxNode"/> for a type by its display name.
    /// </summary>
    private static Storm.Runtime.SyntaxNode? FindDeclaringNodeForType(
        string typeName,
        SymbolTable symbolTable)
    {
        foreach (var symbol in symbolTable.AllSymbols)
        {
            if ((symbol.Kind == SymbolKind.Class || symbol.Kind == SymbolKind.Struct || symbol.Kind == SymbolKind.Interface || symbol.Kind == SymbolKind.Trait)
                && symbol.Name == typeName)
            {
                return symbolTable.GetDeclaringNode(symbol);
            }
        }
        return null;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Pass 1e — Check for member redeclarations in derived classes
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// After base types are resolved and cycles are broken, walk each class's
    /// ancestor chain to enforce the additive-only constraint: a derived class
    /// cannot redeclare a field or method that already exists in any ancestor.
    /// For methods, same name with a different parameter signature is a valid
    /// overload (not a redeclaration). Only same name AND identical parameter
    /// signature is flagged as a redeclaration error. Constructors are skipped
    /// because they do not inherit across the class hierarchy.
    /// </summary>
    private static void Pass1_CheckMemberRedeclarations(
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation,
        DiagnosticBag diagnostics)
    {
        foreach (var symbol in symbolTable.AllSymbols)
        {
            if (symbol.Kind != SymbolKind.Class || symbol.DeclaredType is not ClassType classType)
            {
                continue;
            }

            if (classType.BaseType is null)
            {
                continue;
            }

            // Collect the names of members declared directly on this class.
            foreach (var member in classType.MemberScope.Members)
            {
                // Only check fields and methods (not constructors, parameters, etc.)
                // Constructors don't inherit, so they are never redeclarations.
                if (member.Kind != SymbolKind.Field && member.Kind != SymbolKind.Method)
                {
                    continue;
                }

                // For methods, extract the parameter signature so we can distinguish
                // overloads (same name, different signature) from true redeclarations
                // (same name AND same signature).
                IReadOnlyList<IType>? memberSignature = null;
                if (member.Kind == SymbolKind.Method)
                {
                    memberSignature = ParameterSignature.Extract(member, symbolTable);
                }

                // Walk the ancestor chain looking for a member with the same name.
                var ancestor = classType.BaseType as ClassType;
                while (ancestor is not null)
                {
                    foreach (var ancestorMember in ancestor.MemberScope.Members)
                    {
                        if (ancestorMember.Name != member.Name)
                        {
                            continue;
                        }

                        // Fields use name-only matching (unchanged behavior).
                        if (member.Kind == SymbolKind.Field && ancestorMember.Kind == SymbolKind.Field)
                        {
                            EmitRedeclarationDiagnostic(classType, member, ancestor, symbolTable, asts, compilation, diagnostics);
                            goto nextMember;
                        }

                        // Methods: only a redeclaration if the parameter signatures match.
                        // Same name with a different signature is a valid overload.
                        if (member.Kind == SymbolKind.Method && ancestorMember.Kind == SymbolKind.Method)
                        {
                            var ancestorSignature = ParameterSignature.Extract(ancestorMember, symbolTable);
                            if (ParameterSignature.AreEqual(memberSignature!, ancestorSignature))
                            {
                                EmitRedeclarationDiagnostic(classType, member, ancestor, symbolTable, asts, compilation, diagnostics);
                                goto nextMember;
                            }
                            // Different signature — valid overload, keep checking other ancestors.
                            continue;
                        }

                        // Cross-kind conflict (e.g. field in derived vs method in base with same name).
                        if ((ancestorMember.Kind == SymbolKind.Field || ancestorMember.Kind == SymbolKind.Method))
                        {
                            EmitRedeclarationDiagnostic(classType, member, ancestor, symbolTable, asts, compilation, diagnostics);
                            goto nextMember;
                        }
                    }

                    ancestor = ancestor.BaseType as ClassType;
                }

                nextMember:;
            }
        }
    }

    /// <summary>
    /// Emits a <c>MIST_MEMBER_REDECLARATION</c> diagnostic for a member that
    /// conflicts with an ancestor member.
    /// </summary>
    private static void EmitRedeclarationDiagnostic(
        ClassType classType,
        ISymbol member,
        ClassType ancestor,
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation,
        DiagnosticBag diagnostics)
    {
        var memberNode = symbolTable.GetDeclaringNode(member);
        var redeclSourceText = FindSourceTextForClassType(
            classType, symbolTable, asts, compilation);

        if (memberNode is not null)
        {
            diagnostics.Add(MakeDiagnosticWithLocation(
                "MIST_MEMBER_REDECLARATION",
                DiagnosticSeverity.Error,
                $"'{classType.DisplayName}' cannot redeclare member '{member.Name}' already defined in '{ancestor.DisplayName}'.",
                memberNode,
                redeclSourceText));
        }
        else
        {
            string? compilationUnitPath = FindCompilationUnitPathForClass(
                classType, symbolTable, asts, compilation);

            diagnostics.Add(new Diagnostic(
                new DiagnosticId("MIST_MEMBER_REDECLARATION"),
                DiagnosticSeverity.Error,
                PipelineStage.SemanticModelConstruction,
                $"'{classType.DisplayName}' cannot redeclare member '{member.Name}' already defined in '{ancestor.DisplayName}'.",
                Location: null,
                CompilationUnitPath: compilationUnitPath,
                AnalyzerName: null));
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Pass 1f — Check base constructor requirements are satisfied
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// After base types are resolved and cycles are broken, check that each
    /// derived class satisfies its base class's constructor requirements.
    /// If the base class has explicit constructors that all require parameters,
    /// the derived class must have at least one constructor with a
    /// <c>: base(args)</c> chain. If the derived class has no explicit
    /// constructors at all, or has constructors without <c>: base(...)</c>,
    /// and the base class has no parameterless constructor, emit
    /// <c>MIST_BASE_CTOR_UNSATISFIED</c>.
    /// </summary>
    private static void Pass1_CheckBaseConstructorSatisfied(
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation,
        DiagnosticBag diagnostics)
    {
        foreach (var symbol in symbolTable.AllSymbols)
        {
            if (symbol.Kind != SymbolKind.Class || symbol.DeclaredType is not ClassType classType)
            {
                continue;
            }

            if (classType.BaseType is null)
            {
                continue;
            }

            var baseType = classType.BaseType;

            // Get the member scope from the base type.
            IScope? baseScope = baseType switch
            {
                ClassType ct => ct.MemberScope,
                ConstructedType cst => cst.MemberScope,
                _ => null,
            };
            if (baseScope is null)
                continue;

            // Find explicit constructors on the base class.
            var baseConstructors = baseScope.Members
                .Where(m => m.Kind == SymbolKind.Constructor)
                .ToList();

            // If the base class has no explicit constructors, it has an implicit
            // parameterless constructor — no issue.
            if (baseConstructors.Count == 0)
            {
                continue;
            }

            // Check if the base class has at least one parameterless constructor.
            var baseHasParameterlessCtor = baseConstructors.Any(ctor =>
            {
                var ctorNode = symbolTable.GetDeclaringNode(ctor);
                return ctorNode is ConstructorDeclarationNode ctorDecl && ctorDecl.Parameters is null;
            });

            // If the base class has a parameterless constructor, derived classes
            // can implicitly call it — no issue.
            if (baseHasParameterlessCtor)
            {
                continue;
            }

            // The base class requires constructor arguments. Check if the derived
            // class satisfies this requirement.
            var derivedConstructors = classType.MemberScope.Members
                .Where(m => m.Kind == SymbolKind.Constructor)
                .ToList();

            if (derivedConstructors.Count == 0)
            {
                // Derived class has no explicit constructors and the base class
                // requires arguments — emit diagnostic.
                var classSourceText = FindSourceTextForClassType(
                    classType, symbolTable, asts, compilation);
                Storm.Runtime.SyntaxNode? classNode = null;
                foreach (var sym in symbolTable.AllSymbols)
                {
                    if (sym.DeclaredType == classType)
                    {
                        classNode = symbolTable.GetDeclaringNode(sym);
                        break;
                    }
                }

                if (classNode is not null)
                {
                    diagnostics.Add(MakeDiagnosticWithLocation(
                        "MIST_BASE_CTOR_UNSATISFIED",
                        DiagnosticSeverity.Error,
                        $"Base class '{baseType.DisplayName}' requires constructor arguments that are not provided.",
                        classNode,
                        classSourceText));
                }
                else
                {
                    string? compilationUnitPath = FindCompilationUnitPathForClass(
                        classType, symbolTable, asts, compilation);

                    diagnostics.Add(new Diagnostic(
                        new DiagnosticId("MIST_BASE_CTOR_UNSATISFIED"),
                        DiagnosticSeverity.Error,
                        PipelineStage.SemanticModelConstruction,
                        $"Base class '{baseType.DisplayName}' requires constructor arguments that are not provided.",
                        Location: null,
                        CompilationUnitPath: compilationUnitPath,
                        AnalyzerName: null));
                }
                continue;
            }

            // Check each derived constructor: at least one must have a `: base(...)` chain,
            // and any constructor without `: base(...)` is an error since the base class
            // has no parameterless constructor.
            foreach (var derivedCtor in derivedConstructors)
            {
                var ctorNode = symbolTable.GetDeclaringNode(derivedCtor);
                if (ctorNode is not ConstructorDeclarationNode ctorDecl)
                {
                    continue;
                }

                var hasBaseChain = ctorDecl.Chain is BaseChainConstructorChainNode;

                if (!hasBaseChain)
                {
                    // This constructor does not chain to the base class, but the
                    // base class requires arguments — emit diagnostic.
                    var ctorSourceText = FindSourceTextForClassType(
                        classType, symbolTable, asts, compilation);

                    diagnostics.Add(MakeDiagnosticWithLocation(
                        "MIST_BASE_CTOR_UNSATISFIED",
                        DiagnosticSeverity.Error,
                        $"Base class '{baseType.DisplayName}' requires constructor arguments that are not provided.",
                        ctorDecl,
                        ctorSourceText));
                }
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Post-Pass 2 — Check duplicate method/constructor signatures
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// After Pass 2 (type resolution), check for duplicate method and constructor
    /// signatures within each type scope. Two methods with the same name and
    /// identical parameter types are a duplicate; two constructors with identical
    /// parameter types are a duplicate.
    /// </summary>
    private static void Pass1_CheckDuplicateSignatures(
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation,
        DiagnosticBag diagnostics)
    {
        foreach (var symbol in symbolTable.AllSymbols)
        {
            IScope memberScope;
            string typeName;

            switch (symbol.Kind)
            {
                case SymbolKind.Class when symbol.DeclaredType is ClassType classType:
                    memberScope = classType.MemberScope;
                    typeName = classType.DisplayName;
                    break;
                case SymbolKind.Struct when symbol.DeclaredType is StructType structType:
                    memberScope = structType.MemberScope;
                    typeName = structType.DisplayName;
                    break;
                case SymbolKind.Interface when symbol.DeclaredType is InterfaceType interfaceType:
                    memberScope = interfaceType.MemberScope;
                    typeName = interfaceType.DisplayName;
                    break;
                default:
                    continue;
            }

            // Check duplicate method signatures: group methods by name.
            var methods = memberScope.Members
                .Where(m => m.Kind == SymbolKind.Method)
                .ToList();

            var methodGroups = methods.GroupBy(m => m.Name);
            foreach (var group in methodGroups)
            {
                var groupList = group.ToList();
                if (groupList.Count <= 1)
                    continue;

                CheckDuplicatePairs(
                    groupList, typeName, isConstructor: false,
                    symbolTable, asts, compilation, diagnostics);
            }

            // Check duplicate constructor signatures.
            var constructors = memberScope.Members
                .Where(m => m.Kind == SymbolKind.Constructor)
                .ToList();

            if (constructors.Count > 1)
            {
                CheckDuplicatePairs(
                    constructors, typeName, isConstructor: true,
                    symbolTable, asts, compilation, diagnostics);
            }
        }
    }

    /// <summary>
    /// Checks all pairs of symbols in the list for identical parameter signatures
    /// and emits the appropriate duplicate diagnostic for each match.
    /// </summary>
    private static void CheckDuplicatePairs(
        List<ISymbol> symbols,
        string typeName,
        bool isConstructor,
        SymbolTable symbolTable,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        MistCompilation compilation,
        DiagnosticBag diagnostics)
    {
        // Pre-extract signatures for all symbols to avoid redundant extraction.
        var signatures = new IReadOnlyList<IType>[symbols.Count];
        for (var i = 0; i < symbols.Count; i++)
        {
            signatures[i] = ParameterSignature.Extract(symbols[i], symbolTable);
        }

        // Track which symbols have already been reported as duplicates
        // to avoid emitting multiple diagnostics for the same pair.
        var reported = new HashSet<int>();

        for (var i = 0; i < symbols.Count; i++)
        {
            for (var j = i + 1; j < symbols.Count; j++)
            {
                if (reported.Contains(j))
                    continue;

                if (!ParameterSignature.AreEqual(signatures[i], signatures[j]))
                    continue;

                // Found a duplicate — emit diagnostic on the second (later) declaration.
                reported.Add(j);

                var diagnosticId = isConstructor
                    ? DuplicateConstructorSignature
                    : DuplicateMethodSignature;

                var paramTypeNames = string.Join(", ", signatures[j].Select(t => t.DisplayName));

                var message = isConstructor
                    ? $"Type '{typeName}' already defines a constructor with parameter types ({paramTypeNames})"
                    : $"Type '{typeName}' already defines method '{symbols[j].Name}' with parameter types ({paramTypeNames})";

                var duplicateNode = symbolTable.GetDeclaringNode(symbols[j]);
                var sourceText = FindSourceTextForType(
                    typeName, symbolTable, asts, compilation);

                if (duplicateNode is not null)
                {
                    diagnostics.Add(MakeDiagnosticWithLocation(
                        diagnosticId.Value,
                        DiagnosticSeverity.Error,
                        message,
                        duplicateNode,
                        sourceText));
                }
                else
                {
                    var compilationUnitPath = FindCompilationUnitPathForType(
                        typeName, symbolTable, asts, compilation);

                    diagnostics.Add(new Diagnostic(
                        diagnosticId,
                        DiagnosticSeverity.Error,
                        PipelineStage.SemanticModelConstruction,
                        message,
                        Location: null,
                        CompilationUnitPath: compilationUnitPath,
                        AnalyzerName: null));
                }
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Pass 2 — Resolve types and build expression types
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Pass 2: Walk the AST again to resolve type references, register parameters
    /// and local variables, and compute expression types.
    /// </summary>
    private static void Pass2_ResolveTypes(
        Storm.Runtime.SyntaxNode ast,
        Scope globalScope,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        if (ast is not CompilationUnitNode compilationUnit)
        {
            return;
        }

        // Compute the inferred namespace for this file (same logic as Pass 1).
        var filePath = sourceText?.Path ?? "";
        // We need namespaceRoot and projectFolder — extract from the global scope context.
        // Since Pass 2 doesn't receive these directly, we infer the primary namespace
        // by looking at what Pass 1 registered for top-level types in this file,
        // or by finding the first namespace declaration.
        var inferredNamespaceScope = FindInferredNamespaceScopeForFile(
            compilationUnit, globalScope, symbolTable, sourceText);

        // Determine the primary namespace scope for this file.
        // The scope chain is: local → file → namespace → global.
        // For files with no explicit namespace, the inferred namespace scope is used.
        // For files with explicit namespaces, each namespace section gets its own scope.
        Scope primaryNamespaceScope = inferredNamespaceScope ?? globalScope;

        // Create a file-level scope to hold using imports. This scope sits between
        // the namespace scope and the type scopes.
        var fileScope = new Scope(parent: primaryNamespaceScope);

        // Process using declarations — verify namespace existence and record imports.
        foreach (var usingDecl in compilationUnit.Usings)
        {
            var usingName = ExtractQualifiedName(usingDecl.Name, sourceText);
            var nsExists = false;

            // Check user-defined namespaces.
            foreach (var member in globalScope.Members)
            {
                if (member.Kind == SymbolKind.Namespace && member.Name == usingName)
                {
                    nsExists = true;
                    break;
                }
            }

            // Check ambient namespaces.
            if (!nsExists && ambientTypeResolver is not null)
            {
                nsExists = ambientTypeResolver.HasNamespace(usingName);
            }

            if (nsExists)
            {
                fileScope.AddImportedNamespace(usingName);
            }
            else
            {
                diagnostics.Add(MakeDiagnosticWithLocation(
                    "MIST_UNDEFINED_NAMESPACE",
                    DiagnosticSeverity.Error,
                    $"The namespace '{usingName}' could not be found.",
                    usingDecl,
                    sourceText));
            }
        }

        // Resolve types in items (namespace body types and top-level types).
        // For each namespace section, we use a file scope whose parent is the
        // appropriate namespace scope so that same-namespace types are visible.
        foreach (var item in compilationUnit.Items)
        {
            switch (item)
            {
                case NamespaceItemTopLevelItemNode nsItem:
                {
                    var nsDecl = nsItem.NamespaceDeclaration;
                    var declaredName = ExtractNamespaceName(nsDecl, sourceText);

                    // Find the namespace scope for this declaration.
                    var nsScope = FindExistingNamespaceScope(globalScope, symbolTable, declaredName);

                    // Create a file scope for this namespace section if it differs
                    // from the primary namespace scope.
                    Scope sectionFileScope;
                    if (nsScope is not null && nsScope != primaryNamespaceScope)
                    {
                        sectionFileScope = new Scope(parent: nsScope);
                        // Copy using imports to this section scope.
                        foreach (var ns in fileScope.ImportedNamespaces)
                        {
                            sectionFileScope.AddImportedNamespace(ns);
                        }
                    }
                    else
                    {
                        sectionFileScope = fileScope;
                    }

                    var namespaceTypes = GetNamespaceBodyTypes(nsDecl);
                    foreach (var typeDecl in namespaceTypes)
                    {
                        ResolveTypeDeclaration(typeDecl, sectionFileScope, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                    }
                    break;
                }
                case TypeItemTopLevelItemNode typeItem:
                    ResolveTypeDeclaration(typeItem.TypeDeclaration, fileScope, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                    break;
            }
        }
    }

    /// <summary>
    /// Finds the inferred namespace scope for a file by looking at the first top-level
    /// type registered in Pass 1 (types outside any explicit namespace declaration).
    /// If the file has no top-level types, falls back to the first namespace declaration's scope.
    /// </summary>
    private static Scope? FindInferredNamespaceScopeForFile(
        CompilationUnitNode compilationUnit,
        Scope globalScope,
        SymbolTable symbolTable,
        SourceText? sourceText)
    {
        // First, try to find a top-level type (outside any namespace) and use its containing scope.
        foreach (var item in compilationUnit.Items)
        {
            if (item is TypeItemTopLevelItemNode typeItem)
            {
                Storm.Runtime.SyntaxNode declNode = typeItem.TypeDeclaration switch
                {
                    ClassTypeTypeDeclarationNode ct => ct.ClassDeclaration,
                    StructTypeTypeDeclarationNode st => st.StructDeclaration,
                    InterfaceTypeTypeDeclarationNode it => it.InterfaceDeclaration,
                    _ => typeItem.TypeDeclaration
                };

                var symbol = symbolTable.GetDeclaredSymbol(declNode);
                if (symbol?.ContainingScope is Scope namespaceScope && namespaceScope.Parent == globalScope)
                {
                    return namespaceScope;
                }
            }
        }

        // No top-level types found. Try the first namespace declaration.
        foreach (var item in compilationUnit.Items)
        {
            if (item is NamespaceItemTopLevelItemNode nsItem)
            {
                var nsName = ExtractNamespaceName(nsItem.NamespaceDeclaration, sourceText);
                var nsScope = FindExistingNamespaceScope(globalScope, symbolTable, nsName);
                if (nsScope is not null)
                    return nsScope;

                // Fall back to FindNamespaceScope which looks at body types.
                return FindNamespaceScope(globalScope, symbolTable, nsItem.NamespaceDeclaration);
            }
        }

        return null;
    }

    /// <summary>
    /// Finds the namespace scope created in Pass 1 by looking at the type declarations
    /// registered under the namespace. Returns the parent scope of the first type found.
    /// </summary>
    private static Scope FindNamespaceScope(
        Scope globalScope,
        SymbolTable symbolTable,
        NamespaceDeclarationNode namespaceNode)
    {
        var bodyTypes = GetNamespaceBodyTypes(namespaceNode);
        if (bodyTypes.Length > 0)
        {
            // Get the first type declaration and find its symbol's containing scope.
            var firstTypeDecl = bodyTypes[0];
            Storm.Runtime.SyntaxNode declNode = firstTypeDecl switch
            {
                ClassTypeTypeDeclarationNode ct => ct.ClassDeclaration,
                StructTypeTypeDeclarationNode st => st.StructDeclaration,
                InterfaceTypeTypeDeclarationNode it => it.InterfaceDeclaration,
                _ => firstTypeDecl
            };

            var symbol = symbolTable.GetDeclaredSymbol(declNode);
            if (symbol?.ContainingScope is Scope namespaceScope)
            {
                return namespaceScope;
            }
        }

        // Fallback: create a new scope (shouldn't happen in practice).
        return new Scope(parent: globalScope);
    }

    /// <summary>
    /// Finds an existing namespace scope by looking at type symbols already registered
    /// under the given namespace name. Used in Pass 1 to reuse the same scope when
    /// multiple files contribute to the same namespace.
    /// </summary>
    private static Scope? FindExistingNamespaceScope(
        Scope globalScope,
        SymbolTable symbolTable,
        string namespaceName)
    {
        // Look through all symbols in the global scope for types whose
        // ContainingScope is a child of globalScope (i.e. a namespace scope).
        // We identify the right one by checking if any type in that scope
        // belongs to the target namespace.
        foreach (var member in globalScope.Members)
        {
            if (member.Kind == SymbolKind.Class || member.Kind == SymbolKind.Struct || member.Kind == SymbolKind.Interface || member.Kind == SymbolKind.Trait)
            {
                // Top-level types (no namespace) — skip.
                continue;
            }
        }

        // Walk all registered symbols to find a type in this namespace.
        foreach (var symbol in symbolTable.AllSymbols)
        {
            if ((symbol.Kind == SymbolKind.Class || symbol.Kind == SymbolKind.Struct || symbol.Kind == SymbolKind.Interface || symbol.Kind == SymbolKind.Trait)
                && symbol.ContainingScope is Scope parentScope
                && parentScope.Parent == globalScope)
            {
                // This type is in a namespace scope. Check if it matches.
                if (symbol.DeclaredType is ClassType ct && ct.ContainingNamespace == namespaceName)
                {
                    return parentScope;
                }
                if (symbol.DeclaredType is StructType st && st.ContainingNamespace == namespaceName)
                {
                    return parentScope;
                }
                if (symbol.DeclaredType is InterfaceType it && it.ContainingNamespace == namespaceName)
                {
                    return parentScope;
                }
                if (symbol.DeclaredType is TraitType tt && tt.ContainingNamespace == namespaceName)
                {
                    return parentScope;
                }
            }
        }

        return null;
    }

    private static void ResolveTypeDeclaration(
        TypeDeclarationNode typeDecl,
        Scope fileScope,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        switch (typeDecl)
        {
            case ClassTypeTypeDeclarationNode classTypeNode:
                ResolveClassDeclaration(classTypeNode.ClassDeclaration, fileScope, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                break;
            case StructTypeTypeDeclarationNode structTypeNode:
                ResolveStructDeclaration(structTypeNode.StructDeclaration, fileScope, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                break;
            case InterfaceTypeTypeDeclarationNode interfaceTypeNode:
                ResolveInterfaceDeclaration(interfaceTypeNode.InterfaceDeclaration, fileScope, symbolTable, ambientTypeResolver, sourceText, diagnostics);
                break;
            case TraitTypeTypeDeclarationNode traitTypeNode:
                ResolveTraitDeclaration(traitTypeNode.TraitDeclaration, fileScope, symbolTable, ambientTypeResolver, sourceText, diagnostics);
                break;
        }
    }

    private static void ResolveClassDeclaration(
        ClassDeclarationNode classDecl,
        Scope fileScope,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        var classSymbol = symbolTable.GetDeclaredSymbol(classDecl);
        if (classSymbol?.DeclaredType is not ClassType classType) return;

        var typeScope = (Scope)classType.MemberScope;

        // Propagate type parameters from the type scope to the file scope so
        // that type parameter names (e.g., T in GenericBox<T>) can be resolved
        // when resolving member type references through the file scope.
        PropagateTypeParameters(typeScope, fileScope);

        foreach (var memberDecl in classDecl.Members)
        {
            ResolveMemberDeclaration(memberDecl, typeScope, fileScope, classType, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
        }
    }

    private static void ResolveStructDeclaration(
        StructDeclarationNode structDecl,
        Scope fileScope,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        var structSymbol = symbolTable.GetDeclaredSymbol(structDecl);
        if (structSymbol?.DeclaredType is not StructType structType) return;

        var typeScope = (Scope)structType.MemberScope;

        // Propagate type parameters from the type scope to the file scope.
        PropagateTypeParameters(typeScope, fileScope);

        foreach (var memberDecl in structDecl.Members)
        {
            ResolveMemberDeclaration(memberDecl, typeScope, fileScope, structType, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
        }
    }

    private static void ResolveInterfaceDeclaration(
        InterfaceDeclarationNode interfaceDecl,
        Scope fileScope,
        SymbolTable symbolTable,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        var interfaceSymbol = symbolTable.GetDeclaredSymbol(interfaceDecl);
        if (interfaceSymbol?.DeclaredType is not InterfaceType interfaceType) return;

        // Propagate type parameters from the interface's type scope to the file
        // scope so that type parameter names (e.g., T in IGenericContainer<T>)
        // can be resolved when resolving member type references.
        var typeScope = (Scope)interfaceType.MemberScope;
        PropagateTypeParameters(typeScope, fileScope);

        // Resolve return types and parameter types for each interface member.
        foreach (var member in interfaceDecl.Members)
        {
            switch (member)
            {
                case InterfaceMethodMemberInterfaceMemberDeclarationNode methodMember:
                {
                    var methodDecl = methodMember.Method;
                    var returnType = ResolveTypeReference(methodDecl.ReturnType, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);
                    var methodSymbol = symbolTable.GetDeclaredSymbol(methodDecl) as Symbol;
                    if (methodSymbol is not null)
                    {
                        methodSymbol.DeclaredType = returnType;
                    }

                    // Register parameters so ParameterSignature.Extract can resolve
                    // parameter types for overload duplicate-signature detection.
                    if (methodDecl.Parameters is not null)
                    {
                        var methodScope = new Scope(parent: fileScope);
                        RegisterParameters(methodDecl.Parameters, methodScope, fileScope, symbolTable, ambientTypeResolver, sourceText, diagnostics);
                    }
                    break;
                }
                case InterfacePropertyMemberInterfaceMemberDeclarationNode propertyMember:
                {
                    var propDecl = propertyMember.Property;
                    var propType = ResolveTypeReference(propDecl.Type, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);
                    var propSymbol = symbolTable.GetDeclaredSymbol(propDecl) as Symbol;
                    if (propSymbol is not null)
                    {
                        propSymbol.DeclaredType = propType;
                    }
                    break;
                }
            }
        }
    }

    private static void ResolveTraitDeclaration(
        TraitDeclarationNode traitDecl,
        Scope fileScope,
        SymbolTable symbolTable,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        var traitSymbol = symbolTable.GetDeclaredSymbol(traitDecl);
        if (traitSymbol?.DeclaredType is not TraitType traitType) return;

        // Resolve types for each trait member.
        foreach (var member in traitDecl.Members)
        {
            switch (member)
            {
                case TraitFieldMemberTraitMemberDeclarationNode fieldMember:
                {
                    var fieldDecl = fieldMember.Field;
                    var fieldType = ResolveTypeReference(fieldDecl.Type, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);
                    var fieldSymbol = symbolTable.GetDeclaredSymbol(fieldDecl) as Symbol;
                    if (fieldSymbol is not null)
                    {
                        fieldSymbol.DeclaredType = fieldType;
                    }
                    break;
                }
                case TraitMethodMemberTraitMemberDeclarationNode methodMember:
                {
                    var methodDecl = methodMember.Method;
                    var returnType = ResolveTypeReference(methodDecl.ReturnType, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);
                    var methodSymbol = symbolTable.GetDeclaredSymbol(methodDecl) as Symbol;
                    if (methodSymbol is not null)
                    {
                        methodSymbol.DeclaredType = returnType;
                    }

                    // Register parameters so parameter types can be resolved.
                    if (methodDecl.Parameters is not null)
                    {
                        var methodScope = new Scope(parent: fileScope);
                        RegisterParameters(methodDecl.Parameters, methodScope, fileScope, symbolTable, ambientTypeResolver, sourceText, diagnostics);
                    }
                    break;
                }
                case TraitPropertyMemberTraitMemberDeclarationNode propertyMember:
                {
                    var propDecl = propertyMember.Property;
                    var propType = ResolveTypeReference(propDecl.Type, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);
                    var propSymbol = symbolTable.GetDeclaredSymbol(propDecl) as Symbol;
                    if (propSymbol is not null)
                    {
                        propSymbol.DeclaredType = propType;
                    }
                    break;
                }
            }
        }
    }

    private static void ResolveMemberDeclaration(
        MemberDeclarationNode memberDecl,
        Scope typeScope,
        Scope fileScope,
        IType enclosingType,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        switch (memberDecl)
        {
            case FieldMemberMemberDeclarationNode fieldMember:
                ResolveFieldDeclaration(fieldMember.Field, typeScope, fileScope, enclosingType, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                break;
            case ConstructorMemberMemberDeclarationNode ctorMember:
                ResolveConstructorDeclaration(ctorMember.Constructor, typeScope, fileScope, enclosingType, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                break;
            case MethodMemberMemberDeclarationNode methodMember:
                ResolveMethodDeclaration(methodMember.Method, typeScope, fileScope, enclosingType, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                break;
            case OperatorMemberMemberDeclarationNode operatorMember:
                ResolveOperatorDeclaration(operatorMember.Operator, typeScope, fileScope, enclosingType, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                break;
            case CastOperatorMemberMemberDeclarationNode castOpMember:
                ResolveCastOperatorDeclaration(castOpMember.CastOperator, typeScope, fileScope, enclosingType, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                break;
            case PropertyMemberMemberDeclarationNode propertyMember:
                ResolvePropertyDeclaration(propertyMember.Property, typeScope, fileScope, enclosingType, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                break;
            case IndexerMemberMemberDeclarationNode indexerMember:
                ResolveIndexerDeclaration(indexerMember.Indexer, typeScope, fileScope, enclosingType, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                break;
        }
    }

    private static void ResolveFieldDeclaration(
        FieldDeclarationNode fieldDecl,
        Scope typeScope,
        Scope fileScope,
        IType enclosingType,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        var fieldType = ResolveTypeReference(fieldDecl.Type, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);

        // Update the field symbol's declared type.
        var fieldSymbol = symbolTable.GetDeclaredSymbol(fieldDecl) as Symbol;
        if (fieldSymbol is not null)
        {
            fieldSymbol.DeclaredType = fieldType;
        }

        // Resolve the optional initializer expression.
        // The grammar defines: initializer:(EQUAL Expression)?
        // With the bugfix, the Initializer property is now ExpressionNode? directly.
        if (fieldDecl.Initializer is ExpressionNode initExpr)
        {
            var isStatic = fieldDecl.Modifiers.Any(m => m is StaticModifierModifierNode);
            ResolveExpression(initExpr, typeScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
        }
    }

    private static void ResolveConstructorDeclaration(
        ConstructorDeclarationNode ctorDecl,
        Scope typeScope,
        Scope fileScope,
        IType enclosingType,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        // Create a method scope for the constructor body.
        var methodScope = new Scope(parent: typeScope);

        // Register parameters.
        if (ctorDecl.Parameters is not null)
        {
            RegisterParameters(ctorDecl.Parameters, methodScope, fileScope, symbolTable, ambientTypeResolver, sourceText, diagnostics);
        }

        // Resolve constructor chain arguments if present.
        if (ctorDecl.Chain is ThisChainConstructorChainNode thisChain && thisChain.Arguments is not null)
        {
            ResolveArgumentList(thisChain.Arguments, methodScope, fileScope, enclosingType, /* isStatic */ false, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
        }
        else if (ctorDecl.Chain is BaseChainConstructorChainNode baseChain && baseChain.Arguments is not null)
        {
            ResolveArgumentList(baseChain.Arguments, methodScope, fileScope, enclosingType, /* isStatic */ false, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
        }

        // Resolve the body. A no-body (;) form is accepted by the grammar but
        // has no statements to resolve; semantic validation of whether ; is
        // permitted on this declaration is done elsewhere.
        if (ctorDecl.Body.GetBlock() is BlockNode ctorBlock)
            ResolveBlock(ctorBlock, methodScope, fileScope, enclosingType, /* isStatic */ false, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
    }

    private static void ResolveMethodDeclaration(
        MethodDeclarationNode methodDecl,
        Scope typeScope,
        Scope fileScope,
        IType enclosingType,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        // Resolve the return type and update the method symbol.
        var returnType = ResolveTypeReference(methodDecl.ReturnType, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);
        var methodSymbol = symbolTable.GetDeclaredSymbol(methodDecl) as Symbol;
        if (methodSymbol is not null)
        {
            methodSymbol.DeclaredType = returnType;
        }

        // Determine if the method is static.
        var isStatic = methodDecl.Modifiers.Any(m => m is StaticModifierModifierNode);

        // Create a method scope for the body.
        var methodScope = new Scope(parent: typeScope);

        // Register parameters.
        if (methodDecl.Parameters is not null)
        {
            RegisterParameters(methodDecl.Parameters, methodScope, fileScope, symbolTable, ambientTypeResolver, sourceText, diagnostics);
        }

        // Resolve the body. A no-body (;) form is accepted by the grammar but
        // has no statements to resolve.
        if (methodDecl.Body.GetBlock() is BlockNode methodBlock)
            ResolveBlock(methodBlock, methodScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
    }

    /// <summary>
    /// Pass 2 counterpart to <see cref="RegisterOperatorDeclaration"/>. Resolves
    /// the operator's return type (so <see cref="Symbol.DeclaredType"/> is set
    /// to the operator's result type), registers its parameters into a method
    /// scope (so <see cref="ParameterSignature.Extract"/> can retrieve parameter
    /// types for overload resolution in Task 14), and — if the operator has a
    /// body — resolves the body like a static method.
    ///
    /// An operator is treated as a static method for semantic-model purposes
    /// (Requirement 4.2, 4.6): parameter and body resolution use
    /// <c>isStatic = true</c>. Operators don't have type parameters in v1, so
    /// no equivalent of <see cref="RegisterMethodTypeParameters"/> is needed.
    /// </summary>
    private static void ResolveOperatorDeclaration(
        OperatorDeclarationNode opDecl,
        Scope typeScope,
        Scope fileScope,
        IType enclosingType,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        // Resolve the return type and update the operator symbol.
        var returnType = ResolveTypeReference(opDecl.ReturnType, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);
        var opSymbol = symbolTable.GetDeclaredSymbol(opDecl) as Symbol;
        if (opSymbol is not null)
        {
            opSymbol.DeclaredType = returnType;
        }

        // Create a method scope for parameters and the optional body.
        var methodScope = new Scope(parent: typeScope);

        // Register parameters so ParameterSignature.Extract can retrieve
        // parameter types during overload resolution (Task 14).
        if (opDecl.Parameters is not null)
        {
            RegisterParameters(opDecl.Parameters, methodScope, fileScope, symbolTable, ambientTypeResolver, sourceText, diagnostics);
        }

        // Resolve the body. A no-body (;) form is accepted by the grammar
        // (Requirement 5.2 for intrinsic operators) and has no statements to
        // resolve; Pass 1e validates whether ; is permitted on this operator
        // based on the intrinsic modifier.
        if (opDecl.Body.GetBlock() is BlockNode opBlock)
            ResolveBlock(opBlock, methodScope, fileScope, enclosingType, /* isStatic */ true, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
    }

    /// <summary>
    /// Pass 2 counterpart to <see cref="RegisterCastOperatorDeclaration"/>. Resolves
    /// the cast operator's target type reference to an <see cref="IType"/> and sets it
    /// as the symbol's <see cref="Symbol.DeclaredType"/> (return type). Registers the
    /// single parameter as a <see cref="SymbolKind.Parameter"/> symbol within the cast
    /// operator's scope, and resolves the body block (if present) using the same logic
    /// as <see cref="ResolveOperatorDeclaration"/>.
    ///
    /// Cast operators are always static methods (Requirement 6.4), so the body is
    /// resolved with <c>isStatic = true</c>.
    /// </summary>
    private static void ResolveCastOperatorDeclaration(
        CastOperatorDeclarationNode castOpDecl,
        Scope typeScope,
        Scope fileScope,
        IType enclosingType,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        // Resolve the target type reference and set it as the symbol's DeclaredType (return type).
        var targetType = ResolveTypeReference(castOpDecl.TargetType, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);
        var castOpSymbol = symbolTable.GetDeclaredSymbol(castOpDecl) as Symbol;
        if (castOpSymbol is not null)
        {
            castOpSymbol.DeclaredType = targetType;
        }

        // Create a method scope for parameters and the optional body.
        var methodScope = new Scope(parent: typeScope);

        // Register the single parameter within the cast operator's scope.
        if (castOpDecl.Parameters is not null)
        {
            RegisterParameters(castOpDecl.Parameters, methodScope, fileScope, symbolTable, ambientTypeResolver, sourceText, diagnostics);
        }

        // Resolve the body. A no-body (;) form is accepted by the grammar
        // (for intrinsic cast operators) and has no statements to resolve;
        // Pass 1e validates whether ; is permitted based on the intrinsic modifier.
        if (castOpDecl.Body.GetBlock() is BlockNode castOpBlock)
            ResolveBlock(castOpBlock, methodScope, fileScope, enclosingType, /* isStatic */ true, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
    }

    /// <summary>
    /// Resolves property type references, <c>field</c> keyword, and <c>value</c> implicit parameter.
    /// </summary>
    private static void ResolvePropertyDeclaration(
        PropertyDeclarationNode propertyDecl,
        Scope typeScope,
        Scope fileScope,
        IType enclosingType,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        switch (propertyDecl)
        {
            case BlockPropertyPropertyDeclarationNode blockProp:
                ResolveBlockPropertyDeclaration(blockProp, typeScope, fileScope, enclosingType, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                break;
            case ExpressionBodiedPropertyPropertyDeclarationNode exprProp:
                ResolveExpressionBodiedPropertyDeclaration(exprProp, typeScope, fileScope, enclosingType, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                break;
        }
    }

    private static void ResolveBlockPropertyDeclaration(
        BlockPropertyPropertyDeclarationNode blockProp,
        Scope typeScope,
        Scope fileScope,
        IType enclosingType,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        // Resolve the property type and update the property symbol.
        var propType = ResolveTypeReference(blockProp.Type, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);
        var propSymbol = symbolTable.GetDeclaredSymbol(blockProp) as Symbol;
        if (propSymbol is not null)
        {
            propSymbol.DeclaredType = propType;
        }

        // Also update the backing field's declared type if present.
        var propInfo = propSymbol?.PropertyMetadata;
        if (propInfo?.BackingField is Symbol backingField)
        {
            backingField.DeclaredType = propType;
        }

        var isStatic = blockProp.Modifiers.Any(m => m is StaticModifierModifierNode);

        // Resolve each accessor body.
        foreach (var accessor in blockProp.Accessors)
        {
            ResolveAccessorBody(accessor, propType, propInfo, typeScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
        }

        // Resolve the optional initializer expression.
        if (blockProp.Initializer is ExpressionNode initExpr)
        {
            ResolveExpression(initExpr, typeScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
        }
    }

    private static void ResolveExpressionBodiedPropertyDeclaration(
        ExpressionBodiedPropertyPropertyDeclarationNode exprProp,
        Scope typeScope,
        Scope fileScope,
        IType enclosingType,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        // Resolve the property type and update the property symbol.
        var propType = ResolveTypeReference(exprProp.Type, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);
        var propSymbol = symbolTable.GetDeclaredSymbol(exprProp) as Symbol;
        if (propSymbol is not null)
        {
            propSymbol.DeclaredType = propType;
        }

        var isStatic = exprProp.Modifiers.Any(m => m is StaticModifierModifierNode);

        // Resolve the expression body.
        ResolveExpression(exprProp.Body, typeScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
    }

    /// <summary>
    /// Resolves an accessor body, injecting implicit <c>value</c> parameter for set/init
    /// and resolving <c>field</c> keyword references.
    /// </summary>
    private static void ResolveAccessorBody(
        AccessorDeclarationNode accessor,
        IType propType,
        PropertyInfo? propInfo,
        Scope typeScope,
        Scope fileScope,
        IType enclosingType,
        bool isStatic,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        // Create an accessor scope that includes the backing field symbol (if any)
        // so that 'field' keyword resolution finds the correct backing field.
        Scope accessorBaseScope;
        if (propInfo?.BackingField is Symbol backingFieldSym)
        {
            accessorBaseScope = new Scope(parent: typeScope);
            // Register a synthetic property symbol that points to this specific backing field,
            // so ResolveFieldKeyword finds the correct one.
            var syntheticProp = new Symbol("__accessor_property__", SymbolKind.Property, SymbolOrigin.UserCode, accessorBaseScope, propType, declarationSpan: null);
            syntheticProp.PropertyMetadata = propInfo;
            accessorBaseScope.AddMember(syntheticProp);
        }
        else
        {
            accessorBaseScope = typeScope;
        }

        switch (accessor)
        {
            case BlockGetAccessorAccessorDeclarationNode blockGet:
                ResolveBlock(blockGet.Body, accessorBaseScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                break;
            case ExpressionGetAccessorAccessorDeclarationNode exprGet:
                ResolveExpression(exprGet.Expression, accessorBaseScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                break;
            case BlockSetAccessorAccessorDeclarationNode blockSet:
            {
                // Inject implicit 'value' parameter of the property's declared type.
                var setScope = new Scope(parent: accessorBaseScope);
                var valueSymbol = new Symbol("value", SymbolKind.Parameter, SymbolOrigin.UserCode, setScope, propType, declarationSpan: null);
                setScope.AddMember(valueSymbol);
                ResolveBlock(blockSet.Body, setScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                break;
            }
            case ExpressionSetAccessorAccessorDeclarationNode exprSet:
            {
                var setScope = new Scope(parent: accessorBaseScope);
                var valueSymbol = new Symbol("value", SymbolKind.Parameter, SymbolOrigin.UserCode, setScope, propType, declarationSpan: null);
                setScope.AddMember(valueSymbol);
                ResolveExpression(exprSet.Expression, setScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                break;
            }
            case BlockInitAccessorAccessorDeclarationNode blockInit:
            {
                var initScope = new Scope(parent: accessorBaseScope);
                var valueSymbol = new Symbol("value", SymbolKind.Parameter, SymbolOrigin.UserCode, initScope, propType, declarationSpan: null);
                initScope.AddMember(valueSymbol);
                ResolveBlock(blockInit.Body, initScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                break;
            }
            case ExpressionInitAccessorAccessorDeclarationNode exprInit:
            {
                var initScope = new Scope(parent: accessorBaseScope);
                var valueSymbol = new Symbol("value", SymbolKind.Parameter, SymbolOrigin.UserCode, initScope, propType, declarationSpan: null);
                initScope.AddMember(valueSymbol);
                ResolveExpression(exprInit.Expression, initScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                break;
            }
            // Auto accessors (AutoGet, AutoSet, AutoInit) have no body to resolve.
        }
    }

    /// <summary>
    /// Resolves indexer type references and accessor bodies.
    /// </summary>
    private static void ResolveIndexerDeclaration(
        IndexerDeclarationNode indexerDecl,
        Scope typeScope,
        Scope fileScope,
        IType enclosingType,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        if (indexerDecl is not BlockIndexerIndexerDeclarationNode blockIndexer)
            return;

        // Resolve the indexer return type and update the indexer symbol.
        var indexerType = ResolveTypeReference(blockIndexer.Type, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);
        var indexerSymbol = symbolTable.GetDeclaredSymbol(blockIndexer) as Symbol;
        if (indexerSymbol is not null)
        {
            indexerSymbol.DeclaredType = indexerType;
        }

        var isStatic = blockIndexer.Modifiers.Any(m => m is StaticModifierModifierNode);

        // Create a scope for the indexer parameters.
        var indexerScope = new Scope(parent: typeScope);

        // Register indexer parameters.
        RegisterParameters(blockIndexer.Parameters, indexerScope, fileScope, symbolTable, ambientTypeResolver, sourceText, diagnostics);

        // Resolve each accessor body with the indexer scope (which has the parameters).
        var propInfo = indexerSymbol?.PropertyMetadata;
        foreach (var accessor in blockIndexer.Accessors)
        {
            ResolveAccessorBody(accessor, indexerType, propInfo, indexerScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
        }
    }

    private static void RegisterParameters(
        ParameterListNode paramList,
        Scope methodScope,
        Scope fileScope,
        SymbolTable symbolTable,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        // The grammar rule is: ParameterList = Parameter (COMMA Parameter)*
        // Iterate all ParameterNode children uniformly to register every parameter.
        foreach (var child in paramList.Children())
        {
            if (child is ParameterNode param)
            {
                RegisterSingleParameter(param, methodScope, fileScope, symbolTable, ambientTypeResolver, sourceText, diagnostics);
            }
        }
    }

    private static void RegisterSingleParameter(
        ParameterNode param,
        Scope methodScope,
        Scope fileScope,
        SymbolTable symbolTable,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        var paramType = ResolveTypeReference(param.Type, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);
        var paramName = param.Name.Lexeme;
        var paramSpan = ToMistSourceSpan(param.Name.Span);

        // Check for duplicate parameter names.
        var existing = methodScope.Lookup(paramName);
        if (existing is not null && existing.ContainingScope == methodScope)
        {
            diagnostics.Add(MakeDiagnosticWithLocation(
                "MIST_DUPLICATE_DECLARATION",
                DiagnosticSeverity.Error,
                $"A parameter named '{paramName}' is already declared in this scope.",
                param,
                sourceText));
            return;
        }

        var paramSymbol = new Symbol(paramName, SymbolKind.Parameter, SymbolOrigin.UserCode, methodScope, paramType, paramSpan);
        methodScope.AddMember(paramSymbol);
        symbolTable.Register(paramSymbol, param);
    }

    // ─── Block and Statement Resolution ──────────────────────────────────

    private static void ResolveBlock(
        BlockNode block,
        Scope parentScope,
        Scope fileScope,
        IType enclosingType,
        bool isStatic,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        var blockScope = new Scope(parent: parentScope);

        foreach (var stmt in block.Statements)
        {
            ResolveStatement(stmt, blockScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
        }
    }

    private static void ResolveStatement(
        StatementNode stmt,
        Scope blockScope,
        Scope fileScope,
        IType enclosingType,
        bool isStatic,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        switch (stmt)
        {
            case VariableDeclarationStatementStatementNode varDeclStmt:
                ResolveVariableDeclaration(varDeclStmt.VariableDeclaration, blockScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                break;
            case ReturnStatementStatementNode returnStmt:
                if (returnStmt.ReturnStatement.Value is not null)
                {
                    ResolveExpression(returnStmt.ReturnStatement.Value, blockScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                }
                break;
            case IfStatementStatementNode ifStmt:
                ResolveIfStatement(ifStmt.IfStatement, blockScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                break;
            case WhileStatementStatementNode whileStmt:
                ResolveExpression(whileStmt.WhileStatement.Condition, blockScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                ResolveBlock(whileStmt.WhileStatement.Body, blockScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                break;
            case ForStatementStatementNode forStmt:
                ResolveForStatement(forStmt.ForStatement, blockScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                break;
            case ForeachStatementStatementNode foreachStmt:
                ResolveForeachStatement(foreachStmt.ForeachStatement, blockScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                break;
            case ExpressionStatementStatementNode exprStmt:
                ResolveExpression(exprStmt.ExpressionStatement.Expression, blockScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                break;
            case BreakStatementStatementNode:
                // No type resolution needed.
                break;
        }
    }

    private static void ResolveVariableDeclaration(
        VariableDeclarationStatementNode varDecl,
        Scope blockScope,
        Scope fileScope,
        IType enclosingType,
        bool isStatic,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        switch (varDecl)
        {
            case TypedVariableDeclarationVariableDeclarationStatementNode typed:
            {
                var declaredType = ResolveTypeReference(typed.Type, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);
                var initType = ResolveExpression(typed.Initializer, blockScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);

                var varName = typed.Name.Lexeme;
                var varSpan = ToMistSourceSpan(typed.Name.Span);

                CheckDuplicateLocal(varName, blockScope, sourceText, diagnostics);

                var varSymbol = new Symbol(varName, SymbolKind.LocalVariable, SymbolOrigin.UserCode, blockScope, declaredType, varSpan);
                blockScope.AddMember(varSymbol);
                symbolTable.Register(varSymbol, varDecl);
                break;
            }
            case InferredVariableDeclarationVariableDeclarationStatementNode inferred:
            {
                // var — infer type from initializer.
                var initType = ResolveExpression(inferred.Initializer, blockScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);

                var varName = inferred.Name.Lexeme;
                var varSpan = ToMistSourceSpan(inferred.Name.Span);

                CheckDuplicateLocal(varName, blockScope, sourceText, diagnostics);

                var varSymbol = new Symbol(varName, SymbolKind.LocalVariable, SymbolOrigin.UserCode, blockScope, initType, varSpan);
                blockScope.AddMember(varSymbol);
                symbolTable.Register(varSymbol, varDecl);
                break;
            }
        }
    }

    private static void ResolveIfStatement(
        IfStatementNode ifStmt,
        Scope blockScope,
        Scope fileScope,
        IType enclosingType,
        bool isStatic,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        ResolveExpression(ifStmt.Condition, blockScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
        ResolveBlock(ifStmt.ThenBranch, blockScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);

        if (ifStmt.ElseBranch is ElseIfElseClauseNode elseIf)
        {
            ResolveIfStatement(elseIf.ElseIf, blockScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
        }
        else if (ifStmt.ElseBranch is ElseBlockElseClauseNode elseBlock)
        {
            ResolveBlock(elseBlock.ElseBlock, blockScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
        }
    }

    private static void ResolveForStatement(
        ForStatementNode forStmt,
        Scope blockScope,
        Scope fileScope,
        IType enclosingType,
        bool isStatic,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        // Create a scope for the for statement (initializer variable is scoped here).
        var forScope = new Scope(parent: blockScope);

        if (forStmt.Initializer is ForInitializerVariableDeclarationForInitializerNode varInit)
        {
            var initType = ResolveTypeReference(varInit.VariableDeclaration, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);
            var initExprType = ResolveExpression(varInit.Initializer, forScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);

            var varName = varInit.Name.Lexeme;
            var varSpan = ToMistSourceSpan(varInit.Name.Span);
            var varSymbol = new Symbol(varName, SymbolKind.LocalVariable, SymbolOrigin.UserCode, forScope, initType, varSpan);
            forScope.AddMember(varSymbol);
            symbolTable.Register(varSymbol, varInit);
        }
        else if (forStmt.Initializer is ForInitializerExpressionForInitializerNode exprInit)
        {
            ResolveExpression(exprInit.Expression, forScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
        }

        if (forStmt.Condition is not null)
        {
            ResolveExpression(forStmt.Condition, forScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
        }

        if (forStmt.Iterator is not null)
        {
            ResolveExpression(forStmt.Iterator, forScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
        }

        ResolveBlock(forStmt.Body, forScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
    }

    private static void ResolveForeachStatement(
        ForeachStatementNode foreachStmt,
        Scope blockScope,
        Scope fileScope,
        IType enclosingType,
        bool isStatic,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        var elementType = ResolveTypeReference(foreachStmt.Type, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);
        ResolveExpression(foreachStmt.Collection, blockScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);

        // Create a scope for the foreach body with the iteration variable.
        var foreachScope = new Scope(parent: blockScope);
        var varName = foreachStmt.Name.Lexeme;
        var varSpan = ToMistSourceSpan(foreachStmt.Name.Span);
        var varSymbol = new Symbol(varName, SymbolKind.LocalVariable, SymbolOrigin.UserCode, foreachScope, elementType, varSpan);
        foreachScope.AddMember(varSymbol);
        symbolTable.Register(varSymbol, foreachStmt);

        ResolveBlock(foreachStmt.Body, foreachScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
    }

    // ─── Type Reference Resolution ───────────────────────────────────────

    /// <summary>
    /// Resolves a TypeReference AST node to an IType.
    /// Lookup order: primitives → string/void → scope chain → imported namespaces → UnknownType.
    /// </summary>
    private static IType ResolveTypeReference(
        TypeReferenceNode typeRef,
        Scope fileScope,
        IAmbientTypeResolver? ambientTypeResolver,
        SymbolTable symbolTable,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        IType baseType;

        switch (typeRef)
        {
            case PrimitiveTypeRefTypeReferenceNode primitiveRef:
                // Task 31 of the Mist Standard Library spec: primitive
                // keyword references (`int`, `bool`, `string`, …) now
                // resolve through ResolvePrimitiveKeyword, which walks
                // directly to the Standard namespace scope and returns
                // the stdlib IType instance. The resolution bypasses
                // every scope-chain / using-directive / user-type lookup
                // (Requirement 7.6): a user declaring `class Int32 { }`
                // in their own namespace cannot intercept the keyword
                // `int`.
                //
                // Fallback: when no stdlib has been loaded for this
                // compilation (unit tests that build a semantic model
                // from a minimal AST), LookupQualifiedStdlibType throws
                // StdlibMissingException. We catch and fall back to the
                // legacy LegacyPrimitiveTypes singletons
                // (LegacyPrimitiveTypes.Int, LegacyPrimitiveTypes.String,
                // …) — NOT the public property accessors (which read the
                // global registry, whose state might reflect a previous
                // compilation's stdlib). The direct-to-legacy fallback
                // guarantees that a stdlib-less build never embeds
                // another build's stdlib IType instances in its own
                // expression-type map.
                IType primBase;
                try
                {
                    var primGlobalScope = FindGlobalScope(fileScope) ?? fileScope;
                    primBase = ResolvePrimitiveKeyword(primGlobalScope, symbolTable, primitiveRef.PrimitiveType);
                }
                catch (StdlibMissingException)
                {
                    primBase = ResolveLegacyPrimitive(primitiveRef.PrimitiveType);
                }
                if (primitiveRef.ArrayRank.HasValue)
                {
                    return new ArrayType(primBase);
                }
                return primBase;

            case VoidTypeRefTypeReferenceNode:
                // Resolve void through the stdlib path, same as primitives.
                try
                {
                    var voidGlobalScope = FindGlobalScope(fileScope) ?? fileScope;
                    return ResolveVoidKeyword(voidGlobalScope, symbolTable);
                }
                catch (StdlibMissingException)
                {
                    return LegacyPrimitiveTypes.Void;
                }

            case NamedTypeRefTypeReferenceNode namedRef:
                var typeName = ExtractQualifiedName(namedRef.Name, sourceText);

                // Check if the identifier resolves to a TypeParameterType first.
                // Type parameters don't accept type arguments.
                var namedSymbol = fileScope.Lookup(typeName);
                if (namedSymbol is not null && namedSymbol.DeclaredType is TypeParameterType tpt)
                {
                    if (namedRef.TypeArgs is not null)
                    {
                        diagnostics.Add(MakeDiagnosticWithLocation(
                            DiagnosticId.NotGeneric.Value,
                            DiagnosticSeverity.Error,
                            $"Type '{typeName}' is not generic and does not accept type arguments.",
                            namedRef,
                            sourceText));
                    }
                    return tpt;
                }

                baseType = ResolveNamedType(typeName, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);

                // Handle type arguments if present.
                if (namedRef.TypeArgs is not null)
                {
                    baseType = ResolvePass2TypeArguments(baseType, typeName, namedRef.TypeArgs, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);
                }

                if (namedRef.ArrayRank.HasValue)
                {
                    return new ArrayType(baseType);
                }
                return baseType;

            default:
                return UnknownType.Instance;
        }
    }

    /// <summary>
    /// Maps a PrimitiveTypeNode to its well-known legacy fallback type.
    /// Used only when stdlib is not loaded (the primary path goes through
    /// ResolvePrimitiveKeyword). Post-Task 49: uses LegacyPrimitiveTypes
    /// instead of the deleted PrimitiveType singletons.
    /// </summary>
    private static IType ResolvePrimitiveType(PrimitiveTypeNode node)
    {
        return node switch
        {
            IntTypePrimitiveTypeNode => LegacyPrimitiveTypes.Int,
            UintTypePrimitiveTypeNode => LegacyPrimitiveTypes.Uint,
            LongTypePrimitiveTypeNode => LegacyPrimitiveTypes.Long,
            UlongTypePrimitiveTypeNode => LegacyPrimitiveTypes.Ulong,
            ShortTypePrimitiveTypeNode => LegacyPrimitiveTypes.Short,
            UshortTypePrimitiveTypeNode => LegacyPrimitiveTypes.Ushort,
            ByteTypePrimitiveTypeNode => LegacyPrimitiveTypes.Byte,
            SbyteTypePrimitiveTypeNode => LegacyPrimitiveTypes.Sbyte,
            FloatTypePrimitiveTypeNode => LegacyPrimitiveTypes.Float,
            DoubleTypePrimitiveTypeNode => LegacyPrimitiveTypes.Double,
            DecimalTypePrimitiveTypeNode => LegacyPrimitiveTypes.Decimal,
            BoolTypePrimitiveTypeNode => LegacyPrimitiveTypes.Bool,
            CharTypePrimitiveTypeNode => LegacyPrimitiveTypes.Char,
            StringTypePrimitiveTypeNode => LegacyPrimitiveTypes.String,
            _ => UnknownType.Instance,
        };
    }

    /// <summary>
    /// Pass-2 fallback for a primitive keyword reference when no stdlib
    /// has been loaded for the current compilation: returns the legacy
    /// fallback singleton DIRECTLY (bypassing the
    /// <see cref="PrimitiveTypeRegistry"/>-backed
    /// public property accessors). Using the legacy singletons here
    /// guarantees that a build without stdlib never embeds another
    /// parallel build's stdlib <see cref="IType"/> instances in its own
    /// expression-type map — a behaviour that would make reference-
    /// equality assertions order-dependent under parallel test
    /// execution.
    /// </summary>
    private static IType ResolveLegacyPrimitive(PrimitiveTypeNode node)
    {
        return node switch
        {
            IntTypePrimitiveTypeNode => LegacyPrimitiveTypes.Int,
            UintTypePrimitiveTypeNode => LegacyPrimitiveTypes.Uint,
            LongTypePrimitiveTypeNode => LegacyPrimitiveTypes.Long,
            UlongTypePrimitiveTypeNode => LegacyPrimitiveTypes.Ulong,
            ShortTypePrimitiveTypeNode => LegacyPrimitiveTypes.Short,
            UshortTypePrimitiveTypeNode => LegacyPrimitiveTypes.Ushort,
            ByteTypePrimitiveTypeNode => LegacyPrimitiveTypes.Byte,
            SbyteTypePrimitiveTypeNode => LegacyPrimitiveTypes.Sbyte,
            FloatTypePrimitiveTypeNode => LegacyPrimitiveTypes.Float,
            DoubleTypePrimitiveTypeNode => LegacyPrimitiveTypes.Double,
            DecimalTypePrimitiveTypeNode => LegacyPrimitiveTypes.Decimal,
            BoolTypePrimitiveTypeNode => LegacyPrimitiveTypes.Bool,
            CharTypePrimitiveTypeNode => LegacyPrimitiveTypes.Char,
            StringTypePrimitiveTypeNode => LegacyPrimitiveTypes.String,
            _ => UnknownType.Instance,
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Stdlib type lookup — direct, scope-chain-bypassing resolution
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Resolves a fully-qualified Standard Library type name (e.g.,
    /// <c>"Standard.Int32"</c>) by walking directly from the global scope to
    /// the target namespace scope and looking up the simple name among its
    /// members. Does <b>not</b> consult any file scope, <c>using</c>
    /// directive, or user-declared type — per Requirement 7.6 this path
    /// intentionally bypasses all ordinary name-resolution machinery so that
    /// a primitive keyword alias like <c>int</c> always reaches the stdlib
    /// type and cannot be shadowed by user code.
    ///
    /// <para>
    /// Throws <see cref="StdlibMissingException"/> (carrying diagnostic
    /// <c>MIST_STDLIB_MISSING</c>) if the namespace scope is missing (the
    /// stdlib has not been loaded into this compilation) or if the requested
    /// simple name does not resolve to a type symbol inside the namespace.
    /// </para>
    /// </summary>
    /// <param name="globalScope">
    /// The compilation's global scope (returned by the running
    /// <see cref="Build"/> invocation).
    /// </param>
    /// <param name="symbolTable">
    /// The symbol table that <see cref="FindExistingNamespaceScope"/> uses to
    /// locate namespace scopes.
    /// </param>
    /// <param name="qualifiedName">
    /// The fully-qualified stdlib type name in <c>Namespace.SimpleName</c>
    /// form (e.g., <c>"Standard.Int32"</c>).
    /// </param>
    internal static IType LookupQualifiedStdlibType(
        Scope globalScope,
        SymbolTable symbolTable,
        string qualifiedName)
    {
        // Split "Namespace.SimpleName" into its two parts. The stdlib keeps a
        // single-segment namespace ("Standard"); we intentionally split on
        // the LAST '.' so multi-segment namespaces like
        // "Standard.Collections.Generic" would still work if the stdlib ever
        // grows them.
        var lastDot = qualifiedName.LastIndexOf('.');
        if (lastDot <= 0 || lastDot >= qualifiedName.Length - 1)
        {
            throw new StdlibMissingException(
                qualifiedName,
                $"MIST_STDLIB_MISSING: stdlib type name '{qualifiedName}' is not fully qualified.");
        }

        var namespaceName = qualifiedName.Substring(0, lastDot);
        var simpleName = qualifiedName.Substring(lastDot + 1);

        // Walk directly to the target namespace scope. No file scope, no
        // using-directive, no scope chain.
        var namespaceScope = FindExistingNamespaceScope(globalScope, symbolTable, namespaceName);
        if (namespaceScope is null)
        {
            throw new StdlibMissingException(
                qualifiedName,
                $"MIST_STDLIB_MISSING: Standard Library namespace '{namespaceName}' is not present in the semantic model (stdlib not loaded?).");
        }

        // Look up the simple name among the namespace's direct members.
        // `Scope.Lookup` would walk up to the parent on a miss; here we
        // iterate members directly so a miss stays a miss (we must not fall
        // through to an unrelated global-scope symbol).
        foreach (var member in namespaceScope.Members)
        {
            if (member.Name != simpleName) continue;
            if (member.Kind != SymbolKind.Class
                && member.Kind != SymbolKind.Struct
                && member.Kind != SymbolKind.Interface)
            {
                continue;
            }

            if (member.DeclaredType is IType declaredType)
            {
                return declaredType;
            }
        }

        throw new StdlibMissingException(
            qualifiedName,
            $"MIST_STDLIB_MISSING: Standard Library type '{qualifiedName}' is not present in the semantic model.");
    }

    /// <summary>
    /// Resolves a <see cref="PrimitiveTypeNode"/> (e.g., the AST node for the
    /// <c>int</c> keyword) to its corresponding Standard Library type,
    /// bypassing every scoping mechanism (file scope, <c>using</c> directives,
    /// user-declared types with matching simple names). Per Requirement 7.6
    /// this path is not overridable: the keyword <c>int</c> always resolves
    /// to the type named by <see cref="KeywordAliases.Map"/>
    /// (<c>Standard.Int32</c>), regardless of what else is in scope.
    ///
    /// <para>
    /// Task 31 of the Mist Standard Library spec flipped the primitive-
    /// type-reference path in <c>Pass2_ResolveTypes</c> to call this helper
    /// (with a fallback to the legacy <see cref="ResolvePrimitiveType"/>
    /// when no stdlib has been loaded, e.g., in unit tests that build a
    /// semantic model from a minimal AST). The fallback returns the same
    /// <see cref="IType"/> instance the compat-shim
    /// <c>PrimitiveType.Int</c> property would return (Task 32), so
    /// reference equality holds regardless of which path runs.
    /// </para>
    /// </summary>
    /// <param name="globalScope">The compilation's global scope.</param>
    /// <param name="symbolTable">The compilation's symbol table.</param>
    /// <param name="keyword">The primitive-keyword AST node to resolve.</param>
    internal static IType ResolvePrimitiveKeyword(
        Scope globalScope,
        SymbolTable symbolTable,
        PrimitiveTypeNode keyword)
    {
        // Extract the keyword lexeme (e.g., "int") using the existing
        // PrimitiveTypeNode → string mapping.
        var keywordText = FormatPrimitiveType(keyword);

        // Look it up in the single-source-of-truth Keyword_Alias_Mapping
        // (Requirement 7.5, Task 7).
        if (!KeywordAliases.Map.TryGetValue(keywordText, out var qualifiedName))
        {
            throw new StdlibMissingException(
                keywordText,
                $"MIST_STDLIB_MISSING: primitive keyword '{keywordText}' has no entry in KeywordAliases.Map.");
        }

        // Resolve the fully-qualified name directly against the stdlib
        // namespace — never through any scope chain or using-directive.
        return LookupQualifiedStdlibType(globalScope, symbolTable, qualifiedName);
    }

    /// <summary>
    /// Resolves the <c>void</c> keyword to its Standard Library type
    /// (<c>Standard.Void</c>), following the same pattern as
    /// <see cref="ResolvePrimitiveKeyword"/>. Bypasses all scoping.
    /// </summary>
    internal static IType ResolveVoidKeyword(Scope globalScope, SymbolTable symbolTable)
    {
        return LookupQualifiedStdlibType(globalScope, symbolTable, "Standard.Void");
    }

    /// <summary>
    /// Resolves the metatype <c>Type</c> to its Standard Library type
    /// (<c>Standard.Type</c>), following the same pattern as
    /// <see cref="ResolvePrimitiveKeyword"/>. Bypasses all scoping.
    /// </summary>
    internal static IType ResolveTypeKeyword(Scope globalScope, SymbolTable symbolTable)
    {
        return LookupQualifiedStdlibType(globalScope, symbolTable, "Standard.Type");
    }

    // (Task 49: PopulatePrimitiveTypeRegistry has been deleted along with
    // PrimitiveTypeRegistry. Stdlib types are resolved directly via
    // ResolvePrimitiveKeyword in Pass 2.)

    /// <summary>
    /// Resolves a named type by searching the scope chain and imported namespaces.
    /// </summary>
    private static IType ResolveNamedType(
        string typeName,
        Scope fileScope,
        IAmbientTypeResolver? ambientTypeResolver,
        SymbolTable symbolTable,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        // 1. Check well-known primitive types by name.
        var wellKnown = TryResolveWellKnownType(typeName);
        if (wellKnown is not null) return wellKnown;

        // 2. Search current scope → parent scopes (local → file → namespace → global).
        var symbol = fileScope.Lookup(typeName);
        if (symbol is not null && (symbol.Kind == SymbolKind.Class || symbol.Kind == SymbolKind.Struct || symbol.Kind == SymbolKind.Interface || symbol.Kind == SymbolKind.Trait))
        {
            return symbol.DeclaredType ?? UnknownType.Instance;
        }

        // 3. Search imported namespaces from using declarations.
        var importedNamespaces = CollectImportedNamespaces(fileScope);

        // 3a. Search user-defined namespace scopes for the type.
        IType? userResolvedType = null;
        string? userResolvedNs = null;
        string? ambiguousNs = null;

        // Find the global scope by walking up from fileScope.
        var globalScope = FindGlobalScope(fileScope);

        foreach (var ns in importedNamespaces)
        {
            // Search user-defined namespace scopes.
            var nsScope = globalScope is not null
                ? FindExistingNamespaceScope(globalScope, symbolTable, ns)
                : null;

            if (nsScope is not null)
            {
                foreach (var member in nsScope.Members)
                {
                    if (member.Name == typeName &&
                        (member.Kind == SymbolKind.Class || member.Kind == SymbolKind.Struct || member.Kind == SymbolKind.Interface || member.Kind == SymbolKind.Trait))
                    {
                        if (userResolvedType is not null)
                        {
                            // Ambiguity: same type name found in multiple imported namespaces.
                            ambiguousNs = ns;
                        }
                        else
                        {
                            userResolvedType = member.DeclaredType ?? UnknownType.Instance;
                            userResolvedNs = ns;
                        }
                        break;
                    }
                }
            }
        }

        if (ambiguousNs is not null && userResolvedNs is not null)
        {
            diagnostics.Add(new Diagnostic(
                new DiagnosticId("MIST_AMBIGUOUS_TYPE"),
                DiagnosticSeverity.Error,
                PipelineStage.SemanticModelConstruction,
                $"The type name '{typeName}' is ambiguous between namespaces '{userResolvedNs}' and '{ambiguousNs}'.",
                Location: null,
                CompilationUnitPath: sourceText?.Path,
                AnalyzerName: null));
            return UnknownType.Instance;
        }

        if (userResolvedType is not null)
        {
            return userResolvedType;
        }

        // 3b. Search ambient namespaces via ambient resolver.
        if (ambientTypeResolver is not null)
        {
            foreach (var ns in importedNamespaces)
            {
                // Try ambient resolver for each imported namespace.
                var resolvedType = ambientTypeResolver.ResolveType(ns, typeName);
                if (resolvedType is not null)
                {
                    // Register the resolved ambient type in the scope hierarchy.
                    RegisterAmbientTypeInScope(resolvedType, typeName, ns, fileScope, ambientTypeResolver, symbolTable);
                    return resolvedType;
                }
            }
        }

        // 4. Not found → emit diagnostic, return UnknownType.
        diagnostics.Add(new Diagnostic(
            new DiagnosticId("MIST_UNDEFINED_TYPE"),
            DiagnosticSeverity.Error,
            PipelineStage.SemanticModelConstruction,
            $"The type '{typeName}' could not be found.",
            Location: null,
            CompilationUnitPath: sourceText?.Path,
            AnalyzerName: null));

        return UnknownType.Instance;
    }

    /// <summary>
    /// Finds the global scope by walking up the parent chain from the given scope.
    /// The global scope is the one with no parent.
    /// </summary>
    private static Scope? FindGlobalScope(Scope scope)
    {
        IScope? current = scope;
        while (current is not null)
        {
            if (current is Scope s && s.Parent is null)
                return s;
            current = current.Parent;
        }
        return scope; // fallback
    }

    /// <summary>
    /// Resolves type arguments on a type reference in Pass 2 context.
    /// Validates arity and emits diagnostics for mismatches and non-generic types.
    /// Returns a <see cref="ConstructedType"/> if the type is generic with matching arity,
    /// or the original type with diagnostics if there's an error.
    /// </summary>
    private static IType ResolvePass2TypeArguments(
        IType resolvedType,
        string typeName,
        TypeArgumentListNode typeArgList,
        Scope fileScope,
        IAmbientTypeResolver? ambientTypeResolver,
        SymbolTable symbolTable,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        // If the resolved type is unknown, don't cascade errors.
        if (resolvedType is UnknownType)
            return resolvedType;

        var typeParams = GetTypeParameters(resolvedType);
        if (typeParams.Count == 0)
        {
            // The type is not generic — emit MIST_NOT_GENERIC.
            diagnostics.Add(MakeDiagnosticWithLocation(
                DiagnosticId.NotGeneric.Value,
                DiagnosticSeverity.Error,
                $"Type '{typeName}' is not generic and does not accept type arguments.",
                typeArgList,
                sourceText));
            return resolvedType;
        }

        // Resolve each type argument recursively.
        var resolvedTypeArgs = ResolvePass2TypeArgumentList(typeArgList, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);

        if (resolvedTypeArgs.Count != typeParams.Count)
        {
            // Arity mismatch — emit MIST_ARITY_MISMATCH.
            diagnostics.Add(MakeDiagnosticWithLocation(
                DiagnosticId.ArityMismatch.Value,
                DiagnosticSeverity.Error,
                $"Type '{typeName}' expects {typeParams.Count} type argument(s) but received {resolvedTypeArgs.Count}.",
                typeArgList,
                sourceText));
            return UnknownType.Instance;
        }

        return new ConstructedType(resolvedType, resolvedTypeArgs);
    }

    /// <summary>
    /// Resolves a <see cref="TypeArgumentListNode"/> to a list of <see cref="IType"/> instances
    /// in Pass 2 context. Each type argument is resolved using the standard
    /// <see cref="ResolveTypeReference"/> method, supporting nested generics.
    /// Emits <c>MIST_UNDEFINED_TYPE_ARGUMENT</c> for unresolvable type arguments.
    /// </summary>
    private static IReadOnlyList<IType> ResolvePass2TypeArgumentList(
        TypeArgumentListNode typeArgList,
        Scope fileScope,
        IAmbientTypeResolver? ambientTypeResolver,
        SymbolTable symbolTable,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        var resolvedArgs = new List<IType>();

        // Resolve the first type argument.
        var firstType = ResolvePass2SingleTypeArg(typeArgList.TypeReference, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);
        resolvedArgs.Add(firstType);

        // Resolve the rest of the type arguments.
        foreach (var typeRef in typeArgList.TypeReferences)
        {
            var argType = ResolvePass2SingleTypeArg(typeRef, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);
            resolvedArgs.Add(argType);
        }

        return resolvedArgs;
    }

    /// <summary>
    /// Resolves a single type argument in Pass 2 context.
    /// Uses <see cref="ResolveTypeReference"/> for full recursive resolution.
    /// Emits <c>MIST_UNDEFINED_TYPE_ARGUMENT</c> if the result is <see cref="UnknownType"/>
    /// and the type reference is a named type (not a primitive or void).
    /// </summary>
    private static IType ResolvePass2SingleTypeArg(
        TypeReferenceNode typeRef,
        Scope fileScope,
        IAmbientTypeResolver? ambientTypeResolver,
        SymbolTable symbolTable,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        // For named type references, check if the name can be resolved before
        // calling the full ResolveTypeReference (which emits MIST_UNDEFINED_TYPE).
        // If it can't be resolved, emit MIST_UNDEFINED_TYPE_ARGUMENT instead.
        if (typeRef is NamedTypeRefTypeReferenceNode namedRef)
        {
            var argName = ExtractQualifiedName(namedRef.Name, sourceText);

            // Check if the name resolves to anything.
            var wellKnown = TryResolveWellKnownType(argName);
            if (wellKnown is null)
            {
                var symbol = fileScope.Lookup(argName);
                if (symbol is null || (symbol.Kind != SymbolKind.Class && symbol.Kind != SymbolKind.Struct && symbol.Kind != SymbolKind.Interface && symbol.Kind != SymbolKind.Trait))
                {
                    // Check user-defined namespace scopes first.
                    var importedNamespaces = CollectImportedNamespaces(fileScope);
                    var globalScope = FindGlobalScope(fileScope);
                    var userResolved = false;

                    foreach (var ns in importedNamespaces)
                    {
                        var nsScope = globalScope is not null
                            ? FindExistingNamespaceScope(globalScope, symbolTable, ns)
                            : null;

                        if (nsScope is not null)
                        {
                            foreach (var member in nsScope.Members)
                            {
                                if (member.Name == argName &&
                                    (member.Kind == SymbolKind.Class || member.Kind == SymbolKind.Struct || member.Kind == SymbolKind.Interface || member.Kind == SymbolKind.Trait))
                                {
                                    userResolved = true;
                                    break;
                                }
                            }
                            if (userResolved) break;
                        }
                    }

                    // Check ambient resolver.
                    IType? ambientResolved = null;
                    if (!userResolved && ambientTypeResolver is not null)
                    {
                        foreach (var ns in importedNamespaces)
                        {
                            ambientResolved = ambientTypeResolver.ResolveType(ns, argName);
                            if (ambientResolved is not null)
                                break;
                        }
                    }

                    if (!userResolved && ambientResolved is null)
                    {
                        diagnostics.Add(MakeDiagnosticWithLocation(
                            DiagnosticId.UndefinedTypeArgument.Value,
                            DiagnosticSeverity.Error,
                            $"Type argument '{argName}' could not be resolved.",
                            namedRef,
                            sourceText));
                        return UnknownType.Instance;
                    }
                }
            }
        }

        // Use the standard type reference resolution for full recursive support.
        return ResolveTypeReference(typeRef, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);
    }

    /// <summary>
    /// Tries to resolve a well-known type by name (primitives, string, void).
    /// </summary>
    private static IType? TryResolveWellKnownType(string name)
    {
        return name switch
        {
            "int" => LegacyPrimitiveTypes.Int,
            "uint" => LegacyPrimitiveTypes.Uint,
            "long" => LegacyPrimitiveTypes.Long,
            "ulong" => LegacyPrimitiveTypes.Ulong,
            "short" => LegacyPrimitiveTypes.Short,
            "ushort" => LegacyPrimitiveTypes.Ushort,
            "byte" => LegacyPrimitiveTypes.Byte,
            "sbyte" => LegacyPrimitiveTypes.Sbyte,
            "float" => LegacyPrimitiveTypes.Float,
            "double" => LegacyPrimitiveTypes.Double,
            "decimal" => LegacyPrimitiveTypes.Decimal,
            "bool" => LegacyPrimitiveTypes.Bool,
            "char" => LegacyPrimitiveTypes.Char,
            "string" => LegacyPrimitiveTypes.String,
            "void" => LegacyPrimitiveTypes.Void,
            "Void" => LegacyPrimitiveTypes.Void,
            _ => null,
        };
    }

    /// <summary>
    /// Copies type parameter symbols from the type scope to the file scope so that
    /// type parameter names (e.g., T) can be resolved when resolving member type
    /// references through the file scope. Only symbols whose DeclaredType is
    /// <see cref="TypeParameterType"/> are copied, avoiding constructor/method
    /// name collisions.
    /// </summary>
    private static void PropagateTypeParameters(Scope typeScope, Scope fileScope)
    {
        foreach (var member in typeScope.Members)
        {
            if (member.DeclaredType is TypeParameterType)
            {
                fileScope.AddMember(member);
            }
        }
    }

    /// <summary>
    /// Collects all imported namespaces from the scope chain.
    /// </summary>
    private static List<string> CollectImportedNamespaces(Scope scope)
    {
        var namespaces = new List<string>();
        IScope? current = scope;
        while (current is not null)
        {
            if (current is Scope s)
            {
                foreach (var ns in s.ImportedNamespaces)
                {
                    if (!namespaces.Contains(ns))
                    {
                        namespaces.Add(ns);
                    }
                }
            }
            current = current.Parent;
        }
        return namespaces;
    }

    /// <summary>
    /// Registers an ambient type and its members in the scope hierarchy.
    /// </summary>
    private static void RegisterAmbientTypeInScope(
        IType type,
        string typeName,
        string namespaceName,
        Scope fileScope,
        IAmbientTypeResolver ambientTypeResolver,
        SymbolTable symbolTable)
    {
        // Check if already registered in the scope.
        var existing = fileScope.Lookup(typeName);
        if (existing is not null && (existing.Kind == SymbolKind.Class || existing.Kind == SymbolKind.Struct || existing.Kind == SymbolKind.Interface || existing.Kind == SymbolKind.Trait))
        {
            return; // Already registered.
        }

        // Create a type symbol with SymbolOrigin.Backend.
        var kind = type is ClassType ? SymbolKind.Class : type is InterfaceType ? SymbolKind.Interface : type is TraitType ? SymbolKind.Trait : SymbolKind.Struct;
        var typeSymbol = new Symbol(typeName, kind, SymbolOrigin.Backend, fileScope, type, declarationSpan: null);
        fileScope.AddMember(typeSymbol);

        // Register members in the type's member scope.
        var members = ambientTypeResolver.GetMembers(type);
        if (type is ClassType ct && ct.MemberScope is Scope memberScope)
        {
            foreach (var member in members)
            {
                // Only add if not already present.
                if (memberScope.Lookup(member.Name) is null)
                {
                    memberScope.AddMember(member);
                }
            }
        }
        else if (type is StructType st && st.MemberScope is Scope structMemberScope)
        {
            foreach (var member in members)
            {
                if (structMemberScope.Lookup(member.Name) is null)
                {
                    structMemberScope.AddMember(member);
                }
            }
        }
    }

    // ─── Expression Resolution ───────────────────────────────────────────

    /// <summary>
    /// Resolves the type of an expression and records it in the expression type map.
    /// Returns the resolved type.
    /// </summary>
    private static IType ResolveExpression(
        ExpressionNode expr,
        Scope currentScope,
        Scope fileScope,
        IType enclosingType,
        bool isStatic,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        // ExpressionNode wraps AssignmentNode.
        var type = ResolveAssignment(expr.Assignment, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
        expressionTypes[expr] = type;
        return type;
    }

    private static IType ResolveAssignment(
        AssignmentNode assignment,
        Scope currentScope,
        Scope fileScope,
        IType enclosingType,
        bool isStatic,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        var targetType = ResolveLogicalOr(assignment.Target, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);

        if (assignment.Value is not null)
        {
            // This is an assignment expression: target = value.
            var valueType = ResolveAssignment(assignment.Value, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
            expressionTypes[assignment] = targetType;
            return targetType;
        }

        expressionTypes[assignment] = targetType;
        return targetType;
    }

    private static IType ResolveLogicalOr(
        LogicalOrNode node,
        Scope currentScope,
        Scope fileScope,
        IType enclosingType,
        bool isStatic,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        var leftType = ResolveLogicalAnd(node.Left, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);

        if (node.Right is not null && !ReferenceEquals(node.Right, node.Left))
        {
            ResolveLogicalAnd(node.Right, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
            var result = ResolvePrimitiveByKeyword("bool", fileScope, symbolTable);
            expressionTypes[node] = result;
            return result;
        }

        expressionTypes[node] = leftType;
        return leftType;
    }

    private static IType ResolveLogicalAnd(
        LogicalAndNode node,
        Scope currentScope,
        Scope fileScope,
        IType enclosingType,
        bool isStatic,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        var leftType = ResolveEquality(node.Left, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);

        if (node.Right is not null && !ReferenceEquals(node.Right, node.Left))
        {
            ResolveEquality(node.Right, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
            var result = ResolvePrimitiveByKeyword("bool", fileScope, symbolTable);
            expressionTypes[node] = result;
            return result;
        }

        expressionTypes[node] = leftType;
        return leftType;
    }

    private static IType ResolveEquality(
        EqualityNode node,
        Scope currentScope,
        Scope fileScope,
        IType enclosingType,
        bool isStatic,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        var leftType = ResolveRelational(node.Left, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);

        if (node.Right is not null && !ReferenceEquals(node.Right, node.Left))
        {
            ResolveRelational(node.Right, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
            var result = ResolvePrimitiveByKeyword("bool", fileScope, symbolTable);
            expressionTypes[node] = result;
            return result;
        }

        expressionTypes[node] = leftType;
        return leftType;
    }

    private static IType ResolveRelational(
        RelationalNode node,
        Scope currentScope,
        Scope fileScope,
        IType enclosingType,
        bool isStatic,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        var leftType = ResolveAdditive(node.Left, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);

        if (node.Right is not null && !ReferenceEquals(node.Right, node.Left))
        {
            ResolveAdditive(node.Right, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
            var result = ResolvePrimitiveByKeyword("bool", fileScope, symbolTable);
            expressionTypes[node] = result;
            return result;
        }

        expressionTypes[node] = leftType;
        return leftType;
    }

    private static IType ResolveAdditive(
        AdditiveNode node,
        Scope currentScope,
        Scope fileScope,
        IType enclosingType,
        bool isStatic,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        var leftType = ResolveMultiplicative(node.Left, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);

        if (node.Right is not null && !ReferenceEquals(node.Right, node.Left))
        {
            var rightType = ResolveMultiplicative(node.Right, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);

            // String concatenation: if either operand is string, result is string.
            // After Task 50, StringType has been deleted. String is now either
            // the stdlib Standard.String ClassType or the legacy
            // LegacyPrimitiveTypes.String ClassType fallback.
            if (IsStringLikeType(leftType) || IsStringLikeType(rightType))
            {
                // Return whichever string-like type we found (prefer left).
                var stringType = IsStringLikeType(leftType) ? leftType : rightType;
                expressionTypes[node] = stringType;
                return stringType;
            }

            var result = GetCommonNumericType(leftType, rightType);
            expressionTypes[node] = result;
            return result;
        }

        expressionTypes[node] = leftType;
        return leftType;
    }

    private static IType ResolveMultiplicative(
        MultiplicativeNode node,
        Scope currentScope,
        Scope fileScope,
        IType enclosingType,
        bool isStatic,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        var leftType = ResolveUnary(node.Left, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);

        if (node.Right is not null && !ReferenceEquals(node.Right, node.Left))
        {
            var rightType = ResolveUnary(node.Right, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
            var result = GetCommonNumericType(leftType, rightType);
            expressionTypes[node] = result;
            return result;
        }

        expressionTypes[node] = leftType;
        return leftType;
    }

    private static IType ResolveUnary(
        UnaryNode node,
        Scope currentScope,
        Scope fileScope,
        IType enclosingType,
        bool isStatic,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        switch (node)
        {
            case LogicalNotUnaryNode logicalNot:
            {
                ResolveUnary(logicalNot.Operand, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                var result = ResolvePrimitiveByKeyword("bool", fileScope, symbolTable);
                expressionTypes[node] = result;
                return result;
            }
            case NegateUnaryNode negate:
            {
                var operandType = ResolveUnary(negate.Operand, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                expressionTypes[node] = operandType;
                return operandType;
            }
            case CastExpressionUnaryNode castExpr:
                return ResolveCastExpression(castExpr, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
            case PostfixExpressionUnaryNode postfix:
            {
                var type = ResolvePostfix(postfix.Primary, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                expressionTypes[node] = type;
                return type;
            }
            default:
                expressionTypes[node] = UnknownType.Instance;
                return UnknownType.Instance;
        }
    }

    /// <summary>
    /// Resolves a cast expression <c>(TargetType)operand</c> in Pass 2.
    /// Resolves the target type reference to an <see cref="IType"/>, resolves
    /// the operand expression, and records the target type as the expression
    /// type of the cast expression node.
    /// </summary>
    private static IType ResolveCastExpression(
        CastExpressionUnaryNode castExpr,
        Scope currentScope,
        Scope fileScope,
        IType enclosingType,
        bool isStatic,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        // 1. Resolve the target type reference to an IType.
        var targetType = ResolveTypeReference(castExpr.TargetType, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);

        // 2. Resolve the operand expression to get its type.
        ResolveUnary(castExpr.Operand, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);

        // 3. Record the target type as the expression type of the cast expression node.
        expressionTypes[castExpr] = targetType;

        // 4. Return the target type.
        return targetType;
    }

    private static IType ResolvePostfix(
        PostfixNode node,
        Scope currentScope,
        Scope fileScope,
        IType enclosingType,
        bool isStatic,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        var targetType = ResolvePrimary(node.Target, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);

        // Apply each postfix suffix in order.
        foreach (var suffix in node.Suffix)
        {
            targetType = ResolvePostfixSuffix(suffix, targetType, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
        }

        expressionTypes[node] = targetType;
        return targetType;
    }

    private static IType ResolvePostfixSuffix(
        PostfixSuffixNode suffix,
        IType targetType,
        Scope currentScope,
        Scope fileScope,
        IType enclosingType,
        bool isStatic,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        switch (suffix)
        {
            case MethodCallSuffixPostfixSuffixNode methodCall:
            {
                var memberName = methodCall.Name.Lexeme;
                var memberType = LookupMemberType(targetType, memberName, ambientTypeResolver, fileScope, symbolTable, sourceText, diagnostics);

                // Handle type arguments on method invocations (e.g., obj.Method<int>()).
                if (methodCall.TypeArgs is not null)
                {
                    // Look up the method symbol to check its type parameters.
                    var methodSymbol = LookupMemberSymbol(targetType, memberName, ambientTypeResolver, fileScope, symbolTable);
                    if (methodSymbol is Symbol sym && sym.TypeParameters.Count > 0)
                    {
                        // Resolve the type arguments.
                        var resolvedTypeArgs = ResolvePass2TypeArgumentList(methodCall.TypeArgs, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);

                        if (resolvedTypeArgs.Count != sym.TypeParameters.Count)
                        {
                            // Method arity mismatch.
                            diagnostics.Add(MakeDiagnosticWithLocation(
                                DiagnosticId.MethodArityMismatch.Value,
                                DiagnosticSeverity.Error,
                                $"Method '{memberName}' expects {sym.TypeParameters.Count} type argument(s) but received {resolvedTypeArgs.Count}.",
                                methodCall.TypeArgs,
                                sourceText));
                            memberType = UnknownType.Instance;
                        }
                        // Type arguments are valid — substitution will be handled by the type checker.
                    }
                    else if (methodSymbol is not null)
                    {
                        // Method is not generic but type arguments were provided.
                        diagnostics.Add(MakeDiagnosticWithLocation(
                            DiagnosticId.MethodArityMismatch.Value,
                            DiagnosticSeverity.Error,
                            $"Method '{memberName}' expects 0 type argument(s) but received {CountTypeArgs(methodCall.TypeArgs)}.",
                            methodCall.TypeArgs,
                            sourceText));
                    }
                }

                // Resolve arguments.
                if (methodCall.Arguments is not null)
                {
                    ResolveArgumentList(methodCall.Arguments, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                }

                // The result type is the return type of the method (stored as DeclaredType on the member symbol).
                expressionTypes[suffix] = memberType;
                return memberType;
            }
            case MemberAccessSuffixPostfixSuffixNode memberAccess:
            {
                var memberName = memberAccess.Name.Lexeme;

                // Special case: Void.Void → void type instance expression.
                // The identifier "Void" resolves to the void type via
                // TryResolveWellKnownType, but the legacy fallback type has
                // no members. Until the Standard namespace is implicitly
                // imported, this special case bridges the gap.
                // We resolve through the stdlib path to get the same type
                // instance that the `void` keyword resolves to.
                if (targetType.IsVoid() && memberName == "Void")
                {
                    IType voidType;
                    try
                    {
                        var voidGlobalScope = FindGlobalScope(fileScope) ?? fileScope;
                        voidType = ResolveVoidKeyword(voidGlobalScope, symbolTable);
                    }
                    catch (StdlibMissingException)
                    {
                        voidType = targetType;
                    }
                    expressionTypes[suffix] = voidType;
                    return voidType;
                }

                var memberType = LookupMemberType(targetType, memberName, ambientTypeResolver, fileScope, symbolTable, sourceText, diagnostics);
                expressionTypes[suffix] = memberType;
                return memberType;
            }
            case IndexSuffixPostfixSuffixNode indexSuffix:
            {
                // Resolve the index expression.
                ResolveExpression(indexSuffix.Index, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);

                // The result type is the element type of the array.
                if (targetType is ArrayType arrayType)
                {
                    expressionTypes[suffix] = arrayType.ElementType;
                    return arrayType.ElementType;
                }

                expressionTypes[suffix] = UnknownType.Instance;
                return UnknownType.Instance;
            }
            case InvocationSuffixPostfixSuffixNode invocation:
            {
                // Direct invocation (e.g., function call without dot).
                if (invocation.Arguments is not null)
                {
                    ResolveArgumentList(invocation.Arguments, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                }

                // The result type is the return type of the target (if it's a method).
                expressionTypes[suffix] = targetType;
                return targetType;
            }
            default:
                return targetType;
        }
    }

    // ─── Primary Expression Resolution ───────────────────────────────────

    private static IType ResolvePrimary(
        PrimaryNode node,
        Scope currentScope,
        Scope fileScope,
        IType enclosingType,
        bool isStatic,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        IType result;

        switch (node)
        {
            case IntegerLiteralExpressionPrimaryNode intLit:
                result = ResolveIntegerLiteralType(intLit.IntegerLiteral.Lexeme, fileScope, symbolTable);
                break;

            case HexLiteralExpressionPrimaryNode hexLit:
                result = ResolveIntegerLiteralType(hexLit.HexLiteral.Lexeme, fileScope, symbolTable);
                break;

            case BinaryLiteralExpressionPrimaryNode binLit:
                result = ResolveIntegerLiteralType(binLit.BinaryLiteral.Lexeme, fileScope, symbolTable);
                break;

            case FloatLiteralExpressionPrimaryNode floatLit:
                result = ResolveFloatLiteralType(floatLit.FloatLiteral.Lexeme, fileScope, symbolTable);
                break;

            case StringLiteralExpressionPrimaryNode:
                result = ResolvePrimitiveByKeyword("string", fileScope, symbolTable);
                break;

            case InterpolatedStringExpressionPrimaryNode interpolated:
                // Resolve all expression parts within the interpolated string.
                foreach (var part in interpolated.InterpolatedString.Parts)
                {
                    if (part is ExpressionPartInterpolatedPartNode exprPart)
                    {
                        ResolveExpression(exprPart.Expression, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                    }
                }
                result = ResolvePrimitiveByKeyword("string", fileScope, symbolTable);
                break;

            case CharLiteralExpressionPrimaryNode:
                result = ResolvePrimitiveByKeyword("char", fileScope, symbolTable);
                break;

            case TrueLiteralExpressionPrimaryNode:
                result = ResolvePrimitiveByKeyword("bool", fileScope, symbolTable);
                break;

            case FalseLiteralExpressionPrimaryNode:
                result = ResolvePrimitiveByKeyword("bool", fileScope, symbolTable);
                break;

            case ThisExpressionPrimaryNode:
                if (isStatic)
                {
                    diagnostics.Add(MakeDiagnosticWithLocation(
                        "MIST_INVALID_THIS",
                        DiagnosticSeverity.Error,
                        "'this' cannot be used in a static context.",
                        node,
                        sourceText));
                    result = UnknownType.Instance;
                }
                else
                {
                    result = enclosingType;
                }
                break;

            case FieldKeywordExpressionPrimaryNode:
            {
                // Resolve 'field' keyword to the backing field symbol of the enclosing property.
                // Walk the scope chain to find the enclosing property symbol.
                var backingFieldType = ResolveFieldKeyword(currentScope, symbolTable, sourceText, diagnostics);
                result = backingFieldType;
                break;
            }

            case ArrayCreationExpressionPrimaryNode arrayCreation:
            {
                var elementType = ResolveTypeReference(arrayCreation.Type, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);
                ResolveExpression(arrayCreation.Size, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                result = new ArrayType(elementType);
                break;
            }

            case ObjectCreationExpressionPrimaryNode objCreation:
            {
                var typeName = ExtractQualifiedName(objCreation.Type, sourceText);
                var createdType = ResolveNamedType(typeName, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);

                // Handle type arguments on new expressions (e.g., new Box<int>()).
                if (objCreation.TypeArgs is not null)
                {
                    createdType = ResolvePass2TypeArguments(createdType, typeName, objCreation.TypeArgs, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);
                }

                if (objCreation.Arguments is not null)
                {
                    ResolveArgumentList(objCreation.Arguments, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                }
                result = createdType;
                break;
            }

            case ParenthesizedExpressionPrimaryNode paren:
                result = ResolveExpression(paren.Inner, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                break;

            case IdentifierExpressionPrimaryNode identifier:
            {
                var name = identifier.Name.Lexeme;
                var symbol = currentScope.Lookup(name);

                if (symbol is null)
                {
                    // Try to resolve as a type name (for static access like Console.WriteLine).
                    var typeAsSymbol = ResolveIdentifierAsType(name, fileScope, ambientTypeResolver, symbolTable, sourceText);
                    if (typeAsSymbol is not null)
                    {
                        result = typeAsSymbol;
                    }
                    // In a non-static instance context, bare identifiers may refer
                    // to inherited members from the base class chain. Walk the
                    // BaseType chain to find the member, mirroring what
                    // LookupMemberType does for explicit this.member access.
                    else if (!isStatic && enclosingType is ClassType enclosingClass)
                    {
                        IType? inheritedMemberType = null;
                        var ancestor = enclosingClass.BaseType as ClassType;
                        while (ancestor is not null)
                        {
                            foreach (var m in ancestor.MemberScope.Members)
                            {
                                if (m.Name == name)
                                {
                                    inheritedMemberType = m.DeclaredType ?? UnknownType.Instance;
                                    break;
                                }
                            }
                            if (inheritedMemberType is not null)
                                break;
                            ancestor = ancestor.BaseType as ClassType;
                        }

                        if (inheritedMemberType is not null)
                        {
                            result = inheritedMemberType;
                        }
                        else
                        {
                            diagnostics.Add(MakeDiagnosticWithLocation(
                                "MIST_UNDEFINED_IDENTIFIER",
                                DiagnosticSeverity.Error,
                                $"The name '{name}' does not exist in the current context.",
                                identifier,
                                sourceText));
                            result = UnknownType.Instance;
                        }
                    }
                    else
                    {
                        diagnostics.Add(MakeDiagnosticWithLocation(
                            "MIST_UNDEFINED_IDENTIFIER",
                            DiagnosticSeverity.Error,
                            $"The name '{name}' does not exist in the current context.",
                            identifier,
                            sourceText));
                        result = UnknownType.Instance;
                    }
                }
                else
                {
                    result = symbol.DeclaredType ?? UnknownType.Instance;
                }
                break;
            }

            case TypeLiteralExpressionPrimaryNode typeLiteral:
            {
                // Resolve the TypeReference to get the referenced type (for validation).
                var referencedType = ResolveTypeReference(
                    typeLiteral.TypeRef, fileScope, ambientTypeResolver,
                    symbolTable, sourceText, diagnostics);
                // The expression type of a type literal is always the metatype Type.
                try
                {
                    var typeGlobalScope = FindGlobalScope(fileScope) ?? fileScope;
                    result = ResolveTypeKeyword(typeGlobalScope, symbolTable);
                }
                catch (StdlibMissingException)
                {
                    result = LegacyPrimitiveTypes.Type;
                }
                break;
            }

            default:
                result = UnknownType.Instance;
                break;
        }

        expressionTypes[node] = result;
        return result;
    }

    /// <summary>
    /// Tries to resolve an identifier as a type name (for static member access).
    /// Returns the type if found, null otherwise.
    /// </summary>
    private static IType? ResolveIdentifierAsType(
        string name,
        Scope fileScope,
        IAmbientTypeResolver? ambientTypeResolver,
        SymbolTable symbolTable,
        SourceText? sourceText)
    {
        // Check well-known types.
        var wellKnown = TryResolveWellKnownType(name);
        if (wellKnown is not null) return wellKnown;

        // Check scope chain for type symbols (local → file → namespace → global).
        var symbol = fileScope.Lookup(name);
        if (symbol is not null && (symbol.Kind == SymbolKind.Class || symbol.Kind == SymbolKind.Struct || symbol.Kind == SymbolKind.Interface || symbol.Kind == SymbolKind.Trait))
        {
            return symbol.DeclaredType;
        }

        // Check imported namespaces — first user-defined, then ambient.
        var importedNamespaces = CollectImportedNamespaces(fileScope);
        var globalScope = FindGlobalScope(fileScope);

        // Check user-defined namespace scopes.
        foreach (var ns in importedNamespaces)
        {
            var nsScope = globalScope is not null
                ? FindExistingNamespaceScope(globalScope, symbolTable, ns)
                : null;

            if (nsScope is not null)
            {
                foreach (var member in nsScope.Members)
                {
                    if (member.Name == name &&
                        (member.Kind == SymbolKind.Class || member.Kind == SymbolKind.Struct || member.Kind == SymbolKind.Interface || member.Kind == SymbolKind.Trait))
                    {
                        return member.DeclaredType;
                    }
                }
            }
        }

        // Check imported namespaces via ambient resolver.
        if (ambientTypeResolver is not null)
        {
            foreach (var ns in importedNamespaces)
            {
                var resolvedType = ambientTypeResolver.ResolveType(ns, name);
                if (resolvedType is not null)
                {
                    RegisterAmbientTypeInScope(resolvedType, name, ns, fileScope, ambientTypeResolver, symbolTable);
                    return resolvedType;
                }
            }
        }

        return null;
    }

    // ─── Helper Methods ──────────────────────────────────────────────────

    /// <summary>
    /// Looks up a member by name on a given type. Searches the type's member scope
    /// and, for ambient types, queries the ambient resolver.
    /// </summary>
    private static IType LookupMemberType(
        IType targetType,
        string memberName,
        IAmbientTypeResolver? ambientTypeResolver,
        Scope fileScope,
        SymbolTable symbolTable,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        if (targetType is UnknownType)
        {
            return UnknownType.Instance;
        }

        // For ConstructedType, unwrap to the generic definition and look up the member,
        // then substitute type parameters with type arguments.
        IReadOnlyList<TypeParameterType>? constructedTypeParams = null;
        IReadOnlyList<IType>? constructedTypeArgs = null;
        if (targetType is ConstructedType constructedTarget)
        {
            constructedTypeParams = constructedTarget.GenericDefinition switch
            {
                ClassType ct2 => ct2.TypeParameters,
                StructType st2 => st2.TypeParameters,
                InterfaceType it2 => it2.TypeParameters,
                _ => null,
            };
            constructedTypeArgs = constructedTarget.TypeArguments;
            targetType = constructedTarget.GenericDefinition;
        }

        // Get the member scope from the target type.
        IScope? memberScope = targetType switch
        {
            ClassType ct => ct.MemberScope,
            StructType st => st.MemberScope,
            InterfaceType it => it.MemberScope,
            TraitType tt => tt.MemberScope,
            ArrayType => null, // Arrays use special handling.
            _ => null,
        };

        // For string type with no members in its scope (legacy fallback
        // ClassType from LegacyPrimitiveTypes.String), look up members
        // via the stdlib Standard.String type or the ambient resolver.
        // When stdlib IS loaded, the string type is the stdlib ClassType
        // with a populated member scope, so this path only fires for the
        // legacy fallback in tests without stdlib.
        if (IsStringLikeType(targetType) && (memberScope is null || !memberScope.Members.Any(m => m.Name == memberName)))
        {
            var globalScope = FindGlobalScope(fileScope);
            if (globalScope is not null)
            {
                try
                {
                    var stdlibString = LookupQualifiedStdlibType(globalScope, symbolTable, "Standard.String");
                    if (stdlibString is ClassType stdlibStringClass)
                    {
                        foreach (var m in stdlibStringClass.MemberScope.Members)
                        {
                            if (m.Name == memberName)
                                return m.DeclaredType ?? UnknownType.Instance;
                        }
                    }
                }
                catch (StdlibMissingException)
                {
                    // Stdlib not loaded — fall through to ambient resolver fallback below.
                }
            }

            // Legacy fallback: try the ambient resolver (for backward compat
            // with tests that don't load stdlib).
            if (ambientTypeResolver is not null)
            {
                var resolvedStringType = ambientTypeResolver.ResolveType("System", "String");
                if (resolvedStringType is not null)
                {
                    var members = ambientTypeResolver.GetMembers(resolvedStringType);
                    foreach (var member in members)
                    {
                        if (member.Name == memberName)
                            return member.DeclaredType ?? UnknownType.Instance;
                    }
                }
            }
        }

        // For array types, look up Length via the stdlib Standard.Array<T> type.
        // After Task 42, Array is no longer an ambient type — it's a stdlib type.
        if (targetType is ArrayType)
        {
            var globalScope = FindGlobalScope(fileScope);
            if (globalScope is not null)
            {
                try
                {
                    var stdlibArray = LookupQualifiedStdlibType(globalScope, symbolTable, "Standard.Array");
                    if (stdlibArray is ClassType stdlibArrayClass)
                    {
                        foreach (var m in stdlibArrayClass.MemberScope.Members)
                        {
                            if (m.Name == memberName)
                                return m.DeclaredType ?? UnknownType.Instance;
                        }
                    }
                }
                catch (StdlibMissingException)
                {
                    // Stdlib not loaded — fall through to ambient resolver fallback below.
                }
            }

            // Legacy fallback: try the ambient resolver (for backward compat
            // with tests that don't load stdlib).
            if (ambientTypeResolver is not null)
            {
                var arrayBaseType = ambientTypeResolver.ResolveType("System", "Array");
                if (arrayBaseType is not null)
                {
                    var members = ambientTypeResolver.GetMembers(arrayBaseType);
                    foreach (var member in members)
                    {
                        if (member.Name == memberName)
                            return member.DeclaredType ?? UnknownType.Instance;
                    }
                }
            }
        }

        // Search the type's member scope, walking the BaseType chain for class types.
        if (targetType is ClassType classTarget)
        {
            ClassType? current = classTarget;
            while (current is not null)
            {
                // Search only the direct members of this class's scope.
                foreach (var m in current.MemberScope.Members)
                {
                    if (m.Name == memberName)
                    {
                        var memberType = m.DeclaredType ?? UnknownType.Instance;
                        return SubstituteMemberTypeIfNeeded(memberType, constructedTypeParams, constructedTypeArgs);
                    }
                }

                // If the type has ambient members, try the ambient resolver.
                if (ambientTypeResolver is not null)
                {
                    var ambientMembers = ambientTypeResolver.GetMembers(current);
                    foreach (var m in ambientMembers)
                    {
                        if (m.Name == memberName)
                        {
                            var memberType = m.DeclaredType ?? UnknownType.Instance;
                            return SubstituteMemberTypeIfNeeded(memberType, constructedTypeParams, constructedTypeArgs);
                        }
                    }
                }

                current = current.BaseType as ClassType;
            }
        }
        else if (memberScope is not null)
        {
            var member = memberScope.Lookup(memberName);
            if (member is not null)
            {
                var memberType = member.DeclaredType ?? UnknownType.Instance;
                return SubstituteMemberTypeIfNeeded(memberType, constructedTypeParams, constructedTypeArgs);
            }

            // For interface types, walk inherited interfaces transitively.
            if (targetType is InterfaceType interfaceTarget)
            {
                var result = LookupInheritedInterfaceMember(interfaceTarget, memberName);
                if (result is not null)
                {
                    return SubstituteMemberTypeIfNeeded(result, constructedTypeParams, constructedTypeArgs);
                }
            }
        }

        // Use the original target type display name for the diagnostic (including type args).
        var diagnosticTypeName = (constructedTypeParams is not null && constructedTypeArgs is not null)
            ? $"{targetType.DisplayName}<{string.Join(", ", constructedTypeArgs.Select(a => a.DisplayName))}>"
            : targetType.DisplayName;

        diagnostics.Add(new Diagnostic(
            new DiagnosticId("MIST_UNDEFINED_MEMBER"),
            DiagnosticSeverity.Error,
            PipelineStage.SemanticModelConstruction,
            $"'{diagnosticTypeName}' does not contain a definition for '{memberName}'.",
            Location: null,
            CompilationUnitPath: sourceText?.Path,
            AnalyzerName: null));

        return UnknownType.Instance;
    }

    /// <summary>
    /// Substitutes type parameters with type arguments in a member type if a substitution mapping is available.
    /// Used when looking up members on a <see cref="ConstructedType"/>.
    /// </summary>
    private static IType SubstituteMemberTypeIfNeeded(
        IType memberType,
        IReadOnlyList<TypeParameterType>? typeParams,
        IReadOnlyList<IType>? typeArgs)
    {
        if (typeParams is null || typeArgs is null || typeParams.Count == 0)
            return memberType;

        return SubstituteTypeInMember(memberType, typeParams, typeArgs);
    }

    /// <summary>
    /// Substitutes type parameters with corresponding type arguments in a type.
    /// - If the type is a <see cref="TypeParameterType"/> matching one of the type params, returns the corresponding type arg.
    /// - If the type is a <see cref="ConstructedType"/>, recursively substitutes its type arguments.
    /// - Otherwise, returns the type unchanged.
    /// </summary>
    private static IType SubstituteTypeInMember(IType type, IReadOnlyList<TypeParameterType> typeParams, IReadOnlyList<IType> typeArgs)
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
                substitutedArgs[i] = SubstituteTypeInMember(ct.TypeArguments[i], typeParams, typeArgs);
                if (!ReferenceEquals(substitutedArgs[i], ct.TypeArguments[i]))
                    changed = true;
            }
            return changed ? new ConstructedType(ct.GenericDefinition, substitutedArgs) : ct;
        }

        return type;
    }

    /// <summary>
    /// Walks the inherited interfaces of an <see cref="InterfaceType"/> transitively
    /// to find a member by name. Uses a visited set to handle diamond inheritance
    /// and prevent infinite loops.
    /// </summary>
    /// <returns>The member's declared type if found; <c>null</c> otherwise.</returns>
    private static IType? LookupInheritedInterfaceMember(
        InterfaceType interfaceType,
        string memberName)
    {
        var visited = new HashSet<InterfaceType>(ReferenceEqualityComparer.Instance);
        return LookupInheritedInterfaceMemberCore(interfaceType.Interfaces, memberName, visited);
    }

    private static IType? LookupInheritedInterfaceMemberCore(
        IReadOnlyList<IType> interfaces,
        string memberName,
        HashSet<InterfaceType> visited)
    {
        foreach (var ifaceType in interfaces)
        {
            if (ifaceType is not InterfaceType iface)
                continue;

            if (!visited.Add(iface))
                continue;

            var member = iface.MemberScope.Lookup(memberName);
            if (member is not null)
            {
                return member.DeclaredType ?? UnknownType.Instance;
            }

            var inherited = LookupInheritedInterfaceMemberCore(iface.Interfaces, memberName, visited);
            if (inherited is not null)
            {
                return inherited;
            }
        }

        return null;
    }

    /// <summary>
    /// Looks up a member symbol by name on a target type.
    /// Similar to <see cref="LookupMemberType"/> but returns the <see cref="ISymbol"/> instead of the type.
    /// Used for checking method type parameters on generic method invocations.
    /// </summary>
    private static ISymbol? LookupMemberSymbol(
        IType targetType,
        string memberName,
        IAmbientTypeResolver? ambientTypeResolver,
        Scope fileScope,
        SymbolTable symbolTable)
    {
        if (targetType is UnknownType)
            return null;

        // For ConstructedType, look up in the generic definition's member scope.
        if (targetType is ConstructedType ct)
            targetType = ct.GenericDefinition;

        IScope? memberScope = targetType switch
        {
            ClassType classType => classType.MemberScope,
            StructType structType => structType.MemberScope,
            InterfaceType interfaceType => interfaceType.MemberScope,
            _ => null,
        };

        if (targetType is ClassType classTarget)
        {
            ClassType? current = classTarget;
            while (current is not null)
            {
                foreach (var m in current.MemberScope.Members)
                {
                    if (m.Name == memberName)
                        return m;
                }
                current = current.BaseType as ClassType;
            }
        }
        else if (memberScope is not null)
        {
            var member = memberScope.Lookup(memberName);
            if (member is not null)
                return member;
        }

        return null;
    }

    /// <summary>
    /// Counts the number of type arguments in a <see cref="TypeArgumentListNode"/>.
    /// </summary>
    private static int CountTypeArgs(TypeArgumentListNode typeArgList)
    {
        return 1 + typeArgList.TypeReferences.Length;
    }

    /// <summary>
    /// Resolves argument list expressions.
    /// </summary>
    private static void ResolveArgumentList(
        ArgumentListNode argList,
        Scope currentScope,
        Scope fileScope,
        IType enclosingType,
        bool isStatic,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        // The ArgumentListNode has a single Expression property (first argument)
        // and additional arguments via Children().
        ResolveExpression(argList.Expression, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);

        // Walk children to find additional argument expressions.
        foreach (var child in argList.Children())
        {
            if (child is ExpressionNode additionalExpr && !ReferenceEquals(additionalExpr, argList.Expression))
            {
                ResolveExpression(additionalExpr, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
            }
        }
    }

    /// <summary>
    /// Determines the type of an integer literal based on its suffix.
    /// </summary>
    private static IType ResolveIntegerLiteralType(string lexeme, Scope fileScope, SymbolTable symbolTable)
    {
        var upper = lexeme.ToUpperInvariant();

        if (upper.EndsWith("UL") || upper.EndsWith("LU"))
            return ResolvePrimitiveByKeyword("ulong", fileScope, symbolTable);
        if (upper.EndsWith("L"))
            return ResolvePrimitiveByKeyword("long", fileScope, symbolTable);
        if (upper.EndsWith("U"))
            return ResolvePrimitiveByKeyword("uint", fileScope, symbolTable);

        return ResolvePrimitiveByKeyword("int", fileScope, symbolTable);
    }

    /// <summary>
    /// Determines the type of a float literal based on its suffix.
    /// </summary>
    private static IType ResolveFloatLiteralType(string lexeme, Scope fileScope, SymbolTable symbolTable)
    {
        var upper = lexeme.ToUpperInvariant();

        if (upper.EndsWith("F"))
            return ResolvePrimitiveByKeyword("float", fileScope, symbolTable);
        if (upper.EndsWith("M"))
            return ResolvePrimitiveByKeyword("decimal", fileScope, symbolTable);

        return ResolvePrimitiveByKeyword("double", fileScope, symbolTable);
    }

    /// <summary>
    /// Resolves a primitive keyword to its stdlib type when the Standard
    /// Library is loaded, falling back to the legacy
    /// <see cref="LegacyPrimitiveTypes"/> singleton otherwise. This
    /// replaces the deleted <c>PrimitiveType.Int</c> etc. property
    /// accessors that used to consult <c>PrimitiveTypeRegistry</c>.
    /// </summary>
    private static IType ResolvePrimitiveByKeyword(string keyword, Scope fileScope, SymbolTable symbolTable)
    {
        if (!KeywordAliases.Map.TryGetValue(keyword, out var qualifiedName))
        {
            // Not a primitive keyword — return legacy fallback.
            return TryResolveWellKnownType(keyword) ?? UnknownType.Instance;
        }

        try
        {
            var globalScope = FindGlobalScope(fileScope) ?? fileScope;
            return LookupQualifiedStdlibType(globalScope, symbolTable, qualifiedName);
        }
        catch (StdlibMissingException)
        {
            // Stdlib not loaded — return legacy fallback.
            return TryResolveWellKnownType(keyword) ?? UnknownType.Instance;
        }
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="type"/> represents the
    /// Mist <c>string</c> / <c>Standard.String</c> type, regardless of
    /// whether the instance is the legacy <see cref="LegacyPrimitiveTypes.String"/>
    /// fallback or the stdlib <see cref="ClassType"/>.
    /// </summary>
    private static bool IsStringLikeType(IType type)
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
    /// Returns the common numeric type for binary arithmetic operations.
    /// If either operand is UnknownType, returns UnknownType.
    /// </summary>
    private static IType GetCommonNumericType(IType left, IType right)
    {
        if (left is UnknownType || right is UnknownType)
            return UnknownType.Instance;

        // Numeric widening: decimal > double > float > ulong > long > uint > int > smaller types.
        // Use DisplayName-based ranking to handle both legacy (e.g., "int")
        // and stdlib (e.g., "Int32") type instances.
        static int NumericRank(IType t) => t.DisplayName switch
        {
            "sbyte" or "SByte" => 1,
            "byte" or "Byte" => 2,
            "short" or "Int16" => 3,
            "ushort" or "UInt16" => 4,
            "int" or "Int32" => 5,
            "uint" or "UInt32" => 6,
            "long" or "Int64" => 7,
            "ulong" or "UInt64" => 8,
            "float" or "Single" => 9,
            "double" or "Double" => 10,
            "decimal" or "Decimal" => 11,
            _ => -1,
        };

        var leftRank = NumericRank(left);
        var rightRank = NumericRank(right);

        if (leftRank >= 0 && rightRank >= 0)
        {
            return leftRank >= rightRank ? left : right;
        }

        // If not both numeric, return the left type as a fallback.
        return left;
    }

    /// <summary>
    /// Resolves the <c>field</c> keyword to the backing field of the enclosing property.
    /// Walks the symbol table to find a property symbol whose backing field is in the
    /// current scope chain.
    /// </summary>
    private static IType ResolveFieldKeyword(
        Scope currentScope,
        SymbolTable symbolTable,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        // Walk the scope chain to find the type scope that contains a property with a backing field.
        // The 'field' keyword should resolve to the backing field of the enclosing property.
        // We look for property symbols in the type scope (parent of the accessor scope).
        IScope? scope = currentScope;
        while (scope is not null)
        {
            foreach (var member in scope.Members)
            {
                if (member.Kind == SymbolKind.Property && member is Symbol propSymbol && propSymbol.PropertyMetadata?.BackingField is Symbol backingField)
                {
                    return backingField.DeclaredType ?? UnknownType.Instance;
                }
            }
            scope = scope.Parent;
        }

        // 'field' keyword used outside a property accessor or in a computed property.
        // Return UnknownType — the type checker will report the appropriate diagnostic.
        return UnknownType.Instance;
    }

    /// <summary>
    /// Checks for duplicate local variable declarations in the current scope.
    /// </summary>
    private static void CheckDuplicateLocal(string name, Scope scope, SourceText? sourceText, DiagnosticBag diagnostics)
    {
        foreach (var member in scope.Members)
        {
            if (member.Name == name)
            {
                diagnostics.Add(new Diagnostic(
                    new DiagnosticId("MIST_DUPLICATE_DECLARATION"),
                    DiagnosticSeverity.Error,
                    PipelineStage.SemanticModelConstruction,
                    $"A local variable named '{name}' is already declared in this scope.",
                    Location: null,
                    CompilationUnitPath: sourceText?.Path,
                    AnalyzerName: null));
                return;
            }
        }
    }

    /// <summary>
    /// Extracts the full qualified name from a QualifiedIdentifierNode.
    /// </summary>
    private static string ExtractQualifiedName(QualifiedIdentifierNode qualifiedId, SourceText? sourceText)
    {
        if (sourceText is not null)
        {
            var span = qualifiedId.Span;
            if (span.Length > 0 && span.Start + span.Length <= sourceText.Contents.Length)
            {
                return sourceText.Contents.Substring(span.Start, span.Length);
            }
        }

        return qualifiedId.IDENTIFIER.Lexeme;
    }

    // ─── Shared Utilities ────────────────────────────────────────────────

    /// <summary>
    /// Extracts all type names from a <see cref="BaseListNode"/> as a string array.
    /// The first entry comes from <c>First</c>, and additional entries from <c>BaseTypeRefs</c>.
    /// </summary>
    private static string[] ExtractBaseListNames(BaseListNode baseList)
    {
        var names = new List<string>();
        var firstName = baseList.First.Name.Lexeme;
        if (!string.IsNullOrEmpty(firstName))
        {
            names.Add(firstName);
        }
        foreach (var rest in baseList.BaseTypeRefs)
        {
            var restName = rest.Name.Lexeme;
            if (!string.IsNullOrEmpty(restName))
            {
                names.Add(restName);
            }
        }
        return names.ToArray();
    }

    private static string ExtractNamespaceName(
        NamespaceDeclarationNode namespaceNode,
        SourceText? sourceText)
    {
        QualifiedIdentifierNode? qualifiedId = namespaceNode switch
        {
            FileScopedNamespaceNamespaceDeclarationNode fileScopedNs => fileScopedNs.Name,
            BlockNamespaceNamespaceDeclarationNode blockNs => blockNs.Name,
            _ => null
        };

        if (qualifiedId is null)
        {
            return "<unknown>";
        }

        if (sourceText is not null)
        {
            var span = qualifiedId.Span;
            if (span.Length > 0 && span.Start + span.Length <= sourceText.Contents.Length)
            {
                return sourceText.Contents.Substring(span.Start, span.Length);
            }
        }

        return qualifiedId.IDENTIFIER.Lexeme;
    }

    private static ImmutableArray<TypeDeclarationNode> GetNamespaceBodyTypes(
        NamespaceDeclarationNode namespaceNode)
    {
        return namespaceNode switch
        {
            FileScopedNamespaceNamespaceDeclarationNode fileScopedNs => fileScopedNs.Body,
            BlockNamespaceNamespaceDeclarationNode blockNs => blockNs.Body,
            _ => ImmutableArray<TypeDeclarationNode>.Empty
        };
    }

    private static Diagnostic MakeDiagnosticWithLocation(
        string id,
        DiagnosticSeverity severity,
        string message,
        Storm.Runtime.SyntaxNode node,
        SourceText? sourceText)
    {
        LinePositionSpan? location = null;
        string? compilationUnitPath = sourceText?.Path;

        if (sourceText is not null && node.Span.Length > 0)
        {
            var mistSpan = new SourceSpan(node.Span.Start, node.Span.Length);
            location = sourceText.ToLinePositionSpan(mistSpan);
        }

        return new Diagnostic(
            new DiagnosticId(id),
            severity,
            PipelineStage.SemanticModelConstruction,
            message,
            Location: location,
            CompilationUnitPath: compilationUnitPath,
            AnalyzerName: null);
    }

    private static Common.Text.SourceSpan ToMistSourceSpan(Storm.Runtime.SourceSpan span)
    {
        return new Common.Text.SourceSpan(span.Start, span.Length);
    }
}
