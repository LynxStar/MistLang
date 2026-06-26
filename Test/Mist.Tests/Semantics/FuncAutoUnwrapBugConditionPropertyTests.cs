// Bugfix: func-async-auto-await-fix, Property 1: Bug Condition
// Func Invocation Returns Task Instead of Unwrapped Type in Sync Context
//
// **Validates: Requirements 1.1, 2.1**
//
// CRITICAL: This test MUST FAIL on unfixed code — failure confirms the bug exists.
// DO NOT attempt to fix the test or the code when it fails.
//
// Bug Condition: expr IS FuncInvocation AND expr.funcType.returnType IS Task<T>
//                AND NOT isInAsyncContext(expr)
//
// Expected behavior after fix: expression type resolves to T (unwrapped),
// matching direct async method call behavior.

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Semantics;

/// <summary>
/// Bug condition exploration property test for Defect 1:
/// Func invocation does not auto-unwrap Task&lt;T&gt; to T in sync context.
///
/// <b>Property 1: Bug Condition</b> — Func Invocation Returns Task Instead of Unwrapped Type in Sync Context
///
/// <b>Validates: Requirements 1.1, 2.1</b>
///
/// For any Func-typed variable whose return type is Task&lt;T&gt; (e.g., an async lambda),
/// when invoked in a sync context (non-async method body), the expression type at the
/// call site SHALL be T (the unwrapped type), not Task&lt;T&gt;.
///
/// On unfixed code this test is EXPECTED TO FAIL, confirming the bug exists:
/// the Func invocation path in SemanticModelBuilder bypasses the auto-unwrap logic
/// that the direct async method call path uses.
///
/// All work runs in-process using compiler APIs — no external process is spawned.
/// </summary>
public class FuncAutoUnwrapBugConditionPropertyTests
{
    // ─── Pipeline wiring ─────────────────────────────────────────────────

    private static readonly ILexer Lexer = new MistLexer();
    private static readonly IParser Parser = new MistParser();
    private static readonly SemanticModelBuilder Builder = new();

    // ─── Helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Parses a Mist source string and builds a semantic model with stdlib loaded.
    /// Returns the model and diagnostics for assertion.
    /// </summary>
    private static (ISemanticModel Model, ImmutableArray<Diagnostic> Diagnostics) BuildModel(string source)
    {
        var (stdlibUnits, stdlibAsts) = Helpers.StdlibTestHelper.LoadStdlib();

        var userSourceText = new SourceText("test.mist", source);
        var userUnit = new CompilationUnit(userSourceText);
        var diagnostics = new DiagnosticBag();
        var userTokens = Lexer.Lex(userSourceText, diagnostics);
        var userParseResult = Parser.Parse(userTokens, diagnostics);

        if(userParseResult.Root is null)
        {
            throw new InvalidOperationException($"Failed to parse user source:\n{source}");
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

        return (model, diagnostics.ToImmutableArray());
    }

    /// <summary>
    /// Finds a local variable symbol by name in the semantic model.
    /// </summary>
    private static ISymbol? FindLocal(ISemanticModel model, string name)
    {
        return model.AllSymbols
            .FirstOrDefault(s => s.Name == name && s.Kind == SymbolKind.LocalVariable);
    }

    /// <summary>
    /// Checks whether a type is Task&lt;T&gt; from the Standard namespace.
    /// </summary>
    private static bool IsStandardTaskType(IType? type)
    {
        return type is ConstructedType ct
               && ct.GenericDefinition is ClassType taskClass
               && taskClass.DisplayName == "Task"
               && taskClass.ContainingNamespace == "Standard";
    }

    // ─── Scenario record ─────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// A scenario describing a Func invocation with Task&lt;T&gt; return type in a sync context.
    /// The Func is created via an async lambda with explicit parameter types.
    /// </summary>
    public record FuncAutoUnwrapScenario(
        string ParamType,
        string InnerReturnType,
        string ArgLiteral)
    {
        public override string ToString()
        {
            return $"var f = async ({ParamType} x) => <{InnerReturnType}>; var result = f({ArgLiteral});";
        }
    }

#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Maps Mist primitive type names to sample literal values for invocation arguments.
    /// </summary>
    private static readonly (string Type, string Literal, string ReturnExpr, string ExpectedDisplay)[] TypeVariants =
    [
        ("int", "42", "x + 1", "Int32"),
        ("string", "\"hello\"", "x", "String"),
        ("bool", "true", "x", "Boolean"),
    ];

    public static Gen<FuncAutoUnwrapScenario> GenFuncAutoUnwrapScenario()
    {
        return Gen.Elements(TypeVariants).Select(tv =>
            new FuncAutoUnwrapScenario(tv.Type, tv.ReturnExpr, tv.Literal));
    }

    public static Arbitrary<FuncAutoUnwrapScenario> ArbitraryFuncAutoUnwrapScenario()
    {
        return GenFuncAutoUnwrapScenario().ToArbitrary();
    }

    // ─── Source construction ─────────────────────────────────────────────

    /// <summary>
    /// Builds Mist source containing:
    /// - A sync method with an async lambda assigned to a variable (creating a Func with Task&lt;T&gt; return)
    /// - The lambda is invoked in the sync context
    /// - The result is assigned to an inferred-type local
    ///
    /// The expected behavior after fix: the result local's type should be T, not Task&lt;T&gt;.
    /// </summary>
    private static string BuildSource(FuncAutoUnwrapScenario scenario)
    {
        return $$"""
            namespace TestProject;
            public class TestClass
            {
                public static void SyncMethod()
                {
                    var asyncLambda = async ({{scenario.ParamType}} x) => {{scenario.InnerReturnType}};
                    var result = asyncLambda({{scenario.ArgLiteral}});
                }
            }
            """;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 1: Bug Condition — Func Invocation Returns Task Instead of
    //             Unwrapped Type in Sync Context
    //
    // **Validates: Requirements 1.1, 2.1**
    //
    // For any async lambda (Func-typed variable with Task<T> return type)
    // invoked in a sync context, the expression type at the call site SHALL
    // be T (the unwrapped type), not Task<T>.
    //
    // On UNFIXED code this test WILL FAIL — the Func invocation path does
    // not check _isInAsyncContext or call UnwrapTaskType, so the raw Task<T>
    // leaks through as the expression type.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 30, Arbitrary = new[] { typeof(FuncAutoUnwrapBugConditionPropertyTests) })]
    public Property FuncInvocation_InSyncContext_ShouldUnwrapTaskToT(FuncAutoUnwrapScenario scenario)
    {
        var source = BuildSource(scenario);
        var (model, _) = BuildModel(source);

        // Find the `result` local variable to check its inferred type
        var resultSymbol = FindLocal(model, "result");

        if(resultSymbol is null)
        {
            return false.ToProperty()
                .Label($"Could not find local 'result' in scenario: {scenario}");
        }

        var resultType = resultSymbol.DeclaredType;

        if(resultType is null)
        {
            return false.ToProperty()
                .Label($"result.DeclaredType is null in scenario: {scenario}");
        }

        // The EXPECTED behavior (after fix): result type should NOT be Task<T>.
        // It should be the unwrapped inner type T.
        //
        // On UNFIXED code: result type WILL be Task<T>, causing this assertion to fail.
        // That failure CONFIRMS the bug exists.
        if(IsStandardTaskType(resultType))
        {
            // Bug detected: the Func invocation returned Task<T> instead of T
            var ct = (ConstructedType)resultType;
            var innerType = ct.TypeArguments.Count > 0 ? ct.TypeArguments[0].DisplayName : "?";
            return false.ToProperty()
                .Label($"BUG CONFIRMED: Func invocation in sync context returned Task<{innerType}> " +
                       $"instead of unwrapped type '{innerType}'. " +
                       $"Source: {scenario}");
        }

        // If we get here, the type was correctly unwrapped to T
        return true.ToProperty()
            .Label($"Correctly unwrapped to '{resultType.DisplayName}' for scenario: {scenario}");
    }
}