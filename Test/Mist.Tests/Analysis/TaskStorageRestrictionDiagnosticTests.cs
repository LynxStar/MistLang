// **Validates: Requirements 7.5, 7.6, 7.7**

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Tests.Helpers;

namespace Mist.Tests.Analysis;

/// <summary>
/// Unit tests verifying that MIST_TC_0110 is emitted when <c>Task&lt;T&gt;</c>
/// is stored in a field, property, or collection (array element type).
/// Also verifies that <c>Task&lt;T&gt;</c> is allowed in local variables,
/// method parameters, and return types.
/// </summary>
public class TaskStorageRestrictionDiagnosticTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();
    private static readonly TypeCheckerAnalyzer Analyzer = new();

    /// <summary>
    /// Compiles source with stdlib loaded (needed for Task&lt;T&gt; resolution).
    /// </summary>
    private static ImmutableArray<Diagnostic> CompileWithStdlibAndGetDiagnostics(string source)
    {
        var sourceText = new SourceText("test.mist", source);

        var lexDiag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexDiag);

        var parseDiag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseDiag);
        if (parseResult.Root is null)
            return parseDiag.ToImmutableArray();

        var (model, buildDiag) = StdlibTestHelper.BuildModelWithStdlib(
            parseResult.Root, source: source);

        var tcDiag = new DiagnosticBag();
        Analyzer.Analyze(model, tcDiag);

        var all = new List<Diagnostic>();
        all.AddRange(buildDiag);
        all.AddRange(tcDiag.ToImmutableArray());
        return all.ToImmutableArray();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Field storage restriction tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TaskFieldDeclaration_EmitsMIST_TC_0110()
    {
        const string source = """
            using Standard;
            class Foo {
                Task<int> myTask;
            }
            class Program { public static void Main() {} }
            """;

        var diagnostics = CompileWithStdlibAndGetDiagnostics(source);

        var diag = Assert.Single(diagnostics, d => d.Id.Value == "MIST_TC_0110");
        Assert.Contains("field", diag.Message);
        Assert.Contains("Task<T>", diag.Message);
        Assert.Contains("structured concurrency", diag.Message);
    }

    [Fact]
    public void TaskVoidFieldDeclaration_EmitsMIST_TC_0110()
    {
        const string source = """
            using Standard;
            class Foo {
                Task<void> work;
            }
            class Program { public static void Main() {} }
            """;

        var diagnostics = CompileWithStdlibAndGetDiagnostics(source);

        var diag = Assert.Single(diagnostics, d => d.Id.Value == "MIST_TC_0110");
        Assert.Contains("field", diag.Message);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property storage restriction tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TaskPropertyDeclaration_EmitsMIST_TC_0110()
    {
        const string source = """
            using Standard;
            class Foo {
                Task<int> MyTask { get { return default; } }
            }
            class Program { public static void Main() {} }
            """;

        var diagnostics = CompileWithStdlibAndGetDiagnostics(source);

        var diag = Assert.Single(diagnostics, d => d.Id.Value == "MIST_TC_0110");
        Assert.Contains("property", diag.Message);
        Assert.Contains("Task<T>", diag.Message);
        Assert.Contains("structured concurrency", diag.Message);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Collection (array) storage restriction tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TaskArrayFieldDeclaration_EmitsMIST_TC_0110()
    {
        const string source = """
            using Standard;
            class Foo {
                Task<int>[] tasks;
            }
            class Program { public static void Main() {} }
            """;

        var diagnostics = CompileWithStdlibAndGetDiagnostics(source);

        var diag = Assert.Single(diagnostics, d => d.Id.Value == "MIST_TC_0110");
        Assert.Contains("field", diag.Message);
    }

    [Fact]
    public void TaskArrayPropertyDeclaration_EmitsMIST_TC_0110()
    {
        const string source = """
            using Standard;
            class Foo {
                Task<string>[] Tasks { get { return default; } }
            }
            class Program { public static void Main() {} }
            """;

        var diagnostics = CompileWithStdlibAndGetDiagnostics(source);

        var diag = Assert.Single(diagnostics, d => d.Id.Value == "MIST_TC_0110");
        Assert.Contains("property", diag.Message);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Allowed usages (no diagnostic expected)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TaskAsMethodReturnType_DoesNotEmitMIST_TC_0110()
    {
        const string source = """
            using Standard;
            class Foo {
                public Task<int> GetTask() { return default; }
            }
            class Program { public static void Main() {} }
            """;

        var diagnostics = CompileWithStdlibAndGetDiagnostics(source);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0110");
    }

    [Fact]
    public void TaskAsMethodParameter_DoesNotEmitMIST_TC_0110()
    {
        const string source = """
            using Standard;
            class Foo {
                public void Process(Task<int> task) { }
            }
            class Program { public static void Main() {} }
            """;

        var diagnostics = CompileWithStdlibAndGetDiagnostics(source);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0110");
    }

    [Fact]
    public void TaskAsLocalVariable_DoesNotEmitMIST_TC_0110()
    {
        const string source = """
            using Standard;
            class Foo {
                public async int Compute() { return 42; }
                public void Run() {
                    var t = async Compute();
                }
            }
            class Program { public static void Main() {} }
            """;

        var diagnostics = CompileWithStdlibAndGetDiagnostics(source);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0110");
    }

    [Fact]
    public void NonTaskFieldDeclaration_DoesNotEmitMIST_TC_0110()
    {
        const string source = """
            using Standard;
            class Foo {
                int value;
                string name;
            }
            class Program { public static void Main() {} }
            """;

        var diagnostics = CompileWithStdlibAndGetDiagnostics(source);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == "MIST_TC_0110");
    }
}
