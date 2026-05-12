// Feature: ref-parameters, Property 12: C# Emission Preserves Ref Annotations
// **Validates: Requirements 8.1, 8.2, 8.3, 8.4, 8.5, 8.6**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Semantics.Model;

namespace Mist.Tests.Backend;

// ─── Scenario types ─────────────────────────────────────────────────────────

#pragma warning disable CA1034

/// <summary>
/// Scenario for verifying that ref parameters emit the C# <c>ref</c> keyword.
/// </summary>
public sealed record RefParameterEmissionScenario(string Source, string ParamName, string TypeName, string Description)
{
    public override string ToString() => Description;
}

/// <summary>
/// Scenario for verifying that ref return types emit the C# <c>ref</c> keyword.
/// </summary>
public sealed record RefReturnEmissionScenario(string Source, string MethodName, string Description)
{
    public override string ToString() => Description;
}

/// <summary>
/// Scenario for verifying that ref readonly return types emit <c>ref readonly</c>.
/// </summary>
public sealed record RefReadonlyReturnEmissionScenario(string Source, string MethodName, string Description)
{
    public override string ToString() => Description;
}

/// <summary>
/// Scenario for verifying that ref local declarations emit the C# <c>ref</c> keyword.
/// </summary>
public sealed record RefLocalEmissionScenario(string Source, string VarName, string TypeName, bool IsReadonly, string Description)
{
    public override string ToString() => Description;
}

/// <summary>
/// Scenario for verifying that ref arguments emit the C# <c>ref</c> keyword at call sites.
/// </summary>
public sealed record RefArgumentEmissionScenario(string Source, string Description)
{
    public override string ToString() => Description;
}

/// <summary>
/// Scenario for verifying that return ref statements emit <c>return ref</c>.
/// </summary>
public sealed record ReturnRefEmissionScenario(string Source, string Description)
{
    public override string ToString() => Description;
}

#pragma warning restore CA1034

// ─── Property Tests ─────────────────────────────────────────────────────────

/// <summary>
/// Property-based tests for C# emission of ref annotations.
///
/// Property 12 from the ref-parameters design document.
/// Uses in-process compilation via a shared CompilerDriver instance with
/// EmitToDisk: false to capture emitted C# content in memory.
///
/// **Validates: Requirements 8.1, 8.2, 8.3, 8.4, 8.5, 8.6**
/// </summary>
public sealed class RefEmissionPropertyTests : IDisposable
{
    private readonly string _outputDir;

    public RefEmissionPropertyTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), "MistRefEmissionPBT_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_outputDir))
        {
            try { Directory.Delete(_outputDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ─── Shared driver (reused across tests) ─────────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    // ─── Generators ─────────────────────────────────────────────────────

    private static readonly string[] NumericTypes = ["int", "double", "float"];
    private static readonly string[] ParamNames = ["x", "y", "z", "val", "item"];
    private static readonly string[] MethodNames = ["DoWork", "Process", "Compute", "Transform"];
    private static readonly string[] FieldNames = ["stored", "cached", "data", "value"];
    private static readonly string[] VarNames = ["a", "b", "c", "local"];

    /// <summary>
    /// Returns a valid default literal for the given Mist type.
    /// </summary>
    private static string DefaultValueFor(string typeName) => typeName switch
    {
        "bool" => "false",
        _ => "0",
    };

    // ── Property 12a: Ref parameter emission ────────────────────────────

    public static Arbitrary<RefParameterEmissionScenario> ArbitraryRefParameterEmission()
    {
        var gen =
            from typeName in Gen.Elements(NumericTypes)
            from paramName in Gen.Elements(ParamNames)
            from methodName in Gen.Elements(MethodNames)
            let source = $$"""
                class Program {
                    public static void {{methodName}}(ref {{typeName}} {{paramName}}) {
                    }
                    public static void Main() {
                    }
                }
                """
            select new RefParameterEmissionScenario(source, paramName, typeName,
                $"ref {typeName} {paramName} in {methodName}");

        return gen.ToArbitrary();
    }

    // ── Property 12b: Ref return emission ───────────────────────────────

    public static Arbitrary<RefReturnEmissionScenario> ArbitraryRefReturnEmission()
    {
        var gen =
            from typeName in Gen.Elements(NumericTypes)
            from fieldName in Gen.Elements(FieldNames)
            from methodName in Gen.Elements(MethodNames)
            let source = $$"""
                class Container {
                    {{typeName}} {{fieldName}} = 0;
                    public ref {{typeName}} {{methodName}}() {
                        return ref this.{{fieldName}};
                    }
                }
                class Program {
                    public static void Main() {
                    }
                }
                """
            select new RefReturnEmissionScenario(source, methodName,
                $"ref {typeName} {methodName}() returning field {fieldName}");

        return gen.ToArbitrary();
    }

    // ── Property 12c: Ref readonly return emission ──────────────────────

    public static Arbitrary<RefReadonlyReturnEmissionScenario> ArbitraryRefReadonlyReturnEmission()
    {
        var gen =
            from typeName in Gen.Elements(NumericTypes)
            from fieldName in Gen.Elements(FieldNames)
            from methodName in Gen.Elements(MethodNames)
            let source = $$"""
                class Container {
                    {{typeName}} {{fieldName}} = 0;
                    public readonly ref {{typeName}} {{methodName}}() {
                        return ref this.{{fieldName}};
                    }
                }
                class Program {
                    public static void Main() {
                    }
                }
                """
            select new RefReadonlyReturnEmissionScenario(source, methodName,
                $"ref readonly {typeName} {methodName}() returning field {fieldName}");

        return gen.ToArbitrary();
    }

    // ── Property 12d: Ref local emission ────────────────────────────────

    public static Arbitrary<RefLocalEmissionScenario> ArbitraryRefLocalEmission()
    {
        // Only bare `ref` locals are ref locals. `readonly ref` on locals is an ownership
        // lend until the grammar supports disambiguation via ref on initializer.
        var refLocalGen =
            from typeName in Gen.Elements(NumericTypes)
            from varName in Gen.Elements(VarNames)
            let defaultVal = DefaultValueFor(typeName)
            let source = $$"""
                class Program {
                    public static void Main() {
                        {{typeName}} original = {{defaultVal}};
                        ref {{typeName}} {{varName}} = original;
                    }
                }
                """
            select new RefLocalEmissionScenario(source, varName, typeName, false,
                $"ref {typeName} {varName}");

        return refLocalGen.ToArbitrary();
    }

    // ── Property 12e: Ref argument emission ─────────────────────────────

    public static Arbitrary<RefArgumentEmissionScenario> ArbitraryRefArgumentEmission()
    {
        var gen =
            from typeName in Gen.Elements(NumericTypes)
            from paramName in Gen.Elements(ParamNames)
            from methodName in Gen.Elements(MethodNames)
            let source = $$"""
                class Program {
                    public static void {{methodName}}(ref {{typeName}} {{paramName}}) {
                    }
                    public static void Main() {
                        {{typeName}} arg = 0;
                        {{methodName}}(ref arg);
                    }
                }
                """
            select new RefArgumentEmissionScenario(source,
                $"ref argument for {methodName}(ref {typeName} {paramName})");

        return gen.ToArbitrary();
    }

    // ── Property 12f: Return ref emission ───────────────────────────────

    public static Arbitrary<ReturnRefEmissionScenario> ArbitraryReturnRefEmission()
    {
        var gen =
            from typeName in Gen.Elements(NumericTypes)
            from fieldName in Gen.Elements(FieldNames)
            from methodName in Gen.Elements(MethodNames)
            let source = $$"""
                class Container {
                    {{typeName}} {{fieldName}} = 0;
                    public ref {{typeName}} {{methodName}}() {
                        return ref this.{{fieldName}};
                    }
                }
                class Program {
                    public static void Main() {
                    }
                }
                """
            select new ReturnRefEmissionScenario(source,
                $"return ref this.{fieldName} in {methodName}");

        return gen.ToArbitrary();
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    private CompilerResult CompileSource(string source)
    {
        var filePath = Path.Combine(_outputDir, $"Test_{Guid.NewGuid():N}.mist");
        File.WriteAllText(filePath, source);

        var options = new CompilerOptions(
            ProjectFolder: _outputDir,
            SourcePaths: ImmutableArray.Create(filePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: _outputDir,
            BinDirectory: Path.Combine(_outputDir, "bin"),
            ProjectName: "TestProgram",
            NamespaceRoot: "TestProgram",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            EmitToDisk: false);

        return SharedDriver.Compile(options);
    }

    /// <summary>
    /// Gets the emitted C# content from the compilation result.
    /// Returns the content of the primary .cs file (the one containing the test code).
    /// </summary>
    private static string? GetEmittedCSharp(CompilerResult result)
    {
        if (result.EmittedFiles is null)
            return null;

        // Find the user file by looking for the one containing "namespace TestProgram"
        foreach (var (_, content) in result.EmittedFiles)
        {
            if (content.Contains("namespace TestProgram"))
            {
                return content;
            }
        }

        return null;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 12a: Ref Parameters Emit C# ref Keyword
    //
    // For any method with a ref parameter, the emitted C# SHALL contain
    // the `ref` keyword before the parameter type.
    //
    // **Validates: Requirements 8.1**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(RefEmissionPropertyTests) })]
    public Property RefParameter_EmitsCSharpRefKeyword(RefParameterEmissionScenario scenario)
    {
        var result = CompileSource(scenario.Source);

        if (!result.Succeeded)
        {
            var diagnostics = string.Join(", ", result.Diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"));
            return false.ToProperty()
                .Label($"Compilation failed: [{diagnostics}]\nSource:\n{scenario.Source}");
        }

        var emittedCSharp = GetEmittedCSharp(result);
        if (emittedCSharp is null)
        {
            return false.ToProperty()
                .Label($"No emitted C# file found\nSource:\n{scenario.Source}");
        }

        // The emitted C# should contain "ref <type> <name>" in the parameter list.
        // The type is mapped to C# type names (int → Int32, double → Double, etc.)
        var csharpType = MapToCSharpType(scenario.TypeName);
        var containsRefParam = emittedCSharp.Contains($"ref {csharpType} {scenario.ParamName}");

        return containsRefParam.ToProperty()
            .Label($"Expected emitted C# to contain 'ref {csharpType} {scenario.ParamName}' but it did not.\nEmitted C#:\n{emittedCSharp}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 12b: Ref Return Types Emit C# ref Keyword
    //
    // For any method with a ref return type, the emitted C# SHALL contain
    // `ref` before the return type in the method signature.
    //
    // **Validates: Requirements 8.2**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(RefEmissionPropertyTests) })]
    public Property RefReturn_EmitsCSharpRefKeyword(RefReturnEmissionScenario scenario)
    {
        var result = CompileSource(scenario.Source);

        if (!result.Succeeded)
        {
            var diagnostics = string.Join(", ", result.Diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"));
            return false.ToProperty()
                .Label($"Compilation failed: [{diagnostics}]\nSource:\n{scenario.Source}");
        }

        var emittedCSharp = GetEmittedCSharp(result);
        if (emittedCSharp is null)
        {
            return false.ToProperty()
                .Label($"No emitted C# file found\nSource:\n{scenario.Source}");
        }

        // The emitted C# should contain "ref <returnType> <methodName>(" in the method signature.
        var containsRefReturn = emittedCSharp.Contains($"ref ") &&
                                emittedCSharp.Contains($"{scenario.MethodName}(");

        return containsRefReturn.ToProperty()
            .Label($"Expected emitted C# to contain 'ref' before return type for '{scenario.MethodName}' but it did not.\nEmitted C#:\n{emittedCSharp}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 12c: Ref Readonly Return Types Emit C# ref readonly Keywords
    //
    // For any method with a ref readonly return type, the emitted C# SHALL
    // contain `ref readonly` before the return type.
    //
    // **Validates: Requirements 8.3**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(RefEmissionPropertyTests) })]
    public Property RefReadonlyReturn_EmitsCSharpRefReadonlyKeywords(RefReadonlyReturnEmissionScenario scenario)
    {
        var result = CompileSource(scenario.Source);

        if (!result.Succeeded)
        {
            var diagnostics = string.Join(", ", result.Diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"));
            return false.ToProperty()
                .Label($"Compilation failed: [{diagnostics}]\nSource:\n{scenario.Source}");
        }

        var emittedCSharp = GetEmittedCSharp(result);
        if (emittedCSharp is null)
        {
            return false.ToProperty()
                .Label($"No emitted C# file found\nSource:\n{scenario.Source}");
        }

        // The emitted C# should contain "ref readonly" before the return type.
        var containsRefReadonlyReturn = emittedCSharp.Contains("ref readonly") &&
                                        emittedCSharp.Contains($"{scenario.MethodName}(");

        return containsRefReadonlyReturn.ToProperty()
            .Label($"Expected emitted C# to contain 'ref readonly' before return type for '{scenario.MethodName}' but it did not.\nEmitted C#:\n{emittedCSharp}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 12d: Ref Local Declarations Emit C# ref / ref readonly Keywords
    //
    // For any ref local declaration, the emitted C# SHALL contain `ref`
    // (or `ref readonly`) before the local type.
    //
    // **Validates: Requirements 8.4**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(RefEmissionPropertyTests) })]
    public Property RefLocal_EmitsCSharpRefKeyword(RefLocalEmissionScenario scenario)
    {
        var result = CompileSource(scenario.Source);

        if (!result.Succeeded)
        {
            var diagnostics = string.Join(", ", result.Diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"));
            return false.ToProperty()
                .Label($"Compilation failed: [{diagnostics}]\nSource:\n{scenario.Source}");
        }

        var emittedCSharp = GetEmittedCSharp(result);
        if (emittedCSharp is null)
        {
            return false.ToProperty()
                .Label($"No emitted C# file found\nSource:\n{scenario.Source}");
        }

        // The emitted C# should contain "ref <type> <name>" or "ref readonly <type> <name>"
        var csharpType = MapToCSharpType(scenario.TypeName);
        bool containsRefLocal;
        if (scenario.IsReadonly)
        {
            containsRefLocal = emittedCSharp.Contains($"ref readonly {csharpType} {scenario.VarName}");
        }
        else
        {
            containsRefLocal = emittedCSharp.Contains($"ref {csharpType} {scenario.VarName}");
        }

        var expectedPrefix = scenario.IsReadonly ? "ref readonly" : "ref";
        return containsRefLocal.ToProperty()
            .Label($"Expected emitted C# to contain '{expectedPrefix} {csharpType} {scenario.VarName}' but it did not.\nEmitted C#:\n{emittedCSharp}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 12e: Ref Arguments Emit C# ref Keyword at Call Sites
    //
    // For any call-site argument annotated with `ref`, the emitted C# SHALL
    // contain the `ref` keyword before the argument expression.
    //
    // **Validates: Requirements 8.5**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(RefEmissionPropertyTests) })]
    public Property RefArgument_EmitsCSharpRefKeyword(RefArgumentEmissionScenario scenario)
    {
        var result = CompileSource(scenario.Source);

        if (!result.Succeeded)
        {
            var diagnostics = string.Join(", ", result.Diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"));
            return false.ToProperty()
                .Label($"Compilation failed: [{diagnostics}]\nSource:\n{scenario.Source}");
        }

        var emittedCSharp = GetEmittedCSharp(result);
        if (emittedCSharp is null)
        {
            return false.ToProperty()
                .Label($"No emitted C# file found\nSource:\n{scenario.Source}");
        }

        // The emitted C# should contain "ref arg" in the call expression.
        var containsRefArg = emittedCSharp.Contains("ref arg");

        return containsRefArg.ToProperty()
            .Label($"Expected emitted C# to contain 'ref arg' at call site but it did not.\nEmitted C#:\n{emittedCSharp}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 12f: Return Ref Statements Emit C# return ref
    //
    // For any `return ref` statement, the emitted C# SHALL contain
    // `return ref expression;`.
    //
    // **Validates: Requirements 8.6**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(RefEmissionPropertyTests) })]
    public Property ReturnRef_EmitsCSharpReturnRefStatement(ReturnRefEmissionScenario scenario)
    {
        var result = CompileSource(scenario.Source);

        if (!result.Succeeded)
        {
            var diagnostics = string.Join(", ", result.Diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"));
            return false.ToProperty()
                .Label($"Compilation failed: [{diagnostics}]\nSource:\n{scenario.Source}");
        }

        var emittedCSharp = GetEmittedCSharp(result);
        if (emittedCSharp is null)
        {
            return false.ToProperty()
                .Label($"No emitted C# file found\nSource:\n{scenario.Source}");
        }

        // The emitted C# should contain "return ref" in the method body.
        var containsReturnRef = emittedCSharp.Contains("return ref ");

        return containsReturnRef.ToProperty()
            .Label($"Expected emitted C# to contain 'return ref' but it did not.\nEmitted C#:\n{emittedCSharp}");
    }

    // ─── Type mapping helper ────────────────────────────────────────────

    /// <summary>
    /// Maps Mist primitive type names to their C# keyword form as emitted by the CSharpEmitter.
    /// The emitter uses C# keywords (int, double, bool, etc.) not CLR type names.
    /// </summary>
    private static string MapToCSharpType(string mistType) => mistType switch
    {
        "int" => "int",
        "double" => "double",
        "bool" => "bool",
        "float" => "float",
        "long" => "long",
        "string" => "string",
        "char" => "char",
        _ => mistType,
    };
}
