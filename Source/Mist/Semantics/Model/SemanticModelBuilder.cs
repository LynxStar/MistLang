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

    public ISemanticModel Build(
        MistCompilation compilation,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        IAmbientTypeResolver? ambientTypeResolver,
        DiagnosticBag diagnostics)
    {
        var globalScope = new Scope(parent: null);
        var symbolTable = new SymbolTable();

        // Pass 1: Register user-defined types.
        // Walk each AST to discover namespaces, types, and members.
        for (var i = 0; i < asts.Length; i++)
        {
            var ast = asts[i];
            var sourceText = i < compilation.Units.Length ? compilation.Units[i].Source : null;

            try
            {
                Pass1_RegisterTypes(ast, globalScope, symbolTable, sourceText, diagnostics);
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
            expressionTypes);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Pass 1 — Register user-defined types
    // ═══════════════════════════════════════════════════════════════════════

    private static void Pass1_RegisterTypes(
        Storm.Runtime.SyntaxNode ast,
        Scope globalScope,
        SymbolTable symbolTable,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        if (ast is not CompilationUnitNode compilationUnit)
        {
            return;
        }

        Scope typeContainerScope = globalScope;
        string? namespaceName = null;

        if (compilationUnit.Namespace is not null)
        {
            namespaceName = ExtractNamespaceName(compilationUnit.Namespace, sourceText);

            // Reuse an existing namespace scope if this namespace was already
            // registered by a previous compilation unit (multiple files can
            // contribute types to the same namespace).
            Scope? namespaceScope = FindExistingNamespaceScope(globalScope, symbolTable, namespaceName);

            if (namespaceScope is null)
            {
                namespaceScope = new Scope(parent: globalScope);

                var namespaceSpan = ToMistSourceSpan(compilationUnit.Namespace.Span);
                var namespaceSymbol = new Symbol(
                    namespaceName,
                    SymbolKind.Namespace,
                    SymbolOrigin.UserCode,
                    globalScope,
                    declaredType: null,
                    namespaceSpan);

                globalScope.AddMember(namespaceSymbol);
                symbolTable.Register(namespaceSymbol, compilationUnit.Namespace);
            }

            typeContainerScope = namespaceScope;

            var namespaceTypes = GetNamespaceBodyTypes(compilationUnit.Namespace);
            foreach (var typeDecl in namespaceTypes)
            {
                RegisterTypeDeclaration(typeDecl, typeContainerScope, symbolTable, namespaceName, sourceText, diagnostics);
            }
        }

        foreach (var typeDecl in compilationUnit.Types)
        {
            RegisterTypeDeclaration(typeDecl, typeContainerScope, symbolTable, namespaceName, sourceText, diagnostics);
        }
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
        var classType = new ClassType(className, typeScope, namespaceName);

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
        var structType = new StructType(structName, typeScope, namespaceName);

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
            case PropertyMemberMemberDeclarationNode propertyMember:
                RegisterPropertyDeclaration(propertyMember.Property, typeScope, symbolTable);
                break;
            case IndexerMemberMemberDeclarationNode indexerMember:
                RegisterIndexerDeclaration(indexerMember.Indexer, typeScope, symbolTable);
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
        var ctorSymbol = new Symbol(ctorName, SymbolKind.Constructor, SymbolOrigin.UserCode, typeScope, declaredType: null, ctorSpan);
        typeScope.AddMember(ctorSymbol);
        symbolTable.Register(ctorSymbol, ctorDecl);
    }

    private static void RegisterMethodDeclaration(MethodDeclarationNode methodDecl, Scope typeScope, SymbolTable symbolTable, SourceText? sourceText, DiagnosticBag diagnostics)
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
                return VoidType.Instance;

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
        foreach (var typeDecl in cu.Types)
        {
            if (typeDecl is ClassTypeTypeDeclarationNode ct && ReferenceEquals(ct.ClassDeclaration, classDecl))
            {
                return true;
            }
        }

        if (cu.Namespace is not null)
        {
            var namespaceTypes = GetNamespaceBodyTypes(cu.Namespace);
            foreach (var typeDecl in namespaceTypes)
            {
                if (typeDecl is ClassTypeTypeDeclarationNode ct && ReferenceEquals(ct.ClassDeclaration, classDecl))
                {
                    return true;
                }
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
        foreach (var typeDecl in cu.Types)
        {
            if (MatchesTypeDeclaration(typeDecl, declaringNode))
                return true;
        }

        if (cu.Namespace is not null)
        {
            var namespaceTypes = GetNamespaceBodyTypes(cu.Namespace);
            foreach (var typeDecl in namespaceTypes)
            {
                if (MatchesTypeDeclaration(typeDecl, declaringNode))
                    return true;
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
            if ((symbol.Kind == SymbolKind.Class || symbol.Kind == SymbolKind.Struct || symbol.Kind == SymbolKind.Interface)
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
            if ((symbol.Kind == SymbolKind.Class || symbol.Kind == SymbolKind.Struct || symbol.Kind == SymbolKind.Interface)
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
            if ((symbol.Kind == SymbolKind.Class || symbol.Kind == SymbolKind.Struct || symbol.Kind == SymbolKind.Interface)
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

        // Determine the namespace scope and file-level scope for using declarations.
        // The file scope is where using imports are recorded.
        Scope typeContainerScope = globalScope;
        string? namespaceName = null;

        if (compilationUnit.Namespace is not null)
        {
            namespaceName = ExtractNamespaceName(compilationUnit.Namespace, sourceText);

            // Find the namespace scope created in Pass 1.
            // The type container scope is the namespace scope (child of global).
            // We need to find it by looking at the types registered in Pass 1.
            typeContainerScope = FindNamespaceScope(globalScope, symbolTable, compilationUnit.Namespace);
        }

        // Create a file-level scope to hold using imports. This scope sits between
        // the namespace/global scope and the type scopes.
        var fileScope = new Scope(parent: typeContainerScope);

        // Process using declarations — verify namespace existence.
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

        // Resolve types in namespace body type declarations.
        if (compilationUnit.Namespace is not null)
        {
            var namespaceTypes = GetNamespaceBodyTypes(compilationUnit.Namespace);
            foreach (var typeDecl in namespaceTypes)
            {
                ResolveTypeDeclaration(typeDecl, fileScope, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
            }
        }

        // Resolve types in top-level type declarations.
        foreach (var typeDecl in compilationUnit.Types)
        {
            ResolveTypeDeclaration(typeDecl, fileScope, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
        }
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
            if (member.Kind == SymbolKind.Class || member.Kind == SymbolKind.Struct || member.Kind == SymbolKind.Interface)
            {
                // Top-level types (no namespace) — skip.
                continue;
            }
        }

        // Walk all registered symbols to find a type in this namespace.
        foreach (var symbol in symbolTable.AllSymbols)
        {
            if ((symbol.Kind == SymbolKind.Class || symbol.Kind == SymbolKind.Struct || symbol.Kind == SymbolKind.Interface)
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

        // Resolve the body.
        ResolveBlock(ctorDecl.Body, methodScope, fileScope, enclosingType, /* isStatic */ false, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
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

        // Resolve the body.
        ResolveBlock(methodDecl.Body, methodScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
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
                baseType = ResolvePrimitiveType(primitiveRef.PrimitiveType);
                if (primitiveRef.ArrayRank.HasValue)
                {
                    return new ArrayType(baseType);
                }
                return baseType;

            case VoidTypeRefTypeReferenceNode:
                return VoidType.Instance;

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
    /// Maps a PrimitiveTypeNode to its well-known PrimitiveType or StringType.
    /// </summary>
    private static IType ResolvePrimitiveType(PrimitiveTypeNode node)
    {
        return node switch
        {
            IntTypePrimitiveTypeNode => PrimitiveType.Int,
            UintTypePrimitiveTypeNode => PrimitiveType.Uint,
            LongTypePrimitiveTypeNode => PrimitiveType.Long,
            UlongTypePrimitiveTypeNode => PrimitiveType.Ulong,
            ShortTypePrimitiveTypeNode => PrimitiveType.Short,
            UshortTypePrimitiveTypeNode => PrimitiveType.Ushort,
            ByteTypePrimitiveTypeNode => PrimitiveType.Byte,
            SbyteTypePrimitiveTypeNode => PrimitiveType.Sbyte,
            FloatTypePrimitiveTypeNode => PrimitiveType.Float,
            DoubleTypePrimitiveTypeNode => PrimitiveType.Double,
            DecimalTypePrimitiveTypeNode => PrimitiveType.Decimal,
            BoolTypePrimitiveTypeNode => PrimitiveType.Bool,
            CharTypePrimitiveTypeNode => PrimitiveType.Char,
            StringTypePrimitiveTypeNode => StringType.Instance,
            _ => UnknownType.Instance,
        };
    }

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

        // 2. Search current scope → parent scopes.
        var symbol = fileScope.Lookup(typeName);
        if (symbol is not null && (symbol.Kind == SymbolKind.Class || symbol.Kind == SymbolKind.Struct || symbol.Kind == SymbolKind.Interface))
        {
            return symbol.DeclaredType ?? UnknownType.Instance;
        }

        // 3. Search imported namespaces from using declarations.
        if (ambientTypeResolver is not null)
        {
            // Collect imported namespaces from the file scope and its ancestors.
            var importedNamespaces = CollectImportedNamespaces(fileScope);

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
                if (symbol is null || (symbol.Kind != SymbolKind.Class && symbol.Kind != SymbolKind.Struct && symbol.Kind != SymbolKind.Interface))
                {
                    // Check ambient resolver.
                    IType? ambientResolved = null;
                    if (ambientTypeResolver is not null)
                    {
                        var importedNamespaces = CollectImportedNamespaces(fileScope);
                        foreach (var ns in importedNamespaces)
                        {
                            ambientResolved = ambientTypeResolver.ResolveType(ns, argName);
                            if (ambientResolved is not null)
                                break;
                        }
                    }

                    if (ambientResolved is null)
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
            "int" => PrimitiveType.Int,
            "uint" => PrimitiveType.Uint,
            "long" => PrimitiveType.Long,
            "ulong" => PrimitiveType.Ulong,
            "short" => PrimitiveType.Short,
            "ushort" => PrimitiveType.Ushort,
            "byte" => PrimitiveType.Byte,
            "sbyte" => PrimitiveType.Sbyte,
            "float" => PrimitiveType.Float,
            "double" => PrimitiveType.Double,
            "decimal" => PrimitiveType.Decimal,
            "bool" => PrimitiveType.Bool,
            "char" => PrimitiveType.Char,
            "string" => StringType.Instance,
            "void" => VoidType.Instance,
            "Void" => VoidType.Instance,
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
        if (existing is not null && (existing.Kind == SymbolKind.Class || existing.Kind == SymbolKind.Struct || existing.Kind == SymbolKind.Interface))
        {
            return; // Already registered.
        }

        // Create a type symbol with SymbolOrigin.Backend.
        var kind = type is ClassType ? SymbolKind.Class : type is InterfaceType ? SymbolKind.Interface : SymbolKind.Struct;
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
            var result = PrimitiveType.Bool;
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
            var result = PrimitiveType.Bool;
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
            var result = PrimitiveType.Bool;
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
            var result = PrimitiveType.Bool;
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
            if (leftType is StringType || rightType is StringType)
            {
                expressionTypes[node] = StringType.Instance;
                return StringType.Instance;
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
                var result = PrimitiveType.Bool;
                expressionTypes[node] = result;
                return result;
            }
            case NegateUnaryNode negate:
            {
                var operandType = ResolveUnary(negate.Operand, currentScope, fileScope, enclosingType, isStatic, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                expressionTypes[node] = operandType;
                return operandType;
            }
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

                // Special case: Void.Void → VoidType instance expression
                if (targetType is VoidType && memberName == "Void")
                {
                    expressionTypes[suffix] = VoidType.Instance;
                    return VoidType.Instance;
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
                result = ResolveIntegerLiteralType(intLit.IntegerLiteral.Lexeme);
                break;

            case HexLiteralExpressionPrimaryNode hexLit:
                result = ResolveIntegerLiteralType(hexLit.HexLiteral.Lexeme);
                break;

            case FloatLiteralExpressionPrimaryNode floatLit:
                result = ResolveFloatLiteralType(floatLit.FloatLiteral.Lexeme);
                break;

            case StringLiteralExpressionPrimaryNode:
                result = StringType.Instance;
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
                result = StringType.Instance;
                break;

            case CharLiteralExpressionPrimaryNode:
                result = PrimitiveType.Char;
                break;

            case TrueLiteralExpressionPrimaryNode:
                result = PrimitiveType.Bool;
                break;

            case FalseLiteralExpressionPrimaryNode:
                result = PrimitiveType.Bool;
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
                // The expression type of a type literal is always TypeType.
                result = TypeType.Instance;
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

        // Check scope chain for type symbols.
        var symbol = fileScope.Lookup(name);
        if (symbol is not null && (symbol.Kind == SymbolKind.Class || symbol.Kind == SymbolKind.Struct || symbol.Kind == SymbolKind.Interface))
        {
            return symbol.DeclaredType;
        }

        // Check imported namespaces via ambient resolver.
        if (ambientTypeResolver is not null)
        {
            var importedNamespaces = CollectImportedNamespaces(fileScope);
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
            ArrayType => null, // Arrays use special handling.
            StringType => null, // String uses ambient type resolution.
            _ => null,
        };

        // For string type, look up members via the ambient resolver.
        if (targetType is StringType && ambientTypeResolver is not null)
        {
            var stringType = ambientTypeResolver.ResolveType("System", "String");
            if (stringType is not null)
            {
                var members = ambientTypeResolver.GetMembers(stringType);
                foreach (var member in members)
                {
                    if (member.Name == memberName)
                    {
                        return member.DeclaredType ?? UnknownType.Instance;
                    }
                }
            }
        }

        // For array types, look up Length via the ambient resolver.
        if (targetType is ArrayType && ambientTypeResolver is not null)
        {
            var arrayBaseType = ambientTypeResolver.ResolveType("System", "Array");
            if (arrayBaseType is not null)
            {
                var members = ambientTypeResolver.GetMembers(arrayBaseType);
                foreach (var member in members)
                {
                    if (member.Name == memberName)
                    {
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
    private static IType ResolveIntegerLiteralType(string lexeme)
    {
        var upper = lexeme.ToUpperInvariant();

        if (upper.EndsWith("UL") || upper.EndsWith("LU"))
            return PrimitiveType.Ulong;
        if (upper.EndsWith("L"))
            return PrimitiveType.Long;
        if (upper.EndsWith("U"))
            return PrimitiveType.Uint;

        return PrimitiveType.Int;
    }

    /// <summary>
    /// Determines the type of a float literal based on its suffix.
    /// </summary>
    private static IType ResolveFloatLiteralType(string lexeme)
    {
        var upper = lexeme.ToUpperInvariant();

        if (upper.EndsWith("F"))
            return PrimitiveType.Float;
        if (upper.EndsWith("M"))
            return PrimitiveType.Decimal;

        return PrimitiveType.Double;
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
        var numericRank = new Dictionary<IType, int>(ReferenceEqualityComparer.Instance)
        {
            [PrimitiveType.Sbyte] = 1,
            [PrimitiveType.Byte] = 2,
            [PrimitiveType.Short] = 3,
            [PrimitiveType.Ushort] = 4,
            [PrimitiveType.Int] = 5,
            [PrimitiveType.Uint] = 6,
            [PrimitiveType.Long] = 7,
            [PrimitiveType.Ulong] = 8,
            [PrimitiveType.Float] = 9,
            [PrimitiveType.Double] = 10,
            [PrimitiveType.Decimal] = 11,
        };

        if (numericRank.TryGetValue(left, out var leftRank) && numericRank.TryGetValue(right, out var rightRank))
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
