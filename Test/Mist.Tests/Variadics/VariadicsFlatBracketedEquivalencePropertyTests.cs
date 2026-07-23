// variadics, task 7.3: property test for flat/bracketed equivalence.
//
// Property 3: Flat/bracketed equivalence.
// **Validates: Requirements 3, 4**
//
// For any declared slot list containing a single Unbracketed_Pack and any use-site
// argument list, the flat construction form and its bracketed canonical form (the pack's
// contiguous middle run wrapped in [...]) bind to IDENTICAL slot assignments and denote the
// SAME type, so `Foo<int, string>` and `Foo<[int, string]>` are interchangeable.
//
// This test drives program-model construction entirely in-process via
// ProgramModelBuilder.Build (parse -> build program model -> resolve the constructed field
// types), exactly as the task 7.1 smoke test (VariadicsBindingRuleSmokeTests) does. There
// are ZERO external process spawns: the builder runs directly, emit is never invoked, and no
// compiled program is ever run.
//
// Generator strategy (matched flat/bracketed pairs). Each scenario picks a pack POSITION by
// choosing a number of fixed scalar slots before the pack (0..2) and after it (0..2), plus a
// middle element run of length 0..3, so the single `params` pack lands leading, middle,
// trailing, or as the whole list across the generated space. It then renders the SAME family
// `Fam<...>` at two matched use sites over a shared, ordered list of element types drawn from
// a fixed pool of source marker classes (E0..E7):
//   * the FLAT form writes every argument comma-separated -- fixed ends plus the middle run
//     inline, e.g. `Fam<E1, E2, E3, E4>`; and
//   * the BRACKETED canonical form wraps exactly the pack's contiguous middle run in `[...]`
//     while leaving the fixed ends flat, e.g. `Fam<E1, [E2, E3], E4>` (an empty middle
//     renders `[]`).
// Element types are declared source classes (not primitives) so they resolve without a loaded
// stdlib and each spelling resolves a given name to the same interned type instance -- which
// is what lets the two constructions intern to the same ConstructedType (the flat and
// bracketed forms both lift the identical ordered middle Type[] via
// TypeInterner.GetOrCreateLiftedTypeArray, then intern the same (family, arguments) pair via
// GetOrCreateConstructedType). The all-empty case (no fixed slots and an empty middle) would
// render the invalid `Fam<>`, so it is bumped to a single-element middle.

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;
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
public sealed record FlatBracketedScenario(
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
/// Property test for <b>Property 3 (Flat/bracketed equivalence)</b>. Builds the program model
/// in-process and asserts that a flat variadic construction and its bracketed canonical form
/// intern to the very same <see cref="ConstructedType"/> instance (same denoted type) AND bind
/// to identical slot assignments -- fixed ends inward from both ends, the single pack over the
/// ordered middle <c>Type[]</c>. <b>Validates: Requirements 3, 4.</b>
/// </summary>
public sealed class VariadicsFlatBracketedEquivalencePropertyTests
{
    // A fixed pool of declared source marker classes used as element types. Using declared
    // classes (rather than primitives) means resolution needs no loaded stdlib and each
    // spelling resolves a given name to the same interned instance.
    private const int PoolSize = 8;

    // The maximum element run a scenario draws: lead(<=2) + middle(<=3) + trail(<=2). A single
    // fixed-length draw of this size is sliced per scenario, so every `from` clause precedes
    // the `select` (no interdependent generator lengths).
    private const int MaxTotalElements = 7;

    // ═══════════════════════════════════════════════════════════════════════
    // Property 3: the flat form and its bracketed canonical form bind to identical
    // slot assignments and denote the same type.
    // **Validates: Requirements 3, 4**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(Arbitrary = new[] { typeof(VariadicsFlatBracketedEquivalencePropertyTests) }, MaxTest = 200)]
    public bool FlatForm_And_BracketedCanonicalForm_BindIdenticallyAndDenoteSameType(
        FlatBracketedScenario scenario)
    {
        var (model, diagnostics) = BuildModel(scenario.Source);

        // A well-formed single-pack construction: no binding (MIST_VAR_0005) or resolution
        // error should surface. This also guards against a false green in which both fields
        // silently resolve to the shared ErrorType singleton.
        AssertNoErrors(diagnostics);

        var holder = FindConcreteType(model, "Holder");
        var flat = FieldType(holder, "Flat");
        var bracketed = FieldType(holder, "Bracketed");

        // Same denoted type (Requirement 4.4): the flat construction and its bracketed
        // canonical form intern to the very same ConstructedType instance.
        Assert.Same(flat, bracketed);

        // Identical slot assignments (Requirements 3, 4.1-4.5): confirm the shared binding
        // produced the shape the generator intended -- fixed ends bind their scalars inward
        // one-to-one and the single unbracketed pack absorbs the ordered middle Type[].
        var constructed = Assert.IsType<ConstructedType>(flat);
        var expectedSlotCount = scenario.LeadNames.Count + 1 + scenario.TrailNames.Count;
        Assert.Equal(expectedSlotCount, constructed.TypeArguments.Count);

        for(var i = 0; i < scenario.LeadNames.Count; i++)
        {
            Assert.Equal(scenario.LeadNames[i], constructed.TypeArguments[i].Name);
        }

        var packArgument = constructed.TypeArguments[scenario.LeadNames.Count];
        AssertPackElements(packArgument, scenario.MidNames);

        for(var j = 0; j < scenario.TrailNames.Count; j++)
        {
            var slotIndex = scenario.LeadNames.Count + 1 + j;
            Assert.Equal(scenario.TrailNames[j], constructed.TypeArguments[slotIndex].Name);
        }

        return true;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Arbitrary registration for the property above.</summary>
    public static Arbitrary<FlatBracketedScenario> FlatBracketedScenarios()
    {
        return GenScenario().ToArbitrary();
    }

    private static Gen<FlatBracketedScenario> GenScenario()
    {
        return
            from leadCount in Gen.Choose(0, 2)
            from trailCount in Gen.Choose(0, 2)
            from rawMid in Gen.Choose(0, 3)
            from elems in Gen.ArrayOf(Gen.Choose(0, PoolSize - 1), MaxTotalElements)
            select BuildScenario(leadCount, rawMid, trailCount, elems);
    }

    private static FlatBracketedScenario BuildScenario(int leadCount, int rawMid, int trailCount, int[] elems)
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

        return new FlatBracketedScenario(source, leadNames, midNames, trailNames, description);
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
    /// and bracketed (field <c>Bracketed</c>).
    /// </summary>
    private static string BuildSource(string typeParams, string flatArgList, string bracketedArgList)
    {
        var markerDecls = string.Join(
            "\n", Enumerable.Range(0, PoolSize).Select(i => $"public class E{i} {{ }}"));

        return
            "namespace TestProject;\n\n"
            + markerDecls + "\n\n"
            + $"public class Fam<{typeParams}> {{ }}\n\n"
            + "public class Holder\n"
            + "{\n"
            + $"    public Fam<{flatArgList}> Flat;\n"
            + $"    public Fam<{bracketedArgList}> Bracketed;\n"
            + "}\n";
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Assertion helpers
    // ═══════════════════════════════════════════════════════════════════════

    private static void AssertNoErrors(ImmutableArray<Diagnostic> diagnostics)
    {
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(
            errors.Count == 0,
            $"Unexpected errors: {string.Join("; ", errors.Select(d => $"{d.Id.Value} {d.Message}"))}");
    }

    /// <summary>
    /// Asserts a pack slot argument is a lifted comptime <c>Type[]</c> whose element types
    /// match <paramref name="expectedElementNames"/> in order (empty means the empty pack).
    /// </summary>
    private static void AssertPackElements(IMistType packArgument, IReadOnlyList<string> expectedElementNames)
    {
        var lifted = Assert.IsType<LiftedType>(packArgument);
        var elements = Assert.IsType<IMistType[]>(lifted.Value);
        Assert.Equal(expectedElementNames.ToArray(), elements.Select(e => e.Name).ToArray());
    }

    private static IMistType FieldType(ConcreteType type, string fieldName)
    {
        var field = type.FieldsList.Single(f => f.Name == fieldName);
        return field.Type;
    }

    private static ConcreteType FindConcreteType(IProgramModel model, string simpleName)
    {
        var matches = model.TypesByFQN.Values
            .OfType<ConcreteType>()
            .Where(t => t.Origin == TypeOrigin.Source && StripArity(t.Name) == simpleName)
            .ToList();

        _ = Assert.Single(matches);
        return matches[0];
    }

    private static string StripArity(string typeName)
    {
        var angle = typeName.IndexOf('<', StringComparison.Ordinal);
        return angle < 0 ? typeName : typeName[..angle];
    }

    // ═══════════════════════════════════════════════════════════════════════
    // In-process program-model construction (mirrors the task 7.1 smoke test;
    // ZERO external process spawns).
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Parses <paramref name="source"/> and builds the program model entirely in-process,
    /// returning the model together with every diagnostic collected during construction.
    /// </summary>
    private static (IProgramModel Model, ImmutableArray<Diagnostic> Diagnostics) BuildModel(string source)
    {
        var lexer = new MistLexer();
        var parser = new MistParser();
        var parseDiagnostics = new DiagnosticBag();

        var sourceText = new SourceText("test.mist", source);
        var tokens = lexer.Lex(sourceText, parseDiagnostics);
        var parseResult = parser.Parse(tokens, parseDiagnostics);

        Assert.NotNull(parseResult.Root);

        var parseErrors = parseDiagnostics.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.True(
            parseErrors.Count == 0,
            $"Unexpected parse errors for source <{source}>: "
                + string.Join("; ", parseErrors.Select(d => $"{d.Id.Value} {d.Message}")));

        var unit = new CompilationUnit(sourceText);
        var compilation = new MistCompilation(ImmutableArray.Create(unit));

        var options = new CompilerOptions(
            ProjectFolder: ".",
            SourcePaths: ImmutableArray.Create("test.mist"),
            Backend: new BackendId("csharp"),
            OutputDirectory: ".",
            BinDirectory: "bin",
            ProjectName: "TestProject",
            NamespaceRoot: "TestProject",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty);

        var buildDiagnostics = new DiagnosticBag();
        var model = ProgramModelBuilder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root!),
            options,
            buildDiagnostics,
            NullAmbientTypeProvider.Instance);

        Assert.NotNull(model);
        return (model!, buildDiagnostics.ToImmutableArray());
    }
}