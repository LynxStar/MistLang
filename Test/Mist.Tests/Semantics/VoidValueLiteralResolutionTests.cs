// Task 5: Unit tests for void value literal resolution
// Validates: Requirements 1.2, 1.3, 1.4, 1.5, 2.2, 3.2, 3.3, 4.2, 5.1, 5.2

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Semantics;

/// <summary>
/// Unit tests for void value literal resolution in the semantic model.
/// Verifies that the `void` keyword in expression position resolves correctly
/// based on target type context (void → VoidType, Type → Standard.Type, var → Standard.Type).
/// </summary>
public sealed class VoidValueLiteralResolutionTests : IDisposable
{
    private readonly string _scratchDir;

    public VoidValueLiteralResolutionTests()
    {
        _scratchDir = Path.Combine(
            Path.GetTempPath(),
            "mist-void-val-lit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_scratchDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(_scratchDir))
        {
            try { Directory.Delete(_scratchDir, recursive: true); } catch { /* best effort */ }
        }
        GC.SuppressFinalize(this);
    }

    // ─── Infrastructure ──────────────────────────────────────────────────

    /// <summary>
    /// Wraps <see cref="SemanticModelBuilder"/> to capture the semantic model
    /// after the driver builds it, so we can inspect expression types.
    /// </summary>
    private sealed class CapturingSemanticModelBuilder : ISemanticModelBuilder
    {
        private readonly SemanticModelBuilder _inner = new();

        public ISemanticModel? CapturedModel { get; private set; }

        public ISemanticModel Build(
            MistCompilation compilation,
            ImmutableArray<Storm.Runtime.SyntaxNode> asts,
            IAmbientTypeResolver? ambientTypeResolver,
            string namespaceRoot,
            string projectFolder,
            DiagnosticBag diagnostics,
            ImmutableHashSet<string>? directReferencePaths = null)
        {
            var model = _inner.Build(compilation, asts, ambientTypeResolver, namespaceRoot, projectFolder, diagnostics, directReferencePaths);
            CapturedModel = model;
            return model;
        }
    }

    private static CompilerDriver CreateDriver()
    {
        return new CompilerDriver(
            new Mist.Lexing.MistLexer(),
            new Mist.Parsing.MistParser(),
            new SemanticModelBuilder(),
            ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
            ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));
    }

    private static CompilerDriver CreateDriverWithCapture(CapturingSemanticModelBuilder capture)
    {
        return new CompilerDriver(
            new Mist.Lexing.MistLexer(),
            new Mist.Parsing.MistParser(),
            capture,
            ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
            ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));
    }

    private CompilerOptions MakeOptions(string filePath)
    {
        return new CompilerOptions(
            ProjectFolder: _scratchDir,
            SourcePaths: ImmutableArray.Create(filePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: _scratchDir,
            BinDirectory: Path.Combine(_scratchDir, "bin"),
            ProjectName: "TestProgram",
            NamespaceRoot: "TestProgram",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            EmitToDisk: false);
    }

    /// <summary>
    /// Compiles source in-process and returns the result.
    /// </summary>
    private CompilerResult CompileSource(string source)
    {
        var filePath = Path.Combine(_scratchDir, "Program.mist");
        File.WriteAllText(filePath, source);

        var driver = CreateDriver();
        return driver.Compile(MakeOptions(filePath));
    }

    /// <summary>
    /// Compiles source in-process with a capturing builder and returns both
    /// the result and the captured semantic model.
    /// </summary>
    private (CompilerResult Result, ISemanticModel? Model) CompileAndCapture(string source)
    {
        var filePath = Path.Combine(_scratchDir, "Program.mist");
        File.WriteAllText(filePath, source);

        var capture = new CapturingSemanticModelBuilder();
        var driver = CreateDriverWithCapture(capture);
        var result = driver.Compile(MakeOptions(filePath));
        return (result, capture.CapturedModel);
    }

    /// <summary>
    /// Finds the user AST (last one in the model, after stdlib ASTs).
    /// </summary>
    private static Storm.Runtime.SyntaxNode GetUserAst(ISemanticModel model)
    {
        // The user AST is the last one (stdlib ASTs come first)
        return model.Asts[^1];
    }

    /// <summary>
    /// Recursively finds all TypeLiteralExpressionPrimaryNode instances in the AST.
    /// </summary>
    private static List<TypeLiteralExpressionPrimaryNode> FindTypeLiterals(Storm.Runtime.SyntaxNode node)
    {
        var results = new List<TypeLiteralExpressionPrimaryNode>();
        CollectTypeLiterals(node, results);
        return results;
    }

    private static void CollectTypeLiterals(Storm.Runtime.SyntaxNode? node, List<TypeLiteralExpressionPrimaryNode> results)
    {
        if(node is null) return;

        if(node is TypeLiteralExpressionPrimaryNode typeLiteral)
            results.Add(typeLiteral);

        try
        {
            foreach(var child in node.Children())
                CollectTypeLiterals(child, results);
        }
        catch(NullReferenceException)
        {
            // Some AST nodes may have null children that Children() doesn't guard against.
        }
    }

    /// <summary>
    /// Recursively finds all MemberAccessSuffixPostfixSuffixNode instances in the AST.
    /// </summary>
    private static List<MemberAccessSuffixPostfixSuffixNode> FindMemberAccessSuffixes(Storm.Runtime.SyntaxNode node)
    {
        var results = new List<MemberAccessSuffixPostfixSuffixNode>();
        CollectMemberAccessSuffixes(node, results);
        return results;
    }

    private static void CollectMemberAccessSuffixes(Storm.Runtime.SyntaxNode? node, List<MemberAccessSuffixPostfixSuffixNode> results)
    {
        if(node is null) return;

        if(node is MemberAccessSuffixPostfixSuffixNode suffix)
            results.Add(suffix);

        try
        {
            foreach(var child in node.Children())
                CollectMemberAccessSuffixes(child, results);
        }
        catch(NullReferenceException)
        {
            // Some AST nodes may have null children that Children() doesn't guard against.
        }
    }

    private static string FormatDiagnostics(ImmutableArray<Diagnostic> diagnostics)
    {
        return string.Join("\n", diagnostics.Select(d => $"  [{d.Severity}] {d.Id.Value}: {d.Message}"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 1: void x = void; compiles, expression type is VoidType
    //
    // Validates: Requirement 1.2
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void VoidVariable_VoidLiteral_ExpressionTypeIsVoidType()
    {
        var source = """
            class Program {
                static void Main() {
                    void x = void;
                }
            }
            """;

        var (result, model) = CompileAndCapture(source);

        // Should compile without type checker errors
        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.True(errors.Count == 0,
            $"Expected no errors but got:\n{FormatDiagnostics(result.Diagnostics)}");

        // Find the void type literal in the AST and check its expression type
        Assert.NotNull(model);
        var userAst = model!.Asts[^1]; // User AST is last (stdlib ASTs come first)
        var typeLiterals = FindTypeLiterals(userAst);
        var voidLiterals = typeLiterals
            .Where(tl => tl.TypeRef is { SingleTypeRefs.Length: 0, First: VoidSingleTypeRefSingleTypeRefNode })
            .ToList();

        Assert.NotEmpty(voidLiterals);

        // The void literal used as initializer should resolve as VoidType
        var initializerLiteral = voidLiterals.Last(); // The one in expression position
        var exprType = model.GetTypeOf(initializerLiteral);
        Assert.NotNull(exprType);
        Assert.True(exprType!.IsVoid(),
            $"Expected VoidType but got: {exprType.DisplayName}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 2: Type t = void; compiles, expression type is Standard.Type
    //
    // Validates: Requirement 2.2
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TypeVariable_VoidLiteral_ExpressionTypeIsStandardType()
    {
        var source = """
            using Standard;

            class Program {
                static void Main() {
                    Type t = void;
                }
            }
            """;

        var (result, model) = CompileAndCapture(source);

        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.True(errors.Count == 0,
            $"Expected no errors but got:\n{FormatDiagnostics(result.Diagnostics)}");

        Assert.NotNull(model);
        var userAst = model!.Asts[^1]; // User AST is last (stdlib ASTs come first)
        var typeLiterals = FindTypeLiterals(userAst);
        var voidLiterals = typeLiterals
            .Where(tl => tl.TypeRef is { SingleTypeRefs.Length: 0, First: VoidSingleTypeRefSingleTypeRefNode })
            .ToList();

        Assert.NotEmpty(voidLiterals);

        // The void literal with target type Type should resolve as Standard.Type
        var initializerLiteral = voidLiterals.Last();
        var exprType = model.GetTypeOf(initializerLiteral);
        Assert.NotNull(exprType);
        Assert.True(exprType!.IsTypeType(),
            $"Expected Standard.Type but got: {exprType.DisplayName} (Kind={exprType.Kind})");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 3: var x = void; infers Standard.Type
    //
    // Validates: Requirement 3.2
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void VarVariable_VoidLiteral_InfersStandardType()
    {
        var source = """
            class Program {
                static void Main() {
                    var x = void;
                }
            }
            """;

        var (result, model) = CompileAndCapture(source);

        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.True(errors.Count == 0,
            $"Expected no errors but got:\n{FormatDiagnostics(result.Diagnostics)}");

        Assert.NotNull(model);

        // The inferred variable type should be Standard.Type
        var varSymbol = model!.AllSymbols
            .FirstOrDefault(s => s.Name == "x" && s.Kind == SymbolKind.LocalVariable);
        Assert.NotNull(varSymbol);
        Assert.NotNull(varSymbol!.DeclaredType);
        Assert.True(varSymbol.DeclaredType!.IsTypeType(),
            $"Expected Standard.Type but got: {varSymbol.DeclaredType.DisplayName} (Kind={varSymbol.DeclaredType.Kind})");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 4: var x = void; void y = x; produces type mismatch diagnostic
    //
    // Validates: Requirement 3.3
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void VarVoidAssignedToVoidVariable_ProducesTypeMismatch()
    {
        var source = """
            class Program {
                static void Main() {
                    var x = void;
                    void y = x;
                }
            }
            """;

        var result = CompileSource(source);

        // Should produce a type mismatch diagnostic (MIST_TC_*)
        var typeErrors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Where(d => d.Id.Value.StartsWith("MIST_TC", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(typeErrors);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 5: return void; in void method accepted without error
    //
    // Validates: Requirement 1.4
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ReturnVoid_InVoidMethod_AcceptedWithoutError()
    {
        var source = """
            class Program {
                static void Main() { }

                void DoNothing() {
                    return void;
                }
            }
            """;

        var result = CompileSource(source);

        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.True(errors.Count == 0,
            $"Expected no errors but got:\n{FormatDiagnostics(result.Diagnostics)}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 6: void as argument to void-typed parameter accepted
    //
    // Validates: Requirement 1.3
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void VoidArgument_ToVoidParameter_Accepted()
    {
        var source = """
            class Program {
                static void Main() {
                    TakeVoid(void);
                }

                static void TakeVoid(void v) { }
            }
            """;

        var result = CompileSource(source);

        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.True(errors.Count == 0,
            $"Expected no errors but got:\n{FormatDiagnostics(result.Diagnostics)}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 7: void in generic context new Result<void>(void, true) accepted
    //
    // Validates: Requirement 1.5
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void VoidInGenericContext_Accepted()
    {
        var source = """
            class Result<T> {
                T value;
                bool success;

                Result(T value, bool success) {
                    this.value = value;
                    this.success = success;
                }
            }

            class Program {
                static void Main() {
                    var r = new Result<void>(void, true);
                }
            }
            """;

        var result = CompileSource(source);

        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.True(errors.Count == 0,
            $"Expected no errors but got:\n{FormatDiagnostics(result.Diagnostics)}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 8: void v = void; Type t = v; accepted via implicit conversion
    //
    // Validates: Requirement 4.2
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void VoidToType_ImplicitConversion_Accepted()
    {
        var source = """
            using Standard;

            class Program {
                static void Main() {
                    void v = void;
                    Type t = v;
                }
            }
            """;

        var result = CompileSource(source);

        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.True(errors.Count == 0,
            $"Expected no errors but got:\n{FormatDiagnostics(result.Diagnostics)}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 9: Void.Void still resolves as VoidType
    //
    // Validates: Requirement 5.1
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void VoidDotVoid_StillResolvesAsVoidType()
    {
        var source = """
            class Program {
                static void Main() {
                    void x = Void.Void;
                }
            }
            """;

        var (result, model) = CompileAndCapture(source);

        // Should compile (possibly with warnings, but no errors)
        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.True(errors.Count == 0,
            $"Expected no errors but got:\n{FormatDiagnostics(result.Diagnostics)}");

        Assert.NotNull(model);

        // Find the Void.Void member access suffix and verify it resolves as VoidType
        var userAst = model!.Asts[^1]; // User AST is last (stdlib ASTs come first)
        var suffixes = FindMemberAccessSuffixes(userAst);
        var voidSuffix = suffixes
            .FirstOrDefault(s => s.Name.Lexeme == "Void");
        Assert.NotNull(voidSuffix);

        var exprType = model.GetTypeOf(voidSuffix!);
        Assert.NotNull(exprType);
        Assert.True(exprType!.IsVoid(),
            $"Expected VoidType but got: {exprType.DisplayName}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 10: Void.Void emits MIST_SM_0023 warning
    //
    // Validates: Requirement 5.2
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void VoidDotVoid_EmitsDeprecationWarning()
    {
        var source = """
            class Program {
                static void Main() {
                    void x = Void.Void;
                }
            }
            """;

        var result = CompileSource(source);

        // Should compile without errors
        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.True(errors.Count == 0,
            $"Expected no errors but got:\n{FormatDiagnostics(result.Diagnostics)}");

        // Should emit MIST_SM_0023 warning
        var deprecationWarnings = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Warning)
            .Where(d => d.Id.Value == "MIST_SM_0023")
            .ToList();

        Assert.True(deprecationWarnings.Count == 1,
            $"Expected exactly 1 MIST_SM_0023 warning but got {deprecationWarnings.Count}.\n" +
            $"All diagnostics:\n{FormatDiagnostics(result.Diagnostics)}");
    }
}