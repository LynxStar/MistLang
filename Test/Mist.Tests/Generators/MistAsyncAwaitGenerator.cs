// Generators for Mist async/await source strings.
// Produces random but structurally valid programs containing async modifiers,
// async functor expressions, and await expressions for property-based testing.
// Feature: structured-concurrency, Property 1: Async/Await Syntax Round-Trip
// **Validates: Requirements 12.3, 12.4, 12.5, 12.6, 12.7**

using FsCheck;
using FsCheck.Fluent;

namespace Mist.Tests.Generators;

/// <summary>
/// Metadata about a generated async/await source string for property-based testing.
/// Carries the complete Mist program source text along with a human-readable description.
/// </summary>
public sealed class AsyncAwaitSourceScenario
{
    /// <summary>The complete Mist program source string containing async/await constructs.</summary>
    public string Source { get; }

    /// <summary>Human-readable description of the scenario kind.</summary>
    public string Description { get; }

    public AsyncAwaitSourceScenario(string source, string description)
    {
        Source = source;
        Description = description;
    }

    public override string ToString()
    {
        return Description;
    }
}

/// <summary>
/// FsCheck generators for Mist programs containing async/await constructs.
/// Generates random valid programs covering:
/// <list type="bullet">
///   <item>Methods with the <c>async</c> modifier (instance, static, public, combinations)</item>
///   <item><c>async</c> functor expressions on method invocations</item>
///   <item><c>await</c> expressions on expressions</item>
///   <item>Combinations of async modifier + await/async functor in method bodies</item>
///   <item>Multiple methods with varying modifier combinations</item>
/// </list>
/// Each generated program is a complete, parseable Mist source file with namespace,
/// class, and method wrapping.
/// </summary>
public static class MistAsyncAwaitGenerator
{
    // ═══════════════════════════════════════════════════════════════════════
    // Name pools
    // ═══════════════════════════════════════════════════════════════════════

    private static readonly string[] ClassNames =
        ["Worker", "Service", "Handler", "Processor", "Runner"];

    private static readonly string[] MethodNames =
        ["Compute", "Fetch", "Process", "Execute", "Load", "Send", "Transform"];

    private static readonly string[] VarNames =
        ["result", "value", "data", "output", "item"];

    private static readonly string[] ReturnTypes =
        ["int", "string", "bool"];

    // ═══════════════════════════════════════════════════════════════════════
    // Modifier generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Generates a set of modifiers that includes async.</summary>
    private static Gen<string> GenAsyncModifiers()
    {
        return Gen.OneOf(
            Gen.Constant("async"),
            Gen.Constant("public async"),
            Gen.Constant("public static async"),
            Gen.Constant("static async"));
    }

    /// <summary>Generates modifiers without async for sync methods.</summary>
    private static Gen<string> GenSyncModifiers()
    {
        return Gen.OneOf(
            Gen.Constant("public"),
            Gen.Constant("public static"),
            Gen.Constant("static"),
            Gen.Constant(""));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Expression generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Generates a simple method call expression (for use as operand of async/await).</summary>
    private static Gen<string> GenMethodCall()
    {
        return from name in Gen.Elements(MethodNames)
               select $"{name}()";
    }

    /// <summary>Generates an async functor expression: async MethodCall()</summary>
    private static Gen<string> GenAsyncFunctorExpression()
    {
        return from call in GenMethodCall()
               select $"async {call}";
    }

    /// <summary>Generates an await expression: await expr</summary>
    private static Gen<string> GenAwaitExpression()
    {
        return Gen.OneOf(
            from call in GenMethodCall()
            select $"await {call}",
            from varName in Gen.Elements(VarNames)
            select $"await {varName}");
    }

    /// <summary>Generates a simple expression (literal or variable).</summary>
    private static Gen<string> GenSimpleExpression()
    {
        return Gen.OneOf(
            Gen.Constant("42"),
            Gen.Constant("\"hello\""),
            Gen.Constant("true"),
            from name in Gen.Elements(VarNames)
            select name);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Statement generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Generates a variable declaration with an async functor expression.</summary>
    private static Gen<string> GenAsyncFunctorVarDecl()
    {
        return from varName in Gen.Elements(VarNames)
               from asyncExpr in GenAsyncFunctorExpression()
               select $"var {varName} = {asyncExpr};";
    }

    /// <summary>Generates a variable declaration with an await expression.</summary>
    private static Gen<string> GenAwaitVarDecl()
    {
        return from varName in Gen.Elements(VarNames)
               from awaitExpr in GenAwaitExpression()
               select $"var {varName} = {awaitExpr};";
    }

    /// <summary>Generates a standalone async functor expression statement.</summary>
    private static Gen<string> GenAsyncFunctorStatement()
    {
        return from asyncExpr in GenAsyncFunctorExpression()
               select $"{asyncExpr};";
    }

    /// <summary>Generates a standalone await expression statement.</summary>
    private static Gen<string> GenAwaitStatement()
    {
        return from awaitExpr in GenAwaitExpression()
               select $"{awaitExpr};";
    }

    /// <summary>Generates a simple variable declaration statement.</summary>
    private static Gen<string> GenSimpleVarDecl()
    {
        return from varName in Gen.Elements(VarNames)
               from expr in GenSimpleExpression()
               select $"var {varName} = {expr};";
    }

    /// <summary>Generates any statement that can appear in an async method body.</summary>
    private static Gen<string> GenAsyncBodyStatement()
    {
        return Gen.OneOf(
            GenAsyncFunctorVarDecl(),
            GenAwaitVarDecl(),
            GenAsyncFunctorStatement(),
            GenAwaitStatement(),
            GenSimpleVarDecl());
    }

    /// <summary>Generates any statement that can appear in a sync method body (no await/async functor needed but allowed).</summary>
    private static Gen<string> GenSyncBodyStatement()
    {
        return Gen.OneOf(
            GenSimpleVarDecl(),
            GenAsyncFunctorVarDecl(),
            GenAwaitVarDecl(),
            GenAwaitStatement());
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Method generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Generates an async method with a body containing async/await statements.</summary>
    private static Gen<string> GenAsyncMethod()
    {
        return from modifiers in GenAsyncModifiers()
               from returnType in Gen.Elements(ReturnTypes)
               from name in Gen.Elements(MethodNames)
               from stmtCount in Gen.Choose(1, 3)
               from stmts in Gen.ArrayOf(GenAsyncBodyStatement(), stmtCount)
               let body = string.Join("\n        ", stmts)
               select $"    {modifiers} {returnType} {name}() {{\n        {body}\n    }}";
    }

    /// <summary>Generates a sync method that uses async functor or await expressions.</summary>
    private static Gen<string> GenSyncMethodWithAsyncUsage()
    {
        return from modifiers in GenSyncModifiers()
               from returnType in Gen.Elements(ReturnTypes)
               from name in Gen.Elements(MethodNames)
               from stmtCount in Gen.Choose(1, 3)
               from stmts in Gen.ArrayOf(GenSyncBodyStatement(), stmtCount)
               let modPrefix = modifiers.Length > 0 ? $"{modifiers} " : ""
               let body = string.Join("\n        ", stmts)
               select $"    {modPrefix}{returnType} {name}() {{\n        {body}\n    }}";
    }

    /// <summary>Generates a void async method.</summary>
    private static Gen<string> GenAsyncVoidMethod()
    {
        return from modifiers in GenAsyncModifiers()
               from name in Gen.Elements(MethodNames)
               from stmtCount in Gen.Choose(1, 3)
               from stmts in Gen.ArrayOf(GenAsyncBodyStatement(), stmtCount)
               let body = string.Join("\n        ", stmts)
               select $"    {modifiers} void {name}() {{\n        {body}\n    }}";
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Scenario generators (complete source programs)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a class with async methods.
    /// </summary>
    public static Gen<AsyncAwaitSourceScenario> GenClassWithAsyncMethods()
    {
        return from className in Gen.Elements(ClassNames)
               from methodCount in Gen.Choose(1, 3)
               from methods in Gen.ArrayOf(GenAsyncMethod(), methodCount)
               let members = string.Join("\n\n", methods)
               let source = $"namespace TestNs;\n\nclass {className} {{\n{members}\n}}\n"
               select new AsyncAwaitSourceScenario(source,
                   $"class {className} with {methodCount} async methods");
    }

    /// <summary>
    /// Generates a class with a mix of async and sync methods using async/await.
    /// </summary>
    public static Gen<AsyncAwaitSourceScenario> GenClassWithMixedMethods()
    {
        return from className in Gen.Elements(ClassNames)
               from asyncCount in Gen.Choose(1, 2)
               from syncCount in Gen.Choose(1, 2)
               from asyncMethods in Gen.ArrayOf(
                   Gen.OneOf(GenAsyncMethod(), GenAsyncVoidMethod()), asyncCount)
               from syncMethods in Gen.ArrayOf(GenSyncMethodWithAsyncUsage(), syncCount)
               let allMethods = asyncMethods.Concat(syncMethods).ToArray()
               let members = string.Join("\n\n", allMethods)
               let source = $"namespace TestNs;\n\nclass {className} {{\n{members}\n}}\n"
               select new AsyncAwaitSourceScenario(source,
                   $"class {className} with {asyncCount} async + {syncCount} sync methods");
    }

    /// <summary>
    /// Generates a class with async void methods.
    /// </summary>
    public static Gen<AsyncAwaitSourceScenario> GenClassWithAsyncVoidMethods()
    {
        return from className in Gen.Elements(ClassNames)
               from methodCount in Gen.Choose(1, 3)
               from methods in Gen.ArrayOf(GenAsyncVoidMethod(), methodCount)
               let members = string.Join("\n\n", methods)
               let source = $"namespace TestNs;\n\nclass {className} {{\n{members}\n}}\n"
               select new AsyncAwaitSourceScenario(source,
                   $"class {className} with {methodCount} async void methods");
    }

    /// <summary>
    /// Generates a program with nested async/await expressions (e.g., await async Foo()).
    /// </summary>
    public static Gen<AsyncAwaitSourceScenario> GenNestedAsyncAwait()
    {
        var genNestedStmt = Gen.OneOf(
            from call in GenMethodCall()
            select $"var result = await async {call};",
            from call in GenMethodCall()
            select $"var result = async {call};",
            from call in GenMethodCall()
            select $"await {call};");

        return from className in Gen.Elements(ClassNames)
               from name in Gen.Elements(MethodNames)
               from stmtCount in Gen.Choose(1, 3)
               from stmts in Gen.ArrayOf(genNestedStmt, stmtCount)
               let body = string.Join("\n        ", stmts)
               let source = $"namespace TestNs;\n\nclass {className} {{\n    async int {name}() {{\n        {body}\n    }}\n}}\n"
               select new AsyncAwaitSourceScenario(source,
                   $"class {className} with nested async/await in {name}");
    }

    /// <summary>
    /// Generates a program with async modifier combined with other modifiers.
    /// </summary>
    public static Gen<AsyncAwaitSourceScenario> GenAsyncWithVariousModifiers()
    {
        var modifierCombinations = new[]
        {
            "public async",
            "public static async",
            "static async",
            "async",
        };

        return from className in Gen.Elements(ClassNames)
               from modCount in Gen.Choose(1, 4)
               from modIndices in Gen.ArrayOf(Gen.Choose(0, modifierCombinations.Length - 1), modCount)
               from names in Gen.ArrayOf(Gen.Elements(MethodNames), modCount)
               from returnTypes in Gen.ArrayOf(Gen.Elements(ReturnTypes), modCount)
               let methods = modIndices.Zip(names, returnTypes).Select((tuple, _) =>
               {
                   var (modIdx, name, retType) = tuple;
                   var mods = modifierCombinations[modIdx];
                   return $"    {mods} {retType} {name}() {{\n        var x = 42;\n    }}";
               }).ToArray()
               let members = string.Join("\n\n", methods)
               let source = $"namespace TestNs;\n\nclass {className} {{\n{members}\n}}\n"
               select new AsyncAwaitSourceScenario(source,
                   $"class {className} with various async modifier combinations");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Combined generator
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a random async/await source scenario covering all variants.
    /// </summary>
    public static Gen<AsyncAwaitSourceScenario> GenAsyncAwaitSource()
    {
        return Gen.OneOf(
            GenClassWithAsyncMethods(),
            GenClassWithMixedMethods(),
            GenClassWithAsyncVoidMethods(),
            GenNestedAsyncAwait(),
            GenAsyncWithVariousModifiers());
    }

    // ═══════════════════════════════════════════════════════════════════════
    // FsCheck Arbitrary registration
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// FsCheck Arbitrary for <see cref="AsyncAwaitSourceScenario"/>.
    /// Register with <c>[Property(Arbitrary = new[] { typeof(MistAsyncAwaitGenerator) })]</c>.
    /// </summary>
    public static Arbitrary<AsyncAwaitSourceScenario> ArbitraryAsyncAwaitSourceScenario()
    {
        return GenAsyncAwaitSource().ToArbitrary();
    }
}