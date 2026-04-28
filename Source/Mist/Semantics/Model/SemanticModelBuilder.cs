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

        // Pass 1c: Detect circular inheritance chains.
        // After all base types are resolved, walk each class's BaseType chain
        // to detect cycles (including self-inheritance). Cycles are broken by
        // setting BaseType = null on the class that closes the cycle.
        Pass1_DetectCycles(symbolTable, asts, compilation, diagnostics);

        // Pass 1d: Check for member redeclarations in derived classes.
        // After base types are resolved and cycles are broken, walk each
        // class's ancestor chain to ensure no member name is redeclared.
        Pass1_CheckMemberRedeclarations(symbolTable, asts, compilation, diagnostics);

        // Pass 1e: Check that base class constructor requirements are satisfied.
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
                RegisterClassDeclaration(classTypeNode.ClassDeclaration, parentScope, symbolTable, namespaceName, diagnostics);
                break;
            case StructTypeTypeDeclarationNode structTypeNode:
                RegisterStructDeclaration(structTypeNode.StructDeclaration, parentScope, symbolTable, namespaceName, diagnostics);
                break;
        }
    }

    private static void RegisterClassDeclaration(
        ClassDeclarationNode classDecl,
        Scope parentScope,
        SymbolTable symbolTable,
        string? namespaceName,
        DiagnosticBag diagnostics)
    {
        var className = classDecl.Name.Lexeme;
        var typeScope = new Scope(parent: parentScope);
        var classType = new ClassType(className, typeScope, namespaceName);

        var classSpan = ToMistSourceSpan(classDecl.Name.Span);
        var classSymbol = new Symbol(className, SymbolKind.Class, SymbolOrigin.UserCode, parentScope, classType, classSpan);

        parentScope.AddMember(classSymbol);
        symbolTable.Register(classSymbol, classDecl);

        foreach (var memberDecl in classDecl.Members)
        {
            RegisterMemberDeclaration(memberDecl, typeScope, symbolTable, diagnostics);
        }
    }

    private static void RegisterStructDeclaration(
        StructDeclarationNode structDecl,
        Scope parentScope,
        SymbolTable symbolTable,
        string? namespaceName,
        DiagnosticBag diagnostics)
    {
        var structName = structDecl.Name.Lexeme;
        var typeScope = new Scope(parent: parentScope);
        var structType = new StructType(structName, typeScope, namespaceName);

        var structSpan = ToMistSourceSpan(structDecl.Name.Span);
        var structSymbol = new Symbol(structName, SymbolKind.Struct, SymbolOrigin.UserCode, parentScope, structType, structSpan);

        parentScope.AddMember(structSymbol);
        symbolTable.Register(structSymbol, structDecl);

        foreach (var memberDecl in structDecl.Members)
        {
            RegisterMemberDeclaration(memberDecl, typeScope, symbolTable, diagnostics);
        }
    }

    private static void RegisterMemberDeclaration(
        MemberDeclarationNode memberDecl,
        Scope typeScope,
        SymbolTable symbolTable,
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
                RegisterMethodDeclaration(methodMember.Method, typeScope, symbolTable);
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

    private static void RegisterMethodDeclaration(MethodDeclarationNode methodDecl, Scope typeScope, SymbolTable symbolTable)
    {
        var methodName = methodDecl.Name.Lexeme;
        var methodSpan = ToMistSourceSpan(methodDecl.Name.Span);
        var methodSymbol = new Symbol(methodName, SymbolKind.Method, SymbolOrigin.UserCode, typeScope, declaredType: null, methodSpan);
        typeScope.AddMember(methodSymbol);
        symbolTable.Register(methodSymbol, methodDecl);
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
            if (classDecl.BaseClass is null)
            {
                continue;
            }

            var baseClassName = classDecl.BaseClassName.Lexeme;
            var parentScope = symbol.ContainingScope;

            // Look up the base class name in the parent scope.
            var resolvedSymbol = parentScope.Lookup(baseClassName);

            if (resolvedSymbol is null)
            {
                // Determine the source file path for the diagnostic.
                string? compilationUnitPath = FindCompilationUnitPath(classDecl, asts, compilation);

                diagnostics.Add(new Diagnostic(
                    new DiagnosticId("MIST_UNDEFINED_BASE_TYPE"),
                    DiagnosticSeverity.Error,
                    PipelineStage.SemanticModelConstruction,
                    $"The base type '{baseClassName}' could not be found.",
                    Location: null,
                    CompilationUnitPath: compilationUnitPath,
                    AnalyzerName: null));
            }
            else if (resolvedSymbol.DeclaredType is StructType)
            {
                string? compilationUnitPath = FindCompilationUnitPath(classDecl, asts, compilation);

                diagnostics.Add(new Diagnostic(
                    new DiagnosticId("MIST_CANNOT_INHERIT_STRUCT"),
                    DiagnosticSeverity.Error,
                    PipelineStage.SemanticModelConstruction,
                    $"Class '{classType.DisplayName}' cannot inherit from struct '{baseClassName}'.",
                    Location: null,
                    CompilationUnitPath: compilationUnitPath,
                    AnalyzerName: null));
            }
            else if (resolvedSymbol.DeclaredType is ClassType resolvedClassType)
            {
                classType.BaseType = resolvedClassType;
            }
        }
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
    // Pass 1c — Detect circular inheritance chains
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
                    string? compilationUnitPath = FindCompilationUnitPathForClass(closingClass, symbolTable, asts, compilation);

                    diagnostics.Add(new Diagnostic(
                        new DiagnosticId("MIST_CIRCULAR_INHERITANCE"),
                        DiagnosticSeverity.Error,
                        PipelineStage.SemanticModelConstruction,
                        $"Circular inheritance detected: '{chainStr}'.",
                        Location: null,
                        CompilationUnitPath: compilationUnitPath,
                        AnalyzerName: null));

                    // Break the cycle by nulling the BaseType on the closing class.
                    closingClass.BaseType = null;
                    break;
                }

                chain.Add(current);
                current = current.BaseType;
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

    // ═══════════════════════════════════════════════════════════════════════
    // Pass 1d — Check for member redeclarations in derived classes
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// After base types are resolved and cycles are broken, walk each class's
    /// ancestor chain to enforce the additive-only constraint: a derived class
    /// cannot redeclare a field or method that already exists in any ancestor.
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
                if (member.Kind != SymbolKind.Field && member.Kind != SymbolKind.Method)
                {
                    continue;
                }

                // Walk the ancestor chain looking for a member with the same name.
                var ancestor = classType.BaseType;
                while (ancestor is not null)
                {
                    foreach (var ancestorMember in ancestor.MemberScope.Members)
                    {
                        if ((ancestorMember.Kind == SymbolKind.Field || ancestorMember.Kind == SymbolKind.Method)
                            && ancestorMember.Name == member.Name)
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

                            // Stop walking ancestors for this member once we find the first conflict.
                            goto nextMember;
                        }
                    }

                    ancestor = ancestor.BaseType;
                }

                nextMember:;
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Pass 1e — Check base constructor requirements are satisfied
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

            // Find explicit constructors on the base class.
            var baseConstructors = baseType.MemberScope.Members
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
                diagnostics.Add(new Diagnostic(
                    new DiagnosticId("MIST_UNDEFINED_NAMESPACE"),
                    DiagnosticSeverity.Error,
                    PipelineStage.SemanticModelConstruction,
                    $"The namespace '{usingName}' could not be found.",
                    Location: null,
                    CompilationUnitPath: sourceText?.Path,
                    AnalyzerName: null));
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
            if (member.Kind == SymbolKind.Class || member.Kind == SymbolKind.Struct)
            {
                // Top-level types (no namespace) — skip.
                continue;
            }
        }

        // Walk all registered symbols to find a type in this namespace.
        foreach (var symbol in symbolTable.AllSymbols)
        {
            if ((symbol.Kind == SymbolKind.Class || symbol.Kind == SymbolKind.Struct)
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

        foreach (var memberDecl in structDecl.Members)
        {
            ResolveMemberDeclaration(memberDecl, typeScope, fileScope, structType, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
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
                ResolveFieldDeclaration(fieldMember.Field, typeScope, fileScope, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                break;
            case ConstructorMemberMemberDeclarationNode ctorMember:
                ResolveConstructorDeclaration(ctorMember.Constructor, typeScope, fileScope, enclosingType, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                break;
            case MethodMemberMemberDeclarationNode methodMember:
                ResolveMethodDeclaration(methodMember.Method, typeScope, fileScope, enclosingType, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
                break;
        }
    }

    private static void ResolveFieldDeclaration(
        FieldDeclarationNode fieldDecl,
        Scope typeScope,
        Scope fileScope,
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

    private static void RegisterParameters(
        ParameterListNode paramList,
        Scope methodScope,
        Scope fileScope,
        SymbolTable symbolTable,
        IAmbientTypeResolver? ambientTypeResolver,
        SourceText? sourceText,
        DiagnosticBag diagnostics)
    {
        // The ParameterListNode has a single Parameter property (first parameter)
        // and the grammar uses (COMMA Parameter)* for additional parameters.
        // The Storm generator flattens this into a single Parameter property.
        // We need to walk the children to find all ParameterNode instances.
        RegisterSingleParameter(paramList.Parameter, methodScope, fileScope, symbolTable, ambientTypeResolver, sourceText, diagnostics);

        // Walk children to find additional parameters (from the repetition).
        foreach (var child in paramList.Children())
        {
            if (child is ParameterNode additionalParam && !ReferenceEquals(additionalParam, paramList.Parameter))
            {
                RegisterSingleParameter(additionalParam, methodScope, fileScope, symbolTable, ambientTypeResolver, sourceText, diagnostics);
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
            diagnostics.Add(new Diagnostic(
                new DiagnosticId("MIST_DUPLICATE_DECLARATION"),
                DiagnosticSeverity.Error,
                PipelineStage.SemanticModelConstruction,
                $"A parameter named '{paramName}' is already declared in this scope.",
                Location: null,
                CompilationUnitPath: sourceText?.Path,
                AnalyzerName: null));
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
                baseType = ResolveNamedType(typeName, fileScope, ambientTypeResolver, symbolTable, sourceText, diagnostics);
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
        if (symbol is not null && (symbol.Kind == SymbolKind.Class || symbol.Kind == SymbolKind.Struct))
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
            _ => null,
        };
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
        if (existing is not null && (existing.Kind == SymbolKind.Class || existing.Kind == SymbolKind.Struct))
        {
            return; // Already registered.
        }

        // Create a type symbol with SymbolOrigin.Backend.
        var kind = type is ClassType ? SymbolKind.Class : SymbolKind.Struct;
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
                    diagnostics.Add(new Diagnostic(
                        new DiagnosticId("MIST_INVALID_THIS"),
                        DiagnosticSeverity.Error,
                        PipelineStage.SemanticModelConstruction,
                        "'this' cannot be used in a static context.",
                        Location: null,
                        CompilationUnitPath: sourceText?.Path,
                        AnalyzerName: null));
                    result = UnknownType.Instance;
                }
                else
                {
                    result = enclosingType;
                }
                break;

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
                    else
                    {
                        diagnostics.Add(new Diagnostic(
                            new DiagnosticId("MIST_UNDEFINED_IDENTIFIER"),
                            DiagnosticSeverity.Error,
                            PipelineStage.SemanticModelConstruction,
                            $"The name '{name}' does not exist in the current context.",
                            Location: null,
                            CompilationUnitPath: sourceText?.Path,
                            AnalyzerName: null));
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
        if (symbol is not null && (symbol.Kind == SymbolKind.Class || symbol.Kind == SymbolKind.Struct))
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

        // Get the member scope from the target type.
        IScope? memberScope = targetType switch
        {
            ClassType ct => ct.MemberScope,
            StructType st => st.MemberScope,
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
                        return m.DeclaredType ?? UnknownType.Instance;
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
                            return m.DeclaredType ?? UnknownType.Instance;
                        }
                    }
                }

                current = current.BaseType;
            }
        }
        else if (memberScope is not null)
        {
            var member = memberScope.Lookup(memberName);
            if (member is not null)
            {
                return member.DeclaredType ?? UnknownType.Instance;
            }
        }

        diagnostics.Add(new Diagnostic(
            new DiagnosticId("MIST_UNDEFINED_MEMBER"),
            DiagnosticSeverity.Error,
            PipelineStage.SemanticModelConstruction,
            $"'{targetType.DisplayName}' does not contain a definition for '{memberName}'.",
            Location: null,
            CompilationUnitPath: sourceText?.Path,
            AnalyzerName: null));

        return UnknownType.Instance;
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

    private static Common.Text.SourceSpan ToMistSourceSpan(Storm.Runtime.SourceSpan span)
    {
        return new Common.Text.SourceSpan(span.Start, span.Length);
    }
}
