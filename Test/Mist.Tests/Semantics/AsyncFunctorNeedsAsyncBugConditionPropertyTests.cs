// Bugfix: func-async-auto-await-fix, Property 1: Bug Condition
// Async Functor Locals Do Not Seed NeedsCSharpAsync
//
// **Validates: Requirements 1.2, 2.2**
//
// CRITICAL: This test MUST FAIL on unfixed code — failure confirms the bug exists.
// DO NOT attempt to fix the test or the code when it fails.
//
// Bug Condition: NOT method.isAsync
//                AND method.body.contains(AsyncFunctorExpression)
//                AND NOT method.body.contains(DirectAsyncMethodCall)
//                AND NOT method.body.contains(ExplicitAwait)
//
// Expected behavior after fix: NeedsCSharpAsync = true, enabling valid
// scope-exit await emission.

using System.Collections.Immutable;
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

namespace Mist.Tests.Semantics;

/// <summary>
/// Bug condition exploration property test for Defect 2:
/// <c>NeedsAsyncPropagationPass</c> does not seed from async functor expressions.
///
/// <b>Property 1: Bug Condition</b> — Async Functor Locals Do Not Seed NeedsCSharpAsync
///
/// <b>Validates: Requirements 1.2, 2.2</b>
///
/// For any sync method whose body contains only async functor expressions
/// (<c>async SyncMethod()</c>) producing <c>Task&lt;T&gt;</c> locals — without
/// direct async method calls or explicit <c>await</c> — the method SHALL have
/// <c>NeedsCSharpAsync = true</c> after the propagation pass runs.
///
/// On unfixed code this test is EXPECTED TO FAIL, confirming the bug exists:
/// the <c>WalkMethodBody</c> method in <c>NeedsAsyncPropagationPass</c> has no
/// case for <c>AsyncExpressionUnaryNode</c>, so these methods never get seeded.
///
/// All work runs in-process using compiler APIs — no external process is spawned.
/// </summary>
public class AsyncFunctorNeedsAsyncBugConditionPropertyTests
{
    // ─── Pipeline wiring ─────────────────────────────────────────────────

    private static readonly ILexer Lexer = new MistLexer();
    private static readonly IParser Parser = new MistParser();
    private static readonly SemanticModelBuilder Builder = new();

    // ─── Helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Parses a Mist source string, builds a semantic model with stdlib loaded,
    /// and runs <see cref="NeedsAsyncPropagationPass"/> to compute NeedsCSharpAsync.
    /// </summary>
    private static (ISemanticModel Model, ImmutableArray<Diagnostic> Diagnostics) BuildModelAndRunPass(string source)
    {
        var (stdlibUnits, stdlibAsts) = Helpers.StdlibTestHelper.LoadStdlib();

        var userSourceText = new SourceText("test.mist", source);
        var userUnit = new CompilationUnit(userSourceText);
        var diagnostics = new DiagnosticBag();
        var userTokens = Lexer.Lex(userSourceText, diagnostics);
        var userParseResult = Parser.Parse(userTokens, diagnostics);

        if (userParseResult.Root is null)
            throw new InvalidOperationException($"Failed to parse user source:\n{source}");

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

        // Run the NeedsAsyncPropagationPass to compute NeedsCSharpAsync flags.
        NeedsAsyncPropagationPass.Run(model);

        return (model, diagnostics.ToImmutableArray());
    }

    /// <summary>
    /// Finds a method symbol by name in the semantic model.
    /// </summary>
    private static Symbol? FindMethod(ISemanticModel model, string name)
    {
        return model.AllSymbols
            .OfType<Symbol>()
            .FirstOrDefault(s => s.Name == name && s.Kind == SymbolKind.Method);
    }

    // ─── Scenario record ─────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// A scenario describing a sync method containing only async functor expressions
    /// (no direct async calls, no explicit await). The method calls a sync helper
    /// method via the <c>async</c> functor syntax, producing a <c>Task&lt;T&gt;</c> local.
    /// </summary>
    public record AsyncFunctorSeedScenario(
        string HelperReturnType,
        string HelperBody,
        string MethodName,
        int FunctorCount)
    {
        public override string ToString()
        {
            return $"sync method '{MethodName}' with {FunctorCount} async functor(s) calling sync helper returning {HelperReturnType}";
        }
    }

#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly (string ReturnType, string Body)[] HelperVariants =
    [
        ("int", "return 42;"),
        ("string", "return \"hello\";"),
        ("bool", "return true;"),
    ];

    private static readonly string[] MethodNames =
        ["DoWork", "Process", "Execute", "RunTask", "Compute"];

    public static Gen<AsyncFunctorSeedScenario> GenAsyncFunctorSeedScenario()
    {
        return from helper in Gen.Elements(HelperVariants)
               from methodName in Gen.Elements(MethodNames)
               from count in Gen.Choose(1, 3)
               select new AsyncFunctorSeedScenario(helper.ReturnType, helper.Body, methodName, count);
    }

    public static Arbitrary<AsyncFunctorSeedScenario> ArbitraryAsyncFunctorSeedScenario() =>
        GenAsyncFunctorSeedScenario().ToArbitrary();

    // ─── Source construction ─────────────────────────────────────────────

    /// <summary>
    /// Builds Mist source containing:
    /// - A sync helper method (NOT async) that returns a value
    /// - A sync method that uses <c>async HelperMethod()</c> to produce Task&lt;T&gt; locals
    /// - The sync method has NO direct async method calls and NO explicit await
    ///
    /// The bug condition: the sync method containing async functor expressions
    /// should get NeedsCSharpAsync = true (so scope-exit await is valid in emitted C#),
    /// but on unfixed code it does NOT get marked.
    /// </summary>
    private static string BuildSource(AsyncFunctorSeedScenario scenario)
    {
        var functorStatements = string.Join("\n        ",
            Enumerable.Range(0, scenario.FunctorCount)
                .Select(i => $"var task{i} = async SyncHelper();"));

        return $$"""
            namespace TestProject;

            public class TestClass
            {
                public static {{scenario.HelperReturnType}} SyncHelper()
                {
                    {{scenario.HelperBody}}
                }

                public static void {{scenario.MethodName}}()
                {
                    {{functorStatements}}
                }
            }
            """;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 1: Bug Condition — Async Functor Locals Do Not Seed
    //             NeedsCSharpAsync
    //
    // **Validates: Requirements 1.2, 2.2**
    //
    // For any sync method containing only async functor expressions
    // (async SyncMethod()) that produce Task<T> locals — with no direct
    // async calls and no explicit await — the method SHALL have
    // NeedsCSharpAsync = true after the propagation pass.
    //
    // On UNFIXED code this test WILL FAIL — the WalkMethodBody in
    // NeedsAsyncPropagationPass has no case for AsyncExpressionUnaryNode,
    // so these methods never get seeded.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 30, Arbitrary = new[] { typeof(AsyncFunctorNeedsAsyncBugConditionPropertyTests) })]
    public Property SyncMethodWithAsyncFunctorOnly_ShouldHaveNeedsCSharpAsync(AsyncFunctorSeedScenario scenario)
    {
        var source = BuildSource(scenario);
        var (model, _) = BuildModelAndRunPass(source);

        // Find the target method (the one with async functor expressions).
        var methodSymbol = FindMethod(model, scenario.MethodName);

        if (methodSymbol is null)
            return false.ToProperty()
                .Label($"Could not find method '{scenario.MethodName}' in scenario: {scenario}");

        // Verify preconditions: the method is NOT declared async.
        if (methodSymbol.IsAsync)
            return false.ToProperty()
                .Label($"Method '{scenario.MethodName}' should be sync but is declared async: {scenario}");

        // The EXPECTED behavior (after fix): NeedsCSharpAsync should be true.
        // The async functor expression produces a Task<T> local that needs
        // scope-exit await, which requires the method to be emitted as async.
        //
        // On UNFIXED code: NeedsCSharpAsync WILL be false, causing this to fail.
        // That failure CONFIRMS the bug exists.
        if (!methodSymbol.NeedsCSharpAsync)
        {
            return false.ToProperty()
                .Label($"BUG CONFIRMED: Sync method '{scenario.MethodName}' with {scenario.FunctorCount} " +
                       $"async functor expression(s) calling sync helper returning {scenario.HelperReturnType} " +
                       $"has NeedsCSharpAsync = false. " +
                       $"Expected NeedsCSharpAsync = true for valid scope-exit await emission. " +
                       $"Source pattern: void {scenario.MethodName}() {{ var task = async SyncHelper(); }}");
        }

        // If we get here, NeedsCSharpAsync was correctly set to true.
        return true.ToProperty()
            .Label($"Correctly marked NeedsCSharpAsync = true for '{scenario.MethodName}': {scenario}");
    }
}
