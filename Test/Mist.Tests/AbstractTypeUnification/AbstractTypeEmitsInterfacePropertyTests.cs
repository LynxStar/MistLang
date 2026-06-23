// Feature: abstract-type-unification, Property 12: Abstract type emits as C# interface

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
using Mist.Semantics.Model;

namespace Mist.Tests.AbstractTypeUnification;

// ─── Scenario type ──────────────────────────────────────────────────────────

/// <summary>
/// A scenario where an abstract type has a mix of slots (no body) and default methods
/// (with body). The emitter should produce:
/// 1. A C# interface declaration (keyword `interface`)
/// 2. Slots as signature-only members (semicolon-terminated, no braces)
/// 3. Defaults as DIM bodies (method with braces)
/// </summary>
public sealed class AbstractTypeEmitsInterfaceScenario
{
    public string Source { get; }
    public int SlotCount { get; }
    public int DefaultCount { get; }
    public IReadOnlyList<string> SlotNames { get; }
    public IReadOnlyList<string> DefaultNames { get; }
    public string InterfaceName { get; }
    public string Description { get; }

    public AbstractTypeEmitsInterfaceScenario(
        string source,
        int slotCount,
        int defaultCount,
        IReadOnlyList<string> slotNames,
        IReadOnlyList<string> defaultNames,
        string interfaceName,
        string description)
    {
        Source = source;
        SlotCount = slotCount;
        DefaultCount = defaultCount;
        SlotNames = slotNames;
        DefaultNames = defaultNames;
        InterfaceName = interfaceName;
        Description = description;
    }

    public override string ToString() => Description;
}

// ─── Generators ─────────────────────────────────────────────────────────────

/// <summary>
/// Generates Mist source code with abstract types containing varying counts of
/// slots (0-3) and default methods (0-2). Used to verify that the emitter
/// produces a C# interface with correct structure.
/// </summary>
public static class AbstractTypeEmitsInterfaceGenerators
{
    private static readonly string[] ReturnTypes = ["string", "int", "bool"];

    private sealed record SlotSignature(string ReturnType, string Name, (string Type, string Name)[] Parameters);
    private sealed record DefaultMethod(string ReturnType, string Name, (string Type, string Name)[] Parameters);

    private static Gen<SlotSignature> GenSlot(int index)
    {
        return from returnType in Gen.Elements(ReturnTypes)
               from paramCount in Gen.Choose(0, 2)
               from paramTypes in Gen.ArrayOf<string>(Gen.Elements(ReturnTypes), paramCount)
               let methodName = $"Slot{index}"
               let parameters = paramTypes.Select((t, i) => (Type: t, Name: $"s{index}p{i}")).ToArray()
               select new SlotSignature(returnType, methodName, parameters);
    }

    private static Gen<SlotSignature[]> GenSlots(int count)
    {
        Gen<SlotSignature[]> result = Gen.Constant(Array.Empty<SlotSignature>());
        for (var i = 0; i < count; i++)
        {
            var index = i;
            result = result.SelectMany(arr =>
                GenSlot(index).Select(s => arr.Append(s).ToArray()));
        }
        return result;
    }

    private static Gen<DefaultMethod> GenDefaultMethod(int index)
    {
        return from returnType in Gen.Elements(ReturnTypes)
               from paramCount in Gen.Choose(0, 2)
               from paramTypes in Gen.ArrayOf<string>(Gen.Elements(ReturnTypes), paramCount)
               let methodName = $"Default{index}"
               let parameters = paramTypes.Select((t, i) => (Type: t, Name: $"d{index}p{i}")).ToArray()
               select new DefaultMethod(returnType, methodName, parameters);
    }

    private static Gen<DefaultMethod[]> GenDefaultMethods(int count)
    {
        Gen<DefaultMethod[]> result = Gen.Constant(Array.Empty<DefaultMethod>());
        for (var i = 0; i < count; i++)
        {
            var index = i;
            result = result.SelectMany(arr =>
                GenDefaultMethod(index).Select(dm => arr.Append(dm).ToArray()));
        }
        return result;
    }

    /// <summary>
    /// Generates a scenario: abstract type with 0-3 slots and 0-2 default methods.
    /// At least one member (slot or default) is always present to avoid empty bodies
    /// that might not exercise emission.
    /// </summary>
    public static Gen<AbstractTypeEmitsInterfaceScenario> GenScenario()
    {
        return from slotCount in Gen.Choose(0, 3)
               from defaultCount in Gen.Choose(0, 2)
               where slotCount + defaultCount > 0
               from slots in GenSlots(slotCount)
               from defaults in GenDefaultMethods(defaultCount)
               let source = BuildSource(slots, defaults)
               let slotNames = slots.Select(s => s.Name).ToArray()
               let defaultNames = defaults.Select(d => d.Name).ToArray()
               select new AbstractTypeEmitsInterfaceScenario(
                   source,
                   slots.Length,
                   defaults.Length,
                   slotNames,
                   defaultNames,
                   "IFoo",
                   $"{slots.Length} slots [{string.Join(",", slotNames)}], {defaults.Length} defaults [{string.Join(",", defaultNames)}]");
    }

    private static string BuildSource(SlotSignature[] slots, DefaultMethod[] defaults)
    {
        // Build slot members (semicolon-terminated, no body)
        var slotMembers = string.Join("\n", slots.Select(s =>
        {
            var paramList = string.Join(", ", s.Parameters.Select(p => $"{p.Type} {p.Name}"));
            return $"    {s.ReturnType} {s.Name}({paramList});";
        }));

        // Build default method members (with bodies)
        var defaultMembers = string.Join("\n\n", defaults.Select(d =>
        {
            var paramList = string.Join(", ", d.Parameters.Select(p => $"{p.Type} {p.Name}"));
            var returnValue = d.ReturnType switch
            {
                "string" => "\"hello\"",
                "int" => "42",
                "bool" => "true",
                _ => "default"
            };
            return $"    {d.ReturnType} {d.Name}({paramList})\n    {{\n        return {returnValue};\n    }}";
        }));

        var members = new List<string>();
        if (!string.IsNullOrEmpty(slotMembers)) members.Add(slotMembers);
        if (!string.IsNullOrEmpty(defaultMembers)) members.Add(defaultMembers);
        var body = string.Join("\n\n", members);

        return $$"""
            abstract IFoo
            {
            {{body}}
            }

            public class Program
            {
                public static void Main(string[] args)
                {
                }
            }
            """;
    }
}

// ─── Property Tests ─────────────────────────────────────────────────────────

/// <summary>
/// Property-based tests verifying that abstract types emit as C# interfaces.
///
/// Property 12: For any abstract type declaration, the emitter SHALL produce a C#
/// interface declaration. Slots (no body) emit as signature-only members.
/// Default methods (with body) emit as default interface method bodies.
///
/// Uses in-process compilation via a shared CompilerDriver instance with
/// EmitToDisk: false to capture emitted C# content in memory.
/// Spawns 0 external processes.
///
/// **Validates: Requirements 7.1, 7.2, 7.3, 7.5**
/// </summary>
public sealed class AbstractTypeEmitsInterfacePropertyTests : IDisposable
{
    private readonly string _outputDir;

    public AbstractTypeEmitsInterfacePropertyTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), "MistAbstractEmitPBT_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_outputDir))
        {
            try { Directory.Delete(_outputDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ─── Shared driver (reused across iterations) ────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    // ─── Arbitrary registration ─────────────────────────────────────────

    public static Arbitrary<AbstractTypeEmitsInterfaceScenario> ArbitraryAbstractTypeEmitsInterfaceScenario() =>
        AbstractTypeEmitsInterfaceGenerators.GenScenario().ToArbitrary();

    // ─── Helpers ─────────────────────────────────────────────────────────

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
    /// Returns all emitted C# content concatenated, or null if no files were emitted.
    /// </summary>
    private static string? GetAllEmittedCSharp(CompilerResult result)
    {
        if (result.EmittedFiles is null || result.EmittedFiles.Count == 0)
            return null;

        return string.Join("\n\n", result.EmittedFiles.Values);
    }

    // ─── Property test ──────────────────────────────────────────────────

    /// <summary>
    /// Property 12: Abstract type emits as C# interface.
    ///
    /// For any abstract type declaration with a mix of slots (0-3) and default
    /// methods (0-2), the emitter SHALL:
    /// 1. Produce a C# interface declaration (keyword `interface`, not `class` or `abstract class`)
    /// 2. Emit slots as signature-only members (method name + semicolon, no body/braces)
    /// 3. Emit defaults as DIM bodies (method name + braces with implementation)
    ///
    /// **Validates: Requirements 7.1, 7.2, 7.3, 7.5**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AbstractTypeEmitsInterfacePropertyTests) })]
    public Property AbstractTypeEmitsAsCSharpInterface(AbstractTypeEmitsInterfaceScenario scenario)
    {
        var result = CompileSource(scenario.Source);

        // First check: compilation should succeed without errors
        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if (errors.Count > 0)
        {
            return false.ToProperty()
                .Label($"Compilation failed with errors: [{string.Join("; ", errors.Select(d => $"{d.Id.Value}: {d.Message}"))}]")
                .Label($"Source:\n{scenario.Source}");
        }

        var emittedCSharp = GetAllEmittedCSharp(result);
        if (emittedCSharp is null)
        {
            return false.ToProperty()
                .Label($"No emitted C# files found")
                .Label($"Source:\n{scenario.Source}");
        }

        // Check 1: The emitted C# uses `interface` keyword (not `class` or `abstract class`)
        var usesInterfaceKeyword = emittedCSharp.Contains($"interface {scenario.InterfaceName}", StringComparison.Ordinal);
        var usesClassKeyword = emittedCSharp.Contains($"class {scenario.InterfaceName}", StringComparison.Ordinal)
                            || emittedCSharp.Contains($"abstract class {scenario.InterfaceName}", StringComparison.Ordinal);

        if (!usesInterfaceKeyword || usesClassKeyword)
        {
            return false.ToProperty()
                .Label($"Interface keyword check failed: usesInterface={usesInterfaceKeyword}, usesClass={usesClassKeyword}")
                .Label($"Emitted C#:\n{emittedCSharp}")
                .Label($"Source:\n{scenario.Source}");
        }

        // Extract the interface section for member inspection
        var interfaceSection = ExtractTypeSection(emittedCSharp, "interface", scenario.InterfaceName);
        if (interfaceSection is null)
        {
            return false.ToProperty()
                .Label($"Could not extract interface section for {scenario.InterfaceName}")
                .Label($"Emitted C#:\n{emittedCSharp}")
                .Label($"Source:\n{scenario.Source}");
        }

        // Check 2: Slots emit as signatures only (semicolon, no body/braces)
        var slotFailures = new List<string>();
        foreach (var slotName in scenario.SlotNames)
        {
            if (!IsSignatureOnly(interfaceSection, slotName))
            {
                slotFailures.Add(slotName);
            }
        }

        // Check 3: Defaults emit as DIM bodies (method with braces)
        var defaultFailures = new List<string>();
        foreach (var defaultName in scenario.DefaultNames)
        {
            if (!HasMethodBody(interfaceSection, defaultName))
            {
                defaultFailures.Add(defaultName);
            }
        }

        var allPassed = slotFailures.Count == 0 && defaultFailures.Count == 0;

        return allPassed.ToProperty()
            .Label($"Slots not signature-only: [{string.Join(", ", slotFailures)}]")
            .Label($"Defaults missing DIM body: [{string.Join(", ", defaultFailures)}]")
            .Label($"Interface section:\n{interfaceSection}")
            .Label($"Source:\n{scenario.Source}");
    }

    // ─── Emission inspection helpers ────────────────────────────────────

    /// <summary>
    /// Checks if a method in the interface section is signature-only (ends with semicolon,
    /// no body/braces after the method name).
    /// </summary>
    private static bool IsSignatureOnly(string interfaceSection, string methodName)
    {
        var methodIndex = interfaceSection.IndexOf(methodName, StringComparison.Ordinal);
        if (methodIndex < 0)
            return false;

        // After the method name, find the closing parenthesis then look for semicolon vs brace
        var afterMethod = interfaceSection[methodIndex..];
        var parenClose = afterMethod.IndexOf(')');
        if (parenClose < 0)
            return false;

        var afterParen = afterMethod[(parenClose + 1)..];

        // Find the first meaningful character (skip whitespace)
        var trimmed = afterParen.TrimStart();
        // A signature-only method ends with ';' before any '{'
        return trimmed.Length > 0 && trimmed[0] == ';';
    }

    /// <summary>
    /// Checks if a method in the interface section has a body (opening brace after parameters).
    /// </summary>
    private static bool HasMethodBody(string interfaceSection, string methodName)
    {
        var methodIndex = interfaceSection.IndexOf(methodName, StringComparison.Ordinal);
        if (methodIndex < 0)
            return false;

        var afterMethod = interfaceSection[methodIndex..];
        var parenClose = afterMethod.IndexOf(')');
        if (parenClose < 0)
            return false;

        var afterParen = afterMethod[(parenClose + 1)..];

        // Look for an opening brace (DIM body) before a semicolon
        var braceIndex = afterParen.IndexOf('{');
        var semicolonIndex = afterParen.IndexOf(';');

        // Has a body if brace comes before semicolon (or there is no semicolon before the brace)
        return braceIndex >= 0 && (semicolonIndex < 0 || braceIndex < semicolonIndex);
    }

    /// <summary>
    /// Extracts the brace-delimited body of a type declaration from emitted C#.
    /// Matches patterns like "abstract IFoo {" or "abstract IFoo :".
    /// </summary>
    private static string? ExtractTypeSection(string emittedCSharp, string typeKeyword, string typeName)
    {
        var searchPattern = $"{typeKeyword} {typeName}";
        var typeIndex = emittedCSharp.IndexOf(searchPattern, StringComparison.Ordinal);
        if (typeIndex < 0)
            return null;

        var braceStart = emittedCSharp.IndexOf('{', typeIndex);
        if (braceStart < 0)
            return null;

        // Find the matching closing brace (handle nested braces)
        var depth = 0;
        for (var i = braceStart; i < emittedCSharp.Length; i++)
        {
            if (emittedCSharp[i] == '{') depth++;
            else if (emittedCSharp[i] == '}') depth--;

            if (depth == 0)
            {
                return emittedCSharp[braceStart..(i + 1)];
            }
        }

        return null;
    }
}
