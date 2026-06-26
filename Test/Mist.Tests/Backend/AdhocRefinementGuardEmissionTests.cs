// Feature: adhoc-refinement-constraints — Unit tests for compile-time enforcement
// **Validates: Requirements 6, 7, 12**

using System.Collections.Immutable;
using System.Text;
using Mist.Analysis.Builtin;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Semantics.Model;

namespace Mist.Tests.Backend;

/// <summary>
/// Unit tests verifying that methods, constructors, and indexers with adhoc
/// refinement constraints (where clauses on parameters) enforce constraints
/// entirely at compile time — no runtime guards are emitted.
///
/// Non-constant, non-provable arguments are rejected at compile time with
/// the same behavior as named refinement type parameters.
///
/// **Validates: Requirements 6, 7, 12**
/// </summary>
public sealed class AdhocRefinementGuardEmissionTests
{
    private static readonly MistLexer Lexer = new();
    private static readonly MistParser Parser = new();
    private static readonly SemanticModelBuilder ModelBuilder = new();
    private static readonly TypeCheckerAnalyzer TypeChecker = new();

    private static string CompileAndGetEmittedCSharp(string mistSource)
    {
        var sourceText = new SourceText("test.mist", mistSource);
        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));

        var lexBag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexBag);
        Assert.False(lexBag.HasErrors, $"Lex errors: {FormatDiagnostics(lexBag)}");

        var parseBag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseBag);
        Assert.False(parseBag.HasErrors, $"Parse errors: {FormatDiagnostics(parseBag)}");
        Assert.NotNull(parseResult.Root);

        var semBag = new DiagnosticBag();
        var model = ModelBuilder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root),
            ambientTypeResolver: null,
            namespaceRoot: "TestProgram",
            projectFolder: "/test",
            semBag);
        Assert.False(semBag.HasErrors, $"Semantic errors: {FormatDiagnostics(semBag)}");

        var tcBag = new DiagnosticBag();
        TypeChecker.Analyze(model, tcBag);
        var tcErrors = tcBag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.Empty(tcErrors);

        var emitBag = new DiagnosticBag();
        var files = CSharpEmitter.EmitToMemory(model, "TestProgram", emitBag);
        Assert.False(emitBag.HasErrors, $"Emit errors: {FormatDiagnostics(emitBag)}");

        var sb = new StringBuilder();
        foreach(var (_, content) in files)
        {
            _ = sb.AppendLine(content);
        }

        return sb.ToString();
    }

    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string mistSource)
    {
        var sourceText = new SourceText("test.mist", mistSource);
        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));

        var lexBag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexBag);
        if(lexBag.HasErrors)
        {
            return lexBag.ToImmutableArray();
        }

        var parseBag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseBag);
        if(parseBag.HasErrors)
        {
            return parseBag.ToImmutableArray();
        }

        var semBag = new DiagnosticBag();
        var model = ModelBuilder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root!),
            ambientTypeResolver: null,
            namespaceRoot: "TestProgram",
            projectFolder: "/test",
            semBag);
        if(semBag.HasErrors)
        {
            return semBag.ToImmutableArray();
        }

        var tcBag = new DiagnosticBag();
        TypeChecker.Analyze(model, tcBag);
        return tcBag.ToImmutableArray();
    }

    private static string FormatDiagnostics(DiagnosticBag bag)
    {
        return string.Join("\n", bag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"  {d.Id.Value}: {d.Message}"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 6.1: Compile-time constant that satisfies constraint — accepted
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MethodParameter_ConstantSatisfiesConstraint_Accepted()
    {
        const string source = """
            class Program {
                public static void Main(string[] args) {
                    Program.Process(5);
                    Program.Process(10);
                }
                public static void Process(int x) where x >= 0 {
                    int y = x;
                }
            }
            """;

        var output = CompileAndGetEmittedCSharp(source);

        // No runtime guard emitted — compile-time enforcement only
        Assert.DoesNotContain("if (!(x >= 0))", output);
        Assert.DoesNotContain("throw new ArgumentException", output);
        Assert.Contains("void Process(int x)", output);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 6.2: Compile-time constant that violates constraint — error
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MethodParameter_ConstantViolatesConstraint_EmitsError()
    {
        const string source = """
            class Program {
                public static void Main(string[] args) {
                    Program.Process(200);
                }
                public static void Process(int x) where x >= 0 and <= 100 {
                    int y = x;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

        _ = Assert.Single(errors);
        Assert.Equal("MIST_REFINEMENT_0005", errors[0].Id.Value);
        Assert.Contains("200", errors[0].Message);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 6.5: Non-constant argument without refinement type — error
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MethodParameter_NonConstantUnrefined_EmitsError()
    {
        const string source = """
            class Program {
                public static void Main(string[] args) {
                    int n = 5;
                    Program.Process(n);
                }
                public static void Process(int x) where x >= 0 {
                    int y = x;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

        _ = Assert.Single(errors);
        Assert.Equal("MIST_REFINEMENT_0005", errors[0].Id.Value);
        Assert.Contains("pattern matching", errors[0].Message);
    }

    [Fact]
    public void ConstructorParameter_NonConstantUnrefined_EmitsError()
    {
        const string source = """
            class Container {
                int _size;
                public Container(int size) where size > 0 {
                    _size = size;
                }
            }
            class Program {
                public static void Main(string[] args) {
                    int s = 10;
                    Container c = new Container(s);
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

        _ = Assert.Single(errors);
        Assert.Equal("MIST_REFINEMENT_0005", errors[0].Id.Value);
        Assert.Contains("size", errors[0].Message);
    }

    [Fact]
    public void IndexerParameter_NonConstantUnrefined_EmitsError()
    {
        const string source = """
            class MyList {
                int[10] _items = new int[10];
                public MyList() { }
                public int this[int index] where index >= 0 and < 10 {
                    get { return _items[index]; }
                    set { _items[index] = value; }
                }
            }
            class Program {
                public static void Main(string[] args) {
                    MyList list = new MyList();
                    int i = 0;
                    int v = list[i];
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

        _ = Assert.Single(errors);
        Assert.Equal("MIST_REFINEMENT_0005", errors[0].Id.Value);
        Assert.Contains("index", errors[0].Message);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 6.3: Named refinement type that implies constraint — accepted
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MethodParameter_RefinementTypeImpliesConstraint_Accepted()
    {
        const string source = """
            type NonNegative = int >= 0;
            class Program {
                public static void Main(string[] args) {
                    NonNegative n = 5;
                    Program.Process(n);
                }
                public static void Process(int x) where x >= 0 {
                    int y = x;
                }
            }
            """;

        var output = CompileAndGetEmittedCSharp(source);

        // Accepted — no guard emitted
        Assert.DoesNotContain("if (!(x >= 0))", output);
        Assert.Contains("void Process(int x)", output);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 7: No runtime guards for adhoc refinement constraints
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MethodParameter_NoRuntimeGuardEmitted()
    {
        const string source = """
            class Program {
                public static void Main(string[] args) {
                    Program.Process(42);
                }
                public static void Process(int x) where x >= 0 {
                    int y = x;
                }
            }
            """;

        var output = CompileAndGetEmittedCSharp(source);

        // No runtime guard — enforcement is compile-time only
        Assert.DoesNotContain("if (!(x >= 0))", output);
        Assert.DoesNotContain("throw new ArgumentException", output);
    }

    [Fact]
    public void CompoundPredicate_NoRuntimeGuardEmitted()
    {
        const string source = """
            class Program {
                public static void Main(string[] args) {
                    Program.Clamp(50);
                }
                public static void Clamp(int x) where x >= 0 and <= 100 {
                    int y = x;
                }
            }
            """;

        var output = CompileAndGetEmittedCSharp(source);

        Assert.DoesNotContain("if (!", output);
        Assert.DoesNotContain("throw new ArgumentException", output);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 12.2: Dependent argument refinements — no runtime code
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void DependentArgumentRefinement_NoRuntimeCode()
    {
        const string source = """
            class FixedBuffer<Size:int> where Size >= 0 {
                public int GetSize() {
                    return Size;
                }
            }
            class Program {
                public static void Main(string[] args) {
                    FixedBuffer<5> buf = new FixedBuffer<5>();
                    int s = buf.GetSize();
                }
            }
            """;

        var output = CompileAndGetEmittedCSharp(source);

        Assert.DoesNotContain("if (!(Size >= 0))", output);
        Assert.DoesNotContain("nameof(Size)", output);
    }

    [Fact]
    public void MultipleDependentArgumentRefinements_NoRuntimeCode()
    {
        const string source = """
            class Matrix<Rows:int, Cols:int> where Rows >= 1 where Cols >= 1 {
                public int GetRows() {
                    return Rows;
                }
                public int GetCols() {
                    return Cols;
                }
            }
            class Program {
                public static void Main(string[] args) {
                    Matrix<3, 4> m = new Matrix<3, 4>();
                    int r = m.GetRows();
                }
            }
            """;

        var output = CompileAndGetEmittedCSharp(source);

        Assert.DoesNotContain("if (!(Rows >= 1))", output);
        Assert.DoesNotContain("if (!(Cols >= 1))", output);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 12.3: Emitted C# compiles without errors (valid programs)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void FullPipeline_AdhocRefinement_EmitsWithoutErrors()
    {
        const string source = """
            class Program {
                public static void Main(string[] args) {
                    Program.Validate(42);
                }
                public static void Validate(int n) where n >= 0 and <= 1000 {
                    int x = n;
                }
            }
            """;

        var output = CompileAndGetEmittedCSharp(source);
        Assert.NotEmpty(output);
    }

    [Fact]
    public void MixedRefinementTypes_EmitsWithoutErrors()
    {
        const string source = """
            type Positive = int >= 1;
            class Program {
                public static void Main(string[] args) {
                    Program.Process(5, 10);
                }
                public static void Process(Positive a, int b) where b >= 0 {
                    int x = a;
                    int y = b;
                }
            }
            """;

        var output = CompileAndGetEmittedCSharp(source);
        Assert.NotEmpty(output);
        // Named refinement type guard is still emitted (for external callers)
        Assert.Contains("if (!(a >= 1))", output);
        // Adhoc refinement does NOT emit a guard
        Assert.DoesNotContain("if (!(b >= 0))", output);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 12.4: Generic constraints still emitted on signature
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void GenericConstraintAndParameterRefinement_GenericConstraintEmitted()
    {
        const string source = """
            abstract IItem { }
            class Program {
                public static void Main(string[] args) {
                    Program.DoWork(5);
                }
                public static void DoWork(int count) where count > 0 {
                    int x = count;
                }
                public static void Show<T>(T item) where T : IItem {
                    int y = 0;
                }
            }
            """;

        var output = CompileAndGetEmittedCSharp(source);

        // Generic constraint should appear as C# where clause on the method signature
        Assert.Contains("where T : IItem", output);
        // No runtime guard for adhoc parameter refinement
        Assert.DoesNotContain("if (!(count > 0))", output);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Parameter type emission: base type in signature
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ParameterWithAdhocRefinement_EmitsBaseTypeInSignature()
    {
        const string source = """
            class Program {
                public static void Main(string[] args) {
                    Program.Process(5);
                }
                public static void Process(int x) where x >= 0 {
                    int y = x;
                }
            }
            """;

        var output = CompileAndGetEmittedCSharp(source);

        // The parameter should be emitted as the base type `int`
        Assert.Contains("int x", output);
    }
}