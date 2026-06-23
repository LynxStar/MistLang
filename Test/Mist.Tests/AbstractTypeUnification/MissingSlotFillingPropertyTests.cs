// Feature: abstract-type-unification, Property 4: Missing slot filling rejected
//
// For any abstract type with N required slots (N ≥ 2) and any concrete type that
// declares `: AbstractType` but is missing at least one slot implementation, the
// type checker SHALL emit an error diagnostic listing each unfilled slot by name.
//
// Uses in-process compilation via CompilerDriver — zero process spawns.
//
// **Validates: Requirements 4.2, 4.3**

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

/// <summary>
/// Property-based tests verifying that incomplete implementations of abstract types
/// produce error diagnostics listing each unfilled slot.
///
/// Property 4: Missing slot filling rejected — for any abstract type with N ≥ 2
/// required slots and any concrete type that declares `: AbstractType` but only
/// implements some slots (at least 1 missing), the compiler produces error
/// diagnostics mentioning each unfilled slot.
/// </summary>
public sealed class MissingSlotFillingPropertyTests : IDisposable
{
    private readonly string _outputDir;

    public MissingSlotFillingPropertyTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), "MistMissingSlotPBT_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_outputDir))
        {
            try { Directory.Delete(_outputDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ─── Shared driver (reused across all iterations) ────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    // ─── Scenario record ─────────────────────────────────────────────────

    /// <summary>
    /// Represents a test scenario where a concrete type implements some but not all
    /// slots of an abstract type.
    /// </summary>
#pragma warning disable CA1034
    public record MissingSlotScenario(
        string AbstractName,
        ImmutableArray<SlotDefinition> AllSlots,
        ImmutableArray<int> ImplementedIndices,
        ImmutableArray<int> MissingIndices,
        string ClassName)
    {
        public override string ToString()
        {
            var implemented = string.Join(", ", ImplementedIndices.Select(i => AllSlots[i].Name));
            var missing = string.Join(", ", MissingIndices.Select(i => AllSlots[i].Name));
            return $"abstract {AbstractName} [{AllSlots.Length} slots], " +
                   $"class {ClassName} implements [{implemented}], missing [{missing}]";
        }
    }

    public record SlotDefinition(string ReturnType, string Name, ImmutableArray<(string Type, string ParamName)> Parameters)
    {
        public override string ToString()
        {
            var parms = string.Join(", ", Parameters.Select(p => $"{p.Type} {p.ParamName}"));
            return $"{ReturnType} {Name}({parms})";
        }
    }
#pragma warning restore CA1034

    // ─── Name pools ──────────────────────────────────────────────────────

    private static readonly string[] AbstractNames =
        ["IFoo", "IBar", "IBaz", "IProcessor", "IHandler"];

    private static readonly string[] ClassNames =
        ["MyClass", "Handler", "Widget", "Service", "Impl"];

    private static readonly string[] MethodNames =
        ["Execute", "Process", "Compute", "Transform", "Validate",
         "Render", "Serialize", "Initialize", "Dispose", "Reset"];

    private static readonly string[] ReturnTypes = ["string", "int", "bool"];

    private static readonly string[] ParamTypes = ["string", "int", "bool"];

    // ─── Generator ───────────────────────────────────────────────────────

    private static Gen<MissingSlotScenario> GenMissingSlotScenario()
    {
        return from slotCount in Gen.Choose(2, 5)
               from abstractName in Gen.Elements(AbstractNames)
               from className in Gen.Elements(ClassNames)
               from slots in GenSlotDefinitions(slotCount)
               from implementedCount in Gen.Choose(0, slotCount - 1)
               from shuffledIndices in Gen.Shuffle(Enumerable.Range(0, slotCount).ToArray())
               let implemented = shuffledIndices.Take(implementedCount).OrderBy(i => i).ToImmutableArray()
               let missing = shuffledIndices.Skip(implementedCount).OrderBy(i => i).ToImmutableArray()
               select new MissingSlotScenario(
                   abstractName,
                   slots,
                   implemented,
                   missing,
                   className);
    }

    private static Gen<ImmutableArray<SlotDefinition>> GenSlotDefinitions(int count)
    {
        return from methodNameIndices in Gen.Shuffle(Enumerable.Range(0, MethodNames.Length).ToArray())
               from returnTypeChoices in Gen.ArrayOf(Gen.Elements(ReturnTypes), count)
               from hasParam in Gen.ArrayOf(Gen.Elements(true, false), count)
               from paramTypeChoices in Gen.ArrayOf(Gen.Elements(ParamTypes), count)
               let slots = Enumerable.Range(0, count).Select(i =>
               {
                   var name = MethodNames[methodNameIndices[i]];
                   var retType = returnTypeChoices[i];
                   var parms = hasParam[i]
                       ? ImmutableArray.Create<(string, string)>((paramTypeChoices[i], "arg"))
                       : ImmutableArray<(string, string)>.Empty;
                   return new SlotDefinition(retType, name, parms);
               }).ToImmutableArray()
               select slots;
    }

    // ─── Arbitrary registration ──────────────────────────────────────────

    public static Arbitrary<MissingSlotScenario> ArbitraryMissingSlotScenario() =>
        GenMissingSlotScenario().ToArbitrary();

    // ─── Source generation ───────────────────────────────────────────────

    private static string GenerateSource(MissingSlotScenario scenario)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("namespace TestProject;");
        sb.AppendLine();

        // Emit abstract type with all slots
        sb.AppendLine($"abstract {scenario.AbstractName}");
        sb.AppendLine("{");
        foreach (var slot in scenario.AllSlots)
        {
            var parms = string.Join(", ", slot.Parameters.Select(p => $"{p.Type} {p.ParamName}"));
            sb.AppendLine($"    {slot.ReturnType} {slot.Name}({parms});");
        }
        sb.AppendLine("}");
        sb.AppendLine();

        // Emit concrete class implementing only some slots
        sb.AppendLine($"public class {scenario.ClassName} : {scenario.AbstractName}");
        sb.AppendLine("{");
        foreach (var idx in scenario.ImplementedIndices)
        {
            var slot = scenario.AllSlots[idx];
            var parms = string.Join(", ", slot.Parameters.Select(p => $"{p.Type} {p.ParamName}"));
            var body = GetDefaultReturnBody(slot.ReturnType);
            sb.AppendLine($"    public {slot.ReturnType} {slot.Name}({parms})");
            sb.AppendLine("    {");
            sb.AppendLine($"        {body}");
            sb.AppendLine("    }");
        }
        sb.AppendLine("}");
        sb.AppendLine();

        // Entry point (required for compilation)
        sb.AppendLine("public class Program");
        sb.AppendLine("{");
        sb.AppendLine("    public static void Main(string[] args)");
        sb.AppendLine("    {");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static string GetDefaultReturnBody(string returnType) => returnType switch
    {
        "string" => "return \"hello\";",
        "int" => "return 42;",
        "bool" => "return true;",
        _ => "return 0;",
    };

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
            ProjectName: "TestProject",
            NamespaceRoot: "TestProject",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            EmitToDisk: false);

        return SharedDriver.Compile(options);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 4: Missing slot filling rejected
    //
    // **Validates: Requirements 4.2, 4.3**
    //
    // For any abstract type with N ≥ 2 required slots and any concrete type
    // that declares `: AbstractType` but is missing at least one slot
    // implementation, the compiler SHALL emit error diagnostics mentioning
    // each unfilled slot by name and the number of "does not implement"
    // errors SHALL match the number of missing slots.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(MissingSlotFillingPropertyTests) })]
    public Property MissingSlotFilling_ProducesErrorForEachMissingSlot(MissingSlotScenario scenario)
    {
        var source = GenerateSource(scenario);
        var result = CompileSource(source);

        // Collect "does not implement" diagnostics
        var missingSlotDiags = result.Diagnostics
            .Where(d => d.Id.Value == "MIST_SM_0022" &&
                        d.Message.Contains("does not implement abstract member"))
            .ToList();

        // Each missing slot should produce exactly one diagnostic
        var expectedMissingCount = scenario.MissingIndices.Length;
        if (missingSlotDiags.Count != expectedMissingCount)
        {
            return false.ToProperty()
                .Label($"Expected {expectedMissingCount} 'does not implement' diagnostics but got {missingSlotDiags.Count}.\n" +
                       $"Diagnostics: [{string.Join("; ", missingSlotDiags.Select(d => d.Message))}]\n" +
                       $"All diagnostics: [{string.Join("; ", result.Diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]\n" +
                       $"Source:\n{source}");
        }

        // Each missing slot's name should appear in the diagnostics
        foreach (var missingIdx in scenario.MissingIndices)
        {
            var slotName = scenario.AllSlots[missingIdx].Name;
            var mentioned = missingSlotDiags.Any(d => d.Message.Contains(slotName));
            if (!mentioned)
            {
                return false.ToProperty()
                    .Label($"Missing slot '{slotName}' not mentioned in any diagnostic.\n" +
                           $"Diagnostics: [{string.Join("; ", missingSlotDiags.Select(d => d.Message))}]\n" +
                           $"Source:\n{source}");
            }
        }

        return true.ToProperty();
    }
}
