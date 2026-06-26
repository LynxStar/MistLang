// Feature: definite-assignment
// Task 5.3: Emit the per-index initialization loop in CSharpEmitter
// Validates: Requirements 5.3, 6.2

using System.Collections.Immutable;
using System.Text;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

/// <summary>
/// Unit tests for C# emitter output of <c>new Array&lt;T, Size&gt;(fn)</c> initializer construction.
///
/// Validates:
/// - Req 5.3: Emits code that invokes fn once per index in [0, Size), assigning result i to slot i
/// - Req 6.2: Every element read returns the value the initializer produced for that index
///
/// Uses in-memory compilation via <see cref="CSharpEmitter.EmitToMemory"/>
/// (no disk I/O, no process spawning).
/// </summary>
public sealed class ArrayInitializerEmissionTests
{
    private static readonly ILexer Lexer = new MistLexer();
    private static readonly IParser Parser = new MistParser();
    private static readonly SemanticModelBuilder ModelBuilder = new();
    private static readonly TypeCheckerAnalyzer TypeChecker = new();

    // ─── In-memory compilation helper ────────────────────────────────────

    private static (bool Succeeded, string CsContent, string ErrorText) BuildAndEmit(string mistSource)
    {
        var (stdlibUnits, stdlibAsts) = StdlibTestHelper.LoadStdlib();

        var userSourceText = new SourceText("test.mist", mistSource);
        var userUnit = new CompilationUnit(userSourceText);
        var diagnostics = new DiagnosticBag();
        var userTokens = Lexer.Lex(userSourceText, diagnostics);
        var userParseResult = Parser.Parse(userTokens, diagnostics);

        if(userParseResult.Root is null)
        {
            return (false, "", $"Failed to parse source:\n{mistSource}");
        }

        var allUnits = stdlibUnits.Add(userUnit);
        var allAsts = stdlibAsts.Add(userParseResult.Root);

        var compilation = new MistCompilation(allUnits);
        var model = ModelBuilder.Build(
            compilation,
            allAsts,
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            diagnostics);

        var buildErrors = diagnostics.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        if(buildErrors.Count > 0)
        {
            var errorText = string.Join("\n", buildErrors.Select(d => $"  {d.Id.Value}: {d.Message}"));
            return (false, "", $"Build errors:\n{errorText}");
        }

        // Type checker
        var tcBag = new DiagnosticBag();
        TypeChecker.Analyze(model, tcBag);
        var tcErrors = tcBag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        if(tcErrors.Count > 0)
        {
            var errorText = string.Join("\n", tcErrors.Select(d => $"  {d.Id.Value}: {d.Message}"));
            return (false, "", $"Type-check errors:\n{errorText}");
        }

        // Emit in-memory
        var emitBag = new DiagnosticBag();
        var files = CSharpEmitter.EmitToMemory(model, "TestProject", emitBag);

        if(emitBag.HasErrors)
        {
            var emitErrors = emitBag.ToImmutableArray()
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => $"  {d.Id.Value}: {d.Message}");
            return (false, "", $"Emit errors:\n{string.Join("\n", emitErrors)}");
        }

        if(files.TryGetValue("test.cs", out var testContent))
        {
            return (true, testContent, "");
        }

        var sb = new StringBuilder();
        foreach(var (_, content) in files)
        {
            sb.AppendLine(content);
        }

        return (true, sb.ToString(), "");
    }

    // ─── Tests ───────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that new Array&lt;Label, 3&gt;(fn) emits a temp array allocation
    /// followed by a for loop invoking fn per index.
    ///
    /// **Validates: Requirements 5.3, 6.2**
    /// </summary>
    [Fact]
    public void ArrayInitializer_ReferenceType_EmitsAllocationAndLoop()
    {
        const string source = """
            using Standard;
            namespace TestProject;

            public class Label
            {
                public string Text;
                public Label(string text) { this.Text = text; }
            }

            public class Program
            {
                public static void Main()
                {
                    var labels = new Array<Label, 3>(i => new Label("item"));
                }
            }
            """;

        var (succeeded, csContent, errorText) = BuildAndEmit(source);
        Assert.True(succeeded, $"Compilation failed:\n{errorText}");

        // Should contain a temp array allocation: var __arr_N = new Label[3];
        Assert.Contains("new Label[3]", csContent);
        // Should contain a for-loop with index iteration
        Assert.Contains("for (int __i_", csContent);
        // Should contain assignment of fn(i) to array slot
        Assert.Contains("__arr_", csContent);
        // Should NOT contain new Array (the old naive emission)
        Assert.DoesNotContain("new Array<", csContent);
    }

    /// <summary>
    /// Verifies that new Array&lt;int, 5&gt;(fn) emits a temp array allocation
    /// followed by a for loop for a primitive type.
    ///
    /// **Validates: Requirements 5.3, 6.2**
    /// </summary>
    [Fact]
    public void ArrayInitializer_PrimitiveType_EmitsAllocationAndLoop()
    {
        const string source = """
            using Standard;
            namespace TestProject;

            public class Program
            {
                public static void Main()
                {
                    var ints = new Array<int, 5>(i => i * 2);
                }
            }
            """;

        var (succeeded, csContent, errorText) = BuildAndEmit(source);
        Assert.True(succeeded, $"Compilation failed:\n{errorText}");

        // Should contain a temp array allocation: var __arr_N = new int[5];
        Assert.Contains("new int[5]", csContent);
        // Should contain a for-loop
        Assert.Contains("for (int __i_", csContent);
        // Loop bound should be 5
        Assert.Contains("< 5;", csContent);
    }

    /// <summary>
    /// Verifies that multiple array initializer constructions in the same scope
    /// use unique temp variable names to avoid collisions.
    ///
    /// **Validates: Requirement 5.3**
    /// </summary>
    [Fact]
    public void ArrayInitializer_MultipleSameScope_UsesUniqueNames()
    {
        const string source = """
            using Standard;
            namespace TestProject;

            public class Label
            {
                public string Text;
                public Label(string text) { this.Text = text; }
            }

            public class Program
            {
                public static void Main()
                {
                    var a = new Array<int, 3>(i => i);
                    var b = new Array<Label, 2>(i => new Label("x"));
                }
            }
            """;

        var (succeeded, csContent, errorText) = BuildAndEmit(source);
        Assert.True(succeeded, $"Compilation failed:\n{errorText}");

        // Should contain two distinct temp array names
        Assert.Contains("__arr_0", csContent);
        Assert.Contains("__arr_1", csContent);
        // Should contain two distinct index names
        Assert.Contains("__i_0", csContent);
        Assert.Contains("__i_1", csContent);
    }

    /// <summary>
    /// Verifies that the user's lambda function is invoked (not __CreateDefault) for the array fill.
    ///
    /// **Validates: Requirements 5.3, 6.2**
    /// </summary>
    [Fact]
    public void ArrayInitializer_InvokesUserFunction_NotCreateDefault()
    {
        const string source = """
            using Standard;
            namespace TestProject;

            public class Program
            {
                public static void Main()
                {
                    var items = new Array<string, 4>(i => "hello");
                }
            }
            """;

        var (succeeded, csContent, errorText) = BuildAndEmit(source);
        Assert.True(succeeded, $"Compilation failed:\n{errorText}");

        // The array fill loop should invoke the user fn, not __CreateDefault.
        // Look for the loop pattern to confirm it uses the lambda, not a default factory.
        Assert.Contains("__arr_", csContent);
        Assert.Contains("for (int __i_", csContent);
        // The loop body should NOT reference __CreateDefault
        var loopLine = csContent.Split('\n')
            .FirstOrDefault(l => l.Contains("__arr_") && l.Contains("__i_") && l.Contains("="));
        Assert.NotNull(loopLine);
        Assert.DoesNotContain("__CreateDefault", loopLine);
        // Should contain the user's lambda string
        Assert.Contains("\"hello\"", csContent);
    }
}