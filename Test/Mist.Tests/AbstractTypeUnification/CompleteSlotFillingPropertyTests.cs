// Feature: abstract-type-unification, Property 3: Complete slot filling accepted

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
/// A scenario where an abstract type is declared with N slots (method signatures)
/// and a concrete type declares `: AbstractType` and provides matching implementations
/// for ALL slots. The compiler should accept this without error diagnostics.
/// </summary>
public sealed class CompleteSlotFillingScenario
{
    public string Source { get; }
    public int SlotCount { get; }
    public string Description { get; }

    public CompleteSlotFillingScenario(string source, int slotCount, string description)
    {
        Source = source;
        SlotCount = slotCount;
        Description = description;
    }

    public override string ToString() => Description;
}

// ─── Generators ─────────────────────────────────────────────────────────────

/// <summary>
/// Generates Mist source code with abstract types containing 1-5 method slots
/// and concrete types that correctly fill all those slots.
/// </summary>
public static class CompleteSlotFillingGenerators
{
    private static readonly string[] ReturnTypes = ["string", "int", "bool"];
    private static readonly string[] ParamTypes = ["string", "int", "bool"];

    /// <summary>
    /// Represents a single method slot signature.
    /// </summary>
    private sealed record SlotSignature(string ReturnType, string Name, (string Type, string Name)[] Parameters);

    /// <summary>
    /// Generates a list of unique slot signatures with 1-5 methods.
    /// </summary>
    private static Gen<SlotSignature[]> GenSlotSignatures()
    {
        return Gen.Choose(1, 5).SelectMany(slotCount => GenNSlots(slotCount));
    }

    private static Gen<SlotSignature[]> GenNSlots(int n)
    {
        // Build up the array one element at a time using SelectMany chains
        Gen<SlotSignature[]> result = Gen.Constant(Array.Empty<SlotSignature>());
        for (var i = 0; i < n; i++)
        {
            var index = i;
            result = result.SelectMany(arr =>
                GenSlotSignature(index).Select(slot => arr.Append(slot).ToArray()));
        }
        return result;
    }

    private static Gen<SlotSignature> GenSlotSignature(int index)
    {
        return from returnType in Gen.Elements(ReturnTypes)
               from paramCount in Gen.Choose(0, 3)
               from paramTypes in Gen.ArrayOf<string>(Gen.Elements(ParamTypes), paramCount)
               let methodName = $"Method{index}"
               let parameters = paramTypes.Select((t, i) => (Type: t, Name: $"p{i}")).ToArray()
               select new SlotSignature(returnType, methodName, parameters);
    }

    /// <summary>
    /// Generates a complete slot filling scenario: abstract type + conforming concrete type.
    /// </summary>
    public static Gen<CompleteSlotFillingScenario> GenScenario()
    {
        return from slots in GenSlotSignatures()
               let source = BuildSource(slots)
               select new CompleteSlotFillingScenario(
                   source,
                   slots.Length,
                   $"{slots.Length} slots, params=[{string.Join(",", slots.Select(s => s.Parameters.Length))}]");
    }

    private static string BuildSource(SlotSignature[] slots)
    {
        var abstractMembers = string.Join("\n", slots.Select(s =>
        {
            var paramList = string.Join(", ", s.Parameters.Select(p => $"{p.Type} {p.Name}"));
            return $"    {s.ReturnType} {s.Name}({paramList});";
        }));

        var concreteMembers = string.Join("\n\n", slots.Select(s =>
        {
            var paramList = string.Join(", ", s.Parameters.Select(p => $"{p.Type} {p.Name}"));
            var returnValue = s.ReturnType switch
            {
                "string" => "\"hello\"",
                "int" => "42",
                "bool" => "true",
                _ => "default"
            };
            return $"    public {s.ReturnType} {s.Name}({paramList})\n    {{\n        return {returnValue};\n    }}";
        }));

        return $$"""
            abstract IContract
            {
            {{abstractMembers}}
            }

            public class Implementer : IContract
            {
            {{concreteMembers}}
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
/// Property-based tests verifying that complete slot filling is accepted.
///
/// Property 3: For any abstract type with N required slots and any concrete type
/// that declares `: AbstractType` and provides public implementations matching
/// all N slot signatures exactly, the type checker SHALL accept the concrete type
/// as a valid implementer (no error diagnostics emitted).
///
/// **Validates: Requirements 4.1, 4.3**
/// </summary>
public sealed class CompleteSlotFillingPropertyTests
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

    public static Arbitrary<CompleteSlotFillingScenario> ArbitraryCompleteSlotFillingScenario() =>
        CompleteSlotFillingGenerators.GenScenario().ToArbitrary();

    // ─── Property test ──────────────────────────────────────────────────

    /// <summary>
    /// Property 3: Complete slot filling accepted.
    ///
    /// For any abstract type with N required slots (1-5 method signatures with
    /// varying return types and parameters), when a concrete type declares
    /// `: AbstractType` AND provides public implementations matching ALL slot
    /// signatures exactly, the compiler SHALL produce NO error diagnostics.
    ///
    /// **Validates: Requirements 4.1, 4.3**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(CompleteSlotFillingPropertyTests) })]
    public Property CompleteSlotFilling_ProducesNoErrors(CompleteSlotFillingScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        var errors = diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        return (errors.Count == 0).ToProperty()
            .Label($"Expected no error diagnostics for complete slot filling ({scenario.SlotCount} slots)")
            .Label($"Errors: [{string.Join("; ", errors.Select(d => $"{d.Id.Value}: {d.Message}"))}]")
            .Label($"Source:\n{scenario.Source}");
    }
}
