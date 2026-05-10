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
    /// <summary>
    /// Emits C# source files to the specified output directory on disk.
    /// Delegates to <see cref="EmitToMemory"/> for content generation, then
    /// writes each file to disk.
    /// </summary>
    public static void Emit(ISemanticModel model, string outputDirectory, string projectName, DiagnosticBag diagnostics)
    {
        Directory.CreateDirectory(outputDirectory);
        EmitCsproj(outputDirectory, projectName);

        var files = EmitToMemory(model, projectName, diagnostics);

        foreach (var (relativePath, content) in files)
        {
            var fullPath = Path.Combine(outputDirectory, relativePath);
            var dir = Path.GetDirectoryName(fullPath);
            if (dir is not null)
                Directory.CreateDirectory(dir);
            File.WriteAllText(fullPath, content);
        }
    }

    /// <summary>
    /// Emits C# source content in memory without writing to disk.
    /// Returns a dictionary mapping relative file paths to their C# content.
    /// Useful for tests that need to inspect emitted content without disk I/O.
    /// </summary>
    public static Dictionary<string, string> EmitToMemory(ISemanticModel model, string projectName, DiagnosticBag diagnostics)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);

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
            string relativePath;
            if (unit.OriginProjectPath is not null)
            {
                var refProjectDir = Path.GetDirectoryName(unit.OriginProjectPath)!;
                var refProjectName = Path.GetFileName(refProjectDir);
                relativePath = Path.Combine("references", refProjectName, csFileName);
            }
            else
            {
                relativePath = csFileName;
            }

            // Get the AST for this compilation unit (same index as the unit).
            if (i < model.Asts.Length && model.Asts[i] is CompilationUnitNode compilationUnit)
            {
                var isReferencedProject = unit.OriginProjectPath is not null;
                var writer = new CSharpSourceWriter(model, backendNamespaces, unit.Source, diagnostics, isReferencedProject);
                var csContent = writer.EmitCompilationUnit(compilationUnit);

                // Skip emitting files that contain no meaningful type declarations
                // (e.g., stdlib files where all types are backend-backed and were skipped).
                if (!IsEmptyEmission(csContent))
                    files[relativePath] = csContent;

                if (writer.NeedsMistVoidStruct)
                    anyNeedsMistVoid = true;
            }
        }

        // Emit the MistVoid helper struct once into a shared file if any compilation unit uses void as a value.
        if (anyNeedsMistVoid)
        {
            files["MistVoid.g.cs"] = GenerateMistVoidFile();
        }

        // Emit union helper types into a single UnionHelpers.cs file if any union types are used.
        var unionTypes = UnionHelperGenerator.CollectUnionTypes(model);
        if (unionTypes.Count > 0)
        {
            files["UnionHelpers.cs"] = UnionHelperGenerator.GenerateUnionHelpersFile(unionTypes);
        }

        // Emit const-carrier interfaces and carrier structs for dependent type parameters.
        var usedDependentValueTypes = DependentTypeHelpersGenerator.CollectUsedDependentValueTypes(model);
        var carrierRegistry = CarrierStructRegistry.CollectFromModel(model);
        if (usedDependentValueTypes.Count > 0 || carrierRegistry.Entries.Count > 0)
        {
            files["DependentTypeHelpers.g.cs"] = DependentTypeHelpersGenerator.GenerateHelpersFile(usedDependentValueTypes, carrierRegistry);
        }

        // Emit the MistContext runtime helper when any compilation unit uses structured context.
        if (MistContextGenerator.UsesStructuredContext(model))
        {
            files["MistContext.g.cs"] = MistContextGenerator.GenerateMistContextFile();
        }

        return files;
    }

    /// <summary>
    /// Returns true if the emitted C# content contains no meaningful type declarations —
    /// just a namespace line and whitespace. This happens for stdlib files where all types
    /// are backend-backed (e.g., Boolean.mist, Int32.mist) and were skipped during emission.
    /// </summary>
    private static bool IsEmptyEmission(string content)
    {
        // A non-empty emission will contain at least one type keyword (class, struct, interface, enum).
        // An empty emission is just "namespace X;\n\n" or "namespace X\n{\n}\n".
        var trimmed = content.AsSpan().Trim();
        if (trimmed.IsEmpty)
            return true;

        // Check if the content contains any type declaration keywords after the namespace line.
        return !content.Contains("class ")
            && !content.Contains("struct ")
            && !content.Contains("interface ")
            && !content.Contains("enum ");
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
    /// <summary>
    /// Type parameters of the current method being emitted that have parameterized
    /// constructor constraints. Used by <c>new T(args)</c> emission to determine
    /// whether to use a method parameter name (factoryT_...) vs. a class field name (_factoryT_...).
    /// Null when not inside a generic method with factory parameters.
    /// </summary>
    private IReadOnlyList<IType>? _currentMethodFactoryTypeParams;
    /// <summary>
    /// Type parameters of the current class/struct being emitted that have parameterized
    /// constructor constraints. Used by <see cref="EmitConstructorDeclaration"/> to append
    /// factory delegate parameters to user-defined constructors.
    /// Null when not inside a type with parameterized constructor constraints.
    /// </summary>
    private IReadOnlyList<IType>? _currentClassFactoryTypeParams;
    private readonly HashSet<string> _valueReturningVoidMethods = new(StringComparer.Ordinal);
    /// <summary>
    /// The name of the currently-being-emitted extension class. Set during
    /// <see cref="EmitExtensionBlock"/> so that call-site rewriting for
    /// extension properties can reference the correct static class name.
    /// </summary>
    private string? _currentExtensionClassName;
    /// <summary>
    /// Pending variable bindings from union pattern matching. When an <c>is T t</c>
    /// pattern is emitted against a union scrutinee, the condition becomes
    /// <c>scrutinee.Is{T}</c> and the variable extraction <c>var t = scrutinee.As{T}</c>
    /// is deferred to the start of the then-branch block.
    /// </summary>
    private readonly List<string> _pendingUnionPatternBindings = new();
    private readonly bool _isReferencedProject;
    /// <summary>
    /// Tracks whether the emitter is currently inside a struct declaration.
    /// Used by <see cref="EmitMethodDeclaration"/> to emit the C# <c>readonly</c>
    /// modifier on struct methods (C# has no readonly method concept for classes).
    /// </summary>
    private bool _insideStruct;
    /// <summary>
    /// The namespace of the current compilation unit being emitted.
    /// Used to scope type alias fallback resolution to the correct namespace.
    /// </summary>
    private string? _currentNamespace;
    /// <summary>
    /// When non-null, indicates that the current expression being emitted is the
    /// initializer of a typed variable declaration whose type is an array type.
    /// The emitter uses this to emit <c>new T[] { ... }</c> instead of <c>new[] { ... }</c>
    /// for array literals assigned to explicitly-typed array variables.
    /// </summary>
    private IType? _arrayLiteralTargetElementType;
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
        DiagnosticBag diagnostics,
        bool isReferencedProject = false)
    {
        _model = model;
        _backendNamespaces = backendNamespaces;
        _sourceText = sourceText;
        _diagnostics = diagnostics;
        _intrinsicRegistry = new IntrinsicEmissionTable();
        _isReferencedProject = isReferencedProject;
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

        // Emit using System.Text.RegularExpressions when the file references Standard.Regex.
        if (SourceReferencesRegex())
        {
            usings.Add("System.Text.RegularExpressions");
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
                        _currentNamespace = typeNamespace;
                    }

                    EmitTypeDeclaration(typeItem.TypeDeclaration);
                    break;
                }
                case ExtensionItemTopLevelItemNode extensionItem:
                    EmitExtensionBlock(extensionItem.ExtensionDeclaration);
                    break;
                case TypeAliasItemTopLevelItemNode:
                    // Type aliases are erased — no C# declaration is emitted.
                    break;
                case NewtypeItemTopLevelItemNode newtypeItem:
                {
                    // Determine the newtype's namespace from the semantic model.
                    // NewtypeType doesn't have a ContainingNamespace property, so we
                    // find the namespace by looking at sibling symbols in the same scope.
                    var newtypeSymbol = _model.GetDeclaredSymbol(newtypeItem.NewtypeDeclaration);
                    var newtypeNamespace = GetNewtypeContainingNamespace(newtypeSymbol);

                    // If this newtype has a namespace and we haven't emitted it yet,
                    // emit a file-scoped namespace declaration.
                    if (newtypeNamespace is not null && newtypeNamespace != emittedInferredNamespace)
                    {
                        if (emittedInferredNamespace is not null)
                        {
                            AppendLine();
                        }
                        AppendLine($"namespace {newtypeNamespace};");
                        AppendLine();
                        emittedInferredNamespace = newtypeNamespace;
                        _currentNamespace = newtypeNamespace;
                    }

                    EmitNewtypeDeclaration(newtypeItem.NewtypeDeclaration);
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
                    var bodyItems = nsItem.NamespaceDeclaration switch
                    {
                        FileScopedNamespaceNamespaceDeclarationNode fileScopedNs => fileScopedNs.Body,
                        BlockNamespaceNamespaceDeclarationNode blockNs => blockNs.Body,
                        _ => System.Collections.Immutable.ImmutableArray<NamespaceBodyItemNode>.Empty,
                    };

                    foreach (var bodyItem in bodyItems)
                    {
                        if (bodyItem is NamespaceTypeItemNamespaceBodyItemNode typeBodyItem)
                        {
                            CollectVoidMethodNamesFromType(typeBodyItem.TypeDeclaration, voidMethodNames);
                        }
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
                    var bodyItems = nsItem.NamespaceDeclaration switch
                    {
                        FileScopedNamespaceNamespaceDeclarationNode fileScopedNs => fileScopedNs.Body,
                        BlockNamespaceNamespaceDeclarationNode blockNs => blockNs.Body,
                        _ => System.Collections.Immutable.ImmutableArray<NamespaceBodyItemNode>.Empty,
                    };
                    foreach (var bodyItem in bodyItems)
                    {
                        if (bodyItem is NamespaceTypeItemNamespaceBodyItemNode typeBodyItem)
                        {
                            types.Add(typeBodyItem.TypeDeclaration);
                        }
                    }
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

            case ReadonlyBlockStatementStatementNode readonlyBlockStmt:
                ScanBlockForValueReturningCalls(readonlyBlockStmt.ReadonlyBlock.Body, voidMethodNames);
                break;

            case WithVarDeclarationStatementStatementNode withVarStmt:
                ScanExpressionForValueReturningCalls(withVarStmt.WithVarDeclaration.Initializer, voidMethodNames);
                break;

            case WithTypedDeclarationStatementStatementNode withTypedStmt:
                ScanExpressionForValueReturningCalls(withTypedStmt.WithTypedDeclaration.Initializer, voidMethodNames);
                break;

            case WithDiscardDeclarationStatementStatementNode withDiscardStmt:
                ScanExpressionForValueReturningCalls(withDiscardStmt.WithDiscardDeclaration.Initializer, voidMethodNames);
                break;

            case WithPushStatementStatementNode withPushStmt:
                ScanExpressionForValueReturningCalls(withPushStmt.WithPushStatement.Value, voidMethodNames);
                break;

            case WithBlockStatementStatementNode withBlockStmt:
                ScanExpressionForValueReturningCalls(withBlockStmt.WithBlockStatement.Expression, voidMethodNames);
                ScanBlockForValueReturningCalls(withBlockStmt.WithBlockStatement.Body, voidMethodNames);
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
            EnumTypeTypeDeclarationNode enumType => enumType.EnumDeclaration,
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
            EnumType et => et.ContainingNamespace,
            _ => null,
        };
    }

    /// <summary>
    /// Gets the containing namespace for a symbol by inspecting its DeclaredType.
    /// Returns null if the symbol has no type or the type has no namespace.
    /// </summary>
    private static string? GetSymbolContainingNamespace(ISymbol symbol)
    {
        return symbol.DeclaredType switch
        {
            ClassType ct => ct.ContainingNamespace,
            StructType st => st.ContainingNamespace,
            InterfaceType it => it.ContainingNamespace,
            TraitType tt => tt.ContainingNamespace,
            _ => null,
        };
    }

    /// <summary>
    /// Gets the containing namespace for a newtype symbol. Since <see cref="NewtypeType"/>
    /// does not have a <c>ContainingNamespace</c> property, this method finds the namespace
    /// by looking at sibling symbols in the same containing scope that do have a namespace.
    /// </summary>
    private string? GetNewtypeContainingNamespace(ISymbol? symbol)
    {
        if (symbol?.ContainingScope is not Scope containingScope) return null;

        // If the containing scope's parent is not the global scope, the newtype
        // is not in a namespace (or is nested in a way we don't handle here).
        if (containingScope.Parent is null || !ReferenceEquals(containingScope.Parent, _model.GlobalScope))
            return null;

        // Look at sibling symbols in the same scope to find one with a ContainingNamespace.
        foreach (var sibling in containingScope.Members)
        {
            var ns = sibling.DeclaredType switch
            {
                ClassType ct => ct.ContainingNamespace,
                StructType st => st.ContainingNamespace,
                InterfaceType it => it.ContainingNamespace,
                TraitType tt => tt.ContainingNamespace,
                EnumType et => et.ContainingNamespace,
                _ => null,
            };
            if (ns is not null) return ns;
        }

        // Fallback: look for a namespace symbol in the global scope whose name
        // matches the scope structure.
        foreach (var globalMember in _model.GlobalScope.Members)
        {
            if (globalMember.Kind == SymbolKind.Namespace)
            {
                // Check if any symbol in this namespace scope is in our containing scope.
                foreach (var nsChild in _model.AllSymbols)
                {
                    if (ReferenceEquals(nsChild.ContainingScope, containingScope)
                        && nsChild != symbol)
                    {
                        return globalMember.Name;
                    }
                }
            }
        }

        return null;
    }

    private void EmitNamespaceDeclaration(NamespaceDeclarationNode ns)
    {
        switch (ns)
        {
            case FileScopedNamespaceNamespaceDeclarationNode fileScopedNs:
            {
                var name = ExtractQualifiedName(fileScopedNs.Name);
                _currentNamespace = name;
                AppendLine($"namespace {name};");
                AppendLine();

                foreach (var bodyItem in fileScopedNs.Body)
                {
                    switch (bodyItem)
                    {
                        case NamespaceTypeItemNamespaceBodyItemNode typeBodyItem:
                            EmitTypeDeclaration(typeBodyItem.TypeDeclaration);
                            break;
                        case NamespaceExtensionItemNamespaceBodyItemNode extensionBodyItem:
                            EmitExtensionBlock(extensionBodyItem.ExtensionDeclaration);
                            break;
                        case NamespaceTypeAliasItemNamespaceBodyItemNode:
                            // Type aliases are erased — no C# declaration is emitted.
                            break;
                        case NamespaceNewtypeItemNamespaceBodyItemNode newtypeBodyItem:
                            EmitNewtypeDeclaration(newtypeBodyItem.NewtypeDeclaration);
                            break;
                    }
                }
                break;
            }
            case BlockNamespaceNamespaceDeclarationNode blockNs:
            {
                var name = ExtractQualifiedName(blockNs.Name);
                AppendLine($"namespace {name}");
                AppendLine("{");
                _indentLevel++;

                foreach (var bodyItem in blockNs.Body)
                {
                    switch (bodyItem)
                    {
                        case NamespaceTypeItemNamespaceBodyItemNode typeBodyItem:
                            EmitTypeDeclaration(typeBodyItem.TypeDeclaration);
                            break;
                        case NamespaceExtensionItemNamespaceBodyItemNode extensionBodyItem:
                            EmitExtensionBlock(extensionBodyItem.ExtensionDeclaration);
                            break;
                        case NamespaceTypeAliasItemNamespaceBodyItemNode:
                            // Type aliases are erased — no C# declaration is emitted.
                            break;
                        case NamespaceNewtypeItemNamespaceBodyItemNode newtypeBodyItem:
                            EmitNewtypeDeclaration(newtypeBodyItem.NewtypeDeclaration);
                            break;
                    }
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
            case EnumTypeTypeDeclarationNode enumType:
                EmitEnumDeclaration(enumType.EnumDeclaration);
                break;
        }
    }

    // ─── Newtype Declaration Emission ─────────────────────────────────────

    /// <summary>
    /// Emits a newtype declaration as C# code. The strategy depends on the source type:
    /// <list type="bullet">
    /// <item>Class source → C# class with the newtype name containing all cloned members</item>
    /// <item>Struct source → C# struct with the newtype name containing all cloned members</item>
    /// <item>Primitive source (backend-backed) → C# readonly struct wrapper with explicit conversion operators</item>
    /// </list>
    /// </summary>
    private void EmitNewtypeDeclaration(NewtypeDeclarationNode newtypeDecl)
    {
        var symbol = _model.GetDeclaredSymbol(newtypeDecl);
        if (symbol?.DeclaredType is not NewtypeType newtypeType) return;

        var sourceType = newtypeType.SourceType;
        var newtypeName = newtypeType.Name;

        switch (sourceType)
        {
            case StructType st when st.IsBackendBacked:
                EmitPrimitiveNewtypeWrapper(newtypeName, st);
                break;
            case ClassType ct when ct.IsBackendBacked:
                EmitPrimitiveNewtypeWrapperForClass(newtypeName, ct);
                break;
            case ClassType:
                EmitClassNewtype(newtypeName, sourceType);
                break;
            case StructType:
                EmitStructNewtype(newtypeName, sourceType);
                break;
        }
    }

    /// <summary>
    /// Strategy 3: Emits a readonly struct wrapper for a newtype whose source is a primitive type.
    /// Includes a <c>Value</c> field, a constructor, and explicit conversion operators in both directions.
    /// </summary>
    private void EmitPrimitiveNewtypeWrapper(string newtypeName, StructType primitiveType)
    {
        var csharpKeyword = CSharpTypeMap.GetKeyword(primitiveType);

        AppendLine($"public readonly struct {newtypeName}");
        AppendLine("{");
        _indentLevel++;

        // Value field
        AppendLine($"public readonly {csharpKeyword} Value;");
        AppendLine();

        // Constructor
        AppendLine($"public {newtypeName}({csharpKeyword} value)");
        AppendLine("{");
        _indentLevel++;
        AppendLine("Value = value;");
        _indentLevel--;
        AppendLine("}");
        AppendLine();

        // Explicit conversion: primitive → newtype
        AppendLine($"public static explicit operator {newtypeName}({csharpKeyword} value) => new {newtypeName}(value);");
        // Explicit conversion: newtype → primitive
        AppendLine($"public static explicit operator {csharpKeyword}({newtypeName} id) => id.Value;");

        _indentLevel--;
        AppendLine("}");
    }

    /// <summary>
    /// Strategy for backend-backed class types (e.g., string): Emits a C# readonly struct
    /// wrapping the class value with explicit conversion operators in both directions.
    /// </summary>
    private void EmitPrimitiveNewtypeWrapperForClass(string newtypeName, ClassType classType)
    {
        var csharpKeyword = CSharpTypeMap.GetKeyword(classType);

        AppendLine($"public readonly struct {newtypeName}");
        AppendLine("{");
        _indentLevel++;

        // Value field
        AppendLine($"public readonly {csharpKeyword} Value;");
        AppendLine();

        // Constructor
        AppendLine($"public {newtypeName}({csharpKeyword} value)");
        AppendLine("{");
        _indentLevel++;
        AppendLine("Value = value;");
        _indentLevel--;
        AppendLine("}");
        AppendLine();

        // Explicit conversion: class → newtype
        AppendLine($"public static explicit operator {newtypeName}({csharpKeyword} value) => new {newtypeName}(value);");
        // Explicit conversion: newtype → class
        AppendLine($"public static explicit operator {csharpKeyword}({newtypeName} id) => id.Value;");

        _indentLevel--;
        AppendLine("}");
    }

    /// <summary>
    /// Strategy 1: Emits a C# class for a newtype whose source is a class.
    /// Re-emits all members from the source type's declaration under the newtype name.
    /// </summary>
    private void EmitClassNewtype(string newtypeName, IType sourceType)
    {
        // Find the source type's declaring symbol and AST node.
        var sourceSymbol = _model.AllSymbols.FirstOrDefault(s =>
            s.DeclaredType is not null && ReferenceEquals(s.DeclaredType, sourceType));
        var sourceNode = sourceSymbol is not null ? _model.GetDeclaringNode(sourceSymbol) : null;

        AppendLine($"public class {newtypeName}");
        AppendLine("{");
        _indentLevel++;

        if (sourceNode is ClassDeclarationNode classDecl)
        {
            for (var i = 0; i < classDecl.Members.Length; i++)
            {
                if (i > 0)
                {
                    AppendLine();
                }
                EmitNewtypeMemberDeclaration(classDecl.Members[i], newtypeName);
            }
        }

        _indentLevel--;
        AppendLine("}");
    }

    /// <summary>
    /// Strategy 2: Emits a C# struct for a newtype whose source is a struct.
    /// Re-emits all members from the source type's declaration under the newtype name.
    /// </summary>
    private void EmitStructNewtype(string newtypeName, IType sourceType)
    {
        // Find the source type's declaring symbol and AST node.
        var sourceSymbol = _model.AllSymbols.FirstOrDefault(s =>
            s.DeclaredType is not null && ReferenceEquals(s.DeclaredType, sourceType));
        var sourceNode = sourceSymbol is not null ? _model.GetDeclaringNode(sourceSymbol) : null;

        AppendLine($"public struct {newtypeName}");
        AppendLine("{");
        _indentLevel++;

        // Track that we're inside a struct so method emission can
        // emit the C# `readonly` modifier on struct methods.
        var previousInsideStruct = _insideStruct;
        _insideStruct = true;

        if (sourceNode is StructDeclarationNode structDecl)
        {
            for (var i = 0; i < structDecl.Members.Length; i++)
            {
                if (i > 0)
                {
                    AppendLine();
                }
                EmitNewtypeMemberDeclaration(structDecl.Members[i], newtypeName);
            }
        }

        _insideStruct = previousInsideStruct;
        _indentLevel--;
        AppendLine("}");
    }

    /// <summary>
    /// Emits a single member declaration for a newtype, renaming constructors to the newtype name.
    /// Delegates to the standard member emission methods for all other member kinds.
    /// </summary>
    private void EmitNewtypeMemberDeclaration(MemberDeclarationNode memberDecl, string newtypeName)
    {
        // For constructors, we need to emit with the newtype name instead of the source type name.
        if (memberDecl is ConstructorMemberMemberDeclarationNode ctorMember)
        {
            EmitNewtypeConstructorDeclaration(ctorMember.Constructor, newtypeName);
            return;
        }

        // All other members are emitted as-is using the standard emission logic.
        EmitMemberDeclaration(memberDecl);
    }

    /// <summary>
    /// Emits a constructor declaration with the newtype name instead of the original type name.
    /// </summary>
    private void EmitNewtypeConstructorDeclaration(ConstructorDeclarationNode ctorDecl, string newtypeName)
    {
        var modifiers = EmitModifiers(ctorDecl.Modifiers);
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

        AppendLine($"{modifiers}{newtypeName}({parameters}){chain}");
        if (ctorDecl.Body.GetBlock() is BlockNode block)
        {
            EmitBlock(block);
        }
        else
        {
            AppendLine(";");
        }
    }

    // ─── Enum Declaration Emission ─────────────────────────────────────────

    /// <summary>
    /// Emits an enum declaration as a C# <c>record class</c> with:
    /// - A primary constructor parameter <c>Value</c> of the underlying type
    /// - A <c>public static readonly</c> field for each enum member
    /// - An <c>implicit operator</c> converting to the underlying type
    /// Access modifier defaults to <c>internal</c> when none is specified.
    /// Enum members are always <c>public static readonly</c> regardless of the enum's visibility.
    /// </summary>
    private void EmitEnumDeclaration(EnumDeclarationNode enumDecl)
    {
        var symbol = _model.GetDeclaredSymbol(enumDecl);
        if (symbol?.DeclaredType is not EnumType enumType) return;

        var modifiers = EmitModifiers(enumDecl.Modifiers);
        var name = enumDecl.Name.Lexeme;

        // Determine the C# type name for the underlying type.
        // For union types, use the union helper struct name.
        var underlyingTypeName = enumType.UnderlyingType is UnionType ut
            ? UnionHelperGenerator.GetHelperTypeName(ut)
            : CSharpTypeMap.GetTypeName(enumType.UnderlyingType);

        AppendLine($"{modifiers}record class {name}({underlyingTypeName} Value)");
        AppendLine("{");
        _indentLevel++;

        // Emit public static readonly fields for each enum member.
        var memberNodes = new List<EnumMemberNode>();
        if (enumDecl.Members is not null)
        {
            memberNodes.Add(enumDecl.Members.EnumMember);
            foreach (var member in enumDecl.Members.EnumMembers)
            {
                memberNodes.Add(member);
            }
        }

        for (var i = 0; i < memberNodes.Count; i++)
        {
            var memberNode = memberNodes[i];
            var memberName = memberNode.Name.Lexeme;
            string valueExpr;

            if (memberNode.Value is not null)
            {
                // Member has an explicit value expression — emit it.
                valueExpr = EmitExpression(memberNode.Value);
            }
            else
            {
                // Name-only member — use the resolved sequential value from the semantic model.
                var memberInfo = enumType.Members[i];
                var resolvedValue = memberInfo.ResolvedValue;
                valueExpr = EmitEnumMemberValue(resolvedValue, underlyingTypeName);
            }

            AppendLine($"public static readonly {name} {memberName} = new {name}({valueExpr});");
        }

        // Emit blank line before the implicit operator.
        AppendLine();

        // Emit implicit conversion operator to the underlying type.
        AppendLine($"public static implicit operator {underlyingTypeName}({name} e) => e.Value;");

        _indentLevel--;
        AppendLine("}");
    }

    /// <summary>
    /// Emits the value expression for a name-only enum member.
    /// For byte underlying types, emits <c>(byte)N</c>. For other integral types,
    /// emits the numeric literal directly.
    /// </summary>
    private static string EmitEnumMemberValue(object? resolvedValue, string underlyingTypeName)
    {
        if (resolvedValue is long longVal)
        {
            return underlyingTypeName switch
            {
                "byte" => $"(byte){longVal}",
                "sbyte" => $"(sbyte){longVal}",
                "short" => $"(short){longVal}",
                "ushort" => $"(ushort){longVal}",
                "int" => $"{longVal}",
                "uint" => $"{longVal}U",
                "long" => $"{longVal}L",
                "ulong" => $"{(ulong)longVal}UL",
                _ => $"({underlyingTypeName}){longVal}",
            };
        }

        // Fallback for non-integer resolved values.
        return resolvedValue?.ToString() ?? "default";
    }

    // ─── Extension Block Emission (Tasks 9.1–9.4) ────────────────────────

    /// <summary>
    /// Emits an extension block as a C# static class with deterministic name:
    /// <c>{ReceiverTypeName}Extensions_{FileName}_{LineNumber}</c>.
    /// Instance methods get a <c>this</c>-annotated receiver as first parameter.
    /// Static methods are emitted without <c>this</c> annotation.
    /// Generic extension type parameters are hoisted to individual methods.
    /// Extension properties are emitted as <c>get_PropertyName</c> static methods.
    /// Universal extensions map receiver type <c>unknown</c> to C# <c>object</c>.
    /// </summary>
    private void EmitExtensionBlock(ExtensionBlockDeclarationNode extDecl)
    {
        // Determine the receiver type name for the generated class name.
        var receiverTypeName = GetExtensionReceiverTypeName(extDecl.ReceiverType);
        var fileName = SanitizeForIdentifier(Path.GetFileNameWithoutExtension(_sourceText.Path));
        var lineNumber = GetLineNumber(extDecl);
        var className = $"{receiverTypeName}Extensions_{fileName}_L{lineNumber}";

        // Store the class name for call-site rewriting of extension properties.
        _currentExtensionClassName = className;

        // Determine the receiver C# type string.
        var receiverCSharpType = EmitTypeReference(extDecl.ReceiverType);
        var receiverName = extDecl.ReceiverName.Lexeme;

        // Collect type parameters from the extension block (for generic extensions).
        var blockTypeParams = EmitTypeParameterList(extDecl.TypeParams);

        AppendLine($"public static class {className}");
        AppendLine("{");
        _indentLevel++;

        for (var i = 0; i < extDecl.Members.Length; i++)
        {
            if (i > 0)
            {
                AppendLine();
            }
            EmitExtensionMember(extDecl.Members[i], receiverCSharpType, receiverName, blockTypeParams);
        }

        _indentLevel--;
        AppendLine("}");

        _currentExtensionClassName = null;
    }

    /// <summary>
    /// Emits a single extension member (method or property) as a static method
    /// in the generated extension class.
    /// </summary>
    private void EmitExtensionMember(
        ExtensionMemberDeclarationNode member,
        string receiverCSharpType,
        string receiverName,
        string blockTypeParams)
    {
        switch (member)
        {
            case ExtensionMethodMemberExtensionMemberDeclarationNode methodMember:
                EmitExtensionMethod(methodMember.Method, receiverCSharpType, receiverName, blockTypeParams);
                break;
            case ExtensionPropertyMemberExtensionMemberDeclarationNode propertyMember:
                EmitExtensionProperty(propertyMember.Property, receiverCSharpType, receiverName, blockTypeParams);
                break;
        }
    }

    /// <summary>
    /// Emits an extension method. Instance methods get <c>this ReceiverType receiverName</c>
    /// as the first parameter. Static methods are emitted without <c>this</c>.
    /// Generic extension type parameters are hoisted to the method level.
    /// </summary>
    private void EmitExtensionMethod(
        MethodDeclarationNode methodDecl,
        string receiverCSharpType,
        string receiverName,
        string blockTypeParams)
    {
        var returnType = EmitTypeReference(methodDecl.ReturnType);
        var name = methodDecl.Name.Lexeme;
        var methodTypeParams = EmitTypeParameterList(methodDecl.TypeParams);

        // Combine block-level and method-level type parameters.
        var combinedTypeParams = CombineTypeParameters(blockTypeParams, methodTypeParams);

        var isStatic = methodDecl.Modifiers.Any(m => m is StaticModifierModifierNode);
        var parameters = EmitParameterList(methodDecl.Parameters);

        // Build the full parameter list.
        string fullParams;
        if (isStatic)
        {
            // Static extension methods don't get the receiver parameter.
            fullParams = parameters;
        }
        else
        {
            // Instance extension methods get `this ReceiverType receiverName` as first param.
            var receiverParam = $"this {receiverCSharpType} {receiverName}";
            fullParams = parameters.Length > 0
                ? $"{receiverParam}, {parameters}"
                : receiverParam;
        }

        AppendLine($"public static {returnType} {name}{combinedTypeParams}({fullParams})");

        // Set the return type context for covariant unknown widening.
        var previousReturnType = _currentMethodReturnType;
        _currentMethodReturnType = ResolveMethodReturnType(methodDecl);

        if (methodDecl.Body.GetBlock() is BlockNode block)
        {
            var extMethodSymbol = _model.GetDeclaredSymbol(methodDecl) as Symbol;
            var withClauses = extMethodSymbol?.WithClauses;
            EmitMethodBlock(block, false, withClauses);
        }
        else
        {
            AppendLine(";");
        }

        _currentMethodReturnType = previousReturnType;
    }

    /// <summary>
    /// Emits an extension property as a <c>get_PropertyName</c> static method
    /// with <c>this ReceiverType receiverName</c> as the first parameter.
    /// Expression-bodied properties emit as <c>return expr;</c>.
    /// Block-bodied properties emit the get accessor body.
    /// </summary>
    private void EmitExtensionProperty(
        PropertyDeclarationNode propertyDecl,
        string receiverCSharpType,
        string receiverName,
        string blockTypeParams)
    {
        switch (propertyDecl)
        {
            case ExpressionBodiedPropertyPropertyDeclarationNode exprProp:
            {
                var type = EmitTypeReference(exprProp.Type);
                var propName = exprProp.Name.Lexeme;
                var body = EmitExpression(exprProp.Body);
                var typeParams = blockTypeParams.Length > 0 ? blockTypeParams : "";
                var receiverParam = $"this {receiverCSharpType} {receiverName}";

                AppendLine($"public static {type} get_{propName}{typeParams}({receiverParam})");
                AppendLine("{");
                _indentLevel++;
                AppendLine($"return {body};");
                _indentLevel--;
                AppendLine("}");
                break;
            }
            case BlockPropertyPropertyDeclarationNode blockProp:
            {
                var type = EmitTypeReference(blockProp.Type);
                var propName = blockProp.Name.Lexeme;
                var typeParams = blockTypeParams.Length > 0 ? blockTypeParams : "";
                var receiverParam = $"this {receiverCSharpType} {receiverName}";

                AppendLine($"public static {type} get_{propName}{typeParams}({receiverParam})");
                AppendLine("{");
                _indentLevel++;

                // Emit the get accessor body statements.
                foreach (var accessor in blockProp.Accessors)
                {
                    if (accessor is BlockGetAccessorAccessorDeclarationNode blockGet)
                    {
                        foreach (var stmt in blockGet.Body.Statements)
                        {
                            EmitStatement(stmt);
                        }
                    }
                }

                _indentLevel--;
                AppendLine("}");
                break;
            }
        }
    }

    /// <summary>
    /// Gets the simple receiver type name for the generated extension class name.
    /// Maps <c>unknown</c> to <c>Unknown</c>, named types to their simple name,
    /// and generic types to <c>NameOfT</c> form.
    /// </summary>
    private string GetExtensionReceiverTypeName(TypeReferenceNode typeRef)
    {
        return typeRef switch
        {
            UnknownTypeRefTypeReferenceNode => "Unknown",
            NamedTypeRefTypeReferenceNode namedRef => ExtractSimpleTypeName(namedRef),
            PrimitiveTypeRefTypeReferenceNode primitiveRef => GetPrimitiveTypeName(primitiveRef),
            _ => "Unknown",
        };
    }

    /// <summary>
    /// Extracts the simple type name from a named type reference, including
    /// generic type arguments in a flattened form (e.g., <c>List&lt;T&gt;</c> → <c>ListOfT</c>).
    /// </summary>
    private string ExtractSimpleTypeName(NamedTypeRefTypeReferenceNode namedRef)
    {
        // Use the qualified name extraction which handles dotted names via source span.
        var fullName = ExtractQualifiedName(namedRef.Name);
        // Take only the last segment for the simple name (e.g., "System.Collections.List" → "List").
        var dotIndex = fullName.LastIndexOf('.');
        var name = dotIndex >= 0 ? fullName[(dotIndex + 1)..] : fullName;

        if (namedRef.TypeArgs is not null)
        {
            // Flatten type args into the name: List<T> → ListOfT
            var typeArgNames = new List<string>();
            typeArgNames.Add(GetTypeArgNameFromTypeArgument(namedRef.TypeArgs.TypeArgument));
            foreach (var arg in namedRef.TypeArgs.TypeArguments)
            {
                typeArgNames.Add(GetTypeArgNameFromTypeArgument(arg));
            }
            return $"{name}Of{string.Join("", typeArgNames)}";
        }

        return name;
    }

    /// <summary>
    /// Gets a simple name for a type argument used in extension class naming.
    /// </summary>
    private string GetTypeArgName(TypeReferenceNode typeRef)
    {
        return typeRef switch
        {
            NamedTypeRefTypeReferenceNode named => ExtractSimpleTypeName(named),
            PrimitiveTypeRefTypeReferenceNode prim => GetPrimitiveTypeName(prim),
            UnknownTypeRefTypeReferenceNode => "Unknown",
            _ => "T"
        };
    }

    /// <summary>
    /// Gets a simple name for a type argument node (handles both type references and literal values).
    /// Used in extension class naming where dependent type arguments may appear.
    /// </summary>
    private string GetTypeArgNameFromTypeArgument(TypeArgumentNode arg)
    {
        return arg switch
        {
            TypeReferenceTypeArgumentTypeArgumentNode typeRefArg => GetTypeArgName(typeRefArg.Type),
            IntegerLiteralTypeArgumentTypeArgumentNode intLit => intLit.Literal.Lexeme,
            NegatedIntegerTypeArgumentTypeArgumentNode negLit => $"Neg{negLit.Literal.Lexeme}",
            HexLiteralTypeArgumentTypeArgumentNode hexLit => hexLit.Literal.Lexeme,
            BinaryLiteralTypeArgumentTypeArgumentNode binLit => binLit.Literal.Lexeme,
            TrueLiteralTypeArgumentTypeArgumentNode => "True",
            FalseLiteralTypeArgumentTypeArgumentNode => "False",
            CharLiteralTypeArgumentTypeArgumentNode charLit => charLit.Literal.Lexeme,
            _ => "T"
        };
    }

    /// <summary>
    /// Gets the C#-friendly name for a primitive type reference.
    /// </summary>
    private static string GetPrimitiveTypeName(PrimitiveTypeRefTypeReferenceNode primitiveRef)
    {
        return primitiveRef.PrimitiveType switch
        {
            IntTypePrimitiveTypeNode => "Int32",
            UintTypePrimitiveTypeNode => "UInt32",
            LongTypePrimitiveTypeNode => "Int64",
            UlongTypePrimitiveTypeNode => "UInt64",
            ShortTypePrimitiveTypeNode => "Int16",
            UshortTypePrimitiveTypeNode => "UInt16",
            ByteTypePrimitiveTypeNode => "Byte",
            SbyteTypePrimitiveTypeNode => "SByte",
            FloatTypePrimitiveTypeNode => "Single",
            DoubleTypePrimitiveTypeNode => "Double",
            DecimalTypePrimitiveTypeNode => "Decimal",
            BoolTypePrimitiveTypeNode => "Boolean",
            CharTypePrimitiveTypeNode => "Char",
            StringTypePrimitiveTypeNode => "String",
            RegexTypePrimitiveTypeNode => "Regex",
            _ => "Unknown"
        };
    }

    /// <summary>
    /// Gets the 1-based line number of an extension block declaration in the source file.
    /// </summary>
    private int GetLineNumber(ExtensionBlockDeclarationNode extDecl)
    {
        var span = extDecl.Span;
        if (span.Length > 0)
        {
            var linePos = _sourceText.ToLinePositionSpan(new Common.Text.SourceSpan(span.Start, span.Length));
            return linePos.Start.Line;
        }
        return 1;
    }

    /// <summary>
    /// Sanitizes a file name for use as a C# identifier by replacing non-alphanumeric
    /// characters with underscores.
    /// </summary>
    private static string SanitizeForIdentifier(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
        }
        return sb.ToString();
    }

    /// <summary>
    /// Combines block-level and method-level type parameter lists.
    /// If both are present, merges them into a single list.
    /// </summary>
    private static string CombineTypeParameters(string blockTypeParams, string methodTypeParams)
    {
        if (blockTypeParams.Length == 0) return methodTypeParams;
        if (methodTypeParams.Length == 0) return blockTypeParams;

        // Both have type params — merge: strip angle brackets and combine.
        var blockInner = blockTypeParams[1..^1]; // Remove < and >
        var methodInner = methodTypeParams[1..^1];
        return $"<{blockInner}, {methodInner}>";
    }

    /// <summary>
    /// Computes the deterministic extension class name for a given extension block info.
    /// Used for call-site rewriting of extension properties and static extension methods.
    /// The name follows the pattern: <c>{ReceiverTypeName}Extensions_{FileName}_{LineNumber}</c>.
    /// </summary>
    private string ComputeExtensionClassName(ExtensionBlockInfo extBlock)
    {
        var declaringNode = extBlock.DeclaringNode;
        var receiverTypeName = GetReceiverTypeNameFromIType(extBlock.ReceiverType);

        // Find the source file containing this extension block.
        string fileName = "Unknown";
        var lineNumber = 1;

        for (var i = 0; i < _model.Units.Count; i++)
        {
            var unit = _model.Units[i];
            if (i < _model.Asts.Length && _model.Asts[i] is CompilationUnitNode cu)
            {
                // Check if this compilation unit contains the declaring node.
                if (ContainsNode(cu, declaringNode))
                {
                    fileName = SanitizeForIdentifier(Path.GetFileNameWithoutExtension(unit.Source.Path));
                    var span = declaringNode.Span;
                    if (span.Length > 0)
                    {
                        var linePos = unit.Source.ToLinePositionSpan(
                            new Common.Text.SourceSpan(span.Start, span.Length));
                        lineNumber = linePos.Start.Line;
                    }
                    break;
                }
            }
        }

        return $"{receiverTypeName}Extensions_{fileName}_L{lineNumber}";
    }

    /// <summary>
    /// Gets the simple receiver type name from a resolved IType for extension class naming.
    /// </summary>
    private static string GetReceiverTypeNameFromIType(IType type)
    {
        return type switch
        {
            UnknownType => "Unknown",
            ClassType ct => ct.DisplayName,
            StructType st => st.DisplayName,
            InterfaceType it => it.DisplayName,
            ConstructedType constructed => GetReceiverTypeNameFromIType(constructed.GenericDefinition),
            TypeParameterType tp => tp.Name,
            _ => "Unknown"
        };
    }

    /// <summary>
    /// Checks if a compilation unit AST contains the given node (shallow check via extension blocks).
    /// </summary>
    private static bool ContainsNode(CompilationUnitNode cu, Storm.Runtime.SyntaxNode targetNode)
    {
        foreach (var item in cu.Items)
        {
            switch (item)
            {
                case ExtensionItemTopLevelItemNode extItem:
                    if (ReferenceEquals(extItem.ExtensionDeclaration, targetNode))
                        return true;
                    break;
                case NamespaceItemTopLevelItemNode nsItem:
                {
                    var bodyItems = nsItem.NamespaceDeclaration switch
                    {
                        FileScopedNamespaceNamespaceDeclarationNode fileScopedNs => fileScopedNs.Body,
                        BlockNamespaceNamespaceDeclarationNode blockNs => blockNs.Body,
                        _ => System.Collections.Immutable.ImmutableArray<NamespaceBodyItemNode>.Empty,
                    };
                    foreach (var bodyItem in bodyItems)
                    {
                        if (bodyItem is NamespaceExtensionItemNamespaceBodyItemNode extBodyItem &&
                            ReferenceEquals(extBodyItem.ExtensionDeclaration, targetNode))
                            return true;
                    }
                    break;
                }
            }
        }
        return false;
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
                foreach (var trait in classTypeFromBase.SatisfiedTraits)
                {
                    traitNames.Add(CSharpTypeMap.GetTypeName(trait));
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
            foreach (var trait in classType.SatisfiedTraits)
            {
                parts.Add(CSharpTypeMap.GetTypeName(trait));
            }

            if (parts.Count > 0)
            {
                baseClause = $" : {string.Join(", ", parts)}";
            }
        }
        // Emit [DependentType(...)] attributes for each dependent type parameter.
        if (symbol?.DeclaredType is not null)
        {
            EmitDependentTypeAttributes(GetTypeParametersFromType(symbol.DeclaredType));
        }
        AppendLine($"{modifiers}class {name}{typeParams}{baseClause}");
        // Emit constraint clauses (where T : ...) for constrained type parameters.
        if (symbol?.DeclaredType is not null)
        {
            var typeParamsForConstraints = GetTypeParametersFromType(symbol.DeclaredType);
            EmitConstraintClauses(typeParamsForConstraints);
        }
        AppendLine("{");
        _indentLevel++;

        // Emit dependent type properties (e.g., `public static int Size => TSize.Value;`)
        var hasDependentProperties = false;
        if (symbol?.DeclaredType is not null)
        {
            hasDependentProperties = EmitDependentTypeProperties(GetTypeParametersFromType(symbol.DeclaredType));
        }

        // Emit factory delegate fields and constructor for parameterized constructor constraints.
        var hasFactoryFields = false;
        if (symbol?.DeclaredType is not null)
        {
            var typeParamsForFactory = GetTypeParametersFromType(symbol.DeclaredType);
            if (HasParameterizedConstructorConstraints(typeParamsForFactory))
            {
                hasFactoryFields = true;
                EmitFactoryDelegateFields(typeParamsForFactory);

                // If the class has user-defined constructors, merge factory params into them
                // instead of emitting a separate synthetic constructor.
                var hasUserConstructors = classDecl.Members.Any(m =>
                    m is ConstructorMemberMemberDeclarationNode);
                if (hasUserConstructors)
                {
                    _currentClassFactoryTypeParams = typeParamsForFactory;
                }
                else
                {
                    EmitFactoryDelegateConstructor(name, typeParamsForFactory);
                }
            }
        }

        // Emit every member in declaration order. No name-based grouping or
        // deduplication is performed, so overloaded methods and constructors
        // are each emitted as separate C# declarations.
        for (var i = 0; i < classDecl.Members.Length; i++)
        {
            if (i > 0 || hasFactoryFields || hasDependentProperties)
            {
                AppendLine();
            }
            EmitMemberDeclaration(classDecl.Members[i]);
        }

        // Clear class-level factory tracking after emitting all members.
        _currentClassFactoryTypeParams = null;

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
                var classTypeForCtor = (ClassType)symbol!.DeclaredType!;
                var ownFields = classTypeForCtor.DefaultValues!
                    .Where(kvp => kvp.Value.ValueKind != DefaultValueKind.InheritedDefault)
                    .ToList();

                // Only emit a protected parameterless constructor if there are own fields
                // to initialize. Zero-field classes can rely on the implicit public
                // parameterless constructor — emitting a protected one would suppress it
                // and make the class unconstructable from outside.
                if (ownFields.Count > 0)
                {
                    emittedDefaultCtor = true;

                    if (classDecl.Members.Length > 0)
                    {
                        AppendLine();
                    }

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
        // Emit [DependentType(...)] attributes for each dependent type parameter.
        var interfaceSymbol = _model.GetDeclaredSymbol(interfaceDecl);
        if (interfaceSymbol?.DeclaredType is InterfaceType interfaceTypeForAttrs)
        {
            EmitDependentTypeAttributes(interfaceTypeForAttrs.TypeParameters);
        }
        AppendLine($"interface {name}{typeParams}{interfaceList}");
        // Emit constraint clauses (where T : ...) for constrained type parameters.
        if (interfaceSymbol?.DeclaredType is InterfaceType interfaceTypeForConstraints)
        {
            EmitConstraintClauses(interfaceTypeForConstraints.TypeParameters);
        }
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

        // For generic interface methods with parameterized constructor constraints,
        // append factory delegate parameters.
        var methodSymbol = _model.GetDeclaredSymbol(methodDecl) as Symbol;
        var methodTypeParams = methodSymbol?.TypeParameters ?? (IReadOnlyList<IType>)Array.Empty<IType>();
        var factoryParams = GetFactoryDelegateMethodParams(methodTypeParams);
        if (factoryParams.Length > 0)
        {
            parameters = parameters.Length > 0
                ? $"{parameters}, {factoryParams}"
                : factoryParams;
        }

        // Check if there are constraint clauses to emit (regular constraints or dependent type parameters).
        var hasConstraints = methodTypeParams.Any(tp =>
            tp is DependentTypeParameterType ||
            (tp is TypeParameterType tpc && tpc.Constraints is not null &&
            (tpc.Constraints.HasClassConstraint || tpc.Constraints.HasStructConstraint ||
             tpc.Constraints.BaseClassConstraint is not null ||
             tpc.Constraints.InterfaceConstraints.Count > 0 ||
             tpc.Constraints.TraitConstraints.Count > 0 ||
             tpc.Constraints.ConstructorConstraints.Any(c => c.Count == 0))));

        if (hasConstraints)
        {
            AppendLine($"{returnType} {name}{typeParams}({parameters})");
            EmitConstraintClauses(methodTypeParams);
            AppendLine(";");
        }
        else
        {
            AppendLine($"{returnType} {name}{typeParams}({parameters});");
        }
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
        var typeParams = EmitTypeParameterList(traitDecl.TypeParams);
        var inheritanceClause = "";
        if (traitDecl.Parents is not null)
        {
            inheritanceClause = $" : {EmitBaseListWithTypeArgs(traitDecl.Parents)}";
        }
        AppendLine($"interface {name}{typeParams}{inheritanceClause}");

        // Emit constraint clauses for the trait's type parameters.
        var traitSymbol = _model.AllSymbols.FirstOrDefault(s => s.Name == name && s.DeclaredType is TraitType);
        if (traitSymbol?.DeclaredType is TraitType traitTypeForConstraints)
        {
            EmitConstraintClauses(traitTypeForConstraints.TypeParameters);
        }

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

            var traitName = CSharpTypeMap.GetTypeName(trait);

            // Build type parameter substitution map for constructed traits.
            // When the trait is ConstructedTraitType (e.g., Comparable<int>),
            // type parameter names (T) in member signatures must be replaced
            // with the concrete type arguments (int).
            Dictionary<string, string>? typeParamSubstitutions = null;
            if (trait is ConstructedTraitType ctt)
            {
                typeParamSubstitutions = new Dictionary<string, string>(StringComparer.Ordinal);
                for (var i = 0; i < ctt.GenericTraitDefinition.TypeParameters.Count && i < ctt.TypeArguments.Count; i++)
                {
                    var paramName = ctt.GenericTraitDefinition.TypeParameters[i].DisplayName;
                    var argTypeName = CSharpTypeMap.GetTypeName(ctt.TypeArguments[i]);
                    typeParamSubstitutions[paramName] = argTypeName;
                }
            }

            // Get the trait's member scope (works for both TraitType and ConstructedTraitType).
            var traitMemberScope = trait switch
            {
                TraitType tt => tt.MemberScope,
                ConstructedTraitType constructedTt => constructedTt.MemberScope,
                _ => null,
            };
            if (traitMemberScope is null)
                continue;

            foreach (var (traitMemberName, sourceMemberSymbol) in satisfaction.WithMappings)
            {
                // Look up the trait member symbol from the trait's member scope.
                var traitMemberSymbol = traitMemberScope.Lookup(traitMemberName);
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
                        // int Comparable<int>.x => this.a;
                        var fieldType = EmitTypeReferenceWithSubstitution(fieldDecl.Type, typeParamSubstitutions);
                        AppendLine($"{fieldType} {traitName}.{traitMemberName} => this.{sourceName};");
                        break;
                    }
                    case TraitMethodDeclarationNode methodDecl:
                    {
                        // Method mapping: emit as method with expression body delegating to source.
                        // int Comparable<int>.CompareTo(int other) => this.Compare(other);
                        var returnType = EmitTypeReferenceWithSubstitution(methodDecl.ReturnType, typeParamSubstitutions);
                        var parameters = EmitTraitMethodParametersWithSubstitution(methodDecl.Parameters, typeParamSubstitutions);
                        var arguments = EmitTraitMethodArguments(methodDecl.Parameters);
                        AppendLine($"{returnType} {traitName}.{traitMemberName}({parameters}) => this.{sourceName}({arguments});");
                        break;
                    }
                    case TraitPropertyDeclarationNode propertyDecl:
                    {
                        // Property mapping: emit as property delegating to source.
                        // int Comparable<int>.x => this.a;  (for get-only)
                        // or with get/set accessors
                        var propType = EmitTypeReferenceWithSubstitution(propertyDecl.Type, typeParamSubstitutions);
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
    /// Emits a type reference, substituting type parameter names with their concrete
    /// type argument names when a substitution map is provided. Used for explicit
    /// interface implementations on constructed trait types.
    /// </summary>
    private string EmitTypeReferenceWithSubstitution(TypeReferenceNode typeRef, Dictionary<string, string>? substitutions)
    {
        if (substitutions is null)
            return EmitTypeReference(typeRef);

        // For named type references, check if the name is a type parameter that needs substitution.
        if (typeRef is NamedTypeRefTypeReferenceNode namedRef)
        {
            var name = namedRef.Name.IDENTIFIER.Lexeme;
            if (string.IsNullOrEmpty(name))
            {
                // Fallback: extract from source span via the qualified name helper.
                name = ExtractQualifiedName(namedRef.Name);
            }

            // If name is still null/empty (node from a different source file where
            // span-based extraction failed), attempt cross-file lookup immediately.
            if (string.IsNullOrEmpty(name))
            {
                var span = namedRef.Name.Span;
                if (span.Length > 0 && span.Length <= 128)
                {
                    foreach (var unit in _model.Units)
                    {
                        if (unit.Source is not null
                            && span.Start + span.Length <= unit.Source.Contents.Length)
                        {
                            var crossFileName = unit.Source.Contents.Substring(span.Start, span.Length);
                            if (substitutions.TryGetValue(crossFileName, out var crossFileSubstituted))
                            {
                                return namedRef.ArrayRank.HasValue ? $"{crossFileSubstituted}[]" : crossFileSubstituted;
                            }
                            // Use the cross-file name even if it's not a type parameter.
                            if (!string.IsNullOrEmpty(crossFileName))
                            {
                                name = crossFileName;
                                break;
                            }
                        }
                    }
                }

                // If still null after all attempts, fall back to emitting without substitution.
                if (string.IsNullOrEmpty(name))
                    return EmitTypeReference(typeRef);
            }

            if (substitutions.TryGetValue(name, out var substituted))
            {
                // Type parameter substituted with concrete type.
                return namedRef.ArrayRank.HasValue ? $"{substituted}[]" : substituted;
            }

            // The name extraction may have failed because the node is from a
            // different source file (e.g., a trait method parameter accessed
            // during explicit interface implementation emission). Try to find
            // the name from the correct compilation unit's source text.
            var nameSpan = namedRef.Name.Span;
            if (nameSpan.Length > 0 && nameSpan.Length <= 128)
            {
                foreach (var unit in _model.Units)
                {
                    if (unit.Source is not null
                        && nameSpan.Start + nameSpan.Length <= unit.Source.Contents.Length)
                    {
                        var crossFileName = unit.Source.Contents.Substring(nameSpan.Start, nameSpan.Length);
                        if (substitutions.TryGetValue(crossFileName, out var crossFileSubstituted))
                        {
                            return namedRef.ArrayRank.HasValue ? $"{crossFileSubstituted}[]" : crossFileSubstituted;
                        }
                    }
                }
            }

            // Not a type parameter — emit normally but substitute type arguments recursively.
            if (namedRef.TypeArgs is not null)
            {
                var typeArgs = EmitTypeArgumentListWithSubstitution(namedRef.TypeArgs, substitutions);
                var mappedName = CSharpTypeMap.TryGetMappedName(name);
                var finalName = mappedName ?? name;
                return namedRef.ArrayRank.HasValue ? $"{finalName}{typeArgs}[]" : $"{finalName}{typeArgs}";
            }

            // No type args, not a type parameter — emit normally.
            return EmitTypeReference(typeRef);
        }

        // Fallback: emit without substitution.
        return EmitTypeReference(typeRef);
    }

    /// <summary>
    /// Emits a type argument list with type parameter substitution applied.
    /// Handles both type reference arguments and literal dependent type arguments.
    /// </summary>
    private string EmitTypeArgumentListWithSubstitution(TypeArgumentListNode typeArgs, Dictionary<string, string> substitutions)
    {
        var parts = new List<string>();
        parts.Add(EmitSingleTypeArgumentWithSubstitution(typeArgs.TypeArgument, substitutions));
        foreach (var arg in typeArgs.TypeArguments)
        {
            parts.Add(EmitSingleTypeArgumentWithSubstitution(arg, substitutions));
        }
        return $"<{string.Join(", ", parts)}>";
    }

    /// <summary>
    /// Emits a single type argument with substitution. For type reference arguments,
    /// applies substitution. For literal dependent type arguments, emits the carrier struct name.
    /// </summary>
    private string EmitSingleTypeArgumentWithSubstitution(TypeArgumentNode arg, Dictionary<string, string> substitutions)
    {
        if (arg is TypeReferenceTypeArgumentTypeArgumentNode typeRefArg)
            return EmitTypeReferenceWithSubstitution(typeRefArg.Type, substitutions);

        // Literal type arguments (dependent types) — emit carrier struct name directly.
        return EmitSingleTypeArgument(arg);
    }

    /// <summary>
    /// Emits the parameter declarations for a trait method with type parameter substitution.
    /// Used when generating explicit interface implementations for constructed trait types.
    /// </summary>
    private string EmitTraitMethodParametersWithSubstitution(ParameterListNode? paramList, Dictionary<string, string>? substitutions)
    {
        if (paramList is null) return "";

        var parts = new List<string>();
        foreach (var child in paramList.Children())
        {
            if (child is ParameterNode param)
            {
                var type = EmitTypeReferenceWithSubstitution(param.Type, substitutions);
                var name = param.Name.Lexeme;
                parts.Add($"{type} {name}");
            }
        }

        return string.Join(", ", parts);
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
                foreach (var trait in structTypeFromBase.SatisfiedTraits)
                {
                    traitNames.Add(CSharpTypeMap.GetTypeName(trait));
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
            foreach (var trait in structType.SatisfiedTraits)
            {
                parts.Add(CSharpTypeMap.GetTypeName(trait));
            }

            if (parts.Count > 0)
            {
                interfaceList = $" : {string.Join(", ", parts)}";
            }
        }
        // Emit [DependentType(...)] attributes for each dependent type parameter.
        if (symbol?.DeclaredType is not null)
        {
            EmitDependentTypeAttributes(GetTypeParametersFromType(symbol.DeclaredType));
        }

        // Task 12.3 — Emit `readonly struct` for readonly struct types.
        var readonlyPrefix = "";
        if (symbol?.DeclaredType is StructType readonlyCheck && readonlyCheck.IsReadonly)
        {
            readonlyPrefix = "readonly ";
        }
        AppendLine($"{modifiers}{readonlyPrefix}struct {name}{typeParams}{interfaceList}");
        // Emit constraint clauses (where T : ...) for constrained type parameters.
        if (symbol?.DeclaredType is not null)
        {
            var typeParamsForConstraints = GetTypeParametersFromType(symbol.DeclaredType);
            EmitConstraintClauses(typeParamsForConstraints);
        }
        AppendLine("{");
        _indentLevel++;

        // Emit dependent type properties (e.g., `public static int Size => TSize.Value;`)
        var hasDependentProperties = false;
        if (symbol?.DeclaredType is not null)
        {
            hasDependentProperties = EmitDependentTypeProperties(GetTypeParametersFromType(symbol.DeclaredType));
        }

        // Task 12.4 — Track that we're inside a struct so method emission can
        // emit the C# `readonly` modifier on struct methods.
        var previousInsideStruct = _insideStruct;
        _insideStruct = true;

        // Emit factory delegate fields and constructor for parameterized constructor constraints.
        var hasFactoryFields = false;
        if (symbol?.DeclaredType is not null)
        {
            var typeParamsForFactory = GetTypeParametersFromType(symbol.DeclaredType);
            if (HasParameterizedConstructorConstraints(typeParamsForFactory))
            {
                hasFactoryFields = true;
                EmitFactoryDelegateFields(typeParamsForFactory);

                // If the struct has user-defined constructors, merge factory params into them
                // instead of emitting a separate synthetic constructor.
                var hasUserConstructors = structDecl.Members.Any(m =>
                    m is ConstructorMemberMemberDeclarationNode);
                if (hasUserConstructors)
                {
                    _currentClassFactoryTypeParams = typeParamsForFactory;
                }
                else
                {
                    EmitFactoryDelegateConstructor(name, typeParamsForFactory);
                }
            }
        }

        // Emit every member in declaration order. No name-based grouping or
        // deduplication is performed, so overloaded methods and constructors
        // are each emitted as separate C# declarations.
        for (var i = 0; i < structDecl.Members.Length; i++)
        {
            if (i > 0 || hasFactoryFields || hasDependentProperties)
            {
                AppendLine();
            }
            EmitMemberDeclaration(structDecl.Members[i]);
        }

        // Clear class-level factory tracking after emitting all members.
        _currentClassFactoryTypeParams = null;

        // Emit explicit interface implementations for with-mapped trait members.
        if (symbol?.DeclaredType is StructType structTypeForTraits)
        {
            EmitExplicitTraitImplementations(structTypeForTraits, structDecl.Members.Length > 0);
        }

        // Emit __CreateDefault factory method for defaultable structs.
        // For readonly structs, fields cannot be assigned outside a constructor, so we emit
        // a private constructor with a sentinel parameter that sets the default block values.
        // __CreateDefault() calls this private constructor instead of assigning fields directly.
        // This keeps the user's parameterless constructor (if any) separate from the default block.
        if (symbol?.DeclaredType is StructType defaultableStruct
            && defaultableStruct.Defaultability is DefaultabilityStatus.Inferred or DefaultabilityStatus.Explicit
            && defaultableStruct.DefaultValues is not null)
        {
            var hasExistingMembers = structDecl.Members.Length > 0;

            if (defaultableStruct.IsReadonly)
            {
                var ownFields = defaultableStruct.DefaultValues
                    .Where(kvp => kvp.Value.ValueKind != DefaultValueKind.InheritedDefault)
                    .ToList();

                if (ownFields.Count > 0)
                {
                    // Emit a private constructor with a bool sentinel that sets default values.
                    // __CreateDefault() calls this constructor, not the user's parameterless one.
                    if (hasExistingMembers)
                    {
                        AppendLine();
                    }

                    AppendLine($"private {structDecl.Name.Lexeme}(bool __defaultSentinel)");
                    AppendLine("{");
                    _indentLevel++;
                    foreach (var (fieldName, fieldValue) in ownFields)
                    {
                        EmitDefaultFieldAssignment("this", fieldName, fieldValue);
                    }
                    _indentLevel--;
                    AppendLine("}");
                }

                // Emit __CreateDefault using the sentinel constructor.
                if (hasExistingMembers || ownFields.Count > 0)
                {
                    AppendLine();
                }
                AppendLine($"public static {defaultableStruct.DisplayName} __CreateDefault()");
                AppendLine("{");
                _indentLevel++;
                if (ownFields.Count > 0)
                {
                    AppendLine($"var inst = new {defaultableStruct.DisplayName}(true);");
                }
                else
                {
                    AppendLine($"var inst = new {defaultableStruct.DisplayName}();");
                }
                AppendLine("return inst;");
                _indentLevel--;
                AppendLine("}");
            }
            else
            {
                // Non-readonly structs: use the original pattern (direct field assignment).
                EmitCreateDefaultMethod(defaultableStruct.DisplayName, defaultableStruct.DefaultValues, hasExistingMembers);
            }
        }

        _insideStruct = previousInsideStruct;
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
    /// Emits a regex literal as a C# <c>new System.Text.RegularExpressions.Regex</c>
    /// constructor call with <c>RegexOptions.Compiled</c>.
    /// Steps:
    /// 1. Strip backtick delimiters from the lexeme.
    /// 2. Resolve Mist escape sequences: <c>\`</c> → <c>`</c> and <c>\\</c> → <c>\</c>.
    /// 3. Apply C# string escaping to the resulting pattern.
    /// 4. Emit the fully-qualified constructor call.
    /// </summary>
    private static string EmitRegexLiteral(string lexeme)
    {
        // Strip backtick delimiters.
        var raw = lexeme[1..^1];

        // Resolve Mist regex escape sequences to get the actual regex pattern.
        var pattern = new StringBuilder(raw.Length);
        for (var i = 0; i < raw.Length; i++)
        {
            if (raw[i] == '\\' && i + 1 < raw.Length)
            {
                var next = raw[i + 1];
                if (next == '`')
                {
                    pattern.Append('`');
                    i++;
                }
                else if (next == '\\')
                {
                    pattern.Append('\\');
                    i++;
                }
                else
                {
                    // Not a Mist escape — preserve the backslash and next char verbatim.
                    pattern.Append(raw[i]);
                }
            }
            else
            {
                pattern.Append(raw[i]);
            }
        }

        // Apply C# string escaping to the resolved pattern.
        var escaped = EscapeString(pattern.ToString());

        return $"new System.Text.RegularExpressions.Regex(\"{escaped}\", System.Text.RegularExpressions.RegexOptions.Compiled)";
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

        // Task 12.1 — Emit C# `readonly` modifier on fields with ReadonlyStatus != Mutable.
        var readonlyPrefix = "";
        var fieldSymbol = _model.GetDeclaredSymbol(fieldDecl) as Symbol;
        if (fieldSymbol is not null && fieldSymbol.ReadonlyStatus is not ReadonlyStatus.Mutable and not ReadonlyStatus.Unknown)
        {
            readonlyPrefix = "readonly ";
        }

        if (fieldDecl.Initializer != null)
        {
            // The field has an initializer expression.
            // With the bugfix, the Initializer property is now ExpressionNode? directly.
            AppendLine($"{modifiers}{readonlyPrefix}{type} {name} = {EmitExpression(fieldDecl.Initializer)};");
        }
        else
        {
            AppendLine($"{modifiers}{readonlyPrefix}{type} {name};");
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

        // Append factory delegate parameters when the enclosing type has parameterized
        // constructor constraints and user-defined constructors (merged approach).
        if (_currentClassFactoryTypeParams is not null)
        {
            var factoryParams = GetFactoryDelegateMethodParams(_currentClassFactoryTypeParams);
            if (factoryParams.Length > 0)
            {
                parameters = parameters.Length > 0
                    ? $"{parameters}, {factoryParams}"
                    : factoryParams;
            }
        }

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
            // Resolve with-clauses for the constructor.
            var ctorSymbol = _model.GetDeclaredSymbol(ctorDecl) as Symbol;
            var withClauses = ctorSymbol?.WithClauses;

            // Emit factory field assignments at the start of the constructor body.
            if (_currentClassFactoryTypeParams is not null)
            {
                AppendLine("{");
                _indentLevel++;
                EmitWithClausePeekBindings(withClauses);
                EmitFactoryFieldAssignments(_currentClassFactoryTypeParams);
                EmitBlockStatements(block);
                _indentLevel--;
                AppendLine("}");
            }
            else if (withClauses is { Count: > 0 })
            {
                AppendLine("{");
                _indentLevel++;
                EmitWithClausePeekBindings(withClauses);
                EmitBlockStatements(block);
                _indentLevel--;
                AppendLine("}");
            }
            else
            {
                EmitBlock(block);
            }
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
        // Skip Main methods from referenced projects to avoid multiple
        // entry points in the generated C# project.
        if (_isReferencedProject && methodDecl.Name.Lexeme == "Main")
            return;

        var modifiers = EmitModifiers(methodDecl.Modifiers);
        var returnType = EmitTypeReference(methodDecl.ReturnType);
        var name = methodDecl.Name.Lexeme;
        var typeParams = EmitTypeParameterList(methodDecl.TypeParams);
        var parameters = EmitParameterList(methodDecl.Parameters);

        // Task 12.4 — Emit C# `readonly` modifier on struct methods with ReadonlyStatus != Mutable.
        // C# has no `readonly` method concept for classes, so only emit for structs.
        var readonlyPrefix = "";
        var methodSymbol = _model.GetDeclaredSymbol(methodDecl) as Symbol;
        if (_insideStruct && methodSymbol is not null
            && methodSymbol.ReadonlyStatus is not ReadonlyStatus.Mutable and not ReadonlyStatus.Unknown)
        {
            readonlyPrefix = "readonly ";
        }

        // Context-sensitive void return type: if this void method's return value
        // is consumed at any call site, emit MistVoid instead of void.
        var isValueReturningVoid = methodDecl.ReturnType is VoidTypeRefTypeReferenceNode
            && _valueReturningVoidMethods.Contains(name);
        if (isValueReturningVoid)
        {
            returnType = "MistVoid";
            _needsMistVoidStruct = true;
        }

        // For generic methods with parameterized constructor constraints,
        // append factory delegate parameters to the method signature.
        var methodTypeParams = methodSymbol?.TypeParameters ?? (IReadOnlyList<IType>)Array.Empty<IType>();
        var factoryParams = GetFactoryDelegateMethodParams(methodTypeParams);
        if (factoryParams.Length > 0)
        {
            parameters = parameters.Length > 0
                ? $"{parameters}, {factoryParams}"
                : factoryParams;
        }

        AppendLine($"{modifiers}{readonlyPrefix}{returnType} {name}{typeParams}({parameters})");
        // Emit constraint clauses for the method's type parameters.
        EmitConstraintClauses(methodTypeParams);

        // Set the flag so EmitReturnStatement knows to emit `return MistVoid.Instance;`
        var previousFlag = _inValueReturningVoidMethod;
        _inValueReturningVoidMethod = isValueReturningVoid;

        // Task 31.1 — track the enclosing method's return type so return
        // statements can detect covariant unknown widening against it.
        var previousReturnType = _currentMethodReturnType;
        _currentMethodReturnType = ResolveMethodReturnType(methodDecl);

        // Track method-level factory type parameters for new T(args) emission.
        var previousFactoryTypeParams = _currentMethodFactoryTypeParams;
        _currentMethodFactoryTypeParams = HasParameterizedConstructorConstraints(methodTypeParams)
            ? methodTypeParams
            : null;

        if (methodDecl.Body.GetBlock() is BlockNode block)
        {
            var withClauses = methodSymbol?.WithClauses;
            EmitMethodBlockWithRefinementGuards(block, isValueReturningVoid, methodDecl.Parameters, withClauses);
        }
        else
        {
            // No-body form (;) — grammar permits this for intrinsic members.
            AppendLine(";");
        }

        _inValueReturningVoidMethod = previousFlag;
        _currentMethodReturnType = previousReturnType;
        _currentMethodFactoryTypeParams = previousFactoryTypeParams;
    }

    /// <summary>
    /// Emits a method body block with optional refinement type parameter guards.
    /// For each parameter whose resolved type is a <see cref="RefinementType"/>,
    /// emits a runtime validation guard at method entry that throws
    /// <see cref="ArgumentException"/> if the value fails the predicate.
    /// </summary>
    private void EmitMethodBlockWithRefinementGuards(BlockNode block, bool appendImplicitReturn, ParameterListNode? paramList, IReadOnlyList<ContextClauseInfo>? withClauses = null)
    {
        // Collect refinement-typed parameters.
        var refinementGuards = CollectRefinementParameterGuards(paramList);

        if (refinementGuards.Count == 0)
        {
            // No refinement guards needed — emit the block normally.
            EmitMethodBlock(block, appendImplicitReturn, withClauses);
            return;
        }

        // Emit the block with guards injected at the top.
        AppendLine("{");
        _indentLevel++;

        // Emit with-clause peek bindings at method entry, before refinement guards.
        EmitWithClausePeekBindings(withClauses);

        foreach (var (paramName, refinementType) in refinementGuards)
        {
            var condition = EmitRefinementPredicateCondition(refinementType.Predicate, paramName);
            AppendLine($"if (!({condition}))");
            _indentLevel++;
            AppendLine($"throw new ArgumentException($\"Value {{{paramName}}} does not satisfy refinement '{refinementType.Predicate.Description}' on type '{refinementType.Name}'\", nameof({paramName}));");
            _indentLevel--;
        }

        EmitStatementsFromIndex(block.Statements, 0);

        if (appendImplicitReturn && !BlockEndsWithReturn(block))
        {
            AppendLine("return MistVoid.Instance;");
        }

        _indentLevel--;
        AppendLine("}");
    }

    /// <summary>
    /// Collects parameters from the given parameter list whose resolved type is a
    /// <see cref="RefinementType"/>. Returns a list of (parameterName, refinementType) pairs.
    /// </summary>
    private List<(string Name, RefinementType Type)> CollectRefinementParameterGuards(ParameterListNode? paramList)
    {
        var guards = new List<(string Name, RefinementType Type)>();
        if (paramList is null) return guards;

        foreach (var child in paramList.Children())
        {
            if (child is ParameterNode param)
            {
                var symbol = _model.GetDeclaredSymbol(param);
                if (symbol?.DeclaredType is RefinementType refinementType)
                {
                    guards.Add((param.Name.Lexeme, refinementType));
                }
            }
        }

        return guards;
    }

    /// <summary>
    /// Emits a method body block. For value-returning void methods, appends
    /// <c>return MistVoid.Instance;</c> if the block does not already end with a return statement.
    /// When <paramref name="withClauses"/> is non-null and non-empty, emits
    /// <c>var identifier = (TypeReference)MistContext.Peek&lt;TypeReference&gt;();</c>
    /// at the start of the method body for each with-clause.
    /// </summary>
    private void EmitMethodBlock(BlockNode block, bool appendImplicitReturn, IReadOnlyList<ContextClauseInfo>? withClauses = null)
    {
        AppendLine("{");
        _indentLevel++;

        // Emit with-clause peek bindings at method entry.
        EmitWithClausePeekBindings(withClauses);

        EmitStatementsFromIndex(block.Statements, 0);

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
    /// Emits <c>var identifier = (TypeReference)MistContext.Peek&lt;TypeReference&gt;();</c>
    /// for each with-clause binding at method entry.
    /// </summary>
    private void EmitWithClausePeekBindings(IReadOnlyList<ContextClauseInfo>? withClauses)
    {
        if (withClauses is null || withClauses.Count == 0)
            return;

        foreach (var clause in withClauses)
        {
            var typeName = CSharpTypeMap.GetTypeName(ResolveContextTypeForEmission(clause.ContextType));
            AppendLine($"var {clause.Identifier} = ({typeName})MistContext.Peek<{typeName}>();");
        }
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

        // Task 12.2 — Determine if this property is readonly (emit `init` instead of `set`).
        var propSymbol = _model.GetDeclaredSymbol(blockProp) as Symbol;
        var isReadonlyProperty = propSymbol is not null
            && propSymbol.ReadonlyStatus is not ReadonlyStatus.Mutable and not ReadonlyStatus.Unknown;

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
            accessorParts.Add(EmitAccessorDeclaration(accessor, usesField ? backingFieldName : null, isReadonlyProperty));
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

    private string EmitAccessorDeclaration(AccessorDeclarationNode accessor, string? backingFieldName, bool isReadonlyProperty = false)
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
            // Task 12.2 — When the property is readonly, emit `init` instead of `set`.
            case AutoSetAccessorAccessorDeclarationNode autoSet:
            {
                var mod = EmitAccessorModifier(autoSet.AccessorModifier);
                var keyword = isReadonlyProperty ? "init" : "set";
                return $"{mod}{keyword};";
            }
            case BlockSetAccessorAccessorDeclarationNode blockSet:
            {
                var mod = EmitAccessorModifier(blockSet.AccessorModifier);
                var body = EmitAccessorBlock(blockSet.Body, backingFieldName);
                var keyword = isReadonlyProperty ? "init" : "set";
                return $"{mod}{keyword} {body}";
            }
            case ExpressionSetAccessorAccessorDeclarationNode exprSet:
            {
                var mod = EmitAccessorModifier(exprSet.AccessorModifier);
                var expr = EmitAccessorExpression(exprSet.Expression, backingFieldName);
                var keyword = isReadonlyProperty ? "init" : "set";
                return $"{mod}{keyword} => {expr};";
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

        // Task 12.2 — Determine if this indexer is readonly (emit `init` instead of `set`).
        var indexerSymbol = _model.GetDeclaredSymbol(blockIndexer) as Symbol;
        var isReadonlyIndexer = indexerSymbol is not null
            && indexerSymbol.ReadonlyStatus is not ReadonlyStatus.Mutable and not ReadonlyStatus.Unknown;

        // Build accessor list.
        var accessorParts = new List<string>();
        foreach (var accessor in blockIndexer.Accessors)
        {
            accessorParts.Add(EmitAccessorDeclaration(accessor, null, isReadonlyIndexer));
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
            //   * Union types (`int | string`) → helper type name
            //     (`Union_Int32_String`) per the type-unions feature.
            // We check both the first parameter's AST type (structurally) and
            // the full parameter-list source text (as a lexical fallback) so
            // subsequent parameters whose types contain `unknown` or `|` are also
            // caught. A lexical check is safe here because `unknown` is a
            // reserved keyword (not usable as an identifier in Mist grammar),
            // so any word-boundary `unknown` in the parameter-list source is
            // necessarily the keyword that needs rewriting. Similarly, `|` in
            // a parameter list is always the union type operator (not logical OR
            // which is `||`).
            var firstType = paramList.Parameter.Type;
            if (firstType is not VoidTypeRefTypeReferenceNode
                && firstType is not UnionTypeRefTypeReferenceNode
                && !TypeReferenceContainsUnknown(firstType)
                && !ContainsVoidOrUnknownKeyword(sourceText)
                && !ContainsPipeOperator(sourceText)
                && !ParameterListContainsTypeAlias(paramList, sourceText)
                && !TypeReferenceHasDependentTypeArguments(firstType)
                && !SourceContainsDependentTypeArguments(sourceText)
                && !ContainsReadonlyKeyword(sourceText))
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
    /// Task 12.5 — Returns <c>true</c> if <paramref name="source"/> contains the
    /// Mist keyword <c>readonly</c> as a standalone identifier (matched at word
    /// boundaries). Used as a lexical fallback in <see cref="EmitParameterList"/>
    /// to detect readonly parameter modifiers that must be erased in C# emission
    /// (readonly parameters have no C# equivalent — enforcement is compile-time only).
    /// </summary>
    private static bool ContainsReadonlyKeyword(string source)
    {
        return ContainsWord(source, "readonly");
    }

    /// <summary>
    /// Returns <c>true</c> if the compilation unit references <c>Standard.Regex</c>,
    /// detected by the presence of the <c>regex</c> keyword or a backtick-delimited
    /// regex literal in the source text. When true, the emitter adds a
    /// <c>using System.Text.RegularExpressions;</c> directive.
    /// </summary>
    private bool SourceReferencesRegex()
    {
        var source = _sourceText.Contents;
        // Check for the `regex` keyword as a standalone word.
        if (ContainsWord(source, "regex"))
            return true;
        // Check for backtick (regex literal delimiter).
        if (source.Contains('`'))
            return true;
        return false;
    }

    /// <summary>
    /// Returns <c>true</c> if <paramref name="source"/> contains a single <c>|</c>
    /// character that is not part of <c>||</c>. Used as a lexical fallback in
    /// <see cref="EmitParameterList"/> to detect union type annotations in
    /// subsequent parameters (beyond the first) that the Storm parser does not
    /// expose structurally. A single <c>|</c> in a parameter list is always the
    /// union type operator; logical OR (<c>||</c>) cannot appear in a type context.
    /// </summary>
    private static bool ContainsPipeOperator(string source)
    {
        for (var i = 0; i < source.Length; i++)
        {
            if (source[i] == '|')
            {
                // Check it's not part of ||
                if (i + 1 < source.Length && source[i + 1] == '|')
                {
                    i++; // Skip the second |
                    continue;
                }
                // Check it's not preceded by another | (second char of ||)
                if (i > 0 && source[i - 1] == '|')
                    continue;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Returns <c>true</c> if the source text contains a dependent type argument
    /// (a literal value inside angle brackets, like <c>&lt;5&gt;</c> or <c>&lt;true&gt;</c>).
    /// Used to bypass the raw source text path in parameter list emission.
    /// </summary>
    private static bool SourceContainsDependentTypeArguments(string source)
    {
        for (var i = 0; i < source.Length - 1; i++)
        {
            if (source[i] == '<')
            {
                var next = i + 1;
                while (next < source.Length && source[next] == ' ') next++;
                if (next < source.Length)
                {
                    var c = source[next];
                    // Check for digit, minus (negated), quote (char), or true/false
                    if (char.IsDigit(c) || c == '-' || c == '\'')
                        return true;
                    if (next + 4 <= source.Length && source.Substring(next, 4) == "true")
                        return true;
                    if (next + 5 <= source.Length && source.Substring(next, 5) == "false")
                        return true;
                }
            }
        }
        return false;
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
                // Check all type arguments — only TypeReferenceTypeArgument variants
                // can contain unknown. Literal type arguments (int, bool, char, etc.)
                // used for dependent types can never contain unknown.
                if (TypeArgumentContainsUnknown(named.TypeArgs.TypeArgument))
                    return true;
                foreach (var arg in named.TypeArgs.TypeArguments)
                {
                    if (TypeArgumentContainsUnknown(arg))
                        return true;
                }
                return false;

            default:
                return false;
        }
    }

    /// <summary>
    /// Checks if a single type argument contains <see cref="UnknownTypeRefTypeReferenceNode"/>.
    /// Only <see cref="TypeReferenceTypeArgumentTypeArgumentNode"/> variants can contain unknown;
    /// literal type arguments (integers, booleans, chars) for dependent types never contain unknown.
    /// </summary>
    private static bool TypeArgumentContainsUnknown(TypeArgumentNode arg)
    {
        return arg is TypeReferenceTypeArgumentTypeArgumentNode typeRefArg
            && TypeReferenceContainsUnknown(typeRefArg.Type);
    }

    /// <summary>
    /// Checks if a type reference contains dependent type arguments (literal values like
    /// <c>&lt;5&gt;</c>, <c>&lt;true&gt;</c>, <c>&lt;'x'&gt;</c>). Used to distinguish
    /// dependent type static member access (e.g., <c>FixedBuffer&lt;5&gt;.Size</c>) from
    /// reflection property access on type literals (e.g., <c>int.Name</c> → <c>typeof(int).Name</c>).
    /// </summary>
    private bool TypeReferenceHasDependentTypeArguments(TypeReferenceNode typeRef)
    {
        if (typeRef is not NamedTypeRefTypeReferenceNode named)
            return false;
        if (named.TypeArgs is null)
            return false;

        // Check if any type argument is a literal (non-type-reference) dependent value.
        if (IsDependentTypeArgument(named.TypeArgs.TypeArgument)
            || named.TypeArgs.TypeArguments.Any(IsDependentTypeArgument))
            return true;

        // Check if the target type has dependent type parameters and any type argument
        // in a dependent position is an identifier (forwarded dependent parameter).
        var typeName = ExtractQualifiedName(named.Name);
        var targetTypeParams = GetTypeParamsForName(typeName);
        if (targetTypeParams is not null)
        {
            var allArgs = new List<TypeArgumentNode> { named.TypeArgs.TypeArgument };
            foreach (var arg in named.TypeArgs.TypeArguments)
                allArgs.Add(arg);

            for (var i = 0; i < allArgs.Count && i < targetTypeParams.Count; i++)
            {
                if (targetTypeParams[i] is DependentTypeParameterType
                    && allArgs[i] is TypeReferenceTypeArgumentTypeArgumentNode typeRefArg
                    && typeRefArg.Type is NamedTypeRefTypeReferenceNode argNamedRef
                    && argNamedRef.TypeArgs is null
                    && !argNamedRef.ArrayRank.HasValue)
                {
                    // This position expects a dependent value, but we have an identifier.
                    // It's a forwarded dependent parameter that needs T-prefix rewriting.
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Returns true if a type argument is a literal value (integer, boolean, char, hex, binary,
    /// negated integer, or const reference) rather than a type reference.
    /// </summary>
    private static bool IsDependentTypeArgument(TypeArgumentNode arg)
    {
        return arg is not TypeReferenceTypeArgumentTypeArgumentNode;
    }

    /// <summary>
    /// Resolves a type alias for C# emission. For non-generic aliases, returns
    /// the C# name of the underlying type directly. For generic aliases with
    /// type arguments on the reference node, substitutes the type arguments
    /// into the underlying type before emitting.
    /// </summary>
    private string ResolveAliasForEmission(
        TypeAliasType aliasType,
        NamedTypeRefTypeReferenceNode namedRef,
        bool isValueContext)
    {
        var underlying = aliasType.UnderlyingType;

        // Non-generic alias or no type arguments provided — emit underlying directly.
        if (aliasType.TypeParameters.Count == 0 || namedRef.TypeArgs is null)
        {
            return CSharpTypeMap.GetTypeName(underlying, isValueContext);
        }

        // If all type parameters are dependent (value) parameters, the underlying
        // type doesn't use them — emit the underlying type without type arguments.
        if (aliasType.TypeParameters.All(tp => tp is DependentTypeParameterType))
        {
            return CSharpTypeMap.GetTypeName(underlying, isValueContext);
        }

        // Generic alias with type arguments — emit the underlying type's generic
        // definition with the substituted type arguments from the reference site.
        // e.g., `Box<int>` where `type Box<T> = GenericBox<T>` → `GenericBox<int>`
        if (underlying is ConstructedType ct)
        {
            // Emit the generic definition name and substitute type args.
            var defName = CSharpTypeMap.GetTypeName(ct.GenericDefinition, isValueContext);
            var typeArgs = EmitTypeArgumentList(namedRef.TypeArgs);
            return $"{defName}{typeArgs}";
        }

        // Fallback: emit the underlying type with the type arguments appended.
        var baseName = CSharpTypeMap.GetTypeName(underlying, isValueContext);
        var args = EmitTypeArgumentList(namedRef.TypeArgs);
        return $"{baseName}{args}";
    }

    /// <summary>
    /// Returns <c>true</c> if the parameter list contains a type reference that
    /// resolves to a type alias. Type aliases must be rewritten to their underlying
    /// type in C# output, so the source-text fast-path cannot be used.
    /// Checks both the first parameter's AST type and the source text for alias names.
    /// </summary>
    private bool ParameterListContainsTypeAlias(ParameterListNode paramList, string sourceText)
    {
        // Check the first parameter's type reference structurally.
        if (paramList.Parameter.Type is NamedTypeRefTypeReferenceNode namedRef)
        {
            var name = ExtractQualifiedName(namedRef.Name);
            if (_model.AllSymbols.Any(s => s.Name == name && s.Kind == SymbolKind.TypeAlias))
                return true;
        }

        // Lexical fallback: check if any type alias name appears in the source text
        // as a standalone word (word-boundary match). This catches subsequent parameters
        // (beyond the first) that the Storm parser does not expose structurally.
        // We use ContainsWord instead of Contains to avoid false positives where an
        // alias name is a substring of a parameter name (e.g., alias "Name" matching
        // inside "appName").
        foreach (var sym in _model.AllSymbols)
        {
            if (sym.Kind == SymbolKind.TypeAlias
                && ContainsWord(sourceText, sym.Name))
            {
                return true;
            }
        }

        return false;
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

        // Check if any argument has a resolved implicit or explicit cast operator.
        var hasImplicitCast = false;
        foreach (var argExpr in argExprs)
        {
            var resolved = _model.GetResolvedOperator(argExpr);
            if (resolved is Symbol sym && (sym.Name.StartsWith("operator_implicit_") || sym.Name.StartsWith("operator_explicit_")))
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
        // However, if any argument contains a type literal (e.g., int.Name),
        // we must fall through to the expression emitter so typeof() is applied.
        // Similarly, if any argument contains a dependent type parameter reference,
        // we must fall through so TN.Value substitution is applied.
        // Also bypass when any argument involves an intrinsic type member access
        // (e.g., Regex.Pattern, Regex.IsFullMatch) that needs rewriting.
        if (!ArgumentContainsTypeLiteral(argExprs) && !ArgumentContainsDependentTypeParam(argExprs)
            && !ArgumentContainsIntrinsicMemberAccess(argExprs))
        {
            var span = argList.Span;
            if (span.Length > 0 && span.Start + span.Length <= _sourceText.Contents.Length)
            {
                var rawText = _sourceText.Contents.Substring(span.Start, span.Length);
                if (rawText.Contains("Void.Void"))
                {
                    _needsMistVoidStruct = true;
                    return rawText.Replace("Void.Void", "MistVoid.Instance");
                }
                // Bypass the fast path if the source contains the `regex` keyword,
                // which needs rewriting to System.Text.RegularExpressions.Regex.
                if (ContainsWord(rawText, "regex"))
                {
                    // Fall through to expression emitter.
                }
                else
                {
                    return rawText;
                }
            }
        }

        // Fallback: emit the single argument we do have.
        return EmitExpression(argList.Expression);
    }

    /// <summary>
    /// Returns true if any argument expression contains a type literal (e.g., <c>int.Name</c>)
    /// that requires <c>typeof()</c> wrapping. When true, the source-text fast path must be
    /// bypassed so the expression emitter can apply the wrapping.
    /// </summary>
    private static bool ArgumentContainsTypeLiteral(List<ExpressionNode> argExprs)
    {
        foreach (var expr in argExprs)
        {
            if (ExpressionContainsTypeLiteral(expr))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Returns true if any argument expression contains a reference to a dependent type parameter.
    /// When true, the source-text fast path must be bypassed so the expression emitter can
    /// apply TN.Value substitution.
    /// </summary>
    private bool ArgumentContainsDependentTypeParam(List<ExpressionNode> argExprs)
    {
        foreach (var expr in argExprs)
        {
            if (ExpressionContainsDependentTypeParam(expr))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Returns true if any argument expression involves a member access on an intrinsic
    /// backend-backed type that requires rewriting (e.g., Regex.Pattern → .ToString(),
    /// Regex.IsFullMatch → anchored IsMatch). When true, the source-text fast path must
    /// be bypassed so the expression emitter can apply intrinsic templates.
    /// </summary>
    private bool ArgumentContainsIntrinsicMemberAccess(List<ExpressionNode> argExprs)
    {
        foreach (var expr in argExprs)
        {
            if (ExpressionContainsIntrinsicMemberAccess(expr))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Recursively checks if an expression tree contains a member access or method call
    /// on a type that has intrinsic emission templates registered.
    /// </summary>
    private bool ExpressionContainsIntrinsicMemberAccess(Storm.Runtime.SyntaxNode node)
    {
        if (node is PostfixNode postfix && postfix.Suffix.Length > 0)
        {
            var receiverType = _model.GetTypeOf(postfix.Target);
            if (receiverType is not null)
            {
                var qualifiedName = GetQualifiedTypeNameFromType(receiverType);
                if (qualifiedName is not null && _intrinsicRegistry.HasInstanceMethods(qualifiedName))
                    return true;
            }
        }

        foreach (var child in node.Children())
        {
            if (child is not null && ExpressionContainsIntrinsicMemberAccess(child))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Recursively checks if an expression tree contains an identifier that references
    /// a dependent type parameter.
    /// </summary>
    private bool ExpressionContainsDependentTypeParam(Storm.Runtime.SyntaxNode node)
    {
        if (node is IdentifierExpressionPrimaryNode ident)
        {
            var resolvedType = _model.GetTypeOf(ident);
            if (resolvedType is DependentTypeParameterType)
                return true;
            if (IsDependentTypeParameterName(ident.Name.Lexeme))
                return true;
        }

        foreach (var child in node.Children())
        {
            if (child is not null && ExpressionContainsDependentTypeParam(child))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Recursively checks if an expression contains a <see cref="TypeLiteralExpressionPrimaryNode"/>.
    /// </summary>
    private static bool ExpressionContainsTypeLiteral(ExpressionNode expr)
    {
        // Walk the expression tree: Assignment → LogicalOr → ... → Unary → Postfix → Primary
        var assignment = expr.Assignment;
        return AssignmentContainsTypeLiteral(assignment);
    }

    private static bool AssignmentContainsTypeLiteral(AssignmentNode assignment)
    {
        if (LogicalOrContainsTypeLiteral(assignment.Target))
            return true;
        if (assignment.Value is not null)
            return AssignmentContainsTypeLiteral(assignment.Value);
        return false;
    }

    private static bool LogicalOrContainsTypeLiteral(LogicalOrNode logicalOr)
    {
        return LogicalAndContainsTypeLiteral(logicalOr.Left);
    }

    private static bool LogicalAndContainsTypeLiteral(LogicalAndNode logicalAnd)
    {
        return EqualityContainsTypeLiteral(logicalAnd.Left);
    }

    private static bool EqualityContainsTypeLiteral(EqualityNode equality)
    {
        return RelationalContainsTypeLiteral(equality.Left);
    }

    private static bool RelationalContainsTypeLiteral(RelationalNode relational)
    {
        return relational switch
        {
            RelationalComparisonRelationalNode comp => AdditiveContainsTypeLiteral(comp.Left),
            _ => false,
        };
    }

    private static bool AdditiveContainsTypeLiteral(AdditiveNode additive)
    {
        return MultiplicativeContainsTypeLiteral(additive.Left);
    }

    private static bool MultiplicativeContainsTypeLiteral(MultiplicativeNode multiplicative)
    {
        return UnaryContainsTypeLiteral(multiplicative.Left);
    }

    private static bool UnaryContainsTypeLiteral(UnaryNode unary)
    {
        return unary switch
        {
            PostfixExpressionUnaryNode postfix => PostfixContainsTypeLiteral(postfix.Primary),
            _ => false,
        };
    }

    private static bool PostfixContainsTypeLiteral(PostfixNode postfix)
    {
        return postfix.Target is TypeLiteralExpressionPrimaryNode;
    }

    /// <summary>
    /// Returns true if the given type reference represents an array type
    /// (i.e., has an ArrayRank suffix like <c>int[]</c> or <c>MyClass[]</c>).
    /// </summary>
    private static bool IsArrayTypeReference(TypeReferenceNode typeRef)
    {
        return typeRef switch
        {
            PrimitiveTypeRefTypeReferenceNode primitiveRef => primitiveRef.ArrayRank.HasValue,
            NamedTypeRefTypeReferenceNode namedRef => namedRef.ArrayRank.HasValue,
            _ => false,
        };
    }

    /// <summary>
    /// Emits the element type name for an array type reference (without the <c>[]</c> suffix).
    /// Used to produce the type argument in <c>Array.Empty&lt;T&gt;()</c> emission.
    /// </summary>
    private string EmitArrayElementTypeName(TypeReferenceNode typeRef)
    {
        return typeRef switch
        {
            PrimitiveTypeRefTypeReferenceNode primitiveRef => EmitPrimitiveType(primitiveRef.PrimitiveType),
            NamedTypeRefTypeReferenceNode namedRef => ExtractQualifiedName(namedRef.Name) + EmitTypeArgumentList(namedRef.TypeArgs),
            _ => "object",
        };
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

                // ── type-aliases feature: transparent resolution (Requirement 6.1) ──
                // If this named type reference resolves to a type alias, emit the
                // underlying type instead of the alias name. Type aliases are erased
                // during transpilation — no alias name should appear in C# output.
                // Check the expression type map first, then fall back to symbol lookup.
                var resolvedType = _model.GetTypeOf(namedRef);
                if (resolvedType is TypeAliasType aliasFromExprMap)
                {
                    var emittedType = ResolveAliasForEmission(aliasFromExprMap, namedRef, isValueContext);
                    return namedRef.ArrayRank.HasValue ? $"{emittedType}[]" : emittedType;
                }
                // ── refinement-types feature: type erasure (Requirement 12.1) ──
                // Refinement types are erased to their base type in C#. For example,
                // `AdultAge x = 20;` emits as `int x = 20;`. The refinement predicate
                // is enforced by the type checker — no alias name appears in C# output.
                if (resolvedType is RefinementType refinementFromExprMap)
                {
                    var emittedType = CSharpTypeMap.GetTypeName(refinementFromExprMap.BaseType, isValueContext);
                    return namedRef.ArrayRank.HasValue ? $"{emittedType}[]" : emittedType;
                }
                // ── Union type alias: emit as the helper struct name ──
                // When a type alias resolves to a UnionType (e.g., `type AgeRange = int >= 3 | byte >= 0`),
                // emit the generated helper type name (e.g., `Union_Byte_Int32`).
                if (resolvedType is UnionType unionFromExprMap)
                {
                    var emittedType = UnionHelperGenerator.GetHelperTypeName(unionFromExprMap);
                    return namedRef.ArrayRank.HasValue ? $"{emittedType}[]" : emittedType;
                }
                // Fallback: search all symbols for a type alias with this name.
                // Only apply if no concrete type (class/struct/interface/newtype) with the
                // same name exists in the current namespace — prevents cross-namespace
                // alias collisions when a class and a type alias share the same simple
                // name in different namespaces compiled together.
                if (resolvedType is null)
                {
                    var aliasSymbol = _model.AllSymbols
                        .FirstOrDefault(s => s.Name == name && s.Kind == SymbolKind.TypeAlias);
                    if (aliasSymbol?.DeclaredType is TypeAliasType aliasType)
                    {
                        // Check that no concrete type with this name exists in the
                        // current namespace (which would take precedence over an alias
                        // from a different namespace). Newtypes are checked by scope
                        // since NewtypeType does not carry a ContainingNamespace property.
                        var hasConcreteTypeInCurrentNamespace = _currentNamespace is not null
                            && _model.AllSymbols.Any(s => s.Name == name
                                && s.Kind is SymbolKind.Class or SymbolKind.Struct or SymbolKind.Interface
                                && GetSymbolContainingNamespace(s) == _currentNamespace);

                        // Also check for newtypes: a newtype with the same name in any
                        // namespace takes precedence over a type alias from a different
                        // namespace, since newtypes are nominal (not transparent).
                        if (!hasConcreteTypeInCurrentNamespace && _currentNamespace is not null)
                        {
                            hasConcreteTypeInCurrentNamespace = _model.AllSymbols.Any(s =>
                                s.Name == name && s.Kind == SymbolKind.Newtype
                                && GetNewtypeContainingNamespace(s) == _currentNamespace);
                        }
                        if (!hasConcreteTypeInCurrentNamespace)
                        {
                            var emittedType = ResolveAliasForEmission(aliasType, namedRef, isValueContext);
                            return namedRef.ArrayRank.HasValue ? $"{emittedType}[]" : emittedType;
                        }
                    }
                    // Refinement type alias fallback: same logic as TypeAliasType above.
                    if (aliasSymbol?.DeclaredType is RefinementType refinementType)
                    {
                        var hasConcreteTypeInCurrentNamespace = _currentNamespace is not null
                            && _model.AllSymbols.Any(s => s.Name == name
                                && s.Kind is SymbolKind.Class or SymbolKind.Struct or SymbolKind.Interface
                                && GetSymbolContainingNamespace(s) == _currentNamespace);
                        if (!hasConcreteTypeInCurrentNamespace)
                        {
                            var emittedType = CSharpTypeMap.GetTypeName(refinementType.BaseType, isValueContext);
                            return namedRef.ArrayRank.HasValue ? $"{emittedType}[]" : emittedType;
                        }
                    }
                    // Union type alias fallback: emit as the helper struct name.
                    if (aliasSymbol?.DeclaredType is UnionType unionFallback)
                    {
                        var hasConcreteTypeInCurrentNamespace = _currentNamespace is not null
                            && _model.AllSymbols.Any(s => s.Name == name
                                && s.Kind is SymbolKind.Class or SymbolKind.Struct or SymbolKind.Interface
                                && GetSymbolContainingNamespace(s) == _currentNamespace);
                        if (!hasConcreteTypeInCurrentNamespace)
                        {
                            var emittedType = UnionHelperGenerator.GetHelperTypeName(unionFallback);
                            return namedRef.ArrayRank.HasValue ? $"{emittedType}[]" : emittedType;
                        }
                    }
                }

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

                // Check if this named type is a stdlib type that maps to a
                // C# fully-qualified name (e.g., "Type" → "System.Type").
                // This ensures stdlib types emit correctly without requiring
                // a `using` directive in the generated C#.
                var mappedName = CSharpTypeMap.TryGetMappedName(name);
                if (mappedName is not null && namedRef.TypeArgs is null)
                {
                    return namedRef.ArrayRank.HasValue ? $"{mappedName}[]" : mappedName;
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

                // Check if this named type reference is a dependent type parameter
                // (e.g., `N` in `FixedBuffer<N>` where N is a method-level dependent param).
                // Dependent type parameters are emitted with a T-prefix in C#.
                if (namedRef.TypeArgs is null && !namedRef.ArrayRank.HasValue)
                {
                    var resolvedNamedType = _model.GetTypeOf(namedRef);
                    if (resolvedNamedType is DependentTypeParameterType depParamType)
                    {
                        return $"T{depParamType.Name}";
                    }
                }

                var typeArgs = EmitTypeArgumentList(namedRef.TypeArgs, GetTypeParamsForName(name));
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
            case UnionTypeRefTypeReferenceNode unionRef:
            {
                // Union type references emit as the generated helper type name.
                // Collect constituent names matching UnionHelperGenerator.GetConstituentCSharpName,
                // sort alphabetically, and join with "_" to form "Union_{Type1}_{Type2}".
                var constituentNames = new List<string>();
                constituentNames.Add(GetConstituentNameForHelper(unionRef.UnionType.First));
                foreach (var rest in unionRef.UnionType.SingleTypeRefs)
                {
                    constituentNames.Add(GetConstituentNameForHelper(rest));
                }
                constituentNames.Sort(StringComparer.Ordinal);
                return "Union_" + string.Join("_", constituentNames);
            }
            default:
                return "object";
        }
    }

    /// <summary>
    /// Maps a <see cref="SingleTypeRefNode"/> to the constituent name used by
    /// <see cref="UnionHelperGenerator.GetHelperTypeName"/> for constructing
    /// the helper type name. This mirrors <c>GetConstituentCSharpName</c> but
    /// operates on AST nodes rather than semantic <see cref="IType"/> instances.
    /// </summary>
    private string GetConstituentNameForHelper(SingleTypeRefNode singleTypeRef)
    {
        switch (singleTypeRef)
        {
            case PrimitiveSingleTypeRefSingleTypeRefNode primitiveRef:
            {
                var name = GetPrimitiveDisplayName(primitiveRef.PrimitiveType);
                return primitiveRef.ArrayRank.HasValue ? $"{name}Array" : name;
            }
            case VoidSingleTypeRefSingleTypeRefNode:
                return "Void";
            case UnknownSingleTypeRefSingleTypeRefNode:
                return "Object";
            case NamedSingleTypeRefSingleTypeRefNode namedRef:
            {
                var name = ExtractQualifiedName(namedRef.Name);
                if (namedRef.TypeArgs is not null)
                {
                    var typeArgNames = new List<string>();
                    CollectTypeArgConstituentNames(namedRef.TypeArgs, typeArgNames);
                    name = $"{name}_{typeArgNames.Count}_{string.Join("_", typeArgNames)}";
                }
                return namedRef.ArrayRank.HasValue ? $"{name}Array" : name;
            }
            case ParenthesizedTypeRefSingleTypeRefNode parenRef:
            {
                // Parenthesized type refs contain an inner TypeReference.
                // For union helper naming, we recurse into the inner type.
                var innerName = GetConstituentNameFromTypeRef(parenRef.Inner);
                return parenRef.ArrayRank.HasValue ? $"{innerName}Array" : innerName;
            }
            case TupleSingleTypeRefSingleTypeRefNode:
                // Tuple types in union constituents use a generic ValueTuple name.
                return "ValueTuple";
            default:
                return "Object";
        }
    }

    /// <summary>
    /// Maps a <see cref="TypeReferenceNode"/> to a constituent name for the union
    /// helper type. Used when a parenthesized type ref contains an inner type reference.
    /// </summary>
    private string GetConstituentNameFromTypeRef(TypeReferenceNode typeRef)
    {
        switch (typeRef)
        {
            case PrimitiveTypeRefTypeReferenceNode primitiveRef:
            {
                var name = GetPrimitiveDisplayName(primitiveRef.PrimitiveType);
                return primitiveRef.ArrayRank.HasValue ? $"{name}Array" : name;
            }
            case VoidTypeRefTypeReferenceNode:
                return "Void";
            case UnknownTypeRefTypeReferenceNode:
                return "Object";
            case NamedTypeRefTypeReferenceNode namedRef:
            {
                var name = ExtractQualifiedName(namedRef.Name);
                if (namedRef.TypeArgs is not null)
                {
                    var typeArgNames = new List<string>();
                    CollectTypeArgConstituentNames(namedRef.TypeArgs, typeArgNames);
                    name = $"{name}_{typeArgNames.Count}_{string.Join("_", typeArgNames)}";
                }
                return namedRef.ArrayRank.HasValue ? $"{name}Array" : name;
            }
            case UnionTypeRefTypeReferenceNode unionRef:
            {
                // Nested union inside parenthesized ref — flatten by returning
                // the helper type name itself as the constituent name.
                var constituentNames = new List<string>();
                constituentNames.Add(GetConstituentNameForHelper(unionRef.UnionType.First));
                foreach (var rest in unionRef.UnionType.SingleTypeRefs)
                {
                    constituentNames.Add(GetConstituentNameForHelper(rest));
                }
                constituentNames.Sort(StringComparer.Ordinal);
                return "Union_" + string.Join("_", constituentNames);
            }
            default:
                return "Object";
        }
    }

    /// <summary>
    /// Collects constituent names from a type argument list for use in helper type naming.
    /// Handles both type reference arguments and literal dependent type arguments.
    /// </summary>
    private void CollectTypeArgConstituentNames(TypeArgumentListNode typeArgs, List<string> names)
    {
        // TypeArgumentList has TypeArgument (first) and TypeArguments (rest).
        names.Add(GetConstituentNameFromTypeArgument(typeArgs.TypeArgument));
        foreach (var additional in typeArgs.TypeArguments)
        {
            names.Add(GetConstituentNameFromTypeArgument(additional));
        }
    }

    /// <summary>
    /// Gets the constituent name from a type argument node for union helper type naming.
    /// For type reference arguments, delegates to <see cref="GetConstituentNameFromTypeRef"/>.
    /// For literal dependent type arguments, returns the carrier struct name.
    /// </summary>
    private string GetConstituentNameFromTypeArgument(TypeArgumentNode arg)
    {
        if (arg is TypeReferenceTypeArgumentTypeArgumentNode typeRefArg)
            return GetConstituentNameFromTypeRef(typeRefArg.Type);

        // Literal type arguments — use the carrier struct name as the constituent name.
        return EmitSingleTypeArgument(arg);
    }

    /// <summary>
    /// Maps a <see cref="PrimitiveTypeNode"/> to its stdlib DisplayName
    /// (e.g., <c>int</c> → <c>Int32</c>, <c>string</c> → <c>String</c>).
    /// This matches the names used by <see cref="UnionHelperGenerator.GetConstituentCSharpName"/>.
    /// </summary>
    private static string GetPrimitiveDisplayName(PrimitiveTypeNode primitiveType)
    {
        return primitiveType switch
        {
            IntTypePrimitiveTypeNode => "Int32",
            UintTypePrimitiveTypeNode => "UInt32",
            LongTypePrimitiveTypeNode => "Int64",
            UlongTypePrimitiveTypeNode => "UInt64",
            ShortTypePrimitiveTypeNode => "Int16",
            UshortTypePrimitiveTypeNode => "UInt16",
            ByteTypePrimitiveTypeNode => "Byte",
            SbyteTypePrimitiveTypeNode => "SByte",
            FloatTypePrimitiveTypeNode => "Single",
            DoubleTypePrimitiveTypeNode => "Double",
            DecimalTypePrimitiveTypeNode => "Decimal",
            BoolTypePrimitiveTypeNode => "Boolean",
            CharTypePrimitiveTypeNode => "Char",
            StringTypePrimitiveTypeNode => "String",
            RegexTypePrimitiveTypeNode => "Regex",
            _ => "Object",
        };
    }

    /// <summary>
    /// Emits a tuple type reference as a <c>System.ValueTuple&lt;T1, T2, ...&gt;</c>
    /// generic form, stripping any tuple-element names. This is the variant
    /// used by <see cref="EmitPrimaryPattern"/> for type patterns against a
    /// tuple type: C# treats <c>is (T1, T2) name</c> as a positional
    /// deconstruction pattern (requiring <c>Deconstruct</c> on the
    /// scrutinee), whereas <c>is ValueTuple&lt;T1, T2&gt; name</c> is an
    /// unambiguous type pattern that works against any scrutinee including
    /// <c>object</c>. Element names are meaningless in a pattern position,
    /// so dropping them produces the correct C# with no loss of
    /// information.
    /// </summary>
    private string EmitTupleTypePatternType(TupleTypeRefTypeReferenceNode tupleRef)
    {
        var parts = new List<string>();
        parts.Add(EmitTypeReference(tupleRef.First.Type));
        parts.Add(EmitTypeReference(tupleRef.Rest.Type));
        foreach (var additional in tupleRef.TupleTypeElements)
        {
            parts.Add(EmitTypeReference(additional.Type));
        }
        return $"System.ValueTuple<{string.Join(", ", parts)}>";
    }

    /// <summary>
    /// Returns <c>true</c> if any element in <paramref name="tupleRef"/>
    /// carries an identifier name (e.g., <c>(int a, int b)</c>). Used by
    /// <see cref="EmitPrimaryPattern"/> to decide whether to emit a
    /// chained deconstruction pattern that introduces the element names
    /// as locals.
    /// </summary>
    private static bool TupleTypeRefHasAnyElementName(TupleTypeRefTypeReferenceNode tupleRef)
    {
        if (tupleRef.First.Name.HasValue) return true;
        if (tupleRef.Rest.Name.HasValue) return true;
        foreach (var additional in tupleRef.TupleTypeElements)
        {
            if (additional.Name.HasValue) return true;
        }
        return false;
    }

    /// <summary>
    /// Emits a C# positional deconstruction pattern for a named tuple
    /// type reference, including the element identifier names as
    /// binding targets. For example, <c>(int a, int b)</c> emits as
    /// <c>(int a, int b)</c> — the same surface syntax, but produced
    /// from the resolved AST rather than the raw source text so the
    /// emitter is faithful to type-map rewrites (e.g., an element
    /// typed <c>unknown</c> lowers to <c>object</c>). Used as the
    /// second stage of the chained emission for tuple type patterns
    /// against non-tuple scrutinees: the first stage narrows to
    /// <c>System.ValueTuple&lt;...&gt;</c>, and this form deconstructs
    /// the narrowed binding so C# introduces the element names in the
    /// enclosing scope.
    /// </summary>
    private string EmitTupleDeconstructionPattern(TupleTypeRefTypeReferenceNode tupleRef)
    {
        string FormatElement(TupleTypeElementNode element)
        {
            var elementType = EmitTypeReference(element.Type);
            return element.Name.HasValue
                ? $"{elementType} {element.Name.Value.Lexeme}"
                : elementType;
        }

        var parts = new List<string>();
        parts.Add(FormatElement(tupleRef.First));
        parts.Add(FormatElement(tupleRef.Rest));
        foreach (var additional in tupleRef.TupleTypeElements)
        {
            parts.Add(FormatElement(additional));
        }
        return $"({string.Join(", ", parts)})";
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
            RegexTypePrimitiveTypeNode => "System.Text.RegularExpressions.Regex",
            _ => "object",
        };
    }

    private void EmitBlock(BlockNode block)
    {
        AppendLine("{");
        _indentLevel++;
        EmitBlockStatements(block);
        _indentLevel--;
        AppendLine("}");
    }

    /// <summary>
    /// Emits the statements within a block (including pending union pattern bindings)
    /// without emitting the surrounding braces or adjusting indentation.
    /// </summary>
    private void EmitBlockStatements(BlockNode block)
    {
        // Inject any pending union pattern variable bindings at the start of the block.
        // These are queued by TryEmitUnionPatternMatch when emitting `if (x.IsT)` conditions.
        if (_pendingUnionPatternBindings.Count > 0)
        {
            foreach (var binding in _pendingUnionPatternBindings)
            {
                AppendLine(binding);
            }
            _pendingUnionPatternBindings.Clear();
        }

        EmitStatementsFromIndex(block.Statements, 0);
    }

    /// <summary>
    /// Emits statements starting from the given index. When a context declaration
    /// statement is encountered, it emits the declaration/push, wraps the remaining
    /// statements in a try/finally block, and pops in the finally clause.
    /// Multiple context declarations in the same scope nest try/finally blocks
    /// so that pops happen in reverse declaration order.
    /// </summary>
    private void EmitStatementsFromIndex(ImmutableArray<StatementNode> statements, int startIndex)
    {
        for (var i = startIndex; i < statements.Length; i++)
        {
            var stmt = statements[i];

            // Check if this statement is a context declaration that needs try/finally wrapping.
            var contextTypeName = TryGetContextDeclarationTypeName(stmt);
            if (contextTypeName is not null)
            {
                EmitContextDeclarationWithWrapping(stmt, contextTypeName, statements, i + 1);
                return; // Remaining statements are emitted inside the try block.
            }

            EmitStatement(stmt);
        }
    }

    /// <summary>
    /// Emits a context declaration statement followed by a try/finally block that
    /// wraps all remaining statements in the scope. The finally clause pops the
    /// context value.
    /// </summary>
    private void EmitContextDeclarationWithWrapping(
        StatementNode stmt,
        string contextTypeName,
        ImmutableArray<StatementNode> statements,
        int nextIndex)
    {
        switch (stmt)
        {
            case WithVarDeclarationStatementStatementNode withVarStmt:
            {
                var decl = withVarStmt.WithVarDeclaration;
                var name = decl.Name.Lexeme;
                var init = EmitExpression(decl.Initializer);
                AppendLine($"var {name} = {init};");
                AppendLine($"MistContext.Push<{contextTypeName}>({name});");
                break;
            }
            case WithTypedDeclarationStatementStatementNode withTypedStmt:
            {
                var decl = withTypedStmt.WithTypedDeclaration;
                var typeName = EmitTypeReference(decl.Type);
                var name = decl.Name.Lexeme;
                var init = EmitExpression(decl.Initializer);
                AppendLine($"{typeName} {name} = {init};");
                AppendLine($"MistContext.Push<{contextTypeName}>({name});");
                break;
            }
            case WithDiscardDeclarationStatementStatementNode withDiscardStmt:
            {
                var decl = withDiscardStmt.WithDiscardDeclaration;
                var init = EmitExpression(decl.Initializer);
                AppendLine($"MistContext.Push<{contextTypeName}>({init});");
                break;
            }
            case WithPushStatementStatementNode withPushStmt:
            {
                var value = EmitExpression(withPushStmt.WithPushStatement.Value);
                AppendLine($"MistContext.Push<{contextTypeName}>({value});");
                break;
            }
        }

        AppendLine("try");
        AppendLine("{");
        _indentLevel++;
        EmitStatementsFromIndex(statements, nextIndex);
        _indentLevel--;
        AppendLine("}");
        AppendLine("finally");
        AppendLine("{");
        _indentLevel++;
        AppendLine($"MistContext.Pop<{contextTypeName}>();");
        _indentLevel--;
        AppendLine("}");
    }

    /// <summary>
    /// Attempts to resolve the C# type name for a context declaration statement.
    /// Returns null if the statement is not a context declaration.
    /// </summary>
    private string? TryGetContextDeclarationTypeName(StatementNode stmt)
    {
        switch (stmt)
        {
            case WithVarDeclarationStatementStatementNode withVarStmt:
            {
                // Type is inferred from the initializer expression.
                var type = _model.GetTypeOf(withVarStmt.WithVarDeclaration.Initializer);
                return type is not null ? CSharpTypeMap.GetTypeName(ResolveContextTypeForEmission(type)) : "object";
            }
            case WithTypedDeclarationStatementStatementNode withTypedStmt:
            {
                // Type is the explicit type reference.
                var type = _model.GetTypeOf(withTypedStmt.WithTypedDeclaration.Type);
                if (type is not null)
                    return CSharpTypeMap.GetTypeName(ResolveContextTypeForEmission(type));
                // Fall back to the declared symbol's type.
                var sym = _model.GetDeclaredSymbol(withTypedStmt.WithTypedDeclaration);
                return sym?.DeclaredType is not null
                    ? CSharpTypeMap.GetTypeName(ResolveContextTypeForEmission(sym.DeclaredType))
                    : "object";
            }
            case WithDiscardDeclarationStatementStatementNode withDiscardStmt:
            {
                // Type is the explicit type reference.
                var type = _model.GetTypeOf(withDiscardStmt.WithDiscardDeclaration.Type);
                if (type is not null)
                    return CSharpTypeMap.GetTypeName(ResolveContextTypeForEmission(type));
                // Fall back to the initializer type.
                var initType = _model.GetTypeOf(withDiscardStmt.WithDiscardDeclaration.Initializer);
                return initType is not null
                    ? CSharpTypeMap.GetTypeName(ResolveContextTypeForEmission(initType))
                    : "object";
            }
            case WithPushStatementStatementNode withPushStmt:
            {
                // Type is inferred from the expression.
                var type = _model.GetTypeOf(withPushStmt.WithPushStatement.Value);
                return type is not null ? CSharpTypeMap.GetTypeName(ResolveContextTypeForEmission(type)) : "object";
            }
            default:
                return null;
        }
    }

    /// <summary>
    /// Resolves type aliases for context type emission. Type aliases are resolved
    /// to their underlying type before being used as the type argument for
    /// MistContext.Push/Pop/Peek.
    /// </summary>
    private static IType ResolveContextTypeForEmission(IType type)
    {
        while (type is TypeAliasType alias)
        {
            type = alias.UnderlyingType;
        }
        return type;
    }

    /// <summary>
    /// Emits a <c>with(expr) { ... }</c> context block as:
    /// <code>
    /// MistContext.Push&lt;T&gt;(expr);
    /// try {
    ///     // ... block body ...
    /// } finally {
    ///     MistContext.Pop&lt;T&gt;();
    /// }
    /// </code>
    /// </summary>
    private void EmitWithBlockStatement(WithBlockStatementNode withBlock)
    {
        var expr = EmitExpression(withBlock.Expression);
        var type = _model.GetTypeOf(withBlock.Expression);
        var typeName = type is not null
            ? CSharpTypeMap.GetTypeName(ResolveContextTypeForEmission(type))
            : "object";

        AppendLine($"MistContext.Push<{typeName}>({expr});");
        AppendLine("try");
        AppendLine("{");
        _indentLevel++;
        EmitBlockStatements(withBlock.Body);
        _indentLevel--;
        AppendLine("}");
        AppendLine("finally");
        AppendLine("{");
        _indentLevel++;
        AppendLine($"MistContext.Pop<{typeName}>();");
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
            // Task 12.5 — Readonly blocks emit as regular blocks (no C# equivalent).
            case ReadonlyBlockStatementStatementNode readonlyBlockStmt:
                EmitBlock(readonlyBlockStmt.ReadonlyBlock.Body);
                break;
            case WithBlockStatementStatementNode withBlockStmt:
                EmitWithBlockStatement(withBlockStmt.WithBlockStatement);
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

                // When the declared type is an array type, set the target element type
                // so that array literal emission produces `new T[] { ... }` instead of `new[] { ... }`.
                var declaredType = _model.GetDeclaredSymbol(typed)?.DeclaredType;
                if (declaredType is ArrayType targetArrayType)
                    _arrayLiteralTargetElementType = targetArrayType.ElementType;

                var init = EmitExpression(typed.Initializer);
                _arrayLiteralTargetElementType = null;

                init = WrapWithImplicitCastIfNeeded(typed.Initializer, init);
                // Task 31.1 — covariant unknown widening at the typed local
                // declaration's assignment site. The declared type is
                // recorded on the local variable's symbol; the initializer
                // expression type comes from the expression-type map.
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
            case ForInitializerVarDeclarationForInitializerNode varDeclInit:
            {
                var name = varDeclInit.Name.Lexeme;
                var init = EmitExpression(varDeclInit.Initializer);
                return $"var {name} = {init}";
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
                var scrutineeType = _model.GetTypeOf(isExpr.Left);

                // Union type pattern matching: emit Is{T}/As{T} property access
                // instead of C#'s native `is T t` pattern.
                if (scrutineeType is UnionType unionType)
                {
                    var unionResult = TryEmitUnionPatternMatch(isExpr.Pattern, unionType, left);
                    if (unionResult is not null)
                        return unionResult;
                }

                // Refinement type pattern matching: emit runtime predicate check
                // instead of C#'s native `is T t` pattern. Refinement types are
                // erased at runtime, so `x is AdultAge age` becomes a predicate
                // condition (e.g., `x >= 18`) with a variable binding queued for
                // the then-branch.
                var refinementResult = TryEmitRefinementPatternMatch(isExpr.Pattern, left);
                if (refinementResult is not null)
                    return refinementResult;

                // Enum member pattern matching: emit value equality check
                // (`scrutinee == EnumName.MemberName`) instead of C#'s `is` pattern.
                var enumResult = TryEmitEnumPatternMatch(isExpr.Pattern, left);
                if (enumResult is not null)
                    return enumResult;

                // Regex pattern matching: emit Regex.IsMatch(scrutinee, "^(?:pattern)$")
                // instead of C#'s native `is` pattern (C# doesn't support regex patterns).
                var regexResult = TryEmitRegexPatternMatch(isExpr.Pattern, left);
                if (regexResult is not null)
                    return regexResult;

                var pattern = EmitPattern(isExpr.Pattern, scrutineeType);
                return $"{left} is {pattern}";
            }

            default:
                return "";
        }
    }

    // ─── Union Pattern Matching ──────────────────────────────────────────

    /// <summary>
    /// Attempts to emit a union-specific pattern match. For type patterns
    /// (<c>is T t</c>) on a union scrutinee, emits <c>scrutinee.Is{T}</c> as
    /// the condition and queues <c>var t = scrutinee.As{T}</c> as a pending
    /// binding to be injected at the start of the then-branch.
    /// For var patterns (<c>is var v</c>), emits <c>true</c> (always matches)
    /// and queues <c>var v = scrutinee</c>.
    /// Returns <c>null</c> if the pattern is not a simple type or var pattern
    /// (e.g., or-patterns, and-patterns), in which case the caller falls through
    /// to standard emission.
    /// </summary>
    private string? TryEmitUnionPatternMatch(PatternNode pattern, UnionType unionType, string scrutineeExpr)
    {
        // Navigate the pattern chain: Pattern → OrPattern → AndPattern → UnaryPattern → PrimaryPattern
        // Only handle simple (non-compound) patterns for union emission.
        var orPattern = pattern.OrPattern;
        if (orPattern.Right is not null && !ReferenceEquals(orPattern.Right, orPattern.Left))
            return null; // or-pattern — fall through to standard emission

        var andPattern = orPattern.Left;
        if (andPattern.Right is not null && !ReferenceEquals(andPattern.Right, andPattern.Left))
            return null; // and-pattern — fall through to standard emission

        var unaryPattern = andPattern.Left;
        if (unaryPattern is not DirectPatternUnaryPatternNode directPattern)
            return null; // not-pattern — fall through to standard emission

        var primaryPattern = directPattern.Primary;

        switch (primaryPattern)
        {
            case TypePatternPrimaryPatternNode typePattern:
            {
                // Resolve the pattern type to find the matching constituent.
                // The semantic model may store the type on the type reference node
                // or on the pattern's declared symbol (the introduced variable).
                var patternType = _model.GetTypeOf(typePattern.Type);
                patternType ??= _model.GetDeclaredSymbol(typePattern)?.DeclaredType;
                if (patternType is null)
                    return null;

                // Find the constituent that matches the pattern type.
                var constituentName = FindConstituentName(patternType, unionType);
                if (constituentName is null)
                    return null; // Not a constituent — emit standard (dead code per type checker warning)

                var bindingName = typePattern.Name.Lexeme;
                var csharpTypeName = CSharpTypeMap.GetTypeName(patternType);

                // Queue the variable binding for injection into the then-branch.
                if (bindingName != "_")
                {
                    _pendingUnionPatternBindings.Add(
                        $"{csharpTypeName} {bindingName} = {scrutineeExpr}.As{constituentName};");
                }

                return $"{scrutineeExpr}.Is{constituentName}";
            }

            case VarPatternPrimaryPatternNode varPattern:
            {
                var bindingName = varPattern.Name.Lexeme;
                var helperTypeName = UnionHelperGenerator.GetHelperTypeName(unionType);

                // Queue the variable binding for injection into the then-branch.
                if (bindingName != "_")
                {
                    _pendingUnionPatternBindings.Add(
                        $"{helperTypeName} {bindingName} = {scrutineeExpr};");
                }

                // var pattern always matches — emit `true` so the if-condition is trivially true.
                return "true";
            }

            case DiscardPatternPrimaryPatternNode:
            {
                // Bare discard pattern always matches — emit `true` so the if-condition is trivially true.
                return "true";
            }

            default:
                return null;
        }
    }

    /// <summary>
    /// Finds the constituent C# name (e.g., "Int32", "String") for a pattern type
    /// within a union type. Returns <c>null</c> if the type is not a constituent.
    /// </summary>
    private static string? FindConstituentName(IType patternType, UnionType unionType)
    {
        foreach (var constituent in unionType.Constituents)
        {
            if (constituent.DisplayName == patternType.DisplayName)
            {
                return UnionHelperGenerator.GetConstituentCSharpName(constituent);
            }
        }
        return null;
    }

    // ─── Union Switch Arm Pattern Emission ───────────────────────────────

    /// <summary>
    /// Attempts to emit a switch arm pattern for a union type scrutinee using
    /// the union's discriminator properties (<c>Is{T}</c> / <c>As{T}</c>).
    /// <para>
    /// For type patterns (e.g., <c>int n</c>), emits a C# property pattern:
    /// <c>{ IsInt32: true, AsInt32: var n }</c>.
    /// </para>
    /// <para>
    /// For bare type patterns (e.g., <c>int</c>), emits:
    /// <c>{ IsInt32: true }</c>.
    /// </para>
    /// <para>
    /// For var and discard patterns, returns <c>null</c> to fall through to
    /// standard emission (they work directly against the union helper type).
    /// </para>
    /// Returns <c>null</c> if the pattern cannot be emitted as a union property
    /// pattern (compound patterns, non-type patterns, etc.).
    /// </summary>
    private string? TryEmitUnionSwitchArmPattern(PatternNode pattern, UnionType unionType, string scrutineeExpr)
    {
        // Navigate the pattern chain: Pattern → OrPattern → AndPattern → UnaryPattern → PrimaryPattern
        // Only handle simple (non-compound) patterns for union emission.
        var orPattern = pattern.OrPattern;
        if (orPattern.Right is not null && !ReferenceEquals(orPattern.Right, orPattern.Left))
            return null; // or-pattern — fall through to standard emission

        var andPattern = orPattern.Left;
        if (andPattern.Right is not null && !ReferenceEquals(andPattern.Right, andPattern.Left))
            return null; // and-pattern — fall through to standard emission

        var unaryPattern = andPattern.Left;
        if (unaryPattern is not DirectPatternUnaryPatternNode directPattern)
            return null; // not-pattern — fall through to standard emission

        var primaryPattern = directPattern.Primary;

        switch (primaryPattern)
        {
            case TypePatternPrimaryPatternNode typePattern:
            {
                // Resolve the pattern type to find the matching constituent.
                var patternType = _model.GetTypeOf(typePattern.Type);
                patternType ??= _model.GetDeclaredSymbol(typePattern)?.DeclaredType;
                if (patternType is null)
                    return null;

                var constituentName = FindConstituentName(patternType, unionType);
                if (constituentName is null)
                    return null;

                var bindingName = typePattern.Name.Lexeme;

                // Emit property pattern: { Is{T}: true, As{T}: var name }
                if (bindingName != "_")
                {
                    return $"{{ Is{constituentName}: true, As{constituentName}: var {bindingName} }}";
                }

                // No binding needed — just check the discriminator.
                return $"{{ Is{constituentName}: true }}";
            }

            case BareTypePatternPrimaryPatternNode bareTypePattern:
            {
                // Resolve the pattern type to find the matching constituent.
                var patternType = _model.GetTypeOf(bareTypePattern.Type);
                if (patternType is null)
                    return null;

                var constituentName = FindConstituentName(patternType, unionType);
                if (constituentName is null)
                    return null;

                // Emit property pattern: { Is{T}: true }
                return $"{{ Is{constituentName}: true }}";
            }

            default:
                // var patterns, discard patterns, constant patterns, etc.
                // fall through to standard emission.
                return null;
        }
    }

    // ─── Refinement Type Pattern Matching ────────────────────────────────

    /// <summary>
    /// Attempts to emit a refinement-type-specific pattern match. For type patterns
    /// (<c>is AdultAge age</c>) where the resolved type is a <see cref="RefinementType"/>,
    /// emits the refinement predicate as a C# conditional expression (e.g., <c>x >= 18</c>)
    /// and queues <c>var age = x;</c> as a pending binding to be injected at the start
    /// of the then-branch.
    /// <para>
    /// For bare type patterns (<c>is AdultAge</c>), emits only the predicate condition
    /// without a variable binding.
    /// </para>
    /// Returns <c>null</c> if the pattern does not reference a refinement type,
    /// in which case the caller falls through to standard emission.
    /// </summary>
    private string? TryEmitRefinementPatternMatch(PatternNode pattern, string scrutineeExpr)
    {
        // Navigate the pattern chain: Pattern → OrPattern → AndPattern → UnaryPattern → PrimaryPattern
        // Only handle simple (non-compound) patterns for refinement emission.
        var orPattern = pattern.OrPattern;
        if (orPattern.Right is not null && !ReferenceEquals(orPattern.Right, orPattern.Left))
            return null; // or-pattern — fall through to standard emission

        var andPattern = orPattern.Left;
        if (andPattern.Right is not null && !ReferenceEquals(andPattern.Right, andPattern.Left))
            return null; // and-pattern — fall through to standard emission

        var unaryPattern = andPattern.Left;
        if (unaryPattern is not DirectPatternUnaryPatternNode directPattern)
            return null; // not-pattern — fall through to standard emission

        var primaryPattern = directPattern.Primary;

        switch (primaryPattern)
        {
            case TypePatternPrimaryPatternNode typePattern:
            {
                // Resolve the pattern type to check if it's a refinement type.
                var patternType = _model.GetTypeOf(typePattern.Type);
                patternType ??= _model.GetDeclaredSymbol(typePattern)?.DeclaredType;
                if (patternType is not RefinementType refinementType)
                    return null;

                var bindingName = typePattern.Name.Lexeme;
                var condition = EmitRefinementPredicateCondition(refinementType.Predicate, scrutineeExpr);

                // Queue the variable binding for injection into the then-branch.
                if (bindingName != "_")
                {
                    var csharpTypeName = CSharpTypeMap.GetTypeName(refinementType.BaseType);
                    _pendingUnionPatternBindings.Add(
                        $"{csharpTypeName} {bindingName} = {scrutineeExpr};");
                }

                return condition;
            }

            case BareTypePatternPrimaryPatternNode bareTypePattern:
            {
                // Resolve the pattern type to check if it's a refinement type.
                var patternType = _model.GetTypeOf(bareTypePattern.Type);
                if (patternType is not RefinementType refinementType)
                    return null;

                return EmitRefinementPredicateCondition(refinementType.Predicate, scrutineeExpr);
            }

            default:
                return null;
        }
    }

    /// <summary>
    /// Attempts to emit a switch arm pattern for a refinement type scrutinee.
    /// For type patterns (e.g., <c>AdultAge age</c>), emits a C# pattern with
    /// a <c>when</c> guard containing the refinement predicate:
    /// <c>var age when age >= 18</c>.
    /// <para>
    /// For bare type patterns (e.g., <c>AdultAge</c>), emits:
    /// <c>var __refinement when __refinement >= 18</c>.
    /// </para>
    /// Returns <c>null</c> if the pattern does not reference a refinement type.
    /// </summary>
    private string? TryEmitRefinementSwitchArmPattern(PatternNode pattern, string scrutineeExpr)
    {
        // Navigate the pattern chain: Pattern → OrPattern → AndPattern → UnaryPattern → PrimaryPattern
        var orPattern = pattern.OrPattern;
        if (orPattern.Right is not null && !ReferenceEquals(orPattern.Right, orPattern.Left))
            return null;

        var andPattern = orPattern.Left;
        if (andPattern.Right is not null && !ReferenceEquals(andPattern.Right, andPattern.Left))
            return null;

        var unaryPattern = andPattern.Left;
        if (unaryPattern is not DirectPatternUnaryPatternNode directPattern)
            return null;

        var primaryPattern = directPattern.Primary;

        switch (primaryPattern)
        {
            case TypePatternPrimaryPatternNode typePattern:
            {
                var patternType = _model.GetTypeOf(typePattern.Type);
                patternType ??= _model.GetDeclaredSymbol(typePattern)?.DeclaredType;
                if (patternType is not RefinementType refinementType)
                    return null;

                var bindingName = typePattern.Name.Lexeme;
                var condition = EmitRefinementPredicateCondition(refinementType.Predicate, bindingName);
                return $"var {bindingName} when {condition}";
            }

            case BareTypePatternPrimaryPatternNode bareTypePattern:
            {
                var patternType = _model.GetTypeOf(bareTypePattern.Type);
                if (patternType is not RefinementType refinementType)
                    return null;

                // Use a synthetic variable name for the guard condition.
                var condition = EmitRefinementPredicateCondition(refinementType.Predicate, scrutineeExpr);
                return $"_ when {condition}";
            }

            default:
                return null;
        }
    }

    /// <summary>
    /// Converts a <see cref="RefinementPredicate"/> into a C# conditional expression
    /// string by walking the predicate's AST and substituting the scrutinee expression
    /// for the implicit subject.
    /// <para>
    /// For simple relational predicates (e.g., <c>>= 18</c>), emits <c>scrutinee >= 18</c>.
    /// For compound predicates (e.g., <c>>= 0 and &lt;= 100</c>), emits
    /// <c>scrutinee >= 0 &amp;&amp; scrutinee &lt;= 100</c>.
    /// For property patterns (e.g., <c>{ Age: >= 18 }</c>), emits
    /// <c>scrutinee.Age >= 18</c>.
    /// </para>
    /// </summary>
    private string EmitRefinementPredicateCondition(RefinementPredicate predicate, string scrutineeExpr)
    {
        var parts = new List<string>();

        EmitUnaryPatternCondition(predicate.AstNode.Left, scrutineeExpr, parts);

        if (predicate.AstNode.Right is not null &&
            !ReferenceEquals(predicate.AstNode.Right, predicate.AstNode.Left))
        {
            EmitUnaryPatternCondition(predicate.AstNode.Right, scrutineeExpr, parts);
        }

        if (!predicate.AstNode.UnaryPatterns.IsDefaultOrEmpty)
        {
            foreach (var part in predicate.AstNode.UnaryPatterns)
            {
                EmitUnaryPatternCondition(part, scrutineeExpr, parts);
            }
        }

        return string.Join(" && ", parts);
    }

    /// <summary>
    /// Emits a single unary pattern as a C# condition fragment and adds it to the parts list.
    /// </summary>
    private void EmitUnaryPatternCondition(UnaryPatternNode unary, string scrutineeExpr, List<string> parts)
    {
        switch (unary)
        {
            case DirectPatternUnaryPatternNode direct:
                EmitPrimaryPatternCondition(direct.Primary, scrutineeExpr, parts);
                break;
            case NotPatternUnaryPatternNode not:
            {
                var innerParts = new List<string>();
                EmitPrimaryPatternCondition(not.Operand, scrutineeExpr, innerParts);
                if (innerParts.Count == 1)
                    parts.Add($"!({innerParts[0]})");
                else
                    parts.Add($"!({string.Join(" && ", innerParts)})");
                break;
            }
        }
    }

    /// <summary>
    /// Emits a primary pattern as a C# condition fragment. Handles relational patterns,
    /// constant patterns, and property patterns.
    /// </summary>
    private void EmitPrimaryPatternCondition(PrimaryPatternNode primary, string scrutineeExpr, List<string> parts)
    {
        switch (primary)
        {
            case GreaterThanOrEqualRelationalPatternPrimaryPatternNode gte:
                parts.Add($"{scrutineeExpr} >= {EmitExpression(gte.Constant)}");
                break;
            case LessThanOrEqualRelationalPatternPrimaryPatternNode lte:
                parts.Add($"{scrutineeExpr} <= {EmitExpression(lte.Constant)}");
                break;
            case GreaterThanRelationalPatternPrimaryPatternNode gt:
                parts.Add($"{scrutineeExpr} > {EmitExpression(gt.Constant)}");
                break;
            case LessThanRelationalPatternPrimaryPatternNode lt:
                parts.Add($"{scrutineeExpr} < {EmitExpression(lt.Constant)}");
                break;
            case ConstantPatternPrimaryPatternNode constant:
                if (constant.Literal is RegexConstantConstantLiteralNode regexConstInCondition)
                {
                    var escapedPatternInCondition = ExtractRegexPatternForMatch(regexConstInCondition.Value.Lexeme);
                    parts.Add($"System.Text.RegularExpressions.Regex.IsMatch({scrutineeExpr}, \"^(?:{escapedPatternInCondition})$\")");
                }
                else
                {
                    parts.Add($"{scrutineeExpr} == {EmitConstantLiteral(constant.Literal)}");
                }
                break;
            case PropertyPatternPrimaryPatternNode propertyPattern:
                EmitPropertyPatternConditions(propertyPattern, scrutineeExpr, parts);
                break;
            case ParenthesizedPatternPrimaryPatternNode paren:
            {
                // Recursively emit the inner pattern as a grouped condition.
                var innerCondition = EmitInnerPatternCondition(paren.Inner, scrutineeExpr);
                parts.Add($"({innerCondition})");
                break;
            }
        }
    }

    /// <summary>
    /// Emits property pattern conditions by generating property access checks.
    /// For <c>{ Age: >= 18, Name: "test" }</c>, emits
    /// <c>scrutinee.Age >= 18 &amp;&amp; scrutinee.Name == "test"</c>.
    /// </summary>
    private void EmitPropertyPatternConditions(PropertyPatternPrimaryPatternNode propertyPattern, string scrutineeExpr, List<string> parts)
    {
        if (propertyPattern.Subpatterns is null)
            return;

        // Emit the first subpattern.
        EmitSinglePropertySubpatternCondition(propertyPattern.Subpatterns.PropertySubpattern, scrutineeExpr, parts);

        // Emit additional subpatterns.
        foreach (var subpattern in propertyPattern.Subpatterns.PropertySubpatterns)
        {
            EmitSinglePropertySubpatternCondition(subpattern, scrutineeExpr, parts);
        }
    }

    /// <summary>
    /// Emits a single property subpattern as a condition. The property name becomes
    /// a member access on the scrutinee, and the subpattern's pattern is applied
    /// to that member access expression.
    /// </summary>
    private void EmitSinglePropertySubpatternCondition(PropertySubpatternNode subpattern, string scrutineeExpr, List<string> parts)
    {
        var propertyName = subpattern.Name.Lexeme;
        var propertyAccess = $"{scrutineeExpr}.{propertyName}";

        // The subpattern's pattern is a full PatternNode — recursively emit it.
        var condition = EmitInnerPatternCondition(subpattern.Pattern, propertyAccess);
        if (!string.IsNullOrEmpty(condition))
            parts.Add(condition);
    }

    /// <summary>
    /// Emits a full inner pattern (OrPattern → AndPattern → ...) as a C# condition string.
    /// Used for nested patterns within property subpatterns and parenthesized patterns.
    /// </summary>
    private string EmitInnerPatternCondition(PatternNode pattern, string scrutineeExpr)
    {
        var orPattern = pattern.OrPattern;

        // Handle or-patterns: emit as `(cond1) || (cond2)`.
        if (orPattern.Right is not null && !ReferenceEquals(orPattern.Right, orPattern.Left))
        {
            var leftCond = EmitAndPatternCondition(orPattern.Left, scrutineeExpr);
            var rightCond = EmitAndPatternCondition(orPattern.Right, scrutineeExpr);
            return $"({leftCond} || {rightCond})";
        }

        return EmitAndPatternCondition(orPattern.Left, scrutineeExpr);
    }

    /// <summary>
    /// Emits an and-pattern as a C# condition string with <c>&amp;&amp;</c> joining parts.
    /// </summary>
    private string EmitAndPatternCondition(AndPatternNode andPattern, string scrutineeExpr)
    {
        var parts = new List<string>();

        EmitUnaryPatternCondition(andPattern.Left, scrutineeExpr, parts);

        if (andPattern.Right is not null && !ReferenceEquals(andPattern.Right, andPattern.Left))
        {
            EmitUnaryPatternCondition(andPattern.Right, scrutineeExpr, parts);
        }

        if (!andPattern.UnaryPatterns.IsDefaultOrEmpty)
        {
            foreach (var part in andPattern.UnaryPatterns)
            {
                EmitUnaryPatternCondition(part, scrutineeExpr, parts);
            }
        }

        return string.Join(" && ", parts);
    }

    // ─── Enum Pattern Matching ───────────────────────────────────────────

    /// <summary>
    /// Attempts to emit an enum member pattern match as a value equality check.
    /// For <c>is EnumName.MemberName</c> patterns where the resolved type is an
    /// <see cref="EnumType"/>, emits <c>scrutinee == EnumName.MemberName</c>
    /// instead of C#'s native <c>is</c> pattern.
    /// Returns <c>null</c> if the pattern is not a simple enum member pattern.
    /// </summary>
    private string? TryEmitEnumPatternMatch(PatternNode pattern, string scrutineeExpr)
    {
        // Navigate the pattern chain: Pattern → OrPattern → AndPattern → UnaryPattern → PrimaryPattern
        // Only handle simple (non-compound) patterns for enum emission.
        var orPattern = pattern.OrPattern;
        if (orPattern.Right is not null && !ReferenceEquals(orPattern.Right, orPattern.Left))
            return null; // or-pattern — fall through to standard emission

        var andPattern = orPattern.Left;
        if (andPattern.Right is not null && !ReferenceEquals(andPattern.Right, andPattern.Left))
            return null; // and-pattern — fall through to standard emission

        var unaryPattern = andPattern.Left;
        if (unaryPattern is not DirectPatternUnaryPatternNode directPattern)
            return null; // not-pattern — fall through to standard emission

        var primaryPattern = directPattern.Primary;

        if (primaryPattern is not BareTypePatternPrimaryPatternNode bareTypePattern)
            return null;

        // Check if the resolved type for this pattern is an EnumType.
        var patternType = _model.GetTypeOf(bareTypePattern.Type);
        if (patternType is not EnumType)
            return null;

        // The type reference contains the qualified name (e.g., "Direction.North").
        // Extract it directly — this is an enum member access, not a type reference.
        if (bareTypePattern.Type is NamedTypeRefTypeReferenceNode namedRef)
        {
            var qualifiedName = ExtractQualifiedName(namedRef.Name);
            return $"{scrutineeExpr} == {qualifiedName}";
        }

        return null;
    }

    // ─── Regex Pattern Matching ─────────────────────────────────────────

    /// <summary>
    /// Attempts to emit a regex pattern match for an <c>is</c> expression.
    /// For <c>x is `[a-z]+`</c>, emits
    /// <c>System.Text.RegularExpressions.Regex.IsMatch(x, "^(?:[a-z]+)$")</c>.
    /// Returns <c>null</c> if the pattern is not a simple regex constant pattern.
    /// </summary>
    private static string? TryEmitRegexPatternMatch(PatternNode pattern, string scrutineeExpr)
    {
        // Navigate the pattern chain: Pattern → OrPattern → AndPattern → UnaryPattern → PrimaryPattern
        var orPattern = pattern.OrPattern;
        if (orPattern.Right is not null && !ReferenceEquals(orPattern.Right, orPattern.Left))
            return null;

        var andPattern = orPattern.Left;
        if (andPattern.Right is not null && !ReferenceEquals(andPattern.Right, andPattern.Left))
            return null;

        var unaryPattern = andPattern.Left;
        if (unaryPattern is not DirectPatternUnaryPatternNode directPattern)
            return null;

        if (directPattern.Primary is not ConstantPatternPrimaryPatternNode { Literal: RegexConstantConstantLiteralNode regexConst })
            return null;

        var escapedPattern = ExtractRegexPatternForMatch(regexConst.Value.Lexeme);
        return $"System.Text.RegularExpressions.Regex.IsMatch({scrutineeExpr}, \"^(?:{escapedPattern})$\")";
    }

    /// <summary>
    /// Attempts to emit a switch arm pattern for a regex constant.
    /// For a regex pattern in a switch arm, emits
    /// <c>string __s when System.Text.RegularExpressions.Regex.IsMatch(__s, "^(?:pattern)$")</c>.
    /// Returns <c>null</c> if the pattern is not a simple regex constant pattern.
    /// </summary>
    private static string? TryEmitRegexSwitchArmPattern(PatternNode pattern)
    {
        // Navigate the pattern chain: Pattern → OrPattern → AndPattern → UnaryPattern → PrimaryPattern
        var orPattern = pattern.OrPattern;
        if (orPattern.Right is not null && !ReferenceEquals(orPattern.Right, orPattern.Left))
            return null;

        var andPattern = orPattern.Left;
        if (andPattern.Right is not null && !ReferenceEquals(andPattern.Right, andPattern.Left))
            return null;

        var unaryPattern = andPattern.Left;
        if (unaryPattern is not DirectPatternUnaryPatternNode directPattern)
            return null;

        if (directPattern.Primary is not ConstantPatternPrimaryPatternNode { Literal: RegexConstantConstantLiteralNode regexConst })
            return null;

        var escapedPattern = ExtractRegexPatternForMatch(regexConst.Value.Lexeme);
        return $"string __s when System.Text.RegularExpressions.Regex.IsMatch(__s, \"^(?:{escapedPattern})$\")";
    }

    /// <summary>
    /// Extracts the regex pattern from a regex literal lexeme (backtick-delimited),
    /// resolves Mist escape sequences, and applies C# string escaping for use in
    /// a <c>Regex.IsMatch</c> call with full-match anchoring.
    /// </summary>
    private static string ExtractRegexPatternForMatch(string lexeme)
    {
        // Strip backtick delimiters.
        var raw = lexeme[1..^1];

        // Resolve Mist regex escape sequences to get the actual regex pattern.
        var pattern = new StringBuilder(raw.Length);
        for (var i = 0; i < raw.Length; i++)
        {
            if (raw[i] == '\\' && i + 1 < raw.Length)
            {
                var next = raw[i + 1];
                if (next == '`')
                {
                    pattern.Append('`');
                    i++;
                }
                else if (next == '\\')
                {
                    pattern.Append('\\');
                    i++;
                }
                else
                {
                    // Not a Mist escape — preserve the backslash and next char verbatim.
                    pattern.Append(raw[i]);
                }
            }
            else
            {
                pattern.Append(raw[i]);
            }
        }

        // Apply C# string escaping to the resolved pattern.
        return EscapeString(pattern.ToString());
    }

    // ─── Pattern Emission ────────────────────────────────────────────────

    private string EmitPattern(PatternNode pattern, IType? scrutineeType = null)
    {
        return EmitOrPattern(pattern.OrPattern, scrutineeType);
    }

    private string EmitOrPattern(OrPatternNode orPattern, IType? scrutineeType)
    {
        var left = EmitAndPattern(orPattern.Left, scrutineeType);

        if (orPattern.Right is null || ReferenceEquals(orPattern.Right, orPattern.Left))
            return left;

        var right = EmitAndPattern(orPattern.Right, scrutineeType);
        return $"{left} or {right}";
    }

    private string EmitAndPattern(AndPatternNode andPattern, IType? scrutineeType)
    {
        var left = EmitUnaryPattern(andPattern.Left, scrutineeType);

        if (andPattern.Right is null || ReferenceEquals(andPattern.Right, andPattern.Left))
            return left;

        var right = EmitUnaryPattern(andPattern.Right, scrutineeType);
        return $"{left} and {right}";
    }

    private string EmitUnaryPattern(UnaryPatternNode unaryPattern, IType? scrutineeType)
    {
        switch (unaryPattern)
        {
            case NotPatternUnaryPatternNode notPattern:
                return $"not {EmitPrimaryPattern(notPattern.Operand, scrutineeType)}";
            case DirectPatternUnaryPatternNode directPattern:
                return EmitPrimaryPattern(directPattern.Primary, scrutineeType);
            default:
                return "";
        }
    }

    private string EmitPrimaryPattern(PrimaryPatternNode primaryPattern, IType? scrutineeType = null)
    {
        switch (primaryPattern)
        {
            case TypePatternPrimaryPatternNode typePattern:
            {
                // Tuple type patterns require emission logic that depends on
                // the scrutinee's static type. C# distinguishes two forms:
                //
                //   * `is (int, int) pair` or `is (int a, int b) pair` —
                //     a positional deconstruction pattern. Requires the
                //     scrutinee to have a `Deconstruct` method; C# tuple
                //     types do, `object` does not. When emitted against
                //     a scrutinee typed as a C# tuple, this form is
                //     correct and matches C# semantics exactly: `a` and
                //     `b` are bound as separate locals (the deconstructed
                //     element values) and `pair` is bound to the whole
                //     tuple (without element names on the binding).
                //
                //   * `is System.ValueTuple<int, int> pair` — an
                //     unambiguous type pattern that works against any
                //     scrutinee, including `object`. Does not deconstruct:
                //     only `pair` is bound, with the unnamed tuple type.
                //     Used when the scrutinee is not a concrete C# tuple
                //     (e.g., a bare `unknown` that lowered to `object`).
                //
                // We pick the form based on the scrutinee's Mist static
                // type: a `TupleType` scrutinee lowers to a C# tuple and
                // can use the deconstruction form; anything else must use
                // the generic form. This mirrors C#'s own restrictions —
                // if C# would reject `is (int a, int b) pair` against a
                // given scrutinee, the Mist emitter routes through the
                // `ValueTuple<>` form instead.
                if (typePattern.Type is TupleTypeRefTypeReferenceNode tupleRef)
                {
                    var name = typePattern.Name.Lexeme;
                    if (scrutineeType is TupleType)
                    {
                        // Scrutinee is a concrete tuple. Emit verbatim;
                        // C# handles deconstruction and element-name
                        // binding naturally.
                        var tupleTypeName = EmitTypeReference(typePattern.Type);
                        return $"{tupleTypeName} {name}";
                    }

                    // Scrutinee is not a tuple (object / unknown / etc.).
                    // Use the generic `System.ValueTuple<...>` form for the
                    // type test, which works against any scrutinee.
                    var genericTypeName = EmitTupleTypePatternType(tupleRef);

                    // If the pattern declares element names (e.g.,
                    // `(int a, int b) pair`), C# can't deconstruct
                    // directly against the `object` scrutinee. Chain a
                    // second pattern match against the freshly-bound
                    // typed local so `a` and `b` are introduced in the
                    // enclosing scope. The resulting form is accepted
                    // by C# 14 and matches Mist's semantics exactly:
                    //   `u is System.ValueTuple<int, int> pair && pair is (int a, int b)`
                    // When there are no element names, no chain is
                    // needed — the simple `ValueTuple<...>` form is
                    // enough to make `pair` available in the branch.
                    if (TupleTypeRefHasAnyElementName(tupleRef))
                    {
                        var deconstructionPattern = EmitTupleDeconstructionPattern(tupleRef);
                        return $"{genericTypeName} {name} && {name} is {deconstructionPattern}";
                    }

                    return $"{genericTypeName} {name}";
                }

                var type = EmitTypeReference(typePattern.Type);
                var nameDefault = typePattern.Name.Lexeme;
                return $"{type} {nameDefault}";
            }
            case BareTypePatternPrimaryPatternNode bareTypePattern:
            {
                // Bare type pattern: emit just the type (no binding name).
                if (bareTypePattern.Type is TupleTypeRefTypeReferenceNode tupleRef)
                {
                    if (scrutineeType is TupleType)
                    {
                        return EmitTypeReference(bareTypePattern.Type);
                    }

                    return EmitTupleTypePatternType(tupleRef);
                }

                return EmitTypeReference(bareTypePattern.Type);
            }
            case VarPatternPrimaryPatternNode varPattern:
                return $"var {varPattern.Name.Lexeme}";
            case DiscardPatternPrimaryPatternNode:
                return "var _";
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
                return $"({EmitPattern(parenPattern.Inner, scrutineeType)})";
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
        if (resolved is Symbol resolvedSymbol)
        {
            if (resolvedSymbol.Name.StartsWith("operator_implicit_") || resolvedSymbol.Name.StartsWith("operator_explicit_"))
            {
                // Check if this cast has an intrinsic emission template
                // (e.g., Regex → string emits .ToString(), string → Regex emits new Regex(...)).
                var declaringType = GetQualifiedTypeName(resolvedSymbol);
                if (declaringType != "Unknown")
                {
                    var targetTypeName = resolvedSymbol.DeclaredType?.DisplayName;
                    if (targetTypeName is not null)
                    {
                        var emittedTypeName = CSharpTypeMap.GetTypeName(resolvedSymbol.DeclaredType!);
                        var castTemplate = _intrinsicRegistry.TryGetCastOperator(declaringType, emittedTypeName);
                        if (castTemplate is not null)
                        {
                            return string.Format(castTemplate.Template, expression);
                        }
                    }
                }

                // Fallback: emit as a C# cast expression.
                var fallbackTargetTypeName = resolvedSymbol.DeclaredType?.DisplayName;
                if (fallbackTargetTypeName is not null)
                {
                    var emittedTypeName = CSharpTypeMap.GetTypeName(resolvedSymbol.DeclaredType!);
                    return $"(({emittedTypeName}){expression})";
                }
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
    /// ReadonlyAnalyzer still guarantees that no non-readonly member
    /// is called on the widened receiver at compile time, so the DLR
    /// only sees readonly-safe calls.
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
                Location: default,
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
                Location: default,
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
        // Special case: TypeLiteralExpression followed by member access or method call
        // represents a static member access on a dependent type (e.g., FixedBuffer<5>.Size).
        // Emit the type name directly without typeof() wrapping.
        // This ONLY applies when the type reference contains dependent type arguments
        // (literal values like <5>, <true>, etc.). For plain types like `int.Name`,
        // we fall through to the normal typeof() path below.
        if (postfix.Target is TypeLiteralExpressionPrimaryNode typeLiteral
            && postfix.Suffix.Length > 0
            && TypeReferenceHasDependentTypeArguments(typeLiteral.TypeRef))
        {
            var result = EmitTypeReference(typeLiteral.TypeRef, isValueContext: true);

            IType? receiverType = _model.GetTypeOf(postfix.Target);

            for (var i = 0; i < postfix.Suffix.Length; i++)
            {
                var suffix = postfix.Suffix[i];
                result = EmitPostfixSuffix(result, suffix, receiverType);
                receiverType = _model.GetTypeOf(suffix);
            }

            return result;
        }

        var primaryResult = EmitPrimary(postfix.Target);

        // Track the receiver type through the suffix chain for extension property rewriting.
        // The receiver type for the first suffix is the type of the primary target.
        // For subsequent suffixes, it's the result type of the previous suffix.
        IType? primaryReceiverType = _model.GetTypeOf(postfix.Target);

        for (var i = 0; i < postfix.Suffix.Length; i++)
        {
            var suffix = postfix.Suffix[i];
            primaryResult = EmitPostfixSuffix(primaryResult, suffix, primaryReceiverType);

            // Update receiver type for the next suffix.
            primaryReceiverType = _model.GetTypeOf(suffix);
        }

        return primaryResult;
    }

    /// <summary>
    /// Members of ambient types that are properties in C# and should be emitted
    /// without parentheses, even though Mist models them as method calls.
    /// </summary>
    private static readonly HashSet<string> CSharpPropertyMembers = new(StringComparer.Ordinal)
    {
        "Length", // string.Length, Array.Length
    };

    private string EmitPostfixSuffix(string target, PostfixSuffixNode suffix, IType? receiverType)
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

                // Check if this method call resolves to an extension property —
                // if so, rewrite to ClassName.get_PropertyName(instance).
                if (methodCall.Arguments is null && methodCall.TypeArgs is null && receiverType is not null)
                {
                    var extPropertyBlock = _model.ExtensionRegistry.FindExtensionPropertyBlock(receiverType, name);
                    if (extPropertyBlock is not null)
                    {
                        var extClassName = ComputeExtensionClassName(extPropertyBlock);
                        return $"{extClassName}.get_{name}({target})";
                    }
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

                // Check if this is an intrinsic static method call (e.g., Type.Of(x) → x.GetType()).
                // The target is a type name; check if it maps to a stdlib type with an intrinsic.
                var intrinsicStaticTemplate = TryGetIntrinsicStaticMethodTemplate(target, name, methodCall.Arguments);
                if (intrinsicStaticTemplate is not null)
                {
                    return intrinsicStaticTemplate;
                }

                // Check if this is an intrinsic instance method call on a backend-backed type
                // (e.g., Regex.IsMatch, Regex.Pattern). Look up the receiver type's qualified
                // name and query the intrinsic emission table.
                if (receiverType is not null)
                {
                    var qualifiedReceiverType = GetQualifiedTypeNameFromType(receiverType);
                    if (qualifiedReceiverType is not null)
                    {
                        var paramTypes = CollectArgumentTypes(methodCall.Arguments);
                        var instanceTemplate = _intrinsicRegistry.TryGetInstanceMethod(qualifiedReceiverType, name, paramTypes);
                        if (instanceTemplate is not null)
                        {
                            // Format: {0} = receiver (target), {1}..{N} = arguments
                            var templateArgs = new List<string> { target };
                            if (methodCall.Arguments is not null)
                            {
                                foreach (var child in methodCall.Arguments.Children())
                                {
                                    if (child is ExpressionNode argExpr)
                                        templateArgs.Add(EmitExpression(argExpr));
                                }
                            }
                            return string.Format(instanceTemplate.Template, templateArgs.ToArray());
                        }
                    }
                }

                // Call-site transformation for generic methods with parameterized constructor constraints:
                // append factory lambda arguments when calling a method that has factory delegate parameters.
                var calledMethodSymbol = _model.GetDeclaredSymbol(suffix) as Symbol;
                if (calledMethodSymbol is not null && calledMethodSymbol.Kind == SymbolKind.Method &&
                    calledMethodSymbol.TypeParameters.Count > 0 &&
                    HasParameterizedConstructorConstraints(calledMethodSymbol.TypeParameters))
                {
                    var factoryLambdas = GenerateFactoryLambdas(calledMethodSymbol.TypeParameters, methodCall.TypeArgs);
                    if (factoryLambdas.Length > 0)
                    {
                        args = args.Length > 0
                            ? $"{args}, {factoryLambdas}"
                            : factoryLambdas;
                    }
                }

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

                // Check if this is an intrinsic property access on a backend-backed type
                // (e.g., Regex.Pattern → .ToString()). Look up the receiver type's qualified
                // name and query the intrinsic emission table as a zero-arg instance method.
                var memberName = memberAccess.Name.Lexeme;
                if (receiverType is not null)
                {
                    var qualifiedReceiverType = GetQualifiedTypeNameFromType(receiverType);
                    if (qualifiedReceiverType is not null)
                    {
                        var instanceTemplate = _intrinsicRegistry.TryGetInstanceMethod(
                            qualifiedReceiverType, memberName, Array.Empty<string>());
                        if (instanceTemplate is not null)
                        {
                            return string.Format(instanceTemplate.Template, target);
                        }
                    }
                }

                // MemberAccessSuffix always emits as property access (no parentheses).
                // This is the correct form for both field access and property access in C#.
                return $"{target}.{memberName}";
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
            case SwitchExpressionSuffixPostfixSuffixNode switchExpr:
            {
                var sb = new StringBuilder();
                sb.Append($"{target} switch");
                sb.Append('\n');
                // Emit opening brace at current indent level
                for (var i = 0; i < _indentLevel; i++)
                    sb.Append(IndentUnit);
                sb.Append('{');
                _indentLevel++;

                if (switchExpr.Arms is not null)
                {
                    void EmitArm(SwitchArmNode arm)
                    {
                        sb.Append('\n');
                        for (var i = 0; i < _indentLevel; i++)
                            sb.Append(IndentUnit);

                        // Union type scrutinee: emit property patterns using Is{T}/As{T}
                        // discriminator properties instead of C#'s native type patterns.
                        if (receiverType is UnionType unionType)
                        {
                            var unionPattern = TryEmitUnionSwitchArmPattern(arm.Pattern, unionType, target);
                            if (unionPattern is not null)
                            {
                                sb.Append(unionPattern);
                                if (arm.Guard is not null)
                                {
                                    sb.Append($" when {EmitExpression(arm.Guard.Condition)}");
                                }
                                sb.Append($" => {EmitExpression(arm.Result)},");
                                return;
                            }
                        }

                        // Refinement type pattern: emit as `var varName when predicate`
                        // guard condition. Refinement types are erased, so the pattern
                        // becomes a runtime predicate check in the when-clause.
                        var refinementArm = TryEmitRefinementSwitchArmPattern(arm.Pattern, target);
                        if (refinementArm is not null)
                        {
                            sb.Append(refinementArm);
                            if (arm.Guard is not null)
                            {
                                // Combine the refinement guard with the user's guard.
                                sb.Append($" && {EmitExpression(arm.Guard.Condition)}");
                            }
                            sb.Append($" => {EmitExpression(arm.Result)},");
                            return;
                        }

                        // Regex pattern: emit as `string __s when Regex.IsMatch(__s, "^(?:pattern)$")`
                        // guard condition. C# doesn't natively support regex patterns in switch.
                        var regexArm = TryEmitRegexSwitchArmPattern(arm.Pattern);
                        if (regexArm is not null)
                        {
                            sb.Append(regexArm);
                            if (arm.Guard is not null)
                            {
                                // Combine the regex guard with the user's guard.
                                sb.Append($" && {EmitExpression(arm.Guard.Condition)}");
                            }
                            sb.Append($" => {EmitExpression(arm.Result)},");
                            return;
                        }

                        sb.Append(EmitPattern(arm.Pattern, receiverType));
                        if (arm.Guard is not null)
                        {
                            sb.Append($" when {EmitExpression(arm.Guard.Condition)}");
                        }
                        sb.Append($" => {EmitExpression(arm.Result)},");
                    }

                    EmitArm(switchExpr.Arms.SwitchArm);
                    foreach (var arm in switchExpr.Arms.SwitchArms)
                    {
                        EmitArm(arm);
                    }
                }

                _indentLevel--;
                sb.Append('\n');
                for (var i = 0; i < _indentLevel; i++)
                    sb.Append(IndentUnit);
                sb.Append('}');
                return sb.ToString();
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

    /// <summary>
    /// Checks if a static method call on a type name is an intrinsic that should
    /// be rewritten. For example, <c>Type.Of(x)</c> → <c>x.GetType()</c>.
    /// Returns the rewritten C# expression, or null if not an intrinsic.
    /// </summary>
    private string? TryGetIntrinsicStaticMethodTemplate(string target, string methodName, ArgumentListNode? arguments)
    {
        // Map common Mist type names to their fully-qualified stdlib form.
        // The target may already be the C# form (e.g., "System.Type") due to
        // CSharpTypeMap remapping, or it may be the Mist name (e.g., "Type").
        var stdlibType = target switch
        {
            "Type" or "System.Type" => "Standard.Type",
            _ => null,
        };

        if (stdlibType is null)
            return null;

        // Build parameter type list for lookup.
        // For now, use "unknown" for unknown-typed parameters since that's
        // how Type.Of is declared.
        var paramTypes = new List<string>();
        if (arguments is not null)
        {
            paramTypes.Add("unknown");
        }

        var template = _intrinsicRegistry.TryGetStaticMethod(stdlibType, methodName, paramTypes);
        if (template is null)
            return null;

        // Emit arguments and substitute into the template.
        if (arguments is not null)
        {
            var arg0 = EmitExpression(arguments.Expression);
            return string.Format(template.Template, arg0);
        }

        return string.Format(template.Template);
    }

    /// <summary>
    /// Returns the fully-qualified stdlib type name (e.g., "Standard.Regex") for an IType,
    /// or null if the type is not a backend-backed stdlib type.
    /// </summary>
    private static string? GetQualifiedTypeNameFromType(IType type)
    {
        return type switch
        {
            ClassType ct when ct.ContainingNamespace is not null => $"{ct.ContainingNamespace}.{ct.DisplayName}",
            StructType st when st.ContainingNamespace is not null => $"{st.ContainingNamespace}.{st.DisplayName}",
            _ => null,
        };
    }

    /// <summary>
    /// Collects the fully-qualified stdlib type names of arguments in an argument list,
    /// for use as the parameter type key when looking up intrinsic instance methods.
    /// </summary>
    private IReadOnlyList<string> CollectArgumentTypes(ArgumentListNode? arguments)
    {
        if (arguments is null)
            return Array.Empty<string>();

        var types = new List<string>();
        foreach (var child in arguments.Children())
        {
            if (child is ExpressionNode expr)
            {
                var exprType = _model.GetTypeOf(expr);
                if (exprType is not null)
                {
                    var qualifiedName = GetQualifiedTypeNameFromType(exprType);
                    types.Add(qualifiedName ?? exprType.DisplayName);
                }
            }
        }
        return types;
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
            case RegexLiteralExpressionPrimaryNode regexLit:
                return EmitRegexLiteral(regexLit.RegexLiteral.Lexeme);
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
                // Look up the type's parameters to correctly emit carrier struct names
                // for non-int dependent type arguments (uint, long, etc.).
                var typeSymbol = _model.AllSymbols
                    .FirstOrDefault(s => s.Name == type && s.Kind is SymbolKind.Class or SymbolKind.Struct or SymbolKind.Interface);
                var targetTypeParams = typeSymbol is not null ? GetTypeParametersFromType(typeSymbol.DeclaredType) : null;
                var typeArgs = EmitTypeArgumentList(objCreation.TypeArgs, targetTypeParams);

                // Check if the type being constructed is a type parameter with a parameterized
                // constructor constraint — if so, emit as factory delegate invocation.
                var exprType = _model.GetTypeOf(objCreation);
                if (exprType is TypeParameterType typeParamType && typeParamType.Constraints is not null)
                {
                    var argTypes = GetArgumentTypes(objCreation.Arguments);
                    if (argTypes.Count > 0)
                    {
                        // Parameterized new T(args) → factory delegate invocation
                        var matchingConstraint = FindMatchingParameterizedConstraint(typeParamType, argTypes);
                        if (matchingConstraint is not null)
                        {
                            // Determine whether to use field name or parameter name:
                            // - Inside a method with factory params → use parameter name (factoryT_...)
                            // - Inside a class with factory fields → use field name (_factoryT_...)
                            string factoryName;
                            if (_currentMethodFactoryTypeParams is not null &&
                                _currentMethodFactoryTypeParams.Any(tp => tp.DisplayName == typeParamType.Name))
                            {
                                factoryName = GetFactoryParamName(typeParamType, matchingConstraint);
                            }
                            else
                            {
                                factoryName = GetFactoryFieldName(typeParamType, matchingConstraint);
                            }
                            var args = EmitArgumentList(objCreation.Arguments);
                            return $"{factoryName}({args})";
                        }
                    }
                    // new T() with parameterless constraint → standard new T()
                    var args0 = EmitArgumentList(objCreation.Arguments);
                    return $"new {type}{typeArgs}({args0})";
                }

                // Type aliases are transparent — resolve to the underlying type name
                // for object creation expressions (e.g., `new IntBox(42)` → `new GenericBox<int>(42)`).
                // Only apply if no concrete type with the same name exists in the
                // current namespace, to prevent cross-namespace collisions.
                var aliasSymbol = _model.AllSymbols
                    .FirstOrDefault(s => s.Name == type && s.Kind == SymbolKind.TypeAlias);
                if (aliasSymbol?.DeclaredType is TypeAliasType objAlias)
                {
                    var hasConcreteTypeInCurrentNamespace = _currentNamespace is not null
                        && _model.AllSymbols.Any(s => s.Name == type
                            && s.Kind is SymbolKind.Class or SymbolKind.Struct
                            && GetSymbolContainingNamespace(s) == _currentNamespace);
                    if (!hasConcreteTypeInCurrentNamespace)
                    {
                        var underlying = objAlias.UnderlyingType;
                        if (objCreation.TypeArgs is not null && objAlias.TypeParameters.Count > 0)
                        {
                            // Generic alias with type arguments: emit definition + substituted args.
                            if (underlying is ConstructedType ct)
                            {
                                type = CSharpTypeMap.GetTypeName(ct.GenericDefinition);
                            }
                            else
                            {
                                type = CSharpTypeMap.GetTypeName(underlying);
                                typeArgs = "";
                            }
                        }
                        else
                        {
                            // Non-generic alias or closed generic alias: emit the full resolved type.
                            type = CSharpTypeMap.GetTypeName(underlying);
                            typeArgs = "";
                        }
                    }
                }

                var argsStr = EmitArgumentList(objCreation.Arguments);

                // Call-site transformation: when constructing a type that has parameterized
                // constructor constraints, append factory lambda arguments.
                var constructedSymbol = _model.AllSymbols
                    .FirstOrDefault(s => s.Name == type && s.Kind is SymbolKind.Class or SymbolKind.Struct);
                if (constructedSymbol?.DeclaredType is not null)
                {
                    var constructedTypeParams = GetTypeParametersFromType(constructedSymbol.DeclaredType);
                    if (HasParameterizedConstructorConstraints(constructedTypeParams))
                    {
                        var factoryLambdas = GenerateFactoryLambdas(constructedTypeParams, objCreation.TypeArgs);
                        if (factoryLambdas.Length > 0)
                        {
                            argsStr = argsStr.Length > 0
                                ? $"{argsStr}, {factoryLambdas}"
                                : factoryLambdas;
                        }
                    }
                }

                if (objCreation.Initializer is not null)
                {
                    var initParts = new List<string>();
                    if (objCreation.Initializer.Assignments is not null)
                    {
                        var assignments = objCreation.Initializer.Assignments;
                        initParts.Add($"{assignments.PropertyAssignment.Name.Lexeme} = {EmitExpression(assignments.PropertyAssignment.Value)}");
                        foreach (var assignment in assignments.PropertyAssignments)
                        {
                            initParts.Add($"{assignment.Name.Lexeme} = {EmitExpression(assignment.Value)}");
                        }
                    }
                    return $"new {type}{typeArgs}({argsStr}) {{ {string.Join(", ", initParts)} }}";
                }

                return $"new {type}{typeArgs}({argsStr})";
            }
            case ObjectCreationExpressionNoArgsPrimaryNode objCreationNoArgs:
            {
                var type = ExtractQualifiedName(objCreationNoArgs.Type);
                var typeArgs = EmitTypeArgumentList(objCreationNoArgs.TypeArgs);
                var initParts = new List<string>();
                if (objCreationNoArgs.Initializer.Assignments is not null)
                {
                    var assignments = objCreationNoArgs.Initializer.Assignments;
                    initParts.Add($"{assignments.PropertyAssignment.Name.Lexeme} = {EmitExpression(assignments.PropertyAssignment.Value)}");
                    foreach (var assignment in assignments.PropertyAssignments)
                    {
                        initParts.Add($"{assignment.Name.Lexeme} = {EmitExpression(assignment.Value)}");
                    }
                }
                return $"new {type}{typeArgs}() {{ {string.Join(", ", initParts)} }}";
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
            case EmptyArrayLiteralExpressionPrimaryNode:
            {
                // Empty array literal [] — emit Array.Empty<T>() when target type is known.
                // When no target type is available, this is a compile error (the type checker
                // will have already reported MIST_TC_0096), so we emit a fallback.
                if (_arrayLiteralTargetElementType is not null)
                {
                    var elementTypeName = CSharpTypeMap.GetTypeName(_arrayLiteralTargetElementType);
                    return $"Array.Empty<{elementTypeName}>()";
                }
                // Fallback for error case — should not normally reach here because
                // the type checker rejects empty array literals without target type.
                return "Array.Empty<object>()";
            }
            case ArrayLiteralExpressionPrimaryNode arrayLiteral:
            {
                var elements = new List<string> { EmitExpression(arrayLiteral.First) };
                foreach (var elem in arrayLiteral.Expressions)
                    elements.Add(EmitExpression(elem));

                // When the array literal is assigned to a variable with an explicit
                // array type annotation, emit `new T[] { ... }` instead of `new[] { ... }`.
                if (_arrayLiteralTargetElementType is not null)
                {
                    var elementTypeName = CSharpTypeMap.GetTypeName(_arrayLiteralTargetElementType);
                    return $"new {elementTypeName}[] {{ {string.Join(", ", elements)} }}";
                }

                return $"new[] {{ {string.Join(", ", elements)} }}";
            }
            case IdentifierExpressionPrimaryNode ident:
            {
                var identName = ident.Name.Lexeme;
                // Check if this identifier resolves to a dependent type parameter.
                // If so, emit TName.Value to access the carrier struct's value.
                var resolvedType = _model.GetTypeOf(ident);
                if (resolvedType is DependentTypeParameterType)
                {
                    return $"T{identName}.Value";
                }
                // Fallback: check if the name matches a dependent type parameter
                // (the semantic model may resolve it to the value type rather than
                // the DependentTypeParameterType itself in some contexts).
                if (IsDependentTypeParameterName(identName))
                {
                    return $"T{identName}.Value";
                }
                return identName;
            }
            case FieldKeywordExpressionPrimaryNode:
                return "field";
            case TypeLiteralExpressionPrimaryNode typeLiteral:
            {
                return $"typeof({EmitTypeReference(typeLiteral.TypeRef)})";
            }
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

    // ─── Dependent Type Emission Helpers ─────────────────────────────────

    /// <summary>
    /// Emits <c>[DependentType("ParamName", typeof(valueType))]</c> attributes for each
    /// dependent type parameter on a type declaration.
    /// </summary>
    private void EmitDependentTypeAttributes(IReadOnlyList<IType> typeParameters)
    {
        foreach (var typeParam in typeParameters)
        {
            if (typeParam is DependentTypeParameterType depParam)
            {
                var keyword = CSharpTypeMap.GetKeyword(depParam.ValueType);
                AppendLine($"[DependentType(\"{depParam.Name}\", typeof({keyword}))]");
            }
        }
    }

    /// <summary>
    /// Emits dependent type properties (e.g., <c>public static int Size =&gt; TSize.Value;</c>)
    /// for each dependent type parameter. Returns <c>true</c> if any properties were emitted.
    /// </summary>
    private bool EmitDependentTypeProperties(IReadOnlyList<IType> typeParameters)
    {
        var emitted = false;
        foreach (var typeParam in typeParameters)
        {
            if (typeParam is DependentTypeParameterType depParam)
            {
                var keyword = CSharpTypeMap.GetKeyword(depParam.ValueType);
                if (emitted)
                    AppendLine();
                AppendLine($"public static {keyword} {depParam.Name} => T{depParam.Name}.Value;");
                emitted = true;
            }
        }
        return emitted;
    }

    // ─── Utilities ───────────────────────────────────────────────────────

    private static string EmitTypeParameterList(TypeParameterListNode? typeParams)
    {
        if (typeParams is null) return "";

        var parts = new List<string>();
        parts.Add(EmitSingleTypeParameterName(typeParams.TypeParameter));
        foreach (var tp in typeParams.TypeParameters)
        {
            parts.Add(EmitSingleTypeParameterName(tp));
        }
        return $"<{string.Join(", ", parts)}>";
    }

    /// <summary>
    /// Emits the C# name for a single type parameter node.
    /// Dependent type parameters are prefixed with <c>T</c> (e.g., <c>Size</c> → <c>TSize</c>)
    /// to follow C# generic naming conventions and avoid collisions with the generated property name.
    /// </summary>
    private static string EmitSingleTypeParameterName(TypeParameterNode typeParam)
    {
        return typeParam switch
        {
            DependentTypeParameterTypeParameterNode dep => $"T{dep.Name.Lexeme}",
            _ => typeParam.GetName().Lexeme,
        };
    }

    private string EmitTypeArgumentList(TypeArgumentListNode? typeArgs)
    {
        return EmitTypeArgumentList(typeArgs, targetTypeParams: null);
    }

    /// <summary>
    /// Emits a type argument list, optionally using the target type's parameters to determine
    /// the correct carrier struct type for literal dependent type arguments.
    /// </summary>
    private string EmitTypeArgumentList(TypeArgumentListNode? typeArgs, IReadOnlyList<IType>? targetTypeParams)
    {
        if (typeArgs is null) return "";

        var allArgs = new List<TypeArgumentNode> { typeArgs.TypeArgument };
        foreach (var arg in typeArgs.TypeArguments)
            allArgs.Add(arg);

        var parts = new List<string>(allArgs.Count);
        for (var i = 0; i < allArgs.Count; i++)
        {
            string? expectedKeyword = null;
            if (targetTypeParams is not null && i < targetTypeParams.Count
                && targetTypeParams[i] is DependentTypeParameterType depParam)
            {
                try { expectedKeyword = CSharpTypeMap.GetKeyword(depParam.ValueType); }
                catch { /* ignore if type can't be mapped */ }
            }
            parts.Add(EmitSingleTypeArgument(allArgs[i], expectedKeyword));
        }
        return $"<{string.Join(", ", parts)}>";
    }

    /// <summary>
    /// Emits a single type argument. For regular type references, delegates to <see cref="EmitTypeReference"/>.
    /// For dependent value arguments (literals, const references), emits the carrier struct name
    /// (e.g., <c>5</c> → <c>Const_Int_5</c>) or the T-prefixed parameter name for forwarded parameters.
    /// </summary>
    private string EmitSingleTypeArgument(TypeArgumentNode arg)
    {
        return EmitSingleTypeArgument(arg, expectedKeyword: null);
    }

    /// <summary>
    /// Emits a single type argument with an optional expected type keyword for carrier struct naming.
    /// When <paramref name="expectedKeyword"/> is provided (e.g., "uint", "long"), integer literals
    /// will generate carrier structs with the correct type (e.g., <c>Const_Uint_100</c> instead of
    /// <c>Const_Int_100</c>).
    /// </summary>
    private string EmitSingleTypeArgument(TypeArgumentNode arg, string? expectedKeyword)
    {
        switch (arg)
        {
            case TypeReferenceTypeArgumentTypeArgumentNode typeRefArg:
            {
                // Check if this type reference resolves to a DependentTypeParameterType
                // (forwarded dependent parameter like `FixedBuffer<N>` where N is a dependent param).
                var resolvedType = _model.GetTypeOf(typeRefArg.Type);
                if (resolvedType is DependentTypeParameterType depParam)
                {
                    return $"T{depParam.Name}";
                }

                // Also check by name: if the type reference is a simple identifier that matches
                // a dependent type parameter name on any type in the model, emit T-prefixed.
                if (typeRefArg.Type is NamedTypeRefTypeReferenceNode namedRef
                    && namedRef.TypeArgs is null
                    && !namedRef.ArrayRank.HasValue)
                {
                    var name = ExtractQualifiedName(namedRef.Name);
                    if (IsDependentTypeParameterName(name))
                    {
                        return $"T{name}";
                    }
                    // If we have an expectedKeyword, this position expects a dependent
                    // value argument. A bare identifier here that doesn't resolve to a
                    // known type is a forwarded parameter.
                    if (expectedKeyword is not null)
                    {
                        // Verify it's not a known type before treating as forwarded param.
                        var isKnownType = _model.AllSymbols.Any(s =>
                            s.Name == name && s.Kind is SymbolKind.Class or SymbolKind.Struct
                                or SymbolKind.Interface or SymbolKind.Enum or SymbolKind.TypeAlias);
                        if (!isKnownType)
                        {
                            return $"T{name}";
                        }
                    }
                }

                return EmitTypeReference(typeRefArg.Type, isValueContext: true);
            }

            // For literal type arguments, we emit the carrier struct name based on the literal value.
            // When expectedKeyword is provided, use it for the type prefix instead of defaulting to "int".
            case IntegerLiteralTypeArgumentTypeArgumentNode intLit:
                return GetCarrierStructNameForIntLiteral(intLit.Literal.Lexeme, negated: false, expectedKeyword);
            case NegatedIntegerTypeArgumentTypeArgumentNode negLit:
                return GetCarrierStructNameForIntLiteral(negLit.Literal.Lexeme, negated: true, expectedKeyword);
            case HexLiteralTypeArgumentTypeArgumentNode hexLit:
                return GetCarrierStructNameForHexLiteral(hexLit.Literal.Lexeme, expectedKeyword);
            case BinaryLiteralTypeArgumentTypeArgumentNode binLit:
                return GetCarrierStructNameForBinaryLiteral(binLit.Literal.Lexeme, expectedKeyword);
            case TrueLiteralTypeArgumentTypeArgumentNode:
                return "Const_Bool_True";
            case FalseLiteralTypeArgumentTypeArgumentNode:
                return "Const_Bool_False";
            case CharLiteralTypeArgumentTypeArgumentNode charLit:
                return GetCarrierStructNameForCharLiteral(charLit.Literal.Lexeme);

            default:
                return "object"; // Fallback — should not happen for well-formed programs
        }
    }

    /// <summary>
    /// Checks if a name corresponds to a dependent type parameter on any type in the model.
    /// Used to detect forwarded dependent parameters in type argument positions.
    /// </summary>
    private bool IsDependentTypeParameterName(string name)
    {
        foreach (var symbol in _model.AllSymbols)
        {
            // Check type-level type parameters.
            var typeParams = GetTypeParametersFromType(symbol.DeclaredType);
            foreach (var tp in typeParams)
            {
                if (tp is DependentTypeParameterType depParam && depParam.Name == name)
                    return true;
            }

            // Check method-level type parameters.
            if (symbol is Symbol sym && sym.TypeParameters.Count > 0)
            {
                foreach (var tp in sym.TypeParameters)
                {
                    if (tp is DependentTypeParameterType depParam && depParam.Name == name)
                        return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Gets the carrier struct name for an integer literal type argument.
    /// When <paramref name="expectedKeyword"/> is provided, uses it for the type prefix.
    /// </summary>
    private static string GetCarrierStructNameForIntLiteral(string lexeme, bool negated, string? expectedKeyword = null)
    {
        var typePrefix = GetCapitalizedTypePrefix(expectedKeyword ?? "int");
        // Parse the integer value
        if (long.TryParse(lexeme, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var value))
        {
            if (negated) value = -value;
            var suffix = value < 0
                ? $"Neg{(-value).ToString(System.Globalization.CultureInfo.InvariantCulture)}"
                : value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return $"Const_{typePrefix}_{suffix}";
        }
        return $"Const_{typePrefix}_{(negated ? "Neg" : "")}{lexeme}";
    }

    /// <summary>
    /// Gets the carrier struct name for a hex literal type argument.
    /// </summary>
    private static string GetCarrierStructNameForHexLiteral(string lexeme, string? expectedKeyword = null)
    {
        var typePrefix = GetCapitalizedTypePrefix(expectedKeyword ?? "int");
        // Hex literals like 0xFF — parse to long
        var hex = lexeme.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? lexeme[2..] : lexeme;
        if (long.TryParse(hex, System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture, out var value))
        {
            return $"Const_{typePrefix}_{value.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        }
        return $"Const_{typePrefix}_{lexeme}";
    }

    /// <summary>
    /// Gets the carrier struct name for a binary literal type argument.
    /// </summary>
    private static string GetCarrierStructNameForBinaryLiteral(string lexeme, string? expectedKeyword = null)
    {
        var typePrefix = GetCapitalizedTypePrefix(expectedKeyword ?? "int");
        // Binary literals like 0b1010 — parse to long
        var bin = lexeme.StartsWith("0b", StringComparison.OrdinalIgnoreCase)
            ? lexeme[2..] : lexeme;
        try
        {
            var value = Convert.ToInt64(bin, 2);
            return $"Const_{typePrefix}_{value.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        }
        catch
        {
            return $"Const_{typePrefix}_{lexeme}";
        }
    }

    /// <summary>
    /// Maps a C# keyword to its capitalized form for carrier struct naming.
    /// </summary>
    private static string GetCapitalizedTypePrefix(string keyword)
    {
        return keyword switch
        {
            "int" => "Int",
            "uint" => "Uint",
            "long" => "Long",
            "ulong" => "Ulong",
            "short" => "Short",
            "ushort" => "Ushort",
            "byte" => "Byte",
            "sbyte" => "Sbyte",
            "bool" => "Bool",
            "char" => "Char",
            _ => "Int"
        };
    }

    /// <summary>
    /// Looks up the type parameters for a named type by searching the model's symbols.
    /// Returns null if the type is not found or has no type parameters.
    /// </summary>
    private IReadOnlyList<IType>? GetTypeParamsForName(string name)
    {
        foreach (var symbol in _model.AllSymbols)
        {
            if (symbol.Name == name && symbol.Kind is SymbolKind.Class or SymbolKind.Struct or SymbolKind.Interface)
            {
                var typeParams = GetTypeParametersFromType(symbol.DeclaredType);
                if (typeParams.Count > 0)
                    return typeParams;
            }
        }
        return null;
    }

    /// <summary>
    /// Gets the carrier struct name for a char literal type argument.
    /// </summary>
    private static string GetCarrierStructNameForCharLiteral(string lexeme)
    {
        // Char literals are like 'X' or '\n' — extract the character
        if (lexeme.Length >= 3 && lexeme[0] == '\'' && lexeme[^1] == '\'')
        {
            var inner = lexeme[1..^1];
            char c;
            if (inner.Length == 1)
            {
                c = inner[0];
            }
            else if (inner.StartsWith('\\'))
            {
                c = inner switch
                {
                    "\\n" => '\n',
                    "\\r" => '\r',
                    "\\t" => '\t',
                    "\\0" => '\0',
                    "\\'" => '\'',
                    "\\\\" => '\\',
                    _ when inner.StartsWith("\\u") && inner.Length == 6
                        => (char)Convert.ToInt32(inner[2..], 16),
                    _ => inner[1],
                };
            }
            else
            {
                c = inner[0];
            }

            var suffix = char.IsLetterOrDigit(c)
                ? c.ToString()
                : $"0x{((int)c).ToString("X2", System.Globalization.CultureInfo.InvariantCulture)}";
            return $"Const_Char_{suffix}";
        }
        return $"Const_Char_{lexeme}";
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

    // ─── Generic Constraint Emission ────────────────────────────────────

    /// <summary>
    /// Emits C# <c>where</c> clauses for all constrained type parameters on a type or method.
    /// Constraints are reordered to C#'s required order: class/struct → base class → interfaces/traits → new() last.
    /// Parameterized constructor constraints are excluded from the where clause (they use factory delegate lowering).
    /// </summary>
    private void EmitConstraintClauses(IReadOnlyList<IType> typeParameters)
    {
        foreach (var typeParam in typeParameters)
        {
            // Emit carrier interface constraint for dependent type parameters.
            if (typeParam is DependentTypeParameterType depParam)
            {
                var keyword = CSharpTypeMap.GetKeyword(depParam.ValueType);
                var capitalized = keyword switch
                {
                    "int" => "Int",
                    "uint" => "Uint",
                    "long" => "Long",
                    "ulong" => "Ulong",
                    "short" => "Short",
                    "ushort" => "Ushort",
                    "byte" => "Byte",
                    "sbyte" => "Sbyte",
                    "bool" => "Bool",
                    "char" => "Char",
                    _ => keyword,
                };
                AppendLine($"    where T{depParam.Name} : IConst{capitalized}");
                continue;
            }

            if (typeParam is not TypeParameterType tpt) continue;
            var constraints = tpt.Constraints;
            if (constraints is null) continue;

            var parts = new List<string>();

            // 1. class/struct first
            if (constraints.HasClassConstraint)
                parts.Add("class");
            else if (constraints.HasStructConstraint)
                parts.Add("struct");

            // 2. Base class
            if (constraints.BaseClassConstraint is not null)
                parts.Add(CSharpTypeMap.GetTypeName(constraints.BaseClassConstraint));

            // 3. Interfaces and traits
            foreach (var iface in constraints.InterfaceConstraints)
                parts.Add(CSharpTypeMap.GetTypeName(iface));
            foreach (var trait in constraints.TraitConstraints)
                parts.Add(CSharpTypeMap.GetTypeName(trait));

            // 4. new() last (only parameterless constructor constraint)
            var hasParameterlessNew = constraints.ConstructorConstraints
                .Any(c => c.Count == 0);
            if (hasParameterlessNew)
                parts.Add("new()");

            if (parts.Count > 0)
            {
                AppendLine($"    where {tpt.Name} : {string.Join(", ", parts)}");
            }
        }
    }

    /// <summary>
    /// Gets the parameterized (non-parameterless) constructor constraints for a type parameter.
    /// These are the constraints that require factory delegate lowering.
    /// </summary>
    private static IReadOnlyList<IReadOnlyList<IType>> GetParameterizedConstructorConstraints(TypeParameterConstraints? constraints)
    {
        if (constraints is null) return Array.Empty<IReadOnlyList<IType>>();
        return constraints.ConstructorConstraints
            .Where(c => c.Count > 0)
            .ToList();
    }

    /// <summary>
    /// Generates the factory delegate field name for a parameterized constructor constraint.
    /// E.g., for new(int, string) on T → "_factoryT_int_string"
    /// </summary>
    private static string GetFactoryFieldName(TypeParameterType typeParam, IReadOnlyList<IType> paramTypes)
    {
        var paramPart = string.Join("_", paramTypes.Select(t => SanitizeTypeName(t)));
        return $"_factory{typeParam.Name}_{paramPart}";
    }

    /// <summary>
    /// Generates the factory delegate parameter name for a parameterized constructor constraint.
    /// E.g., for new(int, string) on T → "factoryT_int_string"
    /// </summary>
    private static string GetFactoryParamName(TypeParameterType typeParam, IReadOnlyList<IType> paramTypes)
    {
        var paramPart = string.Join("_", paramTypes.Select(t => SanitizeTypeName(t)));
        return $"factory{typeParam.Name}_{paramPart}";
    }

    /// <summary>
    /// Sanitizes a type name for use in identifiers (removes angle brackets, dots, etc.).
    /// </summary>
    private static string SanitizeTypeName(IType type)
    {
        var name = CSharpTypeMap.GetTypeName(type);
        return name.Replace("<", "").Replace(">", "").Replace(",", "").Replace(" ", "").Replace(".", "");
    }

    /// <summary>
    /// Generates the Func delegate type string for a factory delegate.
    /// E.g., for new(int, string) on T → "Func&lt;int, string, T&gt;"
    /// </summary>
    private static string GetFactoryDelegateType(TypeParameterType typeParam, IReadOnlyList<IType> paramTypes)
    {
        var typeArgs = paramTypes.Select(t => CSharpTypeMap.GetTypeName(t)).ToList();
        typeArgs.Add(typeParam.Name);
        return $"Func<{string.Join(", ", typeArgs)}>";
    }

    /// <summary>
    /// Emits factory delegate fields for all parameterized constructor constraints on the given type parameters.
    /// </summary>
    private void EmitFactoryDelegateFields(IReadOnlyList<IType> typeParameters)
    {
        foreach (var tp in typeParameters)
        {
            if (tp is not TypeParameterType typeParam) continue;
            var parameterizedConstraints = GetParameterizedConstructorConstraints(typeParam.Constraints);
            foreach (var paramTypes in parameterizedConstraints)
            {
                var fieldName = GetFactoryFieldName(typeParam, paramTypes);
                var delegateType = GetFactoryDelegateType(typeParam, paramTypes);
                AppendLine($"private readonly {delegateType} {fieldName};");
            }
        }
    }

    /// <summary>
    /// Checks whether any type parameter has parameterized constructor constraints
    /// that require factory delegate lowering.
    /// </summary>
    private static bool HasParameterizedConstructorConstraints(IReadOnlyList<IType> typeParameters)
    {
        return typeParameters.Any(tp =>
            tp is TypeParameterType tpt && tpt.Constraints is not null &&
            tpt.Constraints.ConstructorConstraints.Any(c => c.Count > 0));
    }

    /// <summary>
    /// Emits a synthetic constructor that accepts factory delegates for parameterized constructor constraints.
    /// </summary>
    private void EmitFactoryDelegateConstructor(string typeName, IReadOnlyList<IType> typeParameters)
    {
        var parameters = new List<string>();
        var assignments = new List<string>();

        foreach (var tp in typeParameters)
        {
            if (tp is not TypeParameterType typeParam) continue;
            var parameterizedConstraints = GetParameterizedConstructorConstraints(typeParam.Constraints);
            foreach (var paramTypes in parameterizedConstraints)
            {
                var paramName = GetFactoryParamName(typeParam, paramTypes);
                var fieldName = GetFactoryFieldName(typeParam, paramTypes);
                var delegateType = GetFactoryDelegateType(typeParam, paramTypes);
                parameters.Add($"{delegateType} {paramName}");
                assignments.Add($"{fieldName} = {paramName};");
            }
        }

        if (parameters.Count == 0) return;

        AppendLine();
        AppendLine($"public {typeName}({string.Join(", ", parameters)})");
        AppendLine("{");
        _indentLevel++;
        foreach (var assignment in assignments)
        {
            AppendLine(assignment);
        }
        _indentLevel--;
        AppendLine("}");
    }

    /// <summary>
    /// Emits factory field assignment statements for use inside user-defined constructors.
    /// E.g., "_factoryT_int = factoryT_int;"
    /// </summary>
    private void EmitFactoryFieldAssignments(IReadOnlyList<IType> typeParameters)
    {
        foreach (var tp in typeParameters)
        {
            if (tp is not TypeParameterType typeParam) continue;
            var parameterizedConstraints = GetParameterizedConstructorConstraints(typeParam.Constraints);
            foreach (var paramTypes in parameterizedConstraints)
            {
                var paramName = GetFactoryParamName(typeParam, paramTypes);
                var fieldName = GetFactoryFieldName(typeParam, paramTypes);
                AppendLine($"{fieldName} = {paramName};");
            }
        }
    }

    /// <summary>
    /// Gets the additional factory delegate parameters that should be appended to a method signature
    /// for generic methods with parameterized constructor constraints.
    /// </summary>
    private static string GetFactoryDelegateMethodParams(IReadOnlyList<IType> typeParameters)
    {
        var parameters = new List<string>();
        foreach (var tp in typeParameters)
        {
            if (tp is not TypeParameterType typeParam) continue;
            var parameterizedConstraints = GetParameterizedConstructorConstraints(typeParam.Constraints);
            foreach (var paramTypes in parameterizedConstraints)
            {
                var paramName = GetFactoryParamName(typeParam, paramTypes);
                var delegateType = GetFactoryDelegateType(typeParam, paramTypes);
                parameters.Add($"{delegateType} {paramName}");
            }
        }
        return parameters.Count > 0 ? string.Join(", ", parameters) : "";
    }

    /// <summary>
    /// Checks if a type parameter has a parameterized constructor constraint matching the given argument types.
    /// Returns the matching constraint's parameter types, or null if no match.
    /// </summary>
    private static IReadOnlyList<IType>? FindMatchingParameterizedConstraint(
        TypeParameterType typeParam, IReadOnlyList<IType> argTypes)
    {
        if (typeParam.Constraints is null) return null;
        // Find the constraint whose parameter types match the argument types by name.
        // We compare using the sanitized C# type name (same mechanism used for factory naming)
        // to ensure the correct factory delegate is selected.
        return typeParam.Constraints.ConstructorConstraints
            .FirstOrDefault(c => c.Count == argTypes.Count && c.Count > 0 &&
                c.Select(SanitizeTypeName).SequenceEqual(argTypes.Select(SanitizeTypeName)));
    }

    /// <summary>
    /// Gets the type parameters for a declared type (class, struct, or interface).
    /// </summary>
    private static IReadOnlyList<IType> GetTypeParametersFromType(IType? type)
    {
        return type switch
        {
            ClassType ct => ct.TypeParameters,
            StructType st => st.TypeParameters,
            InterfaceType it => it.TypeParameters,
            _ => Array.Empty<IType>()
        };
    }

    /// <summary>
    /// Collects the resolved types of all arguments in an argument list node.
    /// </summary>
    private IReadOnlyList<IType> GetArgumentTypes(ArgumentListNode? argList)
    {
        if (argList is null) return Array.Empty<IType>();
        var types = new List<IType>();
        var firstType = _model.GetTypeOf(argList.Expression);
        if (firstType is not null) types.Add(firstType);
        foreach (var child in argList.Children())
        {
            if (child is ExpressionNode expr && !ReferenceEquals(expr, argList.Expression))
            {
                var t = _model.GetTypeOf(expr);
                if (t is not null) types.Add(t);
            }
        }
        return types;
    }

    /// <summary>
    /// Generates factory lambda expressions for call-site transformation.
    /// When constructing a type with parameterized constructor constraints,
    /// the caller must supply lambda expressions that create instances.
    /// E.g., for new(int) on T with type arg MyClass → "(arg0) => new MyClass(arg0)"
    /// </summary>
    private string GenerateFactoryLambdas(IReadOnlyList<IType> typeParameters, TypeArgumentListNode? typeArgs)
    {
        var lambdas = new List<string>();

        // Resolve type arguments from the type argument list node.
        var resolvedTypeArgs = new List<string>();
        if (typeArgs is not null)
        {
            resolvedTypeArgs.Add(EmitSingleTypeArgument(typeArgs.TypeArgument));
            foreach (var arg in typeArgs.TypeArguments)
            {
                resolvedTypeArgs.Add(EmitSingleTypeArgument(arg));
            }
        }

        for (var i = 0; i < typeParameters.Count; i++)
        {
            if (typeParameters[i] is not TypeParameterType typeParam) continue;
            var parameterizedConstraints = GetParameterizedConstructorConstraints(typeParam.Constraints);
            if (parameterizedConstraints.Count == 0) continue;

            // Get the concrete type argument for this type parameter.
            var concreteType = i < resolvedTypeArgs.Count ? resolvedTypeArgs[i] : typeParam.Name;

            foreach (var paramTypes in parameterizedConstraints)
            {
                // Generate lambda: (arg0, arg1, ...) => new ConcreteType(arg0, arg1, ...)
                var argNames = Enumerable.Range(0, paramTypes.Count)
                    .Select(j => $"arg{j}")
                    .ToList();
                var argList = string.Join(", ", argNames);
                lambdas.Add($"({argList}) => new {concreteType}({argList})");
            }
        }

        return string.Join(", ", lambdas);
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
