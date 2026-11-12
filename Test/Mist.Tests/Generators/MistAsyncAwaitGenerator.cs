// Feature: structured-concurrency, Property 1: Async/Await Syntax Round-Trip
// **Validates: Requirements 12.3, 12.4, 12.5, 12.6, 12.7**

using FsCheck;
using FsCheck.Fluent;

namespace Mist.Tests.Generators;

public sealed class AsyncAwaitSourceScenario
{
    public string Source { get; }

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

public static class MistAsyncAwaitGenerator
{
    private static readonly string[] ClassNames =
        ["Worker", "Service", "Handler", "Processor", "Runner"];

    private static readonly string[] MethodNames =
        ["Compute", "Fetch", "Process", "Execute", "Load", "Send", "Transform"];

    private static readonly string[] VarNames =
        ["result", "value", "data", "output", "item"];

    private static readonly string[] ReturnTypes =
        ["int", "string", "bool"];

    private static Gen<string> GenAsyncModifiers()
    {
        return Gen.OneOf(
            Gen.Constant("async"),
            Gen.Constant("public async"),
            Gen.Constant("public static async"),
            Gen.Constant("static async"));
    }

    private static Gen<string> GenSyncModifiers()
    {
        return Gen.OneOf(
            Gen.Constant("public"),
            Gen.Constant("public static"),
            Gen.Constant("static"),
            Gen.Constant(""));
    }

    private static Gen<string> GenMethodCall()
    {
        return from name in Gen.Elements(MethodNames)
               select $"{name}()";
    }

    private static Gen<string> GenAsyncFunctorExpression()
    {
        return from call in GenMethodCall()
               select $"async {call}";
    }

    private static Gen<string> GenAwaitExpression()
    {
        return Gen.OneOf(
            from call in GenMethodCall()
            select $"await {call}",
            from varName in Gen.Elements(VarNames)
            select $"await {varName}");
    }

    private static Gen<string> GenSimpleExpression()
    {
        return Gen.OneOf(
            Gen.Constant("42"),
            Gen.Constant("\"hello\""),
            Gen.Constant("true"),
            from name in Gen.Elements(VarNames)
            select name);
    }

    private static Gen<string> GenAsyncFunctorVarDecl()
    {
        return from varName in Gen.Elements(VarNames)
               from asyncExpr in GenAsyncFunctorExpression()
               select $"var {varName} = {asyncExpr};";
    }

    private static Gen<string> GenAwaitVarDecl()
    {
        return from varName in Gen.Elements(VarNames)
               from awaitExpr in GenAwaitExpression()
               select $"var {varName} = {awaitExpr};";
    }

    private static Gen<string> GenAsyncFunctorStatement()
    {
        return from asyncExpr in GenAsyncFunctorExpression()
               select $"{asyncExpr};";
    }

    private static Gen<string> GenAwaitStatement()
    {
        return from awaitExpr in GenAwaitExpression()
               select $"{awaitExpr};";
    }

    private static Gen<string> GenSimpleVarDecl()
    {
        return from varName in Gen.Elements(VarNames)
               from expr in GenSimpleExpression()
               select $"var {varName} = {expr};";
    }

    private static Gen<string> GenAsyncBodyStatement()
    {
        return Gen.OneOf(
            GenAsyncFunctorVarDecl(),
            GenAwaitVarDecl(),
            GenAsyncFunctorStatement(),
            GenAwaitStatement(),
            GenSimpleVarDecl());
    }

    private static Gen<string> GenSyncBodyStatement()
    {
        return Gen.OneOf(
            GenSimpleVarDecl(),
            GenAsyncFunctorVarDecl(),
            GenAwaitVarDecl(),
            GenAwaitStatement());
    }

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

    private static Gen<string> GenAsyncVoidMethod()
    {
        return from modifiers in GenAsyncModifiers()
               from name in Gen.Elements(MethodNames)
               from stmtCount in Gen.Choose(1, 3)
               from stmts in Gen.ArrayOf(GenAsyncBodyStatement(), stmtCount)
               let body = string.Join("\n        ", stmts)
               select $"    {modifiers} void {name}() {{\n        {body}\n    }}";
    }

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

    public static Gen<AsyncAwaitSourceScenario> GenAsyncAwaitSource()
    {
        return Gen.OneOf(
            GenClassWithAsyncMethods(),
            GenClassWithMixedMethods(),
            GenClassWithAsyncVoidMethods(),
            GenNestedAsyncAwait(),
            GenAsyncWithVariousModifiers());
    }

    public static Arbitrary<AsyncAwaitSourceScenario> ArbitraryAsyncAwaitSourceScenario()
    {
        return GenAsyncAwaitSource().ToArbitrary();
    }
}