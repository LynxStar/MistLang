// Feature: abstract-type-unification, Property 16: Abstract-typed assignability
// **Validates: Requirements 9.1, 9.2**

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

// ─── Scenario types ─────────────────────────────────────────────────────────

/// <summary>
/// A scenario where a concrete type declares `: AbstractType` and fills all slots,
/// then is assigned to an abstract-typed variable. The compiler should accept this.
/// </summary>
public sealed class ConformingAssignabilityScenario
{
    public string Source { get; }
    public int SlotCount { get; }
    public string Description { get; }

    public ConformingAssignabilityScenario(string source, int slotCount, string description)
    {
        Source = source;
        SlotCount = slotCount;
        Description = description;
    }

    public override string ToString() => Description;
}

/// <summary>
/// A scenario where a concrete type does NOT declare `: AbstractType` and has no
/// `with` block, then is assigned to an abstract-typed variable. The compiler should
/// reject this with an error.
/// </summary>
public sealed class NonConformingAssignabilityScenario
{
    public string Source { get; }
    public int SlotCount { get; }
    public string Description { get; }

    public NonConformingAssignabilityScenario(string source, int slotCount, string description)
    {
        Source = source;
        SlotCount = slotCount;
        Description = description;
    }

    public override string ToString() => Description;
}

// ─── Generators ─────────────────────────────────────────────────────────────

/// <summary>
/// Generates Mist source code for abstract-typed assignability scenarios.
/// Produces both conforming (with `: AbstractType`) and non-conforming (without
/// conformance declaration) scenarios.
/// </summary>
public static class AbstractTypedAssignabilityGenerators
{
    private static readonly string[] ReturnTypes = ["string", "int", "bool"];
    private static readonly string[] ParamTypes = ["string", "int", "bool"];

    private sealed record SlotSignature(string ReturnType, string Name, (string Type, string Name)[] Parameters);

    private static Gen<SlotSignature[]> GenSlotSignatures()
    {
        return Gen.Choose(1, 3).SelectMany(GenNSlots);
    }

    private static Gen<SlotSignature[]> GenNSlots(int n)
    {
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
               from paramCount in Gen.Choose(0, 2)
               from paramTypes in Gen.ArrayOf<string>(Gen.Elements(ParamTypes), paramCount)
               let methodName = $"Method{index}"
               let parameters = paramTypes.Select((t, i) => (Type: t, Name: $"p{i}")).ToArray()
               select new SlotSignature(returnType, methodName, parameters);
    }

    /// <summary>
    /// Generates a conforming scenario: abstract type + concrete type that declares
    /// `: AbstractType`, fills all slots, and is assigned to an abstract-typed variable.
    /// </summary>
    public static Gen<ConformingAssignabilityScenario> GenConformingScenario()
    {
        return from slots in GenSlotSignatures()
               let source = BuildConformingSource(slots)
               select new ConformingAssignabilityScenario(
                   source,
                   slots.Length,
                   $"conforming: {slots.Length} slots, params=[{string.Join(",", slots.Select(s => s.Parameters.Length))}]");
    }

    /// <summary>
    /// Generates a non-conforming scenario: abstract type + concrete type that does NOT
    /// declare `: AbstractType` and has no `with` block, assigned to abstract-typed variable.
    /// </summary>
    public static Gen<NonConformingAssignabilityScenario> GenNonConformingScenario()
    {
        return from slots in GenSlotSignatures()
               let source = BuildNonConformingSource(slots)
               select new NonConformingAssignabilityScenario(
                   source,
                   slots.Length,
                   $"non-conforming: {slots.Length} slots, params=[{string.Join(",", slots.Select(s => s.Parameters.Length))}]");
    }

    private static string BuildConformingSource(SlotSignature[] slots)
    {
        var abstractMembers = string.Join("\n", slots.Select(s =>
        {
            var paramList = string.Join(", ", s.Parameters.Select(p => $"{p.Type} {p.Name}"));
            return $"    {s.ReturnType} {s.Name}({paramList});";
        }));

        var concreteMembers = string.Join("\n\n", slots.Select(s =>
        {
            var paramList = string.Join(", ", s.Parameters.Select(p => $"{p.Type} {p.Name}"));
            var returnValue = GetDefaultValue(s.ReturnType);
            return $"    public {s.ReturnType} {s.Name}({paramList})\n    {{\n        return {returnValue};\n    }}";
        }));

        return $$"""
            abstract IContract
            {
            {{abstractMembers}}
            }

            public class Conformer : IContract
            {
            {{concreteMembers}}
            }

            public class Program
            {
                public static void Main(string[] args)
                {
                    IContract x = new Conformer();
                }
            }
            """;
    }

    private static string BuildNonConformingSource(SlotSignature[] slots)
    {
        var abstractMembers = string.Join("\n", slots.Select(s =>
        {
            var paramList = string.Join(", ", s.Parameters.Select(p => $"{p.Type} {p.Name}"));
            return $"    {s.ReturnType} {s.Name}({paramList});";
        }));

        return $$"""
            abstract IContract
            {
            {{abstractMembers}}
            }

            public class NonConformer
            {
            }

            public class Program
            {
                public static void Main(string[] args)
                {
                    IContract x = new NonConformer();
                }
            }
            """;
    }

    private static string GetDefaultValue(string returnType) => returnType switch
    {
        "string" => "\"hello\"",
        "int" => "42",
        "bool" => "true",
        _ => "default"
    };
}

// ─── Property Tests ─────────────────────────────────────────────────────────

/// <summary>
/// Property-based tests for Property 16: Abstract-typed assignability.
///
/// For any variable declared as an abstract type, the type checker SHALL accept
/// assignment of any concrete type that has established conformance (via `: AbstractType`)
/// and SHALL reject types without conformance.
///
/// **Validates: Requirements 9.1, 9.2**
/// </summary>
public sealed class AbstractTypedAssignabilityPropertyTests
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

    public static Arbitrary<ConformingAssignabilityScenario> ArbitraryConformingScenario() =>
        AbstractTypedAssignabilityGenerators.GenConformingScenario().ToArbitrary();

    public static Arbitrary<NonConformingAssignabilityScenario> ArbitraryNonConformingScenario() =>
        AbstractTypedAssignabilityGenerators.GenNonConformingScenario().ToArbitrary();

    // ═══════════════════════════════════════════════════════════════════════
    // Sub-property 1: Conforming types accepted
    //
    // For any abstract type with N required slots and any concrete type that
    // declares `: AbstractType` and fills all slots, assignment to an
    // abstract-typed variable SHALL produce NO error diagnostics.
    //
    // **Validates: Requirements 9.1**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AbstractTypedAssignabilityPropertyTests) })]
    public Property ConformingType_AssignedToAbstractVariable_Accepted(
        ConformingAssignabilityScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        var errors = diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        return (errors.Count == 0).ToProperty()
            .Label($"Expected no error diagnostics for conforming type assignment ({scenario.SlotCount} slots)")
            .Label($"Errors: [{string.Join("; ", errors.Select(d => $"{d.Id.Value}: {d.Message}"))}]")
            .Label($"Source:\n{scenario.Source}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Sub-property 2: Non-conforming types rejected
    //
    // For any abstract type with N required slots and any concrete type that
    // does NOT declare `: AbstractType` and has no `with` block, assignment
    // to an abstract-typed variable SHALL produce an error diagnostic.
    //
    // **Validates: Requirements 9.2**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AbstractTypedAssignabilityPropertyTests) })]
    public Property NonConformingType_AssignedToAbstractVariable_Rejected(
        NonConformingAssignabilityScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        var hasAssignabilityError = diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Error &&
            (d.Id.Value == "MIST_ABSTRACT_UNSATISFIED" ||
             d.Id.Value == "MIST_TRAIT_UNSATISFIED" ||
             d.Message.Contains("does not implement the abstract type") ||
             d.Message.Contains("does not satisfy") ||
             d.Message.Contains("Cannot assign")));

        return hasAssignabilityError.ToProperty()
            .Label($"Expected an assignability error for non-conforming type ({scenario.SlotCount} slots)")
            .Label($"Diagnostics: [{string.Join("; ", diagnostics.Select(d => $"{d.Id.Value}({d.Severity}): {d.Message}"))}]")
            .Label($"Source:\n{scenario.Source}");
    }
}
