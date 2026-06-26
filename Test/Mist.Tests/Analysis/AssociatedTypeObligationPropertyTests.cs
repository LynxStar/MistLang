// Feature: associated-types, Property 7: Implementor obligation enforcement
// **Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7, 3.8, 3.9, 8.1, 8.2**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Tests.Helpers;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests verifying implementor obligation enforcement for associated types.
///
/// <b>Property 7</b>: For any abstract type with N associated type slots (each with a constraint list)
/// and any implementor that provides M concrete type members (where 0 ≤ M ≤ N), the type checker
/// SHALL emit exactly (N − M) diagnostics for missing slots (MIST_AT_0002), and for each provided
/// concrete type member that does not satisfy its slot's constraints, the type checker SHALL emit
/// diagnostic MIST_AT_0003 identifying the unsatisfied constraint.
///
/// All work runs in-process using compiler APIs — no external process is spawned.
/// </summary>
public class AssociatedTypeObligationPropertyTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();
    private static readonly TypeCheckerAnalyzer Analyzer = new();

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static ImmutableArray<Diagnostic> CompileAndGetAllDiagnostics(string source)
    {
        var sourceText = new SourceText("test.mist", source);

        var lexDiag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexDiag);

        var parseDiag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseDiag);
        if(parseResult.Root is null)
            return lexDiag.ToImmutableArray().AddRange(parseDiag.ToImmutableArray());

        var (model, buildDiag) = StdlibTestHelper.BuildModelWithStdlib(
            parseResult.Root, source: source);

        var tcDiag = new DiagnosticBag();
        Analyzer.Analyze(model, tcDiag);

        var all = new List<Diagnostic>();
        all.AddRange(lexDiag.ToImmutableArray());
        all.AddRange(parseDiag.ToImmutableArray());
        all.AddRange(buildDiag);
        all.AddRange(tcDiag.ToImmutableArray());
        return all.ToImmutableArray();
    }

    // ─── Constraint kinds ────────────────────────────────────────────────

    /// <summary>Describes the kind of constraint on an associated type slot.</summary>
    public enum ConstraintKind
    {
        Struct,
        Class,
        Abstract
    }

    // ─── Scenario model ──────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>Describes a single associated type slot in the abstract type.</summary>
    public sealed class SlotDef
    {
        public string Name { get; }
        public ConstraintKind Constraint { get; }
        public string? AbstractConstraintName { get; }

        public SlotDef(string name, ConstraintKind constraint, string? abstractConstraintName = null)
        {
            Name = name;
            Constraint = constraint;
            AbstractConstraintName = abstractConstraintName;
        }

        public string ConstraintText => Constraint switch
        {
            ConstraintKind.Struct => "struct",
            ConstraintKind.Class => "class",
            ConstraintKind.Abstract => AbstractConstraintName!,
            _ => "struct"
        };
    }

    /// <summary>Describes how an implementor satisfies (or fails to satisfy) a slot.</summary>
    public enum SatisfactionKind
    {
        /// <summary>No concrete type member provided — slot is missing.</summary>
        Missing,
        /// <summary>Concrete type member provided and satisfies the constraint.</summary>
        Satisfied,
        /// <summary>Concrete type member provided but does NOT satisfy the constraint.</summary>
        Unsatisfied
    }

    /// <summary>Describes a complete test scenario.</summary>
    public sealed class ObligationScenario
    {
        public string AbstractName { get; }
        public string ImplementorName { get; }
        public string ImplementorKind { get; }
        public IReadOnlyList<SlotDef> Slots { get; }
        public IReadOnlyList<SatisfactionKind> Satisfactions { get; }
        public int ExpectedMissingCount { get; }
        public int ExpectedUnsatisfiedCount { get; }

        public ObligationScenario(
            string abstractName,
            string implementorName,
            string implementorKind,
            IReadOnlyList<SlotDef> slots,
            IReadOnlyList<SatisfactionKind> satisfactions)
        {
            AbstractName = abstractName;
            ImplementorName = implementorName;
            ImplementorKind = implementorKind;
            Slots = slots;
            Satisfactions = satisfactions;
            ExpectedMissingCount = satisfactions.Count(s => s == SatisfactionKind.Missing);
            ExpectedUnsatisfiedCount = satisfactions.Count(s => s == SatisfactionKind.Unsatisfied);
        }

        public override string ToString()
        {
            var slotDescs = Slots.Zip(Satisfactions, (slot, sat) => $"{slot.Name}:{slot.ConstraintText}={sat}");
            return $"{ImplementorKind} {ImplementorName} : {AbstractName} [{string.Join(", ", slotDescs)}]";
        }
    }

#pragma warning restore CA1034

    // ─── Name pools ──────────────────────────────────────────────────────

    private static readonly string[] AbstractNames =
        ["IIterable", "IRepository", "ICollection", "IMapper", "IFactory"];

    private static readonly string[] SlotNames =
        ["Enumerator", "Element", "Key", "Value", "Iterator"];

    private static readonly string[] ImplementorNames =
        ["MyList", "Container", "Wrapper", "Handler", "Service"];

    private static readonly string[] ConstraintAbstractNames =
        ["IComparable", "IDisposable", "ICloneable"];

    // ─── Generators ──────────────────────────────────────────────────────

    private static Gen<ConstraintKind> GenConstraintKind()
    {
        return Gen.Elements(ConstraintKind.Struct, ConstraintKind.Class, ConstraintKind.Abstract);
    }

    private static Gen<SlotDef> GenSlotDef(string name)
    {
        return from kind in GenConstraintKind()
               from abstractName in Gen.Elements(ConstraintAbstractNames)
               select new SlotDef(name, kind, kind == ConstraintKind.Abstract ? abstractName : null);
    }

    private static Gen<SatisfactionKind> GenSatisfaction()
    {
        return Gen.Elements(SatisfactionKind.Missing, SatisfactionKind.Satisfied, SatisfactionKind.Unsatisfied);
    }

    private static Gen<ObligationScenario> GenScenario()
    {
        return from slotCount in Gen.Choose(1, 3)
               from slotIndices in Gen.ArrayOf(Gen.Choose(0, SlotNames.Length - 1), slotCount)
               let distinctSlotNames = slotIndices.Select(i => SlotNames[i]).Distinct().ToArray()
               where distinctSlotNames.Length > 0
               from slots in GenSlotDefs(distinctSlotNames)
               from satisfactions in Gen.ArrayOf(GenSatisfaction(), slots.Length)
               from abstractIdx in Gen.Choose(0, AbstractNames.Length - 1)
               from implIdx in Gen.Choose(0, ImplementorNames.Length - 1)
               from implKind in Gen.Elements("class", "struct")
               select new ObligationScenario(
                   AbstractNames[abstractIdx],
                   ImplementorNames[implIdx],
                   implKind,
                   slots,
                   satisfactions);
    }

    private static Gen<SlotDef[]> GenSlotDefs(string[] names)
    {
        if(names.Length == 0)
            return Gen.Constant(Array.Empty<SlotDef>());

        Gen<SlotDef[]> result = GenSlotDef(names[0]).Select(s => new[] { s });
        for(var i = 1; i < names.Length; i++)
        {
            var name = names[i];
            result = result.SelectMany(prev =>
                GenSlotDef(name).Select(s => prev.Append(s).ToArray()));
        }

        return result;
    }

    // ─── Source generation ───────────────────────────────────────────────

    /// <summary>
    /// Builds the Mist source code for the scenario.
    /// </summary>
    private static string BuildSource(ObligationScenario scenario)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("namespace TestProject;");
        sb.AppendLine();

        // Emit stub abstract types for any abstract type constraints referenced
        var referencedAbstracts = scenario.Slots
            .Where(s => s.Constraint == ConstraintKind.Abstract)
            .Select(s => s.AbstractConstraintName!)
            .Distinct()
            .ToList();

        foreach(var abs in referencedAbstracts)
        {
            sb.AppendLine($"abstract {abs} {{ }}");
        }

        if(referencedAbstracts.Count > 0)
            sb.AppendLine();

        // Emit the abstract type with associated type slots
        sb.AppendLine($"abstract {scenario.AbstractName} {{");
        foreach(var slot in scenario.Slots)
        {
            sb.AppendLine($"    type {slot.Name} : {slot.ConstraintText};");
        }
        sb.AppendLine("}");
        sb.AppendLine();

        // Emit the implementor
        sb.AppendLine($"{scenario.ImplementorKind} {scenario.ImplementorName} : {scenario.AbstractName} {{");

        for(var i = 0; i < scenario.Slots.Count; i++)
        {
            var slot = scenario.Slots[i];
            var satisfaction = scenario.Satisfactions[i];

            if(satisfaction == SatisfactionKind.Missing)
                continue;

            // Provide a concrete type member (nested type) for this slot
            if(satisfaction == SatisfactionKind.Satisfied)
            {
                EmitSatisfyingNestedType(sb, slot);
            }
            else // Unsatisfied
            {
                EmitUnsatisfyingNestedType(sb, slot);
            }
        }

        sb.AppendLine("}");

        return sb.ToString();
    }

    /// <summary>
    /// Emits a nested type that satisfies the slot's constraint.
    /// </summary>
    private static void EmitSatisfyingNestedType(System.Text.StringBuilder sb, SlotDef slot)
    {
        switch(slot.Constraint)
        {
            case ConstraintKind.Struct:
                // Provide a nested struct — satisfies `struct` constraint
                sb.AppendLine($"    struct {slot.Name} {{ }}");
                break;

            case ConstraintKind.Class:
                // Provide a nested class — satisfies `class` constraint
                sb.AppendLine($"    class {slot.Name} {{ }}");
                break;

            case ConstraintKind.Abstract:
                // Provide a nested class that implements the required abstract type
                sb.AppendLine($"    class {slot.Name} : {slot.AbstractConstraintName} {{ }}");
                break;
        }
    }

    /// <summary>
    /// Emits a nested type that does NOT satisfy the slot's constraint.
    /// </summary>
    private static void EmitUnsatisfyingNestedType(System.Text.StringBuilder sb, SlotDef slot)
    {
        switch(slot.Constraint)
        {
            case ConstraintKind.Struct:
                // Provide a nested class when struct is required — violates `struct` constraint
                sb.AppendLine($"    class {slot.Name} {{ }}");
                break;

            case ConstraintKind.Class:
                // Provide a nested struct when class is required — violates `class` constraint
                sb.AppendLine($"    struct {slot.Name} {{ }}");
                break;

            case ConstraintKind.Abstract:
                // Provide a nested class that does NOT implement the required abstract type
                sb.AppendLine($"    class {slot.Name} {{ }}");
                break;
        }
    }

    // ─── Arbitrary registration ──────────────────────────────────────────

    public static Arbitrary<ObligationScenario> ArbitraryObligationScenario() =>
        GenScenario().ToArbitrary();

    // ═══════════════════════════════════════════════════════════════════════
    // Property 7: Implementor obligation enforcement
    //
    // **Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7, 3.8, 3.9, 8.1, 8.2**
    //
    // For any abstract type with N associated type slots (each with a constraint
    // list) and any implementor that provides M concrete type members (where
    // 0 ≤ M ≤ N), the type checker SHALL emit exactly (N − M) diagnostics
    // for missing slots (MIST_AT_0002), and for each provided concrete type
    // member that does not satisfy its slot's constraints, the type checker
    // SHALL emit diagnostic MIST_AT_0003 identifying the unsatisfied
    // constraint.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AssociatedTypeObligationPropertyTests) })]
    public Property ImplementorObligation_EmitsCorrectDiagnostics(ObligationScenario scenario)
    {
        var source = BuildSource(scenario);
        var diagnostics = CompileAndGetAllDiagnostics(source);

        var missingDiags = diagnostics.Where(d => d.Id.Value == "MIST_AT_0002").ToList();
        var unsatisfiedDiags = diagnostics.Where(d => d.Id.Value == "MIST_AT_0003").ToList();

        // Verify MIST_AT_0002 count = number of missing slots
        if(missingDiags.Count != scenario.ExpectedMissingCount)
        {
            return false.ToProperty()
                .Label($"Expected {scenario.ExpectedMissingCount} MIST_AT_0002 diagnostic(s) but got {missingDiags.Count}.\n" +
                       $"Scenario: {scenario}\n" +
                       $"Source:\n{source}\n" +
                       $"MIST_AT_0002 diagnostics: [{string.Join(", ", missingDiags.Select(d => d.Message))}]\n" +
                       $"All diagnostics: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
        }

        // Verify MIST_AT_0003 count = number of unsatisfied constraints
        if(unsatisfiedDiags.Count != scenario.ExpectedUnsatisfiedCount)
        {
            return false.ToProperty()
                .Label($"Expected {scenario.ExpectedUnsatisfiedCount} MIST_AT_0003 diagnostic(s) but got {unsatisfiedDiags.Count}.\n" +
                       $"Scenario: {scenario}\n" +
                       $"Source:\n{source}\n" +
                       $"MIST_AT_0003 diagnostics: [{string.Join(", ", unsatisfiedDiags.Select(d => d.Message))}]\n" +
                       $"All diagnostics: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
        }

        return true.ToProperty();
    }
}