// Bugfix: func-async-auto-await-fix, Property 2: Preservation
// Existing Drop Emission Behaviors — Simple Drops and Explicit Captures
//
// **Validates: Requirements 3.5, 3.6, 3.7, 3.8**
//
// These tests capture EXISTING correct behaviors that must be preserved
// after the recursive drop chaining fix is implemented. They MUST PASS
// on unfixed code.
//
// Preservation scenarios:
// - Simple Drop() returning void emits single `localName.Drop();` (Req 3.8)
// - Task<T> local from direct async call emits `await localName;` (Req 3.5, 3.6)
// - Explicit capture `var x = v.Drop()` does NOT chain (Req 3.7)

using System.Text;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Backend;

/// <summary>
/// Preservation property tests for drop emission behaviors that must remain
/// unchanged after the recursive drop chaining fix is applied.
///
/// <b>Property 2: Preservation</b> — Existing Drop Emission Behaviors
///
/// <b>Validates: Requirements 3.5, 3.6, 3.7, 3.8</b>
///
/// These tests observe and encode the CURRENT correct behavior for non-bug
/// inputs (simple drops, Task locals, explicit captures). They should PASS
/// on both unfixed and fixed code.
///
/// All work runs in-process using compiler APIs — no external process is spawned.
/// </summary>
public class DropEmissionPreservationPropertyTests
{
    // ─── Pipeline wiring ─────────────────────────────────────────────────

    private static readonly ILexer Lexer = new MistLexer();
    private static readonly IParser Parser = new MistParser();
    private static readonly SemanticModelBuilder Builder = new();

    // ─── Helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Parses a Mist source string, builds a semantic model with stdlib loaded,
    /// runs <see cref="NeedsAsyncPropagationPass"/>, and emits C# in memory.
    /// Returns the concatenated emitted C# content for inspection.
    /// </summary>
    private static (bool Succeeded, string CsContent, string ErrorText) BuildAndEmit(string source)
    {
        var (stdlibUnits, stdlibAsts) = Helpers.StdlibTestHelper.LoadStdlib();

        var userSourceText = new SourceText("test.mist", source);
        var userUnit = new CompilationUnit(userSourceText);
        var diagnostics = new DiagnosticBag();
        var userTokens = Lexer.Lex(userSourceText, diagnostics);
        var userParseResult = Parser.Parse(userTokens, diagnostics);

        if(userParseResult.Root is null)
        {
            return (false, "", $"Failed to parse source:\n{source}");
        }

        var allUnits = stdlibUnits.Add(userUnit);
        var allAsts = stdlibAsts.Add(userParseResult.Root);

        var compilation = new MistCompilation(allUnits);
        var model = Builder.Build(
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

        var emitBag = new DiagnosticBag();
        var files = CSharpEmitter.EmitToMemory(model, "TestProgram", emitBag);

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
            _ = sb.AppendLine(content);
        }

        return (true, sb.ToString(), "");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Preservation Scenario 5: Simple Drop() returning void emits single
    //                          `localName.Drop();` without chaining
    //
    // **Validates: Requirements 3.8**
    //
    // When Drop() returns void or a non-droppable type, only a single drop
    // statement should be emitted. No recursive chaining should occur.
    // ═══════════════════════════════════════════════════════════════════════

#pragma warning disable CA1034

    public record SimpleDropScenario(string TypeName, string DropReturnType, string FieldType, string FieldInit)
    {
        public override string ToString()
        {
            return $"linear class '{TypeName}' with Drop() returning {DropReturnType}";
        }
    }

#pragma warning restore CA1034

    private static readonly (string TypeName, string DropReturn, string FieldType, string FieldInit)[] SimpleDropVariants =
    [
        ("Resource", "void", "string", "\"data\""),
        ("Handle", "void", "int", "42"),
        ("Connection", "void", "bool", "true"),
    ];

    public static Gen<SimpleDropScenario> GenSimpleDropScenario()
    {
        return Gen.Elements(SimpleDropVariants).Select(v =>
            new SimpleDropScenario(v.TypeName, v.DropReturn, v.FieldType, v.FieldInit));
    }

    public static Arbitrary<SimpleDropScenario> ArbitrarySimpleDropScenario()
    {
        return GenSimpleDropScenario().ToArbitrary();
    }

    private static string BuildSimpleDropSource(SimpleDropScenario scenario)
    {
        return $$"""
            using Standard;

            namespace TestProject;

            public linear class {{scenario.TypeName}}
            {
                {{scenario.FieldType}} Value;

                public {{scenario.TypeName}}({{scenario.FieldType}} value)
                {
                    this.Value = value;
                }

                public void Drop()
                {
                    Console.WriteLine("dropped");
                }
            }

            public class TestClass
            {
                public static void DoWork()
                {
                    var resource = new {{scenario.TypeName}}({{scenario.FieldInit}});
                    Console.WriteLine("work");
                }
            }
            """;
    }

    [Property(MaxTest = 20, Arbitrary = new[] { typeof(DropEmissionPreservationPropertyTests) })]
    public Property SimpleVoidDrop_EmitsSingleDropStatement(SimpleDropScenario scenario)
    {
        var source = BuildSimpleDropSource(scenario);
        var (succeeded, csContent, errorText) = BuildAndEmit(source);

        if(!succeeded)
        {
            return false.ToProperty()
                .Label($"Compilation/emission failed for scenario: {scenario}\n{errorText}");
        }

        // PRESERVATION: Simple void-returning Drop() should emit just `resource.Drop();`
        // in the finally block — no chaining, no await.
        var hasSimpleDrop = csContent.Contains("resource.Drop();");
        if(!hasSimpleDrop)
        {
            return false.ToProperty()
                .Label($"REGRESSION: Simple void Drop() did not emit 'resource.Drop();' in finally block. " +
                       $"Scenario: {scenario}\nEmitted C#:\n{csContent}");
        }

        // Verify no await is applied to the Drop() result (void can't be awaited).
        var hasAwaitedDrop = csContent.Contains("await resource.Drop()");
        if(hasAwaitedDrop)
        {
            return false.ToProperty()
                .Label($"REGRESSION: Void-returning Drop() was incorrectly awaited. " +
                       $"Scenario: {scenario}");
        }

        return true.ToProperty()
            .Label($"Preserved: simple void Drop() emits single 'resource.Drop();' for: {scenario}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Preservation Scenario 6: Discarded async method call in a block emits
    //                          scope-exit await via DiscardedTaskInfo wrapping
    //
    // **Validates: Requirements 3.5, 3.6**
    //
    // When an async method is called as a bare expression statement (result
    // not assigned), the emitter creates a synthetic `__task_N` variable and
    // emits `await __task_N;` in a try/finally block. This ensures the task
    // is not silently abandoned. This behavior WORKS correctly today.
    //
    // Also verifies that direct async calls with result assignment emit
    // `await` at the call site (the auto-await mechanism), which is the
    // existing correct behavior for Req 3.6.
    // ═══════════════════════════════════════════════════════════════════════

#pragma warning disable CA1034

    public record DiscardedTaskScenario(string ReturnType, string ReturnExpr)
    {
        public override string ToString()
        {
            return $"discarded async call returning {ReturnType} in sync context";
        }
    }

#pragma warning restore CA1034

    private static readonly (string Type, string Expr)[] DiscardedTaskVariants =
    [
        ("int", "42"),
        ("string", "\"hello\""),
        ("bool", "true"),
    ];

    public static Gen<DiscardedTaskScenario> GenDiscardedTaskScenario()
    {
        return Gen.Elements(DiscardedTaskVariants).Select(v =>
            new DiscardedTaskScenario(v.Type, v.Expr));
    }

    public static Arbitrary<DiscardedTaskScenario> ArbitraryDiscardedTaskScenario()
    {
        return GenDiscardedTaskScenario().ToArbitrary();
    }

    private static string BuildDiscardedTaskSource(DiscardedTaskScenario scenario)
    {
        return $$"""
            using Standard;

            namespace TestProject;

            public class TestClass
            {
                public static async {{scenario.ReturnType}} AsyncHelper()
                {
                    return {{scenario.ReturnExpr}};
                }

                public static void Caller()
                {
                    AsyncHelper();
                    Console.WriteLine("work");
                }
            }
            """;
    }

    [Property(MaxTest = 20, Arbitrary = new[] { typeof(DropEmissionPreservationPropertyTests) })]
    public Property DiscardedAsyncCall_EmitsScopeExitAwait(DiscardedTaskScenario scenario)
    {
        var source = BuildDiscardedTaskSource(scenario);
        var (succeeded, csContent, errorText) = BuildAndEmit(source);

        if(!succeeded)
        {
            return false.ToProperty()
                .Label($"Compilation/emission failed for scenario: {scenario}\n{errorText}");
        }

        // PRESERVATION: A discarded async call should emit a synthetic task variable
        // and await it in a finally block. Look for the pattern:
        //   var __task_N = await AsyncHelper();  (or similar synthetic variable)
        //   try { ... } finally { await __task_N; }
        //
        // The key behavior: the task is NOT silently abandoned.
        // Check that either:
        // 1. A synthetic __task variable exists with await in finally, OR
        // 2. The call is emitted with await at the call site (direct auto-await)
        var hasAwaitInFinally = csContent.Contains("finally") && csContent.Contains("await");
        var hasDirectAwait = csContent.Contains("await AsyncHelper()");

        if(!hasAwaitInFinally && !hasDirectAwait)
        {
            return false.ToProperty()
                .Label($"REGRESSION: Discarded async call did not emit scope-exit await. " +
                       $"Expected either a synthetic task with await in finally, or direct await. " +
                       $"Scenario: {scenario}\nEmitted C#:\n{csContent}");
        }

        return true.ToProperty()
            .Label($"Preserved: discarded async call has scope-exit await for: {scenario}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Preservation Scenario 7: Explicit capture `var x = v.Drop()` does NOT
    //                          trigger recursive chaining — x gets its own
    //                          scope-exit drop
    //
    // **Validates: Requirements 3.7**
    //
    // When the user explicitly captures the return value of Drop() into a
    // variable, the system should NOT recursively drop that value. Instead,
    // the captured variable gets its own independent scope-exit drop as a
    // named local.
    // ═══════════════════════════════════════════════════════════════════════

#pragma warning disable CA1034

    public record ExplicitCaptureScenario(string InnerType, string ReturnExpr, string OuterName, string InnerName)
    {
        public override string ToString()
        {
            return $"var {InnerName} = {OuterName}.Drop() — explicit capture of Drop() returning {InnerType}";
        }
    }

#pragma warning restore CA1034

    private static readonly (string Type, string Expr)[] CaptureVariants =
    [
        ("int", "42"),
        ("string", "\"data\""),
    ];

    private static readonly (string Outer, string Inner)[] CaptureNamePairs =
    [
        ("outer", "inner"),
        ("resource", "result"),
    ];

    public static Gen<ExplicitCaptureScenario> GenExplicitCaptureScenario()
    {
        return from variant in Gen.Elements(CaptureVariants)
               from names in Gen.Elements(CaptureNamePairs)
               select new ExplicitCaptureScenario(variant.Type, variant.Expr, names.Outer, names.Inner);
    }

    public static Arbitrary<ExplicitCaptureScenario> ArbitraryExplicitCaptureScenario()
    {
        return GenExplicitCaptureScenario().ToArbitrary();
    }

    private static string BuildExplicitCaptureSource(ExplicitCaptureScenario scenario)
    {
        // A linear class whose Drop() returns a non-droppable type (string/int).
        // The user explicitly captures the return value — no recursive chaining
        // should occur. The captured variable may get its own scope-exit handling
        // if it is itself droppable, but the outer Drop() should not chain.
        return $$"""
            using Standard;

            namespace TestProject;

            public linear class Wrapper
            {
                {{scenario.InnerType}} Value;

                public Wrapper({{scenario.InnerType}} value)
                {
                    this.Value = value;
                }

                public {{scenario.InnerType}} Drop()
                {
                    Console.WriteLine("wrapper dropped");
                    return Value;
                }
            }

            public class TestClass
            {
                public static void DoWork()
                {
                    var {{scenario.OuterName}} = new Wrapper({{scenario.ReturnExpr}});
                    var {{scenario.InnerName}} = {{scenario.OuterName}}.Drop();
                    Console.WriteLine({{scenario.InnerName}});
                }
            }
            """;
    }

    [Property(MaxTest = 20, Arbitrary = new[] { typeof(DropEmissionPreservationPropertyTests) })]
    public Property ExplicitCapture_DoesNotChainDrop(ExplicitCaptureScenario scenario)
    {
        var source = BuildExplicitCaptureSource(scenario);
        var (succeeded, csContent, errorText) = BuildAndEmit(source);

        if(!succeeded)
        {
            return false.ToProperty()
                .Label($"Compilation/emission failed for scenario: {scenario}\n{errorText}");
        }

        // PRESERVATION: Explicit capture should NOT trigger recursive chaining.
        // The emitted code should show a simple `outer.Drop()` call as part of the
        // user's explicit statement — NOT in a scope-exit finally block for 'outer'.
        //
        // Key insight: when the user writes `var inner = outer.Drop();`, the compiler
        // should NOT also emit a scope-exit drop for 'outer' since Drop() was explicitly
        // called. The 'inner' variable may get its own scope-exit handling if needed.

        // Verify there is NO additional scope-exit Drop() for the outer variable
        // beyond what the user explicitly wrote. The explicit `.Drop()` call consumes
        // the linear obligation.
        var lines = csContent.Split('\n');
        var outerDropInFinally = false;
        var inFinally = false;

        for(var i = 0; i < lines.Length; i++)
        {
            if(lines[i].Contains("finally"))
            {
                inFinally = true;
            }
            else if(inFinally && lines[i].Trim() == "}")
            {
                inFinally = false;
            }

            // Check if there's a scope-exit drop for the outer variable in a finally block
            if(inFinally && lines[i].Contains($"{scenario.OuterName}.Drop()"))
            {
                outerDropInFinally = true;
            }
        }

        if(outerDropInFinally)
        {
            return false.ToProperty()
                .Label($"REGRESSION: Explicit capture '{scenario.InnerName} = {scenario.OuterName}.Drop()' " +
                       $"still triggered a scope-exit drop for '{scenario.OuterName}' in finally block. " +
                       $"The explicit Drop() call should consume the linear obligation. " +
                       $"Scenario: {scenario}");
        }

        return true.ToProperty()
            .Label($"Preserved: explicit capture does not generate scope-exit drop for '{scenario.OuterName}': {scenario}");
    }
}