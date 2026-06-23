// Feature: associated-types, Properties 3, 4, 6: Diagnostics for duplicate, modifiers, and wrong context
// **Validates: Requirements 1.6, 1.8, 2.7**

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
/// Property-based tests verifying that the compiler emits the correct diagnostics
/// for associated type slot misuse:
///
/// <list type="bullet">
///   <item><b>Property 3</b>: Abstract slot in class/struct emits <c>MIST_AT_0006</c></item>
///   <item><b>Property 4</b>: Modifiers on associated type slot emit <c>MIST_AT_0005</c></item>
///   <item><b>Property 6</b>: Duplicate slot names emit <c>MIST_AT_0001</c></item>
/// </list>
///
/// All work runs in-process using compiler APIs — no external process is spawned.
/// </summary>
public class AssociatedTypeErrorPropertyTests
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
        if (parseResult.Root is null)
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

    // ─── Shared name pools ───────────────────────────────────────────────

    private static readonly string[] SlotNames = ["Enumerator", "Element", "Key", "Value", "Iterator", "Builder"];
    private static readonly string[] TypeNames = ["MyClass", "Container", "Wrapper", "Handler", "Service"];
    private static readonly string[] InterfaceNames = ["IIterable", "IRepository", "ICollection", "IMapper"];
    private static readonly string[] TraitNames = ["Sortable", "Hashable", "Serializable", "Cloneable"];

    private static readonly string[] Modifiers = ["public", "private", "static"];

    // ═══════════════════════════════════════════════════════════════════════
    // Property 3: Abstract slot in class/struct emits diagnostic
    //
    // **Validates: Requirements 1.6**
    //
    // For any valid associated type slot declaration text, placing that
    // declaration inside a class or struct body SHALL cause the compiler to
    // emit diagnostic MIST_AT_0006.
    // ═══════════════════════════════════════════════════════════════════════

#pragma warning disable CA1034

    /// <summary>Describes a scenario where an associated type slot is placed in a class or struct.</summary>
    public record WrongContextScenario(
        string ContainerKind,
        string ContainerName,
        string SlotDeclaration,
        string Description)
    {
        public override string ToString() => Description;
    }

#pragma warning restore CA1034

    private static Gen<string> GenSlotDeclarationText()
    {
        return Gen.OneOf(
            // Constrained: type Name : Constraint;
            from name in Gen.Elements(SlotNames)
            from constraint in Gen.Elements("struct", "class", "IEnumerator", "IComparable")
            select $"type {name} : {constraint};",
            // Unconstrained: type Name;
            from name in Gen.Elements(SlotNames)
            select $"type {name};",
            // With multiple constraints: type Name : C1, C2;
            from name in Gen.Elements(SlotNames)
            from c1 in Gen.Elements("struct", "IEnumerator")
            from c2 in Gen.Elements("IComparable", "IDisposable")
            select $"type {name} : {c1}, {c2};",
            // With type parameters: type Name<T> : Constraint;
            from name in Gen.Elements(SlotNames)
            from constraint in Gen.Elements("struct", "IEnumerator")
            select $"type {name}<T> : {constraint};");
    }

    private static Gen<WrongContextScenario> GenWrongContextScenario()
    {
        return from containerKind in Gen.Elements("class", "struct")
               from containerName in Gen.Elements(TypeNames)
               from slotDecl in GenSlotDeclarationText()
               let desc = $"{containerKind} {containerName} {{ {slotDecl} }}"
               select new WrongContextScenario(containerKind, containerName, slotDecl, desc);
    }

    public static Arbitrary<WrongContextScenario> ArbitraryWrongContextScenario() =>
        GenWrongContextScenario().ToArbitrary();

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AssociatedTypeErrorPropertyTests) })]
    public Property AbstractSlotInClassOrStruct_EmitsMIST_AT_0006(WrongContextScenario scenario)
    {
        var source = $@"
{scenario.ContainerKind} {scenario.ContainerName} {{
    {scenario.SlotDeclaration}
}}
";
        var diagnostics = CompileAndGetAllDiagnostics(source);

        var matching = diagnostics.Where(d => d.Id.Value == "MIST_AT_0006").ToList();

        if (matching.Count == 0)
            return false.ToProperty()
                .Label($"Expected MIST_AT_0006 but none emitted.\nSource:\n{source}\n" +
                       $"All diagnostics: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");

        return true.ToProperty();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 4: Modifiers on associated type slot emit diagnostic
    //
    // **Validates: Requirements 1.8**
    //
    // For any non-empty combination of modifier keywords preceding an
    // associated type slot declaration in an interface or trait body, the
    // compiler SHALL emit diagnostic MIST_AT_0005.
    // ═══════════════════════════════════════════════════════════════════════

#pragma warning disable CA1034

    /// <summary>Describes a scenario where modifiers precede an associated type slot.</summary>
    public record ModifierScenario(
        string ContainerKind,
        string ContainerName,
        string ModifierText,
        string SlotName,
        string ConstraintText,
        string Description)
    {
        public override string ToString() => Description;
    }

#pragma warning restore CA1034

    private static Gen<ModifierScenario> GenModifierScenario()
    {
        return from containerKind in Gen.Elements("interface", "trait")
               from containerName in Gen.OneOf(Gen.Elements(InterfaceNames), Gen.Elements(TraitNames))
               from modifierCount in Gen.Choose(1, 3)
               from modifiers in Gen.ArrayOf(Gen.Elements(Modifiers), modifierCount)
               from slotName in Gen.Elements(SlotNames)
               from hasConstraint in Gen.Elements(true, false)
               from constraint in Gen.Elements("struct", "class", "IEnumerator")
               let constraintText = hasConstraint ? $" : {constraint}" : ""
               let modifierStr = string.Join(" ", modifiers)
               let decl = $"{modifierStr} type {slotName}{constraintText};"
               let desc = $"{containerKind} {containerName} {{ {decl} }}"
               select new ModifierScenario(containerKind, containerName, modifierStr, slotName, constraintText, desc);
    }

    public static Arbitrary<ModifierScenario> ArbitraryModifierScenario() =>
        GenModifierScenario().ToArbitrary();

    [Property(MaxTest = 100, Skip = "Blocked by task 7.1 — abstract type member resolution", Arbitrary = new[] { typeof(AssociatedTypeErrorPropertyTests) })]
    public Property ModifiersOnAssociatedTypeSlot_EmitsMIST_AT_0005(ModifierScenario scenario)
    {
        var source = $@"
{scenario.ContainerKind} {scenario.ContainerName} {{
    {scenario.ModifierText} type {scenario.SlotName}{scenario.ConstraintText};
}}
";
        var diagnostics = CompileAndGetAllDiagnostics(source);

        var matching = diagnostics.Where(d => d.Id.Value == "MIST_AT_0005").ToList();

        if (matching.Count == 0)
            return false.ToProperty()
                .Label($"Expected MIST_AT_0005 but none emitted.\nSource:\n{source}\n" +
                       $"All diagnostics: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");

        return true.ToProperty();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 6: Duplicate slot names emit diagnostic
    //
    // **Validates: Requirements 2.7**
    //
    // For any interface or trait body containing two or more associated type
    // slots with the same name (regardless of differing constraint lists),
    // the semantic model SHALL emit diagnostic MIST_AT_0001.
    // ═══════════════════════════════════════════════════════════════════════

#pragma warning disable CA1034

    /// <summary>Describes a scenario with duplicate associated type slot names.</summary>
    public record DuplicateSlotScenario(
        string ContainerKind,
        string ContainerName,
        string SlotName,
        string FirstConstraint,
        string SecondConstraint,
        int DuplicateCount,
        string Description)
    {
        public override string ToString() => Description;
    }

#pragma warning restore CA1034

    private static Gen<DuplicateSlotScenario> GenDuplicateSlotScenario()
    {
        return from containerKind in Gen.Elements("interface", "trait")
               from containerName in Gen.OneOf(Gen.Elements(InterfaceNames), Gen.Elements(TraitNames))
               from slotName in Gen.Elements(SlotNames)
               from firstConstraint in Gen.Elements("struct", "class", "IEnumerator", "IComparable")
               from secondConstraint in Gen.Elements("struct", "class", "IDisposable", "IComparable")
               from extraDuplicates in Gen.Choose(0, 1)
               let duplicateCount = 2 + extraDuplicates
               let desc = $"{containerKind} {containerName} with {duplicateCount}x '{slotName}'"
               select new DuplicateSlotScenario(containerKind, containerName, slotName, firstConstraint, secondConstraint, duplicateCount, desc);
    }

    public static Arbitrary<DuplicateSlotScenario> ArbitraryDuplicateSlotScenario() =>
        GenDuplicateSlotScenario().ToArbitrary();

    [Property(MaxTest = 100, Skip = "Blocked by task 7.1 — abstract type member resolution", Arbitrary = new[] { typeof(AssociatedTypeErrorPropertyTests) })]
    public Property DuplicateSlotNames_EmitsMIST_AT_0001(DuplicateSlotScenario scenario)
    {
        // Build the slot declarations — first one plus duplicates with different constraints
        var slots = new List<string>
        {
            $"    type {scenario.SlotName} : {scenario.FirstConstraint};",
            $"    type {scenario.SlotName} : {scenario.SecondConstraint};"
        };

        // Add extra duplicates if requested
        for (var i = 2; i < scenario.DuplicateCount; i++)
        {
            slots.Add($"    type {scenario.SlotName} : struct;");
        }

        var body = string.Join("\n", slots);
        var source = $@"
{scenario.ContainerKind} {scenario.ContainerName} {{
{body}
}}
";
        var diagnostics = CompileAndGetAllDiagnostics(source);

        var matching = diagnostics.Where(d => d.Id.Value == "MIST_AT_0001").ToList();

        // We expect at least (duplicateCount - 1) diagnostics (one per duplicate after the first)
        var expectedCount = scenario.DuplicateCount - 1;

        if (matching.Count < expectedCount)
            return false.ToProperty()
                .Label($"Expected at least {expectedCount} MIST_AT_0001 diagnostic(s) but got {matching.Count}.\n" +
                       $"Source:\n{source}\n" +
                       $"All diagnostics: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");

        return true.ToProperty();
    }
}
