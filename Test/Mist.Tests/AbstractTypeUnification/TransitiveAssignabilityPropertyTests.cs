// Feature: abstract-type-unification, Property 17: Transitive assignability through abstract hierarchy

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

namespace Mist.Tests.AbstractTypeUnification;

// ─── Scenario type ──────────────────────────────────────────────────────────

/// <summary>
/// Scenario for Property 17: Transitive assignability through abstract hierarchy.
/// An abstract IParent with N slots, an abstract IChild : IParent with M additional slots,
/// and a concrete class Impl : IChild that fills all slots from both. An assignment
/// IParent x = new Impl() should succeed transitively.
/// </summary>
public sealed class TransitiveAssignabilityScenario
{
    public string Source { get; }
    public int ParentSlotCount { get; }
    public int ChildSlotCount { get; }
    public string Description { get; }

    public TransitiveAssignabilityScenario(string source, int parentSlotCount, int childSlotCount, string description)
    {
        Source = source;
        ParentSlotCount = parentSlotCount;
        ChildSlotCount = childSlotCount;
        Description = description;
    }

    public override string ToString() => Description;
}

// ─── Generators ─────────────────────────────────────────────────────────────

/// <summary>
/// Generates Mist source code with abstract type hierarchies (IParent, IChild : IParent)
/// and a concrete implementer of IChild, then assigns to an IParent-typed variable.
/// Parent has 1-2 slots, child has 1-2 additional slots.
/// </summary>
public static class TransitiveAssignabilityGenerators
{
    private static readonly string[] ReturnTypes = ["string", "int", "bool"];

    /// <summary>
    /// Represents a single method slot signature.
    /// </summary>
    private sealed record SlotSignature(string ReturnType, string Name, (string Type, string Name)[] Parameters);

    public static Gen<TransitiveAssignabilityScenario> GenScenario()
    {
        return from parentSlotCount in Gen.Choose(1, 2)
               from childSlotCount in Gen.Choose(1, 2)
               from parentSlots in GenNSlots(parentSlotCount, "Parent")
               from childSlots in GenNSlots(childSlotCount, "Child")
               let source = BuildSource(parentSlots, childSlots)
               select new TransitiveAssignabilityScenario(
                   source,
                   parentSlotCount,
                   childSlotCount,
                   $"IParent({parentSlotCount} slots) <- IChild({childSlotCount} slots) <- Impl, assign Impl to IParent");
    }

    private static Gen<SlotSignature[]> GenNSlots(int n, string prefix)
    {
        Gen<SlotSignature[]> result = Gen.Constant(Array.Empty<SlotSignature>());
        for (var i = 0; i < n; i++)
        {
            var index = i;
            var methodPrefix = prefix;
            result = result.SelectMany(arr =>
                GenSlotSignature(index, methodPrefix).Select(slot => arr.Append(slot).ToArray()));
        }
        return result;
    }

    private static Gen<SlotSignature> GenSlotSignature(int index, string prefix)
    {
        return from returnType in Gen.Elements(ReturnTypes)
               from paramCount in Gen.Choose(0, 2)
               from paramTypes in Gen.ArrayOf(Gen.Elements(ReturnTypes), paramCount)
               let methodName = $"{prefix}Method{index}"
               let parameters = paramTypes.Select((t, i) => (Type: t, Name: $"p{i}")).ToArray()
               select new SlotSignature(returnType, methodName, parameters);
    }

    private static string BuildSource(SlotSignature[] parentSlots, SlotSignature[] childSlots)
    {
        var parentMembers = string.Join("\n", parentSlots.Select(s =>
        {
            var paramList = string.Join(", ", s.Parameters.Select(p => $"{p.Type} {p.Name}"));
            return $"    {s.ReturnType} {s.Name}({paramList});";
        }));

        var childMembers = string.Join("\n", childSlots.Select(s =>
        {
            var paramList = string.Join(", ", s.Parameters.Select(p => $"{p.Type} {p.Name}"));
            return $"    {s.ReturnType} {s.Name}({paramList});";
        }));

        var allSlots = parentSlots.Concat(childSlots).ToArray();
        var implMembers = string.Join("\n\n", allSlots.Select(s =>
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
            namespace TestProject;

            abstract IParent
            {
            {{parentMembers}}
            }

            abstract IChild : IParent
            {
            {{childMembers}}
            }

            public class Impl : IChild
            {
            {{implMembers}}
            }

            public class Program
            {
                public static void Main(string[] args)
                {
                    IParent x = new Impl();
                }
            }
            """;
    }
}

// ─── Property Tests ─────────────────────────────────────────────────────────

/// <summary>
/// Property-based tests verifying transitive assignability through abstract hierarchy.
///
/// Property 17: For any abstract type hierarchy where IChild : IParent, an implementer
/// of IChild SHALL be assignable to a variable typed as IParent.
///
/// **Validates: Requirements 9.4**
/// </summary>
public sealed class TransitiveAssignabilityPropertyTests : IDisposable
{
    // ─── Shared infrastructure ───────────────────────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private readonly string _outputDir;

    public TransitiveAssignabilityPropertyTests()
    {
        _outputDir = Path.Combine(
            Path.GetTempPath(),
            "MistTransitiveAssignPBT_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_outputDir))
        {
            try { Directory.Delete(_outputDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ─── Arbitrary registration ─────────────────────────────────────────

    public static Arbitrary<TransitiveAssignabilityScenario> ArbitraryScenario() =>
        TransitiveAssignabilityGenerators.GenScenario().ToArbitrary();

    // ─── Helpers ────────────────────────────────────────────────────────

    private CompilerResult Compile(string source)
    {
        var filePath = Path.Combine(_outputDir, $"Test_{Guid.NewGuid():N}.mist");
        File.WriteAllText(filePath, source);

        var options = new CompilerOptions(
            ProjectFolder: _outputDir,
            SourcePaths: ImmutableArray.Create(filePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: Path.Combine(_outputDir, "out"),
            BinDirectory: Path.Combine(_outputDir, "bin"),
            ProjectName: "TestProject",
            NamespaceRoot: "TestProject",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            EmitToDisk: false);

        return SharedDriver.Compile(options);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 17: Transitive assignability through abstract hierarchy
    //
    // **Validates: Requirements 9.4**
    //
    // For any abstract type hierarchy where IChild : IParent, an implementer
    // of IChild SHALL be assignable to a variable typed as IParent (transitive).
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(TransitiveAssignabilityPropertyTests) })]
    public Property TransitiveAssignability_ImplementerOfChild_AssignableToParent(
        TransitiveAssignabilityScenario scenario)
    {
        var result = Compile(scenario.Source);

        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        return (errors.Count == 0).ToProperty()
            .Label($"Expected no error diagnostics for transitive assignability " +
                   $"(IParent: {scenario.ParentSlotCount} slots, IChild: {scenario.ChildSlotCount} slots)")
            .Label($"Errors: [{string.Join("; ", errors.Select(d => $"{d.Id.Value}: {d.Message}"))}]")
            .Label($"Source:\n{scenario.Source}");
    }
}
