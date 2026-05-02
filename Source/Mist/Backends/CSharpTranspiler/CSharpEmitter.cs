using System.Collections.Immutable;
using System.Text;
using Mist.Backends.CSharpTranspiler.Intrinsics;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Backends.CSharpTranspiler;

/// <summary>
/// Walks the ASTs (via the semantic model) and emits C# source files into the output directory.
/// Only symbols with SymbolOrigin.UserCode are emitted; Backend symbols are skipped
/// but their namespaces generate using directives.
/// </summary>
public sealed class CSharpEmitter
{
    public static void Emit(ISemanticModel model, string outputDirectory, string projectName, DiagnosticBag diagnostics)
    {
        Directory.CreateDirectory(outputDirectory);
        EmitCsproj(outputDirectory, projectName);

        // Collect namespaces from Backend-origin symbols for using directives.
        var backendNamespaces = CollectBackendNamespaces(model);

        // Emit one .cs file per Mist source file.
        var anyNeedsMistVoid = false;
        for (var i = 0; i < model.Units.Count; i++)
        {
            var unit = model.Units[i];

            // Stdlib units are processed normally — pure-Mist stdlib types
            // like Optional<T> need to be emitted as C#. Backend-backed
            // types (Int32, String, etc.) are skipped at the per-type level
            // in EmitClassDeclaration / EmitStructDeclaration via the
            // IsBackendBacked flag.

            var csFileName = Path.ChangeExtension(
                Path.GetFileName(unit.Source.Path), ".cs");
            var csPath = Path.Combine(outputDirectory, csFileName);

            // Get the AST for this compilation unit (same index as the unit).
            if (i < model.Asts.Length && model.Asts[i] is CompilationUnitNode compilationUnit)
            {
                var writer = new CSharpSourceWriter(model, backendNamespaces, unit.Source, diagnostics);
                var csContent = writer.EmitCompilationUnit(compilationUnit);
                File.WriteAllText(csPath, csContent);

                if (writer.NeedsMistVoidStruct)
                    anyNeedsMistVoid = true;
            }
        }

        // Emit the MistVoid helper struct once into a shared file if any compilation unit uses void as a value.
        if (anyNeedsMistVoid)
        {
            var mistVoidPath = Path.Combine(outputDirectory, "MistVoid.g.cs");
            File.WriteAllText(mistVoidPath, GenerateMistVoidFile());
        }
    }

    private static void EmitCsproj(string outputDirectory, string projectName)
    {
        var csprojPath = Path.Combine(outputDirectory, $"{projectName}.csproj");
        var csprojContent = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
                <Deterministic>true</Deterministic>
                <EnableNETAnalyzers>false</EnableNETAnalyzers>
              </PropertyGroup>
            </Project>
            """;
        File.WriteAllText(csprojPath, csprojContent);
    }

    private static ImmutableArray<string> CollectBackendNamespaces(ISemanticModel model)
    {
        var namespaces = new HashSet<string>(StringComparer.Ordinal);

        foreach (var symbol in model.AllSymbols)
        {
            if (symbol.Origin == SymbolOrigin.Backend)
            {
                if (symbol.DeclaredType is ClassType ct && ct.ContainingNamespace is not null)
                {
                    namespaces.Add(ct.ContainingNamespace);
                }
                else if (symbol.DeclaredType is StructType st && st.ContainingNamespace is not null)
                {
                    namespaces.Add(st.ContainingNamespace);
                }
                else if (symbol.DeclaredType is InterfaceType it && it.ContainingNamespace is not null)
                {
                    namespaces.Add(it.ContainingNamespace);
                }
                else if (symbol.DeclaredType is TraitType tt && tt.ContainingNamespace is not null)
                {
                    namespaces.Add(tt.ContainingNamespace);
                }
                else if (symbol.Kind == SymbolKind.Namespace)
                {
                    namespaces.Add(symbol.Name);
                }
            }
        }

        return [.. namespaces.OrderBy(n => n, StringComparer.Ordinal)];
    }

    private static string GenerateMistVoidFile()
    {
        return """
            #nullable enable
            [System.Runtime.CompilerServices.CompilerGenerated]
            public readonly struct MistVoid : System.IEquatable<MistVoid>
            {
                public static readonly MistVoid Instance = default;
                public bool Equals(MistVoid other) => true;
                public override bool Equals(object? obj) => obj is MistVoid;
                public override int GetHashCode() => 0;
                public static bool operator ==(MistVoid left, MistVoid right) => true;
                public static bool operator !=(MistVoid left, MistVoid right) => false;
                public override string ToString() => "Void";
            }
            """;
    }
}

/// <summary>
/// Writes C# source text for a single compilation unit by walking the AST.
/// Uses the semantic model for type information when needed.
/// </summary>
internal sealed class CSharpSourceWriter
{
    private readonly StringBuilder _sb = new();
    private readonly ISemanticModel _model;
    private readonly ImmutableArray<string> _backendNamespaces;
    private readonly Common.Text.SourceText _sourceText;
    private readonly IIntrinsicEmissionRegistry _intrinsicRegistry;
    private readonly DiagnosticBag _diagnostics;
    private int _indentLevel;
    private bool _needsMistVoidStruct;
    private bool _inValueReturningVoidMethod;
    private readonly HashSet<string> _valueReturningVoidMethods = new(StringComparer.Ordinal);
    private const string IndentUnit = "    ";

    /// <summary>
    /// Indicates whether this compilation unit requires the MistVoid helper struct.
    /// Checked by the emitter after emission to decide whether to generate the shared MistVoid file.
    /// </summary>
    public bool NeedsMistVoidStruct => _needsMistVoidStruct;

    public CSharpSourceWriter(
        ISemanticModel model,
        ImmutableArray<string> backendNamespaces,
        Common.Text.SourceText sourceText,
        DiagnosticBag diagnostics)
    {
        _model = model;
        _backendNamespaces = backendNamespaces;
        _sourceText = sourceText;
        _diagnostics = diagnostics;
        _intrinsicRegistry = new IntrinsicEmissionTable();
    }

    public string EmitCompilationUnit(CompilationUnitNode cu)
    {
        // Analyze which void methods have their return values consumed at call sites.
        AnalyzeValueReturningVoidMethods(cu);

        // Collect using directives from the source.
        var usings = new HashSet<string>(StringComparer.Ordinal);

        foreach (var usingDecl in cu.Usings)
        {
            var name = ExtractQualifiedName(usingDecl.Name);
            usings.Add(name);
        }

        // Also add backend namespaces that are referenced.
        foreach (var ns in _backendNamespaces)
        {
            usings.Add(ns);
        }

        // Emit using directives.
        foreach (var ns in usings.OrderBy(n => n, StringComparer.Ordinal))
        {
            AppendLine($"using {ns};");
        }

        if (usings.Count > 0)
        {
            AppendLine();
        }

        // Emit namespace and type declarations from items.
        // Top-level types (outside any explicit namespace) belong to the inferred
        // namespace. We look up each type's ContainingNamespace from the semantic
        // model and emit a file-scoped namespace declaration before the first type
        // in each inferred namespace group.
        string? emittedInferredNamespace = null;

        foreach (var item in cu.Items)
        {
            switch (item)
            {
                case NamespaceItemTopLevelItemNode nsItem:
                    EmitNamespaceDeclaration(nsItem.NamespaceDeclaration);
                    break;
                case TypeItemTopLevelItemNode typeItem:
                {
                    // Determine the type's namespace from the semantic model.
                    var typeNamespace = GetTypeContainingNamespace(typeItem.TypeDeclaration);

                    // If this type has a namespace and we haven't emitted it yet,
                    // emit a file-scoped namespace declaration.
                    if (typeNamespace is not null && typeNamespace != emittedInferredNamespace)
                    {
                        if (emittedInferredNamespace is not null)
                        {
                            // Separate namespace sections with a blank line.
                            AppendLine();
                        }
                        AppendLine($"namespace {typeNamespace};");
                        AppendLine();
                        emittedInferredNamespace = typeNamespace;
                    }

                    EmitTypeDeclaration(typeItem.TypeDeclaration);
                    break;
                }
            }
        }

        return _sb.ToString();
    }

    /// <summary>
    /// <summary>
    /// Scans the compilation unit AST to identify void methods whose return values
    /// are consumed at any call site. A call's return value is "consumed" if the call
    /// does not appear as a standalone expression statement — i.e., it appears as a
    /// variable initializer, method argument, assignment right-hand side, or part of
    /// a larger expression.
    /// </summary>
    private void AnalyzeValueReturningVoidMethods(CompilationUnitNode cu)
    {
        // Step 1: Collect all void method names declared in this compilation unit.
        var voidMethodNames = new HashSet<string>(StringComparer.Ordinal);
        CollectVoidMethodNames(cu, voidMethodNames);

        if (voidMethodNames.Count == 0)
            return;

        // Step 2: Walk the AST to find call sites where a void method's return value is consumed.
        FindValueReturningCallSites(cu, voidMethodNames);
    }

    /// <summary>
    /// Recursively collects the names of all methods with a void return type
    /// declared in the compilation unit.
    /// </summary>
    private static void CollectVoidMethodNames(CompilationUnitNode cu, HashSet<string> voidMethodNames)
    {
        foreach (var item in cu.Items)
        {
            switch (item)
            {
                case NamespaceItemTopLevelItemNode nsItem:
                {
                    var types = nsItem.NamespaceDeclaration switch
                    {
                        FileScopedNamespaceNamespaceDeclarationNode fileScopedNs => fileScopedNs.Body,
                        BlockNamespaceNamespaceDeclarationNode blockNs => blockNs.Body,
                        _ => System.Collections.Immutable.ImmutableArray<TypeDeclarationNode>.Empty,
                    };

                    foreach (var typeDecl in types)
                    {
                        CollectVoidMethodNamesFromType(typeDecl, voidMethodNames);
                    }
                    break;
                }
                case TypeItemTopLevelItemNode typeItem:
                    CollectVoidMethodNamesFromType(typeItem.TypeDeclaration, voidMethodNames);
                    break;
            }
        }
    }

    private static void CollectVoidMethodNamesFromType(TypeDeclarationNode typeDecl, HashSet<string> voidMethodNames)
    {
        var members = typeDecl switch
        {
            ClassTypeTypeDeclarationNode classType => classType.ClassDeclaration.Members,
            StructTypeTypeDeclarationNode structType => structType.StructDeclaration.Members,
            _ => System.Collections.Immutable.ImmutableArray<MemberDeclarationNode>.Empty,
        };

        foreach (var member in members)
        {
            if (member is MethodMemberMemberDeclarationNode methodMember &&
                methodMember.Method.ReturnType is VoidTypeRefTypeReferenceNode)
            {
                voidMethodNames.Add(methodMember.Method.Name.Lexeme);
            }
        }
    }

    /// <summary>
    /// Walks the AST to find call sites of void methods where the return value is consumed.
    /// A call's return value is consumed if it appears in any context other than a standalone
    /// expression statement (e.g., as a variable initializer, method argument, or assignment RHS).
    /// </summary>
    private void FindValueReturningCallSites(CompilationUnitNode cu, HashSet<string> voidMethodNames)
    {
        // Walk all type declarations to find method bodies.
        var types = new List<TypeDeclarationNode>();

        foreach (var item in cu.Items)
        {
            switch (item)
            {
                case NamespaceItemTopLevelItemNode nsItem:
                {
                    var nsTypes = nsItem.NamespaceDeclaration switch
                    {
                        FileScopedNamespaceNamespaceDeclarationNode fileScopedNs => fileScopedNs.Body,
                        BlockNamespaceNamespaceDeclarationNode blockNs => blockNs.Body,
                        _ => System.Collections.Immutable.ImmutableArray<TypeDeclarationNode>.Empty,
                    };
                    types.AddRange(nsTypes);
                    break;
                }
                case TypeItemTopLevelItemNode typeItem:
                    types.Add(typeItem.TypeDeclaration);
                    break;
            }
        }

        foreach (var typeDecl in types)
        {
            var members = typeDecl switch
            {
                ClassTypeTypeDeclarationNode classType => classType.ClassDeclaration.Members,
                StructTypeTypeDeclarationNode structType => structType.StructDeclaration.Members,
                _ => System.Collections.Immutable.ImmutableArray<MemberDeclarationNode>.Empty,
            };

            foreach (var member in members)
            {
                switch (member)
                {
                    case MethodMemberMemberDeclarationNode methodMember:
                        if (methodMember.Method.Body.GetBlock() is BlockNode mb)
                            ScanBlockForValueReturningCalls(mb, voidMethodNames);
                        break;
                    case ConstructorMemberMemberDeclarationNode ctorMember:
                        if (ctorMember.Constructor.Body.GetBlock() is BlockNode cb)
                            ScanBlockForValueReturningCalls(cb, voidMethodNames);
                        break;
                }
            }
        }
    }

    /// <summary>
    /// Scans a block of statements for void method calls whose return values are consumed.
    /// </summary>
    private void ScanBlockForValueReturningCalls(BlockNode block, HashSet<string> voidMethodNames)
    {
        foreach (var stmt in block.Statements)
        {
            ScanStatementForValueReturningCalls(stmt, voidMethodNames);
        }
    }

    /// <summary>
    /// Scans a statement for void method calls whose return values are consumed.
    /// Expression statements (standalone calls like `method();`) do NOT consume the return value.
    /// All other contexts (variable initializers, arguments, assignments, etc.) DO consume it.
    /// </summary>
    private void ScanStatementForValueReturningCalls(StatementNode stmt, HashSet<string> voidMethodNames)
    {
        switch (stmt)
        {
            case ExpressionStatementStatementNode exprStmt:
                // A standalone expression statement does NOT consume the return value.
                // However, the expression itself might contain nested calls that do consume values.
                // For example: `otherMethod(voidMethod())` as a statement — the inner call is consumed.
                ScanExpressionForNestedValueReturningCalls(exprStmt.ExpressionStatement.Expression, voidMethodNames);
                break;

            case VariableDeclarationStatementStatementNode varDeclStmt:
                // The initializer expression is in a value-consuming context.
                switch (varDeclStmt.VariableDeclaration)
                {
                    case TypedVariableDeclarationVariableDeclarationStatementNode typed:
                        ScanExpressionForValueReturningCalls(typed.Initializer, voidMethodNames);
                        break;
                    case InferredVariableDeclarationVariableDeclarationStatementNode inferred:
                        ScanExpressionForValueReturningCalls(inferred.Initializer, voidMethodNames);
                        break;
                }
                break;

            case ReturnStatementStatementNode returnStmt:
                if (returnStmt.ReturnStatement.Value is not null)
                {
                    ScanExpressionForValueReturningCalls(returnStmt.ReturnStatement.Value, voidMethodNames);
                }
                break;

            case IfStatementStatementNode ifStmt:
                ScanExpressionForValueReturningCalls(ifStmt.IfStatement.Condition, voidMethodNames);
                ScanBlockForValueReturningCalls(ifStmt.IfStatement.ThenBranch, voidMethodNames);
                if (ifStmt.IfStatement.ElseBranch is not null)
                {
                    ScanElseClauseForValueReturningCalls(ifStmt.IfStatement.ElseBranch, voidMethodNames);
                }
                break;

            case WhileStatementStatementNode whileStmt:
                ScanExpressionForValueReturningCalls(whileStmt.WhileStatement.Condition, voidMethodNames);
                ScanBlockForValueReturningCalls(whileStmt.WhileStatement.Body, voidMethodNames);
                break;

            case ForStatementStatementNode forStmt:
                if (forStmt.ForStatement.Condition is not null)
                    ScanExpressionForValueReturningCalls(forStmt.ForStatement.Condition, voidMethodNames);
                if (forStmt.ForStatement.Iterator is not null)
                    ScanExpressionForValueReturningCalls(forStmt.ForStatement.Iterator, voidMethodNames);
                ScanBlockForValueReturningCalls(forStmt.ForStatement.Body, voidMethodNames);
                break;

            case ForeachStatementStatementNode foreachStmt:
                ScanExpressionForValueReturningCalls(foreachStmt.ForeachStatement.Collection, voidMethodNames);
                ScanBlockForValueReturningCalls(foreachStmt.ForeachStatement.Body, voidMethodNames);
                break;
        }
    }

    private void ScanElseClauseForValueReturningCalls(ElseClauseNode elseClause, HashSet<string> voidMethodNames)
    {
        switch (elseClause)
        {
            case ElseIfElseClauseNode elseIf:
                ScanExpressionForValueReturningCalls(elseIf.ElseIf.Condition, voidMethodNames);
                ScanBlockForValueReturningCalls(elseIf.ElseIf.ThenBranch, voidMethodNames);
                if (elseIf.ElseIf.ElseBranch is not null)
                    ScanElseClauseForValueReturningCalls(elseIf.ElseIf.ElseBranch, voidMethodNames);
                break;
            case ElseBlockElseClauseNode elseBlock:
                ScanBlockForValueReturningCalls(elseBlock.ElseBlock, voidMethodNames);
                break;
        }
    }

    /// <summary>
    /// Scans an expression that is in a value-consuming context. Any void method call
    /// found at the top level or nested within this expression has its return value consumed.
    /// </summary>
    private void ScanExpressionForValueReturningCalls(ExpressionNode expr, HashSet<string> voidMethodNames)
    {
        // Walk the expression tree looking for method calls to void methods.
        ScanAssignmentForValueReturningCalls(expr.Assignment, voidMethodNames, isValueContext: true);
    }

    /// <summary>
    /// Scans an expression statement's expression for nested value-returning calls.
    /// The top-level call in an expression statement is NOT value-consuming, but nested
    /// calls (e.g., arguments) ARE value-consuming.
    /// </summary>
    private void ScanExpressionForNestedValueReturningCalls(ExpressionNode expr, HashSet<string> voidMethodNames)
    {
        // The top-level expression in an expression statement is NOT in a value context,
        // but sub-expressions (like arguments) ARE.
        ScanAssignmentForValueReturningCalls(expr.Assignment, voidMethodNames, isValueContext: false);
    }

    private void ScanAssignmentForValueReturningCalls(AssignmentNode assignment, HashSet<string> voidMethodNames, bool isValueContext)
    {
        // The target of an assignment is not in a value context for the purpose of the call itself,
        // but if there's an assignment value, the RHS is in a value context.
        ScanLogicalOrForValueReturningCalls(assignment.Target, voidMethodNames, isValueContext);

        if (assignment.Value is not null)
        {
            // The RHS of an assignment is always in a value context.
            ScanAssignmentForValueReturningCalls(assignment.Value, voidMethodNames, isValueContext: true);
        }
    }

    private void ScanLogicalOrForValueReturningCalls(LogicalOrNode node, HashSet<string> voidMethodNames, bool isValueContext)
    {
        ScanLogicalAndForValueReturningCalls(node.Left, voidMethodNames, isValueContext);
        if (node.Right is not null && !ReferenceEquals(node.Right, node.Left))
            ScanLogicalAndForValueReturningCalls(node.Right, voidMethodNames, isValueContext);
    }

    private void ScanLogicalAndForValueReturningCalls(LogicalAndNode node, HashSet<string> voidMethodNames, bool isValueContext)
    {
        ScanEqualityForValueReturningCalls(node.Left, voidMethodNames, isValueContext);
        if (node.Right is not null && !ReferenceEquals(node.Right, node.Left))
            ScanEqualityForValueReturningCalls(node.Right, voidMethodNames, isValueContext);
    }

    private void ScanEqualityForValueReturningCalls(EqualityNode node, HashSet<string> voidMethodNames, bool isValueContext)
    {
        ScanRelationalForValueReturningCalls(node.Left, voidMethodNames, isValueContext);
        if (node.Right is not null && !ReferenceEquals(node.Right, node.Left))
            ScanRelationalForValueReturningCalls(node.Right, voidMethodNames, isValueContext);
    }

    private void ScanRelationalForValueReturningCalls(RelationalNode node, HashSet<string> voidMethodNames, bool isValueContext)
    {
        ScanAdditiveForValueReturningCalls(node.Left, voidMethodNames, isValueContext);
        if (node.Right is not null && !ReferenceEquals(node.Right, node.Left))
            ScanAdditiveForValueReturningCalls(node.Right, voidMethodNames, isValueContext);
    }

    private void ScanAdditiveForValueReturningCalls(AdditiveNode node, HashSet<string> voidMethodNames, bool isValueContext)
    {
        ScanMultiplicativeForValueReturningCalls(node.Left, voidMethodNames, isValueContext);
        if (node.Right is not null && !ReferenceEquals(node.Right, node.Left))
            ScanMultiplicativeForValueReturningCalls(node.Right, voidMethodNames, isValueContext);
    }

    private void ScanMultiplicativeForValueReturningCalls(MultiplicativeNode node, HashSet<string> voidMethodNames, bool isValueContext)
    {
        ScanUnaryForValueReturningCalls(node.Left, voidMethodNames, isValueContext);
        if (node.Right is not null && !ReferenceEquals(node.Right, node.Left))
            ScanUnaryForValueReturningCalls(node.Right, voidMethodNames, isValueContext);
    }

    private void ScanUnaryForValueReturningCalls(UnaryNode unary, HashSet<string> voidMethodNames, bool isValueContext)
    {
        switch (unary)
        {
            case LogicalNotUnaryNode logicalNot:
                ScanUnaryForValueReturningCalls(logicalNot.Operand, voidMethodNames, isValueContext);
                break;
            case NegateUnaryNode negate:
                ScanUnaryForValueReturningCalls(negate.Operand, voidMethodNames, isValueContext);
                break;
            case PostfixExpressionUnaryNode postfix:
                ScanPostfixForValueReturningCalls(postfix.Primary, voidMethodNames, isValueContext);
                break;
        }
    }

    private void ScanPostfixForValueReturningCalls(PostfixNode postfix, HashSet<string> voidMethodNames, bool isValueContext)
    {
        // Walk through the postfix chain. The last suffix determines whether this is a call.
        // For `target.method(args)`, the MethodCallSuffix is the last suffix.
        // For `func(args)`, the InvocationSuffix is the last suffix.
        var suffixes = postfix.Suffix;

        for (var i = 0; i < suffixes.Length; i++)
        {
            var suffix = suffixes[i];
            var isLastSuffix = i == suffixes.Length - 1;

            switch (suffix)
            {
                case MethodCallSuffixPostfixSuffixNode methodCall:
                {
                    var methodName = methodCall.Name.Lexeme;

                    // If this is a call to a void method and we're in a value context,
                    // mark it as value-returning.
                    if (isValueContext && isLastSuffix && voidMethodNames.Contains(methodName))
                    {
                        _valueReturningVoidMethods.Add(methodName);
                    }

                    // Scan arguments — they are always in a value context.
                    if (methodCall.Arguments is not null)
                    {
                        ScanArgumentListForValueReturningCalls(methodCall.Arguments, voidMethodNames);
                    }
                    break;
                }
                case InvocationSuffixPostfixSuffixNode invocation:
                {
                    // Direct invocation: the target identifier is the method name.
                    if (isValueContext && isLastSuffix && postfix.Target is IdentifierExpressionPrimaryNode ident)
                    {
                        var methodName = ident.Name.Lexeme;
                        if (voidMethodNames.Contains(methodName))
                        {
                            _valueReturningVoidMethods.Add(methodName);
                        }
                    }

                    // Scan arguments — they are always in a value context.
                    if (invocation.Arguments is not null)
                    {
                        ScanArgumentListForValueReturningCalls(invocation.Arguments, voidMethodNames);
                    }
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Scans an argument list for void method calls whose return values are consumed.
    /// All arguments are in a value-consuming context.
    /// </summary>
    private void ScanArgumentListForValueReturningCalls(ArgumentListNode argList, HashSet<string> voidMethodNames)
    {
        // Scan the first argument expression.
        ScanExpressionForValueReturningCalls(argList.Expression, voidMethodNames);

        // The Storm parser may not expose additional arguments as separate AST nodes
        // (they're captured via source span). We scan what's available in the AST.
    }

    /// <summary>
    /// Looks up the ContainingNamespace for a type declaration by querying the
    /// semantic model for the symbol declared by the inner declaration node
    /// (ClassDeclaration, StructDeclaration, or InterfaceDeclaration).
    /// </summary>
    private string? GetTypeContainingNamespace(TypeDeclarationNode typeDecl)
    {
        Storm.Runtime.SyntaxNode? innerNode = typeDecl switch
        {
            ClassTypeTypeDeclarationNode classType => classType.ClassDeclaration,
            StructTypeTypeDeclarationNode structType => structType.StructDeclaration,
            InterfaceTypeTypeDeclarationNode interfaceType => interfaceType.InterfaceDeclaration,
            TraitTypeTypeDeclarationNode traitType => traitType.TraitDeclaration,
            _ => null,
        };

        if (innerNode is null) return null;

        var symbol = _model.GetDeclaredSymbol(innerNode);
        if (symbol?.DeclaredType is null) return null;

        return symbol.DeclaredType switch
        {
            ClassType ct => ct.ContainingNamespace,
            StructType st => st.ContainingNamespace,
            InterfaceType it => it.ContainingNamespace,
            TraitType tt => tt.ContainingNamespace,
            _ => null,
        };
    }

    private void EmitNamespaceDeclaration(NamespaceDeclarationNode ns)
    {
        switch (ns)
        {
            case FileScopedNamespaceNamespaceDeclarationNode fileScopedNs:
            {
                var name = ExtractQualifiedName(fileScopedNs.Name);
                AppendLine($"namespace {name};");
                AppendLine();

                foreach (var typeDecl in fileScopedNs.Body)
                {
                    EmitTypeDeclaration(typeDecl);
                }
                break;
            }
            case BlockNamespaceNamespaceDeclarationNode blockNs:
            {
                var name = ExtractQualifiedName(blockNs.Name);
                AppendLine($"namespace {name}");
                AppendLine("{");
                _indentLevel++;

                foreach (var typeDecl in blockNs.Body)
                {
                    EmitTypeDeclaration(typeDecl);
                }

                _indentLevel--;
                AppendLine("}");
                break;
            }
        }
    }

    private void EmitTypeDeclaration(TypeDeclarationNode typeDecl)
    {
        switch (typeDecl)
        {
            case ClassTypeTypeDeclarationNode classType:
                EmitClassDeclaration(classType.ClassDeclaration);
                break;
            case StructTypeTypeDeclarationNode structType:
                EmitStructDeclaration(structType.StructDeclaration);
                break;
            case TraitTypeTypeDeclarationNode traitType:
                EmitTraitDeclaration(traitType.TraitDeclaration);
                break;
            case InterfaceTypeTypeDeclarationNode interfaceType:
                EmitInterfaceDeclaration(interfaceType.InterfaceDeclaration);
                break;
        }
    }

    private void EmitClassDeclaration(ClassDeclarationNode classDecl)
    {
        // Check if this is a Backend symbol — skip if so.
        var symbol = _model.GetDeclaredSymbol(classDecl);
        if (symbol is not null && symbol.Origin == SymbolOrigin.Backend)
        {
            return;
        }

        // Skip intrinsic backend-backed stdlib types (e.g., Standard.String,
        // Standard.Array<T>). Their storage and methods come from the
        // Backing_Type_Map / IntrinsicEmissionTable, not from Mist bodies.
        // Pure-Mist stdlib types like Optional<T> are NOT backend-backed
        // and must be emitted normally.
        if (symbol?.DeclaredType is ClassType ct && ct.IsBackendBacked)
        {
            return;
        }

        var modifiers = EmitModifiers(classDecl.Modifiers);
        var name = classDecl.Name.Lexeme;
        var typeParams = EmitTypeParameterList(classDecl.TypeParams);
        var baseClause = "";
        if (classDecl.BaseList is not null)
        {
            var baseParts = EmitBaseListWithTypeArgs(classDecl.BaseList);

            // Append trait interfaces for this class
            var traitNames = new List<string>();
            if (symbol?.DeclaredType is ClassType classTypeFromBase)
            {
                foreach (var (_, trait, _) in _model.TraitSatisfactionMap.All()
                    .Where(s => ReferenceEquals(s.Type, classTypeFromBase)))
                {
                    traitNames.Add(trait.DisplayName);
                }
            }

            baseClause = traitNames.Count > 0
                ? $" : {baseParts}, {string.Join(", ", traitNames)}"
                : $" : {baseParts}";
        }
        else if (symbol?.DeclaredType is ClassType classType)
        {
            var parts = new List<string>();

            // Base class (if any)
            if (classType.BaseType is not null)
            {
                parts.Add(CSharpTypeMap.GetTypeName(classType.BaseType));
            }

            // Interfaces
            foreach (var iface in classType.Interfaces)
            {
                parts.Add(CSharpTypeMap.GetTypeName(iface));
            }

            // Trait interfaces
            foreach (var (_, trait, _) in _model.TraitSatisfactionMap.All()
                .Where(s => ReferenceEquals(s.Type, classType)))
            {
                parts.Add(trait.DisplayName);
            }

            if (parts.Count > 0)
            {
                baseClause = $" : {string.Join(", ", parts)}";
            }
        }
        AppendLine($"{modifiers}class {name}{typeParams}{baseClause}");
        AppendLine("{");
        _indentLevel++;

        // Emit every member in declaration order. No name-based grouping or
        // deduplication is performed, so overloaded methods and constructors
        // are each emitted as separate C# declarations.
        for (var i = 0; i < classDecl.Members.Length; i++)
        {
            if (i > 0)
            {
                AppendLine();
            }
            EmitMemberDeclaration(classDecl.Members[i]);
        }

        // Emit explicit interface implementations for with-mapped trait members.
        if (symbol?.DeclaredType is ClassType classTypeForTraits)
        {
            EmitExplicitTraitImplementations(classTypeForTraits, classDecl.Members.Length > 0);
        }

        _indentLevel--;
        AppendLine("}");
    }

    private void EmitInterfaceDeclaration(InterfaceDeclarationNode interfaceDecl)
    {
        var name = interfaceDecl.Name.Lexeme;
        var typeParams = EmitTypeParameterList(interfaceDecl.TypeParams);
        var interfaceList = "";
        if (interfaceDecl.Interfaces is not null)
        {
            interfaceList = $" : {EmitBaseListWithTypeArgs(interfaceDecl.Interfaces)}";
        }
        AppendLine($"interface {name}{typeParams}{interfaceList}");
        AppendLine("{");
        _indentLevel++;

        foreach (var member in interfaceDecl.Members)
        {
            switch (member)
            {
                case InterfaceMethodMemberInterfaceMemberDeclarationNode methodMember:
                    EmitInterfaceMethodDeclaration(methodMember.Method);
                    break;
                case InterfacePropertyMemberInterfaceMemberDeclarationNode propertyMember:
                    EmitInterfacePropertyDeclaration(propertyMember.Property);
                    break;
            }
        }

        _indentLevel--;
        AppendLine("}");
    }

    private void EmitInterfaceMethodDeclaration(InterfaceMethodDeclarationNode methodDecl)
    {
        var returnType = EmitTypeReference(methodDecl.ReturnType);
        var name = methodDecl.Name.Lexeme;
        var typeParams = EmitTypeParameterList(methodDecl.TypeParams);
        var parameters = EmitParameterList(methodDecl.Parameters);
        AppendLine($"{returnType} {name}{typeParams}({parameters});");
    }

    private void EmitInterfacePropertyDeclaration(InterfacePropertyDeclarationNode propertyDecl)
    {
        var type = EmitTypeReference(propertyDecl.Type);
        var name = propertyDecl.Name.Lexeme;

        var accessors = new List<string>();
        foreach (var accessor in propertyDecl.Accessors)
        {
            switch (accessor)
            {
                case InterfaceGetAccessorInterfaceAccessorDeclarationNode:
                    accessors.Add("get;");
                    break;
                case InterfaceSetAccessorInterfaceAccessorDeclarationNode:
                    accessors.Add("set;");
                    break;
            }
        }

        AppendLine($"{type} {name} {{ {string.Join(" ", accessors)} }}");
    }

    // ─── Trait Declarations ─────────────────────────────────────────────

    private void EmitTraitDeclaration(TraitDeclarationNode traitDecl)
    {
        var name = traitDecl.Name.Lexeme;
        AppendLine($"interface {name}");
        AppendLine("{");
        _indentLevel++;

        foreach (var member in traitDecl.Members)
        {
            switch (member)
            {
                case TraitFieldMemberTraitMemberDeclarationNode fieldMember:
                    EmitTraitFieldAsProperty(fieldMember.Field);
                    break;
                case TraitMethodMemberTraitMemberDeclarationNode methodMember:
                    EmitTraitMethodDeclaration(methodMember.Method);
                    break;
                case TraitPropertyMemberTraitMemberDeclarationNode propertyMember:
                    EmitTraitPropertyDeclaration(propertyMember.Property);
                    break;
            }
        }

        _indentLevel--;
        AppendLine("}");
    }

    private void EmitTraitFieldAsProperty(TraitFieldDeclarationNode fieldDecl)
    {
        var type = EmitTypeReference(fieldDecl.Type);
        var name = fieldDecl.Name.Lexeme;
        AppendLine($"{type} {name} {{ get; }}");
    }

    private void EmitTraitMethodDeclaration(TraitMethodDeclarationNode methodDecl)
    {
        var returnType = EmitTypeReference(methodDecl.ReturnType);
        var name = methodDecl.Name.Lexeme;
        var parameters = EmitParameterList(methodDecl.Parameters);
        AppendLine($"{returnType} {name}({parameters});");
    }

    private void EmitTraitPropertyDeclaration(TraitPropertyDeclarationNode propertyDecl)
    {
        var type = EmitTypeReference(propertyDecl.Type);
        var name = propertyDecl.Name.Lexeme;

        var accessors = new List<string>();
        foreach (var accessor in propertyDecl.Accessors)
        {
            switch (accessor)
            {
                case InterfaceGetAccessorInterfaceAccessorDeclarationNode:
                    accessors.Add("get;");
                    break;
                case InterfaceSetAccessorInterfaceAccessorDeclarationNode:
                    accessors.Add("set;");
                    break;
            }
        }

        AppendLine($"{type} {name} {{ {string.Join(" ", accessors)} }}");
    }

    // ─── Explicit Trait Interface Implementations ────────────────────────

    /// <summary>
    /// Emits explicit interface implementations for all <c>with</c>-mapped members
    /// on the given type. Only <c>with</c>-mapped members get explicit implementations;
    /// structurally matched members are already compatible by name.
    /// </summary>
    private void EmitExplicitTraitImplementations(IType type, bool hasExistingMembers)
    {
        foreach (var (_, trait, satisfaction) in _model.TraitSatisfactionMap.All()
            .Where(s => ReferenceEquals(s.Type, type)))
        {
            if (satisfaction.WithMappings.Count == 0)
                continue;

            var traitName = trait.DisplayName;

            foreach (var (traitMemberName, sourceMemberSymbol) in satisfaction.WithMappings)
            {
                // Look up the trait member symbol from the trait's member scope.
                var traitMemberSymbol = trait.MemberScope.Lookup(traitMemberName);
                if (traitMemberSymbol is null)
                    continue;

                // Get the declaring node of the trait member to determine its kind.
                var declaringNode = _model.GetDeclaringNode(traitMemberSymbol);
                var sourceName = sourceMemberSymbol.Name;

                if (hasExistingMembers)
                {
                    AppendLine();
                }
                hasExistingMembers = true;

                switch (declaringNode)
                {
                    case TraitFieldDeclarationNode fieldDecl:
                    {
                        // Field mapping: emit as property with expression body.
                        // int ITraitName.x => this.a;
                        var fieldType = EmitTypeReference(fieldDecl.Type);
                        AppendLine($"{fieldType} {traitName}.{traitMemberName} => this.{sourceName};");
                        break;
                    }
                    case TraitMethodDeclarationNode methodDecl:
                    {
                        // Method mapping: emit as method with expression body delegating to source.
                        // int ITraitName.calculate(int a) => this.compute(a);
                        var returnType = EmitTypeReference(methodDecl.ReturnType);
                        var parameters = EmitTraitMethodParameters(methodDecl.Parameters);
                        var arguments = EmitTraitMethodArguments(methodDecl.Parameters);
                        AppendLine($"{returnType} {traitName}.{traitMemberName}({parameters}) => this.{sourceName}({arguments});");
                        break;
                    }
                    case TraitPropertyDeclarationNode propertyDecl:
                    {
                        // Property mapping: emit as property delegating to source.
                        // int ITraitName.x => this.a;  (for get-only)
                        // or with get/set accessors
                        var propType = EmitTypeReference(propertyDecl.Type);
                        var hasGet = false;
                        var hasSet = false;
                        foreach (var accessor in propertyDecl.Accessors)
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

                        if (hasGet && !hasSet)
                        {
                            // Get-only property: use expression body.
                            AppendLine($"{propType} {traitName}.{traitMemberName} => this.{sourceName};");
                        }
                        else
                        {
                            // Property with get and/or set: use block body.
                            AppendLine($"{propType} {traitName}.{traitMemberName}");
                            AppendLine("{");
                            _indentLevel++;
                            if (hasGet)
                                AppendLine($"get => this.{sourceName};");
                            if (hasSet)
                                AppendLine($"set => this.{sourceName} = value;");
                            _indentLevel--;
                            AppendLine("}");
                        }
                        break;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Emits the parameter declarations for a trait method (type + name pairs).
    /// Used when generating explicit interface implementations.
    /// </summary>
    private string EmitTraitMethodParameters(ParameterListNode? paramList)
    {
        if (paramList is null) return "";

        var parts = new List<string>();
        foreach (var child in paramList.Children())
        {
            if (child is ParameterNode param)
            {
                var type = EmitTypeReference(param.Type);
                var name = param.Name.Lexeme;
                parts.Add($"{type} {name}");
            }
        }

        return string.Join(", ", parts);
    }

    /// <summary>
    /// Emits the argument names for forwarding a trait method call to the source method.
    /// Used when generating explicit interface implementations.
    /// </summary>
    private static string EmitTraitMethodArguments(ParameterListNode? paramList)
    {
        if (paramList is null) return "";

        var names = new List<string>();
        foreach (var child in paramList.Children())
        {
            if (child is ParameterNode param)
            {
                names.Add(param.Name.Lexeme);
            }
        }

        return string.Join(", ", names);
    }

    private void EmitStructDeclaration(StructDeclarationNode structDecl)
    {
        var symbol = _model.GetDeclaredSymbol(structDecl);
        if (symbol is not null && symbol.Origin == SymbolOrigin.Backend)
        {
            return;
        }

        // Skip intrinsic backend-backed stdlib types (e.g., Standard.Int32,
        // Standard.Boolean). Their storage and methods come from the
        // Backing_Type_Map / IntrinsicEmissionTable, not from Mist bodies.
        if (symbol?.DeclaredType is StructType st && st.IsBackendBacked)
        {
            return;
        }

        var modifiers = EmitModifiers(structDecl.Modifiers);
        var name = structDecl.Name.Lexeme;
        var typeParams = EmitTypeParameterList(structDecl.TypeParams);
        var interfaceList = "";
        if (structDecl.Interfaces is not null)
        {
            var baseParts = EmitBaseListWithTypeArgs(structDecl.Interfaces);

            // Append trait interfaces for this struct
            var traitNames = new List<string>();
            if (symbol?.DeclaredType is StructType structTypeFromBase)
            {
                foreach (var (_, trait, _) in _model.TraitSatisfactionMap.All()
                    .Where(s => ReferenceEquals(s.Type, structTypeFromBase)))
                {
                    traitNames.Add(trait.DisplayName);
                }
            }

            interfaceList = traitNames.Count > 0
                ? $" : {baseParts}, {string.Join(", ", traitNames)}"
                : $" : {baseParts}";
        }
        else if (symbol?.DeclaredType is StructType structType)
        {
            var parts = new List<string>();

            // Interfaces
            foreach (var iface in structType.Interfaces)
            {
                parts.Add(CSharpTypeMap.GetTypeName(iface));
            }

            // Trait interfaces
            foreach (var (_, trait, _) in _model.TraitSatisfactionMap.All()
                .Where(s => ReferenceEquals(s.Type, structType)))
            {
                parts.Add(trait.DisplayName);
            }

            if (parts.Count > 0)
            {
                interfaceList = $" : {string.Join(", ", parts)}";
            }
        }
        AppendLine($"{modifiers}struct {name}{typeParams}{interfaceList}");
        AppendLine("{");
        _indentLevel++;

        // Emit every member in declaration order. No name-based grouping or
        // deduplication is performed, so overloaded methods and constructors
        // are each emitted as separate C# declarations.
        for (var i = 0; i < structDecl.Members.Length; i++)
        {
            if (i > 0)
            {
                AppendLine();
            }
            EmitMemberDeclaration(structDecl.Members[i]);
        }

        // Emit explicit interface implementations for with-mapped trait members.
        if (symbol?.DeclaredType is StructType structTypeForTraits)
        {
            EmitExplicitTraitImplementations(structTypeForTraits, structDecl.Members.Length > 0);
        }

        _indentLevel--;
        AppendLine("}");
    }

    /// <summary>
    /// Emits a single member declaration from the AST.
    /// Each member is emitted individually without any name-based deduplication,
    /// which ensures that overloaded methods and constructors (multiple declarations
    /// sharing the same name but with different parameter signatures) are all emitted
    /// as separate C# declarations.
    /// </summary>
    private void EmitMemberDeclaration(MemberDeclarationNode memberDecl)
    {
        switch (memberDecl)
        {
            case FieldMemberMemberDeclarationNode fieldMember:
                EmitFieldDeclaration(fieldMember.Field);
                break;
            case ConstructorMemberMemberDeclarationNode ctorMember:
                EmitConstructorDeclaration(ctorMember.Constructor);
                break;
            case MethodMemberMemberDeclarationNode methodMember:
                EmitMethodDeclaration(methodMember.Method);
                break;
            case PropertyMemberMemberDeclarationNode propertyMember:
                EmitPropertyDeclaration(propertyMember.Property);
                break;
            case IndexerMemberMemberDeclarationNode indexerMember:
                EmitIndexerDeclaration(indexerMember.Indexer);
                break;
        }
    }

    private void EmitFieldDeclaration(FieldDeclarationNode fieldDecl)
    {
        var modifiers = EmitModifiers(fieldDecl.Modifiers);
        var type = EmitTypeReference(fieldDecl.Type, isValueContext: true);
        var name = fieldDecl.Name.Lexeme;

        if (fieldDecl.Initializer != null)
        {
            // The field has an initializer expression.
            // With the bugfix, the Initializer property is now ExpressionNode? directly.
            AppendLine($"{modifiers}{type} {name} = {EmitExpression(fieldDecl.Initializer)};");
        }
        else
        {
            AppendLine($"{modifiers}{type} {name};");
        }
    }

    private void EmitConstructorDeclaration(ConstructorDeclarationNode ctorDecl)
    {
        var modifiers = EmitModifiers(ctorDecl.Modifiers);
        var name = ctorDecl.Name.Lexeme;
        var parameters = EmitParameterList(ctorDecl.Parameters);

        var chain = "";
        if (ctorDecl.Chain is ThisChainConstructorChainNode thisChain)
        {
            chain = " : this(" + EmitArgumentList(thisChain.Arguments) + ")";
        }
        else if (ctorDecl.Chain is BaseChainConstructorChainNode baseChain)
        {
            chain = " : base(" + EmitArgumentList(baseChain.Arguments) + ")";
        }

        AppendLine($"{modifiers}{name}({parameters}){chain}");
        if (ctorDecl.Body.GetBlock() is BlockNode block)
        {
            EmitBlock(block);
        }
        else
        {
            // No-body form (;) — grammar permits this for intrinsic members;
            // semantic validation handles whether it is legal on this decl.
            AppendLine(";");
        }
    }

    private void EmitMethodDeclaration(MethodDeclarationNode methodDecl)
    {
        var modifiers = EmitModifiers(methodDecl.Modifiers);
        var returnType = EmitTypeReference(methodDecl.ReturnType);
        var name = methodDecl.Name.Lexeme;
        var typeParams = EmitTypeParameterList(methodDecl.TypeParams);
        var parameters = EmitParameterList(methodDecl.Parameters);

        // Context-sensitive void return type: if this void method's return value
        // is consumed at any call site, emit MistVoid instead of void.
        var isValueReturningVoid = methodDecl.ReturnType is VoidTypeRefTypeReferenceNode
            && _valueReturningVoidMethods.Contains(name);
        if (isValueReturningVoid)
        {
            returnType = "MistVoid";
            _needsMistVoidStruct = true;
        }

        AppendLine($"{modifiers}{returnType} {name}{typeParams}({parameters})");

        // Set the flag so EmitReturnStatement knows to emit `return MistVoid.Instance;`
        var previousFlag = _inValueReturningVoidMethod;
        _inValueReturningVoidMethod = isValueReturningVoid;

        if (methodDecl.Body.GetBlock() is BlockNode block)
        {
            EmitMethodBlock(block, isValueReturningVoid);
        }
        else
        {
            // No-body form (;) — grammar permits this for intrinsic members.
            AppendLine(";");
        }

        _inValueReturningVoidMethod = previousFlag;
    }

    /// <summary>
    /// Emits a method body block. For value-returning void methods, appends
    /// <c>return MistVoid.Instance;</c> if the block does not already end with a return statement.
    /// </summary>
    private void EmitMethodBlock(BlockNode block, bool appendImplicitReturn)
    {
        AppendLine("{");
        _indentLevel++;

        foreach (var stmt in block.Statements)
        {
            EmitStatement(stmt);
        }

        // For value-returning void methods, ensure the method body ends with a return.
        // If the last statement is not a return, append `return MistVoid.Instance;`.
        if (appendImplicitReturn && !BlockEndsWithReturn(block))
        {
            AppendLine("return MistVoid.Instance;");
        }

        _indentLevel--;
        AppendLine("}");
    }

    /// <summary>
    /// Checks whether a block's last statement is a return statement.
    /// </summary>
    private static bool BlockEndsWithReturn(BlockNode block)
    {
        if (block.Statements.Length == 0)
            return false;

        return block.Statements[^1] is ReturnStatementStatementNode;
    }

    // ─── Property Emission ───────────────────────────────────────────────

    private void EmitPropertyDeclaration(PropertyDeclarationNode propertyDecl)
    {
        switch (propertyDecl)
        {
            case BlockPropertyPropertyDeclarationNode blockProp:
                EmitBlockPropertyDeclaration(blockProp);
                break;
            case ExpressionBodiedPropertyPropertyDeclarationNode exprProp:
                EmitExpressionBodiedPropertyDeclaration(exprProp);
                break;
        }
    }

    private void EmitBlockPropertyDeclaration(BlockPropertyPropertyDeclarationNode blockProp)
    {
        var modifiers = EmitModifiers(blockProp.Modifiers);
        var type = EmitTypeReference(blockProp.Type);
        var name = blockProp.Name.Lexeme;

        // Check if any accessor uses the 'field' keyword — if so, emit a backing field first.
        var backingFieldName = $"_{name}_k__BackingField";
        var usesField = AccessorsUseFieldKeyword(blockProp.Accessors);
        if (usesField)
        {
            AppendLine($"private {type} {backingFieldName};");
        }

        // Build accessor list.
        var accessorParts = new List<string>();
        foreach (var accessor in blockProp.Accessors)
        {
            accessorParts.Add(EmitAccessorDeclaration(accessor, usesField ? backingFieldName : null));
        }

        var accessorBody = string.Join(" ", accessorParts);

        // Emit initializer if present.
        if (blockProp.Initializer is not null)
        {
            var init = EmitExpression(blockProp.Initializer);
            AppendLine($"{modifiers}{type} {name} {{ {accessorBody} }} = {init};");
        }
        else
        {
            AppendLine($"{modifiers}{type} {name} {{ {accessorBody} }}");
        }
    }

    private void EmitExpressionBodiedPropertyDeclaration(ExpressionBodiedPropertyPropertyDeclarationNode exprProp)
    {
        var modifiers = EmitModifiers(exprProp.Modifiers);
        var type = EmitTypeReference(exprProp.Type);
        var name = exprProp.Name.Lexeme;
        var body = EmitExpression(exprProp.Body);

        AppendLine($"{modifiers}{type} {name} => {body};");
    }

    private string EmitAccessorDeclaration(AccessorDeclarationNode accessor, string? backingFieldName)
    {
        switch (accessor)
        {
            // ── Get accessors ──
            case AutoGetAccessorAccessorDeclarationNode autoGet:
            {
                var mod = EmitAccessorModifier(autoGet.AccessorModifier);
                return $"{mod}get;";
            }
            case BlockGetAccessorAccessorDeclarationNode blockGet:
            {
                var mod = EmitAccessorModifier(blockGet.AccessorModifier);
                var body = EmitAccessorBlock(blockGet.Body, backingFieldName);
                return $"{mod}get {body}";
            }
            case ExpressionGetAccessorAccessorDeclarationNode exprGet:
            {
                var mod = EmitAccessorModifier(exprGet.AccessorModifier);
                var expr = EmitAccessorExpression(exprGet.Expression, backingFieldName);
                return $"{mod}get => {expr};";
            }

            // ── Set accessors ──
            case AutoSetAccessorAccessorDeclarationNode autoSet:
            {
                var mod = EmitAccessorModifier(autoSet.AccessorModifier);
                return $"{mod}set;";
            }
            case BlockSetAccessorAccessorDeclarationNode blockSet:
            {
                var mod = EmitAccessorModifier(blockSet.AccessorModifier);
                var body = EmitAccessorBlock(blockSet.Body, backingFieldName);
                return $"{mod}set {body}";
            }
            case ExpressionSetAccessorAccessorDeclarationNode exprSet:
            {
                var mod = EmitAccessorModifier(exprSet.AccessorModifier);
                var expr = EmitAccessorExpression(exprSet.Expression, backingFieldName);
                return $"{mod}set => {expr};";
            }

            // ── Init accessors ──
            case AutoInitAccessorAccessorDeclarationNode autoInit:
            {
                var mod = EmitAccessorModifier(autoInit.AccessorModifier);
                return $"{mod}init;";
            }
            case BlockInitAccessorAccessorDeclarationNode blockInit:
            {
                var mod = EmitAccessorModifier(blockInit.AccessorModifier);
                var body = EmitAccessorBlock(blockInit.Body, backingFieldName);
                return $"{mod}init {body}";
            }
            case ExpressionInitAccessorAccessorDeclarationNode exprInit:
            {
                var mod = EmitAccessorModifier(exprInit.AccessorModifier);
                var expr = EmitAccessorExpression(exprInit.Expression, backingFieldName);
                return $"{mod}init => {expr};";
            }

            default:
                return "";
        }
    }

    private static string EmitAccessorModifier(ModifierNode? modifier)
    {
        if (modifier is null) return "";

        return modifier switch
        {
            PublicModifierModifierNode => "public ",
            PrivateModifierModifierNode => "private ",
            StaticModifierModifierNode => "static ",
            _ => "",
        };
    }

    /// <summary>
    /// Emits a block body for an accessor, replacing 'field' keyword references with the backing field name.
    /// </summary>
    private string EmitAccessorBlock(BlockNode block, string? backingFieldName)
    {
        var sb = new StringBuilder();
        sb.Append("{ ");

        foreach (var stmt in block.Statements)
        {
            sb.Append(EmitStatementInline(stmt, backingFieldName));
            sb.Append(' ');
        }

        sb.Append('}');
        return sb.ToString();
    }

    /// <summary>
    /// Emits an expression for an accessor, replacing 'field' keyword references with the backing field name.
    /// </summary>
    private string EmitAccessorExpression(ExpressionNode expr, string? backingFieldName)
    {
        if (backingFieldName is not null)
        {
            return ReplaceFieldKeyword(EmitExpression(expr), backingFieldName);
        }
        return EmitExpression(expr);
    }

    /// <summary>
    /// Emits a statement as inline text (for accessor block bodies).
    /// </summary>
    private string EmitStatementInline(StatementNode stmt, string? backingFieldName)
    {
        var text = EmitStatementToString(stmt);
        if (backingFieldName is not null)
        {
            text = ReplaceFieldKeyword(text, backingFieldName);
        }
        return text;
    }

    /// <summary>
    /// Emits a statement to a string (without appending to the main builder).
    /// </summary>
    private string EmitStatementToString(StatementNode stmt)
    {
        switch (stmt)
        {
            case ReturnStatementStatementNode returnStmt:
            {
                if (returnStmt.ReturnStatement.Value is not null)
                {
                    return $"return {EmitExpression(returnStmt.ReturnStatement.Value)};";
                }
                return "return;";
            }
            case ExpressionStatementStatementNode exprStmt:
                return $"{EmitExpression(exprStmt.ExpressionStatement.Expression)};";
            case VariableDeclarationStatementStatementNode varDeclStmt:
            {
                switch (varDeclStmt.VariableDeclaration)
                {
                    case TypedVariableDeclarationVariableDeclarationStatementNode typed:
                    {
                        var type = EmitTypeReference(typed.Type, isValueContext: true);
                        var name = typed.Name.Lexeme;
                        var init = EmitExpression(typed.Initializer);
                        return $"{type} {name} = {init};";
                    }
                    case InferredVariableDeclarationVariableDeclarationStatementNode inferred:
                    {
                        var name = inferred.Name.Lexeme;
                        var init = EmitExpression(inferred.Initializer);
                        return $"var {name} = {init};";
                    }
                }
                return "";
            }
            case IfStatementStatementNode ifStmt:
            {
                var condition = EmitExpression(ifStmt.IfStatement.Condition);
                var sb = new StringBuilder();
                sb.Append($"if ({condition}) {{ ");
                foreach (var s in ifStmt.IfStatement.ThenBranch.Statements)
                {
                    sb.Append(EmitStatementToString(s));
                    sb.Append(' ');
                }
                sb.Append('}');
                return sb.ToString();
            }
            case BreakStatementStatementNode:
                return "break;";
            default:
                return "";
        }
    }

    /// <summary>
    /// Replaces occurrences of the 'field' keyword identifier with the backing field name.
    /// The 'field' keyword in Mist source is emitted as the literal text "field" by EmitPrimary.
    /// We replace it with the backing field name using word-boundary matching.
    /// </summary>
    private static string ReplaceFieldKeyword(string text, string backingFieldName)
    {
        return System.Text.RegularExpressions.Regex.Replace(text, @"\bfield\b", backingFieldName);
    }

    /// <summary>
    /// Checks whether any accessor in the list uses the 'field' keyword.
    /// </summary>
    private static bool AccessorsUseFieldKeyword(ImmutableArray<AccessorDeclarationNode> accessors)
    {
        foreach (var accessor in accessors)
        {
            switch (accessor)
            {
                case BlockGetAccessorAccessorDeclarationNode blockGet:
                    if (ContainsFieldKeywordInBlock(blockGet.Body)) return true;
                    break;
                case ExpressionGetAccessorAccessorDeclarationNode exprGet:
                    if (ContainsFieldKeywordInNode(exprGet.Expression)) return true;
                    break;
                case BlockSetAccessorAccessorDeclarationNode blockSet:
                    if (ContainsFieldKeywordInBlock(blockSet.Body)) return true;
                    break;
                case ExpressionSetAccessorAccessorDeclarationNode exprSet:
                    if (ContainsFieldKeywordInNode(exprSet.Expression)) return true;
                    break;
                case BlockInitAccessorAccessorDeclarationNode blockInit:
                    if (ContainsFieldKeywordInBlock(blockInit.Body)) return true;
                    break;
                case ExpressionInitAccessorAccessorDeclarationNode exprInit:
                    if (ContainsFieldKeywordInNode(exprInit.Expression)) return true;
                    break;
            }
        }
        return false;
    }

    private static bool ContainsFieldKeywordInBlock(BlockNode block)
    {
        return ContainsFieldKeywordInNode(block);
    }

    private static bool ContainsFieldKeywordInNode(Storm.Runtime.SyntaxNode node)
    {
        if (node is FieldKeywordExpressionPrimaryNode)
            return true;

        foreach (var child in node.Children())
        {
            if (child is Storm.Runtime.SyntaxNode childNode && ContainsFieldKeywordInNode(childNode))
                return true;
        }
        return false;
    }

    // ─── Indexer Emission ────────────────────────────────────────────────

    private void EmitIndexerDeclaration(IndexerDeclarationNode indexerDecl)
    {
        if (indexerDecl is not BlockIndexerIndexerDeclarationNode blockIndexer)
            return;

        var modifiers = EmitModifiers(blockIndexer.Modifiers);
        var type = EmitTypeReference(blockIndexer.Type);
        var parameters = EmitParameterList(blockIndexer.Parameters);

        // Build accessor list.
        var accessorParts = new List<string>();
        foreach (var accessor in blockIndexer.Accessors)
        {
            accessorParts.Add(EmitAccessorDeclaration(accessor, null));
        }

        var accessorBody = string.Join(" ", accessorParts);
        AppendLine($"{modifiers}{type} this[{parameters}] {{ {accessorBody} }}");
    }

    private string EmitParameterList(ParameterListNode? paramList)
    {
        if (paramList is null) return "";

        // The Storm generator only stores the first parameter as a named property;
        // additional comma-separated parameters are parsed but discarded from the AST.
        // Use the source span to faithfully reproduce the full parameter list,
        // but only if no parameter has a void type (which needs MistVoid substitution).
        var span = paramList.Span;
        if (span.Length > 0 && span.Start + span.Length <= _sourceText.Contents.Length)
        {
            // Check if any parameter uses void type — if so, we must emit manually
            // to apply the void → MistVoid transformation.
            if (paramList.Parameter.Type is not VoidTypeRefTypeReferenceNode)
            {
                return _sourceText.Contents.Substring(span.Start, span.Length);
            }
        }

        // Fallback: emit the single parameter we do have.
        return EmitParameter(paramList.Parameter);
    }

    private string EmitParameter(ParameterNode param)
    {
        var type = EmitTypeReference(param.Type, isValueContext: true);
        var name = param.Name.Lexeme;
        return $"{type} {name}";
    }

    private string EmitArgumentList(ArgumentListNode? argList)
    {
        if (argList is null) return "";

        // The Storm generator only stores the first expression as a named property;
        // additional comma-separated arguments are parsed but discarded from the AST.
        // Use the source span to faithfully reproduce the full argument list,
        // applying Void.Void → MistVoid.Instance substitution if needed.
        var span = argList.Span;
        if (span.Length > 0 && span.Start + span.Length <= _sourceText.Contents.Length)
        {
            var rawText = _sourceText.Contents.Substring(span.Start, span.Length);
            if (rawText.Contains("Void.Void"))
            {
                _needsMistVoidStruct = true;
                return rawText.Replace("Void.Void", "MistVoid.Instance");
            }
            return rawText;
        }

        // Fallback: emit the single argument we do have.
        return EmitExpression(argList.Expression);
    }

    private string EmitTypeReference(TypeReferenceNode typeRef, bool isValueContext = false)
    {
        switch (typeRef)
        {
            case PrimitiveTypeRefTypeReferenceNode primitiveRef:
            {
                var keyword = EmitPrimitiveType(primitiveRef.PrimitiveType);
                if (primitiveRef.ArrayRank.HasValue)
                {
                    return $"{keyword}[]";
                }
                return keyword;
            }
            case VoidTypeRefTypeReferenceNode:
                if (isValueContext)
                {
                    _needsMistVoidStruct = true;
                    return "MistVoid";
                }
                return "void";
            case NamedTypeRefTypeReferenceNode namedRef:
            {
                var name = ExtractQualifiedName(namedRef.Name);

                // Handle "Void" identifier as equivalent to the void keyword.
                if (name == "Void" && namedRef.TypeArgs is null && !namedRef.ArrayRank.HasValue)
                {
                    if (isValueContext)
                    {
                        _needsMistVoidStruct = true;
                        return "MistVoid";
                    }
                    return "void";
                }

                var typeArgs = EmitTypeArgumentList(namedRef.TypeArgs);
                if (namedRef.ArrayRank.HasValue)
                {
                    return $"{name}{typeArgs}[]";
                }
                return $"{name}{typeArgs}";
            }
            default:
                return "object";
        }
    }

    private static string EmitPrimitiveType(PrimitiveTypeNode primitiveType)
    {
        return primitiveType switch
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
            _ => "object",
        };
    }

    private void EmitBlock(BlockNode block)
    {
        AppendLine("{");
        _indentLevel++;

        foreach (var stmt in block.Statements)
        {
            EmitStatement(stmt);
        }

        _indentLevel--;
        AppendLine("}");
    }

    private void EmitStatement(StatementNode stmt)
    {
        switch (stmt)
        {
            case VariableDeclarationStatementStatementNode varDeclStmt:
                EmitVariableDeclaration(varDeclStmt.VariableDeclaration);
                break;
            case ReturnStatementStatementNode returnStmt:
                EmitReturnStatement(returnStmt.ReturnStatement);
                break;
            case IfStatementStatementNode ifStmt:
                EmitIfStatement(ifStmt.IfStatement);
                break;
            case WhileStatementStatementNode whileStmt:
                EmitWhileStatement(whileStmt.WhileStatement);
                break;
            case ForStatementStatementNode forStmt:
                EmitForStatement(forStmt.ForStatement);
                break;
            case ForeachStatementStatementNode foreachStmt:
                EmitForeachStatement(foreachStmt.ForeachStatement);
                break;
            case BreakStatementStatementNode:
                AppendLine("break;");
                break;
            case ExpressionStatementStatementNode exprStmt:
                AppendLine($"{EmitExpression(exprStmt.ExpressionStatement.Expression)};");
                break;
        }
    }

    private void EmitVariableDeclaration(VariableDeclarationStatementNode varDecl)
    {
        switch (varDecl)
        {
            case TypedVariableDeclarationVariableDeclarationStatementNode typed:
            {
                var type = EmitTypeReference(typed.Type, isValueContext: true);
                var name = typed.Name.Lexeme;
                var init = EmitExpression(typed.Initializer);
                AppendLine($"{type} {name} = {init};");
                break;
            }
            case InferredVariableDeclarationVariableDeclarationStatementNode inferred:
            {
                var name = inferred.Name.Lexeme;
                var init = EmitExpression(inferred.Initializer);
                AppendLine($"var {name} = {init};");
                break;
            }
        }
    }

    private void EmitReturnStatement(ReturnStatementNode returnStmt)
    {
        if (returnStmt.Value is not null)
        {
            AppendLine($"return {EmitExpression(returnStmt.Value)};");
        }
        else if (_inValueReturningVoidMethod)
        {
            // In value-returning void methods, `return;` becomes `return MistVoid.Instance;`
            AppendLine("return MistVoid.Instance;");
        }
        else
        {
            AppendLine("return;");
        }
    }

    private void EmitIfStatement(IfStatementNode ifStmt)
    {
        var condition = EmitExpression(ifStmt.Condition);
        AppendLine($"if ({condition})");
        EmitBlock(ifStmt.ThenBranch);

        if (ifStmt.ElseBranch is not null)
        {
            EmitElseClause(ifStmt.ElseBranch);
        }
    }

    private void EmitElseClause(ElseClauseNode elseClause)
    {
        switch (elseClause)
        {
            case ElseIfElseClauseNode elseIf:
            {
                var condition = EmitExpression(elseIf.ElseIf.Condition);
                AppendLine($"else if ({condition})");
                EmitBlock(elseIf.ElseIf.ThenBranch);
                if (elseIf.ElseIf.ElseBranch is not null)
                {
                    EmitElseClause(elseIf.ElseIf.ElseBranch);
                }
                break;
            }
            case ElseBlockElseClauseNode elseBlock:
                AppendLine("else");
                EmitBlock(elseBlock.ElseBlock);
                break;
        }
    }

    private void EmitWhileStatement(WhileStatementNode whileStmt)
    {
        var condition = EmitExpression(whileStmt.Condition);
        AppendLine($"while ({condition})");
        EmitBlock(whileStmt.Body);
    }

    private void EmitForStatement(ForStatementNode forStmt)
    {
        var init = EmitForInitializer(forStmt.Initializer);
        var condition = forStmt.Condition is not null ? EmitExpression(forStmt.Condition) : "";
        var iterator = forStmt.Iterator is not null ? EmitExpression(forStmt.Iterator) : "";

        AppendLine($"for ({init}; {condition}; {iterator})");
        EmitBlock(forStmt.Body);
    }

    private string EmitForInitializer(ForInitializerNode? initializer)
    {
        if (initializer is null) return "";

        switch (initializer)
        {
            case ForInitializerVariableDeclarationForInitializerNode varDecl:
            {
                var type = EmitTypeReference(varDecl.VariableDeclaration, isValueContext: true);
                var name = varDecl.Name.Lexeme;
                var init = EmitExpression(varDecl.Initializer);
                return $"{type} {name} = {init}";
            }
            case ForInitializerExpressionForInitializerNode exprInit:
                return EmitExpression(exprInit.Expression);
            default:
                return "";
        }
    }

    private void EmitForeachStatement(ForeachStatementNode foreachStmt)
    {
        var type = EmitTypeReference(foreachStmt.Type, isValueContext: true);
        var name = foreachStmt.Name.Lexeme;
        var collection = EmitExpression(foreachStmt.Collection);

        AppendLine($"foreach ({type} {name} in {collection})");
        EmitBlock(foreachStmt.Body);
    }

    // ─── Expression Emission ─────────────────────────────────────────────

    private string EmitExpression(ExpressionNode expr)
    {
        return EmitAssignment(expr.Assignment);
    }

    private string EmitAssignment(AssignmentNode assignment)
    {
        var target = EmitLogicalOr(assignment.Target);

        if (assignment.Value is not null)
        {
            var value = EmitAssignment(assignment.Value);
            return $"{target} = {value}";
        }

        return target;
    }

    private string EmitLogicalOr(LogicalOrNode logicalOr)
    {
        var left = EmitLogicalAnd(logicalOr.Left);

        if (logicalOr.Right is null || ReferenceEquals(logicalOr.Right, logicalOr.Left))
            return left;

        var right = EmitLogicalAnd(logicalOr.Right);
        return EmitBinaryOperator(logicalOr, "||", left, right);
    }

    private string EmitLogicalAnd(LogicalAndNode logicalAnd)
    {
        var left = EmitEquality(logicalAnd.Left);

        if (logicalAnd.Right is null || ReferenceEquals(logicalAnd.Right, logicalAnd.Left))
            return left;

        var right = EmitEquality(logicalAnd.Right);
        return EmitBinaryOperator(logicalAnd, "&&", left, right);
    }

    private string EmitEquality(EqualityNode equality)
    {
        var left = EmitRelational(equality.Left);

        if (equality.Right is null || ReferenceEquals(equality.Right, equality.Left))
            return left;

        var op = equality.Operator.Lexeme;
        var right = EmitRelational(equality.Right);
        return EmitBinaryOperator(equality, op, left, right);
    }

    private string EmitRelational(RelationalNode relational)
    {
        var left = EmitAdditive(relational.Left);

        if (relational.Right is null || ReferenceEquals(relational.Right, relational.Left))
            return left;

        var op = relational.Operator.Lexeme;
        var right = EmitAdditive(relational.Right);
        return EmitBinaryOperator(relational, op, left, right);
    }

    private string EmitAdditive(AdditiveNode additive)
    {
        var left = EmitMultiplicative(additive.Left);

        if (additive.Right is null || ReferenceEquals(additive.Right, additive.Left))
            return left;

        var op = additive.Operator.Lexeme;
        var right = EmitMultiplicative(additive.Right);
        return EmitBinaryOperator(additive, op, left, right);
    }

    private string EmitMultiplicative(MultiplicativeNode multiplicative)
    {
        var left = EmitUnary(multiplicative.Left);

        if (multiplicative.Right is null || ReferenceEquals(multiplicative.Right, multiplicative.Left))
            return left;

        var op = multiplicative.Operator.Lexeme;
        var right = EmitUnary(multiplicative.Right);
        return EmitBinaryOperator(multiplicative, op, left, right);
    }

    private string EmitUnary(UnaryNode unary)
    {
        switch (unary)
        {
            case LogicalNotUnaryNode logicalNot:
            {
                var operand = EmitUnary(logicalNot.Operand);
                return EmitUnaryOperator(logicalNot, "!", operand);
            }
            case NegateUnaryNode negate:
            {
                var operand = EmitUnary(negate.Operand);
                return EmitUnaryOperator(negate, "-", operand);
            }
            case PostfixExpressionUnaryNode postfix:
                return EmitPostfix(postfix.Primary);
            default:
                return "";
        }
    }

    // ── Operator dispatch helpers ────────────────────────────────────────

    /// <summary>
    /// Dispatches a binary operator expression through the resolved-operator symbol.
    /// If the resolved operator is intrinsic, emits via the emission template registry.
    /// If the resolved operator is user-defined, emits as a static method call.
    /// If no resolved operator is found, falls back to direct operator emission.
    /// </summary>
    private string EmitBinaryOperator(Storm.Runtime.SyntaxNode node, string op, string left, string right)
    {
        var resolved = _model.GetResolvedOperator(node);

        if (resolved is null)
        {
            // No resolved operator — backward compatibility fallback.
            return $"{left} {op} {right}";
        }

        if (resolved is Symbol { IsIntrinsic: true } intrinsicSymbol)
        {
            // Intrinsic operator: look up the emission template.
            var declaringType = GetQualifiedTypeName(intrinsicSymbol);
            var template = _intrinsicRegistry.TryGetBinaryOperator(declaringType, op);
            if (template is not null)
                return string.Format(template.Template, left, right);

            // Template not found — emit MIST_BE_0002 hard error (Requirements 5.9, 5.10).
            var memberSig = $"{declaringType}.operator{op}";
            _diagnostics.Add(new Diagnostic(
                DiagnosticId.MissingIntrinsicEmissionTemplate,
                DiagnosticSeverity.Error,
                PipelineStage.Backend,
                $"No emission template found for intrinsic '{memberSig}' in backend 'CSharp'.",
                Location: null,
                CompilationUnitPath: _sourceText.Path,
                AnalyzerName: null));
            return "";
        }

        if (resolved is Symbol nonIntrinsicSymbol)
        {
            // User-defined operator: emit as a static method call.
            var declaringType = GetEmittedDeclaringTypeName(nonIntrinsicSymbol);
            var methodName = CSharpOperatorName(op);
            return $"{declaringType}.{methodName}({left}, {right})";
        }

        // Fallback for any other ISymbol implementation.
        return $"{left} {op} {right}";
    }

    /// <summary>
    /// Dispatches a unary operator expression through the resolved-operator symbol.
    /// If the resolved operator is intrinsic, emits via the emission template registry.
    /// If the resolved operator is user-defined, emits as a static method call.
    /// If no resolved operator is found, falls back to direct operator emission.
    /// </summary>
    private string EmitUnaryOperator(Storm.Runtime.SyntaxNode node, string op, string operand)
    {
        var resolved = _model.GetResolvedOperator(node);

        if (resolved is null)
        {
            // No resolved operator — backward compatibility fallback.
            return op == "!" ? $"!{operand}" : $"-{operand}";
        }

        if (resolved is Symbol { IsIntrinsic: true } intrinsicSymbol)
        {
            // Intrinsic operator: look up the emission template.
            var declaringType = GetQualifiedTypeName(intrinsicSymbol);
            var template = _intrinsicRegistry.TryGetUnaryOperator(declaringType, op);
            if (template is not null)
                return string.Format(template.Template, operand);

            // Template not found — emit MIST_BE_0002 hard error (Requirements 5.9, 5.10).
            var memberSig = $"{declaringType}.operator{op}";
            _diagnostics.Add(new Diagnostic(
                DiagnosticId.MissingIntrinsicEmissionTemplate,
                DiagnosticSeverity.Error,
                PipelineStage.Backend,
                $"No emission template found for intrinsic '{memberSig}' in backend 'CSharp'.",
                Location: null,
                CompilationUnitPath: _sourceText.Path,
                AnalyzerName: null));
            return "";
        }

        if (resolved is Symbol nonIntrinsicSymbol)
        {
            // User-defined operator: emit as a static method call.
            var declaringType = GetEmittedDeclaringTypeName(nonIntrinsicSymbol);
            var methodName = CSharpOperatorName(op);
            return $"{declaringType}.{methodName}({operand})";
        }

        return op == "!" ? $"!{operand}" : $"-{operand}";
    }

    /// <summary>
    /// Returns the fully-qualified Mist type name for an operator symbol's declaring type.
    /// Used for looking up intrinsic emission templates in the registry.
    /// E.g., for <c>Standard.Int32.operator+</c>, returns <c>"Standard.Int32"</c>.
    /// </summary>
    private static string GetQualifiedTypeName(Symbol operatorSymbol)
    {
        // The operator symbol's ContainingScope is the type's member scope.
        // Walk up to find the type symbol in the parent scope.
        var memberScope = operatorSymbol.ContainingScope;
        if (memberScope?.Parent is not null)
        {
            // Look for a type symbol whose MemberScope is the operator's ContainingScope.
            foreach (var sibling in memberScope.Parent.Members)
            {
                if (sibling.DeclaredType is ClassType ct && ct.MemberScope == memberScope)
                {
                    return ct.ContainingNamespace is not null
                        ? $"{ct.ContainingNamespace}.{ct.DisplayName}"
                        : ct.DisplayName;
                }

                if (sibling.DeclaredType is StructType st && st.MemberScope == memberScope)
                {
                    return st.ContainingNamespace is not null
                        ? $"{st.ContainingNamespace}.{st.DisplayName}"
                        : st.DisplayName;
                }
            }
        }

        // Fallback: extract from the symbol name.
        return "Unknown";
    }

    /// <summary>
    /// Returns the C# emitted type name for a user-defined operator's declaring type.
    /// For user types, this is the simple DisplayName (e.g., "Vector3").
    /// For stdlib types, this goes through the Backing_Type_Map.
    /// </summary>
    private static string GetEmittedDeclaringTypeName(Symbol operatorSymbol)
    {
        var memberScope = operatorSymbol.ContainingScope;
        if (memberScope?.Parent is not null)
        {
            foreach (var sibling in memberScope.Parent.Members)
            {
                if (sibling.DeclaredType is ClassType ct && ct.MemberScope == memberScope)
                    return CSharpTypeMap.GetTypeName(ct);

                if (sibling.DeclaredType is StructType st && st.MemberScope == memberScope)
                    return CSharpTypeMap.GetTypeName(st);
            }
        }

        return "Unknown";
    }

    /// <summary>
    /// Maps a Mist operator symbol to its C# operator method name per the
    /// .NET Common Language Specification naming convention.
    /// </summary>
    private static string CSharpOperatorName(string op) => op switch
    {
        "+" => "op_Addition",
        "-" => "op_Subtraction",
        "*" => "op_Multiply",
        "/" => "op_Division",
        "%" => "op_Modulus",
        "==" => "op_Equality",
        "!=" => "op_Inequality",
        "<" => "op_LessThan",
        ">" => "op_GreaterThan",
        "<=" => "op_LessThanOrEqual",
        ">=" => "op_GreaterThanOrEqual",
        "&&" => "op_LogicalAnd",
        "||" => "op_LogicalOr",
        "!" => "op_LogicalNot",
        _ => $"op_Unknown_{op}",
    };

    private string EmitPostfix(PostfixNode postfix)
    {
        var result = EmitPrimary(postfix.Target);

        foreach (var suffix in postfix.Suffix)
        {
            result = EmitPostfixSuffix(result, suffix);
        }

        return result;
    }

    /// <summary>
    /// Members of ambient types that are properties in C# and should be emitted
    /// without parentheses, even though Mist models them as method calls.
    /// </summary>
    private static readonly HashSet<string> CSharpPropertyMembers = new(StringComparer.Ordinal)
    {
        "Length", // string.Length, Array.Length
    };

    private string EmitPostfixSuffix(string target, PostfixSuffixNode suffix)
    {
        switch (suffix)
        {
            case MethodCallSuffixPostfixSuffixNode methodCall:
            {
                var name = methodCall.Name.Lexeme;
                var typeArgs = EmitTypeArgumentList(methodCall.TypeArgs);

                // In Mist, property-like members (e.g., string.Length) use method call
                // syntax: input.Length(). In C#, these are properties and must be emitted
                // without parentheses: input.Length.
                if (methodCall.Arguments is null && methodCall.TypeArgs is null && CSharpPropertyMembers.Contains(name))
                {
                    return $"{target}.{name}";
                }

                // Check if this method call actually resolves to a property symbol —
                // if so, emit as property access (no parentheses).
                if (methodCall.Arguments is null && methodCall.TypeArgs is null)
                {
                    var resolvedSymbol = TryResolveToPropertySymbol(suffix);
                    if (resolvedSymbol is not null)
                    {
                        return $"{target}.{name}";
                    }
                }

                var args = EmitArgumentList(methodCall.Arguments);
                return $"{target}.{name}{typeArgs}({args})";
            }
            case MemberAccessSuffixPostfixSuffixNode memberAccess:
            {
                // Special case: Void.Void → MistVoid.Instance
                if (target == "Void" && memberAccess.Name.Lexeme == "Void")
                {
                    _needsMistVoidStruct = true;
                    return "MistVoid.Instance";
                }

                // MemberAccessSuffix always emits as property access (no parentheses).
                // This is the correct form for both field access and property access in C#.
                var name = memberAccess.Name.Lexeme;
                return $"{target}.{name}";
            }
            case IndexSuffixPostfixSuffixNode indexSuffix:
            {
                var index = EmitExpression(indexSuffix.Index);
                return $"{target}[{index}]";
            }
            case InvocationSuffixPostfixSuffixNode invocation:
            {
                var args = EmitArgumentList(invocation.Arguments);
                return $"{target}({args})";
            }
            default:
                return target;
        }
    }

    /// <summary>
    /// Attempts to resolve a postfix suffix node to a property symbol via the semantic model.
    /// Returns the symbol if it is a property, null otherwise.
    /// </summary>
    private ISymbol? TryResolveToPropertySymbol(PostfixSuffixNode suffix)
    {
        // The semantic model stores type information keyed by AST node.
        // We check if the resolved type's source symbol is a property.
        var declaredSymbol = _model.GetDeclaredSymbol(suffix);
        if (declaredSymbol is not null && declaredSymbol.Kind == SymbolKind.Property)
        {
            return declaredSymbol;
        }
        return null;
    }

    private string EmitPrimary(PrimaryNode primary)
    {
        switch (primary)
        {
            case IntegerLiteralExpressionPrimaryNode intLit:
                return intLit.IntegerLiteral.Lexeme;
            case HexLiteralExpressionPrimaryNode hexLit:
                return hexLit.HexLiteral.Lexeme;
            case FloatLiteralExpressionPrimaryNode floatLit:
                return floatLit.FloatLiteral.Lexeme;
            case StringLiteralExpressionPrimaryNode stringLit:
                return stringLit.StringLiteral.Lexeme;
            case InterpolatedStringExpressionPrimaryNode interpolated:
                return EmitInterpolatedString(interpolated.InterpolatedString);
            case CharLiteralExpressionPrimaryNode charLit:
                return charLit.CharLiteral.Lexeme;
            case TrueLiteralExpressionPrimaryNode:
                return "true";
            case FalseLiteralExpressionPrimaryNode:
                return "false";
            case ThisExpressionPrimaryNode:
                return "this";
            case ArrayCreationExpressionPrimaryNode arrayCreation:
            {
                var type = EmitTypeReference(arrayCreation.Type);
                var size = EmitExpression(arrayCreation.Size);
                return $"new {type}[{size}]";
            }
            case ObjectCreationExpressionPrimaryNode objCreation:
            {
                var type = ExtractQualifiedName(objCreation.Type);
                var typeArgs = EmitTypeArgumentList(objCreation.TypeArgs);
                var args = EmitArgumentList(objCreation.Arguments);
                return $"new {type}{typeArgs}({args})";
            }
            case ParenthesizedExpressionPrimaryNode paren:
                return $"({EmitExpression(paren.Inner)})";
            case IdentifierExpressionPrimaryNode ident:
                return ident.Name.Lexeme;
            case FieldKeywordExpressionPrimaryNode:
                return "field";
            case TypeLiteralExpressionPrimaryNode typeLiteral:
                return $"typeof({EmitTypeReference(typeLiteral.TypeRef)})";
            default:
                return "";
        }
    }

    private string EmitInterpolatedString(InterpolatedStringNode interpolated)
    {
        var sb = new StringBuilder();
        sb.Append("$\"");

        foreach (var part in interpolated.Parts)
        {
            switch (part)
            {
                case TextPartInterpolatedPartNode textPart:
                    sb.Append(textPart.Text.Lexeme);
                    break;
                case ExpressionPartInterpolatedPartNode exprPart:
                    sb.Append('{');
                    sb.Append(EmitExpression(exprPart.Expression));
                    sb.Append('}');
                    break;
            }
        }

        sb.Append('"');
        return sb.ToString();
    }

    // ─── Modifier Emission ───────────────────────────────────────────────

    private static string EmitModifiers(ImmutableArray<ModifierNode> modifiers)
    {
        if (modifiers.IsEmpty) return "";

        var parts = new List<string>();
        foreach (var mod in modifiers)
        {
            switch (mod)
            {
                case PublicModifierModifierNode:
                    parts.Add("public");
                    break;
                case PrivateModifierModifierNode:
                    parts.Add("private");
                    break;
                case StaticModifierModifierNode:
                    parts.Add("static");
                    break;
            }
        }

        return string.Join(" ", parts) + " ";
    }

    // ─── Utilities ───────────────────────────────────────────────────────

    private static string EmitTypeParameterList(TypeParameterListNode? typeParams)
    {
        if (typeParams is null) return "";

        var parts = new List<string>();
        parts.Add(typeParams.TypeParameter.Name.Lexeme);
        foreach (var tp in typeParams.TypeParameters)
        {
            parts.Add(tp.Name.Lexeme);
        }
        return $"<{string.Join(", ", parts)}>";
    }

    private string EmitTypeArgumentList(TypeArgumentListNode? typeArgs)
    {
        if (typeArgs is null) return "";

        var parts = new List<string>();
        parts.Add(EmitTypeReference(typeArgs.TypeReference, isValueContext: true));
        foreach (var typeRef in typeArgs.TypeReferences)
        {
            parts.Add(EmitTypeReference(typeRef, isValueContext: true));
        }
        return $"<{string.Join(", ", parts)}>";
    }

    private string EmitBaseTypeRef(BaseTypeRefNode baseTypeRef)
    {
        return baseTypeRef.Name.Lexeme + EmitTypeArgumentList(baseTypeRef.TypeArgs);
    }

    private string EmitBaseListWithTypeArgs(BaseListNode baseList)
    {
        var parts = new List<string>();
        parts.Add(EmitBaseTypeRef(baseList.First));
        foreach (var rest in baseList.BaseTypeRefs)
        {
            parts.Add(EmitBaseTypeRef(rest));
        }
        return string.Join(", ", parts);
    }

    private static string EmitBaseList(BaseListNode baseList)
    {
        var parts = new List<string>();
        parts.Add(baseList.First.Name.Lexeme);
        foreach (var rest in baseList.BaseTypeRefs)
        {
            parts.Add(rest.Name.Lexeme);
        }
        return string.Join(", ", parts);
    }

    private string ExtractQualifiedName(QualifiedIdentifierNode qualifiedId)
    {
        var span = qualifiedId.Span;
        if (span.Length > 0 && span.Start + span.Length <= _sourceText.Contents.Length)
        {
            return _sourceText.Contents.Substring(span.Start, span.Length);
        }

        return qualifiedId.IDENTIFIER.Lexeme;
    }

    private void AppendLine(string text = "")
    {
        if (string.IsNullOrEmpty(text))
        {
            _sb.AppendLine();
        }
        else
        {
            for (var i = 0; i < _indentLevel; i++)
            {
                _sb.Append(IndentUnit);
            }
            _sb.AppendLine(text);
        }
    }
}
