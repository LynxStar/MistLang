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

            // Units from referenced projects are emitted into a
            // .references/{ProjectName}/ subdirectory to avoid filename
            // collisions (e.g., multiple projects having Program.mist).
            string csPath;
            if (unit.OriginProjectPath is not null)
            {
                var refProjectDir = Path.GetDirectoryName(unit.OriginProjectPath)!;
                var refProjectName = Path.GetFileName(refProjectDir);
                var refOutputDir = Path.Combine(outputDirectory, "references", refProjectName);
                Directory.CreateDirectory(refOutputDir);
                csPath = Path.Combine(refOutputDir, csFileName);
            }
            else
            {
                csPath = Path.Combine(outputDirectory, csFileName);
            }

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
              <!--
                Microsoft.CSharp provides the DLR machinery (`System.Runtime.CompilerServices.CSharpArgumentInfo`,
                `System.Runtime.CompilerServices.Binder`, etc.) that `dynamic` dispatch compiles against. The
                Mist backend uses `dynamic` as the C# emission target for `Closed_Generic_With_Unknown` receivers
                (e.g., Mist `List<unknown>` → C# `dynamic`) so that member access on such a receiver routes through
                the DLR at runtime instead of requiring C# generic covariance that the CLR does not provide for
                user-defined generics.
              -->
              <ItemGroup>
                <PackageReference Include="Microsoft.CSharp" Version="4.7.0" />
              </ItemGroup>
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
    /// <summary>
    /// Return type of the enclosing method, used by <see cref="EmitReturnStatement"/>
    /// to detect covariant unknown widening at return sites (Task 31.1).
    /// <c>null</c> outside a method body or when the return type is
    /// unavailable (e.g., void methods where no widening can occur).
    /// </summary>
    private IType? _currentMethodReturnType;
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
        switch (node)
        {
            case RelationalComparisonRelationalNode cmp:
                ScanAdditiveForValueReturningCalls(cmp.Left, voidMethodNames, isValueContext);
                if (cmp.Right is not null && !ReferenceEquals(cmp.Right, cmp.Left))
                    ScanAdditiveForValueReturningCalls(cmp.Right, voidMethodNames, isValueContext);
                break;

            case IsExpressionRelationalNode isExpr:
                // Only scan the left additive — patterns contain no method calls
                ScanAdditiveForValueReturningCalls(isExpr.Left, voidMethodNames, isValueContext);
                break;
        }
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

        // Emit protected parameterless constructor for defaultable classes if one doesn't already exist.
        // This is needed so __CreateDefault() can instantiate the class via `new ClassName()`.
        // The constructor sets the class's own default field values (not inherited ones).
        // When a derived class calls `new Derived()`, the chain `Derived() → base() → Base()`
        // ensures each level initializes its own fields. This avoids accessibility issues
        // with private base fields.
        // Using `protected` (not `private`) so derived classes can call `base()`.
        // Structs don't need this because C# allows `new MyStruct()` without an explicit
        // parameterless constructor.
        var isDefaultableClass = symbol?.DeclaredType is ClassType defaultableClassForCtor
            && defaultableClassForCtor.Defaultability is DefaultabilityStatus.Inferred or DefaultabilityStatus.Explicit
            && defaultableClassForCtor.DefaultValues is not null;
        var emittedDefaultCtor = false;

        if (isDefaultableClass)
        {
            var hasParameterlessCtor = classDecl.Members.Any(m =>
                m is ConstructorMemberMemberDeclarationNode ctorMember
                && ctorMember.Constructor.Parameters is null);

            if (!hasParameterlessCtor)
            {
                emittedDefaultCtor = true;
                var classTypeForCtor = (ClassType)symbol!.DeclaredType!;
                var ownFields = classTypeForCtor.DefaultValues!
                    .Where(kvp => kvp.Value.ValueKind != DefaultValueKind.InheritedDefault)
                    .ToList();

                if (classDecl.Members.Length > 0)
                {
                    AppendLine();
                }

                if (ownFields.Count == 0)
                {
                    AppendLine($"protected {classDecl.Name.Lexeme}() {{ }}");
                }
                else
                {
                    AppendLine($"protected {classDecl.Name.Lexeme}()");
                    AppendLine("{");
                    _indentLevel++;
                    foreach (var (fieldName, fieldValue) in ownFields)
                    {
                        EmitDefaultFieldAssignment("this", fieldName, fieldValue);
                    }
                    _indentLevel--;
                    AppendLine("}");
                }
            }
        }

        // Emit __CreateDefault factory method for defaultable classes.
        // If we emitted our own parameterless constructor that sets own fields,
        // __CreateDefault just creates the instance (constructor chain handles initialization).
        // Otherwise, __CreateDefault sets own fields directly.
        if (symbol?.DeclaredType is ClassType defaultableClass
            && defaultableClass.Defaultability is DefaultabilityStatus.Inferred or DefaultabilityStatus.Explicit
            && defaultableClass.DefaultValues is not null)
        {
            var hasExistingMembers = classDecl.Members.Length > 0 || isDefaultableClass;
            EmitCreateDefaultMethod(defaultableClass.DisplayName, defaultableClass.DefaultValues,
                hasExistingMembers, skipOwnFields: emittedDefaultCtor);
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

        // Skip Standard.Void — its C# representation is the MistVoid helper
        // struct generated by the emitter. The Mist source defines the
        // semantics (private constructor, static property, equality operators)
        // but the C# backend provides its own implementation because C# has
        // restrictions on void-typed members and same-name-as-type members.
        if (symbol?.DeclaredType is StructType voidSt && voidSt.IsVoid()
            && voidSt.ContainingNamespace == "Standard")
        {
            _needsMistVoidStruct = true;
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

        // Emit __CreateDefault factory method for defaultable structs.
        if (symbol?.DeclaredType is StructType defaultableStruct
            && defaultableStruct.Defaultability is DefaultabilityStatus.Inferred or DefaultabilityStatus.Explicit
            && defaultableStruct.DefaultValues is not null)
        {
            var hasExistingMembers = structDecl.Members.Length > 0;
            EmitCreateDefaultMethod(defaultableStruct.DisplayName, defaultableStruct.DefaultValues, hasExistingMembers);
        }

        _indentLevel--;
        AppendLine("}");
    }

    /// <summary>
    /// Emits a <c>public static ClassName __CreateDefault()</c> factory method for a
    /// defaultable type (class or struct). The method creates a new instance, sets each
    /// field to its computed default value, and returns the instance.
    /// </summary>
    private void EmitCreateDefaultMethod(
        string typeName,
        IReadOnlyDictionary<string, DefaultFieldValue> defaultValues,
        bool hasExistingMembers,
        bool skipOwnFields = false)
    {
        if (hasExistingMembers)
        {
            AppendLine();
        }

        AppendLine($"public static {typeName} __CreateDefault()");
        AppendLine("{");
        _indentLevel++;

        AppendLine($"var inst = new {typeName}();");

        foreach (var (fieldName, fieldValue) in defaultValues)
        {
            // Skip inherited fields — they are initialized by the base class's
            // parameterless constructor via the constructor chain (base() call).
            if (fieldValue.ValueKind == DefaultValueKind.InheritedDefault)
                continue;

            // If the emitted parameterless constructor already sets own fields,
            // skip them here (the constructor chain handles everything).
            if (skipOwnFields)
                continue;

            EmitDefaultFieldAssignment("inst", fieldName, fieldValue);
        }

        AppendLine("return inst;");

        _indentLevel--;
        AppendLine("}");
    }

    /// <summary>
    /// Emits a single field assignment within a <c>__CreateDefault</c> factory method body.
    /// Handles primitive literals, nested defaultable types, type bindings, and type bindings
    /// with nested overrides.
    /// </summary>
    private void EmitDefaultFieldAssignment(string instanceName, string fieldName, DefaultFieldValue fieldValue)
    {
        // Auto-properties generate backing fields named _PropertyName_k__BackingField,
        // but the emitted C# uses property syntax (get; set;), so the backing field is
        // not directly accessible. Use the property name for assignment instead.
        fieldName = ResolveFieldOrPropertyName(fieldName);
        switch (fieldValue.ValueKind)
        {
            case DefaultValueKind.TypeBinding:
            {
                // Interface/trait field bound to a concrete type.
                var concreteTypeName = CSharpTypeMap.GetTypeName(fieldValue.ConcreteType!);

                if (fieldValue.NestedOverrides is { Count: > 0 })
                {
                    // Type binding with nested overrides — use a temp variable.
                    var tempName = $"__temp_{fieldName}";
                    AppendLine($"var {tempName} = {concreteTypeName}.__CreateDefault();");
                    foreach (var (nestedFieldName, nestedValue) in fieldValue.NestedOverrides)
                    {
                        EmitDefaultFieldAssignment(tempName, nestedFieldName, nestedValue);
                    }
                    AppendLine($"{instanceName}.{fieldName} = {tempName};");
                }
                else
                {
                    // Type binding without nested overrides — direct call.
                    AppendLine($"{instanceName}.{fieldName} = {concreteTypeName}.__CreateDefault();");
                }
                break;
            }

            case DefaultValueKind.NestedTypeDefault:
            {
                // Field whose type is a defaultable class or struct — recursively call __CreateDefault.
                var nestedTypeName = CSharpTypeMap.GetTypeName(fieldValue.FieldType);
                AppendLine($"{instanceName}.{fieldName} = {nestedTypeName}.__CreateDefault();");
                break;
            }

            default:
            {
                // Primitive, string, field initializer, explicit override, or inherited default —
                // emit the literal constant value directly.
                var literal = EmitDefaultLiteral(fieldValue);
                AppendLine($"{instanceName}.{fieldName} = {literal};");
                break;
            }
        }
    }

    /// <summary>
    /// Returns the C# literal representation of a <see cref="DefaultFieldValue"/>'s constant value.
    /// </summary>
    private static string EmitDefaultLiteral(DefaultFieldValue fieldValue)
    {
        var value = fieldValue.ConstantValue;

        return value switch
        {
            null => "default",
            bool b => b ? "true" : "false",
            int i => i.ToString(),
            uint u => $"{u}u",
            long l => $"{l}L",
            ulong ul => $"{ul}UL",
            short s => s.ToString(),
            ushort us => us.ToString(),
            byte b => b.ToString(),
            sbyte sb => sb.ToString(),
            float f => $"{f}f",
            double d => d.ToString(),
            decimal m => $"{m}m",
            char c => $"'{EscapeChar(c)}'",
            string s => $"\"{EscapeString(s)}\"",
            _ => value.ToString() ?? "default",
        };
    }

    /// <summary>
    /// If <paramref name="fieldName"/> is a compiler-generated backing field
    /// (e.g. <c>_PropertyName_k__BackingField</c>), returns the property name
    /// (<c>PropertyName</c>) so the emitter uses the property setter instead of
    /// the inaccessible backing field. Otherwise returns the name unchanged.
    /// </summary>
    private static string ResolveFieldOrPropertyName(string fieldName)
    {
        const string suffix = "_k__BackingField";
        if (fieldName.StartsWith('_') && fieldName.EndsWith(suffix, StringComparison.Ordinal))
        {
            return fieldName.Substring(1, fieldName.Length - 1 - suffix.Length);
        }
        return fieldName;
    }

    /// <summary>
    /// Escapes a character for use in a C# character literal.
    /// </summary>
    private static string EscapeChar(char c) => c switch
    {
        '\0' => "\\0",
        '\'' => "\\'",
        '\\' => "\\\\",
        '\n' => "\\n",
        '\r' => "\\r",
        '\t' => "\\t",
        _ => c.ToString(),
    };

    /// <summary>
    /// Escapes a string for use in a C# string literal.
    /// </summary>
    private static string EscapeString(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            sb.Append(c switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                '\0' => "\\0",
                _ => c.ToString(),
            });
        }
        return sb.ToString();
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
        // Task 32.2 / Req 15.5 — Best-effort storage skipping. If a field,
        // property, or indexer has a declared type that satisfies
        // `TypeContainment.IsStorageBanned` (i.e., contains `unknown` at any
        // storage position), the semantic model builder has already emitted
        // the appropriate `MIST_SB_005x` diagnostic. The emitter skips
        // generating the C# declaration so that — if downstream tooling
        // invokes the emitter despite errors — the unaffected portions of
        // the program still produce compilable C#. Centralizing the check
        // here keeps the skip logic in a single place rather than threading
        // it through each per-member emission routine.
        if (IsStorageBannedMember(memberDecl))
            return;

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
            case CastOperatorMemberMemberDeclarationNode castOpMember:
                EmitCastOperatorDeclaration(castOpMember.CastOperator);
                break;
            case DefaultBlockMemberMemberDeclarationNode:
                // The default block is consumed by the semantic model and emitter's
                // __CreateDefault generation logic — it is not emitted as a C# member.
                break;
        }
    }

    /// <summary>
    /// Task 32.2 / Req 15.5 — Returns <c>true</c> when <paramref name="member"/>
    /// is a field, property, or indexer declaration whose semantic declared
    /// type is storage-banned (per <see cref="TypeContainment.IsStorageBanned"/>).
    /// The emitter uses this to skip emission of declarations that the
    /// semantic model has already flagged with <c>MIST_SB_0050</c>,
    /// <c>MIST_SB_0051</c>, or <c>MIST_SB_0052</c> — those declarations
    /// would emit as <c>object</c>-typed slots in C#, which is both
    /// redundant with the Mist diagnostic and potentially misleading.
    /// </summary>
    /// <remarks>
    /// The check consults the resolved semantic type via
    /// <see cref="ISemanticModel.GetDeclaredSymbol"/> rather than the AST
    /// <c>TypeReferenceNode</c>, so nested cascades such as
    /// <c>List&lt;unknown&gt;</c> and <c>Map&lt;string, unknown&gt;</c> are
    /// correctly recognized as banned even though their AST form is a
    /// plain <c>ConstructedTypeRefTypeReferenceNode</c>. Constructors,
    /// methods, operators, and cast operators are never storage-banned
    /// — the ban applies only to writable storage slots.
    /// </remarks>
    private bool IsStorageBannedMember(MemberDeclarationNode member)
    {
        IType? declaredType = member switch
        {
            FieldMemberMemberDeclarationNode fm =>
                _model.GetDeclaredSymbol(fm.Field)?.DeclaredType,
            PropertyMemberMemberDeclarationNode pm =>
                _model.GetDeclaredSymbol(pm.Property)?.DeclaredType,
            IndexerMemberMemberDeclarationNode im =>
                _model.GetDeclaredSymbol(im.Indexer)?.DeclaredType,
            _ => null,
        };

        return declaredType is not null
            && TypeContainment.IsStorageBanned(declaredType);
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

    private void EmitCastOperatorDeclaration(CastOperatorDeclarationNode castOpDecl)
    {
        var modifiers = EmitModifiers(castOpDecl.Modifiers);
        var castKind = castOpDecl.CastKind is ImplicitCastCastOperatorKindNode
            ? "implicit" : "explicit";
        var targetType = EmitTypeReference(castOpDecl.TargetType);
        var parameters = EmitParameterList(castOpDecl.Parameters);

        AppendLine($"{modifiers}{castKind} operator {targetType}({parameters})");

        if (castOpDecl.Body is OperatorBlockBodyOperatorBodyNode blockBody)
        {
            EmitBlock(blockBody.Block);
        }
        else
        {
            // Intrinsic — should not reach here for user code.
            AppendLine(";");
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

        // Task 31.1 — track the enclosing method's return type so return
        // statements can detect covariant unknown widening against it.
        var previousReturnType = _currentMethodReturnType;
        _currentMethodReturnType = ResolveMethodReturnType(methodDecl);

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
        _currentMethodReturnType = previousReturnType;
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
                    var value = EmitExpression(returnStmt.ReturnStatement.Value);
                    value = WrapWithImplicitCastIfNeeded(returnStmt.ReturnStatement, value);
                    // Task 31.1 — covariant unknown widening at the return
                    // site against the enclosing method's return type.
                    var valueType = _model.GetTypeOf(returnStmt.ReturnStatement.Value);
                    var widened = TryEmitCovariantUnknownCast(valueType, _currentMethodReturnType, value);
                    if (widened is not null)
                        value = widened;
                    return $"return {value};";
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
                        init = WrapWithImplicitCastIfNeeded(typed.Initializer, init);
                        // Task 31.1 — covariant unknown widening at the
                        // typed local declaration's assignment site.
                        var declaredType = _model.GetDeclaredSymbol(typed)?.DeclaredType;
                        var initType = _model.GetTypeOf(typed.Initializer);
                        var widened = TryEmitCovariantUnknownCast(initType, declaredType, init);
                        if (widened is not null)
                            init = widened;
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
        // but only if no parameter has a type that requires a Mist-to-C# rewrite.
        var span = paramList.Span;
        if (span.Length > 0 && span.Start + span.Length <= _sourceText.Contents.Length)
        {
            var sourceText = _sourceText.Contents.Substring(span.Start, span.Length);

            // Types that need rewriting:
            //   * `void` → `MistVoid` (when in value context).
            //   * `unknown` (bare or nested inside a generic type argument)
            //     → `object` or `dynamic` per the Closed_Generic_With_Unknown
            //     emission rule (Task 30 / Task 31 / Req 15.1, 15.4).
            // We check both the first parameter's AST type (structurally) and
            // the full parameter-list source text (as a lexical fallback) so
            // subsequent parameters whose types contain `unknown` are also
            // caught. A lexical check is safe here because `unknown` is a
            // reserved keyword (not usable as an identifier in Mist grammar),
            // so any word-boundary `unknown` in the parameter-list source is
            // necessarily the keyword that needs rewriting.
            var firstType = paramList.Parameter.Type;
            if (firstType is not VoidTypeRefTypeReferenceNode
                && !TypeReferenceContainsUnknown(firstType)
                && !ContainsVoidOrUnknownKeyword(sourceText))
            {
                return sourceText;
            }
        }

        // Fallback: emit the single parameter we do have.
        return EmitParameter(paramList.Parameter);
    }

    /// <summary>
    /// Returns <c>true</c> if <paramref name="source"/> contains the
    /// Mist keywords <c>unknown</c> or <c>void</c> as standalone
    /// identifiers (matched at word boundaries). Used as a lexical
    /// fallback in <see cref="EmitParameterList"/> for parameter-list
    /// source spans that contain parameters beyond the first, which
    /// the Storm parser does not expose structurally. Both keywords
    /// require emitter-side rewriting (`void` → `MistVoid` in value
    /// contexts, `unknown` → `object`/`dynamic` per the
    /// Closed_Generic_With_Unknown rule), so either match requires
    /// taking the fallback path that routes through
    /// <see cref="EmitTypeReference"/>.
    /// </summary>
    private static bool ContainsVoidOrUnknownKeyword(string source)
    {
        return ContainsWord(source, "unknown") || ContainsWord(source, "void");
    }

    /// <summary>
    /// Word-boundary substring match: returns <c>true</c> if
    /// <paramref name="word"/> appears in <paramref name="source"/>
    /// with non-identifier characters (or start/end of string) on
    /// both sides.
    /// </summary>
    private static bool ContainsWord(string source, string word)
    {
        var index = 0;
        while ((index = source.IndexOf(word, index, StringComparison.Ordinal)) >= 0)
        {
            var before = index == 0
                || !IsIdentifierChar(source[index - 1]);
            var afterPos = index + word.Length;
            var after = afterPos >= source.Length
                || !IsIdentifierChar(source[afterPos]);
            if (before && after)
                return true;
            index = afterPos;
        }
        return false;
    }

    private static bool IsIdentifierChar(char c) =>
        char.IsLetterOrDigit(c) || c == '_';

    /// <summary>
    /// Returns <c>true</c> if <paramref name="typeRef"/> is either
    /// <see cref="UnknownTypeRefTypeReferenceNode"/> directly, or a
    /// <see cref="NamedTypeRefTypeReferenceNode"/> whose type-argument
    /// list transitively contains an <see cref="UnknownTypeRefTypeReferenceNode"/>.
    /// Used by <see cref="EmitParameterList"/> to decide whether the
    /// source-text fast-path is safe; any appearance of <c>unknown</c>
    /// in the type requires the fallback (which routes through
    /// <see cref="EmitTypeReference"/> / <see cref="CSharpTypeMap"/>)
    /// so the Mist keyword is rewritten to the correct C# form
    /// (<c>object</c> or <c>dynamic</c>).
    /// </summary>
    private static bool TypeReferenceContainsUnknown(TypeReferenceNode typeRef)
    {
        switch (typeRef)
        {
            case UnknownTypeRefTypeReferenceNode:
                return true;

            case NamedTypeRefTypeReferenceNode named:
                if (named.TypeArgs is null) return false;
                if (TypeReferenceContainsUnknown(named.TypeArgs.TypeReference))
                    return true;
                foreach (var arg in named.TypeArgs.TypeReferences)
                {
                    if (TypeReferenceContainsUnknown(arg))
                        return true;
                }
                return false;

            default:
                return false;
        }
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

        // Collect all argument expressions from the AST.
        var argExprs = new List<ExpressionNode> { argList.Expression };
        foreach (var child in argList.Children())
        {
            if (child is ExpressionNode expr && !ReferenceEquals(expr, argList.Expression))
                argExprs.Add(expr);
        }

        // Check if any argument has a resolved implicit cast operator.
        var hasImplicitCast = false;
        foreach (var argExpr in argExprs)
        {
            var resolved = _model.GetResolvedOperator(argExpr);
            if (resolved is Symbol sym && sym.Name.StartsWith("operator_implicit_"))
            {
                hasImplicitCast = true;
                break;
            }
        }

        // If any argument needs implicit cast wrapping, emit all arguments
        // through the expression emitter so we can wrap the ones that need it.
        if (hasImplicitCast)
        {
            var emitted = new List<string>();
            foreach (var argExpr in argExprs)
            {
                var emittedArg = EmitExpression(argExpr);
                emitted.Add(WrapWithImplicitCastIfNeeded(argExpr, emittedArg));
            }
            return string.Join(", ", emitted);
        }

        // No implicit casts needed — use the source span for faithful reproduction,
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
            case UnknownTypeRefTypeReferenceNode:
                // User-facing `unknown` (Task 30.1 / Req 13.5, 15.1, 15.4):
                // emit C# `object` at every syntactic type-reference position.
                // This is the emission partner to `CSharpTypeMap.GetTypeName`'s
                // `UnknownType => "object"` rule — the type-map path is used
                // when the emitter looks up a semantic IType, and this AST
                // path is used when the emitter walks a source `TypeReference`
                // node directly (for example, in a cast expression
                // `(unknown) expr`, which `EmitCastExpression` lowers to
                // `((object) <operand>)` without any operator dispatch).
                return "object";
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

                // Closed_Generic_With_Unknown emission (Task 31 revised,
                // Req 15.1/15.2): any named type whose type-argument list
                // transitively contains `unknown` lowers to C# `dynamic`.
                // This mirrors `CSharpTypeMap.GetTypeName`'s handling on
                // the semantic-model path and ensures the AST-walk path
                // produces the same C# spelling. `dynamic` routes member
                // access through the DLR, which dispatches against the
                // receiver's actual runtime generic type (e.g., the
                // underlying `List<int>` object). Without this, the C#
                // compiler would reject `List<unknown>` verbatim and any
                // naive `List<object>` lowering would throw
                // `InvalidCastException` at runtime due to C# generic
                // invariance.
                if (namedRef.TypeArgs is not null
                    && TypeReferenceContainsUnknown(namedRef))
                {
                    return namedRef.ArrayRank.HasValue ? "dynamic[]" : "dynamic";
                }

                var typeArgs = EmitTypeArgumentList(namedRef.TypeArgs);
                if (namedRef.ArrayRank.HasValue)
                {
                    return $"{name}{typeArgs}[]";
                }
                return $"{name}{typeArgs}";
            }
            case TupleTypeRefTypeReferenceNode tupleRef:
            {
                // Emit C# tuple type syntax: (T1, T2) or (T1 name1, T2 name2).
                var parts = new List<string>();

                string FormatTupleElement(TupleTypeElementNode element)
                {
                    var type = EmitTypeReference(element.Type, isValueContext);
                    return element.Name.HasValue ? $"{type} {element.Name.Value.Lexeme}" : type;
                }

                parts.Add(FormatTupleElement(tupleRef.First));
                parts.Add(FormatTupleElement(tupleRef.Rest));
                foreach (var additional in tupleRef.TupleTypeElements)
                {
                    parts.Add(FormatTupleElement(additional));
                }

                return $"({string.Join(", ", parts)})";
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
                init = WrapWithImplicitCastIfNeeded(typed.Initializer, init);
                // Task 31.1 — covariant unknown widening at the typed local
                // declaration's assignment site. The declared type is
                // recorded on the local variable's symbol; the initializer
                // expression type comes from the expression-type map.
                var declaredType = _model.GetDeclaredSymbol(typed)?.DeclaredType;
                var initType = _model.GetTypeOf(typed.Initializer);
                var widened = TryEmitCovariantUnknownCast(initType, declaredType, init);
                if (widened is not null)
                    init = widened;
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
            case VarTupleDeconstructionVariableDeclarationStatementNode varTuple:
            {
                // Emit: var (a, b, ...) = expr;
                var names = new List<string> { varTuple.FirstName.Lexeme };
                foreach (var restName in varTuple.RestNames)
                {
                    names.Add(restName.Lexeme);
                }
                var init = EmitExpression(varTuple.Initializer);
                AppendLine($"var ({string.Join(", ", names)}) = {init};");
                break;
            }
            case TypedTupleDeconstructionVariableDeclarationStatementNode typedTuple:
            {
                // Emit: (Type1 a, Type2 b, ...) = expr;
                var parts = new List<string>();

                string FormatDeconstructionElement(TupleDeconstructionElementNode element)
                {
                    var type = EmitTypeReference(element.Type, isValueContext: true);
                    return $"{type} {element.Name.Lexeme}";
                }

                parts.Add(FormatDeconstructionElement(typedTuple.First));
                parts.Add(FormatDeconstructionElement(typedTuple.Rest));
                foreach (var additional in typedTuple.TupleDeconstructionElements)
                {
                    parts.Add(FormatDeconstructionElement(additional));
                }

                var init = EmitExpression(typedTuple.Initializer);
                AppendLine($"({string.Join(", ", parts)}) = {init};");
                break;
            }
        }
    }

    private void EmitReturnStatement(ReturnStatementNode returnStmt)
    {
        if (returnStmt.Value is not null)
        {
            var value = EmitExpression(returnStmt.Value);
            value = WrapWithImplicitCastIfNeeded(returnStmt, value);
            // Task 31.1 — covariant unknown widening at the return site
            // against the enclosing method's return type.
            var valueType = _model.GetTypeOf(returnStmt.Value);
            var widened = TryEmitCovariantUnknownCast(valueType, _currentMethodReturnType, value);
            if (widened is not null)
                value = widened;
            AppendLine($"return {value};");
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
            value = WrapWithImplicitCastIfNeeded(assignment, value);
            // Task 31.1 — covariant unknown widening at the assignment
            // site against the target's static type.
            var targetType = _model.GetTypeOf(assignment.Target);
            var valueType = _model.GetTypeOf(assignment.Value);
            var widened = TryEmitCovariantUnknownCast(valueType, targetType, value);
            if (widened is not null)
                value = widened;
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
        switch (relational)
        {
            case RelationalComparisonRelationalNode cmp:
            {
                var left = EmitAdditive(cmp.Left);

                if (cmp.Right is null || ReferenceEquals(cmp.Right, cmp.Left))
                    return left;

                var op = cmp.Operator.Lexeme;
                var right = EmitAdditive(cmp.Right);
                return EmitBinaryOperator(cmp, op, left, right);
            }

            case IsExpressionRelationalNode isExpr:
            {
                var left = EmitAdditive(isExpr.Left);
                var pattern = EmitPattern(isExpr.Pattern);
                return $"{left} is {pattern}";
            }

            default:
                return "";
        }
    }

    // ─── Pattern Emission ────────────────────────────────────────────────

    private string EmitPattern(PatternNode pattern)
    {
        return EmitOrPattern(pattern.OrPattern);
    }

    private string EmitOrPattern(OrPatternNode orPattern)
    {
        var left = EmitAndPattern(orPattern.Left);

        if (orPattern.Right is null || ReferenceEquals(orPattern.Right, orPattern.Left))
            return left;

        var right = EmitAndPattern(orPattern.Right);
        return $"{left} or {right}";
    }

    private string EmitAndPattern(AndPatternNode andPattern)
    {
        var left = EmitUnaryPattern(andPattern.Left);

        if (andPattern.Right is null || ReferenceEquals(andPattern.Right, andPattern.Left))
            return left;

        var right = EmitUnaryPattern(andPattern.Right);
        return $"{left} and {right}";
    }

    private string EmitUnaryPattern(UnaryPatternNode unaryPattern)
    {
        switch (unaryPattern)
        {
            case NotPatternUnaryPatternNode notPattern:
                return $"not {EmitPrimaryPattern(notPattern.Operand)}";
            case DirectPatternUnaryPatternNode directPattern:
                return EmitPrimaryPattern(directPattern.Primary);
            default:
                return "";
        }
    }

    private string EmitPrimaryPattern(PrimaryPatternNode primaryPattern)
    {
        switch (primaryPattern)
        {
            case TypePatternPrimaryPatternNode typePattern:
            {
                var type = EmitTypeReference(typePattern.Type);
                var name = typePattern.Name.Lexeme;
                return $"{type} {name}";
            }
            case VarPatternPrimaryPatternNode varPattern:
                return $"var {varPattern.Name.Lexeme}";
            case ConstantPatternPrimaryPatternNode constantPattern:
                return EmitConstantLiteral(constantPattern.Literal);
            case LessThanRelationalPatternPrimaryPatternNode ltPattern:
                return $"< {EmitExpression(ltPattern.Constant)}";
            case GreaterThanRelationalPatternPrimaryPatternNode gtPattern:
                return $"> {EmitExpression(gtPattern.Constant)}";
            case LessThanOrEqualRelationalPatternPrimaryPatternNode ltePattern:
                return $"<= {EmitExpression(ltePattern.Constant)}";
            case GreaterThanOrEqualRelationalPatternPrimaryPatternNode gtePattern:
                return $">= {EmitExpression(gtePattern.Constant)}";
            case PropertyPatternPrimaryPatternNode propertyPattern:
            {
                var sb = new StringBuilder();

                if (propertyPattern.TypeName is not null)
                {
                    sb.Append(ExtractQualifiedName(propertyPattern.TypeName));
                    sb.Append(' ');
                }

                sb.Append("{ ");
                if (propertyPattern.Subpatterns is not null)
                {
                    sb.Append(EmitPropertySubpatternList(propertyPattern.Subpatterns));
                    sb.Append(' ');
                }
                sb.Append('}');

                if (propertyPattern.Name.HasValue)
                {
                    sb.Append(' ');
                    sb.Append(propertyPattern.Name.Value.Lexeme);
                }

                return sb.ToString();
            }
            case ListPatternPrimaryPatternNode listPattern:
            {
                if (listPattern.Elements is null)
                    return "[]";

                return $"[{EmitListPatternElementList(listPattern.Elements)}]";
            }
            case ParenthesizedPatternPrimaryPatternNode parenPattern:
                return $"({EmitPattern(parenPattern.Inner)})";
            default:
                return "";
        }
    }

    private static string EmitConstantLiteral(ConstantLiteralNode literal)
    {
        return literal switch
        {
            IntegerConstantConstantLiteralNode intConst => intConst.Value.Lexeme,
            FloatConstantConstantLiteralNode floatConst => floatConst.Value.Lexeme,
            StringConstantConstantLiteralNode strConst => strConst.Value.Lexeme,
            CharConstantConstantLiteralNode charConst => charConst.Value.Lexeme,
            TrueConstantConstantLiteralNode => "true",
            FalseConstantConstantLiteralNode => "false",
            _ => "",
        };
    }

    private string EmitPropertySubpatternList(PropertySubpatternListNode subpatternList)
    {
        var parts = new List<string>();
        parts.Add(EmitPropertySubpattern(subpatternList.PropertySubpattern));

        foreach (var subpattern in subpatternList.PropertySubpatterns)
        {
            parts.Add(EmitPropertySubpattern(subpattern));
        }

        return string.Join(", ", parts);
    }

    private string EmitPropertySubpattern(PropertySubpatternNode subpattern)
    {
        var name = subpattern.Name.Lexeme;
        var pattern = EmitPattern(subpattern.Pattern);
        return $"{name}: {pattern}";
    }

    private string EmitListPatternElementList(ListPatternElementListNode elementList)
    {
        var parts = new List<string>();
        parts.Add(EmitListPatternElement(elementList.ListPatternElement));

        foreach (var element in elementList.ListPatternElements)
        {
            parts.Add(EmitListPatternElement(element));
        }

        return string.Join(", ", parts);
    }

    private string EmitListPatternElement(ListPatternElementNode element)
    {
        switch (element)
        {
            case PatternElementListPatternElementNode patternElement:
                return EmitPattern(patternElement.Pattern);
            case SliceElementListPatternElementNode sliceElement:
            {
                // C# requires `.. var name` for slice variables (Mist uses `.. name`)
                if (sliceElement.Name.HasValue)
                    return $".. var {sliceElement.Name.Value.Lexeme}";
                return "..";
            }
            default:
                return "";
        }
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
            case CastExpressionUnaryNode castExpr:
                return EmitCastExpression(castExpr);
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

    /// <summary>
    /// Emits a cast expression. For user-defined cast operators, emits as a standard
    /// C# cast expression <c>((TargetType)operand)</c> which lets the C# compiler
    /// invoke the user-defined implicit/explicit operator. For intrinsic operators,
    /// looks up the emission template. For identity/subtype casts (no resolved
    /// operator), emits a standard C# cast <c>((TargetType)expr)</c>.
    /// </summary>
    private string EmitCastExpression(CastExpressionUnaryNode castExpr)
    {
        var targetType = EmitTypeReference(castExpr.TargetType);
        var operand = EmitUnary(castExpr.Operand);

        var resolved = _model.GetResolvedOperator(castExpr);

        if (resolved is null)
        {
            // Identity/subtype cast — emit standard C# cast.
            return $"(({targetType}){operand})";
        }

        if (resolved is Symbol { IsIntrinsic: true } intrinsicSymbol)
        {
            // Intrinsic cast operator — look up emission template.
            // Note: TryGetCastOperator is added in task 8.4; for now, fall back to standard cast.
            var declaringType = GetQualifiedTypeName(intrinsicSymbol);
            var template = _intrinsicRegistry.TryGetCastOperator(declaringType, targetType);
            if (template is not null)
                return string.Format(template.Template, operand);

            // Fallback to standard C# cast if no template found.
            return $"(({targetType}){operand})";
        }

        // User-defined cast operator — emit as standard C# cast expression.
        // The C# compiler will resolve the implicit/explicit operator automatically.
        return $"(({targetType}){operand})";
    }

    /// <summary>
    /// If the given AST node has a resolved implicit cast operator recorded by the type checker,
    /// wraps the emitted expression in a C# cast to the target type so the C# compiler
    /// invokes the user-defined implicit operator. Otherwise returns the expression unchanged.
    /// </summary>
    private string WrapWithImplicitCastIfNeeded(Storm.Runtime.SyntaxNode node, string expression)
    {
        var resolved = _model.GetResolvedOperator(node);
        if (resolved is Symbol resolvedSymbol && resolvedSymbol.Name.StartsWith("operator_implicit_"))
        {
            // Extract the target type name from the operator's declared type.
            var targetTypeName = resolvedSymbol.DeclaredType?.DisplayName;
            if (targetTypeName is not null)
            {
                var emittedTypeName = CSharpTypeMap.GetTypeName(resolvedSymbol.DeclaredType!);
                return $"(({emittedTypeName}){expression})";
            }
        }
        return expression;
    }

    /// <summary>
    /// Task 31.1 / Req 15.2 — Covariant unknown widening emission.
    /// <para>
    /// Under the revised Option C emission strategy (November 2026), this
    /// helper is a no-op. The widening site needs <b>no explicit cast</b>
    /// because the target type (a Closed_Generic_With_Unknown such as
    /// <c>List&lt;unknown&gt;</c>) lowers to C# <c>dynamic</c>, and every
    /// value in C# is implicitly convertible to <c>dynamic</c> (which is
    /// <c>object</c> plus DLR metadata). A plain assignment
    /// <c>dynamic xs = intList;</c> is accepted by the C# compiler and
    /// succeeds at runtime with no cast operation.
    /// </para>
    /// <para>
    /// Historical note: the original Option 2 emission produced
    /// <c>(List&lt;object&gt;)(object)intList</c>. Option 2 was accepted
    /// by the C# compiler but threw <see cref="InvalidCastException"/> at
    /// runtime because the CLR enforces strict generic type identity for
    /// user-defined generics (no implicit coercion from
    /// <c>List&lt;int&gt;</c> to <c>List&lt;object&gt;</c>). Option C
    /// replaces that double cast with <c>dynamic</c> so member access on
    /// the widened receiver routes through the DLR, which dispatches
    /// against the receiver's actual runtime generic type. The
    /// Reachability Analyzer (Phase C) still guarantees that no
    /// write-path member is called on the widened receiver at compile
    /// time, so the DLR only sees reachability-safe calls.
    /// </para>
    /// <para>
    /// The helper is retained (rather than removed) so every call site
    /// continues to document the widening-aware emission path. It always
    /// returns <c>null</c>, causing the caller to emit the operand
    /// unchanged.
    /// </para>
    /// </summary>
    private static string? TryEmitCovariantUnknownCast(
        IType? sourceType,
        IType? targetType,
        string emittedOperand)
    {
        // Option C emission: no explicit cast needed. See XML remarks.
        // Suppress unused-parameter warnings for API stability.
        _ = sourceType;
        _ = targetType;
        _ = emittedOperand;
        return null;
    }

    /// <summary>
    /// Looks up the return type of a method declaration from the semantic
    /// model via the declared symbol. Used by <see cref="EmitMethodDeclaration"/>
    /// to set <see cref="_currentMethodReturnType"/> for the duration of
    /// the method body's emission.
    /// </summary>
    private IType? ResolveMethodReturnType(MethodDeclarationNode methodDecl)
    {
        var symbol = _model.GetDeclaredSymbol(methodDecl);
        return symbol?.DeclaredType;
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
            case BinaryLiteralExpressionPrimaryNode binLit:
                return binLit.BinaryLiteral.Lexeme;
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
            case TupleLiteralExpressionPrimaryNode tupleLit:
            {
                // Emit C# tuple literal: (expr1, expr2) or (name1: expr1, name2: expr2).
                var parts = new List<string>();

                string FormatTupleLiteralElement(TupleLiteralElementNode element)
                {
                    return element switch
                    {
                        NamedTupleLiteralElementTupleLiteralElementNode named =>
                            $"{named.Name.Lexeme}: {EmitExpression(named.Value)}",
                        UnnamedTupleLiteralElementTupleLiteralElementNode unnamed =>
                            EmitExpression(unnamed.Value),
                        _ => ""
                    };
                }

                parts.Add(FormatTupleLiteralElement(tupleLit.First));
                parts.Add(FormatTupleLiteralElement(tupleLit.Rest));
                foreach (var additional in tupleLit.TupleLiteralElements)
                {
                    parts.Add(FormatTupleLiteralElement(additional));
                }

                return $"({string.Join(", ", parts)})";
            }
            case IdentifierExpressionPrimaryNode ident:
                return ident.Name.Lexeme;
            case FieldKeywordExpressionPrimaryNode:
                return "field";
            case TypeLiteralExpressionPrimaryNode typeLiteral:
                return $"typeof({EmitTypeReference(typeLiteral.TypeRef)})";
            case DefaultExpressionPrimaryNode defaultExpr:
            {
                // Resolve the type from the semantic model's expression type map.
                var resolvedType = _model.GetTypeOf(defaultExpr);
                if (resolvedType is not null)
                    return EmitDefaultValueForType(resolvedType);

                // Fallback: emit using the type reference text directly.
                var typeRef = EmitTypeReference(defaultExpr.Type);
                return $"default({typeRef})";
            }
            case DefaultLiteralExpressionPrimaryNode defaultLiteral:
            {
                // The inferred type was recorded in the expression type map
                // during Pass 2 or by the type checker's inference pass.
                var resolvedType = _model.GetTypeOf(defaultLiteral);
                if (resolvedType is not null)
                    return EmitDefaultValueForType(resolvedType);

                // Fallback: bare default keyword (should not normally reach here
                // because the type checker would have emitted a diagnostic).
                return "default";
            }
            default:
                return "";
        }
    }

    /// <summary>
    /// Emits the C# representation of a default value for the given resolved type.
    /// For class/struct types that are not intrinsically defaultable (not backend-backed
    /// primitives), emits <c>TypeName.__CreateDefault()</c>.
    /// For primitive types, emits <c>default(csharpKeyword)</c>.
    /// For string, emits <c>""</c>.
    /// </summary>
    private static string EmitDefaultValueForType(IType type)
    {
        // Check if this is a backend-backed type (primitive or string).
        var isBackendBacked = type switch
        {
            StructType st => st.IsBackendBacked,
            ClassType ct => ct.IsBackendBacked,
            _ => false,
        };

        if (isBackendBacked)
        {
            var csharpName = CSharpTypeMap.GetTypeName(type);

            // String default in Mist is "" (not null).
            if (csharpName == "string")
                return "\"\"";

            // Primitive types use C# default literal.
            return $"default({csharpName})";
        }

        // Non-backend-backed class or struct — use the factory method.
        if (type is ClassType or StructType)
        {
            var typeName = CSharpTypeMap.GetTypeName(type);
            return $"{typeName}.__CreateDefault()";
        }

        // Fallback for other types (should not normally reach here).
        return "default";
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
