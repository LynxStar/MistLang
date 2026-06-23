// Feature: abstract-type-unification, Property 10: With-block emits explicit interface implementation

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
/// A scenario where an abstract type has N required slots (1-3 method signatures),
/// a concrete type has methods with DIFFERENT names, and a `with` block maps each
/// method to its slot using `as`. After compilation, the emitted C# should contain
/// explicit interface implementation delegates (e.g., `string IContract.SlotName() => this.ConcreteMethodName();`).
/// </summary>
public sealed class WithBlockExplicitImplScenario
{
    public string Source { get; }
    public int SlotCount { get; }
    public IReadOnlyList<(string SlotName, string ConcreteMethodName)> Mappings { get; }
    public string InterfaceName { get; }
    public string ClassName { get; }
    public string Description { get; }

    public WithBlockExplicitImplScenario(
        string source,
        int slotCount,
        IReadOnlyList<(string SlotName, string ConcreteMethodName)> mappings,
        string interfaceName,
        string className,
        string description)
    {
        Source = source;
        SlotCount = slotCount;
        Mappings = mappings;
        InterfaceName = interfaceName;
        ClassName = className;
        Description = description;
    }

    public override string ToString() => Description;
}

// ─── Generators ─────────────────────────────────────────────────────────────

/// <summary>
/// Generates Mist source code with:
/// 1. An abstract type with 1-3 method slots
/// 2. A concrete type with differently-named public methods matching the slot signatures
/// 3. A `with` block mapping each concrete method to its corresponding slot using `as`
///
/// Used to verify that the emitter produces explicit interface implementations
/// that delegate to the concrete methods.
/// </summary>
public static class WithBlockExplicitImplGenerators
{
    private static readonly string[] ReturnTypes = ["string", "int", "bool"];

    /// <summary>
    /// Represents a slot on the abstract type and its corresponding concrete method.
    /// </summary>
    private sealed record SlotMapping(
        string SlotName,
        string ConcreteMethodName,
        string ReturnType);

    private static Gen<SlotMapping> GenSlotMapping(int index)
    {
        return from returnType in Gen.Elements(ReturnTypes)
               let slotName = $"Slot{index}"
               let concreteName = $"Do{index}"
               select new SlotMapping(slotName, concreteName, returnType);
    }

    private static Gen<SlotMapping[]> GenSlotMappings(int count)
    {
        Gen<SlotMapping[]> result = Gen.Constant(Array.Empty<SlotMapping>());
        for (var i = 0; i < count; i++)
        {
            var index = i;
            result = result.SelectMany(arr =>
                GenSlotMapping(index).Select(m => arr.Append(m).ToArray()));
        }
        return result;
    }

    /// <summary>
    /// Generates a complete with-block explicit implementation scenario.
    /// </summary>
    public static Gen<WithBlockExplicitImplScenario> GenScenario()
    {
        return from slotCount in Gen.Choose(1, 3)
               from mappings in GenSlotMappings(slotCount)
               let source = BuildSource(mappings)
               let mappingPairs = mappings.Select(m => (m.SlotName, m.ConcreteMethodName)).ToArray()
               select new WithBlockExplicitImplScenario(
                   source,
                   mappings.Length,
                   mappingPairs,
                   "IContract",
                   "MyClass",
                   $"{mappings.Length} slots mapped via with-block: [{string.Join(", ", mappings.Select(m => $"{m.ConcreteMethodName} as {m.SlotName}"))}]");
    }

    private static string BuildSource(SlotMapping[] mappings)
    {
        // Abstract type with slots (no bodies)
        var abstractMembers = string.Join("\n", mappings.Select(m =>
            $"    {m.ReturnType} {m.SlotName}();"));

        // Concrete type with differently-named public methods
        var concreteMembers = string.Join("\n\n", mappings.Select(m =>
        {
            var returnValue = m.ReturnType switch
            {
                "string" => "\"hello\"",
                "int" => "42",
                "bool" => "true",
                _ => "default"
            };
            return $"    public {m.ReturnType} {m.ConcreteMethodName}()\n    {{\n        return {returnValue};\n    }}";
        }));

        // With-block mappings: ConcreteMethod as SlotName
        var withMappings = string.Join("\n", mappings.Select(m =>
            $"    {m.ConcreteMethodName} as {m.SlotName};"));

        return $$"""
            abstract IContract
            {
            {{abstractMembers}}
            }

            public class MyClass
            {
            {{concreteMembers}}
            }

            with MyClass : IContract
            {
            {{withMappings}}
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
/// Property-based tests verifying that with-block emits explicit interface implementations.
///
/// Property 10: For any `with` block mapping with `SourceMember as TargetSlot`,
/// the emitter SHALL generate an explicit interface implementation method that
/// delegates to the source member.
///
/// Uses in-process compilation via a shared CompilerDriver instance with
/// EmitToDisk: false to capture emitted C# content in memory.
/// Spawns 0 external processes.
///
/// **Validates: Requirements 6.2**
/// </summary>
public sealed class WithBlockExplicitImplPropertyTests : IDisposable
{
    private readonly string _outputDir;

    public WithBlockExplicitImplPropertyTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), "MistWithBlockImplPBT_" + Guid.NewGuid().ToString("N"));
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

    public static Arbitrary<WithBlockExplicitImplScenario> ArbitraryWithBlockExplicitImplScenario() =>
        WithBlockExplicitImplGenerators.GenScenario().ToArbitrary();

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

    /// <summary>
    /// Extracts the brace-delimited body of a type declaration from emitted C#.
    /// Matches patterns like "abstract IContract {" or "class MyClass :".
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

    // ─── Property test ──────────────────────────────────────────────────

    /// <summary>
    /// Property 10: With-block emits explicit interface implementation.
    ///
    /// For any abstract type with N required slots (1-3 method signatures) and
    /// any concrete type with differently-named public methods, when a `with` block
    /// maps each concrete method to its corresponding slot using `as`, the emitter
    /// SHALL generate explicit interface implementations in the emitted C# that
    /// delegate to the source member.
    ///
    /// Checks:
    /// 1. Explicit interface implementation exists (pattern: `ReturnType IContract.SlotName()`)
    /// 2. Each implementation delegates to the concrete method (contains `this.ConcreteMethodName()`)
    ///
    /// **Validates: Requirements 6.2**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(WithBlockExplicitImplPropertyTests) })]
    public Property WithBlockEmitsExplicitInterfaceImplementation(WithBlockExplicitImplScenario scenario)
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
                .Label("No emitted C# files found")
                .Label($"Source:\n{scenario.Source}");
        }

        // Extract the class section where explicit interface implementations should appear
        var classSection = ExtractTypeSection(emittedCSharp, "class", scenario.ClassName);
        if (classSection is null)
        {
            return false.ToProperty()
                .Label($"Could not find class '{scenario.ClassName}' in emitted C#")
                .Label($"Emitted C#:\n{emittedCSharp}")
                .Label($"Source:\n{scenario.Source}");
        }

        // Check each mapping produces an explicit interface implementation
        var explicitImplFailures = new List<string>();
        var delegationFailures = new List<string>();

        foreach (var (slotName, concreteMethodName) in scenario.Mappings)
        {
            // Pattern: "IContract.SlotName()" — explicit interface implementation
            var explicitImplPattern = $"{scenario.InterfaceName}.{slotName}(";
            if (!classSection.Contains(explicitImplPattern, StringComparison.Ordinal))
            {
                explicitImplFailures.Add($"{scenario.InterfaceName}.{slotName}");
            }

            // Pattern: "this.ConcreteMethodName()" — delegation to source member
            var delegationPattern = $"this.{concreteMethodName}()";
            if (!classSection.Contains(delegationPattern, StringComparison.Ordinal))
            {
                delegationFailures.Add($"this.{concreteMethodName}() for slot {slotName}");
            }
        }

        var allPassed = explicitImplFailures.Count == 0 && delegationFailures.Count == 0;

        return allPassed.ToProperty()
            .Label($"Missing explicit interface implementations: [{string.Join(", ", explicitImplFailures)}]")
            .Label($"Missing delegations: [{string.Join(", ", delegationFailures)}]")
            .Label($"Class section:\n{classSection}")
            .Label($"Source:\n{scenario.Source}");
    }
}
