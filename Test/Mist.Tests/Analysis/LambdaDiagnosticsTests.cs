// Feature: first-class-functions, Property 4: Static Lambda Isolation
// Validates: Requirements 8.2, 8.3, 8.4, 8.5

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
/// Unit tests for lambda diagnostics and static-lambda isolation.
///
/// Covers each diagnostic from MIST_TC_0115 through MIST_TC_0126:
/// <list type="bullet">
///   <item><c>MIST_TC_0115</c> — Mixed typed/untyped params</item>
///   <item><c>MIST_TC_0116</c> — Cannot infer param types</item>
///   <item><c>MIST_TC_0117</c> — Inconsistent return types</item>
///   <item><c>MIST_TC_0118</c> — Missing return in non-void block lambda</item>
///   <item><c>MIST_TC_0119</c> — Func invocation argument count mismatch</item>
///   <item><c>MIST_TC_0120</c> — Func invocation argument type mismatch</item>
///   <item><c>MIST_TC_0121</c> — Ambiguous method group</item>
///   <item><c>MIST_TC_0122</c> — Static lambda capture violation</item>
///   <item><c>MIST_TC_0123</c> — Ref struct capture</item>
///   <item><c>MIST_TC_0124</c> — Owned capture</item>
///   <item><c>MIST_TC_0125</c> — Borrowed/frozen/readonly-ref capture</item>
///   <item><c>MIST_TC_0126</c> — Await in non-async lambda</item>
/// </list>
///
/// All tests run fully in-process (no <c>dotnet</c> process spawning).
///
/// <b>Property 4: Static Lambda Isolation</b>
/// <b>Validates: Requirements 8.2, 8.3, 8.4, 8.5</b>
/// </summary>
[Trait("Feature", "first-class-functions")]
[Trait("Property", "4: Static Lambda Isolation")]
public sealed class LambdaDiagnosticsTests
{
    // ─── Pipeline wiring ─────────────────────────────────────────────────

    private static readonly MistLexer Lexer = new();
    private static readonly MistParser Parser = new();
    private static readonly SemanticModelBuilder Builder = new();
    private static readonly TypeCheckerAnalyzer Analyzer = new();

    // ─── Helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Parses a Mist source string, builds a semantic model with stdlib loaded,
    /// runs the type checker, and returns all diagnostics.
    /// </summary>
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

    // ═══════════════════════════════════════════════════════════════════════
    // MIST_TC_0115: Mixed typed/untyped parameters
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MixedTypedUntypedParams_EmitsMIST_TC_0115()
    {
        const string source = """
            class Program
            {
                public static void Main()
                {
                    func<int, string, string> f = (int x, y) => "hello";
                }
            }
            """;

        var diagnostics = GetDiagnostics(source);

        Assert.Contains(diagnostics, d => d.Id.Value == "MIST_TC_0115");
    }

    [Fact]
    public void AllTypedParams_DoesNotEmitMIST_TC_0115()
    {
        const string source = """
            class Program
            {
                public static void Main()
                {
                    func<int, string, string> f = (int x, string y) => "hello";
                }
            }
            """;

        var diagnostics = GetDiagnostics(source);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0115");
    }

    [Fact]
    public void AllUntypedParams_DoesNotEmitMIST_TC_0115()
    {
        const string source = """
            class Program
            {
                public static void Main()
                {
                    func<int, string, string> f = (x, y) => "hello";
                }
            }
            """;

        var diagnostics = GetDiagnostics(source);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0115");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // MIST_TC_0116: Cannot infer parameter types
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ImplicitParamsNoTargetType_EmitsMIST_TC_0116()
    {
        const string source = """
            class Program
            {
                public static void Main()
                {
                    var f = x => x;
                }
            }
            """;

        var diagnostics = GetDiagnostics(source);

        Assert.Contains(diagnostics, d => d.Id.Value == "MIST_TC_0116");
    }

    [Fact]
    public void ImplicitParamsWithTargetType_DoesNotEmitMIST_TC_0116()
    {
        const string source = """
            class Program
            {
                public static void Main()
                {
                    func<int, int> f = x => x;
                }
            }
            """;

        var diagnostics = GetDiagnostics(source);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0116");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // MIST_TC_0117: Inconsistent return types in block lambda
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void InconsistentReturnTypes_EmitsMIST_TC_0117()
    {
        const string source = """
            class Program
            {
                public static void Main()
                {
                    var f = (int x) => {
                        if (x > 0)
                        {
                            return "hello";
                        }
                        return 42;
                    };
                }
            }
            """;

        var diagnostics = GetDiagnostics(source);

        Assert.Contains(diagnostics, d => d.Id.Value == "MIST_TC_0117");
    }

    [Fact]
    public void ConsistentReturnTypes_DoesNotEmitMIST_TC_0117()
    {
        const string source = """
            class Program
            {
                public static void Main()
                {
                    var f = (int x) => {
                        if (x > 0)
                        {
                            return "hello";
                        }
                        return "world";
                    };
                }
            }
            """;

        var diagnostics = GetDiagnostics(source);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0117");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // MIST_TC_0118: Missing return in non-void block lambda
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MissingReturnInNonVoidBlockLambda_EmitsMIST_TC_0118()
    {
        const string source = """
            class Program
            {
                public static void Main()
                {
                    func<int, string> f = (x) => {
                        int y = x + 1;
                    };
                }
            }
            """;

        var diagnostics = GetDiagnostics(source);

        Assert.Contains(diagnostics, d => d.Id.Value == "MIST_TC_0118");
    }

    [Fact]
    public void BlockLambdaWithReturn_DoesNotEmitMIST_TC_0118()
    {
        const string source = """
            class Program
            {
                public static void Main()
                {
                    func<int, string> f = (x) => {
                        return "hello";
                    };
                }
            }
            """;

        var diagnostics = GetDiagnostics(source);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0118");
    }

    [Fact]
    public void VoidBlockLambdaWithoutReturn_DoesNotEmitMIST_TC_0118()
    {
        const string source = """
            class Program
            {
                public static void Main()
                {
                    func<int, void> f = (x) => {
                        int y = x + 1;
                    };
                }
            }
            """;

        var diagnostics = GetDiagnostics(source);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0118");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // MIST_TC_0119: Func invocation argument count mismatch
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void FuncInvocationTooFewArgs_EmitsMIST_TC_0119()
    {
        const string source = """
            class Program
            {
                public static void Main()
                {
                    func<int, int, int> f = (x, y) => x + y;
                    int result = f(1);
                }
            }
            """;

        var diagnostics = GetDiagnostics(source);

        Assert.Contains(diagnostics, d => d.Id.Value == "MIST_TC_0119");
    }

    [Fact]
    public void FuncInvocationTooManyArgs_EmitsMIST_TC_0119()
    {
        const string source = """
            class Program
            {
                public static void Main()
                {
                    func<int, int> f = (x) => x;
                    int result = f(1, 2);
                }
            }
            """;

        var diagnostics = GetDiagnostics(source);

        Assert.Contains(diagnostics, d => d.Id.Value == "MIST_TC_0119");
    }

    [Fact]
    public void FuncInvocationCorrectArgCount_DoesNotEmitMIST_TC_0119()
    {
        const string source = """
            class Program
            {
                public static void Main()
                {
                    func<int, int, int> f = (x, y) => x + y;
                    int result = f(1, 2);
                }
            }
            """;

        var diagnostics = GetDiagnostics(source);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0119");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // MIST_TC_0120: Func invocation argument type mismatch
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void FuncInvocationWrongArgType_EmitsMIST_TC_0120()
    {
        const string source = """
            class Program
            {
                public static void Main()
                {
                    func<int, int> f = (x) => x;
                    int result = f("hello");
                }
            }
            """;

        var diagnostics = GetDiagnostics(source);

        Assert.Contains(diagnostics, d => d.Id.Value == "MIST_TC_0120");
    }

    [Fact]
    public void FuncInvocationCorrectArgType_DoesNotEmitMIST_TC_0120()
    {
        const string source = """
            class Program
            {
                public static void Main()
                {
                    func<int, int> f = (x) => x;
                    int result = f(42);
                }
            }
            """;

        var diagnostics = GetDiagnostics(source);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0120");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // MIST_TC_0121: Ambiguous method group conversion
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void AmbiguousMethodGroup_EmitsMIST_TC_0121()
    {
        const string source = """
            class Program
            {
                public static int Process(int x)
                {
                    return x;
                }
                public static int Process(int x)
                {
                    return x + 1;
                }
                public static void Main()
                {
                    func<int, int> f = Process;
                }
            }
            """;

        var diagnostics = GetDiagnostics(source);

        Assert.Contains(diagnostics, d => d.Id.Value == "MIST_TC_0121");
    }

    [Fact]
    public void UniqueMethodGroup_DoesNotEmitMIST_TC_0121()
    {
        const string source = """
            class Program
            {
                public static int Process(int x)
                {
                    return x;
                }
                public static void Main()
                {
                    func<int, int> f = Process;
                }
            }
            """;

        var diagnostics = GetDiagnostics(source);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0121");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // MIST_TC_0122: Static lambda capture violation
    // (Property 4: Static Lambda Isolation)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void StaticLambda_ReferencingOuterVariable_EmitsMIST_TC_0122()
    {
        const string source = """
            class Program
            {
                public static void Main()
                {
                    int outer = 10;
                    func<int, int> f = static (x) => x + outer;
                }
            }
            """;

        var diagnostics = GetDiagnostics(source);

        Assert.Contains(diagnostics, d => d.Id.Value == "MIST_TC_0122");
    }

    [Fact]
    public void StaticLambda_ReferencingThis_EmitsMIST_TC_0122()
    {
        const string source = """
            class MyClass
            {
                public int value;
                public void DoWork()
                {
                    func<int> f = static () => this.value;
                }
            }
            class Program
            {
                public static void Main() {}
            }
            """;

        var diagnostics = GetDiagnostics(source);

        Assert.Contains(diagnostics, d => d.Id.Value == "MIST_TC_0122");
    }

    [Fact]
    public void StaticLambda_ReferencingOwnParams_DoesNotEmitMIST_TC_0122()
    {
        const string source = """
            class Program
            {
                public static void Main()
                {
                    func<int, int> f = static (x) => x + 1;
                }
            }
            """;

        var diagnostics = GetDiagnostics(source);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0122");
    }

    [Fact]
    public void StaticLambda_ReferencingStaticField_DoesNotEmitMIST_TC_0122()
    {
        const string source = """
            class Config
            {
                public static int Multiplier;
            }
            class Program
            {
                public static void Main()
                {
                    func<int, int> f = static (x) => x + Config.Multiplier;
                }
            }
            """;

        var diagnostics = GetDiagnostics(source);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0122");
    }

    [Fact]
    public void StaticLambda_ReferencingInstanceField_EmitsMIST_TC_0122()
    {
        const string source = """
            class MyClass
            {
                public int value;
                public void DoWork()
                {
                    func<int> f = static () => value;
                }
            }
            class Program
            {
                public static void Main() {}
            }
            """;

        var diagnostics = GetDiagnostics(source);

        Assert.Contains(diagnostics, d => d.Id.Value == "MIST_TC_0122");
    }

    [Fact]
    public void NonStaticLambda_ReferencingOuterVariable_DoesNotEmitMIST_TC_0122()
    {
        const string source = """
            class Program
            {
                public static void Main()
                {
                    int outer = 10;
                    func<int, int> f = (x) => x + outer;
                }
            }
            """;

        var diagnostics = GetDiagnostics(source);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0122");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // MIST_TC_0123: Ref struct capture
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Lambda_CapturingRefStructVariable_EmitsMIST_TC_0123()
    {
        const string source = """
            public ref struct Span
            {
                public int value;
            }
            class Program
            {
                public static void Main()
                {
                    Span s = new Span();
                    func<int> f = () => s.value;
                }
            }
            """;

        var diagnostics = GetDiagnostics(source);

        Assert.Contains(diagnostics, d => d.Id.Value == "MIST_TC_0123");
    }

    [Fact]
    public void Lambda_NotCapturingRefStruct_DoesNotEmitMIST_TC_0123()
    {
        const string source = """
            public ref struct Span
            {
                public int value;
            }
            class Program
            {
                public static void Main()
                {
                    int x = 10;
                    func<int> f = () => x;
                }
            }
            """;

        var diagnostics = GetDiagnostics(source);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0123");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // MIST_TC_0124: Owned capture
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Lambda_CapturingOwnedVariable_EmitsMIST_TC_0124()
    {
        const string source = """
            owned class Resource
            {
                public int value;
            }
            class Program
            {
                public static void Main()
                {
                    owned Resource r = new Resource();
                    func<int> f = () => r.value;
                }
            }
            """;

        var diagnostics = GetDiagnostics(source);

        Assert.Contains(diagnostics, d => d.Id.Value == "MIST_TC_0124");
    }

    [Fact]
    public void Lambda_NotCapturingOwnedVariable_DoesNotEmitMIST_TC_0124()
    {
        const string source = """
            owned class Resource
            {
                public int value;
            }
            class Program
            {
                public static void Main()
                {
                    int x = 10;
                    func<int> f = () => x;
                }
            }
            """;

        var diagnostics = GetDiagnostics(source);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0124");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // MIST_TC_0125: Borrowed/frozen/readonly-ref capture
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Lambda_CapturingBorrowedVariable_EmitsMIST_TC_0125()
    {
        const string source = """
            owned class Resource
            {
                public int value;
            }
            class Program
            {
                public static void Main()
                {
                    owned Resource r = new Resource();
                    borrowed Resource b = r;
                    func<int> f = () => b.value;
                }
            }
            """;

        var diagnostics = GetDiagnostics(source);

        Assert.Contains(diagnostics, d => d.Id.Value == "MIST_TC_0125");
    }

    [Fact]
    public void Lambda_CapturingFrozenVariable_EmitsMIST_TC_0125()
    {
        const string source = """
            owned class Resource
            {
                public int value;
            }
            class Program
            {
                public static void Main()
                {
                    owned Resource r = new Resource();
                    frozen Resource fr = r;
                    func<int> f = () => fr.value;
                }
            }
            """;

        var diagnostics = GetDiagnostics(source);

        Assert.Contains(diagnostics, d => d.Id.Value == "MIST_TC_0125");
    }

    [Fact]
    public void Lambda_CapturingReadonlyRefVariable_EmitsMIST_TC_0125()
    {
        const string source = """
            owned class Resource
            {
                public int value;
            }
            class Program
            {
                public static void Main()
                {
                    owned Resource r = new Resource();
                    readonly ref Resource rr = r;
                    func<int> f = () => rr.value;
                }
            }
            """;

        var diagnostics = GetDiagnostics(source);

        Assert.Contains(diagnostics, d => d.Id.Value == "MIST_TC_0125");
    }

    [Fact]
    public void Lambda_CapturingUnrestrictedVariable_DoesNotEmitMIST_TC_0125()
    {
        const string source = """
            class Resource
            {
                public int value;
            }
            class Program
            {
                public static void Main()
                {
                    Resource r = new Resource();
                    func<int> f = () => r.value;
                }
            }
            """;

        var diagnostics = GetDiagnostics(source);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0125");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // MIST_TC_0126: Await in non-async lambda
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void AwaitInNonAsyncLambda_EmitsMIST_TC_0126()
    {
        const string source = """
            class Program
            {
                public static async int GetValue()
                {
                    return 42;
                }
                public static void Main()
                {
                    func<int, int> f = (x) => {
                        int result = await GetValue();
                        return result + x;
                    };
                }
            }
            """;

        var diagnostics = GetDiagnostics(source);

        Assert.Contains(diagnostics, d => d.Id.Value == "MIST_TC_0126");
    }

    [Fact]
    public void AwaitInAsyncLambda_DoesNotEmitMIST_TC_0126()
    {
        const string source = """
            class Program
            {
                public static async int GetValue()
                {
                    return 42;
                }
                public static void Main()
                {
                    var f = async (int x) => {
                        int result = await GetValue();
                        return result + x;
                    };
                }
            }
            """;

        var diagnostics = GetDiagnostics(source);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0126");
    }

    [Fact]
    public void AwaitInExpressionLambdaNonAsync_EmitsMIST_TC_0126()
    {
        const string source = """
            class Program
            {
                public static async int GetValue()
                {
                    return 42;
                }
                public static void Main()
                {
                    func<int> f = () => await GetValue();
                }
            }
            """;

        var diagnostics = GetDiagnostics(source);

        Assert.Contains(diagnostics, d => d.Id.Value == "MIST_TC_0126");
    }
}