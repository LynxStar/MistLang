// Bugfix: func-async-auto-await-fix, Property 2: Preservation
// Existing Auto-Await, Func, and NeedsAsync Propagation Behaviors
//
// **Validates: Requirements 3.1, 3.2, 3.3, 3.4**
//
// These tests capture EXISTING correct behaviors that must be preserved
// after the fixes are implemented. They MUST PASS on unfixed code.
//
// Preservation scenarios:
// - Direct async method call in sync context → type resolves to T (Req 3.2)
// - Func invocation in async context → type stays Task<T> (Req 3.1)
// - Func invocation with non-Task return → returns Func return type (Req 3.3)
// - Methods with direct async calls → NeedsCSharpAsync = true (Req 3.4)

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
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Semantics;

/// <summary>
/// Preservation property tests for the auto-await and NeedsAsync behaviors
/// that must remain unchanged after the bugfix is applied.
///
/// <b>Property 2: Preservation</b> — Existing Auto-Await, Func, and Propagation Behaviors
///
/// <b>Validates: Requirements 3.1, 3.2, 3.3, 3.4</b>
///
/// These tests observe and encode the CURRENT correct behavior on non-bug-condition
/// inputs. They should PASS on both unfixed and fixed code.
///
/// All work runs in-process using compiler APIs — no external process is spawned.
/// </summary>
public class AutoAwaitPreservationPropertyTests
{
    // ─── Pipeline wiring ─────────────────────────────────────────────────

    private static readonly ILexer Lexer = new MistLexer();
    private static readonly IParser Parser = new MistParser();
    private static readonly SemanticModelBuilder Builder = new();

    // ─── Helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Parses a Mist source string, builds a semantic model with stdlib loaded,
    /// and runs <see cref="NeedsAsyncPropagationPass"/>.
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

        NeedsAsyncPropagationPass.Run(model);

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
    /// Finds a method symbol by name in the semantic model.
    /// </summary>
    private static Symbol? FindMethod(ISemanticModel model, string name)
    {
        return model.AllSymbols
            .OfType<Symbol>()
            .FirstOrDefault(s => s.Name == name && s.Kind == SymbolKind.Method);
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

    // ═══════════════════════════════════════════════════════════════════════
    // Preservation Scenario 1: Direct async method call in sync context
    //                          auto-unwraps Task<T> → T
    //
    // **Validates: Requirements 3.2**
    //
    // This behavior WORKS correctly today and must continue to work after
    // the Func auto-unwrap fix is applied.
    // ═══════════════════════════════════════════════════════════════════════

#pragma warning disable CA1034

    public record DirectAsyncCallScenario(string ReturnType, string ReturnExpr, string MethodName)
    {
        public override string ToString() =>
            $"sync method '{MethodName}' calls async method returning {ReturnType}";
    }

#pragma warning restore CA1034

    private static readonly (string Type, string Expr)[] AsyncReturnVariants =
    [
        ("int", "42"),
        ("string", "\"hello\""),
        ("bool", "true"),
    ];

    private static readonly string[] CallerNames = ["Caller", "Process", "DoWork"];

    public static Gen<DirectAsyncCallScenario> GenDirectAsyncCallScenario()
    {
        return from variant in Gen.Elements(AsyncReturnVariants)
               from caller in Gen.Elements(CallerNames)
               select new DirectAsyncCallScenario(variant.Type, variant.Expr, caller);
    }

    public static Arbitrary<DirectAsyncCallScenario> ArbitraryDirectAsyncCallScenario() =>
        GenDirectAsyncCallScenario().ToArbitrary();

    private static string BuildDirectAsyncCallSource(DirectAsyncCallScenario scenario)
    {
        return $$"""
            namespace TestProject;

            public class TestClass
            {
                public static async {{scenario.ReturnType}} AsyncHelper()
                {
                    return {{scenario.ReturnExpr}};
                }

                public static void {{scenario.MethodName}}()
                {
                    var result = AsyncHelper();
                }
            }
            """;
    }

    [Property(MaxTest = 20, Arbitrary = new[] { typeof(AutoAwaitPreservationPropertyTests) })]
    public Property DirectAsyncCallInSyncContext_UnwrapsTaskToT(DirectAsyncCallScenario scenario)
    {
        var source = BuildDirectAsyncCallSource(scenario);
        var (model, _) = BuildModelAndRunPass(source);

        var resultSymbol = FindLocal(model, "result");
        if (resultSymbol is null)
            return false.ToProperty()
                .Label($"Could not find local 'result' in scenario: {scenario}");

        var resultType = resultSymbol.DeclaredType;
        if (resultType is null)
            return false.ToProperty()
                .Label($"result.DeclaredType is null in scenario: {scenario}");

        // PRESERVATION: Direct async call in sync context should unwrap Task<T> → T.
        // This is the existing auto-await behavior that must not regress.
        if (IsStandardTaskType(resultType))
            return false.ToProperty()
                .Label($"REGRESSION: Direct async call in sync context returned Task<T> instead of " +
                       $"unwrapped type. result type = {resultType.DisplayName}. Scenario: {scenario}");

        return true.ToProperty()
            .Label($"Preserved: direct async call unwraps to '{resultType.DisplayName}' for: {scenario}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Preservation Scenario 2: Func invocation in async context returns
    //                          Task<T> (no unwrap)
    //
    // **Validates: Requirements 3.1**
    //
    // In an async context, Func invocations with Task<T> return type should
    // NOT auto-unwrap — the developer is working with Tasks explicitly.
    // This behavior WORKS correctly today.
    // ═══════════════════════════════════════════════════════════════════════

#pragma warning disable CA1034

    public record FuncInAsyncContextScenario(string ParamType, string ReturnExpr, string ArgLiteral)
    {
        public override string ToString() =>
            $"async lambda ({ParamType} x) => {ReturnExpr} invoked in async context with arg {ArgLiteral}";
    }

#pragma warning restore CA1034

    private static readonly (string Type, string Literal, string ReturnExpr)[] FuncAsyncVariants =
    [
        ("int", "42", "x + 1"),
        ("string", "\"hello\"", "x"),
        ("bool", "true", "x"),
    ];

    public static Gen<FuncInAsyncContextScenario> GenFuncInAsyncContextScenario()
    {
        return Gen.Elements(FuncAsyncVariants).Select(v =>
            new FuncInAsyncContextScenario(v.Type, v.ReturnExpr, v.Literal));
    }

    public static Arbitrary<FuncInAsyncContextScenario> ArbitraryFuncInAsyncContextScenario() =>
        GenFuncInAsyncContextScenario().ToArbitrary();

    private static string BuildFuncInAsyncContextSource(FuncInAsyncContextScenario scenario)
    {
        return $$"""
            namespace TestProject;

            public class TestClass
            {
                public static async void AsyncCaller()
                {
                    var asyncLambda = async ({{scenario.ParamType}} x) => {{scenario.ReturnExpr}};
                    var result = asyncLambda({{scenario.ArgLiteral}});
                }
            }
            """;
    }

    [Property(MaxTest = 20, Arbitrary = new[] { typeof(AutoAwaitPreservationPropertyTests) })]
    public Property FuncInvocationInAsyncContext_ReturnsTaskT(FuncInAsyncContextScenario scenario)
    {
        var source = BuildFuncInAsyncContextSource(scenario);
        var (model, _) = BuildModelAndRunPass(source);

        var resultSymbol = FindLocal(model, "result");
        if (resultSymbol is null)
            return false.ToProperty()
                .Label($"Could not find local 'result' in scenario: {scenario}");

        var resultType = resultSymbol.DeclaredType;
        if (resultType is null)
            return false.ToProperty()
                .Label($"result.DeclaredType is null in scenario: {scenario}");

        // PRESERVATION: In an async context, Func invocations should NOT unwrap.
        // The result should remain Task<T>.
        if (!IsStandardTaskType(resultType))
            return false.ToProperty()
                .Label($"REGRESSION: Func invocation in async context did NOT return Task<T>. " +
                       $"result type = {resultType.DisplayName}. Expected Task<T>. Scenario: {scenario}");

        return true.ToProperty()
            .Label($"Preserved: Func in async context returns Task<T> ('{resultType.DisplayName}') for: {scenario}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Preservation Scenario 3: Func invocation with non-Task return type
    //                          returns Func's return type directly
    //
    // **Validates: Requirements 3.3**
    //
    // A Func<int, string> invoked in any context should return string, not
    // try to unwrap anything. This behavior WORKS correctly today.
    // ═══════════════════════════════════════════════════════════════════════

#pragma warning disable CA1034

    public record NonTaskFuncScenario(string ParamType, string ReturnType, string ReturnExpr, string ArgLiteral)
    {
        public override string ToString() =>
            $"lambda ({ParamType} x) => {ReturnExpr} (returns {ReturnType}) invoked in sync context";
    }

#pragma warning restore CA1034

    private static readonly (string ParamType, string ReturnType, string ReturnExpr, string ArgLiteral)[] NonTaskFuncVariants =
    [
        ("int", "int", "x + 1", "42"),
        ("string", "string", "x", "\"hello\""),
        ("int", "string", "\"value\"", "5"),
        ("bool", "bool", "x", "true"),
    ];

    public static Gen<NonTaskFuncScenario> GenNonTaskFuncScenario()
    {
        return Gen.Elements(NonTaskFuncVariants).Select(v =>
            new NonTaskFuncScenario(v.ParamType, v.ReturnType, v.ReturnExpr, v.ArgLiteral));
    }

    public static Arbitrary<NonTaskFuncScenario> ArbitraryNonTaskFuncScenario() =>
        GenNonTaskFuncScenario().ToArbitrary();

    private static string BuildNonTaskFuncSource(NonTaskFuncScenario scenario)
    {
        // Non-async lambda: (ParamType x) => ReturnExpr
        // This creates a Func<ParamType, ReturnType> — no Task involved.
        return $$"""
            namespace TestProject;

            public class TestClass
            {
                public static void SyncMethod()
                {
                    var lambda = ({{scenario.ParamType}} x) => {{scenario.ReturnExpr}};
                    var result = lambda({{scenario.ArgLiteral}});
                }
            }
            """;
    }

    [Property(MaxTest = 20, Arbitrary = new[] { typeof(AutoAwaitPreservationPropertyTests) })]
    public Property NonTaskFuncInvocation_ReturnsDirectType(NonTaskFuncScenario scenario)
    {
        var source = BuildNonTaskFuncSource(scenario);
        var (model, _) = BuildModelAndRunPass(source);

        var resultSymbol = FindLocal(model, "result");
        if (resultSymbol is null)
            return false.ToProperty()
                .Label($"Could not find local 'result' in scenario: {scenario}");

        var resultType = resultSymbol.DeclaredType;
        if (resultType is null)
            return false.ToProperty()
                .Label($"result.DeclaredType is null in scenario: {scenario}");

        // PRESERVATION: Non-Task Func return type should be returned directly.
        // No unwrapping should occur since the return type is not Task<T>.
        if (IsStandardTaskType(resultType))
            return false.ToProperty()
                .Label($"REGRESSION: Non-Task Func invocation returned Task<T>. " +
                       $"result type = {resultType.DisplayName}. Scenario: {scenario}");

        return true.ToProperty()
            .Label($"Preserved: non-Task Func returns '{resultType.DisplayName}' for: {scenario}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Preservation Scenario 4: Methods with direct async calls get
    //                          NeedsCSharpAsync = true
    //
    // **Validates: Requirements 3.4**
    //
    // Sync methods that directly call async methods should be marked
    // NeedsCSharpAsync = true. This is the existing propagation behavior.
    // ═══════════════════════════════════════════════════════════════════════

#pragma warning disable CA1034

    public record DirectAsyncSeedScenario(string ReturnType, string ReturnExpr, string CallerName)
    {
        public override string ToString() =>
            $"sync method '{CallerName}' directly calls async method returning {ReturnType}";
    }

#pragma warning restore CA1034

    public static Gen<DirectAsyncSeedScenario> GenDirectAsyncSeedScenario()
    {
        return from variant in Gen.Elements(AsyncReturnVariants)
               from caller in Gen.Elements(CallerNames)
               select new DirectAsyncSeedScenario(variant.Type, variant.Expr, caller);
    }

    public static Arbitrary<DirectAsyncSeedScenario> ArbitraryDirectAsyncSeedScenario() =>
        GenDirectAsyncSeedScenario().ToArbitrary();

    private static string BuildDirectAsyncSeedSource(DirectAsyncSeedScenario scenario)
    {
        return $$"""
            namespace TestProject;

            public class TestClass
            {
                public static async {{scenario.ReturnType}} AsyncHelper()
                {
                    return {{scenario.ReturnExpr}};
                }

                public static void {{scenario.CallerName}}()
                {
                    var result = AsyncHelper();
                }
            }
            """;
    }

    [Property(MaxTest = 20, Arbitrary = new[] { typeof(AutoAwaitPreservationPropertyTests) })]
    public Property MethodWithDirectAsyncCall_HasNeedsCSharpAsync(DirectAsyncSeedScenario scenario)
    {
        var source = BuildDirectAsyncSeedSource(scenario);
        var (model, _) = BuildModelAndRunPass(source);

        var methodSymbol = FindMethod(model, scenario.CallerName);
        if (methodSymbol is null)
            return false.ToProperty()
                .Label($"Could not find method '{scenario.CallerName}' in scenario: {scenario}");

        // PRESERVATION: Sync methods that directly call async methods should
        // be marked NeedsCSharpAsync = true via seed rule 2 of NeedsAsyncPropagationPass.
        if (!methodSymbol.NeedsCSharpAsync)
            return false.ToProperty()
                .Label($"REGRESSION: Method '{scenario.CallerName}' directly calls an async method " +
                       $"but NeedsCSharpAsync is false. Scenario: {scenario}");

        return true.ToProperty()
            .Label($"Preserved: '{scenario.CallerName}' has NeedsCSharpAsync = true for: {scenario}");
    }
}
