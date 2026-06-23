// Feature: abstract-type-unification, Property 8: Forwarding methods emitted for defaults

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
/// A scenario where an abstract type has N default methods (1-3, methods with bodies)
/// and a concrete type declares `: AbstractType`, fills required slots, but does NOT
/// override any default method. The emitter should produce:
/// 1. DIM bodies on the C# interface
/// 2. Forwarding methods on the concrete class
/// </summary>
public sealed class ForwardingMethodEmissionScenario
{
    public string Source { get; }
    public int DefaultMethodCount { get; }
    public IReadOnlyList<string> DefaultMethodNames { get; }
    public string InterfaceName { get; }
    public string ClassName { get; }
    public string Description { get; }

    public ForwardingMethodEmissionScenario(
        string source,
        int defaultMethodCount,
        IReadOnlyList<string> defaultMethodNames,
        string interfaceName,
        string className,
        string description)
    {
        Source = source;
        DefaultMethodCount = defaultMethodCount;
        DefaultMethodNames = defaultMethodNames;
        InterfaceName = interfaceName;
        ClassName = className;
        Description = description;
    }

    public override string ToString() => Description;
}

// ─── Generators ─────────────────────────────────────────────────────────────

/// <summary>
/// Generates Mist source code with abstract types containing default methods
/// and concrete types that do NOT override those defaults. Used to verify that
/// the emitter produces both DIM bodies and forwarding stubs.
/// </summary>
public static class ForwardingMethodEmissionGenerators
{
    private static readonly string[] ReturnTypes = ["string", "int", "bool"];

    /// <summary>
    /// Represents a default method (has a body).
    /// </summary>
    private sealed record DefaultMethod(string ReturnType, string Name, (string Type, string Name)[] Parameters);

    /// <summary>
    /// Represents a required slot (no body).
    /// </summary>
    private sealed record SlotSignature(string ReturnType, string Name, (string Type, string Name)[] Parameters);

    private static Gen<DefaultMethod> GenDefaultMethod(int index)
    {
        return from returnType in Gen.Elements(ReturnTypes)
               from paramCount in Gen.Choose(0, 2)
               from paramTypes in Gen.ArrayOf<string>(Gen.Elements(ReturnTypes), paramCount)
               let methodName = $"Default{index}"
               let parameters = paramTypes.Select((t, i) => (Type: t, Name: $"d{i}")).ToArray()
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

    private static Gen<SlotSignature> GenSlotSignature()
    {
        return from returnType in Gen.Elements(ReturnTypes)
               let methodName = "RequiredSlot0"
               select new SlotSignature(returnType, methodName, Array.Empty<(string, string)>());
    }

    /// <summary>
    /// Generates a scenario: abstract type with 1 required slot + N default methods,
    /// and a concrete type that fills only the required slot (takes all defaults).
    /// </summary>
    public static Gen<ForwardingMethodEmissionScenario> GenScenario()
    {
        return from defaultCount in Gen.Choose(1, 3)
               from defaults in GenDefaultMethods(defaultCount)
               from slot in GenSlotSignature()
               let source = BuildSource(slot, defaults)
               let defaultNames = defaults.Select(d => d.Name).ToArray()
               select new ForwardingMethodEmissionScenario(
                   source,
                   defaults.Length,
                   defaultNames,
                   "IContract",
                   "Implementer",
                   $"{defaults.Length} default methods, names=[{string.Join(",", defaultNames)}]");
    }

    private static string BuildSource(SlotSignature slot, DefaultMethod[] defaults)
    {
        // Build required slot (semicolon-terminated)
        var slotParamList = string.Join(", ", slot.Parameters.Select(p => $"{p.Type} {p.Name}"));
        var slotMember = $"    {slot.ReturnType} {slot.Name}({slotParamList});";

        // Build default method members (with bodies)
        var defaultMembers = string.Join("\n\n", defaults.Select(d =>
        {
            var paramList = string.Join(", ", d.Parameters.Select(p => $"{p.Type} {p.Name}"));
            var returnValue = d.ReturnType switch
            {
                "string" => "\"default\"",
                "int" => "42",
                "bool" => "true",
                _ => "default"
            };
            return $"    {d.ReturnType} {d.Name}({paramList})\n    {{\n        return {returnValue};\n    }}";
        }));

        // Build concrete type: fills only the required slot, NO overrides of defaults
        var slotReturnValue = slot.ReturnType switch
        {
            "string" => "\"filled\"",
            "int" => "99",
            "bool" => "false",
            _ => "default"
        };
        var concreteSlot = $"    public {slot.ReturnType} {slot.Name}({slotParamList})\n    {{\n        return {slotReturnValue};\n    }}";

        return $$"""
            abstract IContract
            {
            {{slotMember}}

            {{defaultMembers}}
            }

            public class Implementer : IContract
            {
            {{concreteSlot}}
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
/// Property-based tests verifying that forwarding methods are emitted for defaults.
///
/// Property 8: For any abstract type with default methods and any concrete type that
/// does not provide its own implementation for those defaults, the emitter SHALL generate
/// both a default interface method body on the C# interface AND a forwarding method on
/// the concrete class that delegates to the DIM.
///
/// Uses in-process compilation via a shared CompilerDriver instance with
/// EmitToDisk: false to capture emitted C# content in memory.
/// Spawns 0 external processes.
///
/// **Validates: Requirements 5.5, 7.6**
/// </summary>
public sealed class ForwardingMethodEmissionPropertyTests : IDisposable
{
    private readonly string _outputDir;

    public ForwardingMethodEmissionPropertyTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), "MistForwardingEmitPBT_" + Guid.NewGuid().ToString("N"));
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

    public static Arbitrary<ForwardingMethodEmissionScenario> ArbitraryForwardingMethodEmissionScenario() =>
        ForwardingMethodEmissionGenerators.GenScenario().ToArbitrary();

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
    /// Property 8: Forwarding methods emitted for defaults.
    ///
    /// For any abstract type with N default methods (1-3) and any concrete type
    /// that declares `: AbstractType`, fills required slots, but does NOT override
    /// any default method, the emitter SHALL generate:
    /// 1. Default interface method (DIM) bodies on the C# interface
    /// 2. Forwarding methods on the concrete class that delegate to the DIM
    ///
    /// **Validates: Requirements 5.5, 7.6**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ForwardingMethodEmissionPropertyTests) })]
    public Property ForwardingMethodsEmittedForDefaults(ForwardingMethodEmissionScenario scenario)
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

        // Check 1: The interface declaration contains DIM bodies
        // Look for the default method names appearing inside an interface-like context with a body
        var dimFailures = new List<string>();
        foreach (var methodName in scenario.DefaultMethodNames)
        {
            // A DIM body means the method name appears with a body (opening brace) inside the interface
            // Pattern: the method name followed by parameters and a brace block in the interface section
            if (!HasDimBody(emittedCSharp, scenario.InterfaceName, methodName))
            {
                dimFailures.Add(methodName);
            }
        }

        // Check 2: The concrete class contains forwarding methods for each default
        var forwardingFailures = new List<string>();
        foreach (var methodName in scenario.DefaultMethodNames)
        {
            // A forwarding method means the default method name appears in the concrete class body
            if (!HasForwardingMethod(emittedCSharp, scenario.ClassName, methodName))
            {
                forwardingFailures.Add(methodName);
            }
        }

        var allPassed = dimFailures.Count == 0 && forwardingFailures.Count == 0;

        return allPassed.ToProperty()
            .Label($"DIM body missing for: [{string.Join(", ", dimFailures)}]")
            .Label($"Forwarding method missing for: [{string.Join(", ", forwardingFailures)}]")
            .Label($"Emitted C#:\n{emittedCSharp}")
            .Label($"Source:\n{scenario.Source}");
    }

    // ─── Emission inspection helpers ────────────────────────────────────

    /// <summary>
    /// Checks if the emitted C# contains a default interface method body for the given
    /// method name within the interface declaration.
    /// Looks for the method name appearing with a braced body in the interface section.
    /// </summary>
    private static bool HasDimBody(string emittedCSharp, string interfaceName, string methodName)
    {
        // Find the interface section in the emitted C#
        var interfaceSection = ExtractTypeSection(emittedCSharp, "interface", interfaceName);
        if (interfaceSection is null)
            return false;

        // Check that the method name appears with a body (contains both the method name and '{')
        // A DIM has the method signature followed by a body block
        var methodIndex = interfaceSection.IndexOf(methodName, StringComparison.Ordinal);
        if (methodIndex < 0)
            return false;

        // After the method name, look for an opening brace (indicating a body, not just a signature)
        var afterMethod = interfaceSection[methodIndex..];
        var braceIndex = afterMethod.IndexOf('{');
        var semicolonIndex = afterMethod.IndexOf(';');

        // If there's a brace before a semicolon (or no semicolon), it's a DIM body
        return braceIndex >= 0 && (semicolonIndex < 0 || braceIndex < semicolonIndex);
    }

    /// <summary>
    /// Checks if the emitted C# contains a forwarding method for the given method name
    /// within the concrete class declaration.
    /// </summary>
    private static bool HasForwardingMethod(string emittedCSharp, string className, string methodName)
    {
        // Find the class section in the emitted C#
        var classSection = ExtractTypeSection(emittedCSharp, "class", className);
        if (classSection is null)
            return false;

        // The forwarding method should contain the default method name in the class body
        return classSection.Contains(methodName, StringComparison.Ordinal);
    }

    /// <summary>
    /// Extracts the brace-delimited body of a type declaration from emitted C#.
    /// Matches patterns like "abstract IContract {" or "class Implementer :".
    /// </summary>
    private static string? ExtractTypeSection(string emittedCSharp, string typeKeyword, string typeName)
    {
        // Look for the type declaration pattern
        var searchPattern = $"{typeKeyword} {typeName}";
        var typeIndex = emittedCSharp.IndexOf(searchPattern, StringComparison.Ordinal);
        if (typeIndex < 0)
            return null;

        // Find the opening brace of this type
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
