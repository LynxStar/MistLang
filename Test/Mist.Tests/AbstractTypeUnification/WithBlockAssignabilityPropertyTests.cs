// Feature: abstract-type-unification, Property 9: With-block establishes assignability
// **Validates: Requirements 6.1, 6.6**

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
/// A scenario where an abstract type has N required slots, a concrete type has
/// public methods with DIFFERENT names, and a `with` block maps each concrete
/// method to the corresponding slot using `as`. An assignment to the abstract
/// type is then attempted, which should succeed (no error diagnostics).
/// </summary>
public sealed class WithBlockAssignabilityScenario
{
    public string Source { get; }
    public int SlotCount { get; }
    public string Description { get; }

    public WithBlockAssignabilityScenario(string source, int slotCount, string description)
    {
        Source = source;
        SlotCount = slotCount;
        Description = description;
    }

    public override string ToString() => Description;
}

// ─── Generators ─────────────────────────────────────────────────────────────

/// <summary>
/// Generates Mist source code with:
/// 1. An abstract type with 1-3 method slots
/// 2. A concrete type with differently-named public methods matching the signatures
/// 3. A `with` block mapping each concrete method to its corresponding slot
/// 4. An assignment of the concrete type to an abstract-typed variable
/// </summary>
public static class WithBlockAssignabilityGenerators
{
    private static readonly string[] ReturnTypes = ["string", "int", "bool"];
    private static readonly string[] ParamTypes = ["string", "int", "bool"];

    /// <summary>
    /// Represents a slot on the abstract type and its corresponding concrete method name.
    /// </summary>
    private sealed record SlotMapping(
        string SlotName,
        string ConcreteMethodName,
        string ReturnType,
        (string Type, string Name)[] Parameters);

    /// <summary>
    /// Generates 1-3 slot mappings with unique slot names and unique concrete method names.
    /// </summary>
    private static Gen<SlotMapping[]> GenSlotMappings()
    {
        return Gen.Choose(1, 3).SelectMany(GenNSlotMappings);
    }

    private static Gen<SlotMapping[]> GenNSlotMappings(int n)
    {
        Gen<SlotMapping[]> result = Gen.Constant(Array.Empty<SlotMapping>());
        for (var i = 0; i < n; i++)
        {
            var index = i;
            result = result.SelectMany(arr =>
                GenSlotMapping(index).Select(mapping => arr.Append(mapping).ToArray()));
        }
        return result;
    }

    private static Gen<SlotMapping> GenSlotMapping(int index)
    {
        // Slot names: Slot0, Slot1, Slot2
        // Concrete method names: Do0, Do1, Do2 (always different from slot names)
        return from returnType in Gen.Elements(ReturnTypes)
               from paramCount in Gen.Choose(0, 2)
               from paramTypes in Gen.ArrayOf<string>(Gen.Elements(ParamTypes), paramCount)
               let slotName = $"Slot{index}"
               let concreteName = $"Do{index}"
               let parameters = paramTypes.Select((t, i) => (Type: t, Name: $"p{i}")).ToArray()
               select new SlotMapping(slotName, concreteName, returnType, parameters);
    }

    /// <summary>
    /// Generates a complete with-block assignability scenario.
    /// </summary>
    public static Gen<WithBlockAssignabilityScenario> GenScenario()
    {
        return from mappings in GenSlotMappings()
               let source = BuildSource(mappings)
               select new WithBlockAssignabilityScenario(
                   source,
                   mappings.Length,
                   $"{mappings.Length} slots mapped via with-block, params=[{string.Join(",", mappings.Select(m => m.Parameters.Length))}]");
    }

    private static string BuildSource(SlotMapping[] mappings)
    {
        // Abstract type with slots
        var abstractMembers = string.Join("\n", mappings.Select(m =>
        {
            var paramList = string.Join(", ", m.Parameters.Select(p => $"{p.Type} {p.Name}"));
            return $"    {m.ReturnType} {m.SlotName}({paramList});";
        }));

        // Concrete type with differently-named methods
        var concreteMembers = string.Join("\n\n", mappings.Select(m =>
        {
            var paramList = string.Join(", ", m.Parameters.Select(p => $"{p.Type} {p.Name}"));
            var returnValue = m.ReturnType switch
            {
                "string" => "\"hello\"",
                "int" => "42",
                "bool" => "true",
                _ => "default"
            };
            return $"    public {m.ReturnType} {m.ConcreteMethodName}({paramList})\n    {{\n        return {returnValue};\n    }}";
        }));

        // With-block mappings: ConcreteMethod as SlotName
        var withMappings = string.Join("\n", mappings.Select(m =>
            $"    {m.ConcreteMethodName} as {m.SlotName};"));

        return $$"""
            abstract IContract
            {
            {{abstractMembers}}
            }

            public class Adapter
            {
            {{concreteMembers}}
            }

            with Adapter : IContract
            {
            {{withMappings}}
            }

            public class Program
            {
                public static void Main(string[] args)
                {
                    IContract x = new Adapter();
                }
            }
            """;
    }
}

// ─── Property Tests ─────────────────────────────────────────────────────────

/// <summary>
/// Property-based tests verifying that with-block establishes assignability.
///
/// Property 9: For any type and any abstract type, when a valid `with` block maps
/// all required slots (those without defaults), the type SHALL be accepted for
/// assignment to variables and parameters typed as the abstract type.
///
/// **Validates: Requirements 6.1, 6.6**
/// </summary>
public sealed class WithBlockAssignabilityPropertyTests
{
    // ─── Shared infrastructure ───────────────────────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mist_pbt_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourcePath = Path.Combine(tempDir, "Program.mist");
            File.WriteAllText(sourcePath, source);

            var options = new CompilerOptions(
                ProjectFolder: tempDir,
                SourcePaths: ImmutableArray.Create(sourcePath),
                Backend: new BackendId("csharp"),
                OutputDirectory: tempDir,
                BinDirectory: Path.Combine(tempDir, "bin"),
                ProjectName: "TestProgram",
                NamespaceRoot: "TestProgram",
                EnabledAnalyzerIds: ImmutableArray<string>.Empty,
                EmitToDisk: false);

            var result = SharedDriver.Compile(options);
            return result.Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ─── Arbitrary registration ─────────────────────────────────────────

    public static Arbitrary<WithBlockAssignabilityScenario> ArbitraryWithBlockAssignabilityScenario() =>
        WithBlockAssignabilityGenerators.GenScenario().ToArbitrary();

    // ─── Property test ──────────────────────────────────────────────────

    /// <summary>
    /// Property 9: With-block establishes assignability.
    ///
    /// For any abstract type with N required slots (1-3 method signatures) and
    /// any concrete type with differently-named public methods, when a `with`
    /// block maps each concrete method to the corresponding slot using `as`,
    /// an assignment of the concrete type to an abstract-typed variable SHALL
    /// produce NO error diagnostics.
    ///
    /// **Validates: Requirements 6.1, 6.6**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(WithBlockAssignabilityPropertyTests) })]
    public Property WithBlockAssignability_MappedSlots_AcceptsAssignment(
        WithBlockAssignabilityScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        var errors = diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        return (errors.Count == 0).ToProperty()
            .Label($"Expected no error diagnostics for with-block assignability ({scenario.SlotCount} slots)")
            .Label($"Errors: [{string.Join("; ", errors.Select(d => $"{d.Id.Value}: {d.Message}"))}]")
            .Label($"Source:\n{scenario.Source}");
    }
}
