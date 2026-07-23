// Feature: variadics, Task 17.3: Generalize Func invocation to a single rule
// Validates: Requirements 15.3, 15.4, 15.5

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Confirms that the generalized Func invocation rule works uniformly across
/// all arities, including the empty-pack case (zero parameters). The existing
/// <c>CheckFuncInvocationArguments</c> logic loops over
/// <c>TypeArguments.Count - 1</c> parameter types, which naturally handles
/// every arity in one rule without per-arity recognition.
///
/// <b>Validates: Requirements 15.3, 15.4, 15.5</b>
/// </summary>
[Trait("Feature", "variadics")]
[Trait("Requirement", "15.3, 15.4, 15.5")]
public sealed class FuncInvocationGeneralizationTests
{
    // --- Pipeline wiring ---

    private static readonly MistLexer Lexer = new();
    private static readonly MistParser Parser = new();
    private static readonly SemanticModelBuilder Builder = new();
    private static readonly TypeCheckerAnalyzer Analyzer = new();

    // --- Helpers ---

    private static ImmutableArray<Diagnostic> GetDiagnostics(string source)
    {
        var (stdlibUnits, stdlibAsts) = Helpers.StdlibTestHelper.LoadStdlib();

        var sourceText = new SourceText("test.mist", source);
        var userUnit = new CompilationUnit(sourceText);

        var lexDiag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexDiag);

        var parseDiag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseDiag);
        if(parseResult.Root is null)
        {
            return parseDiag.ToImmutableArray();
        }

        var allUnits = stdlibUnits.Add(userUnit);
        var allAsts = stdlibAsts.Add(parseResult.Root);

        var compilation = new MistCompilation(allUnits);
        var buildDiag = new DiagnosticBag();
        var model = Builder.Build(
            compilation,
            allAsts,
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            buildDiag);

        var tcDiag = new DiagnosticBag();
        Analyzer.Analyze(model, tcDiag);

        var all = new List<Diagnostic>();
        all.AddRange(buildDiag.ToImmutableArray());
        all.AddRange(tcDiag.ToImmutableArray());
        return all.ToImmutableArray();
    }

    // =====================================================================
    // 15.3: Func invocation is well-typed when arg list matches TArgs
    // =====================================================================

    [Fact]
    public void FuncInvocation_MultipleArgs_MatchingTypes_NoDiagnostic()
    {
        // func<int, string, bool> has TArgs=[int, string], TResult=bool.
        // Invoking with (int, string) should be accepted.
        const string source = """
            class Program
            {
                public static void Main()
                {
                    func<int, string, bool> f = (x, y) => true;
                    bool result = f(42, "hello");
                }
            }
            """;

        var diagnostics = GetDiagnostics(source);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0119");
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0120");
    }

    // =====================================================================
    // 15.4: Empty-pack case - func<TResult> with zero arguments
    // =====================================================================

    [Fact]
    public void FuncInvocation_ZeroArgs_EmptyPack_NoDiagnostic()
    {
        // func<int> has TArgs=[], TResult=int.
        // Invoking with zero arguments should be accepted (empty pack).
        const string source = """
            class Program
            {
                public static void Main()
                {
                    func<int> f = () => 42;
                    int result = f();
                }
            }
            """;

        var diagnostics = GetDiagnostics(source);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0119");
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0120");
    }

    [Fact]
    public void FuncInvocation_ZeroArgs_PassingArg_EmitsMIST_TC_0119()
    {
        // func<int> has TArgs=[], TResult=int.
        // Passing any argument to a zero-param Func should be rejected.
        const string source = """
            class Program
            {
                public static void Main()
                {
                    func<int> f = () => 42;
                    int result = f(1);
                }
            }
            """;

        var diagnostics = GetDiagnostics(source);

        Assert.Contains(diagnostics, d => d.Id.Value == "MIST_TC_0119");
    }

    // =====================================================================
    // 15.3: Length mismatch rejected
    // =====================================================================

    [Fact]
    public void FuncInvocation_MultipleArgs_TooFew_EmitsMIST_TC_0119()
    {
        // func<int, string, bool> expects 2 args; passing 1 should be rejected.
        const string source = """
            class Program
            {
                public static void Main()
                {
                    func<int, string, bool> f = (x, y) => true;
                    bool result = f(42);
                }
            }
            """;

        var diagnostics = GetDiagnostics(source);

        Assert.Contains(diagnostics, d => d.Id.Value == "MIST_TC_0119");
    }

    [Fact]
    public void FuncInvocation_MultipleArgs_TooMany_EmitsMIST_TC_0119()
    {
        // func<int, string, bool> expects 2 args; passing 3 should be rejected.
        const string source = """
            class Program
            {
                public static void Main()
                {
                    func<int, string, bool> f = (x, y) => true;
                    bool result = f(42, "hello", 99);
                }
            }
            """;

        var diagnostics = GetDiagnostics(source);

        Assert.Contains(diagnostics, d => d.Id.Value == "MIST_TC_0119");
    }

    // =====================================================================
    // 15.3: Assignability mismatch rejected
    // =====================================================================

    [Fact]
    public void FuncInvocation_MultipleArgs_WrongType_EmitsMIST_TC_0120()
    {
        // func<int, string, bool> expects (int, string); passing (int, int)
        // should be rejected with a type mismatch.
        const string source = """
            class Program
            {
                public static void Main()
                {
                    func<int, string, bool> f = (x, y) => true;
                    bool result = f(42, 99);
                }
            }
            """;

        var diagnostics = GetDiagnostics(source);

        Assert.Contains(diagnostics, d => d.Id.Value == "MIST_TC_0120");
    }

    // =====================================================================
    // 15.5: Coexistence with existing logic - single-param Func still works
    // =====================================================================

    [Fact]
    public void FuncInvocation_SingleArg_CorrectType_NoDiagnostic()
    {
        // func<int, int> has TArgs=[int], TResult=int.
        // Existing single-arg behavior must continue to work.
        const string source = """
            class Program
            {
                public static void Main()
                {
                    func<int, int> f = (x) => x + 1;
                    int result = f(42);
                }
            }
            """;

        var diagnostics = GetDiagnostics(source);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0119");
        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0120");
    }

    [Fact]
    public void FuncInvocation_SingleArg_WrongType_EmitsMIST_TC_0120()
    {
        // func<int, int> expects (int); passing (string) should be rejected.
        const string source = """
            class Program
            {
                public static void Main()
                {
                    func<int, int> f = (x) => x + 1;
                    int result = f("hello");
                }
            }
            """;

        var diagnostics = GetDiagnostics(source);

        Assert.Contains(diagnostics, d => d.Id.Value == "MIST_TC_0120");
    }
}