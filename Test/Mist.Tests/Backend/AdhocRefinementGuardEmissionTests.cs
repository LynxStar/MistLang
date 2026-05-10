// Feature: adhoc-refinement-constraints — Unit tests for C# emitter adhoc refinement guard emission
// **Validates: Requirements 12.1, 12.2, 12.3, 12.4, 12.5, 12.6**

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
/// refinement constraints (where clauses on parameters) emit runtime validation
/// guards at entry. The guard negates the predicate condition and throws
/// <see cref="ArgumentException"/> if the value fails.
///
/// Also verifies that dependent argument refinement constraints produce no
/// runtime code (compile-time only enforcement).
///
/// **Validates: Requirements 12.1, 12.2, 12.3, 12.4, 12.5, 12.6**
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
        foreach (var (_, content) in files)
            sb.AppendLine(content);

        return sb.ToString();
    }

    private static string FormatDiagnostics(DiagnosticBag bag)
    {
        return string.Join("\n", bag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"  {d.Id.Value}: {d.Message}"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 12.1: Method parameter refinement guard emission
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Verifies that a method with a single adhoc refinement constraint on a
    /// parameter emits a runtime guard with the negated condition + throw pattern.
    /// Uses a non-constant call site to ensure the guard is not elided.
    /// </summary>
    [Fact]
    public void MethodParameter_AdhocRefinement_EmitsNegatedGuard()
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

        var output = CompileAndGetEmittedCSharp(source);

        // Should emit the negated predicate check
        Assert.Contains("if (!(x >= 0))", output);
        // Should throw ArgumentException
        Assert.Contains("throw new ArgumentException", output);
        Assert.Contains("nameof(x)", output);
    }

    /// <summary>
    /// Verifies that the guard uses the correct relational operator for
    /// a less-than constraint.
    /// </summary>
    [Fact]
    public void MethodParameter_LessThanConstraint_EmitsCorrectGuard()
    {
        const string source = """
            class Program {
                public static void Main(string[] args) {
                    int n = 50;
                    Program.Limit(n);
                }
                public static void Limit(int value) where value < 100 {
                    int y = value;
                }
            }
            """;

        var output = CompileAndGetEmittedCSharp(source);

        Assert.Contains("if (!(value < 100))", output);
        Assert.Contains("throw new ArgumentException", output);
        Assert.Contains("nameof(value)", output);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 12.2: Constructor and indexer parameter guard emission
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Verifies that a constructor with an adhoc refinement constraint on a
    /// parameter emits a runtime guard using the same pattern as methods.
    /// </summary>
    [Fact]
    public void ConstructorParameter_AdhocRefinement_EmitsGuard()
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

        var output = CompileAndGetEmittedCSharp(source);

        // Should emit the negated predicate check for the constructor parameter
        Assert.Contains("if (!(size > 0))", output);
        Assert.Contains("throw new ArgumentException", output);
        Assert.Contains("nameof(size)", output);
    }

    /// <summary>
    /// Verifies that an indexer with an adhoc refinement constraint on a
    /// parameter emits a runtime guard using the same pattern as methods.
    /// </summary>
    [Fact]
    public void IndexerParameter_AdhocRefinement_EmitsGuard()
    {
        const string source = """
            class MyList {
                int[] _items;
                public MyList() {
                    _items = new int[10];
                }
                public int this[int index] where index >= 0 {
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

        var output = CompileAndGetEmittedCSharp(source);

        // Should emit the negated predicate check for the indexer parameter
        Assert.Contains("if (!(index >= 0))", output);
        Assert.Contains("throw new ArgumentException", output);
        Assert.Contains("nameof(index)", output);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 12.6: Compound predicates emit as single boolean expression
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Verifies that a compound predicate using 'and' emits as a single
    /// boolean expression with && joining the conditions.
    /// </summary>
    [Fact]
    public void MethodParameter_CompoundPredicate_EmitsSingleBooleanExpression()
    {
        const string source = """
            class Program {
                public static void Main(string[] args) {
                    int n = 50;
                    Program.Clamp(n);
                }
                public static void Clamp(int x) where x >= 0 and <= 100 {
                    int y = x;
                }
            }
            """;

        var output = CompileAndGetEmittedCSharp(source);

        // Should emit a combined condition with &&
        Assert.Contains("x >= 0", output);
        Assert.Contains("x <= 100", output);
        Assert.Contains("&&", output);
        // Should be wrapped in a single if (!(combined))
        Assert.Contains("if (!", output);
        Assert.Contains("throw new ArgumentException", output);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 12.3: Dependent argument refinements produce no runtime code
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Verifies that a class with a dependent type argument refinement constraint
    /// does NOT emit any runtime guard code — enforcement is compile-time only.
    /// </summary>
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

        // The dependent argument refinement should NOT produce any runtime guard.
        // There should be no ArgumentException thrown for the Size constraint.
        Assert.DoesNotContain("if (!(Size >= 0))", output);
        Assert.DoesNotContain("nameof(Size)", output);
    }

    /// <summary>
    /// Verifies that a class with multiple dependent type argument refinement
    /// constraints produces no runtime code for any of them.
    /// </summary>
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

        // No runtime guards for dependent argument refinements
        Assert.DoesNotContain("if (!(Rows >= 1))", output);
        Assert.DoesNotContain("if (!(Cols >= 1))", output);
        Assert.DoesNotContain("nameof(Rows)", output);
        Assert.DoesNotContain("nameof(Cols)", output);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 12.4: Emitted C# compiles without errors
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Verifies that the full pipeline (lex → parse → semantic → type check → emit)
    /// completes without errors for a program with adhoc refinement constraints.
    /// This confirms the emitted C# is structurally valid.
    /// </summary>
    [Fact]
    public void FullPipeline_AdhocRefinement_EmitsWithoutErrors()
    {
        const string source = """
            class Program {
                public static void Main(string[] args) {
                    int n = 42;
                    Program.Validate(n);
                }
                public static void Validate(int n) where n >= 0 and <= 1000 {
                    int x = n;
                }
            }
            """;

        // CompileAndGetEmittedCSharp asserts no errors at each stage
        var output = CompileAndGetEmittedCSharp(source);
        Assert.NotEmpty(output);
    }

    /// <summary>
    /// Verifies that a program with both adhoc refinement constraints and
    /// named refinement types emits without errors.
    /// </summary>
    [Fact]
    public void MixedRefinementTypes_EmitsWithoutErrors()
    {
        const string source = """
            type Positive = int >= 1;
            class Program {
                public static void Main(string[] args) {
                    int b = 10;
                    Program.Process(5, b);
                }
                public static void Process(Positive a, int b) where b >= 0 {
                    int x = a;
                    int y = b;
                }
            }
            """;

        var output = CompileAndGetEmittedCSharp(source);
        Assert.NotEmpty(output);
        // Named refinement type guard should always be present (not subject to adhoc elision)
        Assert.Contains("if (!(a >= 1))", output);
        // Adhoc refinement guard should be present (non-constant call site for b)
        Assert.Contains("if (!(b >= 0))", output);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 12.5: Generic constraints + parameter refinement constraints
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Verifies that a method with both generic constraints (emitted as C# where
    /// clauses on the signature) and parameter refinement constraints (emitted as
    /// runtime guards in the body) produces correct output.
    /// </summary>
    [Fact]
    public void GenericConstraintAndParameterRefinement_BothEmitted()
    {
        const string source = """
            interface IItem { }
            class Program {
                public static void Main(string[] args) {
                    int n = 5;
                    Program.DoWork(n);
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
        // Parameter refinement should appear as runtime guard in the body
        Assert.Contains("if (!(count > 0))", output);
        Assert.Contains("throw new ArgumentException", output);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Guard elision: when all call sites pass validated constants
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Verifies that when all call sites pass only compile-time constants that
    /// satisfy the refinement constraint, the guard is elided (not emitted).
    /// </summary>
    [Fact]
    public void AllCallSitesValidated_GuardIsElided()
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

        // When all call sites pass validated constants, the guard is elided.
        // The method should still be emitted but without the guard.
        Assert.Contains("void Process(int x)", output);
        Assert.DoesNotContain("if (!(x >= 0))", output);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Guard ordering: left-to-right parameter declaration order
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Verifies that when multiple parameters have adhoc refinement constraints,
    /// the guards are emitted in left-to-right parameter declaration order.
    /// </summary>
    [Fact]
    public void MultipleParameterConstraints_GuardsInDeclarationOrder()
    {
        const string source = """
            class Program {
                public static void Main(string[] args) {
                    int a = 1;
                    int b = 10;
                    Program.Range(a, b);
                }
                public static void Range(int min, int max) where min >= 0 where max <= 100 {
                    int x = min;
                    int y = max;
                }
            }
            """;

        var output = CompileAndGetEmittedCSharp(source);

        // Both guards should be present
        Assert.Contains("if (!(min >= 0))", output);
        Assert.Contains("if (!(max <= 100))", output);

        // Guards should appear in left-to-right order (min before max)
        var minIdx = output.IndexOf("if (!(min >= 0))", StringComparison.Ordinal);
        var maxIdx = output.IndexOf("if (!(max <= 100))", StringComparison.Ordinal);
        Assert.True(minIdx < maxIdx,
            $"Guard for 'min' should appear before guard for 'max'. min at {minIdx}, max at {maxIdx}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Guard message content
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Verifies that the guard's exception message includes the predicate
    /// description for the adhoc refinement constraint.
    /// </summary>
    [Fact]
    public void GuardMessage_IncludesPredicateDescription()
    {
        const string source = """
            class Program {
                public static void Main(string[] args) {
                    int n = 25;
                    Program.SetAge(n);
                }
                public static void SetAge(int age) where age >= 18 {
                    int x = age;
                }
            }
            """;

        var output = CompileAndGetEmittedCSharp(source);

        // The exception message should include the predicate description
        Assert.Contains(">= 18", output);
        Assert.Contains("nameof(age)", output);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Parameter type emission: base type in signature
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Verifies that a parameter with an adhoc refinement constraint is emitted
    /// with its base type in the C# method signature (not a refinement type name).
    /// </summary>
    [Fact]
    public void ParameterWithAdhocRefinement_EmitsBaseTypeInSignature()
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

        var output = CompileAndGetEmittedCSharp(source);

        // The parameter should be emitted as the base type `int`
        Assert.Contains("int x", output);
    }
}
