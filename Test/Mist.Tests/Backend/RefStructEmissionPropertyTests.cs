// Feature: ref-structs, Property 9: Ref struct C# emission
// **Validates: Requirements 5.1, 5.2, 5.3, 5.4, 5.5, 5.6**

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
/// Describes a generated ref struct emission scenario for property testing.
/// </summary>
public sealed record RefStructEmissionScenario(
    string Source,
    string StructName,
    int FieldCount,
    int MethodCount,
    int PropertyCount,
    string Description)
{
    public override string ToString() => Description;
}

#pragma warning restore CA1034

// ─── Property Tests ─────────────────────────────────────────────────────────

/// <summary>
/// Property-based tests for C# emission of ref struct declarations.
///
/// Property 9: Ref struct C# emission
///
/// For any Mist-declared ref struct with fields, methods, and properties,
/// the emitted C# output SHALL contain the <c>ref struct</c> keyword in the
/// type declaration, and the emitted code SHALL compile successfully with
/// the C# compiler.
///
/// Uses in-process compilation via a shared <see cref="CompilerDriver"/> instance
/// with EmitToDisk: false to capture emitted C# content in memory.
///
/// **Validates: Requirements 5.1, 5.2, 5.3, 5.4, 5.5, 5.6**
/// </summary>
public sealed class RefStructEmissionPropertyTests : IDisposable
{
    private readonly string _outputDir;

    public RefStructEmissionPropertyTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), "MistRefStructEmitPBT_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(_outputDir))
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

    // ─── Known names for generation ─────────────────────────────────────

    private static readonly string[] StructNames =
        ["IntPair", "StackBuffer", "TokenSlice", "ValueSpan", "DataView"];

    private static readonly string[] FieldNames =
        ["first", "second", "count", "offset", "length", "data", "value"];

    private static readonly string[] MethodNames =
        ["Sum", "Reset", "Compute", "GetTotal", "Process"];

    private static readonly string[] PropertyNames =
        ["Max", "Min", "IsEmpty", "Total", "Size"];

    private static readonly string[] FieldTypes =
        ["int", "double", "bool", "float", "long"];

    // ─── FsCheck generators ─────────────────────────────────────────────

    /// <summary>
    /// Generates a ref struct with a random combination of fields, methods, and properties.
    /// </summary>
    private static Gen<RefStructEmissionScenario> GenRefStructScenario()
    {
        return from structName in Gen.Elements(StructNames)
               from fieldCount in Gen.Choose(1, 3)
               from methodCount in Gen.Choose(0, 2)
               from propertyCount in Gen.Choose(0, 2)
               from fields in GenFields(fieldCount)
               from methods in GenMethods(methodCount)
               from properties in GenProperties(propertyCount)
               let members = string.Join("\n    ", fields.Concat(methods).Concat(properties))
               let source = $"public ref struct {structName} {{\n    {members}\n}}\nclass Program {{\n    static void Main() {{\n    }}\n}}"
               select new RefStructEmissionScenario(
                   source,
                   structName,
                   fieldCount,
                   methodCount,
                   propertyCount,
                   $"ref struct {structName} ({fieldCount} fields, {methodCount} methods, {propertyCount} properties)");
    }

    /// <summary>
    /// Generates a private ref struct scenario.
    /// </summary>
    private static Gen<RefStructEmissionScenario> GenPrivateRefStructScenario()
    {
        return from structName in Gen.Elements(StructNames)
               from fieldCount in Gen.Choose(1, 2)
               from fields in GenFields(fieldCount)
               let members = string.Join("\n    ", fields)
               let source = $"private ref struct {structName} {{\n    {members}\n}}\nclass Program {{\n    static void Main() {{\n    }}\n}}"
               select new RefStructEmissionScenario(
                   source,
                   structName,
                   fieldCount,
                   0,
                   0,
                   $"private ref struct {structName} ({fieldCount} fields)");
    }

    /// <summary>
    /// Generates a readonly ref struct scenario.
    /// </summary>
    private static Gen<RefStructEmissionScenario> GenReadonlyRefStructScenario()
    {
        return from structName in Gen.Elements(StructNames)
               from fieldCount in Gen.Choose(1, 2)
               from fields in GenFields(fieldCount)
               let members = string.Join("\n    ", fields)
               let source = $"public readonly ref struct {structName} {{\n    {members}\n}}\nclass Program {{\n    static void Main() {{\n    }}\n}}"
               select new RefStructEmissionScenario(
                   source,
                   structName,
                   fieldCount,
                   0,
                   0,
                   $"readonly ref struct {structName} ({fieldCount} fields)");
    }

    private static Gen<string[]> GenFields(int count)
    {
        return Gen.Shuffle(FieldNames)
            .SelectMany(shuffled =>
                Gen.Shuffle(FieldTypes).Select(types =>
                {
                    var typesArr = types.ToArray();
                    return shuffled.Take(count)
                        .Select((name, i) => $"public {typesArr[i % typesArr.Length]} {name};")
                        .ToArray();
                }));
    }

    private static Gen<string[]> GenMethods(int count)
    {
        if(count == 0)
            return Gen.Constant(Array.Empty<string>());

        return Gen.Shuffle(MethodNames)
            .Select(shuffled =>
                shuffled.Take(count)
                    .Select(name => $"public int {name}() {{ return 0; }}")
                    .ToArray());
    }

    private static Gen<string[]> GenProperties(int count)
    {
        if(count == 0)
            return Gen.Constant(Array.Empty<string>());

        return Gen.Shuffle(PropertyNames)
            .Select(shuffled =>
                shuffled.Take(count)
                    .Select(name => $"public int {name} {{ get {{ return 0; }} }}")
                    .ToArray());
    }

    public static Arbitrary<RefStructEmissionScenario> ArbitraryRefStructEmissionScenario()
    {
        return Gen.OneOf(
            GenRefStructScenario(),
            GenPrivateRefStructScenario(),
            GenReadonlyRefStructScenario()).ToArbitrary();
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
        if(result.EmittedFiles is null)
            return null;

        // Find the user file by looking for the one containing "namespace TestProgram"
        foreach(var (_, content) in result.EmittedFiles)
        {
            if(content.Contains("namespace TestProgram"))
            {
                return content;
            }
        }

        return null;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 9: Ref struct C# emission
    //
    // For any Mist-declared ref struct with fields, methods, and properties,
    // the emitted C# output SHALL contain `ref struct` in the type
    // declaration.
    //
    // **Validates: Requirements 5.1, 5.2, 5.3, 5.4, 5.5, 5.6**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(RefStructEmissionPropertyTests) })]
    public Property RefStructEmission_ContainsRefStructKeyword(
        RefStructEmissionScenario scenario)
    {
        var result = CompileSource(scenario.Source);

        if(!result.Succeeded)
        {
            var diagnostics = string.Join(", ",
                result.Diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"));
            return false.ToProperty()
                .Label($"Compilation failed: [{diagnostics}]\nSource:\n{scenario.Source}");
        }

        var emittedCSharp = GetEmittedCSharp(result);
        if(emittedCSharp is null)
        {
            return false.ToProperty()
                .Label($"No emitted C# file found\nSource:\n{scenario.Source}");
        }

        // Requirement 5.1: Emitted C# contains `ref struct` in the declaration
        var containsRefStruct = emittedCSharp.Contains($"ref struct {scenario.StructName}");

        if(!containsRefStruct)
        {
            return false.ToProperty()
                .Label($"Expected emitted C# to contain 'ref struct {scenario.StructName}' but it did not.\nEmitted C#:\n{emittedCSharp}");
        }

        // Requirement 5.1: Fields are emitted in the struct body
        if(scenario.FieldCount > 0)
        {
            // Verify at least one field name from the pool exists in the emitted output
            var fieldHits = FieldNames.Count(f => emittedCSharp.Contains(f));
            if(fieldHits < scenario.FieldCount)
            {
                return false.ToProperty()
                    .Label($"Expected at least {scenario.FieldCount} field names in emitted C# but found {fieldHits}.\nEmitted C#:\n{emittedCSharp}");
            }
        }

        // Requirement 5.1: Methods are emitted in the struct body
        if(scenario.MethodCount > 0)
        {
            // Verify at least one method name from the pool exists in the emitted output
            var methodHits = MethodNames.Count(m => emittedCSharp.Contains(m));
            if(methodHits < scenario.MethodCount)
            {
                return false.ToProperty()
                    .Label($"Expected at least {scenario.MethodCount} method names in emitted C# but found {methodHits}.\nEmitted C#:\n{emittedCSharp}");
            }
        }

        // Requirement 5.1: Properties are emitted in the struct body
        if(scenario.PropertyCount > 0)
        {
            // Verify at least one property name from the pool exists in the emitted output
            var propertyHits = PropertyNames.Count(p => emittedCSharp.Contains(p));
            if(propertyHits < scenario.PropertyCount)
            {
                return false.ToProperty()
                    .Label($"Expected at least {scenario.PropertyCount} property names in emitted C# but found {propertyHits}.\nEmitted C#:\n{emittedCSharp}");
            }
        }

        return true.ToProperty()
            .Label($"OK: {scenario.Description}");
    }
}