// Feature: abstract-type-unification, Property 2: No structural conformance
// **Validates: Requirements 3.3, 3.4**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Semantics.Model;

namespace Mist.Tests.AbstractTypeUnification;

// ─── Scenario type ──────────────────────────────────────────────────────────

/// <summary>
/// Scenario for Property 2: No structural conformance.
/// A concrete type has public members matching ALL slot signatures of an abstract type
/// by name, return type, and parameters, but does NOT declare `: AbstractType` and has
/// no `with` block. An assignment to an abstract-typed variable is attempted.
/// </summary>
#pragma warning disable CA1034 // Nested types — FsCheck scenario-record style
#pragma warning disable CA1819 // Properties should not return arrays — FsCheck requires mutable record fields
public sealed record NoStructuralConformanceScenario(
    string AbstractName,
    string ConcreteName,
    SlotSignature[] Slots,
    string Description)
{
    public override string ToString() => Description;
}

/// <summary>
/// Describes a single slot signature on the abstract type.
/// </summary>
public sealed record SlotSignature(
    string Name,
    string ReturnType,
    ParameterSignature[] Parameters);

/// <summary>
/// A parameter in a slot signature.
/// </summary>
public sealed record ParameterSignature(string Type, string Name);
#pragma warning restore CA1819
#pragma warning restore CA1034

// ─── Generators ─────────────────────────────────────────────────────────────

public static class NoStructuralConformanceGenerators
{
    private static readonly string[] AbstractNames =
        ["IFoo", "IBar", "IService", "IProcessor", "IHandler"];

    private static readonly string[] ConcreteNames =
        ["MyClass", "Widget", "Handler", "Processor", "Service"];

    private static readonly string[] MethodNames =
        ["Run", "Execute", "Process", "Handle", "Compute", "Get", "Apply", "Transform"];

    private static readonly string[] ParamNames =
        ["x", "y", "value", "input", "data", "item", "count", "index"];

    /// <summary>
    /// Types that are always available in Mist without imports.
    /// Using simple primitive types that the compiler always knows about.
    /// </summary>
    private static readonly string[] SimpleTypes =
        ["int", "string", "bool"];

    public static Gen<NoStructuralConformanceScenario> GenScenario()
    {
        return from abstractName in Gen.Elements(AbstractNames)
               from concreteName in Gen.Elements(ConcreteNames)
               from slotCount in Gen.Choose(1, 3)
               from slots in GenSlots(slotCount)
               let description = $"abstract {abstractName} with {slotCount} slot(s), " +
                                 $"{concreteName} matches all but has no conformance declaration"
               select new NoStructuralConformanceScenario(
                   abstractName, concreteName, slots, description);
    }

    private static Gen<SlotSignature[]> GenSlots(int count)
    {
        return Gen.ArrayOf(GenSlot(), count);
    }

    private static Gen<SlotSignature> GenSlot()
    {
        return from name in Gen.Elements(MethodNames)
               from returnType in Gen.Elements(SimpleTypes)
               from paramCount in Gen.Choose(0, 2)
               from parameters in GenParameters(paramCount)
               select new SlotSignature(name, returnType, parameters);
    }

    private static Gen<ParameterSignature[]> GenParameters(int count)
    {
        return Gen.ArrayOf(GenParameter(), count);
    }

    private static Gen<ParameterSignature> GenParameter()
    {
        return from type in Gen.Elements(SimpleTypes)
               from name in Gen.Elements(ParamNames)
               select new ParameterSignature(type, name);
    }
}

// ─── Property test ──────────────────────────────────────────────────────────

/// <summary>
/// Property-based test for Property 2: No structural conformance.
///
/// For any concrete type that has public members matching all slots of an abstract type
/// but does NOT declare `: AbstractType` in its base clause and has no `with` block,
/// the type checker SHALL reject assignment of that type to a variable typed as the
/// abstract type.
///
/// **Validates: Requirements 3.3, 3.4**
/// </summary>
public sealed class NoStructuralConformancePropertyTests : IDisposable
{
    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private readonly string _outputDir;

    public NoStructuralConformancePropertyTests()
    {
        _outputDir = Path.Combine(
            Path.GetTempPath(),
            "MistNoStructuralConformancePBT_" + Guid.NewGuid().ToString("N"));
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

    public static Arbitrary<NoStructuralConformanceScenario> ArbitraryScenario() =>
        NoStructuralConformanceGenerators.GenScenario().ToArbitrary();

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

    /// <summary>
    /// Builds a Mist source string with:
    /// 1. An abstract type with the given slots
    /// 2. A concrete type with matching public members (but NO `: AbstractType`)
    /// 3. A Program class that assigns the concrete type to an abstract-typed variable
    /// </summary>
    private static string BuildSource(NoStructuralConformanceScenario scenario)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("namespace TestProject;");
        sb.AppendLine();

        // Deduplicate slot names to avoid duplicate member declarations
        var uniqueSlots = scenario.Slots
            .GroupBy(s => s.Name)
            .Select(g => g.First())
            .ToArray();

        // Abstract type declaration with slots
        sb.AppendLine($"abstract {scenario.AbstractName}");
        sb.AppendLine("{");
        foreach (var slot in uniqueSlots)
        {
            var paramList = string.Join(", ", slot.Parameters.Select((p, i) => $"{p.Type} {p.Name}{i}"));
            sb.AppendLine($"    {slot.ReturnType} {slot.Name}({paramList});");
        }
        sb.AppendLine("}");
        sb.AppendLine();

        // Concrete type with matching members but NO `: AbstractType`
        sb.AppendLine($"public class {scenario.ConcreteName}");
        sb.AppendLine("{");
        foreach (var slot in uniqueSlots)
        {
            var paramList = string.Join(", ", slot.Parameters.Select((p, i) => $"{p.Type} {p.Name}{i}"));
            var returnValue = GetDefaultReturnValue(slot.ReturnType);
            sb.AppendLine($"    public {slot.ReturnType} {slot.Name}({paramList})");
            sb.AppendLine("    {");
            sb.AppendLine($"        return {returnValue};");
            sb.AppendLine("    }");
        }
        sb.AppendLine("}");
        sb.AppendLine();

        // Program class that attempts the assignment
        sb.AppendLine("public class Program");
        sb.AppendLine("{");
        sb.AppendLine("    public static void Main(string[] args)");
        sb.AppendLine("    {");
        sb.AppendLine($"        {scenario.AbstractName} x = new {scenario.ConcreteName}();");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static string GetDefaultReturnValue(string returnType) => returnType switch
    {
        "int" => "0",
        "string" => "\"hello\"",
        "bool" => "true",
        _ => "0"
    };

    // ═══════════════════════════════════════════════════════════════════════
    // Property 2: No structural conformance
    //
    // **Validates: Requirements 3.3, 3.4**
    //
    // For any concrete type with public members matching all slots of an
    // abstract type but NO `: AbstractType` declaration and NO `with` block,
    // the type checker SHALL reject assignment to an abstract-typed variable.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(NoStructuralConformancePropertyTests) })]
    public Property NoStructuralConformance_MatchingMembersWithoutDeclaration_Rejected(
        NoStructuralConformanceScenario scenario)
    {
        var source = BuildSource(scenario);
        var result = Compile(source);

        var hasAbstractUnsatisfied = result.Diagnostics.Any(d =>
            (d.Id.Value == "MIST_ABSTRACT_UNSATISFIED" || d.Id.Value == "MIST_TRAIT_UNSATISFIED") &&
            (d.Message.Contains("does not implement the abstract type") || d.Message.Contains("does not satisfy")));

        return hasAbstractUnsatisfied.ToProperty()
            .Label($"Expected MIST_ABSTRACT_UNSATISFIED or MIST_TRAIT_UNSATISFIED diagnostic but got none.\n" +
                   $"Diagnostics: [{string.Join(", ", result.Diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]\n" +
                   $"Source:\n{source}");
    }
}
