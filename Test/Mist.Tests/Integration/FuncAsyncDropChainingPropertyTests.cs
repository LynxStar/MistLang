// Bugfix: func-async-auto-await-fix, Task 7: Comprehensive Property-Based Tests
// Covers all three defects with wider input generation than the exploration tests.
//
// **Validates: Requirements 1.1, 1.2, 1.3, 2.1, 2.2, 2.3, 2.4, 2.5, 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7, 3.8**
//
// Tests verify the FIXED behavior — all properties should PASS on current code.
// Uses in-process compilation only (no process spawning per iteration).

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
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Integration;

/// <summary>
/// Comprehensive property-based tests for the Func/Async/Drop chaining fixes.
/// Generates wider, more varied inputs than the exploration tests (Tasks 1-4):
/// <list type="bullet">
///   <item>Defect 1: Varied Func types with multiple parameters and inner types</item>
///   <item>Defect 2: Mixed async functor + direct async + explicit await combinations</item>
///   <item>Defect 3: Drop chains of depth &gt; 2, mixed task-like and non-task droppable</item>
///   <item>Preservation: Non-bug-condition inputs remain unchanged</item>
/// </list>
///
/// All work runs in-process using compiler APIs — no external process is spawned.
/// </summary>
public class FuncAsyncDropChainingPropertyTests
{
    // ─── Pipeline wiring ─────────────────────────────────────────────────

    private static readonly ILexer Lexer = new MistLexer();
    private static readonly IParser Parser = new MistParser();
    private static readonly SemanticModelBuilder Builder = new();

    // ─── Shared compilation helpers ──────────────────────────────────────

    private static (ISemanticModel Model, ImmutableArray<Diagnostic> Diagnostics) BuildModelAndRunPass(string source)
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

        NeedsAsyncPropagationPass.Run(model);

        return (model, diagnostics.ToImmutableArray());
    }

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

    private static ISymbol? FindLocal(ISemanticModel model, string name)
    {
        return model.AllSymbols
            .FirstOrDefault(s => s.Name == name && s.Kind == SymbolKind.LocalVariable);
    }

    private static Symbol? FindMethod(ISemanticModel model, string name)
    {
        return model.AllSymbols
            .OfType<Symbol>()
            .FirstOrDefault(s => s.Name == name && s.Kind == SymbolKind.Method);
    }

    private static bool IsStandardTaskType(IType? type)
    {
        return type is ConstructedType ct
               && ct.GenericDefinition is ClassType taskClass
               && taskClass.DisplayName == "Task"
               && taskClass.ContainingNamespace == "Standard";
    }

    // ─── Shared type data ────────────────────────────────────────────────

    private static readonly (string Type, string Literal)[] PrimitiveVariants =
    [
        ("int", "42"),
        ("string", "\"hello\""),
        ("bool", "true"),
    ];

    // ═══════════════════════════════════════════════════════════════════════
    // DEFECT 1: Wider Func Type Generation
    //
    // Generate Func types with varying parameter counts, different inner
    // types, and different lambda body expressions across sync/async contexts.
    //
    // **Validates: Requirements 1.1, 2.1, 3.1, 3.3**
    // ═══════════════════════════════════════════════════════════════════════

#pragma warning disable CA1034 // Nested types should not be visible — test scenario records

    public record WiderFuncScenario(
        int ParamCount,
        int ParamTypeIndex,
        bool IsAsyncLambda,
        bool InAsyncContext)
    {
        public string ParamType => PrimitiveVariants[ParamTypeIndex].Type;
        public string ArgLiteral => PrimitiveVariants[ParamTypeIndex].Literal;

        public string ReturnExpr => ParamType switch
        {
            "int" => "p0 + 1",
            "string" => "p0",
            _ => "p0"
        };

        public override string ToString()
        {
            var paramSig = string.Join(", ", Enumerable.Range(0, ParamCount).Select(i => $"{ParamType} p{i}"));
            var context = InAsyncContext ? "async" : "sync";
            var lambdaKind = IsAsyncLambda ? "async" : "sync";
            return $"{lambdaKind} lambda ({paramSig}) => {ReturnExpr} in {context} context";
        }
    }

#pragma warning restore CA1034

    public static Gen<WiderFuncScenario> GenWiderFuncScenario()
    {
        return from paramCount in Gen.Choose(1, 3)
               from paramTypeIdx in Gen.Choose(0, PrimitiveVariants.Length - 1)
               from isAsync in Gen.Elements(true, false)
               from inAsyncCtx in Gen.Elements(true, false)
               select new WiderFuncScenario(paramCount, paramTypeIdx, isAsync, inAsyncCtx);
    }

    public static Arbitrary<WiderFuncScenario> ArbitraryWiderFuncScenario()
    {
        return GenWiderFuncScenario().ToArbitrary();
    }

    private static string BuildWiderFuncSource(WiderFuncScenario scenario)
    {
        var paramList = string.Join(", ", Enumerable.Range(0, scenario.ParamCount)
            .Select(i => $"{scenario.ParamType} p{i}"));
        var argList = string.Join(", ", Enumerable.Range(0, scenario.ParamCount)
            .Select(_ => scenario.ArgLiteral));
        var asyncKeyword = scenario.IsAsyncLambda ? "async " : "";
        var methodAsync = scenario.InAsyncContext ? "async " : "";

        return $$"""
            namespace TestProject;

            public class TestClass
            {
                public static {{methodAsync}}void TestMethod()
                {
                    var lambda = {{asyncKeyword}}({{paramList}}) => {{scenario.ReturnExpr}};
                    var result = lambda({{argList}});
                }
            }
            """;
    }

    /// <summary>
    /// Property: Func type resolution varies correctly by context.
    /// - Async lambda in sync context → unwraps Task&lt;T&gt; to T (Defect 1 fix)
    /// - Async lambda in async context → stays Task&lt;T&gt; (Preservation 3.1)
    /// - Non-async lambda in any context → returns direct type (Preservation 3.3)
    /// </summary>
    [Property(MaxTest = 50, Arbitrary = new[] { typeof(FuncAsyncDropChainingPropertyTests) })]
    public Property FuncTypeResolution_VariesCorrectlyByContext(WiderFuncScenario scenario)
    {
        var source = BuildWiderFuncSource(scenario);
        var (model, _) = BuildModelAndRunPass(source);

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

        var isTaskType = IsStandardTaskType(resultType);

        if(scenario.IsAsyncLambda && !scenario.InAsyncContext)
        {
            // Defect 1 fix: async lambda in sync context should unwrap Task<T> → T
            if(isTaskType)
            {
                return false.ToProperty()
                    .Label($"DEFECT 1 REGRESSION: Async lambda in sync context returned Task<T>. " +
                           $"Type = {resultType.DisplayName}. Scenario: {scenario}");
            }

            return true.ToProperty()
                .Label($"Correctly unwrapped to '{resultType.DisplayName}': {scenario}");
        }

        if(scenario.IsAsyncLambda && scenario.InAsyncContext)
        {
            // Preservation 3.1: async lambda in async context stays Task<T>
            if(!isTaskType)
            {
                return false.ToProperty()
                    .Label($"PRESERVATION REGRESSION 3.1: Async lambda in async context did NOT return Task<T>. " +
                           $"Type = {resultType.DisplayName}. Scenario: {scenario}");
            }

            return true.ToProperty()
                .Label($"Preserved Task<T> in async context: {scenario}");
        }

        // Non-async lambda: should return direct type (never Task<T>)
        if(isTaskType)
        {
            return false.ToProperty()
                .Label($"PRESERVATION REGRESSION 3.3: Non-async lambda returned Task<T>. " +
                       $"Type = {resultType.DisplayName}. Scenario: {scenario}");
        }

        return true.ToProperty()
            .Label($"Non-async lambda returns direct type '{resultType.DisplayName}': {scenario}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // DEFECT 2: Wider Async Method Body Generation
    //
    // Generate methods with varying combinations of async functor expressions,
    // direct async calls, and explicit await — verify NeedsCSharpAsync.
    //
    // **Validates: Requirements 1.2, 2.2, 3.4**
    // ═══════════════════════════════════════════════════════════════════════

#pragma warning disable CA1034

    public record WiderAsyncSeedScenario(
        bool HasAsyncFunctor,
        bool HasDirectAsyncCall,
        bool HasExplicitAwait,
        int AsyncFunctorCount,
        int HelperVariantIndex)
    {
        public string HelperReturnType => SeedHelperVariants[HelperVariantIndex].ReturnType;
        public string HelperBody => SeedHelperVariants[HelperVariantIndex].Body;

        /// <summary>
        /// Whether the method should have NeedsCSharpAsync = true.
        /// Any of the three seed sources should trigger it.
        /// </summary>
        public bool ExpectedNeedsCSharpAsync =>
            HasAsyncFunctor || HasDirectAsyncCall || HasExplicitAwait;

        public override string ToString()
        {
            var parts = new List<string>();
            if(HasAsyncFunctor)
            {
                parts.Add($"{AsyncFunctorCount} async functor(s)");
            }

            if(HasDirectAsyncCall)
            {
                parts.Add("direct async call");
            }

            if(HasExplicitAwait)
            {
                parts.Add("explicit await");
            }

            if(parts.Count == 0)
            {
                parts.Add("no async seeds");
            }

            return $"method with [{string.Join(", ", parts)}] returning {HelperReturnType}";
        }
    }

#pragma warning restore CA1034

    private static readonly (string ReturnType, string Body)[] SeedHelperVariants =
    [
        ("int", "return 42;"),
        ("string", "return \"data\";"),
        ("bool", "return true;"),
    ];

    public static Gen<WiderAsyncSeedScenario> GenWiderAsyncSeedScenario()
    {
        return from hasFunctor in Gen.Elements(true, false)
               from hasDirect in Gen.Elements(true, false)
               from hasAwait in Gen.Elements(true, false)
               where hasFunctor || hasDirect || hasAwait // at least one seed source
               from functorCount in hasFunctor ? Gen.Choose(1, 3) : Gen.Constant(0)
               from helperIdx in Gen.Choose(0, SeedHelperVariants.Length - 1)
               select new WiderAsyncSeedScenario(
                   hasFunctor, hasDirect, hasAwait, functorCount, helperIdx);
    }

    public static Arbitrary<WiderAsyncSeedScenario> ArbitraryWiderAsyncSeedScenario()
    {
        return GenWiderAsyncSeedScenario().ToArbitrary();
    }

    private static string BuildWiderAsyncSeedSource(WiderAsyncSeedScenario scenario)
    {
        var sb = new StringBuilder();
        _ = sb.AppendLine("namespace TestProject;");
        _ = sb.AppendLine();
        _ = sb.AppendLine("public class TestClass");
        _ = sb.AppendLine("{");

        // Sync helper (for async functor)
        _ = sb.AppendLine($"    public static {scenario.HelperReturnType} SyncHelper()");
        _ = sb.AppendLine("    {");
        _ = sb.AppendLine($"        {scenario.HelperBody}");
        _ = sb.AppendLine("    }");
        _ = sb.AppendLine();

        // Async helper (for direct async call / explicit await)
        _ = sb.AppendLine($"    public static async {scenario.HelperReturnType} AsyncHelper()");
        _ = sb.AppendLine("    {");
        _ = sb.AppendLine($"        {scenario.HelperBody}");
        _ = sb.AppendLine("    }");
        _ = sb.AppendLine();

        // Target method: sync, containing various seed sources
        _ = sb.AppendLine("    public static void TargetMethod()");
        _ = sb.AppendLine("    {");

        if(scenario.HasAsyncFunctor)
        {
            for(var i = 0; i < scenario.AsyncFunctorCount; i++)
            {
                _ = sb.AppendLine($"        var functorTask{i} = async SyncHelper();");
            }
        }

        if(scenario.HasDirectAsyncCall)
        {
            _ = sb.AppendLine("        var directResult = AsyncHelper();");
        }

        if(scenario.HasExplicitAwait)
        {
            _ = sb.AppendLine("        var awaitResult = await AsyncHelper();");
        }

        _ = sb.AppendLine("    }");
        _ = sb.AppendLine("}");

        return sb.ToString();
    }

    /// <summary>
    /// Property: Any method with at least one async seed source gets NeedsCSharpAsync.
    /// Covers defect 2 (async functor seeding) AND preservation (direct call seeding).
    /// </summary>
    [Property(MaxTest = 50, Arbitrary = new[] { typeof(FuncAsyncDropChainingPropertyTests) })]
    public Property NeedsCSharpAsync_SetForAllAsyncSeedCombinations(WiderAsyncSeedScenario scenario)
    {
        var source = BuildWiderAsyncSeedSource(scenario);
        var (model, _) = BuildModelAndRunPass(source);

        var methodSymbol = FindMethod(model, "TargetMethod");
        if(methodSymbol is null)
        {
            return false.ToProperty()
                .Label($"Could not find method 'TargetMethod' in scenario: {scenario}");
        }

        if(methodSymbol.NeedsCSharpAsync != scenario.ExpectedNeedsCSharpAsync)
        {
            var label = scenario.ExpectedNeedsCSharpAsync
                ? $"DEFECT 2: Method with async seeds has NeedsCSharpAsync = false. Scenario: {scenario}"
                : $"UNEXPECTED: Method without async seeds has NeedsCSharpAsync = true. Scenario: {scenario}";
            return false.ToProperty().Label(label);
        }

        return true.ToProperty()
            .Label($"NeedsCSharpAsync = {scenario.ExpectedNeedsCSharpAsync} correct for: {scenario}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // DEFECT 3: Wider Drop Chain Generation
    //
    // Generate type hierarchies with varying Drop() return types:
    // - Depth > 2 chains (custom linear → Task → await)
    // - Mix of task-like and non-task droppable in the chain
    // - Void-returning Drop() terminates correctly
    //
    // **Validates: Requirements 1.3, 2.3, 2.4, 2.5, 3.7, 3.8**
    // ═══════════════════════════════════════════════════════════════════════

#pragma warning disable CA1034

    public enum DropChainKind
    {
        /// <summary>Task&lt;Task&lt;T&gt;&gt; — double await chain.</summary>
        NestedTask,

        /// <summary>Custom linear whose Drop() returns Task&lt;T&gt; — await of Drop().</summary>
        CustomToTask,

        /// <summary>Simple void-returning Drop() — no chaining (preservation 3.8).</summary>
        SimpleVoid,
    }

    public record WiderDropChainScenario(
        DropChainKind Kind,
        int InnerTypeIndex,
        int MethodNameIndex,
        int FieldTypeIndex)
    {
        public string InnerType => PrimitiveVariants[InnerTypeIndex].Type;
        public string InnerReturnExpr => PrimitiveVariants[InnerTypeIndex].Literal;
        public string MethodName => DropMethodNames[MethodNameIndex];

        public string FieldType => FieldTypes[FieldTypeIndex];

        public override string ToString()
        {
            return Kind switch
            {
                DropChainKind.NestedTask =>
                    $"Task<Task<{InnerType}>> in '{MethodName}' — needs double await",
                DropChainKind.CustomToTask =>
                    $"linear class Drop()->Task<{InnerType}> in '{MethodName}' — needs await of Drop()",
                DropChainKind.SimpleVoid =>
                    $"linear class Drop()->void ({FieldType} field) in '{MethodName}' — single drop",
                _ => $"Unknown in '{MethodName}'"
            };
        }
    }

#pragma warning restore CA1034

    private static readonly string[] DropMethodNames = ["UseResource", "ProcessData", "HandleRequest", "Cleanup"];
    private static readonly string[] FieldTypes = ["string", "int", "bool"];

    public static Gen<WiderDropChainScenario> GenWiderDropChainScenario()
    {
        return from kind in Gen.Elements(
                   DropChainKind.NestedTask,
                   DropChainKind.CustomToTask,
                   DropChainKind.SimpleVoid)
               from innerIdx in Gen.Choose(0, PrimitiveVariants.Length - 1)
               from methodIdx in Gen.Choose(0, DropMethodNames.Length - 1)
               from fieldIdx in Gen.Choose(0, FieldTypes.Length - 1)
               select new WiderDropChainScenario(kind, innerIdx, methodIdx, fieldIdx);
    }

    public static Arbitrary<WiderDropChainScenario> ArbitraryWiderDropChainScenario()
    {
        return GenWiderDropChainScenario().ToArbitrary();
    }

    private static string BuildWiderDropChainSource(WiderDropChainScenario scenario)
    {
        return scenario.Kind switch
        {
            DropChainKind.NestedTask => BuildNestedTaskChainSource(scenario),
            DropChainKind.CustomToTask => BuildCustomToTaskChainSource(scenario),
            DropChainKind.SimpleVoid => BuildSimpleVoidDropSource(scenario),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
    }

    private static string BuildNestedTaskChainSource(WiderDropChainScenario scenario)
    {
        return $$"""
            using Standard;

            namespace TestProject;

            public class TestClass
            {
                public static async {{scenario.InnerType}} AsyncHelper()
                {
                    return {{scenario.InnerReturnExpr}};
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

    private static string BuildCustomToTaskChainSource(WiderDropChainScenario scenario)
    {
        return $$"""
            using Standard;

            namespace TestProject;

            public linear class CustomBox
            {
                Task<{{scenario.InnerType}}> Inner;

                public CustomBox(Task<{{scenario.InnerType}}> inner)
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
                    return {{scenario.InnerReturnExpr}};
                }

                public static void {{scenario.MethodName}}()
                {
                    var box = new CustomBox(async AsyncHelper());
                    Console.WriteLine("work");
                }
            }
            """;
    }

    private static string BuildSimpleVoidDropSource(WiderDropChainScenario scenario)
    {
        var fieldInit = scenario.FieldType switch
        {
            "string" => "\"data\"",
            "int" => "99",
            "bool" => "false",
            _ => "0"
        };

        return $$"""
            using Standard;

            namespace TestProject;

            public linear class SimpleResource
            {
                {{scenario.FieldType}} Value;

                public SimpleResource({{scenario.FieldType}} value)
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
                public static void {{scenario.MethodName}}()
                {
                    var resource = new SimpleResource({{fieldInit}});
                    Console.WriteLine("work");
                }
            }
            """;
    }

    /// <summary>
    /// Property: Drop chaining is correct for each drop chain kind.
    /// - NestedTask: emitted C# contains double-await pattern
    /// - CustomToTask: emitted C# awaits the Drop() return value
    /// - SimpleVoid: emitted C# has single drop without chaining (preservation)
    /// </summary>
    [Property(MaxTest = 50, Arbitrary = new[] { typeof(FuncAsyncDropChainingPropertyTests) })]
    public Property DropChainEmission_CorrectForKind(WiderDropChainScenario scenario)
    {
        var source = BuildWiderDropChainSource(scenario);
        var (succeeded, csContent, errorText) = BuildAndEmit(source);

        if(!succeeded)
        {
            return false.ToProperty()
                .Label($"Compilation/emission failed for scenario: {scenario}\n{errorText}");
        }

        return scenario.Kind switch
        {
            DropChainKind.NestedTask => VerifyNestedTaskChain(csContent, scenario),
            DropChainKind.CustomToTask => VerifyCustomToTaskChain(csContent, scenario),
            DropChainKind.SimpleVoid => VerifySimpleVoidDrop(csContent, scenario),
            _ => false.ToProperty().Label("Unknown kind")
        };
    }

    private static Property VerifyNestedTaskChain(string csContent, WiderDropChainScenario scenario)
    {
        // After fix: `await (await nested);` — the inner Task is also awaited
        if(!csContent.Contains("await (await"))
        {
            return false.ToProperty()
                .Label($"DEFECT 3 REGRESSION: Nested Task<Task<T>> does not emit double-await. " +
                       $"Expected 'await (await' pattern. Scenario: {scenario}\n" +
                       $"Emitted:\n{ExtractFinallyLines(csContent, "nested")}");
        }

        return true.ToProperty()
            .Label($"Correctly emits double-await for nested task: {scenario}");
    }

    private static Property VerifyCustomToTaskChain(string csContent, WiderDropChainScenario scenario)
    {
        // After fix: `await box.Drop();` — the Task<T> returned by Drop() is awaited
        var hasAwaitedDrop = csContent.Contains("await box.Drop()")
                             || csContent.Contains("await (box.Drop()");
        if(!hasAwaitedDrop)
        {
            return false.ToProperty()
                .Label($"DEFECT 3 REGRESSION: Custom type Drop()->Task<T> not awaited. " +
                       $"Expected 'await box.Drop()' pattern. Scenario: {scenario}\n" +
                       $"Emitted:\n{ExtractFinallyLines(csContent, "box")}");
        }

        return true.ToProperty()
            .Label($"Correctly awaits Drop() return for custom type: {scenario}");
    }

    private static Property VerifySimpleVoidDrop(string csContent, WiderDropChainScenario scenario)
    {
        // Preservation 3.8: void-returning Drop() emits single drop, no chaining
        var hasSimpleDrop = csContent.Contains("resource.Drop();");
        if(!hasSimpleDrop)
        {
            return false.ToProperty()
                .Label($"PRESERVATION REGRESSION 3.8: Void-returning Drop() missing 'resource.Drop();'. " +
                       $"Scenario: {scenario}");
        }

        // Should NOT have await applied to the void Drop()
        var hasAwaitedDrop = csContent.Contains("await resource.Drop()");
        if(hasAwaitedDrop)
        {
            return false.ToProperty()
                .Label($"PRESERVATION REGRESSION 3.8: Void-returning Drop() was incorrectly awaited. " +
                       $"Scenario: {scenario}");
        }

        return true.ToProperty()
            .Label($"Preserved: simple void Drop() emits single 'resource.Drop();': {scenario}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // PRESERVATION: Direct Async Call Still Unwraps in Sync Context
    //
    // **Validates: Requirements 3.2, 3.4**
    // ═══════════════════════════════════════════════════════════════════════

#pragma warning disable CA1034

    public record DirectAsyncPreservationScenario(
        int ReturnTypeIndex,
        int ParamCount)
    {
        public string ReturnType => PrimitiveVariants[ReturnTypeIndex].Type;
        public string ReturnExpr => PrimitiveVariants[ReturnTypeIndex].Literal;

        public override string ToString()
        {
            return $"direct async call with {ParamCount} params returning {ReturnType}";
        }
    }

#pragma warning restore CA1034

    public static Gen<DirectAsyncPreservationScenario> GenDirectAsyncPreservation()
    {
        return from returnIdx in Gen.Choose(0, PrimitiveVariants.Length - 1)
               from paramCount in Gen.Choose(0, 2)
               select new DirectAsyncPreservationScenario(returnIdx, paramCount);
    }

    public static Arbitrary<DirectAsyncPreservationScenario> ArbitraryDirectAsyncPreservation()
    {
        return GenDirectAsyncPreservation().ToArbitrary();
    }

    private static string BuildDirectAsyncPreservationSource(DirectAsyncPreservationScenario scenario)
    {
        var paramDecl = string.Join(", ", Enumerable.Range(0, scenario.ParamCount)
            .Select(i => $"int arg{i}"));
        var argList = string.Join(", ", Enumerable.Range(0, scenario.ParamCount)
            .Select(_ => "1"));
        var callArgs = $"({argList})";

        return $$"""
            namespace TestProject;

            public class TestClass
            {
                public static async {{scenario.ReturnType}} AsyncHelper({{paramDecl}})
                {
                    return {{scenario.ReturnExpr}};
                }

                public static void Caller()
                {
                    var result = AsyncHelper{{callArgs}};
                }
            }
            """;
    }

    /// <summary>
    /// Property: Direct async method calls in sync context continue unwrapping Task&lt;T&gt; → T.
    /// Also verifies NeedsCSharpAsync is set for these methods (Preservation 3.4).
    /// </summary>
    [Property(MaxTest = 30, Arbitrary = new[] { typeof(FuncAsyncDropChainingPropertyTests) })]
    public Property DirectAsyncCall_StillUnwrapsAndSeedsNeedsAsync(DirectAsyncPreservationScenario scenario)
    {
        var source = BuildDirectAsyncPreservationSource(scenario);
        var (model, _) = BuildModelAndRunPass(source);

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

        // Preservation 3.2: direct async call unwraps
        if(IsStandardTaskType(resultType))
        {
            return false.ToProperty()
                .Label($"PRESERVATION REGRESSION 3.2: Direct async call in sync context returned Task<T>. " +
                       $"Type = {resultType.DisplayName}. Scenario: {scenario}");
        }

        // Preservation 3.4: method gets NeedsCSharpAsync
        var callerSymbol = FindMethod(model, "Caller");
        if(callerSymbol is not null && !callerSymbol.NeedsCSharpAsync)
        {
            return false.ToProperty()
                .Label($"PRESERVATION REGRESSION 3.4: Method with direct async call missing NeedsCSharpAsync. " +
                       $"Scenario: {scenario}");
        }

        return true.ToProperty()
            .Label($"Preserved: direct async call unwraps and seeds NeedsCSharpAsync: {scenario}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // PRESERVATION: Explicit Capture Does NOT Trigger Recursive Drop
    //
    // **Validates: Requirements 3.7**
    // ═══════════════════════════════════════════════════════════════════════

#pragma warning disable CA1034

    public record ExplicitCapturePreservationScenario(
        int InnerTypeIndex,
        int ClassNameIndex)
    {
        public string InnerType => PrimitiveVariants[InnerTypeIndex].Type;
        public string ReturnExpr => PrimitiveVariants[InnerTypeIndex].Literal;
        public string ClassName => CaptureClassNames[ClassNameIndex];

        public override string ToString()
        {
            return $"explicit capture of {ClassName}.Drop() returning {InnerType}";
        }
    }

#pragma warning restore CA1034

    private static readonly string[] CaptureClassNames = ["Wrapper", "Container", "Holder"];

    public static Gen<ExplicitCapturePreservationScenario> GenExplicitCapturePreservation()
    {
        return from innerIdx in Gen.Choose(0, PrimitiveVariants.Length - 1)
               from classIdx in Gen.Choose(0, CaptureClassNames.Length - 1)
               select new ExplicitCapturePreservationScenario(innerIdx, classIdx);
    }

    public static Arbitrary<ExplicitCapturePreservationScenario> ArbitraryExplicitCapturePreservation()
    {
        return GenExplicitCapturePreservation().ToArbitrary();
    }

    private static string BuildExplicitCapturePreservationSource(ExplicitCapturePreservationScenario scenario)
    {
        return $$"""
            using Standard;

            namespace TestProject;

            public linear class {{scenario.ClassName}}
            {
                {{scenario.InnerType}} Value;

                public {{scenario.ClassName}}({{scenario.InnerType}} value)
                {
                    this.Value = value;
                }

                public {{scenario.InnerType}} Drop()
                {
                    Console.WriteLine("dropped");
                    return Value;
                }
            }

            public class TestClass
            {
                public static void DoWork()
                {
                    var outer = new {{scenario.ClassName}}({{scenario.ReturnExpr}});
                    var captured = outer.Drop();
                    Console.WriteLine(captured);
                }
            }
            """;
    }

    /// <summary>
    /// Property: Explicit capture of Drop() return does NOT trigger recursive chaining.
    /// The outer variable's linear obligation is consumed by the explicit Drop() call.
    /// </summary>
    [Property(MaxTest = 20, Arbitrary = new[] { typeof(FuncAsyncDropChainingPropertyTests) })]
    public Property ExplicitCapture_NoRecursiveChaining(ExplicitCapturePreservationScenario scenario)
    {
        var source = BuildExplicitCapturePreservationSource(scenario);
        var (succeeded, csContent, errorText) = BuildAndEmit(source);

        if(!succeeded)
        {
            return false.ToProperty()
                .Label($"Compilation/emission failed for scenario: {scenario}\n{errorText}");
        }

        // The explicit Drop() call consumes the linear obligation.
        // There should be NO scope-exit drop for 'outer' in a finally block.
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

            if(inFinally && lines[i].Contains("outer.Drop()"))
            {
                outerDropInFinally = true;
            }
        }

        if(outerDropInFinally)
        {
            return false.ToProperty()
                .Label($"PRESERVATION REGRESSION 3.7: Explicit capture still triggered scope-exit drop " +
                       $"for 'outer' in finally block. Scenario: {scenario}");
        }

        return true.ToProperty()
            .Label($"Preserved: explicit capture consumes obligation, no scope-exit drop: {scenario}");
    }

    // ─── Utility ─────────────────────────────────────────────────────────

    private static string ExtractFinallyLines(string csContent, string localName)
    {
        var lines = csContent.Split('\n');
        var sb = new StringBuilder();
        for(var i = 0; i < lines.Length; i++)
        {
            if(lines[i].Contains("finally"))
            {
                for(var j = i; j < Math.Min(i + 6, lines.Length); j++)
                {
                    _ = sb.AppendLine(lines[j]);
                    if(lines[j].Contains(localName))
                    {
                        break;
                    }
                }
                break;
            }
        }

        return sb.Length > 0 ? sb.ToString() : "(finally block not found)";
    }
}