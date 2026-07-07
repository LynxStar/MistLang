using System.Collections.Immutable;
using System.Text;
using Mist.Analysis.Builtin;
using Mist.Backends.CSharpTranspiler.Intrinsics;
using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;
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
    public static void Emit(ISemanticModel model, string outputDirectory, string projectName, DiagnosticBag diagnostics, ProjectOutputType outputType = ProjectOutputType.Executable)
    {
        _ = Directory.CreateDirectory(outputDirectory);
        EmitCsproj(outputDirectory, projectName, outputType);

        var files = EmitToMemory(model, diagnostics);

        foreach(var (relativePath, content) in files)
        {
            var fullPath = Path.Combine(outputDirectory, relativePath);
            var dir = Path.GetDirectoryName(fullPath);
            if(dir is not null)
            {
                _ = Directory.CreateDirectory(dir);
            }

            File.WriteAllText(fullPath, content);
        }
    }

    /// <summary>
    /// Emits C# source content in memory without writing to disk.
    /// Returns a dictionary mapping relative file paths to their C# content.
    /// Useful for tests that need to inspect emitted content without disk I/O.
    /// </summary>
    public static Dictionary<string, string> EmitToMemory(ISemanticModel model, DiagnosticBag diagnostics)
    {
        // Ensure the needs-async propagation pass has run. This is idempotent —
        // if the backend already ran it, re-running is harmless (already-marked
        // symbols stay marked).
        NeedsAsyncPropagationPass.Run(model);

        // Run the ValueTask usage analysis pass to determine which async methods
        // can emit ValueTask<T> instead of Task<T>. Must run after needs-async
        // propagation so that all async method symbols are properly identified.
        ValueTaskUsageAnalysisPass.Run();

        var files = new Dictionary<string, string>(StringComparer.Ordinal);

        // Collect namespaces from Backend-origin symbols for using directives.
        var backendNamespaces = CollectBackendNamespaces(model);

        // Emit one .cs file per Mist source file.
        // NOTE: Only Mist-declared types are emitted. Ambient types projected from
        // .NET assemblies (including any synthesized interfaces like IEnumerable<T>
        // on duck-typed types such as Span<T>) are never in model.Units and therefore
        // never appear in emitted C# output. (Requirements: 10.4, 10.5)
        var anyNeedsMistVoid = false;
        for(var i = 0; i < model.Units.Count; i++)
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
            if(unit.OriginProjectPath is not null)
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
            if(i < model.Asts.Length && model.Asts[i] is CompilationUnitNode compilationUnit)
            {
                var isReferencedProject = unit.OriginProjectPath is not null;
                var writer = new CSharpSourceWriter(model, backendNamespaces, unit.Source, diagnostics, isReferencedProject);
                var csContent = writer.EmitCompilationUnit(compilationUnit);

                // Skip emitting files that contain no meaningful type declarations
                // (e.g., stdlib files where all types are backend-backed and were skipped).
                // Top-level statement files are never empty — they always have statements to emit.
                var hasTopLevelStatements = compilationUnit.Items.Any(item => item is StatementItemTopLevelItemNode);
                if(hasTopLevelStatements || !IsEmptyEmission(csContent))
                {
                    files[relativePath] = csContent;
                }

                if(writer.NeedsMistVoidStruct)
                {
                    anyNeedsMistVoid = true;
                }
            }
        }

        // Emit the MistVoid helper struct once into a shared file if any compilation unit uses void as a value.
        if(anyNeedsMistVoid)
        {
            files["MistVoid.g.cs"] = GenerateMistVoidFile();
        }

        // Emit union helper types into a single UnionHelpers.cs file if any union types are used.
        var unionTypes = UnionHelperGenerator.CollectUnionTypes(model);
        if(unionTypes.Count > 0)
        {
            files["UnionHelpers.cs"] = UnionHelperGenerator.GenerateUnionHelpersFile(unionTypes);
        }

        // Emit const-carrier interfaces and carrier structs for dependent type parameters.
        var usedDependentValueTypes = DependentTypeHelpersGenerator.CollectUsedDependentValueTypes(model);
        var carrierRegistry = CarrierStructRegistry.CollectFromModel(model);
        if(usedDependentValueTypes.Count > 0 || carrierRegistry.Entries.Count > 0)
        {
            files["DependentTypeHelpers.g.cs"] = DependentTypeHelpersGenerator.GenerateHelpersFile(usedDependentValueTypes, carrierRegistry);
        }

        // Emit the MistContext runtime helper when any compilation unit uses structured context.
        if(MistContextGenerator.UsesStructuredContext(model))
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
        if(trimmed.IsEmpty)
        {
            return true;
        }

        // Check if the content contains any type declaration keywords after the namespace line.
        return !content.Contains("class ")
            && !content.Contains("struct ")
            && !content.Contains("interface ")
            && !content.Contains("enum ");
    }

    private static void EmitCsproj(string outputDirectory, string projectName, ProjectOutputType outputType)
    {
        var csprojPath = Path.Combine(outputDirectory, $"{projectName}.csproj");
        var outputTypeValue = outputType == ProjectOutputType.Library ? "Library" : "Exe";

        // Library projects that inline referenced sources with Main methods conflict with
        // the test SDK's auto-generated entry point (CS0017). Suppress by disabling the
        // test SDK's generated Program file so the inlined Main is the sole entry point.
        // This workaround is temporary until multi-project emission is implemented
        // (see project/TODO-multi-project-emission.md).
        var libraryEntryPointWorkaround = outputType == ProjectOutputType.Library
            ? """

              <PropertyGroup>
                <GenerateProgramFile>false</GenerateProgramFile>
              </PropertyGroup>
            """
            : "";

        var csprojContent = $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>{outputTypeValue}</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
                <Deterministic>true</Deterministic>
                <EnableNETAnalyzers>false</EnableNETAnalyzers>
              </PropertyGroup>{libraryEntryPointWorkaround}
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

        // Always include System — the C# backend emits types like Func<>,
        // Array, ArgumentException, etc. that live in the System namespace.
        _ = namespaces.Add("System");

        foreach(var symbol in model.AllSymbols)
        {
            if(symbol.Origin == SymbolOrigin.Backend)
            {
                if(symbol.DeclaredType is ClassType ct && ct.ContainingNamespace is not null)
                {
                    _ = namespaces.Add(ct.ContainingNamespace);
                }
                else if(symbol.DeclaredType is StructType st && st.ContainingNamespace is not null)
                {
                    _ = namespaces.Add(st.ContainingNamespace);
                }
                else if(symbol.DeclaredType is AbstractType it && it.ContainingNamespace is not null)
                {
                    _ = namespaces.Add(it.ContainingNamespace);
                }
                else if(symbol.Kind == SymbolKind.Namespace)
                {
                    _ = namespaces.Add(symbol.Name);
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
                public static implicit operator System.Type(MistVoid v) => typeof(void);
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
    private readonly StringBuilder sb = new();
    private readonly ISemanticModel model;
    private readonly ImmutableArray<string> backendNamespaces;
    private readonly Common.Source.SourceText sourceText;
    private readonly IIntrinsicEmissionRegistry intrinsicRegistry;
    private readonly DiagnosticBag diagnostics;
    private int indentLevel;
    private bool inValueReturningVoidMethod;
    /// <summary>
    /// When <c>true</c>, <c>Void.Void</c> expressions emit <c>null</c> instead of
    /// <c>MistVoid.Instance</c>. Set when the enclosing context targets a two-constituent
    /// <c>T | void</c> union (the "optional" pattern) that maps to C# <c>T?</c> or <c>T</c>
    /// (reference type) where absence is represented by <c>null</c>.
    /// </summary>
    private bool emitVoidAsNull;
    /// <summary>
    /// Return type of the enclosing method, used by <see cref="EmitReturnStatement"/>
    /// to detect covariant unknown widening at return sites (Task 31.1).
    /// <c>null</c> outside a method body or when the return type is
    /// unavailable (e.g., void methods where no widening can occur).
    /// </summary>
    private IType? currentMethodReturnType;
    /// <summary>
    /// Type parameters of the current method being emitted that have parameterized
    /// constructor constraints. Used by <c>new T(args)</c> emission to determine
    /// whether to use a method parameter name (factoryT_...) vs. a class field name (_factoryT_...).
    /// Null when not inside a generic method with factory parameters.
    /// </summary>
    private IReadOnlyList<IType>? currentMethodFactoryTypeParams;
    /// <summary>
    /// Type parameters of the current class/struct being emitted that have parameterized
    /// constructor constraints. Used by <see cref="EmitConstructorDeclaration"/> to append
    /// factory delegate parameters to user-defined constructors.
    /// Null when not inside a type with parameterized constructor constraints.
    /// </summary>
    private IReadOnlyList<IType>? currentClassFactoryTypeParams;
    private readonly HashSet<string> valueReturningVoidMethods = new(StringComparer.Ordinal);
    /// <summary>
    /// The name of the currently-being-emitted extension class. Set during
    /// <summary>
    /// Pending variable bindings from union pattern matching. When an <c>is T t</c>
    /// pattern is emitted against a union scrutinee, the condition becomes
    /// <c>scrutinee.Is{T}</c> and the variable extraction <c>var t = scrutinee.As{T}</c>
    /// is deferred to the start of the then-branch block.
    /// </summary>
    private readonly List<string> pendingUnionPatternBindings = new();
    /// <summary>
    /// Monotonically increasing ID for generating unique temporary variable names
    /// in desugared property pattern emission on unknown scrutinees.
    /// </summary>
    private int nextPatternId;
    /// <summary>
    /// Maps discarded task origin nodes (expression nodes from expression statements)
    /// to their <see cref="DiscardedTaskInfo"/> for the current block being emitted.
    /// Used by <see cref="EmitStatementsFromIndex"/> to detect discarded-task expression
    /// statements and route them through the unified drop try/finally wrapping.
    /// </summary>
    private Dictionary<Storm.Runtime.SyntaxNode, DiscardedTaskInfo>? currentBlockDiscardedTasks;
    private readonly bool isReferencedProject;
    /// <summary>
    /// Tracks whether the emitter is currently inside a struct declaration.
    /// Used by <see cref="EmitMethodDeclaration"/> to emit the C# <c>readonly</c>
    /// modifier on struct methods (C# has no readonly method concept for classes).
    /// </summary>
    private bool insideStruct;
    /// <summary>
    /// The <see cref="ClassType"/> of the class currently being emitted, or <c>null</c>
    /// if not inside a class declaration. Used by <see cref="EmitMethodDeclaration"/>
    /// to detect implicit override matches against CLR base class virtual/abstract methods.
    /// </summary>
    private ClassType? currentClassType;
    /// <summary>
    /// The namespace of the current compilation unit being emitted.
    /// Used to scope type alias fallback resolution to the correct namespace.
    /// </summary>
    private string? currentNamespace;
    /// <summary>
    /// When non-null, indicates that the current expression being emitted is the
    /// initializer of a typed variable declaration whose type is an array type.
    /// The emitter uses this to emit <c>new T[] { ... }</c> instead of <c>new[] { ... }</c>
    /// for array literals assigned to explicitly-typed array variables.
    /// </summary>
    private IType? arrayLiteralTargetElementType;
    /// <summary>
    /// Tracks the dependent type parameter names declared on the current enclosing type
    /// (class or struct) being emitted. Used by the identifier emission fallback to
    /// distinguish between a dependent type parameter reference and a field/property
    /// with the same name declared in a different type.
    /// </summary>
    private HashSet<string>? currentTypeDependentParamNames;
    /// <summary>
    /// The symbol of the method currently being emitted. Used to determine whether
    /// the enclosing context is sync or async for auto-unwrap emission.
    /// </summary>
    private Symbol? currentMethodSymbol;
    /// <summary>
    /// Set to <c>true</c> while emitting the operand of an <c>async</c> functor expression.
    /// When inside an async functor, auto-unwrap of async method calls is suppressed
    /// because the functor explicitly captures the <c>Task&lt;T&gt;</c>.
    /// </summary>
    private bool insideAsyncFunctor;
    /// <summary>
    /// Set to <c>true</c> while emitting the operand of an explicit <c>await</c> expression.
    /// When inside an explicit await, auto-unwrap is suppressed to prevent double-await
    /// (the explicit await already handles the async call).
    /// </summary>
    private bool insideExplicitAwait;
    /// <summary>
    /// Set to <c>true</c> while emitting the body of an async lambda. In async context,
    /// auto-await must NOT fire — calls to async methods or Func&lt;Task&lt;T&gt;&gt; should
    /// return <c>Task&lt;T&gt;</c> without implicit unwrapping.
    /// </summary>
    private bool insideAsyncLambda;
    /// <summary>
    /// Set to <c>true</c> while emitting the body of a sync lambda that contains
    /// auto-await calls. In this context, auto-await emits <c>.GetAwaiter().GetResult()</c>
    /// instead of <c>await</c> because the lambda's declared return type is not <c>Task&lt;T&gt;</c>.
    /// </summary>
    private bool insideSyncLambdaAutoAwait;
    /// <summary>
    /// Active associated type slot substitutions for the current method being emitted.
    /// Maps dotted names like "T.Enumerator" to synthesized type parameter names like "TEnumerator".
    /// Set during <see cref="EmitMethodDeclaration"/> when the method has type parameters
    /// constrained by abstract types with associated type slots.
    /// </summary>
    private Dictionary<string, string>? currentMethodSlotSubstitutions;

    /// <summary>
    /// Parameterized associated type slots for the current abstract type being emitted.
    /// These slots have their own type parameters and cannot be lifted to C# type parameters.
    /// Instead, references to them are erased to their constraint interface.
    /// </summary>
    private List<AssociatedTypeSlotType>? currentParameterizedSlots;

    /// <summary>
    /// Maps abstract type parameter names to their concrete type arguments for the
    /// current implementor class. Used during parameterized slot erasure to substitute
    /// e.g., TIn → int when emitting ITransform&lt;int, string&gt; instead of ITransform&lt;TIn, string&gt;.
    /// </summary>
    private Dictionary<string, string>? currentAbstractTypeParamSubs;

    /// <summary>
    /// Tracks linear variables that have a conditional move in an if/else statement.
    /// When the emitter encounters an if-statement and this set is non-empty, it injects
    /// <c>Drop()</c> calls at the end of branches where the variable is still live.
    /// Each entry maps the local name to whether it's task-like (emit <c>await</c>).
    /// </summary>
    private readonly Dictionary<string, bool> pendingBranchDrops = new(StringComparer.Ordinal);

    /// <summary>
    /// Counter for generating unique temp variable names for with-block drop targets
    /// when the bound expression has no named variable (discard, push, or block forms).
    /// </summary>
    private int withDropCounter;

    /// <summary>
    /// Counter for generating unique temp variable names for array initializer constructions
    /// (<c>new Array&lt;T, Size&gt;(fn)</c>) that are lifted to locals.
    /// </summary>
    private int arrayInitCounter;

    /// <summary>
    /// Counter for generating unique temp variable names in non-provable <c>as</c>
    /// expression emissions (<c>__as_tmp_0</c>, <c>__as_tmp_1</c>, etc.).
    /// </summary>
    private int asExprCounter;

    /// <summary>
    /// Pending statements emitted by array initializer construction lowering.
    /// When <c>new Array&lt;T, Size&gt;(fn)</c> appears in expression position,
    /// the emitter generates the allocation + for-loop into this list and returns
    /// the temp variable name as the expression value. The enclosing statement
    /// emitter flushes these before its own output line.
    /// </summary>
    private readonly List<string> pendingArrayInitStatements = new();

    /// <summary>
    /// Pre-computed set of all dependent type parameter names across the entire model.
    /// Eliminates the per-call-site O(AllSymbols) iteration in IsDependentTypeParameterName.
    /// </summary>
    private readonly HashSet<string> dependentTypeParamNames;

    private const string IndentUnit = "    ";

    /// <summary>
    /// Indicates whether this compilation unit requires the MistVoid helper struct.
    /// Checked by the emitter after emission to decide whether to generate the shared MistVoid file.
    /// </summary>
    public bool NeedsMistVoidStruct { get; private set; }

    public CSharpSourceWriter(
        ISemanticModel model,
        ImmutableArray<string> backendNamespaces,
        Common.Source.SourceText sourceText,
        DiagnosticBag diagnostics,
        bool isReferencedProject = false)
    {
        this.model = model;
        this.backendNamespaces = backendNamespaces;
        this.sourceText = sourceText;
        this.diagnostics = diagnostics;
        intrinsicRegistry = new IntrinsicEmissionTable();
        this.isReferencedProject = isReferencedProject;

        // Pre-compute dependent type parameter names for O(1) lookups during emission.
        dependentTypeParamNames = new HashSet<string>(StringComparer.Ordinal);
        foreach(var symbol in model.AllSymbols)
        {
            var typeParams = GetTypeParametersFromType(symbol.DeclaredType);
            foreach(var tp in typeParams)
            {
                if(tp is DependentTypeParameterType depParam)
                {
                    _ = dependentTypeParamNames.Add(depParam.Name);
                }
            }

            if(symbol is Symbol sym && sym.TypeParameters.Count > 0)
            {
                foreach(var tp in sym.TypeParameters)
                {
                    if(tp is DependentTypeParameterType depParam)
                    {
                        _ = dependentTypeParamNames.Add(depParam.Name);
                    }
                }
            }
        }
    }

    public string EmitCompilationUnit(CompilationUnitNode cu)
    {
        // Detect top-level statements — if present, delegate to the TLS-specific emitter.
        var topLevelStatements = cu.Items
            .OfType<StatementItemTopLevelItemNode>()
            .ToList();

        if(topLevelStatements.Count > 0)
        {
            if(isReferencedProject)
            {
                // Referenced project TLS files: skip the top-level statements (they are
                // the entry point of the referenced project, not relevant here) and emit
                // only the type declarations as a normal compilation unit.
                return EmitReferencedProjectTlsUnit(cu);
            }

            return EmitTopLevelStatementUnit(cu, topLevelStatements);
        }

        // Analyze which void methods have their return values consumed at call sites.
        AnalyzeValueReturningVoidMethods(cu);

        // Collect using directives from the source.
        var usings = new HashSet<string>(StringComparer.Ordinal);

        foreach(var usingDecl in cu.Usings)
        {
            var name = ExtractQualifiedName(usingDecl.Name);
            _ = usings.Add(name);
        }
        // Also collect using directives that appear after a file-scoped namespace
        // (parsed as UsingItem top-level items rather than CompilationUnit.Usings).
        foreach(var item in cu.Items)
        {
            if(item is UsingItemTopLevelItemNode usingItem)
            {
                var name = ExtractQualifiedName(usingItem.UsingDeclaration.Name);
                _ = usings.Add(name);
            }
        }

        // Also add backend namespaces that are referenced.
        foreach(var ns in backendNamespaces)
        {
            _ = usings.Add(ns);
        }

        // Add global usings for this compilation unit.
        foreach(var unit in model.Units)
        {
            if(ReferenceEquals(unit.Source, sourceText))
            {
                foreach(var globalNs in unit.GlobalUsings)
                {
                    _ = usings.Add(globalNs);
                }

                break;
            }
        }

        // Emit using System.Text.RegularExpressions when the file references Standard.Regex.
        if(SourceReferencesRegex())
        {
            _ = usings.Add("System.Text.RegularExpressions");
        }

        // Remove the Mist stdlib namespace — most Standard.* types are mapped to their
        // CLR counterparts (System.Console, int, etc.) via the CSharpTypeMap. However,
        // some Standard-namespace types (e.g., IClonable<T>) are emitted as-is without
        // a CLR mapping. If the current file implements or references such a type, we
        // must keep `using Standard;` so the emitted C# can resolve the reference.
        if(!FileReferencesUnmappedStandardType(cu))
        {
            _ = usings.Remove("Standard");
        }

        // Emit using directives.
        foreach(var ns in usings.OrderBy(n => n, StringComparer.Ordinal))
        {
            AppendLine($"using {ns};");
        }

        if(usings.Count > 0)
        {
            AppendLine();
        }

        // Emit namespace and type declarations from items.
        // Top-level types (outside any explicit namespace) belong to the inferred
        // namespace. We look up each type's ContainingNamespace from the semantic
        // model and emit a file-scoped namespace declaration before the first type
        // in each inferred namespace group.
        string? emittedInferredNamespace = null;

        // Collect top-level block namespaces for same-name merging (Requirement 11.3).
        var topLevelBlockNamespaces = new Dictionary<string, List<BlockNamespaceNamespaceDeclarationNode>>(StringComparer.Ordinal);
        var topLevelBlockNamespaceOrder = new List<string>();
        var emittedTopLevelNamespaces = new HashSet<string>(StringComparer.Ordinal);

        foreach(var item in cu.Items)
        {
            if(item is NamespaceItemTopLevelItemNode nsItem
                && nsItem.NamespaceDeclaration is BlockNamespaceNamespaceDeclarationNode blockNsDecl)
            {
                var nsName = ExtractQualifiedName(blockNsDecl.Name);
                if(!topLevelBlockNamespaces.TryGetValue(nsName, out var list))
                {
                    list = new List<BlockNamespaceNamespaceDeclarationNode>();
                    topLevelBlockNamespaces[nsName] = list;
                    topLevelBlockNamespaceOrder.Add(nsName);
                }

                list.Add(blockNsDecl);
            }
        }

        foreach(var item in cu.Items)
        {
            switch(item)
            {
                case NamespaceItemTopLevelItemNode nsItem:
                    {
                        switch(nsItem.NamespaceDeclaration)
                        {
                            case FileScopedNamespaceNamespaceDeclarationNode fileScopedNsDecl:
                                // File-scoped namespaces are emitted directly (no merging needed).
                                EmitNamespaceDeclaration(nsItem.NamespaceDeclaration);
                                // Track the emitted namespace so subsequent top-level TypeItems
                                // don't re-emit it (supports using-after-namespace ordering).
                                var fileScopedNsName = ExtractQualifiedName(fileScopedNsDecl.Name);
                                if(emittedInferredNamespace is null)
                                {
                                    emittedInferredNamespace = fileScopedNsName;
                                    currentNamespace = fileScopedNsName;
                                }

                                break;
                            case BlockNamespaceNamespaceDeclarationNode blockNsDecl:
                                {
                                    // Emit merged block namespace on first encounter of this FQN.
                                    var nsName = ExtractQualifiedName(blockNsDecl.Name);
                                    if(emittedTopLevelNamespaces.Add(nsName))
                                    {
                                        EmitMergedTopLevelBlockNamespace(nsName, topLevelBlockNamespaces[nsName]);
                                    }

                                    break;
                                }
                        }

                        break;
                    }
                case TypeItemTopLevelItemNode typeItem:
                    {
                        // Determine the type's namespace from the semantic model.
                        var typeNamespace = GetTypeContainingNamespace(typeItem.TypeDeclaration);

                        // If this type has a namespace and we haven't emitted it yet,
                        // emit a file-scoped namespace declaration.
                        if(typeNamespace is not null && typeNamespace != emittedInferredNamespace)
                        {
                            if(emittedInferredNamespace is not null)
                            {
                                // Separate namespace sections with a blank line.
                                AppendLine();
                            }

                            AppendLine($"namespace {typeNamespace};");
                            AppendLine();
                            emittedInferredNamespace = typeNamespace;
                            currentNamespace = typeNamespace;
                        }

                        EmitTypeDeclaration(typeItem.TypeDeclaration);
                        break;
                    }
                case ExtensionItemTopLevelItemNode extensionItem:
                    EmitExtensionBlock(extensionItem.ExtensionDeclaration);
                    break;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Emits a compilation unit that contains top-level statements using C# top-level statement syntax.
    /// Statements are emitted directly (no class/method wrapper), followed by type declarations in their namespace.
    /// </summary>
    private string EmitTopLevelStatementUnit(CompilationUnitNode cu, List<StatementItemTopLevelItemNode> statements)
    {
        // Analyze which void methods have their return values consumed at call sites.
        AnalyzeValueReturningVoidMethods(cu);

        // 1. Emit using declarations at the top.
        var usings = new HashSet<string>(StringComparer.Ordinal);

        foreach(var usingDecl in cu.Usings)
        {
            var name = ExtractQualifiedName(usingDecl.Name);
            _ = usings.Add(name);
        }
        // Also collect using directives that appear after a file-scoped namespace.
        foreach(var item in cu.Items)
        {
            if(item is UsingItemTopLevelItemNode usingItem)
            {
                var name = ExtractQualifiedName(usingItem.UsingDeclaration.Name);
                _ = usings.Add(name);
            }
        }

        foreach(var ns in backendNamespaces)
        {
            _ = usings.Add(ns);
        }

        if(SourceReferencesRegex())
        {
            _ = usings.Add("System.Text.RegularExpressions");
        }

        // 3. Collect type declarations (non-statement items) for namespace analysis.
        var typeDecls = cu.Items
            .Where(i => i is not StatementItemTopLevelItemNode)
            .ToList();

        // Determine the namespace for type declarations early so we can add a using for it.
        // In a TLS file, type declarations get their own namespace block, and the
        // top-level statements need a using directive to reference those types.
        string? inferredNamespace = null;

        // Check file-scoped namespace declarations first.
        foreach(var item in typeDecls)
        {
            if(item is NamespaceItemTopLevelItemNode nsItem
                && nsItem.NamespaceDeclaration is FileScopedNamespaceNamespaceDeclarationNode fileScopedNs)
            {
                inferredNamespace = ExtractQualifiedName(fileScopedNs.Name);
                break;
            }
        }

        // If no file-scoped namespace, find the namespace from type declarations.
        if(inferredNamespace is null)
        {
            foreach(var item in typeDecls)
            {
                switch(item)
                {
                    case TypeItemTopLevelItemNode typeItem:
                        inferredNamespace = GetTypeContainingNamespace(typeItem.TypeDeclaration);
                        break;
                    case NamespaceItemTopLevelItemNode nsItem
                        when nsItem.NamespaceDeclaration is BlockNamespaceNamespaceDeclarationNode:
                        // Block namespaces carry their own name.
                        break;
                }

                if(inferredNamespace is not null)
                {
                    break;
                }
            }
        }

        // If type declarations live in a namespace, add a using for it so
        // top-level statements can reference those types without qualification.
        if(inferredNamespace is not null)
        {
            _ = usings.Add(inferredNamespace);
        }

        // Remove the Mist stdlib namespace — it has no C# equivalent.
        _ = usings.Remove("Standard");

        foreach(var ns in usings.OrderBy(n => n, StringComparer.Ordinal))
        {
            AppendLine($"using {ns};");
        }

        if(usings.Count > 0)
        {
            AppendLine();
        }

        // 2. Emit top-level statements directly (no class/method wrapper).
        // Iterate items in order and emit only the statement items.
        foreach(var item in cu.Items)
        {
            if(item is StatementItemTopLevelItemNode stmtItem)
            {
                EmitStatement(stmtItem.Statement);
            }
        }

        // 3. Emit type declarations after statements, wrapped in their namespace block.
        if(typeDecls.Count > 0)
        {
            // Separate statements from type declarations with a blank line.
            if(statements.Count > 0)
            {
                AppendLine();
            }

            // Emit type declarations inside a namespace block if we have a namespace.
            if(inferredNamespace is not null)
            {
                currentNamespace = inferredNamespace;
                AppendLine($"namespace {inferredNamespace}");
                AppendLine("{");
                indentLevel++;

                foreach(var item in typeDecls)
                {
                    switch(item)
                    {
                        case TypeItemTopLevelItemNode typeItem:
                            EmitTypeDeclaration(typeItem.TypeDeclaration);
                            break;
                        case ExtensionItemTopLevelItemNode extensionItem:
                            EmitExtensionBlock(extensionItem.ExtensionDeclaration);
                            break;
                        case NamespaceItemTopLevelItemNode nsItem:
                            // Skip file-scoped namespace declarations — they are already
                            // handled by the wrapping block namespace above. Only emit
                            // block namespaces (nested namespaces within the TLS file).
                            if(nsItem.NamespaceDeclaration is BlockNamespaceNamespaceDeclarationNode)
                            {
                                EmitNamespaceDeclaration(nsItem.NamespaceDeclaration);
                            }
                            else if(nsItem.NamespaceDeclaration is FileScopedNamespaceNamespaceDeclarationNode fileScopedNs
                                     && fileScopedNs.Body.Length > 0)
                            {
                                // Emit any type declarations inside the file-scoped namespace body.
                                foreach(var bodyItem in fileScopedNs.Body)
                                {
                                    switch(bodyItem)
                                    {
                                        case NamespaceTypeItemNamespaceBodyItemNode typeBodyItem:
                                            EmitTypeDeclaration(typeBodyItem.TypeDeclaration);
                                            break;
                                        case NamespaceExtensionItemNamespaceBodyItemNode extensionBodyItem:
                                            EmitExtensionBlock(extensionBodyItem.ExtensionDeclaration);
                                            break;
                                    }
                                }
                            }

                            break;
                    }
                }

                indentLevel--;
                AppendLine("}");
            }
            else
            {
                // No namespace — emit declarations at top level (handles explicit
                // block namespaces that carry their own name).
                foreach(var item in typeDecls)
                {
                    switch(item)
                    {
                        case TypeItemTopLevelItemNode typeItem:
                            EmitTypeDeclaration(typeItem.TypeDeclaration);
                            break;
                        case ExtensionItemTopLevelItemNode extensionItem:
                            EmitExtensionBlock(extensionItem.ExtensionDeclaration);
                            break;
                        case NamespaceItemTopLevelItemNode nsItem:
                            EmitNamespaceDeclaration(nsItem.NamespaceDeclaration);
                            break;
                    }
                }
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Emits a referenced project's TLS file by skipping the top-level statements
    /// and emitting only the type declarations. The statements are the entry point
    /// of the referenced project and are not relevant to the consuming project.
    /// </summary>
    private string EmitReferencedProjectTlsUnit(CompilationUnitNode cu)
    {
        // Analyze which void methods have their return values consumed at call sites.
        AnalyzeValueReturningVoidMethods(cu);

        // Collect type declarations (non-statement items).
        var typeDecls = cu.Items
            .Where(i => i is not StatementItemTopLevelItemNode)
            .ToList();

        if(typeDecls.Count == 0)
        {
            return "";
        }

        // Emit using declarations.
        var usings = new HashSet<string>(StringComparer.Ordinal);

        foreach(var usingDecl in cu.Usings)
        {
            var name = ExtractQualifiedName(usingDecl.Name);
            _ = usings.Add(name);
        }
        // Also collect using directives that appear after a file-scoped namespace.
        foreach(var item in cu.Items)
        {
            if(item is UsingItemTopLevelItemNode usingItem)
            {
                var name = ExtractQualifiedName(usingItem.UsingDeclaration.Name);
                _ = usings.Add(name);
            }
        }

        foreach(var ns in backendNamespaces)
        {
            _ = usings.Add(ns);
        }

        // Remove the Mist stdlib namespace — it has no C# equivalent.
        _ = usings.Remove("Standard");

        foreach(var ns in usings.OrderBy(n => n, StringComparer.Ordinal))
        {
            AppendLine($"using {ns};");
        }

        if(usings.Count > 0)
        {
            AppendLine();
        }

        // Determine the namespace for type declarations.
        string? inferredNamespace = null;

        foreach(var item in typeDecls)
        {
            if(item is NamespaceItemTopLevelItemNode nsItem
                && nsItem.NamespaceDeclaration is FileScopedNamespaceNamespaceDeclarationNode fileScopedNs)
            {
                inferredNamespace = ExtractQualifiedName(fileScopedNs.Name);
                break;
            }
        }

        if(inferredNamespace is null)
        {
            foreach(var item in typeDecls)
            {
                switch(item)
                {
                    case TypeItemTopLevelItemNode typeItem:
                        inferredNamespace = GetTypeContainingNamespace(typeItem.TypeDeclaration);
                        break;
                }

                if(inferredNamespace is not null)
                {
                    break;
                }
            }
        }

        // Emit type declarations with their namespace.
        if(inferredNamespace is not null)
        {
            currentNamespace = inferredNamespace;
            AppendLine($"namespace {inferredNamespace};");
            AppendLine();
        }

        foreach(var item in typeDecls)
        {
            switch(item)
            {
                case TypeItemTopLevelItemNode typeItem:
                    EmitTypeDeclaration(typeItem.TypeDeclaration);
                    break;
                case ExtensionItemTopLevelItemNode extensionItem:
                    EmitExtensionBlock(extensionItem.ExtensionDeclaration);
                    break;
                // Type aliases and newtypes now come through TypeItemTopLevelItemNode
                // → EmitTypeDeclaration → TypeAliasTypeTypeDeclarationNode / NewtypeTypeTypeDeclarationNode.
                case NamespaceItemTopLevelItemNode nsItem:
                    if(nsItem.NamespaceDeclaration is BlockNamespaceNamespaceDeclarationNode)
                    {
                        EmitNamespaceDeclaration(nsItem.NamespaceDeclaration);
                    }
                    else if(nsItem.NamespaceDeclaration is FileScopedNamespaceNamespaceDeclarationNode fileScopedNs
                             && fileScopedNs.Body.Length > 0)
                    {
                        foreach(var bodyItem in fileScopedNs.Body)
                        {
                            switch(bodyItem)
                            {
                                case NamespaceTypeItemNamespaceBodyItemNode typeBodyItem:
                                    EmitTypeDeclaration(typeBodyItem.TypeDeclaration);
                                    break;
                                case NamespaceExtensionItemNamespaceBodyItemNode extensionBodyItem:
                                    EmitExtensionBlock(extensionBodyItem.ExtensionDeclaration);
                                    break;
                                    // Type aliases and newtypes now come through NamespaceTypeItemNamespaceBodyItemNode
                                    // → EmitTypeDeclaration → TypeAliasTypeTypeDeclarationNode / NewtypeTypeTypeDeclarationNode.
                            }
                        }
                    }

                    break;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Emits a merged top-level block namespace by combining all blocks with the same FQN
    /// into a single C# namespace block. Omits the block if it has no emittable content.
    /// </summary>
    private void EmitMergedTopLevelBlockNamespace(string nsName, List<BlockNamespaceNamespaceDeclarationNode> blocks)
    {
        // Collect all body items from all blocks with this FQN.
        var mergedBodyItems = new List<NamespaceBodyItemNode>();
        foreach(var block in blocks)
        {
            foreach(var item in block.Body)
            {
                mergedBodyItems.Add(item);
            }
        }

        // Omit empty namespace blocks from output (Requirement 11.4).
        if(!HasEmittableContentInList(mergedBodyItems))
        {
            return;
        }

        AppendLine($"namespace {nsName}");
        AppendLine("{");
        indentLevel++;

        foreach(var item in mergedBodyItems)
        {
            switch(item)
            {
                case NamespaceTypeItemNamespaceBodyItemNode typeBodyItem:
                    EmitTypeDeclaration(typeBodyItem.TypeDeclaration);
                    break;
                case NamespaceExtensionItemNamespaceBodyItemNode extensionBodyItem:
                    EmitExtensionBlock(extensionBodyItem.ExtensionDeclaration);
                    break;
                // Type aliases and newtypes now come through NamespaceTypeItemNamespaceBodyItemNode
                // → EmitTypeDeclaration → TypeAliasTypeTypeDeclarationNode / NewtypeTypeTypeDeclarationNode.
                case NamespaceNamespaceItemNamespaceBodyItemNode:
                    // Handled below via merged emission.
                    break;
            }
        }

        // Emit nested namespaces with same-name merging.
        EmitMergedNestedNamespacesFromList(mergedBodyItems, nsName);

        indentLevel--;
        AppendLine("}");
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

        if(voidMethodNames.Count == 0)
        {
            return;
        }

        // Step 2: Walk the AST to find call sites where a void method's return value is consumed.
        FindValueReturningCallSites(cu, voidMethodNames);
    }

    /// <summary>
    /// Recursively collects the names of all methods with a void return type
    /// declared in the compilation unit.
    /// </summary>
    private static void CollectVoidMethodNames(CompilationUnitNode cu, HashSet<string> voidMethodNames)
    {
        foreach(var item in cu.Items)
        {
            switch(item)
            {
                case NamespaceItemTopLevelItemNode nsItem:
                    {
                        var bodyItems = nsItem.NamespaceDeclaration switch
                        {
                            FileScopedNamespaceNamespaceDeclarationNode fileScopedNs => fileScopedNs.Body,
                            BlockNamespaceNamespaceDeclarationNode blockNs => blockNs.Body,
                            _ => ImmutableArray<NamespaceBodyItemNode>.Empty,
                        };

                        foreach(var bodyItem in bodyItems)
                        {
                            if(bodyItem is NamespaceTypeItemNamespaceBodyItemNode typeBodyItem)
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
            BodyTypeTypeDeclarationNode body => body.TypeBody.Members,
            _ => ImmutableArray<MemberDeclarationNode>.Empty,
        };

        foreach(var member in members)
        {
            if(member is MethodMemberMemberDeclarationNode methodMember &&
                methodMember.Method.ReturnType.First is VoidSingleTypeRefSingleTypeRefNode)
            {
                _ = voidMethodNames.Add(methodMember.Method.Name.Lexeme);
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

        foreach(var item in cu.Items)
        {
            switch(item)
            {
                case NamespaceItemTopLevelItemNode nsItem:
                    {
                        var bodyItems = nsItem.NamespaceDeclaration switch
                        {
                            FileScopedNamespaceNamespaceDeclarationNode fileScopedNs => fileScopedNs.Body,
                            BlockNamespaceNamespaceDeclarationNode blockNs => blockNs.Body,
                            _ => ImmutableArray<NamespaceBodyItemNode>.Empty,
                        };
                        foreach(var bodyItem in bodyItems)
                        {
                            if(bodyItem is NamespaceTypeItemNamespaceBodyItemNode typeBodyItem)
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

        foreach(var typeDecl in types)
        {
            var members = typeDecl switch
            {
                BodyTypeTypeDeclarationNode body => body.TypeBody.Members,
                _ => ImmutableArray<MemberDeclarationNode>.Empty,
            };

            foreach(var member in members)
            {
                switch(member)
                {
                    case MethodMemberMemberDeclarationNode methodMember:
                        if(methodMember.Method.Body.GetBlock() is BlockNode mb)
                        {
                            ScanBlockForValueReturningCalls(mb, voidMethodNames);
                        }

                        break;
                    case ConstructorMemberMemberDeclarationNode ctorMember:
                        if(ctorMember.Constructor.Body.GetBlock() is BlockNode cb)
                        {
                            ScanBlockForValueReturningCalls(cb, voidMethodNames);
                        }

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
        foreach(var stmt in block.Statements)
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
        switch(stmt)
        {
            case ExpressionStatementStatementNode exprStmt:
                // A standalone expression statement does NOT consume the return value.
                // However, the expression itself might contain nested calls that do consume values.
                // For example: `otherMethod(voidMethod())` as a statement — the inner call is consumed.
                ScanExpressionForNestedValueReturningCalls(exprStmt.ExpressionStatement.Expression, voidMethodNames);
                break;

            case VariableDeclarationStatementStatementNode varDeclStmt:
                // The initializer expression is in a value-consuming context.
                switch(varDeclStmt.VariableDeclaration)
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
                if(returnStmt.ReturnStatement.Value is not null)
                {
                    ScanExpressionForValueReturningCalls(returnStmt.ReturnStatement.Value, voidMethodNames);
                }

                break;

            case IfStatementStatementNode ifStmt:
                ScanExpressionForValueReturningCalls(ifStmt.IfStatement.Condition, voidMethodNames);
                ScanBlockForValueReturningCalls(ifStmt.IfStatement.ThenBranch, voidMethodNames);
                if(ifStmt.IfStatement.ElseBranch is not null)
                {
                    ScanElseClauseForValueReturningCalls(ifStmt.IfStatement.ElseBranch, voidMethodNames);
                }

                break;

            case WhileStatementStatementNode whileStmt:
                ScanExpressionForValueReturningCalls(whileStmt.WhileStatement.Condition, voidMethodNames);
                ScanBlockForValueReturningCalls(whileStmt.WhileStatement.Body, voidMethodNames);
                break;

            case ForStatementStatementNode forStmt:
                if(forStmt.ForStatement.Condition is not null)
                {
                    ScanExpressionForValueReturningCalls(forStmt.ForStatement.Condition, voidMethodNames);
                }

                if(forStmt.ForStatement.Iterator is not null)
                {
                    ScanExpressionForValueReturningCalls(forStmt.ForStatement.Iterator, voidMethodNames);
                }

                ScanBlockForValueReturningCalls(forStmt.ForStatement.Body, voidMethodNames);
                break;

            case ForeachStatementStatementNode foreachStmt:
                ScanExpressionForValueReturningCalls(foreachStmt.ForeachStatement.GetCollection(), voidMethodNames);
                ScanBlockForValueReturningCalls(foreachStmt.ForeachStatement.GetBody(), voidMethodNames);
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

            case BlockStatementStatementNode blockStmt:
                ScanBlockForValueReturningCalls(blockStmt.BlockStatement, voidMethodNames);
                break;
        }
    }

    private void ScanElseClauseForValueReturningCalls(ElseClauseNode elseClause, HashSet<string> voidMethodNames)
    {
        switch(elseClause)
        {
            case ElseIfElseClauseNode elseIf:
                ScanExpressionForValueReturningCalls(elseIf.ElseIf.Condition, voidMethodNames);
                ScanBlockForValueReturningCalls(elseIf.ElseIf.ThenBranch, voidMethodNames);
                if(elseIf.ElseIf.ElseBranch is not null)
                {
                    ScanElseClauseForValueReturningCalls(elseIf.ElseIf.ElseBranch, voidMethodNames);
                }

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
        // Lambda-form expressions have their own body scope; their value-returning
        // call handling is deferred to lambda emission (task 13.1).
        if(expr.AsAssignmentOrNull() is { } assignment)
        {
            ScanAssignmentForValueReturningCalls(assignment, voidMethodNames, isValueContext: true);
        }
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
        // Lambda-form expressions are handled by lambda emission (task 13.1).
        if(expr.AsAssignmentOrNull() is { } assignment)
        {
            ScanAssignmentForValueReturningCalls(assignment, voidMethodNames, isValueContext: false);
        }
    }

    private void ScanAssignmentForValueReturningCalls(AssignmentNode assignment, HashSet<string> voidMethodNames, bool isValueContext)
    {
        // The target of an assignment is not in a value context for the purpose of the call itself,
        // but if there's an assignment value, the RHS is in a value context.
        ScanLogicalOrForValueReturningCalls(assignment.Target.Left, voidMethodNames, isValueContext);

        // Scan the right operands of ?? (VoidCoalescing) — they are in a value context
        // when the overall expression is in a value context.
        foreach(var rightOperand in assignment.Target.LogicalOrs)
        {
            ScanLogicalOrForValueReturningCalls(rightOperand, voidMethodNames, isValueContext);
        }

        if(assignment.Value is not null)
        {
            // The RHS of an assignment is always in a value context. It is now a full
            // ExpressionNode (widened to reach lambdas), so scan via the expression entry.
            ScanExpressionForValueReturningCalls(assignment.Value, voidMethodNames);
        }
    }

    private void ScanLogicalOrForValueReturningCalls(LogicalOrNode node, HashSet<string> voidMethodNames, bool isValueContext)
    {
        ScanLogicalAndForValueReturningCalls(node.Left, voidMethodNames, isValueContext);
        if(node.Right is not null && !ReferenceEquals(node.Right, node.Left))
        {
            ScanLogicalAndForValueReturningCalls(node.Right, voidMethodNames, isValueContext);
        }
    }

    private void ScanLogicalAndForValueReturningCalls(LogicalAndNode node, HashSet<string> voidMethodNames, bool isValueContext)
    {
        ScanEqualityForValueReturningCalls(node.Left, voidMethodNames, isValueContext);
        if(node.Right is not null && !ReferenceEquals(node.Right, node.Left))
        {
            ScanEqualityForValueReturningCalls(node.Right, voidMethodNames, isValueContext);
        }
    }

    private void ScanEqualityForValueReturningCalls(EqualityNode node, HashSet<string> voidMethodNames, bool isValueContext)
    {
        ScanRelationalForValueReturningCalls(node.Left, voidMethodNames, isValueContext);
        if(node.Right is not null && !ReferenceEquals(node.Right, node.Left))
        {
            ScanRelationalForValueReturningCalls(node.Right, voidMethodNames, isValueContext);
        }
    }

    private void ScanRelationalForValueReturningCalls(RelationalNode node, HashSet<string> voidMethodNames, bool isValueContext)
    {
        switch(node)
        {
            case RelationalComparisonRelationalNode cmp:
                ScanAdditiveForValueReturningCalls(cmp.Left, voidMethodNames, isValueContext);
                if(cmp.Right is not null && !ReferenceEquals(cmp.Right, cmp.Left))
                {
                    ScanAdditiveForValueReturningCalls(cmp.Right, voidMethodNames, isValueContext);
                }

                break;

            case IsExpressionRelationalNode isExpr:
                // Only scan the left additive — patterns contain no method calls
                ScanAdditiveForValueReturningCalls(isExpr.Left, voidMethodNames, isValueContext);
                break;

            case AsExpressionRelationalNode asExpr:
                // Only scan the left additive — target type contains no method calls
                ScanAdditiveForValueReturningCalls(asExpr.Left, voidMethodNames, isValueContext);
                break;
        }
    }

    private void ScanAdditiveForValueReturningCalls(AdditiveNode node, HashSet<string> voidMethodNames, bool isValueContext)
    {
        ScanMultiplicativeForValueReturningCalls(node.Left, voidMethodNames, isValueContext);
        if(node.Right is not null && !ReferenceEquals(node.Right, node.Left))
        {
            ScanMultiplicativeForValueReturningCalls(node.Right, voidMethodNames, isValueContext);
        }
    }

    private void ScanMultiplicativeForValueReturningCalls(MultiplicativeNode node, HashSet<string> voidMethodNames, bool isValueContext)
    {
        ScanUnaryForValueReturningCalls(node.Left, voidMethodNames, isValueContext);
        if(node.Right is not null && !ReferenceEquals(node.Right, node.Left))
        {
            ScanUnaryForValueReturningCalls(node.Right, voidMethodNames, isValueContext);
        }
    }

    private void ScanUnaryForValueReturningCalls(UnaryNode unary, HashSet<string> voidMethodNames, bool isValueContext)
    {
        switch(unary)
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

        for(var i = 0; i < suffixes.Length; i++)
        {
            var suffix = suffixes[i];
            var isLastSuffix = i == suffixes.Length - 1;

            switch(suffix)
            {
                case MethodCallSuffixPostfixSuffixNode methodCall:
                    {
                        var methodName = methodCall.Name.Lexeme;

                        // If this is a call to a void method and we're in a value context,
                        // mark it as value-returning.
                        if(isValueContext && isLastSuffix && voidMethodNames.Contains(methodName))
                        {
                            _ = valueReturningVoidMethods.Add(methodName);
                        }

                        // Scan arguments — they are always in a value context.
                        if(methodCall.Arguments is not null)
                        {
                            ScanArgumentListForValueReturningCalls(methodCall.Arguments, voidMethodNames);
                        }

                        break;
                    }
                case InvocationSuffixPostfixSuffixNode invocation:
                    {
                        // Direct invocation: the target identifier is the method name.
                        if(isValueContext && isLastSuffix && postfix.Target is IdentifierExpressionPrimaryNode ident)
                        {
                            var methodName = ident.Name.Lexeme;
                            if(voidMethodNames.Contains(methodName))
                            {
                                _ = valueReturningVoidMethods.Add(methodName);
                            }
                        }

                        // Scan arguments — they are always in a value context.
                        if(invocation.Arguments is not null)
                        {
                            ScanArgumentListForValueReturningCalls(invocation.Arguments, voidMethodNames);
                        }

                        break;
                    }
                case OptionalMethodCallSuffixPostfixSuffixNode optionalMethodCall:
                    {
                        // Optional chaining method call: scan arguments for value-returning calls.
                        if(optionalMethodCall.Arguments is not null)
                        {
                            ScanArgumentListForValueReturningCalls(optionalMethodCall.Arguments, voidMethodNames);
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
        // Scan all argument expressions.
        foreach(var expr in argList.GetAllExpressions())
        {
            ScanExpressionForValueReturningCalls(expr, voidMethodNames);
        }
    }

    /// <summary>
    /// Looks up the ContainingNamespace for a type declaration by querying the
    /// semantic model for the symbol declared by the inner declaration node
    /// (ClassDeclaration, StructDeclaration, or AbstractDeclaration).
    /// </summary>
    private string? GetTypeContainingNamespace(TypeDeclarationNode typeDecl)
    {
        Storm.Runtime.SyntaxNode? innerNode = typeDecl switch
        {
            BodyTypeTypeDeclarationNode body => body.TypeBody,
            EnumTypeTypeDeclarationNode enumType => enumType.EnumDeclaration,
            NewtypeTypeTypeDeclarationNode newtypeType => newtypeType.NewtypeDeclaration,
            _ => null,
        };

        if(innerNode is null)
        {
            return null;
        }

        var symbol = model.GetDeclaredSymbol(innerNode);
        if(symbol?.DeclaredType is null)
        {
            return null;
        }

        return symbol.DeclaredType switch
        {
            ClassType ct => ct.ContainingNamespace,
            StructType st => st.ContainingNamespace,
            AbstractType it => it.ContainingNamespace,
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
            AbstractType it => it.ContainingNamespace,
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
        if(symbol?.ContainingScope is not Scope containingScope)
        {
            return null;
        }

        // If the containing scope's parent is not the global scope, the newtype
        // is not in a namespace (or is nested in a way we don't handle here).
        if(containingScope.Parent is null || !ReferenceEquals(containingScope.Parent, model.GlobalScope))
        {
            return null;
        }

        // Look at sibling symbols in the same scope to find one with a ContainingNamespace.
        foreach(var sibling in containingScope.Members)
        {
            var ns = sibling.DeclaredType switch
            {
                ClassType ct => ct.ContainingNamespace,
                StructType st => st.ContainingNamespace,
                AbstractType it => it.ContainingNamespace,
                EnumType et => et.ContainingNamespace,
                _ => null,
            };
            if(ns is not null)
            {
                return ns;
            }
        }

        // Fallback: look for a namespace symbol in the global scope whose name
        // matches the scope structure.
        foreach(var globalMember in model.GlobalScope.Members)
        {
            if(globalMember.Kind == SymbolKind.Namespace)
            {
                // Check if any symbol in this namespace scope is in our containing scope.
                foreach(var nsChild in model.AllSymbols)
                {
                    if(ReferenceEquals(nsChild.ContainingScope, containingScope)
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
        switch(ns)
        {
            case FileScopedNamespaceNamespaceDeclarationNode fileScopedNs:
                {
                    var name = ExtractQualifiedName(fileScopedNs.Name);
                    currentNamespace = name;
                    AppendLine($"namespace {name};");
                    AppendLine();

                    foreach(var bodyItem in fileScopedNs.Body)
                    {
                        switch(bodyItem)
                        {
                            case NamespaceTypeItemNamespaceBodyItemNode typeBodyItem:
                                EmitTypeDeclaration(typeBodyItem.TypeDeclaration);
                                break;
                            case NamespaceExtensionItemNamespaceBodyItemNode extensionBodyItem:
                                EmitExtensionBlock(extensionBodyItem.ExtensionDeclaration);
                                break;
                            case NamespaceNamespaceItemNamespaceBodyItemNode:
                                // Handled below via merged emission.
                                break;
                        }
                    }

                    // Emit nested namespaces with same-name merging.
                    EmitMergedNestedNamespaces(fileScopedNs.Body, name);
                    break;
                }
            case BlockNamespaceNamespaceDeclarationNode blockNs:
                {
                    var name = ExtractQualifiedName(blockNs.Name);

                    // Check if this block namespace has any emittable content (including nested namespaces).
                    if(!HasEmittableContent(blockNs.Body))
                    {
                        break;
                    }

                    AppendLine($"namespace {name}");
                    AppendLine("{");
                    indentLevel++;

                    foreach(var bodyItem in blockNs.Body)
                    {
                        switch(bodyItem)
                        {
                            case NamespaceTypeItemNamespaceBodyItemNode typeBodyItem:
                                EmitTypeDeclaration(typeBodyItem.TypeDeclaration);
                                break;
                            case NamespaceExtensionItemNamespaceBodyItemNode extensionBodyItem:
                                EmitExtensionBlock(extensionBodyItem.ExtensionDeclaration);
                                break;
                            case NamespaceNamespaceItemNamespaceBodyItemNode:
                                // Handled below via merged emission.
                                break;
                        }
                    }

                    // Emit nested namespaces with same-name merging.
                    EmitMergedNestedNamespaces(blockNs.Body, name);

                    indentLevel--;
                    AppendLine("}");
                    break;
                }
        }
    }

    /// <summary>
    /// Emits nested namespace declarations from a namespace body, merging blocks with
    /// identical fully qualified names into a single C# namespace block. Empty namespace
    /// blocks (those with no type declarations, extensions, or newtypes) are omitted.
    /// </summary>
    private void EmitMergedNestedNamespaces(ImmutableArray<NamespaceBodyItemNode> bodyItems, string parentFqn)
    {
        // Collect all nested namespace items grouped by their FQN.
        var groupedNamespaces = new Dictionary<string, List<NamespaceDeclarationNode>>(StringComparer.Ordinal);
        var insertionOrder = new List<string>();

        foreach(var bodyItem in bodyItems)
        {
            if(bodyItem is not NamespaceNamespaceItemNamespaceBodyItemNode nsItem)
            {
                continue;
            }

            var nestedNsDecl = nsItem.NamespaceDeclaration;
            if(nestedNsDecl is not BlockNamespaceNamespaceDeclarationNode nestedBlockNs)
            {
                continue;
            }

            var declaredName = ExtractQualifiedName(nestedBlockNs.Name);
            var fqn = parentFqn + "." + declaredName;

            if(!groupedNamespaces.TryGetValue(fqn, out var list))
            {
                list = new List<NamespaceDeclarationNode>();
                groupedNamespaces[fqn] = list;
                insertionOrder.Add(fqn);
            }

            list.Add(nestedNsDecl);
        }

        // Emit each unique FQN as a single merged namespace block.
        foreach(var fqn in insertionOrder)
        {
            var nsDecls = groupedNamespaces[fqn];

            // Collect all body items from all blocks with this FQN.
            var mergedBodyItems = new List<NamespaceBodyItemNode>();
            foreach(var nsDecl in nsDecls)
            {
                if(nsDecl is BlockNamespaceNamespaceDeclarationNode blockNsDecl)
                {
                    foreach(var item in blockNsDecl.Body)
                    {
                        mergedBodyItems.Add(item);
                    }
                }
            }

            // Omit empty namespace blocks from output.
            if(!HasEmittableContentInList(mergedBodyItems))
            {
                continue;
            }

            AppendLine($"namespace {fqn}");
            AppendLine("{");
            indentLevel++;

            foreach(var item in mergedBodyItems)
            {
                switch(item)
                {
                    case NamespaceTypeItemNamespaceBodyItemNode typeBodyItem:
                        EmitTypeDeclaration(typeBodyItem.TypeDeclaration);
                        break;
                    case NamespaceExtensionItemNamespaceBodyItemNode extensionBodyItem:
                        EmitExtensionBlock(extensionBodyItem.ExtensionDeclaration);
                        break;
                    case NamespaceNamespaceItemNamespaceBodyItemNode:
                        // Handled below via recursive merged emission.
                        break;
                }
            }

            // Recursively emit nested namespaces within the merged body.
            EmitMergedNestedNamespacesFromList(mergedBodyItems, fqn);

            indentLevel--;
            AppendLine("}");
        }
    }

    /// <summary>
    /// Emits merged nested namespaces from a list of body items (used for recursion
    /// when merging already-collected body items from multiple same-name blocks).
    /// </summary>
    private void EmitMergedNestedNamespacesFromList(List<NamespaceBodyItemNode> bodyItems, string parentFqn)
    {
        var groupedNamespaces = new Dictionary<string, List<NamespaceDeclarationNode>>(StringComparer.Ordinal);
        var insertionOrder = new List<string>();

        foreach(var bodyItem in bodyItems)
        {
            if(bodyItem is not NamespaceNamespaceItemNamespaceBodyItemNode nsItem)
            {
                continue;
            }

            var nestedNsDecl = nsItem.NamespaceDeclaration;
            if(nestedNsDecl is not BlockNamespaceNamespaceDeclarationNode nestedBlockNs)
            {
                continue;
            }

            var declaredName = ExtractQualifiedName(nestedBlockNs.Name);
            var fqn = parentFqn + "." + declaredName;

            if(!groupedNamespaces.TryGetValue(fqn, out var list))
            {
                list = new List<NamespaceDeclarationNode>();
                groupedNamespaces[fqn] = list;
                insertionOrder.Add(fqn);
            }

            list.Add(nestedNsDecl);
        }

        foreach(var fqn in insertionOrder)
        {
            var nsDecls = groupedNamespaces[fqn];

            var mergedBodyItems = new List<NamespaceBodyItemNode>();
            foreach(var nsDecl in nsDecls)
            {
                if(nsDecl is BlockNamespaceNamespaceDeclarationNode blockNsDecl)
                {
                    foreach(var item in blockNsDecl.Body)
                    {
                        mergedBodyItems.Add(item);
                    }
                }
            }

            if(!HasEmittableContentInList(mergedBodyItems))
            {
                continue;
            }

            AppendLine($"namespace {fqn}");
            AppendLine("{");
            indentLevel++;

            foreach(var item in mergedBodyItems)
            {
                switch(item)
                {
                    case NamespaceTypeItemNamespaceBodyItemNode typeBodyItem:
                        EmitTypeDeclaration(typeBodyItem.TypeDeclaration);
                        break;
                    case NamespaceExtensionItemNamespaceBodyItemNode extensionBodyItem:
                        EmitExtensionBlock(extensionBodyItem.ExtensionDeclaration);
                        break;
                    case NamespaceNamespaceItemNamespaceBodyItemNode:
                        break;
                }
            }

            EmitMergedNestedNamespacesFromList(mergedBodyItems, fqn);

            indentLevel--;
            AppendLine("}");
        }
    }

    /// <summary>
    /// Returns true if the namespace body contains any emittable content
    /// (type declarations, extension blocks, or newtype declarations, or nested namespaces with content).
    /// </summary>
    private static bool HasEmittableContent(ImmutableArray<NamespaceBodyItemNode> bodyItems)
    {
        foreach(var item in bodyItems)
        {
            switch(item)
            {
                case NamespaceTypeItemNamespaceBodyItemNode:
                case NamespaceExtensionItemNamespaceBodyItemNode:
                    return true;
                case NamespaceNamespaceItemNamespaceBodyItemNode nsItem:
                    if(nsItem.NamespaceDeclaration is BlockNamespaceNamespaceDeclarationNode nestedBlock
                        && HasEmittableContent(nestedBlock.Body))
                    {
                        return true;
                    }

                    break;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns true if the list of namespace body items contains any emittable content.
    /// </summary>
    private static bool HasEmittableContentInList(List<NamespaceBodyItemNode> bodyItems)
    {
        foreach(var item in bodyItems)
        {
            switch(item)
            {
                case NamespaceTypeItemNamespaceBodyItemNode:
                case NamespaceExtensionItemNamespaceBodyItemNode:
                    return true;
                case NamespaceNamespaceItemNamespaceBodyItemNode nsItem:
                    if(nsItem.NamespaceDeclaration is BlockNamespaceNamespaceDeclarationNode nestedBlock
                        && HasEmittableContent(nestedBlock.Body))
                    {
                        return true;
                    }

                    break;
            }
        }

        return false;
    }

    private void EmitTypeDeclaration(TypeDeclarationNode typeDecl)
    {
        switch(typeDecl)
        {
            case BodyTypeTypeDeclarationNode body:
                switch(body.TypeBody.Keyword)
                {
                    case ClassKeywordTypeKeywordNode:
                        EmitClassDeclaration(body.TypeBody);
                        break;
                    case StructKeywordTypeKeywordNode:
                        EmitStructDeclaration(body.TypeBody);
                        break;
                    case AbstractKeywordTypeKeywordNode:
                        EmitAbstractDeclaration(body.TypeBody);
                        break;
                }

                break;
            case EnumTypeTypeDeclarationNode enumType:
                EmitEnumDeclaration(enumType.EnumDeclaration);
                break;
            case NewtypeTypeTypeDeclarationNode newtypeType:
                EmitNewtypeDeclaration(newtypeType.NewtypeDeclaration);
                break;
            case TypeAliasTypeTypeDeclarationNode:
                // Type aliases are erased — no C# declaration is emitted.
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
        var symbol = model.GetDeclaredSymbol(newtypeDecl);
        if(symbol?.DeclaredType is not NewtypeType newtypeType)
        {
            return;
        }

        var sourceType = newtypeType.SourceType;
        var newtypeName = newtypeType.Name;

        switch(sourceType)
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
        indentLevel++;

        // Value field
        AppendLine($"public readonly {csharpKeyword} Value;");
        AppendLine();

        // Constructor
        AppendLine($"public {newtypeName}({csharpKeyword} value)");
        AppendLine("{");
        indentLevel++;
        AppendLine("Value = value;");
        indentLevel--;
        AppendLine("}");
        AppendLine();

        // Explicit conversion: primitive → newtype
        AppendLine($"public static explicit operator {newtypeName}({csharpKeyword} value) => new {newtypeName}(value);");
        // Explicit conversion: newtype → primitive
        AppendLine($"public static explicit operator {csharpKeyword}({newtypeName} id) => id.Value;");

        indentLevel--;
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
        indentLevel++;

        // Value field
        AppendLine($"public readonly {csharpKeyword} Value;");
        AppendLine();

        // Constructor
        AppendLine($"public {newtypeName}({csharpKeyword} value)");
        AppendLine("{");
        indentLevel++;
        AppendLine("Value = value;");
        indentLevel--;
        AppendLine("}");
        AppendLine();

        // Explicit conversion: class → newtype
        AppendLine($"public static explicit operator {newtypeName}({csharpKeyword} value) => new {newtypeName}(value);");
        // Explicit conversion: newtype → class
        AppendLine($"public static explicit operator {csharpKeyword}({newtypeName} id) => id.Value;");

        indentLevel--;
        AppendLine("}");
    }

    /// <summary>
    /// Strategy 1: Emits a C# class for a newtype whose source is a class.
    /// Re-emits all members from the source type's declaration under the newtype name.
    /// </summary>
    private void EmitClassNewtype(string newtypeName, IType sourceType)
    {
        // Find the source type's declaring symbol and AST node.
        var sourceSymbol = model.AllSymbols.FirstOrDefault(s =>
            s.DeclaredType is not null && ReferenceEquals(s.DeclaredType, sourceType));
        var sourceNode = sourceSymbol is not null ? model.GetDeclaringNode(sourceSymbol) : null;

        AppendLine($"public class {newtypeName}");
        AppendLine("{");
        indentLevel++;

        if(sourceNode is TypeBodyDeclarationNode { Keyword: ClassKeywordTypeKeywordNode } typeBody)
        {
            for(var i = 0; i < typeBody.Members.Length; i++)
            {
                if(i > 0)
                {
                    AppendLine();
                }

                EmitNewtypeMemberDeclaration(typeBody.Members[i], newtypeName);
            }
        }

        indentLevel--;
        AppendLine("}");
    }

    /// <summary>
    /// Strategy 2: Emits a C# struct for a newtype whose source is a struct.
    /// Re-emits all members from the source type's declaration under the newtype name.
    /// </summary>
    private void EmitStructNewtype(string newtypeName, IType sourceType)
    {
        // Find the source type's declaring symbol and AST node.
        var sourceSymbol = model.AllSymbols.FirstOrDefault(s =>
            s.DeclaredType is not null && ReferenceEquals(s.DeclaredType, sourceType));
        var sourceNode = sourceSymbol is not null ? model.GetDeclaringNode(sourceSymbol) : null;

        AppendLine($"public struct {newtypeName}");
        AppendLine("{");
        indentLevel++;

        // Track that we're inside a struct so method emission can
        // emit the C# `readonly` modifier on struct methods.
        var previousInsideStruct = insideStruct;
        insideStruct = true;

        if(sourceNode is TypeBodyDeclarationNode { Keyword: StructKeywordTypeKeywordNode } typeBody)
        {
            for(var i = 0; i < typeBody.Members.Length; i++)
            {
                if(i > 0)
                {
                    AppendLine();
                }

                EmitNewtypeMemberDeclaration(typeBody.Members[i], newtypeName);
            }
        }

        insideStruct = previousInsideStruct;
        indentLevel--;
        AppendLine("}");
    }

    /// <summary>
    /// Emits a single member declaration for a newtype, renaming constructors to the newtype name.
    /// Delegates to the standard member emission methods for all other member kinds.
    /// </summary>
    private void EmitNewtypeMemberDeclaration(MemberDeclarationNode memberDecl, string newtypeName)
    {
        // For constructors, we need to emit with the newtype name instead of the source type name.
        if(memberDecl is ConstructorMemberMemberDeclarationNode ctorMember)
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
        EmitAttributeLists(ctorDecl.Attributes);
        var modifiers = EmitModifiers(ctorDecl.Modifiers);
        var parameters = EmitParameterList(ctorDecl.Parameters);

        var chain = "";
        if(ctorDecl.Chain is ThisChainConstructorChainNode thisChain)
        {
            chain = " : this(" + EmitArgumentList(thisChain.Arguments) + ")";
        }
        else if(ctorDecl.Chain is BaseChainConstructorChainNode baseChain)
        {
            chain = " : base(" + EmitArgumentList(baseChain.Arguments) + ")";
        }

        AppendLine($"{modifiers}{newtypeName}({parameters}){chain}");
        if(ctorDecl.Body.GetBlock() is BlockNode block)
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
        var symbol = model.GetDeclaredSymbol(enumDecl);
        if(symbol?.DeclaredType is not EnumType enumType)
        {
            return;
        }

        var modifiers = EmitModifiers(enumDecl.Modifiers);
        // Mist enums without an explicit access modifier default to public in emitted C#.
        if(!modifiers.Contains("public") && !modifiers.Contains("private"))
        {
            modifiers = "public " + modifiers;
        }

        var name = enumDecl.Name.Lexeme;

        // Determine the C# type name for the underlying type.
        // For union types, use the union helper struct name.
        var underlyingTypeName = enumType.UnderlyingType is UnionType ut
            ? UnionHelperGenerator.GetHelperTypeName(ut)
            : CSharpTypeMap.GetTypeName(enumType.UnderlyingType);

        // Emit user-declared attribute annotations from source.
        EmitAttributeLists(enumDecl.Attributes);

        AppendLine($"{modifiers}record class {name}({underlyingTypeName} Value)");
        AppendLine("{");
        indentLevel++;

        // Emit public static readonly fields for each enum member.
        var memberNodes = new List<EnumMemberNode>();
        if(enumDecl.Members is not null)
        {
            memberNodes.Add(enumDecl.Members.EnumMember);
            foreach(var member in enumDecl.Members.EnumMembers)
            {
                memberNodes.Add(member);
            }
        }

        for(var i = 0; i < memberNodes.Count; i++)
        {
            var memberNode = memberNodes[i];
            var memberName = memberNode.Name.Lexeme;
            string valueExpr;

            if(memberNode.Value is not null)
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

        // Emit explicit conversion operator from the underlying type to the enum.
        // Returns null when no variant matches the value, which Mist maps to T | void.
        // Uses Equals for value comparison (works for primitives, strings, and structs
        // that implement Equals correctly).
        AppendLine();
        AppendLine($"public static explicit operator {name}?({underlyingTypeName} value)");
        AppendLine("{");
        indentLevel++;
        for(var i = 0; i < memberNodes.Count; i++)
        {
            var memberName = memberNodes[i].Name.Lexeme;
            var prefix = i == 0 ? "if" : "else if";
            AppendLine($"{prefix} (Equals(value, {memberName}.Value)) return {memberName};");
        }

        AppendLine("return null;");
        indentLevel--;
        AppendLine("}");

        // Emit ToString() override that returns the variant name.
        // This makes string interpolation of enum values produce the variant name
        // (e.g., "North") rather than the default record ToString format.
        // Uses ReferenceEquals to compare against static readonly fields since
        // record structural equality may not work correctly for all underlying types.
        AppendLine();
        AppendLine($"public override string ToString()");
        AppendLine("{");
        indentLevel++;
        for(var i = 0; i < memberNodes.Count; i++)
        {
            var memberName = memberNodes[i].Name.Lexeme;
            var prefix = i == 0 ? "if" : "else if";
            AppendLine($"{prefix} (ReferenceEquals(this, {memberName})) return \"{memberName}\";");
        }

        AppendLine($"return Value.ToString();");
        indentLevel--;
        AppendLine("}");

        // Emit __CreateDefault() factory method for enums with a designated default variant.
        if(enumType.Defaultability is DefaultabilityStatus.Explicit && enumType.DefaultMember is not null)
        {
            AppendLine();
            AppendLine($"public static {name} __CreateDefault() => {enumType.DefaultMember.Name};");
        }

        indentLevel--;
        AppendLine("}");
    }

    /// <summary>
    /// Emits the value expression for a name-only enum member.
    /// For byte underlying types, emits <c>(byte)N</c>. For other integral types,
    /// Emits the value expression for a name-only enum member.
    /// For byte underlying types, emits <c>(byte)N</c>. For string underlying types,
    /// emits the string literal. For other integral types, emits the numeric literal directly.
    /// </summary>
    private static string EmitEnumMemberValue(object? resolvedValue, string underlyingTypeName)
    {
        if(resolvedValue is string strVal)
        {
            return $"\"{strVal}\"";
        }

        if(resolvedValue is long longVal)
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
        var fileName = SanitizeForIdentifier(Path.GetFileNameWithoutExtension(sourceText.Path));
        var lineNumber = GetLineNumber(extDecl);
        var className = $"{receiverTypeName}Extensions_{fileName}_L{lineNumber}";

        // Determine the receiver C# type string.
        var receiverCSharpType = EmitTypeReference(extDecl.ReceiverType);
        var receiverName = extDecl.ReceiverName.Lexeme;

        // Collect type parameters from the extension block (for generic extensions).
        var blockTypeParams = EmitTypeParameterList(extDecl.TypeParams);

        AppendLine($"public static class {className}");
        AppendLine("{");
        indentLevel++;

        for(var i = 0; i < extDecl.Members.Length; i++)
        {
            if(i > 0)
            {
                AppendLine();
            }

            EmitExtensionMember(extDecl.Members[i], receiverCSharpType, receiverName, blockTypeParams);
        }

        indentLevel--;
        AppendLine("}");
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
        switch(member)
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

        // Emit ref / ref readonly prefix for ref-returning methods.
        var extMethodSymbol = model.GetDeclaredSymbol(methodDecl) as Symbol;
        var refReturnPrefix = extMethodSymbol?.ReturnRefKind switch
        {
            RefKind.Ref => "ref ",
            RefKind.RefReadonly => "ref readonly ",
            _ => ""
        };

        // Build the full parameter list.
        string fullParams;
        if(isStatic)
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

        AppendLine($"public static {refReturnPrefix}{returnType} {name}{combinedTypeParams}({fullParams})");

        // Set the return type context for covariant unknown widening.
        var previousReturnType = currentMethodReturnType;
        currentMethodReturnType = ResolveMethodReturnType(methodDecl);

        // Track the enclosing method symbol for auto-unwrap emission.
        var previousMethodSymbol = currentMethodSymbol;
        currentMethodSymbol = extMethodSymbol;

        if(methodDecl.Body.GetBlock() is BlockNode block)
        {
            var withClauses = extMethodSymbol?.WithClauses;
            EmitMethodBlockWithRefinementGuards(block, false, methodDecl.Parameters, withClauses);
        }
        else
        {
            AppendLine(";");
        }

        currentMethodReturnType = previousReturnType;
        currentMethodSymbol = previousMethodSymbol;
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
        switch(propertyDecl)
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
                    indentLevel++;
                    AppendLine($"return {body};");
                    indentLevel--;
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
                    indentLevel++;

                    // Emit the get accessor body statements.
                    foreach(var accessor in blockProp.Accessors)
                    {
                        if(accessor is BlockGetAccessorAccessorDeclarationNode blockGet)
                        {
                            foreach(var stmt in blockGet.Body.Statements)
                            {
                                EmitStatement(stmt);
                            }
                        }
                    }

                    indentLevel--;
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
        return typeRef.First switch
        {
            UnknownSingleTypeRefSingleTypeRefNode => "Unknown",
            NamedSingleTypeRefSingleTypeRefNode namedRef => ExtractSimpleTypeName(namedRef),
            PrimitiveSingleTypeRefSingleTypeRefNode primitiveRef => GetPrimitiveTypeName(primitiveRef),
            _ => "Unknown",
        };
    }

    /// <summary>
    /// Extracts the simple type name from a named type reference, including
    /// generic type arguments in a flattened form (e.g., <c>List&lt;T&gt;</c> → <c>ListOfT</c>).
    /// </summary>
    private string ExtractSimpleTypeName(NamedSingleTypeRefSingleTypeRefNode namedRef)
    {
        // Use the qualified name extraction which handles dotted names via source span.
        var fullName = ExtractQualifiedName(namedRef.Name);
        // Take only the last segment for the simple name (e.g., "System.Collections.List" → "List").
        var dotIndex = fullName.LastIndexOf('.');
        var name = dotIndex >= 0 ? fullName[(dotIndex + 1)..] : fullName;

        if(namedRef.TypeArgs is not null)
        {
            // Flatten type args into the name: List<T> → ListOfT
            var typeArgNames = new List<string>
            {
                GetTypeArgNameFromTypeArgument(namedRef.TypeArgs.TypeArgument)
            };
            foreach(var arg in namedRef.TypeArgs.TypeArguments)
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
        return typeRef.First switch
        {
            NamedSingleTypeRefSingleTypeRefNode named => ExtractSimpleTypeName(named),
            PrimitiveSingleTypeRefSingleTypeRefNode prim => GetPrimitiveTypeName(prim),
            UnknownSingleTypeRefSingleTypeRefNode => "Unknown",
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
    private static string GetPrimitiveTypeName(PrimitiveSingleTypeRefSingleTypeRefNode primitiveRef)
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
        if(span.Length > 0)
        {
            var linePos = sourceText.ToLinePositionSpan(new Common.Source.SourceSpan(span.Start, span.Length));
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
        foreach(var c in name)
        {
            _ = sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
        }

        return sb.ToString();
    }

    /// <summary>
    /// Combines block-level and method-level type parameter lists.
    /// If both are present, merges them into a single list.
    /// </summary>
    private static string CombineTypeParameters(string blockTypeParams, string methodTypeParams)
    {
        if(blockTypeParams.Length == 0)
        {
            return methodTypeParams;
        }

        if(methodTypeParams.Length == 0)
        {
            return blockTypeParams;
        }

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
        // Ambient extensions use their static class name directly.
        if(extBlock.IsAmbient)
        {
            return extBlock.AmbientStaticClassName ?? "AmbientExtensions";
        }

        var declaringNode = extBlock.DeclaringNode!;
        var receiverTypeName = GetReceiverTypeNameFromIType(extBlock.ReceiverType);

        // Find the source file containing this extension block.
        var fileName = "Unknown";
        var lineNumber = 1;

        for(var i = 0; i < model.Units.Count; i++)
        {
            var unit = model.Units[i];
            if(i < model.Asts.Length && model.Asts[i] is CompilationUnitNode cu)
            {
                // Check if this compilation unit contains the declaring node.
                if(ContainsNode(cu, declaringNode))
                {
                    fileName = SanitizeForIdentifier(Path.GetFileNameWithoutExtension(unit.Source.Path));
                    var span = declaringNode.Span;
                    if(span.Length > 0)
                    {
                        var linePos = unit.Source.ToLinePositionSpan(
                            new Common.Source.SourceSpan(span.Start, span.Length));
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
            AbstractType it => it.DisplayName,
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
        foreach(var item in cu.Items)
        {
            switch(item)
            {
                case ExtensionItemTopLevelItemNode extItem:
                    if(ReferenceEquals(extItem.ExtensionDeclaration, targetNode))
                    {
                        return true;
                    }

                    break;
                case NamespaceItemTopLevelItemNode nsItem:
                    {
                        var bodyItems = nsItem.NamespaceDeclaration switch
                        {
                            FileScopedNamespaceNamespaceDeclarationNode fileScopedNs => fileScopedNs.Body,
                            BlockNamespaceNamespaceDeclarationNode blockNs => blockNs.Body,
                            _ => ImmutableArray<NamespaceBodyItemNode>.Empty,
                        };
                        foreach(var bodyItem in bodyItems)
                        {
                            if(bodyItem is NamespaceExtensionItemNamespaceBodyItemNode extBodyItem &&
                                ReferenceEquals(extBodyItem.ExtensionDeclaration, targetNode))
                            {
                                return true;
                            }
                        }

                        break;
                    }
            }
        }

        return false;
    }

    private void EmitClassDeclaration(TypeBodyDeclarationNode classDecl)
    {
        // Check if this is a Backend symbol — skip if so.
        var symbol = model.GetDeclaredSymbol(classDecl);
        if(symbol is not null && symbol.Origin == SymbolOrigin.Backend)
        {
            return;
        }

        // Skip intrinsic backend-backed stdlib types (e.g., Standard.String,
        // Standard.Array<T>). Their storage and methods come from the
        // Backing_Type_Map / IntrinsicEmissionTable, not from Mist bodies.
        // Pure-Mist stdlib types like Optional<T> are NOT backend-backed
        // and must be emitted normally.
        if(symbol?.DeclaredType is ClassType ct && ct.IsBackendBacked)
        {
            return;
        }

        var modifiers = EmitModifiers(classDecl.Modifiers);
        // Mist classes without an explicit access modifier default to public in emitted C#.
        if(!modifiers.Contains("public") && !modifiers.Contains("private"))
        {
            modifiers = "public " + modifiers;
        }

        var name = classDecl.Name.Lexeme;
        var typeParams = EmitTypeParameterList(classDecl.TypeParams);
        var baseClause = "";
        if(classDecl.BaseList is not null)
        {
            var implementorScope = (symbol?.DeclaredType as ClassType)?.MemberScope;
            var baseParts = implementorScope is not null
                ? EmitBaseListWithAssociatedTypeArgs(
                    classDecl.BaseList,
                    implementorScope,
                    symbol?.DeclaredType as ClassType,
                    name,
                    typeParams)
                : EmitBaseListWithTypeArgs(classDecl.BaseList);

            // Append with-block conformance interfaces to the inheritance list.
            var withBlockInterfaces = CollectWithBlockInterfaceNames(symbol?.DeclaredType);
            if(withBlockInterfaces.Count > 0)
            {
                baseParts = $"{baseParts}, {string.Join(", ", withBlockInterfaces)}";
            }

            baseClause = $" : {baseParts}";
        }
        else if(symbol?.DeclaredType is ClassType classType)
        {
            var parts = new List<string>();

            // Base class (if any)
            if(classType.BaseType is not null)
            {
                parts.Add(CSharpTypeMap.GetTypeName(classType.BaseType));
            }

            // Abstracts — append associated type slot concrete types as additional type arguments.
            foreach(var abs in classType.Abstracts)
            {
                parts.Add(EmitAbstractWithAssociatedTypeArgs(abs, classType.MemberScope, classType, name, typeParams));
            }

            // Append with-block conformance interfaces to the inheritance list.
            var withBlockInterfaces = CollectWithBlockInterfaceNames(classType);
            parts.AddRange(withBlockInterfaces);

            if(parts.Count > 0)
            {
                baseClause = $" : {string.Join(", ", parts)}";
            }
        }
        // Emit user-declared attribute annotations from source.
        EmitAttributeLists(classDecl.Attributes);

        // Emit [DependentType(...)] attributes for each dependent type parameter.
        if(symbol?.DeclaredType is not null)
        {
            EmitDependentTypeAttributes(GetTypeParametersFromType(symbol.DeclaredType));
        }

        AppendLine($"{modifiers}class {name}{typeParams}{baseClause}");
        // Emit constraint clauses (where T : ...) for constrained type parameters.
        if(symbol?.DeclaredType is not null)
        {
            var typeParamsForConstraints = GetTypeParametersFromType(symbol.DeclaredType);
            EmitConstraintClauses(typeParamsForConstraints);
        }

        AppendLine("{");
        indentLevel++;

        // Emit dependent type properties (e.g., `public static int Size => TSize.Value;`)
        var hasDependentProperties = false;
        if(symbol?.DeclaredType is not null)
        {
            hasDependentProperties = EmitDependentTypeProperties(GetTypeParametersFromType(symbol.DeclaredType));
        }

        // Emit factory delegate fields and constructor for parameterized constructor constraints.
        var hasFactoryFields = false;
        if(symbol?.DeclaredType is not null)
        {
            var typeParamsForFactory = GetTypeParametersFromType(symbol.DeclaredType);
            if(HasParameterizedConstructorConstraints(typeParamsForFactory))
            {
                hasFactoryFields = true;
                EmitFactoryDelegateFields(typeParamsForFactory);

                // If the class has user-defined constructors, merge factory params into them
                // instead of emitting a separate synthetic constructor.
                var hasUserConstructors = classDecl.Members.Any(m =>
                    m is ConstructorMemberMemberDeclarationNode);
                if(hasUserConstructors)
                {
                    currentClassFactoryTypeParams = typeParamsForFactory;
                }
                else
                {
                    EmitFactoryDelegateConstructor(name, typeParamsForFactory);
                }
            }
        }

        // Collect parameterized associated type slots from implemented abstract types.
        // These are needed so that method return types referencing the slot's nested type
        // are erased to the constraint interface (matching the abstract type declaration).
        var previousParameterizedSlotsForClass = currentParameterizedSlots;
        Dictionary<string, string>? abstractTypeParamSubstitutions = null;
        var previousAbstractTypeParamSubs = currentAbstractTypeParamSubs;
        if(symbol?.DeclaredType is ClassType classTypeForSlots)
        {
            var pSlots = new List<AssociatedTypeSlotType>();
            var subs = new Dictionary<string, string>(StringComparer.Ordinal);

            // Collect from semantic model abstract types.
            foreach(var abs in classTypeForSlots.Abstracts)
            {
                var def = abs is ConstructedType ctAbstract ? ctAbstract.GenericDefinition : abs;
                pSlots.AddRange(CollectAssociatedTypeSlotsFromType(def).Where(s => s.TypeParameters.Count > 0));

                // Build substitutions from abstract type params to concrete type args.
                if(abs is ConstructedType constructed)
                {
                    var defType = constructed.GenericDefinition;
                    var defTypeParams = GetTypeParametersFromType(defType);
                    for(var i = 0; i < defTypeParams.Count && i < constructed.TypeArguments.Count; i++)
                    {
                        if(defTypeParams[i] is TypeParameterType tpt)
                        {
                            subs[tpt.Name] = CSharpTypeMap.GetTypeName(constructed.TypeArguments[i]);
                        }
                    }
                }
            }

            // Also check the AST base list for abstract types not yet in the semantic model.
            if(pSlots.Count == 0 && classDecl.BaseList is not null)
            {
                // Try to find parameterized slots by looking up abstract type symbols by name.
                var allEntries = new List<BaseTypeRefNode> { classDecl.BaseList.First };
                allEntries.AddRange(classDecl.BaseList.BaseTypeRefs);
                foreach(var entry in allEntries)
                {
                    var abstractName = entry.Name.Lexeme;
                    var abstractSymbol = model.AllSymbols
                        .FirstOrDefault(s => s.Name == abstractName && s.DeclaredType is AbstractType);
                    if(abstractSymbol?.DeclaredType is AbstractType abstractDef)
                    {
                        var slots = CollectAssociatedTypeSlotsFromType(abstractDef);
                        pSlots.AddRange(slots.Where(s => s.TypeParameters.Count > 0));

                        // Build type param substitutions from the AST type arguments.
                        if(entry.TypeArgs is not null)
                        {
                            var abstractTypeParams = abstractDef.TypeParameters;
                            var argNodes = new List<TypeArgumentNode> { entry.TypeArgs.TypeArgument };
                            argNodes.AddRange(entry.TypeArgs.TypeArguments);
                            for(var i = 0; i < abstractTypeParams.Count && i < argNodes.Count; i++)
                            {
                                if(abstractTypeParams[i] is TypeParameterType tpt)
                                {
                                    subs[tpt.Name] = EmitSingleTypeArgument(argNodes[i]);
                                }
                            }
                        }
                    }
                }
            }

            currentParameterizedSlots = pSlots.Count > 0 ? pSlots : null;
            abstractTypeParamSubstitutions = subs.Count > 0 ? subs : null;
        }

        currentAbstractTypeParamSubs = abstractTypeParamSubstitutions;

        // Track dependent type parameter names declared on this type so the
        // identifier emission fallback can distinguish local dep params from
        // fields/properties with the same name in other types.
        var previousTypeDependentParamNames = currentTypeDependentParamNames;
        if(symbol?.DeclaredType is not null)
        {
            var typeParamsForDep = GetTypeParametersFromType(symbol.DeclaredType);
            var depNames = new HashSet<string>(StringComparer.Ordinal);
            foreach(var tp in typeParamsForDep)
            {
                if(tp is DependentTypeParameterType depParam)
                {
                    _ = depNames.Add(depParam.Name);
                }
            }

            currentTypeDependentParamNames = depNames.Count > 0 ? depNames : null;
        }

        // Track the current class type for implicit override detection.
        var previousClassType = currentClassType;
        currentClassType = symbol?.DeclaredType as ClassType;

        // Emit every member in declaration order. No name-based grouping or
        // deduplication is performed, so overloaded methods and constructors
        // are each emitted as separate C# declarations.
        for(var i = 0; i < classDecl.Members.Length; i++)
        {
            if(i > 0 || hasFactoryFields || hasDependentProperties)
            {
                AppendLine();
            }

            EmitMemberDeclaration(classDecl.Members[i]);
        }

        // Clear class-level factory tracking after emitting all members.
        currentClassFactoryTypeParams = null;
        currentTypeDependentParamNames = previousTypeDependentParamNames;
        currentClassType = previousClassType;
        currentParameterizedSlots = previousParameterizedSlotsForClass;
        currentAbstractTypeParamSubs = previousAbstractTypeParamSubs;

        // Emit CLR boilerplate for IEnumerator<T> and IEnumerable<T> implementations.
        // C#'s System.Collections.Generic.IEnumerator<T> requires IDisposable.Dispose(),
        // IEnumerator.Reset(), and non-generic IEnumerator.Current. IEnumerable<T> requires
        // IEnumerator<T> GetEnumerator() and non-generic IEnumerable.GetEnumerator().
        if(symbol?.DeclaredType is ClassType classTypeForIterator)
        {
            EmitIteratorInterfaceBoilerplate(classTypeForIterator);
        }

        // Emit forwarding stubs for default interface methods the class inherits but does
        // not override, so the defaults are callable directly on the class (Rust-style).
        if(symbol?.DeclaredType is ClassType classTypeForDefaults)
        {
            EmitDefaultMethodForwardingStubs(
                classTypeForDefaults.Abstracts,
                classDecl.Members,

                abstractTypeParamSubstitutions);
        }

        // Emit explicit interface implementations and forwarding stubs for with-block
        // conformances. For each with-block entry targeting this type, emit:
        // 1. Explicit interface implementations delegating to mapped source members.
        // 2. Forwarding stubs for default methods without mappings.
        if(symbol?.DeclaredType is not null)
        {
            EmitWithBlockConformanceMembers(symbol.DeclaredType, classDecl.Members);
        }

        // Emit explicit interface implementations for methods that use parameterized
        // associated type slots. The public method returns the concrete nested type,
        // and the explicit impl bridges to it with the erased constraint interface type.
        if(abstractTypeParamSubstitutions is not null && currentParameterizedSlots is null)
        {
            // _currentParameterizedSlots was already restored to previous, use the local pSlots.
        }

        if(symbol?.DeclaredType is ClassType classTypeForParamSlots && abstractTypeParamSubstitutions is not null)
        {
            EmitExplicitInterfaceImplsForParameterizedSlots(
                classTypeForParamSlots, classDecl, abstractTypeParamSubstitutions);
        }

        // Emit parameterless constructor for defaultable classes if one doesn't already exist.
        // This is needed so __CreateDefault() can instantiate the class via `new ClassName()`.
        // The constructor sets the class's own default field values (not inherited ones).
        // When a derived class calls `new Derived()`, the chain `Derived() → base() → Base()`
        // ensures each level initializes its own fields. This avoids accessibility issues
        // with private base fields.
        // Accessibility:
        //   - public when the class has NO user-defined constructors (implicit constructor for `new T()`)
        //   - protected when the class HAS user-defined constructors (only __CreateDefault needs it)
        // Structs don't need this because C# allows `new MyStruct()` without an explicit
        // parameterless constructor.
        var isDefaultableClass = symbol?.DeclaredType is ClassType defaultableClassForCtor
            && defaultableClassForCtor.Defaultability is DefaultabilityStatus.Inferred or DefaultabilityStatus.Explicit
            && defaultableClassForCtor.DefaultValues is not null;
        var emittedDefaultCtor = false;

        if(isDefaultableClass)
        {
            var hasParameterlessCtor = classDecl.Members.Any(m =>
                m is ConstructorMemberMemberDeclarationNode ctorMember
                && ctorMember.Constructor.Parameters is null);

            if(!hasParameterlessCtor)
            {
                var classTypeForCtor = (ClassType)symbol!.DeclaredType!;
                var ownFields = classTypeForCtor.DefaultValues!
                    .Where(kvp => kvp.Value.ValueKind != DefaultValueKind.InheritedDefault)
                    .ToList();

                // Only emit a parameterless constructor if there are own fields
                // to initialize. Zero-field classes can rely on the implicit public
                // parameterless constructor — emitting a protected one would suppress it
                // and make the class unconstructable from outside.
                if(ownFields.Count > 0)
                {
                    emittedDefaultCtor = true;

                    if(classDecl.Members.Length > 0)
                    {
                        AppendLine();
                    }

                    // If the class has no user-defined constructors, the synthesized
                    // parameterless constructor must be public — it serves as the implicit
                    // constructor that allows `new ClassName()`. When the class has user-defined
                    // constructors, keep it protected (only __CreateDefault needs it).
                    var hasAnyUserConstructor = classDecl.Members.Any(m =>
                        m is ConstructorMemberMemberDeclarationNode);
                    var ctorAccessibility = hasAnyUserConstructor ? "protected" : "public";

                    AppendLine($"{ctorAccessibility} {classDecl.Name.Lexeme}()");
                    AppendLine("{");
                    indentLevel++;
                    foreach(var (fieldName, fieldValue) in ownFields)
                    {
                        EmitDefaultFieldAssignment("this", fieldName, fieldValue);
                    }

                    indentLevel--;
                    AppendLine("}");
                }
            }
        }

        // Emit __CreateDefault factory method for defaultable classes.
        // If we emitted our own parameterless constructor that sets own fields,
        // __CreateDefault just creates the instance (constructor chain handles initialization).
        // Otherwise, __CreateDefault sets own fields directly.
        if(symbol?.DeclaredType is ClassType defaultableClass
            && defaultableClass.Defaultability is DefaultabilityStatus.Inferred or DefaultabilityStatus.Explicit
            && defaultableClass.DefaultValues is not null)
        {
            var hasExistingMembers = classDecl.Members.Length > 0 || isDefaultableClass;
            EmitCreateDefaultMethod(defaultableClass.DisplayName, defaultableClass.DefaultValues,
                hasExistingMembers, skipOwnFields: emittedDefaultCtor);
        }

        // Emit synthesized Drop() method for types that have droppable fields but
        // no explicit Drop() (SyntheticDropFields is set by SyntheticDropInferenceEngine).
        if(symbol?.DeclaredType is ClassType classTypeForDrop && classTypeForDrop.SyntheticDropFields is not null)
        {
            AppendLine();
            AppendLine("public void Drop()");
            AppendLine("{");
            indentLevel++;
            foreach(var field in classTypeForDrop.SyntheticDropFields)
            {
                AppendLine($"this.{field.Name}.Drop();");
            }

            indentLevel--;
            AppendLine("}");
        }

        indentLevel--;
        AppendLine("}");
    }

    private void EmitAbstractDeclaration(TypeBodyDeclarationNode abstractDecl)
    {
        // Skip abstract types that map to CLR counterparts (e.g., IEnumerable → System.Collections.Generic.IEnumerable).
        // These are not emitted — the C# runtime provides them.
        var abstractSymbol2 = model.GetDeclaredSymbol(abstractDecl);
        if(abstractSymbol2?.DeclaredType is AbstractType abstractType
            && abstractType.ContainingNamespace == "Standard"
            && CSharpTypeMap.TryGetMappedName($"Standard.{abstractType.DisplayName}") is not null)
        {
            return;
        }

        var name = abstractDecl.Name.Lexeme;
        var typeParams = EmitTypeParameterList(abstractDecl.TypeParams);
        var abstractList = "";
        if(abstractDecl.BaseList is not null)
        {
            abstractList = $" : {EmitBaseListWithTypeArgs(abstractDecl.BaseList)}";
        }

        // ── Associated type generic lifting ──────────────────────────────
        // Collect associated type slots from the abstract type's member scope.
        // Each slot becomes a synthesized type parameter (T + slot name) on
        // the emitted C# interface, since C# cannot declare abstract nested types.
        // Parameterized slots (those with their own type parameters) cannot be lifted
        // to type parameters because C# doesn't support higher-kinded types. Instead,
        // they are erased to their constraint interface in method signatures.
        var abstractSymbol = model.GetDeclaredSymbol(abstractDecl);
        var allAssociatedTypeSlots = CollectAssociatedTypeSlots(abstractSymbol);
        var associatedTypeSlots = allAssociatedTypeSlots.Where(s => s.TypeParameters.Count == 0).ToList();
        var parameterizedSlots = allAssociatedTypeSlots.Where(s => s.TypeParameters.Count > 0).ToList();
        var slotSubstitutions = BuildAssociatedTypeSubstitutions(associatedTypeSlots);

        // Append synthesized type parameters for non-parameterized associated type slots.
        if(associatedTypeSlots.Count > 0)
        {
            var synthesizedParams = string.Join(", ", associatedTypeSlots.Select(s => $"T{s.DisplayName}"));
            typeParams = typeParams.Length > 0
                ? $"{typeParams[..^1]}, {synthesizedParams}>"
                : $"<{synthesizedParams}>";
        }

        // Emit user-declared attribute annotations from source.
        EmitAttributeLists(abstractDecl.Attributes);

        // Emit [DependentType(...)] attributes for each dependent type parameter.
        if(abstractSymbol?.DeclaredType is AbstractType abstractTypeForAttrs)
        {
            EmitDependentTypeAttributes(abstractTypeForAttrs.TypeParameters);
        }
        // Set parameterized slots for the duration of abstract type emission so that
        // EmitTypeReferenceWithSubstitution can erase them to constraint interfaces.
        var previousParameterizedSlots = currentParameterizedSlots;
        currentParameterizedSlots = parameterizedSlots.Count > 0 ? parameterizedSlots : null;
        AppendLine($"public interface {name}{typeParams}{abstractList}");
        // Emit constraint clauses (where T : ...) for constrained type parameters.
        if(abstractSymbol?.DeclaredType is AbstractType abstractTypeForConstraints)
        {
            EmitConstraintClauses(abstractTypeForConstraints.TypeParameters);
        }
        // Emit where constraints for synthesized associated type parameters.
        EmitAssociatedTypeSlotConstraints(associatedTypeSlots);

        AppendLine("{");
        indentLevel++;

        foreach(var member in abstractDecl.Members)
        {
            switch(member)
            {
                case MethodMemberMemberDeclarationNode methodMember
                    when methodMember.Method.Body is CallableNoBodyCallableBodyNode:
                    EmitAbstractMethodDeclaration(methodMember.Method, slotSubstitutions);
                    break;
                case PropertyMemberMemberDeclarationNode propertyMember:
                    EmitAbstractPropertyDeclaration(propertyMember.Property, slotSubstitutions);
                    break;
                // Default methods (methods with bodies) emit as C# default interface methods
                // (DIM) — a method WITH a body on the interface. Implementers may omit them.
                case MethodMemberMemberDeclarationNode defaultMethodMember
                    when defaultMethodMember.Method.Body is CallableBlockBodyCallableBodyNode:
                    EmitMethodDeclaration(defaultMethodMember.Method);
                    break;
                // Skip associated type slot declarations — they are lifted to type parameters.
                case AssociatedTypeMemberMemberDeclarationNode:
                    break;
            }
        }

        indentLevel--;
        AppendLine("}");
        currentParameterizedSlots = previousParameterizedSlots;
    }

    /// <summary>
    /// Emits forwarding stubs on a concrete type (class or struct) for every default
    /// interface method (DIM) it inherits from its abstract types but does NOT override.
    ///
    /// C# default interface methods are only callable through an interface-typed reference
    /// (e.g., <c>((IGreeting)x).Greet()</c>). To preserve Rust-style semantics — where the
    /// default is callable directly on the concrete type — the default method body is
    /// duplicated on the concrete type:
    /// <code>
    /// public string Greet()
    /// {
    ///     return $"Hello, I am {this.Name()}";
    /// }
    /// </code>
    ///
    /// A naive <c>((IFace)this).Method()</c> forwarding stub does not work because the
    /// public method on the concrete type becomes the implicit interface implementation,
    /// causing infinite recursion.
    ///
    /// When the concrete type provides its own implementation for a default method (matched
    /// by name + arity), no stub is emitted — the concrete implementation takes precedence.
    /// Defaults inherited transitively through the abstract type hierarchy are also covered.
    /// </summary>
    private void EmitDefaultMethodForwardingStubs(
        IReadOnlyList<IType> directAbstracts,
        IReadOnlyList<MemberDeclarationNode> concreteMembers,

        Dictionary<string, string>? abstractParamSubs)
    {
        if(directAbstracts.Count == 0)
        {
            return;
        }

        // Build the transitive closure of abstract types. Each entry is the IType to cast
        // to (a definition or a constructed instantiation). Defs are de-duplicated.
        var closure = new List<IType>();
        var seenDefs = new HashSet<AbstractType>();

        void visit(IType abs)
        {
            var def = abs is ConstructedType ct ? ct.GenericDefinition as AbstractType : abs as AbstractType;
            if(def is null)
            {
                return;
            }

            if(!seenDefs.Add(def))
            {
                return;
            }

            closure.Add(abs);
            foreach(var parent in def.Abstracts)
            {
                visit(parent);
            }
        }

        foreach(var abs in directAbstracts)
        {
            visit(abs);
        }

        // A default is considered overridden when the concrete type declares a method with
        // the same name and parameter count. The type checker (task 3.3) guarantees that a
        // same-name method matching a default also matches its signature exactly, so name +
        // arity is a safe discriminator. Methods with a different arity are unrelated
        // overloads and do not suppress the default's forwarding stub.
        var concreteMethodKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach(var member in concreteMembers)
        {
            if(member is MethodMemberMemberDeclarationNode mm)
            {
                _ = concreteMethodKeys.Add($"{mm.Method.Name.Lexeme}/{CountParameters(mm.Method.Parameters)}");
            }
        }

        // Track emitted (name + arity) keys so diamond inheritance does not produce duplicate
        // members. The first abstract encountered in the closure wins (deterministic order).
        var emittedKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach(var abs in closure)
        {
            var def = abs is ConstructedType ct ? ct.GenericDefinition as AbstractType : abs as AbstractType;
            if(def is null || def.DefaultMethods.Count == 0)
            {
                continue;
            }

            foreach(var entry in def.DefaultMethods.OrderBy(kvp => kvp.Key, StringComparer.Ordinal))
            {
                var methodName = entry.Key;
                if(model.GetDeclaringNode(entry.Value) is not MethodDeclarationNode node)
                {
                    continue;
                }

                var key = $"{methodName}/{CountParameters(node.Parameters)}";

                // Concrete type overrides this default — emit only the concrete implementation.
                if(concreteMethodKeys.Contains(key))
                {
                    continue;
                }
                // Already forwarded via a more-derived abstract type (diamond) — skip.
                if(!emittedKeys.Add(key))
                {
                    continue;
                }

                // Emit the default method's full body on the concrete type. This duplicates
                // the DIM body, ensuring the method is callable directly on the concrete type
                // without interface casting (Rust-style semantics). C# DIM forwarding via
                // ((IFace)this).Method() does not work because the concrete type's own method
                // becomes the implicit interface implementation, causing infinite recursion.
                // Force `public` visibility since interface members are implicitly public but
                // the duplicated method on a class needs an explicit access modifier.
                AppendLine();
                var returnType = abstractParamSubs is not null
                    ? EmitTypeReferenceWithSubstitution(node.ReturnType, abstractParamSubs)
                    : EmitTypeReference(node.ReturnType);
                var parameters = abstractParamSubs is not null
                    ? EmitParameterListWithSubstitution(node.Parameters, abstractParamSubs)
                    : EmitParameterList(node.Parameters);
                var methodTypeParams = EmitTypeParameterList(node.TypeParams);

                // Check if the node declares explicit modifiers (e.g., `public`). If not,
                // force `public` since interface members are implicitly public.
                var modifiers = EmitModifiers(node.Modifiers);
                if(string.IsNullOrEmpty(modifiers))
                {
                    modifiers = "public ";
                }

                AppendLine($"{modifiers}{returnType} {methodName}{methodTypeParams}({parameters})");

                // Emit the body.
                if(node.Body is CallableBlockBodyCallableBodyNode blockBody)
                {
                    EmitMethodBlock(blockBody.Block, appendImplicitReturn: false);
                }
            }
        }
    }

    /// <summary>
    /// Collects C# interface names from with-block conformance entries targeting
    /// the given concrete type. These names are appended to the type's inheritance list.
    /// </summary>
    private List<string> CollectWithBlockInterfaceNames(IType? concreteType)
    {
        var result = new List<string>();
        if(concreteType is null)
        {
            return result;
        }

        foreach(var (type, abstractType, _) in model.ConformanceMap.All())
        {
            if(!ReferenceEquals(type, concreteType))
            {
                continue;
            }

            result.Add(CSharpTypeMap.GetTypeName(abstractType));
        }

        return result;
    }

    /// <summary>
    /// Emits explicit interface implementations and default method forwarding stubs
    /// for with-block conformances targeting the given concrete type.
    /// For each conformance entry:
    /// 1. Emit explicit interface implementations delegating to mapped source members.
    /// 2. For default methods without mappings, emit a forwarding stub (body duplication).
    /// 3. For mapped slots that have defaults, the mapping takes precedence.
    /// </summary>
    private void EmitWithBlockConformanceMembers(
        IType concreteType,
        IReadOnlyList<MemberDeclarationNode> concreteMembers)
    {
        foreach(var (type, abstractTypeRef, mappings) in model.ConformanceMap.All())
        {
            if(!ReferenceEquals(type, concreteType))
            {
                continue;
            }

            if(abstractTypeRef is not AbstractType abstractType)
            {
                continue;
            }

            var interfaceName = CSharpTypeMap.GetTypeName(abstractTypeRef);

            // Build a set of mapped target slot names so we know which defaults need forwarding.
            var mappedTargetNames = new HashSet<string>(StringComparer.Ordinal);
            foreach(var mapping in mappings)
            {
                _ = mappedTargetNames.Add(mapping.TargetName);
            }

            // 1. Emit explicit interface implementations for mapped members.
            foreach(var mapping in mappings)
            {
                var targetNode = model.GetDeclaringNode(mapping.TargetSlot);

                if(targetNode is MethodDeclarationNode targetMethodDecl)
                {
                    var returnType = EmitTypeReference(targetMethodDecl.ReturnType);
                    var parameters = EmitParameterList(targetMethodDecl.Parameters);
                    var paramNames = EmitParameterNamesOnly(targetMethodDecl.Parameters);
                    var typeParamsStr = EmitTypeParameterList(targetMethodDecl.TypeParams);

                    AppendLine();
                    AppendLine($"{returnType} {interfaceName}.{mapping.TargetName}{typeParamsStr}({parameters}) => this.{mapping.SourceName}({paramNames});");
                }
                else if(targetNode is BlockPropertyPropertyDeclarationNode targetPropDecl)
                {
                    var propType = EmitTypeReference(targetPropDecl.Type);

                    AppendLine();
                    // Emit explicit property implementation with accessors delegating to mapped source.
                    var accessors = new List<string>();
                    foreach(var accessor in targetPropDecl.Accessors)
                    {
                        switch(accessor)
                        {
                            case AutoGetAccessorAccessorDeclarationNode:
                                accessors.Add($"get => this.{mapping.SourceName};");
                                break;
                            case AutoSetAccessorAccessorDeclarationNode:
                                accessors.Add($"set => this.{mapping.SourceName} = value;");
                                break;
                        }
                    }

                    AppendLine($"{propType} {interfaceName}.{mapping.TargetName} {{ {string.Join(" ", accessors)} }}");
                }
            }

            // 2. Emit forwarding stubs for default methods without mappings.
            // These are default methods on the abstract type that the with-block did not map.
            // We duplicate the DIM body on the concrete type (same approach as Phase 3).
            if(abstractType.DefaultMethods.Count > 0)
            {
                // Build a set of concrete method keys to detect if the concrete type already
                // has an implementation for any default method.
                var concreteMethodKeys = new HashSet<string>(StringComparer.Ordinal);
                foreach(var member in concreteMembers)
                {
                    if(member is MethodMemberMemberDeclarationNode mm)
                    {
                        _ = concreteMethodKeys.Add($"{mm.Method.Name.Lexeme}/{CountParameters(mm.Method.Parameters)}");
                    }
                }

                foreach(var entry in abstractType.DefaultMethods.OrderBy(kvp => kvp.Key, StringComparer.Ordinal))
                {
                    var methodName = entry.Key;

                    // If this default was explicitly mapped, the mapping takes precedence —
                    // the explicit interface implementation was already emitted above.
                    if(mappedTargetNames.Contains(methodName))
                    {
                        continue;
                    }

                    // If the concrete type already has an implementation, skip.
                    if(model.GetDeclaringNode(entry.Value) is not MethodDeclarationNode node)
                    {
                        continue;
                    }

                    var key = $"{methodName}/{CountParameters(node.Parameters)}";
                    if(concreteMethodKeys.Contains(key))
                    {
                        continue;
                    }

                    // Emit the default method body on the concrete type.
                    AppendLine();
                    var returnType = EmitTypeReference(node.ReturnType);
                    var parameters = EmitParameterList(node.Parameters);
                    var methodTypeParams = EmitTypeParameterList(node.TypeParams);

                    var modifiers = EmitModifiers(node.Modifiers);
                    if(string.IsNullOrEmpty(modifiers))
                    {
                        modifiers = "public ";
                    }

                    AppendLine($"{modifiers}{returnType} {methodName}{methodTypeParams}({parameters})");

                    if(node.Body is CallableBlockBodyCallableBodyNode blockBody)
                    {
                        EmitMethodBlock(blockBody.Block, appendImplicitReturn: false);
                    }
                }
            }
        }
    }

    private void EmitAbstractMethodDeclaration(MethodDeclarationNode methodDecl, Dictionary<string, string>? slotSubstitutions = null)
    {
        var returnType = slotSubstitutions is not null
            ? EmitTypeReferenceWithSubstitution(methodDecl.ReturnType, slotSubstitutions)
            : EmitTypeReference(methodDecl.ReturnType);
        var name = methodDecl.Name.Lexeme;
        var typeParams = EmitTypeParameterList(methodDecl.TypeParams);
        var parameters = slotSubstitutions is not null
            ? EmitParameterListWithSubstitution(methodDecl.Parameters, slotSubstitutions)
            : EmitParameterList(methodDecl.Parameters);

        // For generic abstract type methods with parameterized constructor constraints,
        // append factory delegate parameters.
        var methodSymbol = model.GetDeclaredSymbol(methodDecl) as Symbol;
        var methodTypeParams = methodSymbol?.TypeParameters ?? Array.Empty<IType>();
        var factoryParams = GetFactoryDelegateMethodParams(methodTypeParams);
        if(factoryParams.Length > 0)
        {
            parameters = parameters.Length > 0
                ? $"{parameters}, {factoryParams}"
                : factoryParams;
        }

        // Emit ref / ref readonly prefix for ref-returning methods.
        var refReturnPrefix = methodSymbol?.ReturnRefKind switch
        {
            RefKind.Ref => "ref ",
            RefKind.RefReadonly => "ref readonly ",
            _ => ""
        };

        // Check if there are constraint clauses to emit (regular constraints or dependent type parameters).
        var hasConstraints = methodTypeParams.Any(tp =>
            tp is DependentTypeParameterType ||
            (tp is TypeParameterType tpc && tpc.Constraints is not null &&
            (tpc.Constraints.HasClassConstraint || tpc.Constraints.HasStructConstraint ||
             tpc.Constraints.BaseClassConstraint is not null ||
             tpc.Constraints.AbstractConstraints.Count > 0 ||
             tpc.Constraints.ConstructorConstraints.Any(c => c.Count == 0))));

        if(hasConstraints)
        {
            AppendLine($"{refReturnPrefix}{returnType} {name}{typeParams}({parameters})");
            EmitConstraintClauses(methodTypeParams);
            AppendLine(";");
        }
        else
        {
            AppendLine($"{refReturnPrefix}{returnType} {name}{typeParams}({parameters});");
        }
    }

    private void EmitAbstractPropertyDeclaration(PropertyDeclarationNode propertyDecl, Dictionary<string, string>? slotSubstitutions = null)
    {
        // Abstract properties always use the BlockProperty variant (they have auto accessors).
        if(propertyDecl is not BlockPropertyPropertyDeclarationNode blockProp)
        {
            return;
        }

        var type = slotSubstitutions is not null
            ? EmitTypeReferenceWithSubstitution(blockProp.Type, slotSubstitutions)
            : EmitTypeReference(blockProp.Type);
        var name = blockProp.Name.Lexeme;

        var accessors = new List<string>();
        foreach(var accessor in blockProp.Accessors)
        {
            switch(accessor)
            {
                case AutoGetAccessorAccessorDeclarationNode:
                    accessors.Add("get;");
                    break;
                case AutoSetAccessorAccessorDeclarationNode:
                    accessors.Add("set;");
                    break;
            }
        }

        AppendLine($"{type} {name} {{ {string.Join(" ", accessors)} }}");
    }

    /// <summary>
    /// Emits CLR boilerplate members required by System.Collections.Generic.IEnumerator&lt;T&gt;
    /// and System.Collections.Generic.IEnumerable&lt;T&gt;. These interfaces inherit from
    /// non-generic IEnumerator/IEnumerable and IDisposable, requiring additional members
    /// that Mist does not model explicitly.
    /// </summary>
    private void EmitIteratorInterfaceBoilerplate(IType type)
    {
        var abstracts = type switch
        {
            ClassType ct => ct.Abstracts,
            StructType st => st.Abstracts,
            _ => null
        };
        if(abstracts is null)
        {
            return;
        }

        foreach(var abs in abstracts)
        {
            var definition = abs is ConstructedType ct2 ? ct2.GenericDefinition : abs;
            if(definition is not AbstractType abstractType || abstractType.ContainingNamespace != "Standard")
            {
                continue;
            }

            if(abstractType.DisplayName == "IEnumerator")
            {
                // Extract TElement from IEnumerator<TElement>
                _ = abs is ConstructedType ctEnum
                    ? CSharpTypeMap.GetTypeName(ctEnum.TypeArguments[0])
                    : "object";

                AppendLine();
                AppendLine("// CLR IEnumerator<T> boilerplate");
                AppendLine($"object System.Collections.IEnumerator.Current => Current;");
                AppendLine($"public void Reset() {{ throw new System.NotSupportedException(); }}");
                AppendLine($"public void Dispose() {{ }}");
            }
            else if(abstractType.DisplayName == "IEnumerable")
            {
                // Extract TElement from IEnumerable<TElement>
                var elementType = abs is ConstructedType ctEnum
                    ? CSharpTypeMap.GetTypeName(ctEnum.TypeArguments[0])
                    : "object";

                AppendLine();
                AppendLine("// CLR IEnumerable<T> boilerplate");
                AppendLine($"System.Collections.Generic.IEnumerator<{elementType}> System.Collections.Generic.IEnumerable<{elementType}>.GetEnumerator() => GetEnumerator();");
                AppendLine($"System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();");
            }
        }
    }

    /// <summary>
    /// Emits explicit interface implementations for methods whose return type uses a
    /// parameterized associated type slot. The public method returns the concrete nested
    /// type (preserving full type info), and this explicit impl bridges to it with the
    /// erased constraint interface type to satisfy the C# interface contract.
    /// </summary>
    private void EmitExplicitInterfaceImplsForParameterizedSlots(
        ClassType classType,
        TypeBodyDeclarationNode classDecl,
        Dictionary<string, string> abstractParamSubs)
    {
        // Collect parameterized slots from implemented abstract types.
        var paramSlots = new List<(AbstractType Abstract, AssociatedTypeSlotType Slot, IType ConstructedAbstract)>();
        foreach(var abs in classType.Abstracts)
        {
            var def = abs is ConstructedType ctAbstract ? ctAbstract.GenericDefinition as AbstractType : abs as AbstractType;
            if(def is null)
            {
                continue;
            }

            foreach(var slot in CollectAssociatedTypeSlotsFromType(def))
            {
                if(slot.TypeParameters.Count > 0)
                {
                    paramSlots.Add((def, slot, abs));
                }
            }
        }

        if(paramSlots.Count == 0)
        {
            return;
        }

        // For each method in the class, check if its return type matches a parameterized slot.
        foreach(var member in classDecl.Members)
        {
            if(member is not MethodMemberMemberDeclarationNode methodMember)
            {
                continue;
            }

            var methodDecl = methodMember.Method;
            var returnTypeRef = methodDecl.ReturnType;
            if(returnTypeRef.First is not NamedSingleTypeRefSingleTypeRefNode namedReturn)
            {
                continue;
            }

            if(namedReturn.TypeArgs is null)
            {
                continue;
            }

            var returnName = ExtractQualifiedName(namedReturn.Name);
            var matchingSlot = paramSlots.FirstOrDefault(ps => ps.Slot.DisplayName == returnName);
            if(matchingSlot.Slot is null)
            {
                continue;
            }

            // This method uses a parameterized slot as its return type.
            // Emit: ErasedType AbstractName.MethodName(params) => MethodName(params);
            var erasedReturnType = EraseParameterizedSlotToConstraint(matchingSlot.Slot, namedReturn.TypeArgs, abstractParamSubs);
            if(erasedReturnType is null)
            {
                continue;
            }

            var methodName = methodDecl.Name.Lexeme;
            var abstractName = CSharpTypeMap.GetTypeName(matchingSlot.ConstructedAbstract);
            // Remove the lifted non-parameterized slot type args from the abstract type name
            // (they were added by the lifting process for non-parameterized slots).
            var parameters = EmitParameterList(methodDecl.Parameters);
            var argList = EmitParameterNamesOnly(methodDecl.Parameters);

            AppendLine();
            AppendLine($"{erasedReturnType} {abstractName}.{methodName}({parameters}) => {methodName}({argList});");
        }
    }

    /// <summary>
    /// Emits just the parameter names (without types) for forwarding calls.
    /// </summary>
    private static string EmitParameterNamesOnly(ParameterListNode? paramList)
    {
        if(paramList is null)
        {
            return "";
        }

        var names = new List<string>();
        foreach(var child in paramList.Children())
        {
            if(child is ParameterNode param)
            {
                names.Add(param.Name.Lexeme);
            }
        }

        return string.Join(", ", names);
    }

    /// <summary>
    /// Emits a type reference, substituting type parameter names with their concrete
    /// type argument names when a substitution map is provided. Used for explicit
    /// interface implementations on constructed types.
    /// </summary>
    private string EmitTypeReferenceWithSubstitution(TypeReferenceNode typeRef, Dictionary<string, string>? substitutions)
    {
        if(substitutions is null)
        {
            return EmitTypeReference(typeRef);
        }

        // For named type references, check if the name is a type parameter that needs substitution.
        if(typeRef.First is NamedSingleTypeRefSingleTypeRefNode namedRef)
        {
            var name = namedRef.Name.IDENTIFIER.Lexeme;
            if(string.IsNullOrEmpty(name))
            {
                // Fallback: extract from source span via the qualified name helper.
                name = ExtractQualifiedName(namedRef.Name);
            }

            // If name is still null/empty (node from a different source file where
            // span-based extraction failed), attempt cross-file lookup immediately.
            if(string.IsNullOrEmpty(name))
            {
                var span = namedRef.Name.Span;
                if(span.Length is > 0 and <= 128)
                {
                    foreach(var unit in model.Units)
                    {
                        if(unit.Source is not null
                            && span.Start + span.Length <= unit.Source.Contents.Length)
                        {
                            var crossFileName = unit.Source.Contents.Substring(span.Start, span.Length);
                            if(substitutions.TryGetValue(crossFileName, out var crossFileSubstituted))
                            {
                                return namedRef.ArrayRank is not null ? $"{crossFileSubstituted}[]" : crossFileSubstituted;
                            }
                            // Use the cross-file name even if it's not a type parameter.
                            if(!string.IsNullOrEmpty(crossFileName))
                            {
                                name = crossFileName;
                                break;
                            }
                        }
                    }
                }

                // If still null after all attempts, fall back to emitting without substitution.
                if(string.IsNullOrEmpty(name))
                {
                    return EmitTypeReference(typeRef);
                }
            }

            if(substitutions.TryGetValue(name, out var substituted))
            {
                // Type parameter substituted with concrete type.
                return namedRef.ArrayRank is not null ? $"{substituted}[]" : substituted;
            }

            // Check if this is a parameterized associated type slot that should be
            // erased to its constraint interface. For example, `Mapped<string>` where
            // `type Mapped<TOut> : ITransform<TIn, TOut>` becomes `ITransform<TIn, string>`.
            if(currentParameterizedSlots is not null && namedRef.TypeArgs is not null)
            {
                var matchingSlot = currentParameterizedSlots.FirstOrDefault(s => s.DisplayName == name);
                if(matchingSlot is not null)
                {
                    var erasedType = EraseParameterizedSlotToConstraint(matchingSlot, namedRef.TypeArgs, substitutions);
                    if(erasedType is not null)
                    {
                        return namedRef.ArrayRank is not null ? $"{erasedType}[]" : erasedType;
                    }
                }
            }

            // The name extraction may have failed because the node is from a
            // different source file (e.g., an abstract type method parameter accessed
            // during explicit interface implementation emission). Try to find
            // the name from the correct compilation unit's source text.
            var nameSpan = namedRef.Name.Span;
            if(nameSpan.Length is > 0 and <= 128)
            {
                foreach(var unit in model.Units)
                {
                    if(unit.Source is not null
                        && nameSpan.Start + nameSpan.Length <= unit.Source.Contents.Length)
                    {
                        var crossFileName = unit.Source.Contents.Substring(nameSpan.Start, nameSpan.Length);
                        if(substitutions.TryGetValue(crossFileName, out var crossFileSubstituted))
                        {
                            return namedRef.ArrayRank is not null ? $"{crossFileSubstituted}[]" : crossFileSubstituted;
                        }
                    }
                }
            }

            // Not a type parameter — emit normally but substitute type arguments recursively.
            if(namedRef.TypeArgs is not null)
            {
                var typeArgs = EmitTypeArgumentListWithSubstitution(namedRef.TypeArgs, substitutions);
                var mappedName = CSharpTypeMap.TryGetMappedName(name);
                var finalName = mappedName ?? name;
                return namedRef.ArrayRank is not null ? $"{finalName}{typeArgs}[]" : $"{finalName}{typeArgs}";
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
        var parts = new List<string>
        {
            EmitSingleTypeArgumentWithSubstitution(typeArgs.TypeArgument, substitutions)
        };
        foreach(var arg in typeArgs.TypeArguments)
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
        if(arg is TypeReferenceTypeArgumentTypeArgumentNode typeRefArg)
        {
            return EmitTypeReferenceWithSubstitution(typeRefArg.Type, substitutions);
        }

        // Literal type arguments (dependent types) — emit carrier struct name directly.
        return EmitSingleTypeArgument(arg);
    }

    private void EmitStructDeclaration(TypeBodyDeclarationNode structDecl)
    {
        var symbol = model.GetDeclaredSymbol(structDecl);
        if(symbol is not null && symbol.Origin == SymbolOrigin.Backend)
        {
            return;
        }

        // Skip intrinsic backend-backed stdlib types (e.g., Standard.Int32,
        // Standard.Boolean). Their storage and methods come from the
        // Backing_Type_Map / IntrinsicEmissionTable, not from Mist bodies.
        if(symbol?.DeclaredType is StructType st && st.IsBackendBacked)
        {
            return;
        }

        // Skip Standard.Void — its C# representation is the MistVoid helper
        // struct generated by the emitter. The Mist source defines the
        // semantics (private constructor, static property, equality operators)
        // but the C# backend provides its own implementation because C# has
        // restrictions on void-typed members and same-name-as-type members.
        if(symbol?.DeclaredType is StructType voidSt && voidSt.IsVoid()
            && voidSt.ContainingNamespace == "Standard")
        {
            NeedsMistVoidStruct = true;
            return;
        }

        var modifiers = EmitModifiers(structDecl.Modifiers);
        // Mist structs without an explicit access modifier default to public in emitted C#.
        if(!modifiers.Contains("public") && !modifiers.Contains("private"))
        {
            modifiers = "public " + modifiers;
        }

        var name = structDecl.Name.Lexeme;
        var typeParams = EmitTypeParameterList(structDecl.TypeParams);
        var abstractList = "";
        if(structDecl.BaseList is not null)
        {
            // If the struct has a base struct, skip the first entry in the base list
            // (it was consumed as the base struct type, not an interface). The CLR does
            // not support struct inheritance, so we must NOT emit `: BaseStruct`.
            var hasBaseStruct = symbol?.DeclaredType is StructType stWithBase && stWithBase.BaseType is not null;
            var implementorScope = (symbol?.DeclaredType as StructType)?.MemberScope;
            var baseParts = hasBaseStruct
                ? (implementorScope is not null
                    ? EmitBaseListSkippingFirstWithAssociatedTypeArgs(
                        structDecl.BaseList,
                        implementorScope,
                        null,
                        name,
                        typeParams)
                    : EmitBaseListSkippingFirst(structDecl.BaseList))
                : (implementorScope is not null
                    ? EmitBaseListWithAssociatedTypeArgs(
                        structDecl.BaseList,
                        implementorScope,
                        null,
                        name,
                        typeParams)
                    : EmitBaseListWithTypeArgs(structDecl.BaseList));

            // Append with-block conformance interfaces to the inheritance list.
            var withBlockInterfaces = CollectWithBlockInterfaceNames(symbol?.DeclaredType);
            if(withBlockInterfaces.Count > 0)
            {
                baseParts = baseParts.Length > 0
                    ? $"{baseParts}, {string.Join(", ", withBlockInterfaces)}"
                    : string.Join(", ", withBlockInterfaces);
            }

            if(baseParts.Length > 0)
            {
                abstractList = $" : {baseParts}";
            }
        }
        else if(symbol?.DeclaredType is StructType structType)
        {
            var parts = new List<string>();

            // Abstracts — append associated type slot concrete types as additional type arguments.
            foreach(var abs in structType.Abstracts)
            {
                parts.Add(EmitAbstractWithAssociatedTypeArgs(abs, structType.MemberScope, null, name, typeParams));
            }

            // Append with-block conformance interfaces to the inheritance list.
            var withBlockInterfaces = CollectWithBlockInterfaceNames(structType);
            parts.AddRange(withBlockInterfaces);

            if(parts.Count > 0)
            {
                abstractList = $" : {string.Join(", ", parts)}";
            }
        }
        // Emit user-declared attribute annotations from source.
        EmitAttributeLists(structDecl.Attributes);

        // Emit [DependentType(...)] attributes for each dependent type parameter.
        if(symbol?.DeclaredType is not null)
        {
            EmitDependentTypeAttributes(GetTypeParametersFromType(symbol.DeclaredType));
        }

        // Task 12.3 — Emit `readonly struct` for readonly struct types.
        var readonlyPrefix = "";
        if(symbol?.DeclaredType is StructType readonlyCheck && readonlyCheck.IsReadonly)
        {
            readonlyPrefix = "readonly ";
        }

        // Task 10.1 — Emit `ref struct` for ref struct types.
        var refPrefix = "";
        if(symbol?.DeclaredType is StructType refCheck && refCheck.IsRefStruct)
        {
            refPrefix = "ref ";
        }

        AppendLine($"{modifiers}{readonlyPrefix}{refPrefix}struct {name}{typeParams}{abstractList}");
        // Emit constraint clauses (where T : ...) for constrained type parameters.
        if(symbol?.DeclaredType is not null)
        {
            var typeParamsForConstraints = GetTypeParametersFromType(symbol.DeclaredType);
            EmitConstraintClauses(typeParamsForConstraints);
        }

        AppendLine("{");
        indentLevel++;

        // Emit dependent type properties (e.g., `public static int Size => TSize.Value;`)
        var hasDependentProperties = false;
        if(symbol?.DeclaredType is not null)
        {
            hasDependentProperties = EmitDependentTypeProperties(GetTypeParametersFromType(symbol.DeclaredType));
        }

        // Task 12.4 — Track that we're inside a struct so method emission can
        // emit the C# `readonly` modifier on struct methods.
        var previousInsideStruct = insideStruct;
        insideStruct = true;

        // Emit factory delegate fields and constructor for parameterized constructor constraints.
        var hasFactoryFields = false;
        if(symbol?.DeclaredType is not null)
        {
            var typeParamsForFactory = GetTypeParametersFromType(symbol.DeclaredType);
            if(HasParameterizedConstructorConstraints(typeParamsForFactory))
            {
                hasFactoryFields = true;
                EmitFactoryDelegateFields(typeParamsForFactory);

                // If the struct has user-defined constructors, merge factory params into them
                // instead of emitting a separate synthetic constructor.
                var hasUserConstructors = structDecl.Members.Any(m =>
                    m is ConstructorMemberMemberDeclarationNode);
                if(hasUserConstructors)
                {
                    currentClassFactoryTypeParams = typeParamsForFactory;
                }
                else
                {
                    EmitFactoryDelegateConstructor(name, typeParamsForFactory);
                }
            }
        }

        // Emit flattened ancestor fields (root-first order). Since the CLR does not
        // support struct inheritance, all ancestor fields are emitted directly in the
        // derived struct body. Root ancestor fields appear first, then immediate base
        // fields, then the struct's own declared fields.
        var hasAncestorFields = false;
        if(symbol?.DeclaredType is StructType derivedStruct && derivedStruct.BaseType is not null)
        {
            var ancestors = new List<StructType>(derivedStruct.GetAncestorChain());
            ancestors.Reverse(); // root-first order

            foreach(var ancestor in ancestors)
            {
                foreach(var field in ancestor.MemberScope.Members)
                {
                    if(field.Kind != SymbolKind.Field)
                    {
                        continue;
                    }

                    // Only emit fields that were declared directly on this ancestor
                    // (not inherited into its scope from its own ancestors).
                    if(!ReferenceEquals(field.ContainingScope, ancestor.MemberScope))
                    {
                        continue;
                    }

                    if(hasAncestorFields || hasFactoryFields || hasDependentProperties)
                    {
                        AppendLine();
                    }

                    EmitInheritedFieldDeclaration(field);
                    hasAncestorFields = true;
                }
            }
        }

        // Track dependent type parameter names declared on this struct.
        var previousTypeDependentParamNames = currentTypeDependentParamNames;
        if(symbol?.DeclaredType is not null)
        {
            var typeParamsForDep = GetTypeParametersFromType(symbol.DeclaredType);
            var depNames = new HashSet<string>(StringComparer.Ordinal);
            foreach(var tp in typeParamsForDep)
            {
                if(tp is DependentTypeParameterType depParam)
                {
                    _ = depNames.Add(depParam.Name);
                }
            }

            currentTypeDependentParamNames = depNames.Count > 0 ? depNames : null;
        }

        // Emit every member in declaration order. No name-based grouping or
        // deduplication is performed, so overloaded methods and constructors
        // are each emitted as separate C# declarations.
        for(var i = 0; i < structDecl.Members.Length; i++)
        {
            if(i > 0 || hasFactoryFields || hasDependentProperties || hasAncestorFields)
            {
                AppendLine();
            }

            EmitMemberDeclaration(structDecl.Members[i]);
        }

        // Clear class-level factory tracking after emitting all members.
        currentClassFactoryTypeParams = null;
        currentTypeDependentParamNames = previousTypeDependentParamNames;

        // Emit CLR boilerplate for IEnumerator<T> and IEnumerable<T> implementations.
        if(symbol?.DeclaredType is StructType structTypeForIterator)
        {
            EmitIteratorInterfaceBoilerplate(structTypeForIterator);
        }

        // Emit forwarding stubs for default interface methods the struct inherits but does
        // not override, so the defaults are callable directly on the struct (Rust-style).
        if(symbol?.DeclaredType is StructType structTypeForDefaults)
        {
            EmitDefaultMethodForwardingStubs(
                structTypeForDefaults.Abstracts,
                structDecl.Members,

                abstractParamSubs: null);
        }

        // Emit explicit interface implementations and forwarding stubs for with-block
        // conformances. For each with-block entry targeting this type, emit:
        // 1. Explicit interface implementations delegating to mapped source members.
        // 2. Forwarding stubs for default methods without mappings.
        if(symbol?.DeclaredType is not null)
        {
            EmitWithBlockConformanceMembers(symbol.DeclaredType, structDecl.Members);
        }

        // Emit __CreateDefault factory method for defaultable structs.
        // For readonly structs, fields cannot be assigned outside a constructor, so we emit
        // a private constructor with a sentinel parameter that sets the default block values.
        // __CreateDefault() calls this private constructor instead of assigning fields directly.
        // This keeps the user's parameterless constructor (if any) separate from the default block.
        if(symbol?.DeclaredType is StructType defaultableStruct
            && defaultableStruct.Defaultability is DefaultabilityStatus.Inferred or DefaultabilityStatus.Explicit
            && defaultableStruct.DefaultValues is not null)
        {
            var hasExistingMembers = structDecl.Members.Length > 0;

            if(defaultableStruct.IsReadonly)
            {
                var ownFields = defaultableStruct.DefaultValues
                    .Where(kvp => kvp.Value.ValueKind != DefaultValueKind.InheritedDefault)
                    .ToList();

                if(ownFields.Count > 0)
                {
                    // Emit a private constructor with a bool sentinel that sets default values.
                    // __CreateDefault() calls this constructor, not the user's parameterless one.
                    if(hasExistingMembers)
                    {
                        AppendLine();
                    }

                    AppendLine($"private {structDecl.Name.Lexeme}(bool __defaultSentinel)");
                    AppendLine("{");
                    indentLevel++;
                    foreach(var (fieldName, fieldValue) in ownFields)
                    {
                        EmitDefaultFieldAssignment("this", fieldName, fieldValue);
                    }

                    indentLevel--;
                    AppendLine("}");
                }

                // Emit __CreateDefault using the sentinel constructor.
                if(hasExistingMembers || ownFields.Count > 0)
                {
                    AppendLine();
                }

                AppendLine($"public static {defaultableStruct.DisplayName} __CreateDefault()");
                AppendLine("{");
                indentLevel++;
                if(ownFields.Count > 0)
                {
                    AppendLine($"var inst = new {defaultableStruct.DisplayName}(true);");
                }
                else
                {
                    AppendLine($"var inst = new {defaultableStruct.DisplayName}();");
                }

                AppendLine("return inst;");
                indentLevel--;
                AppendLine("}");
            }
            else
            {
                // Non-readonly structs: use the original pattern (direct field assignment).
                EmitCreateDefaultMethod(defaultableStruct.DisplayName, defaultableStruct.DefaultValues, hasExistingMembers);
            }
        }

        // Emit implicit conversion operators for struct inheritance.
        // Each operator converts the derived struct to an ancestor by copying fields.
        if(symbol?.DeclaredType is StructType derivedStructForConversions
            && derivedStructForConversions.BaseType is not null)
        {
            EmitStructImplicitConversions(derivedStructForConversions, name);
        }

        insideStruct = previousInsideStruct;
        indentLevel--;
        AppendLine("}");
    }

    /// <summary>
    /// Emits implicit conversion operators from a derived struct to each of its
    /// ancestor structs. Each operator constructs the ancestor by copying all
    /// fields that belong to that ancestor (including fields the ancestor itself
    /// inherited).
    /// </summary>
    private void EmitStructImplicitConversions(StructType derivedStruct, string derivedName)
    {
        var current = derivedStruct.BaseType as StructType;
        while(current is not null)
        {
            var baseName = CSharpTypeMap.GetTypeName(current);
            AppendLine();
            AppendLine($"public static implicit operator {baseName}({derivedName} value)");
            AppendLine("{");
            indentLevel++;
            AppendLine($"return new {baseName}");
            AppendLine("{");
            indentLevel++;
            foreach(var field in GetAllFieldsForStruct(current))
            {
                AppendLine($"{field.Name} = value.{field.Name},");
            }

            indentLevel--;
            AppendLine("};");
            indentLevel--;
            AppendLine("}");

            current = current.BaseType as StructType;
        }
    }

    /// <summary>
    /// Collects all field symbols for a struct type, including fields inherited
    /// from its ancestors. Uses the ancestor's <see cref="StructType.GetAncestorChain"/>
    /// to walk up the chain and collect fields from each level, ensuring all fields
    /// are included regardless of visibility (since the emitted C# struct has all
    /// fields flattened as public).
    /// </summary>
    private static IReadOnlyList<ISymbol> GetAllFieldsForStruct(StructType structType)
    {
        var fields = new List<ISymbol>();
        var seen = new HashSet<string>();

        // Collect ancestor fields in root-first order.
        var ancestors = structType.GetAncestorChain();
        for(var i = ancestors.Count - 1; i >= 0; i--)
        {
            foreach(var member in ancestors[i].MemberScope.Members)
            {
                if(member.Kind == SymbolKind.Field && seen.Add(member.Name))
                {
                    fields.Add(member);
                }
            }
        }

        // Add the struct's own declared fields.
        foreach(var member in structType.MemberScope.Members)
        {
            if(member.Kind == SymbolKind.Field && seen.Add(member.Name))
            {
                fields.Add(member);
            }
        }

        return fields;
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
        if(hasExistingMembers)
        {
            AppendLine();
        }

        AppendLine($"public static {typeName} __CreateDefault()");
        AppendLine("{");
        indentLevel++;

        AppendLine($"var inst = new {typeName}();");

        foreach(var (fieldName, fieldValue) in defaultValues)
        {
            // Skip inherited fields — they are initialized by the base class's
            // parameterless constructor via the constructor chain (base() call).
            if(fieldValue.ValueKind == DefaultValueKind.InheritedDefault)
            {
                continue;
            }

            // If the emitted parameterless constructor already sets own fields,
            // skip them here (the constructor chain handles everything).
            if(skipOwnFields)
            {
                continue;
            }

            EmitDefaultFieldAssignment("inst", fieldName, fieldValue);
        }

        AppendLine("return inst;");

        indentLevel--;
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
        switch(fieldValue.ValueKind)
        {
            case DefaultValueKind.TypeBinding:
                {
                    // Abstract type field bound to a concrete type.
                    var concreteTypeName = CSharpTypeMap.GetTypeName(fieldValue.ConcreteType!);

                    if(fieldValue.NestedOverrides is { Count: > 0 })
                    {
                        // Type binding with nested overrides — use a temp variable.
                        var tempName = $"__temp_{fieldName}";
                        AppendLine($"var {tempName} = {concreteTypeName}.__CreateDefault();");
                        foreach(var (nestedFieldName, nestedValue) in fieldValue.NestedOverrides)
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
        if(fieldName.StartsWith('_') && fieldName.EndsWith(suffix, StringComparison.Ordinal))
        {
            return fieldName.Substring(1, fieldName.Length - 1 - suffix.Length);
        }

        return fieldName;
    }

    /// <summary>
    /// Escapes a character for use in a C# character literal.
    /// </summary>
    private static string EscapeChar(char c)
    {
        return c switch
        {
            '\0' => "\\0",
            '\'' => "\\'",
            '\\' => "\\\\",
            '\n' => "\\n",
            '\r' => "\\r",
            '\t' => "\\t",
            _ => c.ToString(),
        };
    }

    /// <summary>
    /// Escapes a string for use in a C# string literal.
    /// </summary>
    private static string EscapeString(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach(var c in s)
        {
            _ = sb.Append(c switch
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
        for(var i = 0; i < raw.Length; i++)
        {
            if(raw[i] == '\\' && i + 1 < raw.Length)
            {
                var next = raw[i + 1];
                if(next == '`')
                {
                    _ = pattern.Append('`');
                    i++;
                }
                else if(next == '\\')
                {
                    _ = pattern.Append('\\');
                    i++;
                }
                else
                {
                    // Not a Mist escape — preserve the backslash and next char verbatim.
                    _ = pattern.Append(raw[i]);
                }
            }
            else
            {
                _ = pattern.Append(raw[i]);
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
        if(IsStorageBannedMember(memberDecl))
        {
            return;
        }

        switch(memberDecl)
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
            case NestedBodyTypeMemberMemberDeclarationNode nestedBodyMember:
                switch(nestedBodyMember.NestedBody.Keyword)
                {
                    case ClassKeywordTypeKeywordNode:
                        EmitClassDeclaration(nestedBodyMember.NestedBody);
                        break;
                    case StructKeywordTypeKeywordNode:
                        EmitStructDeclaration(nestedBodyMember.NestedBody);
                        break;
                    case AbstractKeywordTypeKeywordNode:
                        EmitAbstractDeclaration(nestedBodyMember.NestedBody);
                        break;
                }

                break;
            case NestedEnumMemberMemberDeclarationNode nestedEnumMember:
                EmitEnumDeclaration(nestedEnumMember.NestedEnum);
                break;
            case NestedNewtypeMemberMemberDeclarationNode:
            case NestedTypeAliasMemberMemberDeclarationNode:
                // Newtypes and type aliases are erased at the C# level.
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
        var declaredType = member switch
        {
            FieldMemberMemberDeclarationNode fm =>
                model.GetDeclaredSymbol(fm.Field)?.DeclaredType,
            PropertyMemberMemberDeclarationNode pm =>
                model.GetDeclaredSymbol(pm.Property)?.DeclaredType,
            IndexerMemberMemberDeclarationNode im =>
                model.GetDeclaredSymbol(im.Indexer)?.DeclaredType,
            _ => null,
        };

        return declaredType is not null
            && TypeContainment.IsStorageBanned(declaredType);
    }

    private void EmitFieldDeclaration(FieldDeclarationNode fieldDecl)
    {
        EmitAttributeLists(fieldDecl.Attributes);
        var modifiers = EmitModifiers(fieldDecl.Modifiers);

        // Struct fields default to public when no explicit modifier is specified.
        if(insideStruct && modifiers.Length == 0)
        {
            modifiers = "public ";
        }

        var type = EmitTypeReference(fieldDecl.Type, isValueContext: true);
        var name = fieldDecl.Name.Lexeme;

        // Task 12.1 — Emit C# `readonly` modifier on fields with ReadonlyStatus != Mutable.
        var readonlyPrefix = "";
        var fieldSymbol = model.GetDeclaredSymbol(fieldDecl) as Symbol;
        if(fieldSymbol is not null && fieldSymbol.ReadonlyStatus is not ReadonlyStatus.Mutable and not ReadonlyStatus.Unknown)
        {
            readonlyPrefix = "readonly ";
        }

        if(fieldDecl.Initializer != null)
        {
            // Task 14.2: When the field type is a two-constituent T | void union,
            // emit Void.Void as null instead of MistVoid.Instance.
            var previousVoidAsNull = emitVoidAsNull;
            if(IsOptionalUnionType(fieldSymbol?.DeclaredType))
            {
                emitVoidAsNull = true;
            }

            // The field has an initializer expression.
            // With the bugfix, the Initializer property is now ExpressionNode? directly.
            var fieldInit = EmitExpression(fieldDecl.Initializer);
            emitVoidAsNull = previousVoidAsNull;
            AppendLine($"{modifiers}{readonlyPrefix}{type} {name} = {fieldInit};");
        }
        else
        {
            AppendLine($"{modifiers}{readonlyPrefix}{type} {name};");
        }
    }

    /// <summary>
    /// Emits a field declaration from a semantic symbol (used for flattened ancestor fields
    /// in struct inheritance). Preserves the original field name, type, and access modifier.
    /// </summary>
    private void EmitInheritedFieldDeclaration(ISymbol field)
    {
        var fieldType = field.DeclaredType;
        var typeName = fieldType is not null ? CSharpTypeMap.GetTypeName(fieldType, isValueContext: true) : "object";
        var fieldName = field.Name;

        // Determine access modifier from the declaring AST node.
        var accessModifier = "";
        var declaringNode = model.GetDeclaringNode(field);
        if(declaringNode is FieldDeclarationNode fieldDecl)
        {
            accessModifier = EmitModifiers(fieldDecl.Modifiers);
        }

        // Emit readonly modifier if the field is readonly.
        var readonlyPrefix = "";
        if(field is Symbol sym && sym.ReadonlyStatus is not ReadonlyStatus.Mutable and not ReadonlyStatus.Unknown)
        {
            readonlyPrefix = "readonly ";
        }

        AppendLine($"{accessModifier}{readonlyPrefix}{typeName} {fieldName};");
    }

    private void EmitCastOperatorDeclaration(CastOperatorDeclarationNode castOpDecl)
    {
        var modifiers = EmitModifiers(castOpDecl.Modifiers);
        var castKind = castOpDecl.CastKind is ImplicitCastCastOperatorKindNode
            ? "implicit" : "explicit";
        var targetType = EmitTypeReference(castOpDecl.TargetType);
        var parameters = EmitParameterList(castOpDecl.Parameters);

        AppendLine($"{modifiers}{castKind} operator {targetType}({parameters})");

        if(castOpDecl.Body is CallableBlockBodyCallableBodyNode blockBody)
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
        EmitAttributeLists(ctorDecl.Attributes);
        var modifiers = EmitModifiers(ctorDecl.Modifiers);

        // Constructors without explicit modifiers default to public in Mist
        // (no constructor access restriction is enforced by the type checker).
        if(modifiers.Length == 0)
        {
            modifiers = "public ";
        }

        var name = ctorDecl.Name.Lexeme;
        var parameters = EmitParameterList(ctorDecl.Parameters);

        // Append factory delegate parameters when the enclosing type has parameterized
        // constructor constraints and user-defined constructors (merged approach).
        if(currentClassFactoryTypeParams is not null)
        {
            var factoryParams = GetFactoryDelegateMethodParams(currentClassFactoryTypeParams);
            if(factoryParams.Length > 0)
            {
                parameters = parameters.Length > 0
                    ? $"{parameters}, {factoryParams}"
                    : factoryParams;
            }
        }

        // Collect inlined base field assignments for struct constructors with `: base(...)`.
        // C# structs cannot use `: base(...)` syntax, so we suppress the chain and inline
        // the base constructor's field assignments at the start of the constructor body.
        List<string>? inlinedBaseAssignments = null;
        var chain = "";
        if(ctorDecl.Chain is ThisChainConstructorChainNode thisChain)
        {
            chain = " : this(" + EmitArgumentList(thisChain.Arguments) + ")";
        }
        else if(ctorDecl.Chain is BaseChainConstructorChainNode baseChain)
        {
            if(insideStruct)
            {
                // Struct constructors cannot emit `: base(...)` — inline the base
                // constructor's field assignments instead (Requirement 7.5).
                inlinedBaseAssignments = CollectInlinedBaseConstructorAssignments(ctorDecl, baseChain);
            }
            else
            {
                chain = " : base(" + EmitArgumentList(baseChain.Arguments) + ")";
            }
        }

        AppendLine($"{modifiers}{name}({parameters}){chain}");
        if(ctorDecl.Body.GetBlock() is BlockNode block)
        {
            // Resolve with-clauses for the constructor.
            var ctorSymbol = model.GetDeclaredSymbol(ctorDecl) as Symbol;
            var withClauses = ctorSymbol?.WithClauses;

            // Collect refinement guards for constructor parameters.
            var refinementGuards = CollectRefinementParameterGuards(ctorDecl.Parameters);

            if(currentClassFactoryTypeParams is not null || refinementGuards.Count > 0
                || withClauses is { Count: > 0 } || inlinedBaseAssignments is { Count: > 0 })
            {
                AppendLine("{");
                indentLevel++;

                // Emit inlined base constructor field assignments first (struct inheritance).
                if(inlinedBaseAssignments is { Count: > 0 })
                {
                    foreach(var assignment in inlinedBaseAssignments)
                    {
                        AppendLine(assignment);
                    }
                }

                // Emit with-clause peek bindings at constructor entry.
                EmitWithClausePeekBindings(withClauses);

                // Emit refinement guards (before factory field assignments).
                EmitRefinementGuards(refinementGuards);

                // Emit factory field assignments at the start of the constructor body.
                if(currentClassFactoryTypeParams is not null)
                {
                    EmitFactoryFieldAssignments(currentClassFactoryTypeParams);
                }

                EmitBlockStatements(block);
                indentLevel--;
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

    /// <summary>
    /// Collects the inlined field assignments for a struct constructor with a <c>: base(args)</c>
    /// chain. Since C# structs cannot use <c>: base(...)</c>, we find the base constructor's
    /// parameter-to-field mappings and emit <c>this.FieldName = argExpr;</c> for each one.
    /// </summary>
    private List<string> CollectInlinedBaseConstructorAssignments(
        ConstructorDeclarationNode ctorDecl,
        BaseChainConstructorChainNode baseChain)
    {
        var assignments = new List<string>();

        // Find the enclosing struct type.
        var ctorSymbol = model.GetDeclaredSymbol(ctorDecl);
        if(ctorSymbol is null)
        {
            return assignments;
        }

        var enclosingType = FindEnclosingStructType(ctorSymbol);
        if(enclosingType is null || enclosingType.BaseType is not StructType baseStruct)
        {
            return assignments;
        }

        // Find constructors on the base struct.
        var baseConstructors = baseStruct.MemberScope.Members
            .Where(m => m.Kind == SymbolKind.Constructor)
            .ToList();

        if(baseConstructors.Count == 0)
        {
            return assignments;
        }

        // Collect the base chain argument expressions.
        var baseArgExprs = baseChain.Arguments?.GetAllExpressions()
            ?? ImmutableArray<ExpressionNode>.Empty;

        // Find the matching base constructor by parameter count.
        ISymbol? matchingCtor = null;
        foreach(var baseCtor in baseConstructors)
        {
            Storm.Runtime.SyntaxNode? baseCtorNode;
            try { baseCtorNode = model.GetDeclaringNode(baseCtor); }
            catch { continue; }

            if(baseCtorNode is not ConstructorDeclarationNode baseCtorDecl)
            {
                continue;
            }

            var paramCount = CountParameters(baseCtorDecl.Parameters);
            if(paramCount == baseArgExprs.Length)
            {
                matchingCtor = baseCtor;
                break;
            }
        }

        if(matchingCtor is null)
        {
            return assignments;
        }

        // Get the base constructor's declaring node.
        Storm.Runtime.SyntaxNode? matchingCtorNode;
        try { matchingCtorNode = model.GetDeclaringNode(matchingCtor); }
        catch { return assignments; }

        if(matchingCtorNode is not ConstructorDeclarationNode matchingCtorDecl)
        {
            return assignments;
        }

        // Build a map from parameter name → argument index.
        var paramNames = CollectParameterNames(matchingCtorDecl.Parameters);

        // Extract field assignments from the base constructor body.
        // Each statement of the form `FieldName = paramName;` maps to
        // `this.FieldName = emittedArg;` where emittedArg is the corresponding
        // argument from the derived constructor's `: base(args)`.
        var baseBody = matchingCtorDecl.Body.GetBlock();
        if(baseBody is null)
        {
            return assignments;
        }

        foreach(var stmt in baseBody.Statements)
        {
            if(stmt is not ExpressionStatementStatementNode exprStmt)
            {
                continue;
            }

            var expr = exprStmt.ExpressionStatement.Expression;
            if(expr.AsAssignmentOrNull() is not { } assignment)
            {
                continue; // Lambda-form expression statement — not a field initializer.
            }

            // Check if this is a simple assignment (has a Value part).
            if(assignment.Value is null)
            {
                continue;
            }

            // The target should be a simple identifier (field name).
            var targetText = GetSimpleIdentifierFromAssignmentTarget(assignment.Target.Left);
            if(targetText is null)
            {
                continue;
            }

            // The value should be a simple identifier matching a parameter name.
            // The RHS is now a full ExpressionNode; only the assignment form can be a
            // simple identifier reference.
            if(assignment.Value.AsAssignmentOrNull() is not { } valueAssignment)
            {
                continue;
            }

            var valueText = GetSimpleIdentifierFromAssignmentTarget(valueAssignment.Target.Left);
            if(valueText is null)
            {
                continue;
            }

            // Find the parameter index.
            var paramIndex = paramNames.IndexOf(valueText);
            if(paramIndex < 0 || paramIndex >= baseArgExprs.Length)
            {
                continue;
            }

            // Emit the inlined assignment using the derived constructor's argument.
            var emittedArg = EmitExpression(baseArgExprs[paramIndex]);
            assignments.Add($"this.{targetText} = {emittedArg};");
        }

        return assignments;
    }

    /// <summary>
    /// Finds the enclosing <see cref="StructType"/> for a constructor symbol by walking
    /// up the scope chain.
    /// </summary>
    private static StructType? FindEnclosingStructType(ISymbol ctorSymbol)
    {
        var typeScope = ctorSymbol.ContainingScope;
        var parentScope = typeScope?.Parent;
        if(parentScope is null)
        {
            return null;
        }

        foreach(var member in parentScope.Members)
        {
            if(member.Kind == SymbolKind.Struct && member.DeclaredType is StructType st
                && ReferenceEquals(st.MemberScope, typeScope))
            {
                return st;
            }
        }

        return null;
    }

    /// <summary>
    /// Counts the number of parameters in a parameter list node.
    /// </summary>
    private static int CountParameters(ParameterListNode? paramList)
    {
        if(paramList is null)
        {
            return 0;
        }

        return 1 + paramList.Parameters.Length;
    }

    /// <summary>
    /// Collects parameter names from a parameter list node in order.
    /// </summary>
    private static List<string> CollectParameterNames(ParameterListNode? paramList)
    {
        var names = new List<string>();
        if(paramList is null)
        {
            return names;
        }

        names.Add(paramList.Parameter.Name.Lexeme);
        foreach(var param in paramList.Parameters)
        {
            names.Add(param.Name.Lexeme);
        }

        return names;
    }

    /// <summary>
    /// Attempts to extract a simple identifier name from an assignment target (LogicalOrNode).
    /// Returns the identifier text if the target is a simple name reference, or null otherwise.
    /// </summary>
    private static string? GetSimpleIdentifierFromAssignmentTarget(LogicalOrNode target)
    {
        // Walk down: LogicalOr → LogicalAnd → Equality → Relational → Additive →
        // Multiplicative → Unary → Postfix → Primary
        if(target.Right is not null && !ReferenceEquals(target.Right, target.Left))
        {
            return null;
        }

        var logicalAnd = target.Left;
        if(logicalAnd.Right is not null && !ReferenceEquals(logicalAnd.Right, logicalAnd.Left))
        {
            return null;
        }

        var equality = logicalAnd.Left;
        if(equality.Right is not null && !ReferenceEquals(equality.Right, equality.Left))
        {
            return null;
        }

        var relational = equality.Left;
        if(relational is not RelationalComparisonRelationalNode cmp)
        {
            return null;
        }

        if(cmp.Right is not null && !ReferenceEquals(cmp.Right, cmp.Left))
        {
            return null;
        }

        var additive = cmp.Left;
        if(additive.Right is not null && !ReferenceEquals(additive.Right, additive.Left))
        {
            return null;
        }

        var multiplicative = additive.Left;
        if(multiplicative.Right is not null && !ReferenceEquals(multiplicative.Right, multiplicative.Left))
        {
            return null;
        }

        var unary = multiplicative.Left;
        if(unary is not PostfixExpressionUnaryNode postfixUnary)
        {
            return null;
        }

        var postfix = postfixUnary.Primary;
        if(postfix.Suffix.Length > 0)
        {
            return null;
        }

        var primary = postfix.Target;
        if(primary is not IdentifierExpressionPrimaryNode idPrimary)
        {
            return null;
        }

        return idPrimary.Name.Lexeme;
    }

    private void EmitMethodDeclaration(MethodDeclarationNode methodDecl)
    {
        EmitAttributeLists(methodDecl.Attributes);
        var modifiers = EmitModifiers(methodDecl.Modifiers);
        var name = methodDecl.Name.Lexeme;
        var typeParams = EmitTypeParameterList(methodDecl.TypeParams);

        // ── Implicit override detection for CLR base class virtual/abstract methods ──
        // When a Mist class inherits from a CLR base and defines a method with the same
        // name as a virtual/abstract method on that base, automatically emit `override`
        // with the base method's access modifier. This is purely a C# backend concern —
        // Mist source has no override keyword.
        var implicitOverridePrefix = "";
        if(!insideStruct && currentClassType?.BaseType is not null)
        {
            var baseOverrideSlot = FindBaseOverrideSlot(name);
            if(baseOverrideSlot is not null)
            {
                var clrAccess = baseOverrideSlot.ClrAccessModifier ?? "public";

                // Validate access level compatibility:
                // - Base is protected/protected internal → Mist private (or unspecified) can match
                // - Base is public → Mist public can match
                var mistIsPublic = methodDecl.Modifiers.Any(m => m is PublicModifierModifierNode);
                var baseIsPublic = clrAccess == "public";

                if(baseIsPublic && mistIsPublic)
                {
                    implicitOverridePrefix = "public override ";
                    modifiers = ""; // Replace Mist modifiers entirely
                }
                else if(!baseIsPublic && !mistIsPublic)
                {
                    implicitOverridePrefix = $"{clrAccess} override ";
                    modifiers = ""; // Replace Mist modifiers entirely
                }
                // If Mist declares public but base is protected, don't override —
                // we won't expose something as public that was protected in C#.
                // The method just becomes a new method (no override).

                if(implicitOverridePrefix.Length > 0)
                {
                    // Emit informational diagnostic: "hey, we automatically made this link"
                    diagnostics.Report(
                        BackendDiagnostics.ImplicitOverrideInfo,
                        default,
                        sourceText.Path,
                        "ImplicitOverride",
                        name,
                        $"{currentClassType.BaseType.DisplayName}.{name}",
                        clrAccess);
                }
            }
        }

        // Task 12.4 — Emit C# `readonly` modifier on struct methods with ReadonlyStatus != Mutable.
        // C# has no `readonly` method concept for classes, so only emit for structs.
        var readonlyPrefix = "";
        var methodSymbol = model.GetDeclaredSymbol(methodDecl) as Symbol;
        if(insideStruct && methodSymbol is not null
            && methodSymbol.ReadonlyStatus is not ReadonlyStatus.Mutable and not ReadonlyStatus.Unknown)
        {
            readonlyPrefix = "readonly ";
        }

        // Emit ref / ref readonly prefix for ref-returning methods.
        var refReturnPrefix = methodSymbol?.ReturnRefKind switch
        {
            RefKind.Ref => "ref ",
            RefKind.RefReadonly => "ref readonly ",
            _ => ""
        };

        // ── Associated type generic lifting for generic functions (Task 7.3) ──
        // When a method's type parameters are constrained by abstract types with
        // associated type slots, synthesize additional type parameters for each slot
        // and set up substitutions so T.SlotName references resolve to the synthesized name.
        // This must be set up BEFORE emitting return type and parameters so that
        // T.SlotName references in those positions are correctly substituted.
        var methodTypeParams = methodSymbol?.TypeParameters ?? Array.Empty<IType>();
        var methodAssociatedSlots = CollectAssociatedTypeSlotsFromMethodTypeParams(methodTypeParams);
        var previousSlotSubstitutions = currentMethodSlotSubstitutions;
        if(methodAssociatedSlots.Count > 0)
        {
            var synthesizedParams = string.Join(", ", methodAssociatedSlots.Select(s => $"T{s.Slot.DisplayName}"));
            typeParams = typeParams.Length > 0
                ? $"{typeParams[..^1]}, {synthesizedParams}>"
                : $"<{synthesizedParams}>";

            // Build substitutions for T.SlotName → TSlotName in type references.
            var slotSubs = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach(var entry in methodAssociatedSlots)
            {
                // Map "T.SlotName" → "TSlotName" for each type parameter and its slots.
                slotSubs[$"{entry.TypeParamName}.{entry.Slot.DisplayName}"] = $"T{entry.Slot.DisplayName}";
                // Also map bare slot name for substitution in type references.
                slotSubs[entry.Slot.DisplayName] = $"T{entry.Slot.DisplayName}";
            }

            currentMethodSlotSubstitutions = slotSubs;
        }

        // Emit return type and parameters AFTER setting up slot substitutions
        // so that T.SlotName references are correctly resolved.
        var returnType = EmitTypeReference(methodDecl.ReturnType);
        var parameters = EmitParameterList(methodDecl.Parameters);

        // ── Async method signature emission (Task 12.1) ──
        // When NeedsCSharpAsync is true, emit the `async` keyword and adjust
        // the return type to Task<T> (or non-generic Task for void).
        var asyncPrefix = "";
        if(methodSymbol is { NeedsCSharpAsync: true })
        {
            asyncPrefix = "async ";

            if(methodSymbol.IsAsync)
            {
                // Method declared async in Mist: EffectiveReturnType is already Task<T>.
                // Use the usage analysis result to choose ValueTask<T> vs Task<T>.
                returnType = EmitAsyncReturnType(methodSymbol);
            }
            else
            {
                // Sync method that needs C# async due to propagation:
                // Wrap the declared return type in Task<T>.
                returnType = methodDecl.ReturnType.First is VoidSingleTypeRefSingleTypeRefNode
                    && methodDecl.ReturnType.SingleTypeRefs.Length == 0
                    ? "System.Threading.Tasks.Task"
                    : $"System.Threading.Tasks.Task<{returnType}>";
            }
        }

        // Context-sensitive void return type: if this void method's return value
        // is consumed at any call site, emit MistVoid instead of void.
        // (Only applies when not already wrapped in Task by async emission above.)
        var isValueReturningVoid = methodDecl.ReturnType.First is VoidSingleTypeRefSingleTypeRefNode
            && methodDecl.ReturnType.SingleTypeRefs.Length == 0
            && valueReturningVoidMethods.Contains(name)
            && asyncPrefix.Length == 0;
        if(isValueReturningVoid)
        {
            returnType = "MistVoid";
            NeedsMistVoidStruct = true;
        }

        // For generic methods with parameterized constructor constraints,
        // append factory delegate parameters to the method signature.
        var factoryParams = GetFactoryDelegateMethodParams(methodTypeParams);
        if(factoryParams.Length > 0)
        {
            parameters = parameters.Length > 0
                ? $"{parameters}, {factoryParams}"
                : factoryParams;
        }

        AppendLine($"{(implicitOverridePrefix.Length > 0 ? implicitOverridePrefix : modifiers)}{asyncPrefix}{readonlyPrefix}{refReturnPrefix}{returnType} {name}{typeParams}({parameters})");
        // Emit constraint clauses for the method's type parameters.
        // When associated type slots are present, interface constraints need to include
        // the synthesized type parameters as additional type arguments.
        if(methodAssociatedSlots.Count > 0)
        {
            EmitConstraintClausesWithAssociatedTypes(methodTypeParams, methodAssociatedSlots);
            var slotsOnly = methodAssociatedSlots.Select(s => s.Slot).ToList();
            EmitAssociatedTypeSlotConstraints(slotsOnly);
        }
        else
        {
            EmitConstraintClauses(methodTypeParams);
        }

        // Set the flag so EmitReturnStatement knows to emit `return MistVoid.Instance;`
        var previousFlag = inValueReturningVoidMethod;
        inValueReturningVoidMethod = isValueReturningVoid;

        // Task 31.1 — track the enclosing method's return type so return
        // statements can detect covariant unknown widening against it.
        var previousReturnType = currentMethodReturnType;
        currentMethodReturnType = ResolveMethodReturnType(methodDecl);

        // Track method-level factory type parameters for new T(args) emission.
        var previousFactoryTypeParams = currentMethodFactoryTypeParams;
        currentMethodFactoryTypeParams = HasParameterizedConstructorConstraints(methodTypeParams)
            ? methodTypeParams
            : null;

        // Track the enclosing method symbol for auto-unwrap emission.
        var previousMethodSymbol = currentMethodSymbol;
        currentMethodSymbol = methodSymbol;

        if(methodDecl.Body.GetBlock() is BlockNode block)
        {
            var withClauses = methodSymbol?.WithClauses;
            EmitMethodBlockWithRefinementGuards(block, isValueReturningVoid, methodDecl.Parameters, withClauses);
        }
        else
        {
            // No-body form (;) — grammar permits this for intrinsic members.
            AppendLine(";");
        }

        inValueReturningVoidMethod = previousFlag;
        currentMethodReturnType = previousReturnType;
        currentMethodFactoryTypeParams = previousFactoryTypeParams;
        currentMethodSymbol = previousMethodSymbol;
        currentMethodSlotSubstitutions = previousSlotSubstitutions;
    }

    /// <summary>
    /// Emits a method body block with optional refinement type parameter guards.
    /// For each parameter whose resolved type is a <see cref="RefinementType"/>,
    /// emits a runtime validation guard at method entry that throws
    /// <see cref="ArgumentException"/> if the value fails the predicate.
    /// </summary>
    private void EmitMethodBlockWithRefinementGuards(BlockNode block, bool appendImplicitReturn, ParameterListNode? paramList, IReadOnlyList<ContextClauseInfo>? withClauses = null)
    {
        // Collect refinement-typed parameters and adhoc refinement constraints.
        var refinementGuards = CollectRefinementParameterGuards(paramList);

        if(refinementGuards.Count == 0)
        {
            // No refinement guards needed — emit the block normally.
            EmitMethodBlock(block, appendImplicitReturn, withClauses);
            return;
        }

        // Emit the block with guards injected at the top.
        AppendLine("{");
        indentLevel++;

        // Emit with-clause peek bindings at method entry, before refinement guards.
        EmitWithClausePeekBindings(withClauses);

        EmitRefinementGuards(refinementGuards);

        EmitStatementsFromIndex(block.Statements, 0);

        if(appendImplicitReturn && !BlockEndsWithReturn(block))
        {
            // Task 14.2: If the return type is an optional union, emit null instead.
            if(IsOptionalUnionType(currentMethodReturnType))
            {
                AppendLine("return null;");
            }
            else
            {
                AppendLine("return MistVoid.Instance;");
            }
        }

        indentLevel--;
        AppendLine("}");
    }

    /// <summary>
    /// Emits runtime validation guards for the given list of refinement guard entries.
    /// Each guard emits <c>if (!(condition)) throw new ArgumentException(...);</c>.
    /// </summary>
    private void EmitRefinementGuards(List<(string Name, RefinementPredicate Predicate, string TypeName)> guards)
    {
        foreach(var (paramName, predicate, typeName) in guards)
        {
            var condition = EmitRefinementPredicateCondition(predicate, paramName);
            AppendLine($"if (!({condition}))");
            indentLevel++;
            AppendLine($"throw new ArgumentException($\"Value {{{paramName}}} does not satisfy refinement '{predicate.Description}' on type '{typeName}'\", nameof({paramName}));");
            indentLevel--;
        }
    }

    /// <summary>
    /// Collects parameters from the given parameter list that require runtime refinement guards.
    /// This includes both parameters whose declared type is a <see cref="RefinementType"/> (named
    /// refinement types) and parameters with adhoc refinement constraints from <c>where</c> clauses.
    /// Returns guards in left-to-right parameter declaration order.
    /// </summary>
    private List<(string Name, RefinementPredicate Predicate, string TypeName)> CollectRefinementParameterGuards(ParameterListNode? paramList)
    {
        var guards = new List<(string Name, RefinementPredicate Predicate, string TypeName)>();
        if(paramList is null)
        {
            return guards;
        }

        foreach(var child in paramList.Children())
        {
            if(child is ParameterNode param)
            {
                var symbol = model.GetDeclaredSymbol(param);
                if(symbol is null)
                {
                    continue;
                }

                // Check for named refinement type parameter.
                if(symbol.DeclaredType is RefinementType refinementType)
                {
                    guards.Add((param.Name.Lexeme, refinementType.Predicate, refinementType.Name));
                }
                // Check for adhoc refinement constraint from where clause.
                // Adhoc refinement constraints are enforced entirely at compile time
                // (same as named refinement types), so no runtime guard is emitted.
                else
                {
                    // No guard needed — compile-time enforcement handles this.
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
        indentLevel++;

        // Emit with-clause peek bindings at method entry.
        EmitWithClausePeekBindings(withClauses);

        // Collect linear/relevant parameters that need auto-drop at method exit.
        // Only linear/relevant owned parameters that are not returned or moved within
        // the body should be dropped (Requirement 9.6).
        var paramsToAutoDropAtExit = CollectAutoDropParameters(block);

        if(paramsToAutoDropAtExit.Count > 0)
        {
            // Wrap body in try/finally for exception safety (Requirement 14.3).
            AppendLine("try");
            AppendLine("{");
            indentLevel++;
        }

        EmitStatementsFromIndex(block.Statements, 0);

        // Task 7.4 — Manual Drop() body completion (Requirements 10.7, 10.8):
        // When this method is a manual Drop() on a type with droppable fields,
        // emit Drop() for any fields the programmer did not explicitly drop
        // in the body, in declaration order.
        EmitRemainingFieldDropsIfManualDrop(block);

        // For value-returning void methods, ensure the method body ends with a return.
        // If the last statement is not a return, append `return MistVoid.Instance;`.
        if(appendImplicitReturn && !BlockEndsWithReturn(block))
        {
            // Task 14.2: If the return type is an optional union, emit null instead.
            if(IsOptionalUnionType(currentMethodReturnType))
            {
                AppendLine("return null;");
            }
            else
            {
                AppendLine("return MistVoid.Instance;");
            }
        }

        if(paramsToAutoDropAtExit.Count > 0)
        {
            indentLevel--;
            AppendLine("}");
            AppendLine("finally");
            AppendLine("{");
            indentLevel++;
            // Drop parameters in reverse declaration order (matching local variable behavior).
            for(var i = paramsToAutoDropAtExit.Count - 1; i >= 0; i--)
            {
                var (paramName, isTaskLike) = paramsToAutoDropAtExit[i];
                if(isTaskLike)
                {
                    AppendLine($"await {paramName};");
                }
                else
                {
                    AppendLine($"{paramName}.Drop();");
                }
            }

            indentLevel--;
            AppendLine("}");
        }

        indentLevel--;
        AppendLine("}");
    }

    /// <summary>
    /// Collects parameters of the current method that have a linear or relevant type
    /// with a <c>Drop()</c> method, making them eligible for auto-drop at method exit.
    /// Only includes parameters that are NOT returned or moved within the method body
    /// (Requirement 9.6: "When a function receives a linear/affine parameter, THE Compiler
    /// SHALL insert Drop() at end of function if the parameter was not moved or returned").
    /// </summary>
    private List<(string Name, bool IsTaskLike)> CollectAutoDropParameters(BlockNode block)
    {
        var result = new List<(string Name, bool IsTaskLike)>();

        if(currentMethodSymbol is null)
        {
            return result;
        }

        // Skip Drop() methods themselves — they shouldn't auto-drop their implicit 'self'.
        if(currentMethodSymbol.Name == "Drop")
        {
            return result;
        }

        // Get the declaring node to access the parameter list.
        Storm.Runtime.SyntaxNode declaringNode;
        try { declaringNode = model.GetDeclaringNode(currentMethodSymbol); }
        catch { return result; }

        var paramList = declaringNode switch
        {
            MethodDeclarationNode m => m.Parameters,
            _ => null,
        };

        if(paramList is null)
        {
            return result;
        }

        foreach(var child in paramList.Children())
        {
            if(child is not ParameterNode paramNode)
            {
                continue;
            }

            if(model.GetDeclaredSymbol(paramNode) is not Symbol paramSymbol)
            {
                continue;
            }

            // Only by-value parameters can be auto-dropped.
            // ref/readonly ref parameters are borrows — the caller retains drop responsibility.
            if(paramSymbol.RefKind != RefKind.None)
            {
                continue;
            }

            var paramType = paramSymbol.DeclaredType;
            if(paramType is null || !ShouldAutoInsertScopeExitDrop(paramType))
            {
                continue;
            }

            var paramName = paramNode.Name.Lexeme;

            // Check if the parameter is returned or moved within the method body.
            var disposition = AnalyzeDropDisposition(paramName, block.Statements, 0);
            if(disposition == DropDisposition.UnconditionallyMoved)
            {
                continue;
            }

            var isTaskLike = IsTaskLikeType(paramType);
            result.Add((paramName, isTaskLike));
        }

        return result;
    }

    /// <summary>
    /// Emits <c>var identifier = (TypeReference)MistContext.Peek&lt;TypeReference&gt;();</c>
    /// for each with-clause binding at method entry.
    /// </summary>
    private void EmitWithClausePeekBindings(IReadOnlyList<ContextClauseInfo>? withClauses)
    {
        if(withClauses is null || withClauses.Count == 0)
        {
            return;
        }

        foreach(var clause in withClauses)
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
        if(block.Statements.Length == 0)
        {
            return false;
        }

        return block.Statements[^1] is ReturnStatementStatementNode;
    }

    // ─── Property Emission ───────────────────────────────────────────────

    private void EmitPropertyDeclaration(PropertyDeclarationNode propertyDecl)
    {
        switch(propertyDecl)
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
        var propSymbol = model.GetDeclaredSymbol(blockProp) as Symbol;
        var isReadonlyProperty = propSymbol is not null
            && propSymbol.ReadonlyStatus is not ReadonlyStatus.Mutable and not ReadonlyStatus.Unknown;

        // Check if any accessor uses the 'field' keyword — if so, emit a backing field first.
        var backingFieldName = $"_{name}_k__BackingField";
        var usesField = AccessorsUseFieldKeyword(blockProp.Accessors);
        if(usesField)
        {
            AppendLine($"private {type} {backingFieldName};");
        }

        // Build accessor list.
        var accessorParts = new List<string>();
        foreach(var accessor in blockProp.Accessors)
        {
            accessorParts.Add(EmitAccessorDeclaration(accessor, usesField ? backingFieldName : null, isReadonlyProperty));
        }

        var accessorBody = string.Join(" ", accessorParts);

        // Emit initializer if present.
        if(blockProp.Initializer is not null)
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
        switch(accessor)
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
        if(modifier is null)
        {
            return "";
        }

        return modifier switch
        {
            PublicModifierModifierNode => "public ",
            PrivateModifierModifierNode => "private ",
            StaticModifierModifierNode => "static ",
            _ => "",
        };
    }

    /// <summary>
    /// Emits an accessor declaration with optional refinement guards injected at the start
    /// of block-bodied accessors. Used by indexer emission to validate parameter constraints.
    /// If no guards are present, delegates to <see cref="EmitAccessorDeclaration"/>.
    /// </summary>
    private string EmitAccessorDeclarationWithGuards(
        AccessorDeclarationNode accessor,
        string? backingFieldName,
        bool isReadonlyProperty,
        List<(string Name, RefinementPredicate Predicate, string TypeName)> guards)
    {
        if(guards.Count == 0)
        {
            return EmitAccessorDeclaration(accessor, backingFieldName, isReadonlyProperty);
        }

        switch(accessor)
        {
            case BlockGetAccessorAccessorDeclarationNode blockGet:
                {
                    var mod = EmitAccessorModifier(blockGet.AccessorModifier);
                    var body = EmitAccessorBlockWithGuards(blockGet.Body, backingFieldName, guards);
                    return $"{mod}get {body}";
                }
            case BlockSetAccessorAccessorDeclarationNode blockSet:
                {
                    var mod = EmitAccessorModifier(blockSet.AccessorModifier);
                    var body = EmitAccessorBlockWithGuards(blockSet.Body, backingFieldName, guards);
                    var keyword = isReadonlyProperty ? "init" : "set";
                    return $"{mod}{keyword} {body}";
                }
            case BlockInitAccessorAccessorDeclarationNode blockInit:
                {
                    var mod = EmitAccessorModifier(blockInit.AccessorModifier);
                    var body = EmitAccessorBlockWithGuards(blockInit.Body, backingFieldName, guards);
                    return $"{mod}init {body}";
                }
            default:
                // Auto and expression-bodied accessors cannot have guards injected.
                return EmitAccessorDeclaration(accessor, backingFieldName, isReadonlyProperty);
        }
    }

    /// <summary>
    /// Emits a block body for an accessor with refinement guards injected at the start.
    /// </summary>
    private string EmitAccessorBlockWithGuards(
        BlockNode block,
        string? backingFieldName,
        List<(string Name, RefinementPredicate Predicate, string TypeName)> guards)
    {
        var sb = new StringBuilder();
        _ = sb.Append("{ ");

        // Emit refinement guards at the start of the accessor body.
        foreach(var (paramName, predicate, typeName) in guards)
        {
            var condition = EmitRefinementPredicateCondition(predicate, paramName);
            _ = sb.Append($"if (!({condition})) ");
            _ = sb.Append($"throw new ArgumentException($\"Value {{{paramName}}} does not satisfy refinement '{predicate.Description}' on type '{typeName}'\", nameof({paramName})); ");
        }

        foreach(var stmt in block.Statements)
        {
            _ = sb.Append(EmitStatementInline(stmt, backingFieldName));
            _ = sb.Append(' ');
        }

        _ = sb.Append('}');
        return sb.ToString();
    }

    /// <summary>
    /// Emits a block body for an accessor, replacing 'field' keyword references with the backing field name.
    /// </summary>
    private string EmitAccessorBlock(BlockNode block, string? backingFieldName)
    {
        var sb = new StringBuilder();
        _ = sb.Append("{ ");

        foreach(var stmt in block.Statements)
        {
            _ = sb.Append(EmitStatementInline(stmt, backingFieldName));
            _ = sb.Append(' ');
        }

        _ = sb.Append('}');
        return sb.ToString();
    }

    /// <summary>
    /// Emits an expression for an accessor, replacing 'field' keyword references with the backing field name.
    /// </summary>
    private string EmitAccessorExpression(ExpressionNode expr, string? backingFieldName)
    {
        if(backingFieldName is not null)
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
        if(backingFieldName is not null)
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
        switch(stmt)
        {
            case ReturnStatementStatementNode returnStmt:
                {
                    if(returnStmt.ReturnStatement.Value is not null)
                    {
                        // Task 14.2: When the method return type is a two-constituent T | void union,
                        // emit Void.Void as null instead of MistVoid.Instance.
                        var previousVoidAsNull = emitVoidAsNull;
                        if(IsOptionalUnionType(currentMethodReturnType))
                        {
                            emitVoidAsNull = true;
                        }

                        var value = EmitExpression(returnStmt.ReturnStatement.Value);
                        emitVoidAsNull = previousVoidAsNull;

                        value = WrapWithImplicitCastIfNeeded(returnStmt.ReturnStatement, value);
                        // Task 31.1 — covariant unknown widening at the return
                        // site against the enclosing method's return type.
                        var valueType = model.GetTypeOf(returnStmt.ReturnStatement.Value);
                        var widened = TryEmitCovariantUnknownCast(valueType, currentMethodReturnType, value);
                        if(widened is not null)
                        {
                            value = widened;
                        }

                        return $"return {value};";
                    }

                    return "return;";
                }
            case ExpressionStatementStatementNode exprStmt:
                return $"{EmitExpression(exprStmt.ExpressionStatement.Expression)};";
            case VariableDeclarationStatementStatementNode varDeclStmt:
                {
                    switch(varDeclStmt.VariableDeclaration)
                    {
                        case TypedVariableDeclarationVariableDeclarationStatementNode typed:
                            {
                                var type = EmitTypeReference(typed.Type, isValueContext: true);
                                var name = typed.Name.Lexeme;

                                var declaredSymbol = model.GetDeclaredSymbol(typed) as Symbol;
                                var declaredType = declaredSymbol?.DeclaredType;

                                // Task 14.2: When the declared type is a two-constituent T | void union,
                                // emit Void.Void as null instead of MistVoid.Instance.
                                var previousVoidAsNull = emitVoidAsNull;
                                if(IsOptionalUnionType(declaredType))
                                {
                                    emitVoidAsNull = true;
                                }

                                var init = EmitExpression(typed.Initializer);
                                emitVoidAsNull = previousVoidAsNull;

                                init = WrapWithImplicitCastIfNeeded(typed.Initializer, init);
                                // Task 31.1 — covariant unknown widening at the
                                // typed local declaration's assignment site.
                                var initType = model.GetTypeOf(typed.Initializer);
                                var widened = TryEmitCovariantUnknownCast(initType, declaredType, init);
                                if(widened is not null)
                                {
                                    init = widened;
                                }

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
                    _ = sb.Append($"if ({condition}) {{ ");
                    foreach(var s in ifStmt.IfStatement.ThenBranch.Statements)
                    {
                        _ = sb.Append(EmitStatementToString(s));
                        _ = sb.Append(' ');
                    }

                    _ = sb.Append('}');
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
        foreach(var accessor in accessors)
        {
            switch(accessor)
            {
                case BlockGetAccessorAccessorDeclarationNode blockGet:
                    if(ContainsFieldKeywordInBlock(blockGet.Body))
                    {
                        return true;
                    }

                    break;
                case ExpressionGetAccessorAccessorDeclarationNode exprGet:
                    if(ContainsFieldKeywordInNode(exprGet.Expression))
                    {
                        return true;
                    }

                    break;
                case BlockSetAccessorAccessorDeclarationNode blockSet:
                    if(ContainsFieldKeywordInBlock(blockSet.Body))
                    {
                        return true;
                    }

                    break;
                case ExpressionSetAccessorAccessorDeclarationNode exprSet:
                    if(ContainsFieldKeywordInNode(exprSet.Expression))
                    {
                        return true;
                    }

                    break;
                case BlockInitAccessorAccessorDeclarationNode blockInit:
                    if(ContainsFieldKeywordInBlock(blockInit.Body))
                    {
                        return true;
                    }

                    break;
                case ExpressionInitAccessorAccessorDeclarationNode exprInit:
                    if(ContainsFieldKeywordInNode(exprInit.Expression))
                    {
                        return true;
                    }

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
        if(node is FieldKeywordExpressionPrimaryNode)
        {
            return true;
        }

        foreach(var child in node.Children())
        {
            if(child is Storm.Runtime.SyntaxNode childNode && ContainsFieldKeywordInNode(childNode))
            {
                return true;
            }
        }

        return false;
    }

    // ─── Indexer Emission ────────────────────────────────────────────────

    private void EmitIndexerDeclaration(IndexerDeclarationNode indexerDecl)
    {
        if(indexerDecl.Body is not BlockIndexerBodyIndexerBodyNode blockBody)
        {
            return;
        }

        var modifiers = EmitModifiers(indexerDecl.Modifiers);
        var type = EmitTypeReference(indexerDecl.Type);
        var parameters = EmitParameterList(indexerDecl.Parameters);

        // Task 12.2 — Determine if this indexer is readonly (emit `init` instead of `set`).
        var indexerSymbol = model.GetDeclaredSymbol(indexerDecl) as Symbol;
        var isReadonlyIndexer = indexerSymbol is not null
            && indexerSymbol.ReadonlyStatus is not ReadonlyStatus.Mutable and not ReadonlyStatus.Unknown;

        // Collect refinement guards for indexer parameters.
        var refinementGuards = CollectRefinementParameterGuards(indexerDecl.Parameters);

        // Build accessor list.
        var accessorParts = new List<string>();
        foreach(var accessor in blockBody.Accessors)
        {
            accessorParts.Add(EmitAccessorDeclarationWithGuards(accessor, null, isReadonlyIndexer, refinementGuards));
        }

        var accessorBody = string.Join(" ", accessorParts);
        AppendLine($"{modifiers}{type} this[{parameters}] {{ {accessorBody} }}");
    }

    private string EmitParameterList(ParameterListNode? paramList)
    {
        if(paramList is null)
        {
            return "";
        }

        // The Storm generator only stores the first parameter as a named property;
        // additional comma-separated parameters are parsed but discarded from the AST.
        // Use the source span to faithfully reproduce the full parameter list,
        // but only if no parameter has a type that requires a Mist-to-C# rewrite.
        var span = paramList.Span;
        if(span.Length > 0 && span.Start + span.Length <= sourceText.Contents.Length)
        {
            var sourceText = this.sourceText.Contents.Substring(span.Start, span.Length);

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
            if(firstType.First is not VoidSingleTypeRefSingleTypeRefNode
                && firstType.First is not FuncSingleTypeRefSingleTypeRefNode
                && firstType.SingleTypeRefs.Length == 0
                && !TypeReferenceContainsUnknown(firstType)
                && !TypeReferenceHasOptionalSuffix(firstType)
                && !ContainsVoidOrUnknownKeyword(sourceText)
                && !ContainsPipeOperator(sourceText)
                && !ContainsFuncKeyword(sourceText)
                && !ParameterListContainsTypeAlias(paramList, sourceText)
                && !TypeReferenceHasDependentTypeArguments(firstType)
                && !SourceContainsDependentTypeArguments(sourceText)
                && !ContainsReadonlyKeyword(sourceText)
                && !ContainsOwnershipKeyword(sourceText)
                && !ContainsOptionalSuffix(sourceText))
            {
                return sourceText;
            }
        }

        // Fallback: emit all parameters from the AST (erasing ownership specifiers and readonly modifiers).
        var parts = new List<string> { EmitParameter(paramList.Parameter) };
        foreach(var param in paramList.Parameters)
        {
            parts.Add(EmitParameter(param));
        }

        return string.Join(", ", parts);
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
    /// Returns <c>true</c> if <paramref name="source"/> contains the
    /// Mist keyword <c>func</c> as a standalone identifier (matched at word
    /// boundaries). Used as a lexical fallback in <see cref="EmitParameterList"/>
    /// to detect func type annotations that must be rewritten to
    /// <c>System.Func&lt;...&gt;</c> or <c>System.Action&lt;...&gt;</c> in C# emission.
    /// </summary>
    private static bool ContainsFuncKeyword(string source)
    {
        return ContainsWord(source, "func");
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
    /// Returns <c>true</c> if <paramref name="source"/> contains any ownership
    /// keyword (<c>owned</c>, <c>frozen</c>, <c>borrowed</c>, or <c>ref</c>) as a standalone
    /// identifier (matched at word boundaries). Used as a lexical fallback in
    /// <see cref="EmitParameterList"/> to detect ownership specifiers that must
    /// be erased in C# emission (ownership is compile-time only — no runtime cost).
    /// </summary>
    private static bool ContainsOwnershipKeyword(string source)
    {
        return ContainsWord(source, "owned") || ContainsWord(source, "frozen") || ContainsWord(source, "borrowed") || ContainsWord(source, "ref") || ContainsWord(source, "linear") || ContainsWord(source, "affine") || ContainsWord(source, "relevant");
    }

    /// <summary>
    /// Returns <c>true</c> if the given type reference node has the optional suffix
    /// (<c>?</c>) set. Used in <see cref="EmitParameterList"/> to detect optional
    /// type annotations that require emitter-side rewriting (reference types emit
    /// as plain <c>T</c> instead of <c>T?</c>).
    /// </summary>
    private static bool TypeReferenceHasOptionalSuffix(TypeReferenceNode typeRef)
    {
        return typeRef.First switch
        {
            PrimitiveSingleTypeRefSingleTypeRefNode p => p.Optional is not null,
            NamedSingleTypeRefSingleTypeRefNode n => n.Optional is not null,
            _ => false,
        };
    }

    /// <summary>
    /// Returns <c>true</c> if <paramref name="source"/> contains a <c>?</c> character
    /// that could indicate an optional type suffix. Used as a lexical fallback in
    /// <see cref="EmitParameterList"/> to detect optional type annotations in
    /// subsequent parameters (beyond the first) that the Storm parser does not
    /// expose structurally. The <c>?</c> in a parameter list is always the optional
    /// type suffix (not the ternary operator, which cannot appear in type positions).
    /// </summary>
    private static bool ContainsOptionalSuffix(string source)
    {
        return source.Contains('?');
    }

    /// <summary>
    /// Returns <c>true</c> if the compilation unit references <c>Standard.Regex</c>,
    /// detected by the presence of the <c>regex</c> keyword or a backtick-delimited
    /// regex literal in the source text. When true, the emitter adds a
    /// <c>using System.Text.RegularExpressions;</c> directive.
    /// </summary>
    private bool SourceReferencesRegex()
    {
        var source = sourceText.Contents;
        // Check for the `regex` keyword as a standalone word.
        if(ContainsWord(source, "regex"))
        {
            return true;
        }
        // Check for backtick (regex literal delimiter).
        if(source.Contains('`'))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Returns <c>true</c> if any type declared in the compilation unit implements or
    /// references a Standard-namespace abstract type that is NOT mapped to a CLR counterpart
    /// via <see cref="CSharpTypeMap"/>. When true, the emitter must retain
    /// <c>using Standard;</c> so the emitted C# can resolve the type reference.
    /// </summary>
    private bool FileReferencesUnmappedStandardType(CompilationUnitNode cu)
    {
        foreach(var item in cu.Items)
        {
            switch(item)
            {
                case TypeItemTopLevelItemNode typeItem:
                    if(TypeImplementsUnmappedStandardAbstract(typeItem.TypeDeclaration))
                    {
                        return true;
                    }

                    break;
                case NamespaceItemTopLevelItemNode nsItem:
                    {
                        var bodyItems = nsItem.NamespaceDeclaration switch
                        {
                            FileScopedNamespaceNamespaceDeclarationNode fileScopedNs => fileScopedNs.Body,
                            BlockNamespaceNamespaceDeclarationNode blockNs => blockNs.Body,
                            _ => ImmutableArray<NamespaceBodyItemNode>.Empty,
                        };

                        foreach(var bodyItem in bodyItems)
                        {
                            if(bodyItem is NamespaceTypeItemNamespaceBodyItemNode typeBodyItem
                                && TypeImplementsUnmappedStandardAbstract(typeBodyItem.TypeDeclaration))
                            {
                                return true;
                            }
                        }

                        break;
                    }
            }
        }

        return false;
    }

    /// <summary>
    /// Returns <c>true</c> if the given type declaration implements a Standard-namespace
    /// abstract type that does not have a CLR mapping in <see cref="CSharpTypeMap"/>.
    /// </summary>
    private bool TypeImplementsUnmappedStandardAbstract(TypeDeclarationNode typeDecl)
    {
        var symbol = typeDecl switch
        {
            BodyTypeTypeDeclarationNode body => model.GetDeclaredSymbol(body.TypeBody),
            _ => null,
        };

        var abstracts = symbol?.DeclaredType switch
        {
            ClassType ct => ct.Abstracts,
            StructType st => st.Abstracts,
            _ => null,
        };

        if(abstracts is null)
        {
            return false;
        }

        foreach(var abs in abstracts)
        {
            var definition = abs is ConstructedType ctAbstract ? ctAbstract.GenericDefinition : abs;
            if(definition is AbstractType abstractType
                && abstractType.ContainingNamespace == "Standard"
                && CSharpTypeMap.TryGetMappedName($"Standard.{abstractType.DisplayName}") is null)
            {
                return true;
            }
        }

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
        for(var i = 0; i < source.Length; i++)
        {
            if(source[i] == '|')
            {
                // Check it's not part of ||
                if(i + 1 < source.Length && source[i + 1] == '|')
                {
                    i++; // Skip the second |
                    continue;
                }
                // Check it's not preceded by another | (second char of ||)
                if(i > 0 && source[i - 1] == '|')
                {
                    continue;
                }

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
        for(var i = 0; i < source.Length - 1; i++)
        {
            if(source[i] == '<')
            {
                var next = i + 1;
                while(next < source.Length && source[next] == ' ')
                {
                    next++;
                }

                if(next < source.Length)
                {
                    var c = source[next];
                    // Check for digit, minus (negated), quote (char), or true/false
                    if(char.IsDigit(c) || c == '-' || c == '\'')
                    {
                        return true;
                    }

                    if(next + 4 <= source.Length && source.Substring(next, 4) == "true")
                    {
                        return true;
                    }

                    if(next + 5 <= source.Length && source.Substring(next, 5) == "false")
                    {
                        return true;
                    }
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
        while((index = source.IndexOf(word, index, StringComparison.Ordinal)) >= 0)
        {
            var before = index == 0
                || !IsIdentifierChar(source[index - 1]);
            var afterPos = index + word.Length;
            var after = afterPos >= source.Length
                || !IsIdentifierChar(source[afterPos]);
            if(before && after)
            {
                return true;
            }

            index = afterPos;
        }

        return false;
    }

    private static bool IsIdentifierChar(char c)
    {
        return char.IsLetterOrDigit(c) || c == '_';
    }

    /// <summary>
    /// Returns <c>true</c> if <paramref name="typeRef"/> is either
    /// <see cref="UnknownSingleTypeRefSingleTypeRefNode"/> directly, or a
    /// <see cref="NamedSingleTypeRefSingleTypeRefNode"/> whose type-argument
    /// list transitively contains an <see cref="UnknownSingleTypeRefSingleTypeRefNode"/>.
    /// Used by <see cref="EmitParameterList"/> to decide whether the
    /// source-text fast-path is safe; any appearance of <c>unknown</c>
    /// in the type requires the fallback (which routes through
    /// <see cref="EmitTypeReference"/> / <see cref="CSharpTypeMap"/>)
    /// so the Mist keyword is rewritten to the correct C# form
    /// (<c>object</c> or <c>dynamic</c>).
    /// </summary>
    private static bool TypeReferenceContainsUnknown(TypeReferenceNode typeRef)
    {
        // Unions (non-empty continuation list) were a distinct node variant
        // pre-restructure that fell through to `return false`; preserve that.
        if(typeRef.SingleTypeRefs.Length > 0)
        {
            return false;
        }

        switch(typeRef.First)
        {
            case UnknownSingleTypeRefSingleTypeRefNode:
                return true;

            case NamedSingleTypeRefSingleTypeRefNode named:
                if(named.TypeArgs is null)
                {
                    return false;
                }
                // Check all type arguments — only TypeReferenceTypeArgument variants
                // can contain unknown. Literal type arguments (int, bool, char, etc.)
                // used for dependent types can never contain unknown.
                if(TypeArgumentContainsUnknown(named.TypeArgs.TypeArgument))
                {
                    return true;
                }

                foreach(var arg in named.TypeArgs.TypeArguments)
                {
                    if(TypeArgumentContainsUnknown(arg))
                    {
                        return true;
                    }
                }

                return false;

            default:
                return false;
        }
    }

    /// <summary>
    /// Checks if a single type argument contains <see cref="UnknownSingleTypeRefSingleTypeRefNode"/>.
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
        if(typeRef.First is not NamedSingleTypeRefSingleTypeRefNode named)
        {
            return false;
        }

        if(named.TypeArgs is null)
        {
            return false;
        }

        // Check if any type argument is a literal (non-type-reference) dependent value.
        if(IsDependentTypeArgument(named.TypeArgs.TypeArgument)
            || named.TypeArgs.TypeArguments.Any(IsDependentTypeArgument))
        {
            return true;
        }

        // Check if the target type has dependent type parameters and any type argument
        // in a dependent position is an identifier (forwarded dependent parameter).
        var typeName = ExtractQualifiedName(named.Name);
        var targetTypeParams = GetTypeParamsForName(typeName);
        if(targetTypeParams is not null)
        {
            var allArgs = new List<TypeArgumentNode> { named.TypeArgs.TypeArgument };
            foreach(var arg in named.TypeArgs.TypeArguments)
            {
                allArgs.Add(arg);
            }

            for(var i = 0; i < allArgs.Count && i < targetTypeParams.Count; i++)
            {
                if(targetTypeParams[i] is DependentTypeParameterType
                    && allArgs[i] is TypeReferenceTypeArgumentTypeArgumentNode typeRefArg
                    && typeRefArg.Type.First is NamedSingleTypeRefSingleTypeRefNode argNamedRef
                    && argNamedRef.TypeArgs is null
                    && argNamedRef.ArrayRank is null)
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
        NamedSingleTypeRefSingleTypeRefNode namedRef,
        bool isValueContext)
    {
        var underlying = aliasType.UnderlyingType;

        // Non-generic alias or no type arguments provided — emit underlying directly.
        if(aliasType.TypeParameters.Count == 0 || namedRef.TypeArgs is null)
        {
            return CSharpTypeMap.GetTypeName(underlying, isValueContext);
        }

        // If all type parameters are dependent (value) parameters, the underlying
        // type doesn't use them — emit the underlying type without type arguments.
        if(aliasType.TypeParameters.All(tp => tp is DependentTypeParameterType))
        {
            return CSharpTypeMap.GetTypeName(underlying, isValueContext);
        }

        // Generic alias with type arguments — emit the underlying type's generic
        // definition with the substituted type arguments from the reference site.
        // e.g., `Box<int>` where `type Box<T> = GenericBox<T>` → `GenericBox<int>`
        if(underlying is ConstructedType ct)
        {
            // For Standard.Func underlying types (e.g., type Predicate<T> = func<T, bool>),
            // we must substitute the alias's type parameters into the underlying type's
            // type arguments and emit the resulting type string directly.
            // This handles the Func→System.Func / Action→System.Action mapping correctly.
            if(ct.GenericDefinition is ClassType { DisplayName: "Func", ContainingNamespace: "Standard", IsBackendBacked: true })
            {
                // Build a substitution map from alias type parameter names to emitted
                // type argument strings (resolved through EmitTypeReference).
                var refTypeArgs = new List<TypeArgumentNode> { namedRef.TypeArgs.TypeArgument };
                foreach(var arg in namedRef.TypeArgs.TypeArguments)
                {
                    refTypeArgs.Add(arg);
                }

                var paramNameToEmitted = new Dictionary<string, string>(StringComparer.Ordinal);
                for(var p = 0; p < aliasType.TypeParameters.Count && p < refTypeArgs.Count; p++)
                {
                    if(aliasType.TypeParameters[p] is TypeParameterType aliasTpt
                        && refTypeArgs[p] is TypeReferenceTypeArgumentTypeArgumentNode typeRefArg)
                    {
                        paramNameToEmitted[aliasTpt.Name] = EmitTypeReference(typeRefArg.Type, isValueContext: true);
                    }
                }

                // Build the substituted type argument strings for the Func type.
                var funcArgStrings = new List<string>(ct.TypeArguments.Count);
                for(var i = 0; i < ct.TypeArguments.Count; i++)
                {
                    var ctArg = ct.TypeArguments[i];
                    if(ctArg is TypeParameterType tpt && paramNameToEmitted.TryGetValue(tpt.Name, out var emitted))
                    {
                        funcArgStrings.Add(emitted);
                    }
                    else
                    {
                        // Fixed type argument (e.g., `bool` in `type Predicate<T> = func<T, bool>`).
                        funcArgStrings.Add(CSharpTypeMap.GetTypeName(ctArg, isValueContext: true));
                    }
                }

                // Determine if this is Action (last arg is void) or Func.
                var lastArg = ct.TypeArguments[ct.TypeArguments.Count - 1];
                if(lastArg.IsVoid())
                {
                    // Action family: drop the trailing void return type.
                    if(ct.TypeArguments.Count == 1)
                    {
                        return "System.Action";
                    }

                    var actionArgs = funcArgStrings.Take(funcArgStrings.Count - 1);
                    return $"System.Action<{string.Join(", ", actionArgs)}>";
                }

                // Func family: all type arguments, including the return type.
                return $"System.Func<{string.Join(", ", funcArgStrings)}>";
            }

            // Emit the generic definition name and substitute type args.
            var defName = CSharpTypeMap.GetTypeName(ct.GenericDefinition, isValueContext);
            var typeArgs = EmitTypeArgumentList(namedRef.TypeArgs);
            return $"{defName}{typeArgs}";
        }

        // Union type underlying (e.g., `Optional<T> = T | void`) — substitute type
        // parameters into the union constituents and emit the resulting type.
        // For the common case of `type Optional<T> = T | void`, emit as nullable.
        if(underlying is UnionType underlyingUnion)
        {
            // Check if this is a two-constituent void union (the Optional<T> pattern).
            var isOptionalPattern = underlyingUnion.Constituents.Count == 2
                && underlyingUnion.Constituents.Any(c => c.IsVoid());

            if(isOptionalPattern && namedRef.TypeArgs is not null)
            {
                // Emit the type argument using the standard type reference path.
                var firstTypeRef = namedRef.TypeArgs.GetTypeReference();
                var emittedArg = EmitTypeReference(firstTypeRef, isValueContext);

                // Determine if the type argument is a value type.
                // Check the semantic model for the resolved type of the type argument.
                var argSymbol = model.GetDeclaredSymbol(firstTypeRef);
                var argType = argSymbol?.DeclaredType;

                // If we can determine it's a value type, emit T?. Otherwise emit T (reference type).
                if(argType is not null && CSharpTypeMap.IsValueType(argType))
                {
                    return $"{emittedArg}?";
                }

                // Fallback: check if the emitted name is a known C# value type keyword.
                if(IsKnownValueTypeKeyword(emittedArg))
                {
                    return $"{emittedArg}?";
                }

                // Reference type — null represents void, so just emit the type name.
                return emittedArg;
            }

            // Fallback: emit underlying directly.
            return CSharpTypeMap.GetTypeName(underlying, isValueContext);
        }

        // Fallback: emit the underlying type with the type arguments appended.
        // SpanType/ArrayType already include their element type in GetTypeName output,
        // so we emit just the base name (without the baked-in type arg) and append
        // the call-site type arguments to get proper substitution.
        if(underlying is SpanType fallbackSpan)
        {
            var spanBase = fallbackSpan.IsReadOnly ? "System.ReadOnlySpan" : "System.Span";
            var args = EmitTypeArgumentList(namedRef.TypeArgs);
            return $"{spanBase}{args}";
        }

        var baseName = CSharpTypeMap.GetTypeName(underlying, isValueContext);
        var args2 = EmitTypeArgumentList(namedRef.TypeArgs);
        return $"{baseName}{args2}";
    }

    /// <summary>
    /// Checks if a C# type name is a known value type keyword.
    /// Used as a fallback when the semantic model can't determine the type kind.
    /// </summary>
    private static bool IsKnownValueTypeKeyword(string typeName)
    {
        return typeName is
        "int" or "bool" or "float" or "double" or "long" or "short" or "byte"
        or "char" or "decimal" or "uint" or "ulong" or "ushort" or "sbyte"
        or "nint" or "nuint";
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
        if(paramList.Parameter.Type.First is NamedSingleTypeRefSingleTypeRefNode namedRef)
        {
            var name = ExtractQualifiedName(namedRef.Name);
            if(model.AllSymbols.Any(s => s.Name == name && s.Kind == SymbolKind.TypeAlias
                && !IsStdlibTypeAlias(s)))
            {
                return true;
            }
        }

        // Lexical fallback: check if any NON-STDLIB type alias name appears in the source
        // text as a standalone word (word-boundary match). This catches subsequent parameters
        // (beyond the first) that the Storm parser does not expose structurally.
        // We use ContainsWord instead of Contains to avoid false positives where an
        // alias name is a substring of a parameter name (e.g., alias "Name" matching
        // inside "appName").
        // Stdlib type aliases (Action, Span, ReadOnlySpan, Task, etc.) are excluded
        // because they are either handled by dedicated emission paths or produce
        // identical source text when emitted.
        foreach(var sym in model.AllSymbols)
        {
            if(sym.Kind == SymbolKind.TypeAlias
                && !IsStdlibTypeAlias(sym)
                && ContainsWord(sourceText, sym.Name))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns true if the symbol is a stdlib type alias whose source text can be
    /// used as-is without rewriting. Span and ReadOnlySpan are copied verbatim
    /// because the source text fast path uses the same spelling as C# (they're
    /// resolved through the mapped name path when the fallback triggers elsewhere).
    /// Other stdlib aliases like Task, Action, SpanBounds need rewriting and should
    /// NOT be excluded.
    /// </summary>
    private static bool IsStdlibTypeAlias(ISymbol sym)
    {
        // Only Span and ReadOnlySpan produce identical C# source text.
        // Other stdlib aliases (Task → System.Threading.Tasks.Task, 
        // Action → System.Action, etc.) need rewriting.
        return sym.Name is "Span" or "ReadOnlySpan";
    }

    private string EmitParameter(ParameterNode param)
    {
        var type = EmitTypeReference(param.Type, isValueContext: true);
        var name = param.Name.Lexeme;

        // Emit ref / ref readonly prefix for ref parameters (Requirement 8.1).
        var symbol = model.GetDeclaredSymbol(param) as Symbol;
        if(symbol is not null)
        {
            return symbol.RefKind switch
            {
                RefKind.Ref => $"ref {type} {name}",
                RefKind.RefReadonly => $"ref readonly {type} {name}",
                _ => $"{type} {name}",
            };
        }

        return $"{type} {name}";
    }

    private string EmitArgumentList(ArgumentListNode? argList, Symbol? calledMethod = null)
    {
        if(argList is null)
        {
            return "";
        }

        // Collect all argument nodes (first + rest) to detect ref arguments.
        var argNodes = new List<ArgumentNode>(1 + argList.Arguments.Length)
        {
            argList.Argument
        };
        foreach(var arg in argList.Arguments)
        {
            argNodes.Add(arg);
        }

        // Check if any argument is a ref argument — if so, we must bypass the
        // source-text fast path and emit each argument individually with `ref` prefix.
        var hasRefArgument = false;
        foreach(var argNode in argNodes)
        {
            if(argNode is RefArgumentArgumentNode)
            {
                hasRefArgument = true;
                break;
            }
        }

        // Collect all argument expressions from the AST.
        var argExprs = new List<ExpressionNode>();
        foreach(var expr in argList.GetAllExpressions())
        {
            argExprs.Add(expr);
        }

        // Check if any argument has a resolved implicit or explicit cast operator.
        var hasImplicitCast = false;
        foreach(var argExpr in argExprs)
        {
            var resolved = model.GetResolvedOperator(argExpr);
            if(resolved is Symbol sym && (sym.Name.StartsWith("operator_implicit_") || sym.Name.StartsWith("operator_explicit_")))
            {
                hasImplicitCast = true;
                break;
            }
        }

        // Check if any argument requires delegate constructor wrapping (Task 9.1).
        var hasDelegateWrapping = ArgumentRequiresDelegateWrapping(argExprs, calledMethod);

        // If any argument is a ref argument, needs implicit cast wrapping, or needs
        // delegate wrapping, emit all arguments through the expression emitter.
        if(hasRefArgument || hasImplicitCast || hasDelegateWrapping)
        {
            var emitted = new List<string>();
            for(var i = 0; i < argExprs.Count; i++)
            {
                var emittedArg = EmitExpression(argExprs[i]);
                emittedArg = WrapWithImplicitCastIfNeeded(argExprs[i], emittedArg);
                if(argNodes[i] is RefArgumentArgumentNode)
                {
                    // Check if the corresponding parameter has ClrCallingConvention.In or .Out —
                    // if so, emit 'in' or 'out' instead of 'ref' at the call site (Requirement 3.4).
                    var refKeyword = "ref";
                    if(calledMethod?.Parameters is { Count: > 0 } parameters && i < parameters.Count)
                    {
                        if(parameters[i] is Symbol paramSymbol)
                        {
                            if(paramSymbol.ClrCallingConvention == ClrCallingConvention.In)
                            {
                                refKeyword = "in";
                            }
                            else if(paramSymbol.ClrCallingConvention == ClrCallingConvention.Out)
                            {
                                refKeyword = "out";
                            }
                        }
                    }

                    emittedArg = $"{refKeyword} {emittedArg}";
                }
                else
                {
                    // Task 12.1 — emit .Clone() at resolver-marked implicit-clone sites (Req 14 AC 7).
                    emittedArg = WrapWithImplicitCloneIfNeeded(argExprs[i], emittedArg);
                }
                // Task 9.1 — delegate constructor wrapping at call sites (Req 4.3, 4.4, 4.7, 4.8).
                emittedArg = WrapWithDelegateConstructorIfNeeded(argExprs[i], emittedArg, i, calledMethod);
                emitted.Add(emittedArg);
            }

            return string.Join(", ", emitted);
        }

        // No implicit casts or ref arguments — use the source span for faithful reproduction,
        // applying Void.Void → MistVoid.Instance substitution if needed.
        // However, if any argument contains a type literal (e.g., int.Name),
        // we must fall through to the expression emitter so typeof() is applied.
        // Similarly, if any argument contains a dependent type parameter reference,
        // we must fall through so TN.Value substitution is applied.
        // Also bypass when any argument involves an intrinsic type member access
        // (e.g., Regex.Pattern, Regex.IsFullMatch) that needs rewriting.
        // Task 12.1: Also bypass when any argument has an implicit-clone annotation
        // so that .Clone() can be emitted (Req 14 AC 7).
        // Also bypass when any argument contains an async block expression
        // (async { ... } or async valuetask { ... }) that needs rewriting.
        if(!ArgumentContainsTypeLiteral(argExprs) && !ArgumentContainsDependentTypeParam(argExprs)
            && !ArgumentContainsIntrinsicMemberAccess(argExprs)
            && !ArgumentContainsAutoAwaitCall(argExprs)
            && !ArgumentsContainImplicitClone(argExprs)
            && !ArgumentContainsOptionalInterpolation(argExprs)
            && !ArgumentContainsAsyncBlockExpression(argExprs)
            && !ArgumentContainsAnonymousTypeEquality(argExprs))
        {
            var span = argList.Span;
            if(span.Length > 0 && span.Start + span.Length <= sourceText.Contents.Length)
            {
                var rawText = sourceText.Contents.Substring(span.Start, span.Length);
                if(rawText.Contains("Void.Void"))
                {
                    // Task 14.2: When in an optional type context, emit null instead of MistVoid.Instance.
                    if(emitVoidAsNull)
                    {
                        return rawText.Replace("Void.Void", "null");
                    }

                    NeedsMistVoidStruct = true;
                    return rawText.Replace("Void.Void", "MistVoid.Instance");
                }
                // Bypass the fast path if the source contains the `regex` keyword,
                // which needs rewriting to System.Text.RegularExpressions.Regex.
                if(ContainsWord(rawText, "regex"))
                {
                    // Fall through to expression emitter.
                }
                else
                {
                    return rawText;
                }
            }
        }

        // Fallback: emit all arguments through the expression emitter.
        {
            var emitted = new List<string>();
            for(var i = 0; i < argExprs.Count; i++)
            {
                var emittedArg = EmitExpression(argExprs[i]);
                emittedArg = WrapWithImplicitCastIfNeeded(argExprs[i], emittedArg);
                // Task 12.1 — emit .Clone() at resolver-marked implicit-clone sites (Req 14 AC 7).
                emittedArg = WrapWithImplicitCloneIfNeeded(argExprs[i], emittedArg);
                // Task 9.1 — delegate constructor wrapping at call sites (Req 4.3, 4.4, 4.7, 4.8).
                emittedArg = WrapWithDelegateConstructorIfNeeded(argExprs[i], emittedArg, i, calledMethod);
                emitted.Add(emittedArg);
            }

            return string.Join(", ", emitted);
        }
    }

    /// <summary>
    /// Returns true if any argument expression contains a type literal (e.g., <c>int.Name</c>)
    /// that requires <c>typeof()</c> wrapping. When true, the source-text fast path must be
    /// bypassed so the expression emitter can apply the wrapping.
    /// </summary>
    private static bool ArgumentContainsTypeLiteral(List<ExpressionNode> argExprs)
    {
        foreach(var expr in argExprs)
        {
            if(ExpressionContainsTypeLiteral(expr))
            {
                return true;
            }
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
        foreach(var expr in argExprs)
        {
            if(ExpressionContainsDependentTypeParam(expr))
            {
                return true;
            }
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
        foreach(var expr in argExprs)
        {
            if(ExpressionContainsIntrinsicMemberAccess(expr))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Recursively checks if an expression tree contains a member access or method call
    /// on a type that has intrinsic emission templates registered.
    /// </summary>
    private bool ExpressionContainsIntrinsicMemberAccess(Storm.Runtime.SyntaxNode node)
    {
        if(node is PostfixNode postfix && postfix.Suffix.Length > 0)
        {
            var receiverType = model.GetTypeOf(postfix.Target);
            if(receiverType is not null)
            {
                var qualifiedName = GetQualifiedTypeNameFromType(receiverType);
                if(qualifiedName is not null && intrinsicRegistry.HasInstanceMethods(qualifiedName))
                {
                    return true;
                }
            }
        }

        foreach(var child in node.Children())
        {
            if(child is not null && ExpressionContainsIntrinsicMemberAccess(child))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns true if any argument expression contains a Func invocation with Task&lt;T&gt;
    /// return type that would trigger auto-await emission. When true, the source-text fast
    /// path must be bypassed so the expression emitter can prepend <c>await</c>.
    /// Only checks for Func invocations (not direct async method calls, which are already
    /// handled by the existing auto-await mechanism independent of the fast path).
    /// </summary>
    private bool ArgumentContainsAutoAwaitCall(List<ExpressionNode> argExprs)
    {
        // Only relevant in sync contexts where auto-await fires.
        if(insideAsyncFunctor || insideAsyncLambda
            || (currentMethodSymbol is not null && currentMethodSymbol.IsAsync))
        {
            return false;
        }

        foreach(var expr in argExprs)
        {
            if(ExpressionContainsFuncAutoAwaitCall(expr))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Recursively checks if an expression tree contains a Func invocation whose return
    /// type is Task&lt;T&gt; (requiring auto-await in the emitted C#). Only detects Func
    /// invocations — direct async method calls are handled separately.
    /// </summary>
    private bool ExpressionContainsFuncAutoAwaitCall(Storm.Runtime.SyntaxNode node)
    {
        if(node is PostfixNode postfix && postfix.Suffix.Length > 0
            && postfix.Suffix[^1] is InvocationSuffixPostfixSuffixNode)
        {
            // Check if target is a Func variable with Task<T> return type.
            var targetType = model.GetTypeOf(postfix.Target);
            if(targetType is not null
                && SemanticModelBuilder.IsFuncType(targetType)
                && targetType is ConstructedType funcCt)
            {
                var funcReturnType = SemanticModelBuilder.GetFuncReturnType(funcCt);
                if(SemanticModelBuilder.IsStandardTaskType(funcReturnType))
                {
                    return true;
                }
            }

            // Fallback: check via declared symbol type.
            var targetSymbol = model.GetDeclaredSymbol(postfix.Target);
            if(targetSymbol is ISymbol { DeclaredType: { } declaredType }
                && SemanticModelBuilder.IsFuncType(declaredType)
                && declaredType is ConstructedType funcCtFromSym)
            {
                var funcReturnType = SemanticModelBuilder.GetFuncReturnType(funcCtFromSym);
                if(SemanticModelBuilder.IsStandardTaskType(funcReturnType))
                {
                    return true;
                }
            }
        }

        foreach(var child in node.Children())
        {
            if(child is not null && ExpressionContainsFuncAutoAwaitCall(child))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Recursively checks if an expression tree contains an identifier that references
    /// a dependent type parameter.
    /// </summary>
    private bool ExpressionContainsDependentTypeParam(Storm.Runtime.SyntaxNode node)
    {
        if(node is IdentifierExpressionPrimaryNode ident)
        {
            var resolvedType = model.GetTypeOf(ident);
            if(resolvedType is DependentTypeParameterType)
            {
                return true;
            }

            if(IsDependentTypeParameterName(ident.Name.Lexeme))
            {
                return true;
            }
        }

        foreach(var child in node.Children())
        {
            if(child is not null && ExpressionContainsDependentTypeParam(child))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Recursively checks if an expression tree contains a
    /// <see cref="TypeLiteralExpressionPrimaryNode"/> anywhere within it (not just
    /// on the left spine). A type literal in expression position requires special
    /// emission — <c>typeof(...)</c> for type-literal contexts or
    /// <c>MistVoid.Instance</c> for void value literals — so its presence anywhere
    /// in a method argument (including nested binary operands and interpolated
    /// string holes such as <c>$"{void == void}"</c>) must bypass the source-text
    /// fast path and route through the expression emitter.
    /// </summary>
    private static bool ExpressionContainsTypeLiteral(Storm.Runtime.SyntaxNode node)
    {
        if(node is TypeLiteralExpressionPrimaryNode)
        {
            return true;
        }

        foreach(var child in node.Children())
        {
            if(child is not null && ExpressionContainsTypeLiteral(child))
            {
                return true;
            }
        }

        return false;
    }

    private string EmitElementTypeRef(ElementTypeRefNode elementTypeRef)
    {
        return elementTypeRef switch
        {
            PrimitiveElementTypeRefElementTypeRefNode primitiveRef => EmitPrimitiveType(primitiveRef.PrimitiveType),
            UnknownElementTypeRefElementTypeRefNode => "object",
            NamedElementTypeRefElementTypeRefNode namedRef => ExtractQualifiedName(namedRef.Name) + (namedRef.TypeArgs is not null ? EmitTypeArgumentList(namedRef.TypeArgs) : ""),
            _ => "object"
        };
    }

    private string EmitTypeReference(TypeReferenceNode typeRef, bool isValueContext = false)
    {
        // Union handling (non-empty PIPE-separated continuation list). Must run
        // before the switch because `typeRef.First` is never itself a union.
        if(typeRef.SingleTypeRefs.Length > 0)
        {
            // Check if this is a two-constituent void union (T | void) that should
            // emit as Nullable<T> or plain T instead of a union helper struct.
            var resolvedUnionType = model.GetTypeOf(typeRef) as UnionType;
            if(resolvedUnionType is not null && CSharpTypeMap.TryGetOptionalTypeName(resolvedUnionType) is { } optionalName)
            {
                return optionalName;
            }

            // Multi-constituent unions or non-void unions emit as the generated helper type name.
            // Collect constituent names matching UnionHelperGenerator.GetConstituentCSharpName,
            // sort alphabetically, and join with "_" to form "Union_{Type1}_{Type2}".
            var constituentNames = new List<string>
            {
                GetConstituentNameForHelper(typeRef.First)
            };
            foreach(var rest in typeRef.SingleTypeRefs)
            {
                constituentNames.Add(GetConstituentNameForHelper(rest));
            }

            constituentNames.Sort(StringComparer.Ordinal);
            return "Union_" + string.Join("_", constituentNames);
        }

        switch(typeRef.First)
        {
            case PrimitiveSingleTypeRefSingleTypeRefNode primitiveRef:
                {
                    var keyword = EmitPrimitiveType(primitiveRef.PrimitiveType);
                    if(primitiveRef.ArrayRank is not null)
                    {
                        // Task 14.6: int[]? → int[]? (Nullable array), int?[] → int?[] (array of Nullable)
                        var arrayType = $"{keyword}[]";
                        return primitiveRef.Optional is not null ? $"{arrayType}?" : arrayType;
                    }
                    // Task 14.6: When the optional suffix is present (e.g., int?), emit as
                    // C# Nullable<T> (T?) since all primitive types are value types.
                    if(primitiveRef.Optional is not null)
                    {
                        return $"{keyword}?";
                    }

                    return keyword;
                }
            case VoidSingleTypeRefSingleTypeRefNode:
                if(isValueContext)
                {
                    NeedsMistVoidStruct = true;
                    return "MistVoid";
                }

                return "void";
            case UnknownSingleTypeRefSingleTypeRefNode:
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
            case NamedSingleTypeRefSingleTypeRefNode namedRef:
                {
                    var name = ExtractQualifiedName(namedRef.Name);

                    // ── type-aliases feature: transparent resolution (Requirement 6.1) ──
                    // If this named type reference resolves to a type alias, emit the
                    // underlying type instead of the alias name. Type aliases are erased
                    // during transpilation — no alias name should appear in C# output.
                    // Check the expression type map first, then fall back to symbol lookup.
                    // The semantic model keys resolved types on the flat TypeReferenceNode,
                    // so look up via `typeRef` (not the SingleTypeRef child `namedRef`).
                    var resolvedType = model.GetTypeOf(typeRef);
                    // ── Associated type slot reference: T.SlotName → TSlotName (Task 7.3) ──
                    // When a type reference resolves to an AssociatedTypeSlotType (e.g., T.Enumerator
                    // in a generic context), emit the synthesized type parameter name.
                    if(resolvedType is AssociatedTypeSlotType resolvedSlot)
                    {
                        // Check if this is a parameterized slot — erase to constraint interface.
                        if(resolvedSlot.TypeParameters.Count > 0 && namedRef.TypeArgs is not null)
                        {
                            var erasedType = EraseParameterizedSlotToConstraint(resolvedSlot, namedRef.TypeArgs, currentAbstractTypeParamSubs);
                            if(erasedType is not null)
                            {
                                return namedRef.ArrayRank is not null ? $"{erasedType}[]" : erasedType;
                            }
                        }
                        // Use the active method-level substitution if available.
                        if(currentMethodSlotSubstitutions is not null
                            && currentMethodSlotSubstitutions.TryGetValue(name, out var synthesizedName))
                        {
                            return namedRef.ArrayRank is not null ? $"{synthesizedName}[]" : synthesizedName;
                        }
                        // Fallback: use T + slot display name.
                        var fallbackName = $"T{resolvedSlot.DisplayName}";
                        return namedRef.ArrayRank is not null ? $"{fallbackName}[]" : fallbackName;
                    }
                    // ── Parameterized slot erasure for implementor methods ──
                    // Parameterized slots are NOT erased in the implementor — the method
                    // returns the concrete nested type so callers can access its members.
                    // An explicit interface implementation is emitted separately to satisfy
                    // the interface contract (which uses the erased constraint type).
                    if(resolvedType is TypeAliasType aliasFromExprMap)
                    {
                        var emittedType = ResolveAliasForEmission(aliasFromExprMap, namedRef, isValueContext);
                        return namedRef.ArrayRank is not null ? $"{emittedType}[]" : emittedType;
                    }
                    // ── refinement-types feature: type erasure (Requirement 12.1) ──
                    // Refinement types are erased to their base type in C#. For example,
                    // `AdultAge x = 20;` emits as `int x = 20;`. The refinement predicate
                    // is enforced by the type checker — no alias name appears in C# output.
                    if(resolvedType is RefinementType refinementFromExprMap)
                    {
                        var emittedType = CSharpTypeMap.GetTypeName(refinementFromExprMap.BaseType, isValueContext);
                        return namedRef.ArrayRank is not null ? $"{emittedType}[]" : emittedType;
                    }
                    // ── Union type alias: emit as nullable or helper struct name ──
                    // When a type alias resolves to a UnionType (e.g., `type AgeRange = int >= 3 | byte >= 0`),
                    // check if it's a two-constituent void union first (emit as Nullable<T> or T),
                    // otherwise emit the generated helper type name (e.g., `Union_Byte_Int32`).
                    if(resolvedType is UnionType unionFromExprMap)
                    {
                        var emittedType = CSharpTypeMap.TryGetOptionalTypeName(unionFromExprMap)
                            ?? UnionHelperGenerator.GetHelperTypeName(unionFromExprMap);
                        return namedRef.ArrayRank is not null ? $"{emittedType}[]" : emittedType;
                    }
                    // ── Func type from resolved alias: emit via CSharpTypeMap ──
                    // When a type alias (e.g., Predicate<int>) resolves to a ConstructedType
                    // whose generic definition is Standard.Func, emit it directly through
                    // CSharpTypeMap which handles the Func→System.Func / Action→System.Action mapping.
                    if(resolvedType is ConstructedType resolvedCtFunc
                        && resolvedCtFunc.GenericDefinition is ClassType { DisplayName: "Func", ContainingNamespace: "Standard", IsBackendBacked: true })
                    {
                        var emittedType = CSharpTypeMap.GetTypeName(resolvedCtFunc, isValueContext);
                        return namedRef.ArrayRank is not null ? $"{emittedType}[]" : emittedType;
                    }
                    // ── Task type: emit via CSharpTypeMap ──
                    // When a named type reference resolves to a ConstructedType whose generic
                    // definition is Standard.Task, emit it through CSharpTypeMap which handles
                    // Task<Void> → non-generic Task mapping.
                    if(resolvedType is ConstructedType resolvedCtTask
                        && resolvedCtTask.GenericDefinition is ClassType { DisplayName: "Task", ContainingNamespace: "Standard", IsBackendBacked: true })
                    {
                        var emittedType = CSharpTypeMap.GetTypeName(resolvedCtTask, isValueContext);
                        return namedRef.ArrayRank is not null ? $"{emittedType}[]" : emittedType;
                    }
                    // ── SpanType: emit via CSharpTypeMap ──
                    // When a named type reference resolves to a SpanType (e.g., Span<int>
                    // which resolves through the type alias Span<T> = Span<T, unknown>),
                    // emit it through CSharpTypeMap which handles the length-erasure:
                    // Span<T, Length> → System.Span<T>, ReadOnlySpan<T, Length> → System.ReadOnlySpan<T>.
                    if(resolvedType is SpanType resolvedSpanType)
                    {
                        var emittedType = CSharpTypeMap.GetTypeName(resolvedSpanType, isValueContext);
                        return namedRef.ArrayRank is not null ? $"{emittedType}[]" : emittedType;
                    }
                    // Fallback: search all symbols for a type alias with this name.
                    // Only apply if no concrete type (class/struct/abstract/newtype) with the
                    // same name exists in the current namespace — prevents cross-namespace
                    // alias collisions when a class and a type alias share the same simple
                    // name in different namespaces compiled together.
                    if(resolvedType is null)
                    {
                        // Determine the arity of the type reference (number of type arguments).
                        var refArity = 0;
                        if(namedRef.TypeArgs is not null)
                        {
                            refArity = 1; // First type argument
                            refArity += namedRef.TypeArgs.TypeArguments.Length;
                        }

                        // Look up type alias symbols matching by name and arity.
                        // For TypeAliasType, match arity to disambiguate overloaded aliases
                        // (e.g., Action vs Action<T1> vs Action<T1, T2>).
                        var aliasSymbol = model.AllSymbols
                            .FirstOrDefault(s => s.Name == name && s.Kind == SymbolKind.TypeAlias
                                && ((s.DeclaredType is TypeAliasType at && at.TypeParameters.Count == refArity)
                                    || s.DeclaredType is RefinementType
                                    || s.DeclaredType is UnionType));
                        if(aliasSymbol?.DeclaredType is TypeAliasType aliasType)
                        {
                            // Check that no concrete type with this name exists in the
                            // current namespace (which would take precedence over an alias
                            // from a different namespace). Newtypes are checked by scope
                            // since NewtypeType does not carry a ContainingNamespace property.
                            var hasConcreteTypeInCurrentNamespace = currentNamespace is not null
                                && model.AllSymbols.Any(s => s.Name == name
                                    && s.Kind is SymbolKind.Class or SymbolKind.Struct or SymbolKind.Abstract
                                    && GetSymbolContainingNamespace(s) == currentNamespace);

                            // Also check for newtypes: a newtype with the same name in any
                            // namespace takes precedence over a type alias from a different
                            // namespace, since newtypes are nominal (not transparent).
                            if(!hasConcreteTypeInCurrentNamespace && currentNamespace is not null)
                            {
                                hasConcreteTypeInCurrentNamespace = model.AllSymbols.Any(s =>
                                    s.Name == name && s.Kind == SymbolKind.Newtype
                                    && GetNewtypeContainingNamespace(s) == currentNamespace);
                            }

                            if(!hasConcreteTypeInCurrentNamespace)
                            {
                                var emittedType = ResolveAliasForEmission(aliasType, namedRef, isValueContext);
                                return namedRef.ArrayRank is not null ? $"{emittedType}[]" : emittedType;
                            }
                        }
                        // Refinement type alias fallback: same logic as TypeAliasType above.
                        if(aliasSymbol?.DeclaredType is RefinementType refinementType)
                        {
                            var hasConcreteTypeInCurrentNamespace = currentNamespace is not null
                                && model.AllSymbols.Any(s => s.Name == name
                                    && s.Kind is SymbolKind.Class or SymbolKind.Struct or SymbolKind.Abstract
                                    && GetSymbolContainingNamespace(s) == currentNamespace);
                            if(!hasConcreteTypeInCurrentNamespace)
                            {
                                var emittedType = CSharpTypeMap.GetTypeName(refinementType.BaseType, isValueContext);
                                return namedRef.ArrayRank is not null ? $"{emittedType}[]" : emittedType;
                            }
                        }
                        // Union type alias fallback: emit as nullable or helper struct name.
                        if(aliasSymbol?.DeclaredType is UnionType unionFallback)
                        {
                            var hasConcreteTypeInCurrentNamespace = currentNamespace is not null
                                && model.AllSymbols.Any(s => s.Name == name
                                    && s.Kind is SymbolKind.Class or SymbolKind.Struct or SymbolKind.Abstract
                                    && GetSymbolContainingNamespace(s) == currentNamespace);
                            if(!hasConcreteTypeInCurrentNamespace)
                            {
                                var emittedType = CSharpTypeMap.TryGetOptionalTypeName(unionFallback)
                                    ?? UnionHelperGenerator.GetHelperTypeName(unionFallback);
                                return namedRef.ArrayRank is not null ? $"{emittedType}[]" : emittedType;
                            }
                        }
                    }

                    // Handle "Void" identifier as equivalent to the void keyword.
                    if(name == "Void" && namedRef.TypeArgs is null && namedRef.ArrayRank is null)
                    {
                        if(isValueContext)
                        {
                            NeedsMistVoidStruct = true;
                            return "MistVoid";
                        }

                        return "void";
                    }

                    // Check if this named type is a stdlib type that maps to a
                    // C# fully-qualified name (e.g., "Type" → "System.Type").
                    // This ensures stdlib types emit correctly without requiring
                    // a `using` directive in the generated C#.
                    var mappedName = CSharpTypeMap.TryGetMappedName(name);
                    if(mappedName is not null && namedRef.TypeArgs is null)
                    {
                        return namedRef.ArrayRank is not null ? $"{mappedName}[]" : mappedName;
                    }
                    // When a mapped name exists AND there are type arguments (e.g., Task<string>),
                    // emit the mapped name with the type arguments (e.g., System.Threading.Tasks.Task<string>).
                    if(mappedName is not null && namedRef.TypeArgs is not null)
                    {
                        // Special case: Task<void> → non-generic Task.
                        // C# uses `Task` (not `Task<void>`) for async operations with no return value.
                        if(name == "Task" && namedRef.TypeArgs.TypeArguments.Length == 0)
                        {
                            var singleArg = namedRef.TypeArgs.TypeArgument;
                            if(singleArg is TypeReferenceTypeArgumentTypeArgumentNode typeRefArg
                                && typeRefArg.Type.SingleTypeRefs.Length == 0
                                && typeRefArg.Type.First is VoidSingleTypeRefSingleTypeRefNode)
                            {
                                return namedRef.ArrayRank is not null
                                    ? "System.Threading.Tasks.Task[]"
                                    : "System.Threading.Tasks.Task";
                            }
                        }

                        var mappedTypeArgs = EmitTypeArgumentList(namedRef.TypeArgs, GetTypeParamsForName(name));
                        var mappedResult = $"{mappedName}{mappedTypeArgs}";
                        return namedRef.ArrayRank is not null ? $"{mappedResult}[]" : mappedResult;
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
                    if(namedRef.TypeArgs is not null
                        && TypeReferenceContainsUnknown(typeRef))
                    {
                        return namedRef.ArrayRank is not null ? "dynamic[]" : "dynamic";
                    }

                    // Check if this named type reference is a dependent type parameter
                    // (e.g., `N` in `FixedBuffer<N>` where N is a method-level dependent param).
                    // Dependent type parameters are emitted with a T-prefix in C#.
                    if(namedRef.TypeArgs is null && namedRef.ArrayRank is null)
                    {
                        var resolvedNamedType = model.GetTypeOf(typeRef);
                        if(resolvedNamedType is DependentTypeParameterType depParamType)
                        {
                            return $"T{depParamType.Name}";
                        }
                    }

                    var typeArgs = EmitTypeArgumentList(namedRef.TypeArgs, GetTypeParamsForName(name));
                    if(namedRef.ArrayRank is not null)
                    {
                        var arrayType = $"{name}{typeArgs}[]";
                        // Task 14.6: NamedType[]? → emit with optional suffix for value types
                        return namedRef.Optional is not null ? $"{arrayType}?" : arrayType;
                    }
                    // Task 14.6: When the optional suffix is present on a named type reference
                    // and the semantic model path didn't handle it (GetTypeOf returned null),
                    // look up the declared symbol to determine if it's a value type or reference type.
                    // Value types → T?, reference types → plain T (null represents void).
                    if(namedRef.Optional is not null)
                    {
                        var baseName = $"{name}{typeArgs}";
                        // Look up the type to determine if it's a value type.
                        var declaredSymbol = model.AllSymbols
                            .FirstOrDefault(s => s.Name == name && s.Kind is SymbolKind.Struct or SymbolKind.Class or SymbolKind.Enum);
                        if(declaredSymbol?.DeclaredType is not null && CSharpTypeMap.IsValueType(declaredSymbol.DeclaredType))
                        {
                            return $"{baseName}?";
                        }
                        // Reference types: emit plain T (null represents void).
                        return baseName;
                    }

                    return $"{name}{typeArgs}";
                }
            case FuncSingleTypeRefSingleTypeRefNode:
                {
                    // The `func<T1, ..., TN>` keyword form resolves to Standard.Func<T1, ..., TN>
                    // in the semantic model. Use the resolved type to emit via CSharpTypeMap,
                    // which handles the Func→System.Func / Action→System.Action mapping.
                    var resolvedFuncType = model.GetTypeOf(typeRef);
                    if(resolvedFuncType is not null and not ErrorType)
                    {
                        return CSharpTypeMap.GetTypeName(resolvedFuncType, isValueContext);
                    }
                    // Fallback: emit type arguments manually through the Func mapping path.
                    var funcTypeArgs = EmitTypeArgumentList(((FuncSingleTypeRefSingleTypeRefNode)typeRef.First).TypeArgs);
                    return $"System.Func{funcTypeArgs}";
                }
            case TupleSingleTypeRefSingleTypeRefNode tupleRef:
                {
                    // Emit C# tuple type syntax: (T1, T2) or (T1 name1, T2 name2).
                    var parts = new List<string>();

                    string formatTupleElement(TupleTypeElementNode element)
                    {
                        var type = EmitTypeReference(element.Type, isValueContext);
                        return element.Name.HasValue ? $"{type} {element.Name.Value.Lexeme}" : type;
                    }

                    parts.Add(formatTupleElement(tupleRef.First));
                    parts.Add(formatTupleElement(tupleRef.Rest));
                    foreach(var additional in tupleRef.TupleTypeElements)
                    {
                        parts.Add(formatTupleElement(additional));
                    }

                    return $"({string.Join(", ", parts)})";
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
        switch(singleTypeRef)
        {
            case PrimitiveSingleTypeRefSingleTypeRefNode primitiveRef:
                {
                    var name = GetPrimitiveDisplayName(primitiveRef.PrimitiveType);
                    return primitiveRef.ArrayRank is not null ? $"{name}Array" : name;
                }
            case VoidSingleTypeRefSingleTypeRefNode:
                return "Void";
            case UnknownSingleTypeRefSingleTypeRefNode:
                return "Object";
            case NamedSingleTypeRefSingleTypeRefNode namedRef:
                {
                    var name = ExtractQualifiedName(namedRef.Name);
                    if(namedRef.TypeArgs is not null)
                    {
                        var typeArgNames = new List<string>();
                        CollectTypeArgConstituentNames(namedRef.TypeArgs, typeArgNames);
                        name = $"{name}_{typeArgNames.Count}_{string.Join("_", typeArgNames)}";
                    }

                    return namedRef.ArrayRank is not null ? $"{name}Array" : name;
                }
            case ParenthesizedTypeRefSingleTypeRefNode parenRef:
                {
                    // Parenthesized type refs contain an inner TypeReference.
                    // For union helper naming, we recurse into the inner type.
                    var innerName = GetConstituentNameFromTypeRef(parenRef.Inner);
                    return parenRef.ArrayRank is not null ? $"{innerName}Array" : innerName;
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
        // Nested union (non-empty continuation list) — flatten by returning the
        // helper type name as the constituent name.
        if(typeRef.SingleTypeRefs.Length > 0)
        {
            var unionNames = new List<string>
            {
                GetConstituentNameForHelper(typeRef.First)
            };
            foreach(var rest in typeRef.SingleTypeRefs)
            {
                unionNames.Add(GetConstituentNameForHelper(rest));
            }

            unionNames.Sort(StringComparer.Ordinal);
            return "Union_" + string.Join("_", unionNames);
        }

        switch(typeRef.First)
        {
            case PrimitiveSingleTypeRefSingleTypeRefNode primitiveRef:
                {
                    var name = GetPrimitiveDisplayName(primitiveRef.PrimitiveType);
                    return primitiveRef.ArrayRank is not null ? $"{name}Array" : name;
                }
            case VoidSingleTypeRefSingleTypeRefNode:
                return "Void";
            case UnknownSingleTypeRefSingleTypeRefNode:
                return "Object";
            case NamedSingleTypeRefSingleTypeRefNode namedRef:
                {
                    var name = ExtractQualifiedName(namedRef.Name);
                    if(namedRef.TypeArgs is not null)
                    {
                        var typeArgNames = new List<string>();
                        CollectTypeArgConstituentNames(namedRef.TypeArgs, typeArgNames);
                        name = $"{name}_{typeArgNames.Count}_{string.Join("_", typeArgNames)}";
                    }

                    return namedRef.ArrayRank is not null ? $"{name}Array" : name;
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
        foreach(var additional in typeArgs.TypeArguments)
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
        if(arg is TypeReferenceTypeArgumentTypeArgumentNode typeRefArg)
        {
            return GetConstituentNameFromTypeRef(typeRefArg.Type);
        }

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
    private string EmitTupleTypePatternType(TupleSingleTypeRefSingleTypeRefNode tupleRef)
    {
        var parts = new List<string>
        {
            EmitTypeReference(tupleRef.First.Type),
            EmitTypeReference(tupleRef.Rest.Type)
        };
        foreach(var additional in tupleRef.TupleTypeElements)
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
    private static bool TupleTypeRefHasAnyElementName(TupleSingleTypeRefSingleTypeRefNode tupleRef)
    {
        if(tupleRef.First.Name.HasValue)
        {
            return true;
        }

        if(tupleRef.Rest.Name.HasValue)
        {
            return true;
        }

        foreach(var additional in tupleRef.TupleTypeElements)
        {
            if(additional.Name.HasValue)
            {
                return true;
            }
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
    private string EmitTupleDeconstructionPattern(TupleSingleTypeRefSingleTypeRefNode tupleRef)
    {
        string formatElement(TupleTypeElementNode element)
        {
            var elementType = EmitTypeReference(element.Type);
            return element.Name.HasValue
                ? $"{elementType} {element.Name.Value.Lexeme}"
                : elementType;
        }

        var parts = new List<string>
        {
            formatElement(tupleRef.First),
            formatElement(tupleRef.Rest)
        };
        foreach(var additional in tupleRef.TupleTypeElements)
        {
            parts.Add(formatElement(additional));
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
        indentLevel++;
        EmitBlockStatements(block);
        indentLevel--;
        AppendLine("}");
    }

    /// <summary>
    /// Emits the statements within a block (including pending union pattern bindings)
    /// without emitting the surrounding braces or adjusting indentation.
    /// Sets up the discarded-task lookup so that <see cref="EmitStatementsFromIndex"/>
    /// can route discarded-task expression statements through the unified drop
    /// try/finally wrapping mechanism.
    /// </summary>
    private void EmitBlockStatements(BlockNode block)
    {
        // Inject any pending union pattern variable bindings at the start of the block.
        // These are queued by TryEmitUnionPatternMatch when emitting `if (x.IsT)` conditions.
        if(pendingUnionPatternBindings.Count > 0)
        {
            foreach(var binding in pendingUnionPatternBindings)
            {
                AppendLine(binding);
            }

            pendingUnionPatternBindings.Clear();
        }

        // Query the semantic model for discarded tasks in this block scope.
        var liveTasks = model.GetDiscardedTasks(block);

        // Set up the discarded task lookup so EmitStatementsFromIndex can detect
        // discarded-task expression statements and route them through the unified
        // drop try/finally wrapping (Requirements 7.9.3, 7.3, 7.10, 14.5).
        var previousDiscardedTasks = currentBlockDiscardedTasks;
        if(liveTasks.Count > 0)
        {
            currentBlockDiscardedTasks = new Dictionary<Storm.Runtime.SyntaxNode, DiscardedTaskInfo>();
            foreach(var task in liveTasks)
            {
                // Discarded tasks have synthetic __task_N names and their origin
                // is the expression node (not a variable declaration node).
                if(task.GeneratedVariableName.StartsWith("__task_", StringComparison.Ordinal))
                {
                    currentBlockDiscardedTasks[task.OriginNode] = task;
                }
            }
        }
        else
        {
            currentBlockDiscardedTasks = null;
        }

        EmitStatementsFromIndex(block.Statements, 0);

        currentBlockDiscardedTasks = previousDiscardedTasks;
    }

    /// <summary>
    /// Emits statements starting from the given index. When a context declaration
    /// statement is encountered, it emits the declaration/push, wraps the remaining
    /// statements in a try/finally block, and pops in the finally clause.
    /// Multiple context declarations in the same scope nest try/finally blocks
    /// so that pops happen in reverse declaration order.
    /// Discarded-task expression statements are also routed through the unified
    /// drop try/finally mechanism: captured as <c>var __task_N = expr;</c> and
    /// awaited in the finally clause (Requirements 7.9.3, 14.5).
    /// </summary>
    private void EmitStatementsFromIndex(ImmutableArray<StatementNode> statements, int startIndex)
    {
        for(var i = startIndex; i < statements.Length; i++)
        {
            var stmt = statements[i];

            // Check if this statement is a context declaration that needs try/finally wrapping.
            var contextTypeName = TryGetContextDeclarationTypeName(stmt);
            if(contextTypeName is not null)
            {
                EmitContextDeclarationWithWrapping(stmt, contextTypeName, statements, i + 1);
                return; // Remaining statements are emitted inside the try block.
            }

            // Auto-drop insertion (Requirements 4.1, 4.4, 4.5, 14.1-14.3): when this
            // statement declares a linear/relevant local with a Drop() obligation, emit
            // the declaration and then wrap the remaining statements in a try/finally so
            // the scope-exit Drop() fires even when an exception is thrown. Nesting these
            // wrappers produces reverse declaration order (last declared, first dropped).
            if(TryGetScopeExitDropLocalName(stmt, out var dropLocalName, out var dropLocalIsTaskLike))
            {
                EmitDroppableLocalWithWrapping(stmt, dropLocalName, dropLocalIsTaskLike, statements, i + 1);
                return; // Remaining statements are emitted inside the try block.
            }

            // Discarded-task unification (Requirements 7.9.3, 7.3, 7.10, 14.5): when this
            // statement is a bare async call whose Task result is discarded, emit it as
            // a synthetic variable declaration and wrap remaining statements in try/finally
            // with await in the finally clause — same mechanism as auto-drop for named
            // task locals.
            if(TryGetDiscardedTaskInfo(stmt, out var discardedTask))
            {
                EmitDiscardedTaskWithWrapping(stmt, discardedTask!, statements, i + 1);
                return; // Remaining statements are emitted inside the try block.
            }

            EmitStatement(stmt);
        }
    }

    /// <summary>
    /// Emits a variable declaration for a linear/relevant local with a <c>Drop()</c>
    /// obligation, followed by a try/finally block wrapping the remaining statements in
    /// the scope. The <c>finally</c> clause emits the scope-exit <c>Drop()</c> call (or
    /// <c>await</c> for Task/ValueTask types) so the obligation is satisfied even on
    /// exception (Requirement 14.3). Multiple droppable locals nest, producing reverse
    /// declaration order (Requirements 4.5, 13.1).
    ///
    /// Drop elision (Requirements 4.2, 4.3): when the variable is unconditionally moved
    /// or returned, the try/finally is skipped entirely.
    ///
    /// Conditional-move branch sensitivity (Requirements 4.6, 13.5): when the variable is
    /// moved in one branch of an if/else but not the other, Drop() is inserted only in the
    /// branches where the variable was NOT moved.
    /// </summary>
    private void EmitDroppableLocalWithWrapping(
        StatementNode declStmt,
        string localName,
        bool isTaskLike,
        ImmutableArray<StatementNode> statements,
        int nextIndex)
    {
        // Analyze the drop disposition of this variable in the remaining statements.
        var disposition = AnalyzeDropDisposition(localName, statements, nextIndex);

        // Relevant types always drop (idempotent) — they are never elided.
        // Only linear types participate in elision/conditional logic.
        var declType = GetDeclaredTypeFromStatement(declStmt);
        var isRelevant = declType is not null && GetDisciplineForType(declType) == SubstructuralDiscipline.Relevant;
        if(isRelevant)
        {
            disposition = DropDisposition.AlwaysLive;
        }

        switch(disposition)
        {
            case DropDisposition.UnconditionallyMoved:
                // Drop elision: the variable is moved/returned in all paths.
                // Emit the declaration and remaining statements without try/finally wrapping.
                EmitStatement(declStmt);
                EmitStatementsFromIndex(statements, nextIndex);
                break;

            case DropDisposition.ConditionallyMoved:
                // Branch-sensitive drop: the variable is moved in some branches but not others.
                // Register it for per-branch drop injection. Emit a boolean flag, then wrap
                // remaining statements in try/finally with the flag-guarded drop in finally.
                EmitStatement(declStmt);
                var flagName = $"__dropped_{localName}";
                pendingBranchDrops[localName] = isTaskLike;
                AppendLine($"var {flagName} = false;");
                AppendLine("try");
                AppendLine("{");
                indentLevel++;
                EmitStatementsFromIndex(statements, nextIndex);
                indentLevel--;
                AppendLine("}");
                AppendLine("finally");
                AppendLine("{");
                indentLevel++;
                // Guard: only drop if the variable wasn't already moved/dropped in a branch.
                // Apply recursive drop chaining (Requirement 2.3).
                {
                    // The fallback $"{localName}.Drop()" path only fires when declType is null,
                    // which cannot happen for properly-projected CLR types (they always resolve
                    // to a ClassType/StructType). IDisposable dispatch is therefore not needed here.
                    var chainedExpr = declType is not null
                        ? EmitChainedDropExpression(declType, localName)
                        : isTaskLike ? $"await {localName}" : $"{localName}.Drop()";
                    AppendLine($"if (!{flagName}) {chainedExpr};");
                }

                indentLevel--;
                AppendLine("}");
                _ = pendingBranchDrops.Remove(localName);
                break;

            case DropDisposition.AlwaysLive:
            default:
                // Standard behavior: try/finally with unconditional drop.
                // Apply recursive drop chaining (Requirement 2.3).
                EmitStatement(declStmt);
                AppendLine("try");
                AppendLine("{");
                indentLevel++;
                EmitStatementsFromIndex(statements, nextIndex);
                indentLevel--;
                AppendLine("}");
                AppendLine("finally");
                AppendLine("{");
                indentLevel++;
                {
                    // The fallback $"{localName}.Drop()" path only fires when declType is null,
                    // which cannot happen for properly-projected CLR types (they always resolve
                    // to a ClassType/StructType). IDisposable dispatch is therefore not needed here.
                    var chainedExpr = declType is not null
                        ? EmitChainedDropExpression(declType, localName)
                        : isTaskLike ? $"await {localName}" : $"{localName}.Drop()";
                    AppendLine($"{chainedExpr};");
                }

                indentLevel--;
                AppendLine("}");
                break;
        }
    }

    /// <summary>
    /// Checks if the given statement is a discarded-task expression statement — a bare
    /// async call whose Task result is not assigned to a variable. Returns the associated
    /// <see cref="DiscardedTaskInfo"/> through <paramref name="taskInfo"/> when found.
    /// </summary>
    private bool TryGetDiscardedTaskInfo(StatementNode stmt, out DiscardedTaskInfo? taskInfo)
    {
        taskInfo = null;
        if(currentBlockDiscardedTasks is null)
        {
            return false;
        }

        if(stmt is not ExpressionStatementStatementNode exprStmt)
        {
            return false;
        }

        return currentBlockDiscardedTasks.TryGetValue(
            exprStmt.ExpressionStatement.Expression, out taskInfo);
    }

    /// <summary>
    /// Emits a discarded-task expression statement as a synthetic variable declaration
    /// (<c>var __task_N = expr;</c>) wrapped in a try/finally block with <c>await __task_N;</c>
    /// in the finally clause. This routes discarded tasks through the same unified drop
    /// mechanism used for named task locals (Requirements 7.9.3, 7.3, 7.10, 14.5).
    /// Multiple discarded tasks in the same block nest try/finally blocks, producing
    /// reverse-declaration-order await at scope exit.
    /// </summary>
    private void EmitDiscardedTaskWithWrapping(
        StatementNode exprStmt,
        DiscardedTaskInfo taskInfo,
        ImmutableArray<StatementNode> statements,
        int nextIndex)
    {
        // Emit the synthetic variable declaration: var __task_N = expr;
        var exprNode = ((ExpressionStatementStatementNode)exprStmt).ExpressionStatement.Expression;
        var taskExpr = EmitExpression(exprNode);
        AppendLine($"var {taskInfo.GeneratedVariableName} = {taskExpr};");

        // Wrap remaining statements in try/finally with chained drop in the finally.
        // Apply recursive drop chaining (Requirement 2.3).
        AppendLine("try");
        AppendLine("{");
        indentLevel++;
        EmitStatementsFromIndex(statements, nextIndex);
        indentLevel--;
        AppendLine("}");
        AppendLine("finally");
        AppendLine("{");
        indentLevel++;
        var chainedExpr = EmitChainedDropExpression(taskInfo.TaskType, taskInfo.GeneratedVariableName);
        AppendLine($"{chainedExpr};");
        indentLevel--;
        AppendLine("}");
    }

    /// <summary>
    /// Returns the declared type from a variable declaration statement, or <c>null</c> if
    /// it cannot be determined.
    /// </summary>
    private IType? GetDeclaredTypeFromStatement(StatementNode stmt)
    {
        if(stmt is not VariableDeclarationStatementStatementNode varDeclStmt)
        {
            return null;
        }

        return varDeclStmt.VariableDeclaration switch
        {
            TypedVariableDeclarationVariableDeclarationStatementNode typed =>
                (model.GetDeclaredSymbol(typed) as Symbol)?.DeclaredType,
            InferredVariableDeclarationVariableDeclarationStatementNode inferred =>
                (model.GetDeclaredSymbol(inferred) as Symbol)?.DeclaredType,
            _ => null,
        };
    }

    /// <summary>
    /// Returns the substructural discipline for a type.
    /// </summary>
    private static SubstructuralDiscipline GetDisciplineForType(IType type)
    {
        var resolved = type is ConstructedType constructed ? constructed.GenericDefinition : type;
        return resolved switch
        {
            ClassType ct => ct.Discipline,
            StructType st => st.Discipline,
            _ => SubstructuralDiscipline.None,
        };
    }

    /// <summary>
    /// Determines whether the given statement declares a single local of a linear or
    /// relevant type with a <c>Drop()</c> method, making it eligible for scope-exit
    /// auto-drop. Returns the C# local name through <paramref name="localName"/> when so.
    /// Affine and unrestricted locals never auto-drop at scope exit (Requirement 4.8).
    /// For <c>Task</c>/<c>ValueTask</c> types, <paramref name="isTaskLike"/> is set to
    /// <c>true</c> so the emitter knows to emit <c>await</c> instead of <c>.Drop()</c>
    /// (Requirements 7.10, 8.8, 14.5, 14.6).
    /// </summary>
    private bool TryGetScopeExitDropLocalName(StatementNode stmt, out string localName, out bool isTaskLike)
    {
        localName = "";
        isTaskLike = false;

        if(stmt is not VariableDeclarationStatementStatementNode varDeclStmt)
        {
            return false;
        }

        // Only simple single-name declarations participate in scope-exit auto-drop.
        switch(varDeclStmt.VariableDeclaration)
        {
            case TypedVariableDeclarationVariableDeclarationStatementNode typed:
                {
                    if((model.GetDeclaredSymbol(typed) as Symbol)?.DeclaredType is not { } typedType
                        || !ShouldAutoInsertScopeExitDrop(typedType))
                    {
                        return false;
                    }

                    localName = typed.Name.Lexeme;
                    isTaskLike = IsTaskLikeType(typedType);
                    return true;
                }
            case InferredVariableDeclarationVariableDeclarationStatementNode inferred:
                {
                    if((model.GetDeclaredSymbol(inferred) as Symbol)?.DeclaredType is not { } inferredType
                        || !ShouldAutoInsertScopeExitDrop(inferredType))
                    {
                        return false;
                    }

                    localName = inferred.Name.Lexeme;
                    isTaskLike = IsTaskLikeType(inferredType);
                    return true;
                }
            default:
                return false;
        }
    }

    /// <summary>
    /// Returns <c>true</c> when a local of the given type should have a <c>Drop()</c> call
    /// auto-inserted at scope exit: the type has a <c>Drop()</c> method and its discipline
    /// is <c>Linear</c> or <c>Relevant</c>. Affine and unrestricted types are excluded
    /// (they only drop within <c>with</c> blocks). Task/ValueTask are included — the emitter
    /// emits <c>await</c> instead of <c>.Drop()</c> for them (Requirements 7.10, 8.8, 14.5).
    /// </summary>
    private static bool ShouldAutoInsertScopeExitDrop(IType type)
    {
        var resolved = type is ConstructedType constructed ? constructed.GenericDefinition : type;
        var (discipline, hasDrop) = resolved switch
        {
            ClassType ct => (ct.Discipline, ct.HasDrop),
            StructType st => (st.Discipline, st.HasDrop),
            _ => (SubstructuralDiscipline.None, false),
        };

        return hasDrop
            && discipline is SubstructuralDiscipline.Linear or SubstructuralDiscipline.Relevant;
    }

    /// <summary>
    /// Returns <c>true</c> when the type is the standard-library <c>Task</c> or
    /// <c>ValueTask</c> (generic or void alias). Used to determine whether auto-drop
    /// and explicit <c>Drop()</c> calls should emit <c>await</c> instead of <c>.Drop()</c>
    /// (Requirements 7.10, 8.8, 14.5, 14.6).
    /// </summary>
    private static bool IsTaskLikeType(IType type)
    {
        var resolved = type is ConstructedType constructed ? constructed.GenericDefinition : type;
        return resolved is ClassType { ContainingNamespace: "Standard" } ct
            && ct.DisplayName is "Task" or "ValueTask";
    }

    // ── Manual Drop() body completion (Requirements 10.7, 10.8) ────────────────

    /// <summary>
    /// When the current method is a manual <c>Drop()</c> on a type with droppable fields,
    /// emits <c>Drop()</c> calls for any droppable fields that were not explicitly dropped
    /// in the body. The remaining fields are dropped in declaration order at the end of
    /// the method body (Requirement 10.8).
    /// </summary>
    private void EmitRemainingFieldDropsIfManualDrop(BlockNode block)
    {
        // Only applies when we're inside a named Drop() method with zero parameters.
        if(currentMethodSymbol is null
            || currentMethodSymbol.Name != "Drop"
            || currentMethodSymbol.Kind != SymbolKind.Method)
        {
            return;
        }

        // Find the enclosing type. The method's ContainingScope is the type's member scope.
        var enclosingType = FindEnclosingTypeForMethod(currentMethodSymbol);
        if(enclosingType is null)
        {
            return;
        }

        // Only process types with synthesizable droppable fields but NO SyntheticDropFields
        // (i.e., the type has a manual Drop() — SyntheticDropFields is null for these).
        var syntheticFields = enclosingType switch
        {
            ClassType ct => ct.SyntheticDropFields,
            StructType st => st.SyntheticDropFields,
            _ => null,
        };
        if(syntheticFields is not null)
        {
            return; // Fully-synthesized Drop() — this body shouldn't exist.
        }

        // Collect all droppable fields in declaration order.
        var droppableFields = CollectDroppableFields(enclosingType);
        if(droppableFields.Count == 0)
        {
            return;
        }

        // Determine which fields were explicitly dropped in the body.
        var explicitlyDropped = CollectExplicitlyDroppedFieldNames(block);

        // Collect the remaining un-dropped fields.
        var remaining = new List<ISymbol>();
        foreach(var field in droppableFields)
        {
            if(!explicitlyDropped.Contains(field.Name))
            {
                remaining.Add(field);
            }
        }

        if(remaining.Count == 0)
        {
            return;
        }

        // Emit Drop() for remaining un-dropped fields in declaration order.
        // Apply recursive drop chaining (Requirement 2.3).
        // Skip task-like fields (Task<T>, ValueTask<T>) — their "drop" would require
        // await, which changes the method's async signature. If the user's manual Drop()
        // doesn't explicitly handle them, they are intentionally abandoned.
        foreach(var field in remaining)
        {
            var fieldType = field.DeclaredType;
            if(fieldType is not null)
            {
                if(IsTaskLikeType(fieldType))
                {
                    continue;
                }
                // For type parameter fields (e.g., T Value in a generic class), we cannot
                // call .Drop() directly in C# because T has no Drop constraint. Use dynamic
                // dispatch to call Drop at runtime.
                if(fieldType is TypeParameterType)
                {
                    AppendLine($"((dynamic)this.{field.Name}).Drop();");
                }
                else
                {
                    var chainedExpr = EmitChainedDropExpression(fieldType, $"this.{field.Name}");
                    AppendLine($"{chainedExpr};");
                }
            }
            else
            {
                AppendLine($"this.{field.Name}.Drop();");
            }
        }
    }

    /// <summary>
    /// Finds the enclosing class or struct type for a method symbol by walking
    /// up the scope chain (the method's <c>ContainingScope</c> is the type's member scope).
    /// </summary>
    private static IType? FindEnclosingTypeForMethod(ISymbol methodSymbol)
    {
        var typeScope = methodSymbol.ContainingScope;
        var parentScope = typeScope?.Parent;
        if(parentScope is null)
        {
            return null;
        }

        foreach(var member in parentScope.Members)
        {
            if(member.Kind == SymbolKind.Class && member.DeclaredType is ClassType ct
                && ReferenceEquals(ct.MemberScope, typeScope))
            {
                return ct;
            }

            if(member.Kind == SymbolKind.Struct && member.DeclaredType is StructType st
                && ReferenceEquals(st.MemberScope, typeScope))
            {
                return st;
            }
        }

        return null;
    }

    /// <summary>
    /// Collects all droppable fields (fields whose resolved type has <c>HasDrop == true</c>)
    /// from the given type, in declaration order. Mirrors the field selection logic in
    /// <see cref="SyntheticDropInferenceEngine"/>.
    /// </summary>
    private static List<ISymbol> CollectDroppableFields(IType type)
    {
        var memberScope = type switch
        {
            ClassType ct => ct.MemberScope,
            StructType st => st.MemberScope,
            _ => null,
        };

        if(memberScope is null)
        {
            return [];
        }

        var result = new List<ISymbol>();
        foreach(var member in memberScope.Members)
        {
            if(member.Kind != SymbolKind.Field)
            {
                continue;
            }

            var fieldType = member.DeclaredType;
            if(fieldType is null)
            {
                continue;
            }

            if(TypeHasDrop(fieldType))
            {
                result.Add(member);
            }
        }

        return result;
    }

    /// <summary>
    /// Returns whether a type has a <c>Drop()</c> method, resolving through
    /// type aliases and generic constructions.
    /// </summary>
    private static bool TypeHasDrop(IType type)
    {
        return type switch
        {
            ClassType ct => ct.HasDrop,
            StructType st => st.HasDrop,
            ConstructedType constructed => TypeHasDrop(constructed.GenericDefinition),
            TypeAliasType alias => TypeHasDrop(alias.UnderlyingType),
            // Type parameters with substructural constraints (linear, affine, relevant)
            // are guaranteed to have Drop since all substructural types must have Drop.
            TypeParameterType tpt => tpt.Constraints is { } c
                && (c.HasLinearConstraint || c.HasAffineConstraint || c.HasRelevantConstraint),
            _ => false,
        };
    }

    /// <summary>
    /// Walks the AST of a <c>Drop()</c> method body and collects the names of fields
    /// that are explicitly dropped via <c>this.FieldName.Drop()</c> calls.
    /// </summary>
    private static HashSet<string> CollectExplicitlyDroppedFieldNames(BlockNode block)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        CollectExplicitFieldDropsRecursive(block, result);
        return result;
    }

    /// <summary>
    /// Recursively scans an AST node for <c>this.FieldName.Drop()</c> patterns
    /// and records the field names found.
    /// </summary>
    private static void CollectExplicitFieldDropsRecursive(Storm.Runtime.SyntaxNode node, HashSet<string> fieldNames)
    {
        // Look for postfix expressions of the form: this.Field.Drop()
        if(node is PostfixNode postfix && postfix.Suffix.Length >= 2
            && postfix.Target is ThisExpressionPrimaryNode)
        {
            var last = postfix.Suffix[^1];
            var prev = postfix.Suffix[^2];

            if(last is MethodCallSuffixPostfixSuffixNode { Arguments: null } methodCall
                && methodCall.Name.Lexeme == "Drop"
                && prev is MemberAccessSuffixPostfixSuffixNode fieldAccess)
            {
                _ = fieldNames.Add(fieldAccess.Name.Lexeme);
            }
        }

        foreach(var child in node.Children())
        {
            if(child is not null)
            {
                CollectExplicitFieldDropsRecursive(child, fieldNames);
            }
        }
    }

    // ── Drop elision and conditional-move branch sensitivity (Requirements 4.2, 4.3, 4.6, 13.4, 13.5) ──

    /// <summary>
    /// Describes how a droppable variable is consumed in the remaining statements after its
    /// declaration. Used to decide whether to emit try/finally, skip it, or inject per-branch drops.
    /// </summary>
    private enum DropDisposition
    {
        /// <summary>The variable is never moved or returned — emit try/finally with unconditional drop.</summary>
        AlwaysLive,

        /// <summary>The variable is unconditionally moved or returned in all paths — skip drop (elision).</summary>
        UnconditionallyMoved,

        /// <summary>The variable is moved in some if/else branches but not others — inject per-branch drops.</summary>
        ConditionallyMoved,
    }

    /// <summary>
    /// Scans the remaining statements to determine the drop disposition of a linear variable.
    /// Returns how the variable is consumed (never, always, or conditionally) so the emitter
    /// can decide whether to wrap in try/finally, elide, or inject per-branch drops.
    /// </summary>
    private DropDisposition AnalyzeDropDisposition(
        string localName,
        ImmutableArray<StatementNode> statements,
        int startIndex)
    {
        // Walk statements linearly. If the variable is unconditionally moved/returned
        // before any branching, it's UnconditionallyMoved. If it's moved in one branch
        // of an if/else but not the other, it's ConditionallyMoved.
        for(var i = startIndex; i < statements.Length; i++)
        {
            var stmt = statements[i];

            // Check for return of the variable.
            if(IsReturnOfVariable(stmt, localName))
            {
                return DropDisposition.UnconditionallyMoved;
            }

            // Check for explicit .Drop() call on the variable (consumes it — Requirements 3.1, 3.2).
            if(IsExplicitDropOfVariable(stmt, localName))
            {
                return DropDisposition.UnconditionallyMoved;
            }

            // Check for unconditional move (assignment to another variable or pass to owned param).
            if(IsUnconditionalMoveOfVariable(stmt, localName))
            {
                return DropDisposition.UnconditionallyMoved;
            }

            // Check for conditional move in an if/else.
            if(IsConditionalMoveOfVariable(stmt, localName))
            {
                return DropDisposition.ConditionallyMoved;
            }
        }

        return DropDisposition.AlwaysLive;
    }

    /// <summary>
    /// Returns <c>true</c> when the statement is a <c>return varName;</c> that returns
    /// the droppable variable (ownership transferred to caller — Requirement 4.3).
    /// </summary>
    private static bool IsReturnOfVariable(StatementNode stmt, string localName)
    {
        if(stmt is not ReturnStatementStatementNode returnStmt)
        {
            return false;
        }

        if(returnStmt.ReturnStatement.Value is null)
        {
            return false;
        }

        var symbol = ResolveToIdentifierName(returnStmt.ReturnStatement.Value);
        return symbol is not null && symbol == localName;
    }

    /// <summary>
    /// Returns <c>true</c> when the statement contains an explicit <c>varName.Drop()</c> call,
    /// which consumes the variable for owned types (Requirements 3.1, 3.2). This includes
    /// cases where the drop return value is captured: <c>T result = varName.Drop();</c>.
    /// </summary>
    private static bool IsExplicitDropOfVariable(StatementNode stmt, string localName)
    {
        // Check expression statement: `varName.Drop();`
        if(stmt is ExpressionStatementStatementNode exprStmt)
        {
            return ExpressionContainsDropOfVariable(exprStmt.ExpressionStatement.Expression, localName);
        }

        // Check variable declaration: `T result = varName.Drop();`
        if(stmt is VariableDeclarationStatementStatementNode varDeclStmt)
        {
            var initializer = varDeclStmt.VariableDeclaration switch
            {
                TypedVariableDeclarationVariableDeclarationStatementNode typed => typed.Initializer,
                InferredVariableDeclarationVariableDeclarationStatementNode inferred => inferred.Initializer,
                _ => null,
            };

            return initializer is not null && ExpressionContainsDropOfVariable(initializer, localName);
        }

        return false;
    }

    /// <summary>
    /// Returns <c>true</c> when the expression is (or contains) <c>varName.Drop()</c>.
    /// </summary>
    private static bool ExpressionContainsDropOfVariable(Storm.Runtime.SyntaxNode node, string localName)
    {
        if(node is PostfixNode postfix)
        {
            // Look for pattern: identifier.Drop() — target is the identifier, suffixes include
            // a MethodCallSuffix named "Drop" with no arguments (or empty arguments).
            var targetName = ResolveToIdentifierName(postfix.Target);
            if(targetName == localName)
            {
                foreach(var suffix in postfix.Suffix)
                {
                    if(suffix is MethodCallSuffixPostfixSuffixNode methodCall
                        && methodCall.Name.Lexeme == "Drop"
                        && (methodCall.Arguments is null || methodCall.Arguments.GetAllExpressions().Length == 0))
                    {
                        return true;
                    }
                }
            }
        }

        // Recurse into child nodes.
        foreach(var child in node.Children())
        {
            if(child is not null && ExpressionContainsDropOfVariable(child, localName))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns <c>true</c> when the statement unconditionally moves the variable
    /// (passes it to an owned parameter or assigns it to another owned variable).
    /// </summary>
    private bool IsUnconditionalMoveOfVariable(StatementNode stmt, string localName)
    {
        // Check expression statement: might be a call that passes the variable to an owned parameter.
        if(stmt is ExpressionStatementStatementNode exprStmt)
        {
            return ExpressionMovesVariable(exprStmt.ExpressionStatement.Expression, localName);
        }

        // Check variable declaration: `linear T x = varName;` moves the variable.
        if(stmt is VariableDeclarationStatementStatementNode varDeclStmt)
        {
            return VariableDeclarationMovesVariable(varDeclStmt.VariableDeclaration, localName);
        }

        return false;
    }

    /// <summary>
    /// Returns <c>true</c> when the statement is an if/else where the variable is moved
    /// in at least one branch but not all branches.
    /// </summary>
    private bool IsConditionalMoveOfVariable(StatementNode stmt, string localName)
    {
        if(stmt is not IfStatementStatementNode ifStmt)
        {
            return false;
        }

        var movedInThen = BlockMovesVariable(ifStmt.IfStatement.ThenBranch, localName);
        var movedInElse = ElseMovesVariable(ifStmt.IfStatement.ElseBranch, localName);

        // Conditional: moved in one branch but not the other.
        // If moved in BOTH branches → unconditional (handled separately).
        return movedInThen != movedInElse;
    }

    /// <summary>
    /// Returns <c>true</c> when the block contains a move of the named variable (passed to
    /// an owned parameter, assigned to another variable, or returned).
    /// </summary>
    private bool BlockMovesVariable(BlockNode block, string localName)
    {
        foreach(var stmt in block.Statements)
        {
            if(IsReturnOfVariable(stmt, localName))
            {
                return true;
            }

            if(IsUnconditionalMoveOfVariable(stmt, localName))
            {
                return true;
            }
            // Recurse into nested if-statements for unconditional moves within branches.
            if(stmt is IfStatementStatementNode nestedIf)
            {
                var movedInThen = BlockMovesVariable(nestedIf.IfStatement.ThenBranch, localName);
                var movedInElse = ElseMovesVariable(nestedIf.IfStatement.ElseBranch, localName);
                if(movedInThen && movedInElse)
                {
                    return true; // Moved in all branches of the nested if.
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Returns <c>true</c> when the else clause moves the named variable.
    /// If there is no else clause, the variable is NOT moved in that "branch" (the implicit
    /// fall-through path keeps it live).
    /// </summary>
    private bool ElseMovesVariable(ElseClauseNode? elseClause, string localName)
    {
        return elseClause switch
        {
            ElseBlockElseClauseNode elseBlock => BlockMovesVariable(elseBlock.ElseBlock, localName),
            ElseIfElseClauseNode elseIf => BlockMovesVariable(elseIf.ElseIf.ThenBranch, localName)
                && ElseMovesVariable(elseIf.ElseIf.ElseBranch, localName),
            _ => false, // No else → variable is live in the "else" path
        };
    }

    /// <summary>
    /// Checks whether an expression moves the named variable by passing it as an argument
    /// to a function with an owned parameter.
    /// </summary>
    private bool ExpressionMovesVariable(ExpressionNode expr, string localName)
    {
        // Walk down the expression chain to find method call suffixes.
        return ScanExpressionForMoveOfVariable(expr, localName);
    }

    /// <summary>
    /// Recursively scans an expression node (and its children) for a method call that
    /// passes the named variable to an owned parameter.
    /// </summary>
    private bool ScanExpressionForMoveOfVariable(Storm.Runtime.SyntaxNode node, string localName)
    {
        if(node is PostfixNode postfix)
        {
            foreach(var suffix in postfix.Suffix)
            {
                if(suffix is MethodCallSuffixPostfixSuffixNode methodCall)
                {
                    if(MethodCallMovesVariable(methodCall, localName))
                    {
                        return true;
                    }
                }
                else if(suffix is InvocationSuffixPostfixSuffixNode invocation)
                {
                    if(InvocationMovesVariable(invocation, postfix.Target, localName))
                    {
                        return true;
                    }
                }
            }

            // After examining all suffixes of this PostfixNode, return false.
            // The variable was not moved by any method call on this node.
            // Do NOT recurse into children — the arguments were already checked by
            // MethodCallMovesVariable/InvocationMovesVariable. Recursing could produce
            // false positives by finding the variable name in nested expressions without
            // respecting ref-kind checks.
            return false;
        }

        // Also check if this is an assignment `target = source` where source is the variable.
        if(node is AssignmentNode assignment && assignment.Value is not null)
        {
            // `target = source` — the source (rhs) is a move if it names the variable.
            if(assignment.Value.AsAssignmentOrNull() is { } rhs && rhs.Value is null)
            {
                var name = ResolveToIdentifierName(rhs.Target);
                if(name == localName)
                {
                    return true;
                }
            }
        }

        foreach(var child in node.Children())
        {
            if(child is not null && ScanExpressionForMoveOfVariable(child, localName))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns <c>true</c> when a method call passes the named variable to a by-value
    /// parameter, constituting a move. For linear/affine types (owned types), any by-value
    /// pass transfers ownership. Passes to <c>ref</c> or <c>ref readonly</c> parameters are
    /// lending operations and do NOT constitute moves (Requirements 9.1, 9.2, 9.3).
    ///
    /// When the called method's parameters cannot be resolved, falls back to searching
    /// <see cref="ISemanticModel.AllSymbols"/> by name. If still unresolvable, defaults to
    /// <c>true</c> (assumes move) for correctness with the existing tests.
    /// </summary>
    private bool MethodCallMovesVariable(MethodCallSuffixPostfixSuffixNode methodCall, string localName)
    {
        var argExprs = CollectArgumentExpressionsForMoveCheck(methodCall.Arguments);

        // Resolve the called method's parameter ref kinds.
        var calledSymbol = model.GetDeclaredSymbol(methodCall) as Symbol;

        // Fallback: search AllSymbols for the method by name.
        if(calledSymbol is null)
        {
            var methodName = methodCall.Name.Lexeme;
            foreach(var sym in model.AllSymbols)
            {
                if(sym is Symbol s && s.Kind == SymbolKind.Method && s.Name == methodName)
                {
                    calledSymbol = s;
                    break;
                }
            }
        }

        var paramRefKinds = ResolveParameterRefKinds(calledSymbol);

        for(var i = 0; i < argExprs.Count; i++)
        {
            var argName = ResolveToIdentifierName(argExprs[i]);
            if(argName == localName)
            {
                // If we resolved the parameter's ref kind and it's ref/readonly ref, this is a lend.
                if(paramRefKinds is not null && i < paramRefKinds.Count)
                {
                    return paramRefKinds[i] == RefKind.None; // Only by-value passes are moves.
                }
                // Default: assume move for owned types (preserves existing behavior for
                // methods like Sink.ConsumeHandle where params are by-value).
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns <c>true</c> when a direct invocation passes the named variable to a by-value
    /// parameter, constituting a move. Passes to <c>ref</c> or <c>ref readonly</c> parameters
    /// are lending operations and do NOT constitute moves (Requirements 9.1, 9.2, 9.3).
    ///
    /// When the called method's parameters cannot be resolved (symbol lookup fails),
    /// defaults to <c>false</c> (not a move) to ensure drop safety — a spurious auto-drop
    /// is less harmful than a missed one.
    /// </summary>
    private bool InvocationMovesVariable(InvocationSuffixPostfixSuffixNode invocation, PrimaryNode target, string localName)
    {
        var argExprs = CollectArgumentExpressionsForMoveCheck(invocation.Arguments);

        // Resolve the called method symbol.
        var calledSymbol = (model.GetDeclaredSymbol(target) ?? model.GetDeclaredSymbol(invocation)) as Symbol;

        // Fallback: search AllSymbols for the method by name and matching arg count.
        if(calledSymbol is null && target is IdentifierExpressionPrimaryNode idTarget)
        {
            _ = argExprs.Count;
            foreach(var sym in model.AllSymbols)
            {
                if(sym is Symbol s && s.Kind == SymbolKind.Method && s.Name == idTarget.Name.Lexeme
                    && s.Origin == SymbolOrigin.UserCode)
                {
                    calledSymbol = s;
                    break;
                }
            }
        }

        // If we resolved the method, check parameter ref kinds using AST fields directly.
        if(calledSymbol is not null)
        {
            Storm.Runtime.SyntaxNode? declaringNode = null;
            try { declaringNode = model.GetDeclaringNode(calledSymbol); } catch { }

            if(declaringNode is MethodDeclarationNode methodDecl && methodDecl.Parameters is { } paramList)
            {
                var paramNodes = new List<ParameterNode>();
                foreach(var child in paramList.Children())
                {
                    if(child is ParameterNode pn)
                    {
                        paramNodes.Add(pn);
                    }
                }

                for(var i = 0; i < argExprs.Count; i++)
                {
                    var argName = ResolveToIdentifierName(argExprs[i]);
                    if(argName == localName && i < paramNodes.Count)
                    {
                        // Check AST field directly for ref modifier.
                        if(paramNodes[i].RefModifier is not null)
                        {
                            return false; // ref or readonly ref → lend, not move
                        }

                        return true; // by-value → move
                    }
                }
            }
        }

        // Fallback: check if any argument is the variable and assume move.
        for(var i = 0; i < argExprs.Count; i++)
        {
            var argName = ResolveToIdentifierName(argExprs[i]);
            if(argName == localName)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Resolves the <see cref="RefKind"/> for each parameter of a called method symbol.
    /// Returns <c>null</c> if the symbol cannot be resolved or has no parameters.
    /// For CLR-projected methods, reads from <see cref="Symbol.Parameters"/>; for
    /// Mist-declared methods, reads from the declaring AST node's parameter list
    /// using the AST fields directly (refModifier + modifier).
    /// </summary>
    private IReadOnlyList<RefKind>? ResolveParameterRefKinds(Symbol? methodSymbol)
    {
        if(methodSymbol is null || methodSymbol.Kind != SymbolKind.Method)
        {
            return null;
        }

        // CLR-projected methods have Parameters populated directly.
        if(methodSymbol.Parameters.Count > 0)
        {
            var kinds = new List<RefKind>(methodSymbol.Parameters.Count);
            foreach(var param in methodSymbol.Parameters)
            {
                kinds.Add(param is Symbol ps ? ps.RefKind : RefKind.None);
            }

            return kinds;
        }

        // Mist-declared methods: get the declaring AST node and extract ref kinds from AST fields.
        Storm.Runtime.SyntaxNode declaringNode;
        try { declaringNode = model.GetDeclaringNode(methodSymbol); }
        catch { return null; }

        var paramList = declaringNode switch
        {
            MethodDeclarationNode m => m.Parameters,
            _ => null,
        };

        if(paramList is null)
        {
            return null;
        }

        var result = new List<RefKind>();
        foreach(var child in paramList.Children())
        {
            if(child is ParameterNode paramNode)
            {
                // Use AST fields directly (same logic as ExtractParameterRefKinds in the builder).
                if(paramNode.RefModifier is not null)
                {
                    result.Add(paramNode.Modifier is not null ? RefKind.RefReadonly : RefKind.Ref);
                }
                else
                {
                    result.Add(RefKind.None);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Checks whether a variable declaration initializer moves the named variable.
    /// E.g., <c>linear T x = varName;</c> moves <c>varName</c>, as does
    /// <c>linear T x = SomeFunction(varName);</c> when it passes the variable by value.
    /// </summary>
    private bool VariableDeclarationMovesVariable(VariableDeclarationStatementNode varDecl, string localName)
    {
        var initializer = varDecl switch
        {
            TypedVariableDeclarationVariableDeclarationStatementNode typed => typed.Initializer,
            InferredVariableDeclarationVariableDeclarationStatementNode inferred => inferred.Initializer,
            _ => null,
        };

        if(initializer is null)
        {
            return false;
        }

        // Check if the initializer is simply the variable name (a move-via-assignment).
        var initName = ResolveToIdentifierName(initializer);
        if(initName == localName)
        {
            // Only counts as a move if the destination variable is of an owned type.
            var declaredSymbol = model.GetDeclaredSymbol(varDecl);
            if(declaredSymbol?.DeclaredType is null)
            {
                return false;
            }

            return IsOwnedType(declaredSymbol.DeclaredType);
        }

        // Check if the initializer is a function call that passes the variable as an argument
        // (e.g., `linear T x = Sink.PassThrough(varName);`).
        return ScanExpressionForMoveOfVariable(initializer, localName);
    }

    /// <summary>
    /// Returns <c>true</c> when the type is owned (affine or linear discipline, or explicit owned flag).
    /// </summary>
    /// <summary>
    /// If the semantic model records an implicit-clone annotation for <paramref name="sourceNode"/>,
    /// wraps <paramref name="emittedExpr"/> with a <c>.Clone()</c> call — unless the destination
    /// type is a pure value type (struct/enum), in which case the clone is elided because value
    /// semantics already produce an independent copy (Req 8 AC 8, Req 14 AC 7).
    /// </summary>
    private string WrapWithImplicitCloneIfNeeded(Storm.Runtime.SyntaxNode sourceNode, string emittedExpr)
    {
        var destinationType = model.GetImplicitCloneDestination(sourceNode);
        if(destinationType is null)
        {
            return emittedExpr;
        }

        // Pure-struct optimization: value types (structs/enums) copy by value, so
        // .Clone() is redundant — elide it (Req 8 AC 8).
        if(destinationType.Kind is TypeKind.Struct or TypeKind.Enum)
        {
            return emittedExpr;
        }

        // Also check the unwrapped type for constructed generics.
        var unwrapped = destinationType is ConstructedType ct ? ct.GenericDefinition : destinationType;
        if(unwrapped.Kind is TypeKind.Struct or TypeKind.Enum)
        {
            return emittedExpr;
        }

        // Backend-backed types (Task<T>, ValueTask<T>, etc.) do not have a
        // Mist-generated .Clone() method — elide the clone call.
        if(unwrapped is ClassType { IsBackendBacked: true })
        {
            return emittedExpr;
        }

        return $"{emittedExpr}.Clone()";
    }

    /// <summary>
    /// Returns <c>true</c> if any argument expression in <paramref name="argExprs"/> has an
    /// implicit-clone annotation recorded in the semantic model. When true, the argument-list
    /// source-text fast path must be bypassed so <c>.Clone()</c> can be emitted.
    /// </summary>
    private bool ArgumentsContainImplicitClone(List<ExpressionNode> argExprs)
    {
        foreach(var expr in argExprs)
        {
            if(model.GetImplicitCloneDestination(expr) is not null)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns true if any argument expression contains an interpolated string with
    /// an expression whose type is an optional union (T | void). When true, the
    /// source-text fast path must be bypassed so the interpolation emitter can wrap
    /// nullable expressions to display "Void" for the null/void case.
    /// </summary>
    private bool ArgumentContainsOptionalInterpolation(List<ExpressionNode> argExprs)
    {
        foreach(var expr in argExprs)
        {
            if(ExpressionContainsOptionalInterpolation(expr))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Recursively checks if an expression tree contains an interpolated string
    /// with an interpolation expression whose type is an optional union (T | void).
    /// </summary>
    private bool ExpressionContainsOptionalInterpolation(Storm.Runtime.SyntaxNode node)
    {
        if(node is InterpolatedStringExpressionPrimaryNode interpolated)
        {
            foreach(var part in interpolated.InterpolatedString.Parts)
            {
                if(part is ExpressionPartInterpolatedPartNode exprPart)
                {
                    var exprType = model.GetTypeOf(exprPart.Expression);
                    if(IsOptionalUnionType(exprType))
                    {
                        return true;
                    }
                }
            }
        }

        foreach(var child in node.Children())
        {
            if(child is not null && ExpressionContainsOptionalInterpolation(child))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns true if any argument expression contains an async block expression
    /// (<c>async { return expr; }</c> or <c>async valuetask { return expr; }</c>)
    /// that requires rewriting from Mist syntax to C# Task/ValueTask construction.
    /// </summary>
    private static bool ArgumentContainsAsyncBlockExpression(List<ExpressionNode> argExprs)
    {
        foreach(var expr in argExprs)
        {
            if(ExpressionContainsAsyncBlock(expr))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ExpressionContainsAsyncBlock(Storm.Runtime.SyntaxNode node)
    {
        if(node is AsyncBlockExpressionUnaryNode or AsyncHintBlockExpressionUnaryNode)
        {
            return true;
        }

        foreach(var child in node.Children())
        {
            if(child is not null && ExpressionContainsAsyncBlock(child))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns <c>true</c> if any argument expression contains an equality comparison
    /// (<c>==</c> or <c>!=</c>) between two anonymous types. When true, the source-text
    /// fast path must be bypassed so the emitter can rewrite <c>a == b</c> to
    /// <c>object.Equals(a, b)</c> for structural comparison (C# anonymous types
    /// override Equals/GetHashCode but not operator==).
    /// </summary>
    private bool ArgumentContainsAnonymousTypeEquality(List<ExpressionNode> argExprs)
    {
        foreach(var expr in argExprs)
        {
            if(ExpressionContainsAnonymousTypeEquality(expr))
            {
                return true;
            }
        }

        return false;
    }

    private bool ExpressionContainsAnonymousTypeEquality(Storm.Runtime.SyntaxNode node)
    {
        if(node is EqualityNode equality && equality.Right is not null
            && !ReferenceEquals(equality.Right, equality.Left))
        {
            var lhsType = model.GetTypeOf(equality.Left);
            var rhsType = model.GetTypeOf(equality.Right);
            if(lhsType is AnonymousType && rhsType is AnonymousType)
            {
                return true;
            }
        }

        foreach(var child in node.Children())
        {
            if(child is not null && ExpressionContainsAnonymousTypeEquality(child))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns <c>true</c> if any argument requires delegate constructor wrapping —
    /// i.e., the parameter type is a <see cref="DelegateType"/> and the argument's
    /// type is a func/lambda/method-group (not already a DelegateType). When true,
    /// the source-text fast path must be bypassed so the expression emitter can apply
    /// <c>new DelegateName(...)</c> wrapping.
    /// </summary>
    private bool ArgumentRequiresDelegateWrapping(List<ExpressionNode> argExprs, Symbol? calledMethod)
    {
        if(calledMethod?.Parameters is not { Count: > 0 } parameters)
        {
            return false;
        }

        for(var i = 0; i < argExprs.Count && i < parameters.Count; i++)
        {
            var paramType = parameters[i].DeclaredType;
            if(paramType is not DelegateType)
            {
                continue;
            }

            // The parameter expects a named delegate. Check if the argument's type
            // is a func/lambda (not already a DelegateType value).
            var argType = model.GetTypeOf(argExprs[i]);
            if(argType is DelegateType)
            {
                continue; // DelegateType → DelegateType: no wrapping needed (same type)
            }

            // If the argument is a lambda expression or has a func type, wrapping is needed.
            if(argExprs[i] is LambdaFormExpressionNode)
            {
                return true;
            }

            if(argType is not null && SemanticModelBuilder.IsFuncType(argType))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Wraps an emitted argument in a delegate constructor invocation if the corresponding
    /// parameter type is a <see cref="DelegateType"/> and the argument is a lambda, func value,
    /// or method group. When the target is <see cref="BaseDelegateType"/>, the argument is emitted
    /// as-is (C# 11+ handles the conversion implicitly). When a <see cref="DelegateType"/> value
    /// is passed where <c>func&lt;...&gt;</c> is expected, it is also emitted as-is.
    /// </summary>
    private string WrapWithDelegateConstructorIfNeeded(ExpressionNode argExpr, string emittedArg, int argIndex, Symbol? calledMethod)
    {
        if(calledMethod?.Parameters is not { Count: > 0 } parameters)
        {
            return emittedArg;
        }

        if(argIndex >= parameters.Count)
        {
            return emittedArg;
        }

        var paramType = parameters[argIndex].DeclaredType;

        // When target is BaseDelegateType: emit as-is (C# 11+ implicit conversion).
        if(paramType is BaseDelegateType)
        {
            return emittedArg;
        }

        // When target is not a DelegateType: no wrapping needed.
        if(paramType is not DelegateType delegateType)
        {
            return emittedArg;
        }

        // When a DelegateType value is passed where DelegateType is expected: emit as-is.
        var argType = model.GetTypeOf(argExpr);
        if(argType is DelegateType)
        {
            return emittedArg;
        }

        // When the argument is a lambda, func value, or method group and the parameter
        // type is a DelegateType: wrap with new DelegateName(argument).
        if(argExpr is LambdaFormExpressionNode
            || (argType is not null && SemanticModelBuilder.IsFuncType(argType)))
        {
            var delegateTypeName = CSharpTypeMap.GetTypeName(delegateType);
            return $"new {delegateTypeName}({emittedArg})";
        }

        return emittedArg;
    }

    private static bool IsOwnedType(IType type)
    {
        var resolved = type is ConstructedType constructed ? constructed.GenericDefinition : type;
        return resolved switch
        {
            ClassType ct => ct.IsOwnedType,
            StructType st => st.IsOwnedType,
            _ => false,
        };
    }

    /// <summary>
    /// Resolves an expression (or syntax node) to its simple identifier name if it references
    /// a local variable or parameter. Returns <c>null</c> for anything that is not a bare identifier.
    /// </summary>
    private static string? ResolveToIdentifierName(Storm.Runtime.SyntaxNode node)
    {
        switch(node)
        {
            case ExpressionNode expr:
                return expr.AsAssignmentOrNull() is { } exprAssignment
                    ? ResolveToIdentifierName(exprAssignment)
                    : null;

            case AssignmentNode assignment:
                return assignment.Value is not null
                    ? null
                    : ResolveToIdentifierName(assignment.Target);

            case PostfixNode postfix:
                return postfix.Suffix.Length > 0
                    ? null
                    : ResolveToIdentifierName(postfix.Target);

            case IdentifierExpressionPrimaryNode identifier:
                return identifier.Name.Lexeme;
        }

        Storm.Runtime.SyntaxNode? onlyChild = null;
        var childCount = 0;
        foreach(var child in node.Children())
        {
            if(child is null)
            {
                continue;
            }

            childCount++;
            onlyChild = child;
            if(childCount > 1)
            {
                return null;
            }
        }

        return childCount == 1 && onlyChild is not null
            ? ResolveToIdentifierName(onlyChild)
            : null;
    }

    /// <summary>
    /// Collects argument expression nodes from an argument list for move checking.
    /// </summary>
    private static IReadOnlyList<Storm.Runtime.SyntaxNode> CollectArgumentExpressionsForMoveCheck(ArgumentListNode? argList)
    {
        if(argList is null)
        {
            return Array.Empty<Storm.Runtime.SyntaxNode>();
        }

        var exprs = argList.GetAllExpressions();
        var result = new List<Storm.Runtime.SyntaxNode>(exprs.Length);
        foreach(var expr in exprs)
        {
            result.Add(expr);
        }

        return result;
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
        // Determine whether the bound type has a Drop() method and resolve the drop target.
        string? dropTarget = null;
        IType? boundType = null;

        switch(stmt)
        {
            case WithVarDeclarationStatementStatementNode withVarStmt:
                {
                    var decl = withVarStmt.WithVarDeclaration;
                    var name = decl.Name.Lexeme;
                    var init = EmitExpression(decl.Initializer);
                    AppendLine($"var {name} = {init};");
                    AppendLine($"MistContext.Push<{contextTypeName}>({name});");
                    boundType = model.GetTypeOf(decl.Initializer);
                    if(boundType is not null && TypeHasDrop(boundType))
                    {
                        dropTarget = name;
                    }

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
                    boundType = model.GetTypeOf(decl.Initializer);
                    if(boundType is not null && TypeHasDrop(boundType))
                    {
                        dropTarget = name;
                    }

                    break;
                }
            case WithDiscardDeclarationStatementStatementNode withDiscardStmt:
                {
                    var decl = withDiscardStmt.WithDiscardDeclaration;
                    var init = EmitExpression(decl.Initializer);
                    boundType = model.GetTypeOf(decl.Initializer);
                    if(boundType is not null && TypeHasDrop(boundType))
                    {
                        dropTarget = $"__with_{withDropCounter++}";
                        AppendLine($"var {dropTarget} = {init};");
                        AppendLine($"MistContext.Push<{contextTypeName}>({dropTarget});");
                    }
                    else
                    {
                        AppendLine($"MistContext.Push<{contextTypeName}>({init});");
                    }

                    break;
                }
            case WithPushStatementStatementNode withPushStmt:
                {
                    var value = EmitExpression(withPushStmt.WithPushStatement.Value);
                    boundType = model.GetTypeOf(withPushStmt.WithPushStatement.Value);
                    if(boundType is not null && TypeHasDrop(boundType))
                    {
                        dropTarget = $"__with_{withDropCounter++}";
                        AppendLine($"var {dropTarget} = {value};");
                        AppendLine($"MistContext.Push<{contextTypeName}>({dropTarget});");
                    }
                    else
                    {
                        AppendLine($"MistContext.Push<{contextTypeName}>({value});");
                    }

                    break;
                }
        }

        AppendLine("try");
        AppendLine("{");
        indentLevel++;
        EmitStatementsFromIndex(statements, nextIndex);
        indentLevel--;
        AppendLine("}");
        AppendLine("finally");
        AppendLine("{");
        indentLevel++;
        AppendLine($"MistContext.Pop<{contextTypeName}>();");
        if(dropTarget is not null && boundType is not null)
        {
            EmitWithBlockDropCall(dropTarget, boundType);
        }

        indentLevel--;
        AppendLine("}");
    }

    /// <summary>
    /// Attempts to resolve the C# type name for a context declaration statement.
    /// Returns null if the statement is not a context declaration.
    /// </summary>
    private string? TryGetContextDeclarationTypeName(StatementNode stmt)
    {
        switch(stmt)
        {
            case WithVarDeclarationStatementStatementNode withVarStmt:
                {
                    // Type is inferred from the initializer expression.
                    var type = model.GetTypeOf(withVarStmt.WithVarDeclaration.Initializer);
                    return type is not null ? CSharpTypeMap.GetTypeName(ResolveContextTypeForEmission(type)) : "object";
                }
            case WithTypedDeclarationStatementStatementNode withTypedStmt:
                {
                    // Type is the explicit type reference.
                    var type = model.GetTypeOf(withTypedStmt.WithTypedDeclaration.Type);
                    if(type is not null)
                    {
                        return CSharpTypeMap.GetTypeName(ResolveContextTypeForEmission(type));
                    }
                    // Fall back to the declared symbol's type.
                    var sym = model.GetDeclaredSymbol(withTypedStmt.WithTypedDeclaration);
                    return sym?.DeclaredType is not null
                        ? CSharpTypeMap.GetTypeName(ResolveContextTypeForEmission(sym.DeclaredType))
                        : "object";
                }
            case WithDiscardDeclarationStatementStatementNode withDiscardStmt:
                {
                    // Type is the explicit type reference.
                    var type = model.GetTypeOf(withDiscardStmt.WithDiscardDeclaration.Type);
                    if(type is not null)
                    {
                        return CSharpTypeMap.GetTypeName(ResolveContextTypeForEmission(type));
                    }
                    // Fall back to the initializer type.
                    var initType = model.GetTypeOf(withDiscardStmt.WithDiscardDeclaration.Initializer);
                    return initType is not null
                        ? CSharpTypeMap.GetTypeName(ResolveContextTypeForEmission(initType))
                        : "object";
                }
            case WithPushStatementStatementNode withPushStmt:
                {
                    // Type is inferred from the expression.
                    var type = model.GetTypeOf(withPushStmt.WithPushStatement.Value);
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
        while(type is TypeAliasType alias)
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
        var type = model.GetTypeOf(withBlock.Expression);
        var typeName = type is not null
            ? CSharpTypeMap.GetTypeName(ResolveContextTypeForEmission(type))
            : "object";

        // If the type has a Drop() method, we need a temp variable to call Drop() on
        // in the finally block (the expression may not be a simple variable reference).
        var hasDrop = type is not null && TypeHasDrop(type);
        string? dropTarget = null;
        if(hasDrop)
        {
            dropTarget = $"__with_{withDropCounter++}";
            AppendLine($"var {dropTarget} = {expr};");
            AppendLine($"MistContext.Push<{typeName}>({dropTarget});");
        }
        else
        {
            AppendLine($"MistContext.Push<{typeName}>({expr});");
        }

        AppendLine("try");
        AppendLine("{");
        indentLevel++;
        EmitBlockStatements(withBlock.Body);
        indentLevel--;
        AppendLine("}");
        AppendLine("finally");
        AppendLine("{");
        indentLevel++;
        AppendLine($"MistContext.Pop<{typeName}>();");
        if(hasDrop && dropTarget is not null)
        {
            EmitWithBlockDropCall(dropTarget, type!);
        }

        indentLevel--;
        AppendLine("}");
    }

    /// <summary>
    /// Emits a <c>with (var name = expr) { block }</c> statement as a scoped
    /// variable declaration with a try/finally that drops the variable.
    /// </summary>
    private void EmitWithVarBlockStatement(WithVarBlockStatementNode withVarBlock)
    {
        var name = withVarBlock.Name.Lexeme;
        var init = EmitExpression(withVarBlock.Initializer);
        var type = model.GetTypeOf(withVarBlock.Initializer);
        var hasDrop = type is not null && TypeHasDrop(type);

        // Wrap in braces to create a new scope — prevents variable name leakage
        // into the surrounding method body (the with-var introduces a scoped local).
        AppendLine("{");
        indentLevel++;
        AppendLine($"var {name} = {init};");
        AppendLine("try");
        AppendLine("{");
        indentLevel++;
        EmitBlockStatements(withVarBlock.Body);
        indentLevel--;
        AppendLine("}");
        AppendLine("finally");
        AppendLine("{");
        indentLevel++;
        if(hasDrop)
        {
            EmitWithBlockDropCall(name, type!);
        }

        indentLevel--;
        AppendLine("}");
        indentLevel--;
        AppendLine("}");
    }

    /// <summary>
    /// Emits the Drop() call (or <c>await</c> for Task/ValueTask) in a with-block's finally
    /// clause. The return value is always discarded in the with context (Requirement 5.6).
    /// Pop is emitted before this call (Requirement 5.7).
    /// Applies recursive drop chaining when Drop() returns a droppable type (Requirement 2.3).
    /// </summary>
    private void EmitWithBlockDropCall(string dropTarget, IType boundType)
    {
        var chainedExpr = EmitChainedDropExpression(boundType, dropTarget);
        AppendLine($"{chainedExpr};");
    }

    /// <summary>
    /// Builds a chained drop expression string by recursively following the drop return type.
    /// <para>
    /// Given a type and a base expression, determines the appropriate drop action (await for
    /// task-like types, .Drop() for other droppable types) and then checks whether the result
    /// of that drop is itself droppable. If so, wraps the expression further until reaching
    /// a non-droppable type or void (Requirements 2.3, 2.4, 2.5).
    /// </para>
    /// <para>
    /// Termination: recursion stops when <c>DropReturnType</c> is null (void) or the return
    /// type is not linear/relevant with HasDrop (Requirement 3.8).
    /// </para>
    /// <para>
    /// This method does NOT apply to explicit user captures (<c>var x = v.Drop()</c>) — those
    /// go through the variable declaration path and get their own scope-exit drop (Requirement 3.7).
    /// </para>
    /// </summary>
    /// <summary>
    /// Determines whether a type's droppability originates from implementing
    /// <c>System.IDisposable</c> (CLR interop) rather than an explicit Mist <c>Drop()</c> method.
    /// Resolves through <see cref="ConstructedType"/> and <see cref="TypeAliasType"/> to check
    /// the flag on the underlying definition.
    /// </summary>
    private static bool IsDisposableDrop(IType type)
    {
        return type switch
        {
            ConstructedType ct => IsDisposableDrop(ct.GenericDefinition),
            TypeAliasType alias => IsDisposableDrop(alias.UnderlyingType),
            ClassType c => c.IsDisposableDrop,
            StructType s => s.IsDisposableDrop,
            _ => false,
        };
    }

    private static string EmitChainedDropExpression(IType type, string baseExpr)
    {
        // Determine the first drop action based on the type itself.
        string currentExpr;
        IType? dropResultType;

        if(IsTaskLikeType(type))
        {
            // Task/ValueTask: drop action is await.
            currentExpr = $"await {baseExpr}";
            // The drop return type of Task<T> is T (the inner type argument).
            dropResultType = ResolveDropReturnType(type);
        }
        else
        {
            // Non-task droppable: drop action is .Drop() or .Dispose() for IDisposable CLR types.
            currentExpr = IsDisposableDrop(type)
                ? $"{baseExpr}.Dispose()"
                : $"{baseExpr}.Drop()";
            dropResultType = ResolveDropReturnType(type);
        }

        // Recurse: if the drop return type is itself droppable, chain further.
        while(dropResultType is not null && ShouldAutoInsertScopeExitDrop(dropResultType))
        {
            if(IsTaskLikeType(dropResultType))
            {
                currentExpr = $"await ({currentExpr})";
                dropResultType = ResolveDropReturnType(dropResultType);
            }
            else
            {
                currentExpr = IsDisposableDrop(dropResultType)
                    ? $"({currentExpr}).Dispose()"
                    : $"({currentExpr}).Drop()";
                dropResultType = ResolveDropReturnType(dropResultType);
            }
        }

        return currentExpr;
    }

    /// <summary>
    /// Resolves the concrete <c>DropReturnType</c> for a type, handling type parameter
    /// substitution for constructed generic types (e.g., <c>Task&lt;int&gt;</c> resolves
    /// its <c>DropReturnType</c> from <c>TResult</c> → <c>int</c>).
    /// Returns null when <c>Drop()</c> returns void or the type has no Drop.
    /// </summary>
    private static IType? ResolveDropReturnType(IType type)
    {
        if(type is ConstructedType constructed)
        {
            // Get the DropReturnType from the generic definition.
            var rawDropReturn = constructed.GenericDefinition switch
            {
                ClassType ct => ct.DropReturnType,
                StructType st => st.DropReturnType,
                _ => null,
            };

            if(rawDropReturn is null)
            {
                return null;
            }

            // If the DropReturnType is a type parameter, substitute with the actual type argument.
            if(rawDropReturn is TypeParameterType tpt)
            {
                var typeParams = constructed.GenericDefinition switch
                {
                    ClassType ct => ct.TypeParameters,
                    StructType st => st.TypeParameters,
                    _ => null,
                };

                if(typeParams is not null)
                {
                    for(var i = 0; i < typeParams.Count; i++)
                    {
                        if(typeParams[i] is TypeParameterType param && param.Name == tpt.Name
                            && i < constructed.TypeArguments.Count)
                        {
                            return constructed.TypeArguments[i];
                        }
                    }
                }

                // Fallback: use ordinal if name matching didn't work.
                if(tpt.Ordinal < constructed.TypeArguments.Count)
                {
                    return constructed.TypeArguments[tpt.Ordinal];
                }
            }

            return rawDropReturn;
        }

        // Non-constructed type: DropReturnType is directly on the type.
        return type switch
        {
            ClassType ct => ct.DropReturnType,
            StructType st => st.DropReturnType,
            _ => null,
        };
    }

    private void EmitStatement(StatementNode stmt)
    {
        switch(stmt)
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
                // Task 14.5 — void-coalescing assignment (??=): emit as x ??= value;
                if(TryEmitVoidCoalescingAssignment(exprStmt.ExpressionStatement.Expression))
                {
                    break;
                }

                {
                    var exprResult = EmitExpression(exprStmt.ExpressionStatement.Expression);
                    FlushPendingArrayInitStatements();
                    AppendLine($"{exprResult};");
                }

                break;
            // Task 12.5 — Readonly blocks emit as regular blocks (no C# equivalent).
            case ReadonlyBlockStatementStatementNode readonlyBlockStmt:
                EmitBlock(readonlyBlockStmt.ReadonlyBlock.Body);
                break;
            case BlockStatementStatementNode blockStmt:
                EmitBlock(blockStmt.BlockStatement);
                break;
            case WithBlockStatementStatementNode withBlockStmt:
                EmitWithBlockStatement(withBlockStmt.WithBlockStatement);
                break;
            case WithVarBlockStatementStatementNode withVarBlockStmt:
                EmitWithVarBlockStatement(withVarBlockStmt.WithVarBlockStatement);
                break;
            case UninitializedVariableStatementStatementNode uninitStmt:
                {
                    var uninit = uninitStmt.UninitializedVariable;
                    var type = EmitTypeReference(uninit.Type, isValueContext: true);
                    var name = uninit.Name.Lexeme;
                    AppendLine($"{type} {name};");
                    break;
                }
        }
    }

    private void EmitVariableDeclaration(VariableDeclarationStatementNode varDecl)
    {
        switch(varDecl)
        {
            case TypedVariableDeclarationVariableDeclarationStatementNode typed:
                {
                    var type = EmitTypeReference(typed.Type, isValueContext: true);
                    var name = typed.Name.Lexeme;

                    // When the declared type is an array type, set the target element type
                    // so that array literal emission produces `new T[] { ... }` instead of `new[] { ... }`.
                    var declaredSymbol = model.GetDeclaredSymbol(typed) as Symbol;
                    var declaredType = declaredSymbol?.DeclaredType;
                    if(declaredType is ArrayType targetArrayType)
                    {
                        arrayLiteralTargetElementType = targetArrayType.ElementType;
                    }

                    // Span types: the type reference may contain `unknown` (e.g.,
                    // `ReadOnlySpan<int, unknown>`), which EmitTypeReference lowers
                    // to `dynamic`. Override with the proper CLR type name from the
                    // resolved SpanType so the C# compiler sees System.Span<T> /
                    // System.ReadOnlySpan<T> instead of `dynamic`.
                    if(declaredType is SpanType typedSpanType)
                    {
                        type = CSharpTypeMap.GetTypeName(typedSpanType);
                    }

                    // Task 14.2: When the declared type is a two-constituent T | void union,
                    // emit Void.Void as null instead of MistVoid.Instance.
                    // Also check the emitted type string — if it ends with '?', the type
                    // reference resolved to a nullable type (even if ??= narrowed the symbol).
                    var previousVoidAsNull = emitVoidAsNull;
                    if(IsOptionalUnionType(declaredType) || type.EndsWith("?"))
                    {
                        emitVoidAsNull = true;
                    }

                    var init = EmitExpression(typed.Initializer);
                    arrayLiteralTargetElementType = null;
                    emitVoidAsNull = previousVoidAsNull;

                    init = WrapWithImplicitCastIfNeeded(typed.Initializer, init);
                    // Task 12.1 — emit .Clone() at resolver-marked implicit-clone sites (Req 14 AC 7).
                    init = WrapWithImplicitCloneIfNeeded(typed.Initializer, init);
                    // Task 31.1 — covariant unknown widening at the typed local
                    // declaration's assignment site. The declared type is
                    // recorded on the local variable's symbol; the initializer
                    // expression type comes from the expression-type map.
                    var initType = model.GetTypeOf(typed.Initializer);
                    var widened = TryEmitCovariantUnknownCast(initType, declaredType, init);
                    if(widened is not null)
                    {
                        init = widened;
                    }

                    // Delegate-func conversion wrapping: when assigning a func value to a
                    // DelegateType variable or a DelegateType value to a func variable, emit
                    // a constructor invocation to satisfy C#'s nominal delegate type system.
                    init = WrapDelegateConversionIfNeeded(initType, declaredType, init);

                    // Anonymous types have no denotable name in C#; always emit `var`.
                    if(declaredType is AnonymousType)
                    {
                        type = "var";
                    }

                    // Emit ref / ref readonly prefix for ref locals.
                    var refPrefix = declaredSymbol?.RefKind switch
                    {
                        RefKind.Ref => "ref ",
                        RefKind.RefReadonly => "ref readonly ",
                        _ => "",
                    };
                    // In C#, ref locals require `ref` before the initializer too:
                    // `ref int x = ref expr;`
                    var refInitPrefix = declaredSymbol?.RefKind switch
                    {
                        RefKind.Ref or RefKind.RefReadonly => "ref ",
                        _ => "",
                    };
                    FlushPendingArrayInitStatements();
                    AppendLine($"{refPrefix}{type} {name} = {refInitPrefix}{init};");
                    break;
                }
            case InferredVariableDeclarationVariableDeclarationStatementNode inferred:
                {
                    var name = inferred.Name.Lexeme;
                    var init = EmitExpression(inferred.Initializer);

                    // Task 12.1 — emit .Clone() at resolver-marked implicit-clone sites (Req 14 AC 7).
                    init = WrapWithImplicitCloneIfNeeded(inferred.Initializer, init);

                    // Emit ref / ref readonly prefix for ref locals.
                    var inferredSymbol = model.GetDeclaredSymbol(inferred) as Symbol;
                    var refPrefix = inferredSymbol?.RefKind switch
                    {
                        RefKind.Ref => "ref ",
                        RefKind.RefReadonly => "ref readonly ",
                        _ => "",
                    };
                    // In C#, ref locals require `ref` before the initializer too:
                    // `ref var x = ref expr;`
                    var refInitPrefix = inferredSymbol?.RefKind switch
                    {
                        RefKind.Ref or RefKind.RefReadonly => "ref ",
                        _ => "",
                    };
                    FlushPendingArrayInitStatements();

                    // Stackalloc-backed locals: emit the explicit System.Span<T> /
                    // System.ReadOnlySpan<T> type instead of `var` so that the C#
                    // compiler uses the safe Span path rather than pointer allocation
                    // (which requires an unsafe context). The declared type on the
                    // symbol is a SpanType set during semantic model construction.
                    var inferredDeclaredType = inferredSymbol?.DeclaredType;
                    if(inferredDeclaredType is SpanType inferredSpanType)
                    {
                        var spanTypeName = CSharpTypeMap.GetTypeName(inferredSpanType);
                        AppendLine($"{refPrefix}{spanTypeName} {name} = {refInitPrefix}{init};");
                    }
                    else
                    {
                        AppendLine($"{refPrefix}var {name} = {refInitPrefix}{init};");
                    }

                    break;
                }
            case VarTupleDeconstructionVariableDeclarationStatementNode varTuple:
                {
                    // Emit: var (a, b, ...) = expr;
                    var names = new List<string> { varTuple.FirstName.Lexeme };
                    foreach(var restName in varTuple.RestNames)
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

                    string formatDeconstructionElement(TupleDeconstructionElementNode element)
                    {
                        var type = EmitTypeReference(element.Type, isValueContext: true);
                        return $"{type} {element.Name.Lexeme}";
                    }

                    parts.Add(formatDeconstructionElement(typedTuple.First));
                    parts.Add(formatDeconstructionElement(typedTuple.Rest));
                    foreach(var additional in typedTuple.TupleDeconstructionElements)
                    {
                        parts.Add(formatDeconstructionElement(additional));
                    }

                    var init = EmitExpression(typedTuple.Initializer);
                    AppendLine($"({string.Join(", ", parts)}) = {init};");
                    break;
                }
        }
    }

    private void EmitReturnStatement(ReturnStatementNode returnStmt)
    {
        if(returnStmt.Value is not null)
        {
            // Task 14.2: When the method return type is a two-constituent T | void union,
            // emit Void.Void as null instead of MistVoid.Instance.
            var previousVoidAsNull = emitVoidAsNull;
            if(IsOptionalUnionType(currentMethodReturnType))
            {
                emitVoidAsNull = true;
            }

            var value = EmitExpression(returnStmt.Value);
            emitVoidAsNull = previousVoidAsNull;

            value = WrapWithImplicitCastIfNeeded(returnStmt, value);
            // Task 31.1 — covariant unknown widening at the return site
            // against the enclosing method's return type.
            var valueType = model.GetTypeOf(returnStmt.Value);
            var widened = TryEmitCovariantUnknownCast(valueType, currentMethodReturnType, value);
            if(widened is not null)
            {
                value = widened;
            }

            if(returnStmt.RefKeyword is not null)
            {
                FlushPendingArrayInitStatements();
                AppendLine($"return ref {value};");
            }
            else
            {
                FlushPendingArrayInitStatements();
                AppendLine($"return {value};");
            }
        }
        else if(inValueReturningVoidMethod)
        {
            // In value-returning void methods, `return;` becomes `return MistVoid.Instance;`
            // Task 14.2: If the return type is an optional union, emit null instead.
            if(IsOptionalUnionType(currentMethodReturnType))
            {
                AppendLine("return null;");
            }
            else
            {
                AppendLine("return MistVoid.Instance;");
            }
        }
        else
        {
            AppendLine("return;");
        }
    }

    private void EmitIfStatement(IfStatementNode ifStmt)
    {
        var condition = EmitExpression(ifStmt.Condition);

        // Wrap the if-statement in a block scope when the condition introduces
        // pattern variables (via `is` patterns with var/type bindings). In C#,
        // pattern variables declared in `if` conditions are scoped to the enclosing
        // block, which can conflict with pattern variables in later switch arms.
        // Wrapping in an extra block prevents CS0136 name conflicts.
        var needsScope = ConditionHasPatternBindings(condition);

        if(needsScope)
        {
            AppendLine("{");
            indentLevel++;
        }

        AppendLine($"if ({condition})");

        // Conditional-move branch sensitivity (Requirements 4.6, 13.5):
        // When there are pending branch drops, inject Drop() into branches where the
        // variable is still live (not moved).
        if(pendingBranchDrops.Count > 0)
        {
            EmitBlockWithBranchDrops(ifStmt.ThenBranch, ifStmt.ThenBranch);

            if(ifStmt.ElseBranch is not null)
            {
                EmitElseClauseWithBranchDrops(ifStmt.ElseBranch);
            }
        }
        else
        {
            EmitBlock(ifStmt.ThenBranch);

            if(ifStmt.ElseBranch is not null)
            {
                EmitElseClause(ifStmt.ElseBranch);
            }
        }

        if(needsScope)
        {
            indentLevel--;
            AppendLine("}");
        }
    }

    /// <summary>
    /// Determines whether a condition string contains <c>is</c> pattern bindings that
    /// would introduce variables into the enclosing scope. Detects patterns like
    /// <c>x is (var n, var s)</c> or <c>x is int n</c> where named variables (not discards)
    /// are introduced by the pattern matching expression.
    /// </summary>
    private static bool ConditionHasPatternBindings(string condition)
    {
        // Look for `is` followed by a pattern that contains `var` with a non-discard name,
        // or a type pattern with a trailing identifier binding.
        var isIndex = condition.IndexOf(" is ", StringComparison.Ordinal);
        if(isIndex < 0)
        {
            return false;
        }

        var afterIs = condition[(isIndex + 4)..];

        // Check for `var` patterns that bind a non-discard variable.
        // This handles `is (var n, var s)`, `is var x`, `is (int a, string b)`, etc.
        var varIdx = 0;
        while((varIdx = afterIs.IndexOf("var ", varIdx, StringComparison.Ordinal)) >= 0)
        {
            var nameStart = varIdx + 4;
            if(nameStart < afterIs.Length && afterIs[nameStart] != '_')
            {
                return true;
            }

            // `var _` is a discard — skip it
            varIdx = nameStart;
        }

        // Check for type patterns with a trailing binding name: `is int n`, `is string str`
        // This is a simpler heuristic — if there's a type name followed by an identifier
        // after `is`, it's likely a binding pattern. The presence of `(` indicates tuple/positional.
        // For simplicity, only trigger on `var` bindings which cover the proof code case.
        return false;
    }

    /// <summary>
    /// Emits a block with branch-sensitive drop injection. For each pending branch drop,
    /// if the variable is NOT moved in this branch, append the Drop() call at the end
    /// and set the tracking flag. In the move branch, just set the flag.
    /// </summary>
    private void EmitBlockWithBranchDrops(BlockNode block, BlockNode branchBlock)
    {
        AppendLine("{");
        indentLevel++;
        EmitBlockStatements(block);

        // Inject drops for variables that are NOT moved in this branch.
        foreach(var (localName, isTaskLike) in pendingBranchDrops)
        {
            var flagName = $"__dropped_{localName}";
            if(!BlockMovesVariable(branchBlock, localName))
            {
                if(isTaskLike)
                {
                    AppendLine($"await {localName};");
                }
                else
                {
                    AppendLine($"{localName}.Drop();");
                }
            }

            AppendLine($"{flagName} = true;");
        }

        indentLevel--;
        AppendLine("}");
    }

    /// <summary>
    /// Emits an else clause with branch-sensitive drop injection.
    /// </summary>
    private void EmitElseClauseWithBranchDrops(ElseClauseNode elseClause)
    {
        switch(elseClause)
        {
            case ElseIfElseClauseNode elseIf:
                {
                    var condition = EmitExpression(elseIf.ElseIf.Condition);
                    AppendLine($"else if ({condition})");
                    EmitBlockWithBranchDrops(elseIf.ElseIf.ThenBranch, elseIf.ElseIf.ThenBranch);
                    if(elseIf.ElseIf.ElseBranch is not null)
                    {
                        EmitElseClauseWithBranchDrops(elseIf.ElseIf.ElseBranch);
                    }

                    break;
                }
            case ElseBlockElseClauseNode elseBlock:
                AppendLine("else");
                EmitBlockWithBranchDrops(elseBlock.ElseBlock, elseBlock.ElseBlock);
                break;
        }
    }

    private void EmitElseClause(ElseClauseNode elseClause)
    {
        switch(elseClause)
        {
            case ElseIfElseClauseNode elseIf:
                {
                    var condition = EmitExpression(elseIf.ElseIf.Condition);
                    AppendLine($"else if ({condition})");
                    EmitBlock(elseIf.ElseIf.ThenBranch);
                    if(elseIf.ElseIf.ElseBranch is not null)
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
        if(initializer is null)
        {
            return "";
        }

        switch(initializer)
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

    /// <summary>
    /// Emits a C# <c>foreach</c> statement. The emitted code calls <c>GetEnumerator()</c>
    /// on the concrete collection type — C#'s duck-typing handles the rest. This means
    /// synthesized <c>IEnumerable&lt;T&gt;</c> interfaces (added to ambient types like
    /// <c>Span&lt;T&gt;</c> for Mist's type system) are never referenced in emitted code.
    /// </summary>
    /// <remarks>Requirements: 10.4, 10.5.</remarks>
    private void EmitForeachStatement(ForeachStatementNode foreachStmt)
    {
        var typeRef = foreachStmt.GetTypeOrNull();
        var type = typeRef is not null ? EmitTypeReference(typeRef, isValueContext: true) : "var";
        var name = foreachStmt.GetName().Lexeme;
        var collection = EmitExpression(foreachStmt.GetCollection());

        AppendLine($"foreach ({type} {name} in {collection})");
        EmitBlock(foreachStmt.GetBody());
    }

    // ─── Property / Collection Initializer Entry Emission ────────────────

    private string EmitPropertyAssignmentEntry(PropertyAssignmentNode assignment)
    {
        return assignment switch
        {
            NamedPropertyAssignmentPropertyAssignmentNode named => $"{named.Name.Lexeme} = {EmitExpression(named.Value)}",
            CollectionElementAssignmentPropertyAssignmentNode element => EmitExpression(element.Value),
            _ => ""
        };
    }

    // ─── Expression Emission ─────────────────────────────────────────────

    private string EmitExpression(ExpressionNode expr)
    {
        return expr switch
        {
            AssignmentFormExpressionNode assignmentForm => EmitAssignment(assignmentForm.Assignment),
            LambdaFormExpressionNode lambdaForm => EmitLambda(lambdaForm.Lambda),
            _ => throw new InvalidOperationException(
                $"Unknown ExpressionNode variant: {expr.GetType().Name}"),
        };
    }

    /// <summary>
    /// Emits a lambda expression to C#.
    /// Handles expression bodies (<c>(params) =&gt; expr</c>), block bodies
    /// (<c>(params) =&gt; { stmts }</c>), <c>static</c> (C# 9 static lambdas),
    /// and <c>async</c> modifiers. For non-async Mist lambdas whose body calls
    /// async methods (auto-await), the emitted C# lambda receives the <c>async</c>
    /// keyword so that the auto-unwrap <c>await</c> is valid.
    /// </summary>
    private string EmitLambda(LambdaExpressionNode lambda)
    {
        var modifiers = lambda switch
        {
            ExpressionLambdaLambdaExpressionNode exprLambda => exprLambda.Modifiers,
            BlockLambdaLambdaExpressionNode blockLambda => blockLambda.Modifiers,
            _ => ImmutableArray<LambdaModifierNode>.Empty,
        };

        var paramList = lambda switch
        {
            ExpressionLambdaLambdaExpressionNode exprLambda => exprLambda.Params,
            BlockLambdaLambdaExpressionNode blockLambda => blockLambda.Params,
            _ => null!,
        };

        // Determine if the lambda has explicit static/async modifiers.
        var hasStatic = false;
        var hasAsync = false;
        foreach(var modifier in modifiers)
        {
            switch(modifier)
            {
                case StaticLambdaModifierLambdaModifierNode:
                    hasStatic = true;
                    break;
                case AsyncLambdaModifierLambdaModifierNode:
                    hasAsync = true;
                    break;
            }
        }

        // For non-async lambdas that contain auto-await calls, we do NOT add the
        // `async` keyword. Instead, the auto-await will emit `.GetAwaiter().GetResult()`
        // at the call site, keeping the lambda synchronous (matching the declared
        // `Func<..., T>` return type rather than `Func<..., Task<T>>`).
        var needsSyncAutoAwait = !hasAsync && LambdaBodyContainsAutoAwait(lambda);

        // Build the modifier prefix.
        var prefix = "";
        if(hasStatic && hasAsync)
        {
            prefix = "static async ";
        }
        else if(hasStatic)
        {
            prefix = "static ";
        }
        else if(hasAsync)
        {
            prefix = "async ";
        }

        // Emit the parameter list.
        var parameters = EmitLambdaParameterList(paramList);

        // Save and set lambda context flags for body emission.
        var previousSyncLambdaAutoAwait = insideSyncLambdaAutoAwait;
        var previousAsyncLambda = insideAsyncLambda;

        if(needsSyncAutoAwait)
        {
            insideSyncLambdaAutoAwait = true;
        }

        if(hasAsync)
        {
            insideAsyncLambda = true;
        }

        // Emit the body.
        var result = lambda switch
        {
            ExpressionLambdaLambdaExpressionNode exprLambda => $"{prefix}{parameters} => {EmitExpression(exprLambda.Body)}",
            BlockLambdaLambdaExpressionNode blockLambda => $"{prefix}{parameters} => {EmitLambdaBlockBody(blockLambda.Body)}",
            _ => throw new InvalidOperationException(
                                $"Unknown LambdaExpressionNode variant: {lambda.GetType().Name}"),
        };

        // Restore lambda context flags.
        insideSyncLambdaAutoAwait = previousSyncLambdaAutoAwait;
        insideAsyncLambda = previousAsyncLambda;

        return result;
    }

    /// <summary>
    /// Emits a lambda parameter list to C#.
    /// Single implicit params emit without parentheses (<c>x</c>);
    /// parenthesized params emit with parens (<c>(int x, string y)</c> or <c>()</c>).
    /// </summary>
    private string EmitLambdaParameterList(LambdaParameterListNode paramList)
    {
        return paramList switch
        {
            SingleImplicitParamLambdaParameterListNode singleParam => singleParam.Name.Lexeme,
            ParenthesizedLambdaParamsLambdaParameterListNode parenParams => EmitParenthesizedLambdaParams(parenParams),
            _ => throw new InvalidOperationException(
                $"Unknown LambdaParameterListNode variant: {paramList.GetType().Name}"),
        };
    }

    /// <summary>
    /// Emits a parenthesized lambda parameter list to C#.
    /// </summary>
    private string EmitParenthesizedLambdaParams(ParenthesizedLambdaParamsLambdaParameterListNode parenParams)
    {
        if(parenParams.Params is null)
        {
            return "()";
        }

        var parts = new List<string>
        {
            EmitLambdaParam(parenParams.Params.LambdaParam)
        };
        foreach(var param in parenParams.Params.LambdaParams)
        {
            parts.Add(EmitLambdaParam(param));
        }

        return $"({string.Join(", ", parts)})";
    }

    /// <summary>
    /// Emits a single lambda parameter to C#.
    /// Typed params emit as <c>Type name</c>; untyped params emit as just <c>name</c>.
    /// </summary>
    private string EmitLambdaParam(LambdaParamNode param)
    {
        return param switch
        {
            TypedLambdaParamLambdaParamNode typed => $"{EmitTypeReference(typed.Type)} {typed.Name.Lexeme}",
            UntypedLambdaParamLambdaParamNode untyped => untyped.Name.Lexeme,
            _ => throw new InvalidOperationException(
                $"Unknown LambdaParamNode variant: {param.GetType().Name}"),
        };
    }

    /// <summary>
    /// Emits a lambda block body as an inline C# block: <c>{ stmt1; stmt2; }</c>.
    /// </summary>
    private string EmitLambdaBlockBody(BlockNode block)
    {
        var sb = new StringBuilder();
        _ = sb.Append("{ ");

        foreach(var stmt in block.Statements)
        {
            _ = sb.Append(EmitStatementToString(stmt));
            _ = sb.Append(' ');
        }

        _ = sb.Append('}');
        return sb.ToString();
    }

    /// <summary>
    /// Determines whether a non-async lambda body contains calls to async methods
    /// that will trigger auto-await (emitting <c>await</c> in the C# output).
    /// When this returns <c>true</c>, the emitted C# lambda needs the <c>async</c>
    /// keyword for the <c>await</c> to be valid.
    /// </summary>
    private bool LambdaBodyContainsAutoAwait(LambdaExpressionNode lambda)
    {
        Storm.Runtime.SyntaxNode? body = lambda switch
        {
            ExpressionLambdaLambdaExpressionNode exprLambda => exprLambda.Body,
            BlockLambdaLambdaExpressionNode blockLambda => blockLambda.Body,
            _ => null,
        };

        return body is not null && ContainsAutoAwaitCall(body);
    }

    /// <summary>
    /// Recursively walks an AST subtree looking for postfix expressions that call
    /// async methods (which would trigger auto-await emission).
    /// </summary>
    private bool ContainsAutoAwaitCall(Storm.Runtime.SyntaxNode node)
    {
        // If we hit a nested lambda, stop — its auto-await is independent.
        if(node is LambdaExpressionNode)
        {
            return false;
        }

        // If we hit an async functor expression, stop — the functor explicitly
        // captures the Task (either passing it through or wrapping in Task.Run),
        // so the contained call does not need auto-await.
        if(node is AsyncExpressionUnaryNode)
        {
            return false;
        }

        // If we hit an explicit await expression, stop — the user already handles
        // the async call explicitly, so it should not trigger auto-await.
        if(node is AwaitExpressionUnaryNode)
        {
            return false;
        }

        if(node is PostfixNode postfix && IsPostfixAsyncMethodCall(postfix))
        {
            return true;
        }

        foreach(var child in node.Children())
        {
            if(child is null)
            {
                continue;
            }

            if(ContainsAutoAwaitCall(child))
            {
                return true;
            }
        }

        return false;
    }

    private string EmitAssignment(AssignmentNode assignment)
    {
        var target = EmitVoidCoalescing(assignment.Target);

        if(assignment.Value is not null)
        {
            // Task 14.2: When the assignment target type is a two-constituent T | void union,
            // emit Void.Void as null instead of MistVoid.Instance.
            var previousVoidAsNull = emitVoidAsNull;
            var assignTargetType = model.GetTypeOf(assignment.Target);
            if(IsOptionalUnionType(assignTargetType))
            {
                emitVoidAsNull = true;
            }

            var value = EmitExpression(assignment.Value);
            emitVoidAsNull = previousVoidAsNull;

            value = WrapWithImplicitCastIfNeeded(assignment, value);
            // Task 12.1 — emit .Clone() at resolver-marked implicit-clone sites (Req 14 AC 7).
            value = WrapWithImplicitCloneIfNeeded(assignment.Value, value);
            // Task 31.1 — covariant unknown widening at the assignment
            // site against the target's static type.
            var targetType = model.GetTypeOf(assignment.Target);
            var valueType = model.GetTypeOf(assignment.Value);
            var widened = TryEmitCovariantUnknownCast(valueType, targetType, value);
            if(widened is not null)
            {
                value = widened;
            }

            return $"{target} = {value}";
        }

        return target;
    }

    /// <summary>
    /// Task 14.5 — Attempts to emit a void-coalescing assignment (<c>x ??= value</c>)
    /// using C#'s native null-coalescing assignment operator: <c>x ??= value;</c>.
    /// Returns <c>true</c> if the expression was a <c>??=</c> assignment and was emitted;
    /// <c>false</c> otherwise.
    /// Requirements: 7.4, 5.2, 5.3
    /// </summary>
    private bool TryEmitVoidCoalescingAssignment(ExpressionNode expression)
    {
        // A lambda-form expression is never a ??= assignment.
        if(expression.AsAssignmentOrNull() is not { } assignment)
        {
            return false;
        }

        // Check if this is a ??= assignment by inspecting the operator token.
        if(assignment.Operator is not { } op || op.Kind != (int)TokenKind.QUESTION_QUESTION_EQUAL)
        {
            return false;
        }

        if(assignment.Value is null)
        {
            return false;
        }

        // Emit the target variable name.
        var target = EmitLogicalOr(assignment.Target.Left);

        // Emit the value expression. The RHS is now a full ExpressionNode (widened to
        // reach lambdas); route through the expression-level entry point.
        var value = EmitExpression(assignment.Value);
        value = WrapWithImplicitCastIfNeeded(assignment, value);

        // Generate: x ??= value;
        AppendLine($"{target} ??= {value};");

        return true;
    }

    /// <summary>
    /// Emits a void-coalescing expression (<c>left ?? right</c>) using
    /// C#'s native <c>??</c> operator. For chained <c>a ?? b ?? c</c>, emits
    /// <c>a ?? b ?? c</c> directly (C# handles right-associativity natively).
    /// Requirements: 7.3, 7.5
    /// </summary>
    private string EmitVoidCoalescing(VoidCoalescingNode node)
    {
        var left = EmitLogicalOr(node.Left);

        if(node.LogicalOrs.Length == 0)
        {
            return left;
        }

        // Build the expression using C#'s native ?? operator.
        // For `a ?? b ?? c`, emit `a ?? b ?? c` directly.
        var sb = new StringBuilder();
        _ = sb.Append(left);

        for(var i = 0; i < node.LogicalOrs.Length; i++)
        {
            _ = sb.Append(" ?? ");
            _ = sb.Append(EmitLogicalOr(node.LogicalOrs[i]));
        }

        return sb.ToString();
    }

    private string EmitLogicalOr(LogicalOrNode logicalOr)
    {
        var left = EmitLogicalAnd(logicalOr.Left);

        if(logicalOr.Right is null || ReferenceEquals(logicalOr.Right, logicalOr.Left))
        {
            return left;
        }

        var right = EmitLogicalAnd(logicalOr.Right);
        return EmitBinaryOperator(logicalOr, "||", left, right);
    }

    private string EmitLogicalAnd(LogicalAndNode logicalAnd)
    {
        var left = EmitEquality(logicalAnd.Left);

        if(logicalAnd.Right is null || ReferenceEquals(logicalAnd.Right, logicalAnd.Left))
        {
            return left;
        }

        var right = EmitEquality(logicalAnd.Right);
        return EmitBinaryOperator(logicalAnd, "&&", left, right);
    }

    private string EmitEquality(EqualityNode equality)
    {
        var left = EmitRelational(equality.Left);

        if(equality.Right is null || ReferenceEquals(equality.Right, equality.Left))
        {
            return left;
        }

        var op = equality.Operator.Lexeme;
        var right = EmitRelational(equality.Right);

        // ── anonymous-types feature: structural equality (Requirement 9.2) ──
        // C# anonymous types override Equals/GetHashCode but not operator==.
        // Emit object.Equals(left, right) to get structural comparison.
        // The type checker sets the EqualityNode's type to bool when both operands
        // are same-shape AnonymousType. Check operand types via the model.
        var lhsType = model.GetTypeOf(equality.Left);
        var rhsType = model.GetTypeOf(equality.Right);
        if(lhsType is AnonymousType && rhsType is AnonymousType)
        {
            var equalsCall = $"object.Equals({left}, {right})";
            return op == "!=" ? $"!{equalsCall}" : equalsCall;
        }

        return EmitBinaryOperator(equality, op, left, right);
    }

    private string EmitRelational(RelationalNode relational)
    {
        switch(relational)
        {
            case RelationalComparisonRelationalNode cmp:
                {
                    var left = EmitAdditive(cmp.Left);

                    if(cmp.Right is null || ReferenceEquals(cmp.Right, cmp.Left))
                    {
                        return left;
                    }

                    var op = cmp.Operator.Lexeme;
                    var right = EmitAdditive(cmp.Right);
                    return EmitBinaryOperator(cmp, op, left, right);
                }

            case IsExpressionRelationalNode isExpr:
                {
                    var left = EmitAdditive(isExpr.Left);
                    var scrutineeType = model.GetTypeOf(isExpr.Left);

                    // Union type pattern matching: emit Is{T}/As{T} property access
                    // instead of C#'s native `is T t` pattern.
                    if(scrutineeType is UnionType unionType)
                    {
                        var unionResult = TryEmitUnionPatternMatch(isExpr.Pattern, unionType, left);
                        if(unionResult is not null)
                        {
                            return unionResult;
                        }
                    }

                    // Refinement type pattern matching: emit runtime predicate check
                    // instead of C#'s native `is T t` pattern. Refinement types are
                    // erased at runtime, so `x is AdultAge age` becomes a predicate
                    // condition (e.g., `x >= 18`) with a variable binding queued for
                    // the then-branch.
                    var refinementResult = TryEmitRefinementPatternMatch(isExpr.Pattern, left);
                    if(refinementResult is not null)
                    {
                        return refinementResult;
                    }

                    // Unknown scrutinee with property pattern: C# property patterns
                    // don't work against `object` because the properties aren't known
                    // at compile time. Desugar into a null check + dynamic property
                    // extraction in the then-branch (Requirement 8.2).
                    if(scrutineeType is UnknownType)
                    {
                        var unknownResult = TryEmitUnknownPropertyPatternMatch(isExpr.Pattern, left);
                        if(unknownResult is not null)
                        {
                            return unknownResult;
                        }
                    }

                    // Enum member pattern matching: emit value equality check
                    // (`scrutinee == EnumName.MemberName`) instead of C#'s `is` pattern.
                    var enumResult = TryEmitEnumPatternMatch(isExpr.Pattern, left);
                    if(enumResult is not null)
                    {
                        return enumResult;
                    }

                    // Regex pattern matching: emit Regex.IsMatch(scrutinee, "^(?:pattern)$")
                    // instead of C#'s native `is` pattern (C# doesn't support regex patterns).
                    var regexResult = TryEmitRegexPatternMatch(isExpr.Pattern, left);
                    if(regexResult is not null)
                    {
                        return regexResult;
                    }

                    var pattern = EmitPattern(isExpr.Pattern, scrutineeType);
                    return $"{left} is {pattern}";
                }

            case AsExpressionRelationalNode asExpr:
                return EmitAsExpression(asExpr);

            default:
                return "";
        }
    }

    // ─── As Expression Emission ──────────────────────────────────────────

    /// <summary>
    /// Emits C# code for a Mist <c>as</c> expression. Dispatches based on the
    /// conversion kind recorded by the type checker:
    /// <list type="bullet">
    /// <item>Provable: identity (no-op), struct widening (cast triggers implicit operator),
    /// class/interface widening (upcast).</item>
    /// <item>NonProvable: reference-type narrowing (C# <c>as</c>), value-type narrowing
    /// (is-pattern), union extraction (tag check).</item>
    /// </list>
    /// </summary>
    private string EmitAsExpression(AsExpressionRelationalNode asExpr)
    {
        var operand = EmitAdditive(asExpr.Left);
        var targetTypeName = EmitTypeReference(asExpr.TargetType);
        var conversionKind = model.GetAsConversionKind(asExpr);
        var sourceType = model.GetTypeOf(asExpr.Left);
        var resolvedTargetType = model.GetTypeOf(asExpr.TargetType);

        return conversionKind switch
        {
            AsConversionKind.Provable => EmitProvableAsConversion(operand, sourceType, resolvedTargetType, targetTypeName),
            AsConversionKind.NonProvable => EmitNonProvableAsConversion(operand, sourceType, resolvedTargetType, targetTypeName),
            _ => operand,// Should not reach — type checker rejects invalid conversions.
        };
    }

    /// <summary>
    /// Emits a provable (always-succeeds) <c>as</c> conversion. Identity yields
    /// the operand unchanged; widening yields a cast expression.
    /// </summary>
    private static string EmitProvableAsConversion(
        string operand, IType? sourceType, IType? targetType, string emittedTargetType)
    {
        // Identity — no cast needed (same type on both sides).
        if(sourceType is not null && targetType is not null
            && TypeCheckerAnalyzer.TypesEqual(sourceType, targetType))
        {
            return operand;
        }

        // All widening cases (struct→base struct, class→base class, class→interface)
        // emit a cast that the C# compiler resolves to the appropriate conversion.
        return $"(({emittedTargetType}){operand})";
    }

    /// <summary>
    /// Emits a non-provable (runtime-checked) <c>as</c> conversion. The result is
    /// a <c>T | void</c> value: either the extracted target value or void/null.
    /// </summary>
    private string EmitNonProvableAsConversion(
        string operand, IType? sourceType, IType? targetType, string emittedTargetType)
    {
        // Union source: emit union tag-based extraction (Is{T} / As{T}).
        if(sourceType is UnionType sourceUnion && targetType is not null)
        {
            return EmitUnionAsExtraction(operand, sourceUnion, targetType, emittedTargetType);
        }

        // Reference type target (class, interface, abstract type): C#'s `as` operator
        // produces null on failure, and the T|void union (emitted as nullable
        // reference or Nullable<T>) maps null → void naturally.
        if(targetType is not null && !CSharpTypeMap.IsValueType(targetType))
        {
            return $"({operand} as {emittedTargetType})";
        }

        // Value type target (struct, enum) from unknown or class source:
        // Use C#'s `is` pattern to extract or produce null for Nullable<T>.
        var tmpName = $"__as_tmp_{asExprCounter++}";
        return $"({operand} is {emittedTargetType} {tmpName} ? {tmpName} : default({emittedTargetType}?))";
    }

    /// <summary>
    /// Emits union extraction for <c>unionExpr as T</c>. Uses the union helper's
    /// <c>Is{T}</c> / <c>As{T}</c> properties to check and extract the constituent.
    /// For reference-type constituents, produces null on failure (maps to void).
    /// For value-type constituents, produces <c>Nullable&lt;T&gt;</c>.
    /// </summary>
    private string EmitUnionAsExtraction(
        string operand, UnionType sourceUnion, IType targetType, string emittedTargetType)
    {
        var constituentName = FindConstituentName(targetType, sourceUnion);
        if(constituentName is not null)
        {
            // Direct constituent: emit ternary using Is{T}/As{T} properties.
            // The result type is T|void, which for value types is Nullable<T>
            // and for reference types is just T (with null representing void).
            if(CSharpTypeMap.IsValueType(targetType))
            {
                return $"({operand}.Is{constituentName} ? ({emittedTargetType}?){operand}.As{constituentName} : default({emittedTargetType}?))";
            }

            return $"({operand}.Is{constituentName} ? {operand}.As{constituentName} : null)";
        }

        // Target is related to a constituent but not an exact match (e.g., narrowing
        // from a constituent's base type). Fall back to is-pattern on the boxed value.
        var tmpName = $"__as_tmp_{asExprCounter++}";
        if(!CSharpTypeMap.IsValueType(targetType))
        {
            return $"({operand} is {emittedTargetType} {tmpName} ? {tmpName} : null)";
        }

        return $"({operand} is {emittedTargetType} {tmpName} ? {tmpName} : default({emittedTargetType}?))";
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
        // Optional unions (T | void) are emitted as C# nullable types (T? for value types,
        // plain T for reference types with null representing void). They do NOT use union
        // helper structs, so standard C# `is` pattern emission handles them correctly.
        if(CSharpTypeMap.IsOptionalUnion(unionType))
        {
            return null;
        }

        // Navigate the pattern chain: Pattern → OrPattern → AndPattern → UnaryPattern → PrimaryPattern
        // Only handle simple (non-compound) patterns for union emission.
        var orPattern = pattern.OrPattern;
        if(orPattern.Right is not null && !ReferenceEquals(orPattern.Right, orPattern.Left))
        {
            return null; // or-pattern — fall through to standard emission
        }

        var andPattern = orPattern.Left;
        if(andPattern.Right is not null && !ReferenceEquals(andPattern.Right, andPattern.Left))
        {
            return null; // and-pattern — fall through to standard emission
        }

        var unaryPattern = andPattern.Left;
        if(unaryPattern is not DirectPatternUnaryPatternNode directPattern)
        {
            return null; // not-pattern — fall through to standard emission
        }

        var primaryPattern = directPattern.Primary;

        switch(primaryPattern)
        {
            case TypePatternPrimaryPatternNode typePattern:
                {
                    // Resolve the pattern type to find the matching constituent.
                    // The semantic model may store the type on the type reference node
                    // or on the pattern's declared symbol (the introduced variable).
                    var patternType = model.GetTypeOf(typePattern.Type);
                    patternType ??= model.GetDeclaredSymbol(typePattern)?.DeclaredType;
                    if(patternType is null)
                    {
                        return null;
                    }

                    // Find the constituent that matches the pattern type.
                    var constituentName = FindConstituentName(patternType, unionType);
                    if(constituentName is null)
                    {
                        return null; // Not a constituent — emit standard (dead code per type checker warning)
                    }

                    var bindingName = typePattern.Name.Lexeme;
                    var csharpTypeName = CSharpTypeMap.GetTypeName(patternType);

                    // Queue the variable binding for injection into the then-branch.
                    if(bindingName != "_")
                    {
                        pendingUnionPatternBindings.Add(
                            $"{csharpTypeName} {bindingName} = {scrutineeExpr}.As{constituentName};");
                    }

                    return $"{scrutineeExpr}.Is{constituentName}";
                }

            case VarPatternPrimaryPatternNode varPattern:
                {
                    var bindingName = varPattern.Name.Lexeme;
                    var helperTypeName = UnionHelperGenerator.GetHelperTypeName(unionType);

                    // Queue the variable binding for injection into the then-branch.
                    if(bindingName != "_")
                    {
                        pendingUnionPatternBindings.Add(
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
        foreach(var constituent in unionType.Constituents)
        {
            if(constituent.DisplayName == patternType.DisplayName)
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
    private string? TryEmitUnionSwitchArmPattern(PatternNode pattern, UnionType unionType)
    {
        // Navigate the pattern chain: Pattern → OrPattern → AndPattern → UnaryPattern → PrimaryPattern
        // Only handle simple (non-compound) patterns for union emission.
        var orPattern = pattern.OrPattern;
        if(orPattern.Right is not null && !ReferenceEquals(orPattern.Right, orPattern.Left))
        {
            return null; // or-pattern — fall through to standard emission
        }

        var andPattern = orPattern.Left;
        if(andPattern.Right is not null && !ReferenceEquals(andPattern.Right, andPattern.Left))
        {
            return null; // and-pattern — fall through to standard emission
        }

        var unaryPattern = andPattern.Left;
        if(unaryPattern is not DirectPatternUnaryPatternNode directPattern)
        {
            return null; // not-pattern — fall through to standard emission
        }

        var primaryPattern = directPattern.Primary;

        switch(primaryPattern)
        {
            case TypePatternPrimaryPatternNode typePattern:
                {
                    // Resolve the pattern type to find the matching constituent.
                    var patternType = model.GetTypeOf(typePattern.Type);
                    patternType ??= model.GetDeclaredSymbol(typePattern)?.DeclaredType;
                    if(patternType is null)
                    {
                        return null;
                    }

                    var constituentName = FindConstituentName(patternType, unionType);
                    if(constituentName is null)
                    {
                        return null;
                    }

                    var bindingName = typePattern.Name.Lexeme;

                    // Emit property pattern: { Is{T}: true, As{T}: var name }
                    if(bindingName != "_")
                    {
                        return $"{{ Is{constituentName}: true, As{constituentName}: var {bindingName} }}";
                    }

                    // No binding needed — just check the discriminator.
                    return $"{{ Is{constituentName}: true }}";
                }

            case BareTypePatternPrimaryPatternNode bareTypePattern:
                {
                    // Resolve the pattern type to find the matching constituent.
                    var patternType = model.GetTypeOf(bareTypePattern.Type);
                    if(patternType is null)
                    {
                        return null;
                    }

                    var constituentName = FindConstituentName(patternType, unionType);
                    if(constituentName is null)
                    {
                        return null;
                    }

                    // Emit property pattern: { Is{T}: true }
                    return $"{{ Is{constituentName}: true }}";
                }

            default:
                // var patterns, discard patterns, constant patterns, etc.
                // fall through to standard emission.
                return null;
        }
    }

    // ─── Unknown Scrutinee Property Pattern Matching ─────────────────────

    /// <summary>
    /// Attempts to emit a property pattern match when the scrutinee type is <c>unknown</c>
    /// (emitted as C# <c>object</c>). C# property patterns cannot resolve property names
    /// against <c>object</c> at compile time, so this method desugars the pattern into:
    /// <list type="bullet">
    /// <item>Condition: <c>scrutinee is not null</c></item>
    /// <item>Pending bindings (injected at then-branch start):
    ///   <c>dynamic __pat = scrutinee; var x = __pat.X; var y = __pat.Y;</c></item>
    /// </list>
    /// This uses the DLR to resolve property accesses at runtime (Requirement 8.2).
    /// Returns <c>null</c> if the pattern is not a simple property pattern.
    /// </summary>
    private string? TryEmitUnknownPropertyPatternMatch(PatternNode pattern, string scrutineeExpr)
    {
        // Navigate: Pattern → OrPattern → AndPattern → UnaryPattern → PrimaryPattern
        var orPattern = pattern.OrPattern;
        if(orPattern.Right is not null && !ReferenceEquals(orPattern.Right, orPattern.Left))
        {
            return null;
        }

        var andPattern = orPattern.Left;
        if(andPattern.Right is not null && !ReferenceEquals(andPattern.Right, andPattern.Left))
        {
            return null;
        }

        var unaryPattern = andPattern.Left;
        if(unaryPattern is not DirectPatternUnaryPatternNode directPattern)
        {
            return null;
        }

        if(directPattern.Primary is not PropertyPatternPrimaryPatternNode propertyPattern)
        {
            return null;
        }

        if(propertyPattern.Subpatterns is null)
        {
            return null;
        }

        // Generate a unique dynamic variable name to avoid collisions.
        var dynVar = $"__pat_{nextPatternId++}";
        pendingUnionPatternBindings.Add($"dynamic {dynVar} = {scrutineeExpr};");

        // Process each subpattern — extract the var binding via dynamic property access.
        void processSubpattern(PropertySubpatternNode subpattern)
        {
            var propName = subpattern.Name.Lexeme;

            // Check if the subpattern is a simple `var name` pattern.
            var subOr = subpattern.Pattern.OrPattern;
            var subAnd = subOr.Left;
            var subUnary = subAnd.Left;
            if(subUnary is DirectPatternUnaryPatternNode subDirect
                && subDirect.Primary is VarPatternPrimaryPatternNode varPat)
            {
                var bindingName = varPat.Name.Lexeme;
                pendingUnionPatternBindings.Add($"var {bindingName} = {dynVar}.{propName};");
            }
            else
            {
                // For non-var subpatterns (constants, nested patterns, etc.),
                // extract the value and defer further matching to runtime.
                // This is a simplification — full recursive desugaring would
                // require condition composition.
                var tempName = $"__prop_{propName}_{nextPatternId++}";
                pendingUnionPatternBindings.Add($"var {tempName} = {dynVar}.{propName};");
            }
        }

        processSubpattern(propertyPattern.Subpatterns.PropertySubpattern);
        foreach(var subpattern in propertyPattern.Subpatterns.PropertySubpatterns)
        {
            processSubpattern(subpattern);
        }

        // The condition is a null check — the dynamic property access in the
        // then-branch handles the runtime type extraction.
        return $"{scrutineeExpr} is not null";
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
        if(orPattern.Right is not null && !ReferenceEquals(orPattern.Right, orPattern.Left))
        {
            return null; // or-pattern — fall through to standard emission
        }

        var andPattern = orPattern.Left;
        if(andPattern.Right is not null && !ReferenceEquals(andPattern.Right, andPattern.Left))
        {
            return null; // and-pattern — fall through to standard emission
        }

        var unaryPattern = andPattern.Left;
        if(unaryPattern is not DirectPatternUnaryPatternNode directPattern)
        {
            return null; // not-pattern — fall through to standard emission
        }

        var primaryPattern = directPattern.Primary;

        switch(primaryPattern)
        {
            case TypePatternPrimaryPatternNode typePattern:
                {
                    // Resolve the pattern type to check if it's a refinement type.
                    var patternType = model.GetTypeOf(typePattern.Type);
                    patternType ??= model.GetDeclaredSymbol(typePattern)?.DeclaredType;
                    if(patternType is not RefinementType refinementType)
                    {
                        return null;
                    }

                    var bindingName = typePattern.Name.Lexeme;
                    var condition = EmitRefinementPredicateCondition(refinementType.Predicate, scrutineeExpr);

                    // Queue the variable binding for injection into the then-branch.
                    if(bindingName != "_")
                    {
                        var csharpTypeName = CSharpTypeMap.GetTypeName(refinementType.BaseType);
                        pendingUnionPatternBindings.Add(
                            $"{csharpTypeName} {bindingName} = {scrutineeExpr};");
                    }

                    return condition;
                }

            case BareTypePatternPrimaryPatternNode bareTypePattern:
                {
                    // Resolve the pattern type to check if it's a refinement type.
                    var patternType = model.GetTypeOf(bareTypePattern.Type);
                    if(patternType is not RefinementType refinementType)
                    {
                        return null;
                    }

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
        if(orPattern.Right is not null && !ReferenceEquals(orPattern.Right, orPattern.Left))
        {
            return null;
        }

        var andPattern = orPattern.Left;
        if(andPattern.Right is not null && !ReferenceEquals(andPattern.Right, andPattern.Left))
        {
            return null;
        }

        var unaryPattern = andPattern.Left;
        if(unaryPattern is not DirectPatternUnaryPatternNode directPattern)
        {
            return null;
        }

        var primaryPattern = directPattern.Primary;

        switch(primaryPattern)
        {
            case TypePatternPrimaryPatternNode typePattern:
                {
                    var patternType = model.GetTypeOf(typePattern.Type);
                    patternType ??= model.GetDeclaredSymbol(typePattern)?.DeclaredType;
                    if(patternType is not RefinementType refinementType)
                    {
                        return null;
                    }

                    var bindingName = typePattern.Name.Lexeme;
                    var condition = EmitRefinementPredicateCondition(refinementType.Predicate, bindingName);
                    return $"var {bindingName} when {condition}";
                }

            case BareTypePatternPrimaryPatternNode bareTypePattern:
                {
                    var patternType = model.GetTypeOf(bareTypePattern.Type);
                    if(patternType is not RefinementType refinementType)
                    {
                        return null;
                    }

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

        if(predicate.AstNode.Right is not null &&
            !ReferenceEquals(predicate.AstNode.Right, predicate.AstNode.Left))
        {
            EmitUnaryPatternCondition(predicate.AstNode.Right, scrutineeExpr, parts);
        }

        if(!predicate.AstNode.UnaryPatterns.IsDefaultOrEmpty)
        {
            foreach(var part in predicate.AstNode.UnaryPatterns)
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
        switch(unary)
        {
            case DirectPatternUnaryPatternNode direct:
                EmitPrimaryPatternCondition(direct.Primary, scrutineeExpr, parts);
                break;
            case NotPatternUnaryPatternNode not:
                {
                    var innerParts = new List<string>();
                    EmitPrimaryPatternCondition(not.Operand, scrutineeExpr, innerParts);
                    if(innerParts.Count == 1)
                    {
                        parts.Add($"!({innerParts[0]})");
                    }
                    else
                    {
                        parts.Add($"!({string.Join(" && ", innerParts)})");
                    }

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
        switch(primary)
        {
            case GreaterThanOrEqualRelationalPatternPrimaryPatternNode gte:
                parts.Add($"{scrutineeExpr} >= {EmitVoidCoalescing(gte.Constant)}");
                break;
            case LessThanOrEqualRelationalPatternPrimaryPatternNode lte:
                parts.Add($"{scrutineeExpr} <= {EmitVoidCoalescing(lte.Constant)}");
                break;
            case GreaterThanRelationalPatternPrimaryPatternNode gt:
                parts.Add($"{scrutineeExpr} > {EmitVoidCoalescing(gt.Constant)}");
                break;
            case LessThanRelationalPatternPrimaryPatternNode lt:
                parts.Add($"{scrutineeExpr} < {EmitVoidCoalescing(lt.Constant)}");
                break;
            case ConstantPatternPrimaryPatternNode constant:
                if(constant.Literal is RegexConstantConstantLiteralNode regexConstInCondition)
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
        if(propertyPattern.Subpatterns is null)
        {
            return;
        }

        // Emit the first subpattern.
        EmitSinglePropertySubpatternCondition(propertyPattern.Subpatterns.PropertySubpattern, scrutineeExpr, parts);

        // Emit additional subpatterns.
        foreach(var subpattern in propertyPattern.Subpatterns.PropertySubpatterns)
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
        if(!string.IsNullOrEmpty(condition))
        {
            parts.Add(condition);
        }
    }

    /// <summary>
    /// Emits a full inner pattern (OrPattern → AndPattern → ...) as a C# condition string.
    /// Used for nested patterns within property subpatterns and parenthesized patterns.
    /// </summary>
    private string EmitInnerPatternCondition(PatternNode pattern, string scrutineeExpr)
    {
        var orPattern = pattern.OrPattern;

        // Handle or-patterns: emit as `(cond1) || (cond2)`.
        if(orPattern.Right is not null && !ReferenceEquals(orPattern.Right, orPattern.Left))
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

        if(andPattern.Right is not null && !ReferenceEquals(andPattern.Right, andPattern.Left))
        {
            EmitUnaryPatternCondition(andPattern.Right, scrutineeExpr, parts);
        }

        if(!andPattern.UnaryPatterns.IsDefaultOrEmpty)
        {
            foreach(var part in andPattern.UnaryPatterns)
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
        if(orPattern.Right is not null && !ReferenceEquals(orPattern.Right, orPattern.Left))
        {
            return null; // or-pattern — fall through to standard emission
        }

        var andPattern = orPattern.Left;
        if(andPattern.Right is not null && !ReferenceEquals(andPattern.Right, andPattern.Left))
        {
            return null; // and-pattern — fall through to standard emission
        }

        var unaryPattern = andPattern.Left;
        if(unaryPattern is not DirectPatternUnaryPatternNode directPattern)
        {
            return null; // not-pattern — fall through to standard emission
        }

        var primaryPattern = directPattern.Primary;

        if(primaryPattern is not BareTypePatternPrimaryPatternNode bareTypePattern)
        {
            return null;
        }

        // Check if the resolved type for this pattern is an EnumType.
        var patternType = model.GetTypeOf(bareTypePattern.Type);
        if(patternType is not EnumType)
        {
            return null;
        }

        // The type reference contains the qualified name (e.g., "Direction.North").
        // Extract it directly — this is an enum member access, not a type reference.
        if(bareTypePattern.Type.First is NamedSingleTypeRefSingleTypeRefNode namedRef)
        {
            var qualifiedName = ExtractQualifiedName(namedRef.Name);
            return $"{scrutineeExpr} == {qualifiedName}";
        }

        return null;
    }

    // ─── Enum Switch Arm Pattern Matching ───────────────────────────────

    /// <summary>
    /// Attempts to emit a switch arm pattern containing enum member references.
    /// Mist enums are emitted as <c>record class</c> types with <c>static readonly</c>
    /// fields, which C# does not accept as constant patterns. This method detects
    /// enum member patterns in both tuple positional patterns and standalone bare type
    /// patterns, replacing them with <c>var __eN</c> bindings and equality-checking
    /// <c>when</c> guards.
    /// <para>
    /// For <c>(Direction.North, Direction.East)</c>, emits:
    /// <c>(var __e0, var __e1) when __e0 == Direction.North &amp;&amp; __e1 == Direction.East</c>
    /// </para>
    /// <para>
    /// For a standalone <c>Direction.North</c>, emits:
    /// <c>var __e when __e == Direction.North</c>
    /// </para>
    /// Returns <c>null</c> if the pattern does not contain any enum member references.
    /// </summary>
    private string? TryEmitEnumSwitchArmPattern(SwitchArmNode arm, IType? receiverType)
    {
        // Navigate the pattern chain: Pattern → OrPattern → AndPattern → UnaryPattern → PrimaryPattern
        var orPattern = arm.Pattern.OrPattern;
        if(orPattern.Right is not null && !ReferenceEquals(orPattern.Right, orPattern.Left))
        {
            return null;
        }

        var andPattern = orPattern.Left;
        if(andPattern.Right is not null && !ReferenceEquals(andPattern.Right, andPattern.Left))
        {
            return null;
        }

        var unaryPattern = andPattern.Left;
        if(unaryPattern is not DirectPatternUnaryPatternNode directPattern)
        {
            return null;
        }

        var primaryPattern = directPattern.Primary;

        // Case 1: Tuple positional pattern containing enum elements.
        if(primaryPattern is TuplePositionalPatternPrimaryPatternNode tuplePattern)
        {
            return TryEmitEnumTupleSwitchArmPattern(tuplePattern, arm, receiverType);
        }

        // Case 2: Standalone bare type pattern resolving to EnumType.
        if(primaryPattern is BareTypePatternPrimaryPatternNode bareTypePattern)
        {
            var patternType = model.GetTypeOf(bareTypePattern.Type);
            if(patternType is not EnumType)
            {
                return null;
            }

            if(bareTypePattern.Type.First is not NamedSingleTypeRefSingleTypeRefNode namedRef)
            {
                return null;
            }

            var qualifiedName = ExtractQualifiedName(namedRef.Name);
            var guardExpr = $"__e == {qualifiedName}";

            if(arm.Guard is not null)
            {
                guardExpr += $" && {EmitVoidCoalescing(arm.Guard.Condition)}";
            }

            return $"var __e when {guardExpr} => {EmitExpression(arm.Result)},";
        }

        return null;
    }

    /// <summary>
    /// Emits a tuple positional pattern that contains at least one enum member
    /// element as a pattern with <c>var</c> bindings and a <c>when</c> guard for
    /// the enum equality checks.
    /// Returns <c>null</c> if no element is an enum member pattern.
    /// </summary>
    private string? TryEmitEnumTupleSwitchArmPattern(
        TuplePositionalPatternPrimaryPatternNode tuplePattern,
        SwitchArmNode arm,
        IType? receiverType)
    {
        // Collect all element patterns.
        var elementPatterns = new List<PatternNode> { tuplePattern.First, tuplePattern.Rest };
        foreach(var additional in tuplePattern.Patterns)
        {
            elementPatterns.Add(additional);
        }

        // Resolve the tuple element types from the receiver type.
        var tupleType = receiverType as TupleType;

        // First pass: check if ANY element is an enum member pattern.
        var hasEnumElement = false;
        for(var i = 0; i < elementPatterns.Count; i++)
        {
            if(IsEnumMemberPattern(elementPatterns[i]))
            {
                hasEnumElement = true;
                break;
            }
        }

        if(!hasEnumElement)
        {
            return null;
        }

        // Second pass: build the replacement tuple pattern and when guard.
        var patternElements = new List<string>();
        var guardConditions = new List<string>();
        var enumVarIndex = 0;

        for(var i = 0; i < elementPatterns.Count; i++)
        {
            var elementType = tupleType is not null && i < tupleType.ElementTypes.Count
                ? tupleType.ElementTypes[i]
                : null;

            if(IsEnumMemberPattern(elementPatterns[i]))
            {
                var qualifiedName = GetEnumMemberQualifiedName(elementPatterns[i]);
                var varName = $"__e{enumVarIndex}";
                patternElements.Add($"var {varName}");
                guardConditions.Add($"{varName} == {qualifiedName}");
                enumVarIndex++;
            }
            else
            {
                // Emit the element using the standard pattern emission.
                patternElements.Add(EmitPattern(elementPatterns[i], elementType));
            }
        }

        var tuplePatternText = $"({string.Join(", ", patternElements)})";
        var guardExpr = string.Join(" && ", guardConditions);

        if(arm.Guard is not null)
        {
            guardExpr += $" && {EmitVoidCoalescing(arm.Guard.Condition)}";
        }

        return $"{tuplePatternText} when {guardExpr} => {EmitExpression(arm.Result)},";
    }

    /// <summary>
    /// Determines whether a pattern node is a bare type pattern resolving to an enum type.
    /// </summary>
    private bool IsEnumMemberPattern(PatternNode pattern)
    {
        var orPat = pattern.OrPattern;
        if(orPat.Right is not null && !ReferenceEquals(orPat.Right, orPat.Left))
        {
            return false;
        }

        var andPat = orPat.Left;
        if(andPat.Right is not null && !ReferenceEquals(andPat.Right, andPat.Left))
        {
            return false;
        }

        if(andPat.Left is not DirectPatternUnaryPatternNode direct)
        {
            return false;
        }

        if(direct.Primary is not BareTypePatternPrimaryPatternNode bare)
        {
            return false;
        }

        var patternType = model.GetTypeOf(bare.Type);
        return patternType is EnumType;
    }

    /// <summary>
    /// Extracts the fully-qualified enum member name from a pattern node that is
    /// known to be an enum member pattern (as determined by <see cref="IsEnumMemberPattern"/>).
    /// </summary>
    private string GetEnumMemberQualifiedName(PatternNode pattern)
    {
        var bare = (BareTypePatternPrimaryPatternNode)((DirectPatternUnaryPatternNode)pattern.OrPattern.Left.Left).Primary;
        var namedRef = (NamedSingleTypeRefSingleTypeRefNode)bare.Type.First;
        return ExtractQualifiedName(namedRef.Name);
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
        if(orPattern.Right is not null && !ReferenceEquals(orPattern.Right, orPattern.Left))
        {
            return null;
        }

        var andPattern = orPattern.Left;
        if(andPattern.Right is not null && !ReferenceEquals(andPattern.Right, andPattern.Left))
        {
            return null;
        }

        var unaryPattern = andPattern.Left;
        if(unaryPattern is not DirectPatternUnaryPatternNode directPattern)
        {
            return null;
        }

        if(directPattern.Primary is not ConstantPatternPrimaryPatternNode { Literal: RegexConstantConstantLiteralNode regexConst })
        {
            return null;
        }

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
        if(orPattern.Right is not null && !ReferenceEquals(orPattern.Right, orPattern.Left))
        {
            return null;
        }

        var andPattern = orPattern.Left;
        if(andPattern.Right is not null && !ReferenceEquals(andPattern.Right, andPattern.Left))
        {
            return null;
        }

        var unaryPattern = andPattern.Left;
        if(unaryPattern is not DirectPatternUnaryPatternNode directPattern)
        {
            return null;
        }

        if(directPattern.Primary is not ConstantPatternPrimaryPatternNode { Literal: RegexConstantConstantLiteralNode regexConst })
        {
            return null;
        }

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
        for(var i = 0; i < raw.Length; i++)
        {
            if(raw[i] == '\\' && i + 1 < raw.Length)
            {
                var next = raw[i + 1];
                if(next == '`')
                {
                    _ = pattern.Append('`');
                    i++;
                }
                else if(next == '\\')
                {
                    _ = pattern.Append('\\');
                    i++;
                }
                else
                {
                    // Not a Mist escape — preserve the backslash and next char verbatim.
                    _ = pattern.Append(raw[i]);
                }
            }
            else
            {
                _ = pattern.Append(raw[i]);
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

        if(orPattern.Right is null || ReferenceEquals(orPattern.Right, orPattern.Left))
        {
            return left;
        }

        var right = EmitAndPattern(orPattern.Right, scrutineeType);
        return $"{left} or {right}";
    }

    private string EmitAndPattern(AndPatternNode andPattern, IType? scrutineeType)
    {
        var left = EmitUnaryPattern(andPattern.Left, scrutineeType);

        if(andPattern.Right is null || ReferenceEquals(andPattern.Right, andPattern.Left))
        {
            return left;
        }

        var right = EmitUnaryPattern(andPattern.Right, scrutineeType);
        return $"{left} and {right}";
    }

    private string EmitUnaryPattern(UnaryPatternNode unaryPattern, IType? scrutineeType)
    {
        return unaryPattern switch
        {
            NotPatternUnaryPatternNode notPattern => $"not {EmitPrimaryPattern(notPattern.Operand, scrutineeType)}",
            DirectPatternUnaryPatternNode directPattern => EmitPrimaryPattern(directPattern.Primary, scrutineeType),
            _ => "",
        };
    }

    private string EmitPrimaryPattern(PrimaryPatternNode primaryPattern, IType? scrutineeType = null)
    {
        switch(primaryPattern)
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
                    if(typePattern.Type.First is TupleSingleTypeRefSingleTypeRefNode tupleRef)
                    {
                        var name = typePattern.Name.Lexeme;
                        if(scrutineeType is TupleType)
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
                        if(TupleTypeRefHasAnyElementName(tupleRef))
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
                    if(bareTypePattern.Type.First is TupleSingleTypeRefSingleTypeRefNode tupleRef)
                    {
                        if(scrutineeType is TupleType)
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
                return $"< {EmitVoidCoalescing(ltPattern.Constant)}";
            case GreaterThanRelationalPatternPrimaryPatternNode gtPattern:
                return $"> {EmitVoidCoalescing(gtPattern.Constant)}";
            case LessThanOrEqualRelationalPatternPrimaryPatternNode ltePattern:
                return $"<= {EmitVoidCoalescing(ltePattern.Constant)}";
            case GreaterThanOrEqualRelationalPatternPrimaryPatternNode gtePattern:
                return $">= {EmitVoidCoalescing(gtePattern.Constant)}";
            case PropertyPatternPrimaryPatternNode propertyPattern:
                {
                    var sb = new StringBuilder();

                    if(propertyPattern.TypeName is not null)
                    {
                        _ = sb.Append(ExtractQualifiedName(propertyPattern.TypeName));
                        _ = sb.Append(' ');
                    }

                    _ = sb.Append("{ ");
                    if(propertyPattern.Subpatterns is not null)
                    {
                        _ = sb.Append(EmitPropertySubpatternList(propertyPattern.Subpatterns));
                        _ = sb.Append(' ');
                    }

                    _ = sb.Append('}');

                    if(propertyPattern.Name.HasValue)
                    {
                        _ = sb.Append(' ');
                        _ = sb.Append(propertyPattern.Name.Value.Lexeme);
                    }

                    return sb.ToString();
                }
            case ListPatternPrimaryPatternNode listPattern:
                {
                    if(listPattern.Elements is null)
                    {
                        return "[]";
                    }

                    return $"[{EmitListPatternElementList(listPattern.Elements)}]";
                }
            case TuplePositionalPatternPrimaryPatternNode tuplePattern:
                {
                    var elements = new List<string>();
                    var tupleType = scrutineeType as TupleType;

                    elements.Add(EmitPattern(tuplePattern.First, tupleType is not null && tupleType.ElementTypes.Count > 0 ? tupleType.ElementTypes[0] : null));
                    elements.Add(EmitPattern(tuplePattern.Rest, tupleType is not null && tupleType.ElementTypes.Count > 1 ? tupleType.ElementTypes[1] : null));

                    var idx = 2;
                    foreach(var additional in tuplePattern.Patterns)
                    {
                        elements.Add(EmitPattern(additional, tupleType is not null && idx < tupleType.ElementTypes.Count ? tupleType.ElementTypes[idx] : null));
                        idx++;
                    }

                    return $"({string.Join(", ", elements)})";
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
        var parts = new List<string>
        {
            EmitPropertySubpattern(subpatternList.PropertySubpattern)
        };

        foreach(var subpattern in subpatternList.PropertySubpatterns)
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
        var parts = new List<string>
        {
            EmitListPatternElement(elementList.ListPatternElement)
        };

        foreach(var element in elementList.ListPatternElements)
        {
            parts.Add(EmitListPatternElement(element));
        }

        return string.Join(", ", parts);
    }

    private string EmitListPatternElement(ListPatternElementNode element)
    {
        switch(element)
        {
            case PatternElementListPatternElementNode patternElement:
                return EmitPattern(patternElement.Pattern);
            case SliceElementListPatternElementNode sliceElement:
                {
                    // C# requires `.. var name` for slice variables (Mist uses `.. name`)
                    if(sliceElement.Name.HasValue)
                    {
                        return $".. var {sliceElement.Name.Value.Lexeme}";
                    }

                    return "..";
                }
            default:
                return "";
        }
    }

    private string EmitAdditive(AdditiveNode additive)
    {
        var left = EmitMultiplicative(additive.Left);

        if(additive.Right is null || ReferenceEquals(additive.Right, additive.Left))
        {
            return left;
        }

        var op = additive.Operator.Lexeme;
        var right = EmitMultiplicative(additive.Right);
        return EmitBinaryOperator(additive, op, left, right);
    }

    private string EmitMultiplicative(MultiplicativeNode multiplicative)
    {
        var left = EmitUnary(multiplicative.Left);

        if(multiplicative.Right is null || ReferenceEquals(multiplicative.Right, multiplicative.Left))
        {
            return left;
        }

        var op = multiplicative.Operator.Lexeme;
        var right = EmitUnary(multiplicative.Right);
        return EmitBinaryOperator(multiplicative, op, left, right);
    }

    private string EmitUnary(UnaryNode unary)
    {
        switch(unary)
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
            case AsyncExpressionUnaryNode asyncExpr:
                {
                    // Set the flag to suppress auto-unwrap inside the async functor operand.
                    var previousAsyncFunctorFlag = insideAsyncFunctor;
                    insideAsyncFunctor = true;
                    var operand = EmitUnary(asyncExpr.Operand);
                    insideAsyncFunctor = previousAsyncFunctorFlag;
                    // Determine if the target method is async.
                    // If async: emit the call directly (captures the Task without await).
                    // If sync: wrap in Task.Run(() => call).
                    if(IsAsyncFunctorTargetAsync(asyncExpr))
                    {
                        return operand;
                    }

                    return $"System.Threading.Tasks.Task.Run(() => {operand})";
                }
            case AwaitExpressionUnaryNode awaitExpr:
                {
                    // Suppress auto-await inside the explicit await operand to prevent
                    // double-await (e.g., `await await AsyncLookup(id)`).
                    var previousExplicitAwait = insideExplicitAwait;
                    insideExplicitAwait = true;
                    var operand = EmitUnary(awaitExpr.Operand);
                    insideExplicitAwait = previousExplicitAwait;
                    return $"await {operand}";
                }
            case PostfixExpressionUnaryNode postfix:
                return EmitPostfix(postfix.Primary);
            case AsyncBlockExpressionUnaryNode asyncBlock:
                {
                    // async { return expr; } → Task.Run(async () => { ... })
                    // Extract the return expression from the block body.
                    var retExpr = "default";
                    foreach(var blockStmt in asyncBlock.Body.Statements)
                    {
                        if(blockStmt is ReturnStatementStatementNode returnStmt && returnStmt.ReturnStatement.Value is not null)
                        {
                            retExpr = EmitExpression(returnStmt.ReturnStatement.Value);
                            break;
                        }
                    }

                    return $"System.Threading.Tasks.Task.Run(() => {retExpr})";
                }
            case AsyncHintBlockExpressionUnaryNode asyncHintBlock:
                {
                    // async valuetask { return expr; } → ValueTask wrapping pattern.
                    var retExpr = "default";
                    foreach(var blockStmt in asyncHintBlock.Body.Statements)
                    {
                        if(blockStmt is ReturnStatementStatementNode returnStmt && returnStmt.ReturnStatement.Value is not null)
                        {
                            retExpr = EmitExpression(returnStmt.ReturnStatement.Value);
                            break;
                        }
                    }

                    var hint = asyncHintBlock.Hint.Lexeme;
                    if(hint == "valuetask")
                    {
                        // Resolve the actual type argument from the semantic model.
                        var resolvedType = model.GetTypeOf(asyncHintBlock);
                        var typeArgName = "object";
                        if(resolvedType is ConstructedType ct && ct.TypeArguments.Count > 0)
                        {
                            typeArgName = CSharpTypeMap.GetTypeName(ct.TypeArguments[0]);
                        }

                        return $"new System.Threading.Tasks.ValueTask<{typeArgName}>({retExpr})";
                    }

                    return $"System.Threading.Tasks.Task.Run(() => {retExpr})";
                }
            default:
                return "";
        }
    }

    /// <summary>
    /// Determines whether the target method of an <c>async</c> functor expression
    /// is itself declared <c>async</c>. When the target is async, the emitter
    /// emits the call directly (capturing the Task) rather than wrapping in
    /// <c>Task.Run</c>.
    /// </summary>
    private bool IsAsyncFunctorTargetAsync(AsyncExpressionUnaryNode asyncExpr)
    {
        // The operand should be a PostfixExpressionUnaryNode containing the method call.
        if(asyncExpr.Operand is not PostfixExpressionUnaryNode postfixUnary)
        {
            return false;
        }

        var postfix = postfixUnary.Primary;

        // Case 1: Direct invocation — identifier(args)
        if(postfix.Suffix.Length > 0 && postfix.Suffix[^1] is InvocationSuffixPostfixSuffixNode)
        {
            var targetSymbol = model.GetDeclaredSymbol(postfix.Target);
            return targetSymbol is Symbol { IsAsync: true };
        }

        // Case 2: Member method call — obj.Method(args)
        if(postfix.Suffix.Length > 0 && postfix.Suffix[^1] is MethodCallSuffixPostfixSuffixNode methodCall)
        {
            var memberName = methodCall.Name.Lexeme;

            // Determine the receiver type for the method call.
            // The receiver is the type of the expression before the last suffix.
            var receiverType = postfix.Suffix.Length == 1 ? model.GetTypeOf(postfix.Target) : model.GetTypeOf(postfix.Suffix[^2]);
            if(receiverType is null)
            {
                return false;
            }

            // Look up the method symbol in the receiver type.
            var methodSymbol = FindMethodInType(receiverType, memberName);
            return methodSymbol is { IsAsync: true };
        }

        return false;
    }

    /// <summary>
    /// Finds a method symbol by name in a type's member scope.
    /// Used by <see cref="IsAsyncFunctorTargetAsync"/> to resolve method calls.
    /// </summary>
    private static Symbol? FindMethodInType(IType type, string methodName)
    {
        var scope = GetMemberScopeForType(type);
        if(scope is null)
        {
            return null;
        }

        var found = scope.Lookup(methodName);
        if(found is Symbol { Kind: SymbolKind.Method } sym)
        {
            return sym;
        }

        // Check base type for classes.
        if(type is ClassType classType && classType.BaseType is not null)
        {
            return FindMethodInType(classType.BaseType, methodName);
        }

        return null;
    }

    /// <summary>
    /// Resolves a method symbol from a type by name and argument count.
    /// Used as a fallback when <see cref="ISemanticModel.GetDeclaredSymbol"/> returns null
    /// for CLR backend method calls (which have no AST declaring node).
    /// Returns the unique matching method, or null if zero or multiple matches exist.
    /// </summary>
    private static Symbol? ResolveMethodFromType(IType type, string methodName, int argCount)
    {
        var scope = GetMemberScopeForType(type);
        if(scope is not null)
        {
            // Use LookupAll to find all overloads with the given name, then filter by arg count.
            var allMatches = scope.LookupAll(methodName);
            Symbol? match = null;
            var matchCount = 0;
            foreach(var member in allMatches)
            {
                if(member is Symbol { Kind: SymbolKind.Method } memberSym
                    && memberSym.Parameters.Count == argCount)
                {
                    match = memberSym;
                    matchCount++;
                    if(matchCount > 1)
                    {
                        return null; // Ambiguous — multiple matches, can't determine which one.
                    }
                }
            }

            if(matchCount == 1)
            {
                return match;
            }
        }

        // Check base type for classes.
        if(type is ClassType classType && classType.BaseType is not null)
        {
            return ResolveMethodFromType(classType.BaseType, methodName, argCount);
        }

        return null;
    }

    /// <summary>
    /// Gets the member scope for a type, handling constructed types by
    /// looking at their generic definition.
    /// </summary>
    private static IScope? GetMemberScopeForType(IType type)
    {
        return type switch
        {
            ClassType ct => ct.MemberScope,
            StructType st => st.MemberScope,
            AbstractType it => it.MemberScope,
            ConstructedType constructed => GetMemberScopeForType(constructed.GenericDefinition),
            _ => null,
        };
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

        var resolved = model.GetResolvedOperator(castExpr);

        if(resolved is null)
        {
            // Identity/subtype cast — emit standard C# cast.
            return $"(({targetType}){operand})";
        }

        if(resolved is Symbol { IsIntrinsic: true } intrinsicSymbol)
        {
            // Intrinsic cast operator — look up emission template.
            // Note: TryGetCastOperator is added in task 8.4; for now, fall back to standard cast.
            var declaringType = GetQualifiedTypeName(intrinsicSymbol);
            var template = intrinsicRegistry.TryGetCastOperator(declaringType, targetType);
            if(template is not null)
            {
                return string.Format(template.Template, operand);
            }

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
        var resolved = model.GetResolvedOperator(node);
        if(resolved is Symbol resolvedSymbol)
        {
            if(resolvedSymbol.Name.StartsWith("operator_implicit_") || resolvedSymbol.Name.StartsWith("operator_explicit_"))
            {
                // Check if this cast has an intrinsic emission template
                // (e.g., Regex → string emits .ToString(), string → Regex emits new Regex(...)).
                var declaringType = GetQualifiedTypeName(resolvedSymbol);
                if(declaringType != "Unknown")
                {
                    var targetTypeName = resolvedSymbol.DeclaredType?.DisplayName;
                    if(targetTypeName is not null)
                    {
                        var emittedTypeName = CSharpTypeMap.GetTypeName(resolvedSymbol.DeclaredType!);
                        var castTemplate = intrinsicRegistry.TryGetCastOperator(declaringType, emittedTypeName);
                        if(castTemplate is not null)
                        {
                            return string.Format(castTemplate.Template, expression);
                        }
                    }
                }

                // Fallback: emit as a C# cast expression.
                var fallbackTargetTypeName = resolvedSymbol.DeclaredType?.DisplayName;
                if(fallbackTargetTypeName is not null)
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
    /// Wraps an emitted initializer expression in a delegate constructor invocation when
    /// the declared variable type and the initializer type require delegate-func conversion.
    /// <list type="bullet">
    ///   <item>func → DelegateType: emits <c>new DelegateName(expr)</c></item>
    ///   <item>DelegateType → func: emits <c>new System.Func&lt;...&gt;(expr)</c></item>
    ///   <item>func → BaseDelegateType: emits as-is (C# 11+ handles implicitly)</item>
    ///   <item>DelegateType → BaseDelegateType: emits as-is</item>
    /// </list>
    /// </summary>
    private static string WrapDelegateConversionIfNeeded(
        IType? sourceType,
        IType? targetType,
        string emittedInit)
    {
        if(sourceType is null || targetType is null)
        {
            return emittedInit;
        }

        // func → DelegateType: wrap with new DelegateName(expr)
        if(targetType is DelegateType targetDelegate && SemanticModelBuilder.IsFuncType(sourceType))
        {
            var delegateTypeName = CSharpTypeMap.GetTypeName(targetDelegate);
            return $"new {delegateTypeName}({emittedInit})";
        }

        // DelegateType → func: wrap with new System.Func<...>(expr)
        if(sourceType is DelegateType && SemanticModelBuilder.IsFuncType(targetType))
        {
            var funcTypeName = CSharpTypeMap.GetTypeName(targetType);
            return $"new {funcTypeName}({emittedInit})";
        }

        // func → BaseDelegateType: C# 11+ handles implicitly, emit as-is
        // DelegateType → BaseDelegateType: C# handles implicitly, emit as-is
        return emittedInit;
    }

    /// <summary>
    /// Looks up the return type of a method declaration from the semantic
    /// model via the declared symbol. Used by <see cref="EmitMethodDeclaration"/>
    /// to set <see cref="currentMethodReturnType"/> for the duration of
    /// the method body's emission.
    /// </summary>
    private IType? ResolveMethodReturnType(MethodDeclarationNode methodDecl)
    {
        var symbol = model.GetDeclaredSymbol(methodDecl);
        return symbol?.DeclaredType;
    }

    /// <summary>
    /// Emits the C# return type for an async method, choosing between
    /// <c>ValueTask&lt;T&gt;</c> and <c>Task&lt;T&gt;</c> based on the
    /// <see cref="ValueTaskUsageAnalysisPass"/> result stored on the symbol,
    /// or emitting <c>ValueTask</c> directly when the method's return type is
    /// the explicit user-written <c>ValueTask&lt;TResult&gt;</c>.
    /// </summary>
    private static string EmitAsyncReturnType(Symbol methodSymbol)
    {
        var effectiveType = methodSymbol.EffectiveReturnType;
        if(effectiveType is null)
        {
            return "System.Threading.Tasks.Task";
        }

        if(effectiveType is not ConstructedType ct || ct.TypeArguments.Count == 0)
        {
            return CSharpTypeMap.GetTypeName(effectiveType);
        }

        // Explicit ValueTask<T> return type: the method's EffectiveReturnType is
        // a ConstructedType whose generic definition is Standard.ValueTask. Emit
        // ValueTask directly — this path is driven by the intrinsic type, not by
        // the UseValueTask optimization flag.
        if(ct.GenericDefinition is ClassType { DisplayName: "ValueTask", ContainingNamespace: "Standard" })
        {
            var innerType = ct.TypeArguments[0];
            if(innerType.IsVoid())
            {
                return "System.Threading.Tasks.ValueTask";
            }

            var innerTypeName = CSharpTypeMap.GetTypeName(innerType, isValueContext: true);
            return $"System.Threading.Tasks.ValueTask<{innerTypeName}>";
        }

        var innerTypeForTask = ct.TypeArguments[0];

        // Task<Void> → non-generic Task or ValueTask (no generic argument).
        if(innerTypeForTask.IsVoid())
        {
            return methodSymbol.UseValueTask
                ? "System.Threading.Tasks.ValueTask"
                : "System.Threading.Tasks.Task";
        }

        // Task<T> → ValueTask<T> or Task<T>.
        var innerTypeNameForTask = CSharpTypeMap.GetTypeName(innerTypeForTask, isValueContext: true);
        return methodSymbol.UseValueTask
            ? $"System.Threading.Tasks.ValueTask<{innerTypeNameForTask}>"
            : $"System.Threading.Tasks.Task<{innerTypeNameForTask}>";
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
        var resolved = model.GetResolvedOperator(node);

        if(resolved is null)
        {
            // No resolved operator — backward compatibility fallback.
            return $"{left} {op} {right}";
        }

        if(resolved is Symbol { IsIntrinsic: true } intrinsicSymbol)
        {
            // Intrinsic operator: look up the emission template.
            var declaringType = GetQualifiedTypeName(intrinsicSymbol);
            var template = intrinsicRegistry.TryGetBinaryOperator(declaringType, op);
            if(template is not null)
            {
                return string.Format(template.Template, left, right);
            }

            // Template not found — emit MIST_BE_0002 hard error (Requirements 5.9, 5.10).
            var memberSig = $"{declaringType}.operator{op}";
            diagnostics.Report(
                BackendDiagnostics.MissingIntrinsicEmissionTemplate,
                default,
                sourceText.Path,
                (object?)memberSig);
            return "";
        }

        if(resolved is Symbol nonIntrinsicSymbol)
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
        var resolved = model.GetResolvedOperator(node);

        if(resolved is null)
        {
            // No resolved operator — backward compatibility fallback.
            return op == "!" ? $"!{operand}" : $"-{operand}";
        }

        if(resolved is Symbol { IsIntrinsic: true } intrinsicSymbol)
        {
            // Intrinsic operator: look up the emission template.
            var declaringType = GetQualifiedTypeName(intrinsicSymbol);
            var template = intrinsicRegistry.TryGetUnaryOperator(declaringType, op);
            if(template is not null)
            {
                return string.Format(template.Template, operand);
            }

            // Template not found — emit MIST_BE_0002 hard error (Requirements 5.9, 5.10).
            var memberSig = $"{declaringType}.operator{op}";
            diagnostics.Report(
                BackendDiagnostics.MissingIntrinsicEmissionTemplate,
                default,
                sourceText.Path,
                (object?)memberSig);
            return "";
        }

        if(resolved is Symbol nonIntrinsicSymbol)
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
        if(memberScope?.Parent is not null)
        {
            // Look for a type symbol whose MemberScope is the operator's ContainingScope.
            foreach(var sibling in memberScope.Parent.Members)
            {
                if(sibling.DeclaredType is ClassType ct && ct.MemberScope == memberScope)
                {
                    return ct.ContainingNamespace is not null
                        ? $"{ct.ContainingNamespace}.{ct.DisplayName}"
                        : ct.DisplayName;
                }

                if(sibling.DeclaredType is StructType st && st.MemberScope == memberScope)
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
        if(memberScope?.Parent is not null)
        {
            foreach(var sibling in memberScope.Parent.Members)
            {
                if(sibling.DeclaredType is ClassType ct && ct.MemberScope == memberScope)
                {
                    return CSharpTypeMap.GetTypeName(ct);
                }

                if(sibling.DeclaredType is StructType st && st.MemberScope == memberScope)
                {
                    return CSharpTypeMap.GetTypeName(st);
                }
            }
        }

        return "Unknown";
    }

    /// <summary>
    /// Maps a Mist operator symbol to its C# operator method name per the
    /// .NET Common Language Specification naming convention.
    /// </summary>
    private static string CSharpOperatorName(string op)
    {
        return op switch
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
    }

    private string EmitPostfix(PostfixNode postfix)
    {
        // Special case: TypeLiteralExpression followed by member access or method call.
        // If the referenced type has the member (static member access like
        // string.IsNullOrWhiteSpace), emit the type name directly without typeof().
        // If the referenced type does NOT have the member (reflection access like
        // int.Name), fall through to the normal typeof() path below.
        if(postfix.Target is TypeLiteralExpressionPrimaryNode typeLiteral
            && postfix.Suffix.Length > 0
            && postfix.Suffix[0] is MemberAccessSuffixPostfixSuffixNode or MethodCallSuffixPostfixSuffixNode or OptionalMethodCallSuffixPostfixSuffixNode or OptionalMemberAccessSuffixPostfixSuffixNode)
        {
            // Check if the semantic model resolved the target to the actual type
            // (not the metatype). If model.GetTypeOf returns a ClassType/StructType
            // that is NOT Standard.Type, the semantic model decided it's a static
            // member access and we should emit without typeof().
            var resolvedTargetType = model.GetTypeOf(postfix.Target);
            var isStaticMemberAccess = resolvedTargetType is not null
                && resolvedTargetType.DisplayName != "Type"
                && resolvedTargetType is not ErrorType;

            if(isStaticMemberAccess)
            {
                var result = EmitTypeReference(typeLiteral.TypeRef, isValueContext: true);

                var receiverType = resolvedTargetType;

                for(var i = 0; i < postfix.Suffix.Length; i++)
                {
                    var suffix = postfix.Suffix[i];
                    result = EmitPostfixSuffix(result, suffix, receiverType);
                    receiverType = model.GetTypeOf(suffix);
                }

                return result;
            }
        }

        var primaryResult = EmitPrimary(postfix.Target);

        // Track the receiver type through the suffix chain for extension property rewriting.
        // The receiver type for the first suffix is the type of the primary target.
        // For subsequent suffixes, it's the result type of the previous suffix.
        var primaryReceiverType = model.GetTypeOf(postfix.Target);

        for(var i = 0; i < postfix.Suffix.Length; i++)
        {
            var suffix = postfix.Suffix[i];
            primaryResult = EmitPostfixSuffix(primaryResult, suffix, primaryReceiverType);

            // Update receiver type for the next suffix.
            primaryReceiverType = model.GetTypeOf(suffix);
        }

        // Auto-unwrap: in a sync context, async method calls are implicitly awaited.
        // Emit `await` prefix if the invocation targets an async method, the enclosing
        // method is sync (not declared async in Mist), and we're not inside an async functor.
        // Top-level statements (_currentMethodSymbol is null) are always a sync context —
        // C#'s top-level statement support automatically generates `async Task Main` when
        // `await` is used, so emitting `await` here is sufficient (Task 13.1).
        //
        // Lambda-specific behavior:
        // - Inside an explicit await: suppress auto-await (prevents double-await).
        // - Inside a sync lambda with auto-await: emit `.GetAwaiter().GetResult()` instead
        //   of `await` to keep the lambda synchronous.
        if(!insideAsyncFunctor
            && !insideExplicitAwait
            && !insideAsyncLambda
            && (currentMethodSymbol is null or { IsAsync: false })
            && IsPostfixAsyncMethodCall(postfix))
        {
            primaryResult = insideSyncLambdaAutoAwait ? $"{primaryResult}.GetAwaiter().GetResult()" : $"await {primaryResult}";
        }

        return primaryResult;
    }

    /// <summary>
    /// Determines whether a <see cref="PostfixNode"/> represents a call to an
    /// async method. Used by the auto-unwrap logic in <see cref="EmitPostfix"/>
    /// to decide whether to prepend <c>await</c> in sync contexts.
    /// </summary>
    private bool IsPostfixAsyncMethodCall(PostfixNode postfix)
    {
        if(postfix.Suffix.Length == 0)
        {
            return false;
        }

        // Case 1: Direct invocation — identifier(args)
        if(postfix.Suffix[^1] is InvocationSuffixPostfixSuffixNode)
        {
            var targetSymbol = model.GetDeclaredSymbol(postfix.Target);
            // Check both IsAsync (declared async in Mist) and NeedsCSharpAsync
            // (propagated async due to transitive calls). A method with NeedsCSharpAsync
            // emits as async Task<T> in C#, so callers must await it.
            if(targetSymbol is Symbol { IsAsync: true } or Symbol { NeedsCSharpAsync: true })
            {
                return true;
            }

            // Case 1b: Func invocation — the target is a Func variable with Task<T> return type.
            var targetType = model.GetTypeOf(postfix.Target);
            if(targetType is not null
                && SemanticModelBuilder.IsFuncType(targetType)
                && targetType is ConstructedType funcCt)
            {
                var funcReturnType = SemanticModelBuilder.GetFuncReturnType(funcCt);
                if(SemanticModelBuilder.IsStandardTaskType(funcReturnType))
                {
                    return true;
                }
            }

            // Fallback: check the symbol's declared type (in case GetTypeOf returns
            // the invocation result type rather than the variable's type).
            if(targetSymbol is ISymbol { DeclaredType: { } declaredType }
                && SemanticModelBuilder.IsFuncType(declaredType)
                && declaredType is ConstructedType funcCtFromSym)
            {
                var funcReturnType = SemanticModelBuilder.GetFuncReturnType(funcCtFromSym);
                if(SemanticModelBuilder.IsStandardTaskType(funcReturnType))
                {
                    return true;
                }
            }

            return false;
        }

        // Case 2: Member method call — obj.Method(args)
        if(postfix.Suffix[^1] is MethodCallSuffixPostfixSuffixNode methodCall)
        {
            var memberName = methodCall.Name.Lexeme;

            // Determine the receiver type for the method call.
            // The receiver is the type of the expression before the last suffix.
            var receiverType = postfix.Suffix.Length == 1 ? model.GetTypeOf(postfix.Target) : model.GetTypeOf(postfix.Suffix[^2]);
            if(receiverType is not null and not ErrorType)
            {
                // Look up the method symbol in the receiver type.
                var methodSymbol = FindMethodInType(receiverType, memberName);
                if(methodSymbol is { IsAsync: true } or { NeedsCSharpAsync: true })
                {
                    return true;
                }
            }

            // Fallback: for namespace-qualified calls (Namespace.Type.Method()) where
            // the receiver type resolves to ErrorType, reconstruct the qualified type
            // name and search for the method symbol directly.
            var fallbackSymbol = TryResolveQualifiedMethodCallSymbol(methodCall, postfix);
            return fallbackSymbol is { IsAsync: true } or { NeedsCSharpAsync: true };
        }

        return false;
    }

    /// <summary>
    /// Fallback resolution for namespace-qualified method calls like
    /// <c>Namespace.Type.Method()</c> where the receiver type resolves to
    /// ErrorType. Reconstructs the qualified type name from the suffix chain
    /// and searches the model's symbols for the method.
    /// </summary>
    private Symbol? TryResolveQualifiedMethodCallSymbol(
        MethodCallSuffixPostfixSuffixNode methodCall,
        PostfixNode postfix)
    {
        var methodName = methodCall.Name.Lexeme;

        // Find the index of this method call in the suffix array.
        var suffixIndex = -1;
        for(var i = 0; i < postfix.Suffix.Length; i++)
        {
            if(ReferenceEquals(postfix.Suffix[i], methodCall))
            {
                suffixIndex = i;
                break;
            }
        }

        if(suffixIndex < 1)
        {
            return null;
        }

        if(postfix.Target is not IdentifierExpressionPrimaryNode targetId)
        {
            return null;
        }

        var parts = new List<string> { targetId.Name.Lexeme };
        for(var i = 0; i < suffixIndex; i++)
        {
            if(postfix.Suffix[i] is MemberAccessSuffixPostfixSuffixNode memberAccess)
            {
                parts.Add(memberAccess.Name.Lexeme);
            }
            else
            {
                return null;
            }
        }

        if(parts.Count < 2)
        {
            return null;
        }

        var typeName = parts[^1];
        var namespaceName = string.Join(".", parts.Take(parts.Count - 1));

        foreach(var symbol in model.AllSymbols)
        {
            if(symbol is not Symbol sym)
            {
                continue;
            }

            if(sym.Kind is not (SymbolKind.Class or SymbolKind.Struct))
            {
                continue;
            }

            if(sym.Name != typeName)
            {
                continue;
            }

            var containingNs = (sym.DeclaredType as ClassType)?.ContainingNamespace
                             ?? (sym.DeclaredType as StructType)?.ContainingNamespace;
            if(containingNs != namespaceName)
            {
                continue;
            }

            if(sym.DeclaredType is null)
            {
                continue;
            }

            return FindMethodInType(sym.DeclaredType, methodName);
        }

        return null;
    }

    /// <summary>
    /// Members of ambient types that are properties in C# and should be emitted
    /// without parentheses, even though Mist models them as method calls.
    /// NOTE: This set is now empty. Length was migrated from a method to a property
    /// in the stdlib (Array.mist, String.mist), so it flows through the
    /// MemberAccessSuffix emitter path naturally. Retained for future use if needed.
    /// </summary>
    private static readonly HashSet<string> CSharpPropertyMembers = new(StringComparer.Ordinal)
    {
    };

    private string EmitPostfixSuffix(string target, PostfixSuffixNode suffix, IType? receiverType)
    {
        switch(suffix)
        {
            case MethodCallSuffixPostfixSuffixNode methodCall:
                {
                    var name = methodCall.Name.Lexeme;

                    // Explicit .Drop() on Task<T>/ValueTask<T> emits as `await target`
                    // instead of `target.Drop()` (Requirements 7.10, 8.8, 14.5, 14.6).
                    // This also handles `T r = v.Drop()` → `T r = await v` for task types.
                    if(name == "Drop" && methodCall.Arguments is null && receiverType is not null
                        && IsTaskLikeType(receiverType))
                    {
                        return $"await {target}";
                    }

                    var typeArgs = methodCall.TypeArgs is not null
                        ? EmitTypeArgumentList(methodCall.TypeArgs)
                        : EmitInferredTypeArguments(methodCall);

                    // In Mist, property-like members (e.g., string.Length) use method call
                    // syntax: input.Length(). In C#, these are properties and must be emitted
                    // without parentheses: input.Length.
                    if(methodCall.Arguments is null && methodCall.TypeArgs is null && CSharpPropertyMembers.Contains(name))
                    {
                        return $"{target}.{name}";
                    }

                    // Check if this method call resolves to an extension property —
                    // if so, rewrite to ClassName.get_PropertyName(instance).
                    if(methodCall.Arguments is null && methodCall.TypeArgs is null && receiverType is not null)
                    {
                        var extPropertyBlock = model.ExtensionRegistry.FindExtensionPropertyBlock(receiverType, name);
                        if(extPropertyBlock is not null)
                        {
                            var extClassName = ComputeExtensionClassName(extPropertyBlock);
                            return $"{extClassName}.get_{name}({target})";
                        }
                    }

                    // Check if this method call actually resolves to a property symbol —
                    // if so, emit as property access (no parentheses).
                    if(methodCall.Arguments is null && methodCall.TypeArgs is null)
                    {
                        var resolvedSymbol = TryResolveToPropertySymbol(suffix);
                        if(resolvedSymbol is not null)
                        {
                            return $"{target}.{name}";
                        }
                    }

                    var calledMethodSymbol = model.GetDeclaredSymbol(suffix) as Symbol;

                    // Fallback: for CLR backend methods, GetDeclaredSymbol may return null
                    // if the semantic model didn't register the resolved symbol. Resolve from
                    // the receiver type by matching method name and argument count so the emitter
                    // can check ClrCallingConvention for in/ref keyword selection (Requirement 3.4).
                    if(calledMethodSymbol is null && receiverType is not null)
                    {
                        var argCount = methodCall.Arguments?.GetAllExpressions().Length ?? 0;
                        calledMethodSymbol = ResolveMethodFromType(receiverType, name, argCount);
                    }

                    var args = EmitArgumentList(methodCall.Arguments, calledMethodSymbol);

                    // Check if this is an intrinsic static method call (e.g., Type.Of(x) → x.GetType()).
                    // The target is a type name; check if it maps to a stdlib type with an intrinsic.
                    var intrinsicStaticTemplate = TryGetIntrinsicStaticMethodTemplate(target, name, methodCall.Arguments);
                    if(intrinsicStaticTemplate is not null)
                    {
                        return intrinsicStaticTemplate;
                    }

                    // Check if this is an intrinsic instance method call on a backend-backed type
                    // (e.g., Regex.IsMatch, Regex.Pattern). Look up the receiver type's qualified
                    // name and query the intrinsic emission table.
                    if(receiverType is not null)
                    {
                        var qualifiedReceiverType = GetQualifiedTypeNameFromType(receiverType);
                        if(qualifiedReceiverType is not null)
                        {
                            var paramTypes = CollectArgumentTypes(methodCall.Arguments);
                            var instanceTemplate = intrinsicRegistry.TryGetInstanceMethod(qualifiedReceiverType, name, paramTypes);
                            if(instanceTemplate is not null)
                            {
                                // Format: {0} = receiver (target), {1}..{N} = arguments
                                var templateArgs = new List<string> { target };
                                if(methodCall.Arguments is not null)
                                {
                                    foreach(var argExpr in methodCall.Arguments.GetAllExpressions())
                                    {
                                        templateArgs.Add(EmitExpression(argExpr));
                                    }
                                }

                                return string.Format(instanceTemplate.Template, templateArgs.ToArray());
                            }
                        }
                    }

                    // Call-site transformation for generic methods with parameterized constructor constraints:
                    // append factory lambda arguments when calling a method that has factory delegate parameters.
                    if(calledMethodSymbol is not null && calledMethodSymbol.Kind == SymbolKind.Method &&
                        calledMethodSymbol.TypeParameters.Count > 0 &&
                        HasParameterizedConstructorConstraints(calledMethodSymbol.TypeParameters))
                    {
                        var factoryLambdas = GenerateFactoryLambdas(calledMethodSymbol.TypeParameters, methodCall.TypeArgs);
                        if(factoryLambdas.Length > 0)
                        {
                            args = args.Length > 0
                                ? $"{args}, {factoryLambdas}"
                                : factoryLambdas;
                        }
                    }

                    // Check if this method call resolves to an ambient extension method —
                    // if so, rewrite to StaticClassName.MethodName(receiver, args).
                    // Ambient extensions come from .NET assemblies and must be emitted as
                    // static method invocations on the original extension static class.
                    // Only rewrite if the receiver type does NOT have the method as its own member
                    // (own members take priority over extensions per Requirement 4.3).
                    if(receiverType is not null)
                    {
                        var ownScope = GetMemberScopeForType(receiverType);
                        var isOwnMember = ownScope?.Lookup(name) is not null;

                        if(!isOwnMember)
                        {
                            var ambientExtBlock = model.ExtensionRegistry.FindAmbientExtensionMethodBlock(receiverType, name);
                            if(ambientExtBlock is not null)
                            {
                                // Check if a Mist-native extension also provides this member —
                                // if so, the native extension takes priority (Requirement 4.4).
                                var hasNativeExtension = false;
                                var applicable = model.ExtensionRegistry.GetApplicableExtensions(receiverType);
                                foreach(var ext in applicable)
                                {
                                    if(ext.IsAmbient)
                                    {
                                        continue;
                                    }

                                    if(ext.MemberScope.Lookup(name) is not null)
                                    {
                                        hasNativeExtension = true;
                                        break;
                                    }
                                }

                                if(!hasNativeExtension)
                                {
                                    var staticClassName = ambientExtBlock.AmbientStaticClassName ?? "AmbientExtensions";
                                    var staticArgs = args.Length > 0
                                        ? $"{target}, {args}"
                                        : target;
                                    return $"{staticClassName}.{name}{typeArgs}({staticArgs})";
                                }
                            }
                        }
                    }

                    return $"{target}.{name}{typeArgs}({args})";
                }
            case MemberAccessSuffixPostfixSuffixNode memberAccess:
                {
                    // Special case: Void.Void → null (optional context) or MistVoid.Instance
                    if(target == "Void" && memberAccess.Name.Lexeme == "Void")
                    {
                        if(emitVoidAsNull)
                        {
                            return "null";
                        }

                        NeedsMistVoidStruct = true;
                        return "MistVoid.Instance";
                    }

                    // Check if this is an intrinsic property access on a backend-backed type
                    // (e.g., Regex.Pattern → .ToString()). Look up the receiver type's qualified
                    // name and query the intrinsic emission table as a zero-arg instance method.
                    var memberName = memberAccess.Name.Lexeme;
                    if(receiverType is not null)
                    {
                        var qualifiedReceiverType = GetQualifiedTypeNameFromType(receiverType);
                        if(qualifiedReceiverType is not null)
                        {
                            var instanceTemplate = intrinsicRegistry.TryGetInstanceMethod(
                                qualifiedReceiverType, memberName, Array.Empty<string>());
                            if(instanceTemplate is not null)
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
                    _ = sb.Append($"{target} switch");
                    _ = sb.Append('\n');
                    // Emit opening brace at current indent level
                    for(var i = 0; i < indentLevel; i++)
                    {
                        _ = sb.Append(IndentUnit);
                    }

                    _ = sb.Append('{');
                    indentLevel++;

                    if(switchExpr.Arms is not null)
                    {
                        void emitArm(SwitchArmNode arm)
                        {
                            _ = sb.Append('\n');
                            for(var i = 0; i < indentLevel; i++)
                            {
                                _ = sb.Append(IndentUnit);
                            }

                            // Union type scrutinee: emit property patterns using Is{T}/As{T}
                            // discriminator properties instead of C#'s native type patterns.
                            if(receiverType is UnionType unionType)
                            {
                                var unionPattern = TryEmitUnionSwitchArmPattern(arm.Pattern, unionType);
                                if(unionPattern is not null)
                                {
                                    _ = sb.Append(unionPattern);
                                    if(arm.Guard is not null)
                                    {
                                        _ = sb.Append($" when {EmitVoidCoalescing(arm.Guard.Condition)}");
                                    }

                                    _ = sb.Append($" => {EmitExpression(arm.Result)},");
                                    return;
                                }
                            }

                            // Refinement type pattern: emit as `var varName when predicate`
                            // guard condition. Refinement types are erased, so the pattern
                            // becomes a runtime predicate check in the when-clause.
                            var refinementArm = TryEmitRefinementSwitchArmPattern(arm.Pattern, target);
                            if(refinementArm is not null)
                            {
                                _ = sb.Append(refinementArm);
                                if(arm.Guard is not null)
                                {
                                    // Combine the refinement guard with the user's guard.
                                    _ = sb.Append($" && {EmitVoidCoalescing(arm.Guard.Condition)}");
                                }

                                _ = sb.Append($" => {EmitExpression(arm.Result)},");
                                return;
                            }

                            // Enum tuple positional pattern: emit var patterns with when guards
                            // for enum member elements (Mist enums are record classes, not C# enums).
                            var enumArm = TryEmitEnumSwitchArmPattern(arm, receiverType);
                            if(enumArm is not null)
                            {
                                _ = sb.Append(enumArm);
                                return;
                            }

                            // Regex pattern: emit as `string __s when Regex.IsMatch(__s, "^(?:pattern)$")`
                            // guard condition. C# doesn't natively support regex patterns in switch.
                            var regexArm = TryEmitRegexSwitchArmPattern(arm.Pattern);
                            if(regexArm is not null)
                            {
                                _ = sb.Append(regexArm);
                                if(arm.Guard is not null)
                                {
                                    // Combine the regex guard with the user's guard.
                                    _ = sb.Append($" && {EmitVoidCoalescing(arm.Guard.Condition)}");
                                }

                                _ = sb.Append($" => {EmitExpression(arm.Result)},");
                                return;
                            }

                            _ = sb.Append(EmitPattern(arm.Pattern, receiverType));
                            if(arm.Guard is not null)
                            {
                                _ = sb.Append($" when {EmitVoidCoalescing(arm.Guard.Condition)}");
                            }

                            _ = sb.Append($" => {EmitExpression(arm.Result)},");
                        }

                        emitArm(switchExpr.Arms.SwitchArm);
                        foreach(var arm in switchExpr.Arms.SwitchArms)
                        {
                            emitArm(arm);
                        }
                    }

                    indentLevel--;
                    _ = sb.Append('\n');
                    for(var i = 0; i < indentLevel; i++)
                    {
                        _ = sb.Append(IndentUnit);
                    }

                    _ = sb.Append('}');
                    return sb.ToString();
                }
            case OptionalMemberAccessSuffixPostfixSuffixNode optionalMemberAccess:
                {
                    // Task 14.3: Optional chaining member access using C#'s native ?. operator
                    // (Requirements 7.2, 7.6). Since T | void maps to T? (Nullable) or T (reference
                    // with null), C#'s ?. provides the exact semantics: short-circuit to null when
                    // receiver is null, no double-evaluation of complex receivers.
                    var memberName = optionalMemberAccess.Name.Lexeme;
                    return $"{target}?.{memberName}";
                }
            case OptionalMethodCallSuffixPostfixSuffixNode optionalMethodCall:
                {
                    // Task 14.3: Optional chaining method call using C#'s native ?. operator
                    // (Requirements 7.2, 7.6). C#'s ?. handles short-circuiting and chaining natively.
                    var methodName = optionalMethodCall.Name.Lexeme;
                    var typeArgs = optionalMethodCall.TypeArgs is not null
                        ? EmitTypeArgumentList(optionalMethodCall.TypeArgs)
                        : "";
                    var args = EmitArgumentList(optionalMethodCall.Arguments);
                    return $"{target}?.{methodName}{typeArgs}({args})";
                }
            default:
                return target;
        }
    }


    /// <summary>
    /// Returns <c>true</c> if the given type is a two-constituent union where one
    /// constituent is <c>void</c> — the "optional" pattern (<c>T | void</c>).
    /// These unions map to C# <c>T?</c> (value types) or plain <c>T</c> (reference types)
    /// where absence is represented by <c>null</c>.
    /// </summary>
    private static bool IsOptionalUnionType(IType? type)
    {
        return type is UnionType { Constituents.Count: 2 } union
            && union.Constituents.Any(c => c.IsVoid());
    }

    /// <summary>
    /// Attempts to resolve a postfix suffix node to a property symbol via the semantic model.
    /// Returns the symbol if it is a property, null otherwise.
    /// </summary>
    private ISymbol? TryResolveToPropertySymbol(PostfixSuffixNode suffix)
    {
        // The semantic model stores type information keyed by AST node.
        // We check if the resolved type's source symbol is a property.
        var declaredSymbol = model.GetDeclaredSymbol(suffix);
        if(declaredSymbol is not null && declaredSymbol.Kind == SymbolKind.Property)
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

        if(stdlibType is null)
        {
            return null;
        }

        // Build parameter type list for lookup.
        // For now, use "unknown" for unknown-typed parameters since that's
        // how Type.Of is declared.
        var paramTypes = new List<string>();
        if(arguments is not null)
        {
            paramTypes.Add("unknown");
        }

        var template = intrinsicRegistry.TryGetStaticMethod(stdlibType, methodName, paramTypes);
        if(template is null)
        {
            return null;
        }

        // Emit arguments and substitute into the template.
        if(arguments is not null)
        {
            var arg0 = EmitExpression(arguments.GetExpression());
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
        if(arguments is null)
        {
            return Array.Empty<string>();
        }

        var types = new List<string>();
        foreach(var expr in arguments.GetAllExpressions())
        {
            var exprType = model.GetTypeOf(expr);
            if(exprType is not null)
            {
                var qualifiedName = GetQualifiedTypeNameFromType(exprType);
                types.Add(qualifiedName ?? exprType.DisplayName);
            }
        }

        return types;
    }

    private string EmitPrimary(PrimaryNode primary)
    {
        switch(primary)
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
                    var type = EmitElementTypeRef(arrayCreation.Type);
                    var size = EmitExpression(arrayCreation.Size);
                    return $"new {type}[{size}]";
                }
            case StackallocExpressionPrimaryNode stackallocExpr:
                {
                    // Mirror the array-creation case: the element type resolves to its
                    // CLR/backing name via EmitElementTypeRef and the size expression is
                    // emitted exactly once. The resulting `stackalloc T[n]` initializes a
                    // C# Span<T>/ReadOnlySpan<T> local (length erased — see CSharpTypeMap).
                    var type = EmitElementTypeRef(stackallocExpr.Type);
                    var size = EmitExpression(stackallocExpr.Size);
                    return $"stackalloc {type}[{size}]";
                }
            case ObjectCreationExpressionPrimaryNode objCreation:
                {
                    // ── Special case: new Array<T, Size>(fn) → per-index init loop ──
                    var exprTypeForArray = model.GetTypeOf(objCreation);
                    if(exprTypeForArray is ArrayType initArrayType)
                    {
                        var arrTypeName = ExtractQualifiedName(objCreation.Type);
                        if(arrTypeName is "Array" or "Standard.Array" && objCreation.TypeArgs is not null)
                        {
                            var typeArgNodes = new List<TypeArgumentNode> { objCreation.TypeArgs.TypeArgument };
                            foreach(var extra in objCreation.TypeArgs.TypeArguments)
                            {
                                typeArgNodes.Add(extra);
                            }

                            if(typeArgNodes.Count == 2)
                            {
                                var elementTypeName = CSharpTypeMap.GetTypeName(initArrayType.ElementType);
                                // Extract the size expression from the second type argument.
                                string sizeExpr;
                                if(typeArgNodes[1] is IntegerLiteralTypeArgumentTypeArgumentNode intLitArg)
                                {
                                    sizeExpr = intLitArg.Literal.Lexeme;
                                }
                                else if(initArrayType.KnownLength.HasValue)
                                {
                                    sizeExpr = initArrayType.KnownLength.Value.ToString();
                                }
                                else
                                {
                                    sizeExpr = "0"; // Fallback — should not happen for valid code.
                                }

                                // Emit the initializer function argument.
                                string fnExpr;
                                if(objCreation.Arguments is not null)
                                {
                                    var argExprs = objCreation.Arguments.GetAllExpressions();
                                    fnExpr = argExprs.Length > 0 ? EmitExpression(argExprs[0]) : "_ => default";
                                }
                                else
                                {
                                    fnExpr = "_ => default";
                                }

                                // Generate unique names to avoid collisions.
                                var suffix = arrayInitCounter++;
                                var arrVar = $"__arr_{suffix}";
                                var idxVar = $"__i_{suffix}";

                                // Queue the allocation + for-loop as pending statements.
                                var fnVar = $"__fn_{suffix}";
                                pendingArrayInitStatements.Add(
                                    $"System.Func<int, {elementTypeName}> {fnVar} = {fnExpr};");
                                pendingArrayInitStatements.Add(
                                    $"var {arrVar} = new {elementTypeName}[{sizeExpr}];");
                                pendingArrayInitStatements.Add(
                                    $"for (int {idxVar} = 0; {idxVar} < {sizeExpr}; {idxVar}++) {arrVar}[{idxVar}] = {fnVar}({idxVar});");

                                return arrVar;
                            }
                        }
                    }

                    var type = ExtractQualifiedName(objCreation.Type);

                    // When the source uses a relative namespace-qualified name (e.g., "IO.MemoryStream"),
                    // resolve to the fully-qualified CLR type name so the emitted C# is valid.
                    if(type.Contains('.'))
                    {
                        var resolvedExprType = model.GetTypeOf(objCreation);
                        if(resolvedExprType is ClassType resolvedCt && resolvedCt.ContainingNamespace is not null
                            && !resolvedCt.IsBackendBacked)
                        {
                            type = $"{resolvedCt.ContainingNamespace}.{resolvedCt.DisplayName}";
                        }
                        else if(resolvedExprType is StructType resolvedSt && resolvedSt.ContainingNamespace is not null
                            && !resolvedSt.IsBackendBacked)
                        {
                            type = $"{resolvedSt.ContainingNamespace}.{resolvedSt.DisplayName}";
                        }
                    }

                    // Look up the type's parameters to correctly emit carrier struct names
                    // for non-int dependent type arguments (uint, long, etc.).
                    var typeSymbol = model.AllSymbols
                        .FirstOrDefault(s => s.Name == type && s.Kind is SymbolKind.Class or SymbolKind.Struct or SymbolKind.Abstract);
                    var targetTypeParams = typeSymbol is not null ? GetTypeParametersFromType(typeSymbol.DeclaredType) : null;
                    var typeArgs = EmitTypeArgumentList(objCreation.TypeArgs, targetTypeParams);

                    // Check if the type being constructed is a type parameter with a parameterized
                    // constructor constraint — if so, emit as factory delegate invocation.
                    var exprType = model.GetTypeOf(objCreation);
                    if(exprType is TypeParameterType typeParamType && typeParamType.Constraints is not null)
                    {
                        var argTypes = GetArgumentTypes(objCreation.Arguments);
                        if(argTypes.Count > 0)
                        {
                            // Parameterized new T(args) → factory delegate invocation
                            var matchingConstraint = FindMatchingParameterizedConstraint(typeParamType, argTypes);
                            if(matchingConstraint is not null)
                            {
                                // Determine whether to use field name or parameter name:
                                // - Inside a method with factory params → use parameter name (factoryT_...)
                                // - Inside a class with factory fields → use field name (_factoryT_...)
                                var factoryName = currentMethodFactoryTypeParams is not null &&
                                    currentMethodFactoryTypeParams.Any(tp => tp.DisplayName == typeParamType.Name)
                                    ? GetFactoryParamName(typeParamType, matchingConstraint)
                                    : GetFactoryFieldName(typeParamType, matchingConstraint);
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
                    var aliasSymbol = model.AllSymbols
                        .FirstOrDefault(s => s.Name == type && s.Kind == SymbolKind.TypeAlias);
                    if(aliasSymbol?.DeclaredType is TypeAliasType objAlias)
                    {
                        // Only apply the alias if its arity matches the type arguments
                        // provided at the call site. e.g., `new Result<void>(...)` with 1
                        // type arg should NOT resolve through a non-generic alias
                        // `type Result = Result<string>` (arity 0).
                        var callSiteArity = 0;
                        if(objCreation.TypeArgs is not null)
                        {
                            callSiteArity = 1 + objCreation.TypeArgs.TypeArguments.Length;
                        }

                        if(callSiteArity == objAlias.TypeParameters.Count)
                        {
                            var hasConcreteTypeInCurrentNamespace = currentNamespace is not null
                                && model.AllSymbols.Any(s => s.Name == type
                                    && s.Kind is SymbolKind.Class or SymbolKind.Struct
                                    && GetSymbolContainingNamespace(s) == currentNamespace);
                            if(!hasConcreteTypeInCurrentNamespace)
                            {
                                var underlying = objAlias.UnderlyingType;
                                if(objCreation.TypeArgs is not null && objAlias.TypeParameters.Count > 0)
                                {
                                    // Generic alias with type arguments: emit definition + substituted args.
                                    if(underlying is ConstructedType ct)
                                    {
                                        type = CSharpTypeMap.GetTypeName(ct.GenericDefinition);
                                    }
                                    else if(underlying is SpanType spanUnderlying)
                                    {
                                        // SpanType bakes element type into GetTypeName (e.g., System.Span<T>).
                                        // We emit just the base name and keep the original call-site type args
                                        // so that the alias's type parameter T is substituted with the actual
                                        // type argument (e.g., int).
                                        type = spanUnderlying.IsReadOnly ? "System.ReadOnlySpan" : "System.Span";
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
                    }

                    var argsStr = EmitArgumentList(objCreation.Arguments);

                    // Call-site transformation: when constructing a type that has parameterized
                    // constructor constraints, append factory lambda arguments.
                    var constructedSymbol = model.AllSymbols
                        .FirstOrDefault(s => s.Name == type && s.Kind is SymbolKind.Class or SymbolKind.Struct);
                    if(constructedSymbol?.DeclaredType is not null)
                    {
                        var constructedTypeParams = GetTypeParametersFromType(constructedSymbol.DeclaredType);
                        if(HasParameterizedConstructorConstraints(constructedTypeParams))
                        {
                            var factoryLambdas = GenerateFactoryLambdas(constructedTypeParams, objCreation.TypeArgs);
                            if(factoryLambdas.Length > 0)
                            {
                                argsStr = argsStr.Length > 0
                                    ? $"{argsStr}, {factoryLambdas}"
                                    : factoryLambdas;
                            }
                        }
                    }

                    if(objCreation.Initializer is not null)
                    {
                        var initParts = new List<string>();
                        if(objCreation.Initializer.Assignments is not null)
                        {
                            var assignments = objCreation.Initializer.Assignments;
                            initParts.Add(EmitPropertyAssignmentEntry(assignments.PropertyAssignment));
                            foreach(var assignment in assignments.PropertyAssignments)
                            {
                                initParts.Add(EmitPropertyAssignmentEntry(assignment));
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
                    if(objCreationNoArgs.Initializer.Assignments is not null)
                    {
                        var assignments = objCreationNoArgs.Initializer.Assignments;
                        initParts.Add(EmitPropertyAssignmentEntry(assignments.PropertyAssignment));
                        foreach(var assignment in assignments.PropertyAssignments)
                        {
                            initParts.Add(EmitPropertyAssignmentEntry(assignment));
                        }
                    }

                    return $"new {type}{typeArgs}() {{ {string.Join(", ", initParts)} }}";
                }
            case TargetTypedNewExpressionPrimaryNode targetNew:
                {
                    var resolvedType = model.GetTypeOf(targetNew);
                    if(resolvedType is null or ErrorType)
                    {
                        return "default /* error: unresolved target-typed new */";
                    }

                    var typeName = CSharpTypeMap.GetTypeName(resolvedType);
                    var args = EmitArgumentList(targetNew.Arguments);

                    if(targetNew.Initializer is not null)
                    {
                        var initParts = new List<string>();
                        if(targetNew.Initializer.Assignments is not null)
                        {
                            var assignments = targetNew.Initializer.Assignments;
                            initParts.Add(EmitPropertyAssignmentEntry(assignments.PropertyAssignment));
                            foreach(var assignment in assignments.PropertyAssignments)
                            {
                                initParts.Add(EmitPropertyAssignmentEntry(assignment));
                            }
                        }

                        return $"new {typeName}({args}) {{ {string.Join(", ", initParts)} }}";
                    }

                    return $"new {typeName}({args})";
                }
            case TargetTypedNewExpressionNoArgsPrimaryNode targetNewNoArgs:
                {
                    var resolvedType = model.GetTypeOf(targetNewNoArgs);
                    if(resolvedType is null or ErrorType)
                    {
                        return "default /* error: unresolved target-typed new */";
                    }

                    var typeName = CSharpTypeMap.GetTypeName(resolvedType);
                    var initParts = new List<string>();
                    if(targetNewNoArgs.Initializer.Assignments is not null)
                    {
                        var assignments = targetNewNoArgs.Initializer.Assignments;
                        initParts.Add(EmitPropertyAssignmentEntry(assignments.PropertyAssignment));
                        foreach(var assignment in assignments.PropertyAssignments)
                        {
                            initParts.Add(EmitPropertyAssignmentEntry(assignment));
                        }
                    }

                    return $"new {typeName}() {{ {string.Join(", ", initParts)} }}";
                }
            case AnonymousTypeExpressionPrimaryNode anonType:
                {
                    var parts = new List<string>();

                    string formatAnonymousProperty(AnonymousPropertyNode prop)
                    {
                        return prop switch
                        {
                            ExplicitAnonymousPropertyAnonymousPropertyNode explicit_ =>
                                $"{explicit_.Name.Lexeme} = {EmitExpression(explicit_.Value)}",
                            ProjectedAnonymousPropertyAnonymousPropertyNode projected =>
                                EmitPostfix(projected.Projection),
                            _ => ""
                        };
                    }

                    parts.Add(formatAnonymousProperty(anonType.Properties.AnonymousProperty));
                    foreach(var additional in anonType.Properties.AnonymousPropertys)
                    {
                        parts.Add(formatAnonymousProperty(additional));
                    }

                    return $"new {{ {string.Join(", ", parts)} }}";
                }
            case ParenthesizedExpressionPrimaryNode paren:
                return $"({EmitExpression(paren.Inner)})";
            case TupleLiteralExpressionPrimaryNode tupleLit:
                {
                    // Emit C# tuple literal: (expr1, expr2) or (name1: expr1, name2: expr2).
                    var parts = new List<string>();

                    string formatTupleLiteralElement(TupleLiteralElementNode element)
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

                    parts.Add(formatTupleLiteralElement(tupleLit.First));
                    parts.Add(formatTupleLiteralElement(tupleLit.Rest));
                    foreach(var additional in tupleLit.TupleLiteralElements)
                    {
                        parts.Add(formatTupleLiteralElement(additional));
                    }

                    return $"({string.Join(", ", parts)})";
                }
            case EmptyArrayLiteralExpressionPrimaryNode:
                {
                    // Empty array literal [] — emit Array.Empty<T>() when target type is known.
                    // When no target type is available, this is a compile error (the type checker
                    // will have already reported MIST_TC_0096), so we emit a fallback.
                    if(arrayLiteralTargetElementType is not null)
                    {
                        var elementTypeName = CSharpTypeMap.GetTypeName(arrayLiteralTargetElementType);
                        return $"Array.Empty<{elementTypeName}>()";
                    }
                    // Fallback for error case — should not normally reach here because
                    // the type checker rejects empty array literals without target type.
                    return "Array.Empty<object>()";
                }
            case ArrayLiteralExpressionPrimaryNode arrayLiteral:
                {
                    var elements = new List<string> { EmitExpression(arrayLiteral.First) };
                    foreach(var elem in arrayLiteral.Expressions)
                    {
                        elements.Add(EmitExpression(elem));
                    }

                    // When the array literal is assigned to a variable with an explicit
                    // array type annotation, emit `new T[] { ... }` instead of `new[] { ... }`.
                    if(arrayLiteralTargetElementType is not null)
                    {
                        var elementTypeName = CSharpTypeMap.GetTypeName(arrayLiteralTargetElementType);
                        return $"new {elementTypeName}[] {{ {string.Join(", ", elements)} }}";
                    }

                    return $"new[] {{ {string.Join(", ", elements)} }}";
                }
            case IdentifierExpressionPrimaryNode ident:
                {
                    var identName = ident.Name.Lexeme;
                    // Check if this identifier resolves to a dependent type parameter.
                    // If so, emit TName.Value to access the carrier struct's value.
                    var resolvedType = model.GetTypeOf(ident);
                    if(resolvedType is DependentTypeParameterType)
                    {
                        return $"T{identName}.Value";
                    }
                    // Fallback: check if the name matches a dependent type parameter
                    // declared on the current method or its enclosing type. This prevents
                    // false positives where a field or property in a different type shares
                    // a name with a dependent type parameter declared elsewhere in the
                    // compilation (e.g., a property named "Count" colliding with a
                    // dependent param "Count:PositiveInt" from another class).
                    if(IsDependentTypeParameterName(identName)
                        && IsLocalDependentTypeParameterName(identName))
                    {
                        return $"T{identName}.Value";
                    }

                    // Check if this identifier resolves to a stdlib type that has
                    // a CSharpTypeMap mapping (e.g., Console → System.Console).
                    if(resolvedType is ClassType ct && ct.IsBackendBacked && ct.ContainingNamespace == "Standard")
                    {
                        var mapped = CSharpTypeMap.TryGetMappedName(identName);
                        if(mapped is not null)
                        {
                            return mapped;
                        }
                    }

                    if(resolvedType is StructType st && st.IsBackendBacked && st.ContainingNamespace == "Standard")
                    {
                        var mapped = CSharpTypeMap.TryGetMappedName(identName);
                        if(mapped is not null)
                        {
                            return mapped;
                        }
                    }

                    return identName;
                }
            case FieldKeywordExpressionPrimaryNode:
                return "field";
            case TypeLiteralExpressionPrimaryNode typeLiteral:
                {
                    // Check if this void type literal resolved as a value literal.
                    var resolvedType = model.GetTypeOf(typeLiteral);
                    if(resolvedType is not null && resolvedType.IsVoid())
                    {
                        // Value literal context: emit the Void singleton.
                        if(emitVoidAsNull)
                        {
                            return "null";
                        }

                        NeedsMistVoidStruct = true;
                        return "MistVoid.Instance";
                    }

                    // Existing behavior: type literal → typeof(...)
                    return $"typeof({EmitTypeReference(typeLiteral.TypeRef)})";
                }
            case DefaultExpressionPrimaryNode defaultExpr:
                {
                    // Resolve the type from the semantic model's expression type map.
                    var resolvedType = model.GetTypeOf(defaultExpr);
                    if(resolvedType is not null)
                    {
                        return EmitDefaultValueForType(resolvedType);
                    }

                    // Fallback: emit using the type reference text directly.
                    var typeRef = EmitTypeReference(defaultExpr.Type);
                    return $"default({typeRef})";
                }
            case DefaultLiteralExpressionPrimaryNode defaultLiteral:
                {
                    // The inferred type was recorded in the expression type map
                    // during Pass 2 or by the type checker's inference pass.
                    var resolvedType = model.GetTypeOf(defaultLiteral);
                    if(resolvedType is not null)
                    {
                        return EmitDefaultValueForType(resolvedType);
                    }

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
    /// For type parameters with a default constraint, emits the factory delegate invocation.
    /// </summary>
    private static string EmitDefaultValueForType(IType type)
    {
        // Type parameter with default constraint → invoke the factory delegate parameter.
        if(type is TypeParameterType typeParamType &&
            typeParamType.Constraints?.HasDefaultConstraint == true)
        {
            return $"__default{typeParamType.Name}()";
        }

        // Check if this is a backend-backed type (primitive or string).
        var isBackendBacked = type switch
        {
            StructType st => st.IsBackendBacked,
            ClassType ct => ct.IsBackendBacked,
            _ => false,
        };

        if(isBackendBacked)
        {
            var csharpName = CSharpTypeMap.GetTypeName(type);

            // String default in Mist is "" (not null).
            if(csharpName == "string")
            {
                return "\"\"";
            }

            // Primitive types use C# default literal.
            return $"default({csharpName})";
        }

        // Non-backend-backed class or struct — use the factory method.
        if(type is ClassType or StructType)
        {
            var typeName = CSharpTypeMap.GetTypeName(type);
            return $"{typeName}.__CreateDefault()";
        }

        // Enum type with a designated default variant — use the factory method.
        if(type is EnumType enumType && enumType.Defaultability is DefaultabilityStatus.Explicit)
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
        _ = sb.Append("$\"");

        foreach(var part in interpolated.Parts)
        {
            switch(part)
            {
                case TextPartInterpolatedPartNode textPart:
                    _ = sb.Append(textPart.Text.Lexeme);
                    break;
                case ExpressionPartInterpolatedPartNode exprPart:
                    _ = sb.Append('{');
                    var interpolatedExpr = EmitExpression(exprPart.Expression);
                    // When the expression's Mist type is an optional union (T | void),
                    // wrap the emission so null (void) displays as "Void" instead of empty.
                    var interpolatedExprType = model.GetTypeOf(exprPart.Expression);
                    _ = IsOptionalUnionType(interpolatedExprType)
                        ? sb.Append($"(object?){interpolatedExpr} ?? \"Void\"")
                        : sb.Append(interpolatedExpr);

                    _ = sb.Append('}');
                    break;
            }
        }

        _ = sb.Append('"');
        return sb.ToString();
    }

    // ─── Modifier Emission ───────────────────────────────────────────────

    private static string EmitModifiers(ImmutableArray<ModifierNode> modifiers)
    {
        if(modifiers.IsEmpty)
        {
            return "";
        }

        var parts = new List<string>();
        foreach(var mod in modifiers)
        {
            switch(mod)
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

        if(parts.Count == 0)
        {
            return "";
        }

        return string.Join(" ", parts) + " ";
    }

    // ─── Attribute Emission Helpers ─────────────────────────────────────

    /// <summary>
    /// Emits C# attribute annotations for a declaration's attribute lists.
    /// Each <see cref="AttributeListNode"/> is emitted as <c>[attr1, attr2]</c> on its own line.
    /// </summary>
    private void EmitAttributeLists(ImmutableArray<AttributeListNode> attributeLists)
    {
        if(attributeLists.IsDefaultOrEmpty)
        {
            return;
        }

        foreach(var attrList in attributeLists)
        {
            var attrs = new List<AttributeNode> { attrList.Attribute };
            foreach(var additional in attrList.Attributes)
            {
                attrs.Add(additional);
            }

            var parts = new List<string>(attrs.Count);
            foreach(var attr in attrs)
            {
                var name = ExtractQualifiedName(attr.Name);
                var typeArgs = EmitTypeArgumentList(attr.TypeArgs);
                var args = EmitAttributeArguments(attr);
                parts.Add($"{name}{typeArgs}{args}");
            }

            AppendLine($"[{string.Join(", ", parts)}]");
        }
    }

    /// <summary>
    /// Emits the parenthesized argument list for a single attribute.
    /// Returns an empty string if the attribute has no arguments (no parens in source).
    /// Returns "()" if the attribute has empty parentheses.
    /// Emits positional arguments first, then named arguments as <c>Name = value</c>.
    /// </summary>
    private string EmitAttributeArguments(AttributeNode attr)
    {
        if(attr.Arguments is null)
        {
            return "";
        }

        if(attr.Arguments.ArgList is null)
        {
            return "()";
        }

        var argList = attr.Arguments.ArgList;
        var allArgs = new List<AttributeArgumentNode>(1 + argList.AttributeArguments.Length)
        {
            argList.AttributeArgument
        };

        foreach(var additional in argList.AttributeArguments)
        {
            allArgs.Add(additional);
        }

        // Emit positional arguments first, then named arguments.
        var parts = new List<string>(allArgs.Count);
        foreach(var arg in allArgs)
        {
            if(arg is PositionalAttributeArgumentAttributeArgumentNode positional)
            {
                parts.Add(EmitAttributeExpression(positional.Value));
            }
        }

        foreach(var arg in allArgs)
        {
            if(arg is NamedAttributeArgumentAttributeArgumentNode named)
            {
                parts.Add($"{named.Name.Lexeme} = {EmitAttributeExpression(named.Value)}");
            }
        }

        return $"({string.Join(", ", parts)})";
    }

    /// <summary>
    /// Emits a single attribute expression, handling both generic type literal expressions
    /// (e.g., <c>List&lt;int&gt;</c> → <c>typeof(List&lt;int&gt;)</c>) and regular expressions.
    /// </summary>
    private string EmitAttributeExpression(AttributeExpressionNode attrExpr)
    {
        if(attrExpr is GenericTypeLiteralExpressionAttributeExpressionNode genericTypeLiteral)
        {
            var name = ExtractQualifiedName(genericTypeLiteral.Name);
            var typeArgs = EmitTypeArgumentList(genericTypeLiteral.TypeArgs);
            return $"typeof({name}{typeArgs})";
        }

        if(attrExpr is RegularExpressionAttributeExpressionNode regular)
        {
            return EmitExpression(regular.Expr);
        }

        return attrExpr.ToString() ?? "";
    }

    // ─── Dependent Type Emission Helpers ─────────────────────────────────

    /// <summary>
    /// Emits <c>[DependentType("ParamName", typeof(valueType))]</c> attributes for each
    /// dependent type parameter on a type declaration.
    /// </summary>
    private void EmitDependentTypeAttributes(IReadOnlyList<IType> typeParameters)
    {
        foreach(var typeParam in typeParameters)
        {
            if(typeParam is DependentTypeParameterType depParam)
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
        foreach(var typeParam in typeParameters)
        {
            if(typeParam is DependentTypeParameterType depParam)
            {
                var keyword = CSharpTypeMap.GetKeyword(depParam.ValueType);
                if(emitted)
                {
                    AppendLine();
                }

                AppendLine($"public static {keyword} {depParam.Name} => T{depParam.Name}.Value;");
                emitted = true;
            }
        }

        return emitted;
    }

    // ─── Utilities ───────────────────────────────────────────────────────

    private static string EmitTypeParameterList(TypeParameterListNode? typeParams)
    {
        if(typeParams is null)
        {
            return "";
        }

        var parts = new List<string>
        {
            EmitSingleTypeParameterName(typeParams.TypeParameter)
        };
        foreach(var tp in typeParams.TypeParameters)
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
            NamedConstraintDependentTypeParameterTypeParameterNode named => $"T{named.Name.Lexeme}",
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
        if(typeArgs is null)
        {
            return "";
        }

        var allArgs = new List<TypeArgumentNode> { typeArgs.TypeArgument };
        foreach(var arg in typeArgs.TypeArguments)
        {
            allArgs.Add(arg);
        }

        var parts = new List<string>(allArgs.Count);
        for(var i = 0; i < allArgs.Count; i++)
        {
            string? expectedKeyword = null;
            if(targetTypeParams is not null && i < targetTypeParams.Count
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
    /// Emits inferred type arguments for a method call node where type arguments were
    /// omitted in source but successfully inferred during type checking. Queries the
    /// semantic model for recorded inferred type arguments and formats them as a C#
    /// type argument list string (e.g., <c>&lt;int, string&gt;</c>).
    /// Returns an empty string if no inferred type arguments are recorded.
    /// </summary>
    private string EmitInferredTypeArguments(MethodCallSuffixPostfixSuffixNode methodCall)
    {
        var inferredTypes = model.GetInferredTypeArguments(methodCall);
        if(inferredTypes is null || inferredTypes.Count == 0)
        {
            return "";
        }

        var parts = new List<string>(inferredTypes.Count);
        foreach(var type in inferredTypes)
        {
            parts.Add(CSharpTypeMap.GetTypeName(type));
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
    /// Resolves the IType for a type argument node using the semantic model.
    /// Returns null if the type cannot be resolved (e.g., literal type arguments).
    /// </summary>
    private IType? ResolveTypeFromTypeArgument(TypeArgumentNode arg)
    {
        if(arg is TypeReferenceTypeArgumentTypeArgumentNode typeRefArg)
        {
            return model.GetTypeOf(typeRefArg.Type);
        }

        return null;
    }

    /// <summary>
    /// Emits a single type argument with an optional expected type keyword for carrier struct naming.
    /// When <paramref name="expectedKeyword"/> is provided (e.g., "uint", "long"), integer literals
    /// will generate carrier structs with the correct type (e.g., <c>Const_Uint_100</c> instead of
    /// <c>Const_Int_100</c>).
    /// </summary>
    private string EmitSingleTypeArgument(TypeArgumentNode arg, string? expectedKeyword)
    {
        switch(arg)
        {
            case TypeReferenceTypeArgumentTypeArgumentNode typeRefArg:
                {
                    // Check if this type reference resolves to a DependentTypeParameterType
                    // (forwarded dependent parameter like `FixedBuffer<N>` where N is a dependent param).
                    var resolvedType = model.GetTypeOf(typeRefArg.Type);
                    if(resolvedType is DependentTypeParameterType depParam)
                    {
                        return $"T{depParam.Name}";
                    }

                    // Also check by name: if the type reference is a simple identifier that matches
                    // a dependent type parameter name on any type in the model, emit T-prefixed.
                    if(typeRefArg.Type.First is NamedSingleTypeRefSingleTypeRefNode namedRef
                        && namedRef.TypeArgs is null
                        && namedRef.ArrayRank is null)
                    {
                        var name = ExtractQualifiedName(namedRef.Name);
                        if(IsDependentTypeParameterName(name))
                        {
                            return $"T{name}";
                        }
                        // If we have an expectedKeyword, this position expects a dependent
                        // value argument. A bare identifier here that doesn't resolve to a
                        // known type is a forwarded parameter.
                        if(expectedKeyword is not null)
                        {
                            // Verify it's not a known type before treating as forwarded param.
                            var isKnownType = model.AllSymbols.Any(s =>
                                s.Name == name && s.Kind is SymbolKind.Class or SymbolKind.Struct
                                    or SymbolKind.Abstract or SymbolKind.Enum or SymbolKind.TypeAlias);
                            if(!isKnownType)
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
        return dependentTypeParamNames.Contains(name);
    }

    /// <summary>
    /// Checks if a name corresponds to a dependent type parameter declared on the
    /// current method or enclosing type — not just any type in the compilation.
    /// This prevents cross-type name collisions where a field named "Count" in one
    /// type incorrectly matches a dependent param "Count" on a different type.
    /// </summary>
    private bool IsLocalDependentTypeParameterName(string name)
    {
        // Check the current method's type parameters.
        if(currentMethodSymbol is not null)
        {
            foreach(var tp in currentMethodSymbol.TypeParameters)
            {
                if(tp is DependentTypeParameterType depParam && depParam.Name == name)
                {
                    return true;
                }
            }
        }

        // Check the current enclosing type's dependent parameters.
        if(currentTypeDependentParamNames is not null
            && currentTypeDependentParamNames.Contains(name))
        {
            return true;
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
        if(long.TryParse(lexeme, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var value))
        {
            if(negated)
            {
                value = -value;
            }

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
        if(long.TryParse(hex, System.Globalization.NumberStyles.HexNumber,
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
        foreach(var symbol in model.AllSymbols)
        {
            if(symbol.Name == name && symbol.Kind is SymbolKind.Class or SymbolKind.Struct or SymbolKind.Abstract)
            {
                var typeParams = GetTypeParametersFromType(symbol.DeclaredType);
                if(typeParams.Count > 0)
                {
                    return typeParams;
                }
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
        if(lexeme.Length >= 3 && lexeme[0] == '\'' && lexeme[^1] == '\'')
        {
            var inner = lexeme[1..^1];
            var c = inner.Length == 1
                ? inner[0]
                : inner.StartsWith('\\')
                    ? inner switch
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
                    }
                    : inner[0];
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
        var parts = new List<string>
        {
            EmitBaseTypeRef(baseList.First)
        };
        foreach(var rest in baseList.BaseTypeRefs)
        {
            parts.Add(EmitBaseTypeRef(rest));
        }

        return string.Join(", ", parts);
    }

    /// <summary>
    /// Emits a base list skipping the first entry (which was consumed as the base struct type).
    /// Returns an empty string if no remaining entries exist.
    /// </summary>
    private string EmitBaseListSkippingFirst(BaseListNode baseList)
    {
        var parts = new List<string>();
        foreach(var rest in baseList.BaseTypeRefs)
        {
            parts.Add(EmitBaseTypeRef(rest));
        }

        return string.Join(", ", parts);
    }

    /// <summary>
    /// Emits a base list skipping the first entry (base struct type) while resolving associated
    /// type arguments for abstract types that have associated type slots.
    /// Returns an empty string if no remaining entries exist.
    /// </summary>
    private string EmitBaseListSkippingFirstWithAssociatedTypeArgs(
        BaseListNode baseList,
        IScope implementorScope,
        ClassType? implementorClassType,
        string implementorName,
        string implementorTypeParams)
    {
        var parts = new List<string>();
        foreach(var rest in baseList.BaseTypeRefs)
        {
            parts.Add(EmitBaseTypeRefWithAssociatedTypes(rest, implementorScope, implementorClassType, implementorName, implementorTypeParams));
        }

        return string.Join(", ", parts);
    }

    private string ExtractQualifiedName(QualifiedIdentifierNode qualifiedId)
    {
        var span = qualifiedId.Span;
        if(span.Length > 0 && span.Start + span.Length <= sourceText.Contents.Length)
        {
            return sourceText.Contents.Substring(span.Start, span.Length);
        }

        return qualifiedId.IDENTIFIER.Lexeme;
    }

    // ─── Generic Constraint Emission ────────────────────────────────────

    /// <summary>
    /// Emits C# <c>where</c> clauses for all constrained type parameters on a type or method.
    /// Constraints are reordered to C#'s required order: class/struct → base class → interfaces → new() last.
    /// Parameterized constructor constraints are excluded from the where clause (they use factory delegate lowering).
    /// </summary>
    private void EmitConstraintClauses(IReadOnlyList<IType> typeParameters)
    {
        foreach(var typeParam in typeParameters)
        {
            // Emit carrier interface constraint for dependent type parameters.
            if(typeParam is DependentTypeParameterType depParam)
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

            if(typeParam is not TypeParameterType tpt)
            {
                continue;
            }

            var constraints = tpt.Constraints;
            if(constraints is null)
            {
                continue;
            }

            var parts = new List<string>();

            // 1. class/struct first
            if(constraints.HasClassConstraint)
            {
                parts.Add("class");
            }
            else if(constraints.HasStructConstraint)
            {
                parts.Add("struct");
            }

            // 2. Base class
            if(constraints.BaseClassConstraint is not null)
            {
                parts.Add(CSharpTypeMap.GetTypeName(constraints.BaseClassConstraint));
            }

            // 3. Abstract types
            foreach(var abs in constraints.AbstractConstraints)
            {
                parts.Add(CSharpTypeMap.GetTypeName(abs));
            }

            // 4. new() last (only parameterless constructor constraint)
            var hasParameterlessNew = constraints.ConstructorConstraints
                .Any(c => c.Count == 0);
            if(hasParameterlessNew)
            {
                parts.Add("new()");
            }

            if(parts.Count > 0)
            {
                AppendLine($"    where {tpt.Name} : {string.Join(", ", parts)}");
            }

            // 5. Emit a comment for the default constraint (no C# equivalent)
            if(constraints.HasDefaultConstraint)
            {
                AppendLine($"    // where {tpt.Name} : default (Mist constraint \u2014 enforced at compile time)");
            }
        }
    }

    /// <summary>
    /// Emits constraint clauses for method type parameters, appending synthesized associated
    /// type parameters as additional type arguments to abstract type constraints that declare
    /// associated type slots. For example, <c>where T : Iterable&lt;int&gt;</c> becomes
    /// <c>where T : IIterable&lt;int, TEnumerator&gt;</c> when Iterable has an Enumerator slot.
    /// </summary>
    private void EmitConstraintClausesWithAssociatedTypes(
        IReadOnlyList<IType> typeParameters,
        List<(string TypeParamName, AssociatedTypeSlotType Slot)> associatedSlots)
    {
        foreach(var typeParam in typeParameters)
        {
            // Emit carrier interface constraint for dependent type parameters.
            if(typeParam is DependentTypeParameterType depParam)
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

            if(typeParam is not TypeParameterType tpt)
            {
                continue;
            }

            var constraints = tpt.Constraints;
            if(constraints is null)
            {
                continue;
            }

            var parts = new List<string>();

            // 1. class/struct first
            if(constraints.HasClassConstraint)
            {
                parts.Add("class");
            }
            else if(constraints.HasStructConstraint)
            {
                parts.Add("struct");
            }

            // 2. Base class
            if(constraints.BaseClassConstraint is not null)
            {
                parts.Add(CSharpTypeMap.GetTypeName(constraints.BaseClassConstraint));
            }

            // 3. Abstract types — append synthesized type parameters for abstract types with associated type slots.
            foreach(var abs in constraints.AbstractConstraints)
            {
                var abstractTypeName = CSharpTypeMap.GetTypeName(abs);

                // Check if this abstract type has associated type slots that were lifted.
                var abstractDef = abs is ConstructedType ct
                    ? ct.GenericDefinition as AbstractType
                    : abs as AbstractType;

                if(abstractDef is not null)
                {
                    // Collect slots from this abstract type that are in our associated slots list.
                    var slotsForThisAbstract = associatedSlots
                        .Where(s => s.TypeParamName == tpt.Name
                            && s.Slot.DeclaringType == abstractDef)
                        .Select(s => $"T{s.Slot.DisplayName}")
                        .ToList();

                    if(slotsForThisAbstract.Count > 0)
                    {
                        var synthesizedArgs = string.Join(", ", slotsForThisAbstract);
                        // Append synthesized type parameters to the abstract type arguments.
                        if(abstractTypeName.EndsWith(">"))
                        {
                            // Already has type args: insert before closing >.
                            abstractTypeName = $"{abstractTypeName[..^1]}, {synthesizedArgs}>";
                        }
                        else
                        {
                            // No type args: add them.
                            abstractTypeName = $"{abstractTypeName}<{synthesizedArgs}>";
                        }
                    }
                }

                parts.Add(abstractTypeName);
            }

            // 4. new() last (only parameterless constructor constraint)
            var hasParameterlessNew = constraints.ConstructorConstraints
                .Any(c => c.Count == 0);
            if(hasParameterlessNew)
            {
                parts.Add("new()");
            }

            if(parts.Count > 0)
            {
                AppendLine($"    where {tpt.Name} : {string.Join(", ", parts)}");
            }

            // 5. Emit a comment for the default constraint (no C# equivalent)
            if(constraints.HasDefaultConstraint)
            {
                AppendLine($"    // where {tpt.Name} : default (Mist constraint \u2014 enforced at compile time)");
            }
        }
    }
    /// <summary>
    /// Gets the parameterized (non-parameterless) constructor constraints for a type parameter.
    /// These are the constraints that require factory delegate lowering.
    /// </summary>
    private static IReadOnlyList<IReadOnlyList<IType>> GetParameterizedConstructorConstraints(TypeParameterConstraints? constraints)
    {
        if(constraints is null)
        {
            return Array.Empty<IReadOnlyList<IType>>();
        }

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
        var paramPart = string.Join("_", paramTypes.Select(SanitizeTypeName));
        return $"_factory{typeParam.Name}_{paramPart}";
    }

    /// <summary>
    /// Generates the factory delegate parameter name for a parameterized constructor constraint.
    /// E.g., for new(int, string) on T → "factoryT_int_string"
    /// </summary>
    private static string GetFactoryParamName(TypeParameterType typeParam, IReadOnlyList<IType> paramTypes)
    {
        var paramPart = string.Join("_", paramTypes.Select(SanitizeTypeName));
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
        foreach(var tp in typeParameters)
        {
            if(tp is not TypeParameterType typeParam)
            {
                continue;
            }

            var parameterizedConstraints = GetParameterizedConstructorConstraints(typeParam.Constraints);
            foreach(var paramTypes in parameterizedConstraints)
            {
                var fieldName = GetFactoryFieldName(typeParam, paramTypes);
                var delegateType = GetFactoryDelegateType(typeParam, paramTypes);
                AppendLine($"private readonly {delegateType} {fieldName};");
            }
        }
    }

    /// <summary>
    /// Checks whether any type parameter has parameterized constructor constraints
    /// or default constraints that require factory delegate lowering.
    /// </summary>
    private static bool HasParameterizedConstructorConstraints(IReadOnlyList<IType> typeParameters)
    {
        return typeParameters.Any(tp =>
            tp is TypeParameterType tpt && tpt.Constraints is not null &&
            (tpt.Constraints.ConstructorConstraints.Any(c => c.Count > 0) ||
             tpt.Constraints.HasDefaultConstraint));
    }

    /// <summary>
    /// Emits a synthetic constructor that accepts factory delegates for parameterized constructor constraints.
    /// </summary>
    private void EmitFactoryDelegateConstructor(string typeName, IReadOnlyList<IType> typeParameters)
    {
        var parameters = new List<string>();
        var assignments = new List<string>();

        foreach(var tp in typeParameters)
        {
            if(tp is not TypeParameterType typeParam)
            {
                continue;
            }

            var parameterizedConstraints = GetParameterizedConstructorConstraints(typeParam.Constraints);
            foreach(var paramTypes in parameterizedConstraints)
            {
                var paramName = GetFactoryParamName(typeParam, paramTypes);
                var fieldName = GetFactoryFieldName(typeParam, paramTypes);
                var delegateType = GetFactoryDelegateType(typeParam, paramTypes);
                parameters.Add($"{delegateType} {paramName}");
                assignments.Add($"{fieldName} = {paramName};");
            }
        }

        if(parameters.Count == 0)
        {
            return;
        }

        AppendLine();
        AppendLine($"public {typeName}({string.Join(", ", parameters)})");
        AppendLine("{");
        indentLevel++;
        foreach(var assignment in assignments)
        {
            AppendLine(assignment);
        }

        indentLevel--;
        AppendLine("}");
    }

    /// <summary>
    /// Emits factory field assignment statements for use inside user-defined constructors.
    /// E.g., "_factoryT_int = factoryT_int;"
    /// </summary>
    private void EmitFactoryFieldAssignments(IReadOnlyList<IType> typeParameters)
    {
        foreach(var tp in typeParameters)
        {
            if(tp is not TypeParameterType typeParam)
            {
                continue;
            }

            var parameterizedConstraints = GetParameterizedConstructorConstraints(typeParam.Constraints);
            foreach(var paramTypes in parameterizedConstraints)
            {
                var paramName = GetFactoryParamName(typeParam, paramTypes);
                var fieldName = GetFactoryFieldName(typeParam, paramTypes);
                AppendLine($"{fieldName} = {paramName};");
            }
        }
    }

    /// <summary>
    /// Gets the additional factory delegate parameters that should be appended to a method signature
    /// for generic methods with parameterized constructor constraints or default constraints.
    /// </summary>
    private static string GetFactoryDelegateMethodParams(IReadOnlyList<IType> typeParameters)
    {
        var parameters = new List<string>();
        foreach(var tp in typeParameters)
        {
            if(tp is not TypeParameterType typeParam)
            {
                continue;
            }

            var parameterizedConstraints = GetParameterizedConstructorConstraints(typeParam.Constraints);
            foreach(var paramTypes in parameterizedConstraints)
            {
                var paramName = GetFactoryParamName(typeParam, paramTypes);
                var delegateType = GetFactoryDelegateType(typeParam, paramTypes);
                parameters.Add($"{delegateType} {paramName}");
            }

            // Add a Func<T> parameter for default-constrained type parameters.
            // This enables default(T) to produce the correct default value at runtime.
            if(typeParam.Constraints?.HasDefaultConstraint == true)
            {
                parameters.Add($"Func<{typeParam.Name}> __default{typeParam.Name}");
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
        if(typeParam.Constraints is null)
        {
            return null;
        }
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
            AbstractType it => it.TypeParameters,
            _ => Array.Empty<IType>()
        };
    }

    /// <summary>
    /// Collects the resolved types of all arguments in an argument list node.
    /// </summary>
    private IReadOnlyList<IType> GetArgumentTypes(ArgumentListNode? argList)
    {
        if(argList is null)
        {
            return Array.Empty<IType>();
        }

        var types = new List<IType>();
        foreach(var expr in argList.GetAllExpressions())
        {
            var t = model.GetTypeOf(expr);
            if(t is not null)
            {
                types.Add(t);
            }
        }

        return types;
    }

    /// <summary>
    /// Generates factory lambda expressions for call-site transformation.
    /// When constructing a type with parameterized constructor constraints,
    /// the caller must supply lambda expressions that create instances.
    /// E.g., for new(int) on T with type arg MyClass → "(arg0) => new MyClass(arg0)"
    /// Also generates default factory lambdas for default-constrained type parameters.
    /// </summary>
    private string GenerateFactoryLambdas(IReadOnlyList<IType> typeParameters, TypeArgumentListNode? typeArgs)
    {
        var lambdas = new List<string>();

        // Resolve type arguments from the type argument list node.
        var resolvedTypeArgs = new List<string>();
        if(typeArgs is not null)
        {
            resolvedTypeArgs.Add(EmitSingleTypeArgument(typeArgs.TypeArgument));
            foreach(var arg in typeArgs.TypeArguments)
            {
                resolvedTypeArgs.Add(EmitSingleTypeArgument(arg));
            }
        }

        // Also resolve IType instances for the type arguments (needed for default factory emission).
        var resolvedITypes = new List<IType?>();
        if(typeArgs is not null)
        {
            resolvedITypes.Add(ResolveTypeFromTypeArgument(typeArgs.TypeArgument));
            foreach(var arg in typeArgs.TypeArguments)
            {
                resolvedITypes.Add(ResolveTypeFromTypeArgument(arg));
            }
        }

        for(var i = 0; i < typeParameters.Count; i++)
        {
            if(typeParameters[i] is not TypeParameterType typeParam)
            {
                continue;
            }

            var parameterizedConstraints = GetParameterizedConstructorConstraints(typeParam.Constraints);

            // Get the concrete type argument for this type parameter.
            var concreteType = i < resolvedTypeArgs.Count ? resolvedTypeArgs[i] : typeParam.Name;

            foreach(var paramTypes in parameterizedConstraints)
            {
                // Generate lambda: (arg0, arg1, ...) => new ConcreteType(arg0, arg1, ...)
                var argNames = Enumerable.Range(0, paramTypes.Count)
                    .Select(j => $"arg{j}")
                    .ToList();
                var argList = string.Join(", ", argNames);
                lambdas.Add($"({argList}) => new {concreteType}({argList})");
            }

            // Generate default factory lambda for default-constrained type parameters.
            if(typeParam.Constraints?.HasDefaultConstraint == true)
            {
                var resolvedType = i < resolvedITypes.Count ? resolvedITypes[i] : null;
                if(resolvedType is not null)
                {
                    var defaultExpr = EmitDefaultValueForType(resolvedType);
                    lambdas.Add($"() => {defaultExpr}");
                }
                else
                {
                    lambdas.Add($"() => {concreteType}.__CreateDefault()");
                }
            }
        }

        return string.Join(", ", lambdas);
    }

    // ─── Associated Type Slot Helpers ───────────────────────────────────

    /// <summary>
    /// Collects all <see cref="AssociatedTypeSlotType"/> members from the given symbol's declared type.
    /// </summary>
    private static List<AssociatedTypeSlotType> CollectAssociatedTypeSlots(ISymbol? symbol)
    {
        var type = symbol?.DeclaredType;
        return CollectAssociatedTypeSlotsFromType(type);
    }

    /// <summary>
    /// Collects all <see cref="AssociatedTypeSlotType"/> members from the given type's member scope.
    /// </summary>
    private static List<AssociatedTypeSlotType> CollectAssociatedTypeSlotsFromType(IType? type)
    {
        if(type is null)
        {
            return new List<AssociatedTypeSlotType>();
        }

        var scope = type switch
        {
            AbstractType it => it.MemberScope,
            _ => null
        };

        if(scope is null)
        {
            return new List<AssociatedTypeSlotType>();
        }

        var slots = new List<AssociatedTypeSlotType>();
        foreach(var member in scope.Members)
        {
            if(member.Kind == SymbolKind.TypeAlias && member.DeclaredType is AssociatedTypeSlotType slotType)
            {
                slots.Add(slotType);
            }
        }

        return slots;
    }

    /// <summary>
    /// Builds a substitution dictionary mapping associated type slot names to their
    /// synthesized type parameter names (T + slot name).
    /// </summary>
    private static Dictionary<string, string> BuildAssociatedTypeSubstitutions(List<AssociatedTypeSlotType> slots)
    {
        var substitutions = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach(var slot in slots)
        {
            substitutions[slot.DisplayName] = $"T{slot.DisplayName}";
        }

        return substitutions;
    }

    /// <summary>
    /// Collects associated type slots from a method's type parameters' constraints.
    /// For each type parameter constrained by an interface with associated type slots,
    /// returns the slots paired with the constraining type parameter name.
    /// Used by <see cref="EmitMethodDeclaration"/> to synthesize additional type parameters
    /// for generic functions constrained by abstract types with associated types.
    /// </summary>
    private static List<(string TypeParamName, AssociatedTypeSlotType Slot)> CollectAssociatedTypeSlotsFromMethodTypeParams(
        IReadOnlyList<IType> typeParameters)
    {
        var result = new List<(string TypeParamName, AssociatedTypeSlotType Slot)>();
        var seenSlotNames = new HashSet<string>(StringComparer.Ordinal);

        foreach(var typeParam in typeParameters)
        {
            if(typeParam is not TypeParameterType tpt)
            {
                continue;
            }

            if(tpt.Constraints is null)
            {
                continue;
            }

            // Check abstract type constraints for associated type slots.
            foreach(var abstractConstraint in tpt.Constraints.AbstractConstraints)
            {
                var abstractDef = abstractConstraint is ConstructedType ct
                    ? ct.GenericDefinition as AbstractType
                    : abstractConstraint as AbstractType;

                if(abstractDef is null)
                {
                    continue;
                }

                foreach(var member in abstractDef.MemberScope.Members)
                {
                    if(member.DeclaredType is AssociatedTypeSlotType slotType
                        && seenSlotNames.Add(slotType.DisplayName))
                    {
                        result.Add((tpt.Name, slotType));
                    }
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Emits <c>where</c> constraint clauses for synthesized associated type slot parameters.
    /// </summary>
    private void EmitAssociatedTypeSlotConstraints(List<AssociatedTypeSlotType> slots)
    {
        foreach(var slot in slots)
        {
            var constraints = slot.Constraints;
            if(constraints is null)
            {
                continue;
            }

            var parts = new List<string>();

            // 1. struct/class first (C# requires these to be first in where clause)
            if(constraints.HasStructConstraint)
            {
                parts.Add("struct");
            }
            else if(constraints.HasClassConstraint)
            {
                parts.Add("class");
            }

            // 2. Base class
            if(constraints.BaseClassConstraint is not null)
            {
                parts.Add(CSharpTypeMap.GetTypeName(constraints.BaseClassConstraint));
            }

            // 3. Abstract types
            foreach(var abs in constraints.AbstractConstraints)
            {
                parts.Add(CSharpTypeMap.GetTypeName(abs));
            }

            // 4. new() last
            var hasParameterlessNew = constraints.ConstructorConstraints
                .Any(c => c.Count == 0);
            if(hasParameterlessNew)
            {
                parts.Add("new()");
            }

            if(parts.Count > 0)
            {
                AppendLine($"    where T{slot.DisplayName} : {string.Join(", ", parts)}");
            }
        }
    }

    /// <summary>
    /// Erases a parameterized associated type slot reference to its first interface constraint.
    /// For example, <c>Mapped&lt;string&gt;</c> where <c>type Mapped&lt;TOut&gt; : ITransform&lt;TIn, TOut&gt;</c>
    /// becomes <c>ITransform&lt;TIn, string&gt;</c>. The slot's own type parameters are substituted
    /// with the provided type arguments from the usage site.
    /// </summary>
    private string? EraseParameterizedSlotToConstraint(
        AssociatedTypeSlotType slot,
        TypeArgumentListNode usageTypeArgs,
        Dictionary<string, string>? substitutions)
    {
        var constraints = slot.Constraints;
        if(constraints is null)
        {
            return null;
        }

        // Use the first interface constraint as the erasure target.
        var constraintType = constraints.AbstractConstraints.Count > 0
            ? constraints.AbstractConstraints[0]
            : null;

        if(constraintType is null)
        {
            return null;
        }

        // Build the erased type by substituting type parameters in the constraint.
        // The constraint may be a ConstructedType like ITransform<TIn, TOut>.
        // We need to replace:
        //   - The slot's own type params (TOut) with the usage-site type args (string)
        //   - The interface's type params (TIn) with their concrete values (int)

        // Collect slot type param names → usage args mapping.
        var slotTypeParams = slot.TypeParameters;
        var usageArgs = new List<string>
        {
            EmitSingleTypeArgument(usageTypeArgs.TypeArgument)
        };
        foreach(var arg in usageTypeArgs.TypeArguments)
        {
            usageArgs.Add(EmitSingleTypeArgument(arg));
        }

        // Apply substitutions to the usage args (in case they reference interface type params).
        if(substitutions is not null)
        {
            for(var i = 0; i < usageArgs.Count; i++)
            {
                if(substitutions.TryGetValue(usageArgs[i], out var sub))
                {
                    usageArgs[i] = sub;
                }
            }
        }

        // Build slot param name → usage arg mapping.
        var slotParamMap = new Dictionary<string, string>(StringComparer.Ordinal);
        for(var i = 0; i < slotTypeParams.Count && i < usageArgs.Count; i++)
        {
            var paramName = slotTypeParams[i] is TypeParameterType tpt ? tpt.Name : slotTypeParams[i].DisplayName;
            slotParamMap[paramName] = usageArgs[i];
        }

        // Now build the erased type name by resolving the constraint type's structure.
        return BuildErasedConstraintTypeName(constraintType, slotParamMap, substitutions);
    }

    /// <summary>
    /// Builds the C# type name for a constraint type, substituting type parameters
    /// with their concrete values from the slot and abstract type mappings.
    /// </summary>
    private static string BuildErasedConstraintTypeName(
        IType constraintType,
        Dictionary<string, string> slotParamMap,
        Dictionary<string, string>? abstractParamMap)
    {
        if(constraintType is ConstructedType ct)
        {
            var baseName = ct.GenericDefinition.DisplayName;
            var args = new List<string>();
            foreach(var arg in ct.TypeArguments)
            {
                if(arg is TypeParameterType tpt)
                {
                    // Check slot params first, then abstract type params.
                    if(slotParamMap.TryGetValue(tpt.Name, out var slotSub))
                    {
                        args.Add(slotSub);
                    }
                    else if(abstractParamMap is not null && abstractParamMap.TryGetValue(tpt.Name, out var abstractSub))
                    {
                        args.Add(abstractSub);
                    }
                    else
                    {
                        args.Add(tpt.Name); // Keep as-is (for CLR interface context)
                    }
                }
                else
                {
                    args.Add(CSharpTypeMap.GetTypeName(arg));
                }
            }

            return $"{baseName}<{string.Join(", ", args)}>";
        }

        // Non-generic constraint type — just use its name.
        return CSharpTypeMap.GetTypeName(constraintType);
    }

    /// <summary>
    /// Emits a parameter list with associated type slot name substitution applied.
    /// </summary>
    private string EmitParameterListWithSubstitution(ParameterListNode? paramList, Dictionary<string, string> substitutions)
    {
        if(paramList is null)
        {
            return "";
        }

        var parts = new List<string>();
        foreach(var child in paramList.Children())
        {
            if(child is ParameterNode param)
            {
                var type = EmitTypeReferenceWithSubstitution(param.Type, substitutions);
                var paramName = param.Name.Lexeme;
                parts.Add($"{type} {paramName}");
            }
        }

        return string.Join(", ", parts);
    }

    /// <summary>
    /// Emits a base type reference with additional type arguments for associated type slots.
    /// When the base type is an interface with associated type slots, the concrete
    /// type members from the implementor are appended as additional type arguments.
    /// For example, <c>Iterable&lt;T&gt;</c> becomes <c>IIterable&lt;T, MyList&lt;T&gt;.MyEnumerator&gt;</c>.
    /// </summary>
    private string EmitBaseTypeRefWithAssociatedTypes(
        BaseTypeRefNode baseTypeRef,
        IScope implementorScope,
        ClassType? implementorClassType,
        string implementorName,
        string implementorTypeParams)
    {
        var baseName = baseTypeRef.Name.Lexeme;

        // Resolve the base type reference to its semantic abstract type.
        var resolvedType = ResolveBaseTypeToAbstract(baseName);
        if(resolvedType is null)
        {
            return baseName + EmitTypeArgumentList(baseTypeRef.TypeArgs);
        }

        // Special case: IEnumerable and IEnumerator map directly to their CLR counterparts
        // (System.Collections.Generic.IEnumerable<T> / IEnumerator<T>) which do NOT have
        // the associated type slot lifted as an extra type parameter. Skip slot-lifting
        // and emit the mapped CLR name.
        if(resolvedType is AbstractType clrMapped
            && clrMapped.ContainingNamespace == "Standard"
            && clrMapped.DisplayName is "IEnumerable" or "IEnumerator")
        {
            var mappedName = CSharpTypeMap.TryGetMappedName($"Standard.{clrMapped.DisplayName}");
            if(mappedName is not null)
            {
                return mappedName + EmitTypeArgumentList(baseTypeRef.TypeArgs);
            }
        }

        // Collect associated type slots from the resolved abstract type.
        var slots = CollectAssociatedTypeSlotsFromType(resolvedType);
        // Only non-parameterized slots are lifted to type parameters on the CLR interface.
        // Parameterized slots are erased to their constraint interface and not appended.
        slots = slots.Where(s => s.TypeParameters.Count == 0).ToList();
        if(slots.Count == 0)
        {
            return baseName + EmitTypeArgumentList(baseTypeRef.TypeArgs);
        }

        // For each slot, find the concrete type member in the implementor's scope.
        var concreteTypeArgs = new List<string>();
        foreach(var slot in slots)
        {
            var concreteType = FindConcreteTypeMemberInScope(slot.DisplayName, slot.TypeParameters.Count, implementorScope, implementorClassType);
            if(concreteType is not null)
            {
                concreteTypeArgs.Add(GetConcreteTypeNameForBaseList(concreteType, implementorName, implementorTypeParams));
            }
        }

        if(concreteTypeArgs.Count == 0)
        {
            return baseName + EmitTypeArgumentList(baseTypeRef.TypeArgs);
        }

        // Build the combined type argument list: existing args + concrete type args.
        var existingArgs = new List<string>();
        if(baseTypeRef.TypeArgs is not null)
        {
            var allArgNodes = new List<TypeArgumentNode> { baseTypeRef.TypeArgs.TypeArgument };
            foreach(var arg in baseTypeRef.TypeArgs.TypeArguments)
            {
                allArgNodes.Add(arg);
            }

            foreach(var argNode in allArgNodes)
            {
                existingArgs.Add(EmitSingleTypeArgument(argNode));
            }
        }

        existingArgs.AddRange(concreteTypeArgs);

        return $"{baseName}<{string.Join(", ", existingArgs)}>";
    }

    /// <summary>
    /// Emits a base list with associated type arguments appended for abstract types
    /// that have associated type slots.
    /// </summary>
    private string EmitBaseListWithAssociatedTypeArgs(
        BaseListNode baseList,
        IScope implementorScope,
        ClassType? implementorClassType,
        string implementorName,
        string implementorTypeParams)
    {
        var parts = new List<string>
        {
            EmitBaseTypeRefWithAssociatedTypes(baseList.First, implementorScope, implementorClassType, implementorName, implementorTypeParams)
        };
        foreach(var rest in baseList.BaseTypeRefs)
        {
            parts.Add(EmitBaseTypeRefWithAssociatedTypes(rest, implementorScope, implementorClassType, implementorName, implementorTypeParams));
        }

        return string.Join(", ", parts);
    }

    /// <summary>
    /// Resolves a base type reference name to its underlying AbstractType.
    /// Prefers a match in the current namespace to avoid cross-namespace collisions.
    /// Returns null if the name does not correspond to an abstract type.
    /// </summary>
    private IType? ResolveBaseTypeToAbstract(string name)
    {
        IType? fallback = null;
        foreach(var symbol in model.AllSymbols)
        {
            if(symbol.Name == name && symbol.DeclaredType is AbstractType)
            {
                var containingNs = symbol.DeclaredType switch
                {
                    AbstractType it => it.ContainingNamespace,
                    _ => null
                };

                // Prefer the match in the current namespace.
                if(containingNs == currentNamespace)
                {
                    return symbol.DeclaredType;
                }

                // Keep the first match as a fallback.
                fallback ??= symbol.DeclaredType;
            }
        }

        return fallback;
    }

    /// <summary>
    /// Emits an abstract type name with associated type slot concrete types appended as
    /// additional type arguments. Used when the abstract type is obtained from the semantic model's
    /// <c>Abstracts</c> list (not from the AST base list).
    /// </summary>
    private static string EmitAbstractWithAssociatedTypeArgs(
        IType abstractType,
        IScope implementorScope,
        ClassType? implementorClassType,
        string implementorName,
        string implementorTypeParams)
    {
        // Get the underlying abstract type definition (unwrap ConstructedType if generic).
        var definition = abstractType is ConstructedType ct ? ct.GenericDefinition : abstractType;

        // Special case: IEnumerable and IEnumerator map directly to their CLR counterparts
        // (System.Collections.Generic.IEnumerable<T> / IEnumerator<T>) which do NOT have
        // the associated type slot lifted as an extra type parameter. Skip slot-lifting
        // for these two CLR-mapped abstract types.
        if(definition is AbstractType clrMapped
            && clrMapped.ContainingNamespace == "Standard"
            && clrMapped.DisplayName is "IEnumerable" or "IEnumerator")
        {
            return CSharpTypeMap.GetTypeName(abstractType);
        }

        // Collect associated type slots from the definition.
        // Only non-parameterized slots are lifted to type parameters.
        var slots = CollectAssociatedTypeSlotsFromType(definition)
            .Where(s => s.TypeParameters.Count == 0).ToList();
        if(slots.Count == 0)
        {
            return CSharpTypeMap.GetTypeName(abstractType);
        }

        // For each slot, find the concrete type member in the implementor's scope.
        var concreteTypeArgs = new List<string>();
        foreach(var slot in slots)
        {
            var concreteType = FindConcreteTypeMemberInScope(slot.DisplayName, slot.TypeParameters.Count, implementorScope, implementorClassType);
            if(concreteType is not null)
            {
                concreteTypeArgs.Add(GetConcreteTypeNameForBaseList(concreteType, implementorName, implementorTypeParams));
            }
        }

        if(concreteTypeArgs.Count == 0)
        {
            return CSharpTypeMap.GetTypeName(abstractType);
        }

        // Build the type name with additional type arguments.
        var baseName = definition.DisplayName;
        var existingArgs = new List<string>();
        if(abstractType is ConstructedType constructed)
        {
            foreach(var arg in constructed.TypeArguments)
            {
                existingArgs.Add(CSharpTypeMap.GetTypeName(arg));
            }
        }

        existingArgs.AddRange(concreteTypeArgs);

        return $"{baseName}<{string.Join(", ", existingArgs)}>";
    }

    /// <summary>
    /// Searches the implementor's member scope (and base type chain for classes)
    /// for a concrete type member matching the given slot name and arity.
    /// Returns the resolved concrete type, or null if not found.
    /// </summary>
    private static IType? FindConcreteTypeMemberInScope(
        string slotName,
        int requiredArity,
        IScope memberScope,
        ClassType? classType)
    {
        // Search the direct member scope.
        var result = SearchScopeForConcreteType(slotName, requiredArity, memberScope);
        if(result is not null)
        {
            return result;
        }

        // For classes, walk the base type chain.
        if(classType is not null)
        {
            var ancestor = classType.BaseType as ClassType;
            while(ancestor is not null)
            {
                result = SearchScopeForConcreteType(slotName, requiredArity, ancestor.MemberScope);
                if(result is not null)
                {
                    return result;
                }

                ancestor = ancestor.BaseType as ClassType;
            }
        }

        return null;
    }

    /// <summary>
    /// Searches a single scope for a concrete type member with the given name and arity.
    /// Returns the resolved concrete type, or null if not found.
    /// </summary>
    private static IType? SearchScopeForConcreteType(string slotName, int requiredArity, IScope scope)
    {
        foreach(var member in scope.Members)
        {
            if(member.Name != slotName)
            {
                continue;
            }

            // Accept type aliases that are NOT associated type slots (i.e., concrete type aliases).
            if(member.Kind == SymbolKind.TypeAlias && member.DeclaredType is not AssociatedTypeSlotType)
            {
                var memberArity = member is Symbol sym ? sym.TypeParameters.Count : 0;
                if(memberArity == requiredArity)
                {
                    // Unwrap type alias to get the underlying type.
                    return member.DeclaredType is TypeAliasType alias ? alias.UnderlyingType : member.DeclaredType;
                }
            }

            // Also accept nested type declarations (class/struct) with matching name and arity.
            if(member.Kind is SymbolKind.Class or SymbolKind.Struct)
            {
                var memberArity = member.DeclaredType switch
                {
                    ClassType ct => ct.TypeParameters.Count,
                    StructType st => st.TypeParameters.Count,
                    _ => 0
                };
                if(memberArity == requiredArity)
                {
                    return member.DeclaredType;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the C# type name for a concrete type member to use in the base list.
    /// For nested types (class/struct declared inside the implementor), emits the
    /// fully-qualified nested type name (e.g., <c>MyList&lt;T&gt;.MyEnumerator</c>).
    /// For type aliases pointing to external types, emits the type name directly.
    /// </summary>
    private static string GetConcreteTypeNameForBaseList(
        IType concreteType,
        string implementorName,
        string implementorTypeParams)
    {
        // If the concrete type is a nested type (its DisplayName matches a nested class/struct),
        // qualify it with the implementor name and type parameters.
        if(concreteType is ClassType nestedClass && nestedClass.ContainingNamespace is null)
        {
            // Nested class — qualify with implementor: ImplementorName<TypeParams>.NestedName
            var nestedName = nestedClass.DisplayName;
            var qualifier = implementorTypeParams.Length > 0
                ? $"{implementorName}{implementorTypeParams}"
                : implementorName;
            return $"{qualifier}.{nestedName}";
        }

        if(concreteType is StructType nestedStruct && nestedStruct.ContainingNamespace is null)
        {
            // Nested struct — qualify with implementor: ImplementorName<TypeParams>.NestedName
            var nestedName = nestedStruct.DisplayName;
            var qualifier = implementorTypeParams.Length > 0
                ? $"{implementorName}{implementorTypeParams}"
                : implementorName;
            return $"{qualifier}.{nestedName}";
        }

        // For non-nested types (e.g., type alias pointing to an external type),
        // just use the standard type name.
        return CSharpTypeMap.GetTypeName(concreteType);
    }

    /// <summary>
    /// Flushes any pending array initializer construction statements (allocation + for-loop)
    /// that were queued during expression emission. Called by statement-level emitters before
    /// their own output line so the lifted locals precede the statement that uses them.
    /// </summary>
    private void FlushPendingArrayInitStatements()
    {
        if(pendingArrayInitStatements.Count == 0)
        {
            return;
        }

        foreach(var stmt in pendingArrayInitStatements)
        {
            AppendLine(stmt);
        }

        pendingArrayInitStatements.Clear();
    }

    private void AppendLine(string text = "")
    {
        if(string.IsNullOrEmpty(text))
        {
            _ = sb.AppendLine();
        }
        else
        {
            for(var i = 0; i < indentLevel; i++)
            {
                _ = sb.Append(IndentUnit);
            }

            _ = sb.AppendLine(text);
        }
    }

    // ─── Implicit Override Detection ─────────────────────────────────────────────

    /// <summary>
    /// Searches the CLR base type's member scope for a virtual/abstract method with the
    /// same name as the given Mist method. Only matches when the base type chain contains
    /// a CLR-origin (Backend) type. Returns the base method symbol if found, null otherwise.
    /// </summary>
    private Symbol? FindBaseOverrideSlot(string methodName)
    {
        // Walk the inheritance chain looking for a CLR base with a matching virtual/abstract method.
        var baseType = currentClassType?.BaseType;

        while(baseType is not null)
        {
            var effectiveClassType = baseType switch
            {
                ClassType ct => ct,
                ConstructedType constructed when constructed.GenericDefinition is ClassType ctDef => ctDef,
                _ => null
            };

            if(effectiveClassType is null)
            {
                break;
            }

            var isClrType = effectiveClassType.MemberScope.Members
                .Any(m => m.Origin == SymbolOrigin.Backend);

            if(!isClrType)
            {
                baseType = effectiveClassType.BaseType;
                continue;
            }

            // Search the base type's member scope for a matching virtual/abstract method.
            foreach(var member in effectiveClassType.MemberScope.Members)
            {
                if(member is Symbol memberSym
                    && memberSym.Kind == SymbolKind.Method
                    && memberSym.Name == methodName
                    && memberSym.IsVirtualOrAbstract
                    && !memberSym.IsStatic)
                {
                    return memberSym;
                }
            }

            // Continue up the chain.
            baseType = effectiveClassType.BaseType;
        }

        return null;
    }
}