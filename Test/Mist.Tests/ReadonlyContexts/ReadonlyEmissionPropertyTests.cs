// Feature: readonly-contexts, Property 18: C# Emission of Readonly Constructs

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

namespace Mist.Tests.ReadonlyContexts;

// ─── Scenario types ─────────────────────────────────────────────────────────

/// <summary>
/// Scenario for verifying that readonly fields emit the C# <c>readonly</c> modifier.
/// </summary>
public sealed class ReadonlyFieldEmissionScenario
{
    public string Source { get; }
    public string FieldName { get; }
    public string Description { get; }

    public ReadonlyFieldEmissionScenario(string source, string fieldName, string description)
    {
        Source = source;
        FieldName = fieldName;
        Description = description;
    }

    public override string ToString() => Description;
}

/// <summary>
/// Scenario for verifying that readonly properties with setters emit <c>init</c> instead of <c>set</c>.
/// </summary>
public sealed class ReadonlyPropertyInitEmissionScenario
{
    public string Source { get; }
    public string PropertyName { get; }
    public string Description { get; }

    public ReadonlyPropertyInitEmissionScenario(string source, string propertyName, string description)
    {
        Source = source;
        PropertyName = propertyName;
        Description = description;
    }

    public override string ToString() => Description;
}

/// <summary>
/// Scenario for verifying that readonly structs emit <c>readonly struct</c>.
/// </summary>
public sealed class ReadonlyStructEmissionScenario
{
    public string Source { get; }
    public string StructName { get; }
    public string Description { get; }

    public ReadonlyStructEmissionScenario(string source, string structName, string description)
    {
        Source = source;
        StructName = structName;
        Description = description;
    }

    public override string ToString() => Description;
}

/// <summary>
/// Scenario for verifying that readonly methods on structs emit the C# <c>readonly</c> modifier.
/// </summary>
public sealed class ReadonlyStructMethodEmissionScenario
{
    public string Source { get; }
    public string MethodName { get; }
    public string Description { get; }

    public ReadonlyStructMethodEmissionScenario(string source, string methodName, string description)
    {
        Source = source;
        MethodName = methodName;
        Description = description;
    }

    public override string ToString() => Description;
}

// ─── Generators ─────────────────────────────────────────────────────────────

public static class ReadonlyEmissionGenerators
{
    private static readonly string[] ClassNames = ["Foo", "Bar", "Baz", "Widget"];
    private static readonly string[] StructNames = ["Point", "Vector", "Coord", "Pair"];
    private static readonly string[] FieldNames = ["x", "y", "count", "value"];
    private static readonly string[] PropertyNames = ["Value", "Name", "Count", "Size"];
    private static readonly string[] MethodNames = ["Get", "Compute", "Process", "Calculate"];

    // ─── Readonly Field Emission ────────────────────────────────────────

    /// <summary>
    /// Generates classes with readonly fields. The emitted C# should contain
    /// the <c>readonly</c> modifier on the field.
    /// </summary>
    public static Gen<ReadonlyFieldEmissionScenario> GenReadonlyFieldEmission()
    {
        return from className in Gen.Elements(ClassNames)
               from fieldName in Gen.Elements(FieldNames)
               let source = $@"class {className} {{
    readonly int {fieldName};
    {className}() {{
        this.{fieldName} = 0;
    }}
}}"
               select new ReadonlyFieldEmissionScenario(source, fieldName,
                   $"readonly field '{fieldName}' in class '{className}' emits C# readonly modifier");
    }

    // ─── Readonly Property with Setter → init Emission ──────────────────

    /// <summary>
    /// Generates classes with readonly properties that have setters.
    /// The emitted C# should use <c>init</c> instead of <c>set</c>.
    /// </summary>
    public static Gen<ReadonlyPropertyInitEmissionScenario> GenReadonlyPropertyInitEmission()
    {
        return from className in Gen.Elements(ClassNames)
               from propName in Gen.Elements(PropertyNames)
               let source = $@"class {className} {{
    readonly int {propName} {{
        get {{
            return field;
        }}
        set {{
            field = value;
        }}
    }}
    {className}() {{
        this.{propName} = 10;
    }}
}}"
               select new ReadonlyPropertyInitEmissionScenario(source, propName,
                   $"readonly property '{propName}' with setter in '{className}' emits 'init' accessor");
    }

    // ─── Readonly Struct Emission ───────────────────────────────────────

    /// <summary>
    /// Generates readonly structs. The emitted C# should contain <c>readonly struct</c>.
    /// </summary>
    public static Gen<ReadonlyStructEmissionScenario> GenReadonlyStructEmission()
    {
        return from structName in Gen.Elements(StructNames)
               from fieldName in Gen.Elements(FieldNames)
               let source = $@"readonly struct {structName} {{
    int {fieldName};
    {structName}() {{
        this.{fieldName} = 0;
    }}
    int Get{fieldName}() {{
        return this.{fieldName};
    }}
}}"
               select new ReadonlyStructEmissionScenario(source, structName,
                   $"readonly struct '{structName}' emits 'readonly struct' in C#");
    }

    // ─── Readonly Method on Struct Emission ─────────────────────────────

    /// <summary>
    /// Generates structs with explicitly readonly methods. The emitted C# should
    /// contain the <c>readonly</c> modifier on the method.
    /// </summary>
    public static Gen<ReadonlyStructMethodEmissionScenario> GenReadonlyStructMethodEmission()
    {
        return from structName in Gen.Elements(StructNames)
               from fieldName in Gen.Elements(FieldNames)
               from methodName in Gen.Elements(MethodNames)
               let source = $@"struct {structName} {{
    int {fieldName};
    {structName}() {{
        this.{fieldName} = 0;
    }}
    readonly int {methodName}() {{
        return this.{fieldName};
    }}
}}"
               select new ReadonlyStructMethodEmissionScenario(source, methodName,
                   $"readonly method '{methodName}' on struct '{structName}' emits C# readonly modifier");
    }
}

// ─── Property Tests ─────────────────────────────────────────────────────────

/// <summary>
/// Property-based tests for C# emission of readonly constructs.
///
/// Property 18 from the readonly-contexts design document.
/// Uses in-process compilation via a shared CompilerDriver instance with
/// EmitToDisk: false to capture emitted C# content in memory.
///
/// **Validates: Requirements 17.1, 17.2, 17.4, 17.5**
/// </summary>
public sealed class ReadonlyEmissionPropertyTests : IDisposable
{
    private readonly string outputDir;

    public ReadonlyEmissionPropertyTests()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "MistReadonlyEmissionPBT_" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(outputDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(outputDir))
        {
            try { Directory.Delete(outputDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ─── Shared driver (reused across tests) ─────────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer(), new ReadonlyAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    // ─── Arbitrary registration ─────────────────────────────────────────

    public static Arbitrary<ReadonlyFieldEmissionScenario> ArbitraryReadonlyFieldEmission() =>
        ReadonlyEmissionGenerators.GenReadonlyFieldEmission().ToArbitrary();

    public static Arbitrary<ReadonlyPropertyInitEmissionScenario> ArbitraryReadonlyPropertyInitEmission() =>
        ReadonlyEmissionGenerators.GenReadonlyPropertyInitEmission().ToArbitrary();

    public static Arbitrary<ReadonlyStructEmissionScenario> ArbitraryReadonlyStructEmission() =>
        ReadonlyEmissionGenerators.GenReadonlyStructEmission().ToArbitrary();

    public static Arbitrary<ReadonlyStructMethodEmissionScenario> ArbitraryReadonlyStructMethodEmission() =>
        ReadonlyEmissionGenerators.GenReadonlyStructMethodEmission().ToArbitrary();

    // ─── Helpers ────────────────────────────────────────────────────────

    private CompilerResult CompileSource(string source)
    {
        // Wrap the user source with a namespace and a minimal Main entry point
        // to satisfy the compiler's entry point requirement.
        var fullSource = "namespace TestProject;\n" + source + @"

class Program {
    static void Main(string[] args) {
    }
}
";
        var filePath = Path.Combine(outputDir, $"Test_{Guid.NewGuid():N}.mist");
        File.WriteAllText(filePath, fullSource);

        var options = new CompilerOptions(
            ProjectFolder: outputDir,
            SourcePaths: ImmutableArray.Create(filePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: outputDir,
            BinDirectory: Path.Combine(outputDir, "bin"),
            ProjectName: "TestProject",
            NamespaceRoot: "TestProject",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            EmitToDisk: false);

        return SharedDriver.Compile(options);
    }

    /// <summary>
    /// Gets the emitted C# content from the compilation result.
    /// Returns the content of the user .cs file (the one containing the test namespace).
    /// </summary>
    private static string? GetEmittedCSharp(CompilerResult result)
    {
        if(result.EmittedFiles is null)
        {
            return null;
        }

        // Find the user file by looking for the one containing "namespace TestProject"
        foreach(var (_, content) in result.EmittedFiles)
        {
            if(content.Contains("namespace TestProject"))
            {
                return content;
            }
        }

        return null;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 18: C# Emission of Readonly Constructs
    //
    // **Validates: Requirements 17.1, 17.2, 17.4, 17.5**
    //
    // For any readonly field, the emitted C# SHALL contain the `readonly`
    // modifier; for any readonly property with a setter, the emitted C#
    // SHALL use `init` instead of `set`; for any readonly struct, the
    // emitted C# SHALL contain `readonly struct`; for any readonly method
    // on a struct, the emitted C# SHALL contain the `readonly` modifier
    // on the method.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Readonly fields emit the C# <c>readonly</c> modifier.
    /// **Validates: Requirements 17.1**
    /// </summary>
    [Property(MaxTest = 50, Arbitrary = new[] { typeof(ReadonlyEmissionPropertyTests) })]
    public Property ReadonlyField_EmitsCSharpReadonlyModifier(
        ReadonlyFieldEmissionScenario scenario)
    {
        var result = CompileSource(scenario.Source);

        if(!result.Succeeded)
        {
            var diagnostics = string.Join(", ", result.Diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"));
            return false.ToProperty()
                .Label($"Compilation failed: [{diagnostics}]\nSource:\n{scenario.Source}");
        }

        var emittedCSharp = GetEmittedCSharp(result);
        if(emittedCSharp is null)
        {
            return false.ToProperty()
                .Label($"No emitted C# file found\nSource:\n{scenario.Source}");
        }

        // The emitted C# should contain "readonly" before the field type/name
        // e.g., "readonly int x;" or "private readonly int x;"
        var containsReadonlyField = emittedCSharp.Contains($"readonly int {scenario.FieldName}");

        return containsReadonlyField.ToProperty()
            .Label($"Expected emitted C# to contain 'readonly int {scenario.FieldName}' but it did not.\nEmitted C#:\n{emittedCSharp}");
    }

    /// <summary>
    /// Readonly properties with setters emit <c>init</c> instead of <c>set</c>.
    /// **Validates: Requirements 17.2**
    /// </summary>
    [Property(MaxTest = 50, Arbitrary = new[] { typeof(ReadonlyEmissionPropertyTests) })]
    public Property ReadonlyPropertyWithSetter_EmitsInitAccessor(
        ReadonlyPropertyInitEmissionScenario scenario)
    {
        var result = CompileSource(scenario.Source);

        if(!result.Succeeded)
        {
            var diagnostics = string.Join(", ", result.Diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"));
            return false.ToProperty()
                .Label($"Compilation failed: [{diagnostics}]\nSource:\n{scenario.Source}");
        }

        var emittedCSharp = GetEmittedCSharp(result);
        if(emittedCSharp is null)
        {
            return false.ToProperty()
                .Label($"No emitted C# file found\nSource:\n{scenario.Source}");
        }

        // The emitted C# should contain "init" (not "set") for the property setter
        var containsInit = emittedCSharp.Contains("init");
        // Ensure it does NOT contain "set" for this property (but "set" might appear
        // in other contexts, so we check that "init" is present near the property)
        var containsSetAccessor = emittedCSharp.Contains("set;") || emittedCSharp.Contains("set {");

        return (containsInit && !containsSetAccessor).ToProperty()
            .Label($"Expected emitted C# to contain 'init' and not 'set' accessor.\nEmitted C#:\n{emittedCSharp}");
    }

    /// <summary>
    /// Readonly structs emit <c>readonly struct</c> in C#.
    /// **Validates: Requirements 17.4**
    /// </summary>
    [Property(MaxTest = 50, Arbitrary = new[] { typeof(ReadonlyEmissionPropertyTests) })]
    public Property ReadonlyStruct_EmitsReadonlyStructDeclaration(
        ReadonlyStructEmissionScenario scenario)
    {
        var result = CompileSource(scenario.Source);

        if(!result.Succeeded)
        {
            var diagnostics = string.Join(", ", result.Diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"));
            return false.ToProperty()
                .Label($"Compilation failed: [{diagnostics}]\nSource:\n{scenario.Source}");
        }

        var emittedCSharp = GetEmittedCSharp(result);
        if(emittedCSharp is null)
        {
            return false.ToProperty()
                .Label($"No emitted C# file found\nSource:\n{scenario.Source}");
        }

        // The emitted C# should contain "readonly struct StructName"
        var containsReadonlyStruct = emittedCSharp.Contains($"readonly struct {scenario.StructName}");

        return containsReadonlyStruct.ToProperty()
            .Label($"Expected emitted C# to contain 'readonly struct {scenario.StructName}' but it did not.\nEmitted C#:\n{emittedCSharp}");
    }

    /// <summary>
    /// Readonly methods on structs emit the C# <c>readonly</c> modifier on the method.
    /// **Validates: Requirements 17.5**
    /// </summary>
    [Property(MaxTest = 50, Arbitrary = new[] { typeof(ReadonlyEmissionPropertyTests) })]
    public Property ReadonlyMethodOnStruct_EmitsReadonlyModifier(
        ReadonlyStructMethodEmissionScenario scenario)
    {
        var result = CompileSource(scenario.Source);

        if(!result.Succeeded)
        {
            var diagnostics = string.Join(", ", result.Diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"));
            return false.ToProperty()
                .Label($"Compilation failed: [{diagnostics}]\nSource:\n{scenario.Source}");
        }

        var emittedCSharp = GetEmittedCSharp(result);
        if(emittedCSharp is null)
        {
            return false.ToProperty()
                .Label($"No emitted C# file found\nSource:\n{scenario.Source}");
        }

        // The emitted C# should contain "readonly" before the method return type and name
        // e.g., "public readonly int Get()" or "readonly int Get()"
        var containsReadonlyMethod = emittedCSharp.Contains($"readonly int {scenario.MethodName}(");

        return containsReadonlyMethod.ToProperty()
            .Label($"Expected emitted C# to contain 'readonly int {scenario.MethodName}(' but it did not.\nEmitted C#:\n{emittedCSharp}");
    }
}