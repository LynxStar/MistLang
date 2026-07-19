// Feature: variadics, task 12.3: property test for flat/bracketed equivalence over the CLASSIC
// (legacy Mist.Semantics) pipeline.
//
// Property 3: Flat/bracketed equivalence.
// **Validates: Requirements 3, 4**
//
// For any declared slot list containing a single Unbracketed_Pack and any use-site argument
// list, the flat construction form and its bracketed canonical form (the pack's contiguous
// middle run wrapped in [...]) bind to IDENTICAL slot assignments and denote the SAME type, so
// `Foo<int, string>` and `Foo<[int, string]>` are interchangeable.
//
// This is the CLASSIC-track counterpart of the ProgramModel-track property test
// (VariadicsFlatBracketedEquivalencePropertyTests, task 7.3). Where that test drives
// ProgramModelBuilder.Build and asserts the two constructions INTERN to the very same
// ConstructedType instance (Assert.Same), the classic model does NOT intern -- it builds a
// fresh ConstructedType per construction site -- so "denote the same type" is asserted through
// the classic structural identity relation TypeCheckerAnalyzer.TypesEqual instead (exactly as
// the task 12.1 example test VariadicsSemanticModelBindingRuleTests does). Both fields are
// resolved by driving SemanticModelBuilder.Build entirely in-process via the shared
// VariadicsCompilerFixture; nothing is emitted or run, so there are ZERO external process
// spawns.
//
// Generator strategy (matched flat/bracketed pairs; mirrors the ProgramModel-track test). Each
// scenario picks a pack POSITION by choosing a number of fixed scalar slots before the pack
// (0..2) and after it (0..2), plus a middle element run of length 0..3, so the single `params`
// pack lands leading, middle, trailing, or as the whole list across the generated space. It
// then renders the SAME family `Fam<...>` at two matched use sites over a shared, ordered list
// of element types drawn from a fixed pool of source marker classes (E0..E7):
//   * the FLAT form writes every argument comma-separated -- fixed ends plus the middle run
//     inline, e.g. `Fam<E1, E2, E3, E4>`; and
//   * the BRACKETED canonical form wraps exactly the pack's contiguous middle run in `[...]`
//     while leaving the fixed ends flat, e.g. `Fam<E1, [E2, E3], E4>` (an empty middle renders
//     `[]`).
// Element types are declared source classes (not primitives) so each spelling resolves a given
// name to a structurally identical type and the two constructions compare TypesEqual. The
// all-empty case (no fixed slots and an empty middle) would render the invalid `Fam<>`, so it
// is bumped to a single-element middle.

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Variadics;

/// <summary>
/// A generated flat/bracketed equivalence scenario: a complete, parseable Mist source that
/// declares a single-<c>params</c>-pack family <c>Fam&lt;...&gt;</c> and constructs it twice on a
/// <c>Holder</c> -- once in the flat spelling (field <c>Flat</c>) and once in the bracketed
/// canonical spelling (field <c>Bracketed</c>). The expected binding is known by construction:
/// the fixed lead slots bind <see cref="LeadNames"/> inward from the left, the fixed trail slots
/// bind <see cref="TrailNames"/> inward from the right, and the single pack absorbs the ordered
/// middle <see cref="MidNames"/>.
/// </summary>
public sealed record SemanticFlatBracketedScenario(
    string Source,
    IReadOnlyList<string> LeadNames,
    IReadOnlyList<string> MidNames,
    IReadOnlyList<string> TrailNames,
    string Description)
{
    public override string ToString()
    {
        return Description;
    }
}

/// <summary>
/// Property test for <b>Property 3 (Flat/bracketed equivalence)</b> over the classic (legacy
/// <c>Mist.Semantics</c>) pipeline. Builds the semantic model in-process and asserts that a flat
/// variadic construction and its bracketed canonical form bind to identical slot assignments --
/// fixed ends inward from both ends, the single pack over the ordered middle -- and denote the
/// same type under <see cref="TypeCheckerAnalyzer.TypesEqual"/>. <b>Validates: Requirements 3, 4.</b>
/// </summary>
public sealed class VariadicsSemanticModelFlatBracketedEquivalencePropertyTests
{
    // A fixed pool of declared source marker classes used as element types. Using declared
    // classes (rather than primitives) keeps each element's DisplayName equal to its source
    // name and lets the flat and bracketed constructions compare structurally equal.
    private const int PoolSize = 8;

    // The maximum element run a scenario draws: lead(<=2) + middle(<=3) + trail(<=2). A single
    // fixed-length draw of this size is sliced per scenario, so every `from` clause precedes
    // the `select` (no interdependent generator lengths).
    private const int MaxTotalElements = 7;

    // =====================================================================
    // Property 3: the flat form and its bracketed canonical form bind to identical
    // slot assignments and denote the same type.
    // **Validates: Requirements 3, 4**
    // =====================================================================

    [Property(Arbitrary = new[] { typeof(VariadicsSemanticModelFlatBracketedEquivalencePropertyTests) }, MaxTest = 200)]
    public bool FlatForm_And_BracketedCanonicalForm_BindIdenticallyAndDenoteSameType_OverLegacyModel(
        SemanticFlatBracketedScenario scenario)
    {
        var (model, diagnostics) = BuildModel(scenario.Source);

        // A well-formed single-pack construction: no binding (MIST_VAR_0005) or resolution
        // error should surface. This also guards against a false green in which a field
        // silently fails to resolve to a ConstructedType.
        AssertNoErrors(diagnostics);

        var flat = FieldConstructed(model, "Flat");
        var bracketed = FieldConstructed(model, "Bracketed");

        var expectedSlotCount = scenario.LeadNames.Count + 1 + scenario.TrailNames.Count;
        Assert.Equal(expectedSlotCount, flat.TypeArguments.Count);
        Assert.Equal(expectedSlotCount, bracketed.TypeArguments.Count);

        // Identical slot assignments (Requirements 3, 4.1-4.5): both spellings must bind the
        // fixed ends to their scalars inward one-to-one and the single unbracketed pack to the
        // ordered middle. Asserting each form against the generator's intended lead/mid/trail
        // shape proves they produce the very same assignment.
        AssertSlotAssignment(flat, scenario);
        AssertSlotAssignment(bracketed, scenario);

        // Same denoted type (Requirement 4.4): the flat construction and its bracketed canonical
        // form are equal under the classic structural identity relation (the legacy model does
        // not intern, so they are distinct instances that compare equal), and the relation is
        // symmetric.
        Assert.True(
            TypeCheckerAnalyzer.TypesEqual(flat, bracketed),
            $"Flat and bracketed forms must denote the same type (Requirement 4.4): {scenario.Description}");
        Assert.True(
            TypeCheckerAnalyzer.TypesEqual(bracketed, flat),
            $"Flat/bracketed equivalence must be symmetric: {scenario.Description}");

        return true;
    }

    // =====================================================================
    // Generators
    // =====================================================================

    /// <summary>Arbitrary registration for the property above.</summary>
    public static Arbitrary<SemanticFlatBracketedScenario> FlatBracketedScenarios()
    {
        return GenScenario().ToArbitrary();
    }

    private static Gen<SemanticFlatBracketedScenario> GenScenario()
    {
        return
            from leadCount in Gen.Choose(0, 2)
            from trailCount in Gen.Choose(0, 2)
            from rawMid in Gen.Choose(0, 3)
            from elems in Gen.ArrayOf(Gen.Choose(0, PoolSize - 1), MaxTotalElements)
            select BuildScenario(leadCount, rawMid, trailCount, elems);
    }

    private static SemanticFlatBracketedScenario BuildScenario(
        int leadCount, int rawMid, int trailCount, int[] elems)
    {
        // Guarantee at least one flat argument: a pack-only family constructed with zero
        // arguments would render `Fam<>`, which is not a valid type-argument list. Only the
        // all-zero case needs the bump, and it becomes the minimal single-element pack.
        var midLen = leadCount + trailCount + rawMid == 0 ? 1 : rawMid;

        var leadNames = SliceNames(elems, 0, leadCount);
        var midNames = SliceNames(elems, leadCount, midLen);
        var trailNames = SliceNames(elems, leadCount + midLen, trailCount);

        // Family type-parameter list: fixed lead slots, the single params pack, fixed trail slots.
        var typeParams = new List<string>(leadCount + 1 + trailCount);
        for(var i = 0; i < leadCount; i++)
        {
            typeParams.Add($"L{i}");
        }

        typeParams.Add("params TArgs");
        for(var j = 0; j < trailCount; j++)
        {
            typeParams.Add($"R{j}");
        }

        // Flat use site: every argument written flat (fixed ends + inline middle run).
        var flatArgs = new List<string>(leadCount + midLen + trailCount);
        flatArgs.AddRange(leadNames);
        flatArgs.AddRange(midNames);
        flatArgs.AddRange(trailNames);

        // Bracketed canonical form: the pack's contiguous middle wrapped in [...], fixed ends flat.
        var bracketedArgs = new List<string>(leadCount + 1 + trailCount);
        bracketedArgs.AddRange(leadNames);
        bracketedArgs.Add($"[{string.Join(", ", midNames)}]");
        bracketedArgs.AddRange(trailNames);

        var flatArgList = string.Join(", ", flatArgs);
        var bracketedArgList = string.Join(", ", bracketedArgs);
        var source = BuildSource(string.Join(", ", typeParams), flatArgList, bracketedArgList);
        var description =
            $"lead={leadCount} mid={midLen} trail={trailCount}: Fam<{flatArgList}> vs Fam<{bracketedArgList}>";

        return new SemanticFlatBracketedScenario(source, leadNames, midNames, trailNames, description);
    }

    private static List<string> SliceNames(int[] elems, int start, int count)
    {
        var names = new List<string>(count);
        for(var i = 0; i < count; i++)
        {
            names.Add($"E{elems[start + i]}");
        }

        return names;
    }

    /// <summary>
    /// Builds a complete Mist source declaring the marker-class pool, the single-<c>params</c>
    /// family <c>Fam</c>, and a <c>Holder</c> that constructs the family flat (field <c>Flat</c>)
    /// and bracketed (field <c>Bracketed</c>). The source is namespace-free; the classic builder
    /// supplies the namespace root, mirroring the task 12.1 example tests.
    /// </summary>
    private static string BuildSource(string typeParams, string flatArgList, string bracketedArgList)
    {
        var markerDecls = string.Join(
            "\n", Enumerable.Range(0, PoolSize).Select(i => $"public class E{i} {{ }}"));

        return
            markerDecls + "\n"
            + $"public class Fam<{typeParams}> {{ }}\n"
            + "public class Holder\n"
            + "{\n"
            + $"    public Fam<{flatArgList}> Flat;\n"
            + $"    public Fam<{bracketedArgList}> Bracketed;\n"
            + "}";
    }

    // =====================================================================
    // Assertion helpers
    // =====================================================================

    /// <summary>
    /// Asserts a constructed variadic type matches the scenario's intended assignment: fixed
    /// lead slots carry <see cref="SemanticFlatBracketedScenario.LeadNames"/> in order, the pack
    /// slot at index <c>LeadNames.Count</c> carries the ordered middle
    /// <see cref="SemanticFlatBracketedScenario.MidNames"/>, and fixed trail slots carry
    /// <see cref="SemanticFlatBracketedScenario.TrailNames"/> in order.
    /// </summary>
    private static void AssertSlotAssignment(ConstructedType constructed, SemanticFlatBracketedScenario scenario)
    {
        for(var i = 0; i < scenario.LeadNames.Count; i++)
        {
            AssertFixedSlot(constructed, i, scenario.LeadNames[i]);
        }

        AssertPackSlot(constructed, scenario.LeadNames.Count, scenario.MidNames.ToArray());

        for(var j = 0; j < scenario.TrailNames.Count; j++)
        {
            var slotIndex = scenario.LeadNames.Count + 1 + j;
            AssertFixedSlot(constructed, slotIndex, scenario.TrailNames[j]);
        }
    }

    /// <summary>
    /// Asserts slot <paramref name="index"/> is a pack whose lifted comptime <c>Type[]</c>
    /// element types match <paramref name="expectedElementNames"/> in order (empty means the
    /// empty pack). The pack rides on a <see cref="DependentValueArgument"/> at that slot.
    /// </summary>
    private static void AssertPackSlot(ConstructedType constructed, int index, string[] expectedElementNames)
    {
        Assert.NotNull(constructed.DependentArguments);
        var dependent = constructed.DependentArguments![index];
        Assert.NotNull(dependent);
        Assert.NotNull(dependent!.PackElementTypes);
        Assert.Equal(
            expectedElementNames,
            dependent.PackElementTypes!.Select(e => e.DisplayName).ToArray());
    }

    /// <summary>
    /// Asserts slot <paramref name="index"/> is a fixed scalar slot bound to the type named
    /// <paramref name="expectedTypeName"/>, carrying no pack dependent argument.
    /// </summary>
    private static void AssertFixedSlot(ConstructedType constructed, int index, string expectedTypeName)
    {
        Assert.Equal(expectedTypeName, constructed.TypeArguments[index].DisplayName);
        if(constructed.DependentArguments is not null)
        {
            Assert.Null(constructed.DependentArguments[index]);
        }
    }

    private static void AssertNoErrors(ImmutableArray<Diagnostic> diagnostics)
    {
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(
            errors.Count == 0,
            $"Unexpected errors: {string.Join("; ", errors.Select(d => $"{d.Id.Value} {d.Message}"))}");
    }

    /// <summary>
    /// Returns the constructed variadic type resolved for the field named
    /// <paramref name="fieldName"/> on the single <c>Holder</c> type.
    /// </summary>
    private static ConstructedType FieldConstructed(ISemanticModel model, string fieldName)
    {
        var field = model.AllSymbols.Single(s => s.Kind == SymbolKind.Field && s.Name == fieldName);
        return Assert.IsType<ConstructedType>(field.DeclaredType);
    }

    // =====================================================================
    // In-process classic semantic-model construction (mirrors the task 12.1 example
    // tests; ZERO external process spawns).
    // =====================================================================

    private static (ISemanticModel Model, ImmutableArray<Diagnostic> Diagnostics) BuildModel(string source)
    {
        var parse = VariadicsCompilerFixture.Parse(source);
        Assert.NotNull(parse.Root);
        Assert.True(
            VariadicsCompilerFixture.ParsedCleanly(parse),
            $"Flat/bracketed scenario source is expected to parse without lex/parse errors: <{source}>");

        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(new SourceText("test.mist", source))));
        var diagnostics = new DiagnosticBag();
        var builder = new SemanticModelBuilder();

        var model = builder.Build(
            compilation,
            ImmutableArray.Create(parse.Root!),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            diagnostics);

        return (model, diagnostics.ToImmutableArray());
    }
}