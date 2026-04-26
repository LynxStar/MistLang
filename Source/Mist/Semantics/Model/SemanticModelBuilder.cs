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
            var namespaceScope = new Scope(parent: globalScope);

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
        if (ctorDecl.Chain is not null && ctorDecl.Chain.Arguments is not null)
        {
            ResolveArgumentList(ctorDecl.Chain.Arguments, methodScope, fileScope, enclosingType, /* isStatic */ false, symbolTable, expressionTypes, ambientTypeResolver, sourceText, diagnostics);
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

        // Search the type's member scope.
        if (memberScope is not null)
        {
            var member = memberScope.Lookup(memberName);
            if (member is not null)
            {
                return member.DeclaredType ?? UnknownType.Instance;
            }

            // If the type has ambient members, try the ambient resolver.
            if (ambientTypeResolver is not null && targetType is ClassType ct2)
            {
                var members = ambientTypeResolver.GetMembers(targetType);
                foreach (var m in members)
                {
                    if (m.Name == memberName)
                    {
                        return m.DeclaredType ?? UnknownType.Instance;
                    }
                }
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
