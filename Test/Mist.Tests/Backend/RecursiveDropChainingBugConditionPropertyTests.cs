// Bugfix: func-async-auto-await-fix, Property 1: Bug Condition
// Drop Returns Droppable Type But Chain Does Not Continue
//
// **Validates: Requirements 1.3, 2.3, 2.4, 2.5**
//
// CRITICAL: This test MUST FAIL on unfixed code — failure confirms the bug exists.
// DO NOT attempt to fix the test or the code when it fails.
//
// Bug Condition: dropSite IS AutoDropEmission
//                AND dropSite.dropReturnType.hasDrop = true
//                AND dropSite.dropReturnType.discipline IN {Linear, Relevant}
//                AND NOT dropSite.returnValueCapturedByUser
//
// Expected behavior after fix: chained drop emission — e.g., `await (await nested);`
// for Task<Task<T>>, or `await localName.Drop();` for custom type returning Task.

using System.Collections.Immutable;
using System.Text;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Backend;

/// <summary>
/// Bug condition exploration property test for Defect 3:
/// Recursive drop chaining is not implemented in the emitter.
///
/// <b>Property 1: Bug Condition</b> — Drop Returns Droppable Type But Chain Does Not Continue
///
/// <b>Validates: Requirements 1.3, 2.3, 2.4, 2.5</b>
///
/// For any auto-drop site where <c>Drop()</c> returns a type that is itself droppable
/// (linear or relevant with <c>HasDrop = true</c>) and the return value is not captured
/// by the user, the emitter SHALL emit chained drop operations recursively until reaching
/// a non-droppable or void return type.
///
/// On unfixed code this test is EXPECTED TO FAIL, confirming the bug exists:
/// all four auto-drop sites (<c>EmitDroppableLocalWithWrapping</c>,
/// <c>EmitDiscardedTaskWithWrapping</c>, <c>EmitRemainingFieldDropsIfManualDrop</c>,
/// <c>EmitWithBlockDropCall</c>) emit exactly one drop action without checking whether
/// the result of that drop is itself droppable.
///
/// All work runs in-process using compiler APIs — no external process is spawned.
/// </summary>
public class RecursiveDropChainingBugConditionPropertyTests
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

        // Emit in-memory (runs NeedsAsyncPropagationPass internally)
        var emitBag = new DiagnosticBag();
        var files = CSharpEmitter.EmitToMemory(model, "TestProgram", emitBag);

        if(emitBag.HasErrors)
        {
            var emitErrors = emitBag.ToImmutableArray()
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => $"  {d.Id.Value}: {d.Message}");
            return (false, "", $"Emit errors:\n{string.Join("\n", emitErrors)}");
        }

        // Return just the test.cs content (the user's file)
        if(files.TryGetValue("test.cs", out var testContent))
        {
            return (true, testContent, "");
        }

        // Fallback: concatenate all emitted content
        var sb = new StringBuilder();
        foreach(var (_, content) in files)
        {
            sb.AppendLine(content);
        }

        return (true, sb.ToString(), "");
    }

    // ─── Scenario record ─────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// A scenario describing an auto-drop site where <c>Drop()</c> returns a droppable type.
    /// The test generates varying combinations of droppable types to cover:
    /// - <c>Task&lt;Task&lt;T&gt;&gt;</c> — double-await needed
    /// - Custom linear type whose <c>Drop()</c> returns <c>Task&lt;T&gt;</c> — await of Drop() needed
    /// </summary>
    public record RecursiveDropScenario(
        RecursiveDropKind Kind,
        string InnerType,
        string MethodName)
    {
        /// <summary>
        /// The expected chained expression pattern in emitted C#.
        /// </summary>
        public string ExpectedChainPattern => Kind switch
        {
            // Task<Task<T>> at scope exit: first await yields Task<T>,
            // which is itself relevant with Drop, so a second await is needed.
            RecursiveDropKind.NestedTask => "await (await",

            // Custom linear type whose Drop() returns Task<T>:
            // localName.Drop() yields Task<T>, which needs await.
            RecursiveDropKind.CustomDropReturnsTask => "await",

            _ => ""
        };

        /// <summary>
        /// Pattern that proves single-layer (buggy) emission: only one await/Drop
        /// without chaining.
        /// </summary>
        public string SingleLayerPattern => Kind switch
        {
            RecursiveDropKind.NestedTask => "await ",
            RecursiveDropKind.CustomDropReturnsTask => ".Drop();",
            _ => ""
        };

        public override string ToString()
        {
            return Kind switch
            {
                RecursiveDropKind.NestedTask =>
                    $"Task<Task<{InnerType}>> local '{MethodName}' at scope exit — needs await (await x)",
                RecursiveDropKind.CustomDropReturnsTask =>
                    $"linear class Drop()->Task<{InnerType}> local '{MethodName}' at scope exit — needs await x.Drop()",
                _ => $"Unknown scenario in '{MethodName}'"
            };
        }
    }

    public enum RecursiveDropKind
    {
        /// <summary><c>Task&lt;Task&lt;T&gt;&gt;</c> local at scope exit.</summary>
        NestedTask,

        /// <summary>Custom linear type whose <c>Drop()</c> returns <c>Task&lt;T&gt;</c>.</summary>
        CustomDropReturnsTask,
    }

#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] InnerTypes = ["int", "string", "bool"];
    private static readonly string[] MethodNames = ["DoWork", "Process", "Execute", "RunTask"];

    public static Gen<RecursiveDropScenario> GenRecursiveDropScenario()
    {
        return from kind in Gen.Elements(
                   RecursiveDropKind.NestedTask,
                   RecursiveDropKind.CustomDropReturnsTask)
               from innerType in Gen.Elements(InnerTypes)
               from methodName in Gen.Elements(MethodNames)
               select new RecursiveDropScenario(kind, innerType, methodName);
    }

    public static Arbitrary<RecursiveDropScenario> ArbitraryRecursiveDropScenario() =>
        GenRecursiveDropScenario().ToArbitrary();

    // ─── Source construction ─────────────────────────────────────────────

    /// <summary>
    /// Builds Mist source for the given scenario.
    /// </summary>
    private static string BuildSource(RecursiveDropScenario scenario)
    {
        return scenario.Kind switch
        {
            RecursiveDropKind.NestedTask => BuildNestedTaskSource(scenario),
            RecursiveDropKind.CustomDropReturnsTask => BuildCustomDropReturnsTaskSource(scenario),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
    }

    /// <summary>
    /// Builds Mist source with a <c>Task&lt;Task&lt;T&gt;&gt;</c> local at scope exit.
    ///
    /// Pattern: a sync method has a local of type <c>Task&lt;Task&lt;T&gt;&gt;</c> produced
    /// by using the <c>async</c> functor on a NON-async method that returns <c>Task&lt;T&gt;</c>.
    /// Since the target method is not async, no auto-unwrap fires on the operand, so the
    /// async functor wraps <c>Task&lt;T&gt;</c> into <c>Task&lt;Task&lt;T&gt;&gt;</c>.
    /// At scope exit, <c>await nested</c> yields <c>Task&lt;T&gt;</c> — which is itself
    /// relevant with Drop — but on unfixed code, only one <c>await</c> is emitted.
    ///
    /// After fix: <c>await (await nested);</c> in the finally clause.
    /// </summary>
    private static string BuildNestedTaskSource(RecursiveDropScenario scenario)
    {
        var returnExpr = scenario.InnerType switch
        {
            "int" => "42",
            "string" => "\"hello\"",
            "bool" => "true",
            _ => "42"
        };

        return $$"""
            using Standard;

            namespace TestProject;

            public class TestClass
            {
                public static async {{scenario.InnerType}} AsyncHelper()
                {
                    return {{returnExpr}};
                }

                public static Task<{{scenario.InnerType}}> GetTask()
                {
                    return async AsyncHelper();
                }

                public static void {{scenario.MethodName}}()
                {
                    var nested = async GetTask();
                    Console.WriteLine("work");
                }
            }
            """;
    }

    /// <summary>
    /// Builds Mist source with a custom linear class whose <c>Drop()</c> returns
    /// <c>Task&lt;T&gt;</c>. At scope exit, <c>box.Drop()</c> yields <c>Task&lt;T&gt;</c>,
    /// which is relevant with Drop — but on unfixed code, only <c>box.Drop();</c> is emitted
    /// without awaiting the returned task.
    ///
    /// After fix: <c>await box.Drop();</c> in the finally clause.
    /// </summary>
    private static string BuildCustomDropReturnsTaskSource(RecursiveDropScenario scenario)
    {
        var returnExpr = scenario.InnerType switch
        {
            "int" => "42",
            "string" => "\"hello\"",
            "bool" => "true",
            _ => "42"
        };

        return $$"""
            using Standard;

            namespace TestProject;

            public linear class TaskBox
            {
                Task<{{scenario.InnerType}}> Inner;

                public TaskBox(Task<{{scenario.InnerType}}> inner)
                {
                    this.Inner = inner;
                }

                public Task<{{scenario.InnerType}}> Drop()
                {
                    return Inner;
                }
            }

            public class TestClass
            {
                public static async {{scenario.InnerType}} AsyncHelper()
                {
                    return {{returnExpr}};
                }

                public static void {{scenario.MethodName}}()
                {
                    var box = new TaskBox(async AsyncHelper());
                    Console.WriteLine("work");
                }
            }
            """;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 1: Bug Condition — Drop Returns Droppable Type But Chain
    //             Does Not Continue
    //
    // **Validates: Requirements 1.3, 2.3, 2.4, 2.5**
    //
    // For any auto-drop site where Drop() returns a type that is itself
    // droppable (linear or relevant with HasDrop = true) and the return
    // value is not captured by the user, the emitter SHALL emit chained
    // drop operations recursively.
    //
    // On UNFIXED code this test WILL FAIL — all four auto-drop sites emit
    // exactly one drop action without checking the result type.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 20, Arbitrary = new[] { typeof(RecursiveDropChainingBugConditionPropertyTests) })]
    public Property AutoDropSite_WithDroppableReturnType_ShouldEmitChainedDrop(RecursiveDropScenario scenario)
    {
        var source = BuildSource(scenario);
        var (succeeded, csContent, errorText) = BuildAndEmit(source);

        if(!succeeded)
        {
            return false.ToProperty()
                .Label($"Compilation/emission failed for scenario: {scenario}\n{errorText}");
        }

        // Inspect the emitted C# for the expected chaining pattern.
        //
        // For NestedTask (Task<Task<T>>):
        //   Expected after fix: `await (await nested);` in finally block
        //   On unfixed code: only `await nested;` — single layer, inner Task<T> discarded
        //
        // For CustomDropReturnsTask:
        //   Expected after fix: `await box.Drop();` in finally block
        //   On unfixed code: only `box.Drop();` — Task<T> return value discarded

        var hasChainedDrop = scenario.Kind switch
        {
            RecursiveDropKind.NestedTask =>
                // After fix: the finally block should contain a chained await like
                // `await (await nested);` meaning the inner Task is also awaited.
                csContent.Contains("await (await"),

            RecursiveDropKind.CustomDropReturnsTask =>
                // After fix: the finally block should await the return value of Drop():
                // `await box.Drop();` rather than bare `box.Drop();`
                ContainsAwaitedDrop(csContent, "box"),

            _ => false
        };

        if(!hasChainedDrop)
        {
            // Determine what IS emitted to document the counterexample
            var emittedPattern = scenario.Kind switch
            {
                RecursiveDropKind.NestedTask => ExtractFinallyContent(csContent, "nested"),
                RecursiveDropKind.CustomDropReturnsTask => ExtractFinallyContent(csContent, "box"),
                _ => "(unknown)"
            };

            return false.ToProperty()
                .Label($"BUG CONFIRMED: {scenario}\n" +
                       $"Emitted finally clause does NOT chain drops. " +
                       $"Single-layer emission found: '{emittedPattern.Trim()}'\n" +
                       $"Expected chaining pattern: '{scenario.ExpectedChainPattern}'\n" +
                       $"The Drop() return value (which is itself droppable) is silently discarded.");
        }

        return true.ToProperty()
            .Label($"Correctly emitted chained drop for: {scenario}");
    }

    // ─── Inspection helpers ──────────────────────────────────────────────

    /// <summary>
    /// Checks whether the emitted C# contains <c>await localName.Drop();</c> pattern,
    /// indicating the Drop() return value (a Task) is being awaited.
    /// </summary>
    private static bool ContainsAwaitedDrop(string csContent, string localName)
    {
        // Look for patterns like:
        //   await box.Drop();
        //   await (box.Drop());
        return csContent.Contains($"await {localName}.Drop()")
            || csContent.Contains($"await ({localName}.Drop()");
    }

    /// <summary>
    /// Extracts the content of the finally block that references the given local name.
    /// Used to document what the emitter actually produces as a counterexample.
    /// </summary>
    private static string ExtractFinallyContent(string csContent, string localName)
    {
        // Find lines in finally blocks that reference the local
        var lines = csContent.Split('\n');
        for(var i = 0; i < lines.Length; i++)
        {
            if(lines[i].Contains("finally"))
            {
                // Look at the next few lines for the drop statement
                for(var j = i + 1; j < Math.Min(i + 5, lines.Length); j++)
                {
                    if(lines[j].Contains(localName))
                    {
                        return lines[j];
                    }
                }
            }
        }

        return "(finally block not found for " + localName + ")";
    }
}